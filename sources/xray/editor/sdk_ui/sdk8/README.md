# SDK-8 — початкова реалізація animation authoring

Стан на 2026-11-16: SDK-8 розпочато. Реалізовано редагування оригінальних
`.anim_collection`, незалежний clip preview та виконання nested/reference
collection policies. Поточні правила й evidence описані в [collection-playback.md](collection-playback.md).
Додано flat `.avs` та `.anim_clip` documents зі спільним WPF graph, Undo/Redo,
source save/reopen, interval/partition editing і native-sampled weighted target
preview. Поточний обсяг, reference findings і remaining gaps: [viewer.md](viewer.md).
З 2026-11-18 viewer виконує timed target links і переходи Instant, Linear та
Fermi. Налаштування weighted links редагуються в property grid.
Прямі synchronization links підтримують спільну фазу інтервалів у viewer
та її перенесення при зміні driver в одному group.
Початкова контрольна точка зберігається в `initial-checkpoint.json`. Усі п'ять
родин SDK-8 у `sdk0/function-matrix.json` залишаються неприйнятими як повні workflows;
етап SDK-8 ще не завершений. Прийняті SDK-6 та SDK-7 збережені.

## Що реалізовано

- Документ колекції читає оригінальну Lua-схему: single items `512`, вкладені та
  зовнішні collection/composite items `513/514`, `random`/`sequential`, cyclic і
  don't-repeat flags. Невідомі поля зберігаються. Є додавання, видалення,
  перестановка, зміна reference і policies, власний `CommandEngine`, Undo/Redo,
  saved marker, Revert та dirty-close Save/Discard/Cancel.
- Перед збереженням перевіряється вся залежність колекції: вкладені reference,
  цикли, обмеження розміру/глибини, зміни зовнішнього source і кожен унікальний
  native clip. Запис у writable workspace атомарний; missing resource, cancellation
  або зовнішня зміна залишають попередній файл і dirty draft. Reference assets
  не перезаписуються. Редагування джерел блокується під час Play/Simulate.
- Preview використовує справжні native animation resources і cubic samples,
  skeleton hierarchy та model bone palette. Власний Vulkan model output має
  Play/Pause/Stop, scrub, loop і rate. Він не змінює authored level або інший preview.
  Заміна clip/model публікується після перевірки mapping; невдала заміна зберігає
  попередні clip, output і час. Закриття звільняє clip та output до resource session.
- `Tools → Animation editor` відкриває `.anim_collection`; `Tools → New animation
  collection` створює новий workspace source. Доступний також `Resources → Edit source`.
  У формі слід вибрати single clip item і натиснути `Preview selected clip`.
  Контейнери показують policies в оригінальному WPF property grid;
  `Preview collection` виконує їх із переходами між native clips.

## Reference assets і відтворення

`prepare-sdk8-fixtures.py` копіює 31 оригінальний `.anim_collection`/`.avs`/`.anim_clip`
із read-only reference resources. Три відсутні portable clip binaries відновлюються
через прийнятий legacy decoder та C++ cooker з оригінальних exported splines.
Порівнюються всі канали на кожному authored frame: максимальна похибка менша
`0.0052` у одиницях відповідного каналу. Provenance містить hashes, frame/bone counts
і похибки. Flat viewer/interval documents описано у [viewer.md](viewer.md); повний viewer inventory ще не прийнятий.

Перевірений приклад: `resources/animations/collections/sit_high_guitar_play.anim_collection`,
вкладений `body` clip і default `neutral_01.skinned_model`. Reference `inspect_rnd_1`
не містить bone `Root`, потрібного цій моделі: preview коректно відхиляє таку пару.

37 нових файлів (31 source, 3 raw clips, 3 converted clips) опубліковано у
`build/sdk-assets` зі збереженням усіх попередніх файлів. Список і hashes:
`build/sdk8-implementation-20261116/published-fixtures.json`.

```powershell
# Кожний запуск потребує нового output directory.
python -X utf8 sources/tools/sdk/prepare-sdk8-fixtures.py --output build/sdk8-fixtures-new --cli build/sdk-native-20261108-debug/binaries/xray_asset_cook_cli.exe

ctest --test-dir build/sdk-native-20261108-debug --output-on-failure -L sdk
ctest --test-dir build/sdk-native-20261108-release --output-on-failure -L sdk

& sources/tools/sdk/test-sdk-property.ps1 -BuildRoot build/sdk-native-20261108-debug -AssetsRoot build/sdk8-fixtures-new/assets -OutputRoot build/sdk8-animation-debug-new -FullIde -AnimationCommands -ClosePending -TimeoutSeconds 240
python -X utf8 sources/tools/sdk/verify-sdk-animation.py build/sdk8-animation-debug-new
# Для Release змінити BuildRoot та OutputRoot; GPU workflows виконувати послідовно.
```

## Перевірки початкової контрольної точки

Debug та Release збираються без помилок. SDK CTest пройшов `5/5` у кожній
конфігурації, включно з `25` managed UI compatibility checks на .NET 11.0.0.
Фінальні GPU workflows `gpu-debug-final` і `gpu-release` пройшли по 9 кроків
із 27 readbacks: 22 точні збіги та 2 зміни pose; Vulkan validation —
`errors=0 warnings=0`. Перевірено pending-close drain та порядок teardown.

