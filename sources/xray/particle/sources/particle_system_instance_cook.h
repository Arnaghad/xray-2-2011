////////////////////////////////////////////////////////////////////////////
//	Created		: 28.07.2010
//	Author		: Nikolay Partas
//	Copyright (C) GSC Game World - 2010
////////////////////////////////////////////////////////////////////////////

#ifndef PARTICLE_SYSTEM_INSTANCE_COOK_H_INCLUDED
#define PARTICLE_SYSTEM_INSTANCE_COOK_H_INCLUDED

#include <xray/resources_cook_classes.h>
#include <xray/particle/world_resources.h>

namespace xray {
namespace particle {

struct world;

class particle_system;
class particle_emitter;

class particle_system_instance_cook :	
	public resources::translate_query_cook,
	public xray::noncopyable
{
	typedef resources::translate_query_cook	super;
	
public:
	particle_system_instance_cook							();
	virtual			~particle_system_instance_cook		( ) {}
	
	virtual	void	translate_query			( resources::query_result_for_cook & parent );
	virtual void	delete_resource			( resources::resource_base* res );
	bool cache_by_game_resources_manager() override { return false; }
	
private:
	void on_sub_resources_loaded(resources::queries_result& data, world_ptr const& owner);
	void on_materials_loaded(resources::queries_result& data, world_ptr const& owner,
		resources::unmanaged_resource_ptr const& particle_template);
}; // class particle_system_instance_cook

} // namespace particle
} // namespace xray

#endif // #ifndef PARTICLE_SYSTEM_INSTANCE_COOK_H_INCLUDED
