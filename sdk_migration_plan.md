# Перенесення оригінального X-Ray 2.0 SDK на поточний Vulkan runtime

Дата оновлення: 2026-10-24. Статус: SDK-0 завершено як етап інвентаризації оригіналу; required workflows, source/build/dependencies, construction/event/predicate evidence і known gaps фіналізовано. Controls, native property/undo/redo та docking/layout pilot SDK-1 перевірено; повне приймання SDK-1 і подальших implementation stages ще відкрите.
Перевірка UI-підходу й GitHub-джерел: 2026-10-22; результати враховано нижче.
Звірка коду з планом: 2026-11-08 — див. «Звірка коду з планом, 2026-11-08»;
статуси етапів не змінено, неперевірений код SDK-1 зафіксовано окремо.
Цей файл — єдиний checklist перенесення SDK; `vulkan_integration_plan.md`
залишається окремим записом scope та приймання renderer.

## Мета та межа завершення

Відновити SDK із `C:/Stalker2_2011/xr2-dsgn`: впізнаваний інтерфейс,
редакторські інструменти, формати проєктів та повний цикл створення,
редагування, збереження й повторного відкриття контенту на поточному engine.
Перелік обов'язкових інструментів зафіксовано в завершеній інвентаризації SDK-0.
Наявні інструменти оригіналу не виключаються з цього переліку автоматично.

Завершений Vulkan M9 підтверджує renderer та обмежені game/editor workflows.
Він не є прийманням повного SDK. Його звіти, builds, fixtures, captures і caches
залишаються незмінними. Нове SDK-приймання має окремі джерела доказів.
Рішення про історичний NVIDIA startup-ризик зберігається без заяви про fix.
Після приймання, за окремим запитом користувача, старі migration test suites
видалено з active sources/targets; архів і перевірка очищення —
`build/vulkan-test-retirement/summary.json`. Старі captures/reports збережені
як еталони. Перевірки SDK створюються під фактичні SDK workflows і ризики.

Поточний `EditorWorld.cs` — контрольна оболонка. Її viewport, native/.NET bridge
і перевірки lifecycle можна повторно використати. Відновлення SDK не зараховується
за перейменування цієї оболонки або додавання кнопок без реальної поведінки.

## Початковий стан, зафіксований до реалізації

- Оригінальний запуск: `C:/Stalker2_2011/xr2-dsgn/editor-x64.cmd` запускає
  `build/x64/stalker2-win64-dynamic.exe -editor -no_network`.
  У SDK є DLL editor, editor_base, editor_controls, animation, model,
  particle, sound і dialog. Це інвентаризаційний факт, а не доказ їхньої
  сумісності з поточним engine або наявності всіх потрібних cooked assets.
- Поточний managed host компілює тільки `Bridge.cs` і `EditorWorld.cs`
  через `sources/xray/editor/base/EditorHost.csproj.in`.
  Додаткові C# файли в каталозі самі собою до збірки не потрапляють.
  Target — `net10.0-windows`, увімкнено WinForms; WPF ще не підключено.
  `WindowIde.cs`, `CommandEngine.cs`, `InputEngine.cs` та інші C# заготовки
  не є прийнятою реалізацією оригінального SDK. Зокрема, `WindowIde.cs`
  має мінімальні меню й незавершений closing, а не повний original shell.
- `sources/xray/editor/CMakeLists.txt` підключає base, animation, sound і world.
  Controls/dialog/model/particle закоментовані; окремих каталогів із цими
  іменами тут немає. Однак частина їхньої логіки й UI розташована всередині
  `editor/world/sources`, тому «немає каталогу» не означає «немає реалізації».
- У `world/sources` збережено `window_ide.*`, `project*`, `level_editor*`,
  material/texture/particle editors, terrain tools, action/input/command
  infrastructure та `.resx`. У `xray/wpf_controls` є спільні C#/XAML controls.
  Source/build inventory зафіксовано в SDK-0; працездатність і відповідність
  editor workflows перевіряються в наступних implementation stages.
- GitHub-перевірка знайшла відсутні локально `editor/controls`, `editor/dialog`,
  `editor/model`, `editor/particle`, `WeifenLuo.WinFormsUI.Docking` та
  `wpf_controls.csproj` у baseline `tsnest/xray-2-2011` (commit нижче).
  Отже, це відомі джерела для відновлення, а не невідомі source gaps.
  Усі 423 upstream `.cs/.xaml` файли `wpf_controls` присутні локально та
  збігаються за Git blob hash. Це доказ збігу джерел, не .NET 10 compatibility.
- Animation/sound CMake targets зараз охоплюють вибрані native частини;
  успішна компіляція DLL не доводить працездатність оригінального редактора.
- Наявний M5-C перевіряє обмежений original exported/source runtime-cooking
  профіль. Сумісність усіх SDK assets, імпорту й старих cooked formats
  цим не підтверджена.

## Результат перевірки GitHub і вибір донорів UI

Порядок дослідження: X-Ray 2.0, потім інші X-Ray для незакритого питання
сучасного UI. Перевірено дерева репозиторіїв, конкретні source/build файли та
локальний build inclusion. Репозиторії-донори в цій перевірці не збиралися
й не запускалися. Готового SDK для нашого .NET 10 + MinGW/Vulkan у перевірених
джерелах не виявлено; цей висновок не охоплює всі можливі GitHub-гілки й форки.

