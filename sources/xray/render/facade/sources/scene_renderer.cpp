////////////////////////////////////////////////////////////////////////////
//	Created		: 26.05.2011
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include <xray/render/vulkan/legacy_capabilities.h>
#include <xray/render/facade/scene_renderer.h>
#include <xray/render/engine/world.h>
#include <xray/render/facade/light_props.h>
#include <xray/render/facade/lighting_environment.h>
#include <xray/render/facade/postprocess_settings.h>
#include <xray/render/facade/decal_properties.h>
#include <xray/render/facade/one_way_render_channel.h>
#include "functor_command.h"
#include "functor_with_big_buffer_to_copy_command.h"
#include "update_skeleton_command.h"
#include <xray/render/vulkan/skeletal_resources.h>
#include <stdexcept>
#include "update_model_vertex_buffer_command.h"
#include <xray/ui/world.h>

using xray::render::scene_renderer;
using xray::render::scene_ptr;
using xray::render::scene_view_ptr;
using xray::render::render_output_window_ptr;
using xray::render::render_model_instance_ptr;
using xray::math::float4x4;
using xray::particle::particle_system_instance_ptr;

namespace {
xray::math::frustum listener_frustum(float4x4 const& view,float4x4 const& projection) {
#if defined(USE_VULKAN_RENDERER)
    return xray::math::frustum(xray::math::mul4x4(view,projection),true);
#else
    return xray::math::frustum(xray::math::mul4x4(view,projection));
#endif
}
#ifdef USE_VULKAN_RENDERER
void queue_model_buffer_update(xray::render::one_way_render_channel& channel,xray::memory::base_allocator& allocator,
    xray::render::engine::world& world,render_model_instance_ptr const& model,
    xray::vectora<xray::render::buffer_fragment> const& fragments,bool indices) {
    using namespace xray::render;
    // XRAY_NEW_IMPL uses placement new, so a throwing payload copy needs an allocation guard.
    void* storage=XRAY_MALLOC_IMPL(allocator,sizeof(update_model_vertex_buffer_command),"Model buffer command");
    if (!storage) throw std::bad_alloc();
    update_model_vertex_buffer_command* command;
    try { command=new(storage) update_model_vertex_buffer_command(world,model,fragments,allocator,indices); }
    catch (...) { XRAY_FREE_IMPL(allocator,storage); throw; }
    try { channel.owner_push_back(command); }
    catch (...) { XRAY_DELETE_IMPL(allocator,command); throw; }
}
#endif
}

namespace xray {
namespace render {

void defer_execution	( xray::render::base_command& command, xray::render::scene_view_ptr const& scene_view );

} // namespace render
} // namespace xray

scene_renderer::scene_renderer	(
		xray::render::one_way_render_channel&	channel,
		xray::memory::base_allocator&			allocator,
		xray::render::engine::world&			render_engine_world,
		xray::math::frustum*					frustum_listener
	) :
	m_render_engine_world	( render_engine_world ),
	m_channel				( channel ),
	m_allocator				( allocator ),
	m_frustum_listener		( frustum_listener ),
    m_culling_view(math::float4x4().identity()),
	m_view					( math::float4x4().identity() ),
	m_projection			( math::create_perspective_projection( math::pi_d2, 4.f/3.f, .1f, 1000.f ) )
{
	if ( m_frustum_listener )
		*m_frustum_listener	= listener_frustum(m_culling_view,m_projection);
}

void scene_renderer::set_view_matrix			( scene_view_ptr const& scene_view, float4x4 const& view_and_culling_matrix )
{
	R_ASSERT				( scene_view );
	m_view					= view_and_culling_matrix;
    m_culling_view=view_and_culling_matrix;
	if ( m_frustum_listener )
		*m_frustum_listener	= listener_frustum(m_culling_view,m_projection);

	m_channel.owner_push_back	(
		XRAY_NEW_IMPL( m_allocator, functor_with_big_buffer_to_copy_command< float4x4 > ) (
			std::bind(
				&xray::render::engine::world::set_view_matrix,
				&m_render_engine_world,
				scene_view,
				std::placeholders::_1
			),
			view_and_culling_matrix
		)
	);
}

