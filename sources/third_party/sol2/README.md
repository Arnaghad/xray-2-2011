# sol2 compatibility snapshot

Source: MSYS2 `mingw-w64-x86_64-sol2 3.5.0-2`, copied from the installed
`mingw64/include/sol` on 2026-10-08. Upstream: https://github.com/ThePhD/sol2.
The upstream MIT license is in `LICENSE.txt`; headers retain their notices.
This is a complete header snapshot, not an overlay on mutable system headers.
The system sol2 package is not modified or required by the build.

Local changes for system Lua 5.5:

- `compatibility/lua_version.hpp`, `compat-5.3.h`, `compat-5.4.h`: under
  `XRAY_SOL_SYSTEM_LUA`, select unprefixed system headers rather than the
  historical `sources/lua` tree; accept 5.5 and retain the removed error-code shim.
- `state.hpp`: custom allocator construction supplies `luaL_makeseed(nullptr)`.
- `state_view.hpp`: `step_gc(int)` retains kilobyte units but passes `size_t`
  bytes to the new C API; negative sizes raise `sol::error`.
- On 5.5, `change_gc_mode_incremental()` and `change_gc_mode_generational()`
  take no tuning arguments. Old argument-taking calls intentionally fail to
  compile: Lua 5.5 removed that interface and changed parameter semantics.
  Tune explicitly with `lua_gc(L, LUA_GCPARAM, parameter, value)` instead.
  There are no such old tuning calls in the engine.

The CMake `sol2::sol2` target propagates both this snapshot and the system Lua
interface. Lua 5.5.1 or a later 5.5 patch release is required; 5.6 is rejected
until reviewed. The engine uses only system Lua headers. Do not add a second
Lua include directory or `LUA_COMPAT_5_3`: that would mix Lua ABIs.
Consumers of the engine script/config headers must link `sol2::sol2`;
compile-time guards reject missing usage requirements.
The regression test includes the CS wrappers before sol2 to catch mixed headers.
Non-Debug builds explicitly enable `SOL_ALL_SAFETIES_ON`: malformed binding
arguments must raise a protected Lua error in Release as they do in Debug.
This preserves `NDEBUG` and engine assertion policy; it configures sol2 only.

Regression coverage: `xray_lua_sol_binding` plus the renderer texture-options,
material/resource tests and real-asset probes. Full game/editor script startup
is not implied by those tests. Lua 5.4 bytecode must be regenerated for 5.5;
source scripts must account for language changes (notably reserved `global`
and read-only loop variables): https://www.lua.org/manual/5.5/manual.html#8.

When refreshing this snapshot, reapply and review the changes above, rebuild
all Lua consumers from a fresh build directory, and run the regression suite.
