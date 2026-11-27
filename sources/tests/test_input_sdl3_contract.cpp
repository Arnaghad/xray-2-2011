#define XRAY_LOG_MODULE_INITIATOR "test_input_sdl3_contract"
#include "test_check.hpp"

#include <xray/extensions.h>
#include <xray/input/api.h>
#include <xray/input/handler.h>
#include <xray/input/world.h>
#include <xray/input_event_queue.h>
#include "input_memory.h"
#include "input_world.h"
#include "receiver_gamepad.h"
#include "receiver_keyboard.h"
#include "receiver_mouse.h"

#include <SDL3/SDL.h>

#include <iostream>
#include <string>
#include <vector>

namespace {

struct recording_handler final : xray::input::handler {
    int priority = 0;
    bool consume_keyboard = false;
    bool consume_gamepad = false;
    bool consume_mouse_key = false;
    bool consume_mouse_move = false;

    std::vector<std::string> events;

    explicit recording_handler(int prio)
        : priority(prio) {}

    bool on_keyboard_action(xray::input::world*, xray::input::enum_keyboard key, xray::input::enum_keyboard_action action) override {
        events.push_back("kb:" + std::to_string(int(key)) + ":" + std::to_string(int(action)));
        return consume_keyboard;
    }

    bool on_gamepad_action(xray::input::world*, xray::input::gamepad_button button, xray::input::enum_gamepad_action action) override {
        events.push_back("gp:" + std::to_string(int(button)) + ":" + std::to_string(int(action)));
        return consume_gamepad;
    }

    bool on_mouse_key_action(xray::input::world*, xray::input::mouse_button button, xray::input::enum_mouse_key_action action) override {
        events.push_back("ms:" + std::to_string(int(button)) + ":" + std::to_string(int(action)));
        return consume_mouse_key;
    }

    bool on_mouse_move(xray::input::world*, int x, int y, int z) override {
        events.push_back("mm:" + std::to_string(x) + "," + std::to_string(y) + "," + std::to_string(z));
        return consume_mouse_move;
    }

