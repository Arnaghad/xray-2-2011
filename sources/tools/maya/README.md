# X-Ray Engine 2.0 — Maya authoring alternative and legacy migration

This pipeline replaces the legacy Win32/MSVC 2011 C++ `.mll` frontend with a
Python API 2.0 frontend and a portable cooker. The recovered C++ Maya sources
remain reference material; neutral fitting/playback mathematics now lives in
`sources/xray/animation_math`.

The common v1/v2 writers/validator, cooker bridge, naming and conventions live in
[`../dcc_common/`](../dcc_common/README.md). Maya's original Python import paths
remain supported. Keep this sibling package when copying the tools. The neutral
`xray_asset_cook_cli` executable and `XRAY_ASSET_COOK_CLI` environment variable are
also available; the original CLI name and environment variable still work.
The Maya 2.1 frontend exports **exchange v2**, with `source_dcc: maya`, canonical
`right_handed_y_up`, centimeter-to-meter scale from Maya's internal units, and
source `frame_start` on clips. Both current CLI names read v1 and v2. The old
`exchange.schema_v1.ExchangeBundleWriter` API still defaults to v1 for callers
that use it directly. Build the cooker from the same SDK sources and point
`XRAY_ASSET_COOK_CLI` at that executable when older SDK binaries are also present.
Blender is the default authoring frontend; Maya remains a supported alternative.
See [`../blender/`](../blender/README.md).

## Choose the Maya route

Use Maya for native `.ma` / `.mb` authoring and for evaluated migration of binary
scenes, references, constraints or history outside the basic MA reader. Current
live verification uses **Maya 2027 on Windows**. Blender's no-Maya route supports
all five cooked asset kinds and basic MA, with its declared limits.

For SDK source editing choose **Maya** in **DCC options**, set Maya, mayapy and
the matching asset cooker, then **Associate scene** to make a workspace master.
Use **Open source in DCC**, save changes and **Reimport**. Exact Object/Action
selection, cache publication and failure retention are described in the
[SDK guide](../blender/SDK.md). A failed reimport retains the previous published
resource. Maya source evaluation does not require converting to Blender first.

For one-time `.mb` migration into editable Blender files use the
[maintainer runbook](../blender/MAINTAINER.md) and [migration contracts](../blender/MIGRATION.md).
The original controller rig is represented by evaluated joint poses; its
controllers are not converted to a Blender controller rig. Root.scale=(0,0,0)
source defects retain an explicit failure, as agreed for the 23 known clips.

Both frontends use the same v2 cooker and native playback contracts. Test
selection is controlled by `XRAY_DCC_MAYA_MODE=AUTO|REQUIRED|OFF`; the
[CI guide](../blender/CI.md) records optional-host handling and required parity.

## Architecture

1. **In-Maya Python API 2.0 Frontend** (`sources/tools/maya/plugin/`)
   - Uses `maya.api.OpenMaya` / `maya.api.OpenMayaAnim` / `maya.api.OpenMayaUI` (`maya_useNewAPI`).
   - Targets Maya 2022–2027+ Python API 2.0 without compiling a version-specific
     `.mll`/`.so`; the tested host here is Maya 2027 Windows. Linux and other
     host versions need their own live acceptance.
2. **Exchange Format (`xray-dcc-exchange/v2`, with v1 compatibility)**
   - Deterministic `manifest.json` + little-endian 4-byte-aligned `payload.bin` intermediate representation.
   - Schema validators and reference roundtrip tools (`schema_v1.py`, `reference_codec.py`).
   - Shared [v2 specification](../dcc_common/SPEC_V2.md); physics fitting reads
     the v2 `source` result. Authoring Rigid links export as engine Fixed joints;
     the unlinked Rigid root metadata retains reserved joint slot zero.
3. **Portable C++26 Cooker & CLI** (`sources/xray/maya_cook/`)
   - Zero Win32 APIs, zero engine allocator globals, builds both inside the main repository tree (`sources/CMakeLists.txt`) and completely standalone on Linux/Windows (`sources/xray/maya_cook/CMakeLists.txt`).
   - Uses `meshoptimizer`, portable `NVMeshMender`, pure C++26 uniform cubic B-spline animation compressor (`bspline_fit.hpp`), and recovered Wild Magic 4 containment (`Wm4ContBox3` / `Wm4ContSphere3` in `sources/third_party/wildmagic4/`).

---

## Building the Standalone Cooker CLI (Windows & Linux)

### C++26 toolchain and Ninja

The verified Windows toolchain is MSYS2 MinGW x64 GCC 16; use CMake 4.3+ and a
compiler supporting the requested C++26 mode. The source build supports Linux,
but Linux runtime tests have not run in this workspace.

```bash
cmake -S sources/xray/maya_cook -B build/maya-port/standalone-cook -G Ninja -DCMAKE_BUILD_TYPE=Release
cmake --build build/maya-port/standalone-cook -j 8
ctest --test-dir build/maya-port/standalone-cook --output-on-failure
```

This builds:
- `xray_maya_cook_cli` (or `.exe` on Windows)
- `xray_maya_cook_tests` (unit suite for M1–M5: static, skeletal, animation B-splines, WildMagic4 physics)

---

## Installing & Loading in Autodesk Maya (Windows & Linux)