#ifndef MASTER_GOLD
void scene_renderer::set_view_matrix_only	( scene_view_ptr const& scene_view, float4x4 const& view )
{
	R_ASSERT		( scene_view );
	m_view			= view;
#if !defined(USE_VULKAN_RENDERER)
    m_culling_view=view;
#endif
	if ( m_frustum_listener )
		*m_frustum_listener	= listener_frustum(m_culling_view,m_projection);

	m_channel.owner_push_back	(
		XRAY_NEW_IMPL( m_allocator, functor_with_big_buffer_to_copy_command< float4x4 > ) (
			std::bind(
				&xray::render::engine::world::set_view_matrix_only,
				&m_render_engine_world,
				scene_view,
				std::placeholders::_1
			),
			view
		)
	);
}
#endif // #ifndef MASTER_GOLD

void scene_renderer::set_projection_matrix	( scene_view_ptr const& scene_view, float4x4 const& projection )
{
	R_ASSERT	( scene_view );
	m_projection = projection;
	if ( m_frustum_listener )
		*m_frustum_listener	= listener_frustum(m_culling_view,m_projection);
	m_channel.owner_push_back	(
		XRAY_NEW_IMPL( m_allocator, functor_with_big_buffer_to_copy_command< float4x4 > ) (
			std::bind(
				&xray::render::engine::world::set_projection_matrix,
				&m_render_engine_world,
				scene_view,
				std::placeholders::_1
			),
			projection
		)
	);
}

void scene_renderer::add_model	( scene_ptr const& scene, render_model_instance_ptr const& v, float4x4 const& transform )
{
	R_ASSERT	( scene );
	m_channel.owner_push_back		( XRAY_NEW_IMPL( m_allocator, functor_with_big_buffer_to_copy_command< float4x4 > ) ( std::bind( &xray::render::engine::world::add_model, &m_render_engine_world, scene, v, std::placeholders::_1, true), transform ) );
}

void scene_renderer::remove_model( scene_ptr const& scene, render_model_instance_ptr const& model ) 
{
	R_ASSERT	( scene );
	m_channel.owner_push_back		( XRAY_NEW_IMPL( m_allocator, functor_command ) ( std::bind( &xray::render::engine::world::remove_model, &m_render_engine_world, scene, model)));
}

void scene_renderer::update_model( scene_ptr const& scene, render_model_instance_ptr const& render_model, float4x4 const& model_to_world ) 
{
	R_ASSERT	( scene );
	m_channel.owner_push_back		( XRAY_NEW_IMPL( m_allocator, functor_with_big_buffer_to_copy_command< float4x4 > ) ( std::bind( &xray::render::engine::world::update_model, &m_render_engine_world, scene, render_model, std::placeholders::_1), model_to_world));
}

void scene_renderer::update_skeleton( render_model_instance_ptr const& v, float4x4 const* matrices, u32 count )
{
    if (!v) throw std::runtime_error("Skeleton update requires a model");
    vulkan::validate_skeleton_pose(matrices,count);
    void* storage=XRAY_MALLOC_IMPL(m_allocator,sizeof(update_skeleton_command),"Skeleton update command");
    if (!storage) throw std::bad_alloc();
    update_skeleton_command* command;
    try { command=new(storage) update_skeleton_command(m_render_engine_world,v,matrices,count); }
    catch (...) { XRAY_FREE_IMPL(m_allocator,storage); throw; }
    try { m_channel.owner_push_back(command); }
    catch (...) { XRAY_DELETE_IMPL(m_allocator,command); throw; }
}

void scene_renderer::play_particle_system			( scene_ptr const& scene, particle_system_instance_ptr const& in_instance, xray::float4x4 const& transform )
{
	R_ASSERT	( scene );
	m_channel.owner_push_back	(
		XRAY_NEW_IMPL( m_allocator, functor_with_big_buffer_to_copy_command< float4x4 > ) (
			std::bind(
				&xray::render::engine::world::play_particle_system,
				&m_render_engine_world,
				scene,
				in_instance,
				true,
				false,
				std::placeholders::_1
			),
			transform
		)
	);
}

void scene_renderer::stop_particle_system( scene_ptr const& scene, particle_system_instance_ptr const& in_instance )
{
	R_ASSERT	( scene );
	m_channel.owner_push_back  ( XRAY_NEW_IMPL( m_allocator, functor_command ) ( std::bind( &engine::world::stop_particle_system, &m_render_engine_world, scene, in_instance) ));
}

void scene_renderer::remove_particle_system_instance( scene_ptr const& scene, particle_system_instance_ptr const& in_instance )
{
	R_ASSERT	( scene );
	m_channel.owner_push_back  ( XRAY_NEW_IMPL( m_allocator, functor_command ) ( std::bind( &engine::world::remove_particle_system_instance, &m_render_engine_world, in_instance, scene) ));
}

