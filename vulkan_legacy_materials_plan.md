# План реалізації Legacy-властивостей моделей і матеріалів у Vulkan

Створено: 2026-11-23.

## Мета

Перенести до Vulkan-рендера X-Ray Engine 2.0 всі властивості моделей, матеріалів, скінінгу, LOD та рендер-конфігурації, які наразі заблоковані як `Legacy`, `SchemaOnly` або `Unsupported` у валідаторах та палітрі SDK, без втрати стабільності, коректності синхронізації Vulkan і сумісності з поточним workflow SDK.

Усі перелічені нижче можливості не мають принципових обмежень у Vulkan 1.2–1.4 і реалізуються поетапно поверх чинного конвеєра **HLSL → SPIR-V (DXC)**.

---

## Поточний стан архітектури

На момент створення плану Vulkan-бекенд уже має робочий фундамент:

- **Компіляція шейдерів:** HLSL-шейдери (`[[vk::binding(binding, space)]]`) компілюються у SPIR-V через `rhi::shader_compiler` (DXC).
- **Двопрохідний Light Pre-Pass / Deferred Material конвеєр** (`sources/xray/render/vulkan/sources/vulkan_gbuffer_renderer.cpp`, `sources/xray/render/vulkan/sources/vulkan_static_renderer.cpp`):
  - **Pass 1 (`sources/xray/render/vulkan/assets/static_gbuffer.hlsl`):**
    - `SV_Target0` (`r16g16b16a16_unorm`): `sqrt(view_z / far)` (`R`), **вільний канал `surface.w` (`G`)**, `roughness.xy` (`B, A`).
    - `SV_Target1` (`r16g16b16a16_float`): `normal.xy * 0.5 + 0.5` (`R, G`), `fresnel_at_0_degree` (`B`), `n.z > 0 ? 0 : 1` (`A`).
    - `SV_Target2` (`r8g8b8a8_unorm`): `tangent_cross.xyz * 0.5 + 0.5`, `1`.
    - Глибина/трафарет: `d24_s8_unorm` (`m_targets[3]`).
  - **Pass 2 (`sources/xray/render/vulkan/assets/deferred_lighting.hlsl`):**
    - Вже обчислює анізотропний BRDF (Beckmann / Szirmay-Kalos) за `tangent.xyz` (`SV_Target2`) та `roughness.xy` (`packed.zw`); канал `packed.y` (`output.depth.g`) наразі не використовується.
  - **Pass 3 (`sources/xray/render/vulkan/assets/static_material.hlsl`):**
    - `SV_Target0` (`albedo`), `SV_Target1` (`specular_color`), `SV_Target2` (`emission`) з глибиною `EQUAL` відносно `gbuffer_vs`.
  - **Додаткові проходи:** Forward (`static_forward.hlsl`), Тіні (`static_shadow.hlsl`), Декалі (`decals.hlsl`), Дисторсія (`distortion.hlsl` + `distortion_composite.hlsl`), Постпроцеси (`postprocess_settings.h`: Bloom, FXAA, TAA, SSAO, Distortion, MSAA).
- **Поточні валідатори та обмежувачі, що блокують Legacy-властивості:**
  - `sources/xray/render/vulkan/sources/vulkan_original_material.cpp` (`translate_g_stage`, `translate_forward_stage`, `translate_decals_stage`, `static_material`)
  - `sources/xray/render/vulkan/sources/vulkan_material_cook.cpp` (`load_material_asset`, `material_cook::load_bytes`)
  - `sources/xray/render/vulkan/material_resources.h` (`material_configuration`, `surface_material_parameters`, `surface_material_textures`, `material_asset_description`)
  - `sources/xray/render/vulkan/sources/vulkan_model_cook.cpp` та `sources/xray/render/vulkan/sources/vulkan_model_resources.cpp` (CPU-скінінг та ігнорування `lod_hierrarchy`)
  - `sources/xray/editor/base/SdkMaterialPalette.cs` (`EffectCapabilities`: 20 ефектів зі статусом `SchemaOnly`)
  - `sources/xray/editor/base/SdkRenderOptions.cs` (`Adapted(key)`: лише 2 опції з `resources/render/*.cfg`)

