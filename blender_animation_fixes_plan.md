# План виправлення проблемних анімацій під час міграції Maya → Blender

Створено: 2026-11-23.

> [!NOTE]
> Проєктний план і звіт про виконання (2026-11-23). Основну реалізацію виконано й перевірено на реальному корпусі `C:/Stalker2_2011` (read-only) в окремих каталогах `build/blender-anim-fixes-validation/` без зміни оригіналів та без перезапису прийнятого B5 checkpoint у `build/blender-b5/migration`.

---

## Результати виконання (2026-11-23)

- **Zero-root `.clip` (`navgation_controller`):** без `--normalize-zero-root-scale` `idle.clip` і далі відхиляється (`failed: 1`, exit `2`); із `--normalize-zero-root-scale` усі **23/23** кліпи мігровані, переекспортовані, переварені та пройшли перевірку відтворення (`engine_verified_clips: 23`, exit `0`, `build/blender-anim-fixes-validation/zero-root-fixed/`).
- **Ліміт кісток `80 → 128` (`127` authored + `object_movement`):** синхронізовано у Vulkan (`skeletal_resources.h`, `vulkan_skeletal_resources.cpp`, `skinning.comp.hlsl`, `vulkan_static_renderer.cpp`, `update_skeleton_command.h`), `maya_cook`, `dcc_common`, `xray_blender` і Maya-плагіні; додано `--clip-samples` верифікатор B-spline + матриць обертання у `xray_asset_cook_cli`. `skinning.comp.hlsl` скомпільовано у SPIR-V через `dxc.exe` (0 помилок); Release і Debug збірки `xray_asset_cook_cli`, `xray_maya_cook_cli` та `libxray_render_vulkan.dll` пройшли; скіни `werewolf` (`bones=111`) і `krovosos` (`bones=92`) зварені у raw + Vulkan converted (`rc=0`, `build/blender-anim-fixes-validation/cook-128-check/`).
- **Монстри >80 кісток, `skeleton_man.mb`, multi-rig, Trax `.ma`:** у `build/blender-anim-fixes-validation/maya-fixes/` **9/9** (`failed: 0`, `reopen_verified: True`, `source_unchanged: True`): `werewolf/monster_pose.mb` (111 кісток, 13 playback PASS), `werewolf/run.mb` (111 кісток, 11 playback PASS), `krovosos_attack_1/2.mb` (93/92 кістки, 20+20 playback PASS), `skeleton_man.mb` (вилучено 12 дубльованих незважених листових `'none'`), `sit_middle_noweap_out.mb` і `crouch_walk_trans_back_left_to_fwd_idle_1.mb` (8 і 51 playback PASS без помилок скін-матчингу), `test_clip.ma` (63 `exact` канали, 551 кадр, playback PASS) і `test_clip_crouch_walk_move_fwd_aim_1.ma` (165 `exact` каналів, 41 кадр, playback PASS).
- **Усі сцени `krovosos_*.mb`:** у `build/blender-anim-fixes-validation/krovosos-all/` **14/14** мігровано (`failed: 0`, `reopen_verified: True`, **289/289** перевірених треків `PASS`, 0 попереджень про ліміт 80 кісток, `source_unchanged: True`).
- **Виправлення прив’язки скіна (`bindPreMatrix` + `envelope=0`) та фільтрація прихованої геометрії/рігів:** усунено розрив між скелетом і тілом та «шипи» сітки в сценах, збережених не на bind-кадрі (зокрема `krovosos_attack_2.mb`, збережений на кадрі 150 при `Z = 884.5 см`, де `dagPose -restore -bindPose` падав із `Pose not achieved` через expressions/IK):
  - у `sources/tools/maya/plugin/skeletal_extractor.py` та `sources/tools/maya/plugin/static_extractor.py` bind-матриці кісток читаються напряму з `skinCluster.bindPreMatrix[logicalIndex].inverse()` та `geomMatrix`, а недеформована bind-геометрія екстрагується за тимчасового `skinCluster.envelope = 0.0` (похибка реконструкції деформованої сітки Maya vs Blender ≤ `3.05e-5 см`);
  - у `sources/tools/maya/plugin/migrate_scene.py` додано `_is_dag_visible()` (перевірка `.visibility`, `overrideVisibility` та `displayLayer.visibility` уздовж DAG-предків), що відфільтровує приховану референсну скульптуру анатомічного скелета людини (`|skeleton|skeletonShape`, 617 833 вершини) та 18 прихованих незважених IK/FK helper-рігів (`FkBn_*`, `IkBn_*`, `bnDinamic_*`).
