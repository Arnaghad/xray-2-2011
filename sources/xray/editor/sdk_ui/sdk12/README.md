# SDK-12 — Сумісність, пакування й остаточне приймання

Цей звіт описує пакети, прийняті до завершення міграції UI на Avalonia. Поточні вимоги до рантайму та команди пакування наведено в [README репозиторію](../../../../../README.md).

Статус: **Прийнято (2026-11-19 / перевірено 2026-11-20)** у Debug та Release. Усі докази, SHA-256 контрольні суми пакетів, бінарників, залежностей, шейдерів, джерел і readback-перевірок зафіксовано в [acceptance.json](acceptance.json) і `build/sdk12-acceptance-20261119/`.

## 1. Чисті автономні Windows x64 Debug/Release SDK Deployments

Збірку автономних пакетів виконує [package-sdk-runtime.py](../../../../tools/sdk/package-sdk-runtime.py):
- **Debug deployment:** `build/sdk12-package-20261119/debug` (`1244` файли, `40` PE-бінарників перевірено `objdump -p`, `peImportAuditPassed = true`, Vulkan validation за замовчуванням увімкнено).
- **Release deployment:** `build/sdk12-package-20261119/release` (`1242` файли, `40` PE-бінарників перевірено `objdump -p`, `peImportAuditPassed = true`, опційний `-Validate` прапорець).

### Структура розгортання та точні залежності
- `bin/` — виконувані та бібліотечні бінарники: `stalker2.exe`, `xray_editor_world.dll`, `libxray_core.dll`, `libxray_engine.dll`, `libxray_render_vulkan.dll`, `libxray_editor_base.dll`, `libcs_core.dll`, `libcs_script.dll`, CLI-інструменти (`xray_asset_cook_cli.exe`, `xray_shader_compiler.exe`, `xray_maya_cook_cli.exe`), керовані збірки `.NET 11.0` (`xray.editor.base.dll`, `wpf_controls.dll`, `DockPanelSuite`, `Xceed.Wpf.Toolkit`, `Xceed.Wpf.AvalonDock` + супутні `.resources.dll` та `.pdb` для Debug), MinGW/Bullet/Lua/Vorbis/mimalloc/zlib runtime DLL (`libBulletCollision.dll`, `libBulletDynamics.dll`, `libBulletSoftBody.dll`, `libLinearMath.dll`, `libgcc_s_seh-1.dll`, `libstdc++-6.dll`, `libwinpthread-1.dll`, `libmimalloc.dll`, `lua55.dll`, `zlib1.dll`, `libogg-0.dll`, `libvorbis-0.dll`, `libvorbisenc-2.dll`, `libvorbisfile-3.dll`), DXC (`dxcompiler.dll`, `dxil.dll`).
- `bin/vulkan-validation/` — Khronos Vulkan validation layer (`VkLayer_khronos_validation.dll`, `VkLayer_khronos_validation.json`, `libSPIRV-Tools*.dll`).
- `shaders/` — повний автономний набір HLSL/HLSLI-шейдерів Vulkan (`23` root файли + `4` файли в `basic/`), який автоматично підхоплюється без залежності від дерева вихідного коду репозиторію.
- `assets/` — read-only підготовлений пакет ресурсів (`build/sdk-assets`) + еталонні фікстури (`assets/fixtures/sdk11/`).
- `user-data/` (`workspace/`, `cache/`, `settings/`) та `tmp/` — **окремі writable директорії**, ізольовані від `bin/`, `assets/` та `shaders/`. Уся персистентність налаштувань віконного shell використовує `user-data/sdk-ide-settings.json` замість Windows Registry `HKCU\Software\GSC Game World\X-Ray SDK`.
- `settings/sdk-runtime.json` — конфігурація розгортання, параметри запуску та зовнішні системні передумови (Windows 10/11 x64, `.NET Desktop Runtime 11.0.0`, драйвер Vulkan 1.3+ ICD з `shaderDemoteToHelperInvocation`).

