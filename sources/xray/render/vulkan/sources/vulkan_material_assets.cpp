#include "pch.h"
#include <xray/render/vulkan/material_resources.h>
#include <xray/render/vulkan/decal_resources.h>
#include <xray/render/core/sources/rhi/content_hash.h>
#include "texture_format.h"
#include <cstring>
#include <cmath>
#include <filesystem>
#include <fstream>
#include <mutex>
#include <stdexcept>
#include <string>
#include <unordered_map>
#include <vector>

namespace xray { namespace render { namespace vulkan {
namespace {
// The legacy DDS_HEADER disk structure, independent of D3D/DXGI types.
struct dds_pixel_format { u32 size,flags,fourcc,bits,r,g,b,a; };
struct dds_header {
    u32 size,flags,height,width,pitch,depth,mip_count,reserved[11];
    dds_pixel_format pixel;
    u32 caps,caps2,caps3,caps4,reserved2;
};
static_assert(sizeof(dds_header)==124);
bool filter_valid(rhi::sampler_filter f) { return f==rhi::sampler_filter::nearest || f==rhi::sampler_filter::linear; }
bool address_valid(rhi::sampler_address a) { return a==rhi::sampler_address::repeat || a==rhi::sampler_address::clamp || a==rhi::sampler_address::mirror; }
}
std::shared_ptr<texture_data const> load_dds_texture(const_buffer source, bool srgb) {
    if (!source.c_ptr() || source.size()<128 || source.size()>64u*1024u*1024u)
        throw std::runtime_error("DDS is missing, truncated or exceeds 64 MiB");
    auto* bytes=static_cast<unsigned char const*>(source.c_ptr());
    u32 magic; dds_header header;
    std::memcpy(&magic,bytes,4); std::memcpy(&header,bytes+4,sizeof(header));
    auto const& p=header.pixel;
    if (magic!=0x20534444 || header.size!=124 || p.size!=32 || (header.flags & 0x1007)!=0x1007
        || !(header.caps & 0x1000) || header.depth || (header.flags & 0x800000))
        throw std::runtime_error("DDS must be a 2D texture or complete cubemap");
    bool cube=header.caps2==0xfe00;
    if ((header.caps2 && !cube) || (cube && (!(header.caps & 8) || header.width!=header.height)))
        throw std::runtime_error("DDS cubemap requires six square faces and COMPLEX caps");
    bool compressed=p.flags==4 || p.flags==5;
    rhi::format format=srgb ? rhi::format::r8g8b8a8_srgb : rhi::format::r8g8b8a8_unorm;
    if (compressed) {
        switch (p.fourcc) {
            case 0x31545844: format=srgb ? rhi::format::bc1_srgb : rhi::format::bc1_unorm; break;
            case 0x33545844: format=srgb ? rhi::format::bc2_srgb : rhi::format::bc2_unorm; break;
            case 0x35545844: format=srgb ? rhi::format::bc3_srgb : rhi::format::bc3_unorm; break;
            default: throw std::runtime_error("Unsupported DDS FourCC (expected DXT1, DXT3 or DXT5)");
        }
    } else if (p.flags!=0x41 || p.fourcc || p.bits!=32 || p.g!=0xff00 || p.a!=0xff000000
        || !((p.r==0xff && p.b==0xff0000) || (p.r==0xff0000 && p.b==0xff)))
        throw std::runtime_error("Unsupported DDS pixel format");
    if (!header.width || !header.height || header.width>4096 || header.height>4096
        || (!compressed && (header.flags & 8) && header.pitch!=header.width*4))
        throw std::runtime_error("DDS dimensions or row pitch are unsupported");
    u32 levels=(header.flags & 0x20000) ? header.mip_count : 1;
    u32 max_levels=1; for (u32 side=std::max(header.width,header.height);side>1;side/=2) ++max_levels;
    if (!levels || levels>max_levels || (!(header.flags & 0x20000) && header.mip_count>1))
        throw std::runtime_error("Invalid DDS mip count");
    auto result=std::make_shared<texture_data>();
    result->description.width=header.width; result->description.height=header.height; result->description.mip_levels=levels;
    result->description.pixel_format=format;
    result->description.type=cube ? rhi::texture_type::type_cube : rhi::texture_type::type_2d;
    result->description.array_layers=cube ? 6 : 1;
    size_t offset=128;
    for (u32 layer=0;layer<result->description.array_layers;++layer)
    for (u32 level=0;level<levels;++level) {
        // DDS writers disagree on dwPitchOrLinearSize; derive tight BC rows from dimensions.
        size_t count=sampled_mip_layout(format,std::max(1u,header.width>>level),std::max(1u,header.height>>level)).size;
        if (count>source.size()-offset) throw std::runtime_error("Truncated DDS mip payload");
        auto& pixels=result->mips.emplace_back(bytes+offset,bytes+offset+count);
        if (!compressed && p.r==0xff0000) for (size_t i=0;i<count;i+=4) std::swap(pixels[i],pixels[i+2]);
        offset+=count;
    }
    if (offset!=source.size()) throw std::runtime_error("DDS has extra or inconsistent mip data");
    return result;
}
namespace {
std::shared_ptr<texture_data const> load_texture(const_buffer source,bool srgb,bool legacy,bool force_linear) {
    if (legacy) {
        if (!source.c_ptr() || source.size()<129) throw std::runtime_error("Truncated legacy cooked texture");
        auto* bytes=static_cast<unsigned char const*>(source.c_ptr());
        unsigned flag=bytes[source.size()-1];
        if (flag>1) throw std::runtime_error("Invalid legacy texture sRGB footer");
        srgb=flag!=0; source=const_buffer(bytes,source.size()-1);
    }
    return load_dds_texture(source,srgb && !force_linear);
}

std::string compute_tga_cache_key(const_buffer source,texture_configuration const& config,unsigned dim) {
    rhi::cache_util::sha256_hasher hasher;
    static constexpr char tag[]="xray_tga_v1";
    hasher.update(tag,sizeof(tag)-1);
    u64 const byte_count=u64(source.size());
    hasher.update_pod(byte_count);
    hasher.update(source.c_ptr(),source.size());
    u8 const srgb=u8(config.srgb && !config.force_linear);
    u8 const mips=u8(config.generate_mips);
    u8 const pack_norm=u8(config.pack_tga_normal);
    u8 const cube=u8(config.source_cube || dim==3u);
    u32 const comp=u32(config.source_compression);
    u8 const hq=u8(config.high_quality);
    u32 const dimension=u32(dim);
    u8 const req_rgba=u8(config.require_rgba);
    hasher.update_pod(srgb);
    hasher.update_pod(mips);
    hasher.update_pod(pack_norm);
    hasher.update_pod(cube);
    hasher.update_pod(comp);
    hasher.update_pod(hq);
    hasher.update_pod(dimension);
    hasher.update_pod(req_rgba);
    return rhi::cache_util::hex_string(hasher.finalize());
}

bool valid_cached_texture_format(u32 raw_fmt) noexcept {
    auto const f=static_cast<rhi::format>(raw_fmt);
    return f==rhi::format::r8g8b8a8_unorm || f==rhi::format::r8g8b8a8_srgb
        || f==rhi::format::bc1_unorm || f==rhi::format::bc1_srgb
        || f==rhi::format::bc2_unorm || f==rhi::format::bc2_srgb
        || f==rhi::format::bc3_unorm || f==rhi::format::bc3_srgb;
}

bool valid_cached_texture_type(u32 raw_type) noexcept {
    auto const t=static_cast<rhi::texture_type>(raw_type);
    return t==rhi::texture_type::type_2d || t==rhi::texture_type::type_2d_array || t==rhi::texture_type::type_cube;
}

std::shared_ptr<texture_data const> try_load_tga_disk_cache(std::filesystem::path const& file_path) {
    std::error_code ec;
    auto const file_size=std::filesystem::file_size(file_path,ec);
    constexpr u64 kMaxDiskBytes=256ull*1024*1024+16384;
    if (ec || file_size<40 || file_size>kMaxDiskBytes) return nullptr;
    std::ifstream in(file_path,std::ios::binary);
    if (!in) return nullptr;
    std::vector<unsigned char> raw(static_cast<size_t>(file_size));
    if (!in.read(reinterpret_cast<char*>(raw.data()),static_cast<std::streamsize>(raw.size()))
        || static_cast<size_t>(in.gcount())!=raw.size())
        return nullptr;

    size_t pos=0;
    auto read_u32=[&](u32& out)->bool {
        if (pos+sizeof(u32)>raw.size()) return false;
        std::memcpy(&out,raw.data()+pos,sizeof(u32));
        pos+=sizeof(u32);
        return true;
    };
    auto read_u64=[&](u64& out)->bool {
        if (pos+sizeof(u64)>raw.size()) return false;
        std::memcpy(&out,raw.data()+pos,sizeof(u64));
        pos+=sizeof(u64);
        return true;
    };

    u32 magic=0,version=0,width=0,height=0,mip_levels=0,array_layers=0,type_val=0,fmt_val=0,mip_count=0;
    if (!read_u32(magic) || !read_u32(version) || !read_u32(width) || !read_u32(height)
        || !read_u32(mip_levels) || !read_u32(array_layers) || !read_u32(type_val)
        || !read_u32(fmt_val) || !read_u32(mip_count))
        return nullptr;
    if (magic!=0x58544558u || version!=1u
        || !width || width>4096 || !height || height>4096
        || !mip_levels || mip_levels>13 || !array_layers || array_layers>64
        || !valid_cached_texture_type(type_val) || !valid_cached_texture_format(fmt_val)
        || u64(mip_count)!=u64(mip_levels)*u64(array_layers))
        return nullptr;

    auto result=std::make_shared<texture_data>();
    result->description.width=width;
    result->description.height=height;
    result->description.mip_levels=mip_levels;
    result->description.array_layers=array_layers;
    result->description.type=static_cast<rhi::texture_type>(type_val);
    result->description.pixel_format=static_cast<rhi::format>(fmt_val);
    result->mips.reserve(mip_count);

    constexpr u64 kMaxMipPayload=256ull*1024*1024;
    u64 total_mip_bytes=0;
    for (u32 i=0;i<mip_count;++i) {
        u64 mip_bytes=0;
        if (!read_u64(mip_bytes) || mip_bytes>kMaxMipPayload-total_mip_bytes || mip_bytes>raw.size()-pos)
            return nullptr;
        total_mip_bytes+=mip_bytes;
        auto const* ptr=raw.data()+pos;
        result->mips.emplace_back(ptr,ptr+static_cast<size_t>(mip_bytes));
        pos+=static_cast<size_t>(mip_bytes);
    }

    u32 footer=0;
    if (!read_u32(footer) || footer!=0x444E4554u || pos!=raw.size())
        return nullptr;
    return result;
}

void write_tga_disk_cache(std::filesystem::path const& file_path,texture_data const& tex) {
    std::error_code ec;
    if (std::filesystem::is_regular_file(file_path,ec)) return;
    u32 const magic=0x58544558u,version=1u,footer=0x444E4554u;
    u32 const type_val=static_cast<u32>(tex.description.type);
    u32 const fmt_val=static_cast<u32>(tex.description.pixel_format);
    u32 const mip_count=u32(tex.mips.size());
    size_t total_size=9*sizeof(u32)+sizeof(u32);
    for (auto const& mip:tex.mips) total_size+=sizeof(u64)+mip.size();
    std::vector<unsigned char> blob(total_size);
    size_t pos=0;
    auto push_u32=[&](u32 v) { std::memcpy(blob.data()+pos,&v,sizeof(v)); pos+=sizeof(v); };
    auto push_u64=[&](u64 v) { std::memcpy(blob.data()+pos,&v,sizeof(v)); pos+=sizeof(v); };
    push_u32(magic);
    push_u32(version);
    push_u32(tex.description.width);
    push_u32(tex.description.height);
    push_u32(tex.description.mip_levels);
    push_u32(tex.description.array_layers);
    push_u32(type_val);
    push_u32(fmt_val);
    push_u32(mip_count);
    for (auto const& mip:tex.mips) {
        push_u64(u64(mip.size()));
        if (!mip.empty()) {
            std::memcpy(blob.data()+pos,mip.data(),mip.size());
            pos+=mip.size();
        }
    }
    push_u32(footer);
    rhi::cache_util::atomic_write_file(file_path,blob.data(),blob.size());
}

std::shared_ptr<texture_data const> load_cached_tga_texture(const_buffer source,texture_configuration const& config,unsigned dim) {
    std::string const key=compute_tga_cache_key(source,config,dim);
    static std::mutex g_tex_cache_mutex;
    static std::unordered_map<std::string,std::weak_ptr<texture_data const>> g_tex_mem_cache;
    {
        std::lock_guard lock(g_tex_cache_mutex);
        if (auto it=g_tex_mem_cache.find(key);it!=g_tex_mem_cache.end()) {
            if (auto existing=it->second.lock()) return existing;
            g_tex_mem_cache.erase(it);
        }
    }

    auto const user_data=rhi::cache_util::resolve_sdk_user_data_dir();
    std::filesystem::path disk_path;
    std::shared_ptr<texture_data const> cooked;
    if (!user_data.empty()) {
        disk_path=user_data/L"cooked_cache"/("tex_v1_"+key+".bin");
        cooked=try_load_tga_disk_cache(disk_path);
    }

    bool const from_disk=bool(cooked);
    if (!cooked) {
        cooked=load_tga_texture(source,config.srgb && !config.force_linear,config.generate_mips,config.pack_tga_normal,config.source_cube || dim==3u,config.source_compression,config.high_quality,dim);
        if (config.require_rgba) cooked=rgba_texture(cooked);
    }

    {
        std::lock_guard lock(g_tex_cache_mutex);
        if (auto it=g_tex_mem_cache.find(key);it!=g_tex_mem_cache.end()) {
            if (auto existing=it->second.lock()) return existing;
        }
        if (g_tex_mem_cache.size()>512) {
            for (auto it=g_tex_mem_cache.begin();it!=g_tex_mem_cache.end();) {
                if (it->second.expired()) it=g_tex_mem_cache.erase(it);
                else ++it;
            }
        }
        g_tex_mem_cache[key]=cooked;
    }

    if (!from_disk && !disk_path.empty() && cooked)
        write_tga_disk_cache(disk_path,*cooked);
    return cooked;
}
}
texture_resource::texture_resource(std::shared_ptr<scene_registry> registry, const_buffer source, bool srgb, bool legacy,std::string physical_path)
    : texture_resource(std::move(registry),source,texture_configuration{srgb,legacy},std::move(physical_path)) {}
texture_resource::texture_resource(std::shared_ptr<scene_registry> registry, const_buffer source, texture_configuration const& config,std::string physical_path,std::string virtual_path)
    : owner(std::move(registry)), state(std::make_shared<texture_state>()) {
    if (!owner || !owner->accepting()) throw std::runtime_error("Texture resource session is closed");
    if (config.source_tga && config.legacy_cooked) throw std::runtime_error("TGA cannot use a cooked DDS footer");
    if (config.pack_tga_normal && !config.source_tga) throw std::runtime_error("NORMAL_MAP source packing requires TGA input");
    if ((config.source_cube || config.source_dimension!=1) && !config.source_tga) throw std::runtime_error("Multi-layer/volume source configuration requires TGA input");
    unsigned dim=config.source_dimension?config.source_dimension:(config.source_cube?3u:1u);
    if (config.source_cube && config.source_dimension==1) dim=3u;
    if (config.source_tga) {
        state->data=load_cached_tga_texture(source,config,dim);
    } else {
        state->data=load_texture(source,config.srgb,config.legacy_cooked,config.force_linear);
        if(config.require_rgba)state->data=rgba_texture(state->data);
    }
    state->physical_path=std::move(physical_path);state->virtual_path=std::move(virtual_path); state->configuration=config; state->configuration.reload_read=true;
    if (!state->physical_path.empty()) owner->track_texture(state);
}
texture_resource::texture_resource(std::shared_ptr<scene_registry> registry,std::shared_ptr<texture_data const> data)
    :owner(std::move(registry)),state(std::make_shared<texture_state>()) {
    if(!owner || !owner->accepting() || !data || data->description.type!=rhi::texture_type::type_2d || data->description.array_layers!=1 || !data->description.width || !data->description.height || !data->description.mip_levels || data->mips.size()!=data->description.mip_levels)throw std::runtime_error("Invalid private texture face view");
    state->data=std::move(data);
}
std::shared_ptr<texture_data const> texture_resource::snapshot() const { std::lock_guard lock(state->mutex); return state->data; }
std::shared_ptr<texture_data const> texture_resource::linear_snapshot() const {
    auto value=snapshot();
    if (!value || value->description.type!=rhi::texture_type::type_2d || (value->description.pixel_format!=rhi::format::r8g8b8a8_unorm
        && value->description.pixel_format!=rhi::format::bc1_unorm && value->description.pixel_format!=rhi::format::bc2_unorm
        && value->description.pixel_format!=rhi::format::bc3_unorm)) throw std::runtime_error("Surface maps require linear RGBA/BC texture data");
    return value;
}
texture_resource::~texture_resource() = default;
material_resource::material_resource(std::shared_ptr<scene_registry> state, material_configuration const& config,surface_material_parameters const& parameters,surface_material_textures const& maps,material_contributions const& extra,material_blend_mode mode,distortion_material_parameters const& field,std::string identity)
    : owner(std::move(state)), data(std::make_shared<material_configuration const>(config)),virtual_path(std::move(identity)),surface(parameters),textures(maps),contributions(extra),blend(mode),distortion(field) {
    if (!owner || !owner->accepting()) throw std::runtime_error("Material resource session is closed");
    if(distortion.enabled){
        if(mode!=material_blend_mode::opaque || !config.texture || config.alpha_test || maps.normal || maps.roughness || maps.specular || maps.detail_normal
            || maps.emissive || maps.detail_diffuse || maps.variation_mask || maps.fresnel || maps.anisotropy_dir || maps.diffuse_power
            || maps.bump || maps.translucency || maps.whirl || maps.transparency
            || extra.emission!=std::array<float,3>{})
            throw std::runtime_error("Distortion requires an exclusive RGBA field texture");
        for(float v:distortion.scale)if(!std::isfinite(v)||v<0||v>64)throw std::runtime_error("Distortion scale outside [0,64]");
        for(float v:distortion.panner)if(!std::isfinite(v)||std::abs(v)>16)throw std::runtime_error("Distortion panner outside [-16,16]");
    }
    if (blend!=material_blend_mode::opaque && blend!=material_blend_mode::alpha && blend!=material_blend_mode::additive
        && blend!=material_blend_mode::modulate && blend!=material_blend_mode::subtractive)
        throw std::runtime_error("Unsupported material blend mode");
    if (blend!=material_blend_mode::opaque && !(maps.stages_mask & (material_stage_gbuffer|material_stage_lighting))
        && (maps.normal || maps.roughness || maps.specular || maps.detail_normal
        || maps.emissive || maps.detail_diffuse || maps.variation_mask || maps.fresnel || maps.anisotropy_dir || maps.diffuse_power
        || maps.bump || maps.translucency || maps.whirl
        || extra.emission!=std::array<float,3>{} || parameters.roughness_uv!=std::array<float,2>{1,1}
        || parameters.fresnel_at_0_degree!=.04f || parameters.roughness_range!=std::array<float,2>{0,1}
        || parameters.specular_color!=std::array<float,3>{1,1,1}))
        throw std::runtime_error("Forward material does not accept deferred surface lighting inputs");
    if (!std::isfinite(maps.reflection.min_angle) || maps.reflection.min_angle<0 || maps.reflection.min_angle>90
        || !std::isfinite(maps.reflection.power) || maps.reflection.power<0 || maps.reflection.power>16)
        throw std::runtime_error("Invalid reflection parameters");
    for (float c : maps.reflection.specular_color_multiplier) if (!std::isfinite(c) || c<0 || c>16) throw std::runtime_error("Invalid reflection color multiplier");
    if (!std::isfinite(maps.sss.scattering_depth) || maps.sss.scattering_depth<0 || maps.sss.scattering_depth>16
        || !std::isfinite(maps.sss.scattering_min_depth) || maps.sss.scattering_min_depth<0 || maps.sss.scattering_min_depth>16
        || !std::isfinite(maps.sss.scattering_color_multiplier) || maps.sss.scattering_color_multiplier<0 || maps.sss.scattering_color_multiplier>16
        || !std::isfinite(maps.sss.subdermal_weight) || maps.sss.subdermal_weight<0 || maps.sss.subdermal_weight>16)
        throw std::runtime_error("Invalid subsurface scattering parameters");
    for (float w : maps.sss.blurring_weights) if (!std::isfinite(w) || w<0 || w>16) throw std::runtime_error("Invalid SSS blurring weights");
    if (!std::isfinite(maps.parallax.scale) || maps.parallax.scale<0 || maps.parallax.scale>16)
        throw std::runtime_error("Invalid parallax height scale");
    if (!std::isfinite(maps.translucency_params.constant_translucency) || maps.translucency_params.constant_translucency<0 || maps.translucency_params.constant_translucency>255
        || !std::isfinite(maps.translucency_params.alpha_model_parameter) || maps.translucency_params.alpha_model_parameter<0 || maps.translucency_params.alpha_model_parameter>1)
        throw std::runtime_error("Invalid translucency or alpha_model_parameter");
    if (!std::isfinite(maps.forward.color_power) || maps.forward.color_power<=0 || maps.forward.color_power>16
        || !std::isfinite(maps.forward.soft_distance) || maps.forward.soft_distance<=0 || maps.forward.soft_distance>1024
        || !std::isfinite(maps.forward.attenuation_scale) || maps.forward.attenuation_scale<0 || maps.forward.attenuation_scale>64
        || !std::isfinite(maps.forward.uv_tile) || std::abs(maps.forward.uv_tile)>4096
        || !std::isfinite(maps.forward.emissive_multiplier) || maps.forward.emissive_multiplier<0 || maps.forward.emissive_multiplier>64)
        throw std::runtime_error("Invalid forward material parameters");
    for (float m : maps.forward.move_direction) if (!std::isfinite(m) || std::abs(m)>4096) throw std::runtime_error("Invalid forward move_direction");
    if (maps.sequence.enabled && (!std::isfinite(maps.sequence.width) || maps.sequence.width<1 || maps.sequence.width>64
        || !std::isfinite(maps.sequence.height) || maps.sequence.height<1 || maps.sequence.height>64
        || !std::isfinite(maps.sequence.start_frame) || !std::isfinite(maps.sequence.play_speed)))
        throw std::runtime_error("Invalid sequence atlas parameters");
    if (maps.variation.enabled) {
        if (!std::isfinite(maps.variation.position_divider) || std::abs(maps.variation.position_divider)<1e-4f
            || !std::isfinite(maps.variation.rotate) || !std::isfinite(maps.variation.scale) || !std::isfinite(maps.variation.multiply))
            throw std::runtime_error("Invalid variation mask parameters");
        for (float c : maps.variation.color) if (!std::isfinite(c) || c<0 || c>16) throw std::runtime_error("Invalid variation mask color");
    }
    if (!std::isfinite(maps.diffuse_mod.attenuation_multiplier) || maps.diffuse_mod.attenuation_multiplier<0 || maps.diffuse_mod.attenuation_multiplier>16
        || !std::isfinite(maps.diffuse_mod.diffuse_power) || maps.diffuse_mod.diffuse_power<=0 || maps.diffuse_mod.diffuse_power>16)
        throw std::runtime_error("Invalid diffuse modulation parameters");
    for (float c : maps.diffuse_mod.attenuation_color) if (!std::isfinite(c) || c<0 || c>16) throw std::runtime_error("Invalid diffuse attenuation color");
    for (float value : contributions.emission)
        if (!std::isfinite(value) || value<0 || value>16) throw std::runtime_error("Emission requires finite linear [0,16] channels");
    for (float value : {surface.roughness_uv[0],surface.roughness_uv[1],surface.fresnel_at_0_degree})
        if (!std::isfinite(value) || value<0 || value>1) throw std::runtime_error("Material surface parameters must be finite and normalized");
    for (float value : surface.roughness_range)
        if (!std::isfinite(value) || value<0 || value>surface_material_parameters::maximum_roughness_remap)
            throw std::runtime_error("Material roughness remap must be finite and within [0,16]");
    if (surface.roughness_range[0]>surface.roughness_range[1]) throw std::runtime_error("Material roughness range is reversed");
    for (float value : surface.specular_color) if (!std::isfinite(value) || value<0 || value>16)
        throw std::runtime_error("Material specular color must be finite and within [0,16]");
    if (!std::isfinite(textures.detail.scale) || textures.detail.scale<0 || textures.detail.scale>16
        || !std::isfinite(textures.detail.tile) || textures.detail.tile<0 || textures.detail.tile>4096)
        throw std::runtime_error("Invalid detail normal scale or tiling");
    if (textures.detail_normal && !textures.normal) throw std::runtime_error("Detail normal requires a base normal map");
    for (auto const& map : {textures.normal,textures.roughness,textures.detail_normal,textures.fresnel,textures.anisotropy_dir,textures.diffuse_power,textures.bump,textures.translucency,textures.transparency,textures.cubemap_mask,textures.ao,textures.scattering_amount,textures.scattering_depth}) if (map) {
        auto* texture=dynamic_cast<texture_resource*>(map.c_ptr());
        if (!texture || texture->owner!=owner) throw std::runtime_error("Surface map belongs to another resource session or type");
        texture->linear_snapshot();
    }
    for (auto const& map : {textures.specular,textures.emissive,textures.detail_diffuse,textures.variation_mask,textures.whirl,textures.subdermal,textures.back_illumination}) if (map) {
        auto* texture=dynamic_cast<texture_resource*>(map.c_ptr());
        if (!texture || texture->owner!=owner) throw std::runtime_error("Material map belongs to another resource session or type");
        if (texture->snapshot()->description.type!=rhi::texture_type::type_2d) throw std::runtime_error("Material map requires a 2D texture");
    }
    if (textures.cubemap) {
        auto* texture=dynamic_cast<texture_resource*>(textures.cubemap.c_ptr());
        if (!texture || texture->owner!=owner) throw std::runtime_error("Cubemap texture belongs to another resource session or type");
        if (texture->snapshot()->description.type!=rhi::texture_type::type_cube) throw std::runtime_error("Reflection cubemap requires a Cube texture");
    }
    auto* texture=dynamic_cast<texture_resource*>(config.texture.c_ptr());
    if (config.texture && (!texture || texture->owner!=owner)) throw std::runtime_error("Material texture belongs to another resource session or type");
    if (texture && texture->snapshot()->description.type!=rhi::texture_type::type_2d) throw std::runtime_error("Static material requires a 2D diffuse texture");
    if(distortion.enabled && texture->linear_snapshot()->description.pixel_format!=rhi::format::r8g8b8a8_unorm)
        throw std::runtime_error("Distortion requires linear RGBA8 data");
    for (float value : config.tint) if (!std::isfinite(value) || value<0 || value>1) throw std::runtime_error("Material tint must be finite and normalized");
    if (!std::isfinite(config.alpha_ref) || config.alpha_ref<0 || config.alpha_ref>1)
        throw std::runtime_error("Material alpha reference must be finite and normalized");
    for (float value : config.uv_scale) if (!std::isfinite(value) || std::abs(value)>4096)
        throw std::runtime_error("Material UV scale must be finite and within +/-4096");
    if (config.cull!=rhi::cull_mode::none && config.cull!=rhi::cull_mode::back && config.cull!=rhi::cull_mode::front)
        throw std::runtime_error("Invalid material cull mode");
    auto const& s=config.sampler;
    if (!filter_valid(s.min_filter) || !filter_valid(s.mag_filter) || !filter_valid(s.mip_filter)
        || !address_valid(s.address_u) || !address_valid(s.address_v) || !std::isfinite(s.min_lod) || !std::isfinite(s.max_lod)
        || !std::isfinite(s.lod_bias) || s.min_lod<0 || s.max_lod<s.min_lod || s.anisotropy!=1
        || s.comparison || s.compare<rhi::comparison_func::never || s.compare>rhi::comparison_func::always)
        throw std::runtime_error("Unsupported material sampler descriptor");
}
material_resource::~material_resource() = default;
void scene_registry::track_material(std::string const& key) {
    if(key.empty() || key.find("/__sdk6_preview/")!=std::string::npos)return;std::lock_guard lock(m_mutex);if(!m_accepting || (m_material_keys.size()>=1024 && !m_material_keys.contains(key)))throw std::runtime_error("Material source registry is closed or full");m_material_keys.insert(key);
}
std::vector<std::string> scene_registry::material_keys() const {std::lock_guard lock(m_mutex);return {m_material_keys.begin(),m_material_keys.end()};}
std::map<std::string,resources::unmanaged_resource_ptr> scene_registry::material_overrides() const {std::lock_guard lock(m_mutex);return m_material_overrides;}
resources::unmanaged_resource_ptr scene_registry::material_override(std::string const& key) const {std::lock_guard lock(m_mutex);auto it=m_material_overrides.find(key);return it==m_material_overrides.end()?nullptr:it->second;}
void scene_registry::publish_materials(std::map<std::string,resources::unmanaged_resource_ptr> const& updates) {
    std::map<std::string,resources::unmanaged_resource_ptr> next;std::set<std::string> keys;
    {std::lock_guard lock(m_mutex);if(!m_accepting)throw std::runtime_error("Material publication session closed");next=m_material_overrides;keys=m_material_keys;}
    for(auto const& [key,resource]:updates){
        auto* value=dynamic_cast<material_resource*>(resource.c_ptr());
        auto* decal=dynamic_cast<decal_material_resource*>(resource.c_ptr());
        scene_registry* resource_owner=value?value->owner.get():decal?decal->owner.get():nullptr;
        if(!resource_owner || resource_owner!=this)throw std::runtime_error("Foreign material publication");
        next.insert_or_assign(key,resource);keys.insert(key);
    }
    if(keys.size()>1024)throw std::runtime_error("Material source registry exceeds 1024 identities");
    {std::lock_guard lock(m_mutex);if(!m_accepting)throw std::runtime_error("Material publication session closed");m_material_overrides.swap(next);m_material_keys.swap(keys);}
}

} } }
