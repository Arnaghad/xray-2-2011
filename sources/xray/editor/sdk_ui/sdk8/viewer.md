# SDK-8 — viewer graph та interval documents

Контрольна точка 2026-11-16: flat `.avs` graph documents і `.anim_clip` interval
documents, steady weighted full-pose targets через native samples та незалежний
skeletal output. SDK-8 і повна родина `animation-viewer` ще відкриті. Поточний звіт:
`viewer-checkpoint.json`; попередні контрольні точки та snapshots збережені.

## Редагування

`Tools → Animation editor` відкриває `.anim_collection`, `.avs` і `.anim_clip`;
доступний також `Resources → Edit source`. Нові файли створюються через `New animation
viewer` та `New interval clip` у Tools. Запис відбувається у workspace.

Редактор використовує оригінальний WPF `hypergraph_control`, property grid і спільний
`CommandEngine`. Є animation/addition/target nodes, group movement, link creation,
weights, target playing time, видалення nodes з incident links, node offset/time
scale/cycle interval та interval sequence. Ці операції мають Undo/Redo і saved marker.
Порожні animation nodes дозволені як drafts; preview пояснює відсутність intervals.

Interval посилається на partition ID із `data.event_channels/anim_intervals` у
`.clip_data` або містить explicit offset/length у секундах. Knots переводяться з frames
через оригінальний `default_fps=30`. Без metadata ID 0 означає повний clip; інші IDs
відхиляються. Bounds перевіряються проти cooked duration. `.anim_clip` зберігає власну
`clip.intervals` схему, без додавання viewer graph.

Save перевіряє native dependencies, interval bounds, source/dependency writers і
cancellation, потім атомарно записує файл. Помилка зберігає попередній файл і dirty
state. Source editing блокується під час Play/Simulate та на чужому потоці. Dirty
close має Save/Discard/Cancel; documents/outputs закриваються до resource session.

## Preview

Вибрати node й натиснути `Apply target preview`. Усі clips і skeleton mapping
перевіряються до публікації candidate. Source snapshot відділений від draft edits;
clock чекає готовності output. Є Play/Pause/Stop, scrub, rate і timeline loop.
Standalone clip зберігає останній interval sample на endpoint.

Addition множить weights уздовж expression і нормалізує full-pose contributions.
Poses надходять із native cubic sampler. Managed mixer переносить формули
`bone_matrices_computer.cpp`: linear translation, `to_close_xyz` Euler rotation
і geometric scale. Analytical contracts перевіряють scale, angle wrapping та
equivalent Euler representation. Це не приймання всього legacy mixer.

З 2026-11-18 preview виконує timed target links. `playing_time` задає час до
перемикання з поточного target. Цикли розгортаються в межах тривалості сцени.
Під час blend вихідні clips продовжують відтворення, а нові починають зі свого
offset. Спільний animation node зберігає свій годинник. Наступний target може
перервати blend, і нові weights обчислюються від поточних. Scrub, playback і
reopen використовують одну шкалу часу.

Для weighted link можна змінити `Interpolator`, `Blend duration (s)` і
`Epsilon` у property grid. Підтримуються Instant, Linear та Fermi. Для
вкладеного addition діє найближчий до animation node заданий інтерполятор.
За відсутності кривої на link preview читає `viewer_options.interpolator` з
`.options` активного clip або використовує Fermi 0,3 с з epsilon 0,005.
Зміни записуються у формат `.avs` та мають Undo/Redo.

Preview виконує прямі synchronization links у форматі оригінального `.avs`.
Стрілка йде від driven node до driving node. Під час відтворення target driven
clip бере номер і нормалізовану фазу інтервалу driver. Ту саму фазу він застосовує
до partitions свого першого clip з `.clip_data`. Driver може керувати часом із
нульовим внеском у позу. Вибір самого driven node для preview залишає його
звичайним solo clip із власним часом і заданими intervals.

Пара використовує однаковий невід'ємний `viewer_options.synchronizing_group`
у `.options` перших clips і однакову кількість інтервалів. Без partition metadata
цілий driven clip утворює один інтервал. За зміни driver в одному group timed
target зберігає номер і фазу інтервалу. Новий driver далі використовує свій
time scale. Вибраний link показує driving і driven node у property grid.

Legacy `actors/.../model_name` відображається на portable
`resources/models/.../model_name/model_name.skinned_model`. Reference model name
не переписується. Невдалий mapping/candidate зберігає попередні handles, output і час.

## Межі та reference findings

- Ланцюги, де driving node сам є driven, потребують окремого перенесення. Кілька
  driven nodes можуть посилатися прямо на один driver.
- Additive layers, interpolation of time scales, accumulated root
  motion, multi-model transforms і решта viewer UI inventory потребують перенесення.
- Preview переносить час між targets за ID animation node. Поведінка оригінального
  mixer для різних nodes з однаковими animation intervals та складних добутків
  кривих на кількох weighted links потребує окремої звірки.
