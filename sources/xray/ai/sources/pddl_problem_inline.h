////////////////////////////////////////////////////////////////////////////
//	Created		: 22.07.2011
//	Author		: Tetyana Meleshchenko
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////

#ifndef PDDL_PROBLEM_INLINE_H_INCLUDED
#define PDDL_PROBLEM_INLINE_H_INCLUDED

namespace xray {
namespace ai {
namespace planning {

inline pddl_problem::pddl_problem	( pddl_domain const& domain ) :
	m_domain						( domain )
{
}

inline bool pddl_problem::has_action_instance	( u32 const action_type ) const
{
	return get_action_instance( action_type )	!= 0;
}

inline void initialize_binder( action_instance const& action, property_values_type const& values )
{
	R_ASSERT_CMP_U	( values.size(), ==, 0 );
	( *horrible_cast< pcvoid, std::function< void ( ) > const* >( &action.initialize_storage() ).second )();
}

template < typename T1, typename T2 >
inline void initialize_binder( action_instance const& action, property_values_type const& values )
{
	R_ASSERT_CMP	( values.size(), ==, 1 );
	( *horrible_cast< pcvoid, std::function< void ( T2 ) > const* >( &action.initialize_storage() ).second )(
		static_cast_checked< T2 >( horrible_cast< pcvoid, T1 >( values[0] ).second )
	);
}

template < typename T1, typename T2, typename T3, typename T4 >
inline void initialize_binder( action_instance const& action, property_values_type const& values )
{
	R_ASSERT_CMP	( values.size(), ==, 2 );
	( *horrible_cast< pcvoid, std::function< void ( T3, T4 ) > const* >( &action.initialize_storage() ).second )(
		static_cast_checked< T3 >( horrible_cast< pcvoid, T1 >( values[0] ).second ),
		static_cast_checked< T4 >( horrible_cast< pcvoid, T2 >( values[1] ).second )
	);
}

template < typename T1, typename T2, typename T3, typename T4, typename T5, typename T6 >
inline void initialize_binder( action_instance const& action, property_values_type const& values )
{
	R_ASSERT_CMP	( values.size(), ==, 3 );
	( *horrible_cast< pcvoid, std::function< void ( T4, T5, T6 ) > const* >( &action.initialize_storage() ).second )(
		static_cast_checked< T4 >( horrible_cast< pcvoid, T1 >( values[0] ).second ),
		static_cast_checked< T5 >( horrible_cast< pcvoid, T2 >( values[1] ).second ),
		static_cast_checked< T6 >( horrible_cast< pcvoid, T3 >( values[2] ).second )
	);
}

inline void execute_binder( action_instance const& action, property_values_type const& values )
{
	R_ASSERT_CMP_U	( values.size(), ==, 0 );
	( *horrible_cast< pcvoid, std::function< void ( ) > const* >( &action.execute_storage() ).second )();
}

template < typename T1, typename T2 >
inline void execute_binder( action_instance const& action, property_values_type const& values )
{
	R_ASSERT_CMP	( values.size(), ==, 1 );
	( *horrible_cast< pcvoid, std::function< void ( T2 ) > const* >( &action.execute_storage() ).second )(
		static_cast_checked< T2 >( horrible_cast< pcvoid, T1 >( values[0] ).second )
	);
}

template < typename T1, typename T2, typename T3, typename T4 >
inline void execute_binder( action_instance const& action, property_values_type const& values )
{
	R_ASSERT_CMP	( values.size(), ==, 2 );
	( *horrible_cast< pcvoid, std::function< void ( T3, T4 ) > const* >( &action.execute_storage() ).second )(
		static_cast_checked< T3 >( horrible_cast< pcvoid, T1 >( values[0] ).second ),
		static_cast_checked< T4 >( horrible_cast< pcvoid, T2 >( values[1] ).second )
	);
}

template < typename T1, typename T2, typename T3, typename T4, typename T5, typename T6 >
inline void execute_binder( action_instance const& action, property_values_type const& values )
{
	R_ASSERT_CMP	( values.size(), ==, 3 );
	( *horrible_cast< pcvoid, std::function< void ( T4, T5, T6 ) > const* >( &action.execute_storage() ).second )(
		static_cast_checked< T4 >( horrible_cast< pcvoid, T1 >( values[0] ).second ),
		static_cast_checked< T5 >( horrible_cast< pcvoid, T2 >( values[1] ).second ),
		static_cast_checked< T6 >( horrible_cast< pcvoid, T3 >( values[2] ).second )
	);
}

inline void finalize_binder( action_instance const& action, property_values_type const& values )
{
	R_ASSERT_CMP_U	( values.size(), ==, 0 );
	( *horrible_cast< pcvoid, std::function< void ( ) > const* >( &action.finalize_storage() ).second )();
}

template < typename T1, typename T2 >
inline void finalize_binder( action_instance const& action, property_values_type const& values )
{
	R_ASSERT_CMP	( values.size(), ==, 1 );
	( *horrible_cast< pcvoid, std::function< void ( T2 ) > const* >( &action.finalize_storage() ).second )(
		static_cast_checked< T2 >( horrible_cast< pcvoid, T1 >( values[0] ).second )
	);
}

template < typename T1, typename T2, typename T3, typename T4 >
inline void finalize_binder( action_instance const& action, property_values_type const& values )
{
	R_ASSERT_CMP	( values.size(), ==, 2 );
	( *horrible_cast< pcvoid, std::function< void ( T3, T4 ) > const* >( &action.finalize_storage() ).second )(
		static_cast_checked< T3 >( horrible_cast< pcvoid, T1 >( values[0] ).second ),
		static_cast_checked< T4 >( horrible_cast< pcvoid, T2 >( values[1] ).second )
	);
}

template < typename T1, typename T2, typename T3, typename T4, typename T5, typename T6 >
inline void finalize_binder( action_instance const& action, property_values_type const& values )
{
	R_ASSERT_CMP	( values.size(), ==, 3 );
	( *horrible_cast< pcvoid, std::function< void ( T4, T5, T6 ) > const* >( &action.finalize_storage() ).second )(
		static_cast_checked< T4 >( horrible_cast< pcvoid, T1 >( values[0] ).second ),
		static_cast_checked< T5 >( horrible_cast< pcvoid, T2 >( values[1] ).second ),
		static_cast_checked< T6 >( horrible_cast< pcvoid, T3 >( values[2] ).second )
	);
}

inline void pddl_problem::add_action_instance	(
		action_instance& result,
		std::function< void ( ) > const& initializer,
		std::function< void ( ) > const& executor,
		std::function< void ( ) > const& finalizer
	)
{
	R_ASSERT						( !has_action_instance( result.get_type() ),
									  "attempt to add action instance of the same type as already added: %d", result.get_type()
									);

	if ( initializer )
	{
		result.m_initialize_storage.~function< void () >();
		new( &result.m_initialize_storage )std::function< void ( ) >( initializer );
		result.m_initialize_binder	= &initialize_binder;
	}
	
	if ( executor )
	{
		result.m_execute_storage.~function< void () >();
		new( &result.m_execute_storage )std::function< void ( ) >( executor );
		result.m_execute_binder		= &execute_binder;
	}
	
	if ( finalizer )
	{
		result.m_finalize_storage.~function< void () >();
		new( &result.m_finalize_storage )std::function< void ( ) >( finalizer );
		result.m_finalize_binder	= &finalize_binder;
	}
	
	m_action_instances.push_back	( &result );
}

template < typename T1, typename T2 >
inline void pddl_problem::add_action_instance1	(
		action_instance& result,
		std::function< void ( T2 ) > const& initializer,
		std::function< void ( T2 ) > const& executor,
		std::function< void ( T2 ) > const& finalizer
	)
{
	R_ASSERT						( !has_action_instance( result.get_type() ),
									  "attempt to add action instance of the same type as already added: %d", result.get_type()
									);

	if ( initializer )
	{
		result.m_initialize_storage.~function< void () >();
		new( &result.m_initialize_storage )std::function< void ( T2 ) >( initializer );
		result.m_initialize_binder	= &initialize_binder< T1, T2 >;
	}
	
	if ( executor )
	{
		result.m_execute_storage.~function< void () >();
		new( &result.m_execute_storage )std::function< void ( T2 ) >( executor );
		result.m_execute_binder		= &execute_binder< T1, T2 >;
	}

	if ( finalizer )
	{
		result.m_finalize_storage.~function< void () >();
		new( &result.m_finalize_storage )std::function< void ( T2 ) >( finalizer );
		result.m_finalize_binder	= &finalize_binder< T1, T2 >;
	}
	
	m_action_instances.push_back	( &result );
}

template < typename T >
inline void pddl_problem::add_action_instance	(
		action_instance& result,
		std::function< void ( T ) > const& initializer,
		std::function< void ( T ) > const& executor,
		std::function< void ( T ) > const& finalizer
	)
{
	add_action_instance1< T, T >	( result, initializer, executor, finalizer );
}

template < typename T1, typename T2, typename T3, typename T4 >
inline void pddl_problem::add_action_instance2	(
		action_instance& result,
		std::function< void ( T3, T4 ) > const& initializer,
		std::function< void ( T3, T4 ) > const& executor,
		std::function< void ( T3, T4 ) > const& finalizer
	)
{
	R_ASSERT						( !has_action_instance( result.get_type() ),
									  "attempt to add action instance of the same type as already added: %d", result.get_type()
									);
	
	if ( initializer )
	{
		result.m_initialize_storage.~function< void () >();
		new( &result.m_initialize_storage )std::function< void ( T3, T4 ) >( initializer );
		result.m_initialize_binder	= &initialize_binder< T1, T2, T3, T4 >;
	}
	
	if ( executor )
	{
		result.m_execute_storage.~function< void () >();
		new( &result.m_execute_storage )std::function< void ( T3, T4 ) >( executor );
		result.m_execute_binder		= &execute_binder< T1, T2, T3, T4 >;
	}

	if ( finalizer )
	{
		result.m_finalize_storage.~function< void () >();
		new( &result.m_finalize_storage )std::function< void ( T3, T4 ) >( finalizer );
		result.m_finalize_binder	= &finalize_binder< T1, T2, T3, T4 >;
	}

	m_action_instances.push_back	( &result );
}

template < typename T1, typename T2 >
inline void pddl_problem::add_action_instance	(
		action_instance& result,
		std::function< void ( T1, T2 ) > const& initializer,
		std::function< void ( T1, T2 ) > const& executor,
		std::function< void ( T1, T2 ) > const& finalizer
	)
{
	add_action_instance2< T1, T2, T1, T2 >		( result, initializer, executor, finalizer );
}

template < typename T1, typename T2, typename T3, typename T4, typename T5, typename T6 >
inline void pddl_problem::add_action_instance3	(
		action_instance& result,
		std::function< void ( T4, T5, T6 ) > const& initializer,
		std::function< void ( T4, T5, T6 ) > const& executor,
		std::function< void ( T4, T5, T6 ) > const& finalizer
	)
{
	R_ASSERT						( !has_action_instance( result.get_type() ),
									  "attempt to add action instance of the same type as already added: %d", result.get_type()
									);

	if ( initializer )
	{
		result.m_initialize_storage.~function< void () >();
		new( &result.m_initialize_storage )std::function< void ( T4, T5, T6 ) >( initializer );
		result.m_initialize_binder	= &initialize_binder< T1, T2, T3, T4, T5, T6 >;
	}
	
	if ( executor )
	{
		result.m_execute_storage.~function< void () >();
		new( &result.m_execute_storage )std::function< void ( T4, T5, T6 ) >( executor );
		result.m_execute_binder		= &execute_binder< T1, T2, T3, T4, T5, T6 >;
	}

	if ( finalizer )
	{
		result.m_finalize_storage.~function< void () >();
		new( &result.m_finalize_storage )std::function< void ( T4, T5, T6 ) >( finalizer );
		result.m_finalize_binder	= &finalize_binder< T1, T2, T3, T4, T5, T6 >;
	}

	m_action_instances.push_back	( &result );
}

template < typename T1, typename T2, typename T3 >
inline void pddl_problem::add_action_instance	(
		action_instance& result,
		std::function< void ( T1, T2, T3 ) > const& initializer,
		std::function< void ( T1, T2, T3 ) > const& executor,
		std::function< void ( T1, T2, T3 ) > const& finalizer
	)
{
	add_action_instance3< T1, T2, T3, T1, T2, T3 >( result, initializer, executor, finalizer );
}

inline void pddl_problem::add_action_instance	(
		action_instance& result,
		void ( *initializer )( ),
		void ( *executor )( ),
		void ( *finalizer )( )
	)
{
	add_action_instance	(
		result,
		std::function< void ( ) >( initializer ),
		std::function< void ( ) >( executor ),
		std::function< void ( ) >( finalizer )
	);
}

template < typename T >
inline void pddl_problem::add_action_instance	(
		action_instance& result,
		void ( *initializer )( T ),
		void ( *executor )( T ),
		void ( *finalizer )( T )
	)
{
	add_action_instance	(
		result,
		std::function< void ( T ) >( initializer ),
		std::function< void ( T ) >( executor ),
		std::function< void ( T ) >( finalizer )
	);
}

template < typename T1, typename T2 >
inline void pddl_problem::add_action_instance1	(
		action_instance& result,
		void ( *initializer )( T2 ),
		void ( *executor )( T2 ),
		void ( *finalizer )( T2 )
	)
{
	add_action_instance< T1 >					(
		result,
		std::function< void ( T2 ) >( initializer ),
		std::function< void ( T2 ) >( executor ),
		std::function< void ( T2 ) >( finalizer )
	);
}

template < typename T1, typename T2 >
inline void pddl_problem::add_action_instance	(
		action_instance& result,
		void ( *initializer )( T1, T2 ),
		void ( *executor )( T1, T2 ),
		void ( *finalizer )( T1, T2 )
	)
{
	add_action_instance< T1, T2 >				(
		result,
		std::function< bool ( T1, T2 ) >( initializer ),
		std::function< void ( T1, T2 ) >( executor ),
		std::function< void ( T1, T2 ) >( finalizer )
	);
}

template < typename T1, typename T2, typename T3 >
inline void pddl_problem::add_action_instance	(
		action_instance& result,
		void ( *initializer )( T1, T2, T3 ),
		void ( *executor )( T1, T2, T3 ),
		void ( *finalizer )( T1, T2, T3 )
	)
{
	add_action_instance< T1, T2, T3 >			(
		result,
		std::function< void ( T1, T2, T3 ) >( initializer ),
		std::function< void ( T1, T2, T3 ) >( executor ),
		std::function< void ( T1, T2, T3 ) >( finalizer )
	);
}

template < typename T1, typename T2, typename T3, typename T4 >
inline void pddl_problem::add_action_instance2	(
		action_instance& result,
		void ( *initializer )( T3, T4 ),
		void ( *executor )( T3, T4 ),
		void ( *finalizer )( T3, T4 )
	)
{
	add_action_instance< T1, T2, T3, T4 >		(
		result,
		std::function< void ( T3, T4 ) >( initializer ),
		executor,
		finalizer
	);
}

template < typename T1, typename T2, typename T3, typename T4, typename T5, typename T6 >
inline void pddl_problem::add_action_instance3	(
		action_instance& result,
		void ( *initializer )( T4, T5, T6 ),
		void ( *executor )( T4, T5, T6 ),
		void ( *finalizer )( T4, T5, T6 )
	)
{
	add_action_instance< T1, T2, T3, T4, T5, T6 >(
		result,
		std::function< void ( T4, T5, T6 ) >( initializer ),
		executor,
		finalizer
	);
}

} // namespace planning
} // namespace ai
} // namespace xray

#endif // #ifndef PDDL_PROBLEM_INLINE_H_INCLUDED
