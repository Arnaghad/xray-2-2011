#pragma once
#include <cstdint>
namespace xray::engine::m8_outputs {
inline constexpr char mode_property[]="XRAY_M8_OUTPUT_WORKFLOW";
inline constexpr char request_property[]="XRAY_M8_OUTPUT_REQUEST";
inline constexpr char ack_property[]="XRAY_M8_OUTPUT_ACK";
enum class shot : unsigned {
    baseline=1,preview_resized,preview_restored,main_resized,main_restored,
    preview_minimized,preview_resumed,main_minimized,main_resumed,
    preview_closed,preview_reopened,preview_hidden,preview_visible,
    main_props_hidden,main_props_restored,shutdown_ready
};
inline char const* name(shot value) {
    switch(value) {
#define M8_OUTPUT_SHOT(id) case shot::id:return #id
        M8_OUTPUT_SHOT(baseline);M8_OUTPUT_SHOT(preview_resized);M8_OUTPUT_SHOT(preview_restored);
        M8_OUTPUT_SHOT(main_resized);M8_OUTPUT_SHOT(main_restored);M8_OUTPUT_SHOT(preview_minimized);
        M8_OUTPUT_SHOT(preview_resumed);M8_OUTPUT_SHOT(main_minimized);M8_OUTPUT_SHOT(main_resumed);
        M8_OUTPUT_SHOT(preview_closed);M8_OUTPUT_SHOT(preview_reopened);M8_OUTPUT_SHOT(preview_hidden);
        M8_OUTPUT_SHOT(preview_visible);M8_OUTPUT_SHOT(main_props_hidden);M8_OUTPUT_SHOT(main_props_restored);
        M8_OUTPUT_SHOT(shutdown_ready);
#undef M8_OUTPUT_SHOT
    }
    return nullptr;
}
inline std::uintptr_t token(unsigned frame,shot value) {return (std::uintptr_t(frame)<<8)|unsigned(value);}
inline unsigned frame(std::uintptr_t token) {return unsigned(token>>8);}
inline shot phase(std::uintptr_t token) {return shot(token&255);}
}