---

## Етап 1 (Пакет 1 — Швидкий пакет: розширення без змін базової архітектури Vulkan)

Мета етапу — розблокувати всі параметри `g_stage_default`, `forward_default` та `SdkRenderOptions`, що вкладаються у поточну модель рендера, дескриптори й G-Buffer формат.

### 1.1 Текстура світіння (`use_emissive_map` / `texture_emissive`)
- [x] Додати `resources::unmanaged_resource_ptr emissive;` у `surface_material_textures` та `std::string emissive_texture_name;` у `material_asset_description` (`sources/xray/render/vulkan/material_resources.h`).
- [x] Зняти обмеження `if (emission && emission_map)` у `load_material_asset` (`sources/xray/render/vulkan/sources/vulkan_material_cook.cpp`) і завантажувати `texture_emissive` (sRGB) у `material_cook::load_bytes`.
- [x] У `sources/xray/render/vulkan/sources/vulkan_static_renderer.cpp` (`prepare_material_shader`) та `sources/xray/render/vulkan/assets/static_material.hlsl` додати біт пермутації `USE_EMISSIVE_TEXTURE`, біндинг `[[vk::binding(2, 1)]] Texture2D<float4> emissive_texture` і множити `emissive.rgb * emissive_texture.Sample(material_sampler, uv).rgb` у `SV_Target2`.

### 1.2 Детальна альбедо-текстура (`use_tditail_diffuse` / `texture_ditail_diffuse`)
- [x] Розблокувати поля `use_tditail_diffuse` і `texture_ditail_diffuse` (зі збереженням оригінального написання в асетах) у `translate_g_stage` (`sources/xray/render/vulkan/sources/vulkan_original_material.cpp`).
- [x] Додати `detail_diffuse` у `surface_material_textures` та завантаження sRGB-текстури у `vulkan_material_cook.cpp`.
- [x] У `static_material.hlsl` додати `USE_DETAIL_DIFFUSE_TEXTURE`, біндинг текстури (`[[vk::binding(3, 1)]]`) та змішування з `albedo` (тайлінг через `detail_diffuse.xy`).

### 1.3 Анімація атласу текстур (`use_sequence` і `forward/forward_default_subuv`)
- [x] Прийняти в `vulkan_original_material.cpp` і `vulkan_material_cook.cpp` параметри `use_sequence`, `constant_sequence_array_width`, `constant_sequence_array_height`, `constant_sequence_play_speed`, `constant_sequence_start_frame_index`, а для `forward_default` — `use_subuv`.
- [x] Передавати час анімації матеріалів (`scene_runtime::material_phase()` -> `compute_sequence_uv()`) через `ObjectConstants.sequence_uv` (`static_gbuffer.hlsl`, `static_material.hlsl`) та `parameters.xy`/масштаб UV (`static_forward.hlsl`).
- [x] Реалізувати обчислення UV-клітинки атласу `uv * scale + offset` у піксельних шейдерах (`static_gbuffer.hlsl`, `static_material.hlsl`, `static_forward.hlsl`).
- [x] Перевести `forward/forward_default_subuv` у `sources/xray/editor/base/SdkMaterialPalette.cs` зі статусу `SchemaOnly` у `Supported`.

### 1.4 Варіативна маска (`use_variation_mask`, `texture_variation_mask`, `constant_variation_*`)
- [x] Пропустити `use_variation_mask`, `texture_variation_mask`, `constant_variation_color`, `constant_variation_multiply`, `constant_variation_position_devider`, `constant_variation_rotate`, `constant_variation_scale` у `vulkan_original_material.cpp` та `vulkan_material_cook.cpp`.
- [x] Передавати `world_pos` з `material_vs` у `material_ps` (`static_material.hlsl`) і семплити `variation_mask` за повернутими/масштабованими світовими координатами `world_pos.xz / divider` із модуляцією `albedo`.

