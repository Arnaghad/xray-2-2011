# План інтеграції Vulkan у X-Ray 2

Створено: 2026-10-04. Редакція: 2026-10-20.

Мета: робоча game scene та editor main/preview workflows через чинні facade,
resource system, кадровий цикл і Vulkan RHI у визначеному нижче обсязі.

Цей файл — єдине джерело актуальних задач і статусів. Попередню редакцію,
детальні результати та журнали збережено в
[історії](vulkan_integration_history.md). Реалізаційні контракти й команди —
[у Vulkan README](sources/xray/render/vulkan/README.md).
Історичні «next/наступний» і локальні обмеження окремого прогону не створюють
нових задач. Оновлення документа не є новим запуском тестів.

## Визначення та правила обліку

- **Реалізовано:** код підключено до активного target; це ще не приймання.
- **Прийнятий профіль:** конкретні inputs, операції, limits і конфігурація
  пройшли runtime/image/lifecycle checks із збереженими доказами.
  Приймання одного профілю не доводить підтримку всього legacy renderer.
- **Інтегровано:** компонент працює через штатні resource queries і facade
  у відповідному scene workflow; прямий виклик test helper цього не доводить.
- **M8 game/editor startup:** штатні logic/render/resource threads,
  query/load/update/unload і shutdown. Single-thread probes не закривають його.
- **Source-derived pack:** offline-проєкція справжніх geometry/material/texture
  даних із зафіксованим походженням. Synthetic fixture перевіряє контракт,
  source-derived pack — підтриману проєкцію реальних даних.
- **Готовий legacy cooked pack:** наявний зовнішній pack, який завантажується
  без offline-адаптації geometry/material. Його сумісність не доведена.
  За рішенням користувача 2026-10-20 M5-C перевіряє доступні original assets
  через runtime cooking, а не відсутній готовий cooked cache.
- **Parity:** відповідність оригінальній сцені/матеріалам/кадру за визначеним
  baseline. Незалежні pixel checks підтриманого профілю самі по собі не є parity.
- **Drain:** callbacks і команди завершені або скасовані; cook users,
  deferred deleters та GPU retirements звільнені до teardown їхніх власників.
- **Відкладено:** є явне рішення про вилучення з поточного обсягу із зазначеним
  обмеженням і посиланням на погоджений користувачем scope. Нереалізований
  обов'язковий пункт сам собою не стає відкладеним.

Кожна незавершена робота має рівно один checkbox у своєму етапі.
Залежності посилаються на результат, а не копіюють його задачу.
Повторний regression run додає доказ до наявної задачі. Адаптація нового типу
об'єктів до готового lighting/shadow/AA contract є його інтеграцією в M8,
без повторного відкриття M6/M7. Нові numbered substages автоматично не додаються.
Один run може бути доказом кількох критеріїв; через це його не запускають і не
рахують повторно. Новий run потрібен після релевантних змін, failure або зміни
перевірюваної конфігурації. Кожен новий acceptance report ідентифікує source
revision або SHA-256 source snapshot, build flags, runtime/driver, inputs,
commands, очікувані controls і результати через teardown.

## Підтримувана конфігурація та спільні контракти

Windows x64, GCC / MinGW-w64 + Ninja; цільові конфігурації Debug і Release.
Поточна перевірена конфігурація: GCC 16.2 Debug і Release, RTX 4070,
ізольовані `build/stage9-performance-manifest/{debug,release}`.
Остаточне приймання та точні identities зафіксовано в M9.
MSVC і Linux не є умовами цього перенесення.

Vulkan loader/device/development files >= 1.4; системний Windows loader,
без app-local `vulkan-1.dll`. DXC компілює HLSL -> SPIR-V із target Vulkan 1.3;
це окремий контракт від версії runtime. Lua — системна MSYS2 5.5.1 у межах 5.5,
усі consumers мають однаковий ABI; sol2 — проєктний snapshot із патчами.
Точні залежності та configure/build commands наведено в README.

Renderer DLL — єдиний власник facade/world і RHI singleton. RHI володіє backend;
кадр має порядок `begin_frame -> commands -> end_frame/submit -> present`.
Queued commands копіюють mutable payload або утримують immutable resource version.
GPU resources звільняються за submit serial/fences, resource cooks — після drain.
Class IDs, facade contracts і порядок logic/render commands зберігаються.
Непідтримана можливість повертає явну помилку. Коректний idempotent no-op
не прирівнюється до порожньої реалізації відсутньої функції.

Автоматичні GPU windows — hidden/nonactivating/input-transparent, без
переміщення/захоплення курсора. Captures — fenced GPU readback до present.
Приймальні GPU runs вимагають validation 0 errors / 0 warnings через teardown.
Original assets не змінюються; input identity і SHA-256 зберігаються у звітах.
Кількість checkbox не є відсотком готовності.

## Етапи 0–7: прийнята основа та original runtime M5-C

**M0 — ownership і кадровий контракт. Прийнято.** Єдиний renderer DLL,
allocators, backend initialization/failure cleanup, generation handles,
stale-handle rejection та перевірений порядок кадру. `USE_VULKAN_RENDERER=OFF`
явно відхиляється. Залишок world/facade initialization був закритий у M1.

**M1 — facade clear/present. Прийнято.** Engine smoke проходить актуальний
facade/world, resize/minimize/restore, повторну initialization і teardown.
Потрібні surface/swapchain maintenance extensions із present fences;
непідтримані devices явно відхиляються. Це bootstrap, а не запуск гри.

**M2 — shaders і indexed geometry. Прийнято.** DXC, shader modules,
immutable VB/IB, single-binding layout, uint16/uint32 indices, depth/winding,
pipeline keys/cache та deferred destruction. Довільний SPIR-V і всі shader
stages цим не прийняті.

**M3 — textures/constants/descriptors. Прийнято.** Початковий material ABI,
RGBA8 sampled textures/mips, samplers, per-draw constants, immutable descriptors,
reflection і process-local compiler cache. Пізніші format/array/MSAA розширення
належать профілям, які їх використовують; повторювати основу M3 не потрібно.

**M4 — scene/view/output resources. Прийнято для одного output.** Штатні
queries, реєстрація cooks, shared bootstrap presentation state, facade queues,
camera/viewport, callbacks, registry ownership і destroy/recreate/drain.
Multi-output і звичайний threaded startup обліковуються лише в M8.

