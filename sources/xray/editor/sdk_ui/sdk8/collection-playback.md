# SDK-8 — виконання animation collections

Контрольна точка від 2026-11-16 розширює початковий source editor фактичним
collection playback. SDK-8 у цілому та повні inventory families ще не прийняті.
Початковий `initial-checkpoint.json` і його source snapshots залишаються окремими
історичними доказами. Поточний звіт: `collection-playback-checkpoint.json`.

## Поведінка

У collection editor вибрати root, nested collection або external reference та
натиснути `Preview collection`, потім `Play`. Усі унікальні clips спочатку
завантажуються через native resource session і перевіряються проти skeleton/model
palette. Лише після цього замінюються output, clip ownership і playback state.
Source/dependency changes або cancellation під час loading відхиляють candidate.
Невдала перевірка будь-якого clip зберігає попередній preview.

- Sequential parent утримує unfinished child і переходить до наступного item,
  коли child повертає terminal emission. Cyclic child може утримувати parent
  необмежено довго — як `body` в оригінальному guitar fixture.
- Random parent обирає child на кожній emission. Noncyclic random завершується
  після terminal emission вибраного child; cyclic random продовжується.
- `is_dont_repeat_previous=true` виключає повтор попереднього child index.
  Для одного child повтор дозволено, щоб не утворити нескінченний selection loop.
- Збережені external references поділяють emitter state одного normalized source
  URI. Inline collections мають власний state. Playback копіює дерево; зміни draft
  застосовуються після повторного `Preview collection`.
- Rate переносить залишок часу через межі clips; finite sequence зупиняється на
  точному останньому sample. Pause зберігає час. Play після завершення і Stop
  перезапускають policy state та seed. Scrub працює в межах активного clip.
  `Loop clip` застосовується до single-clip preview; collection використовує свої
  policies. Статус показує поточний clip, час і завершення.

Вибір спирається на `animation_collection::emit_impl`, editor emitter і native
`math::random32` (`0x08088405 * seed + 1`, seed 0 за замовчуванням).
Враховано відмінність старого `animation_collection_cook::new_collection`: він
передає `is_dont_repeat_previous` прямо як `can_repeat_successively`, інвертуючи
сенс поля. Тут використано значення поля з editor emitter: true забороняє repeat.
Ця контрольна точка переносить selection/termination logic у managed controller,
а poses і skeletal mesh output отримує від наявного native runtime.

Переходи між clips зараз без blend. Mixer-driven transitions, root-motion
accumulation і viewer graph залишаються наступними роботами SDK-8.

## Обмеження та перевірки

Source closure обмежена 4096 expanded nodes, depth 32, 128 external sources і
128 unique clips. Повторні cached references також проходять depth/count bounds.
Один clock step допускає до 4096 transitions; перевищення відновлює попередні
selection, RNG state і час. Усі resource handles звільняються на replacement,
cancellation і close.

Managed contracts перевіряють nested completion, exact boundaries, finite/random
termination, no-repeat sequence, singleton, shared references, cached depth bounds,
clock overflow rollback, immutable playback snapshots, source races та handle
ownership. Native дев'ятикроковий workflow використовує справжні intro/body/outro
clips і `neutral_01.skinned_model`: cyclic hold, rate carry, draft isolation,
policy save/reopen, finite completion, mapping failure retention, 16 random
transitions і свіжі native samples для всіх 63 channels. Readbacks порівнюють
collection output з незмінними level і bind-pose output та зіставляють однакову
outro pose, отриману sequential і random policies. Pending-close перевіряє
callback drain до resource destruction.

```powershell
ctest --test-dir build/sdk-native-20261108-debug --output-on-failure -L sdk
ctest --test-dir build/sdk-native-20261108-release --output-on-failure -L sdk

# Нові output directories; GPU workflows виконувати послідовно.
& sources/tools/sdk/test-sdk-property.ps1 -BuildRoot build/sdk-native-20261108-debug -AssetsRoot build/sdk-assets -OutputRoot build/sdk8-collection-playback-new -FullIde -AnimationCommands -CollectionPlayback -ClosePending -TimeoutSeconds 300
python -X utf8 sources/tools/sdk/verify-sdk-collection-playback.py build/sdk8-collection-playback-new
```

Журнали, captures і source snapshots: `build/sdk8-collections-20261116`.

Фінальні Debug/Release збірки — без warnings/errors; SDK CTest — `5/5` в обох
конфігураціях, включно з `25` managed compatibility groups. GPU workflows
`gpu-debug-final` і `gpu-release` — по 27 readbacks, 20 точних збігів і 5 змін pose,
`M8 validation errors=0 warnings=0`. Sample timings: intro `1 s`, body
`3.200000047683716 s`, outro `1.7333333492279053 s`. Finite sequence завершується
на кінці outro; random no-repeat проходить 16 переходів. SHA-256 evidence і source
snapshots перелічені в `collection-playback-checkpoint.json`.