### 1.5 Текстура Френеля (`use_fresnel_at_0_degree_texture` / `texture_fresnel_at_0_degree`)
- [x] Додати linear-текстуру `fresnel` у `material_resources.h` і `vulkan_material_cook.cpp`.
- [x] У `static_gbuffer.hlsl` (за `USE_FRESNEL_TEXTURE`) читати значення з текстури замість скаляра `surface.z` і записувати його в існуючий канал `output.normal.z` (без жодних змін у `deferred_lighting.hlsl`).

### 1.6 Карта напрямку анізотропії (`use_anisotropic_direction_texture` / `anisotropic_direction_texture`)
- [x] Додати linear-текстуру `anisotropy_dir` у `material_resources.h` і `vulkan_material_cook.cpp`.
- [x] У `static_gbuffer.hlsl` повернути дотичний вектор `t` у площині `(t, b)` за `anisotropy_dir_texture` перед обчисленням `output.tangent` (`SV_Target2`).

### 1.7 Множники, дифузний спад і тіні стадії
- [x] **`specular_color_multiplier`:** у `vulkan_original_material.cpp` та `vulkan_material_cook.cpp` множити `surface.specular_color` на `specular_color_multiplier` замість вимоги `{1,1,1,1}`.
- [x] **`is_cast_shadow` (метадані `g_stage`):** додати `bool cast_shadow = true;` у `material_configuration` (`material_resources.h`) і фільтрувати `casters` у `vulkan_scene_runtime.cpp`, якщо `cast_shadow == false`.
- [x] **`use_diffuse_attenuation` та `use_tdiffuse_power` (`texture_diffuse_power`, `constant_diffuse_*`):** передати коефіцієнти та карту степеня у `static_material.hlsl` / `extended_material_object_constants`.

### 1.8 Розширення мапінгу опцій у `SdkRenderOptions.cs` (`resources/render/*.cfg`)
- [x] Розширити `project_view_options`/`ViewportFlags` (`SdkViewport.cs`, `SdkIdePanels.cs`, `control_level.h`, `sdk_scene.h`) і `SdkRenderOptionsDocument.Adapted` (`SdkRenderOptions.cs`) для підтримки `r_enabled_post_process_stage`, `r_enabled_sun_stage`, `r_enabled_ambient_occlusion_stage` (SSAO), `r_enabled_distortion_stage` (Distortion), `r_enabled_fxaa` (FXAA) та `r_enabled_sun_shadows_stage` (SunShadows). *(Опції Bloom, TAA, MSAA і детальні налаштування каскадів тіней у `SdkRenderOptions.cs` залишаються на наступний крок.)*
- [x] Оновити статуси вказаних підтриманих ключів з `"Blocked"` на `"Adapted"` із синхронізацією у `SceneViewPanel`.

### 1.9 Критерії приймання Етапу 1
- [x] Успішні Debug/Release збірки `stalker2` та `xray_editor_world` (`0 Warning(s)`, `0 Error(s)` у керованому коді `EditorHost`).
- [x] Пакування Debug/Release (`sources/tools/sdk/package-sdk-runtime.py --clean`) і проходження живих запусків під `XRAY_VULKAN_REQUIRE_VALIDATION=1` (0 помилок валідації Vulkan).
- [ ] Перевірка в `SdkMaterialEditor` (`Apply preview` / `Save source`) для матеріалів із `texture_emissive`, `texture_ditail_diffuse`, `use_sequence`, `use_variation_mask`, `texture_fresnel_at_0_degree`, `anisotropic_direction_texture` *(потребує ручної інтерактивної перевірки з реальними асетами; автоматизовано пройдено живі запуски `InteractiveSDK` та `-ControlHostHarness`)*.
- [x] Збереження зворотної сумісності з `-ControlHostHarness` (`XRAY_SDK_PROPERTY_EDITOR=0`).

