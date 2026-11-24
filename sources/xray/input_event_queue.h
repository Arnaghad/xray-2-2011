////////////////////////////////////////////////////////////////////////////
//	Created		: 23.11.2026
//	Description	: Normalized cross-thread input event queue (SDL3 / Win32 agnostic)
////////////////////////////////////////////////////////////////////////////

#ifndef XRAY_INPUT_EVENT_QUEUE_H_INCLUDED
#define XRAY_INPUT_EVENT_QUEUE_H_INCLUDED

namespace xray {
namespace input {

enum class normalized_event_kind : u8 {
	key_down,
	key_up,
	mouse_motion,
	mouse_button_down,
	mouse_button_up,
	mouse_wheel,
	gamepad_added,
	gamepad_removed,
	focus_gained,
	focus_lost,
};

struct normalized_input_event {
	normalized_event_kind	kind = normalized_event_kind::focus_gained;
	u32						device_id = 0;
	s32						code = 0; // enum_keyboard or mouse_button bitmask
	float					x = 0.f;
	float					y = 0.f;
	float					z = 0.f;
};

XRAY_CORE_API	void	push_normalized_input_event		( normalized_input_event const& ev );
XRAY_CORE_API	bool	pop_normalized_input_event		( normalized_input_event& out_ev );
XRAY_CORE_API	void	clear_normalized_input_events	( );

} // namespace input
} // namespace xray

#endif // #ifndef XRAY_INPUT_EVENT_QUEUE_H_INCLUDED
