# X-Ray 2.0 for Blender

Install the extension ZIP using **Preferences → Get Extensions → Install from
Disk**, then enable **X-Ray 2.0**. Extension **0.3.2** declares Blender 5.0 as
its minimum; the verified Windows host is the installed **Blender 5.3.0** at
`C:/Program Files/Blender Foundation/Blender 5.3/blender.exe`. Use
`xray_blender-0.3.2.zip`; earlier accepted packages remain historical checkpoints.

## Швидкий старт художника

1. Встановіть ZIP через **Preferences → Get Extensions → Install from Disk**
   і ввімкніть **X-Ray 2.0**. Відкрийте бічну панель клавішею **N**, вкладку
   **X-Ray 2.0**. Для роботи вибирайте **Object Mode**.
2. У **Settings** задайте **Resources Folder** робочого проєкту та **Asset Cooker**,
   наприклад `build/sdk-native-20261108-release/binaries/xray_asset_cook_cli.exe`.
   **Check Paths** має підтвердити обидва шляхи. Збережіть `.blend` у робочому
   каталозі джерел; оригінальний архів контенту залиште для читання.
3. Для першої статичної моделі створіть **Static Root**, підпорядкуйте йому mesh
   з активною UV map, призначте матеріал усім граням. У **Material Properties →
   X-Ray 2.0** заповніть **X-Ray Shader**, **X-Ray Material**, за потреби **Texture**.
   Значення ресурсів узгодьте з матеріалами вашого проєкту.
4. Виділіть root, виберіть **Static Model**, **Logical Asset Name** =
   `props/box`, **Output Folder** = `//xray_export/`, увімкніть **Cook Assets**
   і натисніть **Export X-Ray Asset**. Поруч зі збереженим `.blend` з'явиться
   `xray_export/props/box/` із `exchange`, `raw`, `converted`.
5. Перегляньте модель у SDK. Для постійного зв'язку ресурсу з джерелом відкрийте
   **Resources → DCC source / Reimport**, вкажіть URI на кшталт
   `resources/models/props/box.model` і виконайте **Associate scene**. SDK створює
   власний master; надалі відкривайте його через **Open source in DCC**, збережіть
   зміни й натисніть **Reimport**.

Logical Asset Name — відносне ім'я з `/`, без розширення ресурсу, drive letter,
початкового `/` і сегментів `.` / `..`. Output Folder — каталог для результатів
експорту. Resources Folder потрібний для пошуку ресурсів; вибір цього каталогу
сам по собі не публікує експорт у SDK.

In **X-Ray 2.0 → Settings**, choose your X-Ray **Resources Folder** and the
`xray_asset_cook_cli` **Asset Cooker** executable. **Check Paths** checks these
locations without cooking an asset. Empty settings can use `XRAY_RESOURCES_PATH`
(or `STK2_ROOT_PATH`) and `XRAY_ASSET_COOK_CLI` (legacy `XRAY_MAYA_COOK_CLI`).

The **X-Ray 2.0** header menu, **Add → X-Ray 2.0**, and the **X-Ray 2.0** sidebar
create authoring helpers:

- **Static Root**: an Empty marking the root of a static asset hierarchy.
- **Physics Body**: an Empty with animation-joint name, mass and mass center.
- **Collision Shape**: an Empty parented to the selected physics body and centered
  in its local space. Choose box, sphere or cylinder in Object Properties.
- **Constraint**: an Empty with body links, joint type and six degrees of freedom.
  Body helpers also carry joint metadata. Without an explicit Constraint targeting
  a body, its own metadata creates its joint (parent body, or world, to itself).
- **Locator**: an Empty in the scene's marked `locators` collection. It can name
  an armature and bone; a selected static root becomes its parent.

Edit data in **Object Properties → X-Ray 2.0**. Constraint limits follow the
engine joint-local convention, with translation in meters and rotation in radians.
Ordinary helper transforms remain in Blender scene units. A Z-up cylinder's long
axis is Z; its Empty is a circle marker, not a rendered collision preview.

