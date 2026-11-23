////////////////////////////////////////////////////////////////////////////
//	Module 		: sol_compat.h
//	Created 	: 01.05.2026
// Description : Engine script helpers implemented with sol2
////////////////////////////////////////////////////////////////////////////

#ifndef CS_SCRIPT_SOL_COMPAT_H_INCLUDED
#define CS_SCRIPT_SOL_COMPAT_H_INCLUDED

#ifndef XRAY_SOL_SYSTEM_LUA
# error Script consumers must link the sol2::sol2 CMake target
#endif

#include <sol/sol.hpp>

// ============================================================================
// Engine script helpers
// ============================================================================

namespace script_binding {

// --- object type alias ---
using object = sol::object;

// --- type query ---
inline int type(sol::object const& obj) {
	return static_cast<int>(obj.get_type());
}

// --- object_cast ---
template <typename T>
inline T object_cast(sol::object const& obj) {
	return obj.as<T>();
}

// --- globals ---
inline sol::table globals(lua_State* state) {
	return sol::state_view(state).globals();
}

// --- handle: construct object from stack ---
struct handle {
	lua_State* m_state;
	int m_index;
	handle(lua_State* state, int index) : m_state(state), m_index(index) {}
};

// Allow constructing sol::object from a script_binding::handle
inline sol::object make_object(handle const& h) {
	return sol::stack::get<sol::object>(h.m_state, h.m_index);
}

// --- from_stack ---
struct from_stack {
	lua_State* m_state;
	int m_index;
	from_stack(lua_State* state, int index) : m_state(state), m_index(index) {}
};

// --- iterator wrapper ---
// Wraps sol::table iteration to mimic script_binding::iterator
class iterator {
public:
	using sol_iter = sol::table::iterator;

	iterator() : m_table(nullptr), m_at_end(true) {}

	explicit iterator(sol::object const& obj)
		: m_table(nullptr), m_at_end(true)
	{
		if (obj.get_type() == sol::type::table) {
			m_table_storage = obj.as<sol::table>();
			m_table = &m_table_storage;
			m_iter = m_table_storage.begin();
			m_at_end = (m_iter == m_table_storage.end());
		}
	}

	iterator& operator++() {
		if (!m_at_end) {
			++m_iter;
			m_at_end = (m_iter == m_table->end());
		}
		return *this;
	}

	sol::object operator*() const {
		return (*m_iter).second;
	}

	sol::object key() const {
		return (*m_iter).first;
	}

	bool operator!=(iterator const& other) const {
		if (m_at_end && other.m_at_end) return false;
		if (m_at_end != other.m_at_end) return true;
		return true; // different positions
	}

	bool operator==(iterator const& other) const {
		return !(*this != other);
	}

private:
	sol::table   m_table_storage;
	sol::table*  m_table;
	sol_iter     m_iter;
	bool         m_at_end;
};

// --- memory allocation types ---
typedef void* memory_allocation_function_parameter;
typedef void* (CS_CALL *memory_allocation_function_pointer)(memory_allocation_function_parameter parameter, void const*, size_t);

// --- global allocator variables ---
extern memory_allocation_function_pointer  allocator;
extern memory_allocation_function_parameter allocator_parameter;

// --- pcall wrapper ---
namespace detail {

inline int pcall(lua_State* L, int nargs, int nresults) {
	// Simple pcall - the pcall callback is handled via lua_atpanic / sol2 error handling
	return lua_pcall(L, nargs, nresults, 0);
}

// stack_pop helper (RAII stack cleanup)
struct stack_pop {
	lua_State* m_state;
	int m_count;
	stack_pop(lua_State* state, int count) : m_state(state), m_count(count) {}
	~stack_pop() { lua_pop(m_state, m_count); }
};

} // namespace detail

// --- string type ---
using string = std::string;

// --- vector/map types (previously with custom allocator) ---
template <typename T>
using vector = std::vector<T>;

template <typename K, typename V, typename P = std::less<K>>
using map = std::map<K, V, P>;

} // namespace script_binding

#endif // #ifndef CS_SCRIPT_SOL_COMPAT_H_INCLUDED
