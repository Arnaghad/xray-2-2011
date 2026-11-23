# SDK-4 — scene editing, actions and history

Фінальні докази й hashes: [acceptance.json](acceptance.json).
Evidence root: `build/sdk4-native-20261110/`. Попередні command-foundation та
project-command checkpoints збережені як історичні результати.

## Робочий сценарій

New/Open створює native scene з authored static models та point lights. Tools
library → Insert/double-click створює об'єкт; Project tree та viewport mesh picking
синхронізують selection й inspector. Create/delete/duplicate, typed clipboard,
group/ungroup/reparent, rename, visibility, properties і transform gestures
використовують один `WindowIde.Commands`.

У viewport: Q — selection, W/E/R — translate/rotate/scale; X/Y/Z — axis,
U — uniform scale. Ліва кнопка вибирає об'єкт і тягне його по поточній осі;
Ctrl-click додає/прибирає selection. Arrow keys виконують transform step;
Escape або втрата capture/focus скасовує preview. Ctrl+Z/Ctrl+Y відновлюють
стан; текстові редактори зберігають власні shortcuts.

Coordinate space перемикає World/Local. Grid snap використовує scene grid step;
object/face snap — найближчу triangle intersection, vertex/edge snap — вершину
або проєкцію на ребро цієї грані. Hidden та ignored objects не pickable;
snap виключає поточну selection. Native picking працює з фактичними vertices й
indices cooked model, а не з вигаданим screen-plane hit.

## Native bindings і transaction safety

`sdk_scene_bridge.h` — окремий ABI 1: callbacks 24, request 48, object 72,
transform 68 bytes. Host ABI 2, property ABI 1 і resource ABI 1 збережені.
Owner thread, version/size, session, IDs, paths, finite values, nonzero scale,
ray normalization та transform operands перевіряються до commit.

Candidate scene готує всі ресурси до зміни активної scene pointer. Кожний
candidate отримує незалежні native model instances; clones використовують
immutable asset data попереднього стану. Відхилений cook/enqueue не змінює live
instances. Main view/output/HWND зберігаються; camera й projection беруться з
тієї самої `SceneViewPanel`, що рахує mouse rays.

`CommandEngine.Synchronize` застосовує native candidate перед публікацією history
або зміною undo/redo cursor. Відмова компенсує managed command; redo branch,
retained IDs, selection та saved marker зберігаються. Preview показує зміни без
нової history entry; accept створює один undo unit, cancel повертає origin.
Помилка компенсації блокує commands до session reset. New/Open/Close готують
native state до очищення поточної session/history.

Transforms викликають **наявні native** `transform_control_translation`,
`transform_control_rotation`, `transform_control_scaling` через
`one_step_execute`/`calculate`; C# placeholders не ввімкнені у build. Виправлено
axis-specific scale та local translation у native one-step route. Pivot і signed
scale зберігаються. Managed rotation math враховує знак original X-Ray Euler angles.
World-axis scale, що створив би shear, відхиляється: TRS serialization не може
зберегти таку matrix без втрати. Для цього випадку працюють Local або uniform scale.

## Перевірка

- Debug/Release: production native/managed targets; managed build без warnings/errors;
  CTest 5/5, compatibility 14/14 груп у кожній конфігурації.
- Окремі fault-injection checks: commit/undo/redo/preview compensation, ID/redo/saved
  state preservation, failed New/Open/Close та recovery після compensation failure.
- Authored-scene GPU diagnostic у full IDE: independent instances, point light,
  create/delete/undo, duplicate/group/reparent, mesh pick/snap, failed cook, pivot
  із mirrored scale, native transforms у local/world spaces та visibility.
  Mouse events проходять WinForms `OnMouseDown/Move/Up`, keys — `OnKeyDown` й
  `InputEngine`. Приховані HWNDs використовують звичайний Enter/context route;
  фізичне desktop input не симулюється і manual smoke не заявляється.
- Дев'ять fenced Vulkan readbacks: displacement/rotation/scale/visibility видимі;
  undo/redo, Escape cancel, visibility undo та Cyrillic save/reopen відновлюють
  кадри точно. Другий instance залишається незмінним. Pending preview query
  скасовується, native resources drain відбувається до HWND teardown.
- Окрема регресія попереднього property/resource/layout workflow та legacy host.

Відтворення для кожної конфігурації (використовуйте новий OutputRoot):

```powershell
cmake --build build/sdk-native-20261108-debug --target stalker2 xray_editor_world xray_sdk_ui -j 8
ctest --test-dir build/sdk-native-20261108-debug --output-on-failure
./sources/tools/sdk/test-sdk-property.ps1 -BuildRoot build/sdk-native-20261108-debug -FullIde -SceneCommands -ClosePending -OutputRoot build/sdk4-scene-new
python sources/tools/sdk/verify-sdk-scene.py build/sdk4-scene-new
```

## Межа з наступними етапами

SDK-4 приймає базове scene authoring. Повні viewport services, native gizmo
geometry/draw/axis picking, navigation, outlines/grid/overlays/render modes,
preview docking/reload та performance належать SDK-5. Поточний gesture binding
використовує mesh drag/axis keys і native control calculation.

Missing model dependencies та tool-specific objects показуються selectable
locators; specialized light types також залишаються locators. Їхні raw/unknown
fields зберігаються. Terrain і root scripting scene залишаються protected;
terrain snapping вимкнено у native editing session до підключення terrain tool.
Domain editors/cooks мають окремі acceptance stages. Поточна атомарна синхронізація
перебудовує candidate scene на edit; великі сцени потребують оптимізації renderer
batch service у SDK-5. Whole-scene snapshots в undo history не використовуються.
