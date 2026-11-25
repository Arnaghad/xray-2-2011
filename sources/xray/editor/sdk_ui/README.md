# SDK UI compatibility stage

This page records the historical WinForms/WPF compatibility stage. The current
editor uses Avalonia on .NET 11. See the [build instructions](../../../README.md)
and [UI migration plan](../../../../ui_migration_plan.md).

This target checks the SDK-1 through SDK-3 controls, IDE shell, projects,
documents, resource pipeline, managed bridge, and SDK-4 scene editing.
The native full IDE (`WindowIde`) binds authored models/point lights, mesh picking,
snap and the existing native transform controls to one shared command history.
The SDK-1 `woodbox_mil` property slice remains an independent regression route
inside the same dockable IDE layout.

Versions verified against upstream release metadata on 2026-10-22:

- .NET SDK **10.0.402**, Windows Desktop runtime **10.0.13**, C# **14**.
  `global.json` permits stable patches within the pinned SDK feature band.
- DockPanel Suite and VS2015 theme **3.1.1**.
- Extended WPF Toolkit **5.1.2**.

The NuGet lock files fix resolved dependencies and content hashes. Normal builds
restore in locked mode. The installed system SDK 10.0.203 is too old for this
baseline; use the workspace SDK or install the SDK pinned in `global.json`.
The workspace SDK download was checked against Microsoft's published SHA-512.
Release metadata and download receipt are under `build/sdk-modernization/`.

From the repository root, using PowerShell:

```powershell
./sources/tools/sdk/build-sdk-ui.ps1 -Configuration Debug -Verify
./sources/tools/sdk/build-sdk-ui.ps1 -Configuration Release -Verify
```

`-Dotnet` selects another installation of the pinned SDK; `-OutputRoot` selects
an isolated build/evidence directory. `-PackageSource` optionally selects a local
NuGet package feed. Without it, normal configured NuGet sources are used.
The current downloaded packages can be used with
`-PackageSource build/sdk3-ui-20261109/cli-home/.nuget/packages`.

For CMake, configure an isolated tree with `XRAY_DOTNET_EXECUTABLE` pointing to
the modern SDK and optionally set `XRAY_SDK_UI_PACKAGE_SOURCE`. Build
`xray_sdk_ui`, then run `ctest -R xray_sdk_ui_compatibility --output-on-failure`.
The target is included in the default build; verification runs separately.

The WPF project preserves the original project inclusion list: 286 C# sources,
86 XAML pages and 19 resources. The 423-file source comparison in the migration
plan includes legacy files outside that original build list; they are not
silently enabled alongside their replacements. A source-present file is not
evidence that it is compiled or works.

The 13 compatibility check groups construct and render original property, curve and graph
controls, exercise the current toolkit numeric control, verify nested owner lookup
and release on disposal, round-trip dock contents with stable identities, check
ABI negotiation, rejected edits, undo/redo branching, owner thread/lifetime guards,
repeated WPF initialization in the same process, keyboard focus/text commit/auto-hide/float/DPI,
shared controls (`tree_view` filter/search/multi-select, `document_base`/`document_editor_base`
panel hide vs document close lifecycle, `hypergraph_control` node/link operations,
and `serializer` isolated settings/multi-monitor clamping), and the full `WindowIde`
shell (`GuiBinder` menus/toolbars, `InputEngine` shortcuts, `ImageLoader` 73-icon
atlas, `ProjectTab`, `ToolsTab` with 8 active tools and 2 documented dormant routes,
`ObjectInspectorTab`, `SceneViewPanel` 21 render modes and DPI-invariant picking
rays, `AnimationCurveEditorPanel`, `SceneGraphTab`, `CameraViewPanel`,
`TriangulationTester`, `PropertyConnectionEditor`, `IdeLogForm`, `OptionsDialog`
4 pages with apply/cancel rollback, and `AboutDialog`). They produce JSON, PNGs
(`property-grid.png`, `curve-editor.png`, `hypergraph.png`, `ide-workspace.png`)
and layout XML (`dock-layout.xml`, `ide-layout.xml`). Fake callbacks in this
executable exercise failures deterministically; native rendering is verified
separately.

## Projects and resources (SDK-3)

The production IDE provides New/Open/Save/Save As/Close, recent projects,
dirty/save prompts, project-owned document disposal and recovery. Opening a
reference project stages a writable copy under the separate workspace; the
prepared pack and original SDK remain read-only. Project/library serialization
preserves original GUID keys, paths, object types, symbolic Lua references and
unknown fields. Save uses a flushed staging file and atomic replacement; recovery
validates a complete intermediate or interrupted-save candidate before replacing
the last good file. Unsaved sessions have separate recovery locations and require
Save As. Project modals suspend IDE shortcuts and restore input on exit.

`SdkResourceBrowser` uses the same VFS overlay and dependency inspector as project
loading. `SdkResourcePipeline` retains engine resource handles, fingerprints source
dependencies, invalidates stale entries and cancels queries. Cache files are JSON
receipts in user data, outside authored sources; they are not substitute cooked
assets. `SdkNativeResources` binds a separate resource ABI 1 (24-byte callbacks,
40-byte request, 32-byte result) to the native engine mounts and raw/model/material/
texture cooks. Standalone checks validate raw configuration files; native cooks
are exercised by the GPU diagnostic. Replacing a backend requires releasing its
queries and cache entries first.

