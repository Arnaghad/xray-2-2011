#include "test_check.hpp"

#include <lua.hpp>
#include <sol/sol.hpp>

#include <iostream>
#include <string>

static_assert(XRAY_SYSTEM_LUA == 1, "System Lua runtime must be active");
static_assert(XRAY_SOL_SYSTEM_LUA == 1, "sol2 must bind to system Lua");
static_assert(LUA_VERSION_NUM >= 505, "Lua 5.5+ is required");

namespace {

struct vec2_sample {
    double x = 0.0;
    double y = 0.0;

    double dot(vec2_sample const& other) const {
        return x * other.x + y * other.y;
    }
};

} // namespace

int main() {
    sol::state lua;
    lua.open_libraries(sol::lib::base, sol::lib::math, sol::lib::table, sol::lib::string);

    lua.set_function("add_ints", [](int a, int b) { return a + b; });
    lua.new_usertype<vec2_sample>(
        "vec2_sample",
        sol::constructors<vec2_sample(double, double)>(),
        "x", &vec2_sample::x,
        "y", &vec2_sample::y,
        "dot", &vec2_sample::dot
    );

    auto res = lua.safe_script(R"(
        local a = vec2_sample.new(3.0, 4.0)
        local b = vec2_sample.new(2.0, 5.0)
        return add_ints(10, 32), a:dot(b), string.upper("xray")
    )");
    XRAY_TEST_CHECK(res.valid());

    int sum = res.get<int>(0);
    double dp = res.get<double>(1);
    std::string label = res.get<std::string>(2);
    XRAY_TEST_CHECK(sum == 42);
    XRAY_TEST_CHECK(dp == 26.0);
    XRAY_TEST_CHECK(label == "XRAY");

    // Verify protected error handling (including SOL_ALL_SAFETIES_ON in Release).
    auto bad_syntax = lua.safe_script("local x =", sol::script_pass_on_error);
    XRAY_TEST_CHECK(!bad_syntax.valid());

    auto bad_call = lua.safe_script("return add_ints('not_a_number', 1)", sol::script_pass_on_error);
    XRAY_TEST_CHECK(!bad_call.valid());

    std::cout << "PASS: xray_lua_sol_binding (Lua " << LUA_VERSION_NUM << ")\n";
    return 0;
}
