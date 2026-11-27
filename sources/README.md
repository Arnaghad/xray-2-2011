# Build Scope

The supported builds are **Windows x64** (MSYS2 MinGW-w64 / UCRT64 GCC 16 and Ninja) and **Linux x86_64** (GCC 15+/16 and Ninja, Wayland / X11).
CMake rejects unsupported operating systems and non-x64 pointer ABIs; engine and CS platform headers enforce 64-bit LLP64/LP64 portability via `XRAY_PLATFORM_WINDOWS` / `XRAY_PLATFORM_LINUX` / `XRAY_PLATFORM_POSIX`. CMake is the only maintained project format.

Use the system dependencies discovered by CMake (MSYS2 on Windows; distro `-devel` packages on Linux). Lua is one shared 5.5 runtime for every consumer. The project sol2 snapshot carries the reviewed Lua API patch. Boost 1.92.0, SDL3, mimalloc, Ogg/Vorbis, standalone Asio, and Bullet are provided by single-source system packages (`Boost::headers`, `SDL3::SDL3`, `PkgConfig::OGG_VORBIS`, `asio::asio`, `PkgConfig::BULLET`); no bundled Boost, Ogg, Vorbis, or Asio headers remain in `sources/`.
Terrain chunk 103 decoding links the system zlib package through `ZLIB::ZLIB`; no bundled zlib source or binary is added.
Vulkan uses system headers/import symbols at build time and the installed system loader at run time (`vulkan-1.dll` on Windows, `libvulkan.so.1` on Linux) with `xray::platform::native_surface_handle` across Win32, Wayland, and X11. No `vulkan-1.dll` is copied into `binaries/`.
Audio selects `XAudio2.9` on Windows or the software `SDL3 Audio` backend (`PipeWire` / `PulseAudio` / `dummy`) on Linux via `i_audio_backend`.
Shader compilation loads portable DXC (`dxcompiler.dll` / `libdxcompiler.so`) with `dxc` CLI fallback and SPIR-V disk cache.

BugTrap headers, FastDelegate, ODE, and OPCODE remain active source dependencies.
The small libraries in `third_party` (`sol2`, `spirv_reflect`, `meshoptimizer`,
`wildmagic4`) are also active dependencies.

Unported DX renderer and managed-editor sources remain reference code for
feature migration, not additional CMake targets. Their historical SDK adapters
are not supported builds or proof of current runtime support.

Windows API identifiers containing `WIN32` describe the OS API, not a build
architecture. Numeric widths, texture/index formats and archive reader layouts
are data contracts and remain independent of the host pointer ABI. Archive
writing targets the PC format; existing serialized enum values are preserved.

The Vulkan README documents configuration, regression tests and real-asset
acceptance. Build and test presets live in `sources/CMakePresets.json`, and
pinned toolchain versions for both platforms are recorded in
`sources/tools/ci/toolchain-manifest.json`. The editor uses Avalonia 12.2 and
targets `net11.0` under the SDK pinned in `global.json` (`11.0.100`), loaded
via `hostfxr.dll` / `libhostfxr.so` and C ABI v3 (`surface_descriptor`). The
packaged EditorHost requires `Microsoft.NETCore.App` 11.0 on Windows x64 and Linux x64.

Registered CTest suites (`sources/tests/CMakeLists.txt`):
- Mandatory `cpu` label: `xray_source_contract`, `xray_lua_sol_binding`,
  `xray_windows_x64_contract`, `xray_platform_x64_contract`,
  `xray_fs_vfs_pal2_contract`, `xray_vfs_legacy_decode`,
  `xray_vfs_legacy_python_decode`, `xray_dcc_exchange_cook_roundtrip`,
  `xray_dcc_v1_v2_compatibility_contract`, `xray_editor_host_abi_contract`,
  `xray_avalonia_headless_smoke`, `xray_ogg_vorbis_contract`,
  `xray_dependency_single_source_contract`, `xray_math_and_binary_config_contract`,
  `xray_callback_and_raii_contract`, `xray_diagnostics_pal3_contract`,
  `xray_input_sdl3_contract`, `xray_diagnostics_minidump_contract` (Win32),
  `xray_network_udp_loopback`, `xray_audio_backend_contract`,
  `xray_physics_world_parity_contract`, `xray_collision_bvh_parity_contract`,
  `xray_physics_cook_fitter_contract`, `xray_shader_compiler_pal5_contract`.
- Environment-gated labels (`vulkan`, `dxc`, `sdk`, `audio`):
  `xray_vulkan_original_archive`, `xray_vulkan_sdl3_surface_contract`,
  `xray_vulkan_sync2_dynamic_rendering_contract`, `xray_dxc_shader_compile`,
  `xray_sdk_package_smoke`, `xray_xaudio2_9_contract` (Win32),
  `xray_ogg_vorbis_legacy_corpus`.

Preset quick start (run `--build --preset` and `ctest --preset` inside `sources/`):
```powershell
# Windows x64 (PowerShell)
cd sources
cmake --preset windows-x64-release
cmake --build --preset windows-x64-release --parallel 8
ctest --preset windows-x64-release-cpu
ctest --preset windows-x64-release-all
```

```bash
# Linux x86_64 (bash)
cd sources
cmake --preset linux-x64-release
cmake --build --preset linux-x64-release-cpu-tests --parallel
ctest --preset linux-x64-release-cpu --output-on-failure
cmake --build --preset linux-x64-release --parallel
ctest --preset linux-x64-release-all --output-on-failure
```