Armature objects expose **Armature Space**: **Blender Z-up** for native rigs, or
**Legacy Y-up** for migrated rigs retaining their joint coordinates. Changing the
setting changes metadata only; it does not rotate the rig or rewrite keys.

Materials expose **X-Ray Shader**, **X-Ray Material** and **Texture** in Material Properties.
All object, material and collection metadata is saved in `.blend`. Disabling
the extension removes its UI/RNA definitions and retains the stored authoring
data for the next enable.

## Export and cook

In the sidebar choose **Asset Type**, a logical resource name, an output folder
and **Export X-Ray Asset**. Select the static root, armature or physics container.
The output is `<folder>/<logical name>/{exchange,raw,converted}`. Disable
**Cook Assets** to write exchange v2 alone. Cooking uses the configured executable
as a separate process and preserves existing material settings. To change a
previously cooked material assignment, edit the output `raw/settings` file.

- **Static Model** exports meshes below the marked root, in root-local space.
  Root placement is excluded; child transforms and evaluated modifiers are baked.
  Each mesh needs an active UV map and a material on every face. Material shader
  names define surfaces; an empty shader uses the Blender material name. Material
  and texture fields contain logical resource paths. Mesh Role selects Render,
  Collision or HQ Collision. Split normals and UV seams are retained.
- **Skeleton / Skinned Model** use one armature root, with at most 79 authored
  bones plus the synthetic `object_movement` track (80 total). All bones are
  exported in deterministic hierarchy order; `object_movement` is reserved.
  Meshes are discovered by their Armature modifier. Skin deformation is disabled
  temporarily, other modifiers are evaluated and restored, and the strongest four
  weights are normalized. Unweighted vertices fail with their mesh/vertex name.
  Set **Skeleton Resource** to the matching hierarchy asset.
- **Animation** treats the active Action as one clip, using its inclusive frame
  range and scene fps. **Export Marked Actions** exports checked Actions using
  `<logical name>/<Action name>`. Each Action must have one Object slot; split
  multi-object Actions first. **Sample Scene / NLA** instead exports the evaluated
  scene range with its NLA stack. Sampling includes constraints/IK and all rotation
  modes, emits continuous XYZ Euler samples, and restores the Action, slot, NLA
  setting and time even on failure. **Local Center** removes an object's evaluated
  transform before root-motion extraction. The cooker removes the first root
  pose. Set translation precision in meters, rotation in radians and scale as a
  ratio. **Additive** marks authored delta data; it does not subtract another clip.
- **Physics** exports bodies, child collision shapes, masses and joint links under
  a selected container. Container placement is excluded. Selecting a body directly
  includes that body and descendants with their world placement. Explicit
  constraints override embedded joint metadata for their Body 1. Inverted joint
  limits, missing shapes and links outside the exported asset are errors.
  Use one free **Rigid** root body and connect the other bodies with Body 0 links.
  Export places that body and its unlinked metadata joint first because the engine
  reserves joint slot zero for the root. Linked **Rigid** joints export as engine
  **Fixed** joints, retaining both bodies.

Rig meshes, bind poses and animation samples are in armature space. Set **Rig Unit
in Meters** explicitly (1 for meters, 0.01 for centimeters); static assets and
unparented physics use scene unit scale. **Legacy Y-up** preserves imported joint
coordinates and excludes the armature object's display rotation/unit scale.
**Legacy Joint Frames** removes the fixed correction where Blender bone Y is
original joint X (`Blender frame = joint frame * Rcorr`). Enable it only on rigs
created with that convention. Imported rigs already retain their per-bone
corrections; keep those settings when editing migrated sources. Negative/zero scale and shear on bone/helper
TRS are rejected; geometry transforms can be baked.