- **Maya Animation Layer (`layer_pose_wall_back.mb`):** через `--anim-layer-base xr2-dsgn/resources/sources/scenes/character/human/neutral/neutral_01/temp/neutral_01_rigging_02.mb` у `build/blender-anim-fixes-validation/anim-layer/` поза запечена й мігрована (`reopen_verified: True`, `engine_playback: PASS`, `source_unchanged: True`).
- **Extension ZIP:** зібрано `build/blender/extension/xray_blender-0.3.3.zip` (`80164` байтів, SHA-256 `7bea78a99ae2b0f0a09e831746c069a65c3b7d993b1cbba13249d195e9f6e4f`); `xray_blender-0.3.2.zip` (`f2e4fa7ddd952f3b70b33cf61624b0d79e6565a4a33f6d5ad0e0b79d52baa082`) та `build/blender-b5/migration` залишилися незмінними.

---

## Мета

Відновити та перенести до Blender (з наскрізною перевіркою через `xray_asset_cook_cli`, `xray_animation_math_engine_contract_tests` та Vulkan-рендер) проблемні анімаційні й скелетні ресурси, які залишилися у статусі `failed` або були імпортовані частково (`with_warnings`) за підсумками повного прогону B5 (`build/blender-b5/migration/migration-manifest.json`), зі збереженням зворотної сумісності, незмінності оригіналів і прозорої діагностики.

---

## Поточна діагностика проблемних активів

| Категорія | Статус у B5 | Кількість / приклади файлів | Причина відмови або попередження |
| --- | --- | --- | --- |
| Навігаційні `.clip` з `Root.scale=(0,0,0)` | `failed` | **23** кліпи в `xr2-dsgn/resources/tests/rtp/navgation_controller/animation/test_animations/*.clip` | `Root` має `scale = (0,0,0)`, `T = 0`, `R ≤ 9.76e-6`; реальний рух лежить в `object_movement`, дочірні кістки валідні |
| Монстри понад ліміт `79 + 1 = 80` кісток | `failed` / `with_warnings` | **2** `failed`: `s2_2011_documentation/STA2-docarts/docarts/textures/monster/werewolf/monster_pose.mb`, `run.mb` (**111 кісток**); **15–16** `with_warnings`: `xr2-dsgn/resources/sources/scenes/character/monster/animation/krovosos_*.mb` (**92–93 кістки**) | Жорстке обмеження `80` (`79` authored + `object_movement`) у Vulkan skinning, cooker та DCC-плагінах; у `krovosos` основний скелет відкидається, залишаються дрібні супутні скелети |
| Maya Trax Editor `.ma` кліпи | `failed` | **2** файли: `xr2-dsgn/resources/sources/scenes/character/human/neutral/neutral_01_novice/temp/test_clip.ma`, `test_clip_crouch_walk_move_fwd_aim_1.ma` | Standalone `clipLibrary` + `animClip` + `animCurveTA` без ієрархії `joint` і bind pose у файлі |
| Дублікат службової кістки `'none'` | `failed` | **1** файл: `xr2-dsgn/resources/sources/scenes/character/human/skeleton_man.mb` | Кілька кісток з однаковим коротким ім'ям `'none'` у гілці `|Root` (`|Root: duplicate skeleton bone 'none'`) |
| Multi-rig сцени з неоднозначним скіном | `with_warnings` | Сцени в `camp/` (`sit_middle_noweap_out.mb`, `sit_high_guitar_idle.mb`) та `common_anim_slot_3/**/temp/` (`crouch_walk_trans_*`, `run_move_fwd_aim_1_fix.mb`) | Зіставлення `skinCluster` за множиною коротких імен знаходить `>1` однаковий скелет (`Skinned mesh does not identify one skeleton`) |
| Окремий Maya Animation Layer | `failed` (опційно) | **1** файл: `xr2-dsgn/resources/sources/scenes/character/human/animation/camp/temp/layer_pose_wall_back.mb` | Містить лише `AnimLayer1` із кривими на IK-контролерах (`Right_Feet_control_*`, `Hip_control_*`, `Chest_control_*`) без скелета |

> [!TIP]
> **Поза межами анімацій (довідково):** 5 сцен локацій/об’єктів (`niichas1.mb`, `niichas2.mb`, `niichas3.mb`, `tower_electrical.mb`, `halogen_office.mb`) падають у Maya 2027 з `0xC0000005` (найімовірніше через видалені ноди Mental Ray `Mayatomr`). Їхнє відновлення — окрема R&D-гілка (`Import` / `File Reference` + видалення `unknown`-нод, проміжна Maya 2016–2018 → `.ma`, або IFF `FOR4`-парсер).

