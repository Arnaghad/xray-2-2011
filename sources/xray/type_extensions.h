////////////////////////////////////////////////////////////////////////////
//	Created 	: 29.08.2008
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#ifndef XRAY_TYPE_EXTENSIONS_H_INCLUDED
#define XRAY_TYPE_EXTENSIONS_H_INCLUDED

#include <cstdint>

typedef	std::int8_t		s8;
typedef	std::uint8_t	u8;

typedef	std::int16_t	s16;
typedef	std::uint16_t	u16;

typedef	std::int32_t	s32;
typedef	std::uint32_t	u32;

typedef	std::int64_t	s64;
typedef	std::uint64_t	u64;

static_assert(sizeof(s8) == 1 && sizeof(u8) == 1, "8-bit integer size mismatch");
static_assert(sizeof(s16) == 2 && sizeof(u16) == 2, "16-bit integer size mismatch");
static_assert(sizeof(s32) == 4 && sizeof(u32) == 4, "32-bit integer size mismatch");
static_assert(sizeof(s64) == 8 && sizeof(u64) == 8, "64-bit integer size mismatch");

namespace xray {
	enum assert_enum : int {
		assert_untyped
	};
	enum process_error_enum : int {
		process_error_false,
		process_error_true,
		process_error_to_message_box
	};
	enum assert_on_fail_bool : int {
		assert_on_fail_false,
		assert_on_fail_true
	};

	template <typename T>
	inline T const& identity(T const& value) { return value; }
}

typedef char		string16[16];
typedef char		string32[32];
typedef char		string64[64];
typedef char		string128[128];
typedef char		string256[256];
typedef char		string512[512];
typedef char		string1024[1024];
typedef char		string2048[2048];
typedef char		string4096[4096];
typedef char		string_path[260];

typedef char const* pcstr;
typedef char*		pstr;
typedef void const* pcvoid;
typedef void*		pvoid;
typedef u8 const*	pcbyte;
typedef u8*			pbyte;

#endif // #ifndef XRAY_TYPE_EXTENSIONS_H_INCLUDED