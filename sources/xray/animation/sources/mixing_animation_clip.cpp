////////////////////////////////////////////////////////////////////////////
//	Created		: 02.04.2010
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2010
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include "mixing_animation_clip.h"
#include <xray/animation/base_interpolator.h>
#include "interpolator_comparer.h"

using xray::animation::mixing::animation_clip;

bool animation_clip::operator ==			( animation_clip const& other ) const
{
	if ( type() != other.type() )
		return		false;

	if ( compare(interpolator(), other.interpolator()) != equal )
		return		false;

	return			!strings::compare( identifier(), other.identifier() );
}

bool xray::animation::mixing::operator <	( animation_clip const& left, animation_clip const& right )
{
	if ( left.type() < right.type() )
		return		true;

	if ( right.type() < left.type() )
		return		false;

	switch ( compare(left.interpolator(), right.interpolator()) ) {
		case less : return true;
		case more : return false;
		case equal : 
			return	strings::compare( left.identifier(), right.identifier() ) < 0;
		default :	NODEFAULT(return false);
	}
}
