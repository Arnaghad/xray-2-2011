////////////////////////////////////////////////////////////////////////////
//	Created		: 10.03.2009
//	Author		: Mykhailo Parfeniuk
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

// no header guards, since header may be included into the
// different namespaces to support several allocators

template < typename T >
class deque : public ::std::deque< T, std_allocator< T > > {
public:
	typedef deque<T>								self_type;

private:
	typedef ::std::deque< T, std_allocator< T > >	super;

public:
	typedef typename super::size_type				size_type;
	typedef typename super::value_type				value_type;

public:
	inline						deque		( ) {}
	inline						deque		( size_type count, value_type const& value ) : super(count, value) {}
	inline						deque		( self_type const& other) : super(other) {}
	template < typename input_iterator >
	inline						deque		( input_iterator const& first, input_iterator const& last ) : super(first, last) {}
	inline	self_type&			operator=	( self_type const& other ) { super::operator=(other); return *this; }
	inline	u32					size		( ) const { return (u32)super::size(); }
}; // class deque