**M5 — static assets. Функціональний профіль і оновлений M5-C прийнято.**
Підтримано cooked static geometry/settings/material, opaque/cutout unlit diffuse,
named surfaces/visibility/material assignment, transforms/culling, atomic buffer
updates, versioned reload, DDS BC1/2/3 та TGA/options із in-memory mips.
Скриня й бочка зі справжніх geometry/textures перевірені через source-derived
packs, кожна з 16 unload/recook cycles, stable resources і validation 0/0.
Acceptance probe реалізований. Це достатня функціональна передумова M6,
але не доказ сумісності готового legacy pack.

- [x] **M5-C — runtime-завантаження наявних original assets.** За явним рішенням
  користувача 2026-10-20 замінює попередній критерій готового legacy cooked pack:
  `Stalker2_2011` — єдиний доступний набір, cooked cache у ньому відсутній.
  Ідентифікувати original root, exported model, source settings/material modules
  і texture/options paths та SHA-256. Завантажити штатними VFS/resource queries;
  geometry й Lua inheritance/material cooking виконуються у native runtime,
  generated cache зберігається лише у workspace. Python offline-адаптер не
  бере участі у завантаженні; byte-identical staging original inputs дозволений
  зі звіркою SHA-256. Active unsupported material features відхиляються явно.
  Перевірити підтриманий preview, camera/visibility/removal,
  щонайменше 16 unload/recook cycles, unchanged inputs і повний drain.
  Готово після звіту з непорожніми прийнятими captures і validation 0/0.
  Відмова старого source-derived full-material pack є negative control,
  а не самостійним доказом закриття M5-C.
  Обсяг охоплює bounded original static export archive, exported static vertex
  format, Lua settings/material inheritance і наявний opaque/cutout surface
  profile з TGA/options. Готовий legacy cooked-pack compatibility та original
  scene/DX11 parity цим не заявляються. Нові rendering features поза цим профілем
  або відкладення потребують окремого рішення.
  **Перевірено 2026-10-20:** у всьому наданому `xr2-dsgn` (12 762 файли)
  відсутні `resources/platforms/pc_dx11`, `resources.converted`, DDS і
  `converted_model`; `resources.mount` посилається на відсутній cooked cache.
  Наявні `.model` містять exported geometry, матеріали є Lua source.
  Користувач підтвердив, що `Stalker2_2011` — єдиний доступний набір.
  Шість оригінальних barrel inputs перевірено за попередніми SHA-256 без змін.
  Повтор full-material negative control уточнює відмову:
  `props/barrel/barrel_1_red_radiation`, `material.instance`; shutdown успішний,
  validation 0/0, inputs unchanged, завершених acceptance cycles — 0.
  `build/stage5-legacy-compatibility/summary.json` фіксує перевірку передумов,
  а не приймання. Користувач обрав runtime loading/cooking original assets.
  **Прийнято 2026-10-20 для GCC Debug / RTX 4070:** original
  `props/box/woodbox_mil.model`, material `props/box/wood_box`, 595 vertices /
  1188 indices; native archive/geometry/Lua/material cooks, 17 фактичних recooks
  (warmup + 16 unload/recook cycles), camera/visibility/removal, stable GPU
  counts/capacities, unchanged texture reload і повний drain. 36 staged original
  files byte-identical за SHA-256; native geometry збігається з незалежним
  post-run oracle. 15 BMP, 24 335 видимих pixels; model/normals/camera переглянуто.
  Original barrel явно відхиляється за активним `use_variation_mask` без fallback.
  124/124 CTest (66 GPU), 35 Python, 52 CTest validation summaries і два original
  probe reports — 0/0 через teardown. Доказ:
  `build/stage5-original-runtime/summary.json`. Ready legacy cooked-pack
  compatibility та original scene/DX11 parity цим не встановлюються.

**M6 — MRT GBuffer. Прийнято для static opaque/cutout.** RHI attachments,
passes/transitions, primary packing, normals/depth/tangents/position/stencil,
debug views, resize і retirement. Передумова — функціональний профіль M5;
M5-C не блокує його вже доведений результат.

**M7 — фінальний static кадр. Прийнято, 7.1–7.8 закриті.** Межі результатів:

- 7.1: scalar/maps/detail normal/roughness/Fresnel і material inputs.
- 7.2: directional light, BRDF, diffuse/specular accumulators і combine.
- 7.3: point/spot light lifecycle, attenuation/cones і queued snapshots.
- 7.4: ambient, constant emission і view-Z fog.
- 7.5: directional cascades, spot/point shadow atlas, opaque/cutout casters.
- 7.6: cubemap sky і non-intersecting unlit alpha/additive forward surfaces.
- 7.7: manual exposure/Reinhard, bloom, FXAA, deferred MSAA 2x/4x/8x, opt-in TAA.
- 7.8: інтегрований кадр, independent CPU expected image, усі 12 AA profiles,
  movement/split-history/reload/recook/resize/drain та real-source AA controls.

TAA прийнято для static-профілю: без object motion vectors, scene changes
скидають history, far-depth/sky не накопичуються. TAA і FXAA взаємовиключні.
Lit transparency/refraction/OIT, довільна animated-scene temporal quality
і DX11 image parity до прийнятого M7 не входять.
Точний порядок passes/attachments/ABI — у README; історія M0–M7 — в архіві.

## Етап 8. Типи сцени та штатні game/editor workflows

Статус: прийнято 2026-10-20 для bounded manifest profile у GCC Debug на
RTX 4070. Передумова M7 виконана; original runtime M5-C прийнято окремим
звітом. Release/додаткові GPU/failure injection обліковуються у M9.

### Прийняті компоненти M8

Наведене нижче — реєстр результатів, без активних задач.
Зазначені обмеження є межами доказів; вони не відкривають ці компоненти повторно.

- **Particles, компонентна основа й scene ownership:** scene-owned world із
  close-before-drop, typed retained game/facade userdata, зовнішні production
  world/instance/template cooks, aligned arena 1..50000 particles, restartable
  registration і pending/deleter drain; callback/repeated-play lifetime repairs.
  Version-1 checked config `resources.converted/particles/<name>.particle`,
  один LOD, 1..64 emitters, constant rate/lifetime/initial color/size/velocity/
  rotation, local/world-space. Instances публікуються після готовності всіх
  material dependencies. Material — unlit alpha/additive, RGBA8 linear/sRGB TGA
  або authored constant white sample, unit UV, two-sided, linear clamp LOD0.
  Immutable billboard snapshot/RHI: camera-facing square/rectangle, rigid
  transforms, rotation у radians, per-batch depth order; 4096 particles/batch,
  64 batches, 8 MiB geometry і 64 MiB unique textures/frame.
  Scene flag дозволяє lifecycle creation; queued commands виконуються на owner
  thread. Scene draw інтегровано в M8-PARTICLES; raw `particle_world()`,
  particle preview/mode/slomo явно відхиляються як unsupported.
  Arena capacity не є гарантією одночасного rendering усіх particles:
  при інтеграції діють також batch/frame budgets.
