# X-Ray Engine 2.0 — Modern C++26, Vulkan 1.4, .NET 11 SDK & Blender 5.3 Pipeline

Модернізований рушій, редакторський SDK та DCC-конвеєр **X-Ray Engine 2.0** (кодова база *S.T.A.L.K.E.R. 2* зразка 2011 року), перенесені зі спадкового 32/64-бітного стека Visual Studio 2008/2010 + DirectX 9/10/11 + .NET Framework 3.5 + Maya 2011 C++ `.mll` на сучасну архітектуру **C++26 (GCC 16 / MinGW-w64 + CMake + Ninja)**, **Vulkan 1.4 RHI (HLSL → SPIR-V через DXC)**, **Avalonia 12.2 на .NET 11 (`net11.0`)** та **Blender 5.3 / Maya 2027 (`xray-dcc-exchange/v2`)**.

---

## Зведена таблиця змін відносно базового X-Ray 2.0 (2011)

| Підсистема | Базовий X-Ray 2.0 (2011) | Модернізована версія (цей репозиторій) |
| --- | --- | --- |
| **Мова та система збірки** | C++03, MSVC 2008/2010 (`.sln` / `.vcproj`), Win32 / x64 / Xbox 360 / PS3 | **C++26**, єдиний **CMake 4.3+ + Ninja**, **GCC 16 (MSYS2 MinGW-w64 x64)** |
| **Графічний бекенд (RHI)** | DirectX 9 / DirectX 10 / DirectX 11 (`xray/render/dx11`) | **Vulkan 1.4 RHI** (`xray/render/vulkan`), системний `vulkan-1.dll`, 0 помилок Khronos Validation |
| **Шейдерний конвеєр** | HLSL → D3DCompiler (DXBC SM 4.0/5.0) | **HLSL → SPIR-V (Vulkan 1.3 target)** через **DXC** + інтроспекція **SPIRV-Reflect** |
| **Текстури та матеріали** | Фіксовані слоти DX10/11, одностадійні обмеження в ранніх портах | **Vulkan 1.2 Bindless (`VK_EXT_descriptor_indexing`)** (`Texture2D[1024]`, `TextureCube[128]`), багатостадійні (`multi-stage`) матеріали |
| **Освітлення та поверхні** | Legacy Light Pre-Pass у DX10/11 | **2-Pass Light Pre-Pass / Deferred Material** у Vulkan: анізотропний BRDF, **Pre-Integrated Skin/Organic SSS**, POM, Triplanar, Soft Particles, Cubemap Reflections, Burning Wood |
| **Скінінг та LOD** | CPU-скінінг / DX константні буфери, жорсткий ліміт **80 кісток** (`79 + 1`), без runtime LOD у Vulkan | **GPU Compute Skinning (`skinning.comp.hlsl`)** на SSBO, ліміт **128 кісток** (`127 + object_movement`), автоматичне перемикання **Runtime LOD (`LOD0..LOD2`)** за екранним AABB |
| **Редакторський SDK** | C++/CLI + .NET Framework 3.5 (`AvalonAdapter`, старий WinForms/WPF) | **.NET 11 (`net11.0`)**, оболонка **Avalonia** з `Dock.Avalonia`, нативний Vulkan-в’юпорт через C ABI та `PerMonitorV2` DPI на Windows x64 |
| **DCC-інструменти (3D/Анімація)** | Тільки Autodesk Maya 2011 (32/64-bit C++ `.mll` плагін + пропрієтарний SISL) | **Blender 5.3** як основний DCC (`xray_blender` v0.3.3) + **Maya 2022–2027 Python API 2.0** (`xray_maya_plugin.py` v2.1), спільний формат **`xray-dcc-exchange/v2`** |
| **Кукер асетів та математика кривих** | Прив’язаний до Maya runtime (`xray/maya_animation` + SISL + Win32) | Автономний портативний **C++26 `xray_asset_cook_cli`** та header-only бібліотека **`xray_animation_math`** (кубічний B-spline фітер без Maya/SISL) |
| **Міграція спадкового контенту** | Ручний експорт із Maya 2011 | Автоматичний конвеєр **`migrate_legacy.py`**: декомпіляція `.model`/`.skeleton`/`.skinned_model`/`.clip`/`.physics` без Maya, парсер `.ma` (включно з Trax `clipLibrary`), batch-міграція `.mb` |

