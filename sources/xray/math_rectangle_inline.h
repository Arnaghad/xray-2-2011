////////////////////////////////////////////////////////////////////////////
//	Created 	: 23.10.2008
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#ifndef XRAY_MATH_RECTANGLE_INLINE_H_INCLUDED
#define XRAY_MATH_RECTANGLE_INLINE_H_INCLUDED

#define TEMPLATE_SIGNATURE	template < typename vector_type >
#define RECTANGLE			rectangle< vector_type >
#define RECTANGLE_POD		rectangle_pod< vector_type >

namespace xray {
namespace math {

TEMPLATE_SIGNATURE
inline RECTANGLE::rectangle								( )
{
}

TEMPLATE_SIGNATURE
inline RECTANGLE::rectangle								( vector_type const& _min, vector_type const& _max )
{
	this->min		= _min;
	this->max		= _max;
}

TEMPLATE_SIGNATURE
inline RECTANGLE& RECTANGLE_POD::zero						( )
{
	this->min = this->max = vector_type ( type(0), type(0), type(0) );
	return		( *this );
}

TEMPLATE_SIGNATURE
inline RECTANGLE& RECTANGLE_POD::invalidate					( )
{
	this->min = vector_type ( +infinity, +infinity, +infinity );
	this->max = vector_type ( -infinity, -infinity, -infinity );

	return		( *this );
}

TEMPLATE_SIGNATURE
inline RECTANGLE& RECTANGLE_POD::operator +=				( type value )
{
	this->min -= value;
	this->max += value;

	return		( *this );
}

TEMPLATE_SIGNATURE
inline RECTANGLE& RECTANGLE_POD::operator +=				( vector_type const& value )
{
	this->min -= value;
	this->max += value;

	return		( *this );
}

TEMPLATE_SIGNATURE
inline RECTANGLE& RECTANGLE_POD::operator -=				( type value )
{
	this->min += value;
	this->max -= value;

	return		( *this );
}

TEMPLATE_SIGNATURE
inline RECTANGLE& RECTANGLE_POD::operator -=				( vector_type const& value )
{
	this->min += value;
	this->max -= value;

	return		( *this );
}

TEMPLATE_SIGNATURE
inline RECTANGLE& RECTANGLE_POD::operator *=				( type value )
{
	vector_type		extents, center = this->center( extents );
	extents		*= value;
	this->min = center - extents;
	this->max = center + extents;

	return		( *this );
}

TEMPLATE_SIGNATURE
inline RECTANGLE& RECTANGLE_POD::operator *=				( vector_type const& value )
{
	vector_type		extents, center = this->center( extents );
	extents		*= value;
	this->min = center - extents;
	this->max = center + extents;

	return		( *this );
}

TEMPLATE_SIGNATURE
inline RECTANGLE& RECTANGLE_POD::operator /=				( type value )
{
	vector_type		extents, center = this->center( extents );
	extents		/= value;
	this->min = center - extents;
	this->max = center + extents;

	return		( *this );
}

TEMPLATE_SIGNATURE
inline RECTANGLE& RECTANGLE_POD::operator /=				( vector_type const& value )
{
	vector_type		extents, center = this->center( extents );
	extents		/= value;
	this->min = center - extents;
	this->max = center + extents;

	return		( *this );
}

TEMPLATE_SIGNATURE
inline RECTANGLE& RECTANGLE_POD::move						( vector_type const& value )
{
	this->min += value;
	this->max += value;

	return		( *this );
}

TEMPLATE_SIGNATURE
inline RECTANGLE& RECTANGLE_POD::modify						( vector_type const& position )
{
	this->min = math::min( this->min, position );
	this->max = math::max( this->max, position );

	return		( *this );
}

TEMPLATE_SIGNATURE
inline RECTANGLE& RECTANGLE_POD::modify						( rectangle_pod const& other )
{
	modify		( other.min );
	modify		( other.max );
}

TEMPLATE_SIGNATURE
inline vector_type RECTANGLE_POD::size						( ) const
{
	return		( this->max - this->min );
}

TEMPLATE_SIGNATURE
inline typename RECTANGLE_POD::type RECTANGLE_POD::radius		( ) const
{
	return		( size( ).length( )/type(2) );
}

TEMPLATE_SIGNATURE
inline typename RECTANGLE_POD::type RECTANGLE_POD::volume		( ) const
{
	vector_type const size = this->size( );
	return		( size.x*size.y*size.z );
}

TEMPLATE_SIGNATURE
inline vector_type RECTANGLE_POD::center					( ) const
{
	return		( ( this->min + this->max )/type(2) );
}

TEMPLATE_SIGNATURE
inline vector_type RECTANGLE_POD::center					( vector_type& size ) const
{
	size		= ( this->min - this->max )/type(2);
	return		( this->min + size );
}

TEMPLATE_SIGNATURE
inline bool	RECTANGLE_POD::intersect						( rectangle_pod const& other ) const
{
	return		( ( this->max >= other.min ) && ( this->min <= this->max ) );
}

TEMPLATE_SIGNATURE
inline bool	RECTANGLE_POD::contains							( vector_type const& position ) const
{
	return		( ( this->min <= position ) && ( this->max >= position ) );
}

TEMPLATE_SIGNATURE
inline bool	RECTANGLE_POD::contains							( rectangle_pod const& other ) const
{
	return		( ( this->min <= other.min ) && ( this->max >= other.max  ) );
}

TEMPLATE_SIGNATURE
inline bool	RECTANGLE_POD::similar							( rectangle_pod const& other ) const
{
	return		( this->min.is_similar( other.min ) && this->max.is_similar( other.max ) );
}

TEMPLATE_SIGNATURE
inline vector_type RECTANGLE_POD::vertex					( u32 index ) const
{
	ASSERT		( index < 4 );

	switch ( index ) {
		case 0 : return( vector_type ( this->min.x, this->min.y ) );
		case 1 : return( vector_type ( this->min.x, this->max.y ) );
		case 2 : return( vector_type ( this->max.x, this->min.y ) );
		case 3 : return( vector_type ( this->max.x, this->max.y ) );
		default: NODEFAULT(return vector_type( ));
	}
}

TEMPLATE_SIGNATURE
inline void	RECTANGLE_POD::vertices							( vector_type ( &result )[ 4 ] ) const
{
	result[ 0 ]	= vector_type ( this->min.x, this->min.y );
	result[ 1 ]	= vector_type ( this->min.x, this->max.y );
	result[ 2 ]	= vector_type ( this->max.x, this->min.y );
	result[ 3 ]	= vector_type ( this->max.x, this->max.y );
}

TEMPLATE_SIGNATURE
inline bool	RECTANGLE_POD::valid			( ) const
{
	return		( this->min.valid( ) && this->max.valid( ) );
}

TEMPLATE_SIGNATURE
inline typename RECTANGLE_POD::type RECTANGLE_POD::width		( ) const
{
	ASSERT		( this->max.x >= this->min.x );
	return		( this->max.x - this->min.x );
}

TEMPLATE_SIGNATURE
inline typename RECTANGLE_POD::type RECTANGLE_POD::height		( ) const
{
	ASSERT		( this->max.y >= this->min.y );
	return		( this->max.y - this->min.y );
}

#undef RECTANGLE
#undef TEMPLATE_SIGNATURE

#define TEMPLATE_SIGNATURE	template < typename vector_type >

TEMPLATE_SIGNATURE
inline math::rectangle<vector_type>	operator +						( math::rectangle<vector_type> const& rectangle, typename math::rectangle<vector_type>::type value )
{
	math::rectangle<vector_type>	result;
	result.min = rectangle.min - value;
	result.max = rectangle.max + value;
	return		( result );
}

TEMPLATE_SIGNATURE
inline math::rectangle<vector_type>	operator +						( typename math::rectangle<vector_type>::type value, math::rectangle<vector_type> const& rectangle )
{
	math::rectangle<vector_type>	result;
	result.min = rectangle.min - value;
	result.max = rectangle.max + value;
	return		( result );
}

TEMPLATE_SIGNATURE
inline math::rectangle<vector_type>	operator +						( math::rectangle<vector_type> const& rectangle, typename math::rectangle<vector_type>::vector_type const& value )
{
	math::rectangle<vector_type>	result;
	result.min = rectangle.min - value;
	result.max = rectangle.max + value;
	return		( result );
}

TEMPLATE_SIGNATURE
inline math::rectangle<vector_type>	operator +						( typename math::rectangle<vector_type>::vector_type const& value, math::rectangle<vector_type> const& rectangle )
{
	math::rectangle<vector_type>	result;
	result.min = rectangle.min - value;
	result.max = rectangle.max + value;
	return		( result );
}

TEMPLATE_SIGNATURE
inline math::rectangle<vector_type>	operator -						( math::rectangle<vector_type> const& rectangle, typename math::rectangle<vector_type>::type value )
{
	math::rectangle<vector_type>	result;
	result.min = rectangle.min + value;
	result.max = rectangle.max - value;
	return		( result );
}

TEMPLATE_SIGNATURE
inline math::rectangle<vector_type>	operator -						( typename math::rectangle<vector_type>::type value, math::rectangle<vector_type> const& rectangle )
{
	math::rectangle<vector_type>	result;
	result.min = rectangle.min + value;
	result.max = rectangle.max - value;
	return		( result );
}

TEMPLATE_SIGNATURE
inline math::rectangle<vector_type>	operator -						( math::rectangle<vector_type> const& rectangle, typename math::rectangle<vector_type>::vector_type const& value )
{
	math::rectangle<vector_type>	result;
	result.min = rectangle.min + value;
	result.max = rectangle.max - value;
	return		( result );
}

TEMPLATE_SIGNATURE
inline math::rectangle<vector_type>	operator -						( typename math::rectangle<vector_type>::vector_type const& value, math::rectangle<vector_type> const& rectangle )
{
	math::rectangle<vector_type>	result;
	result.min = rectangle.min + value;
	result.max = rectangle.max - value;
	return		( result );
}

TEMPLATE_SIGNATURE
inline math::rectangle<vector_type>	operator *						( math::rectangle<vector_type> const& rectangle, typename math::rectangle<vector_type>::type value )
{
	math::rectangle<vector_type>	result;
	result.min = rectangle.min * value;
	result.max = rectangle.max * value;
	return		( result );
}

TEMPLATE_SIGNATURE
inline math::rectangle<vector_type>	operator *						( typename math::rectangle<vector_type>::type value, math::rectangle<vector_type> const& rectangle )
{
	math::rectangle<vector_type>	result;
	result.min = rectangle.min * value;
	result.max = rectangle.max * value;
	return		( result );
}

TEMPLATE_SIGNATURE
inline math::rectangle<vector_type>	operator *						( math::rectangle<vector_type> const& rectangle, typename math::rectangle<vector_type>::vector_type const& value )
{
	math::rectangle<vector_type>	result;
	result.min = rectangle.min * value;
	result.max = rectangle.max * value;
	return		( result );
}

TEMPLATE_SIGNATURE
inline math::rectangle<vector_type>	operator *						( typename math::rectangle<vector_type>::vector_type const& value, math::rectangle<vector_type> const& rectangle )
{
	math::rectangle<vector_type>	result;
	result.min = rectangle.min * value;
	result.max = rectangle.max * value;
	return		( result );
}

TEMPLATE_SIGNATURE
inline math::rectangle<vector_type>	operator /						( math::rectangle<vector_type> const& rectangle, typename math::rectangle<vector_type>::type value )
{
	math::rectangle<vector_type>	result;
	result.min = rectangle.min / value;
	result.max = rectangle.max / value;
	return		( result );
}

TEMPLATE_SIGNATURE
inline math::rectangle<vector_type>	operator /						( typename math::rectangle<vector_type>::type value, math::rectangle<vector_type> const& rectangle )
{
	math::rectangle<vector_type>	result;
	result.min = rectangle.min / value;
	result.max = rectangle.max / value;
	return		( result );
}

TEMPLATE_SIGNATURE
inline math::rectangle<vector_type>	operator /						( math::rectangle<vector_type> const& rectangle, typename math::rectangle<vector_type>::vector_type const& value )
{
	math::rectangle<vector_type>	result;
	result.min = rectangle.min / value;
	result.max = rectangle.max / value;
	return		( result );
}

TEMPLATE_SIGNATURE
inline math::rectangle<vector_type>	operator /						( typename math::rectangle<vector_type>::vector_type const& value, math::rectangle<vector_type> const& rectangle )
{
	math::rectangle<vector_type>	result;
	result.min = rectangle.min / value;
	result.max = rectangle.max / value;
	return		( result );
}

} // namespace math
} // namespace xray

#undef TEMPLATE_SIGNATURE

#endif // #ifndef XRAY_MATH_RECTANGLE_INLINE_H_INCLUDED