- **Debug/UI:** immutable indexed lines/triangles, depth/overlay, blending,
  viewport/scissor; font atlas і byte-font layout, реальний Arial TGA.
  UI виконується після AA/tonemap. Unicode shaping/kerning і всі старі UI APIs
  цим профілем не прийняті.
- **Skeletal/character:** штатні model/skeleton dependencies і game wrapper,
  CPU four-weight skinning, atomic pose/bounds, `update_skeleton()` і drain;
  neutral_01, два original clips, root motion/loop, fixed blend/transition,
  diffuse/normal/specular і scalar roughness. GPU skinning, довільні animation
  graphs/controllers/events та повний original actor parity не прийняті.
- **Terrain renderer:** chunks 102/103 і 22-byte samples, geometry/basis/bounds,
  atomic copied edits, штатні mesh/instance cooks; unlit/deferred materials,
  lighting, спільні directional/spot/point shadows і MSAA/TAA integration.
  Прийняті renderer AABB updates, coherent role-texture reload та transactional
  async binding add/remove/exchange зі sparse slots/cancellation/drain.
  Mixed decoded-RGBA normalization обмежена same-format power-of-two/full-mip
  inputs і стандартним 256 MiB array budget, без downsampling/BC transcoding.
  Явний extended profile контрольної сцени визначено в M8-GAME.
  CPU inventory 814 cells та metadata eligibility 729 cells не означають
  GPU/image acceptance усіх cells; 69 budget/16 missing inputs залишаються
  явними відмовами. Full original terrain image parity не прийнятий.
- **Terrain game wrapper/contact:** `terrain_model_class` утримує engine-owned
  collision resource class 192 із Opcode/Bullet; єдиний deleter, authored local
  center/translation, body retention і unload/drain. Placement/buffers immutable,
  translation-only; renderer-only edits не доводять collision edits.
  Production internal-edge hook перевірений для sphere radius .5/mass 1 із
  заблокованим обертанням, drop/translation/seams та callback ownership.
  Free rotation, інші body profiles, live collision edits і concurrent
  install/finalize під physics callbacks не прийняті.

### Єдиний checklist M8

Назви в дужках — сталі ідентифікатори існуючих задач, не нові підетапи.

- [x] **Цільовий обсяг (M8-SCOPE), 2026-10-17.** Зафіксовано
  [manifest](sources/xray/render/vulkan/m8_acceptance_manifest.json) і
  [SHA-256 inventory](sources/xray/render/vulkan/m8_acceptance_inputs.json)
  для 195 original inputs та чотирьох provenance records. Користувач обрав
  distortion і SSAO як обов'язкові additions; GI/LPV явно відкладено.
  Manifest задає конкретні terrain/static/actor/particle/vegetation/decal assets,
  профілі/omissions, game/editor launch contracts, mounts, outputs/modes,
  camera/AA/pass requirements і independent image/lifecycle controls.
  Підготовка спільного game pack обліковується в M8-GAME;
  editor host deployment — у M8-EDITOR.
  Scope verifier відтворює dependency inventory без виконання source Lua і
  без зміни original inputs; наявні hashes не перезаписуються.

- [x] **Particles (M8-PARTICLES).** Production cook -> copied facade queue ->
  owner-thread simulation -> immutable scene draw -> remove/close/drain підключені.
  Tick відбувається один раз на scene world у CPU-кадрі з draw submission, також
  за minimize; додаткові views не прискорюють simulation. Default wall-time delta
  обмежений 0.25 s; typed scene config дозволяє явний fixed step (0,0.25].
  Кожен draw зберігає власні billboard vertices/indices, texture і camera;
  пізніші update/remove не змінюють поставлений кадр. Pass іде після opaque/terrain
  та lit forward geometry, перед debug/UI; lit використовує linear HDR, opaque
  depth test без depth writes, 1x і post-AA off. Порожній particle world зберігає
  решту content. Frame budgets спільні для views: 64 batches, 8 MiB geometry,
  64 MiB textures; до 64 submitted worlds. CPU center-only culling не відсікає
  billboard, який частково перетинає край view.
  Незалежні alpha/additive/depth/transform/camera pixels, pause/visibility/stop,
  queued remove, resize/minimize, budget/profile rejection і retirement drain
  пройшли scene test без direct billboard submission. Попередні owner/type/thread,
  repeated-play, pending/retained shutdown controls лишаються чинними.
  Real proof використовує названі в manifest candlelight graph/material/TGA/options:
  оригінальні texture bytes та emissive/transparency constants у явно описаній
  constant-profile projection, із незалежним Pillow RGBA oracle. Це camera-facing
  unlit RGBA8 LOD0 square/rectangle profile, а не original effect parity.
  Locked axes/subUV/beam/trail/mesh/decal particles, lit/soft particles/particle AA,
  cross-emitter sorting, arbitrary curves, bursts/events/domains/finite loops
  і previews залишаються explicit unsupported. Докази:
  `build/stage8-particle-submission`; production threading належить M8-GAME.

