////////////////////////////////////////////////////////////////////////////
//	Created		: 20.07.2010
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2010
////////////////////////////////////////////////////////////////////////////

#ifndef XRAY_PARTICLE_API_H_INCLUDED
#define XRAY_PARTICLE_API_H_INCLUDED

#ifndef XRAY_PARTICLE_API
#	ifdef XRAY_STATIC_LIBRARIES
#		define XRAY_PARTICLE_API
#	else // #ifdef XRAY_STATIC_LIBRARIES
#		ifdef XRAY_PARTICLE_BUILDING
#			define XRAY_PARTICLE_API		XRAY_DLL_EXPORT
#		else // #ifdef XRAY_PARTICLE_BUILDING
#			define XRAY_PARTICLE_API			XRAY_DLL_IMPORT
#		endif // #ifdef XRAY_PARTICLE_BUILDING
#	endif // #ifdef XRAY_STATIC_LIBRARIES
#endif // #ifndef XRAY_PARTICLE_API

namespace xray {
namespace particle {

XRAY_PARTICLE_API	void	initialize	( );
XRAY_PARTICLE_API	void	finalize	( );
// Leaves cooks registered while pending queries or resource deleters still need them.
XRAY_PARTICLE_API bool try_finalize();

} // namespace particle
} // namespace xray

#endif // #ifndef XRAY_PARTICLE_API_H_INCLUDED

