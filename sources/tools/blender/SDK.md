# Blender / Maya у SDK (B6)

[Гайд художника](xray_blender/README.md) описує підготовку rig, матеріалів,
locators та Actions; [інструкція maintainer’а](MAINTAINER.md) — ZIP і міграцію.

У `Tools → DCC sources / playback` або в `Resources → DCC source / Reimport`
вкажіть URI ресурсу, наприклад `resources/models/props/box.model` чи
`resources/animations/walk.clip`. У model editor є така сама команда.

`DCC options` зберігає вибір інструмента та шляхи до Blender, Maya, mayapy,
`xray_asset_cook_cli.exe`, ZIP розширення й каталогу `sources/tools`.
Blender вибраний за замовчуванням. SDK знаходить встановлений Blender 5.3 у
`C:/Program Files/Blender Foundation/Blender 5.3/blender.exe`,
зібраний ZIP і cooker біля managed SDK-бібліотеки автоматично. Застарілий шлях
до видаленого portable runtime замінюється встановленим host. Для встановленого
SDK ці шляхи можна вказати вручну. Maya залишається додатковим frontend.

Після оновлення виберіть `xray_blender-0.3.2.zip` у DCC options: збережений
шлях до попереднього наявного ZIP сам не перемикається. Пакет 0.3.1 зберігається
як попередній checkpoint. Зовнішні textures, materials і linked dependencies
мають бути доступні у вашому проєкті разом із робочим master.

## Вихідна сцена

- `Associate scene` створює окремий редагований master у
  `workspace/sources/dcc/`. Оригінал не перезаписується.
- `Use migrated .blend` шукає точний URI у manifest B5, перевіряє SHA-256
  успішного `.blend` і копіює його в workspace. Збіги лише за basename або
  непідтверджені Maya candidates не використовуються. Шлях до manifest задається
  в options; сам manifest і пакет B5 залишаються джерелами лише для читання.
- `Import legacy asset` імпортує `.model`, `.skinned_model`, `.clip`, `.skeleton`,
  `.physics` чи підтримувану `.ma` через встановлене Blender extension та
  зберігає новий `.blend` master. Для model folder виберіть його `settings`;
  для clip/skinned model виберіть відповідний skeleton. Помилка імпорту не
  створює association. `.mb` потребує mayapy; без Maya використовуйте результат
  міграції B5. Для 23 zero-root clips зберігається діагностована відмова.
- Для Maya frontend виберіть Maya в options і вкажіть Maya та mayapy.
  `.ma` / `.mb` оцінюються в окремій staging-копії з original directory для
  references. Blender frontend перетворює `.ma` на `.blend` при association.

За потреби вкажіть точні `Object` та Blender `Action` і натисніть
`Save selection`. Без Object експортується єдиний корінь відповідного типу;
неоднозначний вибір завершується діагностикою зі списком кандидатів.
Master має містити доступну геометрію та залежності; цей маршрут не переносить
довільні linked libraries і не відновлює відсутні textures/material assets.

`Open source in DCC` відкриває саме workspace master. Blender отримує
ізольований SDK-профіль та встановлене ZIP-розширення; scripts у `.blend`
автоматично не виконуються. Збережіть зміни звичайною командою Blender/Maya.

## Reimport та перевірка

`Reimport` запускає background DCC → exchange v2 → C++ cooker → native loader.
SDK перевіряє результат і лише тоді публікує покоління. Відкритий model editor
оновлює геометрію; live instances та нові model queries використовують нову
версію. Призначення матеріалів для поверхонь з тими самими іменами зберігаються.
Для анімації `Preview` / `Play / pause` показує skeleton із поз, обчислених
справжнім engine animation cooker. Це DCC playback preview; animation graph,
retargeting та authoring UI SDK8 залишаються окремим етапом.

Опція `Reimport saved sources automatically` вмикає file watcher із debounce.
Перед публікацією збережіть drafts у model/material editors та зупиніть runtime.
`Cancel`, збій DCC/cooker, зміна master під час експорту чи зміна project session
не замінюють попередній ресурс. Журнал невдалого експорту лежить у
`user_data/dcc-cache/<generation>/host.log`.

Якщо log містить `Failed to rename directory ... xray_blender@` / `WinError 5`,
відмовив Windows installer розширення до імпорту сцени. Збережіть log, перевірте
доступ/блокування каталогу та повторіть Reimport після їх усунення. У B8
діагностичний rename probe підтвердив, що одна така відмова зникла через 100 ms;
процес, що блокував каталог, не встановлено. Первинний failure залишається у
звітності; збільшувати animation precision через цю помилку не потрібно.

Options зберігаються в `user_data/dcc.json`, associations — у
`workspace/sources/dcc/links/`. Cooked generations і приватні профілі лежать
окремо в `user_data/dcc-cache/`. Після відкриття SDK останнє успішне покоління
відновлюється з кешу без запуску DCC; втрачений кеш потребує Reimport із master.
Час converted `.clip` у цьому кеші встановлюється після raw із запасом дві
секунди через секундну точність legacy binary-config selector; байти незмінні.

## Перевірка розробником

Після збірки `xray_editor_world` і `xray_sdk_ui`:

```powershell
sources/tools/sdk/test-sdk-dcc.ps1 -BuildRoot build/sdk-native-20261108-release -OutputRoot build/my-b6-run
```

Використовуйте новий output directory. Harness створює сцену через встановлений
ZIP, змінює mesh та Action справжнім Blender і в одному SDK-сеансі перевіряє
геометрію, fresh queries, engine poses, watcher, відмову експорту, cache restore
та GPU readback. `xray_sdk_ui_compatibility` додатково перевіряє транзакції,
скасування, races, ізоляцію джерел, `.mb` без Maya й ABI семплювання.