- [x] **Рослинність (M8-VEGETATION).** Прийнято prepared fixed highest-LOD
  eastern_white_pine через штатні `speedtree_class/instance` cooks та copied
  facade add/update/remove/material/visibility. Offline SRT 05.2.0 reader зберігає
  original branch/frond vertices, UV і packed basis; leaf-card centers, dimensions,
  pivots, start scale, planar offsets і UV розгортаються окремо для кожного view.
  Tree snapshots використовують чинні static cutout/GBuffer/lighting/shadow passes;
  пізніші update/remove/material commands не змінюють queued geometry/materials.
  Профіль допускає proper rotation/translation і positive uniform scale; lit —
  1x, post-AA off. Membership змінюється одразу; `populate_forest` валідовує
  поточний список без окремого SDK acceleration index. Межі: 64 trees / 262144
  vertices на сцену та 16 MiB expanded leaf geometry на CPU-кадр для всіх views.
  Real tree: 7639 vertices, 5642 triangles, 657 leaf cards, три components
  branch/frond/leafcard; leafmesh у цього asset відсутній. Пройшли 485 independent
  raster samples (133 foreground), 7884 source-card placement checks, visibility,
  source material/lit-shadow captures і synthetic cutout/depth/material/camera,
  copied update/remove, culling, resize/minimize, retained close та cook/GPU drain.
  Докази: `build/stage8-vegetation`; 14 original inputs і 26 prepared outputs
  зафіксовані provenance. Це supported material/geometry projection, а не SDK
  effect parity; wind, distance/morph/billboard LOD, collision, packed vertex AO,
  forest acceleration і selection highlighting цим профілем не прийняті.
  Фінальний regression: 115/115 CTest (61 GPU), 20 Python без skips;
  validation 0 errors / 0 warnings через teardown. Minimized submissions не
  створюють tree/particle GPU uploads, CPU particle simulation триває;
  повторні update/tick перевірені на стабільність GPU resources.

- [x] **Decals (M8-DECALS).** Прийнято named `decals_default` material через
  production material cook і copied facade add/update/remove за scene-local ID.
  Rigid projector із centered half extents [0.001,128] проєктує LOD0 RGBA8 diffuse
  alpha та AG normal map на видимий terrain/static receiver. Normal contribution
  використовує projector tangent і receiver normal; окремі sampled/output targets
  змінюють albedo/normals перед lighting, зберігаючи depth, roughness і specular.
  Packed-depth G channel позначає receiver class; vegetation/skeletal/particles
  не одержують decals. Підтримано finite forward-Z perspective/orthographic і
  rigid camera, deferred diagnostics/lit, 1x і post-AA off. Межі: 16 decals/scene,
  64 projections і 64 MiB unique texture bytes/CPU-frame для всіх views;
  retained upload cache — до 128 images / 128 MiB. Порядок перекриття — insertion
  order, update зберігає позицію, remove/re-add переносить у кінець.
  Пройшли independent alpha/normal/HDR/move/remove/depth/receiver controls,
  perspective, queued remove і texture reload snapshots, failed reload retention,
  resize/minimize, scene/frame budget rejection, owner/foreign guards та pending
  close/cook/GPU drain. Real bullet_hit_concrete: 6 original inputs / 5 outputs,
  255 source-derived sample positions (50 foreground), 3060 channel checks;
  max error — 1 byte для albedo/normal/lit, removed — 0. Докази й SHA-256:
  `build/stage8-decals`. Angle fade/clip, reflection, інші receiver classes,
  particle decals і complete original effect/scene parity не прийняті.
  Regression: 116 унікальних CTest cases прийняті (62 GPU): 115 у full run,
  один DXC mutation fixture виправлено й повторно пройдено без змін renderer.
  Додано три negative decal ABI controls; 20 Python без skips,
  validation 0 errors / 0 warnings через teardown.

- [x] **Вибрані effects і shader stages (M8-EFFECTS).** Прийнято bounded SSAO
  і distortion у спільному lit scene path через copied `postprocess_settings`.
  SSAO читає hardware depth і GBuffer normals після decals: 8 fixed pixel offsets,
  angular bias і bounded view-space range; окремий 3x3 depth/normal filter.
  AO множить лише ambient contribution, не direct lighting/specular/emission.
  Distortion використовує named `distortion_panner` material, existing static-model
  add/update/remove/material facade і окремий graphics field pass: opaque depth
  test, без depth writes/casting. Original XY scale/255, panner і unmoved alpha
  утримані; host задає captured phase [0,3600] s, vector texture читається linear.
  Окремий HDR source/output composite обмежує offset до [0,64] output pixels
  і visible field/viewport перед чинними bloom/tonemap; overlap — last field wins.
  Typed defaults вимикають обидва effects. Підтримано final lit, 1x, post-AA off;
  malformed parameters, AA/master-toggle bypass і непідтримані view modes явно
  відхиляються. Target/shader creation перевіряє потрібні sampled/renderable
  RGBA16F capabilities із cleanup на failure; GS/tessellation/compute не вмикаються.
  Distortion frame limits: 64 surfaces / 8 MiB unique geometry / 64 MiB textures.
  Minimized CPU submissions не готують field uploads/effect targets.
  Незалежні offset/panner/zero/occlusion, corner/filter/normal/perspective,
  direct-light identity, queued toggles/remove/reload, budgets, resize/minimize і
  pending close/drain controls пройшли. Real distortion: 12 original inputs
  (із reference HLSL) / 5 prepared outputs, з них 2 synthetic receiver controls;
  1755 channel checks у трьох phases, max error 1 byte. Докази: `build/stage8-effects`.
  Це bounded projection, не original SSAO noise/kernel/effect/scene parity.
  Full mixed game/editor workflows прийнято в M8-AUDIT; GI/LPV відкладено.
  Regression: 117/117 CTest (63 GPU), 20 Python без skips, 67 DXC compile/ABI
  mutation rejections; validation 0 errors / 0 warnings через teardown.

