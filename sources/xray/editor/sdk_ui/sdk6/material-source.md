# SDK-6 — static material та instance source

Другий checkpoint SDK-6: `g_stage_default` source workflow. Повна family
`materials` та весь SDK-6 залишаються відкритими. Перевірки й snapshot hashes:
[material-source-checkpoint.json](material-source-checkpoint.json).
Попередній texture checkpoint залишається окремим незмінним звітом.

## Робота редактора

`Tools → Material editor` та `Resources → Edit source` відкривають обраний
`.material` із materials або material_instances. Original WPF property grid
показує source URI, base material, diffuse color/texture та surface parameters.
Параметри, якими керують texture maps або locked base fields, мають readonly UI.
`Use base value` прибирає обраний instance override. Undo/Redo, Revert, saved
marker і dirty-close належать окремому CommandEngine документа.

Managed resolver читає original `create_property_table`, `__super_tables` та
`create_reference` як дані. Він виявляє цикли й missing references, обмежений
128 modules, 100000 expanded nodes, 32 MiB source і 16 MiB expansion. Source
serialization зберігає inheritance та metadata. Native preview отримує окрему
resolved копію, яку перевіряють чинні bounded Lua reader, original static
material adapter, checked binary-config reader і material/texture cooks.

Apply відкриває незалежний material preview на standard primitive. Save
готує поточний material та залежні queried/published instances, атомарно
записує source у `workspace/sources/materials` або `material_instances` і
публікує весь набір native handles однією операцією. Renderer бере незмінний
набір material versions для кожного viewport submission. Оновлюються scene,
поточні та нові model previews. Published instance відстежується й до свого
першого model/preview cook, тому зміна base не залишає старий cached instance.

Невдалий cook не публікує candidate. Відхилений commit відновлює попередній
source файл і не ставить saved marker. Cancellation, external dependency
changes, повторний material operation та Play під час busy operation мають
явну відмову. Reference pack не переписується. Saved sources повторно
перевіряються та публікуються під час запуску нового SDK host.

## Native integration та закриття

Scene ABI 1 має material extensions 42/43: canonical material identities і
атомарний набір validated resource handles. Renderer resource identity не
замінює source inheritance. Prepared binary bindings для vegetation aliases
збережені; наявність скопійованого uncooked source instance не змінює цей
прийнятий runtime profile. Explicit source editor продовжує перевіряти source
за capabilities static profile.

Release stress-close виявив, що idle APC loops викликали resource callbacks
паралельно зі знищенням resource manager. Admission gate тепер припиняє нові
виклики й чекає активних перед manager destruction. File producers після цього
ще живі для retirement власних queues. Окремий CTest перевіряє waiting,
late-call rejection і exception drain; GPU logs перевіряють порядок shutdown.

## Відтворення

```powershell
python sources/tools/sdk/prepare-sdk6-material-fixtures.py --assets build/sdk-assets
cmake --build build/sdk-native-20261108-debug --target stalker2 xray_editor_world xray_sdk_ui xray_apc_resource_callbacks_contract -j 8
ctest --test-dir build/sdk-native-20261108-debug --output-on-failure
sources/tools/sdk/test-sdk-property.ps1 -BuildRoot build/sdk-native-20261108-debug -OutputRoot build/new-sdk6-material-run -FullIde -MaterialCommands -ClosePending
python sources/tools/sdk/verify-sdk-material.py build/new-sdk6-material-run
```

Кожен GPU run потребує нового OutputRoot. GPU workflows запускаються
послідовно. `-SeedSources` копіює material/texture sources із попереднього
workspace до нового ізольованого host run; input directory не змінюється.

Підсумок: Debug/Release — CTest 7/7 і по 20 managed groups. Чотири native
GPU workflows пройшли validation 0/0 та повне закриття: material authoring у
двох конфігураціях, texture regression і cold-host restore + Play/Simulate.
Material captures дають по 17 exact matches та 7 observable changes. Нестандартний
saved base color у новому host змінює 6026 pixels проти попереднього runtime
baseline. Hashes перевіряє `python sources/tools/sdk/check-sdk6-material-checkpoint.py`.

## Відкрита частина етапу

Цей checkpoint не приймає всю original material library. Stage/effect palette,
instance schema repair, shader authoring/compilation workflows, інші material
stages та спеціальні domains ще потребують port і власних regressions.
Model surface assignment, collision/physics, render-options, texture import/
compression/cube authoring та OBJ/Maya pipelines залишаються у плані SDK-6.
Native material preview використовує standard woodbox primitive; це не є
доказом імпорту довільних моделей. Preview scratch files залишаються в user data.
