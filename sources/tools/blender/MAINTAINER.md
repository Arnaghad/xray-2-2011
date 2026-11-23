# Maintainer: випуск, міграція та діагностика Blender pipeline

Команди нижче виконуються з кореня checkout у PowerShell. Робочий Windows host —
встановлений **Blender 5.3.0**; поточний ZIP — **0.3.2**. Технічний опис state,
fingerprints і reconciliation: [MIGRATION.md](MIGRATION.md). Художнику передайте
[гайд у ZIP](xray_blender/README.md) та [SDK workflow](SDK.md).

## Зібрати й передати extension

```powershell
python -X utf8 sources/tools/blender/build_extension.py
```

Результат: `build/blender/extension/xray_blender-0.3.2.zip`, сусідні `.sha256`
та `.json` із hashes усіх members. У ZIP є README, license і потрібний
`dcc_common`. Передавайте ZIP, його SHA-256, cooker із відповідної SDK-збірки
та resource dependencies проєкту. Новий package ID/version береться з
`xray_blender/blender_manifest.toml`; не перезаписуйте прийняті старі ZIP.
0.3.2 оновлює документацію; Python-код усередині ZIP збігається з 0.3.1.

Під час оновлення SDK виберіть новий ZIP у **DCC options**. Збережений шлях до
наявного 0.3.1 сам не перемикається: discovery підбирає пакет для порожнього
налаштування. Окремий SDK-profile не змінює Preferences звичайного Blender.

Перевірка встановлення, save/reopen і runtime contracts:

```powershell
python -X utf8 sources/tools/blender/run_ci.py --build-root build/sdk-native-20261108-release --configuration Release --maya auto --artifacts build/my-dcc-ci
```

Для кожного прогону використовуйте новий artifacts directory. Дивіться
`result.json`, JUnit і receipts; точний перелік та Maya AUTO/REQUIRED/OFF — у
[CI.md](CI.md). Відсутність host не зараховується як успішний Blender suite.
Локально підготовлений workflow ще потребує Git remote та runner для запуску
на GitHub. Команда повного native SDK reimport приймання:

```powershell
sources/tools/sdk/test-sdk-dcc.ps1 -BuildRoot build/sdk-native-20261108-release -Extension build/blender/extension/xray_blender-0.3.2.zip -OutputRoot build/my-sdk-dcc
```

## Нова міграція: спочатку окремий sample

Оригінальний corpus `C:/Stalker2_2011` відкривається для читання. Output має бути
окремим деревом; запущений coordinator тримає lock. Існуючий прийнятий B5
checkpoint у `build/blender-b5/migration` залишайте незмінним. Новий ZIP або
tool code змінює fingerprint і може повторно обробити значну частину corpus.

Для cooked clips без Maya використайте перевірений каталог і спочатку один
раніше перевірений path. `--include` — case-insensitive substring, кілька значень
об'єднуються. `--limit` обмежує entries inventory, включно з companions; він
не означає кількість успішних `.blend`.

```powershell
$blenderHost = 'C:/Program Files/Blender Foundation/Blender 5.3/blender.exe'
$cookTool = 'build/sdk-native-20261108-release/binaries/xray_asset_cook_cli.exe'
$playbackTool = 'build/sdk-native-20261108-release/binaries/xray_animation_math_engine_contract_tests.exe'
python -X utf8 sources/tools/blender/migrate_legacy.py --source-root C:/Stalker2_2011 --output build/my-clip-sample --blender-exe $blenderHost --cli-exe $cookTool --playback-exe $playbackTool --family-catalog sources/tools/blender/legacy-skeleton-catalog.json --include 'xr2-dsgn/resources/exported/animations/single/human/camp/inspect_high_idle.clip' --batch-size 2 --blender-workers 1
```

Перегляньте фактично вибрані source paths і їх status. Коли sample прийнято,
замініть конкретний `--include` на `--include .clip` для всіх clips. Каталог містить п'ять доведених
parent trees для 347 clips цього corpus; для іншого corpus потрібні власні
докази ієрархії. `.clip_check_data` мають власні inventory entries й також
потрапляють під substring `.clip`.

Для повної міграції зі складними `.ma` / binary `.mb` потрібен встановлений
Maya host. Це окремий output; не запускайте приклад поверх прийнятого B5:

```powershell
python -X utf8 sources/tools/blender/migrate_legacy.py --source-root C:/Stalker2_2011 --output build/my-full-migration --blender-exe $blenderHost --cli-exe $cookTool --playback-exe $playbackTool --mayapy-exe 'C:/Program Files/Autodesk/Maya2027/bin/mayapy.exe' --host-workers 1 --blender-workers 1 --batch-size 8
```

