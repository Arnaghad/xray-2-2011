# SDK-0: уточнення inventory, 2026-10-24

Це результати source/documentation review. **SDK-0 завершено як inventory**;
фінальні критерії й межі зафіксовано в `acceptance.md`. Статус SDK-0 визначає
`sdk_migration_plan.md`. Фактичні runtime-сценарії оригіналу залишаються
неперевіреними через зафіксований startup blocker.

## Команди та маршрути

Усі 339 початкових `register_action` записів збережено зі стабільними IDs,
source path/line і upstream blob. 29 лежать поза original project entries,
5 — API/forwarding. Залишаються 305 command candidates: 272 звичайні
реєстрації, 32 залежні від контексту/життя object/tool і одна з вимкненим
маршрутом конструювання. Статична наявність реєстрації не доводить доступність
команди у вікні. Для кожного candidate є ім'я, shortcut, owner і сценарій.

`object_painter_tool_tab` реєструє команду, але його створення у
`level_editor.cpp` закоментовано. Це окремий gap, а не підтверджений видимий tool.
Вісім вкладок у `level_editor` і 13 resource choosers обліковуються окремо.

92 menu/toolbar bindings зіставлено з command IDs у тих самих source files.
Два визначення API відокремлено. У первинних 85 menu/tool candidates є
14 API/forwarding, 2 entries поза original projects, 45 command menu bindings,
3 menu containers, 13 choosers і 8 tabs. Ці каталоги мають різне покриття;
їхні числа не потрібно додавати як кількість функцій.

Знайдено 21 групу однакових shortcut у межах owner/context. Це кандидати на
перевірку predicates/priority, а не доведені помилки. Зокрема, `particle editor`
і `pin selection` мають `Control+P` у Global. `action_shortcut.cpp` задає
Global для рядка без явного context, а `level_editor::get_input_engine`
повертає input engine `editor_world`. SDK-2 має перевірити пріоритет і доступність
цих команд при переході focus між viewport, project tree та property editor.

Source-owner association пов'язує всі 305 candidates із 36 функціональними
групами. В animation editor один source file реєструє команди кількох document
types; association не є доказом точного document route. Завершальний прохід
додав 16 construction routes, initializer і джерела enabled/capture для всіх
305 candidates, включно з 51 explicit enabled binding. 1371 explicit source/XAML
event binding має source target category. Dynamic reachability, predicates/
priority та effects перевіряються під час порту.

## Документація та original source gaps

Переглянуто текст 5 DOCX, 3 TXT і вміст 1 XLSX: 9 унікальних файлів,
18 copies із SHA-256. Paragraph indexes і worksheet/cell addresses збережено.
Оригінали не редагувалися; layout/recalculation acceptance не заявляється.
Із 126 documentation candidates решта 108 — PNG/JPG/PSD copies. Візуально
переглянуто всі 54 унікальні stored images/PSD composites. Палітра curves,
іконки та annotated IDE layout мають design provenance; forest/tree/pine
зображення — art-reference provenance. Це не fresh runtime captures.

- **Sound.** Документ описує `sound/`, `.lua` items/collections і mixer graph,
  а фактичний editor використовує `sounds/single`, `composite`, `collections`,
  `.sound_collection` і `.composite_sound`. У переглянутому sound editor module
  не знайдено рівнозначного mixer-authoring route. Mixer/EQ/limiter/compressor
  specification залишається scope gap для SDK-9; не вважати її shipped parity.
- **Curves.** Sinusoid, sqrt і noise мають source routes. Original stairs та
  digital-signal handlers відкривають sinusoid settings. Повні documented
  presets, interval distributions і синхронізація 2D/3D camera-track keys
  потребують окремих checks у controls/domain stages.
- **Project filters.** Folder lock і locked-root selection підтверджено кодом.
  Handler `New Filter` додає project folder. Цей напис не доводить реалізації
  type/tag filter. Recursive visibility, isolate та basic/user tags залишаються
  scope/behavior gaps SDK-3.
- **Logic.** Scene/job/event code існує; повний human-template UI з документа
  та всі його поля/events ще не зіставлено. Scripting tab також закоментовано.
  Це gap SDK-11, який не можна приховати назвою DLL.
