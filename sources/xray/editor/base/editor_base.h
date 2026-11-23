//-------------------------------------------------------------------------------------------
//	Created		: 03.06.2010
//	Author		: Sergey Pryshchepa
//	Copyright (C) GSC Game World - 2010
//-------------------------------------------------------------------------------------------

#ifndef EDITOR_BASE_H_INCLUDED
#define EDITOR_BASE_H_INCLUDED

// Cross-platform editor base interface (pure C++).
// Replaces former C++/CLI 'interface class' that used:
//   ref class command_engine^, System::String^,
//   WeifenLuo::WinFormsUI::Docking::IDockContent^
// The full managed interface equivalent is in IEditorBase.cs.

namespace xray {
namespace editor_base {

	class editor_base
	{
	public:
		virtual ~editor_base() = default;

		virtual void	on_render		() = 0;
		virtual void	initialize		() = 0;
		virtual void	destroy			() = 0;
		virtual void	clear_resources	() = 0;
		virtual void	tick			() = 0;
		virtual bool	close_query		() = 0;
		virtual bool	load			( const char* name ) = 0;
		virtual void	show			() = 0;
		virtual void	load_settings	() = 0;
		virtual void	save_settings	() = 0;
		virtual void	set_default_layout() = 0;
		virtual const char*	name		() = 0;

	}; // class editor_base

} // namespace editor_base
} // namespace xray

#endif // #ifndef EDITOR_BASE_H_INCLUDED