---

## Етап 1. Нормалізація 23 legacy-кліпів із `Root.scale = (0, 0, 0)`

**Технічне обґрунтування:** у рушії (`bone_matrices_computer::compute_bones_matrices`, `sources/xray/animation/sources/bone_matrices_computer.cpp`, рядки 237–249) матриця кістки `Root` не обчислюється — дочірнім кісткам передається `float4x4().identity()`, а переміщення об’єкта (`object_movement`) обробляється окремо в `get_object_transform()`. У пізнішому Maya-експортері (`sources/xray/maya/sources/anim_export_util.cpp`, рядки 804–813) у `Root` почали записувати константу `zero` зі `scale = (1, 1, 1)`. Отже, `Root.scale = (0, 0, 0)` у 23 тестових кліпах є дефектом раннього експортера і на позу скелета в рушії не впливає.

- [x] Додати явну opt-in опцію: прапорець `--normalize-zero-root-scale` у `sources/tools/blender/migrate_legacy.py` і параметр `normalize_zero_root_scale=False` у `decode_clip_to_exchange` (`sources/tools/dcc_common/legacy_codec.py`), щоб типова політика `retain_failed` залишалася незмінною за замовчуванням.
- [x] Дозволяти нормалізацію `Root → (T=0, R=0, S=(1,1,1))` **лише** за одночасного виконання всіх умов:
  1. кістка є кореневою під `object_movement` (`Root`);
  2. `|T| <= precision_t / 2` та `|R| <= precision_r / 2` на всіх кадрах;
  3. `S == (0, 0, 0)` на всіх кадрах як у `.clip`, так і в `.clip_check_data` (за наявності companion);
  4. усі дочірні кістки мають валідний додатний масштаб.
- [x] Записувати у `import_metadata` та `migration-manifest.json` (`source_diagnostics` / `import_warnings`) факт нормалізації (`legacy_zero_root_scale_normalized`) і SHA-256 вихідних файлів без модифікації `C:/Stalker2_2011`.
- [x] Перевірено на реальному корпусі: без прапорця `idle.clip` і далі відхиляється (exit `2`); з `--normalize-zero-root-scale` усі **23/23** кліпи проходять `import → export → cook → playback` у межах source precision (`0.005`). *(Окремий CTest unit-test у `xray_blender_migration`/`xray_blender_import` у цьому checkout не присутній.)*

---

## Етап 2. Зняття обмеження 80 кісток для `krovosos` (93) та `werewolf` (111)

**Технічне обґрунтування:** бінарний формат скінінгу вже зберігає індекси кісток у `u16`. Ліміт `80` походить від застарілих constant buffers DX9/DX10. У Vulkan мінімально гарантований `maxUniformBufferRange` становить 16 384 байти:
- **Варіант A (обрано й реалізовано):** підняти ліміт до **128** (`128 * 64 + 16 = 8208` байт ≤ 16 KB, покриває `krovosos` 93 і `werewolf` 111 із запасом без зміни дескрипторів RHI).
- **Варіант B (на майбутнє за потреби >128):** перевести `bones` у `sources/xray/render/vulkan/assets/skinning.comp.hlsl` із `cbuffer` на `StructuredBuffer<row_major float4x4>` (SSBO) та підняти `skeletal_bone_limit` до **256** (`255` authored + `object_movement`).

- [x] Обрати та синхронізувати новий ліміт кісток (`128`) у всіх шарах рушія, cooker і DCC-інструментів:
  - `sources/xray/render/vulkan/skeletal_resources.h` (`skeletal_bone_limit = 128`)
  - `sources/xray/render/vulkan/sources/vulkan_skeletal_resources.cpp` (перевірки та повідомлення `1..128`)
  - `sources/xray/render/vulkan/assets/skinning.comp.hlsl` (`bones[128]`, `min(bi[i], 127u)`)
  - `sources/xray/render/vulkan/sources/vulkan_static_renderer.cpp` (`gpu_skinning_constants` == `8208` байт, clamp `128`)
  - `sources/xray/render/facade/sources/update_skeleton_command.h` (`m_matrices[128]`)
  - `sources/xray/maya_cook/sources/exchange_schema.cpp`, `animation_cooker.cpp`, `skinned_model_cooker.cpp`
  - `sources/tools/dcc_common/exchange.py` (`MAX_BONES = 128`), `legacy_codec.py`, `ma_reader.py`
  - `sources/tools/blender/xray_blender/exporters.py`, `importers.py`
  - `sources/tools/maya/plugin/skeletal_extractor.py`, `migrate_scene.py`