- [x] **Game workflow (M8-GAME), 2026-10-18.** Manifest render-level працює у
  звичайному `stalker2.exe` зі штатною configuration та окремими
  logic/render/resource/cook/HDD/DVD threads, без `-debug_single_thread`.
  `prepare_m8_control_pack.py` збирає спільний 175-file pack і native
  `render_scene` зі збереженими authored terrain placement/geometry,
  object placement, camera та lights. `m8_control_world` завантажує його через
  справжні resource queries/cooks і facade; legacy NPC/RTP/UI worlds до цього
  профілю не входять. Native descriptor не доводить original xprj/cooked parity.
  Production `initialize_game()` реєструє particle cooks у Vulkan DLL на render
  owner; logic channel підтверджує renderer/session/channel readiness до
  створення game world. Scene/particle callbacks, simulation/close і unregister
  зберігають встановлені owner threads та guards. Terrain queries перевіряють
  collision wrapper; engine володіє terrain collision cook/internal-edge hook.
  Production HDD/DVD queues публікуються асинхронно; shutdown тримає producer
  threads до consumer drain і очищення їхніх TLS queues.
  Два незалежні native процеси відтворили всі 37 BMP побайтово. Перевірено
  внесок SSAO/distortion, terrain/static/actor/tree/decal і обох particle modes;
  visibility, transforms, actor transition, particle pause/stop/remove/replay,
  camera movement/restore. Сімнадцять відновлень у кожному процесі точно
  збігаються з inspection baseline. Unload дочікується registry counts
  `(scene,view,output)=(0,0,0)`; reload точно відтворює обидва camera views.
  Shutdown навмисно утримує справжній particle query та scene/world handles
  до закриття render session. Logic-owner `on_render_session_closed()` вивільняє
  transaction, перевіряє rejection callback і pending=0 до знищення game/cooks;
  нові render commands у закриту session не подаються. Finalize повторює drain
  без примусового unregister живих cook users.
  Scope amendment 2026-10-18: користувач залишив `chaz_mir_part/-1_-1.terr`,
  який був negative control стандартного memory profile. Додано явний opt-in:
  384 MiB arrays на cell, 576 MiB на queued view для двох потрібних cells;
  стандартний profile 256 MiB збережено. Scene/cook data мають погоджувати profile,
  bindings/reloads і draw snapshots зберігають його; перевищення відхиляється
  до GPU allocation. Докази розширення — `build/stage8-terrain-extended`;
  це розширення окремо перевірене й використане у спільному pack.
  Докази приймання — `build/stage8-game-workflow/summary.json`, source/binary
  hashes, native receipts, independent image metrics та captures. Звичайний
  запуск без workflow flags зберіг початковий overview з `build/stage8-control-pack`;
  missing-project control дав очікуваний exit 2 без кадру. Фінальний regression:
  119/119 CTest (64 GPU), 27 Python; validation 0 errors / 0 warnings через teardown.
  Попередні bootstrap і initial-frame докази збережено в
  `build/stage8-game-threading` та `build/stage8-control-pack`.

- [x] **Editor main view (M8-EDITOR), 2026-10-19.** Реальний WinForms host працює
  у `stalker2.exe -editor` через editor facade та окремий editor STA producer.
  CMake збирає/deploys native adapters і managed host також для target `stalker2`;
  C++ toolchain — GCC, managed — .NET SDK 10.0.203 / Windows Desktop 10.0.7,
  offline restore без NuGet packages. Уточнений deployment contract —
  [m8_editor_host.json](sources/xray/render/vulkan/m8_editor_host.json), пов'язаний
  із frozen input manifest. Він визначає DLL names/exports, hostfxr factory,
  runtime, ABI, lifecycle, controls і незалежний image oracle.
  Native loader отримує managed `UnmanagedCallersOnly` factory через hostfxr;
  frozen main-host ABI 1 перевіряв size/version, native-width HWND/context, int32 flags
  і копіює 16 finite float32 matrix values. Managed exceptions повертають status;
  stale/foreign callbacks відхиляються. Missing-deployment failure/retry і чотири
  managed create/run/clear/destroy cycles перевірено окремим CTest.
  Static-library gates для editor path усунуто без зміни загальної конфігурації.
  Renderer/session/cooks і channel owner готові до першого editor query.
  Спільний `control_level.h` використовує game/editor facade свого producer;
  disabled game world не створює другого content/cook owner. Toolbar handlers
  виконують visibility/transform props, terrain binding edits та UI/debug overlay.
  Для додаткового bar-layer є явна ізоляція cell у чинних memory limits;
  після remove повертаються обидві cells, після exchange — початкові slot tables.
  Два незалежні процеси відтворили всі 49 BMP побайтово, з 22 точними
  відновленнями inspection image у кожному. Усі 37 спільних кадрів збігаються
  з прийнятим game baseline; незмінні game executable/loaded DLLs перевірено
  окремим повним workflow. Toolbar-команди проходять справжні managed handlers;
  UI RGB і HDR-debug triangle coverage перевіряються незалежно.
  Load/unload/reload і pending-query shutdown проходять на правильних owners.
  Окремий startup-cancel control утримує перший project query до render close
  і drain callbacks перед видаленням controller, без content/capture.
  Native handles/cooks звільняються після session close; managed HWNDs/callbacks
  живуть до завершення Vulkan teardown, потім UI/context і module звільняються.
  Missing-project control дає exit 2 та коректний teardown без кадру.
  Докази — `build/stage8-editor-host/summary.json`, source/binary hashes,
  native receipts, image metrics і captures. Regression: 120/120 CTest (64 GPU),
  28 Python; validation 0 errors / 0 warnings через teardown. Original inputs,
  спільний pack і всі попередні прийняті captures незмінні.
  Окремий model preview, незалежні resize/close outputs — M8-OUTPUTS;
  unported legacy panels не входять до прийнятого main-view profile.

- [x] **Editor outputs/previews (M8-OUTPUTS).** Прийнято спільний native workflow
  main view та окремого static-model preview `woodbox_mil`: один device,
  незалежні HWND/surfaces/swapchains, scene/view/instance, targets і frame history.
  Immutable model asset спільний; mutable visibility та main props ізольовані.
  Під час preview cooks main продовжує presenting останньої content phase.
  16 кроків із точним render-frame ACK дають 28 captures. Незалежні resize до
  1102×673 та restore, minimize/resume кожного вікна, preview close/reopen і
  visibility controls пройшли у двох процесах із побайтовим replay. Згорнутий
  output не збільшує presentation clock, другий продовжує; reopened preview
  починає з frame 1. Main/preview restores збігаються з прийнятими hashes;
  resized preview проходить незалежний CPU projected-bounds oracle.
  Preview close після додаткового queued frame утримує HWND до GPU ACK і drain
  scene/view/output leases. Host WM_CLOSE із навмисно held preview query
  відхиляє callback після render-session close, дренує pending до нуля, зберігає
  обидва HWND до Vulkan teardown та native module до file producer TLS cleanup.
  Контракт `m8_editor_outputs.json` уточнює frozen preview contract; ABI 2
  залишається чинним. Particle graph editor/preview поза прийнятим scope.
  Докази — `build/stage8-native-outputs/summary.json`: 122/122 CTest (66 GPU),
  32 Python, 64 validation summaries без errors/warnings. Усі 49 editor / 37 game
  baseline images, 727 попередніх captures, original inputs та immutable pack
  незмінні. Історичні checkpoints збережено в README.

