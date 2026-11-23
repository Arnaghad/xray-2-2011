# SDK-3: projects, documents і resource pipeline

Дата: **2026-11-10**. SDK-3 завершено в межах критеріїв
`sdk_migration_plan.md`. Machine-readable результати, шляхи й SHA-256 доказів:
[acceptance.json](acceptance.json). Повні прогони збережено в
`build/sdk3-implementation-20261110/`.

## Прийнята поведінка

- Production `WindowIde` виконує New/Open/Save/Save As/Close, веде recent projects,
  відображає dirty state, обробляє Yes/No/Cancel і перший Save через Save As.
  Документи належать project session; скасування переходу зберігає їх, успішний
  перехід звільняє документи, resource browser, queries і cache handles.
- Open створює writable working copy. VFS об'єднує writable overlay і read-only
  prepared sources. Оригінальні SDK directories та prepared asset pack захищено
  від записів; authored content і cache мають окремі корені.
- Lua `.xprj`/`.library` parser/serializer зберігає структуру, GUID keys, references,
  object types, symbolic values, числову точність і невідомі поля. `alex` містить
  7 об'єктів; `dima_light_test` зберігає authored reference відсутньої моделі навіть
  після runtime substitution. Save As копіює `.terr` зі збереженням підкаталогів
  і перевіряє конфлікти до commit.
- Збереження використовує flushed staging file та atomic replacement.
  I/O failure залишає попередній файл і dirty state. Recovery спочатку повністю
  перевіряє snapshot або завершений `.saving.tmp`, потім атомарно відновлює файл.
  Незбережені сесії мають окремі recovery paths; відновлення вимагає Save As.
- Справжній owned `ShowDialog` приймає кириличну назву; головне HWND вимкнене,
  IDE shortcuts призупинені та відновлюються після закриття діалогу.
- Resource browser і project loading використовують спільні mounts/dependencies.
  Native resource ABI 1 має 24-byte callbacks, 40-byte request і 32-byte result;
  host ABI 2 та property ABI 1 збережено. Native engine виконує mounts/queries,
  managed cache утримує resource handles і записує JSON receipts, а не вигадані
  cooked assets. Source fingerprint змінює cache generation; cancellation
  звільняє handles, stale handle відхиляється.
- Explicit format copy підтримує original Lua schema version 1 із provenance,
  окремий destination і відхилення unsupported versions. Open не конвертує файл.

## Перевірки

- CMake Debug і Release: build `stalker2`, `xray_editor_world`, `xray_sdk_ui`;
  managed builds — 0 warnings / 0 errors.
- CTest Debug і Release: **5/5 PASS** у кожній конфігурації.
  Логи: `ctest-debug-final.log`, `ctest-release-final.log`.
- Compatibility runner: **11/11 груп PASS** на .NET 10.0.13 в обох конфігураціях.
  Незмінні копії результатів: `build/sdk3-implementation-20261110/compatibility-final-{debug,release}/result.json`
  (збережені перед повторним використанням CMake build trees для SDK-4).
  SDK-3 checks охоплюють 15 original project/library configs, 7 tool libraries,
  unknown fields, GUIDs/transforms, кириличні paths, `.terr` hashes, invalid input,
  save/recovery failures, interrupted-save recovery, missing resources, recent
  projects, owned documents, справжній modal Save As/reopen і cache cancellation.
- Full production IDE, native Vulkan: `gpu-final-debug`, `gpu-final-release`,
  обидва з `-FullIde -ClosePending`. Model query: **21416 bytes / 1 surface**;
  raw VFS повертає нові bytes після зміни working source; cache invalidation і
  cancelled-handle rejection пройшли. Pending queries drained перед HWND teardown.
- Усі 9 captures кожного full-IDE прогону мають **946×636**. Edit змінює 1607
  pixels; undo/redo/restored/layout-restored стани піксельно ідентичні відповідним
  baseline/edited. `capture-result.json` — PASS у Debug і Release.
- Legacy host: `legacy-final-debug`, `legacy-final-release` — PASS, без SDK
  extensions; `game.bmp` обох конфігурацій має однаковий SHA-256.
- Усі 4 фінальні native прогони: **validation errors=0, warnings=0**, exit 0,
  підтверджений native resources drain і bridge teardown.
- `prepared-source-provenance.json`: 25/25 prepared source files дорівнюють
  original SDK за SHA-256 (2 projects, 13 libraries, 8 terrain tiles та 2 інші
  файли). `fixture-additions.json` містить hashes чотирьох доданих original
  sand/mud texture/options files. Повторюване додавання:
  `sources/tools/sdk/prepare-sdk3-fixtures.py`.

## Інтерактивний прогін і межі доказів

У звичайному desktop SDK перевірено Open `alex` (7 об'єктів), відкриття Save As,
Cancel і чисте закриття (`interactive/desktop-05-*`). Інструмент Sky не може
адресувати editable element owned tool-window: indexed input відхиляється, а
keyboard input повертає фокус головному вікну. Фізичний ввід кирилиці через Sky
не зараховано. Кириличний Save As/reopen перевірено автоматизованим тестом
справжнього `ShowDialog`, без `NonInteractiveDialogOverride`, з перевіркою native
disabled owner та блокування IDE shortcuts.

SDK-3 приймає project persistence і resource services. Повне редагування сцени,
command transactions і native відтворення всіх object types належать SDK-4/5.
Native model доказ обмежений prepared woodbox і raw authored reload; універсальний
імпорт, material/texture authoring та domain cooks потребують SDK-6 і наступних
етапів. Original C++/CLI inventory flags не змінено на «compiled»; compiled C#
та native service implementation записано окремо у matrix й acceptance JSON.

Наступний етап — **SDK-4**.