Static locators must descend from their asset root. Skin locators reference their
armature and optional bone in Object Properties. A bone-parented locator retains
its offset from the evaluated bone; an ordinary object bound by metadata is
interpreted in rest armature space.

## Physics authoring

**Create Ragdoll from Armature** creates editable rest-pose box bodies and ball
joints for every bone. Review generated masses, shapes and limits for the asset.
The result is a container parented to the armature; select it for physics export.
Collision scales are full box dimensions, sphere diameter, or cylinder diameter
and height (native Z-up cylinder axis Z; legacy Y-up axis Y). The Empty display is
a marker, especially for spheres/cylinders.

**Fit Box / Sphere / Cylinder** calls the cooker's `--fit-collision` on selected
evaluated meshes and creates an editable collision Empty. Parent a selected mesh
to its physics body, or select the body last to make it active. Fitting uses world
space and the scene unit scale, then preserves the fitted world transform when
parenting the helper. For legacy rig helpers, fit after setting the rig's display
rotation and unit scale so world space matches the scene.

## Import legacy assets and exchange

Version 0.3 adds **File → Import → X-Ray / Maya ASCII** and the sidebar import
button. Choose an exchange `manifest.json`, a legacy model archive/directory,
`.skeleton`, `.clip`, `.physics`/`.ph`, or a basic `.ma`. Current SDK binary
configs/converted model directories are supported as well as original Lua assets.
Maya is not needed. The importer never executes source scripts or loads plugins.

For skins and clips, select a matching armature or choose **Skeleton File**.
A skin's logical skeleton may also resolve through the resources preference.
A standalone skeleton supplies hierarchy with placeholder positions; importing
the skin with its skeleton creates the bound rig. Bind/name mismatches are errors.

Clips prefer adjacent `.clip_check_data`; otherwise set the original **Fallback
Clip FPS**. Imported Actions retain frame offset/count, fps, additive/root policy
and source precisions. Compatible MA keys and handles transfer exactly; other
channels fit editable curves from source keys/knots, then validate every sampled
pose. JSON exact/fit reports are saved on Actions and the import collection.

Import adds a separate collection and rolls it back on failure. Source files are
read only. Basic MA import supports direct TRS joints/curves, UV-mapped triangle/
quad geometry and skinCluster weights. References, IK/constraints, complex history,
pivots/shear and unsupported tangent/polygon data need a flattened basic `.ma` or
the maintainer Maya route. `.mb` requires that migration route. You can request a
flattened basic `.ma` from its author. FBX/USD need a separate Blender import and
manual preparation to the X-Ray rig conventions; this extension has no automatic
FBX/USD rig-adoption command. Corpus migration and hierarchy reconciliation are
available through the maintainer tools.

Source `.blend` files remain the editable source of truth.

## Персонаж і кліпи: порядок роботи

Спочатку завершіть ієрархію та bind pose, потім експортуйте **Skeleton**,
**Skinned Model** із тим самим **Skeleton Resource**, далі **Animation**.
Усі кістки armature беруть участь в експорті, включно з control/end bones;
позначка Deform не виключає їх. Для великого control rig підготуйте окрему
ієрархію деформації в межах 79 кісток, з одним коренем і збереженими батьками.
Експортер семплить constraints/IK; перетворення Maya controller rig на Blender
controller rig до цього процесу не входить.

Для нового rig залишайте **Blender Z-up** і задайте **Rig Unit in Meters**.
Для мігрованого залишайте його **Legacy Y-up**, одиниці та joint corrections.
Перемикання Armature Space змінює metadata; для виправлення орієнтації воно
не обертає сцену. `.skeleton` без skin містить лише ієрархію: preview bones
мають умовні позиції. Для перевірки деформації потрібен справжній skin bind.

