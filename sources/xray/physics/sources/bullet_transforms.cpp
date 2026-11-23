#include "pch.h"
#include <xray/physics/bullet_utils.h>
namespace xray::physics {
btTransform from_xray( float4x4 const& m )
{
	math::quaternion q_xray(m);
	btQuaternion q0		= from_xray(q_xray);
	return btTransform(q0, from_xray(m.c.xyz()) );
}

float4x4 from_bullet( btTransform const& m )
{
	btQuaternion q_bullet	= m.getRotation();
	math::quaternion q_xray = from_bullet( q_bullet );
	return create_rotation(q_xray) * create_translation( from_bullet(m.getOrigin()) );
}



}
