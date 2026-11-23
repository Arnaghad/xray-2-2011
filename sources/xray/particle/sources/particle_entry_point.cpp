////////////////////////////////////////////////////////////////////////////
//	Created		: 20.07.2010
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2010
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include <xray/particle/api.h>
#include "particle_world_cooker.h"
#include "bounded_particle_template_cook.h"
#include <stdexcept>

namespace xray {
namespace particle {

void register_particle_world_cooker		( );
void unregister_particle_world_cooker	( );
bool try_unregister_particle_world_cooker();

} // namespace particle
} // namespace xray

void xray::particle::initialize	( )
{
	check_particle_template_registration();
	register_particle_world_cooker	( );
	register_bounded_particle_template_cook();
}

void xray::particle::finalize		( )
{
	if (!try_finalize()) throw std::runtime_error("Particle cooks still have pending queries/resources or a foreign thread/registry owner");
}

bool xray::particle::try_finalize()
{
	if (!can_unregister_particle_world_cooker() || !can_unregister_bounded_particle_template_cook()) return false;
	unregister_bounded_particle_template_cook();
	return try_unregister_particle_world_cooker();
}
