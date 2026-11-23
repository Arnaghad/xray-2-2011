#ifndef XRAY_UNREFERENCED_PARAMETER_H_INCLUDED
#define XRAY_UNREFERENCED_PARAMETER_H_INCLUDED


namespace xray {
namespace detail {
	template < typename... Args >
	inline void	unreferenced_parameter_helper ( Args const&... ) {}
} // namespace detail
} // namespace xray

#define XRAY_UNREFERENCED_PARAMETER(parameter) \
			(void)(&parameter)

#define XRAY_UNREFERENCED_PARAMETERS(...) \
	::xray::detail::unreferenced_parameter_helper(__VA_ARGS__)

// Release assertions must discard debug-only expressions entirely. The _U
// variant keeps valid parameters referenced without evaluating their effects.
#define XRAY_EMPTY_EXPRESSION ((void)0)
#define XRAY_EMPTY_EXPRESSION_VA_ARGS(...) ((void)0)
#define XRAY_EMPTY_EXPRESSION_U_VA_ARGS(...) \
	do { if (false) ::xray::detail::unreferenced_parameter_helper(__VA_ARGS__); } while (0)

#endif // #ifndef XRAY_UNREFERENCED_PARAMETER_H_INCLUDED
