////////////////////////////////////////////////////////////////////////////
//	Created		: 27.10.2010
//	Author		: Plichko Alexander
//	Copyright (C) GSC Game World - 2010
////////////////////////////////////////////////////////////////////////////

#ifndef I_CONTEXT_MANAGER_H_INCLUDED
#define I_CONTEXT_MANAGER_H_INCLUDED

// Cross-platform context manager interface (pure C++).
// Replaces former C++/CLI 'interface class' that used System::String^ and
// System::Collections::Generic::List<System::String^>^.
// The managed interface equivalent is now in IContextManager.cs.

#include <vector>
#include <string>

namespace xray {
namespace editor_base {

class i_context_manager {
public:
	virtual ~i_context_manager() = default;

	virtual bool				context_fit			( const char* context_str ) = 0;
	virtual std::vector<std::string>	get_contexts_list	( ) = 0;

}; // class i_context_manager

} // namespace editor_base
} // namespace xray

#endif // #ifndef I_CONTEXT_MANAGER_H_INCLUDED