- [x] Перевірити `sources/xray/render/vulkan/sources/material_contract.cpp` та `vulkan_smoke_materials.cpp` (80 у цих файлах стосувалося лічильника кадрів / ABI-розміру іншого буфера, не ліміту кісток; змін не потребувало).
- [x] У `sources/tools/maya/plugin/migrate_scene.py` додати опційне відсіювання незважених кінцевих helper-кісток через `skinCluster(q=True, weightedInfluence=True)` при перевищенні `127` кісток (зберігаючи всі зважені кістки та їхніх DAG-предків).
- [x] Перепакувати Blender extension ZIP (версія `0.3.3` через `sources/tools/blender/build_extension.py`), не перезаписуючи прийнятий `0.3.2`.

---

## Етап 3. Усунення помилок Maya-екстракції (`skeleton_man.mb` і multi-rig скін-матчинг)

- [x] **`skeleton_man.mb` (`duplicate skeleton bone 'none'`):**
  - У `sources/tools/maya/plugin/migrate_scene.py` додано фільтрацію дубльованих незважених листових `'none'`-кісток (перевірено на `skeleton_man.mb`: вилучено 12 листових `'none'`, міграція та reopen PASS).
- [x] **Multi-rig скін-матчинг (`Skinned mesh does not identify one skeleton`):**
  - У `sources/tools/maya/plugin/migrate_scene.py` зіставлення скіна переведено на однозначний матчинг за повними DAG-шляхами `skinCluster` influences до відповідного кореня `root` (`rig_XXX`), із fallback на `families`. Перевірено на `sit_middle_noweap_out.mb` і `crouch_walk_trans_back_left_to_fwd_idle_1.mb` — помилки скін-матчингу відсутні.
- [x] **Точне відновлення bind pose (`skinCluster.bindPreMatrix` + `envelope=0`) та фільтрація прихованих DAG-вузлів:**
  - У `sources/tools/maya/plugin/skeletal_extractor.py` та `sources/tools/maya/plugin/static_extractor.py` додано `_skin_cluster_bind_info()` та `mesh_xform_overrides`: замість крихкого `dagPose -restore -bindPose` (який частково відмовляв на сценах із IK/expressions на ненульовому кадрі таймлайну, спричиняючи відрив скелета від сітки та спайки вершин) інвертується `skinCluster.bindPreMatrix[logicalIndex]` для кожної кістки впливу, береться `geomMatrix` скін-кластера, а сітка читається при `skinCluster.envelope = 0.0`.
  - У `sources/tools/maya/plugin/migrate_scene.py` додано `_is_dag_visible()` (перевірка `.visibility`, `overrideEnabled`/`overrideVisibility` і `displayLayer.visibility`), щоб не експортувати приховані референсні меші (`|skeleton|skeletonShape`) і приховані незважені допоміжні IK/FK-ланцюги.

---

## Етап 4. Імпорт Maya Trax Editor `.ma` кліпів (`test_clip.ma`, `test_clip_crouch_walk_move_fwd_aim_1.ma`)

- [x] У `sources/tools/dcc_common/ma_reader.py` реалізовано розпізнавання standalone Trax-кліпів (`clipLibrary` + `animClip` + `animCurveT*`), обчислення Maya Auto/Plateau tangents (`tan` 9/10) без збережених векторів та коректне читання `-type "string"` атрибутів (`"64x64 64 64 1.0"`).
- [x] Підтримано передачу цільової арматури / `skeleton_bundle` у `sources/tools/blender/xray_blender/importers.py` та автоматичний підбір сумісної `.skeleton`-сім’ї у `sources/tools/blender/migrate_legacy.py`.
- [ ] Fallback-маршрут через `mayapy` (`Bake Simulation`) не знадобився, оскільки обидва реальні `.ma` файли безпосередньо декодувалися по маршруту `C1` без Maya.
- [x] Перевірено обидва реальні файли (`test_clip.ma` — 63 `exact` канали; `test_clip_crouch_walk_move_fwd_aim_1.ma` — 165 `exact` каналів) у повному ланцюжку `decode_ma_to_exchange → Blender Action → re-export → cook → playback → reopen`.

