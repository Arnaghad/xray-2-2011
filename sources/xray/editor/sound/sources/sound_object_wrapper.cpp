////////////////////////////////////////////////////////////////////////////
//	Created		: 07.07.2011
//	Author		: Sergey Prishchepa
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////
#include "pch.h"
#include "sound_object_wrapper.h"
#include "sound_object_instance.h"

using xray::sound_editor::sound_object_wrapper;
using xray::sound_editor::sound_object_instance;

sound_object_wrapper::sound_object_wrapper(const char* name)
:m_name(name),
m_sound_loaded_callback(nullptr),
m_options_loaded_callback(nullptr),
m_is_loaded(false),
m_is_exist(false)
{
	m_sound = NEW(sound::sound_emitter_ptr)();
}

sound_object_wrapper::~sound_object_wrapper()
{
	if(m_sound)
		DELETE(m_sound);

	m_sound = nullptr;
}

sound_object_instance* sound_object_wrapper::create_instance()
{
	return NEW(sound_object_instance)(this);
}