---

## Детальний огляд архітектурних змін

### 1. Ядро рушія, C++26 та система збірки (`sources/CMakeLists.txt`)

- **Сучасний C++26 та x64-only контракт:** проєкт переведено на стандарт `CMAKE_CXX_STANDARD 26` зі збіркою через **CMake + Ninja** компілятором **GCC 16 (MinGW-w64 x64)**. Застарілі 32-бітні та консольні (Xbox 360 / PS3 Cell) гілки ізольовані; додано контрактні тести `xray_source_contract` і `xray_windows_x64_contract` (перевірка ABI м’ютексів Windows та багатопотокової синхронізації).
- **Уніфікація сторонніх залежностей:**
  - **Lua 5.5 + `sol2`:** усі модулі рушія, кукера та редактора використовують єдиний системний рантайм Lua 5.5 із пропатченим знімком `sources/third_party/sol2` (перевіряється тестом `xray_lua_sol_binding`).
  - **Системні пакети MSYS2 (єдине джерело заголовків і бібліотек):** `Boost` 1.92.0 (`Boost::headers`, `filesystem`, `system`, `thread`), `SDL2`, `mimalloc`, `zlib` (`ZLIB::ZLIB` для декодування terrain chunk 103), `Bullet` (`PkgConfig::BULLET`), `Ogg` / `Vorbis` (`PkgConfig::OGG_VORBIS`), `Asio` (`asio::asio`).
  - **Збережені вбудовані бібліотеки:** `ODE`, `OPCODE`, `FastDelegate`, `BugTrap` та відновлені алгоритми геометричного фітингу `WildMagic4` (`Wm4ContBox3` / `Wm4ContSphere3` у `sources/third_party/wildmagic4`).
- **Захищена віртуальна файлова система (VFS) та конфігурації:** додано 64-бітну валідацію бінарних конфігів (`checked_config`), детектування циклів і перевірку меж при читанні VFS hard links та legacy-архівів.

---

### 2. Графічний рушій Vulkan 1.4 (`sources/xray/render/vulkan/`)

Повністю замінено спадковий DirectX-рендер на сучасний багатопотоковий бекенд **Vulkan 1.4** зі збереженням фасадних контрактів X-Ray 2.0 (`logic` / `render` / `resource` потоки):

- **Власність RHI та детермінований життєвий цикл ресурсів:**
  - Єдиний власник Vulkan RHI (`vulkan_backend.cpp`), який працює через системний Windows loader (`vulkan-1.dll` API ≥ 1.4).
  - Кадровий конвеєр `begin_frame -> commands -> end_frame/submit -> present` із генераційними хендлами, відкладеним звільненням ресурсів за fence-серіалами (`submit serial`) та повним `drain` кукерів і черг перед знищенням вікон (`HWND`).
  - Відсутність `vkDeviceWaitIdle` на звичайних кадрах; повторне використання дескрипторних наборів для кожного frame slot та LRU-кеш `VkRenderPass`/`VkFramebuffer` (до 64 пар на вихід), що забезпечує нульовий ріст пам’яті на довгих прогонах (підтверджено профілем **M9 Performance**).
