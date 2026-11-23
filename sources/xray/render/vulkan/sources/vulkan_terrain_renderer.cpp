#include "pch.h"
#include "vulkan_terrain_renderer.h"
#include <xray/render/core/sources/rhi/rhi_device.h>
#include <xray/render/core/sources/rhi/shader_compiler.h>
#include "texture_format.h"
#include <stdexcept>
namespace xray { namespace render { namespace vulkan {
namespace {
struct terrain_vertex { static_vertex base; float weights[3],ids[3],color[3],shift[2]; };
static_assert(sizeof(terrain_vertex)==76);
bool matches(std::vector<std::weak_ptr<texture_data const>> const& a,std::vector<std::shared_ptr<texture_data const>> const& b) {
    if (a.size()!=b.size()) return false;
    for (size_t i=0;i<a.size();++i) if (a[i].lock()!=b[i]) return false; return true;
}
}
namespace {
void validate_images(std::vector<std::shared_ptr<texture_data const>> const& diffuse,
    std::vector<std::shared_ptr<texture_data const>> const& normal,std::vector<std::shared_ptr<texture_data const>> const& specular,terrain_memory_profile profile) {
    auto limit=terrain_limits(profile).cell_bytes;
    if (diffuse.empty() || diffuse.size()>16) throw std::runtime_error("Terrain requires 1..16 diffuse slots");
    if ((!normal.empty() && normal.size()!=diffuse.size()) || (!specular.empty() && specular.size()!=diffuse.size())
        || (normal.empty()!=specular.empty())) throw std::runtime_error("Terrain deferred draw requires matching normal/specular slots");
    size_t bytes=0;
    unsigned role=0;
    for (auto const* sources : {&diffuse,&normal,&specular}) {
        auto name=role==0 ? "diffuse" : role==1 ? "normal" : "specular"; ++role;
        if (sources->empty()) continue;
        auto const& first=sources->front();
        if (!first) throw std::runtime_error("Missing terrain layer snapshot");
        auto const& reference=first->description;
        auto layout=[](rhi::texture_desc const& d) {
            return std::to_string(d.width)+"x"+std::to_string(d.height)+", format="+std::to_string(unsigned(d.pixel_format))
                +", mips="+std::to_string(d.mip_levels);
        };
        for (size_t slot=0;slot<sources->size();++slot) {
            auto const& image=(*sources)[slot];
            auto prefix=std::string("Terrain ")+name+" slot "+std::to_string(slot)+": ";
            if (!image) throw std::runtime_error(prefix+"missing layer snapshot"); auto const& d=image->description;
            if (d.type!=rhi::texture_type::type_2d || d.array_layers!=1 || !d.width || d.width>4096 || d.width!=d.height || d.samples!=1
                || d.usage!=rhi::texture_usage::sampled || !d.mip_levels || image->mips.size()!=d.mip_levels)
                throw std::runtime_error(prefix+"requires sampled square 2D data <=4096 with complete mips ("+layout(d)+")");
            if (d.width!=reference.width || d.height!=reference.height || d.pixel_format!=reference.pixel_format || d.mip_levels!=reference.mip_levels)
                throw std::runtime_error(prefix+"incompatible array layout ("+layout(d)+"); slot 0 ("+layout(reference)+"); normalization/transcoding is not supported");
            u32 levels=1; for (u32 size=d.width;size>1;size>>=1) ++levels;
            if (d.mip_levels>levels) throw std::runtime_error(prefix+"too many mip levels");
            for (u32 level=0;level<d.mip_levels;++level) {
                auto const& mip=image->mips[level];
                if (mip.size()!=sampled_mip_layout(d.pixel_format,std::max(1u,d.width>>level),std::max(1u,d.height>>level)).size)
                    throw std::runtime_error(prefix+"invalid byte size for mip "+std::to_string(level));
                if (mip.size()>limit-bytes) throw std::runtime_error("Terrain draw arrays exceed "+std::to_string(limit/(1024*1024))+" MiB"); bytes+=mip.size();
            }
        }
    }
}
}
void validate_terrain_textures(terrain_texture_set const& textures) {
    if (textures.normal.empty()) throw std::runtime_error("Terrain texture set requires all three roles");
    validate_images(textures.diffuse,textures.normal,textures.specular,textures.memory_profile);
    for (auto const& normal : textures.normal) {
        auto format=normal->description.pixel_format;
        if (format!=rhi::format::r8g8b8a8_unorm && format!=rhi::format::bc1_unorm && format!=rhi::format::bc2_unorm && format!=rhi::format::bc3_unorm)
            throw std::runtime_error("Terrain normals require linear RGBA/BC data");
    }
}
void validate_terrain_draw(terrain_draw const& draw) {
    if (!draw.cell || !draw.cell->mesh || draw.diffuse.size()!=draw.cell->textures.size())
        throw std::runtime_error("Terrain draw requires its cell and matching diffuse slots");
    validate_images(draw.diffuse,draw.normal,draw.specular,draw.memory_profile);
}
terrain_renderer::terrain_renderer(rhi::rhi_backend& backend) : m_backend(&backend) {
    try {
        rhi::shader_compiler compiler;
        for (unsigned i=0;i<2;++i) {
            auto stage=i ? rhi::shader_stage::pixel : rhi::shader_stage::vertex;
            rhi::shader_compile_options options; options.entry=i ? "terrain_ps" : "terrain_vs";
            if (!i) options.flags={"-fvk-invert-y"};
            auto code=compiler.compile_file(XRAY_VULKAN_ASSET_SHADER_DIR "/terrain_unlit.hlsl",stage,options);
            if (!code.success) throw std::runtime_error(code.error_log);
            auto shader=backend.create_shader(stage,code.bytecode.data(),code.bytecode.size()*4,options.entry.c_str());
            if (!shader) throw std::runtime_error("Terrain shader creation failed"); (i ? m_ps : m_vs)=shader;
        }
        m_frame=backend.create_buffer(rhi::buffer_type::constant,sizeof(frame_constants),nullptr);
        m_object=backend.create_buffer(rhi::buffer_type::constant,sizeof(object_constants),nullptr);
        rhi::sampler_desc sampler; sampler.min_filter=sampler.mag_filter=rhi::sampler_filter::linear; sampler.mip_filter=rhi::sampler_filter::nearest;
        sampler.address_u=sampler.address_v=rhi::sampler_address::repeat; m_sampler=backend.create_sampler(sampler);
        if (!m_frame || !m_object || !m_sampler) throw std::runtime_error("Terrain constants/sampler unavailable");
    } catch (...) { release(); throw; }
}
terrain_renderer::~terrain_renderer() { release(); }
void terrain_renderer::release() {
    if (rhi::rhi_device::ref().get_backend()==m_backend) {
        for (auto const& g : m_geometry) { m_backend->destroy_buffer(g.vertices); m_backend->destroy_buffer(g.indices); }
        for (auto const& image : m_images) m_backend->destroy_texture(image.handle);
        m_backend->destroy_shader(m_vs); m_backend->destroy_shader(m_ps);
        m_backend->destroy_shader(m_deferred_vs); m_backend->destroy_shader(m_material_vs);
        m_backend->destroy_shader(m_gbuffer_ps); m_backend->destroy_shader(m_material_ps);
        m_backend->destroy_buffer(m_surface); m_backend->destroy_buffer(m_material);
        m_backend->destroy_buffer(m_frame); m_backend->destroy_buffer(m_object); m_backend->destroy_sampler(m_sampler);
    }
    m_geometry.clear(); m_images.clear();
}
void terrain_renderer::collect() {
    for (auto it=m_geometry.begin();it!=m_geometry.end();) {
        if (!it->mesh.expired()) { ++it; continue; }
        m_backend->destroy_buffer(it->vertices); m_backend->destroy_buffer(it->indices); it=m_geometry.erase(it);
    }
    for (auto it=m_images.begin();it!=m_images.end();) {
        if (std::all_of(it->sources.begin(),it->sources.end(),[](auto const& s) { return !s.expired(); })) { ++it; continue; }
        m_backend->destroy_texture(it->handle); it=m_images.erase(it);
    }
}
void terrain_renderer::prepare(std::vector<terrain_draw> const& draws) {
    for (auto const& draw : draws) validate_terrain_draw(draw);
    if (std::any_of(draws.begin(),draws.end(),[](auto const& draw) { return !draw.normal.empty(); })) prepare_deferred();
    collect();
    for (auto const& draw : draws) {
        for (auto const* sources : {&draw.diffuse,&draw.normal,&draw.specular}) {
            if (sources->empty() || std::any_of(m_images.begin(),m_images.end(),[&](auto const& i) { return matches(i.sources,*sources); })) continue;
            auto desc=sources->front()->description; desc.type=rhi::texture_type::type_2d_array; desc.array_layers=u32(sources->size());
            std::vector<rhi::texture_subresource_data> upload; images entry;
            for (u32 layer=0;layer<sources->size();++layer) {
                entry.sources.push_back((*sources)[layer]);
                for (u32 mip=0;mip<desc.mip_levels;++mip) {
                    auto const& data=(*sources)[layer]->mips[mip]; upload.push_back({mip,layer,data.data(),data.size(),0});
                }
            }
            entry.handle=m_backend->create_texture(desc,upload);
            if (!entry.handle) throw std::runtime_error("Terrain layer array upload failed");
            try { m_images.push_back(entry); } catch (...) { m_backend->destroy_texture(entry.handle); throw; }
        }
        if (std::any_of(m_geometry.begin(),m_geometry.end(),[&](auto const& g) { return g.mesh.lock()==draw.cell->mesh; })) continue;
        std::vector<terrain_vertex> vertices(draw.cell->samples.size());
        for (size_t i=0;i<vertices.size();++i) {
            auto& v=vertices[i]; auto const& sample=draw.cell->samples[i]; v.base=draw.cell->mesh->vertices[i];
            for (unsigned c=0;c<3;++c) { v.weights[c]=sample.weights[c]/255.f; v.ids[c]=sample.texture_ids[c]; v.color[c]=float((sample.color>>(16-c*8))&255)/255; }
            std::copy(std::begin(sample.shift),std::end(sample.shift),v.shift);
        }
        geometry entry; entry.mesh=draw.cell->mesh;
        try {
            entry.vertices=m_backend->create_buffer(rhi::buffer_type::vertex,vertices.size()*sizeof(terrain_vertex),vertices.data());
            entry.indices=m_backend->create_buffer(rhi::buffer_type::index,draw.cell->mesh->indices.size()*sizeof(u16),draw.cell->mesh->indices.data());
            if (!entry.vertices || !entry.indices) throw std::runtime_error("Terrain geometry upload failed"); m_geometry.push_back(entry);
        } catch (...) { m_backend->destroy_buffer(entry.vertices); m_backend->destroy_buffer(entry.indices); throw; }
    }
}
void terrain_renderer::prepare_deferred() {
    rhi::shader_compiler compiler;
    struct stage { rhi::shader_stage type; char const* entry; rhi::rhi_shader_handle* handle; };
    for (auto s : {stage{rhi::shader_stage::vertex,"terrain_deferred_vs",&m_deferred_vs},
        stage{rhi::shader_stage::vertex,"terrain_material_vs",&m_material_vs},
        stage{rhi::shader_stage::pixel,"terrain_gbuffer_ps",&m_gbuffer_ps},stage{rhi::shader_stage::pixel,"terrain_material_ps",&m_material_ps}}) {
        if (*s.handle) continue;
        rhi::shader_compile_options options; options.entry=s.entry;
        options.defines={std::string("TERRAIN_MATERIAL=")+(s.handle==&m_material_vs || s.handle==&m_material_ps ? "1" : "0")};
        if (s.type==rhi::shader_stage::vertex) options.flags={"-fvk-invert-y"};
        auto code=compiler.compile_file(XRAY_VULKAN_ASSET_SHADER_DIR "/terrain_deferred.hlsl",s.type,options);
        if (!code.success) throw std::runtime_error(code.error_log);
        *s.handle=m_backend->create_shader(s.type,code.bytecode.data(),code.bytecode.size()*4,s.entry);
        if (!*s.handle) throw std::runtime_error("Terrain deferred shader unavailable");
    }
    if (!m_surface) m_surface=m_backend->create_buffer(rhi::buffer_type::constant,sizeof(mapped_surface_object_constants),nullptr);
    if (!m_material) m_material=m_backend->create_buffer(rhi::buffer_type::constant,sizeof(material_object_constants),nullptr);
    if (!m_surface || !m_material) throw std::runtime_error("Terrain surface constants unavailable");
}
void terrain_renderer::draw(std::vector<terrain_draw> const& draws,frame_constants const& camera,float far_plane,bool material_pass) {
    auto& b=*m_backend;
    bool deferred=far_plane>0;
    rhi::vertex_input_layout layout{76,{{0,rhi::format::r32g32b32_float,0},{1,rhi::format::r32g32_float,24},
        {2,rhi::format::r32g32b32_float,32},{3,rhi::format::r32g32b32_float,44},{4,rhi::format::r32g32b32_float,56},{5,rhi::format::r32g32_float,68}}};
    if (deferred && !material_pass) {
        layout.attributes.push_back({6,rhi::format::r8g8b8a8_unorm,12});layout.attributes.push_back({7,rhi::format::r8g8b8a8_unorm,16});
        layout.attributes.push_back({8,rhi::format::r8g8b8a8_unorm,20});
    }
    b.set_vertex_layout(layout);
    b.set_topology(rhi::topology::triangle_list); b.set_rasterizer_state(rhi::fill_mode::solid,rhi::cull_mode::none);
    b.set_depth_stencil_state(true,!material_pass,material_pass ? rhi::comparison_func::equal : rhi::comparison_func::less);
    b.set_stencil_state(deferred,deferred ? 0x82 : 0,255,material_pass ? 0 : 255,
        material_pass ? rhi::comparison_func::equal : rhi::comparison_func::always,material_pass ? rhi::stencil_op::keep : rhi::stencil_op::replace);
    b.set_blend_state(false,rhi::blend::one,rhi::blend::zero,rhi::blend_op::add);
    b.bind_shader(rhi::shader_stage::vertex,material_pass ? m_material_vs : deferred ? m_deferred_vs : m_vs);
    b.bind_shader(rhi::shader_stage::pixel,material_pass ? m_material_ps : deferred ? m_gbuffer_ps : m_ps); b.bind_sampler(0,m_sampler);
    b.update_buffer(m_frame,&camera,sizeof(camera)); b.bind_constant_buffer(0,m_frame);
    b.bind_constant_buffer(1,material_pass ? m_material : deferred ? m_surface : m_object);
    for (auto const& draw : draws) {
        auto g=std::find_if(m_geometry.begin(),m_geometry.end(),[&](auto const& entry) { return entry.mesh.lock()==draw.cell->mesh; });
        auto image=std::find_if(m_images.begin(),m_images.end(),[&](auto const& entry) { return matches(entry.sources,draw.diffuse); });
        if (g==m_geometry.end() || image==m_images.end()) throw std::runtime_error("Terrain draw was not prepared");
        material_object_constants constants={}; auto& object=constants.mapped.scalar.object;
        object.parameters[0]=far_plane; object.parameters[1]=draw.cell->physical_size; object.parameters[2]=float(draw.diffuse.size());
        constants.mapped.scalar.surface[0]=constants.mapped.scalar.surface[1]=50.f/255; constants.mapped.scalar.surface[2]=.075f;
        for (unsigned r=0;r<4;++r) for (unsigned c=0;c<4;++c) object.world[r*4+c]=draw.transform.elements[c][r];
        if (material_pass) b.update_buffer(m_material,&constants,sizeof(constants));
        else if (deferred) b.update_buffer(m_surface,&constants.mapped,sizeof(constants.mapped));
        else b.update_buffer(m_object,&object,sizeof(object));
        b.bind_texture(0,image->handle);
        if (deferred) for (auto slot : {1u,3u}) {
            auto const& sources=slot==1 ? draw.normal : draw.specular;
            auto map=std::find_if(m_images.begin(),m_images.end(),[&](auto const& entry) { return matches(entry.sources,sources); });
            if (sources.empty() || map==m_images.end()) throw std::runtime_error("Terrain deferred maps were not prepared");
            b.bind_texture(slot,map->handle);
        }
        b.bind_vertex_buffer(g->vertices,0); b.bind_index_buffer(g->indices,0); b.draw_indexed(u32(draw.cell->mesh->indices.size()),0,0);
    }
}
} } }
