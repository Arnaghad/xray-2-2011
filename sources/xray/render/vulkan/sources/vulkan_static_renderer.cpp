#include "pch.h"
#include "vulkan_static_renderer.h"
#include <xray/render/vulkan/skeletal_resources.h>
#include <xray/render/core/sources/rhi/rhi_device.h>
#include <xray/render/core/sources/rhi/shader_compiler.h>
#include <cmath>
#include <cstring>
#include <stdexcept>

namespace xray { namespace render { namespace vulkan {
namespace {
std::array<float,4> compute_sequence_uv(sequence_parameters const& seq, float phase) {
    if (!seq.enabled || seq.width < 1.f || seq.height < 1.f) return {0.f, 0.f, 1.f, 1.f};
    int w = std::max(1, int(std::round(seq.width)));
    int h = std::max(1, int(std::round(seq.height)));
    int total = w * h;
    double frame_d = std::floor(double(seq.start_frame) + double(phase) * double(seq.play_speed));
    int frame_idx = ((int(std::fmod(frame_d, double(total))) % total) + total) % total;
    int col = frame_idx % w;
    int row = (frame_idx / w) % h;
    float su = 1.f / float(w), sv = 1.f / float(h);
    return { float(col) * su, float(row) * sv, su, sv };
}
float u32_to_float_bits(u32 v) {
    float f = 0.f;
    std::memcpy(&f, &v, sizeof(f));
    return f;
}
struct gpu_skeletal_vertex {
    float px, py, pz;
    float nx, ny, nz;
    float tx, ty, tz;
    float bx, by, bz;
    float u, v;
    u32 b0, b1, b2, b3;
    float w0, w1, w2, w3;
};
static_assert(sizeof(gpu_skeletal_vertex) == 88);
struct gpu_skinning_constants {
    float bones[128][16];
    u32 counts[4];
};
static_assert(sizeof(gpu_skinning_constants) == 8208);
}
static_renderer::static_renderer(rhi::rhi_backend& backend) : m_backend(&backend) {
    try {
        rhi::shader_compiler compiler; rhi::shader_compile_options options;
        options.entry="static_vs"; options.flags={"-fvk-invert-y"};
        auto vs=compiler.compile_file(XRAY_VULKAN_ASSET_SHADER_DIR "/static_opaque.hlsl",rhi::shader_stage::vertex,options);
        if (!vs.success) throw std::runtime_error(vs.error_log);
        m_vs=backend.create_shader(rhi::shader_stage::vertex,vs.bytecode.data(),vs.bytecode.size()*4,"static_vs");
        for (unsigned variant=0;variant<2;++variant) prepare_pixel_shader(variant);
        m_frame=backend.create_buffer(rhi::buffer_type::constant,sizeof(frame_constants),nullptr);
        m_object=backend.create_buffer(rhi::buffer_type::constant,sizeof(object_constants),nullptr);
        if (!m_vs || !m_frame || !m_object)
            throw std::runtime_error("Static renderer resource creation failed");
    } catch (...) { release(); throw; }
}
static_renderer::~static_renderer() { release(); }
void static_renderer::prepare_pixel_shader(unsigned variant, bool gbuffer) {
    if (gbuffer) {
        if (m_gbuffer_ps.contains(variant) && m_gbuffer_ps[variant]) return;
    } else if (m_ps[variant]) return;
    rhi::shader_compiler compiler; rhi::shader_compile_options options; options.entry=gbuffer ? "gbuffer_ps" : "static_ps";
    options.defines={std::string("USE_DIFFUSE_TEXTURE=")+(variant&1 ? "1" : "0"),
        std::string("USE_ALPHA_TEST=")+(variant&2 ? "1" : "0"),
        std::string("USE_NORMAL_TEXTURE=")+(variant&4 ? "1" : "0"),
        std::string("USE_ROUGHNESS_TEXTURE=")+(variant&8 ? "1" : "0"),
        std::string("USE_DETAIL_NORMAL_TEXTURE=")+(variant&16 ? "1" : "0")};
    if (gbuffer) {
        options.defines.push_back(std::string("USE_FRESNEL_TEXTURE=")+(variant&32 ? "1" : "0"));
        options.defines.push_back(std::string("USE_ANISOTROPY_DIR_TEXTURE=")+(variant&64 ? "1" : "0"));
        options.defines.push_back(std::string("USE_PARALLAX=")+(variant&128 ? "1" : "0"));
        options.defines.push_back(std::string("USE_TRANSLUCENCY_TEXTURE=")+(variant&256 ? "1" : "0"));
        options.defines.push_back(std::string("USE_TRIPLANAR=")+(variant&512 ? "1" : "0"));
        options.defines.push_back(std::string("USE_BURNING_WOOD=")+(variant&1024 ? "1" : "0"));
        options.defines.push_back(std::string("USE_BINDLESS=")+(m_backend->supports_bindless() ? "1" : "0"));
    }
    auto ps=compiler.compile_file(gbuffer ? XRAY_VULKAN_ASSET_SHADER_DIR "/static_gbuffer.hlsl" : XRAY_VULKAN_ASSET_SHADER_DIR "/static_opaque.hlsl",rhi::shader_stage::pixel,options);
    if (!ps.success) throw std::runtime_error(ps.error_log);
    auto handle=m_backend->create_shader(rhi::shader_stage::pixel,ps.bytecode.data(),ps.bytecode.size()*4,options.entry.c_str());
    if (!handle) throw std::runtime_error("Static material shader creation failed (cutout requires shaderDemoteToHelperInvocation)");
    if (gbuffer) m_gbuffer_ps[variant]=handle;
    else m_ps[variant]=handle;
    if(!gbuffer){bool function=false;float work=0;for(size_t i=5;i<ps.bytecode.size();) {auto word=ps.bytecode[i];unsigned count=word>>16,op=word&65535;if(!count)break;if(op==54)function=true;else if(op==56)function=false;else if(function)++work;i+=count;}m_fragment_work[variant]=work;}
}
void static_renderer::release() {
    if (rhi::rhi_device::ref().get_backend()==m_backend) {
        for (auto const& g : m_geometry) {
            m_backend->destroy_buffer(g.vertices);
            m_backend->destroy_buffer(g.indices);
            m_backend->destroy_buffer(g.skin_source);
        }
        for (auto const& t : m_textures) m_backend->destroy_texture(t.handle);
        for (auto const& s : m_samplers) m_backend->destroy_sampler(s.handle);
        for (auto handle : {m_frame,m_object,m_surface_object,m_material_object,m_forward_object,m_skin_constants}) m_backend->destroy_buffer(handle);
        m_backend->destroy_shader(m_skin_cs);
        m_backend->destroy_shader(m_material_vs);
        for (auto const& [_, handle] : m_material_ps) m_backend->destroy_shader(handle);
        m_backend->destroy_shader(m_vs);
        m_backend->destroy_shader(m_gbuffer_vs);
        m_backend->destroy_shader(m_forward_vs);
        for (auto handle : m_ps) m_backend->destroy_shader(handle);
        for (auto handle : m_mask_ps) m_backend->destroy_shader(handle);
        m_backend->destroy_shader(m_diagnostic_ps);m_backend->destroy_shader(m_diagnostic_vs);m_backend->destroy_buffer(m_diagnostic_object);
        for (auto handle : m_shadow_ps) m_backend->destroy_shader(handle);
        for (auto const& [_, handle] : m_forward_ps) m_backend->destroy_shader(handle);
        m_backend->destroy_shader(m_distortion_ps);
        for (auto const& [_, handle] : m_gbuffer_ps) m_backend->destroy_shader(handle);
    }
    m_geometry.clear(); m_textures.clear(); m_samplers.clear(); m_ps={}; m_vs=m_skin_cs=m_skin_constants=m_frame=m_object=0;
    m_gbuffer_vs=m_surface_object=0; m_gbuffer_ps.clear();
    m_material_vs=m_material_object=0; m_material_ps.clear();
    m_shadow_ps={};
    m_forward_vs=m_forward_object=0; m_forward_ps.clear();
}
void static_renderer::prepare_forward_shader(unsigned variant, u32 samples) {
    rhi::shader_compiler compiler; rhi::shader_compile_options options;
    if (!m_forward_vs) {
        options.entry="forward_vs"; options.flags={"-fvk-invert-y"};
        auto code=compiler.compile_file(XRAY_VULKAN_ASSET_SHADER_DIR "/static_forward.hlsl",rhi::shader_stage::vertex,options);
        if (!code.success) throw std::runtime_error(code.error_log);
        m_forward_vs=m_backend->create_shader(rhi::shader_stage::vertex,code.bytecode.data(),code.bytecode.size()*4,"forward_vs");
        if (!m_forward_vs) throw std::runtime_error("Forward vertex shader creation failed");
    }
    if (!m_forward_object) {
        m_forward_object=m_backend->create_buffer(rhi::buffer_type::constant,sizeof(extended_forward_object_constants),nullptr);
        if (!m_forward_object) throw std::runtime_error("Forward object constants creation failed");
    }
    u32 effective_samples=(variant & 8) ? std::max(1u,samples) : 1u;
    u32 key=(variant & 0xfffu) | ((effective_samples & 0xfu) << 12);
    if (m_forward_ps.contains(key) && m_forward_ps[key]) return;
    options.entry="forward_ps"; options.flags={};
    options.defines={
        std::string("USE_DIFFUSE_TEXTURE=")+(variant&1 ? "1" : "0"),
        std::string("USE_TRANSPARENCY_TEXTURE=")+(variant&2 ? "1" : "0"),
        std::string("USE_ALPHA_TEST=")+(variant&4 ? "1" : "0"),
        std::string("USE_SOFT_DEPTH=")+(variant&8 ? "1" : "0"),
        std::string("USE_FIRE_FRESNEL=")+(variant&16 ? "1" : "0"),
        std::string("USE_FORWARD_LIGHTING=")+(variant&32 ? "1" : "0"),
        std::string("USE_TRIPLANAR=")+(variant&64 ? "1" : "0"),
        std::string("USE_VOLUME_CONE=")+(variant&128 ? "1" : "0"),
        std::string("USE_VOLUME_SPHERE=")+(variant&256 ? "1" : "0"),
        std::string("USE_COLOR_POWER=")+(variant&512 ? "1" : "0"),
        std::string("USE_FORWARD_DECAL=")+(variant&1024 ? "1" : "0"),
        std::string("USE_BINDLESS=")+(m_backend->supports_bindless() ? "1" : "0"),
        std::string("SAMPLE_COUNT=")+std::to_string(effective_samples)};
    auto code=compiler.compile_file(XRAY_VULKAN_ASSET_SHADER_DIR "/static_forward.hlsl",rhi::shader_stage::pixel,options);
    if (!code.success) throw std::runtime_error(code.error_log);
    m_forward_ps[key]=m_backend->create_shader(rhi::shader_stage::pixel,code.bytecode.data(),code.bytecode.size()*4,options.entry.c_str());
    if (!m_forward_ps[key]) throw std::runtime_error("Forward material shader unavailable");
}
void static_renderer::prepare_shadow_shader(unsigned variant) {
    if (m_shadow_ps[variant]) return;
    rhi::shader_compiler compiler; rhi::shader_compile_options options; options.entry="shadow_ps";
    options.defines={std::string("USE_DIFFUSE_TEXTURE=")+(variant&1 ? "1" : "0"),std::string("USE_ALPHA_TEST=")+(variant&2 ? "1" : "0")};
    auto code=compiler.compile_file(XRAY_VULKAN_ASSET_SHADER_DIR "/static_shadow.hlsl",rhi::shader_stage::pixel,options);
    if (!code.success) throw std::runtime_error(code.error_log);
    m_shadow_ps[variant]=m_backend->create_shader(rhi::shader_stage::pixel,code.bytecode.data(),code.bytecode.size()*4,"shadow_ps");
    if (!m_shadow_ps[variant]) throw std::runtime_error("Shadow caster shader unavailable");
}
void static_renderer::prepare_material_shader(unsigned variant) {
    rhi::shader_compiler compiler; rhi::shader_compile_options options;
    if (!m_material_vs) {
        options.entry="material_vs"; options.flags={"-fvk-invert-y"};
        auto code=compiler.compile_file(XRAY_VULKAN_ASSET_SHADER_DIR "/static_material.hlsl",rhi::shader_stage::vertex,options);
        if (!code.success) throw std::runtime_error(code.error_log);
        m_material_vs=m_backend->create_shader(rhi::shader_stage::vertex,code.bytecode.data(),code.bytecode.size()*4,options.entry.c_str());
        if (!m_material_vs) throw std::runtime_error("Material vertex shader creation failed");
    }
    if (!m_material_object) {
        m_material_object=m_backend->create_buffer(rhi::buffer_type::constant,sizeof(extended_material_object_constants),nullptr);
        if (!m_material_object) throw std::runtime_error("Material object constants creation failed");
    }
    if (m_material_ps.contains(variant) && m_material_ps[variant]) return;
    options.entry="material_ps"; options.flags={};
    options.defines={std::string("USE_DIFFUSE_TEXTURE=")+(variant&1 ? "1" : "0"),
        std::string("USE_ALPHA_TEST=")+(variant&2 ? "1" : "0"),
        std::string("USE_SPECULAR_TEXTURE=")+(variant&4 ? "1" : "0"),
        std::string("USE_EMISSIVE_TEXTURE=")+(variant&8 ? "1" : "0"),
        std::string("USE_DETAIL_DIFFUSE_TEXTURE=")+(variant&16 ? "1" : "0"),
        std::string("USE_VARIATION_TEXTURE=")+(variant&32 ? "1" : "0"),
        std::string("USE_DIFFUSE_POWER_TEXTURE=")+(variant&64 ? "1" : "0"),
        std::string("USE_PARALLAX=")+(variant&128 ? "1" : "0"),
        std::string("USE_TRIPLANAR=")+(variant&256 ? "1" : "0"),
        std::string("USE_BURNING_WOOD=")+(variant&512 ? "1" : "0"),
        std::string("USE_WHIRL_TEXTURE=")+(variant&1024 ? "1" : "0"),
        std::string("USE_BINDLESS=")+(m_backend->supports_bindless() ? "1" : "0")};
    auto code=compiler.compile_file(XRAY_VULKAN_ASSET_SHADER_DIR "/static_material.hlsl",rhi::shader_stage::pixel,options);
    if (!code.success) throw std::runtime_error(code.error_log);
    m_material_ps[variant]=m_backend->create_shader(rhi::shader_stage::pixel,code.bytecode.data(),code.bytecode.size()*4,options.entry.c_str());
    if (!m_material_ps[variant]) throw std::runtime_error("Material pixel shader creation failed");
}
void static_renderer::collect() {
    for (auto it=m_geometry.begin(); it!=m_geometry.end();) {
        bool alive=it->skin_source ? (!it->skin.expired() && it->used) : !it->mesh.expired();
        if (alive) { if (it->skin_source) it->used=false; ++it; continue; }
        m_backend->destroy_buffer(it->vertices); m_backend->destroy_buffer(it->indices); m_backend->destroy_buffer(it->skin_source);
        it=m_geometry.erase(it);
    }
    for (auto it=m_textures.begin();it!=m_textures.end();) {
        if (!it->data.expired()) { ++it; continue; }
        m_backend->destroy_texture(it->handle); it=m_textures.erase(it);
    }
    for (auto it=m_samplers.begin();it!=m_samplers.end();) {
        if (!it->data.expired()) { ++it; continue; }
        m_backend->destroy_sampler(it->handle); it=m_samplers.erase(it);
    }
}
void static_renderer::prepare(std::vector<model_draw> const& models, bool gbuffer,bool material_pass,bool shadow_pass,bool forward_pass,bool distortion_pass,bool has_scene_depth,u32 scene_depth_samples) {
    collect();
    if(distortion_pass && !m_distortion_ps){
        rhi::shader_compiler compiler;rhi::shader_compile_options options;options.entry="distortion_ps";
        auto code=compiler.compile_file(XRAY_VULKAN_ASSET_SHADER_DIR "/distortion.hlsl",rhi::shader_stage::pixel,options);
        if(!code.success)throw std::runtime_error(code.error_log);
        m_distortion_ps=m_backend->create_shader(rhi::shader_stage::pixel,code.bytecode.data(),code.bytecode.size()*4,"distortion_ps");
        if(!m_distortion_ps)throw std::runtime_error("Distortion graphics shader unavailable");
    }
    if (gbuffer && !m_surface_object) {
        m_surface_object=m_backend->create_buffer(rhi::buffer_type::constant,sizeof(extended_gbuffer_object_constants),nullptr);
        if (!m_surface_object) throw std::runtime_error("Surface material constants creation failed");
    }
    if (gbuffer && !m_gbuffer_vs) {
        rhi::shader_compiler compiler; rhi::shader_compile_options options;
        options.entry="gbuffer_vs"; options.flags={"-fvk-invert-y"};
        auto code=compiler.compile_file(XRAY_VULKAN_ASSET_SHADER_DIR "/static_gbuffer.hlsl",rhi::shader_stage::vertex,options);
        if (!code.success) throw std::runtime_error(code.error_log);
        m_gbuffer_vs=m_backend->create_shader(rhi::shader_stage::vertex,code.bytecode.data(),code.bytecode.size()*4,"gbuffer_vs");
        if (!m_gbuffer_vs) throw std::runtime_error("GBuffer vertex shader creation failed");
    }
    for (auto const& item : models) {
        auto const* mat_res=item.material ? static_cast<material_resource*>(item.material.c_ptr()) : nullptr;
        bool cutout=mat_res && mat_res->data->alpha_test;
        if(distortion_pass){}
        else if (forward_pass) {
            unsigned fvar=(item.texture ? 1u : 0u) | (item.transparency_texture ? 2u : 0u) | (cutout ? 4u : 0u);
            if (mat_res) {
                auto const& fwd=mat_res->textures.forward;
                if (has_scene_depth && (fwd.use_soft_test || fwd.effect==forward_effect_type::soft || fwd.effect==forward_effect_type::volume_cone || fwd.effect==forward_effect_type::volume_sphere)) fvar|=8u;
                if (fwd.use_fire_fresnel || fwd.effect==forward_effect_type::fire_fresnel) fvar|=16u;
                if (fwd.use_lighting || fwd.effect==forward_effect_type::smoke_lighted) fvar|=32u;
                if (fwd.effect==forward_effect_type::tiled) fvar|=64u;
                if (fwd.effect==forward_effect_type::volume_cone) fvar|=128u;
                if (fwd.effect==forward_effect_type::volume_sphere) fvar|=256u;
                if (fwd.use_color_power) fvar|=512u;
                if (fwd.effect==forward_effect_type::decal) fvar|=1024u;
            }
            prepare_forward_shader(fvar,(fvar&8u)?scene_depth_samples:1u);
        }
        else if (shadow_pass) prepare_shadow_shader((item.texture ? 1 : 0) | (cutout ? 2 : 0));
        else {
            unsigned pvar=(item.texture ? 1 : 0) | (cutout ? 2 : 0) | (gbuffer && item.normal_texture ? 4 : 0)
                | (gbuffer && item.roughness_texture ? 8 : 0) | (gbuffer && item.detail_normal_texture ? 16 : 0)
                | (gbuffer && item.fresnel_texture ? 32 : 0) | (gbuffer && item.anisotropy_dir_texture ? 64 : 0);
            if (gbuffer && mat_res) {
                if (item.bump_texture && mat_res->textures.parallax.enabled) pvar|=128;
                if (item.translucency_texture) pvar|=256;
                if (mat_res->textures.g_effect==g_stage_effect_type::tiled) pvar|=512;
                if (mat_res->textures.g_effect==g_stage_effect_type::burning_wood) pvar|=1024;
            }
            prepare_pixel_shader(pvar,gbuffer);
        }
        if (material_pass) {
            unsigned mvar=(item.texture ? 1 : 0) | (cutout ? 2 : 0) | (item.specular_texture ? 4 : 0)
                | (item.emissive_texture ? 8 : 0) | (item.detail_diffuse_texture ? 16 : 0)
                | (item.variation_mask_texture ? 32 : 0) | (item.diffuse_power_texture ? 64 : 0);
            if (mat_res) {
                if (item.bump_texture && mat_res->textures.parallax.enabled) mvar|=128;
                if (mat_res->textures.g_effect==g_stage_effect_type::tiled) mvar|=256;
                if (mat_res->textures.g_effect==g_stage_effect_type::burning_wood) mvar|=512;
                if (item.whirl_texture) mvar|=1024;
            }
            prepare_material_shader(mvar);
        }
        if (item.material) {
            auto const& material=mat_res->data;
            if (!std::any_of(m_samplers.begin(),m_samplers.end(),[&](sampler_entry const& s) { return s.data.lock()==material; })) {
                auto handle=m_backend->create_sampler(material->sampler);
                if (!handle) throw std::runtime_error("Material sampler creation failed");
                try { m_samplers.push_back({material,handle}); } catch (...) { m_backend->destroy_sampler(handle); throw; }
            }
            for (auto const& texture : {item.texture,item.normal_texture,item.roughness_texture,item.specular_texture,item.detail_normal_texture,
                item.emissive_texture,item.detail_diffuse_texture,item.variation_mask_texture,item.fresnel_texture,item.anisotropy_dir_texture,item.diffuse_power_texture,
                item.bump_texture,item.translucency_texture,item.whirl_texture,item.transparency_texture,
                item.cubemap_texture,item.cubemap_mask_texture,item.subdermal_texture,item.ao_texture,
                item.back_illumination_texture,item.scattering_amount_texture,item.scattering_depth_texture}) if (texture) {
                if (!std::any_of(m_textures.begin(),m_textures.end(),[&](texture_entry const& t) { return t.data.lock()==texture; })) {
                    std::vector<rhi::texture_subresource_data> upload;
                    u32 const layers=std::max(1u,texture->description.array_layers);
                    u32 const mips=std::max(1u,texture->description.mip_levels);
                    for (u32 layer=0;layer<layers;++layer) {
                        for (u32 level=0;level<mips;++level) {
                            auto const& mip=texture->mips[size_t(layer)*mips+level];
                            upload.push_back({level,layer,mip.data(),mip.size(),0});
                        }
                    }
                    auto handle=m_backend->create_texture(texture->description,upload);
                    if (!handle) throw std::runtime_error("Material texture upload failed");
                    try { m_textures.push_back({texture,handle}); } catch (...) { m_backend->destroy_texture(handle); throw; }
                }
            }
        }
        auto const& mesh=item.mesh;
        if (item.skin) {
            if (!m_skin_cs) {
                rhi::shader_compiler compiler; rhi::shader_compile_options options; options.entry="skin_cs";
                auto code=compiler.compile_file(XRAY_VULKAN_ASSET_SHADER_DIR "/skinning.comp.hlsl",rhi::shader_stage::compute,options);
                if (!code.success) throw std::runtime_error(code.error_log);
                m_skin_cs=m_backend->create_shader(rhi::shader_stage::compute,code.bytecode.data(),code.bytecode.size()*4,"skin_cs");
                if (!m_skin_cs) throw std::runtime_error("Compute skinning shader unavailable");
            }
            if (!m_skin_constants) {
                m_skin_constants=m_backend->create_buffer(rhi::buffer_type::constant,sizeof(gpu_skinning_constants),nullptr);
                if (!m_skin_constants) throw std::runtime_error("Compute skinning constant buffer unavailable");
            }
            uintptr_t inst_key=reinterpret_cast<uintptr_t>(item.model.c_ptr());
            auto found=std::find_if(m_geometry.begin(),m_geometry.end(),[&](geometry const& g) { return g.skin_source && g.skin.lock()==item.skin && g.instance_key==inst_key; });
            if (found!=m_geometry.end()) { found->used=true; continue; }
            std::vector<gpu_skeletal_vertex> src; src.reserve(item.skin->vertices.size());
            for (auto const& sv : item.skin->vertices) {
                src.push_back({sv.position.x,sv.position.y,sv.position.z,
                    sv.normal.x,sv.normal.y,sv.normal.z,
                    sv.tangent.x,sv.tangent.y,sv.tangent.z,
                    sv.binormal.x,sv.binormal.y,sv.binormal.z,
                    sv.uv.x,sv.uv.y,
                    sv.bones[0],sv.bones[1],sv.bones[2],sv.bones[3],
                    sv.weights[0],sv.weights[1],sv.weights[2],sv.weights[3]});
            }
            geometry g; g.mesh=mesh; g.skin=item.skin; g.instance_key=inst_key; g.used=true;
            try {
                g.skin_source=m_backend->create_buffer(rhi::buffer_type::structured,src.size()*sizeof(gpu_skeletal_vertex),src.data());
                g.vertices=m_backend->create_buffer(rhi::buffer_type::structured,mesh->vertices.size()*sizeof(static_vertex),mesh->vertices.data());
                g.indices=m_backend->create_buffer(rhi::buffer_type::index,mesh->indices.size()*sizeof(u16),mesh->indices.data());
                if (!g.skin_source || !g.vertices || !g.indices) throw std::runtime_error("Skeletal GPU buffer upload failed");
                m_geometry.push_back(g);
            } catch (...) { m_backend->destroy_buffer(g.skin_source); m_backend->destroy_buffer(g.vertices); m_backend->destroy_buffer(g.indices); throw; }
            continue;
        }
        if (std::any_of(m_geometry.begin(),m_geometry.end(),[&](geometry const& g) { return !g.skin_source && g.mesh.lock()==mesh; })) continue;
        geometry g; g.mesh=mesh;
        try {
            g.vertices=m_backend->create_buffer(rhi::buffer_type::vertex,mesh->vertices.size()*sizeof(static_vertex),mesh->vertices.data());
            g.indices=m_backend->create_buffer(rhi::buffer_type::index,mesh->indices.size()*sizeof(u16),mesh->indices.data());
            if (!g.vertices || !g.indices) throw std::runtime_error("Static VB/IB upload failed");
            m_geometry.push_back(g);
        } catch (...) { m_backend->destroy_buffer(g.vertices); m_backend->destroy_buffer(g.indices); throw; }
    }
}
bool static_renderer::has_skinning(std::vector<model_draw> const& models) const {
    return std::any_of(models.begin(),models.end(),[](model_draw const& item) { return item.visible && item.skin && item.bone_palette && !item.bone_palette->empty(); });
}
void static_renderer::run_skinning(std::vector<model_draw> const& models) {
    if (!m_skin_cs || !m_skin_constants) return;
    for (auto const& item : models) {
        if (!item.visible || !item.skin || !item.bone_palette || item.bone_palette->empty()) continue;
        uintptr_t inst_key=reinterpret_cast<uintptr_t>(item.model.c_ptr());
        auto it=std::find_if(m_geometry.begin(),m_geometry.end(),[&](geometry const& g) { return g.skin_source && g.skin.lock()==item.skin && g.instance_key==inst_key; });
        if (it==m_geometry.end() || it->skin_revision==item.skin_revision) continue;
        gpu_skinning_constants constants={};
        u32 const bone_count=u32(std::min<size_t>(item.bone_palette->size(),128));
        for (u32 b=0;b<bone_count;++b) {
            auto const& m=(*item.bone_palette)[b];
            for (unsigned r=0;r<4;++r) for (unsigned c=0;c<4;++c) constants.bones[b][r*4+c]=m.elements[c][r];
        }
        constants.counts[0]=u32(item.skin->vertices.size());
        m_backend->update_buffer(m_skin_constants,&constants,sizeof(constants));
        rhi::rhi_buffer_handle storage[2]={it->skin_source,it->vertices};
        m_backend->dispatch_compute_buffers(m_skin_cs,storage,2,m_skin_constants,(constants.counts[0]+63)/64,1,1);
        m_backend->compute_to_vertex_barrier(it->vertices);
        it->skin_revision=item.skin_revision;
    }
}
void static_renderer::prepare_mask(std::vector<model_draw> const& models) {
    prepare(models);
    for(auto const& model:models) {
        bool cutout=model.material && static_cast<material_resource*>(model.material.c_ptr())->data->alpha_test;
        unsigned variant=(model.texture?1:0)|(cutout?2:0);if(m_mask_ps[variant])continue;
        rhi::shader_compiler compiler;rhi::shader_compile_options options;options.entry="mask_ps";
        options.defines={std::string("USE_DIFFUSE_TEXTURE=")+(variant&1?"1":"0"),std::string("USE_ALPHA_TEST=")+(variant&2?"1":"0")};
        auto shader=compiler.compile_file(XRAY_VULKAN_ASSET_SHADER_DIR "/static_opaque.hlsl",rhi::shader_stage::pixel,options);
        if(!shader.success)throw std::runtime_error(shader.error_log);
        m_mask_ps[variant]=m_backend->create_shader(rhi::shader_stage::pixel,shader.bytecode.data(),shader.bytecode.size()*4,"mask_ps");
        if(!m_mask_ps[variant])throw std::runtime_error("Selection mask shader unavailable");
    }
}
void static_renderer::prepare_diagnostic() {
    if(m_diagnostic_ps)return;rhi::shader_compiler compiler;rhi::shader_compile_options options;options.entry="diagnostic_ps";
    auto shader=compiler.compile_file(XRAY_VULKAN_ASSET_SHADER_DIR "/editor_diagnostic.hlsl",rhi::shader_stage::pixel,options);if(!shader.success)throw std::runtime_error(shader.error_log);
    m_diagnostic_ps=m_backend->create_shader(rhi::shader_stage::pixel,shader.bytecode.data(),shader.bytecode.size()*4,"diagnostic_ps");
    options.entry="diagnostic_vs";options.flags={"-fvk-invert-y"};shader=compiler.compile_file(XRAY_VULKAN_ASSET_SHADER_DIR "/editor_diagnostic.hlsl",rhi::shader_stage::vertex,options);if(!shader.success)throw std::runtime_error(shader.error_log);
    m_diagnostic_vs=m_backend->create_shader(rhi::shader_stage::vertex,shader.bytecode.data(),shader.bytecode.size()*4,"diagnostic_vs");
    m_diagnostic_object=m_backend->create_buffer(rhi::buffer_type::constant,sizeof(surface_object_constants),nullptr);
    if(!m_diagnostic_ps || !m_diagnostic_object)throw std::runtime_error("Editor diagnostic shader unavailable");
}
void static_renderer::draw(std::vector<model_draw> const& models, frame_constants const& camera, float gbuffer_far,bool material_pass,bool shadow_pass,bool forward_pass,bool distortion_pass,float phase,bool mask_pass,scene_view_mode diagnostic,u32 width,u32 height,rhi::rhi_texture_handle scene_depth,u32 scene_depth_samples,forward_lighting_context const& forward_lighting) {
    auto& b=*m_backend;
    bool gbuffer=gbuffer_far>0;
    bool debug_mode=diagnostic==miplevel_view_mode || diagnostic==shader_complexity_view_mode || diagnostic==texture_density_view_mode || diagnostic==geometry_complexity_view_mode || diagnostic==overdraw_view_mode;
    bool additive=diagnostic==overdraw_view_mode || diagnostic==shader_complexity_view_mode;
    rhi::vertex_input_layout layout{sizeof(static_vertex),{{0,rhi::format::r32g32b32_float,0},{1,rhi::format::r32g32_float,24}}};
    if (gbuffer) {
        layout.attributes.push_back({2,rhi::format::r8g8b8a8_unorm,12}); layout.attributes.push_back({3,rhi::format::r8g8b8a8_unorm,16});
        layout.attributes.push_back({4,rhi::format::r8g8b8a8_unorm,20});
    } else if (forward_pass) {
        layout.attributes.push_back({2,rhi::format::r8g8b8a8_unorm,12});
    }
    b.set_vertex_layout(layout);
    b.set_topology(rhi::topology::triangle_list);
    b.set_rasterizer_state(rhi::fill_mode::solid,rhi::cull_mode::none);
    b.set_depth_stencil_state(!mask_pass,!mask_pass && !material_pass && !forward_pass && !distortion_pass,mask_pass?rhi::comparison_func::always:material_pass ? rhi::comparison_func::equal : distortion_pass ? rhi::comparison_func::less_equal : rhi::comparison_func::less);
    if (forward_pass) b.set_depth_bias(0,0);
    b.set_stencil_state(gbuffer,gbuffer ? 0x82 : 0,255,material_pass ? 0 : 255,
        material_pass ? rhi::comparison_func::equal : rhi::comparison_func::always,material_pass ? rhi::stencil_op::keep : rhi::stencil_op::replace);
    b.set_blend_state(false,rhi::blend::one,rhi::blend::zero,rhi::blend_op::add);
    if(additive){b.set_depth_stencil_state(false,false,rhi::comparison_func::always);b.set_blend_state(true,rhi::blend::one,rhi::blend::one,rhi::blend_op::add);}
    b.bind_shader(rhi::shader_stage::vertex,material_pass ? m_material_vs : forward_pass ? m_forward_vs : gbuffer ? m_gbuffer_vs : m_vs);
    b.update_buffer(m_frame,&camera,sizeof(camera)); b.bind_constant_buffer(0,m_frame);
    b.bind_constant_buffer(1,material_pass ? m_material_object : forward_pass ? m_forward_object : gbuffer ? m_surface_object : m_object);
    if(debug_mode){b.bind_shader(rhi::shader_stage::vertex,m_diagnostic_vs);b.bind_constant_buffer(1,m_diagnostic_object);}
    for (auto const& item : models) {
        if (!item.visible) continue;
        auto const& mesh=item.mesh;
        uintptr_t inst_key=reinterpret_cast<uintptr_t>(item.model.c_ptr());
        auto it=std::find_if(m_geometry.begin(),m_geometry.end(),[&](geometry const& g) {
            return item.skin ? (g.skin_source && g.skin.lock()==item.skin && g.instance_key==inst_key) : (!g.skin_source && g.mesh.lock()==mesh);
        });
        if (it==m_geometry.end()) throw std::runtime_error("Static geometry was not prepared before frame recording");
        extended_material_object_constants ext_material={};
        auto& emission_constants=ext_material.emissive_material;
        auto& material_constants=emission_constants.material;
        auto& constants=material_constants.mapped; auto& object=constants.scalar.object;
        extended_gbuffer_object_constants ext_gbuffer={};
        auto& detailed_constants=ext_gbuffer.detailed;
        std::fill(std::begin(detailed_constants.detail_normal_parameters),std::end(detailed_constants.detail_normal_parameters),1);
        ext_gbuffer.sequence_uv[2]=ext_gbuffer.sequence_uv[3]=1.f;
        ext_material.sequence_uv[2]=ext_material.sequence_uv[3]=1.f;
        ext_material.variation_parameters[0]=ext_material.variation_parameters[1]=ext_material.variation_parameters[3]=1.f;
        for (float& c : ext_material.variation_color) c=1.f;
        for (float& c : ext_material.diffuse_attenuation) c=1.f;
        ext_material.detail_diffuse[0]=ext_material.detail_diffuse[1]=1.f;
        material_constants.specular[0]=material_constants.specular[1]=material_constants.specular[2]=1;
        constants.scalar.surface[0]=constants.scalar.surface[1]=1; constants.scalar.surface[2]=.04f;
        constants.scalar.surface[3]=item.decal_receiver;
        constants.map_parameters[1]=1;
        for (auto& channel : object.tint) channel=1;
        object.parameters[2]=object.parameters[3]=1;
        object.parameters[0]=gbuffer_far;
        rhi::rhi_texture_handle texture=0; rhi::rhi_sampler_handle sampler=0;
        auto cull=rhi::cull_mode::none;
        bool alpha_test=false;
        material_blend_mode blend=material_blend_mode::opaque;
        auto const* mat_res=item.material ? static_cast<material_resource*>(item.material.c_ptr()) : nullptr;
        if (mat_res) {
            blend=mat_res->blend;
            auto const& surface=mat_res->surface;
            constants.scalar.surface[0]=surface.roughness_uv[0]; constants.scalar.surface[1]=surface.roughness_uv[1];
            constants.scalar.surface[2]=surface.fresnel_at_0_degree;
            constants.map_parameters[0]=surface.roughness_range[0]; constants.map_parameters[1]=surface.roughness_range[1];
            std::copy(surface.specular_color.begin(),surface.specular_color.end(),material_constants.specular);
            auto const& emission=mat_res->contributions.emission;
            std::copy(emission.begin(),emission.end(),emission_constants.emissive);
            auto const& detail=mat_res->textures.detail;
            detailed_constants.detail_normal_parameters[0]=detailed_constants.detail_normal_parameters[1]=detail.scale;
            detailed_constants.detail_normal_parameters[2]=detailed_constants.detail_normal_parameters[3]=detail.tile;
            ext_material.detail_diffuse[0]=ext_material.detail_diffuse[1]=detail.tile;
            auto seq_uv=compute_sequence_uv(mat_res->textures.sequence,phase);
            std::copy(seq_uv.begin(),seq_uv.end(),ext_gbuffer.sequence_uv);
            std::copy(seq_uv.begin(),seq_uv.end(),ext_material.sequence_uv);
            auto const& parallax=mat_res->textures.parallax;
            auto const& tr=mat_res->textures.translucency_params;
            ext_gbuffer.extra_parameters[0]=parallax.enabled ? parallax.scale*0.03f : 0.f;
            ext_gbuffer.extra_parameters[1]=tr.constant_translucency/255.f;
            ext_gbuffer.extra_parameters[2]=tr.alpha_model_parameter;
            ext_gbuffer.extra_parameters[3]=phase;
            ext_material.detail_diffuse[2]=ext_gbuffer.extra_parameters[0];
            ext_material.detail_diffuse[3]=phase;
            auto const& var=mat_res->textures.variation;
            if (var.enabled) {
                ext_material.variation_parameters[0]=1.f/var.position_divider;
                ext_material.variation_parameters[1]=std::cos(var.rotate);
                ext_material.variation_parameters[2]=std::sin(var.rotate);
                ext_material.variation_parameters[3]=var.scale;
                for (unsigned c=0;c<3;++c) ext_material.variation_color[c]=var.color[c]*var.multiply;
                ext_material.variation_color[3]=1.f;
            }
            auto const& dmod=mat_res->textures.diffuse_mod;
            if (dmod.attenuation_enabled) {
                for (unsigned c=0;c<3;++c) ext_material.diffuse_attenuation[c]=dmod.attenuation_color[c]*dmod.attenuation_multiplier;
            }
            ext_material.diffuse_attenuation[3]=dmod.diffuse_power;
            auto const& material=mat_res->data;
            std::copy(material->tint.begin(),material->tint.end(),object.tint);
            cull=material->cull;
            alpha_test=material->alpha_test;
            object.parameters[1]=material->alpha_ref;
            object.parameters[2]=material->uv_scale[0]; object.parameters[3]=material->uv_scale[1];
            auto s=std::find_if(m_samplers.begin(),m_samplers.end(),[&](sampler_entry const& entry) { return entry.data.lock()==material; });
            if (s==m_samplers.end()) throw std::runtime_error("Material sampler was not prepared"); sampler=s->handle;
            if (item.texture) {
                auto const& data=item.texture;
                auto t=std::find_if(m_textures.begin(),m_textures.end(),[&](texture_entry const& entry) { return entry.data.lock()==data; });
                if (t==m_textures.end()) throw std::runtime_error("Material texture was not prepared"); texture=t->handle;
            }
        }
        auto bindless_idx=[&](std::shared_ptr<texture_data const> const& data) -> u32 {
            if (!data) return 0u;
            auto entry=std::find_if(m_textures.begin(),m_textures.end(),[&](texture_entry const& t) { return t.data.lock()==data; });
            return entry==m_textures.end() ? 0u : m_backend->register_bindless_texture(entry->handle);
        };
        if (mat_res) {
            auto const& refl=mat_res->textures.reflection;
            auto const& sss=mat_res->textures.sss;
            u32 cubemap_id=refl.enabled ? bindless_idx(item.cubemap_texture) : 0u;
            u32 mask_id=(refl.enabled && refl.use_mask) ? bindless_idx(item.cubemap_mask_texture) : 0u;
            u32 subdermal_id=bindless_idx(item.subdermal_texture);
            u32 ao_id=bindless_idx(item.ao_texture);
            u32 back_id=bindless_idx(item.back_illumination_texture);
            u32 scat_amt_id=bindless_idx(item.scattering_amount_texture);
            u32 scat_depth_id=bindless_idx(item.scattering_depth_texture);
            float refl_flags=float((cubemap_id ? 1 : 0) | (refl.write_diffuse ? 2 : 0) | (mask_id ? 4 : 0));
            ext_gbuffer.reflection_parameters[0]=refl.min_angle;
            ext_gbuffer.reflection_parameters[1]=cubemap_id ? refl.power : 0.f;
            ext_gbuffer.reflection_parameters[2]=refl_flags;
            ext_gbuffer.reflection_parameters[3]=float(u8(sss.profile));
            ext_gbuffer.bindless_indices[0]=u32_to_float_bits(cubemap_id);
            ext_gbuffer.bindless_indices[1]=u32_to_float_bits(mask_id);
            ext_gbuffer.bindless_indices[2]=u32_to_float_bits(subdermal_id);
            ext_gbuffer.bindless_indices[3]=u32_to_float_bits(ao_id);
            ext_gbuffer.sss_parameters[0]=float(u8(sss.profile));
            ext_gbuffer.sss_parameters[1]=sss.scattering_depth;
            ext_gbuffer.sss_parameters[2]=sss.subdermal_weight;
            ext_gbuffer.sss_parameters[3]=sss.scattering_color_multiplier;
            ext_material.reflection_parameters[0]=refl.min_angle;
            ext_material.reflection_parameters[1]=cubemap_id ? refl.power : 0.f;
            ext_material.reflection_parameters[2]=refl_flags;
            ext_material.reflection_parameters[3]=sss.subdermal_weight;
            ext_material.bindless_indices[0]=u32_to_float_bits(cubemap_id);
            ext_material.bindless_indices[1]=u32_to_float_bits(mask_id);
            ext_material.bindless_indices[2]=u32_to_float_bits(subdermal_id);
            ext_material.bindless_indices[3]=u32_to_float_bits(ao_id);
            ext_material.bindless_indices_extra[0]=u32_to_float_bits(back_id);
            ext_material.bindless_indices_extra[1]=u32_to_float_bits(scat_amt_id);
            ext_material.bindless_indices_extra[2]=u32_to_float_bits(scat_depth_id);
            ext_material.bindless_indices_extra[3]=float(u8(sss.profile));
        }
        b.bind_texture(0,texture); b.bind_sampler(0,sampler);
        auto bind_map=[&](unsigned slot,std::shared_ptr<texture_data const> const& data) {
            rhi::rhi_texture_handle handle=0;
            if ((gbuffer || material_pass || forward_pass) && data) {
                auto entry=std::find_if(m_textures.begin(),m_textures.end(),[&](texture_entry const& t) { return t.data.lock()==data; });
                if (entry==m_textures.end()) throw std::runtime_error("Surface map was not prepared"); handle=entry->handle;
            }
            b.bind_texture(slot,handle);
        };
        if (material_pass) {
            bind_map(1,item.emissive_texture);
            bind_map(2,item.detail_diffuse_texture);
            bind_map(3,item.specular_texture);
            bind_map(4,item.variation_mask_texture);
            bind_map(5,item.diffuse_power_texture);
            bind_map(6,item.bump_texture);
            bind_map(7,item.whirl_texture);
        } else if (forward_pass) {
            bind_map(1,item.transparency_texture);
            b.bind_texture(2,0); b.bind_texture(3,0); b.bind_texture(4,0);
            b.bind_texture(5,0); b.bind_texture(6,0); b.bind_texture(7,0);
        } else {
            bind_map(1,item.normal_texture);
            bind_map(2,item.roughness_texture);
            bind_map(3,item.fresnel_texture);
            bind_map(4,item.detail_normal_texture);
            bind_map(5,item.anisotropy_dir_texture);
            bind_map(6,item.bump_texture);
            bind_map(7,item.translucency_texture);
        }
        unsigned variant=(texture ? 1 : 0) | (alpha_test ? 2 : 0);
        if (gbuffer && !material_pass) {
            variant|=(item.normal_texture ? 4 : 0) | (item.roughness_texture ? 8 : 0) | (item.detail_normal_texture ? 16 : 0)
                | (item.fresnel_texture ? 32 : 0) | (item.anisotropy_dir_texture ? 64 : 0);
            if (mat_res) {
                if (item.bump_texture && mat_res->textures.parallax.enabled) variant|=128;
                if (item.translucency_texture) variant|=256;
                if (mat_res->textures.g_effect==g_stage_effect_type::tiled) variant|=512;
                if (mat_res->textures.g_effect==g_stage_effect_type::burning_wood) variant|=1024;
            }
        }
        if (material_pass) {
            variant=(texture ? 1 : 0) | (alpha_test ? 2 : 0) | (item.specular_texture ? 4 : 0)
                | (item.emissive_texture ? 8 : 0) | (item.detail_diffuse_texture ? 16 : 0)
                | (item.variation_mask_texture ? 32 : 0) | (item.diffuse_power_texture ? 64 : 0);
            if (mat_res) {
                if (item.bump_texture && mat_res->textures.parallax.enabled) variant|=128;
                if (mat_res->textures.g_effect==g_stage_effect_type::tiled) variant|=256;
                if (mat_res->textures.g_effect==g_stage_effect_type::burning_wood) variant|=512;
                if (item.whirl_texture) variant|=1024;
            }
        }
        u32 forward_key=0;
        extended_forward_object_constants fwd_obj={};
        if (forward_pass) {
            if (blend==material_blend_mode::alpha) b.set_blend_state(true,rhi::blend::src_alpha,rhi::blend::inv_src_alpha,rhi::blend_op::add);
            else if (blend==material_blend_mode::additive) b.set_blend_state(true,rhi::blend::src_alpha,rhi::blend::one,rhi::blend_op::add);
            else if (blend==material_blend_mode::modulate) b.set_blend_state(true,rhi::blend::dest_color,rhi::blend::zero,rhi::blend_op::add);
            else if (blend==material_blend_mode::subtractive) b.set_blend_state(true,rhi::blend::src_alpha,rhi::blend::one,rhi::blend_op::rev_subtract);
            else throw std::runtime_error("Opaque material queued as forward transparent");
            unsigned fvar=(texture ? 1u : 0u) | (item.transparency_texture ? 2u : 0u) | (alpha_test ? 4u : 0u);
            forward_effect_parameters fwd={};
            if (mat_res) {
                fwd=mat_res->textures.forward;
                if (scene_depth && (fwd.use_soft_test || fwd.effect==forward_effect_type::soft || fwd.effect==forward_effect_type::volume_cone || fwd.effect==forward_effect_type::volume_sphere)) fvar|=8u;
                if (fwd.use_fire_fresnel || fwd.effect==forward_effect_type::fire_fresnel) fvar|=16u;
                if (fwd.use_lighting || fwd.effect==forward_effect_type::smoke_lighted) fvar|=32u;
                if (fwd.effect==forward_effect_type::tiled) fvar|=64u;
                if (fwd.effect==forward_effect_type::volume_cone) fvar|=128u;
                if (fwd.effect==forward_effect_type::volume_sphere) fvar|=256u;
                if (fwd.use_color_power) fvar|=512u;
                if (fwd.effect==forward_effect_type::decal) fvar|=1024u;
            }
            if (fwd.effect==forward_effect_type::decal) {
                b.set_depth_bias(-2.f,-2.f);
                b.set_depth_stencil_state(!mask_pass,false,rhi::comparison_func::less_equal);
            } else {
                b.set_depth_bias(0,0);
                b.set_depth_stencil_state(!mask_pass,false,rhi::comparison_func::less);
            }
            if (mat_res && mat_res->textures.reflection.enabled) {
                auto const& refl=mat_res->textures.reflection;
                u32 cubemap_id=bindless_idx(item.cubemap_texture);
                fwd_obj.reflection_parameters[0]=refl.min_angle;
                fwd_obj.reflection_parameters[1]=cubemap_id ? refl.power : 0.f;
                fwd_obj.reflection_parameters[2]=cubemap_id ? 1.f : 0.f;
                fwd_obj.bindless_indices[0]=u32_to_float_bits(cubemap_id);
            }
            u32 eff_samples=(fvar & 8u) ? std::max(1u,scene_depth_samples) : 1u;
            forward_key=(fvar & 0xfffu) | ((eff_samples & 0xfu) << 12);
            b.bind_texture(2,(fvar & 8u) ? scene_depth : 0);
            object.parameters[0]=ext_material.sequence_uv[0];
            object.parameters[1]=ext_material.sequence_uv[1];
            object.parameters[2]*=ext_material.sequence_uv[2];
            object.parameters[3]*=ext_material.sequence_uv[3];
            std::copy(std::begin(object.tint),std::end(object.tint),fwd_obj.tint);
            std::copy(std::begin(object.parameters),std::end(object.parameters),fwd_obj.parameters);
            fwd_obj.forward_params[0]=fwd.soft_distance;
            fwd_obj.forward_params[1]=fwd.attenuation_scale;
            fwd_obj.forward_params[2]=fwd.color_power;
            fwd_obj.forward_params[3]=mat_res ? mat_res->data->alpha_ref : 0.25f;
            std::copy(forward_lighting.light_direction.begin(),forward_lighting.light_direction.end(),fwd_obj.light_direction);
            std::copy(forward_lighting.light_color.begin(),forward_lighting.light_color.end(),fwd_obj.light_color);
            std::copy(forward_lighting.ambient_color.begin(),forward_lighting.ambient_color.end(),fwd_obj.ambient_color);
            fwd_obj.projection_params[0]=camera.projection[10]; fwd_obj.projection_params[1]=camera.projection[11];
            fwd_obj.projection_params[2]=camera.projection[14]; fwd_obj.projection_params[3]=camera.projection[15];
            fwd_obj.volume_params[0]=fwd.move_direction[0]*phase; fwd_obj.volume_params[1]=fwd.move_direction[1]*phase;
            fwd_obj.volume_params[2]=fwd.move_direction[2]*phase; fwd_obj.volume_params[3]=fwd.uv_tile;
            std::copy(forward_lighting.viewport.begin(),forward_lighting.viewport.end(),fwd_obj.viewport_params);
            for (unsigned c=0;c<3;++c) fwd_obj.extra_forward[c]=std::max(0.001f,std::max(std::abs(mesh->minimum[c]),std::abs(mesh->maximum[c])));
            fwd_obj.extra_forward[3]=fwd.emissive_multiplier;
        }
        if(distortion_pass){auto const& settings=mat_res->distortion;
            object.parameters[0]=settings.scale[0]/255;object.parameters[1]=settings.scale[1]/255;
            object.parameters[2]=phase*settings.panner[0];object.parameters[3]=phase*settings.panner[1];}
        b.bind_shader(rhi::shader_stage::pixel,debug_mode ? m_diagnostic_ps : mask_pass ? m_mask_ps[variant] : distortion_pass ? m_distortion_ps : forward_pass ? m_forward_ps.at(forward_key) : shadow_pass ? m_shadow_ps[variant] : material_pass ? m_material_ps.at(variant) : gbuffer ? m_gbuffer_ps.at(variant) : m_ps[variant]);
        bool wire=diagnostic==wireframe_view_mode || diagnostic==wireframe_two_sided_view_mode;
        b.set_rasterizer_state(wire?rhi::fill_mode::wireframe:rhi::fill_mode::solid,wire?(diagnostic==wireframe_two_sided_view_mode?rhi::cull_mode::none:rhi::cull_mode::back):cull);
        for (unsigned r=0;r<4;++r) for (unsigned c=0;c<4;++c) {
            object.world[r*4+c]=item.transform.elements[c][r];
            fwd_obj.world[r*4+c]=item.transform.elements[c][r];
        }
        if(debug_mode) {
            surface_object_constants data={};data.object=object;data.object.parameters[0]=float(diagnostic);data.surface[0]=m_fragment_work[variant];data.surface[3]=alpha_test?1.f:0.f;
            float minx=1,miny=1,maxx=-1,maxy=-1;
            for(unsigned corner=0;corner<8;++corner) {
                auto p=item.transform.transform_position({corner&1?mesh->maximum[0]:mesh->minimum[0],corner&2?mesh->maximum[1]:mesh->minimum[1],corner&4?mesh->maximum[2]:mesh->minimum[2]});
                float value[4]={p.x,p.y,p.z,1},view[4]={},clip[4]={};
                for(unsigned r=0;r<4;++r)for(unsigned c=0;c<4;++c)view[r]+=camera.view[r*4+c]*value[c];
                for(unsigned r=0;r<4;++r)for(unsigned c=0;c<4;++c)clip[r]+=camera.projection[r*4+c]*view[c];
                if(clip[3]<=0)continue;float x=std::clamp(clip[0]/clip[3],-1.f,1.f),y=std::clamp(clip[1]/clip[3],-1.f,1.f);
                minx=std::min(minx,x);maxx=std::max(maxx,x);miny=std::min(miny,y);maxy=std::max(maxy,y);
            }
            float area=std::max(1.f,(maxx-minx)*(maxy-miny)*width*height*.25f);data.surface[1]=(mesh->indices.size()/3)/area;
            b.update_buffer(m_diagnostic_object,&data,sizeof(data));
        }
        else if (material_pass) b.update_buffer(m_material_object,&ext_material,sizeof(ext_material));
        else if (forward_pass) b.update_buffer(m_forward_object,&fwd_obj,sizeof(fwd_obj));
        else if (gbuffer) {
            detailed_constants.mapped=constants;
            b.update_buffer(m_surface_object,&ext_gbuffer,sizeof(ext_gbuffer));
        }
        else b.update_buffer(m_object,&object,sizeof(object));
        b.bind_vertex_buffer(it->vertices,0); b.bind_index_buffer(it->indices,0);
        b.draw_indexed(u32(mesh->indices.size()),0,0);
    }
}
} } }