---

## Етап 2 (Пакет 2 — Розширення шейдерів, вільний канал G-Buffer і читання Depth-буфера)

Мета етапу — додати геометричні/процедурні ефекти поверхні (`POM`, `Triplanar`, `burning_wood`), підповерхневе просвічування через вільний канал `SV_Target0.g` та ефекти прозорих/м’яких forward-матеріалів із читанням depth-буфера.

### 2.1 Parallax Occlusion Mapping (`use_parallax`, `texture_bump`, `constant_parallax_scale`)
- [x] Розблокувати `use_parallax`, `texture_bump` (height-карта) та `constant_parallax_scale` у `vulkan_original_material.cpp` і `vulkan_material_cook.cpp`.
- [x] У `static_gbuffer.hlsl` обчислити `view_dir` у tangent-space (`float3x3(t, b, n)`), виконати ray-marching + лінійну/бінарну доводку по `texture_bump`, змістити `uv` перед семплюванням normal/roughness/alpha-test.
- [x] Узгодити POM-offset у `static_material.hlsl` (`USE_PARALLAX` із тією ж геометрією, 8-кроковим ray-marching і ідентичним `SV_Position` у `material_vs` для `EQUAL` depth test).

### 2.2 Triplanar / World-Space UV (`g_stage/g_stage_default_tiled`, `forward/forward_default_tiled`)
- [x] Додати `USE_TRIPLANAR` у `vulkan_static_renderer.cpp`, `static_gbuffer.hlsl`, `static_material.hlsl`, `static_forward.hlsl`.
- [x] У вершинному шейдері передавати `world_pos` і `world_normal`; у піксельному — обчислювати ваги трьох ортогональних проєкцій і змішувати вибірки текстур уздовж світових осей.
- [x] Перевести `g_stage/g_stage_default_tiled` та `forward/forward_default_tiled` у `SdkMaterialPalette.cs` у статус `Supported`.

### 2.3 Ефект горіння деревини (`g_stage/g_stage_burning_wood`)
- [x] Проаналізувати параметри `g_stage_burning_wood.effect` (`texture_whirl`, обвуглення, шорсткість та світіння жару).
- [x] Реалізувати варіант `USE_BURNING_WOOD` (+ `USE_WHIRL_TEXTURE`) для `static_gbuffer.hlsl` і `static_material.hlsl`: обвуглений `albedo`, підвищена шорсткість та HDR-емісія `SV_Target2` уздовж фронту горіння.
- [x] Перевести `g_stage/g_stage_burning_wood` у `SdkMaterialPalette.cs` у статус `Supported`.

### 2.4 Soft Particles і Fire Fresnel (`forward/forward_soft`, `forward/forward_fire_fresnel`, прапорці `forward_default`)
- [x] Зберігати `m_targets[3]` (`d24_s8_unorm`) у `VK_IMAGE_LAYOUT_DEPTH_STENCIL_READ_ONLY_OPTIMAL` під час forward-проходу (`vulkan_gbuffer_renderer.cpp`, `vulkan_render_passes.cpp` із `VK_ATTACHMENT_STORE_OP_NONE`) та перевіряти заборону `depth_write` у `vulkan_resource_operations.cpp`.
- [x] У `static_forward.hlsl` додати біндинг `Texture2D<float>` / `Texture2DMS<float, SAMPLE_COUNT>` `scene_depth` (`binding(3, 1)`), лінеаризацію depth через `projection_params` та `soft_factor` для `forward/forward_soft` (`use_soft_test`).
- [x] Додати `pow(saturate(1.0 - abs(dot(N, V))), 2.0)` у `static_forward.hlsl` для `forward/forward_fire_fresnel` (`use_fire_fresnel`).
- [x] Розблокувати в `translate_forward_stage` і `static_forward.hlsl` параметри `use_ttransparency` (`texture_transparency`), `use_alpha_test`, `use_Color_Power`, а також `blend_mode` `Modulate`/`Subtractive` і HDR `constant_emissive_multiplier`.
- [x] Перевести `forward/forward_soft` і `forward/forward_fire_fresnel` у `SdkMaterialPalette.cs` у статус `Supported`.