- **`tsnest/xray-2-2011` — основний донор, найвища відповідність цілі.**
  Commit `1795aa33a51fdde2ff5ea10627fc6d0b2b755c56`.
  Є [original editor modules](https://github.com/tsnest/xray-2-2011/tree/1795aa33a51fdde2ff5ea10627fc6d0b2b755c56/sources/xray/editor),
  [docking sources](https://github.com/tsnest/xray-2-2011/tree/1795aa33a51fdde2ff5ea10627fc6d0b2b755c56/sources/WeifenLuo.WinFormsUI.Docking)
  і [WPF project](https://github.com/tsnest/xray-2-2011/blob/1795aa33a51fdde2ff5ea10627fc6d0b2b755c56/sources/xray/wpf_controls/wpf_controls.csproj).
  Каталоги controls/dialog/model/particle містять відповідно 76/143/100/103
  файли; це обсяг джерел, не кількість готових функцій. `window_ide.cpp` та
  `window_ide_serialize.cpp` теж збігаються з локальними за Git blob hash.
  **Використати:** відсутні modules/resources, C# controls, original
  document/tree/hypergraph contracts і docking persistence. Старий WPF project
  на .NET Framework 3.5 слугує списком source/resources/references для нового
  проєкту; його не можна вважати готовою збіркою для поточного host.

- **`bazik210/xray_2` — додатковий донор build mapping і окремих змін.**
  Commit `053030f4c542ba7468a323a6b5f59709292476e2`, гілка `loxotron`.
  [Editor CMake](https://github.com/bazik210/xray_2/blob/053030f4c542ba7468a323a6b5f59709292476e2/xray/editor/world/sources/CMakeLists.txt)
  та [WPF CMake](https://github.com/bazik210/xray_2/blob/053030f4c542ba7468a323a6b5f59709292476e2/xray/wpf_controls/CMakeLists.txt)
  описують sources, resources і залежності; UI targets мають .NET Framework 4.8
  та Visual Studio references. README вказує, що third-party sources треба
  отримувати окремо. Є залежність від `WPFToolkit.Extended`; вона також присутня
  в original `.csproj` через `ProjectReference` та використана graph header control.
  **Використати:** як другу перевірку build inclusion та dependency inventory,
  а зміни коду — лише після diff із 2011 baseline й конкретного behavior case.
  Відповідність висока для структури SDK, часткова для поточного toolchain;
  механічно переносити CMake або додавати нові toolkit dependencies не слід.

- **`tyabus/xray-2.0` — історичний резерв для окремих source gaps.**
  Commit `47461806c25e34005453a373b07ce5b00df2c295`.
  [Репозиторій](https://github.com/tyabus/xray-2.0/tree/47461806c25e34005453a373b07ce5b00df2c295)
  описаний як mirror build 661. У перевіреному дереві є controls/dialog,
  але немає окремих model/particle каталогів; WPF `.cs/.xaml` — 53 проти 423
  у 2011 baseline. **Рішення:** використовувати тільки для конкретної відсутньої
  реалізації після перевірки контрактів; не замінювати ним повніший UI 2011.

- **IX-Ray 1.6 — корисний UX-референс, низька пряма сумісність.**
  `ixray-team/ixray-1.6-stcop`, commit `478899e687b1e5d7eab834120a252ca01b3dca36`.
  [Level Editor UI](https://github.com/ixray-team/ixray-1.6-stcop/blob/478899e687b1e5d7eab834120a252ca01b3dca36/src/Editors/LevelEditor/UI/UIMainForm.cpp)
  має ImGui panels, відновлення відкритих windows і viewport-focus handling.
  **Використати в acceptance cases:** відновлення панелей і повернення focus
  viewport після роботи з іншим tool. Наявність цього коду не є proof для XR2.
  [SceneUndo](https://github.com/ixray-team/ixray-1.6-stcop/blob/478899e687b1e5d7eab834120a252ca01b3dca36/src/Editors/LevelEditor/Editor/Scene/SceneUndo.cpp)
  зберігає/завантажує scene snapshots у temp files; це інша history model.
  Перевірений Actor Editor backend — `imgui_impl_dx9.cpp`, а Level Editor
  залежить від `xrEUI/xrECore/xrEngineCore`. Ці implementations не є заміною
  нашого Vulkan output/bridge або XR2 commands. Перехід усього SDK на ImGui
  не обґрунтований для цілі відтворити original WinForms/WPF workflows.

- **`tsmp/xraySdkEditors` — резервний референс SDK 0.8/1.x.**
  Commit `1217f5ae8f4c6bb7ebbce8e7ea8da0343d9ced86`.
  [README](https://github.com/tsmp/xraySdkEditors/blob/1217f5ae8f4c6bb7ebbce8e7ea8da0343d9ced86/README.md)
  описує редактори на основі RedPanda SDK 0.8 із підтримкою SDK 0.4/0.7,
  x64/VS2022; gizmo та «грати в редакторі» не перенесені.
  Перевірені Level UI/SceneUndo використовують ImGui й scene snapshots.
  **Рішення:** не брати як основу XR2 SDK; розглядати окремий fix лише за
  наявності відповідного дефекту й доказу сумісності формату/поведінки.

- **OpenXRay 1.6 — довідка для runtime diagnostics.**
  [Офіційний опис Game Editor](https://github.com/OpenXRay/xray-16/wiki/%5BEN%5D-Game-Editor)
  документує weather editor та UI debugger. Ця сторінка не підтверджує
  повний SDK authoring workflow; заміна нашої IDE цим editor не обґрунтована.

Локальні матеріали перевірки: `build/sdk-ui-review-20261022/` — дерева п'яти
репозиторіїв, `source-manifest.json` із commit/path/SHA-256 прочитаних файлів,
`wpf-comparison.json`, `module-comparison.json` і копія плану до редагування.
Це research evidence checkpoint 2026-10-22. Поточний стан SDK-0/SDK-1
визначає checklist нижче; runtime acceptance має окремі критерії.

## Технічний напрям

Рекомендований базовий маршрут: залишити перевірений native engine на GCC/MinGW
та Vulkan; переносити managed UI з C++/CLI у C# WinForms/WPF зі збереженням
наявної структури, ресурсів і поведінки. Наявні 423 C#/XAML файли controls
використовувати повторно після перевірки сумісності; відсутні модулі брати
насамперед із pinned 2011 baseline. Вибір такий, щоб зберегти engine baseline
і мати явний контракт між GUI й native кодом; це проєктна рекомендація.

На SDK-1 перевірити цей маршрут одним наскрізним сценарієм. Альтернатива —
окремий MSVC C++/CLI UI-компонент за тим самим C ABI. Вона потребує окремої
перевірки toolchain і не дозволяє передавати GCC C++ objects/STL через MSVC ABI.
Повне переключення engine на MSVC до цього плану не закладається.
Не варто механічно переписувати весь UI до результату цього пробного перенесення.
Оцінити також вузький C++/CLI компонент для складних original controls, якщо
він зменшує обсяг перенесення. Він має працювати з поточним .NET через C ABI;
старі Framework 3.5/4.8 assemblies самі по собі цей варіант не підтверджують.

Microsoft описує міграцію C++/CLI на сучасний .NET як окреме перенесення
Visual C++ проєктів, із Windows/DLL обмеженнями та налаштуванням WinForms/WPF:
[офіційна документація](https://learn.microsoft.com/en-us/dotnet/core/porting/cpp-cli).
Саме додавання старих SDK DLL до поточної папки не є планом інтеграції.

Поточний bridge має ABI v2 та `command(context, uint32)` без довільного payload.
Це lifecycle/control baseline, а не готовий object/property/resource API.
На SDK-1 визначити його версіоноване розширення: typed operations із handle
об'єкта, скопійованим payload, result/error і completion identity. Обидві
сторони зараз перевіряють точні version/size; структури не розширювати мовчки.
Для збереження контрольного host потрібен явний v2 adapter або окремий SDK ABI.
Міст SDK повинен мати opaque handles із перевіркою покоління,
явні create/destroy, ownership, рядки й buffers із визначеним allocator,
коди помилок та асинхронні completion/cancel callbacks.
UI володіє windows на STA; native world/resource/render operations виконуються
на належних threads. Жодні GUI references або borrowed pointers не переживають
закриття документа, output або editor session.

## Послідовність

`SDK-0 → SDK-1 (controls compatibility → наскрізний сценарій) → SDK-2 → SDK-3 → SDK-4 → SDK-5`.
SDK-1 включає мінімальний реальний command/viewport route; повне покриття
цих services належить SDK-4/SDK-5. Pilot не залежить від завершення всіх editors.
Далі SDK-6 забезпечує asset/material основу для SDK-7, SDK-8 і SDK-10.
SDK-9 та SDK-11 використовують спільні документи, commands і resource services.
SDK-12 залежить від завершення всіх обов'язкових пунктів.

### SDK-0. Еталон і повнота джерел

- [x] **SDK-0 — інвентаризація оригіналу та відповідності.**
  **Завершено як inventory, 2026-10-24.** Фіналізовано function → source/build →
  dependency → verification матрицю 36 функціональних груп:
  `sources/xray/editor/sdk_ui/sdk0/function-matrix.json`, пояснення — сусідній
  `README.md`. Є PE/CLR inventory 45 binaries, pinned reference 2483 files,
  13 original project inventories, 339 action registration candidates,
  42 format declarations і hashes 9 fixture seeds. Визначено основні маршрути
  module editors та embedded tools. Повна workspace-копія original SDK
  завершилась на startup: exit 0xC0000142; Windows event — xray_engine_pc.dll,
  exception 0xC0000005, offset 0x10F820. Це зафіксований blocker, не UI acceptance.
  Класифіковано всі 339 registrations: 305 command candidates, 29 поза original
  projects, 5 API/forwarding. Зіставлено 92 menu/toolbar bindings, виявлено
  21 shortcut overlap candidate. Переглянуто 9 унікальних DOCX/TXT/XLSX;
  перевірено залежності 9 seeds і hashes 64 files, дві sound closures замкнені
  статично. Висновки — `sdk0/review-findings.md`; критерії завершення —
  `sdk0/acceptance.md`. 16 construction routes покривають усі 305 candidates;
  для кожної є initializer та enabled/capture source candidates. Обліковано
  1371 explicit source/XAML event bindings; усі target categories розібрано.
  Переглянуто 54 унікальні images/PSD composites (108 copies). У 36 required
  groups є implementation stage, код, залежності та сценарій перевірки.
  Шість known scope gaps, fixture repairs/cook acceptance, current Maya/Excel
  integration та original startup blocker мають owners у наступних stages;
  SDK-0 не зараховує runtime editor workflows як виконані.
  Зняти inventory SDK binaries, assembly/native dependencies, меню, panels,
  tools, hotkeys, формати файлів, project types та зовнішні exporters/converters.
  Для кожної функції знайти фактичні `.cpp/.h/.cs/.xaml/.resx` та build inclusion;
  зафіксувати стан: переноситься, вже реалізовано й перевірено, джерела неповні,
  або відсутня runtime-залежність. Окремо розрізняти standalone editors та tools,
  вбудовані в `world`, незалежно від назв DLL і каталогів.
  Відновити відсутні sources із pinned `tsnest` у робочій копії з manifest
  repo/commit/path/hash і потрібними notices; не перекривати змінені локальні
  engine/bridge файли. Зіставити окремі model/particle/dialog modules із логікою
  у `world`, визначити активний маршрут original SDK та не збирати дві версії
  тієї самої функції. `bazik210` використати для cross-check build dependencies.
  Для docking зафіксувати original source baseline та assembly identity
  (у `window_ide.resx`: `2.3.4183.35052`; actual original DLL —
  `2.3.4358.33208`, підтверджено PE/CLR metadata), для WPF — Page/Compile/
  Resource entries відсутнього `.csproj` і залежності кожного потрібного control.
  Оригінальний SDK запускати на workspace-копії, не на єдиному оригіналі.
  Зафіксувати reference screenshots і сценарії; якщо запуск блокує відсутній
  asset/runtime, зберегти причину й використати доступні джерела/документацію,
  не видаючи їх за виконаний runtime proof.
  **Готово:** погоджений inventory із кожною обов'язковою функцією, її кодом,
  залежностями й способом перевірки. Невідомі джерела є явними gaps.

### SDK-1. Перевірка архітектури host і bridge

- [x] **SDK-1 — один робочий вертикальний сценарій.**
  Спочатку зібрати окремий compatibility target спільних controls для
  `net10.0-windows`: WinForms + WPF, явні `.cs/.xaml/.resx`/resource entries,
  references та CMake build dependencies; перевірити чисту й incremental build.
  Відсутній `.csproj` відновити за upstream inventory у сучасному форматі.
  Окремо перевірити `PresentationFramework.Aero`, designer-only `System.Design`,
  resource assembly names і legacy reflection у `wpf_controls/helpers/utils.cs`:
  він шукає private `AvalonAdapter._hostControl` у WindowsFormsIntegration 3.0.
  Для modern host замінити цей зв'язок явним owner/host mapping; сумісність
  private API не припускати. Відкрити property grid і по одному curve/graph
  control, перевірити keyboard focus, text editing, binding та dispose.
  Перенести оригінальну dockable панель із properties та командою зміни
  native об'єкта: UI → command → engine → viewport → undo → redo → close.
  Використати справжні object handle та payload через розширений ABI;
  property edit має відображатися в native state і viewport, а undo/redo —
  відновлювати значення. Зберегти/відновити layout цієї панелі й повторити edit.
  Перевірити WinForms/WPF integration, handles, exceptions, callback lifetime,
  resource cancellation і renderer teardown. За результатом зафіксувати UI
  toolchain та ABI. Оцінити, які C++/CLI частини залишаються native, а які UI.
  Зафіксувати compile/runtime blockers і обсяг адаптації C# та, якщо потрібен,
  вузького C++/CLI варіанта. Наявність готових original sources сама по собі
  не доводить, який варіант дешевший чи працездатний.
  **Готово:** сценарій реально працює; Debug/Release та close із pending work
  проходять; рішення про architecture ґрунтується на цьому результаті.

### SDK-2. Оригінальна оболонка та спільні controls

- [x] **SDK-2 — IDE shell.**
  Перенести оригінальні menus/toolbars, docking, documents/tabs, Project/Tools/
  Properties/Output windows, options, icons, localized resources і layout save/load.
  Відновити потрібні tree/property grids, curve/timeline та graph controls
  зі спільної бібліотеки, розділивши загальні controls і domain logic.
  Взяти original `editor/controls/sources/document_base.*`,
  `document_editor_base.*`, tree/hypergraph та `dock_panel_serializer.*`;
  адаптувати їхні контракти до вибраного host. Serializer уже має
  `SaveAsXml/LoadFromXml` і registry storage; зберегти stable content IDs та
  mapping `scene_view_panel`/`window_view` із `window_ide_serialize.cpp`.
  Нові settings зберігати окремо від original SDK; old layout імпортувати з копії.
  Перевірити пошкоджений layout, невідомий panel ID, відсутній монітор і reset.
  Закриття/приховування панелі та закриття документа мають різні lifecycle rules.
  Зафіксувати версії й джерела third-party docking/WPF dependencies.
  **Готово:** структура робочого простору відтворює reference, layout зберігається
  й відновлюється, DPI/resize працюють; команда має дію або явний статус.
  Dock/float/redock та focus між tree, properties, WPF control і viewport
  не гублять input; переміщення між DPI monitors не спотворює picking coordinates.
  Форма з непідключеними панелями не закриває пункт.

### SDK-3. Projects, documents і resource pipeline

- [x] **SDK-3 — справжній project workflow.**
  Перенести New/Open/Save/Save As/Close, recent projects, document ownership,
  dirty flags, запит збереження й recovery після незавершеного save.
  Зберегти семантику `resources/projects/<name>/project.xprj`, libraries,
  object IDs, paths, references, object types і невідомих полів.
  Відновити VFS mounts, resource browser, dependency queries, cooking/cache
  invalidation і cancellation для реальних SDK assets. Усі записи — у робочі
  копії; caches відокремлені від authored content. Форматні перетворення явні,
  версіоновані й не переписують оригінал при простому відкритті.
  **Готово:** reference project відкривається, зберігається й відкривається знову
  без втрати структури/посилань; перевірено missing dependencies, невірний формат,
  кириличні paths і save failure без пошкодження останньої доброї копії.

### SDK-4. Scene editing, actions і history

- [x] **SDK-4 — базова робота level editor.**
  Перенести action/input binding, selection, object tree, properties,
  create/delete/duplicate, copy/paste, grouping/reparenting, visibility,
  translate/rotate/scale, coordinate spaces, snap і picking.
  Використати наявну command infrastructure та native transform controls;
  перенести managed bindings замість другої незалежної history.
  Зіставити C# `CommandEngine.cs` із original command implementation перед
  включенням у build: preview, nested transactions, failed commit, redo branch
  та saved-state marker. Snapshot-based `EScene::UndoSave` із X-Ray 1.6
  не підміняє ці контракти.
  Усі зміни проходять через commands із transaction та dirty-state semantics.
  **Готово:** користувач створює й редагує сцену, undo/redo точно відновлюють
  object state, а save/reopen зберігає результат. Працюють mouse і hotkeys.

### SDK-5. Повна інтеграція editor viewports із Vulkan

- [x] **SDK-5 — renderer services для SDK.**
  Підключити main, camera/model/asset previews до editor scene/view/output;
  перенести navigation, selection outlines, gizmos, grid, overlays, render modes
  та потрібні picking/debug routes. Скласти mapping legacy renderer calls
  до typed Vulkan API. Прогалини реалізовувати й перевіряти як конкретні
  renderer dependencies; API не може мовчки повідомляти success без дії.
  Перевірити resize, DPI, minimize, close/reopen preview, scene switch,
  resource reload і shutdown із pending queries/frames.
  Врахувати WinForms/WPF/native HWND hosting: dock/float/redock, можливе
  перестворення HWND і release/reattach Vulkan output; hide не дорівнює destroy.
  Overlays і gizmos, які мають бути над scene, відмальовувати через renderer;
  довільне WPF-перекриття native viewport не вважати доведеним.
  **Готово:** реальні SDK editing workflows працюють у всіх потрібних views;
  output isolation, thread ownership та validation підтверджені через teardown.
  Поточні M8 captures залишаються regression controls.
  **Прийнято 2026-11-12:** main/camera/model/texture/material views, mesh
  silhouettes, усі transform handles та 21 render modes, resize/DPI/minimize,
  dock/float/redock/HWND recreation, reload і atomic incremental scene edits.
  Play/Simulate запускає authored model/light scene на game logic owner:
  pause/step/focus, editing lock, exact stop/reopen та close під час runtime.
  Невідновлені domain kinds відхиляються до їхніх власних етапів.
  Debug/Release: CTest 6/6, 18 UI groups, 24 послідовні GPU workflows;
  validation 0/0 через pending-query/frame та file-producer/module teardown.
  Mapping, source/binary/evidence hashes і exact regression controls:
  sources/xray/editor/sdk_ui/sdk5/acceptance.json, renderer-mapping.json
  та README.md у тій самій теці. Попередні checkpoints незмінні.

### SDK-6. Model, material, texture та asset editors

- [x] **SDK-6 — asset authoring.**
  Відновити наявні model/material/texture editors, asset libraries, surface
  assignment, parameter editing, dependencies та live preview.
  Зіставити original geometry/material/texture formats, import options і потрібні
  external exporters із реально наявними cooks. Перевірений woodbox — один
  regression case, не універсальний доказ імпорту всіх SDK assets.
  Відсутні вихідні модулі або unsupported formats залишаються відкритими
  inventory gaps до відновлення/реалізації потрібної поведінки.
  **Готово:** reference assets імпортуються/відкриваються, редагуються,
  зберігаються й перевантажуються; material edits реально змінюють preview
  та результат у level, failure не залишає застарілий success/cache.

  Прогрес 2026-11-12: реалізовано перший 2D TGA/options workflow —
  original property grid, окремий CommandEngine, Apply preview, writable
  Save/Reopen та live source binding для level/model textures. Reference pack
  захищений; failure/cancel не ставить saved marker. Повна texture family,
  materials/model/render-options editors та OBJ/Maya pipelines залишаються
  відкритими. Scope, перевірки та hashes першого checkpoint:
  sources/xray/editor/sdk_ui/sdk6/texture-source-checkpoint.json і README.md.

  Прогрес material/instance, 2026-11-12: original inheritance та model-parameter
  overrides, Apply/Use base value/Undo/Save/Reopen, атомарна публікація залежних
  static materials і відновлення saved sources у новому SDK host перевірені.
  Виправлено resource callback lifetime під час shutdown; CTest Debug/Release
  7/7, по 20 UI groups. Scope та snapshot evidence:
  sources/xray/editor/sdk_ui/sdk6/material-source-checkpoint.json і material-source.md.
  Stage/effect palette, інші material domains і model surface assignment відкриті.

  Прогрес 2026-11-13: реалізовано та перевірено в Debug/Release model surface
  assignment із native geometry/material extraction і OBJ-експортом selection/project
  (включно з reflected scale winding проти незалежного 56-byte reader),
  render options editor (класифікація 61 опції з `default.cfg`: 2 adapted до Vulkan,
  59 blocked із lossless roundtrip), `stb_dxt` DXT1/1a/3/5 (BC1/BC2/BC3) + HighQuality/A8/NORMAL_MAP
  та 6-face cubemap preview/save + RGBA fallback для terrain/decals/particles.
  Відновлено 170 оригінальних Maya-файлів із перевіреними Git blob hashes та
  завантажено Windows devkit Maya 2027.2 із provenance.
  CTest Debug/Release 8/8, `xray_sdk_ui` по 22 checks, послідовні GPU-прогони
  (model, render-options, cube, texture, material) з validation 0/0.
  Відкритими в SDK-6 залишаються collision/physics та skeletal model editors,
  array/3D texture codecs та stage/effect palette; кросплатформений порт і
  T1/T2 roundtrip Maya-плагіна реалізовані й перевірені в реальній Autodesk Maya 2027.

  Прогрес кросплатформеного Maya-порту (M1–M6 + T2 live Maya 2027), 2026-11-13: реалізовано чистий
  Maya-орієнтований, кросплатформений (Windows + майбутній порт на Linux) пайплайн
  без залежності від Win32 API чи бінарного C++ ABI плагінів Maya:
  - Maya Python API 2.0 frontend (`sources/tools/maya/plugin/`, `xray_maya.mod`):
    locator-вузли `xray_collision`, `xray_body`, `xray_constraint`, `xray_fake_drawing_locator`
    та команди `xray_solid_visual_export`, `xray_skeleton_export`, `xray_skin_export`,
    `xray_anim_export`, `xray_path`, `xray_body`, `xray_collision`, `xray_constraint`,
    `xray_build_collision`, `xray_ph_exporter`, меню Maya (`shelf_menu.py`).
  - Переносний обмінний формат `xray-maya-exchange/v1` (`sources/tools/maya/exchange/`,
    `manifest.json` + little-endian `payload.bin`) із валідатором і еталонним декодером.
  - Портативний C++26 cooker та CLI `xray_maya_cook_cli` (`sources/xray/maya_cook/`),
    який збирається як у спільному дереві, так і повністю автономно (Linux-ready,
    `-Wall -Wextra`): `meshoptimizer`, портативний `NVMeshMender`, кубічні B-сплайни
    (`bspline_fit.hpp`), відновлена Wild Magic 4 containment (`sources/third_party/wildmagic4/`)
    та `--fit-collision` + `.physics`/`.ph` серіалізація.
  - Перевірка T1 + T2 (живий хост Autodesk Maya 2027 `mayapy.exe`, API `20270200`):
    окрім офлайнових T1-тестів (`xray_maya_cook_unit`, `xray_maya_exchange_unit`,
    `xray_maya_plugin_unit`, автономна збірка), виконано реальний T2 end-to-end прогін
    `xray_maya_live_t2` (`sources/tools/maya/plugin/maya_live_t2_suite.py` ->
    `xray_maya_cook_engine_contract_tests --verify-maya-t2`): реальне завантаження/вивантаження
    плагіна через `maya.standalone` + `.mod`, експорт static `.model` (locator-root +
    multi-shader + mirror scale), `.skeleton`, `.skinned_model` (із `skinCluster` і
    bone-attached локатором), `.clip` (standard + additive з `-l`), `.physics`/`.ph`
    (live WildMagic4 `xray_build_collision`, `xray_constraint`, fixed-flattening)
    із валідацією вихідних бінарників реальними engine C++/Vulkan/Collision/Config
    завантажувачами (див. `sources/tools/maya/README.md`).

  Прогрес 2026-11-15: додано collision sphere/box/cylinder, усі original
  rigid-body parameters, transactional settings/collision source pair,
  independent Bullet preview і authored Simulate. Native/GPU Debug/Release
  перевірили advanced properties, fresh query, source reopen та failure retention.
  Skeletal model editor показує named bones/bind positions і відтворює engine
  clip на справжньому mesh в independent Vulkan output; 62-bone reference та
  свіжий 3-bone Maya 2027 export перевірено в Debug/Release. Виправлено VFS
  case-folding для authored surface keys і affine bind restoration.
  Material editor отримав explicit undoable instance schema repair, original
  palette (9 stages/25 effects), typed parameter grid і atomic dependent
  preparation після зміни model-override permissions бази.
  Evidence та reproduction: sources/xray/editor/sdk_ui/sdk6/model-physics-skeletal.md
  і build/sdk6-final-20261115/.
  Фінальне закриття 2026-11-15: додано volume (`texture_dimension = 2`) і
  2D array (`texture_dimension = 4`) source strips з мультишаровими Vulkan
  slice previews та legacy `<1.2` options upgrade/classification; трансляцію та
  preview для `g_stage`/`forward`/`f_stage`/`decals`/`distortion` і класифікацію
  `Supported` vs `SchemaOnly` для всіх 9 stages / 25 effects; `lod_hierrarchy`
  (`LOD0`–`LOD2` + `NextLodSurface`), `SdkCompositeModelDocument` і
  `SdkSpeedTreeDocument` (`speedtree/*.options`). Debug і Release збірки, CTest
  `30/30` та 18 послідовних Vulkan GPU workflows (`texture`, `cube`, `material`,
  `model`, `physics`, `skeletal`, `render-options`, `maya-skeletal`, `dcc` у
  Debug/Release) пройшли з `M8 validation errors=0 warnings=0`.
  Acceptance report і evidence: `sources/xray/editor/sdk_ui/sdk6/acceptance.json`
  та `build/sdk6-acceptance-20261115/`. Етап **SDK-6** завершено.

### SDK-7. Terrain та інші world tools

- [x] **SDK-7 — редагування світу.**
  Відновити terrain brushes: height, smooth, material paint/layers, cell operations,
  collision refresh, undo/redo і save/reload; перенести наявні light, vegetation,
  compound/composite та інші world tools згідно з SDK-0.
  Зняти залежності від тестових команд на кшталт перемикання одного фіксованого
  шару: робота має йти з обраними користувачем objects/cells/assets.
  Дотримуватися memory/upload bounds і lifecycle поточного renderer; більший
  робочий набір потребує окремого виміряного profile, а не прихованого обрізання.
  **Готово:** відредагований реальний level зберігає geometry, layers, weights,
  transforms і collision після reopen; undo/redo й failed edits перевірені.
  Прогрес 2026-11-15 (Фінальне закриття SDK-7): перенесено й верифіковано всі 7 родин
  етапу (`terrain`, `solid`, `lights`, `composites`, `particle-placement`, `speedtree`,
  `misc`): `.terr` cell/layer/paint/sculpt (`RaiseLower`, `Smooth`, `Noise`, `Flatten`),
  collision-тріангуляцію (`8192` трикутники) та Bullet Simulate, static/skinned visuals з
  `SnapToTerrainNow`, 7 типів джерел світла, SpeedTree з `UniformScale` validation і material
  overrides, particle playback controls, projector decals, procedural sky, 11 типізованих
  misc-локаторів і waypoints, `CreateCompositeFromSelection` з ізольованими nested об'єктами,
  undo/redo, кириличне збереження/перевідкриття `SDK7/світ_інструменти` і pending-close
  lifecycle. Збірка Debug і Release, SDK CTest `5/5`, повний CTest `31/31` у Debug та Release
  (після повторного запуску на транзитні Windows `[WinError 5]` у Blender-розширеннях) і
  9-крокові Vulkan GPU workflows у Debug та Release (`M8 validation errors=0 warnings=0`,
  `verify-sdk-world-tools.py`) пройшли успішно. Виправлено координату terrain pick `(32, -35)`
  поза локатором світла, подвійну інверсію камери в `ProjectViewportEditing.cs` та
  `SdkIdePanels.cs`, а також 120-секундний таймаут підготовки/запуску холодної сцени в Debug.
  Acceptance report і evidence: `sources/xray/editor/sdk_ui/sdk7/acceptance.json` та
  `build/sdk7-acceptance-20261115/`. Етап **SDK-7** завершено.

### SDK-8. Animation editor

- [x] **SDK-8 — animation authoring.**
  Перенести documents, collections, clips, timeline/curves, playback,
  transitions/blending та graph operations, які є в inventory оригіналу.
  Підключити наявні animation data models і runtime, перевірити skeleton/bone
  mapping, scrub/pause/loop, edit/save/reload і незалежний preview.
  **Готово:** reference animation workflow відтворено від відкриття до
  збереження й повторного playback; перевірено pose/time та resource lifetime.
  **Прогрес 2026-11-16:** реалізовано першу частину — оригінальні
  `.anim_collection` з nested/reference items, policies, Undo/Redo, перевіркою
  native clip dependencies та атомарним workspace save/reopen; незалежний mesh
  preview вибраного clip зі scrub/pause/loop/rate і перевіркою skeleton mapping.
  Source edits не змінюють level history, missing clip/cancellation зберігають
  попередній source, невдалий mapping зберігає попередній preview. Додано виконання
  nested/reference random/sequential і cyclic/don't-repeat policies: rate carry
  через межі clips, finite completion, shared reference state і immutable playback
  snapshot. Reference guitar workflow перевіряє cyclic body hold та intro/body/outro
  після зміни policy. Додано flat `.avs`/`.anim_clip` documents зі спільним WPF
  graph, owned node/link/interval commands, source save/reopen, partition bounds і
  native-sampled weighted target preview.
  **Прогрес 2026-11-18:** timed target chains/cycles та переходи Instant, Linear і
  Fermi у `.avs`, редагування кривих weighted links із Undo/Redo, одна шкала часу
  для scrub і playback. Додано прямі й ланцюгові synchronization links (`leaf -> mid -> root`)
  із phase mapping для різної тривалості partitions та перенесенням фази при зміні driver
  в одному group, а також additive animation layers (`is_additive = true`, збереження
  незалежної ваги additive-шару без розбавлення base pose, X-Ray кватерніонна композиція,
  часткові кістки).
  **Закрито 2026-11-19:** завершено всі 5 родин `SDK-8` (`animation-collections`,
  `animation-viewer`, `animation-setup`, `animation-groups`, `animation-placement`):
  додано явний імпорт/ремонт legacy nested `childs` (`anim_seve_01.avs`) та
  дедуплікацію link-ів (`anim_seve.avs`, `free.avs`), `time_scale` інтерполяцію й
  `mixing_type`, редагування `scene.models`, акумуляцію `object_movement` root motion
  та mixer crossfade у колекціях; реалізовано `SdkAnimationSetupDocument`/Editor
  (`.clip_data` 30 FPS `intervals`/`partitions`/`events`, `save_first_as_last`, `.options`),
  `SdkAnimationGroupsDocument`/Editor (controller sets `.lua`/`.options`, crouch group `.lua`,
  WPF hypergraph, інтервальний preview, явну діагностику runtime navigation) і
  `SdkAnimationPlacement` (`tool_animations` фільтр + 7-канальні `float_curve` у
  `AnimationCurveEditorPanel` і `.xprj` roundtrip через `Project.Commands`).
  Debug і Release пройшли збірку (`0 Error(s)`), SDK CTest `5/5` (`29` UI checks),
  повний CTest `31/31`, послідовні Vulkan GPU прогони `-ViewerAdditive` (`validation
  errors=0 warnings=0`, 19 точних readback збігів і 5 змін пози) та підтверджені
  у `sources/xray/editor/sdk_ui/sdk8/acceptance.json` і `build/sdk8-acceptance-20261119/`.

### SDK-9. Sound editor

- [x] **SDK-9 — sound authoring.**
  Підключити оригінальні documents/panels до наявних native sound models,
  відновити collections/composites, properties/curves, placement та audition
  відповідно до inventory. Перевірити devices/backend dependencies окремо
  від UI. Потрібен фактичний playback, а не лише успішно створена DLL.
  **Готово:** звук відтворюється, редагується й зберігається; reload/close
  припиняє playback і звільняє resources, missing files мають коректну діагностику.
  **Закрито 2026-11-19:** завершено обидві родини `SDK-9` (`sound-placement`,
  `sound-editor`): реалізовано `SdkWavAudioData` (RIFF/WAVE PCM 8/16/24/32-bit і
  IEEE float декодер + window RMS + 16-bit PCM мікшер), `SdkSoundAudioBackend`
  (відокремлена перевірка `winmm` hardware devices від UI + `WinMM/SoundPlayer+PCM` /
  `SoftwarePCM-Headless` playback session), `SdkSingleSoundDocument` (`.raw_options`
  профілі якості, `.single_sound_options` `spl` крива через WPF `curve_editor_panel` /
  `float_curve_evaluator`, Undo/Redo, атомарне save/reopen), `SdkSoundCollectionDocument`
  (`.sound_collection` `random` із `dont_repeat_sound_successively`, `sequential` із
  `cyclic_repeat_from_sound`, підтримка symbol `resources.single_sound_type` та inline
  вкладених колекцій), `SdkCompositeSoundDocument` (`.composite_sound` паузи,
  `random`/`constant` offsets, multi-layer PCM audition), `SdkSoundSceneDocument`
  (`.editor_sound_scene` 3D listener spatial attenuation/stereo pan) та `SdkSoundEditor` /
  `WindowIde.Sound.cs` (`Tools -> Sound editor`, `ResourceBrowser`, зупинка і
  звільнення буферів при reload/close під час playback); відновлено level placement
  через `tool_sound` (`ToolsTab`, `ProjectEditing.CreateObject`, `ProjectSoundProperties`
  у `SdkWorldTools.cs` для `base:sound` і `base:volumetric_sound`, `sound_type` `point`/`cone`,
  `emitter_type`, `radius`, `collision_geometry`, spatial cone audition, Undo/Redo,
  Cyrillic `.xprj` збереження з ідентичними reference і Vulkan locator rendering у
  `SdkScene.cs`). Збірки Debug/Release пройшли (`0 Warning(s)`, `0 Error(s)`), SDK CTest
  `5/5` (`30` UI checks), повний CTest `31/31`, послідовні Debug/Release Vulkan GPU
  прогони `-SoundCommands -ClosePending` (`validation errors=0 warnings=0`, 4 stage
  transitions і 4 точні Undo/Redo/Reopen readback збіги), прийняття зафіксовано в
  `sources/xray/editor/sdk_ui/sdk9/acceptance.json` і `build/sdk9-acceptance-20261119/`.

### SDK-10. Particle editor

- [x] **SDK-10 — particle authoring.**
  Перенести активний module `editor/particle/sources/particle_editor*`, graph documents, node palette,
  connections, properties, data sources і timeline з урахуванням SDK-0.
  `world/sources/particle_editor.cpp` відсутній у entries original `editor.vcproj`;
  не вмикати його як другу authoring implementation. Placement у world проходить
  через `tool_particle`/`object_particle` і перевіряється окремо.
  Пов'язати graph serialization із фактичними runtime emitter/action/material
  capabilities. Розширювати runtime тільки під конкретні обов'язкові nodes;
  обмежений M8 billboard profile не зараховується за весь particle editor.
  **Готово:** effect можна створити, з'єднати nodes, переглянути, змінити,
  зберегти й повторно завантажити; результат працює також у level.
  Перевірено node transactions/undo та close під час preview/resource loading.
  **Закрито 2026-11-19:** завершено родину `SDK-10` (`particle-editor`): реалізовано
  `SdkParticleLibrary` (`particle_source.library` — 9 категорій `particle_toolbar`, 29 активних
  типів вузлів + `Root`, 10 `domain_types`, класифікація runtime capabilities),
  `SdkParticleDataSource`/`SdkParticleDataSourcesHolder` (`float`, `float3`, `color`,
  `color_matrix`), `SdkParticleDocument` (завантаження/збереження оригінальних `.particle`
  Lua-графів, заборона циклів/невалідних з'єднань, групові/піддеревні `CommandEngine` Undo/Redo
  операції, `DomainType`, `Delay`/`Duration`/`TotalLength`, атомарний `Save()`),
  `SdkParticleSimulator` (часові вікна, домени, криві, `KillVolume`, події `onPlay`/`onBirth`/
  `onDeath`/`onCollide`), `SdkParticleBinaryCompiler`/`SdkParticleRuntimeRegistry` (24-byte-node
  `checked_config` `.particle` + `.material` із CRC32-сортуванням та динамічним VFS-монтуванням
  епох через `ISdkResourceBackend.Mount`), а також `SdkParticleEditor` (`hypergraph_host`,
  `time_layout_host`/`time_layout_item`, `curve_editor_panel`, `property_grid.host`, безпечне
  закриття під час активного preview/pending load) і `WindowIde.Particle.cs` / `tool_particle`
  (`ProjectEditing.cs`, `SdkScene.cs`). Збірки Debug/Release пройшли (`0 Warning(s)`, `0 Error(s)`),
  SDK CTest `5/5` (`31` UI checks), повний CTest `31/31`, послідовні Debug/Release Vulkan GPU
  прогони `-ParticleCommands -ClosePending` (`validation errors=0 warnings=0`, 7 переходів сцени і
  2 піксельно-точні Undo/Redo readback збіги `1==3`, `2==4`), прийняття зафіксовано в
  `sources/xray/editor/sdk_ui/sdk10/acceptance.json` і `build/sdk10-acceptance-20261119/`.

### SDK-11. Dialog, logic та решта інструментів inventory

- [x] **SDK-11 — завершення інструментального покриття.**
  Відновити наявні dialog, scene logic/scripting, patrol/navigation та інші
  редактори/панелі з обов'язкового inventory; кожен має власні reference cases.
  Використовувати загальні graph controls, document/history та serialization
  services, а не окремі несумісні реалізації цих механізмів.
  Наявність DLL чи пункту меню не закриває функцію без її behavior proof.
  **Готово:** всі відповідні inventory rows проходять open/edit/save/reopen,
  undo/redo та runtime integration там, де він потрібний; gaps відсутні або
  мають окремо погоджене рішення, яке видно в кінцевому статусі SDK.
  **Закрито 2026-11-19:** завершено всі чотири родини `SDK-11`
  (`dialog-editor`, `dialog-excel`, `scripting`, `navigation`): реалізовано
  `SdkDialog.cs` / `WindowIde.Dialog.cs` (повний граф `.dlg`/`.template` на
  `hypergraph_host` і `CommandEngine` Undo/Redo, дерево умов `AND`/`OR`/`NOT`,
  дії, локалізація `localizations.cfg`/`references.cfg`/`*.localization`,
  компілятор `SdkCompiledGameDialog` із розгортанням вкладених діалогів і
  runtime-виконанням, беззалежний `SdkDialogExcelExchange` OpenXML `.xlsx` +
  SpreadsheetML 2003 `.xls` + `.batch` із фільтром `references_count` та
  багаторядковим Unicode), `SdkScripting.cs` / `WindowIde.Scripting.cs`
  (активація маршруту `"scripting"` у `ToolsTab`, `Tools -> Logic / Scripting editor`,
  `PropertyConnectionEditor.BindProject` з Undo/Redo, серіалізація
  `property_links` у `project.xprj`, події `SdkLogicRuntimeEngine`), а також
  `SdkNavigation.cs` / `WindowIde.Navigation.cs` (`navmesh.lua` сумісно з
  `graph_generator_serializer.cpp`, `Tools -> Navigation mesh...`, генератор з
  терейну й перешкод, автозбереження/завантаження в `Project.cs`, пошук шляху
  `SdkNavMeshPathfinder` A* + funnel). Збірки Debug/Release пройшли
  (`0 Warning(s)`, `0 Error(s)`), SDK CTest `5/5` (`32` UI checks), повний
  CTest `31/31`, послідовні Debug/Release Vulkan GPU прогони
  `-Sdk11Commands -ClosePending` (`validation errors=0 warnings=0`, 6 переходів
  сцени та 2 піксельно-точні Undo/Redo readback збіги `4==6`, `5==7`),
  прийняття зафіксовано в `sources/xray/editor/sdk_ui/sdk11/acceptance.json` і
  `build/sdk11-acceptance-20261119/`.

### SDK-12. Сумісність, пакування й остаточне приймання

- [x] **SDK-12 — придатний до роботи SDK.**
  Зібрати окремі чисті Windows x64 Debug/Release SDK runtimes із точними
  dependencies, settings, launch command і розділеними writable data/caches.
  У SDK deployment `-editor` відкриває відновлену IDE; контрольний Vulkan host
  лишається окремим test harness. Перевірити перелік функцій із SDK-0,
  щоденний end-to-end workflow, довгу сесію з реальним редагуванням,
  project switching, document/preview reopen, cancellations і shutdown.
  Перевірити задіяні renderer paths у відповідних SDK scenarios; старі migration
  suites архівовані й не є чинними build targets. Фінальну SDK-матрицю виконати
  послідовно без паралельних GPU workloads. Зафіксувати actual validation,
  sources/binaries/dependencies/input hashes, budgets і residual limitations.
  **Готово:** SDK проходить весь обов'язковий inventory і реальні workflows
  оригіналу; його стан не підмінено результатами контрольної сцени.
  **Закрито 2026-11-19 / перевірено 2026-11-20:** зібрано автономні Debug (`1244`
  файли) та Release (`1242` файли) пакети в `build/sdk12-package-20261119/` із
  повним PE-аудитом (`40/40` бінарників), власними DXC (`dxcompiler.dll`/
  `dxil.dll`), Vulkan validation layers, повним каталогом шейдерів `shaders/` і
  розділеними `user-data/`/`cache/`. Запуск із `-editor` за замовчуванням
  відкриває `WindowIde` (`editor_entry_point.cpp`, `EditorWorld.Sdk.cs`,
  `shader_compiler.cpp`, `core_entry_point_win.cpp`, `engine_world_win.cpp`,
  `managed_host.cpp`), а контрольний Vulkan host лишається окремим test harness
  (`-ControlHostHarness` / `XRAY_SDK_PROPERTY_EDITOR=0`). Закрито `40/40` родин у
  `function-matrix.json` (`36` обов'язкових + `4` опційні B6 DCC), `15/15`
  renderer-маршрутів, старі migration suites та `.vcproj`/C++/CLI цілі
  підтверджено архівованими. Фінальні послідовні перевірки пройшли чисто в Debug і
  Release (`0 Warning(s)`, `0 Error(s)`, SDK CTest `5/5` із `33` перевірками,
  повний CTest `31/31`, 5 послідовних GPU-прогонів зі scrubbed `PATH` і
  незмінними `bin/shaders/assets/settings`: `gpu-debug-packaged` /
  `gpu-release-packaged` — `8` візуальних переходів, піксельно-точний Undo
  `6==8`, `validation errors=0 warnings=0`, бюджети `peakWorkingSetMb` `2237.97`/
  `2236.87 <= 4096`, `managedHeapMb` `54.08`/`52.07 <= 512`, `stress25TransformMs`
  `8068`/`7125 <= 15000`, `totalSessionMs` `54845.73`/`40273.13 <= 120000`;
  `control-host-debug`/`release` та `render-modes-release` `21/21` режимів). Усі
  відбитки та залишкові обмеження зафіксовано в
  `sources/xray/editor/sdk_ui/sdk12/acceptance.json`,
  `sources/xray/editor/sdk_ui/sdk12/README.md` і `build/sdk12-acceptance-20261119/`.

## Правила перевірки та обліку

Один пункт відповідає за один результат; загальні механізми повторно
використовуються іншими модулями. SDK-2 володіє controls, SDK-3 — persistence
та resource services, SDK-4 — commands/history, SDK-5 — renderer bindings.
Domain-модулі додають свої operations, не дублюючи ці інфраструктурні задачі.

Для кожної обов'язкової inventory row потрібні reference behavior, actual build
inclusion, real UI/native route, save/reopen proof і lifecycle result.
Screenshots підтверджують layout та видимий результат; вони не доводять
збереження, undo/redo, реальний playback або runtime integration.
Допустимі відмінності сучасного DPI/fonts фіксуються явно; команди й дані
не можна втрачати під виглядом косметичної модернізації.

Оригінальний `Stalker2_2011` використовується read-only. Зміни та перевірки
виконуються на workspace-копіях. Не змінювати accepted M5/M8/M9 roots.
Після релевантних source changes використовувати нові build/evidence roots;
старий checkpoint не переписувати. На старті SDK-0 зафіксувати конкретні
fixture projects/assets та UI/dependency baseline, потім створювати checks
від реальних ризиків: data loss, ABI/lifetime, undo state, stale cache,
async cancellation та втрачена функціональність.

GI/LPV та інші відкладені renderer features зберігають попередній статус.
Якщо обов'язковий original SDK workflow потребує їх, це видима залежність,
яка блокує відповідне приймання; автоматично виключати workflow заборонено.
Рішення про реалізацію або окреме прийняття обмеження має бути явним.
Повну відповідність SDK не оголошувати, поки обов'язкові залежності відкриті.

## Перший практичний результат

### SDK-0, 2026-10-23: початкова інвентаризація та reference startup

Початкову матрицю 36 функціональних груп збережено у
`sources/xray/editor/sdk_ui/sdk0/function-matrix.json`; її 909 source references
пов'язують original project inclusion, поточну native/managed збірку,
залежності та перевірку поведінки. Це не 36 уже перенесених функцій.

- Pinned-source snapshot: 2483 files, усі Git blob hashes перевірено. 982
  збігаються побайтно з локальними; 1225 — без різниці CRLF/LF. 257 відсутніх:
  79 legacy docking files, замінених modern package; 8 legacy project files;
  170 Maya/Maya-animation files, доступних у reference, але не в active build.
  Modified files не перекривалися. Для legacy UI іноді потрібен саме pinned
  reference, бо local world/animation/sound factories уже переписані.
- PE/CLR inventory 45 binaries читає metadata без execution: SHA-256,
  architecture, assembly references, native/delay imports. Actual docking DLL
  має 2.3.4358.33208, а .resx — старішу identity 2.3.4183.35052. Original CLR2,
  Framework2/3/3.5, VC90 та Office12 dependencies не є новим product baseline.
- Original world відкриває окремі model/particle/dialog/animation/sound tool
  windows через factories відповідних DLL; це не standalone executables.
  Texture/material/material-instance/render-options реалізовані в world.
  Legacy world/particle_editor.cpp не входить до original editor.vcproj.
  Scripting tool зареєстрований, але його tab закоментований. Деталі маршрутів
  та source lines є у матриці; дублікати автоматично не вмикаються.
- Зібрано 339 action registration candidates, 85 menu/tool registrations,
  42 format declarations, 17 .resx UI strings і 121 route/exporter reference.
  Reachability/declarations/dead code ще потребують перегляду. Maya static,
  skinned, physics та animation export і dialog Excel COM виділено окремо.
  Дев'ять original seed assets мають hashes, але повні dependency closures
  та придатність fixtures до acceptance ще не доведені.
- Створено повну незалежну workspace-копію x64 binaries/mounts/resources:
  12 680 assets, 37 457 287 306 bytes. Copy завершилася без помилок; binary
  hashes, усі asset paths/sizes і hashes seed assets перевірено. Original SDK
  не змінювався. Probe завершився до UI: exit 0xC0000142. Application Error1000
  підтверджує copied path/PID і xray_engine_pc.dll, exception0xC0000005,
  offset0x10F820. Причина не встановлена; це не доказ NVIDIA causation/fix.
  Historical documentation UI.JPG переглянуто та збережено з provenance;
  воно не зараховується як новий runtime screenshot.
- Перевірка integrity пройшла: незалежний GNU objdump cross-check трьох
  native import tables, build-membership spot checks, copy/fixture integrity,
  exact-path/PID correlation crash event. Source contract CTest пройшов.

Докази: `build/sdk0-baseline/`, `build/sdk0-binary-inventory/`,
`build/sdk0-original-reference/`, `build/sdk0-summary.json`.
На цьому checkpoint наступною роботою SDK-0 були action-level review, повний перегляд documentation,
fixture closures та остаточний required-function inventory. Runtime blocker
дозволяє продовжити source/documentation reference, але не оголосити UI acceptance.

### SDK-0, 2026-10-24: commands, documentation і fixture dependencies

Додано відтворювані `review-sdk0.py` та `check-sdk0-fixtures.py`; генератор
матриці перевіряє source commit/input hashes та asset/seed hashes. У 36 групах
тепер 921 source references, усі 305 command candidates мають source-owner
association. Це не змінює product build inclusion та не є runtime acceptance.

- Action review зберігає всі 339 IDs: 272 registrations, 32 conditional,
  один disabled construction route, 29 entries поза original projects та
  5 API/forwarding. 92 menu/toolbar bindings зіставлено; два API definitions
  вилучено з UI binding count. Первинні 85 menu/tool entries також класифіковано.
- 21 shortcut overlap group потребує predicates/priority checks. Global
  `Control+P` використовується particle editor і pin selection у спільному
  world input engine; поведінковий конфлікт ще не доведено.
- Переглянуто 5 DOCX, 3 TXT і 1 XLSX, 18 copies. Зафіксовано розбіжності sound
  document/source formats, curve preset stubs, New Filter → folder handler та
  непідтверджений human-template UI. Design proposals не зараховуються як
  реалізовані можливості оригіналу. Image/PSD review ще відкритий.
- Перевірено 9 fixture seeds та SHA-256 64 files. Sound scene/composite мають
  замкнені static sound references. Rocket collection має три відсутні WAV;
  знайдено два replacement candidates. У project відсутній exact model path,
  у viewer scene — exact clip, particle seed порожній. Dialog localization IDs
  знайдено у трьох мовах. Runtime/cook acceptance усіх fixtures ще відкрите.

Висновки: `sources/xray/editor/sdk_ui/sdk0/review-findings.md`.
Докази: `build/sdk0-review-20261024-final/`,
`build/sdk0-fixtures-20261024-verified/`. Попередні checkpoints збережено.
18 integrity checks пройшли (`build/sdk0-audit-20261024.json`), включно з
negative stale-evidence test; source-contract CTest пройшов. Підсумок цього
проходу — `build/sdk0-summary-20261024.json`.
На цьому проміжному checkpoint SDK-0 залишався відкритим до route/event та image/design review і остаточного
required-function inventory; startup blocker не заважає цим перевіркам джерел.

### SDK-0 завершено, 2026-10-24: required inventory та UI/source coverage

**Прийнятий результат SDK-0:** inventory оригіналу із source/documentation
fallback через зафіксований startup blocker. `function-matrix.json` має
`sdk0Complete: true`; `inventoryAcceptance` визначає межі покриття,
`implementationFollowUps` — known gaps із відповідальними SDK stages.
Працездатність editor workflows, форматів і cooks перевіряється в SDK-1–SDK-12.

- Усі 305 command candidates мають registration body, action initializer,
  enabled/capture source candidates і один із 16 reviewed construction routes.
  51 explicit enabled binding відокремлено від inherited defaults. Object painter
  залишається dormant route; scripting tab позначено як закоментований.
- Додано 1371 explicit UI event bindings: 1096 interaction/data candidates,
  82 lifecycle/layout/presentation та 193 not-listed source entries. Для 1320
  bindings є один source handler candidate, 23 передають делегати, 3 мають
  virtual/interface dispatch, 25 — inline delegate. Unresolved targets і XAML
  parse errors відсутні. Це lexical source inventory з explicit coverage limits.
- Переглянуто всі 54 stored images/PSD composites, 108 copies. Annotated Tags/
  Library/eye/lock layout та curve palette — design provenance; forest/tree/pine
  references — art provenance. Reviewed image fingerprint захищає рішення
  від автоматичного застосування до змінених зображень.
- Maya inventory: 9 commands, 1 translator, 2 nodes і 3 transforms. Імена
  звірено з literals/macros. Current-host compatibility залишається SDK-6/12.
- Виправлено SDK-10: authoring переноситься з активного `editor/particle`
  module; excluded old `world/particle_editor.cpp` не вмикається як дубль.
- Пройшли 32 acceptance/integrity checks та source-contract CTest. Перевірено
  всі 2483 reference hashes/Git blobs, documentation hashes, delegates/overloads/
  inheritance, routes і stale/missing/changed-image evidence rejection.

Критерії та повторення: `sources/xray/editor/sdk_ui/sdk0/acceptance.md`.
Докази: `build/sdk0-events-20261024-audited/`, `build/sdk0-images-20261024/`,
`build/sdk0-routes-visuals-20261024-complete/`, `build/sdk0-ui-audit-20261024.json`,
`build/sdk0-source-contract-complete-20261024.log`, `build/sdk0-complete-20261024.json`.
Наступний незавершений етап — SDK-1 за його окремими acceptance criteria.

### Виконано 2026-10-22: джерела та сумісність controls

- Відновлено 418 original source/resource файлів controls/dialog/model/particle
  із pinned 2011 commit; 4 legacy `.vcproj` збережено окремо як reference
  у `build/sdk-modernization/legacy-projects/`. Локальні engine/bridge файли
  не перекривалися.
  Кожен файл перевірено за Git blob hash; переносний receipt:
  `sources/xray/editor/sdk_ui/upstream-files.json`. Legacy C++/CLI targets
  залишаються вимкненими до адаптації. Знято SHA-256 inventory 11 original
  UI/editor DLL; оригінальний SDK не запускався й не змінювався.
- За офіційними release metadata використано останні стабільні .NET SDK
  **10.0.402**, Desktop runtime **10.0.13**, C# **14**, DockPanel Suite і theme
  **3.1.1**, Extended WPF Toolkit **5.1.2**. `global.json` фіксує SDK,
  NuGet lock files — resolved versions/content hashes. Нові UI builds
  використовують locked restore. Preview/RC не є default baseline.
- Відновлено modern `wpf_controls.csproj` із original build inclusion:
  286 C# sources, 86 XAML pages, 19 resources. У 423 `.cs/.xaml` файлах
  baseline є також файли поза original project; вони не ввімкнені автоматично.
  Graph numeric editor перенесено на current Xceed namespace/package.
- Private reflection `AvalonAdapter._hostControl` замінено явним WPF owner
  property із успадкуванням та очищенням при dispose. Додано designer
  serialization attributes для runtime properties, актуальні FormClosed
  events, DPI-aware FormattedText та modern RuntimeHelpers allocation.
  WPF warnings є errors; виняток CS8981 зберігає original public/XAML names.
- Додано `xray_sdk_ui`, `xray_sdk_ui_compatibility`, build script та інструкцію
  `sources/xray/editor/sdk_ui/README.md`. Це compatibility executable,
  не production IDE. Debug/Release збираються без warnings/errors; по 5 checks
  проходять на .NET 10.0.13: owner/disposal, property/curve/graph construction
  і rendering, numeric editor, property ElementHost, docking XML roundtrip.
  Captures перевіряють завантаження controls/resources, не domain workflows.
- Загальний source contract допускає лише два конкретні modern SDK-style
  `.csproj` із `net10.0-windows` та відхиляє legacy project formats.
  Перевірка `xray_source_contract` пройшла після відокремлення old `.vcproj`.

Докази: `build/sdk-modernization/` (Release та первинний Debug),
`build/sdk-ui-cmake/sdk-ui/evidence-debug/` (фінальний Debug через CMake/CTest).
Контрольний `EditorHost` також збирається новим SDK в окремому build root.
Прийняті Vulkan roots не використовуються для нових результатів.

Цей controls milestone сам по собі не закриває SDK-0 або SDK-1.

### Виконано 2026-10-23: native property → viewport → undo/redo → close

- Оригінальний WPF property grid підключено до справжньої `woodbox_mil` моделі
  контрольної native-сцени. Панель редагує XYZ, зберігаючи viewport HWND.
  Режим вмикається `XRAY_SDK_PROPERTY_EDITOR=1`; повної original IDE він
  ще не відновлює. Під час завантаження controls не дозволяють редагування.
- Додано окреме SDK extension ABI 1: version/size, object ID із generation,
  revision та copied XYZ payload. Незмінний host ABI 2 зберіг структури
  callbacks 80 bytes / world bridge 112 bytes. Native сторона перевіряє
  thread owner, readiness, identity/revision та finite coordinates, після чого
  викликає renderer `scene().update_model`. Borrowed payload pointers не
  зберігаються. Read/write методи мають різні імена: реальний GPU-прогін
  виявив і допоміг усунути рекурсію через C++ overload resolution.
- Додано обмежену історію 100 position commands для pilot: Undo/Redo та нова
  гілка після undo. Відхилений native write не змінює стан UI, history cursor
  або redo branch. Це не замінює original command/transaction/preview services
  SDK-4. Numeric drag commit виконується після завершення редагування;
  numeric binding обробляє setter exceptions через WPF validation.
- WPF library initialization зроблено повторюваною без повторної реєстрації
  routed events. Selection, history, attached owner і native callbacks
  від'єднуються до native resource drain; HWND знищується після drain.
  Hosted DLL тепер копіює WPF/Toolkit runtime dependencies і використовує
  окремий lock file та locked restore. Baseline залишається .NET SDK 10.0.402,
  Desktop 10.0.13, C# 14, Toolkit 5.1.2, DockPanel Suite 3.1.1.
- Managed Debug/Release: 6 груп checks, без warnings/errors. Додатково
  перевірені ABI rejection, failed edit/undo/redo, branch replacement,
  stale revision, nonfinite value, foreign thread, calls after clear/destroy,
  повторне створення UI в тому самому процесі. CTest compatibility та source
  contract проходять. Fake callbacks тут не зараховуються за GPU evidence.
- Native Debug + Vulkan validation: п'ять реальних readbacks 960×640.
  X=25 → 27 → 25 → 27 → 25; revision 0 → 4. Редагування змінило 1625 pixels
  із channel delta >2. Undo та остаточне відновлення піксельно збігаються
  з baseline; redo піксельно збігається з edited frame. Шість malformed/stale/
  nonfinite native writes відхилено без зміни payload чи сцени. Після close
  native resources drained, bridge destroyed; Vulkan errors=0, warnings=0.
- Regression run без SDK extension також пройшов: host ABI 2, звичайний
  viewport capture, native resource drain та shutdown, Vulkan errors=0/warnings=0.
  Докази: `build/sdk-property-legacy-verified/`. У Debug залишається старе
  нефатальне повідомлення про відсутню BugTrap library; цей етап її не переносить.

Докази: `build/sdk-property-native-verified/` (runtime-result.json,
capture-result.json, 5 readbacks, launch identity, logs),
`build/sdk-ui-cmake/sdk-ui/evidence-debug/`,
`build/sdk-property-tests-release/evidence-release/`.
Повторювані команди: `sources/tools/sdk/test-sdk-property.ps1` та
`sources/tools/sdk/verify-sdk-property.py`; деталі ABI/build у
`sources/xray/editor/sdk_ui/README.md`. Усі native runs використовують
окрему копію assets; прийняті M8/M9 roots не змінюються.

На момент цього property milestone docking/layout залишався наступним кроком;
його результат наведено нижче. На той момент SDK-0 inventory/reference behavior
ще залишався відкритим; фіналізацію inventory наведено в milestone 2026-10-24.

### Виконано 2026-10-23: dockable property panel і layout restore

- `SdkDockLayout.cs` адаптує original `controls/item_properties_panel_base`:
  DockContent із original WPF host, selection/read-only та tool docking areas.
  Native host використовує DockPanel Suite/theme 3.1.1 з locked dependencies;
  Scene document зберігає renderer HWND. Додано Properties, Save layout і Load
  layout; файл обирає користувач. Збережено stable identities та alias
  `xray.editor.window_view`. Це layout двох pilot panels, не імпорт усієї
  original IDE registry configuration.
- XML проходить перевірку identities, DTD/size та library schema в окремому
  контейнері до зміни live contents. Панелі від'єднуються без dispose; selection,
  history і viewport зберігаються. Виправлено резервування місця порожнім
  DockWindow під hidden owner, яке перший GPU-прогін виявив як зміну ширини
  viewport 946 → 639. Приймання вимагає збереження також client dimensions.
- Managed Debug/Release: по 6 груп checks, zero warnings/errors. Перевірено
  malformed XML, DTD, unknown identity, legacy scene alias і три повторні
  переміщення/відновлення layout зі збереженням selection, HWND та geometry.
  CTest source contract і compatibility проходять після завершення builds.
  Source contract не запускається одночасно з WPF compilation: compiler
  тимчасово створює `*_wpftmp.csproj` поряд із source project.
- Native Debug + Vulkan validation: 9 readbacks 946×636, revision 0 → 8.
  Після save → move left → reload right повторний edit/undo/redo/undo дає
  піксельно ті самі стани, що й до layout restore. Edit змінює 1607 pixels
  із channel delta >2; усі 7 порівнянь відповідних restored/redo states мають
  нульову різницю. HWND, viewport dimensions, selection і redo branch збережено;
  6 invalid native writes відхилено. Selection/callbacks очищено до native
  resource drain; штатний shutdown, Vulkan errors=0/warnings=0.
- Regression попереднього host ABI 2 без SDK mode проходить у
  `build/sdk-docking-legacy/`. Поточні докази: `build/sdk-docking-native-verified/`
  (runtime/capture results, layout.xml, 9 BMP, logs),
  `build/sdk-ui-cmake/sdk-ui/evidence-debug/`,
  `build/sdk-docking-verified-release/evidence-release/`,
  `build/sdk-docking-verified-ctest.log`. Зведення і source hashes:
  `build/sdk-docking-summary.json`. Початковий прогін із помилкою geometry
  збережений окремо у `build/sdk-docking-native/`; він не є accepted evidence.

**SDK-1 ще не закрито:** залишаються keyboard focus/text input і DPI,
interactive floating/auto-hide, native Release та close із pending work
саме в SDK-режимі. Для pending-close та DPI після цього запису з'явився
неперевірений код — див. звірку 2026-11-08. SDK-0 inventory завершено;
reference runtime limitations та інші known gaps мають
implementation/acceptance-stage owners.

### Звірка коду з планом, 2026-11-08

Перевірено робочу копію `C:/Backup/Source/xray-2-2011-main` за source/build
inclusion і mtime файлів; Git history відсутня (жодного commit). Стан SDK-0 і
виконаних milestones SDK-1 від 2026-10-22/23 збігається з кодом: modern
`wpf_controls.csproj` (286 Compile / 86 Page / 19 Resource), owner property
замість `AvalonAdapter`, 418 відновлених файлів controls/dialog/model/particle
(upstream 76/143/100/103 мінус по одному `.vcproj`), `SdkProperties.cs`,
`SdkDockLayout.cs`, `sdk_property_bridge.h`, `sdk0Complete: true`.
SDK-2–SDK-12 у коді не розпочато: `WindowIde.cs`, `CommandEngine.cs`,
`InputEngine.cs`, `ProjectTab.cs` не входять у `EditorHost.csproj.in`;
controls/dialog/model/particle закоментовані в `editor/CMakeLists.txt`.

**Код після останнього запису плану (2026-10-24 14:29), не прийнятий:**

- SDK pending-close: diagnostic step 9 під `XRAY_SDK_CLOSE_PENDING=1`
  (`EditorWorld.Sdk.cs`, `editor_entry_point.cpp`), `pending_queries()`/
  `rejected_previews()` у `render/facade/control_level.h`, перемикач
  `-ClosePending` у `tools/sdk/test-sdk-property.ps1`. Перевіряє утримання
  preview query під час close, drain до знищення HWND і відхилення preview.
- DPI: `xray/stalker2/pc/stalker2.manifest` (`PerMonitorV2`, `longPathAware`)
  вбудовано через `stalker2.rc`. Це лише підготовка: multi-monitor DPI,
  picking coordinates і resize не перевірені.

Evidence для цих змін відсутнє; SDK-1 пункти pending-work та DPI лишаються
відкритими до прогону.

**Відсутні evidence roots і blockers середовища (2026-11-08):**

- У `build/` цієї копії є лише `sdk0-original-reference/`. Відсутні всі інші
  roots, на які посилається план: `sdk0-baseline/`, `sdk0-*-20261024*`,
  `sdk-modernization/` (включно з `legacy-projects/` — 4 original `.vcproj`
  і локальний .NET SDK), `sdk-ui-cmake/`, `sdk-property-*`, `sdk-docking-*`,
  `m8-control/assets`, `vulkan-validation/`, `dxc/`, M5/M8/M9 roots. Інших
  копій на диску не знайдено. Виконані milestones тут підтверджуються лише
  джерелами; повторний прогін потребує відновлення цих roots або нового build.
- Встановлено .NET SDK 10.0.203 і Desktop runtime 10.0.7; `global.json`
  (10.0.402, `latestPatch`) та `RuntimeFrameworkVersion` 10.0.13 їх не приймають,
  тому `dotnet` у репозиторії не запускається. NuGet cache не містить
  DockPanelSuite/Extended WPF Toolkit; locked restore потребує мережі або
  `-PackageSource`.

### SDK-1 завершено, 2026-11-08: відновлення середовища, pending-close, Release, focus/DPI та floating/auto-hide

- Відновлено робоче середовище: `.NET SDK 10.0.402` (Desktop runtime `10.0.13`)
  у `build/sdk-modernization/dotnet/`, locked NuGet feed у `build/sdk-modernization/nuget/`,
  4 архівні `.vcproj` у `build/sdk-modernization/legacy-projects/`, DXC `v1.9.2609`
  у `build/dxc/`, Vulkan validation layers у `build/vulkan-validation/`, та
  загальну папку асетів `build/sdk-assets` із read-only `C:/Stalker2_2011/xr2-dsgn/resources`
  (`175` prepared outputs, `130` unchanged original inputs, `manifest_sha256`:
  `b9cf3b00202857e7a170f1807438ec4b4a8268b17129f9507c03ba92992fd21a`).
- Перевірено keyboard focus routing, text editing commit/escape, auto-hide
  (`DockRightAutoHide` → `DockRight`), floating window transition (`FloatAt` →
  redock зі збереженням selection та viewport HWND) і multi-monitor DPI scaling
  (100% / 125% / 150% / 200% поверх `PerMonitorV2` manifest).
- Пройдено CMake/CTest (`5/5` у `build/sdk-native-20261108-debug` і
  `build/sdk-native-20261108-release`) та всі 5 native Vulkan GPU прогонів:
  Debug/Release з `-ClosePending` (`build/sdk1-debug-full-20261108`,
  `build/sdk1-release-full-20261108`, `build/sdk1-debug-pending-20261108`) і
  Debug/Release `-LegacyHost` (`build/sdk1-debug-legacy-20261108`,
  `build/sdk1-release-legacy-20261108`), плюс standalone `SDKUI.Compatibility`
  (`build/sdk-ui-20261108-v2`).

### SDK-2 завершено, 2026-11-09: IDE shell (`WindowIde`), спільні controls, панелі та діалоги

- Перенесено підсистему команд, хоткеїв, прив'язки меню/тулбарів та іконок у
  `sources/xray/editor/base/`: `Action.cs`, `ActionContinuous.cs`, `ActionShortcut.cs`,
  `ActionsLayer.cs`, `InputKeysHolder.cs`, `InputEngine.cs` (пріоритети шарів,
  контекстні предикати для перетинів хоткеїв на кшталт `Control+P`), `GuiBinder.cs`
  (синхронізація `MenuStrip`/`ToolStrip` із `Checked`/`Enabled` станами та
  shortcut labels) і `ImageLoader.cs` (`73` іконки `16x16` із вбудованого
  `images/images16x16.png`).
- Перенесено спільні controls (`xray.editor.controls` у `SdkControls.cs`):
  - `tree_view`, `tree_node`, `tree_view_filter_panel`, `tree_view_search_panel`
    (ієрархічне додавання/видалення за шляхами, фільтрація з контекстом предків,
    пошук із циклічним переходом next/prev, multi-select, `track_active_node`);
  - `document_base` і `document_editor_base` (`file_view_panel_base`,
    `item_properties_panel_base`, `toolbar_panel_base`), з розділенням lifecycle
    приховування tool-панелі (`HideOnClose = true`, без dispose) і закриття
    документа (`HideOnClose = false`, індикатор `" *"` для `is_saved = false`,
    обробка `Cancel`/`No`/`Yes` при закритті незбереженого документа);
  - `hypergraph_control`, `node`, `connection_point`, `link`, `connection_editor`,
    `links_manager_default` на базі оригінального WPF `hypergraph_host`;
  - `serializer` (`SaveDockPanelXml`, `ParseAndValidateLayoutXml`,
    `ClampBoundsToWorkingAreas`, `ClampFloatWindowsInXml`, `SaveWindowSettingsIsolated`,
    `LoadWindowSettingsIsolated`) — ізольоване збереження налаштувань окремо від
    реєстру оригінального SDK 2011 року, відхилення DTD/пошкодженого XML та
    невідомих `PersistString`, притискання off-screen координат відсутнього
    монітора до наявних `Screen.AllScreens`.
- Перенесено оболонку `WindowIde.cs` (`xray.editor.window_ide`), проектні сутності
  (`ProjectItems.cs`, `Project.cs`) та всі панелі й діалоги IDE (`ProjectTab.cs`,
  `ObjectInspectorTab.cs`, `ToolsTab.cs`, `SdkIdePanels.cs`):
  - стабільні `GetPersistString` ідентичності та аліаси з `window_ide_serialize.cpp`:
    `xray.editor.project_tab`, `xray.editor.tools_tab`, `xray.editor.object_inspector_tab`
    (аліас `xray.editor.controls.item_properties_panel_base`), `xray.editor.scene_view_panel`
    (аліас `xray.editor.window_view`);
  - `ToolsTab` із 8 активними інструментами level editor (`object_base`, `light`,
    `composite`, `terrain`, `speedtree`, `particle`, `sound`, `animation`) і 2
    явно задокументованими dormant маршрутами (`object_painter`, `scripting`);
  - `SceneViewPanel` із 21 режимом рендерингу (`ViewModeNames` із `scene_view_panel.cpp`),
    статус-баром/статистикою та DPI-інваріантною математикою 3D picking променя
    (`GetMouseRayFromNormalizedViewportPoint`, похибка `< 1e-6f` між 100%, 150% і 200% DPI);
  - додаткові вікна та діалоги: `CameraViewPanel`, `AnimationCurveEditorPanel`,
    `SceneGraphTab`, `TriangulationTester`, `PropertyConnectionEditor`, `IdeLogForm`
    (Output/Log), `AboutDialog` та `OptionsDialog` із 4 сторінками (`General`,
    `View Panel`, `Project`, `Input`) і повним rollback при `Cancel`;
  - усунено проблему фокусу дочірніх вікон під час перезавантаження вкладених
    `DockPane` у WeifenLuo DockPanel Suite (`SetFocus(Handle)` перед від'єднанням
    панелей у `ApplyLayoutBytesCore` / `ApplyDefaultReferenceWorkspaceLayout`).
- Перевірено всі конфігурації та режими:
  - Standalone `SDKUI.Compatibility` Debug і Release (`build/sdk2-ui-20261109/evidence-debug`,
    `build/sdk2-ui-20261109/evidence-release`): `9/9` checks PASS на `.NET 10.0.13`,
    `0 Warning(s), 0 Error(s)`, згенеровано `ide-workspace.png` та `ide-layout.xml`.
  - CMake build і CTest (`5/5` тестів: `xray_source_contract`,
    `xray_configuration_contract`, `xray_lua_sol_binding`, `xray_windows_x64_contract`,
    `xray_sdk_ui_compatibility`) у `build/sdk-native-20261108-debug` і
    `build/sdk-native-20261108-release`.
  - Native Vulkan GPU прогони (`test-sdk-property.ps1` + `verify-sdk-property.py`):
    Debug із `-ClosePending` (`build/sdk2-debug-full-20261109`), Release із
    `-ClosePending` (`build/sdk2-release-full-20261109`), та `-LegacyHost` регресії
    (`build/sdk2-debug-legacy-run`, `build/sdk2-release-legacy-run`). Усі 9 GPU
    captures `946x636` піксельно ідентичні на відновлених станах (`0.0` mean/max
    channel error), лог підтверджує `SDK IDE shell verified: panels=4 tools=8 render_modes=21 layout_reset=1`,
    Vulkan validation `errors=0, warnings=0`.

### SDK-3 acceptance checkpoint — 2026-11-10

- Production `WindowIde` підключено до project New/Open/Save/Save As/Close,
  recent projects, dirty/save prompts, document ownership і recovery.
  `.xprj`/libraries зберігають GUIDs, paths, types, references та невідомі поля;
  writable working copies і cache відокремлено від read-only original/prepared assets.
- `SdkResourceBrowser`, VFS overlays і dependency inspector використовують native
  resource ABI 1 для mounts/raw/model/material/texture queries. Resource handles,
  invalidation/cancellation і teardown перевірено на prepared SDK assets.
  Cache receipts не підміняють engine cooks. Explicit format copy обмежено
  реалізованою original Lua schema version 1.
- Debug і Release CTest — `5/5 PASS`; compatibility — `11/11` груп на .NET 10.0.13,
  включно зі справжнім modal Cyrillic Save As/reopen, invalid projects, missing
  dependencies, save/recovery failures і document transitions.
- Фінальні full-IDE native/Vulkan прогони з `-ClosePending` та legacy-host регресії
  в Debug/Release — PASS, validation `errors=0, warnings=0`. Дев'ять full-IDE captures
  `946x636` з піксельно ідентичними відновленими станами. Model query — 21416 bytes,
  1 surface; raw VFS source refresh, cache invalidation і cancelled-handle rejection — PASS.
- Докази: `build/sdk3-implementation-20261110/`; звіт із точними межами приймання,
  інтерактивним Open/Cancel, обмеженням Sky для modal keyboard input та hashes:
  `sources/xray/editor/sdk_ui/sdk3/acceptance.md` і `acceptance.json`.
  Повне native scene authoring та domain editors залишаються SDK-4 і далі.

### SDK-4 implementation checkpoint — 2026-11-10

- Зіставлено `CommandEngine.cs` з original `command_engine.cpp`, inline/header,
  `command_transaction.cpp` і transform preview commands до ввімкнення у build.
  Виправлено saved-state identity, failure/redo preservation, nested undo/redo,
  preview accept/cancel/failure та eviction цілих transactions.
- `Command.cs`/`CommandEngine.cs` увімкнено в production host і compatibility target.
  Native property adapter використовує `WindowIde.Commands`; окремий список history
  у `SdkProperties` прибрано. Toolbar і hotkeys користуються спільною гілкою.
- Debug/Release: build із 0 managed warnings/errors, CTest `5/5`, UI `12/12` груп.
  Full IDE native Vulkan із pending-close — PASS у двох конфігураціях,
  validation `0/0`, відновлені captures `946x636` піксельно ідентичні.
  Докази: `build/sdk4-implementation-20261110/` та
  `sources/xray/editor/sdk_ui/sdk4/command-foundation.json`.
- Project-owned editing commands і managed save/selection bindings додано у
  наступному checkpoint нижче. На цьому checkpoint SDK-4 ще був відкритим для native scene
  lifetime/rebuild, picking та transform-control bindings.

### SDK-4 project editing checkpoint — 2026-11-10

- `ProjectEditing`, clipboard і property adapters використовують спільний
  `CommandEngine`: create/delete/duplicate, cut/copy/paste, group/ungroup/reparent,
  rename, visibility/lock та multi-object transforms. Tree/menu/hotkey routes
  підключено; library tree використовує справжні source libraries/models.
- Undo/redo зберігає IDs, hierarchy, sibling order і selection. Clipboard має
  explicit order, internal GUID remapping і graph validation. Selection/ignore
  history не dirty-ть authored content; зовнішній `SetChanged` не губиться після undo.
  Saved marker змінюється після успішного save; invalid Open/failed save зберігають
  session/history, успішні New/Open/Close скидають стару history.
- Відновлено original project option `Track active item in project tree` замість
  підміни project name. Background clipboard ізольовано; text-editing shortcuts
  не виконують scene Delete/Select All. Stale/detached items не проходять live lookup.
- Debug/Release: managed build `0 warnings/errors`, CTest `5/5`, UI `13/13` груп.
  Фінальні full IDE + pending-close native прогони — PASS, validation `0/0`,
  captures `946x636` із піксельно ідентичними відновленими станами.
  Докази: `build/sdk4-project-commands-20261110/` та
  `sources/xray/editor/sdk_ui/sdk4/project-commands.md` / `project-commands.json`.
- Межа: ці commands редагують authored project data. Native captures підтверджують
  попередній M8 property/resource slice; general project scene identity/rebuild,
  viewport picking/selection і existing native transform controls ще не підключені.
  На цьому checkpoint SDK-4 ще залишався `[ ]`; фінальне прийняття наведено нижче.

### SDK-4 acceptance — 2026-11-10

- Базове scene authoring завершено: project commands підключено до native scene,
  independent model instances, point lights, tree/inspector selection і mesh picking.
  Prepare/cook/commit відбувається до публікації history; відмови commit/undo/redo,
  preview та New/Open/Close зберігають попередній стан. Recovery чекає cook readiness.
- Mouse handlers і W/E/R, axis/arrow keys, Ctrl+Z/Ctrl+Y/Escape викликають existing
  native transform-control calculation; World/Local, grid/vertex/edge/face snap,
  pivot, signed scale та exact undo/redo перевірені. Coplanar draw order стабільний
  за persistent GUIDs; Cyrillic save/reopen точно відновлює object state й кадр.
- Debug/Release: managed build `0 warnings/errors`, CTest `5/5`, compatibility
  `14/14` груп. У кожній конфігурації окремо пройшли authored-scene + pending-close,
  попередній property/resource/layout slice + pending-close та legacy host.
  Vulkan validation `0 errors / 0 warnings`; resources drained до HWND teardown.
- Докази: `build/sdk4-native-20261110/` та
  `sources/xray/editor/sdk_ui/sdk4/acceptance.md` / `acceptance.json`.
  Повні viewport/gizmo services і оптимізація scene rebuild належать SDK-5;
  specialized tool locators і protected terrain/root scene мають domain stages.

### SDK-5 main viewport checkpoint — 2026-11-11

- Розпочато renderer mapping: original routes зіставлено з typed Vulkan API;
  16 unsupported legacy modes явно недоступні в native session. Mapping містить
  конкретні відкриті renderer/input/output dependencies, а не success stubs.
- Main viewport: camera/projection, renderer grid і selection bounds, camera light
  та sun, postprocess profiles, culling freeze; look/orbit/pan/dolly/fly і Focus
  через native cooked bounds. Runtime camera metadata не створює object history.
- Виправлено несумісний SSAO/distortion profile для normals/unlit/disabled
  postprocess. Debug/Release: CTest `5/5`, compatibility `15/15`; main viewport
  і SDK-4/M8 regressions — 8 GPU workflows із validation `0/0`. Restored main
  camera/unfreeze images піксельно ідентичні; pending preview teardown чистий.
- Докази: `build/sdk5-viewport-20261111/` і
  `sources/xray/editor/sdk_ui/sdk5/main-viewport.json`; mapping/межа:
  `sources/xray/editor/sdk_ui/sdk5/renderer-mapping.json` та `README.md`.
- SDK-5 ще `[ ]`: потрібні silhouette outlines, full native gizmos/axis picking,
  generic previews/output lifecycle, решта modes, reload/performance і реальний
  play transition. Selection bounds не підміняють outline acceptance; M8 fixed
  woodbox preview залишається regression control.

### Подальша послідовність

Етапи **SDK-0** — **SDK-12** завершено й підтверджено доказами у
`sources/xray/editor/sdk_ui/sdk0/` — `sdk12/` та ізольованих директоріях `build/`.
Загальні підготовлені асети з `C:/Stalker2_2011/xr2-dsgn` розміщено в `build/sdk-assets`.

SDK-4 завершено 2026-11-10: базова native scene, commands/history, mouse/hotkeys,
picking/snap, transforms і save/reopen прийняті.
SDK-5 завершено 2026-11-12 (`sources/xray/editor/sdk_ui/sdk5/acceptance.json`):
viewport-режими/оверлеї, native gizmo picking, незалежні прев'ю та Play/Simulate.
SDK-6 завершено 2026-11-15 (`sources/xray/editor/sdk_ui/sdk6/acceptance.json`,
`build/sdk6-acceptance-20261115/`): 9 родин asset authoring прийняті в Debug і Release.
SDK-7 завершено 2026-11-15 (`sources/xray/editor/sdk_ui/sdk7/acceptance.json`,
`build/sdk7-acceptance-20261115/`): 7 родин world tools (`terrain`, `solid`, `lights`,
`composites`, `particle-placement`, `speedtree`, `misc`) прийняті в Debug і Release.
SDK-8 завершено 2026-11-19 (`sources/xray/editor/sdk_ui/sdk8/acceptance.json`,
`build/sdk8-acceptance-20261119/`): 5 родин animation authoring (`animation-collections`,
`animation-viewer`, `animation-setup`, `animation-groups`, `animation-placement`) прийняті в Debug і Release.
SDK-9 завершено 2026-11-19 (`sources/xray/editor/sdk_ui/sdk9/acceptance.json`,
`build/sdk9-acceptance-20261119/`): `sound-editor` і `audio-tools` прийняті в Debug і Release.
SDK-10 завершено 2026-11-19 (`sources/xray/editor/sdk_ui/sdk10/acceptance.json`,
`build/sdk10-acceptance-20261119/`): `particle-editor` прийнято в Debug і Release.
SDK-11 завершено 2026-11-19 (`sources/xray/editor/sdk_ui/sdk11/acceptance.json`,
`build/sdk11-acceptance-20261119/`): 4 родини (`dialog-editor`, `dialog-excel`, `scripting`, `navigation`) прийняті в Debug і Release.
SDK-12 завершено 2026-11-19 / перевірено 2026-11-20 (`sources/xray/editor/sdk_ui/sdk12/acceptance.json`,
`sources/xray/editor/sdk_ui/sdk12/README.md`, `build/sdk12-package-20261119/`, `build/sdk12-acceptance-20261119/`):
чисті автономні Debug/Release Windows x64 розгортання, `-editor` -> `WindowIde`, закриття
`40/40` родин матриці та остаточне приймання.

## Вихідні матеріали

- Оригінальний SDK: `C:/Stalker2_2011/xr2-dsgn`, документація:
  `C:/Stalker2_2011/s2_2011_documentation`.
- Source shell: `sources/xray/editor/world/sources/window_ide.*`,
  `project*`, `level_editor*`, `sources/xray/editor/base/sources`,
  `sources/xray/wpf_controls`.
- Поточний host/ABI: `sources/xray/editor/base/EditorWorld.cs`, `Bridge.cs`,
  `sources/xray/editor/world/host_bridge.h`,
  `sources/xray/editor/world/sources/managed_host.cpp`.
- Завершене renderer-приймання: `vulkan_integration_plan.md`,
  `build/stage9-final-closeout/summary.json`,
  `build/stage9-performance-manifest/summary.json`.


## Blender B6 — інтеграція DCC, 2026-11-15

Реалізовано `Tools → DCC sources / playback` і `DCC source / Reimport` у model
editor та resource browser. Blender — default, Maya/mayapy — додаткова опція;
workspace master відокремлено від оригіналів. Є точне підключення перевірених
B5 `.blend`, імпорт legacy assets із потрібним skeleton, збережені Object/Action,
відкриття Blender зі встановленим ZIP та зрозуміла відмова `.mb` без Maya.
[Інструкція та межі](sources/tools/blender/SDK.md).

Manual Reimport та saved-source watcher експортують у приватне покоління кешу,
перевіряють native loaders і лише тоді оновлюють live model. Скасування, зміна
джерела/проєкту та невдалий cook залишають попередній ресурс. Відкритий model
document перечитує геометрію; призначення матеріалів зберігаються за surface name.
Останнє покоління відновлюється після відкриття SDK без запуску DCC.

У Release та Debug справжній Blender змінив vertices (native bound X 1.25 →
3.75) і Action (engine pose delta 0.74999994 m) у тому самому SDK-сеансі. GPU
readback підтвердив зміну моделі, Vulkan validation errors=0.
[Acceptance](sources/xray/editor/sdk_ui/dcc/acceptance.json) містить точні runs.

Це приймання B6 DCC workflow та skeleton playback preview. Воно не закриває
повний SDK-6, animation graph/retargeting/authoring SDK8, asset actor integration,
повний UI physics authoring. B7 CI та B8 документацію прийнято окремо. Історичні Maya
рядки інвентаризації лишаються; `dcc-*` описують сучасні альтернативні frontend.

B7 pipeline CI локально прийнято окремо 2026-11-15 на встановленому Blender
5.3.0: T1 без DCC 7/7, Release 22/22 (також із чистої збірки), Debug/Maya OFF
19/19, повний Release CTest 29/29. [Звіт і межі приймання](sources/tools/blender/b7-acceptance.json)
фіксують відсутність віддаленого GitHub запуску та один невідтворений worker
failure; [CI guide](sources/tools/blender/CI.md) містить команди й діагностику.
B8 завершено 2026-11-15: [гайд художника](sources/tools/blender/xray_blender/README.md),
[maintainer runbook](sources/tools/blender/MAINTAINER.md), README Blender/Maya,
import/migration/SDK guides і ZIP 0.3.2 з документацією. Python-код ZIP тотожний
0.3.1; новий пакет пройшов Release CI 22/22 і native SDK live reimport без
перезапуску. [B8 acceptance](sources/tools/blender/b8-acceptance.json) зберігає
успіхи та початкові WinError 5 installer failures. План Blender B0–B8 завершено;
повний SDK8 та неналаштований віддалений CI залишаються окремими роботами.
