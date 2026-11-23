# SDK-5 — renderer services та authored Play/Simulate

SDK-5 прийнято 2026-11-12. Debug/Release проходять CTest 6/6 та 18 managed
compatibility groups. Усі 24 послідовні GPU workflows пройшли fenced readback,
regression controls і teardown з Vulkan validation 0/0.
Повний receipt із source/binary/evidence hashes: [acceptance.json](acceptance.json).
Попередні [main-viewport.json](main-viewport.json) та [gizmos.json](gizmos.json)
залишаються незмінними історичними checkpoint reports.
Mapping original calls → typed API: [renderer-mapping.json](renderer-mapping.json).

## Viewports і renderer

- Main viewport передає camera/projection, grid, cooked selection bounds,
  editor lights, culling freeze і postprocess через versioned typed services.
  Look/orbit/pan/dolly/fly/focus використовують production input handlers.
- Silhouette compositor малює exterior band навколо selected mesh mask з
  alpha cutout, color/width та source pulse `sqrt(abs(cos(time * rate)))`.
  Bounds — окремий optional guide. WPF/native-HWND overlay не використовується.
- XYZ move/rotate/scale, uniform, XY/YZ/ZX planes, camera-plane move/scale,
  camera ring та arcball мають спільну native draw/pick geometry.
  Frozen-origin ray constraints викликають original transform kernels.
  Один gesture — один command; Escape, undo/redo та save/reopen точні.
  Світовий scale, який створює shear поза TRS, явно відхиляється.
- Усі 21 modes мають real routes: physical wireframe/two-sided, unlit/lit,
  normals, specular intensity/power, mip level, lighting/diffuse/specular,
  shader/texture/geometry complexity, emission, distortion, AO, unlit+AO,
  HDR histogram, overdraw та indirect environment lighting.
  Shader complexity рахує SPIR-V function instructions; geometry complexity
  оцінює triangles/projected bounds area. Це не GPU timing counters.
  Indirect використовує current environment/ambient, без заяви про LPV/GI.
- Camera view має незалежні camera/view/output зі shared authored scene;
  після close project він порожній. Selected model/texture/material URI
  створюють незалежні previews. Material використовує explicit preview primitive;
  model не підміняється фіксованим woodbox. Framing отримує actual cooked bounds.
  Mouse orbit/look/pan/dolly змінює тільки відповідну camera. Cook failure видимий.

## Hosting, reload і lifetime

`SdkViewportHost` утримує окремий native child HWND: managed handle recreation
паркує leased surface до знищення parent і reattach до нового HWND.
Explicit close звільняє surface після renderer/GPU acknowledgement;
hide лише припиняє submissions. GPU workflows перевіряють main/preview resize
і exact restore, actual HWND recreation, DockPanelSuite float/redock,
WinForms `WM_DPICHANGED` 96→144→96, native minimize/restore, hide,
close/reopen та scene switch. Це in-process перевірки прихованих HWNDs,
без заяви про physical monitor migration або manual desktop smoke.

Source texture reload реально оновлює level/model/texture preview.
Пошкоджений source повідомляє failure і зберігає попередні GPU resources;
valid reload точно відновлює зображення. 64 transform submissions вимірюються
з нульовими новими scene/model cooks: кожна зміна публікує один atomic batch.
Це CPU submission profile, не large-level frame-time acceptance.
Asset authoring/import/reimport залишається SDK-6.

Shutdown з held query перевіряє rejection після session close, живі HWNDs до
GPU drain, release resources до cook teardown та file producers до module
release. Active Play shutdown вимикає paired game/editor frame handshake
перед final channel drain.

## Play/Simulate

Поточна authored scene, включно з незбереженими transforms, передається через
`engine_world::editor_runtime` на справжній logic owner. `editor_game_world`
отримує незалежні model instances/scene/view, immutable cooked assets,
authored lights та environment/postprocess snapshot. Він не завантажує M8
manifest/control scene. Main output lease спільний; camera preview продовжує
показувати editor scene.

- `F5` — Play/return; `Shift+F5` — Simulate/return.
- `F6` — pause/resume; `F7` — один simulation step; `Shift+F6` — Stop.
- Game ticks/time належать logic thread. Focus loss заморожує game time,
  продовжуючи render/end-frame; focus return відновлює ticking.
- Editing/history/selection mutations блокуються. Runtime camera не переписує
  authored metadata. Stop повертає editor camera/input, object state, history
  та saved marker; runtime state не потрапляє в project.
- Виконуються поточні model/light kinds. Unresolved dependencies і ще не
  відновлені domain kinds явно відхиляють start. Їхні modules входять до
  відповідних SDK-6…SDK-11; успішний shell-only checkbox не використовується.

## Відтворення

```powershell
./sources/tools/sdk/test-sdk5.ps1 -OutputRoot build/sdk5-new-acceptance
python sources/tools/sdk/collect-sdk5-acceptance.py --verify
```

Runner збирає Debug/Release, виконує CTest та 18 managed groups, потім
послідовно запускає 12 GPU workflows у кожній конфігурації: runtime, hosting,
modes, outputs, selection, advanced/original gizmos, viewport, scene,
property/legacy M8 regressions та reload. Readbacks отримуються після
presentation fences. History/reopen restores порівнюються точно;
M8 property/legacy images і SDK-4 saved transforms лишаються regression controls.
Collector викликається один раз перед першим прийняттям і не перезаписує report.
Він перевіряє binaries/sources/evidence hashes, validation 0/0 та teardown
до file devices/module release.