- Із 18 reference `.avs` 15 проходять flat schema. `anim_seve_01.avs` потребує
  legacy nested `childs` conversion; `anim_seve.avs` і `free.avs` мають duplicate
  links і потребують явного import/repair рішення. Sources не виправляються автоматично.
- П'ять відновлених walk/run clips мають 62 channels без потрібного `neutral_01`
  bone `Root`. Деякі старі partition knots виходять за recooked duration. Clip
  `human/common/free/walk/middle/free_walk_arc_fwd_left_idle_1` із `single.avs`
  не знайдено у локальному reference pack. Ці дані не зараховані за original playback.

Позитивний GPU workflow відкриває оригінальний `jes_test.avs`, **явними authoring
commands** від'єднує synchronization і задає intervals сумісних оригінальних guitar
intro/outro. Він перевіряє weights 0.5/0.5, 1/0, 0/1, source undo/save/reopen,
Unicode `.anim_clip`, reorder, pause/stop, missing clip/mapping retention та lifetime.
Основна сцена й bind-pose output незалежні від viewer.

## Відтворення

```powershell
# Нові output directories; missing reference явно записується в provenance.
python -X utf8 sources/tools/sdk/prepare-sdk8-viewer-fixtures.py --output build/sdk8-viewer-fixtures-new --cli build/sdk-native-20261108-debug/binaries/xray_asset_cook_cli.exe
ctest --test-dir build/sdk-native-20261108-debug --output-on-failure -L sdk
ctest --test-dir build/sdk-native-20261108-release --output-on-failure -L sdk
& sources/tools/sdk/test-sdk-property.ps1 -BuildRoot build/sdk-native-20261108-debug -AssetsRoot build/sdk8-viewer-fixtures-new/assets -OutputRoot build/sdk8-viewer-gpu-new -FullIde -AnimationCommands -ViewerGraph -ClosePending -TimeoutSeconds 300
python -X utf8 sources/tools/sdk/verify-sdk-viewer.py build/sdk8-viewer-gpu-new
# Для Release змінити BuildRoot та OutputRoot. GPU workflows — послідовно.
```

Evidence, logs та snapshots: `build/sdk8-viewer-20261116`.

Для перевірки timed transitions використайте `-ViewerGraph -ViewerTransitions`
разом із `-FullIde -AnimationCommands -ClosePending`. Перевірити кадри можна
тим самим `verify-sdk-viewer.py`. Докази переходів зберігаються у
`build/sdk8-transitions-20261118`. У Debug і Release пройшли 5/5 SDK CTest та
Vulkan workflow для циклу з двох targets. По 27 кадрів у кожній конфігурації
дали 19 точних збігів і 6 виміряних змін пози. Кадр посеред blend збігся після
playback, scrub та повторного відкриття. Vulkan validation зафіксував 0 помилок і
попереджень. Перевірка pending-close підтвердила завершення запитів до знищення
ресурсів. Підсумок і SHA-256 записано в `build/sdk8-transitions-20261118/result.json`.

Для перевірки synchronization замініть `-ViewerTransitions` на
`-ViewerSynchronization` і використайте підготовлений `build/sdk-assets`.
Цей workflow зберігає напрямок sync link із `jes_test.avs` і задає в тестовому
workspace сумісні clips, групу та partitions різної тривалості.
Debug і Release пройшли всі 5 SDK CTest, включно з перевіркою перенесення фази
при зміні driver. Vulkan підтвердив 20 точних збігів кадрів і 4 зміни пози
в кожній конфігурації. Validation зафіксував 0 помилок і попереджень.
Підсумок і SHA-256 записано в `build/sdk8-sync-20261118/result.json`.

Фінальні Debug/Release збірки — без warnings/errors; SDK CTest — `5/5` у кожній
конфігурації, включно з `26` managed UI groups та окремим output-loading clock
контрактом. Vulkan `gpu-debug-r2` і `gpu-release-r2`: по 27 readbacks, 21 точний
збіг і 4 зміни pose, `validation errors=0 warnings=0`, pending-close drain до
resource destruction. П'ять clips перевірено проти original splines на кожному
authored frame (maximum channel error < `0.0052`). 20 нових clip/metadata files
додано в `build/sdk-assets` без перезапису попередніх файлів;
`published-fixtures.json` містить їх hashes. Початкові невдалі прогони збережені
окремо; фінальні результати й source snapshots перелічені у `viewer-checkpoint.json`.

## Фінальне закриття SDK-8 (2026-11-19)

Фінальне приймання всіх 5 родин `SDK-8` (включно з явним ремонтом `anim_seve_01.avs`,
`anim_seve.avs` та `free.avs`, інтерполяцією `time_scale`, `mixing_type`, `scene.models`,
`object_movement` root motion, crossfade у колекціях, `.clip_data` setup,
controller groups та level animation placement/curves) зафіксовано у
`sources/xray/editor/sdk_ui/sdk8/acceptance.json` (`build/sdk8-acceptance-20261119/`).