У **Dope Sheet → Action Editor** призначте armature один Action з одним Object
slot. Задайте назву кліпу, fps і тривалість. Для звичайного Action діапазон
включає обидва кінцеві кадри; імпортований Action використовує збережені source
start/count/fps. Зміна лише Scene End не подовжить такий імпортований кліп.
Для свідомого bake нового діапазону можна використати **Sample Scene / NLA**,
перевіривши активні NLA strips, fps та потрібний стандартний тип кліпу.
Цей режим не переносить Additive marker окремого Action автоматично.

**Export Marked Actions** створює окремий кліп для кожного checked Action;
назва результату — `<Logical Asset Name>/<Action name>`. Перевірте позначки
всіх Actions у файлі: batch перелік охоплює весь `.blend`. **Additive** означає,
що дані вже є delta poses; віднімання reference animation не виконується.
Root motion переноситься в синтетичний `object_movement`; власну кістку з таким
іменем створювати не можна. **Local Center** задавайте лише за потреби іншого
простору root motion й перевіряйте рух у playback.

## Exact / fit: як редагувати імпортовані ключі

- **exact**: сумісні Maya TRS channels зберігають час ключів, interpolation і
  Bezier handles після точного перетворення осей. Це рішення для окремого
  channel; весь Action може одночасно містити exact і fit channels.
- **fit**: ключі/knots джерела задають початкові точки, додаткові editable keys
  додаються до досягнення source precision на кожному sampled frame. Кількість
  ключів може збільшитися; це не обіцянка зберегти вигляд усіх source curves.
- JSON report на Action/імпортованій collection містить channel mode, причину
  fit, кількість ключів і max pose error. Збережіть його разом із master, коли
  передаєте проблемний кліп maintainer’у. Після ручного редагування перевірте
  новий cook/playback: первинний report описує стан під час імпорту.

Precision задається в метрах, радіанах і відносному scale. Імпорт відновлює
source precision; поля sidebar під час ручного експорту задають поточний budget.
Не збільшуйте його лише для приховування відмінної пози. Візуальний перегляд
доповнюйте SDK playback і перевіркою деформації відповідного skin.

## Повідомлення й наступна дія

- **expected one Object slot**: розділіть multi-object Action і призначте кліп
  потрібній armature; перевірте batch markers.
- **exactly one root / 80-track limit**: перевірте всю ієрархію, включно з
  control/end bones. Збережіть предків усіх кісток, що залишаються у skin.
- **unweighted vertex / missing UV or material**: виправте вказаний mesh/vertex,
  активну UV map або призначення матеріалу граням.
- **negative or zero scale / sheared transform**: виправте transform у робочому
  джерелі з повторною перевіркою bind та animation; самовільне застосування
  transforms до імпортованого rig може зруйнувати joint corrections.
- **root-joint track must be identity**: передайте source і report maintainer’у.
  Для 23 відомих кліпів із Root.scale=(0,0,0) прийнята діагностована відмова.
  Заміна scale на (1,1,1) виходить за source precision й не виконується.
- **Cannot import .mb / unsupported MA**: використайте перевірений migrated
  `.blend` або maintainer Maya route. Повторне збереження складної `.mb` як
  `.ma` саме по собі не прибирає references, constraints чи history.
- **Choose an existing asset cooker**: виправте **Settings → Asset Cooker**,
  виконайте **Check Paths**, збережіть `.blend` і повторіть export.

Перед передачею ресурсу збережіть master, перевірте початок/кінець кожного
кліпу, root motion, scale, skin deformation, material paths та locators.
Передайте skeleton URI, потрібний Action/Object і зовнішні залежності разом із
джерелом. SDK **Associate scene** робить робочу копію; **Reimport** публікує лише
успішно перевірене покоління. Watcher вмикається окремою опцією
**Reimport saved sources automatically**. Повний animation graph/retargeting UI
SDK8 залишається окремою роботою.

У source checkout повні інструкції SDK та maintainer’а лежать у
`sources/tools/blender/SDK.md` і `sources/tools/blender/MAINTAINER.md`.
