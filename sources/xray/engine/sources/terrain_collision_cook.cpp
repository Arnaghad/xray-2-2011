#include "pch.h"
#include <xray/engine/terrain_collision.h>
#include <xray/render/facade/terrain_collision.h>
#include <xray/render/vulkan/terrain_collision.h>
#include <xray/resources_cook_classes.h>
#include <xray/resources_query_result.h>
#include <stdexcept>
#include <atomic>
#include <mutex>
#include <unordered_map>
#include <BulletCollision/CollisionDispatch/btInternalEdgeUtility.h>
#include <BulletCollision/CollisionDispatch/btCollisionObjectWrapper.h>
#include <BulletCollision/CollisionDispatch/btManifoldResult.h>
#include <BulletCollision/CollisionShapes/btBvhTriangleMeshShape.h>

namespace xray { namespace engine {
static_assert(resources::terrain_collision_shape_class>resources::renderer_class
    && resources::terrain_collision_shape_class<resources::single_sound_class);
namespace {
std::mutex contact_mutex;
std::unordered_map<btCollisionShape const*,u32> terrain_shapes;
ContactAddedCallback previous_contact=nullptr;
std::atomic<u64> mesh_contacts=0,corrected_normals=0,swapped_contacts=0;
bool terrain_triangle(btCollisionObjectWrapper const* wrapper,int part,int index) {
    if (part!=0 || index<0 || wrapper->getCollisionShape()->getShapeType()!=TRIANGLE_SHAPE_PROXYTYPE
        || !(wrapper->getCollisionObject()->getCollisionFlags() & btCollisionObject::CF_CUSTOM_MATERIAL_CALLBACK)) return false;
    std::lock_guard lock(contact_mutex);
    auto it=terrain_shapes.find(wrapper->getCollisionObject()->getCollisionShape());
    return it!=terrain_shapes.end() && u32(index)<it->second;
}
bool terrain_contact(btManifoldPoint& point,btCollisionObjectWrapper const* a,int part_a,int index_a,
    btCollisionObjectWrapper const* b,int part_b,int index_b) {
    bool result=previous_contact ? previous_contact(point,a,part_a,index_a,b,part_b,index_b) : true;
    auto normal=point.m_normalWorldOnB;
    if (terrain_triangle(b,part_b,index_b)) {
        btAdjustInternalEdgeContacts(point,b,a,part_b,index_b);mesh_contacts.fetch_add(1,std::memory_order_relaxed);
    } else if (terrain_triangle(a,part_a,index_a)) {
        // Bullet's edge utility expects the triangle on manifold side B.
        auto reversed=point;
        std::swap(reversed.m_localPointA,reversed.m_localPointB);
        std::swap(reversed.m_positionWorldOnA,reversed.m_positionWorldOnB);
        reversed.m_normalWorldOnB=-reversed.m_normalWorldOnB;
        btAdjustInternalEdgeContacts(reversed,a,b,part_a,index_a);
        point.m_normalWorldOnB=-reversed.m_normalWorldOnB;
        point.m_localPointA=reversed.m_localPointB;point.m_positionWorldOnA=reversed.m_positionWorldOnB;
        mesh_contacts.fetch_add(1,std::memory_order_relaxed);swapped_contacts.fetch_add(1,std::memory_order_relaxed);
    }
    if ((normal-point.m_normalWorldOnB).length2()>1e-6f) corrected_normals.fetch_add(1,std::memory_order_relaxed);
    return result;
}
struct contact_hook {
    contact_hook() {
        if (gContactAddedCallback==&terrain_contact) throw std::runtime_error("Terrain contact hook is already installed without its cook");
        previous_contact=gContactAddedCallback;gContactAddedCallback=&terrain_contact;
    }
    ~contact_hook() { if (owns_callback()) gContactAddedCallback=previous_contact; }
    bool owns_callback() const { return gContactAddedCallback==&terrain_contact; }
};
class terrain_collision_resource final : public physics::bt_collision_shape,public render::terrain_collision_access {
    render::render_model_instance_ptr m_source;
    std::shared_ptr<render::vulkan::terrain_collision_geometry> m_geometry;
public:
    terrain_collision_resource(memory::base_allocator& allocator,render::render_model_instance_ptr source,
        std::shared_ptr<render::vulkan::terrain_collision_geometry> geometry)
        : bt_collision_shape(allocator,geometry->vertices(),geometry->indices(),geometry->center_offset()),
          m_source(std::move(source)),m_geometry(std::move(geometry)) {}
    render::render_model_instance* source_instance() const override { return m_source.c_ptr(); }
    collision::geometry_instance* geometry_instance() const override { return m_geometry->instance(); }
    physics::bt_collision_shape* physics_shape() override { return this; }
    math::float3 center_offset() const override { return m_geometry->center_offset(); }
};
void delete_terrain_resource(memory::base_allocator& allocator,terrain_collision_resource* resource) {
    {
        std::lock_guard lock(contact_mutex);terrain_shapes.erase(resource->get_bt_shape());
    }
    physics::destroy_shape(allocator,resource);
}
class terrain_collision_cook final : public resources::translate_query_cook {
public:
    contact_hook contacts;
    memory::base_allocator& allocator;
    u32 const thread=threading::current_thread_id();
    explicit terrain_collision_cook(memory::base_allocator& value)
        : translate_query_cook(resources::terrain_collision_shape_class,reuse_false,use_current_thread_id),allocator(value) {}
    bool cache_by_game_resources_manager() override { return false; }
    void translate_query(resources::query_result_for_cook& query) override {
        try {
            if (!contacts.owns_callback()) throw std::runtime_error("Terrain collision contact hook was replaced by another owner");
            auto bytes=query.creation_data_from_user();
            auto* source=query.user_data() ? query.user_data()->get_if<render::render_model_instance_ptr>() : nullptr;
            auto* instance=source ? dynamic_cast<render::vulkan::terrain_instance_resource*>(source->c_ptr()) : nullptr;
            if (!bytes.c_ptr() || bytes.size()!=1 || *static_cast<u8 const*>(bytes.c_ptr())!=0
                || !instance || !instance->owner->accepting() || !instance->game_geometry_locked())
                throw std::runtime_error("Terrain collision requires a sealed live render instance and internal creation request");
            auto geometry=std::make_shared<render::vulkan::terrain_collision_geometry>(allocator,instance->cell());
            auto destroy=[this](terrain_collision_resource* resource) { delete_terrain_resource(allocator,resource); };
            std::unique_ptr<terrain_collision_resource,decltype(destroy)> resource(
                XRAY_NEW_IMPL(allocator,terrain_collision_resource)(allocator,*source,std::move(geometry)),destroy);
            {
                std::lock_guard lock(contact_mutex);
                terrain_shapes.emplace(resource->get_bt_shape(),u32(instance->cell()->mesh->indices.size()/3));
            }
            query.set_unmanaged_resource(resource.get(),resources::nocache_memory,sizeof(*resource));resource.release();query.finish_query(result_success);
        } catch (std::exception const& error) {
            LOG_ERROR("Terrain collision query failed: %s",error.what());query.finish_query(result_error);
        }
    }
    void delete_resource(resources::resource_base* resource) override {
        delete_terrain_resource(allocator,static_cast<terrain_collision_resource*>(resource));
    }
};
std::unique_ptr<terrain_collision_cook> cook;
}
void register_terrain_collision_cook(memory::base_allocator& allocator) {
    if (!resources::is_initialized()) throw std::runtime_error("Terrain collision cook requires initialized resources");
    if (cook) {
        if (&cook->allocator!=&allocator || cook->thread!=threading::current_thread_id()
            || resources::cook_base::find_cook(resources::terrain_collision_shape_class)!=cook.get() || !cook->contacts.owns_callback())
            throw std::runtime_error("Terrain collision cook has another allocator/thread or a replaced contact hook");
        return;
    }
    if (resources::cook_base::find_cook(resources::terrain_collision_shape_class))
        throw std::runtime_error("Terrain collision class ID is already owned");
    auto next=std::make_unique<terrain_collision_cook>(allocator);resources::register_cook(next.get());cook=std::move(next);
}
bool finalize_terrain_collision_cook() {
    if (!cook) return true;
    if (!resources::is_initialized() || cook->thread!=threading::current_thread_id() || resources::pending_queries_count()
        || cook->cook_users_count() || resources::cook_base::find_cook(resources::terrain_collision_shape_class)!=cook.get()
        || !cook->contacts.owns_callback()) return false;
    {std::lock_guard lock(contact_mutex);if (!terrain_shapes.empty()) return false;}
    resources::unregister_cook(resources::terrain_collision_shape_class);cook.reset();return true;
}
terrain_contact_report terrain_contact_diagnostics() {
    return {mesh_contacts.load(std::memory_order_relaxed),corrected_normals.load(std::memory_order_relaxed),
        swapped_contacts.load(std::memory_order_relaxed),gContactAddedCallback==&terrain_contact};
}
} }