void scene_renderer::update_particle_system_instance( scene_ptr const& scene, particle_system_instance_ptr const& instance, xray::float4x4 const& transform, bool visible, bool paused )
{
	R_ASSERT	( scene );
	m_channel.owner_push_back  ( XRAY_NEW_IMPL( m_allocator, functor_with_big_buffer_to_copy_command< math::float4x4 > ) ( std::bind( &engine::world::update_particle_system_instance, &m_render_engine_world, instance, scene, std::placeholders::_1, visible, paused), transform ) );
}

void scene_renderer::update_model_vertex_buffer( render_model_instance_ptr const& object, xray::vectora<xray::render::buffer_fragment> const& fragments )
{
#ifdef USE_VULKAN_RENDERER
    queue_model_buffer_update(m_channel,m_allocator,m_render_engine_world,object,fragments,false);
#else
	m_channel.owner_push_back	( XRAY_NEW_IMPL( m_allocator, update_model_vertex_buffer_command ) ( m_render_engine_world, object, fragments, *xray::render::editor::g_allocator ) );
#endif
}
#ifdef USE_VULKAN_RENDERER
void scene_renderer::update_model_index_buffer(render_model_instance_ptr const& object, vectora<buffer_fragment> const& fragments) {
    queue_model_buffer_update(m_channel,m_allocator,m_render_engine_world,object,fragments,true);
}
#endif

void scene_renderer::add_decal( scene_ptr const& scene, u32 id, xray::render::decal_properties const& properties)
{
	R_ASSERT	( scene );
	m_channel.owner_push_back	(
		XRAY_NEW_IMPL( m_allocator, functor_with_big_buffer_to_copy_command< decal_properties > ) (
			std::bind(
				&xray::render::engine::world::add_decal,
				&m_render_engine_world,
				scene,
				id,
				std::placeholders::_1
			),
			properties
		)
	);
}

void scene_renderer::update_decal( scene_ptr const& scene, u32 id, xray::render::decal_properties const& properties)
{
	R_ASSERT	( scene );
	m_channel.owner_push_back	(
		XRAY_NEW_IMPL( m_allocator, functor_with_big_buffer_to_copy_command< decal_properties > ) (
			std::bind(
				&xray::render::engine::world::update_decal,
				&m_render_engine_world,
				scene,
				id,
				std::placeholders::_1
			),
			properties
		)
	);
}

void scene_renderer::remove_decal( scene_ptr const& scene, u32 id)
{
	R_ASSERT	( scene );
	m_channel.owner_push_back	(
		XRAY_NEW_IMPL( m_allocator, functor_command ) (
			std::bind(
				&xray::render::engine::world::remove_decal,
				&m_render_engine_world,
				scene,
				id
			)
		)
	);
}

void scene_renderer::add_light( scene_ptr const& scene, u32 id, xray::render::light_props const& props)
{
	R_ASSERT	( scene );
	m_channel.owner_push_back	(
		XRAY_NEW_IMPL( m_allocator, functor_with_big_buffer_to_copy_command< light_props > ) (
			std::bind(
				&xray::render::engine::world::add_light,
				&m_render_engine_world,
				scene,
				id,
				std::placeholders::_1
			),
			props
		)
	);
}

void scene_renderer::update_light( scene_ptr const& scene, u32 id, xray::render::light_props const& props)
{
	R_ASSERT	( scene );
	m_channel.owner_push_back	(
		XRAY_NEW_IMPL( m_allocator, functor_with_big_buffer_to_copy_command< light_props > ) (
			std::bind(
				&xray::render::engine::world::update_light,
				&m_render_engine_world,
				scene,
				id,
				std::placeholders::_1
			),
			props
		)
	);
}

void scene_renderer::remove_light( scene_ptr const& scene, u32 id)
{
	R_ASSERT	( scene );
	m_channel.owner_push_back	(
		XRAY_NEW_IMPL( m_allocator, functor_command ) (
			std::bind(
				&xray::render::engine::world::remove_light,
				&m_render_engine_world,
				scene,
				id
			)
		)
	);
}

void scene_renderer::terrain_add_cell( scene_ptr const& scene, render_model_instance_ptr const& v )
{
	R_ASSERT	( scene );
	m_channel.owner_push_back	( XRAY_NEW_IMPL( m_allocator, functor_command) ( std::bind(&render::engine::world::terrain_add_cell, &m_render_engine_world, scene, v)) );
}

