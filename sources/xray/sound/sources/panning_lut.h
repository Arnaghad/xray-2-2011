////////////////////////////////////////////////////////////////////////////
//	Created		: 26.05.2011
//	Author		: Dmitry Kulikov
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////

#ifndef PANNING_LUT_H_INCLUDED
#define PANNING_LUT_H_INCLUDED

#include <xray/sound/channels_type.h>
#include <xray/resources_unmanaged_resource.h>
#include <xray/resources_resource_ptr.h>

namespace xray {
namespace sound {

class panning_lut :	public resources::unmanaged_resource, 
					private xray::noncopyable
{
public:
						panning_lut					( channels_type type );
	virtual				~panning_lut				( );

	inline	float&		operator[]					( u32 index ) { return m_table[index]; }
public:
	enum
	{
		quadrant_num		= 128,
		lut_num				= 512,
	};
private:
	float			m_table[ static_cast<int>(channels_count) * static_cast<int>(lut_num) ];
}; // class panning_lut

typedef resources::resource_ptr < panning_lut, resources::unmanaged_intrusive_base > panning_lut_ptr;

} // namespace sound
} // namespace xray

#endif // #ifndef PANNING_LUT_H_INCLUDED
