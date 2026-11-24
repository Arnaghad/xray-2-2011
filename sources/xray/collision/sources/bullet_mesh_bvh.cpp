////////////////////////////////////////////////////////////////////////////
//	Stage 05 Modernization: Bullet btOptimizedBvh mesh query backend
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include "bullet_mesh_bvh.h"

#include <xray/collision/ray_collision_utils.h>
#include <xray/math_convex.h>

#include <BulletCollision/BroadphaseCollision/btQuantizedBvh.h>
#include <BulletCollision/CollisionShapes/btOptimizedBvh.h>
#include <BulletCollision/CollisionShapes/btTriangleIndexVertexArray.h>

#include <algorithm>
#include <cmath>
#include <limits>

namespace xray {
namespace collision {

namespace {

inline btVector3 to_bt( float3 const& v )
{
	return btVector3( v.x, v.y, v.z );
}

inline math::aabb unquantize_node_aabb( btOptimizedBvh const& bvh, btQuantizedBvhNode const& node )
{
	btVector3 const mn = bvh.unQuantize( node.m_quantizedAabbMin );
	btVector3 const mx = bvh.unQuantize( node.m_quantizedAabbMax );
	return math::create_aabb_min_max(
		float3( mn.x(), mn.y(), mn.z() ),
		float3( mx.x(), mx.y(), mx.z() )
	);
}

inline float compute_clamped_ray_length(
	math::aabb const& mesh_aabb,
	float3 const& origin,
	float3 const& direction,
	float max_distance
)
{
	if ( !( max_distance > 0.f ) || !std::isfinite( max_distance ) )
		return 0.f;

	float const dir_len = direction.length();
	if ( !( dir_len > 0.f ) || !std::isfinite( dir_len ) )
		return 0.f;

	float const mesh_bound = ( mesh_aabb.center() - origin ).length() + mesh_aabb.extents().length() + 1.f;
	float const scaled_max = max_distance * dir_len;
	if ( scaled_max <= mesh_bound )
		return max_distance;
	return mesh_bound / dir_len;
}

template <typename VolumeType>
bool query_volume_triangles(
	btOptimizedBvh* bvh,
	VolumeType const& volume,
	object const* obj,
	triangles_type& triangles
)
{
	if ( !bvh )
		return false;

	QuantizedNodeArray const& nodes = bvh->getQuantizedNodeArray();
	int const node_count = nodes.size();
	if ( node_count <= 0 )
		return false;

	u32 const size_before = triangles.size();
	int cur_index = 0;
	while ( cur_index < node_count ) {
		btQuantizedBvhNode const& node = nodes[ cur_index ];
		math::aabb const node_aabb = unquantize_node_aabb( *bvh, node );
		math::intersection const isect = volume.test_inexact( node_aabb );

		if ( isect == math::intersection_outside ) {
			cur_index += node.isLeafNode() ? 1 : node.getEscapeIndex();
			continue;
		}

		if ( node.isLeafNode() ) {
			triangles.push_back( triangle_result( obj, static_cast<u32>( node.getTriangleIndex() ) ) );
			++cur_index;
			continue;
		}

		if ( isect == math::intersection_inside ) {
			int const end_index = std::min( cur_index + node.getEscapeIndex(), node_count );
			for ( ++cur_index; cur_index < end_index; ++cur_index ) {
				btQuantizedBvhNode const& child = nodes[ cur_index ];
				if ( child.isLeafNode() )
					triangles.push_back( triangle_result( obj, static_cast<u32>( child.getTriangleIndex() ) ) );
			}
			continue;
		}

		++cur_index;
	}

	return triangles.size() > size_before;
}

} // namespace

bullet_mesh_bvh::bullet_mesh_bvh( ) :
	m_vertices		( nullptr ),
	m_vertex_count	( 0 ),
	m_indices		( nullptr ),
	m_index_count	( 0 ),
	m_aabb			( math::create_zero_aabb() )
{
}

bullet_mesh_bvh::~bullet_mesh_bvh( )
{
	reset();
}

void bullet_mesh_bvh::reset( )
{
	// Destroy BVH before the mesh interface it references.
	m_bvh.reset();
	m_mesh_interface.reset();
	m_vertices		= nullptr;
	m_vertex_count	= 0;
	m_indices		= nullptr;
	m_index_count	= 0;
	m_aabb			= math::create_zero_aabb();
}

void bullet_mesh_bvh::initialize(
	float3 const* const vertices,
	u32 const vertex_count,
	u32 const* const indices,
	u32 const index_count
)
{
	reset();
	ASSERT( ( index_count % 3 ) == 0 );

	m_vertices		= vertices;
	m_vertex_count	= vertex_count;
	m_indices		= indices;
	m_index_count	= index_count;

	if ( vertex_count == 0 || index_count < 3 || !vertices || !indices ) {
		m_aabb = math::create_zero_aabb();
		return;
	}

	float3 mn = vertices[ 0 ];
	float3 mx = vertices[ 0 ];
	for ( u32 i = 1; i < vertex_count; ++i ) {
		mn = math::min( mn, vertices[ i ] );
		mx = math::max( mx, vertices[ i ] );
	}
	m_aabb = math::create_aabb_min_max( mn, mx );

	static_assert( sizeof( float3 ) == 3 * sizeof( float ) );
	static_assert( sizeof( u32 ) == sizeof( int ) );

	m_mesh_interface = std::make_unique<btTriangleIndexVertexArray>();
	btIndexedMesh indexed_mesh;
	indexed_mesh.m_numTriangles			= static_cast<int>( index_count / 3 );
	indexed_mesh.m_triangleIndexBase	= reinterpret_cast<unsigned char const*>( indices );
	indexed_mesh.m_triangleIndexStride	= static_cast<int>( 3 * sizeof( u32 ) );
	indexed_mesh.m_numVertices			= static_cast<int>( vertex_count );
	indexed_mesh.m_vertexBase			= reinterpret_cast<unsigned char const*>( vertices );
	indexed_mesh.m_vertexStride			= static_cast<int>( sizeof( float3 ) );
	indexed_mesh.m_indexType			= PHY_INTEGER;
	indexed_mesh.m_vertexType			= PHY_FLOAT;
	m_mesh_interface->addIndexedMesh( indexed_mesh, PHY_INTEGER );

	// Pad AABB slightly so planar/coplanar meshes never trigger zero-extent division during quantization.
	btVector3 const pad( 1e-4f, 1e-4f, 1e-4f );
	m_bvh = std::make_unique<btOptimizedBvh>();
	m_bvh->build( m_mesh_interface.get(), true, to_bt( mn ) - pad, to_bt( mx ) + pad );
}

void bullet_mesh_bvh::update_pointers( float3 const* const vertices, u32 const* const indices )
{
	m_vertices	= vertices;
	m_indices	= indices;
	if ( m_mesh_interface && m_mesh_interface->getNumSubParts() > 0 ) {
		IndexedMeshArray& meshes = m_mesh_interface->getIndexedMeshArray();
		meshes[ 0 ].m_vertexBase		= reinterpret_cast<unsigned char const*>( vertices );
		meshes[ 0 ].m_triangleIndexBase	= reinterpret_cast<unsigned char const*>( indices );
	}
}

bool bullet_mesh_bvh::aabb_query(
	object const* const obj,
	math::aabb const& query_aabb,
	triangles_type& triangles
) const
{
	if ( !m_bvh || !m_vertices || !m_indices || m_index_count < 3 )
		return false;

	struct aabb_callback final : btNodeOverlapCallback {
		bullet_mesh_bvh const&	bvh;
		object const*			obj;
		float3 const			center;
		float3 const			extents;
		triangles_type&			out;

		aabb_callback(
			bullet_mesh_bvh const& owner,
			object const* o,
			math::aabb const& box,
			triangles_type& dst
		) :
			bvh		( owner ),
			obj		( o ),
			center	( box.center() ),
			extents	( box.extents() ),
			out		( dst )
		{}

		void processNode( int /*subPart*/, int triangle_index ) override
		{
			u32 const tri = static_cast<u32>( triangle_index );
			u32 const* const idx = bvh.indices( tri );
			float3 const* const verts = bvh.vertices();
			float3 const tri_verts[3] = {
				verts[ idx[0] ],
				verts[ idx[1] ],
				verts[ idx[2] ],
			};
			if ( ::triBoxOverlap( center, extents, tri_verts ) != 0 )
				out.push_back( triangle_result( obj, tri ) );
		}
	};

	u32 const size_before = triangles.size();
	aabb_callback cb( *this, obj, query_aabb, triangles );
	m_bvh->reportAabbOverlappingNodex( &cb, to_bt( query_aabb.min ), to_bt( query_aabb.max ) );
	return triangles.size() > size_before;
}

bool bullet_mesh_bvh::cuboid_query(
	object const* const obj,
	math::cuboid const& query_cuboid,
	triangles_type& triangles
) const
{
	return query_volume_triangles( m_bvh.get(), query_cuboid, obj, triangles );
}

u32 bullet_mesh_bvh::convex_triangle_count( math::convex const& bounding_convex ) const
{
	if ( !m_bvh )
		return 0;

	QuantizedNodeArray const& nodes = m_bvh->getQuantizedNodeArray();
	int const node_count = nodes.size();
	u32 count = 0;
	int cur_index = 0;
	while ( cur_index < node_count ) {
		btQuantizedBvhNode const& node = nodes[ cur_index ];
		math::aabb const node_aabb = unquantize_node_aabb( *m_bvh, node );
		math::intersection const isect = bounding_convex.test_inexact( node_aabb );

		if ( isect == math::intersection_outside ) {
			cur_index += node.isLeafNode() ? 1 : node.getEscapeIndex();
			continue;
		}

		if ( node.isLeafNode() ) {
			++count;
			++cur_index;
			continue;
		}

		if ( isect == math::intersection_inside ) {
			int const end_index = std::min( cur_index + node.getEscapeIndex(), node_count );
			for ( ++cur_index; cur_index < end_index; ++cur_index ) {
				if ( nodes[ cur_index ].isLeafNode() )
					++count;
			}
			continue;
		}

		++cur_index;
	}
	return count;
}

bool bullet_mesh_bvh::ray_query(
	object const* const obj,
	float3 const& origin,
	float3 const& direction,
	float const max_distance,
	ray_triangles_type& triangles,
	triangles_predicate_type const& predicate
) const
{
	if ( !m_bvh || !m_vertices || !m_indices || m_index_count < 3 )
		return false;

	float const seg_len = compute_clamped_ray_length( m_aabb, origin, direction, max_distance );
	if ( !( seg_len > 0.f ) )
		return false;

	struct ray_query_cb final : btNodeOverlapCallback {
		bullet_mesh_bvh const&			bvh;
		object const*					obj;
		float3 const&					origin;
		float3 const&					direction;
		float							max_distance;
		ray_triangles_type&				out;
		triangles_predicate_type const&	predicate;

		ray_query_cb(
			bullet_mesh_bvh const& owner,
			object const* o,
			float3 const& org,
			float3 const& dir,
			float max_d,
			ray_triangles_type& dst,
			triangles_predicate_type const& pred
		) :
			bvh			( owner ),
			obj			( o ),
			origin		( org ),
			direction	( dir ),
			max_distance( max_d ),
			out			( dst ),
			predicate	( pred )
		{}

		void processNode( int /*subPart*/, int triangle_index ) override
		{
			u32 const tri = static_cast<u32>( triangle_index );
			u32 const* const idx = bvh.indices( tri );
			float3 const* const verts = bvh.vertices();
			float range = 0.f;
			if ( !collision::test_triangle(
				verts[ idx[0] ],
				verts[ idx[1] ],
				verts[ idx[2] ],
				origin,
				direction,
				max_distance,
				range
			) )
				return;

			ray_triangle_result const result( obj, tri, range );
			predicate( result );
			out.push_back( result );
		}
	};

	u32 const size_before = triangles.size();
	ray_query_cb cb( *this, obj, origin, direction, max_distance, triangles, predicate );
	btVector3 const from = to_bt( origin );
	btVector3 const to = to_bt( origin + direction * seg_len );
	m_bvh->reportRayOverlappingNodex( &cb, from, to );
	return triangles.size() > size_before;
}

bool bullet_mesh_bvh::ray_test(
	float3 const& origin,
	float3 const& direction,
	float const max_distance,
	triangles_predicate_type const& predicate
) const
{
	if ( !m_bvh || !m_vertices || !m_indices || m_index_count < 3 )
		return false;

	float const seg_len = compute_clamped_ray_length( m_aabb, origin, direction, max_distance );
	if ( !( seg_len > 0.f ) )
		return false;

	struct ray_test_cb final : btNodeOverlapCallback {
		bullet_mesh_bvh const&			bvh;
		float3 const&					origin;
		float3 const&					direction;
		float							max_distance;
		triangles_predicate_type const&	predicate;
		bool							stopped;

		ray_test_cb(
			bullet_mesh_bvh const& owner,
			float3 const& org,
			float3 const& dir,
			float max_d,
			triangles_predicate_type const& pred
		) :
			bvh			( owner ),
			origin		( org ),
			direction	( dir ),
			max_distance( max_d ),
			predicate	( pred ),
			stopped		( false )
		{}

		void processNode( int /*subPart*/, int triangle_index ) override
		{
			if ( stopped )
				return;

			u32 const tri = static_cast<u32>( triangle_index );
			u32 const* const idx = bvh.indices( tri );
			float3 const* const verts = bvh.vertices();
			float range = 0.f;
			if ( !collision::test_triangle(
				verts[ idx[0] ],
				verts[ idx[1] ],
				verts[ idx[2] ],
				origin,
				direction,
				max_distance,
				range
			) )
				return;

			if ( predicate( ray_triangle_result( nullptr, tri, range ) ) )
				stopped = true;
		}
	};

	ray_test_cb cb( *this, origin, direction, max_distance, predicate );
	btVector3 const from = to_bt( origin );
	btVector3 const to = to_bt( origin + direction * seg_len );
	m_bvh->reportRayOverlappingNodex( &cb, from, to );
	return cb.stopped;
}

} // namespace collision
} // namespace xray
