////////////////////////////////////////////////////////////////////////////
//	Created		: 20.07.2010
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2010
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include "particle_world.h"
#include "particle_system_instance_cook.h"
#include "particle_system_cook.h"

#include "help_functions.h"
#include "particle_entity_types.h"
#include "particle_actions.h"
#include <stdexcept>
#include "particle_system.h"

#include "particle_system_instance.h"
#include "particle_emitter.h"
#include "particle_entity_types.h"

#ifndef MASTER_GOLD
#	include "preview_particle_system.h"
#	include "preview_particle_system_instance.h"
#	include "preview_particle_system_cooker.h"
#endif // #ifndef MASTER_GOLD

#include <xray/particle/base_particle.h>
#include "particle_beam_emitter_instance.h"
#include <xray/resources_unmanaged_allocator.h>
#include <xray/resources.h>
#include <xray/console_command.h>
#include <xray/console_command_processor.h>
#include <xray/particle/engine.h>
#include "particle_actions.h"

static u32 s_particle_simulation_speed_value = 100; // in percents
static xray::console_commands::cc_u32 s_particle_simulation_speed("particle_simulation_speed", 
															s_particle_simulation_speed_value, 
															0, 
															100, 
															true, 
															xray::console_commands::command_type_engine_internal);


namespace xray {
namespace particle {


#ifndef MASTER_GOLD
static xray::uninitialized_reference<preview_particle_system_cooker>	s_preview_particle_system_cook_object;
#endif // #ifndef MASTER_GOLD

void register_preview_particle_system_cooker()
{
#ifndef MASTER_GOLD
	XRAY_CONSTRUCT_REFERENCE	( s_preview_particle_system_cook_object, preview_particle_system_cooker ) ( );
	resources::register_cook	( s_preview_particle_system_cook_object.c_ptr() );
#endif // #ifndef MASTER_GOLD
}

void unregister_preview_particle_system_cooker()
{
#ifndef MASTER_GOLD
	resources::unregister_cook	( resources::preview_particle_system_class );
	XRAY_DESTROY_REFERENCE		( s_preview_particle_system_cook_object );
#endif // #ifndef MASTER_GOLD
}


particle_world::particle_world	( particle::engine& engine ) :
	m_num_particles ( 0 ),
	m_max_particles ( 50000 ),	// sizeof(base_particle) == 204 bytes ..., (204 * 50000) == 9.7 Mb
	m_engine		( engine )
{
}

particle_world::~particle_world	( )
{
	close();
}

void particle_world::require_thread() const
{
	if (threading::current_thread_id() != m_thread)
		throw std::runtime_error("Particle world operation requires its owner thread");
}

void particle_world::require_accepting() const
{
	require_thread();
	if (!accepting())
		throw std::runtime_error("Particle world is closed");
}

particle_system_instance_impl& particle_world::require_instance(resources::unmanaged_resource* resource, bool allow_closed) const
{
	if (allow_closed) require_thread(); else require_accepting();
	auto* instance = dynamic_cast<particle_system_instance_impl*>(resource);
	if (!instance || !instance->belongs_to(*this))
		throw std::runtime_error("Particle instance belongs to another world or has an invalid type");
	return *instance;
}

void particle_world::close()
{
	require_thread();
	m_accepting = false;
 	particle_system_instance_impl* instance = m_ticked_instances_list.front();
 	while(instance)
 	{
 		particle_system_instance_impl* next = m_ticked_instances_list.get_next_of_object(instance);
		
 		remove_particle_system_instance(instance);
		
 		instance = next;
 	}
}

base_particle* particle_world::allocate_particle( )
{
	require_accepting();
	if (!m_allocator.initialized())
		throw std::runtime_error("Particle allocation requires an initialized world arena");
	if ( m_num_particles + 1 > m_max_particles )
	{
		return 0;
	}
	
	base_particle* particle		= XRAY_NEW_IMPL( m_allocator, base_particle )();
	CURE_ASSERT					( particle, return 0 );
	particle->set_defaults		( );
	++m_num_particles;
	
	return particle;
}

void particle_world::deallocate_particle( base_particle* P )
{
	require_thread();
	--m_num_particles;
	XRAY_DELETE_IMPL( m_allocator, P );
}

void particle_world::play(particle_system_instance_ptr instance, xray::math::float4x4 const& transform, bool use_transform, bool always_loop)
{
	particle_system_instance_impl* impl = &require_instance(instance.c_ptr());
	
	if (use_transform)
		impl->play_impl(transform);
	else
		impl->play_impl();
	
	impl->m_always_looping = always_loop;
	impl->m_no_more_create = false;
	
	add_particle_system_instance(impl);
}

void particle_world::stop(particle_system_instance_ptr instance, float time)
{
	particle_system_instance_impl* impl = &require_instance(instance.c_ptr());
	impl->stop_impl(time);
}

void particle_world::set_transform_to_particle_instance(particle_system_instance* instance, xray::math::float4x4 const& transform)
{
	particle_system_instance_impl* impl = &require_instance(instance);
	impl->set_transform(transform);
}


void particle_world::set_max_particles(u32 num_max_particles)
{
	require_accepting();
	if (!num_max_particles || num_max_particles > m_capacity || num_max_particles < m_num_particles)
		throw std::runtime_error("Particle limit exceeds its arena capacity or live particle count");
	m_max_particles  = num_max_particles;
}

void particle_world::check_lods(xray::math::float3 const& view_location)
{
 	particle_system_instance_impl* instance = m_ticked_instances_list.front();
 	while(instance)
 	{
 		instance->check_lods(view_location);
 		instance = m_ticked_instances_list.get_next_of_object(instance);
 	}
}

u32 particle_world::calc_num_new_particles( float time_delta )
{
	u32 total_summ = 0;
	
 	particle_system_instance_impl* instance = m_ticked_instances_list.front();
 	while(instance)
 	{
 		total_summ += instance->calc_num_new_particles(time_delta);
 		instance = m_ticked_instances_list.get_next_of_object(instance);
 	}
	return total_summ;
}

u32 particle_world::calc_num_max_particles(float time_delta)
{
	u32 total_summ = 0;

	particle_system_instance_impl* instance = m_ticked_instances_list.front();
	while(instance)
	{
		total_summ += instance->calc_num_max_particles(time_delta);
		instance = m_ticked_instances_list.get_next_of_object(instance);
	}
	return total_summ;
}

u32 particle_world::remove_dead_particles()
{
	u32 num_removed = 0;

	particle_system_instance_impl* instance = m_ticked_instances_list.front();
	while(instance)
	{
		num_removed += instance->remove_dead_particles();
		instance = m_ticked_instances_list.get_next_of_object(instance);
	}
	return num_removed;
}

void particle_world::remove_overflowing_particles()
{
	particle_system_instance_impl* instance = m_ticked_instances_list.front();
	while(instance)
	{
		instance->remove_overflowing_particles();
		instance = m_ticked_instances_list.get_next_of_object(instance);
	}
}


void particle_world::shrink_particles( float time_delta, float limit_over_total, u32 num_need_particles)
{
	particle_system_instance_impl* instance = m_ticked_instances_list.front();
	while(instance)
	{
		instance->shrink_particles(time_delta, limit_over_total, num_need_particles);
		instance = m_ticked_instances_list.get_next_of_object(instance);
	}
}

void particle_world::tick( float time_delta, xray::math::float4x4 const& view_matrix )
{
	require_accepting();
	time_delta *= s_particle_simulation_speed_value / 100.0f;
	
	float4x4 inv_view_matrix;
	inv_view_matrix.try_invert(view_matrix);
	
	check_lods( inv_view_matrix.lines[3].xyz() );
	
	u32 num_need_new	= calc_num_new_particles(time_delta);
	
	u32	num_particles	= calc_num_max_particles(time_delta);
	
	float total			= float(num_particles + num_need_new);
	
	float k = total > 0 ? float(m_max_particles) / total : 1.f;
	xray::math::clamp( k, 0.001f, 1.0f);
	
	shrink_particles( time_delta, k, num_need_new );
	
	remove_overflowing_particles();
	
	particle_system_instance_impl* instance = m_ticked_instances_list.front();
	while(instance)
	{
		bool is_finished = false;
		
		if (instance->get_ticked())
		{
			is_finished = instance->tick( time_delta );
		}
		
		particle_system_instance_impl* next = m_ticked_instances_list.get_next_of_object(instance);
		if (is_finished && !instance->m_child_played)
		{
			remove(instance);
		}
		
		instance = next;
	}
	ASSERT(m_num_particles<=m_max_particles);
}

void particle_world::add_particle_system_instance(particle_system_instance* instance)
{
	//ps_instance_entry* new_entry = MT_NEW(ps_instance_entry)();
	//m_ticked_instances_list2.push_back( new_entry );
	particle_system_instance_impl* impl = &require_instance(instance);
	// Replaying an active instance must not link the same intrusive node twice.
	for (auto* current = m_ticked_instances_list.front(); current;
		current = m_ticked_instances_list.get_next_of_object(current))
		if (current == impl)
			return;
	impl->self_ptr = impl;
	m_ticked_instances_list.push_back( impl );
}

void particle_world::remove_particle_system_instance(particle_system_instance* in_instance)
{
	require_instance(in_instance, true);
	particle_system_instance_impl* inst = m_ticked_instances_list.front();
	
	bool found = false;
	
	while(inst)
	{
		if (inst==in_instance)
		{
			found = true;
			break;
		}
		inst = m_ticked_instances_list.get_next_of_object(inst);
	}
	
	if (!found)
		return;
	
	inst->remove_particles();
	
	m_ticked_instances_list.erase( static_cast_checked<particle_system_instance_impl*>(in_instance) );
	inst->self_ptr = 0;
}

void particle_world::get_render_emitter_instances( particle::particle_system_instance_ptr in_instance, render_particle_emitter_instances_type& out_particle_emitter_instances )
{
	// TODO: duplicated code!
	
	particle_system_instance_impl* instance = &require_instance(in_instance.c_ptr());
	
	if (!instance->get_ticked() || !instance->get_visible())
	{
		instance = m_ticked_instances_list.get_next_of_object(instance);
		return;
	}
	
	emitter_instance_list_type& emm_list = instance->m_lods[instance->m_current_lod].m_emitter_instance_list;
	
	particle_emitter_instance* em_instance = emm_list.front();
	while(em_instance)
	{
		if (!em_instance->get_visible() || 
			!em_instance->get_data_type_action())
		{
			em_instance = emm_list.get_next_of_object(em_instance);
			continue;
		}
		out_particle_emitter_instances.push_back( &em_instance->render_instance() );
		em_instance = emm_list.get_next_of_object(em_instance);
	}
	if (instance->m_old_lod!=instance->m_current_lod)
	{
		em_instance = instance->m_lods[instance->m_old_lod].m_emitter_instance_list.front();
		while(em_instance)
		{
			if (!em_instance->get_visible() || !em_instance->get_data_type_action())
			{
				em_instance = emm_list.get_next_of_object(em_instance);
				continue;
			}
			out_particle_emitter_instances.push_back( &em_instance->render_instance() );
			em_instance = emm_list.get_next_of_object(em_instance);
		}
	}
}

void particle_world::get_render_emitter_instances_unculled(render_particle_emitter_instances_type& output)
{
	require_accepting();
	for (auto* instance=m_ticked_instances_list.front();instance;instance=m_ticked_instances_list.get_next_of_object(instance)) {
		if (!instance->get_ticked() || !instance->get_visible()) continue;
		unsigned passes=instance->m_current_lod==instance->m_old_lod ? 1 : 2;
		for (unsigned pass=0;pass<passes;++pass) {
			auto& list=instance->m_lods[pass ? instance->m_old_lod : instance->m_current_lod].m_emitter_instance_list;
			for (auto* emitter=list.front();emitter;emitter=list.get_next_of_object(emitter))
				if (emitter->get_visible() && emitter->get_data_type_action()) output.push_back(&emitter->render_instance());
		}
	}
}

void particle_world::get_render_emitter_instances( float4x4 const& view_proj_matrix, render_particle_emitter_instances_type& out_render_emitter_instances)
{
	require_accepting();
	math::frustum view_frustum(view_proj_matrix);
	
	particle_system_instance_impl* instance = m_ticked_instances_list.front();
	
	while(instance)
	{
		if (!instance->get_ticked() || !instance->get_visible())
		{
			instance = m_ticked_instances_list.get_next_of_object(instance);
			continue;
		}
		
		emitter_instance_list_type& emm_list = instance->m_lods[instance->m_current_lod].m_emitter_instance_list;
		
		particle_emitter_instance* em_instance = emm_list.front();
		while(em_instance)
		{
			if (!em_instance->get_visible() || 
				!em_instance->get_data_type_action() || 
				view_frustum.test_inexact(em_instance->m_aabbox)==math::intersection_outside)
			{
				em_instance = emm_list.get_next_of_object(em_instance);
				continue;
			}
			out_render_emitter_instances.push_back( &em_instance->render_instance() );
			em_instance = emm_list.get_next_of_object(em_instance);
		}
		if (instance->m_old_lod!=instance->m_current_lod)
		{
			em_instance = instance->m_lods[instance->m_old_lod].m_emitter_instance_list.front();
			while(em_instance)
			{
				if (!em_instance->get_visible() || !em_instance->get_data_type_action())
				{
					em_instance = emm_list.get_next_of_object(em_instance);
					continue;
				}
				out_render_emitter_instances.push_back( &em_instance->render_instance() );
				em_instance = emm_list.get_next_of_object(em_instance);
			}
		}
		instance = m_ticked_instances_list.get_next_of_object(instance);
	}
}

particle_emitter_instance* particle_world::create_emitter_instance	( particle_emitter& emitter, bool is_child_emitter_instance )
{
	require_accepting();
	particle_action_data_type* action	= emitter.get_data_type_action();
	if ( action && (action->get_data_type() == particle_data_type_beam) )
		return							MT_NEW ( particle_beam_emitter_instance ) ( *this, emitter, m_engine, is_child_emitter_instance );

	return								MT_NEW ( particle_emitter_instance ) ( *this, emitter, m_engine, is_child_emitter_instance );
}

particle_emitter_instance* particle_world::create_ready_emitter_instance(particle_emitter& emitter,
	resources::unmanaged_resource_ptr const& material)
{
	require_accepting();
	return MT_NEW(particle_emitter_instance)(*this,emitter,m_engine,false,&material);
}

void particle_world::change_material	( particle_emitter_instance& instance, resources::unmanaged_resource_ptr const& material )
{
	require_accepting();
	if (&instance.m_particle_world != this)
		throw std::runtime_error("Particle emitter belongs to another world");
	instance.change_material			( material );
}

// particle_system_instance_ptr particle_world::play			( particle_system_ptr in_particle_system, math::float4x4 const& transform )
// {
// 	particle_system_instance_ptr out_ptr = ((xray::particle::particle_system*)in_particle_system.c_ptr())->play(*this, transform);
// 	
// 	xray::particle::particle_system_instance_impl* inst = (xray::particle::particle_system_instance_impl*)out_ptr.c_ptr();
// 	inst->m_always_looping = true;/*always_looping*/;
// 	
// 	return out_ptr;
// }
// 
// void particle_world::stop									( particle_system_ptr particle_system )
// {
// 	//((particle_system_instance_impl*)in_instance.c_ptr())->stop_impl( 0.0f );
// }

void particle_world::remove									( particle_system_instance_ptr in_particle_system_instance )
{
	remove_particle_system_instance(&require_instance(in_particle_system_instance.c_ptr(), true));
}

void particle_world::set_transform							( particle_system_instance_ptr particle_system_instance, math::float4x4 const& transform )
{
	particle_system_instance_impl* instance = &require_instance(particle_system_instance.c_ptr());
	instance->set_transform					(transform);
}

void particle_world::set_visible							( particle_system_instance_ptr particle_system_instance, bool is_visible )
{
	particle_system_instance_impl* instance = &require_instance(particle_system_instance.c_ptr());
	instance->set_visible					(is_visible);
}

void particle_world::set_paused								( particle_system_instance_ptr particle_system_instance, bool is_paused )
{
	particle_system_instance_impl* instance = &require_instance(particle_system_instance.c_ptr());
	instance->set_pause						(is_paused);
}


#ifndef MASTER_GOLD

particle_system* particle_world::add(configs::lua_config_value const&, math::float4x4 const&) {
	throw std::runtime_error("Particle preview creation is not integrated");
}
void particle_world::remove(particle_system**) {
	throw std::runtime_error("Particle preview removal is not integrated");
}
void particle_world::update_preview(particle_system**, configs::lua_config_value) {
	throw std::runtime_error("Particle preview update is not integrated");
}
void particle_world::set_transform(particle_system**, math::float4x4 const&) {
	throw std::runtime_error("Particle preview transform is not integrated");
}
void particle_world::set_looped(particle_system**, bool) {
	throw std::runtime_error("Particle preview looping is not integrated");
}
void particle_world::restart(particle_system**) {
	throw std::runtime_error("Particle preview restart is not integrated");
}
void particle_world::show(particle_system**) {
	throw std::runtime_error("Particle preview selection is not integrated");
}
u32 particle_world::get_num_preview_particle_system_emitters(particle_system**) {
	throw std::runtime_error("Particle preview statistics are not integrated");
}
void particle_world::gather_stats(particle_system**, preview_particle_emitter_info*) {
	throw std::runtime_error("Particle preview statistics are not integrated");
}

#endif // #ifndef MASTER_GOLD

} // namespace particle
} // namespace xray
