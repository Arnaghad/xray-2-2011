# SDK-6: model physics, skeletal preview і material schema/palette

Реалізація 2026-11-15. Загальний SDK-6 залишається відкритим; решта вимог
наведена в [README](README.md) і головному migration plan.

## Model physics

Model editor має collision list, Add/Duplicate/Remove та original property
grid для sphere, box і cylinder. Position задається відносно mass center,
rotation — у radians, box dimensions — half-sizes; cylinder використовує
half-length/radius. Зміна mass center зберігає placement primitives у model space.

`settings.rigid_body` охоплює mass, friction, restitution, damping, sleeping
thresholds і всі original additional-damping fields. Composite primitives
зберігаються окремо в `collision/primitives`. Обидва source files проходять
external-writer checks; failed publication компенсує запис обох файлів.
Undo, Revert і saved marker належать model document, окремо від level history.

Native immutable model snapshots містять physics description. Resource ABI
operations 9/10 готують і читають цей description. Source publication передає
validated snapshot у registry; DCC reimport зберігає authored physical properties.

Independent preview має власний Bullet world, floor і clock. Authored Simulate
створює тіла з актуальних model snapshots, крокує ними на game owner thread і
змінює placement runtime copies. Stop залишає authored level без simulation edits.
Physics placement приймає rigid rotation та uniform positive scale; non-affine,
sheared, reflected і nonuniform matrices явно відхиляються.

CPU contract перевіряє gravity/contact для трьох primitives, bounds,
scaled/rotated mass-center roundtrip, invalid-input retention та owner thread.
GPU workflow перевіряє private collision overlay, independent simulation,
source pair Save/Reopen, advanced properties у fresh native query, authored
Simulate з двома тілами та pending-close drain.

## Skeletal preview

Model editor показує named bones і bind positions. Animation URI запускає
engine cubic clip; Play/Pause і Bind pose керують independent model output.
Clip channels зіставляються з model palette за іменами після обчислення
hierarchical absolute transforms, включно з object-movement root.

Scene operation 26 перевіряє всі skin palettes до queue mutation. Renderer
одержує копію matrices і оновлює vertices, normals та culling bounds атомарно.
Некоректна pose утримує попередній mesh. Окремі outputs мають незалежні instances.
Bind inversion канонізує homogeneous column після перевірки affine source;
GPU comparison допускає максимум 2 значення 8-bit channel при цьому roundtrip.

Reference neutral model має 62 bones. Додатково з живої Maya 2027 повторно
експортовано T2 skin із трьома bones і clip; обидва працюють у Vulkan preview
в Debug/Release. Fixture preparation зберігає hashes дев'яти geometry files.
У копії pack нормалізовано legacy settings separators, прив'язано exported
skeleton hierarchy та задано minimal green `g_stage_default` для artist material.

Цей прогін виявив VFS case-folding дефект: folder `actor_matSG` ставав
`actor_matsg`. Native cook відновлює точний authored settings key після
однозначного зіставлення; ambiguous names явно відхиляються.

## Material schema та effect palette

Repair instance schema виправляє obsolete references, removed stages та
несумісні/locked overrides. Numeric int/float overrides сумісні між собою.
Repair створює окрему команду; Undo повертає початковий source стан.
Обов'язковий repair блокує Apply/Save до виправлення, без запису на disk.
Сумісні original instances з неактивними local metadata відкриваються без
обов'язкового переписування; canonical cleanup залишається явною дією.

Коли змінюється schema бази, dependent native resources у тому самому batch
готуються за новими model-override permissions. Source instance залишається
доступним для explicit repair/Undo. Publication не застосовує застарілий locked
override лише тому, що він ще присутній у source file.

Stage/effect palette читає 9 stage і 25 effect definitions як обмежені
data-only schemas. Generic property descriptors відображають bool/int/float,
texture strings, vector channels і model-override flags. Effect selection
зберігає сумісні authored values; stage removal та effect changes використовують
shared history. Production native cook перевіряє capabilities до publication.

## Відтворення

```powershell
python sources/tools/sdk/prepare-sdk6-material-fixtures.py --assets build/sdk-assets
cmake --build build/sdk-native-20261108-debug --target stalker2 xray_editor_world xray_sdk_ui xray_sdk_model_physics_contract -j 8
sources/tools/sdk/test-sdk-property.ps1 -BuildRoot build/sdk-native-20261108-debug -OutputRoot build/new-physics -FullIde -PhysicsCommands -ClosePending
python sources/tools/sdk/verify-sdk-physics.py build/new-physics
sources/tools/sdk/test-sdk-property.ps1 -BuildRoot build/sdk-native-20261108-debug -OutputRoot build/new-skeletal -FullIde -SkeletalCommands -ClosePending
python sources/tools/sdk/verify-sdk-skeletal.py build/new-skeletal
sources/tools/sdk/test-sdk-property.ps1 -BuildRoot build/sdk-native-20261108-debug -OutputRoot build/new-material -FullIde -MaterialCommands -ClosePending
python sources/tools/sdk/verify-sdk-material.py build/new-material
sources/tools/sdk/test-sdk-dcc.ps1 -BuildRoot build/sdk-native-20261108-release -OutputRoot build/new-dcc
```

`maya_live_t2_suite.py --output-dir <new-directory>` залишає свіжі exports/cooks.
`prepare-sdk6-maya-preview.py` створює окремий GPU pack із provenance.
Skeletal harness приймає `-AssetsRoot`, `-ModelUri` і `-AnimationUri` для цього pack.
Release використовує відповідний build root. Кожен workflow потребує нової
output directory; GPU workflows запускаються послідовно.

## Підсумкові докази (`build/sdk6-final-20261115/`)

- **CTest та UI:** Debug і Release по 23 групи `xray_sdk_ui_compatibility`, `14/14` non-host SDK/contract CTest (`ctest-debug-sdk-final.log`, `ctest-release-sdk-final.log`) та повні 30-тестові CTest прогони (`ctest-debug-accepted.log`, `ctest-release-accepted.log` + retry тимчасового блокування `WinError 5` інсталятора розширення Blender).
- **10 послідовних native Vulkan GPU workflows (validation `0 errors / 0 warnings`, pending-close drain):**
  - Physics authoring + authored Simulate: `debug-physics-advanced`, `release-physics`.
  - Skeletal reference (62 bones): `debug-skeletal-r2`, `release-skeletal`.
  - Live Maya 2027 T2 skeletal export → Vulkan preview (3 bones): `debug-maya-skeletal-r3`, `release-maya-skeletal` (`maya-t2`, `maya-gpu-assets-r2`).
  - Material schema repair + 9-stage/25-effect palette: `debug-material-palette-r2`, `release-material-palette`.
  - Blender B6 DCC live reimport regression: `debug-dcc`, `release-dcc-r2` (первинна спроба `release-dcc` зіткнулася з відомим інтермітуючим `WinError 5` під час перейменування `xray_blender@` → `xray_blender` і збережена для прозорості).
- Зведений маніфест із SHA-256 джерел, бінарників і доказів: `build/sdk6-final-20261115/summary.json`.
