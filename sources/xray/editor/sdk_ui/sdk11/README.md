# SDK-11 — Dialog, Excel Localization, Logic/Scripting & Navigation

Статус: **Прийнято (2026-11-19)** у Debug та Release. Усі перевірки зібрано в [acceptance.json](acceptance.json), докази запуску — у `build/sdk11-acceptance-20261119/`.

## Охоплені родини (`sources/xray/editor/sdk_ui/sdk0/function-matrix.json`)

1. **`dialog-editor`** (`world show_dialog_editor -> dialog/editor_creator`):
   - **Граф діалогів і локалізація** ([SdkDialog.cs](../../../base/SdkDialog.cs), [WindowIde.Dialog.cs](../../../base/WindowIde.Dialog.cs), [WindowIde.cs](../../../base/WindowIde.cs), [WindowIde.Projects.cs](../../../base/WindowIde.Projects.cs)):
     - Знято дормантний статус маршруту `"Dialog editor"` у `Tools -> Dialog editor` (залишилося `0` дормантних маршрутів редакторів у `WindowIde`) та підключено відкриття `.dlg`/`.template` з `ResourceBrowser`.
     - Повна підтримка оригінальних Lua-схем `.dlg` та `.template` (`dialog = { nodes, links }`, `layout` inline/sidecar), вузлів фраз і вкладених піддіалогів (`is_dialog = true`, напр. `zaton/test_zaton`), булевих дерев умов (`type = 0..3`: `Predicate(has_info/has_item)`, `And`, `Or`, `Not`) і дій (`give_info`, `give_item`).
     - Керування таблицею локалізацій (`localizations.cfg`, `<lang>.localization`, `references.cfg` із `category`, `references_count`, `batch_file_names`, `last_translators_id`), додавання/видалення мов, `CommandEngine` Undo/Redo для всіх редагувань графа та текстів, атомарне збереження і перевідкриття.
     - Реалізовано компілятор `SdkCompiledGameDialog` (еквівалент `game_dialog_cooker.cpp` / `game_dialog.cpp`) з рекурсивним розгортанням піддіалогів, виявленням циклів, фільтрацією реплік за `SdkDialogRuntimeState` та виконанням actions; UI побудовано на спільному `hypergraph_host` і `property_grid.host` із безпечним `ConfirmClose`.

2. **`dialog-excel`** (`dialog editor translation workflow`):
   - **Сучасний двонапрямний обмін `.xlsx` та `.xls`** (`SdkDialogExcelExchange` у [SdkDialog.cs](../../../base/SdkDialog.cs)):
     - Застарілий COM interop `Microsoft.Office.Interop.Excel` (Office 12) замінено беззалежним керованим **OpenXML `.xlsx`** (`System.IO.Compression.ZipArchive` + `System.Xml.Linq`, `xml:space="preserve"`) плюс **SpreadsheetML 2003 XML `.xls`** для повної сумісності.
     - Дотримано точну структуру колонок `A..E` (`Numeric_ID`, `String_ID`, `Text_Type`, `Original_Text`, `Translated_Text`), фільтрацію невикористовуваних рядків (`references_count == 0`), інкремент `last_translators_id`, генерацію Lua sidecar `.batch` (`translators_id_to_string_table_id`), збереження багаторядкових `\n` реплік і кириличного/UTF-8 тексту при експорті та зворотному імпорті.