### Option A: Maya Module File (Recommended)

Point `MAYA_MODULE_PATH` to `sources/tools/maya/` (which contains `xray_maya.mod`) and set `XRAY_MAYA_COOK_CLI` if your binary is outside the default build locations (`build/maya-port/standalone-cook`, `build/sdk-native-*-release/binaries`, `build/sdk-native-*-debug/binaries`).

```bash
# Linux example
export MAYA_MODULE_PATH=/path/to/repo/sources/tools/maya
export XRAY_MAYA_COOK_CLI=/path/to/repo/build/maya-port/standalone-cook/xray_maya_cook_cli
```

```powershell
# Windows PowerShell example
$env:MAYA_MODULE_PATH = "C:\Backup\Source\xray-2-2011-main\sources\tools\maya"
$env:XRAY_MAYA_COOK_CLI = "C:\Backup\Source\xray-2-2011-main\build\maya-port\standalone-cook\xray_maya_cook_cli.exe"
```

Then in Maya Python / Script Editor:

```python
import maya.cmds as cmds
cmds.loadPlugin("xray_maya_plugin.py")
```

### Option B: Direct Load

```python
import maya.cmds as cmds
cmds.loadPlugin("/path/to/sources/tools/maya/plugin/xray_maya_plugin.py")
```

Set the X-Ray resources root via `cmds.xray_path(resources="/path/to/resources")` or the **X-Ray 2.0 -> Set Resources Path...** menu item (or environment variables `XRAY_RESOURCES_PATH` / `STK2_ROOT_PATH`).

---

## Exposed MEL / Python Commands & Nodes

| Feature | Command / Node | Notes |
| --- | --- | --- |
| Paths | `xray_path` | `-r/-resources`, `-m/-models`, `-a/-animations` |
| Static Model | `xray_solid_visual_export` | Exports `.model` (or `.ogf`/archive); `-o`, `-r`, `-t`, `-hq`, `-ndb`, `-no`, `-ow` |
| Skeleton | `xray_skeleton_export` | Exports `.skeleton`; `-o`, `-r` |
| Skinned Model | `xray_skin_export` | Exports `.skinned_model`; `-o`, `-skn`, `-skl`, `-t`, `-hq`, `-ndb` |
| Animation Clip | `xray_anim_export` | Exports `.clip` with cubic B-splines; `-o`, `-r`, `-l`, `-a`, `-pt`, `-pr`, `-ps` |
| Physics Nodes | `xray_body`, `xray_collision`, `xray_constraint` | Registered via `kLocatorNode` (`0x00700`, `0x00600`, `0x00800`) |
| Auto Ragdoll | `xray_build_collision` | Traverses joints (`-j`, `-r`) and runs WildMagic4 `--fit-collision` via CLI |
| Physics Export | `xray_ph_exporter` | Exports `.physics` / `.ph` Lua + converted binary config |
| Scene Helper | `xray_fake_drawing_locator` | Locator node (`0x00801`) for static export root groups |

---

## T1 Automated Verification vs T2 Real Autodesk Maya Checklist

- **T1 (Automated, Headless in CTest — Passing):**
  - C++26 unit suite (`xray_maya_cook_unit`) — static, skin, skeleton, B-spline clip fit, WildMagic4 containment, `.physics`/`.ph`.
  - Engine contract tests (`xray_maya_cook_engine_contract`) — validates cooked output directly against engine Vulkan loaders, `vfs`, `checked_config`, and `xray::collision::new_composite_geometry_from_physics_shell_config`.
  - Python schema & reference recook suite (`xray_maya_exchange_unit`, `xray_maya_plugin_unit`) using structural Maya API 2.0 mocks + real `xray_maya_cook_cli`.
- **T2 (Live Autodesk Maya `mayapy` Host Verification — Passing on Maya 2027):**
  - Automated CTest target `xray_maya_live_t2` (registered when `mayapy` is detected
    and Maya mode is AUTO/REQUIRED) / manual script [`plugin/maya_live_t2_suite.py`](plugin/maya_live_t2_suite.py):
    ```powershell
    & "C:\Program Files\Autodesk\Maya2027\bin\mayapy.exe" -X utf8 sources/tools/maya/plugin/maya_live_t2_suite.py `
        --cli-exe build/sdk-native-20261108-debug/binaries/xray_maya_cook_cli.exe `
        --contract-exe build/sdk-native-20261108-debug/binaries/xray_maya_cook_engine_contract_tests.exe
    ```
  - Loads `xray_maya_plugin.py` inside real `maya.standalone`, verifies all 4 dependNodes + 10 commands + `shelf_menu` helper, exports static `.model`, `.skeleton`, `.skinned_model`, standard/additive `.clip`, and WildMagic4-fitted `.physics`/`.ph`, unloads cleanly, and verifies every cooked binary with `xray_maya_cook_engine_contract_tests --verify-maya-t2`.
  - `xray_dcc_live_parity` additionally authors equivalent five-bone scenes in
    real Maya and the installed Blender extension (legacy Y-up and native Z-up),
    compares all cooked asset kinds, checks six clips in engine playback and
    confirms v1/v2 header byte compatibility. See the
    [fixture, tolerances and reproduction command](../dcc_common/PARITY.md).

