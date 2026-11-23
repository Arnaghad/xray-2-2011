#include "pch.h"
#include "bounded_particle_template_cook.h"
#include "particle_system.h"
#include <xray/configs_checked_binary.h>
#include <algorithm>
#include <cmath>
#include <cstring>
#include <memory>
#include <stdexcept>
#include <vector>

namespace xray { namespace particle {
namespace {
namespace binary=configs::checked;
using value=configs::binary_config_value;
struct parameters {
    std::string material;
    float lifetime,rate,duration,rotation;
    math::float3 velocity,size;
    math::float4 color;
    u32 max_particles;
    bool world_space,square;
};
void fields(value const& table,std::initializer_list<pcstr> names) {
    if (table.type!=configs::t_table_named) throw std::runtime_error("Expected a particle named table");
    for (auto const& child:table)
        if (std::none_of(names.begin(),names.end(),[&](pcstr name) {return std::strcmp(name,child.key())==0;}))
            throw std::runtime_error(std::string("Unsupported particle field: ")+child.key());
}
float scalar(value const& table,pcstr name,float low,float high) {
    float result=binary::field(table,name,configs::t_float);
    if (!std::isfinite(result) || result<low || result>high)
        throw std::runtime_error(std::string("Particle scalar outside profile: ")+name);
    return result;
}
math::float3 vector(value const& table,pcstr name,float low,float high) {
    math::float3 result=binary::field(table,name,configs::t_float3);
    for (float component:{result.x,result.y,result.z})
        if (!std::isfinite(component) || component<low || component>high)
            throw std::runtime_error(std::string("Particle vector outside profile: ")+name);
    return result;
}
std::vector<parameters> parse(value const& root) {
    fields(root,{"version","emitters"});
    if (u32(binary::field(root,"version",configs::t_integer))!=1)
        throw std::runtime_error("Unsupported cooked particle version");
    auto const& emitters=binary::field(root,"emitters",configs::t_table_indexed);
    if (!emitters.size() || emitters.size()>64) throw std::runtime_error("Particles require 1..64 emitters");
    std::vector<parameters> output;u32 capacity=0;
    for (auto const& emitter:emitters) {
        fields(emitter,{"material","alignment","world_space","max_particles","duration","rate","lifetime","velocity","size","color","rotation"});
        parameters p;
        p.material=pcstr(binary::field(emitter,"material",configs::t_string));
        binary::validate_path(p.material);
        if (p.material.size()>=128) throw std::runtime_error("Particle material name exceeds 127 bytes");
        std::string alignment=pcstr(binary::field(emitter,"alignment",configs::t_string));
        if (alignment!="square" && alignment!="rectangle") throw std::runtime_error("Unsupported particle alignment");
        p.square=alignment=="square";
        p.world_space=bool(binary::field(emitter,"world_space",configs::t_boolean));
        p.max_particles=u32(binary::field(emitter,"max_particles",configs::t_integer));
        if (!p.max_particles || p.max_particles>4096 || (capacity+=p.max_particles)>50000)
            throw std::runtime_error("Particle capacity exceeds 4096/emitter or 50000/template");
        p.duration=scalar(emitter,"duration",.001f,3600.f);
        p.rate=scalar(emitter,"rate",0.f,4096.f);
        p.lifetime=scalar(emitter,"lifetime",.001f,60.f);
        if (p.lifetime<=math::epsilon_3) throw std::runtime_error("Particle lifetime must exceed the simulation immortal threshold");
        if (p.rate*p.lifetime>4094.f) throw std::runtime_error("Particle rate/lifetime occupancy exceeds 4094");
        p.rotation=scalar(emitter,"rotation",-math::pi_x2,math::pi_x2);
        p.velocity=vector(emitter,"velocity",-1000.f,1000.f);
        p.size=vector(emitter,"size",.001f,1000.f);
        if (p.square && p.size.x!=p.size.y) throw std::runtime_error("Square particles require equal X/Y size");
        p.color=math::float4(binary::field(emitter,"color",configs::t_float4));
        for (float component:{p.color.x,p.color.y,p.color.z,p.color.w})
            if (!std::isfinite(component) || component<0.f || component>1.f)
                throw std::runtime_error("Particle color must be finite normalized linear RGBA");
        output.push_back(p);
    }
    return output;
}
struct emitter_actions {
    particle_action_billboard billboard;
    particle_action_initial_color color;
    particle_action_initial_velocity velocity;
    particle_action_initial_size size;
    particle_action_initial_rotation rotation;
    emitter_actions() {
        billboard.set_defaults();color.set_defaults();velocity.set_defaults();size.set_defaults();rotation.set_defaults();
    }
};
uninitialized_reference<bounded_particle_template_cook> template_cook;
u32 registration_thread=0;
}

// Own every allocation; the legacy particle_system destructor does not own parsed arrays/actions.
class particle_template_resource final : public particle_system {
    particle_system_lod lod;
    std::unique_ptr<particle_emitter[]> emitters;
    std::unique_ptr<emitter_actions[]> actions;
public:
    explicit particle_template_resource(std::vector<parameters> const& data)
        : emitters(std::make_unique<particle_emitter[]>(data.size())),actions(std::make_unique<emitter_actions[]>(data.size())) {
        lod.set_defaults();m_lods=&lod;m_num_lods=1;
        lod.m_parent=this;lod.m_emitters_array=emitters.get();lod.m_num_emitters=u32(data.size());
        for (size_t i=0;i<data.size();++i) {
            auto const& p=data[i];auto& emitter=emitters[i];auto& a=actions[i];emitter.set_defaults();
            std::memcpy(emitter.m_material_name,p.material.c_str(),p.material.size()+1);
            emitter.m_world_space=p.world_space;emitter.m_duration=p.duration;emitter.m_max_num_particles=p.max_particles;
            emitter.m_particle_system=this;
            emitter.m_particle_lifetime_curve.set_constant(p.lifetime);emitter.m_particle_spawn_rate_curve.set_constant(p.rate);
            a.billboard.m_billboard_parameters.locked_axis=particle_locked_axis_none;
            a.billboard.m_billboard_parameters.screen_alignment=p.square ? particle_screen_alignment_square : particle_screen_alignment_rectangle;
            a.color.set_constant(p.color);a.velocity.set_constant(p.velocity);a.size.set_constant(p.size);
            a.rotation.set_constant(math::float3(p.rotation/math::pi_x2,0,0));
            particle_action* all[]={&a.billboard,&a.color,&a.velocity,&a.size,&a.rotation};
            for (auto* action:all) {
                emitter.add_action(action);++emitter.m_num_actions;
            }
            emitter.set_data_type_action(&a.billboard);
        }
    }
    size_t memory_size() const {
        // Two ranged emitter constants, three XYZ constant curves and one color curve per emitter.
        return sizeof(*this)+lod.m_num_emitters*(sizeof(particle_emitter)+sizeof(emitter_actions)
            +22*sizeof(curve_point<float>)+sizeof(curve_point<math::float4_pod>));
    }
};

bounded_particle_template_cook::bounded_particle_template_cook()
    : translate_query_cook(resources::particle_system_class,reuse_false,use_current_thread_id) {}
void bounded_particle_template_cook::translate_request_path(pcstr request,fs_new::virtual_path_string& output) const {
    try {
        std::string name=request ? request : "";binary::validate_path(name);
        binary::validate_path("resources.converted/particles/"+name+".particle");output=name.c_str();
    } catch (std::exception const&) {
        // Reject in translate_query before I/O, without asserting in virtual_path_string assignment.
        output.clear();
    }
}
void bounded_particle_template_cook::translate_query(resources::query_result_for_cook& query) {
    try {
        if (query.user_data() || query.creation_data_from_user()) throw std::runtime_error("Cooked particle templates require a logical name without userdata/payload");
        std::string name=query.get_requested_path();binary::validate_path(name);
        std::string path="resources.converted/particles/"+name+".particle";binary::validate_path(path);
        resources::query_resource(path.c_str(),resources::raw_data_class,[&query](resources::queries_result& children) {
            try {
                if (!children.is_successful()) throw std::runtime_error("Cooked particle template file is missing");
                resources::pinned_ptr_const<u8> bytes(children[0].get_managed_resource());
                binary::checked_config config(bytes,*memory::g_mt_allocator);auto data=parse(config.root());
                auto resource=std::make_unique<particle_template_resource>(data);
                query.set_unmanaged_resource(resource.get(),resources::nocache_memory,resource->memory_size());
                resource.release();query.finish_query(result_success);
            } catch (std::exception const& error) {
                LOG_ERROR("Cooked particle template dependency failed: %s",error.what());query.finish_query(result_error);
            }
        },memory::g_mt_allocator,nullptr,&query,assert_on_fail_false);
    } catch (std::exception const& error) {
        LOG_ERROR("Cooked particle template query failed: %s",error.what());query.finish_query(result_error);
    }
}
void bounded_particle_template_cook::delete_resource(resources::resource_base* resource) {
    delete static_cast<particle_template_resource*>(resource);
}
void check_particle_template_registration() {
    if (!resources::is_initialized()) throw std::runtime_error("Particle templates require initialized resources");
    auto* current=resources::cook_base::find_cook(resources::particle_system_class);
    if (template_cook.initialized()) {
        if (registration_thread!=threading::current_thread_id() || current!=template_cook.c_ptr())
            throw std::runtime_error("Particle template cook has another thread or registry owner");
    } else if (current) throw std::runtime_error("Particle template class ID is already owned");
}
void register_bounded_particle_template_cook() {
    check_particle_template_registration();if (template_cook.initialized()) return;
    XRAY_CONSTRUCT_REFERENCE(template_cook,bounded_particle_template_cook)();
    resources::register_cook(template_cook.c_ptr());registration_thread=threading::current_thread_id();
}
bool can_unregister_bounded_particle_template_cook() {
    return !template_cook.initialized() || (resources::is_initialized() && registration_thread==threading::current_thread_id()
        && !resources::pending_queries_count() && !template_cook->cook_users_count()
        && resources::cook_base::find_cook(resources::particle_system_class)==template_cook.c_ptr());
}
void unregister_bounded_particle_template_cook() {
    if (!can_unregister_bounded_particle_template_cook()) throw std::runtime_error("Particle template cook has pending resources or a foreign owner");
    if (!template_cook.initialized()) return;
    resources::unregister_cook(resources::particle_system_class);XRAY_DESTROY_REFERENCE(template_cook);registration_thread=0;
}
} }