3. **`scripting`** (`tool registered; scripting tab commented out in level_editor`):
   - **Розв'язаний маршрут входу, логічні сцени/роботи та з'єднання властивостей** ([SdkScripting.cs](../../../base/SdkScripting.cs), [WindowIde.Scripting.cs](../../../base/WindowIde.Scripting.cs), [ToolsTab.cs](../../../base/ToolsTab.cs), [SdkIdePanels.cs](../../../base/SdkIdePanels.cs), [ProjectEditing.cs](../../../base/ProjectEditing.cs), [Project.cs](../../../base/Project.cs), [SdkScene.cs](../../../base/SdkScene.cs)):
     - Активовано закоментовану вкладку `"scripting"` у `ToolsTab` (`ActiveToolCount = 9`, єдиний рештковий дормант — `"object painter"`), наповнену з `scripting.library` (`base:scene`, `base:npc`, `base:job`), а також додано маршрут `Tools -> Logic / Scripting editor` (`SdkLogicEditor`) на спільному `hypergraph_host` і `property_grid.host`.
     - Парсинг `scripting.library` і `logic_editor.library` (`SdkLogicLibrary`: 8 типів робіт та 12 поведінок із `tool_scripting.cpp`), `SdkLogicScene` / `SdkLogicJob` транзакції через `CommandEngine` (`#StartScene`, `events`, `event_handlers`: `start_scene`, `raise_event`, `switch_behaviour`, `set_property`).
     - Робоче двостороннє зв'язування у `PropertyConnectionEditor` (`BindProject`, `grab left`/`grab right`/`swap`/`Connect`/`Disconnect`) зі збереженням `property_links` у `project.xprj`, Undo/Redo, перевідкриттям із нульовими відсутніми залежностями та live виконанням подій у `SdkLogicRuntimeEngine` (зміна `light` і властивостей об'єктів у Vulkan viewport).

4. **`navigation`** (`project serialize calls AI navigation graph generator`):
   - **Генерація navmesh, інтеграція в проєкт та пошук шляху** ([SdkNavigation.cs](../../../base/SdkNavigation.cs), [WindowIde.Navigation.cs](../../../base/WindowIde.Navigation.cs), [Project.cs](../../../base/Project.cs)):
     - Повна сумісність з оригінальним Lua-форматом `navmesh.lua` (`graph_generator_serializer.cpp`: `vertices`, `indices`, `triangle_data` із `neighbours`, `obstructions`, `plane`), перевірена на еталонних `stohe_selo_2_navmesh.lua` і `plecheg_test_navmesh.lua`.
     - UI-маршрут `Tools -> Navigation mesh...` (`SdkNavigationPanel`), генератор `SdkNavMeshGenerator` із терейну (`Project.Terrain.TrySampleHeightWorld`) та статичних/колізійних перешкод із `AgentRadius`-інфляцією і `CommandEngine` Undo/Redo, автоматичне збереження й завантаження `navmesh.lua` у `Project.SaveProjectFile` / `Project.LoadProjectFileCore` / `SaveProjectAs`.
     - Швидкий runtime pathfinder `SdkNavMeshPathfinder` (A* по трикутній суміжності + 2D Simple Stupid Funnel / string pulling), який прокладає гладкі обхідні маршрути навколо перешкод і відхиляє недосяжні/заблоковані цілі.

## Верифікація та відтворення

- Оригінальні reference fixtures (15 файлів: 11 у [fixtures/dialogs](fixtures/dialogs), 2 у [fixtures/navigation](fixtures/navigation), 2 у [fixtures/scripting](fixtures/scripting)) скопійовані без зміни першоджерел і перевіряються SHA-256 контролем.
- Unit-перевірки: [SdkSdk11Checks.cs](../SdkSdk11Checks.cs) та [SdkContractChecks.cs](../SdkContractChecks.cs) у складі `xray_sdk_ui_compatibility` (`32` перевірки у Debug та Release).
- Vulkan GPU-прогін: [EditorWorld.Sdk11Checks.cs](../../../base/EditorWorld.Sdk11Checks.cs) + [verify-sdk-sdk11.py](../../../../tools/sdk/verify-sdk-sdk11.py):
  - Debug та Release: `0 Warning(s)`, `0 Error(s)`, SDK CTest `5/5`, повний CTest `31/31`, Vulkan validation `errors=0 warnings=0`, pending-close drain і впорядкований `M8 shutdown`, `6` переходів сцени та `2` піксельно-точні збіги Undo (`4==6`) та Redo (`5==7`).
- Консолідація і перевірка гешів: `python sources/tools/sdk/collect-sdk11-acceptance.py --verify`.
