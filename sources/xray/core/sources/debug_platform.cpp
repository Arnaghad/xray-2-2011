//////////////////////////////////////////////////////////////////////////////
//	Created 	: 17.10.2008
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2009
//////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include "debug_platform.h"
#include <xray/os_include.h>

namespace xray {
namespace core {
namespace debug {

void set_thread_stack_guarantee()
{
}

namespace platform {

void setup_unhandled_exception_handler(error_mode)
{
}

void change_unhandled_exception_handler(error_mode)
{
}

bool error_after_dialog()
{
	return false;
}

pstr fill_format_message(int const error_code)
{
	pstr result;
	FormatMessageA(FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM, nullptr, error_code, 0, (LPSTR)&result, 0, nullptr);
	return result;
}

void free_format_message(pstr const buffer)
{
	LocalFree(buffer);
}

pcstr get_graphics_api_error_description(int const error_code)
{
	pstr result;
	FormatMessageA(FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS, nullptr, error_code, MAKELANGID(LANG_NEUTRAL, SUBLANG_DEFAULT), (LPSTR)&result, 0, nullptr);
	return (pcstr)result;
}

void setup_storage_access_handler(error_mode, void (*)(int))
{
}

void change_storage_access_handler(error_mode, void (*)(int))
{
}

void on_error(bool* do_debug_break, char* const message, u32 const /*message_size*/, bool* /*ignore_always*/, _EXCEPTION_POINTERS* /*exception_information*/, error_type_enum /*error_type*/)
{
	if (do_debug_break)
		*do_debug_break = true;
}

void on_error_message_box(bool* do_debug_break, char* const message, u32 const /*message_size*/, bool* /*ignore_always*/, _EXCEPTION_POINTERS* /*exception_information*/, error_type_enum /*error_type*/)
{
	MessageBoxA(nullptr, message, "Fatal Error", MB_OK | MB_ICONERROR | MB_SYSTEMMODAL);
	if (do_debug_break)
		*do_debug_break = true;
}

void terminate(pcstr message, int const error_code)
{
	if (*message)
		MessageBoxA(nullptr, message, "Error", MB_OK | MB_ICONERROR | MB_SYSTEMMODAL);
	TerminateProcess(GetCurrentProcess(), error_code);
	::exit(error_code);
}

void format_message()
{
}

void prologue_dump_call_stack(_EXCEPTION_POINTERS*)
{
}

void save_minidump(_SYSTEMTIME const&, _EXCEPTION_POINTERS*)
{
}

} // namespace platform

} // namespace debug
} // namespace core
} // namespace xray