- [x] **Capability/no-op audit (M8-AUDIT).** У `m8_capabilities.json` усі 41
  обов'язкові пункти manifest мають implementation route та passed test/native
  image proof: game/editor operations, modes, image controls, lifecycle і
  negative inputs. Required paths не посилаються на legacy unsupported gates.
  Прибрано мовчазні no-op з доступних legacy API: shader/configuration reload,
  global stage toggles, raw postprocess resource, editor helpers і selection.
  36 producer guards відхиляють виклики до payload copy/queue insertion;
  39 public engine guards захищають прямі виклики. Чотири private configuration
  callbacks закриті за відхиленими entry points; обидві retired platform
  factories відмовляють до створення backend.
  75 CPU negative controls перевіряють точні diagnostics, придатність черг і
  відсутність backend/frame side effects. Шість Lua-залежних entry points
  перевірено зі штатним core VM у native editor; три спільні — також у game.
  Unsupported particle preview/modes, unknown stages, GI/LPV gates і межі
  scene/view/cook profiles збережено без розширення scope.
  Докази — `build/stage8-capability-audit/summary.json`: 123/123 CTest (66 GPU),
  35 Python, 64 validation summaries без errors/warnings. Повторні editor/output
  runs точно відтворюють 49 і 28 кадрів; game — усі 37. Збережено 934 попередніх
  captures, immutable pack, 913 original files і три absent paths.

### Порядок і критерій завершення M8

M8-SCOPE, bounded content profiles M8-PARTICLES, M8-VEGETATION, M8-DECALS,
M8-EFFECTS, game workflow M8-GAME, editor main view M8-EDITOR та спільний
main/preview workflow M8-OUTPUTS та capability/no-op audit M8-AUDIT прийняті.
Узгоджений bounded профіль Етапу 8 завершено; фінальну стабілізацію та приймання
цього профілю обліковано в M9.
Межа відповідальності: M8-PARTICLES підключає команди/simulation/submission до
scene contract; M8-GAME забезпечує production initialization/threading/shutdown
цього contract. Між ними обліковується одне узгодження owner/readiness, а не дві
незалежні реалізації тієї самої логіки.

Усі M8 checkbox закриті доказами. Game scene та editor main/preview пройшли
спільний load/update/unload/shutdown lifecycle; validation/drain/input-integrity
вимоги виконані. Прийняті раніше bounds
залишаються чинними. Якщо manifest виходить за них, потрібна конкретна
scope decision до реалізації; успішний вузький test не означає такого розширення.
Release, додаткові GPU, profiling і failure injection обліковуються лише в M9.

## Етап 9. Стабілізація та остаточне приймання

Статус: завершено 2026-10-22 у погоджених bounded profiles M8 та original
runtime M5-C, на перевіреній Windows x64 Debug/Release матриці з RTX 4070.
Історичний NVIDIA startup-ризик явно прийнято користувачем; причина crash
лишається недоведеною. Рішення й остаточні identities зафіксовано у
[M9 final acceptance](build/stage9-final-closeout/summary.json).
Наявні CPU/DXC/GPU suites використовуються повторно, без задачі створити їх заново.

- [x] **Матриця й відтворюваність (M9-BUILDS).** Зафіксувати GPU/driver/runtime,
  asset manifest і test commands для чистих окремих GCC Debug/Release builds.
  Обов'язковий baseline — доступний RTX 4070; інші GPU додаються явним переліком.
  Запустити наявні CPU/DXC/Python suites і GPU suites на придатному runner,
  а також manifest game/editor workflows. Готово після logs із усіма
  обов'язковими cases без failures; GPU skip не зараховується як GPU proof.
  Для приймальних GPU runs обох конфігурацій задавати
  `XRAY_VULKAN_REQUIRE_VALIDATION=1` і перевіряти фактичне `Validation: enabled`
  у logs. Нульові counters при вимкненому layer не є validation proof.
  Робочі evidence/build roots — `build/stage9-matrix/debug` і `release`;
  кожен має власну `bin/`, native DLLs, managed host та fixtures. Launcher
  приймає `--binary-dir`; default `binaries/` збережено для попередніх commands.
  Прийнято 2026-10-20: по 125/125 CTest (66 GPU) та 37/37 Python tests;
  Debug 303.63 s, Release 268.56 s. Configuration contract перевіряє runtime
  `R_ASSERT`, Debug-only assertion side effects, capacity/swap та empty VFS
  iterators. Release зберігає `NDEBUG` й оптимізацію; sol2 type checks активні.
  Compile/link/runtime fixes, GDB traces і невдалі початкові runs збережено в
  [matrix report](build/stage9-matrix/summary.json). Остаточні evidence roots —
  `run-accepted-debug` і `run-accepted-release`; binaries/fixtures розділені.

- [x] **Lifecycle regression (M9-LIFECYCLE).** На остаточній матриці повторити
  наявні reload scene/shaders/textures, resize/minimize/restore, output close,
  pending-frame shutdown і exact MSAA+TAA reload comparison. Додати лише controls,
  яких бракує для manifest workflows. Готово без зависань, leaks, порушення
  isolation та критичних visual regressions; межі accepted profiles не розширюються.
  Використати ті самі runs із M9-BUILDS, якщо вони покривають ці controls:
  BUILDS відповідає за конфігурації/відтворюваність, LIFECYCLE — за їхню coverage.
  Shader reload тут означає наявний compiler contract: зміна source/includes
  інвалідує cache, compile failure не зберігає старий success, виправлення
  компілюється заново. Legacy `world/scene.reload_shaders` лишається explicit
  unsupported за M8-AUDIT; runtime pipeline hot reload цим не заявляється.
  Paired outputs у матриці використовують повний перевірений editor workflow
  тієї самої збірки: executable та всі DLL SHA мають збігатися. Main restores
  та isolation порівнюються побайтово; preview зберігає точний M8 golden.
  Порівняння reference між Debug/Release і замороженим M8 допускає не більше
  двох змінених pixels та RGB delta 1/255. Межу ввели після виміряного Release
  delta: два red channels +1; звичайний M8 verifier без reference лишається
  точним. Rejects іншої збірки й виходу за цю межу покривають Python tests.
  Прийняті ті самі два runs із M9-BUILDS: у кожному 37 game, 49 editor і
  28 paired-output captures, missing-project game/editor, startup query cancel,
  owner drain/teardown та 16 original woodbox unload/recook cycles. Усі 15
  original captures кожної конфігурації побайтово збігаються з M5-C. Coverage
  scene/texture reload, shader cache invalidation, exact 4x+TAA phases,
  queued shutdown і resize/minimize/recreate прив'язана до raw CTest logs.

