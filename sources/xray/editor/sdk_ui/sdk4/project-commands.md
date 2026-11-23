# SDK-4: project editing commands

Дата: 2026-11-10. Другий implementation checkpoint SDK-4; повний етап залишається
відкритим. Точні результати й hashes: [project-commands.json](project-commands.json).
Evidence root: `build/sdk4-project-commands-20261110/`.

## Реалізовано

- `ProjectEditing` виконує create/delete/duplicate, cut/copy/paste, grouping,
  ungrouping, reparenting, rename, visibility, lock та transform properties через
  спільний `CommandEngine`. Перенесено семантику original `command_select`,
  `command_delete_object`, `command_paste_objects`, `command_reparent_items`,
  `command_set_object_transform` і відповідних `level_editor_actions`.
- Commands утримують лише змінювані items та їхні before/after values. Undo
  відновлює ті самі GUIDs/runtime IDs, parents, sibling order і selection.
  Вкладені group/ungroup — одна history transaction. Ref-counted retention не
  звільняє ID, доки delete/redo ще потребує item; lookup відхиляє detached items.
- Selection та ignore list — undoable runtime state, що не змінює content saved
  marker. Зовнішній `SetChanged` залишається dirty після undo. Saved marker
  встановлюється після успішного file commit; save failure і active preview його
  не пересувають. New/Open/Close скидають стару history, invalid Open її зберігає.
- Clipboard version 1 містить Lua data без виконання Lua. Нові GUIDs копій
  замінюють internal GUID references; external references та unknown fields
  зберігаються. Окремі root/child order lists запобігають перестановці items через
  сортування GUID keys serializer-ом. Невірний формат, duplicate identities,
  multiple parents, cycles і unreachable items відхиляються до вставки.
- Tree/context menu, Edit menu, hotkeys і property adapters використовують ці
  commands. Multi-object property edit — одна команда; rotation UI у degrees,
  serialized values у radians. Protected terrain/root scene та їхні containers
  не видаляються й не переміщуються. Registry lookup не повертає deleted payloads.
- Library tree у configured session показує справжні models/tool libraries;
  double-click/Insert створює authored object. Tree drag/drop має move/copy
  bindings із приватним string token, без передачі serialized managed objects.
- Text controls зберігають свої editing shortcuts; input reset звільняє active
  shortcut перед поверненням до scene routing. Фізичний full native scene workflow
  ще не прийнято. Background runner має ізольований clipboard і чисто завершується;
  normal IDE використовує typed system clipboard API.
- Project options відновлюють original `Track active item in project tree` з
  apply/cancel semantics. Вигадане shell-чернеткою редагування logical project name
  прибрано; project path/name змінюється через Save As.

`visible` — optional boolean field: записується після зміни visibility або якщо
поле вже було у файлі. Поточний editor читає його; застосування цього розширення
оригінальним SDK runtime не заявляється.

## Перевірка і межі

`SdkProjectCommandChecks.cs` охоплює IDs і sibling order після undo/redo/eviction,
clipboard references/unknown fields/invalid graph, multi-object properties,
locked/invalid/stale edits, Cyrillic rename, failed save, preview save guard,
untracked dirty state, project transitions, text focus та menu/tree/hotkey routes.
CTest і compatibility results збережені окремими snapshot files у evidence root.

- Debug/Release builds: 0 managed warnings/errors; CTest **5/5** у кожній
  конфігурації, compatibility **13/13** груп.
- `gpu-final-debug` і `gpu-final-release`: full IDE з `-ClosePending`, exit 0,
  Vulkan validation **0 errors / 0 warnings**, pending resources drained до HWND
  teardown. Усі 9 captures мають **946×636**, відновлені стани піксельно ідентичні.
- Початковий `gpu-debug` timeout збережено як failed evidence: діагностичний tree
  fixture був dirty, тому Close скасовувався. У фінальних прогонах initial saved
  baseline fixture встановлюється без очищення redo; робочі dirty/cancel rules
  не обходяться. Background UI runner також перевірено на природний exit 0.

Native regression перевіряє наявний M8 property slice, resource bridge, layout і
pending-query shutdown. Reference-project diagnostic використовує окрему project
session: справжній Open скидає її history та не втручається в history control scene.
Ці captures не є доказом native відтворення щойно створених authored objects.

Наступне: project/native object identity та lifetime, scene rebuild, viewport
selection/picking і синхронізація tree/inspector; після цього — bindings наявних
native transform controls, coordinate spaces, snap і реальний mouse workflow.
