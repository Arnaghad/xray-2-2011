#include "pch.h"
#include <xray/render/vulkan/model_resources.h>
#include <xray/memory_chunk_reader.h>
#include <xray/render/engine/model_format.h>
#include <cstring>
#include <cmath>
#include <limits>
#include <stdexcept>

namespace xray { namespace render { namespace vulkan {
namespace {
// D3D9 declaration bytes are a disk contract, not a dependency on D3D APIs.
unsigned char const static_declaration[] = {
    0,0,0,0,2,0,0,0, 0,0,12,0,4,0,3,0, 0,0,16,0,4,0,6,0,
    0,0,20,0,4,0,7,0, 0,0,24,0,1,0,5,0, 255,0,0,0,17,0,0,0
};
u32 word(unsigned char const* p) { u32 value; std::memcpy(&value,p,sizeof(value)); return value; }
void validate_mesh(static_mesh_data& mesh) {
    mesh.minimum.fill(std::numeric_limits<float>::max()); mesh.maximum.fill(std::numeric_limits<float>::lowest());
    for (auto const& vertex : mesh.vertices) {
        for (float value : vertex.uv) if (!std::isfinite(value)) throw std::runtime_error("Non-finite static UV");
        for (unsigned axis=0; axis<3; ++axis) {
            float value=vertex.position[axis];
            if (!std::isfinite(value)) throw std::runtime_error("Non-finite static position");
            mesh.minimum[axis]=std::min(mesh.minimum[axis],value);
            mesh.maximum[axis]=std::max(mesh.maximum[axis],value);
        }
    }
    for (u16 index : mesh.indices) if (index>=mesh.vertices.size()) throw std::runtime_error("Static index exceeds vertex count");
}
}
void validate_model_buffer_fragments(vectora<buffer_fragment> const& fragments, size_t byte_size) {
    if (fragments.empty() || fragments.size()>4096) throw std::runtime_error("Model update requires 1..4096 fragments");
    size_t total=0;
    for (auto const& fragment : fragments) {
        if (!fragment.buffer || !fragment.size || fragment.start>byte_size || fragment.size>byte_size-fragment.start)
            throw std::runtime_error("Model buffer fragment is empty or out of bounds");
        if (fragment.size>64u*1024u*1024u-total) throw std::runtime_error("Model update exceeds 64 MiB aggregate limit");
        total+=fragment.size;
    }
}
std::shared_ptr<static_mesh_data const> load_static_mesh(const_buffer source) {
    if (!source.c_ptr() || source.size() > 64u*1024u*1024u)
        throw std::runtime_error("Static model is missing or exceeds the 64 MiB import limit");
    auto* bytes = static_cast<unsigned char const*>(source.c_ptr());
    bool vertices = false, indices = false;
    // Preflight bounds before entering the legacy assertion-based chunk reader.
    for (size_t offset=0; offset<source.size();) {
        if (source.size()-offset < 8) throw std::runtime_error("Truncated model chunk header");
        u32 id=word(bytes+offset), size=word(bytes+offset+4);
        if ((size & 0xc0000000u) || size > source.size()-offset-8)
            throw std::runtime_error("Unsupported or truncated model chunk");
        if (id == model_chunk_vertices) {
            if (vertices) throw std::runtime_error("Duplicate vertex chunk"); vertices = true;
        } else if (id == model_chunk_indices) {
            if (indices) throw std::runtime_error("Duplicate index chunk"); indices = true;
        } else throw std::runtime_error("Only converted static surface chunks 3/4 are supported");
        offset += 8+size;
    }
    if (!vertices || !indices) throw std::runtime_error("Missing static model vertices/indices");
    memory::chunk_reader chunks(static_cast<pcbyte>(source.c_ptr()),u32(source.size()),memory::chunk_reader::chunk_type_sequential);
    auto vr=chunks.open_reader(model_chunk_vertices), ir=chunks.open_reader(model_chunk_indices);
    constexpr size_t declaration_size=sizeof(static_declaration);
    if (vr.length()<declaration_size+4 || std::memcmp(vr.pointer(),static_declaration,declaration_size))
        throw std::runtime_error("Unsupported static vertex declaration");
    vr.advance(u32(declaration_size));
    u32 vertex_count=vr.r_u32();
    if (!vertex_count || vertex_count>65536 || vr.length()-declaration_size-4 != size_t(vertex_count)*sizeof(static_vertex))
        throw std::runtime_error("Invalid static vertex count/stride");
    if (ir.length()<4) throw std::runtime_error("Truncated static indices");
    u32 index_count=ir.r_u32();
    if (!index_count || index_count%3 || size_t(ir.length())-4 != size_t(index_count)*sizeof(u16))
        throw std::runtime_error("Invalid triangle index count");
    auto result=std::make_shared<static_mesh_data>();
    result->vertices.resize(vertex_count); result->indices.resize(index_count);
    std::memcpy(result->vertices.data(),vr.pointer(),vertex_count*sizeof(static_vertex));
    std::memcpy(result->indices.data(),ir.pointer(),index_count*sizeof(u16));
    validate_mesh(*result);
    return result;
}
namespace {
std::shared_ptr<static_model_data const> single_surface(const_buffer source) {
    auto model=std::make_shared<static_model_data>(); auto mesh=load_static_mesh(source);
    model->properties.type=mt_static_mesh;
    model->properties.minimum=mesh->minimum; model->properties.maximum=mesh->maximum;
    auto properties=model->properties; properties.type=mt_static_submesh;
    model->surfaces.push_back({"default",mesh,std::move(properties)});
    return model;
}
}
static_model_resource::static_model_resource(std::shared_ptr<scene_registry> state, const_buffer source)
    : static_model_resource(std::move(state),single_surface(source)) {}
static_model_resource::static_model_resource(std::shared_ptr<scene_registry> state, std::shared_ptr<static_model_data const> model)
    : owner(std::move(state)),data(std::move(model)) {
    if (!owner || !owner->accepting()) throw std::runtime_error("Static model resource session is closed");
    if (!data || data->surfaces.empty()) throw std::runtime_error("Static model has no surfaces");
    surfaces.resize(data->surfaces.size());
    geometry_minimum.fill(std::numeric_limits<float>::max()); geometry_maximum.fill(std::numeric_limits<float>::lowest());
    for (size_t i=0;i<surfaces.size();++i) {
        surfaces[i].material=data->surfaces[i].material;
        surfaces[i].mesh=data->surfaces[i].mesh;
        auto const& mesh=*data->surfaces[i].mesh;
        for (unsigned axis=0;axis<3;++axis) {
            geometry_minimum[axis]=std::min(geometry_minimum[axis],mesh.minimum[axis]);
            geometry_maximum[axis]=std::max(geometry_maximum[axis],mesh.maximum[axis]);
        }
    }
    transform.identity();
}
static_model_resource::~static_model_resource() = default;
void static_model_resource::update_buffer(vectora<buffer_fragment> const& fragments, bool indices) {
    if (!owner->accepting()) throw std::runtime_error("Static model resource session is closed");
    if (!data->inverse_bind_pose.empty()) throw std::runtime_error("Raw buffer updates are unsupported for skeletal models");
    if (surfaces.size()!=1) throw std::runtime_error("Unnamed buffer updates require a single-surface static model");
    auto const& previous=surfaces[0].mesh;
    size_t size=indices ? previous->indices.size()*sizeof(u16) : previous->vertices.size()*sizeof(static_vertex);
    validate_model_buffer_fragments(fragments,size);
    // Publish only after all fragments and the resulting geometry validate.
    auto next=std::make_shared<static_mesh_data>(*previous);
    auto* destination=indices ? reinterpret_cast<unsigned char*>(next->indices.data()) : reinterpret_cast<unsigned char*>(next->vertices.data());
    for (auto const& fragment : fragments) std::memcpy(destination+fragment.start,fragment.buffer,fragment.size);
    validate_mesh(*next);
    geometry_minimum=next->minimum; geometry_maximum=next->maximum;
    surfaces[0].mesh=std::move(next);
}
size_t static_model_resource::surface_index(fs_new::virtual_path_string const& name) const {
    if (name.empty()) {
        if (surfaces.size()==1) return 0;
        throw std::runtime_error("Multi-surface models require an explicit surface name");
    }
    for (size_t i=0;i<data->surfaces.size();++i) if (data->surfaces[i].name==name.c_str()) return i;
    throw std::runtime_error("Unknown static model surface");
}
bool static_model_resource::get_locator(pcstr name, model_locator_item& result) const {
    if (!name) return false;
    for (auto const& item : data->properties.locators) if (std::strcmp(item.m_name,name)==0) { result=item; return true; }
    return false;
}
} } }