### 2.5 Освітлений дим і об’ємні конуси/сфери (`forward_smoke_custom_lighted`, `forward_volume_cone_base`, `forward_volume_sphere_base`)
- [x] Передати напрямок/колір основного джерела світла та ambient у `extended_forward_object_constants` (`vulkan_scene_runtime.cpp`, `vulkan_static_renderer.cpp`, `static_forward.hlsl`) для `forward/forward_smoke_custom_lighted` (`use_lighting`, wrap-освітлення).
- [x] Реалізувати шейдерні варіанти `USE_VOLUME_CONE` та `USE_VOLUME_SPHERE` у `static_forward.hlsl` (радіальна/view-angle атенюація, прокрутка UV `move_direction * phase`, `uv_tile` і м’яке згасання по глибині) для `forward/forward_volume_cone_base` і `forward/forward_volume_sphere_base`.
- [x] Перевести всі три ефекти у `SdkMaterialPalette.cs` у статус `Supported`.

### 2.6 Просвічування (`use_ttranslucency` / `constant_translucency`) та `alpha_model_parameter`
- [x] Задіяти вільну точність каналу **`output.depth.g` (`SV_Target0.y`, `surface.w`)** у `static_gbuffer.hlsl` (`surface.w + translucency * 0.10`, зі збереженням receiver-маски декалей) та `output.tangent.a` для `alpha_model_parameter`.
- [x] У `deferred_lighting.hlsl` декодувати `translucency` і `alpha_model_parameter` та застосовувати wrap/back-scattering і параметризований знаменник BRDF.
- [ ] При потребі напівпрозорої геометрії в `g_stage` реалізувати dithered alpha-test (screen-door transparency) або автоматичне перенаправлення до forward-черги *(оскільки `g_stage` підтримує alpha-test через `use_alpha_test`, окремий dithered mode не додавався)*.

### 2.7 Критерії приймання Етапу 2
- [x] Відсутність READ-AFTER-WRITE та layout-transition попереджень Vulkan Synchronization Validation під час `InteractiveSDK` і `-ControlHostHarness` із `XRAY_VULKAN_REQUIRE_VALIDATION=1` (`ExitCode=0`, 0 помилок валідації Vulkan; рендер на реальному `forward_soft`/MSAA асеті залишається для візуальної перевірки).
- [x] Підтримка `msaa_samples = 1` і `msaa_samples > 1` (`Texture2DMS<float, SAMPLE_COUNT>` у `static_forward.hlsl`, `VK_IMAGE_LAYOUT_DEPTH_STENCIL_READ_ONLY_OPTIMAL` + `VK_ATTACHMENT_STORE_OP_NONE`).
- [ ] Візуальна перевірка `use_parallax`, `g_stage_default_tiled`, `g_stage_burning_wood`, `forward_soft`, `forward_fire_fresnel` та `use_ttranslucency` у SDK viewport і Model Preview *(потребує інтерактивної перевірки вручну з реальними асетами)*.

---

## Етап 3 (Пакет 3 — Архітектурний пакет Vulkan 1.2–1.4)

Мета етапу — прибрати архітектурні обмеження фіксованих слотів текстур, одностадійних матеріалів і CPU-скінінгу, відкривши підтримку решти модельних і шейдерних можливостей.

