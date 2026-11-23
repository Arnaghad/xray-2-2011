////////////////////////////////////////////////////////////////////////////
//	Created		: 21.02.2011
//	Author		: Tetyana Meleshchenko
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////

#include "pch.h"

#include "ai_sound_player.h"
#include <xray/sound/sound_emitter.h>
#include <xray/sound/sound_instance_proxy.h>
#include <xray/sound/world_user.h>


#include "human_npc.h"
#include <xray/render/facade/debug_renderer.h>
#include <xray/render/facade/game_renderer.h>


namespace stalker2 {

ai_sound_player::sounds_collection_type::sounds_collection_type	(	ai_sound_player* parent,
																	ai::sound_collection_types collection_type,
																	sound::sound_emitter_ptr emitter_ptr,
																	u32 collection_priority ) :
		type				( collection_type ),
		priority			( collection_priority )
{
	emitter.initialize_and_set_parent( parent, emitter_ptr.c_ptr() );
}

ai_sound_player::ai_sound_player	(	sound::sound_scene_ptr& scene,
										u32 sounds_count,
										sound::world_user& user,
										sound::sound_producer const* const producer,
										sound::sound_receiver const* const ignorable_receiver
									) :
	m_scene							( scene ),
	m_user							( user ),
	m_sounds_count					( sounds_count ),
	m_sound_producer				( producer ),
	m_ignorable_receiver			( ignorable_receiver )
{	
}



void ai_sound_player::on_dbg_sound_scene_created	( resources::queries_result& data )
{
	R_ASSERT			( data.is_successful( ) );
	m_dbg_scene			= static_cast_resource_ptr< xray::sound::sound_scene_ptr >( data[0].get_unmanaged_resource() );
}

ai_sound_player::~ai_sound_player	( )
{
	LOG_DEBUG						( "~ai_sound_player" );

	m_dbg_scene						= 0;
	m_active_sound					= 0;
	m_dbg_active_sound				= 0;
//	m_scene							= 0;


	sounds_collection_type const* const begin	= sounds( );
	sounds_collection_type const* const end		= begin + m_sounds_count;
	for ( sounds_collection_type const* i = begin; i != end; ++i )
	{
		i->~sounds_collection_type	( );
	}

}

ai_sound_player::sounds_collection_type const* ai_sound_player::find (
		ai::sound_collection_types const sound_type
	) const
{
	sounds_collection_type const* const begin	= sounds( );
	sounds_collection_type const* const end		= begin + m_sounds_count;
	for ( sounds_collection_type const* i = begin; i != end; ++i )
	{
		if ( i->type == sound_type )
			return i;
	}
	return 0;
}

void ai_sound_player::play			(
		ai::sound_collection_types sound_type,
		bool sound_is_positioned,
		float3 const& position
	)
{
	sounds_collection_type const* type	= find( sound_type );
	R_ASSERT						( type, "such a type is absent in sound collections" );

	m_active_sound					= type->emitter->emit( m_scene, m_user );
	m_active_sound->set_callback	( [this]() { return on_finish_playing(); } );

//	if ( sound_is_positioned )
//		m_active_sound->play		( position, sound::once, m_sound_producer, m_ignorable_receiver );
//	else
//		m_active_sound->play		( sound::once, m_sound_producer, m_ignorable_receiver );
}

void ai_sound_player::play			(
		resources::unmanaged_resource_ptr sound_to_be_played,
		ai::finish_playing_callback const& finish_callback,
		float3 const& position
	)
{
	sound::sound_emitter_ptr sound	= static_cast_resource_ptr< sound::sound_emitter_ptr >( sound_to_be_played );
	m_active_sound					= sound->emit( m_scene, m_user );
	m_active_sound->set_callback	( finish_callback );
	m_active_sound->set_position	( position );
	m_active_sound->play			( sound::once, m_sound_producer, m_ignorable_receiver );
}

void ai_sound_player::play_once		(
		ai::sound_collection_types sound_type,
		bool sound_is_positioned,
		float3 const& position
	)
{
	XRAY_UNREFERENCED_PARAMETER				( sound_is_positioned );
	sounds_collection_type const* type		= find( sound_type );
	R_ASSERT								( type, "such a type is absent in sound collections" );

	sound::sound_instance_proxy_ptr proxy	= type->emitter->emit( m_scene, m_user );
	type->emitter->emit_and_play_once( m_scene, m_user, position, m_sound_producer, m_ignorable_receiver );
}

void ai_sound_player::tick			( )
{
	u32 const current_time_in_ms	= m_dbg_timer.get_elapsed_msec( );
	R_ASSERT_CMP					( current_time_in_ms, >=, m_dbg_last_current_time_in_ms );
	//u32 const time_delta			= current_time_in_ms - m_dbg_last_current_time_in_ms;

	human_npc const* npc			= static_cast_checked<human_npc const*>( m_sound_producer );
	if ( !npc->get_sound_dbg_mode( ) )
		return;

	if ( m_active_sound )
	{
		//m_active_sound->dbg_draw			( npc->get_dbg_render( ), npc->get_dbg_scene( ) );
		//if ( m_active_sound->is_playing( ) )
		//{
		//	math::float4x4 mtx	= math::create_translation( npc->get_position( float3( .0f, .0f, .0f )));
		//	npc->get_dbg_render().debug().draw_line_ellipsoid( npc->get_dbg_scene( ), mtx,/* float3( 1.f, 1.f, 1.f ),*/ math::color( 0, 255, 0 ) );
		//}
	}
	//npc->get_dbg_render( ).debug().

	//m_dbg_time_from_last_playing	+= time_delta;
	//if ( m_dbg_time_from_last_playing > m_dbg_time_to_play_new_sound )
	//{
	//	sounds_collection_type const* type		= find( ai::sound_collection_type_npc_pain );
	//	R_ASSERT								( type, "such a type is absent in sound collections" );

	//	m_active_sound							= type->emitter->emit( m_user );
	//	m_active_sound->play					( npc->get_position( float3( 0.0f, 0.0f, 0.0f )), sound::once, m_sound_producer, m_ignorable_receiver );

	//	u32 val							= m_dbg_random.random( 20000 ) + 5000;
	//	m_dbg_time_to_play_new_sound	= val;
	//	LOG_DEBUG						( "m_dbg_time_to_play_new_sound: %d", m_dbg_time_to_play_new_sound );
	//	m_dbg_time_from_last_playing	= 0;
	//}



	m_dbg_last_current_time_in_ms		= current_time_in_ms;
}



sound::command_result_enum ai_sound_player::on_finish_playing( )
{
//	m_active_sound				= 0;
	LOG_DEBUG					( "ai_sound_player::on_finish_playing( )" );
	return sound::command_result_executed;
}

void ai_sound_player::clear_resources	( )
{
	m_user.remove_sound_scene	( m_dbg_scene );
//	m_dbg_scene				= 0;
	m_active_sound			= 0;
	m_dbg_active_sound		= 0;
//	m_scene					= 0;
}

void ai_sound_player::serialize			( )
{
	memory::writer* w			= XRAY_NEW_IMPL( g_allocator, memory::writer ) ( g_allocator );			

	sounds_collection_type const* const begin	= sounds( );
	sounds_collection_type const* const end		= begin + m_sounds_count;
	for ( sounds_collection_type const* i = begin; i != end; ++i )
	{
		i->emitter->serialize	( *w );
	}

	std::function< void ( memory::writer*, memory::writer* ) > fn
					= [this](auto&& arg1, auto&& arg2) { return on_active_sound_serialized(std::forward<decltype(arg1)>(arg1), std::forward<decltype(arg2)>(arg2)); };	
	m_active_sound->serialize	( fn, w );
}

void ai_sound_player::on_active_sound_serialized	( memory::writer* sound_thread_writer, memory::writer* current_thread_writer )
{
	current_thread_writer->write		( sound_thread_writer->pointer(), sound_thread_writer->size() );
	if ( !current_thread_writer->save_to( "Z:/test.sound_player" ) )
	{
		LOG_ERROR( "unable to write file [Z:/test.sound_player]" );
	}

	XRAY_DELETE_IMPL					( g_allocator, current_thread_writer );
}


void ai_sound_player::deserialize		( )
{
	using namespace fs_new;
	synchronous_device_interface const & device	=	resources::get_synchronous_device();

	file_type*		f;
	if ( !device->open( &f, "Z:/test.sound_player", file_mode::open_existing, file_access::read) )
		LOG_ERROR( "unable to open file [Z:/test.sound_player]" );
	else
	{
		device->seek			(f, 0, seek_file_end);
		u32 read_data_size		= (u32)device->tell(f);
		device->seek			(f, 0, seek_file_begin);

		pvoid buff				= MALLOC( read_data_size, "deserialize ai_sound_player" );
		device->read			( f, buff, read_data_size );
		device->close			( f );

		memory::reader* r		= XRAY_NEW_IMPL( g_allocator, memory::reader ) ( (u8 const*)buff, read_data_size );

		sounds_collection_type const* const begin	= sounds( );
		sounds_collection_type const* const end		= begin + m_sounds_count;
		for ( sounds_collection_type const* i		= begin; i != end; ++i )
		{
			i->emitter->deserialize	( *r );
		}

		sounds_collection_type const* type		= find( ai::sound_collection_type_npc_pain );
		R_ASSERT								( type, "such a type is absent in sound collections" );

		std::function< void ( void ) > func	= std::bind( &ai_sound_player::on_active_sound_deserialized, this, r, buff );
		m_active_sound	= sound::sound_instance_proxy::deserialize(
			func,
			r,
			m_scene,
			m_user,
			*type->emitter.c_ptr( ),
			[this]() { return on_finish_playing(); } );
	}
}

void ai_sound_player::on_active_sound_deserialized( memory::reader* reader, pvoid buf )
{
	XRAY_UNREFERENCED_PARAMETERS		( reader, buf );
//	return sound::command_result_executed;
}

} // namespace stalker2
