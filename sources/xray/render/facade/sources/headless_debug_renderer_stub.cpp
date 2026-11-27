////////////////////////////////////////////////////////////////////////////
//	Created		: 26.11.2026
//	Description	: No-op render::debug::renderer stubs for XRAY_HEADLESS_BUILD
////////////////////////////////////////////////////////////////////////////

#define XRAY_LOG_MODULE_INITIATOR "render_stub"
#include <xray/extensions.h>
#include <xray/render/facade/debug_renderer.h>

namespace xray {
namespace render {
namespace debug {

renderer::renderer( one_way_render_channel& channel, memory::base_allocator& allocator, engine::world& engine_world )
	: m_render_engine_world( engine_world )
	, m_channel( channel )
	, m_allocator( allocator )
{
}

void renderer::draw_line( scene_ptr const&, float3 const&, float3 const&, math::color const&, bool ) {}
void renderer::draw_cube( scene_ptr const&, float4x4 const&, float3 const&, math::color const&, bool ) {}
void renderer::draw_obb( scene_ptr const&, float4x4 const&, float3 const&, math::color const&, bool ) {}
void renderer::draw_frustum( scene_ptr const&, float, float, float, float, float3 const&, float3 const&, float3, math::color const&, bool ) {}
void renderer::draw_rectangle( scene_ptr const&, float4x4 const&, float3 const&, math::color const&, bool ) {}
void renderer::draw_aabb( scene_ptr const&, float3 const&, float3 const&, math::color const&, bool ) {}
void renderer::draw_ellipse( scene_ptr const&, float4x4 const&, math::color const&, bool ) {}
void renderer::draw_ellipsoid( scene_ptr const&, float4x4 const&, math::color const&, bool ) {}
void renderer::draw_ellipsoid( scene_ptr const&, float4x4 const&, float3 const&, math::color const&, bool ) {}
void renderer::draw_circle( scene_ptr const&, float4x4 const&, float3 const&, math::color const&, bool ) {}
void renderer::draw_cylinder( scene_ptr const&, float4x4 const&, float3 const&, math::color const&, bool ) {}
void renderer::draw_sphere( scene_ptr const&, float3 const&, const float&, math::color const&, bool ) {}
void renderer::draw_cone( scene_ptr const&, float4x4 const&, float3 const&, math::color const&, bool ) {}
void renderer::draw_arrow( scene_ptr const&, float3 const&, float3 const&, math::color const&, math::color const&, bool ) {}
void renderer::draw_arrow( scene_ptr const&, float3 const&, float3 const&, math::color const&, bool ) {}
void renderer::draw_lines( scene_ptr const&, vectora< vertex_colored > const&, vectora< u16 > const&, bool ) {}
void renderer::draw_lines( scene_ptr const&, vectora< vertex_colored >::const_iterator, vectora< vertex_colored >::const_iterator, vectora< u16 > const&, bool ) {}
void renderer::draw_lines( scene_ptr const&, float4x4 const&, float const*, u32, u16 const*, u32, math::color const&, bool ) {}
void renderer::draw_origin( scene_ptr const&, float4x4 const&, float const, bool ) {}
void renderer::draw_cross( scene_ptr const&, float3 const&, float const, math::color const&, bool ) {}
void renderer::draw_triangle( scene_ptr const&, float3 const&, float3 const&, float3 const&, math::color const&, bool ) {}
void renderer::draw_triangle( scene_ptr const&, vertex_colored const&, vertex_colored const&, vertex_colored const&, bool ) {}
void renderer::draw_triangle( scene_ptr const&, vertex_colored const ( & )[ 3 ], bool ) {}
void renderer::draw_triangles( scene_ptr const&, debug_vertices_type const&, bool ) {}
void renderer::draw_triangles( scene_ptr const&, debug_vertices_type const&, debug_indices_type const&, bool ) {}
void renderer::draw_frustum_solid( scene_ptr const&, float const, float const, float const, float const, float3 const&, float3 const&, float3, math::color const&, bool ) {}
void renderer::draw_rectangle_solid( scene_ptr const&, float4x4 const&, float3 const&, math::color const&, bool ) {}
void renderer::draw_cube_solid( scene_ptr const&, float4x4 const&, float3 const&, math::color const&, bool ) {}
void renderer::draw_cone_solid( scene_ptr const&, float4x4 const&, float3 const&, math::color const&, bool ) {}
void renderer::draw_cylinder_solid( scene_ptr const&, float4x4 const&, float3 const&, math::color const&, bool ) {}
void renderer::draw_sphere_solid( scene_ptr const&, float3 const&, const float&, math::color const&, bool ) {}
void renderer::draw_ellipsoid_solid( scene_ptr const&, float4x4 const&, float3 const&, math::color const&, bool ) {}
void renderer::draw_line_ellipsoid( scene_ptr const&, float4x4 const&, math::color const&, bool ) {}
void renderer::draw_line_hemisphere( scene_ptr const&, float4x4 const&, float3 const&, bool, math::color const&, bool ) {}
void renderer::draw_line_capsule( scene_ptr const&, float4x4 const&, float3 const&, math::color const&, bool ) {}
void renderer::draw_solid_capsule( scene_ptr const&, float4x4 const&, float3 const&, math::color const&, bool ) {}
void renderer::draw_lines( scene_ptr const&, float4x4 const&, float3 const&, float const*, u32, u16 const*, u32, math::color const&, bool ) {}
void renderer::draw_primitive_solid( scene_ptr const&, float4x4 const&, float3 const&, float const*, u32, u16 const*, u32, math::color const&, bool ) {}
void renderer::create_primitive( scene_ptr const&, vectora< float3 >&, vectora< u16 >&, math::float4x4, float const*, u32, u16 const*, u32 ) {}

} // namespace debug
} // namespace render
} // namespace xray