- **Двопрохідний Light Pre-Pass / Deferred Material конвеєр:**
  1. **G-Buffer Pass (`static_gbuffer.hlsl`):** записує лінеаризовану глибину $\sqrt{z_{\text{view}} / z_{\text{far}}}$, двокомпонентну шорсткість `roughness.xy`, стиснену нормаль, коефіцієнт Френеля `fresnel_at_0_degree`, дотичний вектор анізотропії `tangent_cross`, ідентифікатор профілю освітлення (`Standard` / `Skin` / `Organic`) та коефіцієнт просвічування `translucency`.
  2. **Deferred Lighting Pass (`deferred_lighting.hlsl`):** обчислює анізотропний BRDF (Beckmann / Szirmay-Kalos), **Pre-Integrated Skin & Organic Subsurface Scattering (SSS)**, wrap/back-scattering, каскадні тіні від сонця та освітлення від локальних джерел із 3-екстентовим LRU-атласом тіней.
  3. **Material Pass (`static_material.hlsl`) і Forward Pass (`static_forward.hlsl`):** виконують фінальне затінення з `EQUAL` depth test, підтримують прозорість, декалі (`decals.hlsl`), рефракцію/дисторсію (`distortion.hlsl` + `distortion_composite.hlsl`), skybox (`skybox.hlsl`), ландшафт (`terrain_unlit.hlsl`), частинки й постпроцесинг (**Bloom, SSAO, FXAA, TAA, Distortion, MSAA**).
- **Підтримка Legacy-матеріалів та Vulkan 1.2 Bindless (`VK_EXT_descriptor_indexing`):**
  - Глобальні bindless-масиви `Texture2D[1024]` і `TextureCube[128]` у дескрипторному наборі Set 3 (`space3`, `NonUniformResourceIndex`).
  - **Багатостадійні матеріали (`multi-stage`):** один сабмеш може одночасно містити бітову маску кількох стадій (`g_stage`, `forward`, `decal`, `shadow`), включно з `forward/forward_decal` (depth bias `-2/-2`).
  - **Розблоковані шейдерні ефекти оригінальних асетів:**
    - *Геометричні та процедурні поверхні:* **Parallax Occlusion Mapping** (`use_parallax`, 8-кроковий ray-marching + бінарна доводка, синхронізована між G-Buffer і Material pass), **Triplanar World-Space UV** (`g_stage_default_tiled`, `forward_default_tiled`), **Burning Wood** (`g_stage_burning_wood` з `texture_whirl`, обвугленням і HDR-емісією жару).
    - *Текстурні карти та модулятори:* `use_emissive_map` (`texture_emissive`), `use_tditail_diffuse` (`texture_ditail_diffuse`), анімація атласів `use_sequence` / `forward_default_subuv`, світова варіативна маска `use_variation_mask`, карти `texture_fresnel_at_0_degree` та `anisotropic_direction_texture`, `use_diffuse_attenuation` / `use_tdiffuse_power`, `specular_color_multiplier`, по-матеріальний прапорець `is_cast_shadow`, локальні кубмап-відбиття `use_reflection` (`texture_cubemap`, `texture_cubemap_mask`).
    - *Прозорі та об’ємні forward-ефекти:* **Soft Particles** (`forward/forward_soft`) із читанням буфера глибини `d24_s8_unorm` у `VK_IMAGE_LAYOUT_DEPTH_STENCIL_READ_ONLY_OPTIMAL` (підтримка як `Texture2D`, так і `Texture2DMS` для MSAA), `forward/forward_fire_fresnel`, освітлений дим `forward/forward_smoke_custom_lighted`, об’ємні конуси та сфери `forward/forward_volume_cone_base` і `forward/forward_volume_sphere_base`.