---

## Етап 5. Опційне відновлення Maya Animation Layer (`layer_pose_wall_back.mb`)

> [!IMPORTANT]
> Виконується лише за наявності в корпусі `C:/Stalker2_2011` сумісної вихідної сцени з відповідним human control rig (`Right_Feet_control`, `Hip_control`, `Chest_control`, `Left_Arm_control`, `Left_Knee_control`).

- [x] Знайдено сумісні базові сцени human control rig у `C:/Stalker2_2011/xr2-dsgn/resources/sources/scenes/character/human/` (зокрема `neutral/neutral_01/temp/neutral_01_rigging_02.mb`).
- [x] Додано `--anim-layer-base` у `sources/tools/blender/migrate_legacy.py` і підтримку запікання/семплювання `AnimLayer1` поверх базового ріга у `sources/tools/maya/plugin/migrate_scene.py`; перевірено на `layer_pose_wall_back.mb` (міграція, cook, `engine_playback: PASS`, `reopen_verified: True`).
- [x] Гілка `wont_fix` не знадобилася, оскільки `AnimLayer1` успішно підключився до `neutral_01_rigging_02.mb` і дав реальну позу.

---

## Етап 6. Верифікація, регресійні тести та контрольний прогін міграції

Усі перевірки проводяться в **окремому новому output-каталозі** (`build/blender-anim-fixes-validation/`), не зачіпаючи прийнятий `build/blender-b5/migration` та вихідний корпус `C:/Stalker2_2011`.

- [x] Перебудовано Debug і Release цілі `xray_asset_cook_cli`, `xray_maya_cook_cli`, `xray_render_vulkan` (додано `--clip-samples` у `xray_asset_cook_cli`), а також скомпільовано `skinning.comp.hlsl` у SPIR-V через `dxc.exe`. *(У цьому checkout цілі `xray_animation_math_engine_contract_tests` і `xray_engine` не перебудовувалися.)*
- [ ] Прогнати CI-пакет Blender/Maya (`sources/tools/blender/run_ci.py` відсутній у поточному дереві вихідних кодів).
- [x] Виконано таргетовані прогони `migrate_legacy.py` (`--include`) для кожної групи виправлень:
  1. усі **23/23** кліпи `navgation_controller` (із `--normalize-zero-root-scale`);
  2. `werewolf/monster_pose.mb`, `werewolf/run.mb` (111 кісток) і всі **14/14** сцен `krovosos_*.mb` (**289/289** треків PASS);
  3. `skeleton_man.mb`, `test_clip.ma`, `test_clip_crouch_walk_move_fwd_aim_1.ma`, `layer_pose_wall_back.mb`;
  4. multi-rig анімаційні сцени (`sit_middle_noweap_out.mb`, `crouch_walk_trans_back_left_to_fwd_idle_1.mb`).
- [x] Перевірено компіляцію `skinning.comp.hlsl` (128 кісток) через `dxc.exe` та варіння 111-кісткового `werewolf` і 92-кісткового `krovosos` у Vulkan converted формат через `xray_asset_cook_cli.exe`.
- [ ] Інтерактивний живий запуск GPU-скінінгу у Vulkan під `XRAY_VULKAN_REQUIRE_VALIDATION=1` для моделі > 80 кісток (`krovosos` / `werewolf`).
- [x] Перевірено незмінність вихідного корпусу (`source_unchanged: True` у всіх маніфестах), незмінність `build/blender-b5/migration` та `xray_blender-0.3.2.zip`, і `reopen_verified: True` кожного нового `.blend` у Blender 5.3.0.

---

## Ризики та обмеження

- **Зворотна сумісність із DX9/DX10:** застарілі утиліти або шейдери з жорстким буфером на 80 кісток не зможуть завантажити 93/111-кісткові моделі; розширення ліміту стосується сучасного конвеєра Vulkan / Blender / SDK.
- **Прозорість нормалізації `Root.scale`:** підміна `(0,0,0) → (1,1,1)` змінює сирі дані відносно оригінального `.clip` (хоча математично еквівалентна runtime X-Ray 2.0), тому повинна вмикатися лише явним прапорцем із записом у manifest.
- **Залежність `layer_pose_wall_back.mb` від control rig:** без ідентичного авторського control rig 2011 року прямий імпорт у Blender неможливий.
- **Креші локацій (`0xC0000005`):** 5 бінарних `.mb` сцен локацій/об’єктів не є анімаційними і залишаються окремою задачею відновлення.
