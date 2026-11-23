# Build Scope

The supported build is Windows x64 with GCC / MinGW-w64 and Ninja.
CMake rejects other operating systems and non-x64 pointer ABIs; engine and
CS platform headers also reject non-x64 architectures. Linux is a future
port, not an available build option. CMake is the only maintained project format.

Use the system MSYS2 dependencies discovered by CMake. Lua is one shared
5.5 runtime for every consumer. The project sol2 snapshot carries the reviewed
Lua API patch. Bullet is provided by the system package.
Terrain chunk 103 decoding links the system zlib package through `ZLIB::ZLIB`;
no bundled zlib source or binary is added.
Vulkan uses MSYS2 headers/import symbols at build time and the installed Windows
system runtime at run time. No `vulkan-1.dll` is copied into `binaries/`.
`xray_vulkan_system_loader` verifies API 1.4 and the actual System32 module path.

Bundled Boost headers, BugTrap headers, FastDelegate, ODE, OPCODE, Ogg and
Vorbis remain because the current compiler dependency graph uses them.
In particular, discovering system Boost libraries does not replace the bundled
Boost headers. Migrating those headers requires a separate compatibility review.
The small libraries in `third_party` are also active dependencies.

Unported DX renderer and managed-editor sources remain reference code for
feature migration, not additional CMake targets. Their historical SDK adapters
are not supported builds or proof of current runtime support.

Windows API identifiers containing `WIN32` describe the OS API, not a build
architecture. Numeric widths, texture/index formats and archive reader layouts
are data contracts and remain independent of the host pointer ABI. Archive
writing targets the PC format; existing serialized enum values are preserved.

The Vulkan README documents configuration, regression tests and real-asset
acceptance. `xray_source_contract` prevents obsolete project platform selectors
and duplicate Lua headers from returning; `xray_windows_x64_contract` verifies
the Windows mutex ABI and behavior across threads.