- **GPU Compute Skinning (128 кісток) та Runtime LOD:**
  - По-кадровий CPU-скінінг замінено на **Vulkan Compute Shader** ([skinning.comp.hlsl](sources/xray/render/vulkan/assets/skinning.comp.hlsl)): вершини bind-pose зберігаються в `StructuredBuffer` (`gpu_skeletal_vertex`, 88 байт), а обчислені трансформовані вершини записуються напряму у вершинний буфер (`VK_BUFFER_USAGE_STORAGE_BUFFER_BIT | VK_BUFFER_USAGE_VERTEX_BUFFER_BIT`) із бар’єром `COMPUTE_SHADER_BIT → VERTEX_ATTRIBUTE_READ_BIT`.
  - Ліміт кісток піднято з **80 до 128** (`127` авторських кісток + `object_movement`, розмір `gpu_skinning_constants` = `8208` байт ≤ 16 KB гарантованого Vulkan UBO) у рендері, фасаді (`update_skeleton_command.h`), кукері та DCC-інструментах.
  - Реалізовано автоматичне перемикання рівнів деталізації **`lod_hierrarchy` (`LOD0` / `LOD1` / `LOD2`)** у `vulkan_model_cook.cpp` та `vulkan_scene_runtime.cpp` за площею екранної проєкції AABB.

---

### 3. Відновлення та модернізація X-Ray 2.0 SDK (`sources/xray/editor/`, етапи SDK-0 — SDK-12)

Міграцію UI редактора на **Avalonia 12.2** завершено. `EditorHost`, ядро редактора й бібліотека контролів збираються під **.NET 11 (`net11.0`)**. Цільова платформа SDK — Windows x64 з нативним Vulkan-в’юпортом. [План міграції та результати перевірок](ui_migration_plan.md)

- **Нативно-керований міст та UI-фундамент (SDK-0 — SDK-2):**
  - Чистий C ABI міст (`host_bridge.h`, `managed_host.cpp`, `Bridge.cs`, `sdk_property_bridge.h`) між нативним `xray_editor_world` і керованим `EditorHost`.
  - `xray.editor.core` містить моделі та команди редактора. `xray.editor.avalonia_controls` надає PropertyGrid, редактор кривих, палітру кольорів, графи, таймлайни й панелі спеціалізованих редакторів.
  - `WindowIde` керує головною оболонкою `SdkAvaloniaShellWindow`. `Dock.Avalonia` зберігає макет у `workspace-layout.json` і підтримує імпорт попереднього XML-макета. `AvaloniaViewportHost` керує поверхнею Vulkan при зміні докінгу та DPI.
- **Проєкти, VFS, транзакційний `CommandEngine` та 3D-в’юпорт (SDK-3 — SDK-5):**
  - Повний цикл роботи з проєктами `.xprj` (`New / Open / Save / Save As / Close`, кириличні шляхи, recovery, dirty-індикація, ізоляція робочих копій від read-only оригіналів), `SdkResourceBrowser` та інспектор залежностей поверх нативного resource ABI.
  - Єдиний стек **`CommandEngine`** (create/delete/duplicate, cut/copy/paste з ремапінгом GUID, group/ungroup/reparent, multi-object transforms, вкладені транзакції та preview accept/cancel).
  - Нативні 3D-гізмо трансформацій (`W`/`E`/`R`, World/Local, snap по сітці, вершинах, ребрах і гранях), пікінг мешів променем з DPI-інваріантною математикою, силуетні контури виділення, незалежні вікна прев’ю та режим `Play / Simulate`.
- **Усі 40/40 функціональних родин редакторів (SDK-6 — SDK-12):**
  - **SDK-6 (Asset Authoring):** редактор моделей, редактор матеріалів (`SdkMaterialPalette` з підтримкою всіх нових Vulkan-ефектів), налаштування рендера (`SdkRenderOptions` для `resources/render/*.cfg`), колізій та фізики.
  - **SDK-7 (World Tools):** 7 родин інструментів рівня (`terrain`, `solid`, `lights`, `composites`, `particle-placement`, `speedtree`, `misc`).
  - **SDK-8 (Animation Authoring):** 5 родин (`animation-collections`, `animation-viewer`, `animation-setup`, `animation-groups`, `animation-placement`).
  - **SDK-9 (Audio):** `sound-editor` та `audio-tools`.
  - **SDK-10 (Particles)** — редактор частинок із графом Avalonia, таймлайном і попереднім переглядом симуляції.
  - **SDK-11 (Narrative & AI):** `dialog-editor`, `dialog-excel`, `scripting`, `navigation`.
  - **SDK-12 (Packaging & Deployment)** — пакування Debug/Release та запуск `stalker2.exe -editor`. Пакет містить збірки Avalonia, Dock.Avalonia й нативні бібліотеки SkiaSharp та HarfBuzz. Керований хост використовує `Microsoft.NETCore.App` 11.0 без `Microsoft.WindowsDesktop.App`. [Скрипт пакування](sources/tools/sdk/package-sdk-runtime.py)

