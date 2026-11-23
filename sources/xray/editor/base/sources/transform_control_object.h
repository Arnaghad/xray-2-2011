////////////////////////////////////////////////////////////////////////////
//	Created		: 28.10.2010
//	Author		: Andrew Kolomiets
//	Copyright (C) GSC Game World - 2010
////////////////////////////////////////////////////////////////////////////

#ifndef TRANSFORM_CONTROL_OBJECT_H_INCLUDED
#define TRANSFORM_CONTROL_OBJECT_H_INCLUDED

#include <vector>
using std::vector;

namespace xray {
namespace collision{struct space_partitioning_tree; class object;}

namespace editor_base {
class transform_control_base;

struct collision_object_wrapper
{
	collision::object*		m_collision_object;
};

class transform_control_object
{
public:
	enum class mode { edit_transform, edit_pivot };

	virtual				~transform_control_object( ){}
	
	virtual bool		attach				( transform_control_base* )	{ return true; };
	virtual void		detach				( transform_control_base* )	{};
	virtual void		start_modify		( transform_control_base* )		= 0;
	virtual void		execute_preview		( transform_control_base* )		= 0;
	virtual void		end_modify			( transform_control_base* )		= 0;
	virtual float4x4	get_ancor_transform	( )								= 0;
	virtual u32			get_collision		( vector<collision_object_wrapper>& out_list ) { XRAY_UNREFERENCED_PARAMETER(out_list); return 0; }
			mode		current_mode		( ) {return m_edit_mode_;}

	static	bool		is_edit_pivot_mode				( ) { return m_edit_mode_==mode::edit_pivot;}
	static	void		switch_edit_pivot_mode			( ) { m_edit_mode_ = is_edit_pivot_mode() ? mode::edit_transform : mode::edit_pivot;}
	virtual bool		translation_ignore_rotation		( ) { return true; }
private:
	static mode			m_edit_mode_;

}; // transform_control_object


} // namespace editor_base
} // namespace xray

#endif // #ifndef TRANSFORM_CONTROL_OBJECT_H_INCLUDED
