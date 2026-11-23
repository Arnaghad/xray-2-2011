//-------------------------------------------------------------------------------------------
//	Created		: 04.06.2010
//	Author		: Sergey Pryshchepa
//	Copyright (C) GSC Game World - 2010
//-------------------------------------------------------------------------------------------
#ifndef ACTION_ENGINE_INTERFACE_H_INCLUDED
#define ACTION_ENGINE_INTERFACE_H_INCLUDED

// Cross-platform action engine interface (pure C++).
// Replaces former C++/CLI 'interface class' that used System::String^,
// System::Void, System::Collections::ICollection^, and managed ref class types.
// The managed interface equivalent is now in IActionEngine.cs.

namespace xray {
namespace editor_base {

	class action_engine_interface 
	{
	public:
		virtual ~action_engine_interface() = default;

		virtual void	register_action_single		( const char* action_name, const char* shortcut = nullptr, const char* shortcut2 = nullptr ) = 0;
		virtual void	register_action_continuous	( const char* action_name, const char* shortcut = nullptr, const char* shortcut2 = nullptr ) = 0;

		virtual bool	action_exists	( const char* name ) = 0;
		virtual void	execute			( const char* name ) = 0;

	}; // class action_engine_interface

} // namespace editor_base
} // namespace xray

#endif // #ifndef ACTION_ENGINE_INTERFACE_H_INCLUDED