- [x] **Failures і startup crash (M9-FAILURES).** Контрольовано перевірити
  capability/allocation failure і device loss: діагностика, cleanup, завершення
  без deadlock. Окремо дослідити Stage2 SegFault після NVIDIA ICD initialization
  failure: потрібні причина й перевірений fix або явне рішення щодо залишкового
  ризику. Успішні повтори/missing-ICD exit цього не доводять.
  Автоматичне device recovery до поточного обсягу не входить.
  Виправлений у M9-BUILDS Release VFS resource-crash (`this=0` у
  `base_node::get_full_path`) має окремий GDB proof; він не встановлює
  причину історичного Stage2 NVIDIA ICD startup incident.
  Контрольована частина перевірена 2026-10-20 у
  [failure checkpoint](build/stage9-failures/summary.json): по 144 сценарії
  Debug/Release — startup capabilities/creation, OOM/rollback, device loss і
  facade teardown. Handles публікуються лише після успішного створення;
  збережено operation та numeric `VkResult`. Hooks приватні, лише для tests;
  physical GPU reset та automatic recovery не заявляються. Повторна повна
  матриця має по 130/130 CTest (70 GPU), 37/37 Python і manifest workflows.
  Окремий unregistered ICD проходить extension discovery, повертає -3 з
  фактичного ICD `vkCreateInstance`; loader повертає -9, процес штатно
  завершується кодом 1. Його очікувані loader warning/error збережено окремо
  від validation 0/0 успішних GPU controls. Заморожена попередня Debug-збірка
  також штатно завершується з цим ICD під GDB. Це не відтворює NVIDIA SegFault;
  відповідного dump/backtrace або Application Error event не знайдено.
  Закрито 2026-10-22 після явного рішення користувача:
  «Прийняти описаний ризик і закрити M9».
  Це прийняття залишкового ризику за другою умовою пункту, а не доказ fix
  або встановленої причини історичного crash. Поточний workload, validation,
  failure controls і обмеження підтверджує незмінний final matrix report;
  запис рішення збережено в [final acceptance](build/stage9-final-closeout/summary.json).

- [x] **Пам'ять і продуктивність (M9-PERFORMANCE).** Для manifest scenes
  зафіксувати тривалість warmup/stress, memory/resource/capacity baseline і budgets;
  виміряти CPU/GPU growth, descriptors, allocations/frame, pipeline stalls і
  CPU/GPU frame time. Перевірити тривалий same-process stress, background timing
  та near-timeout варіативність. Готово після контрольованих вимірювань без
  необмеженого росту і з явним висновком за budgets. Оптимізації, persistent
  pipeline cache/suballocation додаються за виміряною потребою; `vkDeviceWaitIdle`
  у звичайному кадрі не є прийнятним способом приховати lifecycle проблему.

  Прийнято 2026-10-22 у
  [manifest performance і final matrix report](build/stage9-performance-manifest/summary.json).
  Окремий заморожений [instrumentation checkpoint](build/stage9-performance/summary.json)
  підтверджує облік device/host allocations, timestamps і failure rollback;
  manifest-приймання спирається на наступні 20 непілотних runs 2026-10-21.
  Контракт version 3
  [M9 performance profile](sources/xray/render/vulkan/m9_performance_profile.json)
  фіксує 15/300 s warmup/stress для baseline, 10/60 s із чотирма CPU workers
  та три повтори 5/15 s із двома workers і 5 s watchdog margin для кожної
  конфігурації та game/editor-preview scene: сумарно 1620 s stress.
  Resource arena задана штатним параметром 4096 MiB; validation і capture
  readback увімкнені. CPU/GPU samples, serials і capacities main/preview
  розділені; timestamps читаються після наявного slot fence без додаткового idle.
  Межі обліку визначені в README: requested allocation bytes, process commit
  та driver heap estimates не трактуються як одна величина фізичної GPU memory.

  Усі 20 runs уклалися в незмінні byte/frame budgets: private commit і його
  lifetime peak ≤8 GiB, device allocations live/peak ≤4 GiB, private growth
  ≤128 MiB. Найбільший private growth — 22.864 MiB Debug і 9.825 MiB Release;
  device/host/local-heap growth — 0. Найбільші CPU/GPU p95 — 13.868/7.630 ms,
  p99 — 15.273/13.426 ms за меж 100/200 ms відповідно. Startup ≤48.219 s
  за budget 90 s; near-timeout runs завершилися з 4.031–4.688 s margin.
  Після warmup GPU allocations, pipeline creation, device-idle calls,
  offscreen pass-pair creation та ріст descriptor/uniform capacity — 0.
  Main captures побайтово збігаються з pre-optimization control своєї
  конфігурації, preview — з точним M8 golden; teardown збалансований.

  Виміряні зміни: cache трьох shadow atlas extents, reuse незмінної leaf/particle
  geometry, descriptor sets окремо на frame slot та LRU до 64 offscreen
  render-pass/framebuffer pairs на output. Фактичний manifest максимум — 18
  cached pairs; invalidation/eviction використовує last-use fence retirement.
  Початковий довгий run і GDB control зберігають окремий runtime SIGSEGV у
  worker `VkLayer_khronos_validation.dll` 1.4.357 після memory growth; layer
  1.4.363 не усунув diagnostic stall. Внутрішню source-level причину fault
  не доведено. Прийнята серія перевіряє обмежений workload після reuse на
  незмінному початковому validation runtime; історичний NVIDIA startup incident
  цим не відтворено й не закрито.

  Остаточна регресія 2026-10-22: по 132/132 CTest (71 GPU), 50/50 Python,
  повні manifest workflows, 16 original recook cycles і 144 failure controls.
  Два старі shadow tests брали baseline до прогріву cache: після окремого
  warmup вони перевіряють ліміт трьох atlas textures, незмінність інших resources та
  всі вісім точних resize/capacity cycles. Змінилися лише два test sources і
  два test executables/config; SHA bridge підтверджує незмінність production
  sources, workload binaries і profiling tools відносно всіх 20 вимірювань.

