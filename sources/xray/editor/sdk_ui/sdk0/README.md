# SDK-0: інвентаризація оригіналу

Дата: 2026-10-24. **SDK-0 завершено як етап інвентаризації.** Єдиний checklist залишається в
`sdk_migration_plan.md`; цей каталог містить докази та матрицю перенесення.

`function-matrix.json` описує 36 обов'язкових функціональних груп: маршрут
відкриття в оригіналі, джерела, original project entries, поточне включення
у збірку, залежності та запропонований сценарій перевірки. Source references
можуть повторюватися між групами. Фіналізований baseline, межі статичного
покриття та stage owners відомих gaps описано в `acceptance.md`.

## Що підтверджено

- Еталон — `tsnest/xray-2-2011`, commit
  `1795aa33a51fdde2ff5ea10627fc6d0b2b755c56`. У новий reference-only каталог
  скопійовано 2483 файли editor/WPF/docking/Maya; кожний перевірено за Git blob
  hash з раніше збереженого дерева upstream. Локальні змінені файли не перекрито.
- Збережено entries 13 original project files; поточні native sources перевірено
  за compiler edges у `build/sdk-ui-cmake/build.ninja`. У цій конфігурації
  компілюються 37 файлів із дослідженого reference набору. Managed Compile/Page/
  Resource entries обліковуються окремо, тому 37 не є числом усіх working UI files.
- 982 файли збігаються побайтно; 1225 — якщо нормалізувати CRLF/LF.
  1244 побайтних відмінності не можна трактувати як 1244 втрачені функції.
  У source corpus є 257 відсутніх локальних файлів: 79 legacy docking
  (заміна modern package), 8 legacy build files та 170 Maya/Maya-animation.
  Maya sources доступні у pinned reference, але ще не перенесені в active build.
- Статичний каталог містить 339 викликів/декларацій `register_action`,
  85 menu/tool registrations, 42 декларації file filters/extensions,
  17 локалізованих UI strings із `.resx` та 121 route/exporter reference.
  Коментарі відкинуто. Прохід 2026-10-24 класифікував action/menu registrations;
  Завершальний прохід додав construction routes, enabled/capture source
  candidates і explicit direct event bindings.
  Ці числа не є кількістю прийнятих функцій чи hotkeys.
- PE/CLR metadata 45 оригінальних `.exe/.dll/.mll` прочитано без виконання:
  SHA-256, architecture, assembly identity/version, assembly references,
  native imports і delay imports. Це dependency inventory, не loadability proof.

## Маршрути редакторів

Original `world/editor_world.cpp` відкриває animation, sound, dialog і particle
через відповідні DLL `editor_creator::create_editor`; tool window holder
зберігає/повторно використовує їхні вікна. Model editor створюється через
`create_model_editor`; код допускає новий екземпляр при кожному відкритті.
Це окремі вікна всередині процесу головної IDE, а не доведені standalone EXE.

Texture, material, material-instance і render-options editors створюються
безпосередньо в `world`. Level editor має tools solid visual, lights, composite,
terrain, particle placement, speedtree, misc і sound. `tool_scripting`
реєструється, але його tab у `level_editor.cpp` закоментовано; animation tool
реєструє resource chooser. Ці два маршрути ще потребують поведінкової перевірки.

Старий `world/particle_editor.cpp` присутній у джерелах, але відсутній у file
entries original `editor.vcproj`. Його не слід автоматично вмикати разом з
окремим particle module. Факти й точні source lines збережено в
`originalRouteEvidence` та `duplicateWorldSources` матриці.

Локальний `world/editor_world.cpp` уже замінено мінімальною реалізацією;
animation `editor_creator` більше не містить original UI factory, а sound
використовує native manager. Успішна збірка цих DLL не доводить перенесення UI.

## Залежності й формати

