# Third-Party and System Dependencies

Цей документ є єдиним джерелом правди щодо активних сторонніх і системних залежностей після очищення дерева збірки (Stage 08). Машиночитна специфікація зберігається у [`sources/tools/ci/dependencies-manifest.json`](tools/ci/dependencies-manifest.json), а точні версії інструментарію — у [`sources/tools/ci/toolchain-manifest.json`](tools/ci/toolchain-manifest.json) і [`sources/versions.txt`](versions.txt).

## 1. Vendored-залежності у репозиторії

| Бібліотека | Шлях | Джерело | Версія / Коміт | Ліцензія | Призначення | Локальні патчі |
| --- | --- | --- | --- | --- | --- | --- |
| **meshoptimizer** | `sources/third_party/meshoptimizer` | `https://github.com/zeux/meshoptimizer` | `v1.3` (`9e1f07b159d3cb777f1c67ed31fc11fd117986f4`) | MIT | Оптимізація геометрії для `xray_maya_cook` / DCC | Без патчів (відстежується через `provenance.json`) |
| **sol2** | `sources/third_party/sol2` | `https://github.com/ThePhD/sol2` (MSYS2 `3.5.0-2`) | `3.5.0-2` | MIT | C++ ↔ Lua 5.5 біндінги | Патчі сумісності з системним Lua 5.5 під `XRAY_SOL_SYSTEM_LUA` (`compatibility/lua_version.hpp`, `compat-5.3.h`, `compat-5.4.h`, `state.hpp`, `state_view.hpp`) |
| **SPIRV-Reflect** | `sources/third_party/spirv_reflect` | `https://github.com/KhronosGroup/SPIRV-Reflect` | `0863a7a7ec76899c8509ff3523e708a9291e4f3b` | Apache-2.0 | Рефлексія дескрипторів і push-constants для Vulkan (`xray_spirv_reflect`) | Без патчів (`README.xray.md`, SHA-256 збережено) |
| **stb_dxt** | `sources/third_party/stb` | `https://github.com/nothings/stb` | `48fb1c9709f973041a7e6992d720ac625266cdd3` | Public Domain / MIT | DXT-стиснення текстур (`stb_dxt.h`) | Підмножина одного заголовка (`dxt-provenance.json`) |
| **WildMagic 4 Foundation** | `sources/third_party/wildmagic4` | Wild Magic 4.9 Foundation | `4.9` (`1795aa33a51fdde2ff5ea10627fc6d0b2b755c56`) | Boost Software License 1.0 / Wild Magic | Числова апроксимація у `xray_wildmagic4` / `xray_maya_cook` | Відновлена підмножина з 120 файлів (`provenance.json`) |
| **ODE** | `sources/ode` | Open Dynamics Engine | `0.11.1` | LGPL-2.1 / BSD-3-Clause | Fallback-шлях фізики (`XRAY_PHYSICS_USE_BULLET=OFF`) | CMake-збірка, x64 MinGW-фікси |
| **OPCODE** | `sources/opcode` | Pierre Terdiman OPCODE | `1.3` | Public Domain / Permissive | Fallback-шлях BVH (`XRAY_COLLISION_USE_BULLET_BVH=OFF`) | Явний CMake-список (без `StdAfx.cpp`/`entry_point.cpp`), `-fpermissive`/`-fno-strict-aliasing` |
| **FastDelegate** | `sources/fastdelegate` | CodeProject / Don Clugston | `1.5` | Permissive | Швидкі делегати в ядрі (`fastdelegate.h`) | Гард `XRAY_CORE_API` для DLL |
| **cs** | `sources/cs` | Внутрішній модуль X-Ray 2.0 | `1.3` | Internal / X-Ray Engine | Спільні бібліотеки `cs_core`, `cs_script` | Конвертація в CMake, міграція на `sol2`+Lua 5.5, вимкнено BugTrap (`CS_USE_BUG_TRAP=0`) |

## 2. Системні та пакетні залежності (MSYS2 / .NET / Windows SDK)

- **Компілятор і CMake/Ninja (MSYS2 MinGW64):** GCC `16.2.0-4`, CMake `4.4.4-3`, Ninja `1.13.2-1`, pkgconf `1~3.0.7-2` (`C++26`).
- **Бібліотеки MSYS2 MinGW64:** Lua `5.5.1-1` (`sources/lua/CMakeLists.txt` interface target), Boost `1.92.0-3`, SDL3 `3.4.18-1`, mimalloc `3.4.4-1`, zlib `1.3.2-2`, Bullet `3.25-11`, Vulkan Headers & Loader `1~1.4.363.0-1`, libogg `1.3.6-1`, libvorbis `1.3.7-3`, Asio `1.38.2-1`.
- **Шейдери та валідація:** DXC `1.9.2609.5` (`build/dxc/bin/x64`), Khronos Vulkan Validation Layers (`build/vulkan-validation/mingw64/bin`).
- **.NET / WPF:** .NET SDK `11.0.100` (`net11.0-windows`, C# `LangVersion 15.0`), NuGet `Extended.Wpf.Toolkit` `5.1.2` (`sources/xray/wpf_controls/wpf_controls.csproj`).
- **Системні Windows DLL:** `vulkan-1.dll`, `xaudio2_9.dll`, `ole32.dll`, `winmm.dll`, `ws2_32.dll`, `dbghelp.dll`, `kernel32.dll`, `user32.dll`, `gdi32.dll`.

## 3. Прибрані архівні залежності (Stage 08)

Вилучені з активного дерева збірки (історія збережена в Git):
- `sources/BugTrap` (`bugtrap-1.3.3291.42976`) після переведення `CS_USE_BUG_TRAP` у `0`.
- `sources/asio.zip` (замінено системним `mingw-w64-x86_64-asio`).
- `sources/xray/render/dx9`, `sources/xray/render/common` (замінено Vulkan 1.3+ бекендом `sources/xray/render/vulkan`).
- `sources/xray/maya`, `sources/xray/maya_animation` (замінено Python/CLI DCC-інтеграцією).
- Застарілі утиліти та логи: `build_log.txt`, `build_log_final.txt`, `UpgradeLog.htm`, `vcpkg.json`, `finger_printer`, `graphics_benchmark`, `rms_generator`, `rtp_learn`, `shader_generator`, `sound_system_usage_example`, `vfs_pack`.