---

### 4. Конвеєр Blender 5.3 / Maya 2027, автономний кукер та міграція анімацій (`sources/tools/`, `sources/xray/maya_cook/`)

Замість прив’язки до Maya 2011 C++ `.mll` створено крос-DCC архітектуру з **Blender 5.3** як основним інструментом за замовчуванням і **Maya 2022–2027** як підтримуваною альтернативою:

- **Незалежне математичне ядро [`xray_animation_math`](sources/xray/animation_math/README.md) та кукер [`xray_asset_cook_cli`](sources/xray/maya_cook/):**
  - Кубічний B-spline фітер анімацій, декодер `.clip_check_data` та перевірка помилки апроксимації винесені в чисту C++26 header-only бібліотеку `xray_animation_math`, спільну для кукера та рантайму `xray_animation` (`track_process.cpp`), без Maya world і без SISL.
  - Портативний C++26 CLI-кукер `xray_asset_cook_cli` (і сумісний аліас `xray_maya_cook_cli`) читає бандли **`xray-dcc-exchange/v1` і `v2`** ([SPEC_V2.md](sources/tools/dcc_common/SPEC_V2.md)), автоматично конвертує координатні системи (`blender_right_handed_z_up` ↔ `right_handed_y_up`), генерує `.model`, `.skeleton`, `.skinned_model`, `.clip` і `.physics`, підтримує `--fit-collision` (WildMagic4) та пряму верифікацію семплів анімації (`--clip-samples`).
- **Розширення Blender 5.3 ([`xray_blender` v0.3.3](sources/tools/blender/xray_blender/README.md)) та Maya Python API 2.0 ([`xray_maya_plugin.py` v2.1](sources/tools/maya/README.md)):**
  - Повний експорт, імпорт і варіння всіх 5 типів ресурсів, створення фізичних тіл/колізій/обмежень (6 DOF), локаторів, auto-ragdoll та підтримка Blender 5.x **Slotted Actions / Channelbags**.
  - **Живий реімпорт у SDK (`DCC Source / Reimport`):** відкриття `.blend` або `.ma`/`.mb` безпосередньо з Model Editor / Resource Browser, автоматичне відстеження збережень (watcher) і гаряче оновлення геометрії та анімацій у Vulkan-в’юпорті без перезапуску SDK.
