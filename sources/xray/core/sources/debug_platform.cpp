//////////////////////////////////////////////////////////////////////////////
//	Created 	: 17.10.2008
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2009
//////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include "debug_platform.h"
#if XRAY_PLATFORM_WINDOWS
#	include <xray/os_include.h>
#else
#	include <cstdio>
#	include <cstring>
#	include <unistd.h>
#endif

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
#if XRAY_PLATFORM_WINDOWS
	pstr result;
	FormatMessageA(FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM, nullptr, error_code, 0, (LPSTR)&result, 0, nullptr);
	return result;
#else
	char* buf = new char[512];
	buf[0] = '\0';
	xray::debug::format_system_error(error_code, buf, 512);
	return buf;
#endif
}

void free_format_message(pstr const buffer)
{
#if XRAY_PLATFORM_WINDOWS
	LocalFree(buffer);
#else
	delete[] buffer;
#endif
}

pcstr get_graphics_api_error_description(int const error_code)
{
#if XRAY_PLATFORM_WINDOWS
	pstr result;
	FormatMessageA(FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS, nullptr, error_code, MAKELANGID(LANG_NEUTRAL, SUBLANG_DEFAULT), (LPSTR)&result, 0, nullptr);
	return (pcstr)result;
#else
	XRAY_UNREFERENCED_PARAMETER(error_code);
	return nullptr;
#endif
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
	XRAY_UNREFERENCED_PARAMETER(message);
}

void on_error_message_box(bool* do_debug_break, char* const message, u32 const /*message_size*/, bool* /*ignore_always*/, _EXCEPTION_POINTERS* /*exception_information*/, error_type_enum /*error_type*/)
{
#if XRAY_PLATFORM_WINDOWS
	MessageBoxA(nullptr, message, "Fatal Error", MB_OK | MB_ICONERROR | MB_SYSTEMMODAL);
#else
	xray::debug::show_error_dialog("Fatal Error", message);
#endif
	if (do_debug_break)
		*do_debug_break = true;
}

void terminate(pcstr message, int const error_code)
{
#if XRAY_PLATFORM_WINDOWS
	if (*message)
		MessageBoxA(nullptr, message, "Error", MB_OK | MB_ICONERROR | MB_SYSTEMMODAL);
	TerminateProcess(GetCurrentProcess(), error_code);
#else
	if (message && *message)
		std::fprintf(stderr, "Error: %s\n", message);
	std::fflush(nullptr);
	::_exit(error_code);
#endif
	::exit(error_code);
}

void format_message()
{
}

void prologue_dump_call_stack(_EXCEPTION_POINTERS*)
{
}

void save_minidump(xray::debug::date_time const&, _EXCEPTION_POINTERS*)
{
}

} // namespace platform

} // namespace debug
} // namespace core
} // namespace xray