The explicit format-copy API supports only the original Lua schema (version 1)
with provenance, writes a separate destination and rejects unsupported versions.
Opening a project does not convert its format. Reference `alex` and
`dima_light_test`, their terrain bytes, seven library sources, missing dependencies,
malformed projects, Cyrillic paths, failed saves, recovery and document transitions
are covered by `SdkProjectChecks.cs`. See [SDK-3 acceptance](sdk3/acceptance.md)
for evidence and the boundary with SDK-4 and later stages.

## SDK-4 commands and scene editing

`Command.cs` and `CommandEngine.cs` are now compiled in the production host and
compatibility target. The original commit/rollback, nested transaction and preview
contracts were compared with `base/sources/command_engine*`, `command_transaction.cpp`
and `world/sources/command_apply_control_transform*` before activation. Commands
retain operation-specific before/after data; the engine does not snapshot the scene.
Rejected commits and cancelled previews preserve redo and saved-state identity.
Nested transactions are retained/evicted as complete undo units; redo regenerates
and validates their shape. Commands must make their own rejected/throwing writes
atomic; the engine compensates successful nested writes.

The native property adapter now uses this engine instead of a separate history
list. The full IDE supplies `WindowIde.Commands` to that adapter, and both the
toolbar and hotkeys use the same branch. The command tests cover nested replay,
failure compensation, saved markers, bounded history, preview accept/cancel/failure,
owner/lifetime and interleaved native/managed commands.

`ProjectEditing`, `ProjectClipboard` and `ProjectPropertyAdapters` now bind project
create/delete/duplicate, clipboard, grouping/reparenting, rename, visibility and
multi-object properties to the same engine. Successful saves establish the content
marker, selection/ignore history stays clean, external dirty state survives undo,
and valid project transitions reset history. Tree/menu/keyboard routes use these
commands; system clipboard is isolated in background runs. Project options restore
the original track-active-item setting rather than mutating the logical project name.
See [project command checkpoint](sdk4/project-commands.md).

The versioned native scene extension prepares independent candidate instances
before committing a scene pointer. Failed commits/undo/redo/previews compensate
managed state before history changes; failed New/Open/Close retain the active session.
Mesh picking and vertex/edge/face snapping share the rendered camera/projection.
Mouse drag, axis/arrow keys and W/E/R use native control calculation. Persistent
GUID ordering keeps coplanar draw order stable across save/reopen; startup recovery
waits for native cook readiness.

See [SDK-4 acceptance and scope](sdk4/acceptance.md). The final matrix is Debug/Release
CTest 5/5 and compatibility 14/14, with separate authored-scene, property-slice and
legacy-host GPU evidence. Full viewport services retain SDK-5 acceptance.

## Native IDE shell and property slice

Build `stalker2` and `xray_editor_world` in the isolated CMake tree. The host uses
locked NuGet restore and deploys WPF/Toolkit/DockPanel assemblies alongside its DLL.
`XRAY_SDK_PROPERTY_EDITOR=1` enables the `WindowIde` shell and property panel
before renderer startup; without it the existing ABI-v2 control host keeps its
previous toolbar. The IDE shell preserves the viewport HWND inside `SceneViewPanel`
and selects the real model after resource loading. Position edits commit when
numeric editing completes. Undo and Redo use the same native write path and a
bounded 100-command history.

`WindowIde.cs`, `SdkControls.cs`, `SdkIdePanels.cs`, `ProjectTab.cs`, `ToolsTab.cs`,
and `ObjectInspectorTab.cs` port the original IDE shell (`window_ide.*`), shared
controls (`editor/controls/sources`), and panels with stable `GetPersistString`
identities (`xray.editor.project_tab`, `xray.editor.tools_tab`,
`xray.editor.object_inspector_tab` + `xray.editor.controls.item_properties_panel_base`,
and `xray.editor.scene_view_panel` + `xray.editor.window_view`). Isolated settings
are stored separately from the original 2011 SDK registry keys.

Before changing live contents, layout loading rejects DTDs, files over 1 MiB,
unknown content identities and invalid library schema in a temporary docking
container. Live contents are detached without disposal (releasing child Win32 focus
before nested `DockPane` reload), empty pane containers are cleared, then the same
viewport and panels are restored. Hidden owners explicitly refresh dock-window
visibility so empty side windows cannot reserve viewport space. Invalid XML leaves
selection and the renderer HWND intact; layout reset restores the 4-panel reference
workspace (`ProjectTab` + `ToolsTab` on `DockLeft`, `SceneViewPanel` in `Document`,
`ObjectInspectorTab` on `DockRight`).

`world/sdk_property_bridge.h` and `base/SdkProperties.cs` define a separate ABI 1:
40-byte snapshot (version, size, object ID, revision, XYZ, reserved), 40-byte
callbacks and 16-byte extension result. Version, size, owner, finite coordinates,
identity and revision are validated before mutation. Native writes copy values
and queue `scene().update_model`; no managed pointer is retained. Rejected writes
preserve the scene, input payload and history cursor/redo branch. The original
80-byte callbacks and 112-byte world bridge remain ABI 2.