Actual original docking DLL: `WeifenLuo.WinFormsUI.Docking 2.3.4358.33208`.
Посилання `2.3.4183.35052` у `.resx` — інша, історична resource identity.
Original WPF/control DLLs мають CLR metadata `v2.0.50727`, Framework 2.0/3.0/3.5
references, Toolkit 1.4.0.0; native/mixed binaries залежать від VC90.
Це не змінює modern baseline: .NET SDK 10.0.402/Desktop 10.0.13/C#14,
DockPanel Suite 3.1.1 та Extended WPF Toolkit 5.1.2.

Dialog translation використовує Excel COM: `excel_file.cpp` створює
`Microsoft.Office.Interop.Excel.Application`; наявність interop DLL сама
по собі не доводить наявності Excel COM server. Maya 2011/2012 `.mll` імпортують
OpenMaya/Foundation і VC90; сумісність із сучасною Maya не припускається.
Вибір current-Maya API та заміни Excel integration — окремі рішення перенесення.

Перевірений раніше `bazik210` CMake world project включає той самий набір
editor modules та Office/WPF/DockPanel references, але цілиться у Framework 4.8.
Він підтверджує структуру залежностей, не є готовою .NET 10 реалізацією.

У матриці є SHA-256 дев'яти seed assets: project.xprj, .dlg, .particle, .avs,
.anim_collection, .editor_sound_scene, .sound_collection, .composite_sound,
.material. Найменший файл вибрано тільки як відтворюваний початок перевірки.
Перегляд 2026-10-24 уточнив залежності й непридатні seeds. Два sound graphs
мають замкнені static sound references; runtime/cook acceptance ще відкрите.
OBJ export та Maya exporters мають окремі функціональні рядки.

## Докази й повторення

`build/sdk0-baseline/`: sources.json, original-projects.json, actions.json,
menus-tools.json, format-declarations.json, ui-texts.json, routes-exporters.json,
assets.json, documentation.json, launchers.json, reference/ і summary.json.
12 680 original asset files мають загальний розмір 37 457 287 306 bytes;
126 documentation candidates знайдено. Текст/табличний вміст 9 унікальних
DOCX/TXT/XLSX (18 copies) переглянуто. Усі 108 PNG/JPG/PSD copies (54 унікальні
stored images/composites) також переглянуто; вони мають design/art provenance.

`build/sdk0-binary-inventory/binaries.json` містить PE/CLR inventory.
Початковий `build/sdk0-inventory/` мав невірний parsing Windows Ninja paths;
для build membership використовувати тільки `build/sdk0-baseline/`.

```powershell
./sources/tools/sdk/inspect-sdk-binaries.ps1 -OutputRoot build/sdk0-binaries-new
python ./sources/tools/sdk/inventory-sdk0.py --output build/sdk0-baseline-new
python ./sources/tools/sdk/map-sdk0.py build/sdk0-baseline-new --binaries build/sdk0-binaries-new/binaries.json
./sources/tools/sdk/prepare-sdk0-reference.ps1 -OutputRoot build/sdk0-reference-new
```

Binary inspector використовує pinned .NET 10 без додаткових NuGet packages.
Його тимчасовий project створюється лише під build/, поза product sources.
Inventory scripts потребують нової output directory. `map-sdk0.py` оновлює
цю матрицю з reviewed definitions; це maintenance tool, не product runtime.

Історичне `Tools/ico/editor_filtres/UI.JPG` переглянуто: File/Project/Edit/View/
Tools/Help, центральний Scene View, project tree ліворуч, tools та object
inspector праворуч. Це зображення з документації, не capture нового запуску.
Runtime reference status відстежується окремо в матриці та плані.

## Пробний запуск оригіналу

`build/sdk0-original-reference/` — незалежна копія build/x64, mounts і всіх
12 680 asset files (без junction/hardlink, robocopy без помилок); hashes усіх
скопійованих x64 binaries звірено. Оригінальні binaries/assets не змінювалися.
Запуск із copied build/x64 завершився за кілька секунд, exit `0xC0000142`,
без stdout/stderr і без reference behavior acceptance.

