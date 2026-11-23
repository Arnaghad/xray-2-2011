////////////////////////////////////////////////////////////////////////////
//	Created 	: 06.10.2008
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#ifndef XRAY_DEBUG_MACROS_H_INCLUDED
#define XRAY_DEBUG_MACROS_H_INCLUDED

#include <xray/type_extensions.h>
#include <xray/macro_extensions.h>
#include <xray/os_include.h>
#include <utility>
#include <xray/debug.h>

#ifndef DEBUG_BREAK
	#if defined(__GNUC__) || defined(__clang__)
		#define DEBUG_BREAK() __builtin_trap()
	#else
		#define DEBUG_BREAK() __debugbreak()
	#endif
#endif


#if defined (R_ASSERT) || defined (R_ASSERT_T) || defined (R_ASSERT_U) || defined (R_ASSERT_T_U) || defined (R_ASSERT_CMP) || defined (R_ASSERT_CMP_T) || defined (R_ASSERT_CMP_U) || defined (R_ASSERT_CMP_T_U)
#	error please do not define R_ASSERT, R_ASSERT_T, R_ASSERT_U, R_ASSERT_T_U, R_ASSERT_CMP, R_ASSERT_CMP_T, R_ASSERT_CMP_U and R_ASSERT_CMP_T_U macros
#endif // #if defined (R_ASSERT) || defined (R_ASSERT_T) || defined (R_ASSERT_U) || defined (R_ASSERT_T_U) || defined (R_ASSERT_CMP) || defined (R_ASSERT_CMP_T) || defined (R_ASSERT_CMP_U) || defined (R_ASSERT_CMP_T_U)

#if defined (ASSERT) || defined (ASSERT_T) || defined (ASSERT_U) || defined (ASSERT_T_U) || defined (ASSERT_CMP) || defined (ASSERT_CMP_T) || defined (ASSERT_CMP_U) || defined (ASSERT_CMP_T_U)
#	error please do not define ASSERT, ASSERT_T, ASSERT_U, ASSERT_T_U, ASSERT_CMP, ASSERT_CMP_T, ASSERT_CMP_U and ASSERT_CMP_T_U macros
#endif // #if defined (ASSERT) || defined (ASSERT_T) || defined (ASSERT_U) || defined (ASSERT_T_U) || defined (ASSERT_CMP) || defined (ASSERT_CMP_T) || defined (ASSERT_CMP_U) || defined (ASSERT_CMP_T_U)

#if defined (CURE_ASSERT) || defined (CURE_ASSERT_CMP)
#	error please do not define CURE_ASSERT and CURE_ASSERT_CMP macros
#endif // #if defined (CURE_ASSERT) defined (CURE_ASSERT_CMP)

#if defined (FATAL) || defined (UNREACHABLE_CODE) || defined (NOT_IMPLEMENTED) || defined (CHECK_AND_CORRECT) || defined (CHECK_OR_EXIT)
#	error please do not define FATAL, UNREACHABLE_CODE, NOT_IMPLEMENTED, CHECK_AND_CONTINUE and CHECK_OR_EXIT macros
#endif // #if defined (FATAL) || defined (UNREACHABLE_CODE) || defined (NOT_IMPLEMENTED) || defined (CHECK_AND_CORRECT) || defined (CHECK_OR_EXIT)

#ifdef NODEFAULT
#	undef NODEFAULT
#endif

