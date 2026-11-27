////////////////////////////////////////////////////////////////////////////
//	Module 		: platform.h
//	Created 	: 10.03.2008
//  Modified 	: 10.03.2008
//	Author		: Dmitriy Iassenev
//	Description : platform specific
////////////////////////////////////////////////////////////////////////////

#ifndef CS_CORE_PLATFORM_H_INCLUDED
#define CS_CORE_PLATFORM_H_INCLUDED

#include <cs/core/os_functions.h>

#if defined(_MSC_VER)
#	include <intrin.h>
#	pragma intrinsic(__rdtsc)
#elif defined(__x86_64__) || defined(__i386__)
#	include <x86intrin.h>
#endif

namespace cs {
namespace core {

enum platform_enum {
	platform_PC 		= (unsigned int)(0),
}; // enum platform_enum

class CS_CORE_API platform {
public:
	u64					qpc_per_second;

public:
						platform		();
	inline u64			get_clocks		()
	{
#if defined(_MSC_VER) || defined(__x86_64__) || defined(__i386__)
		return			__rdtsc();
#else
		return			get_QPC();
#endif
	}
	
	u64					get_QPC			();

	static inline bool	little_endian	()
	{
		u16	const word	= 0x0001;
		return			!!*(u8 const*)&word;
	}

	static inline u32	alignment		()
	{
		struct dummy {};
		return			sizeof(dummy);
	}
};

CS_CORE_API extern platform	g_platform;

} // namespace core
} // namespace cs

#endif // #ifndef CS_CORE_PLATFORM_H_INCLUDED