    int input_priority() override {
        return priority;
    }
};

void test_keyboard_sdl3_and_queue() {
    using namespace xray::input;

    clear_normalized_input_events();

    XRAY_TEST_CHECK(receiver::keyboard::translate_sdl_scancode(SDL_SCANCODE_A) == key_a);
    XRAY_TEST_CHECK(receiver::keyboard::translate_sdl_scancode(SDL_SCANCODE_LCTRL) == key_lcontrol);
    XRAY_TEST_CHECK(receiver::keyboard::translate_sdl_scancode(SDL_SCANCODE_RCTRL) == key_rcontrol);
    XRAY_TEST_CHECK(receiver::keyboard::translate_sdl_scancode(SDL_SCANCODE_RALT) == key_rmenu);
    XRAY_TEST_CHECK(receiver::keyboard::translate_sdl_scancode(SDL_SCANCODE_UP) == key_up);
    XRAY_TEST_CHECK(receiver::keyboard::translate_sdl_scancode(SDL_SCANCODE_DELETE) == key_delete);
    XRAY_TEST_CHECK(receiver::keyboard::translate_sdl_scancode(SDL_SCANCODE_KP_ENTER) == key_numpadenter);
    XRAY_TEST_CHECK(receiver::keyboard::translate_sdl_scancode(SDL_SCANCODE_PAUSE) == key_pause);
    XRAY_TEST_CHECK(receiver::keyboard::translate_sdl_scancode(SDL_SCANCODE_F4) == key_f4);

    int dummy_engine = 0;
    input_world world(reinterpret_cast<xray::input::engine&>(dummy_engine), {});
    world.acquire();

    recording_handler h_low(1);
    recording_handler h_high(10);
    h_low.consume_keyboard = true;
    world.add_handler(h_high);
    world.add_handler(h_low);

    // Push normalized Ctrl+S key events (as main thread SDL_PollEvent does)
    push_normalized_input_event({normalized_event_kind::key_down, 0, s32(SDL_SCANCODE_LCTRL)});
    push_normalized_input_event({normalized_event_kind::key_down, 0, s32(SDL_SCANCODE_S)});
    world.tick();

    XRAY_TEST_CHECK(world.get_keyboard()->is_key_down(key_lcontrol));
    XRAY_TEST_CHECK(world.get_keyboard()->is_key_down(key_s));

    // Alt+Tab focus_lost clears held keys immediately
    push_normalized_input_event({normalized_event_kind::focus_lost});
    world.tick();
    XRAY_TEST_CHECK(!world.get_keyboard()->is_key_down(key_lcontrol));
    XRAY_TEST_CHECK(!world.get_keyboard()->is_key_down(key_s));

    // Keys sent while unfocused are ignored
    push_normalized_input_event({normalized_event_kind::key_down, 0, s32(SDL_SCANCODE_S)});
    world.tick();
    XRAY_TEST_CHECK(!world.get_keyboard()->is_key_down(key_s));

    // focus_gained restores input acceptance
    push_normalized_input_event({normalized_event_kind::focus_gained});
    push_normalized_input_event({normalized_event_kind::key_down, 0, s32(SDL_SCANCODE_S)});
    world.tick();
    XRAY_TEST_CHECK(world.get_keyboard()->is_key_down(key_s));

    // Key release via direct SDL_Event path
    SDL_Event sdl_up{};
    sdl_up.type = SDL_EVENT_KEY_UP;
    sdl_up.key.scancode = SDL_SCANCODE_S;
    sdl_up.key.down = false;
    world.feed_sdl_event(sdl_up);
    h_low.events.clear();
    world.tick();
    XRAY_TEST_CHECK(!world.get_keyboard()->is_key_down(key_s));
    XRAY_TEST_CHECK(!h_low.events.empty());

    world.remove_handler(h_low);
    world.remove_handler(h_high);
}

void test_mouse_sdl3_motion_buttons_wheel() {
    using namespace xray::input;

    clear_normalized_input_events();

    int dummy_engine = 0;
    input_world world(reinterpret_cast<xray::input::engine&>(dummy_engine), {});
    world.acquire();
    recording_handler h(0);
    h.consume_mouse_move = true;
    h.consume_mouse_key = true;
    world.add_handler(h);

    push_normalized_input_event({normalized_event_kind::mouse_motion, 0, 0, 12.f, -7.f, 0.f});
    push_normalized_input_event({normalized_event_kind::mouse_motion, 0, 0, 3.f, 5.f, 0.f});
    push_normalized_input_event({normalized_event_kind::mouse_button_down, 0, s32(mouse_button_left)});
    push_normalized_input_event({normalized_event_kind::mouse_button_down, 0, s32(mouse_button_right)});
    push_normalized_input_event({normalized_event_kind::mouse_wheel, 0, 0, 0.f, 0.f, 120.f});

    world.tick();
    auto const& st = world.get_mouse()->get_state();
    XRAY_TEST_CHECK(st.x == 15);
    XRAY_TEST_CHECK(st.y == -2);
    XRAY_TEST_CHECK(st.z == 120);
    XRAY_TEST_CHECK((st.buttons & mouse_button_left) != 0);
    XRAY_TEST_CHECK((st.buttons & mouse_button_right) != 0);

    // Deltas reset on next tick while button state remains
    world.tick();
    XRAY_TEST_CHECK(world.get_mouse()->get_state().x == 0);
    XRAY_TEST_CHECK(world.get_mouse()->get_state().y == 0);
    XRAY_TEST_CHECK(world.get_mouse()->get_state().z == 0);
    XRAY_TEST_CHECK((world.get_mouse()->get_state().buttons & mouse_button_left) != 0);

    // Focus lost resets held mouse buttons
    push_normalized_input_event({normalized_event_kind::focus_lost});
    world.tick();
    XRAY_TEST_CHECK(world.get_mouse()->get_state().buttons == 0);

    world.remove_handler(h);
}

void test_gamepad_sdl3_hotplug_and_vibration() {
    using namespace xray::input;

    XRAY_TEST_CHECK(SDL_InitSubSystem(SDL_INIT_GAMEPAD));
    int gp_count = 0;
    SDL_JoystickID* gp_ids = SDL_GetGamepads(&gp_count);
    XRAY_TEST_CHECK(gp_count >= 0);
    if (gp_ids) {
        SDL_free(gp_ids);
    }

    int dummy_engine = 0;
    input_world world(reinterpret_cast<xray::input::engine&>(dummy_engine), {});
    receiver::gamepad gp(0, world);

    xray::input::vector<handler*> handlers;
    recording_handler low(1);
    recording_handler high(5);
    handlers.push_back(&low);
    handlers.push_back(&high);

    // 1. Hotplug connect: vibration starts at 0.0
    gp.apply_gamepad_state(
        true,
        static_cast<u16>(gamepad_a),
        1.0f,
        0.5f,
        xray::math::float2(1.0f, 0.0f),
        xray::math::float2(0.0f, -0.5f)
    );
    gp.process(handlers);
    XRAY_TEST_CHECK(gp.get_vibration(gamepad_vibrator_left) == 0.0f);
    XRAY_TEST_CHECK(gp.get_vibration(gamepad_vibrator_right) == 0.0f);
    XRAY_TEST_CHECK(gp.get_state().left_trigger == 1.0f);
    XRAY_TEST_CHECK(gp.get_state().left_thumb_stick.x > 0.99f);
    XRAY_TEST_CHECK(!low.events.empty() && low.events.front() == "gp:4096:" + std::to_string(int(gp_key_down)));
    XRAY_TEST_CHECK(!high.events.empty() && high.events.front() == "gp:4096:" + std::to_string(int(gp_key_down)));

    // 2. Vibration clamping [0.f, 1.f]
    gp.set_vibration(gamepad_vibrator_left, 0.5f);
    gp.set_vibration(gamepad_vibrator_right, 2.0f);
    XRAY_TEST_CHECK(gp.get_vibration(gamepad_vibrator_left) > 0.49f && gp.get_vibration(gamepad_vibrator_left) < 0.51f);
    XRAY_TEST_CHECK(gp.get_vibration(gamepad_vibrator_right) == 1.0f);
    gp.set_vibration(gamepad_vibrator_right, -1.0f);
    XRAY_TEST_CHECK(gp.get_vibration(gamepad_vibrator_right) == 0.0f);

    // 3. Hot-unplug fires clean gp_key_up and resets vibration
    low.events.clear();
    high.events.clear();
    gp.apply_gamepad_state(false, 0, 0.f, 0.f, xray::math::float2(0.f, 0.f), xray::math::float2(0.f, 0.f));
    gp.process(handlers);
    XRAY_TEST_CHECK(gp.get_state().buttons == 0);
    XRAY_TEST_CHECK(gp.get_vibration(gamepad_vibrator_left) == 0.0f);
    XRAY_TEST_CHECK(!low.events.empty() && low.events.front() == "gp:4096:" + std::to_string(int(gp_key_up)));
    XRAY_TEST_CHECK(!high.events.empty() && high.events.front() == "gp:4096:" + std::to_string(int(gp_key_up)));

    SDL_QuitSubSystem(SDL_INIT_GAMEPAD);
}

} // namespace

int main() {
    xray::input::allocator_type alloc(false);
    xray::input::set_memory_allocator(alloc);

    test_keyboard_sdl3_and_queue();
    test_mouse_sdl3_motion_buttons_wheel();
    test_gamepad_sdl3_hotplug_and_vibration();

    std::cout << "PASS: xray_input_sdl3_contract\n";
    return 0;
}