Build logs, fixture provenance, GPU readbacks і source snapshots знаходяться у
`build/sdk8-implementation-20261116`. Підсумкові hashes та результати записані в
`initial-checkpoint.json` поруч із цим документом. Managed checks охоплюють source
round-trip, вкладені references, Unicode, no-op/history, cancellation, external
writers, runtime lock, actual form binding, dirty close, clock і handle ownership.

Дев'ятикроковий native workflow перевіряє original nested source, scrub, pause,
rate, loop, reorder/policy save, undo/save/reopen, відхилення skeleton mismatch,
close/reopen та missing clip без зміни збереженого source. Свіжий engine query
порівнюється з controller sample за часом і всіма bone channels. Readbacks доводять
незалежність mesh preview: 22 точні збіги, дві зміни pose; pending-close та порядок
APC/resource shutdown перевіряються окремо. Це selected-clip preview; виконання
вкладених collection policies не зараховується цією перевіркою.

## Що далі до повного SDK-8

1. Повна звірка collection UI inventory та mixer-driven transitions/root motion.
2. Viewer: chained synchronization, time-scale interpolation, additive layers,
   legacy imports, multi-model transforms і повна звірка UI inventory.
3. Animation setup: channels, partitions, events, intervals і curves; редагування
   з Undo/Redo та source round-trip.
4. Groups/controllers та locomotion/mixing integration з оригінальними fixtures.
5. Level animation chooser, curve/property bindings і placement workflow.

Кожна родина потребує власного reference workflow і runtime evidence до зміни
`runtimeWorkflowAccepted` або позначення SDK-8 завершеним.

## Закриття етапу SDK-8 (2026-11-19)

Усі 5 родин функцій `SDK-8` (`animation-collections`, `animation-viewer`,
`animation-setup`, `animation-groups`, `animation-placement`) реалізовано,
перевірено в Debug і Release та прийнято у `sources/xray/editor/sdk_ui/sdk8/acceptance.json`:

- **Viewer та Collection playback (`SdkAnimationViewerGraph.cs`, `SdkAnimationViewerDocument.cs`, `SdkAnimationViewerEditor.cs`, `SdkAnimationPlayback.cs`, `SdkAnimationPreview.cs`, `SdkAnimationCollectionEditor.cs`)**:
  - Явний імпорт/ремонт legacy nested `childs` (`anim_seve_01.avs`) і дедуплікація повторюваних `(src_id, dst_id)` посилань (`anim_seve.avs`, `free.avs`) без зміни read-only еталонів;
  - `time_scale` інтерполяція (`ViewerInterpolation`) та `mixing_type` (0/1), редагування `scene.models` із Undo/Redo та акумуляція `object_movement` root motion;
  - Mixer crossfade переходи між clips у `SdkAnimationCollectionCursor` та `SdkAnimationPreview`.
- **Animation setup (`SdkAnimationSetup.cs`)**: `.clip_data` (`data.event_channels`: `intervals=0`, `partitions=1` із `save_first_as_last`, `events=2`, knots у кадрах при 30 FPS та domains) і `.options` (`viewer_options`), WPF `animation_setup_host`, `CommandEngine` Undo/Redo, інтервальний preview та атомарний save/reopen.
- **Animation groups & controllers (`SdkAnimationGroups.cs`, `fixtures/controllers/`)**: завантаження, редагування та атомарне збереження controller sets (`.lua` + `.options`) і crouch animation group configs (`groups/crouch/*.lua`), WPF `hypergraph_host`, резолв інтервалів через `.clip_data`, preview і явна діагностика відсутнього AI/navigation runtime (`NavigationRuntimeAvailable = false`).
- **Level animation placement (`SdkAnimationPlacement.cs`, `ProjectPropertyAdapters.cs`)**: ресурс-chooser `tool_animations` (`.clip`/`.anim_collection` без `controllers`, `skeletons`, `viewer_scenes`), прив'язка `animation_source`/`animation_loop`/`animation_speed` та 7-канальних `float_curve` (`Position X/Y/Z`, `Rotation X/Y/Z`, `Time`) до `WindowIde.AnimationCurveEditorPanel.CurveEditorControl` через `Project.Commands` Undo/Redo і `.xprj` roundtrip.
- **Верифікація (`build/sdk8-acceptance-20261119/`)**:
  - Збірка Debug і Release (`stalker2 xray_editor_world xray_sdk_ui`): `0 Warning(s)`, `0 Error(s)`.
  - `ctest -L ^sdk$`: Debug `5/5` і Release `5/5` (включно з `29` UI compatibility checks у Debug/Release).
  - Повний `ctest --repeat until-pass:3`: Debug `31/31`, Release `31/31`.
  - Послідовні Debug і Release Vulkan GPU workflows `-ViewerAdditive`: `PASS: viewer additive/chained sync; 19 exact readback matches, 5 pose changes; Vulkan validation errors=0 warnings=0`, pending-close drain, ordered `M8 shutdown`, та прапорці `setupVerified`, `groupsVerified`, `placementVerified`, `legacyRepairVerified`, `rootMotionVerified`, `collectionCrossfadeVerified`, `timeScaleInterpVerified`.
  - Відтворення та перевірка: `python sources/tools/sdk/collect-sdk8-acceptance.py --verify`.