void scene_renderer::terrain_remove_cell( scene_ptr const& scene, render_model_instance_ptr const& v )
{
	R_ASSERT	( scene );
	m_channel.owner_push_back	( XRAY_NEW_IMPL( m_allocator, functor_command) ( std::bind(&render::engine::world::terrain_remove_cell, &m_render_engine_world, scene, v)) );
}
void scene_renderer::terrain_update_cell_buffer(scene_ptr const& scene,render_model_instance_ptr const& model,
    vectora<terrain_buffer_fragment> const& fragments,float4x4 const& transform) {
    if (fragments.empty() || fragments.size()>4096) throw std::runtime_error("Terrain requires 1..4096 fragments");
    size_t total=0; for (auto const& fragment : fragments) { total+=fragment.buffer.size(); if (total>65536) throw std::runtime_error("Terrain update exceeds 65536 samples"); }
    auto copy=std::make_shared<vectora<terrain_buffer_fragment>>(&m_allocator); copy->reserve(fragments.size());
    for (auto const& source : fragments) {
        auto& destination=copy->emplace_back(&m_allocator); destination.rect=source.rect;
        destination.buffer.assign(source.buffer.begin(),source.buffer.end());
    }
    auto* world=&m_render_engine_world;
    m_channel.owner_push_back(XRAY_NEW_IMPL(m_allocator,functor_command)([world,scene,model,copy,transform] {
        world->terrain_update_cell_buffer(scene,model,*copy,transform);
    }));
}
void scene_renderer::terrain_update_cell_aabb(scene_ptr const& scene,render_model_instance_ptr const& model,math::aabb const& bounds) {
    auto* world=&m_render_engine_world;
    m_channel.owner_push_back(XRAY_NEW_IMPL(m_allocator,functor_command)([world,scene,model,bounds] {
        world->terrain_update_cell_aabb(scene,model,bounds);
    }));
}
void scene_renderer::terrain_add_cell_texture(scene_ptr const& scene,render_model_instance_ptr const& model,fs_new::virtual_path_string const& name,u32 slot) {
    auto* world=&m_render_engine_world;
    m_channel.owner_push_back(XRAY_NEW_IMPL(m_allocator,functor_command)([world,scene,model,name,slot] {world->terrain_add_cell_texture(scene,model,name,slot);}));
}
void scene_renderer::terrain_remove_cell_texture(scene_ptr const& scene,render_model_instance_ptr const& model,u32 slot) {
    auto* world=&m_render_engine_world;
    m_channel.owner_push_back(XRAY_NEW_IMPL(m_allocator,functor_command)([world,scene,model,slot] {world->terrain_remove_cell_texture(scene,model,slot);}));
}
void scene_renderer::terrain_exchange_texture(scene_ptr const& scene,fs_new::virtual_path_string const& old_name,fs_new::virtual_path_string const& name) {
    auto* world=&m_render_engine_world;
    m_channel.owner_push_back(XRAY_NEW_IMPL(m_allocator,functor_command)([world,scene,old_name,name] {world->terrain_exchange_texture(scene,old_name,name);}));
}

void scene_renderer::set_slomo					( scene_ptr const& scene, float time_multiplier )
{
    XRAY_REJECT_VULKAN_LEGACY(set_slomo);
	R_ASSERT	( scene );
	m_channel.owner_push_back	( XRAY_NEW_IMPL( m_allocator, functor_command ) ( std::bind( &engine::world::set_slomo, &m_render_engine_world, scene, time_multiplier) ));
}

void scene_renderer::set_post_process			( scene_view_ptr const& scene_view, xray::resources::unmanaged_resource_ptr const& post_process_resource)
{
    XRAY_REJECT_VULKAN_LEGACY(set_post_process);
	R_ASSERT	( scene_view );
	m_channel.owner_push_back  ( XRAY_NEW_IMPL( m_allocator, functor_command ) ( std::bind( &engine::world::set_post_process, &m_render_engine_world, scene_view, post_process_resource) ));
}

void scene_renderer::reload_shaders			( )
{
    XRAY_REJECT_VULKAN_LEGACY(reload_shaders);
	m_channel.owner_push_back	( XRAY_NEW_IMPL( m_allocator, functor_command ) ( std::bind( &xray::render::engine::world::reload_shaders, &m_render_engine_world) ) );
}