Application Error event 1000 містить точний copied executable path та PID 19948:
`xray_engine_pc.dll`, exception `0xC0000005`, offset `0x10F820`. Збережено
startup-result.json, application-events.json і copy-result.json. Це реальний
startup blocker; за цими даними не встановлено першопричину і не доведено
зв'язок із NVIDIA або відсутнім runtime. Modern Vulkan host цим не перевірявся.

Повторний probe потребує нової копії/evidence root:
`probe-sdk0-reference.ps1 -ReferenceRoot build/sdk0-reference-new`.
Жодного user project не відкривали й не редагували під час probe.

## Прохід 2026-10-24

`review-findings.md` містить висновки й конкретні gaps; `function-matrix.json`
оновлено до 921 source references у тих самих 36 групах. Додані зв'язки не
означають ввімкнення цих файлів у product build.

Усі 339 action candidates класифіковано: 29 поза original project entries,
5 API/forwarding, 272 звичайні registrations, 32 conditional та один disabled
construction route. Для всіх 305 command candidates визначено статичні імена,
shortcuts, source owners і verification steps. 92 menu/toolbar bindings
зіставлено з IDs; 2 API definitions відокремлено. Є 21 shortcut overlap
candidate, зокрема Global `Control+P` для particle editor/pin selection.

Canonical evidence: `build/sdk0-review-20261024-final/` та
`build/sdk0-fixtures-20261024-verified/`. У fixture review є hashes 64 files,
дві sound closures, відсутні model/clip/WAV references, localization IDs у
трьох мовах та два replacement sound candidates. Прийнятих runtime fixtures
поки немає. Lua configurations не виконувалися, оригінали не змінювалися.

```powershell
python sources/tools/sdk/review-sdk0.py --output build/sdk0-review-new
python sources/tools/sdk/check-sdk0-fixtures.py --output build/sdk0-fixtures-new
python sources/tools/sdk/map-sdk0.py build/sdk0-baseline --review build/sdk0-review-new --fixtures build/sdk0-fixtures-new
```

Генератор звіряє source commit/input hashes, asset manifest і seed hashes перед
приєднанням evidence до матриці. Source-owner association є кандидатом на
поведінкову перевірку. Фіналізація inventory та наступні implementation checks
визначені нижче й у `acceptance.md`.

Перевірка evidence пройшла: `build/sdk0-audit-20261024.json`, 18 checks;
source-contract CTest також пройшов. Зокрема, звірено всі command/menu IDs,
artifact hashes, 18 original documentation copies, 64 asset hashes і відхилення
застарілого review до запису матриці. Це перевірка inventory, не product UI test.

## Завершення SDK-0

Фінальна матриця має `sdk0Complete: true`, 36 required workflow groups,
921 source references і stage owner для кожної групи. 16 construction routes
покривають усі 305 command candidates; для кожної є initializer і джерела
enabled/capture. Окремо збережено 51 explicit enabled-delegate binding.

`inspect-sdk0-events.py` обліковує 1371 explicit source/XAML event bindings;
source handler candidates, forwarding, inline delegates та virtual/interface
slots розрізняються. Порожні hooks і not-listed project entries збережено.
`inspect-sdk0-images.py` готує read-only previews, а `review-sdk0-routes.py`
звіряє reviewed image fingerprint і construction/exporter evidence.

Canonical additions: `build/sdk0-events-20261024-audited/`,
`build/sdk0-images-20261024/`, `build/sdk0-routes-visuals-20261024-complete/`.
Acceptance evidence — `build/sdk0-ui-audit-20261024.json` та
`build/sdk0-source-contract-complete-20261024.log`; межі та повторення —
`acceptance.md`. Шість explicit scope gaps мають implementation-stage owners.
Original startup, повні fixture closures і runtime/cook/editor workflow acceptance
залишаються перевірками відповідних SDK-1–SDK-12 stages.