### 3.1 Bindless Descriptor Indexing (`VK_EXT_descriptor_indexing`) + Відбиття (`use_reflection` / `texture_cubemap`)
- [x] Увімкнути у `vulkan_backend.cpp` можливості Vulkan 1.2: `descriptorIndexing`, `shaderSampledImageArrayNonUniformIndexing`, `descriptorBindingPartiallyBound`, `descriptorBindingSampledImageUpdateAfterBind`.
- [x] Створити глобальні bindless-масиви для `Texture2D[1024]` і `TextureCube[128]` у Set 3 (`space3`) та передавати індекси текстур матеріалу через `ObjectConstants` / `DecalConstants` (`NonUniformResourceIndex`).
- [x] Реалізувати локальні/матеріальні кубмапи відбиття: розблокувати `use_reflection`, `use_reflection_diffuse`, `use_reflection_mask`, `texture_cubemap`, `texture_cubemap_mask`, `min_reflection_angle`, `reflection_power` у `g_stage_default`, `forward_default` та `decals_default` (`vulkan_decal_renderer.cpp`, `decals.hlsl`, `static_material.hlsl`, `static_forward.hlsl`).

### 3.2 Багатостадійні матеріали (`multi-stage`) та `forward/forward_decal`
- [x] Зняти вимогу виключно єдиної стадії у `vulkan_original_material.cpp` і `vulkan_material_cook.cpp`.
- [x] Зберігати в `material_resource` бітову маску активних стадій (`stages_mask` / `material_stage_bits`), щоб один сабмеш міг ставати у відповідні черги малювання кількох проходів.
- [x] Додати forward-чергу для `forward/forward_decal` (depth bias `-2/-2`, `less_equal` depth test, опціональна кубмап-рефлексія) і перевести ефект у `Supported` в `SdkMaterialPalette.cs`.

### 3.3 Підповерхневе розсіювання шкіри та органіки (`lighting/lighting_organic_base`, `lighting/lighting_skin_base`)
- [x] Виділити кодування профілю освітлення в G-Buffer (`output.normal.w`: `Standard` `0/1`, `Skin` `-1/2`, `Organic` `-2/3`) зі збереженням умови `normal.w > 0.5 ? -1 : 1`.
- [x] Інтегрувати **Pre-Integrated Skin / Organic SSS** у `deferred_lighting.hlsl` та bindless-карти `subdermal` / `ao` у `static_material.hlsl`.
- [x] Перевести `lighting/lighting_organic_base`, `lighting/lighting_skin_base` та `lighting/lighting_default` у `Supported` в `SdkMaterialPalette.cs`. `lighting/lighting_default` задає стандартний профіль відкладеного освітлення без підповерхневого розсіювання (`sss_profile_type::none`).

### 3.4 GPU Compute Skinning на SSBO (`mt_skinned_submesh_4w`)
- [x] Замінити по-кадровий CPU-скінінг `update_skeleton` на Vulkan Compute Shader (`skinning.comp.hlsl`).
- [x] Зберігати bind-pose вершини у `StructuredBuffer` (`gpu_skeletal_vertex`, 88B), матриці кісток за кадр у `SkinningConstants`, а вихід — у вершинному буфері `buffer_type::structured` із прапорцями `VK_BUFFER_USAGE_STORAGE_BUFFER_BIT | VK_BUFFER_USAGE_VERTEX_BUFFER_BIT` (плюс $O(\text{bones})$ оновлення AABB).
- [x] Вставити бар’єр `COMPUTE_SHADER_BIT` → `VERTEX_ATTRIBUTE_READ_BIT` перед рендером геометрії.

### 3.5 Runtime LOD-перемикання (`lod_hierrarchy`) і `SPIRV-Reflect`
- [x] Додати парсинг `lod_hierrarchy` (`LOD0`/`LOD1`/`LOD2` із `settings`) у `vulkan_model_cook.cpp` і вибирати активний LOD за екранним покриттям AABB у `vulkan_scene_runtime.cpp`.
- [x] Розширити `SPIRV-Reflect` (`material_contract.cpp`, `spirv_entry.h`) для валідації bindless Set 3, `OpAccessChain`/`OpCopyObject` та compute-шейдерів.