void scene_renderer::reload_modified_textures	( )
{
	m_channel.owner_push_back	( XRAY_NEW_IMPL( m_allocator, functor_command ) ( std::bind( &xray::render::engine::world::reload_modified_textures, &m_render_engine_world) ) );
}

void scene_renderer::set_speedtree_instance_material ( 
	xray::render::speedtree_instance_ptr const& v, 
	xray::fs_new::virtual_path_string const& subsurface_name, 
	xray::resources::unmanaged_resource_ptr const& mtl_ptr)
{
	m_channel.owner_push_back  ( XRAY_NEW_IMPL( m_allocator, functor_with_big_buffer_to_copy_command<fs_new::virtual_path_string>) ( std::bind( &render::engine::world::set_speedtree_instance_material, &m_render_engine_world, v, std::placeholders::_1, mtl_ptr), subsurface_name ) );
}

void scene_renderer::set_sky_material ( scene_ptr const& scene, xray::resources::unmanaged_resource_ptr const& mtl_ptr)
{
	m_channel.owner_push_back  ( XRAY_NEW_IMPL( m_allocator, functor_command ) ( std::bind( &engine::world::set_sky_material, &m_render_engine_world, scene, mtl_ptr) ));
}

void scene_renderer::set_model_material(
		render_model_instance_ptr const& v,
		xray::fs_new::virtual_path_string const& subsurface_name,
		xray::resources::unmanaged_resource_ptr const& mtl_ptr
	)
{
	m_channel.owner_push_back  ( XRAY_NEW_IMPL( m_allocator, functor_with_big_buffer_to_copy_command<fs_new::virtual_path_string>) ( std::bind( &render::engine::world::set_model_material, &m_render_engine_world, v, std::placeholders::_1, mtl_ptr), subsurface_name ) );
}

void scene_renderer::set_model_visible( render_model_instance_ptr const& v, xray::fs_new::virtual_path_string const& subsurface_name, bool value)
{
	m_channel.owner_push_back  ( XRAY_NEW_IMPL( m_allocator, functor_with_big_buffer_to_copy_command<fs_new::virtual_path_string>) ( std::bind( &render::engine::world::set_model_visible, &m_render_engine_world, v, std::placeholders::_1, value), subsurface_name ) );
}

void scene_renderer::set_lighting_environment(scene_view_ptr const& view,lighting_environment const& environment)
{
    m_channel.owner_push_back(XRAY_NEW_IMPL(m_allocator,functor_command)(std::bind(&engine::world::set_lighting_environment,&m_render_engine_world,view,environment)));
}
void scene_renderer::set_postprocess_settings(scene_view_ptr const& view,postprocess_settings const& settings)
{
    m_channel.owner_push_back(XRAY_NEW_IMPL(m_allocator,functor_command)(std::bind(&engine::world::set_postprocess_settings,&m_render_engine_world,view,settings)));
}
void scene_renderer::set_view_mode( scene_view_ptr const& scene_view, xray::render::scene_view_mode view_mode )
{
	R_ASSERT					( scene_view );
	m_channel.owner_push_back  ( XRAY_NEW_IMPL( m_allocator, functor_command ) ( std::bind( &engine::world::set_view_mode, &m_render_engine_world, scene_view, view_mode) ));
}
void scene_renderer::set_editor_selection(scene_view_ptr const& view,editor_selection_settings const& settings) {
    m_channel.owner_push_back(XRAY_NEW_IMPL(m_allocator,functor_command)(std::bind(&engine::world::set_editor_selection,&m_render_engine_world,view,settings)));
}
void scene_renderer::apply_model_edits(scene_ptr const& scene,std::vector<model_scene_edit> edits) {
    m_channel.owner_push_back(XRAY_NEW_IMPL(m_allocator,functor_command)(std::bind(&engine::world::apply_model_edits,&m_render_engine_world,scene,std::move(edits))));
}

void scene_renderer::toggle_render_stage( xray::render::enum_render_stage_type stage_type, bool toggle )
{
    XRAY_REJECT_VULKAN_LEGACY(toggle_render_stage);
	m_channel.owner_push_back  ( XRAY_NEW_IMPL( m_allocator, functor_command ) ( std::bind( &engine::world::toggle_render_stage, &m_render_engine_world, stage_type, toggle) ));
}

void scene_renderer::set_particles_render_mode( scene_view_ptr const& scene_view, xray::particle::enum_particle_render_mode render_mode )
{
    XRAY_REJECT_VULKAN_LEGACY(set_particles_render_mode);
	R_ASSERT	( scene_view );
	m_channel.owner_push_back  ( XRAY_NEW_IMPL( m_allocator, functor_command ) ( std::bind( &engine::world::set_particles_render_mode, &m_render_engine_world, scene_view, render_mode) ));
}

