#ifndef XRAY_ENGINE_CAPTURE_CONTROL_BUS_H_INCLUDED
#define XRAY_ENGINE_CAPTURE_CONTROL_BUS_H_INCLUDED

#include <xray/platform_surface.h>
#include <xray/render/vulkan/platform_surface.h>
#include <cstdint>

namespace xray {
namespace engine {
namespace capture_control_bus {

inline bool set_token( platform::native_surface_handle const& surface, char const* key, std::uintptr_t value )
{
	return render::vulkan::surface_adapter::set_surface_property( surface, key, static_cast<u64>( value ) );
}

inline std::uintptr_t get_token( platform::native_surface_handle const& surface, char const* key )
{
	return static_cast<std::uintptr_t>( render::vulkan::surface_adapter::get_surface_property( surface, key ) );
}

inline bool set_flag( platform::native_surface_handle const& surface, char const* key, bool enabled = true )
{
	if ( !enabled ) {
		render::vulkan::surface_adapter::remove_surface_property( surface, key );
		return true;
	}
	return set_token( surface, key, 1 );
}

inline bool has_flag( platform::native_surface_handle const& surface, char const* key )
{
	return get_token( surface, key ) != 0;
}

inline bool set_surface( platform::native_surface_handle const& owner, char const* key, platform::native_surface_handle const& value )
{
	return render::vulkan::surface_adapter::set_surface_handle_property( owner, key, value );
}

inline platform::native_surface_handle get_surface( platform::native_surface_handle const& owner, char const* key )
{
	return render::vulkan::surface_adapter::get_surface_handle_property( owner, key );
}

inline void remove( platform::native_surface_handle const& surface, char const* key )
{
	render::vulkan::surface_adapter::remove_surface_property( surface, key );
}

inline void clear_surface( platform::native_surface_handle const& surface )
{
	render::vulkan::surface_adapter::clear_surface_properties( surface );
}

} // namespace capture_control_bus
} // namespace engine
} // namespace xray

#endif // #ifndef XRAY_ENGINE_CAPTURE_CONTROL_BUS_H_INCLUDED
