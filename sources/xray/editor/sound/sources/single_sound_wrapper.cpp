////////////////////////////////////////////////////////////////////////////
//	Created		: 25.03.2011
//	Author		: Dmitry Kulikov
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////
#include "pch.h"
#include "single_sound_wrapper.h"
#include "sound_editor_manager.h"
#include "wav_file_options.h"
#include "sound_object_instance.h"
#include <xray/sound/sound_emitter.h>
#include <xray/sound/wav_utils.h>
#include <xray/sound/spl_utils.h>
#include <xray/fs_utils.h>

using xray::sound_editor::single_sound_wrapper;
using xray::sound_editor::sound_object_instance;

single_sound_wrapper::single_sound_wrapper(const char* name)
:sound_object_wrapper(name)
{
	m_wav_options = nullptr;
}

single_sound_wrapper::~single_sound_wrapper()
{
	if (m_wav_options)
		DELETE(m_wav_options);
}

xray::sound::sound_rms_ptr single_sound_wrapper::get_sound_rms()
{
	return (xray::static_cast_resource_ptr<xray::sound::single_sound_ptr>(*m_sound))->get_sound_rms();
}

xray::sound::sound_spl_ptr single_sound_wrapper::get_sound_spl()
{
	return (xray::static_cast_resource_ptr<xray::sound::single_sound_ptr>(*m_sound))->get_sound_spl();
}

void single_sound_wrapper::save(xray::configs::lua_config_value& root)
{
	// Logic to save curves using xray::math::float_curve
	XRAY_UNREFERENCED_PARAMETER(root);
}

void single_sound_wrapper::save()
{
	if (m_wav_options)
		m_wav_options->save(m_name.c_str());

	std::string ss_path = std::string(sound_editor_manager::sound_resources_path) + "single/" + m_name + ".single_sound_options";
	configs::lua_config_ptr const& config = configs::create_lua_config(ss_path.c_str());
	configs::lua_config_value root = config->get_root()["spl"];
	save(root);
	config->save(configs::target_sources);
	
	if(m_sound->c_ptr())
		get_sound_spl()->get_curve()->load(root);
}

void single_sound_wrapper::load(xray::configs::lua_config_value const& cfg)
{
	XRAY_UNREFERENCED_PARAMETER(cfg);
}

void single_sound_wrapper::load(callback_type options_callback, callback_type sound_callback)
{
	m_sound_loaded_callback = sound_callback;
	m_options_loaded_callback = options_callback;

	std::string raw_path = std::string(sound_editor_manager::sound_resources_path) + "single/" + m_name + ".raw_options";
	std::string ss_path = std::string(sound_editor_manager::sound_resources_path) + "single/" + m_name + ".single_sound_options";

	fs::path_string source_wav_path;
	source_wav_path.assignf("%ssingle/%s.wav", sound_editor_manager::sound_resources_path, m_name.c_str());

	xray::fs_new::physical_path_info source_wav_info = resources::get_physical_path_info(source_wav_path.c_str(), resources::sources_mount);

	if (source_wav_info.is_file())
		m_is_exist = true;
	else
	{
		m_is_exist = false;
		return;
	}

	resources::request request[] = { 
		{ raw_path.c_str(), resources::lua_config_class },
		{ ss_path.c_str(), resources::lua_config_class }
	};

	resources::query_resources(
		request, 
		2, 
		std::bind(&single_sound_wrapper::on_wav_options_loaded, this, std::placeholders::_1), 
		g_allocator
	);
}

void single_sound_wrapper::on_single_sound_loaded(xray::resources::queries_result& data)
{
	ASSERT(data.is_successful());
	(*m_sound) = xray::static_cast_resource_ptr<xray::sound::sound_emitter_ptr>(data[0].get_unmanaged_resource());
	m_is_loaded = true;
	if(m_sound_loaded_callback)
		m_sound_loaded_callback(this);
}

void single_sound_wrapper::on_wav_options_loaded(xray::resources::queries_result& data)
{
	configs::lua_config_ptr wav_cfg;
	configs::lua_config_ptr spl_cfg;

	if(data[0].is_successful())
		wav_cfg = xray::static_cast_resource_ptr<configs::lua_config_ptr>(data[0].get_unmanaged_resource());
	else
	{
		pcstr requested_path = data[0].get_primary_requested_path();
		xray::fs_new::native_path_string wav_path;
		bool const convertion_result = xray::resources::convert_virtual_to_physical_path(&wav_path, requested_path, resources::sources_mount);
		R_ASSERT(convertion_result);
		xray::fs_new::set_extension_for_path(&wav_path, "wav");
		xray::fs_new::synchronous_device_interface const & device = xray::resources::get_synchronous_device();
		wav_cfg = xray::sound::wav_utils::create_default_config(wav_path, device);
	}

	if(data[1].is_successful())
		spl_cfg = xray::static_cast_resource_ptr<configs::lua_config_ptr>(data[1].get_unmanaged_resource());
	else
		spl_cfg = xray::sound::create_default_spl_config(data[1].get_primary_requested_path(), g_allocator);

	if (m_wav_options) DELETE(m_wav_options);
	m_wav_options = NEW(wav_file_options)(wav_cfg);
	
	read_spl_curve(spl_cfg->get_root()["spl"]);
	
	if(m_options_loaded_callback)
		m_options_loaded_callback(this);

	math::float4x4 matrix = math::float4x4().identity();
	math::float4x4 const* matrix_pointers[] = {&matrix};
	resources::request request = { m_name.c_str(), resources::single_sound_class };

	resources::query_resource_params params(&request, nullptr, 1, 
		std::bind(&single_sound_wrapper::on_single_sound_loaded, this, std::placeholders::_1),
		g_allocator, nullptr, matrix_pointers
	);

	resources::query_resources(params);
}

void single_sound_wrapper::read_spl_curve(xray::configs::lua_config_value const& cfg)
{
	// Logic to read curves using xray::math::float_curve
	XRAY_UNREFERENCED_PARAMETER(cfg);
}

void single_sound_wrapper::apply_changes(bool load_resources)
{
	save();
	if (load_resources)
		load(nullptr, nullptr);
}

void single_sound_wrapper::revert_changes()
{
	load(nullptr, nullptr);
}
