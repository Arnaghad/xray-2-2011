////////////////////////////////////////////////////////////////////////////
//	Created		: 12.08.2011
//	Author		: Tetyana Meleshchenko
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////

#ifndef PDDL_PREDICATE_LEXEME_INLINE_H_INCLUDED
#define PDDL_PREDICATE_LEXEME_INLINE_H_INCLUDED

namespace xray {
namespace ai {
namespace planning {

inline pddl_predicate_lexeme::pddl_predicate_lexeme	( pddl_predicate const* required_predicate ) :
	base_lexeme										( false ),
	m_predicate										( required_predicate ),
	m_value											( true ),
	m_destroy_manually								( false )
{
}

// operators moved to base_lexeme_inline.h

} // namespace planning
} // namespace ai
} // namespace xray

#endif // #ifndef PDDL_PREDICATE_LEXEME_INLINE_H_INCLUDED
