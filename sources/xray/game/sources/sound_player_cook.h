////////////////////////////////////////////////////////////////////////////
//	Created		: 02.03.2011
//	Author		: Tetyana Meleshchenko
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////

#ifndef SOUND_PLAYER_COOK_H_INCLUDED
#define SOUND_PLAYER_COOK_H_INCLUDED

#include <xray/resources_cook_classes.h>
#include <xray/sound/sound.h>

namespace xray {



namespace sound {
	struct world;

} // namespace sound
} // namespace xray

namespace stalker2 {

class sound_player_cook :	public resources::translate_query_cook,
							private xray::noncopyable
{
public:
					sound_player_cook		( sound::sound_scene_ptr& scene, sound::world* world, resources::class_id_enum class_id );



	virtual	void	translate_query			( resources::query_result_for_cook&	parent );
	virtual void	delete_resource			( resources::resource_base* resource );

private:
			void	on_sounds_loaded		( resources::queries_result& data, configs::binary_config_ptr config );
			void	on_config_loaded		( resources::queries_result& data );

private:
	sound::world*							m_world;
	sound::sound_scene_ptr&					m_scene;
}; // class sound_player_cook

} // namespace stalker2

#endif // #ifndef SOUND_PLAYER_COOK_H_INCLUDED
