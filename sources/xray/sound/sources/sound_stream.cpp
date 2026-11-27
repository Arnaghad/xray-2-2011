////////////////////////////////////////////////////////////////////////////
//	Created		: 16.04.2010
//	Author		: Andrew Kolomiets
//	Copyright (C) GSC Game World - 2010
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include "sound_stream.h"

namespace xray {
namespace sound {

sound_stream::sound_stream( resources::managed_resource_ptr raw_file/*, resources::managed_resource_ptr options*/ )
//:m_options( options )
{

	m_raw_file.resource			= raw_file;
	m_raw_file.pointer			= 0;

	memory::zero				(&m_wfx, sizeof(m_wfx));

	ov_callbacks ovc			= {ogg_utils::ov_read_func, ogg_utils::ov_seek_func, ogg_utils::ov_close_func, ogg_utils::ov_tell_func};
 	ov_open_callbacks			(&m_raw_file, &m_ovf, nullptr, 0, ovc);

	vorbis_info* ovi			= ov_info(&m_ovf, -1);

	ASSERT						(ovi, "Invalid source info");

#if 1
	m_wfx.samples_per_sec		= (ovi->rate);
	m_wfx.format_tag			= 1;
	m_wfx.channels				= u16(ovi->channels);
	m_wfx.bits_per_sample		= 16;

	m_wfx.block_align			= (m_wfx.channels * m_wfx.bits_per_sample) / 8;
	m_wfx.avg_bytes_per_sec		= m_wfx.samples_per_sec * m_wfx.block_align;
#endif // #if 1

	m_pcm_total					= (u32)ov_pcm_total(&m_ovf, -1);
}

u32 sound_stream::decompress( pbyte dest, u32& pcm_pointer, u32 bytes_needed )
{

	u32 res			= ogg_utils::decompress(&m_ovf, dest, pcm_pointer, bytes_needed);
	return			res;
}

sound_stream::~sound_stream( )
{
	ov_clear	( &m_ovf );
//	m_rms.clear	( );
}

} // namespace sound
} // namespace xray
