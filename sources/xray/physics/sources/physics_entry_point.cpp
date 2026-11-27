////////////////////////////////////////////////////////////////////////////
//	Created 	: 20.11.2008
//	Author		: Dmitriy Iassenev
//	Description : default entry point functions
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include "physics_world.h"
#include "bullet_physics_world.h"
#include "collision_shape_cook.h"
#include <xray/physics/api.h>
#include <xray/linkage_helper.h>

#if XRAY_PLATFORM_LINUX
#	include <cstdarg>
#	include <cstdio>
#	include <cstdlib>
#	include <ode/error.h>

extern "C" void dError( int num, char const* msg, ... )
{
	va_list ap;
	va_start( ap, msg );
	if ( auto* fn = dGetErrorHandler() ) {
		fn( num, msg, ap );
	} else {
		std::fprintf( stderr, "ODE Error %d: ", num );
		std::vfprintf( stderr, msg, ap );
		std::fprintf( stderr, "\n" );
	}
	va_end( ap );
	std::exit( 1 );
}

extern "C" void dDebug( int num, char const* msg, ... )
{
	va_list ap;
	va_start( ap, msg );
	if ( auto* fn = dGetDebugHandler() ) {
		fn( num, msg, ap );
	} else {
		std::fprintf( stderr, "ODE INTERNAL ERROR %d: ", num );
		std::vfprintf( stderr, msg, ap );
		std::fprintf( stderr, "\n" );
	}
	va_end( ap );
	std::abort();
}

extern "C" void dMessage( int num, char const* msg, ... )
{
	va_list ap;
	va_start( ap, msg );
	if ( auto* fn = dGetMessageHandler() ) {
		fn( num, msg, ap );
	} else {
		std::fprintf( stderr, "ODE Message %d: ", num );
		std::vfprintf( stderr, msg, ap );
		std::fprintf( stderr, "\n" );
	}
	va_end( ap );
}
#endif // #if XRAY_PLATFORM_LINUX

memory::base_allocator*	xray::physics::g_ph_allocator = nullptr;

XRAY_DECLARE_LINKAGE_ID(physics_entry_point)

namespace xray {
namespace physics{

xray::physics::world* create_world( xray::memory::base_allocator* allocator, xray::physics::engine& engine )
{
	return XRAY_NEW_IMPL( allocator, physics_world )	( *allocator, engine );
}

xray::physics::world* create_world_bt( xray::memory::base_allocator* allocator, physics::engine& engine )
{
	return XRAY_NEW_IMPL( allocator, bullet_physics_world )	( *allocator, engine );
}

void set_memory_allocator	( memory::base_allocator* allocator )
{
	ASSERT				( !g_ph_allocator || g_ph_allocator==allocator );
	g_ph_allocator		= allocator;

	static collision_shape_cook collision_shape_cooker_static( true );
	static collision_shape_cook collision_shape_cooker_dynamic( false );
}

void destroy_world( xray::memory::base_allocator* allocator, world* w )
{
	XRAY_DELETE_IMPL( allocator, w);
}

} // namespace physics
} // namespace xray
