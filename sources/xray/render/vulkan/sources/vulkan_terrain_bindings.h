#ifndef XRAY_VULKAN_TERRAIN_BINDINGS_H_INCLUDED
#define XRAY_VULKAN_TERRAIN_BINDINGS_H_INCLUDED
#include <xray/render/vulkan/terrain_resources.h>
#include <deque>
#include <map>
#include <functional>

namespace xray { namespace render { namespace vulkan {
class terrain_binding_runtime : public std::enable_shared_from_this<terrain_binding_runtime> {
    struct command;
    std::shared_ptr<scene_registry> m_owner;
    std::map<base_scene*,std::deque<std::shared_ptr<command>>> m_streams;
    size_t m_pending=0,m_bytes=0;
    void submit(std::shared_ptr<command>);
    void process(base_scene*,bool propagate);
    bool start(std::shared_ptr<command> const&);
    void complete(std::shared_ptr<command> const&,resources::queries_result&);
    bool active(std::shared_ptr<command> const&) const;
    void finish(std::shared_ptr<command> const&,unsigned);
public:
    explicit terrain_binding_runtime(std::shared_ptr<scene_registry> owner) : m_owner(std::move(owner)) {}
    ~terrain_binding_runtime();
    bool busy(scene_ptr const&) const;
    void add(scene_ptr const&,render_model_instance_ptr const&,std::string const&,u32);
    void remove(scene_ptr const&,render_model_instance_ptr const&,u32);
    void exchange(scene_ptr const&,std::string const&,std::string const&);
    void defer(scene_ptr const&,size_t,std::function<void()>);
    // Membership changes cancel the scene stream; query callbacks hold no scene/model/runtime lease.
    void cancel(scene_ptr const&);
};
} } }
#endif