- **Автоматична міграція спадкового корпусу 2011 року ([`migrate_legacy.py`](sources/tools/blender/migrate_legacy.py)) та виправлення проблемних анімацій ([`blender_animation_fixes_plan.md`](blender_animation_fixes_plan.md)):**
  - **Декомпіляція без Maya ([`legacy_codec.py`](sources/tools/dcc_common/legacy_codec.py)) та читання `.ma` ([`ma_reader.py`](sources/tools/dcc_common/ma_reader.py)):** прямий імпорт готових `.model`, `.skinned_model`, `.skeleton`, `.clip` (із реконструкцією редагованих ключів за `.clip_check_data` і вузлами сплайна), `.physics` та Maya ASCII сцен без встановленої Maya.
  - **Підтримка Maya Trax Editor `.ma` кліпів (`clipLibrary` / `animClip`):** парсинг standalone Trax-кліпів (`test_clip.ma`, `test_clip_crouch_walk_move_fwd_aim_1.ma`), обчислення Maya Auto/Plateau тангенсів (`tan` 9/10) та автоматичне підключення до сумісного `.skeleton`.
  - **Точне відновлення скінінгу (`bindPreMatrix` + `envelope = 0`):** у `skeletal_extractor.py` та `static_extractor.py` світові bind-матриці кісток зчитуються напряму з `skinCluster.bindPreMatrix[logicalIndex].inverse()` та `geomMatrix`, а базова сітка екстрагується за тимчасового `skinCluster.envelope = 0.0`. Це повністю усуває розрив між скелетом і тілом та спайки вершин у сценах, збережених на ненульовому кадрі з IK/expressions (зокрема у сценах стрибка кровососа `krovosos_attack_2.mb`, похибка деформованих вершин Maya vs Blender ≤ `3.05e-5 см`).
  - **Фільтрація прихованих DAG-вузлів та очищення ієрархій (`migrate_scene.py`):**
    - функція `_is_dag_visible()` перевіряє `.visibility`, `overrideVisibility` і `displayLayer.visibility` уздовж усіх DAG-предків, відсіваючи приховану референсну скульптуру анатомічного скелета людини (`|skeleton|skeletonShape`, 617 тис. вершин у сценах кровососа) та 18 прихованих незважених IK/FK допоміжних рігів;
    - автоматичне видалення дубльованих незважених листових `'none'`-кісток (`skeleton_man.mb`) та однозначний матчинг `skinCluster` за повними DAG-шляхами у multi-rig сценах;
    - підтримка запікання окремих Maya Animation Layers поверх базового control rig через `--anim-layer-base` (`layer_pose_wall_back.mb`);
    - опційна математично безпечна нормалізація `--normalize-zero-root-scale` для 23 спадкових тестових кліпів `navgation_controller` із дефектом раннього експортера `Root.scale = (0, 0, 0)`.

---

## Структура репозиторію та документація

- [`sources/CMakeLists.txt`](sources/CMakeLists.txt) — головний CMake-файл збірки рушія, рендера, редактора та кукера.
- [`sources/README.md`](sources/README.md) — опис підтримуваного середовища збірки (Windows x64, MSYS2 MinGW-w64 GCC 16, Ninja).
- [`sources/xray/render/vulkan/`](sources/xray/render/vulkan/README.md) — бекенд Vulkan 1.4, HLSL-шейдери (`assets/`), кукери моделей/матеріалів і контракти продуктивності.
- [`sources/xray/editor/`](sources/xray/editor/) — нативні модулі SDK (`world/`, `animation/`, `sound/`) та керовані проєкти .NET 11 (`base/`, `core/`, `avalonia_controls/`, `avalonia_controls.tests/`).
- [`sources/xray/animation_math/`](sources/xray/animation_math/README.md) — DCC-незалежна C++26 математика фітингу та перевірки B-spline анімацій.
- [`sources/xray/maya_cook/`](sources/xray/maya_cook/) — портативний C++26 кукер ресурсів `xray_asset_cook_cli` / `xray_maya_cook_cli`.
- [`sources/tools/dcc_common/`](sources/tools/dcc_common/README.md) — спільне ядро `xray-dcc-exchange/v2` ([SPEC_V2.md](sources/tools/dcc_common/SPEC_V2.md), [PARITY.md](sources/tools/dcc_common/PARITY.md)), декомпілятор `legacy_codec.py` та парсер `ma_reader.py`.
- [`sources/tools/blender/`](sources/tools/blender/README.md) — розширення Blender 5.3 `xray_blender`, конструктор пакета `build_extension.py` та конвеєр міграції `migrate_legacy.py` ([SDK.md](sources/tools/blender/SDK.md), [IMPORT.md](sources/tools/blender/IMPORT.md), [MIGRATION.md](sources/tools/blender/MIGRATION.md), [MAINTAINER.md](sources/tools/blender/MAINTAINER.md)).
- [`sources/tools/maya/`](sources/tools/maya/README.md) — плагін Maya Python API 2.0 (`xray_maya_plugin.py`) та скрипти екстракції сцен.
- **Плани та звіти про реалізацію:**
  - [`vulkan_integration_plan.md`](vulkan_integration_plan.md) та [`vulkan_integration_history.md`](vulkan_integration_history.md) — інтеграція та приймання Vulkan RHI (етапи M0–M9).
  - [`vulkan_legacy_materials_plan.md`](vulkan_legacy_materials_plan.md) — перенесення legacy-властивостей матеріалів, Bindless текстур, SSS, GPU Compute Skinning і Runtime LOD у Vulkan.
  - [`sdk_migration_plan.md`](sdk_migration_plan.md) — відновлення функцій оригінального SDK і перенесення в’юпорта на Vulkan (етапи SDK-0 — SDK-12).
  - [`ui_migration_plan.md`](ui_migration_plan.md) — міграція оболонки, контролів і редакторів на Avalonia (етапи UI-0 — UI-7).
  - [`blender_pipeline_plan.md`](blender_pipeline_plan.md) — архітектура конвеєра Blender 5.3 / Exchange v2 та міграція корпусу (етапи B0 — B8).
  - [`blender_animation_fixes_plan.md`](blender_animation_fixes_plan.md) — виправлення проблемних анімацій, розширення ліміту кісток `80 → 128`, точний скінінг `bindPreMatrix` і Trax `.ma` кліпи.