### Команди запуску та розмежування IDE / Control Host
- **Відновлена IDE за замовчуванням (`-editor` -> `WindowIde`):**
  - Запуск: `launch-sdk.cmd` або `launch-sdk.ps1` або `bin\stalker2.exe -editor`.
  - У [editor_entry_point.cpp](../../../../xray/editor/world/sources/editor_entry_point.cpp) та [EditorWorld.Sdk.cs](../../../base/EditorWorld.Sdk.cs) прапор `-editor` відкриває відновлену `WindowIde` без внутрішніх env-прапорців діагностики, з автоматичним пошуком сусідніх `../assets`, `../shaders`, `dxcompiler.dll` (у [shader_compiler.cpp](../../../../xray/render/core/sources/rhi/shader_compiler.cpp) і smoke shaders) та `../user-data` (у [core_entry_point_win.cpp](../../../../xray/core/sources/core_entry_point_win.cpp), [engine_world_win.cpp](../../../../xray/engine/sources/engine_world_win.cpp), [managed_host.cpp](../../../../xray/editor/world/sources/managed_host.cpp)).
- **Контрольний Vulkan Host (окремий test harness):**
  - Запуск: `launch-sdk.cmd -ControlHostHarness` або з явною змінною середовища `XRAY_SDK_PROPERTY_EDITOR=0`.
  - Зберігає ізольовану роботу контрольного M8 Vulkan host без прив'язки керованих розширень IDE (`SDK property bridge bound:` відсутній, захоплюється лише контрольний `captures/game.bmp`).

## 2. Покриття інвентаризації SDK-0, Renderer Paths та Архівний статус старих Suites

- **Функціональна матриця ([function-matrix.json](../sdk0/function-matrix.json)):** закрито **`40/40`** родин функцій (`runtimeWorkflowAccepted = true`), із яких **`36`** — обов'язкові оригінальні родини SDK-0 (включно з фінально закритими `SDK-2` родинами `shell`, `layout`, `shared-controls`) та **`4`** — сучасні опційні DCC-інтеграції (`dcc-static`, `dcc-skin`, `dcc-physics`, `dcc-animation`).
- **Renderer Paths ([renderer-mapping.json](../sdk5/renderer-mapping.json)):** усі **`15`** маршрутів Vulkan прийнято; окремо у запакованому Release-ратаймі виконано послідовний прогін усіх `21` режимів рендера (`render-modes-release`).
- **Архівовані старі migration suites:** перевірено в [SdkSdk12Checks.cs](../SdkSdk12Checks.cs) та [source_contract.cmake](../../../../tools/tests/source_contract.cmake) — старі `.sln`/`.vcproj`/`.vcxproj` та C++/CLI модулі (`# add_subdirectory(controls)`, `dialog`, `model`, `particle` в [CMakeLists.txt](../../../CMakeLists.txt)), як і старі `m5`/`m8`/`m9` migration test harnesses, виключено з активних CMake/CTest цілей.

## 3. Послідовна верифікація та виміряні бюджети (без паралельних GPU workloads)

### Збірки, CTest та автономні GPU-прогони
1. **Debug та Release збірки (`xray_editor_host`, `xray_sdk_ui`, `stalker2`, CLI):** `Build succeeded.`, `0 Warning(s)`, `0 Error(s)`.
2. **Debug CTest:** SDK subset `5/5` (`20.04s`, `33` суміснісні перевірки в `xray_sdk_ui_compatibility`), повний CTest `31/31` (`124.50s`).
3. **Release CTest:** SDK subset `5/5` (`21.43s`, `33` суміснісні перевірки в `xray_sdk_ui_compatibility`), повний CTest `31/31` (`110.14s`).
4. **Послідовна пакетована Vulkan GPU-матриця (`test-sdk-packaged.ps1` зі scrubbed `PATH = vulkan-validation;System32;SystemRoot` без `XRAY_SDK_PROPERTY_EDITOR`, `XRAY_DXC_PATH`, `XRAY_VULKAN_SMOKE_SHADER_DIR`, `-render_assets`):**
   - `gpu-debug-packaged`: `PASS`, `110.75s`, `1242` файли пакета незмінні, `8` візуальних переходів, піксельно-точний Undo збіг (`6==8`), Vulkan validation `errors=0 warnings=0`, pending-close drain і впорядкований `M8 shutdown`.
   - `gpu-release-packaged`: `PASS`, `77.23s`, `1240` файлів пакета незмінні, `8` візуальних переходів, піксельно-точний Undo збіг (`6==8`), Vulkan validation `errors=0 warnings=0`, pending-close drain і впорядкований `M8 shutdown`.
   - `control-host-debug`: `PASS`, `55.85s`, захоплення `captures/game.bmp` без SDK IDE мостів, `validation errors=0 warnings=0`.
   - `control-host-release`: `PASS`, `35.11s`, захоплення `captures/game.bmp` без SDK IDE мостів, `validation errors=0 warnings=0`.
   - `render-modes-release`: `PASS`, `42.63s`, усі `21` кроків і захоплень режимів рендера, pending-close drain, `validation errors=0 warnings=0`.