### 3.6 Критерії приймання Етапу 3
- [x] Повний прогін `XRAY_VULKAN_REQUIRE_VALIDATION=1` (`InteractiveSDK` та `ControlHostHarness` з `actor=1 clips=2 decals=1`, Vulkan 1.2 bindless enabled, `ExitCode=0`, 0 помилок валідації Vulkan і 0 завислих процесів `stalker2.exe`; інтерактивна перевірка довільних багатостадійних/SSS-асетів у Model Preview залишається для візуального огляду).
- [ ] Заміри часу кадру та CPU-навантаження для скелетних і високонавантажених сцен у порівнянні з поточним baseline CPU-скінінгу *(бенчмарк FPS/профілювання вручну не проводився)*.

---

## Системні `SchemaOnly`-ефекти (поза обсягом матеріалів моделей)

Ці 7 ефектів із `SdkMaterialPalette.cs` відповідають фіксованим внутрішнім проходам DX10/11-рендера X-Ray 2.0. У Vulkan їхні функції виконують системні шейдери.

| Системний ефект | Де реалізовано у Vulkan | Статус у палітрі |
|---|---|---|
| `forward/sky_default` | `assets/skybox.hlsl` + `vulkan_sky_renderer.cpp` | Залишити `SchemaOnly` |
| `distortion/post_process_distortion` | `assets/distortion_composite.hlsl` | Залишити `SchemaOnly` |
| `post_process/post_process_blend_texture` | Системні постпроходи (`vulkan_screen_effects.cpp`) | Залишити `SchemaOnly` |
| `post_process/post_process_terrain_debug` | `assets/terrain_unlit.hlsl` | Залишити `SchemaOnly` |
| `debug_post_process/debug_post_process_terrain_debug` | `assets/terrain_unlit.hlsl` / debug-прохід | Залишити `SchemaOnly` |
| `debug/debug_editor_wireframe` | `assets/debug_primitives.hlsl` + `vulkan_debug_renderer.cpp` | Залишити `SchemaOnly` |
| `shadow/shadow_accumulate` | `assets/static_shadow.hlsl` + `vulkan_scene_shadows.cpp` | Залишити `SchemaOnly` |

---

## Ризики та інженерні правила реалізації

1. **Вибух пермутацій шейдерів:** у `vulkan_static_renderer.cpp` вже використовуються бітові варіанти `m_gbuffer_ps` (`2^5`) та `m_material_ps` (`2^3`). До переходу на Bindless (Етап 3.1) додавання понад 6–7 комбінаторних `#define`-прапорців слід уникати — натомість використовувати bitmask у `ObjectConstants` (`material_flags`) для недорогих гілок або окремі спеціалізовані entry-point варіанти (`burning_wood`, `triplanar`).
2. **Узгодженість глибини двопрохідного Light Pre-Pass:** будь-які зміни положення пікселів чи `clip()` (альфа-тест, POM у `static_gbuffer.hlsl`) повинні давати ідентичну глибину/відсікання в `static_material.hlsl`, оскільки material pass малюється з тестом глибини `EQUAL`.
3. **Сумісність наявних асетів:** усі нові поля в `vulkan_original_material.cpp` та `vulkan_material_cook.cpp` мають бути опціональними з безпечними значеннями за замовчуванням, щоб існуючі cooked-матеріали та `ControlHostHarness` працювали без змін.
4. **Обов’язкова верифікація після кожного етапу:** збірка Debug + Release з `0` керованих попереджень, пакування `package-sdk-runtime.py --clean` та живий запуск під `DISABLE_RTSS_LAYER=1`, `DISABLE_VULKAN_OBS_CAPTURE=1`, `XRAY_VULKAN_REQUIRE_VALIDATION=1`.