The object ID includes the scene load generation. This slice uses the prepared
control scene in `build/sdk-assets` (sourced from `C:/Stalker2_2011/xr2-dsgn/resources`).
Project persistence (`project.xprj`) is implemented in SDK-3. SDK-4 implements
authored scenes and multi-object command history; SDK-5 adds the viewport,
preview and authored Play/Simulate services described below.
The corrected `CommandEngine.cs` also owns the authored-scene commands. Domain
editors retain their later-stage command and native-service acceptance.

GPU validation uses a private asset copy from `build/sdk-assets` and hidden windows,
with nine fenced readbacks at a fixed simulation phase. The diagnostic drives the
original grid property descriptor, actual toolbar Undo/Redo handlers, and the full
SDK-2 IDE shell diagnostic (verifying all 4 primary panels, 8 active tools + 2
dormant routes, 21 render modes, DPI-invariant picking rays, `OptionsDialog`
rollback, off-screen layout clamping, and layout reset while preserving the live
Vulkan viewport HWND and `946x636` dimensions). After the first edit/undo/redo/undo
sequence it saves layout XML, moves the property panel left, reloads the saved
right layout, and repeats editing/history. Viewport HWND and dimensions, selection
and the redo branch must survive reload; all restored images are compared to the
first cycle. When `XRAY_SDK_CLOSE_PENDING=1` (`-ClosePending`) is enabled, step 9
also verifies pending preview query cancellation and drain before HWND destruction.

```powershell
cmake --build build/sdk-native-20261108-debug --target stalker2 xray_editor_world xray_sdk_ui -j 8
./sources/tools/sdk/test-sdk-property.ps1 -BuildRoot build/sdk-native-20261108-debug -FullIde -ClosePending -OutputRoot build/sdk3-new-debug-run
python ./sources/tools/sdk/verify-sdk-property.py build/sdk3-new-debug-run
# Regression: unchanged host mode, with a separate output directory
./sources/tools/sdk/test-sdk-property.ps1 -BuildRoot build/sdk-native-20261108-debug -LegacyHost -OutputRoot build/sdk3-new-legacy-run
```

The runner requires the existing prepared asset pack (`-AssetsRoot` overrides its
location), DXC and Vulkan validation deployment. Each run needs a new output
directory. Its runtime result and the separate capture comparison must both pass;
successful compilation alone does not prove native integration. `-FullIde` exercises
the production `WindowIde` host in background mode; project persistence and real
native resource queries are required evidence. Physical keyboard/mouse focus, DPI
and all interactive docking states need separate interactive coverage. Earlier
five-capture evidence predates the layout-reload checks.

## Source provenance

`upstream-files.json` records 418 restored controls/dialog/model/particle
source/resource files and four reference-only legacy project hashes from
`tsnest/xray-2-2011`, commit
`1795aa33a51fdde2ff5ea10627fc6d0b2b755c56`, with Git blob hashes. Their original
copyright headers are preserved. The four `.vcproj` files are archived under
`build/sdk-modernization/legacy-projects/`, outside active sources.
These legacy C++/CLI modules are reference
sources for migration and are not enabled as native MinGW targets.

The legacy docking implementation is not activated. Both the native host and
compatibility target use the pinned stable DockPanel Suite package. Toolkit runtime usage has been
migrated from the original Microsoft namespace to the current Xceed namespace.
Keep package notices and license metadata when packaging the SDK.

Only CS8981 is suppressed in the WPF project, preserving original public
lowercase names used by XAML and editor bindings. Other warnings are errors.

Upstream metadata:

- https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json
- https://github.com/dockpanelsuite/dockpanelsuite/releases/tag/Release_3.1.1
- https://api.nuget.org/v3-flatcontainer/extended.wpf.toolkit/index.json

## SDK-5 renderer services and authored runtime

Main and independent camera/model/texture/material views use typed Vulkan
scene/view/output services. Native silhouette masks, complete transform handles,
all 21 render modes, live texture reload and atomic incremental model edits are
implemented. Stable native HWND leases cover resize, DPI messages, minimize,
dock/float/redock, managed handle recreation, hide and GPU-acknowledged close.

Play/Simulate hands an authored model/light snapshot to the game logic owner,
with independent instances, pause/step/focus handling and exact return to editing.
Unresolved dependencies and unsupported domain kinds reject start explicitly.
Debug/Release pass CTest 6/6 and 18 managed groups; 24 sequential GPU workflows
pass validation 0/0 through teardown. SDK-5 is accepted; see
[scope, mapping and evidence](sdk5/README.md) and [receipt](sdk5/acceptance.json).

## SDK-6 asset authoring in progress

The first source texture/options workflow is implemented: native preflight,
independent draft preview, shared history, writable workspace save/reopen and
live level/model texture publication. See [scope and remaining work](sdk6/README.md).
Model/material/render-options editors and OBJ/Maya pipelines remain open.