void scene_renderer::enable_post_process( scene_view_ptr const& scene_view, bool enable )
{
	R_ASSERT	( scene_view );
	m_channel.owner_push_back  ( XRAY_NEW_IMPL( m_allocator, functor_command ) ( std::bind( &engine::world::enable_post_process, &m_render_engine_world, scene_view, enable) ));
}

void scene_renderer::set_model_ghost_mode( render_model_instance_ptr const& v, bool b )
{
    XRAY_REJECT_VULKAN_LEGACY(set_model_ghost_mode);
	m_channel.owner_push_back  ( XRAY_NEW_IMPL( m_allocator, functor_command ) ( std::bind( &engine::world::set_model_ghost_mode, &m_render_engine_world, v, b) ));
}

xray::particle::world& scene_renderer::particle_world	( scene_ptr const& scene )
{
    XRAY_REJECT_VULKAN_LEGACY(particle_world);
	R_ASSERT	( scene );
	return 		m_render_engine_world.particle_world( scene );
}

xray::particle::world_ptr scene_renderer::particle_world_handle(scene_ptr const& scene) {
    return m_render_engine_world.particle_world_handle(scene);
}

void scene_renderer::add_speedtree_instance( scene_ptr const& scene, xray::render::speedtree_instance_ptr const& v, xray::math::float4x4 const& transform, bool populate_forest )
{
	m_channel.owner_push_back	( XRAY_NEW_IMPL( m_allocator, functor_with_big_buffer_to_copy_command< math::float4x4 > ) ( std::bind( &engine::world::add_speedtree_instance, &m_render_engine_world, scene, v, std::placeholders::_1, populate_forest), transform ) );	
}

void scene_renderer::populate_speedtree_forest( scene_ptr const& scene )
{
	m_channel.owner_push_back  ( XRAY_NEW_IMPL( m_allocator, functor_command ) ( std::bind( &engine::world::populate_speedtree_forest, &m_render_engine_world, scene) ));
}
void scene_renderer::set_speedtree_instance_visible(speedtree_instance_ptr const& value,bool visible) {
    m_channel.owner_push_back(XRAY_NEW_IMPL(m_allocator,functor_command)(std::bind(&engine::world::set_speedtree_instance_visible,&m_render_engine_world,value,visible)));
}

void scene_renderer::remove_speedtree_instance( scene_ptr const& scene, xray::render::speedtree_instance_ptr const& v, bool populate_forest )
{
	m_channel.owner_push_back  ( XRAY_NEW_IMPL( m_allocator, functor_command ) ( std::bind( &engine::world::remove_speedtree_instance, &m_render_engine_world, scene, v, populate_forest) ));
}

xray::math::uint2 scene_renderer::window_client_size	( xray::render::render_output_window_ptr const& render_output_window )
{
	return		m_render_engine_world.window_client_size( render_output_window );
}
#if defined(USE_VULKAN_RENDERER)
void scene_renderer::close_output(render_output_window_ptr const& output) {
    m_channel.owner_push_back(XRAY_NEW_IMPL(m_allocator,functor_command)(
        std::bind(&engine::world::close_output,&m_render_engine_world,output)));
}
#endif

void scene_renderer::update_speedtree_instance( scene_ptr const& scene, xray::render::speedtree_instance_ptr const& v, xray::math::float4x4 const& transform, bool populate_forest )
{
	m_channel.owner_push_back	( XRAY_NEW_IMPL( m_allocator, functor_with_big_buffer_to_copy_command< math::float4x4 > ) ( std::bind( &engine::world::update_speedtree_instance, &m_render_engine_world, scene, v, std::placeholders::_1, populate_forest), transform ) );
}

#ifndef MASTER_GOLD
void scene_renderer::draw_render_statistics( xray::ui::world& ui_world )
{
    XRAY_REJECT_VULKAN_LEGACY(draw_render_statistics);
	m_channel.owner_push_back	(
		XRAY_NEW_IMPL( m_allocator, functor_command ) (
			std::bind( &xray::render::engine::world::draw_render_statistics, &m_render_engine_world, &ui_world),
			std::bind( &render::defer_execution, std::placeholders::_1, ui_world.get_scene_view() )
		)
	);
}
#endif // #ifndef MASTER_GOLD
