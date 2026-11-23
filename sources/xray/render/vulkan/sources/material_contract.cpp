#include "material_contract.h"
#include "spirv_entry.h"
#include <spirv_reflect.h>
#include <algorithm>
#include <stdexcept>
#include <cstring>
#include <source_location>
#include <unordered_map>
namespace xray { namespace render { namespace vulkan {
namespace {
void require(bool value, std::source_location where = std::source_location::current()) {
    if (!value) throw std::runtime_error("Shader violates material descriptor/constant ABI v1-v12 (check line " + std::to_string(where.line()) + ")");
}
void matrix(SpvReflectBlockVariable const& m, u32 offset, char const* name) {
    require(m.offset == offset && m.size == 64 && m.array.dims_count == 0 && m.member_count == 0
        && m.numeric.scalar.width == 32 && m.numeric.matrix.row_count == 4
        && m.numeric.matrix.column_count == 4 && m.numeric.matrix.stride == 16
        && (m.decoration_flags & SPV_REFLECT_DECORATION_COLUMN_MAJOR)
        && m.type_description && (m.type_description->type_flags & SPV_REFLECT_TYPE_FLAG_FLOAT)
        && m.name && std::strcmp(m.name, name) == 0);
}
void vector(SpvReflectBlockVariable const& m, u32 offset, char const* name) {
    require(m.offset == offset && m.size == 16 && m.array.dims_count == 0 && m.member_count == 0
        && m.numeric.scalar.width == 32 && m.numeric.vector.component_count == 4
        && m.numeric.matrix.column_count == 0 && m.type_description
        && (m.type_description->type_flags & SPV_REFLECT_TYPE_FLAG_FLOAT)
        && m.name && std::strcmp(m.name, name) == 0);
}
void sampling_contract(std::vector<u32> const& words, std::vector<SpvReflectDescriptorBinding*> const& bindings, bool shadow,bool temporal) {
    // DXC uses Depth=2 (unknown) even for SampleCmp; the sampling opcode determines comparison.
    std::unordered_map<u32,u32> slots,loads,pointers;
    std::unordered_map<u32,std::pair<u32,u32>> sampled;
    for (auto const* b : bindings) if (b->set==1 || b->set==3) slots.emplace(b->spirv_id, b->set==3 ? (300u+b->binding) : b->binding);
    bool comparison_seen=false;
    for (size_t i=5;i<words.size();i+=words[i]>>16) {
        auto count=words[i]>>16,opcode=words[i]&0xffff;
        if ((opcode==SpvOpAccessChain || opcode==SpvOpInBoundsAccessChain) && count>=4) {
            u32 base=words[i+3];
            if (auto it=pointers.find(base); it!=pointers.end()) base=it->second;
            pointers[words[i+2]]=base;
        }
        if (opcode==SpvOpLoad && count>=4) {
            u32 src=words[i+3];
            if (auto it=pointers.find(src); it!=pointers.end()) src=it->second;
            loads[words[i+2]]=src;
        }
        if (opcode==SpvOpCopyObject && count>=4) {
            u32 src=words[i+3];
            if (auto it=loads.find(src); it!=loads.end()) loads[words[i+2]]=it->second;
            else if (auto pit=pointers.find(src); pit!=pointers.end()) pointers[words[i+2]]=pit->second;
        }
        if (opcode==SpvOpSampledImage && count==5) {
            auto image=loads.find(words[i+3]),sampler=loads.find(words[i+4]);
            if (image!=loads.end() && sampler!=loads.end() && slots.contains(image->second) && slots.contains(sampler->second))
                sampled[words[i+2]]={slots.at(image->second),slots.at(sampler->second)};
        }
        bool dref=opcode==SpvOpImageSampleDrefImplicitLod || opcode==SpvOpImageSampleDrefExplicitLod;
        bool ordinary=opcode==SpvOpImageSampleImplicitLod || opcode==SpvOpImageSampleExplicitLod;
        if (dref || ordinary) {
            require(count>=5);
            auto pair=sampled.find(words[i+3]);
            require(pair!=sampled.end());
            if (dref) { require(pair->second.first==7 && pair->second.second==6); comparison_seen=true; }
            else require((pair->second.first!=7 || !shadow) && pair->second.second==1 && (!temporal || pair->second.first!=4));
        }
        // Keep the shadow ABI narrow: projected/gather/fetch operations need their own contract.
        if ((opcode>=SpvOpImageSampleProjImplicitLod && opcode<=SpvOpImageDrefGather) && count>=4) {
            auto pair=sampled.find(words[i+3]);
            require(pair==sampled.end() || ((!shadow || pair->second.first!=7) && pair->second.second!=6));
            auto image=loads.find(words[i+3]);
            require(image==loads.end() || !slots.contains(image->second) || !shadow || slots.at(image->second)!=7);
        }
    }
    require(shadow==comparison_seen);
}
}
u64 reflect_material_contract(std::vector<u32> const& words, rhi::shader_stage stage, char const* entry,bool demote,bool multisample,bool bindless) {
    require(spirv_has_entry(words, stage, entry, true,demote,multisample,bindless));
    SpvReflectShaderModule module = {};
    require(spvReflectCreateShaderModule(words.size() * sizeof(u32), words.data(), &module) == SPV_REFLECT_RESULT_SUCCESS);
    struct cleanup { SpvReflectShaderModule* m; ~cleanup() { spvReflectDestroyShaderModule(m); } } guard{&module};
    u32 count = 0;
    u64 mask = 0;
    require(spvReflectEnumerateEntryPointDescriptorBindings(&module, entry, &count, nullptr) == SPV_REFLECT_RESULT_SUCCESS);
    std::vector<SpvReflectDescriptorBinding*> bindings(count);
    require(spvReflectEnumerateEntryPointDescriptorBindings(&module, entry, &count, bindings.data()) == SPV_REFLECT_RESULT_SUCCESS);
    if (stage == rhi::shader_stage::compute) {
        for (auto const* b : bindings) {
            require(b->count == 1 && b->array.dims_count == 0 && b->set == 0);
            if (b->binding == 0 || b->binding == 1) {
                require(b->descriptor_type == SPV_REFLECT_DESCRIPTOR_TYPE_STORAGE_BUFFER);
                mask |= compute_storage_binding;
            } else if (b->binding == 2) {
                require(b->descriptor_type == SPV_REFLECT_DESCRIPTOR_TYPE_UNIFORM_BUFFER);
                mask |= compute_constant_binding;
            } else {
                require(false);
            }
        }
        return mask;
    }
    bool has_comparison_sampler = std::any_of(bindings.begin(), bindings.end(),
        [](SpvReflectDescriptorBinding const* x) { return x->set == 1 && x->binding == 6; });
    for (auto const* b : bindings) {
        if (b->set == 3) {
            require(bindless && stage == rhi::shader_stage::pixel && b->descriptor_type == SPV_REFLECT_DESCRIPTOR_TYPE_SAMPLED_IMAGE);
            require(b->array.dims_count == 1 && b->image.depth != 1 && !b->image.ms && !b->image.arrayed);
            u64 bit = 0;
            if (b->binding == 0) {
                require(b->count == 1024 && b->image.dim == SpvDim2D);
                bit = bindless_2d_binding;
            } else if (b->binding == 1) {
                require(b->count == 128 && b->image.dim == SpvDimCube);
                bit = bindless_cube_binding;
            } else require(false);
            require(!(mask & bit)); mask |= bit;
            continue;
        }
        require(b->count == 1 && b->array.dims_count == 0);
        u64 bit = 0;
        if ((b->set == 0 || b->set == 2) && b->binding == 0) {
            require(b->descriptor_type == SPV_REFLECT_DESCRIPTOR_TYPE_UNIFORM_BUFFER);
            if (b->set == 0) {
                require(stage == rhi::shader_stage::vertex && b->block.size == 128 && b->block.member_count == 2);
                matrix(b->block.members[0], 0, "view"); matrix(b->block.members[1], 64, "projection"); bit = 1;
            } else if (b->block.size==224 && b->block.member_count==8 && b->block.members[0].name && std::strcmp(b->block.members[0].name,"inverse_projection")==0) {
                require(stage==rhi::shader_stage::pixel);
                matrix(b->block.members[0],0,"inverse_projection");matrix(b->block.members[1],64,"view_to_decal");
                vector(b->block.members[2],128,"viewport");vector(b->block.members[3],144,"extents");
                vector(b->block.members[4],160,"tint");vector(b->block.members[5],176,"normal_parameters");
                vector(b->block.members[6],192,"reflection_parameters");vector(b->block.members[7],208,"bindless_indices");
                mask|=2|decal_object_abi;continue;
            } else if (b->block.size==192) {
                require(stage==rhi::shader_stage::pixel && b->block.member_count==6);
                matrix(b->block.members[0],0,"inverse_view_projection"); matrix(b->block.members[1],64,"previous_view_projection");
                vector(b->block.members[2],128,"viewport"); vector(b->block.members[3],144,"source_bounds");
                vector(b->block.members[4],160,"filter"); vector(b->block.members[5],176,"temporal");
                bit=2|temporal_object_abi;
            } else if (b->block.size==64) {
                require(stage==rhi::shader_stage::pixel && b->block.member_count==4);
                vector(b->block.members[0],0,"viewport"); vector(b->block.members[1],16,"source_bounds");
                vector(b->block.members[2],32,"filter"); vector(b->block.members[3],48,"bloom");
                bit=2|postprocess_object_abi;
            } else if (b->block.size==128 && b->block.member_count==5 && b->block.members[2].name && std::strcmp(b->block.members[2].name,"source_bounds")==0) {
                require(stage==rhi::shader_stage::pixel);
                matrix(b->block.members[0],0,"inverse_projection");vector(b->block.members[1],64,"viewport");vector(b->block.members[2],80,"source_bounds");
                vector(b->block.members[3],96,"ao_parameters");vector(b->block.members[4],112,"filter_parameters");bit=2|screen_effect_object_abi;
            } else if ((b->block.size==128 || b->block.size==160 || b->block.size==208 || b->block.size==496) && b->block.member_count && b->block.members[0].name
                && std::strcmp(b->block.members[0].name,"inverse_projection")==0) {
                require(stage==rhi::shader_stage::pixel);
                bool shadow=b->block.size==496,environment=shadow || b->block.size==208,local=environment || b->block.size==160;
                require(b->block.member_count==(shadow ? 16u : environment ? 10u : local ? 7u : 5u));
                matrix(b->block.members[0],0,"inverse_projection"); vector(b->block.members[1],64,"viewport");
                vector(b->block.members[2],80,"light_direction"); vector(b->block.members[3],96,"light_color");
                vector(b->block.members[4],112,"light_factors"); bit=2|lighting_object_abi;
                if (local) {
                    vector(b->block.members[5],128,"light_position_range"); vector(b->block.members[6],144,"spot_parameters");
                    bit|=local_lighting_object_abi;
                }
                if (environment) {
                    vector(b->block.members[7],160,"ambient"); vector(b->block.members[8],176,"fog_color_distance");
                    vector(b->block.members[9],192,"fog_parameters"); bit|=environment_lighting_object_abi;
                }
                if (shadow) {
                    for (unsigned i=0;i<4;++i) matrix(b->block.members[10+i],208+64*i,("shadow_matrix"+std::to_string(i)).c_str());
                    vector(b->block.members[14],464,"shadow_splits"); vector(b->block.members[15],480,"shadow_parameters");
                    bit|=shadow_lighting_object_abi;
                }
            } else if (b->block.size == 224 && b->block.member_count == 11) {
                matrix(b->block.members[0], 0, "world"); vector(b->block.members[1], 64, "tint");
                vector(b->block.members[2], 80, "parameters"); vector(b->block.members[3], 96, "surface");
                vector(b->block.members[4], 112, "map_parameters"); vector(b->block.members[5], 128, "detail_normal_parameters");
                vector(b->block.members[6], 144, "sequence_uv"); vector(b->block.members[7], 160, "extra_parameters");
                vector(b->block.members[8], 176, "reflection_parameters"); vector(b->block.members[9], 192, "bindless_indices");
                vector(b->block.members[10], 208, "sss_parameters");
                bit = 2 | extended_gbuffer_object_abi;
            } else if (b->block.size == 288 && b->block.member_count == 15) {
                matrix(b->block.members[0], 0, "world"); vector(b->block.members[1], 64, "tint");
                vector(b->block.members[2], 80, "parameters"); vector(b->block.members[3], 96, "surface");
                vector(b->block.members[4], 112, "map_parameters"); vector(b->block.members[5], 128, "specular");
                vector(b->block.members[6], 144, "emissive"); vector(b->block.members[7], 160, "sequence_uv");
                vector(b->block.members[8], 176, "variation_parameters"); vector(b->block.members[9], 192, "variation_color");
                vector(b->block.members[10], 208, "diffuse_attenuation"); vector(b->block.members[11], 224, "detail_diffuse");
                vector(b->block.members[12], 240, "reflection_parameters"); vector(b->block.members[13], 256, "bindless_indices");
                vector(b->block.members[14], 272, "bindless_indices_extra");
                bit = 2 | extended_material_object_abi;
            } else if (b->block.size == 256 && b->block.member_count == 13) {
                matrix(b->block.members[0], 0, "world"); vector(b->block.members[1], 64, "tint");
                vector(b->block.members[2], 80, "parameters"); vector(b->block.members[3], 96, "forward_params");
                vector(b->block.members[4], 112, "light_direction"); vector(b->block.members[5], 128, "light_color");
                vector(b->block.members[6], 144, "ambient_color"); vector(b->block.members[7], 160, "projection_params");
                vector(b->block.members[8], 176, "volume_params"); vector(b->block.members[9], 192, "viewport_params");
                vector(b->block.members[10], 208, "extra_forward"); vector(b->block.members[11], 224, "reflection_parameters");
                vector(b->block.members[12], 240, "bindless_indices");
                bit = 2 | extended_forward_object_abi;
            } else {
                bool emissive=b->block.size==160,extended=emissive || b->block.size==144,mapped=extended || b->block.size==128,surface=mapped || b->block.size==112;
                require((surface || b->block.size==96) && b->block.member_count==(emissive ? 7u : extended ? 6u : mapped ? 5u : surface ? 4u : 3u));
                bool detail=extended && b->block.members[5].name && std::strcmp(b->block.members[5].name,"detail_normal_parameters")==0;
                matrix(b->block.members[0], 0, "world"); vector(b->block.members[1], 64, "tint");
                vector(b->block.members[2], 80, "parameters"); bit = 2;
                if (surface) { vector(b->block.members[3],96,"surface"); bit |= extended ? (detail ? detailed_surface_object_abi : material_object_abi) : mapped ? mapped_surface_object_abi : surface_object_abi; }
                if (mapped) vector(b->block.members[4],112,"map_parameters");
                if (extended) vector(b->block.members[5],128,detail ? "detail_normal_parameters" : "specular");
                if (emissive) { require(!detail); vector(b->block.members[6],144,"emissive"); bit|=emissive_material_object_abi; }
            }
        } else if (b->set == 1 && b->binding == 0) {
            require(stage == rhi::shader_stage::pixel && b->descriptor_type == SPV_REFLECT_DESCRIPTOR_TYPE_SAMPLED_IMAGE
                && (b->image.dim == SpvDim2D || b->image.dim == SpvDimCube) && (!b->image.arrayed || (b->image.dim==SpvDim2D && !b->image.ms)) && b->image.depth != 1
                && (!b->image.ms || (multisample && b->image.dim==SpvDim2D)));
            bit = 4 | (b->image.dim==SpvDimCube ? cube_texture_binding : 0);
            if (b->image.arrayed) bit|=array_texture_binding;
            if (b->image.ms) bit|=multisample_texture_binding(0);
        } else if (b->set == 1 && b->binding == 1) {
            require(stage == rhi::shader_stage::pixel && b->descriptor_type == SPV_REFLECT_DESCRIPTOR_TYPE_SAMPLER); bit = 8;
        } else if (b->set==1 && b->binding>=2 && b->binding<=5) {
            require(stage==rhi::shader_stage::pixel && b->descriptor_type==SPV_REFLECT_DESCRIPTOR_TYPE_SAMPLED_IMAGE
                && b->image.dim==SpvDim2D && (!b->image.arrayed || ((b->binding==2 || b->binding==4) && !b->image.ms))
                && (!b->image.ms || multisample) && b->image.depth!=1);
            bit=b->binding==2 ? normal_texture_binding : b->binding==3 ? roughness_texture_binding : b->binding==4 ? specular_texture_binding : detail_normal_texture_binding;
            if (b->image.arrayed) bit|=array_binding(b->binding-1);
            if (b->image.ms) bit|=multisample_texture_binding(b->binding-1);
        } else if (b->set==1 && b->binding==6) {
            require(stage==rhi::shader_stage::pixel && b->descriptor_type==SPV_REFLECT_DESCRIPTOR_TYPE_SAMPLER);
            bit=comparison_sampler_binding;
        } else if (b->set==1 && b->binding==7) {
            if (has_comparison_sampler) {
                require(stage==rhi::shader_stage::pixel && b->descriptor_type==SPV_REFLECT_DESCRIPTOR_TYPE_SAMPLED_IMAGE
                    && b->image.dim==SpvDim2D && !b->image.arrayed && !b->image.ms && b->image.depth!=0);
                bit=shadow_depth_binding;
            } else {
                require(stage==rhi::shader_stage::pixel && b->descriptor_type==SPV_REFLECT_DESCRIPTOR_TYPE_SAMPLED_IMAGE
                    && b->image.dim==SpvDim2D && !b->image.arrayed && !b->image.ms && b->image.depth!=1);
                bit=extra_texture_binding;
            }
        } else if (b->set==1 && (b->binding==8 || b->binding==9)) {
            require(stage==rhi::shader_stage::pixel && b->descriptor_type==SPV_REFLECT_DESCRIPTOR_TYPE_SAMPLED_IMAGE
                && b->image.dim==SpvDim2D && !b->image.arrayed && !b->image.ms && b->image.depth!=1);
            bit=(b->binding==8) ? extra_texture_binding_1 : extra_texture_binding_2;
        } else require(false);
        require(!(mask & bit)); mask |= bit;
    }
    bool ms=(mask & 0x1f000000ull)!=0;
    if (ms) require(mask & (lighting_object_abi|temporal_object_abi|extended_forward_object_abi));
    if (mask & (normal_texture_binding|roughness_texture_binding)) require((mask & (mapped_surface_object_abi|detailed_surface_object_abi|extended_gbuffer_object_abi|extended_material_object_abi|lighting_object_abi|postprocess_object_abi|temporal_object_abi|decal_object_abi|screen_effect_object_abi|extended_forward_object_abi)) && ((mask & 8) || ms || (mask & extended_forward_object_abi)));
    if (mask & postprocess_object_abi) require((mask & ~(2ull|4ull|8ull|normal_texture_binding|postprocess_object_abi))==0 && (mask & 4) && (mask & 8));
    if (mask & detail_normal_texture_binding) require(((mask & (detailed_surface_object_abi|extended_gbuffer_object_abi|decal_object_abi|environment_lighting_object_abi)) && (mask & normal_texture_binding) && (mask & 8)) || ((mask & extended_material_object_abi) && (mask & 8)));
    if (mask & specular_texture_binding) require((mask & (material_object_abi|extended_gbuffer_object_abi|extended_material_object_abi|lighting_object_abi|temporal_object_abi|decal_object_abi)) && ((mask & 8) || ms));
    if (mask & extra_texture_binding) require((mask & (extended_gbuffer_object_abi|extended_material_object_abi)) && (mask & 8));
    if (mask & (extra_texture_binding_1|extra_texture_binding_2)) require((mask & (extended_gbuffer_object_abi|extended_material_object_abi)) && (mask & 8));
    require(bool(mask & shadow_depth_binding)==bool(mask & comparison_sampler_binding));
    if (mask & cube_texture_binding) require(mask & 8);
    if (mask & (bindless_2d_binding | bindless_cube_binding)) require((mask & (extended_gbuffer_object_abi | extended_material_object_abi | extended_forward_object_abi | decal_object_abi)) && (mask & 8));
    sampling_contract(words,bindings,(mask & shadow_depth_binding)!=0,(mask & temporal_object_abi)!=0);
    return mask;
}
} } }
