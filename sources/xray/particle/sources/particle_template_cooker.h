////////////////////////////////////////////////////////////////////////////
//	Created		: 26.07.2010
//	Author		: Nikolay Partas
//	Copyright (C) GSC Game World - 2010
////////////////////////////////////////////////////////////////////////////

#ifndef PARTICLE_TEMPLATE_COOKER_H_INCLUDED
#define PARTICLE_TEMPLATE_COOKER_H_INCLUDED

#include <xray/resources_cook_classes.h>

namespace xray {
namespace particle {

class particle_system;
class particle_system_lod;
class particle_emitter;
class particle_action;
class particle_world;

struct particle_template_cooker_data
{
	fs::path_string		resource_path;
	resources::query_result_for_cook* query_result;
	u64					source_info_time = 0;
	u64					converted_info_time = 0;
	bool				source_ready = false;
	bool				converted_ready = false;
};

class particle_template_cooker :
	public resources::translate_query_cook,
	private xray::noncopyable
{
	typedef resources::translate_query_cook super;
public:
						particle_template_cooker	( xray::particle::world& world );
	
	virtual void		translate_query				( resources::query_result& parent );
	virtual void		delete_resource				( resources::unmanaged_resource* res );

private:
	void				on_sub_resources_loaded		( resources::queries_result& data );
	void				on_lua_config_loaded		( resources::queries_result& result );
	void				on_fs_iterator_ready_source	( particle_template_cooker_data* data, xray::resources::fs_iterator fs_it );
	void				on_fs_iterator_ready_converted( particle_template_cooker_data* data, xray::resources::fs_iterator fs_it );
	void				on_fs_iterators_ready		( particle_template_cooker_data* data );
	void				request_convertion			( fs::path_string* resource_path, resources::query_result_for_cook* parent );
	
	particle_action*	parse_action				( particle_emitter* emitter, configs::lua_config_value const& action_config );
	void				parse_emitter				( particle_emitter* emitter, configs::lua_config_value const& emitter_config );
	void				parse_emitter_type			( particle_emitter* emitter, configs::lua_config_value const& emitter_config );
	void				parse_group					( particle_system_lod& ps_lod, configs::lua_config_value const& group_config );
	void				parse_root					( particle_system* ps, configs::lua_config_value const& config );

private:
	xray::particle::world& m_world;
}; // class particle_template_cooker

void register_particles_template_cooker		( xray::particle::world& world );
void unregister_particles_template_cooker	( );

} // namespace particle
} // namespace xray

#endif // #ifndef PARTICLE_TEMPLATE_COOKER_H_INCLUDED