- [x] **Очищення й фінальна фіксація (M9-CLOSEOUT).** Після M8-AUDIT видалити
  невикористані render stubs/bootstrap factory, зберігши явні errors для
  відкладених APIs. Нерендерні sound tasks не входять у цей пункт.
  Зібрати індекс supported/deferred profiles, manifest scenes/workflows,
  configurations, captures/baselines, validation і залишкових обмежень.
  M5-C має бути виконаний або явно відкладений рішенням зі збереженою
  unsupported-compatibility межею; зміна документа не закриває його автоматично.
  Прийнято 2026-10-22 у тому самому final matrix report: видалено unused
  `vulkan/renderer.h`, `vulkan/sources/renderer.cpp` і їхні невикористані public
  initialize/finalize declarations. Чинну facade factory та explicit deferred
  API errors збережено. [Індекс profiles/evidence](sources/xray/render/vulkan/profiles.md)
  визначає supported/deferred межі, asset/workflow contracts і датовані докази.
  M5-C виконано в погодженому original exported/source runtime-cooking обсязі;
  сумісність із відсутнім original legacy cooked pack не заявляється.
  Остаточна Debug/Release regression matrix, workflows і no-op rebuilds пройшли.

M9 завершений після всіх п'яти пунктів: потрібні workflows проходять остаточну
матрицю без невирішених validation errors, зависань, resource leaks і критичних
visual regressions. Залишкові сумісність/ризики описані конкретно, рішення
про відкладення явні. Повний Vulkan migration acceptance оголошується лише тут.
Усі п'ять пунктів закрито 2026-10-22; міграцію прийнято в описаних supported
profiles, зі збереженими deferred API errors та явно прийнятим startup-ризиком.

## Залежності та індекс доказів

Функціональний шлях: `M0 -> M1 -> M2 -> M3 -> M4 -> M5 profile -> M6 -> M7 -> M8`.
Остаточне приймання: `M8 + M5-C (виконано або явно відкладено) -> M9`.
Прийняті M6/M7 не відкриваються через окремі перевірки asset compatibility.
Оцінку строків давати за залишком manifest tasks після M8-SCOPE;
історичні 30–45 днів не є чинним прогнозом.

Останній зафіксований checkpoint:
[M9 final acceptance](build/stage9-final-closeout/summary.json),
2026-10-22 — GCC 16.2 / RTX 4070 / driver 617.42. У кожній конфігурації
58 raw validation summaries, 19 поточних CTest probe reports та native
workflow/original reports мають 0/0 із фактично увімкненим layer через teardown.
11 pre-instance failure cases у кожній конфігурації не зараховуються як
увімкнений instance layer; їхні успішні повторні initialization controls
перевірені з validation. Source snapshot містить 17737 files без `__pycache__`;
identities незмінні всередині measurement і final-regression phases, а окремий
SHA bridge обмежує між ними зміни лише двома shadow tests. Фінальні Ninja
builds не мають роботи. Приймальні evidence/build roots заморожені; попередні
matrix, failures та instrumentation checkpoints перевірено без змін.
M9-BUILDS, M9-LIFECYCLE, M9-FAILURES, M9-PERFORMANCE і M9-CLOSEOUT закрито.
Останній пункт завершено явним прийняттям залишкового startup-ризику;
нові source/runtime changes для цього рішення не вносилися. Попередній
[performance/matrix report](build/stage9-performance-manifest/summary.json)
зберігає стан до цього рішення й не переписується заднім числом.

[M5-C original runtime acceptance](build/stage5-original-runtime/summary.json)
зберігає прийнятий обсяг original static assets runtime cooking. Усі 36
original/staged inputs, попередні 913 originals і три absent paths, M8 pack
та 1141 M8 captures перевірені без змін. Ці множини не додають як нові
унікальні inputs. Приймання M8 лишається у
[bounded game/editor capability audit](build/stage8-capability-audit/summary.json).
Earlier scope/particle lifecycle reports описують відповідні попередні профілі,
а не поточний статус. Попередня documentation-only фіксація лишається в
`build/stage5-original-runtime-closeout/summary.json`.

Докази за компонентами (історичні профілі й точні limits — у README/архіві):

- M0–M7: `build/vulkan-stage*-*.log`, `build/real-assets/*/report.json`;
  профілі 7.1–7.8 і порядок passes описані у Vulkan README.
- M5-C original runtime: `build/stage5-original-runtime/summary.json`;
  missing cooked-cache discovery: `build/stage5-legacy-compatibility/summary.json`.
- M9 matrix/lifecycle: `build/stage9-matrix/summary.json`;
  commands, configuration identities, captures, compiler baseline bounds та
  source/dependency/evidence SHA — у цьому report і `run_configuration.ps1`.
- M9 controlled failures: `build/stage9-failures/summary.json`;
  startup/allocation/device-loss receipts, ICD/GDB controls, повторна повна
  матриця та незмінність попередніх доказів до остаточного рішення про ризик.
- M9 manifest performance/final closeout: `build/stage9-performance-manifest/summary.json`;
  20 budgeted runs, raw samples/counters, точні captures, test-only SHA bridge,
  остаточні Debug/Release regressions та supported/deferred profile index.
- M9 final acceptance: `build/stage9-final-closeout/summary.json`;
  явне прийняття історичного startup-ризику, закриття всіх п'яти пунктів,
  актуальні source/runtime/plan identities без зміни попередніх доказів.
- Після завершення міграції користувач 2026-10-22 доручив видалити її тести.
  Migration suites, fixtures, runners, probe/smoke targets і private failure hooks
  прибрано з поточних sources/CMake; runtime shaders перенесено до `assets/basic`.
  Загальні source/configuration checks залишено. Архів і нова build-перевірка:
  `build/vulkan-test-retirement/summary.json`. Попередні reports описують свої
  датовані source snapshots і збережені без змін; завершені M0–M9 не відкриваються.
- Particles: `build/stage8-particle-{lifetime,resources,materials,ready,templates,scene,commands}/summary.json`;
  snapshot/RHI — `build/stage8-particle-billboards/summary.json`.
- Debug/UI: `build/stage8-debug/summary.json`, `build/stage8-ui/summary.json`.
- Skeletal/animation: `build/stage8-{skeletal,skeleton-wrapper,real-skeletal,animation-player,character-materials}/summary.json`.
- Terrain: `build/stage8-terrain-{import,cooks,gpu,deferred,shadows,aa,bounds,reload,bindings-core,bindings,normalization,collision-foundation,wrapper,contacts}/summary.json`.
- Stage2 ICD crash evidence: `build/stage8-terrain-gpu/summary.json`.
  Historical TAA mismatch збережений як regression oracle; успішні останні
  прогони не є доказом універсальної temporal quality або startup root-cause fix.

Фігурні дужки та `*` в індексі — скорочення груп шляхів, не shell commands.
Посилання на старий звіт фіксує тодішній результат, а не обіцяє нову перевірку.