- **World outline.** Compound objects і terrain мають source routes; короткий
  document містить також design questions. Вони не є runtime evidence.
- **Art references.** Animation icon brief, tree/pine TXT та `trees.xlsx`
  дають asset/design provenance. Чотири заповнені та один порожній worksheet
  описують види/варіанти дерев; додаткові SDK commands із них не виводяться.

## Придатність fixtures

Початкові дев'ять seeds збережено незмінними. SHA-256 перевірено для 64 files,
прочитаних під час dependency review та вибору sound replacements. Lua не
виконувалася. Static references і runtime/cook acceptance обліковуються окремо.

- **Project:** знайдено чотири external `.terr`; exact model path
  `architecture/research_institute/reserch_institute_roof` відсутній у manifest.
  Bitmap background і terrain texture dependencies також потребують resolution.
- **Dialog:** обидва string IDs наявні в усіх трьох declared localizations;
  config і language files знайдено. Два root nodes без links не покривають
  conditions/actions/linked dialog acceptance.
- **Particle:** `ddd.particle` містить порожній root. Library присутня, але
  visible-preview acceptance потребує nonempty graph і transitive dependencies.
- **Animation viewer:** exact referenced walk clip відсутній. Model mapping,
  options і cooks відкриті; подібно названий model не підставляється автоматично.
- **Animation collection:** exported inspect clip є; options, skeleton,
  preview model і cook acceptance потребують перевірки.
- **Sound scene та composite:** статичні sound references замкнені, включно
  з наявними WAV/options sidecars. Playback, defaults generation, audio cooks
  і save/reopen ще не перевірялися.
- **Sound collection:** три WAV rocket-launcher references відсутні. Як
  replacement candidates знайдено `jokes_reaction.sound_collection` і
  `test.sound_collection` із замкненими static sound references. Selection
  order — file size, потім path; жоден candidate ще не прийнято runtime-перевіркою.
- **Material:** sky texture TGA/options присутні; model-parameter override,
  effect capabilities та shader/texture cooks залишаються відкритими.

Maya host compatibility, OBJ/Excel import/export та відсутні fixtures інших
форматів зберігають окремі verification rows у function matrix. Required baseline
із 36 workflow groups фіналізовано; шість known scope gaps перенесено до
implementation-stage owners у `implementationFollowUps`. Maya registrations
уточнено до 9 commands, 1 translator, 2 nodes і 3 transforms із literal/macro names.

## Відтворення

Початкове evidence цього проходу: `build/sdk0-review-20261024-final/` та
`build/sdk0-fixtures-20261024-verified/`. Матриця містить paths і hashes їхніх
JSON artifacts; генератор відхиляє review з іншим source commit, input hashes
або asset manifest. Старі evidence roots і original startup result збережено.
`build/sdk0-audit-20261024.json` підтверджує 18 integrity checks; source-contract
CTest пройшов. Runtime/cook acceptance цими перевірками не заявляється.
Завершальне evidence: `build/sdk0-events-20261024-audited/`,
`build/sdk0-images-20261024/`, `build/sdk0-routes-visuals-20261024-complete/`.
Повторення фіналізованого inventory наведено в `acceptance.md`; команда нижче
відтворює попередній проміжний checkpoint.

```powershell
python sources/tools/sdk/review-sdk0.py --output build/sdk0-review-new
python sources/tools/sdk/check-sdk0-fixtures.py --output build/sdk0-fixtures-new
python sources/tools/sdk/map-sdk0.py build/sdk0-baseline --review build/sdk0-review-new --fixtures build/sdk0-fixtures-new
```

## SDK-4 implementation acceptance

Базове authored scene editing прийнято окремими Debug/Release GPU workflows і
14 compatibility groups: [acceptance](../sdk4/acceptance.md),
[hashes/results](../sdk4/acceptance.json). Shared history, native scene candidates,
mesh picking/snap, existing native transform controls, mouse/hotkeys і Cyrillic
save/reopen перевірені. Full viewport/gizmo services та domain editors зберігають
наступні acceptance stages. Original SDK-0 inventory counts і source evidence
залишаються історичним baseline; їх не підмінено implementation counts.
