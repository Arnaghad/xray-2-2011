# Build Scope

The supported build is Windows x64 with GCC / MinGW-w64 and Ninja.
CMake rejects other operating systems and non-x64 pointer ABIs; engine and
CS platform headers also reject non-x64 architectures. Linux is a future
port, not an available build option. CMake is the only maintained project format.

Use the system MSYS2 dependencies discovered by CMake. Lua is one shared
5.5 runtime for every consumer. The project sol2 snapshot carries the reviewed
Lua API patch. Boost 1.92.0, Ogg/Vorbis, standalone Asio, and Bullet are
provided by single-source MSYS2 MinGW-w64 packages (`Boost::headers`,
`PkgConfig::OGG_VORBIS`, `asio::asio`, `PkgConfig::BULLET`); no bundled Boost,
Ogg, Vorbis, or Asio headers remain in `sources/`.
Terrain chunk 103 decoding links the system zlib package through `ZLIB::ZLIB`;
no bundled zlib source or binary is added.
Vulkan uses MSYS2 headers/import symbols at build time and the installed Windows
system runtime at run time. No `vulkan-1.dll` is copied into `binaries/`.
`xray_vulkan_system_loader` verifies API 1.4 and the actual System32 module path.

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
pinned Windows x64 toolchain versions are recorded in
`sources/tools/ci/toolchain-manifest.json`. The editor uses Avalonia 12.2 and
targets `net11.0` under the SDK pinned in `global.json` (`11.0.100`). The
packaged EditorHost requires `Microsoft.NETCore.App` 11.0 on Windows x64.

Registered CTest suites (`sources/tests/CMakeLists.txt`):
- Mandatory `cpu` label: `xray_source_contract`, `xray_lua_sol_binding`,
  `xray_windows_x64_contract`, `xray_vfs_legacy_decode`,
  `xray_vfs_legacy_python_decode`, `xray_dcc_exchange_cook_roundtrip`,
  `xray_editor_host_abi_contract`, `xray_avalonia_headless_smoke`, `xray_ogg_vorbis_contract`,
  `xray_dependency_single_source_contract`.
- Environment-gated labels (`vulkan`, `dxc`, `sdk`, `audio`):
  `xray_vulkan_original_archive`, `xray_dxc_shader_compile`,
  `xray_sdk_package_smoke`, `xray_ogg_vorbis_legacy_corpus`.

Preset quick start (run `--build --preset` and `ctest --preset` inside `sources/`):
```powershell
cd sources
cmake --preset windows-x64-release
cmake --build --preset windows-x64-release --parallel 8
ctest --preset windows-x64-release-cpu
ctest --preset windows-x64-release-all
```
