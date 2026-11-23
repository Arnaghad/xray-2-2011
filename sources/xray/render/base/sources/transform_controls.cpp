////////////////////////////////////////////////////////////////////////////
//	Created 	: 27.01.2009
//	Author		: Armen Abroyan
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include "transform_controls.h"
#include <xray/render/base/editor_renderer.h>
#include <xray/render/base/platform.h>
#include "debug_renderer.h"
#include <xray/geometry_utils.h>

namespace xray {
namespace render {
namespace editor {
namespace detail {

namespace math = xray::math;







static void create_torus_colored( debug::debug_renderer::debug_vertices_type& out_vertices, debug::debug_renderer::debug_indices_type& out_indices, float outer_radius, float inner_radius, u16 outer_segments, u16 inner_segments, u32 color )
{
	geometry_utils::geom_vertices_type vertices( out_vertices.get_allocator() );
	geometry_utils::geom_indices_type indices( out_indices.get_allocator() );

	geometry_utils::create_torus( vertices, indices, math::float4x4().identity(), outer_radius, inner_radius, outer_segments, inner_segments );

	out_vertices.resize( vertices.size() );
	for ( u32 i = 0; i < vertices.size(); ++i )
	{
		out_vertices[i].position = vertices[i];
		out_vertices[i].color = color;
	}

	out_indices.resize( indices.size() );
	for ( u32 i = 0; i < indices.size(); ++i )
	{
		out_indices[i] = indices[i];
	}
}

} // namespace detail
} // namespace editor
} // namespace render
} // namespace xray