---

## Швидкий старт: збірка та перевірка

### 1. Збірка рушія, Vulkan-рендера, SDK та кукера (Windows x64, MSYS2 MinGW-w64 GCC + Ninja)

Зафіксовані версії інструментів та пакетів MSYS2 описано в [`sources/tools/ci/toolchain-manifest.json`](sources/tools/ci/toolchain-manifest.json), а ізольовані Debug/Release пресети — у [`sources/CMakePresets.json`](sources/CMakePresets.json).

```powershell
Push-Location sources
cmake --preset windows-x64-release
cmake --build --preset windows-x64-release --parallel 8
ctest --preset windows-x64-release-cpu
ctest --preset windows-x64-release-all
Pop-Location
```

Для збору базових метрик (версії, імпорти DLL, попередження компілятора, JUnit-звіт CTest):

```powershell
python sources/tools/ci/collect_build_baseline.py --preset windows-x64-release --build-dir build/presets/windows-x64-release --output build/modernization-baseline/windows-x64-release/baseline.json
```

### 2. Автономна збірка лише DCC-кукера (`xray_asset_cook_cli`)

```powershell
cmake -S sources/xray/maya_cook -B build/standalone-cook -G Ninja -DCMAKE_BUILD_TYPE=Release
cmake --build build/standalone-cook --parallel 8
ctest --test-dir build/standalone-cook --output-on-failure --no-tests=error
```

### 3. Пакування розширення Blender (`xray_blender`)

```powershell
python sources/tools/blender/build_extension.py
```

Згенерований архів `build/blender/extension/xray_blender-0.3.3.zip` встановлюється у Blender 5.3 через **Edit → Preferences → Get Extensions → Install from Disk**.

### 4. Автономне пакування SDK Runtime

Після збірки Release запустіть пакування з кореня репозиторію. Скрипт бере підготовлені SDK-асети з `build/sdk-assets` і записує дистрибутив у `build/sdk-runtime/release`.

```powershell
python sources/tools/sdk/package-sdk-runtime.py --config release --build-dir build/presets/windows-x64-release --output build/sdk-runtime/release
.\build\sdk-runtime\release\launch-sdk.cmd
```

`xray_avalonia_headless_smoke` перевіряє контролі й оболонку без видимого вікна. `xray_sdk_package_smoke` перевіряє склад пакета, аудит DLL-імпортів і запуск `stalker2.exe -editor` з Vulkan Validation. CMake додає тест пакета за наявності підготовлених SDK-асетів, DXC і бібліотеки Vulkan Validation.