Починайте з одного worker на host; підвищуйте concurrency після успішного sample.
`--prepare-only` створює extraction cache і не засвідчує завершену міграцію.
Повтор тієї самої команди відновлює роботу: source/dependency/tool changes та
зміна output hash викликають потрібну перебудову. `--retry-failed` повторює
незмінені failures; застосовуйте після встановлення причини, збережіть попередні
manifest і logs. Для zero-root source defect повтор не є виправленням.

Exit code **0** означає успіх або warnings для вибраних entries; **2** — наявні
failed/pending entries; setup/argument errors мають окрему помилку запуску.
Під час batch migration статуси pending не можна видавати за готовий пакет.

## Прийняти результат і передати його художнику

У `migration-manifest.json` перевірте `source_unchanged`, повноту status entries,
`reopen_verified`, output hashes і `verified` для кліпів. Поля `reports` /
`key_counts` описують exact/fit channels, source/controller keys та pose errors.
`with_warnings` вимагає читання `warnings`, `import_warnings`, `errors` і
reconciliation; наявність `.blend` сама по собі не засвідчує повну сцену.

`resources/sources/blender/<corpus-relative-path>.blend` — editable output.
Передавайте також manifest з узгодженим output root, потрібні external resources
та `work/<id>/exchange` як archived source evidence. SDK **Use migrated .blend**
шукає точний URI й перевіряє hash; однойменний файл з іншого каталогу не замінює
цей зв'язок. Художник працює в окремому master через SDK association. Шляхи
`work`, `logs`, `jobs` потрібні maintainer’у для відтворення й діагностики.

Прийнятий B5 checkpoint: **3836/3836** entries, **1317 migrated**,
**2320 with_warnings**, **199 failed**, **3291 `.blend`**, **324/347** legacy
clips із engine playback. 23 failures підтверджують Root.scale=(0,0,0), решта
176 пов'язані з іншими дефектами джерел/обмеженнями міграції. Повне дерево
оригіналів має SHA-256 `ce5c2dfc5fff9da7dc797e31df3e65ea682354930392e37bf08cd5a1e9d2d9e9`.
Це датований checkpoint, а не очікувані counts для іншого корпусу.

Zero-root policy — **retain failed**, без заміни scale чи source pose. Команда
нижче додає діагностику до failed records **вашого нового output**, перевіривши
оригінальні hashes і spline/check-data; вона змінює manifest і не ремонтує кліп:

```powershell
python -X utf8 sources/tools/blender/annotate_zero_root_failures.py --output build/my-full-migration
```

## Якщо host або ресурс відмовив

Спочатку збережіть manifest, exact command, host version/hash, ZIP SHA-256,
`jobs` і відповідні `logs`. Для одного source відтворіть той самий `--include`
в новому output. Відрізняйте source defect від відмови host/cooker. Не змінюйте
precision, parent tree чи Root.scale лише заради PASS.

- `root-joint track must be identity`: перевірте source/check-data й zero-root
  diagnostics. Погоджені 23 кліпи зберігаються failed.
- Hierarchy/bind mismatch: порівняйте реальні parent links та bind matrices.
  Імена joints і filename candidates не доводять сумісність. `--family-map`
  застосовуйте лише після підтвердження потрібного hierarchy ID.
- `.mb` без Maya або unsupported basic MA: встановлений mayapy чи перевірений
  migrated `.blend`; plain MA reader не оцінює references/IK/history.
- Missing material/texture/reference: перегляньте оригінальні logical paths і
  dependencies. Generated `.blend` не відновлює відсутній зовнішній ресурс.
- `Blender worker exited ...`: відкрийте worker log. Під час B8 зафіксовано
  `Failed to rename directory ... xray_blender@ -> xray_blender` з **WinError 5**
  у штатному Blender ZIP installer. Пакет не встановлено, Action/cook ще не
  починався. Збережіть повний log; після усунення блокування/доступу повторіть
  вручну в новому output. Окремий filesystem probe показав: один rename з
  WinError 5 пройшов через 100 ms, без зміни ZIP чи прав. Процес блокування
  поки не встановлено. CI не робить автоматичного rerun і лишає failure видимим.

Майбутні migration-contract failures зберігають worker JSON/logs у
`xray_migration_*-failure`. SDK host log лежить у
`user_data/dcc-cache/<generation>/host.log`; fixture harness має `source/create.log`.
Один B7 worker failure не мав збереженого первинного traceback, тому його
причину не можна вважати доведено тотожною B8 rename failure.

## Залишені межі

[B7 acceptance](b7-acceptance.json) зберігає попередні результати й відомий
worker incident. [B8 acceptance](b8-acceptance.json) фіксує поточний пакет,
перевірені інструкції та фактичні host outcomes. Linux runtime у цьому checkout
не виконувався. Повний SDK8 animation graph, retargeting/actor integration,
повний physics authoring UI та запуск workflow на GitHub залишаються окремими
роботами після Blender pipeline B0–B8.