#define XRAY_ASSERT_HELPER( ignore_always, process_error, expression, assert_type, ... ) \
	bool do_debug_break = true; \
	::xray::debug::on_error( &do_debug_break, process_error, &ignore_always, assert_type, "", expression, __FILE__, __FUNCTION__, __LINE__, ##__VA_ARGS__ ); \
	if ( do_debug_break ) DEBUG_BREAK();



#ifdef _MSC_VER
#	define XRAY_ANALYSIS_ASSUME( ... )									__analysis_assume( !!( __VA_ARGS__ ) )
#else
#	define XRAY_ANALYSIS_ASSUME( ... )									
#endif

#	define XRAY_INIT_PRIORITY( priority )

#ifdef MASTER_GOLD
#	define FATAL(...)
#	define R_ASSERT_T(...)
#	define R_ASSERT_CMP_T(...)
#	define R_ASSERT_BOX(...)
#else // #ifdef MASTER_GOLD
#	define FATAL( format, ... )												\
		if ( ::xray::identity(true) ) {										\
			static bool debug_macro_helper_ignore_always = false;			\
			if ( !debug_macro_helper_ignore_always ) {						\
				XRAY_ASSERT_HELPER(											\
					debug_macro_helper_ignore_always,						\
					::xray::process_error_true,								\
					"fatal error",											\
					::xray::assert_untyped,									\
					format,													\
					##__VA_ARGS__											\
				)															\
			}																\
		}																	\
		else (void)0

#	define R_ASSERT_T( assert_type, expression, ... )						\
		if ( ::xray::identity(true) ) {										\
			static bool debug_macro_helper_ignore_always = false;			\
			if ( !debug_macro_helper_ignore_always && !::xray::identity(expression) ) {	\
				XRAY_ASSERT_HELPER(											\
					debug_macro_helper_ignore_always,						\
					::xray::process_error_true,								\
					XRAY_MAKE_STRING(expression),							\
					assert_type,											\
					##__VA_ARGS__											\
				)															\
			}																\
			XRAY_ANALYSIS_ASSUME( !!(expression) );							\
		} else XRAY_ANALYSIS_ASSUME( !!(expression) )

#	define R_ASSERT_CMP_T( value1, operation, value2, assert_type, ... )	\
		if ( ::xray::identity(true) ) {										\
			static bool debug_macro_helper_ignore_always = false;			\
			if ( !debug_macro_helper_ignore_always ) {						\
				bool debug_macro_helper_comparison_result;					\
				::xray::debug::detail::string_helper fail_message;			\
				::xray::debug::detail::make_fail_message(					\
					& fail_message,											\
					debug_macro_helper_comparison_result,					\
					::xray::debug::detail::make_tuple( value1, value2 )		\
					operation												\
					::xray::debug::detail::compare_helper()					\
				);															\
				if ( !debug_macro_helper_comparison_result )	{			\
					fail_message.appendf	(__VA_ARGS__);					\
					XRAY_ASSERT_HELPER(										\
						debug_macro_helper_ignore_always,					\
						::xray::process_error_true,							\
						XRAY_MAKE_STRING(value1)							\
						" "													\
						XRAY_MAKE_STRING(operation)							\
						" "													\
						XRAY_MAKE_STRING(value2),							\
						assert_type,										\
						"%s",												\
						fail_message.c_str( )								\
					);														\
				}															\
			}																\
			XRAY_ANALYSIS_ASSUME( ((value1) operation (value2)) );			\
		}																	\
		else																\
			XRAY_ANALYSIS_ASSUME( ((value1) operation (value2)) )

#	define R_ASSERT_T_U			R_ASSERT_T
#	define R_ASSERT_CMP_T_U		R_ASSERT_CMP_T

#	define R_ASSERT_BOX( expression, ... )									\
		if ( ::xray::identity(true) ) {										\
			static bool debug_macro_helper_ignore_always = false;			\
			if ( !debug_macro_helper_ignore_always && !::xray::identity(expression) ) {		\
				XRAY_ASSERT_HELPER(											\
					debug_macro_helper_ignore_always,						\
					::xray::process_error_to_message_box,					\
					XRAY_MAKE_STRING(expression),							\
					::xray::assert_untyped,									\
					##__VA_ARGS__											\
				)															\
			}																\
			XRAY_ANALYSIS_ASSUME( !!(expression) );							\
		} else XRAY_ANALYSIS_ASSUME( !!(expression) )

#endif // #ifdef MASTER_GOLD

#define R_ASSERT( expression, ... )											R_ASSERT_T( ::xray::assert_untyped, expression, ##__VA_ARGS__ )
#define R_ASSERT_CMP( value1, operation, value2, ... )						R_ASSERT_CMP_T( value1, operation, value2, ::xray::assert_untyped, ##__VA_ARGS__ )

#define R_ASSERT_U( expression, ... )										R_ASSERT_T_U( ::xray::assert_untyped, expression, ##__VA_ARGS__ )
#define R_ASSERT_CMP_U( value1, operation, value2, ... )					R_ASSERT_CMP_T_U( value1, operation, value2, ::xray::assert_untyped, ##__VA_ARGS__ )

////////////////////////////////////////////////////////////////////////////
// ASSERT* macros
////////////////////////////////////////////////////////////////////////////
#ifdef DEBUG
#	define ASSERT_T															R_ASSERT_T
#	define ASSERT_CMP_T														R_ASSERT_CMP_T

#	define ASSERT_T_U														ASSERT_T
#	define ASSERT_CMP_T_U													ASSERT_CMP_T

#if defined(__GNUC__)
#	define XRAY_UNREACHABLE_CODE( description, ... )						\
		if ( ::xray::identity(true) ) {										\
			FATAL( description );											\
			__VA_ARGS__;													\
			__builtin_unreachable();										\
		}																	\
		else																\
			__VA_ARGS__
#else // #if defined(__GNUC__)
#	define XRAY_UNREACHABLE_CODE( description, ... )						\
		if ( ::xray::identity(true) ) {										\
			FATAL( description );											\
			__VA_ARGS__;													\
		}																	\
		else																\
			__VA_ARGS__
#endif // #if defined(__GNUC__)

#else // #ifdef DEBUG
#	define ASSERT_T															XRAY_EMPTY_EXPRESSION_VA_ARGS
#	define ASSERT_CMP_T( value1, operation, value2, assert_type, ... )		XRAY_EMPTY_EXPRESSION

#	define ASSERT_T_U														XRAY_EMPTY_EXPRESSION_U_VA_ARGS
#	define ASSERT_CMP_T_U( value1, operation, value2, assert_type, ... )	if ( ::xray::identity(false) ) { ::xray::debug::detail::expression_eater(value1, value2, assert_type, ##__VA_ARGS__); } else (void)0
#	ifndef __SNC__
#		if defined(__GNUC__)
#			define XRAY_UNREACHABLE_CODE( ... )									__builtin_unreachable()
#		else // #if defined(__GNUC__)
#			define XRAY_UNREACHABLE_CODE( ... )									__assume(0)
#		endif // #if defined(__GNUC__)
#	else // #ifndef __SNC__
#		define XRAY_UNREACHABLE_CODE( description, ... )					__VA_ARGS__
#	endif // #ifndef __SNC__
#endif // #ifdef DEBUG

#define UNREACHABLE_CODE( ... )												XRAY_UNREACHABLE_CODE( "unreachable code accessed!", ##__VA_ARGS__ )
#define NODEFAULT(...)													XRAY_UNREACHABLE_CODE( "unintended default case accessed!", ##__VA_ARGS__ )


#define NOT_IMPLEMENTED( ... )												XRAY_UNREACHABLE_CODE( "functionality has not been implemented yet!", ##__VA_ARGS__ )

#define ASSERT( expression, ... )											ASSERT_T( ::xray::assert_untyped, expression, ##__VA_ARGS__ )
#define ASSERT_CMP( value1, operation, value2, ... )						ASSERT_CMP_T( value1, operation, value2, ::xray::assert_untyped, ##__VA_ARGS__ )

#define ASSERT_U( expression, ... )											ASSERT_T_U( ::xray::assert_untyped, expression, ##__VA_ARGS__ )
#define ASSERT_CMP_U( value1, operation, value2, ... )						ASSERT_CMP_T_U( value1, operation, value2, ::xray::assert_untyped, ##__VA_ARGS__ )

#define CHECK_AND_CORRECT( expression, action, ... )						\
	if ( ::xray::identity(true) ) {											\
		if ( !::xray::identity( expression ) ) {							\
			LOG_WARNING					( __VA_ARGS__ );					\
			action;															\
		}																	\
	} else (void)0
	
#define CHECK_OR_EXIT( expression, format, ... )							\
	if ( ::xray::identity(true) ) {											\
		if ( !::xray::identity( expression ) )								\
		::xray::debug::terminate		( format, ##__VA_ARGS__ );			\
	} else (void)0

////////////////////////////////////////////////////////////////////////////
// CURE_ASSERT* macros
////////////////////////////////////////////////////////////////////////////
#ifdef MASTER_GOLD
#	define CURE_ASSERT( expression, action, ... )							\
		if ( ::xray::identity(true) ) {										\
			static bool debug_macro_helper_ignore_always = false;			\
			if ( !debug_macro_helper_ignore_always && !::xray::identity(expression) ) {	\
				static u32	occurances_left	=	u32(-1);					\
				if ( occurances_left == u32(-1) )							\
					occurances_left	=	assert_log_times_in_gold ();		\
				if ( occurances_left-- ) {									\
					XRAY_ASSERT_HELPER	(									\
						debug_macro_helper_ignore_always,					\
						::xray::process_error_false,						\
						XRAY_MAKE_STRING(expression),						\
						::xray::assert_untyped,								\
						##__VA_ARGS__										\
					);														\
				}															\
				action;														\
			}																\
		} else (void)0

#	define CURE_ASSERT_CMP( value1, operation, value2, action, ... )		\
		if ( ::xray::identity(true) ) {										\
			static bool debug_macro_helper_ignore_always = false;			\
			if ( !debug_macro_helper_ignore_always ) {						\
				bool debug_macro_helper_comparison_result;					\
				::xray::debug::detail::string_helper fail_message;			\
				::xray::debug::detail::make_fail_message(					\
					& fail_message,											\
					debug_macro_helper_comparison_result,					\
					::xray::debug::detail::make_tuple( value1, value2 )		\
					operation												\
					::xray::debug::detail::compare_helper()					\
				);															\
				if ( !debug_macro_helper_comparison_result ) {				\
					fail_message.appendf(__VA_ARGS__);						\
					static u32 occurances_left	= u32(-1);					\
					if ( occurances_left == u32(-1) )						\
						occurances_left	=	assert_log_times_in_gold( );	\
					if ( occurances_left-- )								\
						XRAY_ASSERT_HELPER	(								\
							debug_macro_helper_ignore_always,				\
							::xray::process_error_false,					\
							XRAY_MAKE_STRING(value1)						\
							" "												\
							XRAY_MAKE_STRING(operation)						\
							" "												\
							XRAY_MAKE_STRING(value2),						\
							::xray::assert_untyped,							\
							"%s",											\
							fail_message.c_str( )							\
						);													\
				}															\
				action;														\
			}																\
		} else (void)0

#else // #ifdef MASTER_GOLD
#	define CURE_ASSERT( expression, action, ... )							\
		if ( ::xray::identity(true) ) {										\
			static bool debug_macro_helper_ignore_always = false;			\
			if ( !debug_macro_helper_ignore_always && !::xray::identity(expression) ) {	\
				XRAY_ASSERT_HELPER	(										\
					debug_macro_helper_ignore_always,						\
					::xray::process_error_true,								\
					XRAY_MAKE_STRING(expression),							\
					::xray::assert_untyped,									\
					##__VA_ARGS__											\
				);															\
				action;														\
			}																\
		} else (void)0

#	define CURE_ASSERT_CMP( value1, operation, value2, action, ... )		\
		if ( ::xray::identity(true) ) {										\
			static bool debug_macro_helper_ignore_always = false;			\
			if ( !debug_macro_helper_ignore_always ) {						\
				bool debug_macro_helper_comparison_result;					\
				::xray::debug::detail::string_helper fail_message;			\
				::xray::debug::detail::make_fail_message(					\
					& fail_message,											\
					debug_macro_helper_comparison_result,					\
					::xray::debug::detail::make_tuple( value1, value2 )		\
					operation												\
					::xray::debug::detail::compare_helper()					\
				);															\
				if ( !debug_macro_helper_comparison_result ) {				\
					fail_message.appendf(__VA_ARGS__);						\
					XRAY_ASSERT_HELPER	(									\
						debug_macro_helper_ignore_always,					\
						::xray::process_error_true,							\
						XRAY_MAKE_STRING((value1) operation (value2)),		\
						::xray::assert_untyped,								\
						"%s",												\
						fail_message.c_str( )								\
					);														\
					action;													\
				}															\
			}																\
		} else (void)0

#endif // #ifdef MASTER_GOLD

#include <xray/debug/macros_inline.h>

#endif // #ifndef XRAY_DEBUG_MACROS_H_INCLUDED