### Виміряні ресурсні й часові бюджети довгої сесії (`EditorWorld.Sdk12Checks.cs`)
| Метрика | Ліміт | Debug (пакетований) | Release (пакетований) |
|---|---:|---:|---:|
| Peak Working Set (MiB) | `4096.0` | `2237.97` | `2236.87` |
| Peak Paged Memory (MiB) | — | `7522.38` | `7527.01` |
| Managed GC Heap (MiB) | `512.0` | `54.08` | `52.07` |
| 25x Transform + Undo batch (ms) | `15000` | `8068` | `7125` |
| Повна сесія кроків 0–8 (ms) | `120000.0` | `54845.73` | `40273.13` |
| Vulkan Validation Errors / Warnings | `0 / 0` | `0 / 0` | `0 / 0` |

## 4. Залишкові обмеження (Residual Limitations)

1. **GI / Light Propagation Volumes (LPV):** не реалізовано у Vulkan-паритеті (`render-mapping.json`: `indirect_lighting_mode` використовує environment/ambient-складову без динамічного GI/LPV).
2. **`object painter` tab:** лишається єдиним дормантним інструментом у `ToolsTab` (`DormantLevelEditorTools`, відповідає закоментованому виклику в оригінальному `level_editor.cpp:432`), вихідні файли збережено.
3. **Оригінальний бінарник SDK-0 (2011):** падає під час старту у вихідному середовищі; приймання спирається на відновлені вихідні тексти, документи та повну верифікацію модернізованого SDK без заявлення живого UI-паритету зі старим `.exe`.
4. **Референс-проєкти `alex` та `dima_light_test`:** проходять завантаження/заміну залежностей в ізольованому `Project` (`SDK-3` та `SDK-12`), але повне нативне Vulkan-приготування їхньої сцени обмежується відсутністю текстур шарів їхніх `.terr` і моделі `props/box/dogbox` у підготовленому паку `build/sdk-assets`.
5. **Активні particle-сцени та 21 режим рендера:** сцена з активними емітерами частинок у Vulkan підтримує `lit` і `unlit`; повний каталог із `21` режимів перевіряється послідовно на статичній lit-сцені (`render-modes-release`).
6. **Зовнішні інструменти:** застарілий COM-інтероп `Microsoft.Office.Interop.Excel` (Office 12) замінено беззалежним OpenXML `.xlsx` + SpreadsheetML `.xls` + `.batch`; застарілий C++/CLI Maya-експортер замінено розширенням Blender 5.3 + плагіном Maya Python API 2.0 + C++26 кукером `xray_maya_cook_cli`.
7. **Історичні design-мокапи та ізольовані налаштування:** нереалізовані в оригінальному коді ескізи Word/PSD (теги, пресети кривих, компресори/еквалайзери звуку) виключено з обсягу паритету; замість системного реєстру використовується файл налаштувань `user-data/sdk-ide-settings.json`.

## 5. Відтворення та верифікація

- Перевірка зафіксованих гешів і інваріантів SDK-12: `python sources/tools/sdk/collect-sdk12-acceptance.py --verify`
- Перевірка попередніх етапів SDK-6–SDK-11: `python sources/tools/sdk/collect-sdk<N>-acceptance.py --verify` (`N = 6..11`)
