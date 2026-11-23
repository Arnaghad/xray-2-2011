////////////////////////////////////////////////////////////////////////////
//	Created		: 01.04.2011
//	Author		: Dmitry Kulikov
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////
#ifndef WAV_FILE_OPTIONS_H_INCLUDED
#define WAV_FILE_OPTIONS_H_INCLUDED

#include <xray/configs.h>

namespace xray {
namespace sound_editor {

	class conversion_options
	{
	public:
		conversion_options() : m_bitrate(0), m_samplerate(0) {}
		
		int bitrate;
		int samplerate;

	private:
		int m_bitrate;
		int m_samplerate;
	}; // class conversion_options

	class wav_file_options
	{
	public:
						wav_file_options	(configs::lua_config_ptr config);
						~wav_file_options	();
				void	save				(const char* name);

		int m_sample_rate;
		int m_channels_count;
		int m_bits_per_sample;
		int m_bitrate;
		conversion_options*	m_high_quality_options;
		conversion_options*	m_medium_quality_options;
		conversion_options*	m_low_quality_options;
	}; // class wav_file_options
} // namespace sound_editor
} // namespace xray
#endif // #ifndef WAV_FILE_OPTIONS_H_INCLUDED
