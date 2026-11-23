# Історія інтеграції Vulkan до редакції плану 2026-10-17

Це архів попередньої редакції та доказів, а не робочий план.
Єдине джерело поточного статусу, залежностей і незавершених задач —
[vulkan_integration_plan.md](vulkan_integration_plan.md).

Дати, статуси, обмеження, «наступні кроки» та результати нижче описують
момент відповідного запису. Вони не встановлюють поточних задач і не
скасовують пізніших приймань. Зокрема, старі gates для terrain wrapper,
particle templates/materials/scene ownership уже замінені пізнішими профілями.
Числа тестів належать окремим прогонам; їх не додають і не видають за новий запуск.
Повторення в архіві збережені лише для простежуваності попередніх тверджень.

Оригінальну редакцію збережено нижче без змін.

---
# План інтеграції Vulkan у X-Ray 2

Дата: 2026-10-04. План складено за поточними вихідними файлами та конфігурацією CMake.
Оновлено: 2026-10-16, M7 завершено у зафіксованому static-профілі з MSAA/TAA. M8 відкритий; єдиний актуальний checklist і межа завершення наведені в Етапі 8. Terrain wrapper/collision прийнято у bounded immutable translation-only профілі з production contact hook і rotation-locked sphere controls; не universal physics quality. Наступна робота: particles scene/world/resources/submission/lifecycle. Повний actor/terrain image parity, звичайний threaded game/editor startup і решта M8 не заявляються. GCC Debug results і межі наведено нижче; вікна фонові. Історичний одноразовий Stage2 startup crash із NVIDIA ICD failure не заявляється виправленим і належить ризикам Етапу 9.

Фонові тести 2026-10-10: GPU fixtures, asset probe та автоматичний smoke
створюють приховані no-activate/tool/input-transparent windows; навіть під час
minimize/restore alpha залишається 0, resize використовує `SWP_NOACTIVATE`.
Курсор не переміщується, не обмежується та не захоплюється. Замість desktop
`BitBlt` pixel-перевірки читають swapchain через fenced GPU readback до present;
видимість або перекриття іншими програмами не впливають на captures. Capture
увімкнений лише властивістю тестового HWND; звичайний renderer не створює його
буферів і не записує копіювання. Інтерактивний unbounded smoke залишається видимим.
Новий CPU test перевіряє hidden/show/minimize/restore/resize та відсутність
focus/mouse capture; MRT GPU test перевіряє максимум два capture buffers,
їхній розмір після resize, відмову до першого кадру/під час recording і cleanup.
Фінальна GCC/MinGW-w64 Ninja Debug regression: 72/72 CTest (38 GPU),
10/10 Python tests; сім focused tests пройшли по три повтори, RTX 4070 validation
0/0. Журнали: `build/background-tests`. Це обслуговування test infrastructure,
не новий підетап 7 і не закриття 7.8/M7. Windows services/headless sessions,
GCC Release та інші GPU тут не перевірено.

Реальний barrel material-input pack пройшов 16 фонових recook cycles:
stable GPU counts/capacity, drained retirements/cook users, unchanged inputs,
clean shutdown і validation 0/0. RGB усіх 26 captures точно збігаються з попереднім
Stage 7 AA прогоном; BMP SHA-256 відрізняється через нормалізацію невикористаного
alpha-байта до 0. SHA-256 та розміри десяти оригінальних файлів незмінні до/після.
Звіт: `build/real-assets/barrel-radiation-background-gpu/report.json`, порівняння:
`build/background-tests/real-comparison.json`. Це source-derived projection
із synthetic controls, не production cook або приймання оригінальної сцени.

Оновлення toolchain 2026-10-08: поточний Vulkan baseline піднято до 1.4
(CMake, loader, instance і physical device), MSYS2 повністю оновлено: GCC
16.2.0, Boost 1.92.0, Vulkan headers/loader 1.4.363, mimalloc 3.4.4.
Окремий чистий Debug build: `build/vulkan14-gcc16`; старі GCC 15 objects і
Boost cache не використовуються. Початкова перевірка оновлення ще
використовувала bundled Lua 5.4; наступне перенесення на системну Lua
нижче замінює цю конфігурацію. Це обслуговування базової
інтеграції, не новий підетап 7 і не завершення відкритих задач M7.
Для нового Asio локально в network PCH увімкнено Windows NLS declarations,
потрібні для UTF-8 file paths; глобальні OS include policy не змінено.
Початковий прогін розгортав `vulkan-1.dll` у `binaries/`; наступне оновлення
системного runtime нижче замінює цю схему. На цій машині старі implicit OBS/RTSS layers
із manifest API 1.3 вимикаються лише для тестового процесу через їхні
`disable_environment` switches; Khronos validation та її лічильники залишаються
активними. Чиста загальна Debug-збірка GCC 16, усі 46 CTest і 9 Python tests
пройшли; GCC Release ще не перевірено.
Обидва реальні source-derived lit packs (barrel і woodbox) пройшли по 16
циклів із validation 0/0, незмінними baseline/final GPU counts `[6,11,10,2,0]`
та cleanup. Усі 40 RGB captures збігаються з попередніми lit reports;
22 SHA256 оригіналів звірено до/після. Звіти: `build/real-assets/*-vulkan14-msys2-gpu/report.json`.
Окреме відкрите спостереження продуктивності, не новий підетап M7:
woodbox Debug maximum query/reload 8427/8254 ms проти 5049/4800 ms раніше;
причину уповільнення не встановлено, це не контрольований benchmark.
Повні версії пакетів, журнали, caveat локально встановленого mimalloc і
команди актуальної збірки: `sources/xray/render/vulkan/README.md`,
розділ `MSYS2 Toolchain Refresh (2026-10-08)`.

Системний Vulkan 2026-10-08: офіційний підписаний LunarG x64 runtime installer
оновив `%SystemRoot%/System32/vulkan-1.dll` з 1.4.341.0 до 1.4.363.0.
SHA256 і Authenticode перевірено; installer exit code 0, reboot не потрібний.
Локальний `binaries/vulkan-1.dll` і CMake copy rule видалено. MSYS2 headers та
import library залишаються залежностями GCC, але пакетна DLL не розгортається.
Тест `xray_vulkan_system_loader` перевіряє API 1.4 і фактичний системний шлях
завантаженого модуля. GPU driver не змінювався; це обслуговування інтеграції,
не новий підетап M7. Журнали й підтвердження: `build/system-vulkan`.
Перевірено GCC Debug build, 49/49 CTest (21 GPU) та 9/9 Python tests.
Негативний тест відхиляє app-local shadow DLL; тимчасову копію видалено.
Barrel і woodbox пройшли по 16 циклів із validation 0/0, стабільними counts
`[6,11,10,2,0]` та cleanup. Обидва probe processes завантажили System32 DLL.
Усі 40 RGB captures збігаються з попереднім чистим build, 22 SHA256 оригіналів
не змінилися. Звіти: `build/real-assets/*-system-vulkan-gpu/report.json`.
Це не підтвердження full game/editor startup або GCC Release.

Перенесення Lua 2026-10-08: активна CMake-збірка використовує системну
MSYS2 MINGW64 Lua 5.5.1 (`lua55.dll` / `liblua.dll.a`), а не bundled C sources.
Оновлення пакета не потрібне: встановлено актуальну 5.5.1-1.
Проєктний snapshot sol2 3.5.0-2 у `sources/third_party/sol2` містить
контрольований патч нових state/GC API та системного include discovery;
заголовки MSYS2 не змінено. CS wrappers і всі sol2 consumers отримують
однаковий Lua ABI. Тест включає CS wrappers перед
sol2, щоб header guards не приховали підмішування старого API.
Мінімум CMake 4.3; підтримується лише Lua 5.5.1+
у межах 5.5, без мовчазного переходу на майбутню 5.6.
Новий чистий build: `build/lua55-gcc16`; Lua DLL не копіюється у `binaries/`,
потрібен `C:/msys64/mingw64/bin` у PATH. Lua 5.4 bytecode потрібно
перекомпілювати, нативні Lua modules перебудувати під 5.5; сумісність усіх
game/editor scripts не оголошується.
Розширено `xray_lua_sol_binding`: ABI, config tables, errors, userdata/GC,
coroutines, instruction hook, custom allocator і новий GC API.
Це заміна залежності в існуючій інтеграції, не новий підетап M7;
канонічні 7.1-7.8 та їхні критерії завершення не змінюються.
Перевірено: чиста загальна GCC 16 Debug-збірка, 46/46 CTest (21 GPU),
9/9 Python tests із системною Lua. PE imports CS/core/renderer/test використовують
`lua55.dll`. Прямі тести CS wrappers не приховують ABI через include order.
Barrel і woodbox пройшли по 16 циклів із validation 0/0, стабільними counts
`[6,11,10,2,0]`, drained retirements/cook users і shutdown; усі 40 RGB captures
збігаються з попередніми Vulkan 1.4 reports, 22 SHA256 оригіналів звірено
до/після. Звіти: `build/real-assets/*-system-lua55-gpu/report.json`, журнали
збірки й тестів: `build/lua55-gcc16`. Woodbox maximum query/reload цього
прогону 5341/3274 ms; це не контрольований benchmark і не встановлення
причини попереднього уповільнення. GCC Release та повний game/editor/debugger
startup цим перенесенням не підтверджено.

Очищення платформи й залежностей 2026-10-08: поточна конфігурація лише
Windows x64; Linux залишається майбутнім портом, не опцією збірки.
Платформні заголовки перевіряють OS, архітектуру та pointer ABI; прибрано
консольні гілки, старі project-файли та невикористовувані дерева залежностей.
Видалено 7351 файл, включно з 742 файлами старих проєктів/метаданих.
Окремо прибрано 1523 старі build artifacts. За наступним запитом користувача
їхню ZIP-копію також видалено, разом з іншими legacy `.orig`/`.old` копіями
та backup-layout Visual Studio: 23 файли, без створення нових бекапів.
Журнал: `build/platform-cleanup/backup-removal-manifest.json`.
Збережено залежності, які реально входять у compiler graph, зокрема bundled
Boost headers: системні Boost libraries самі по собі їх не замінюють.
За окремим рішенням користувача незібрані DX11/editor SDK adapters залишено
як reference code для перенесення функцій, не як підтримувані targets.
Перевірка виявила сім допоміжних targets без `sol2::sol2` usage requirements;
їх виправлено, додано явну залежність також для fs і compile-time guards.
Тому попереднє твердження про відсутність дубльованих Lua headers у всьому
старому compiler graph було неточним. Новий чистий build:
`build/windows-x64-clean`; контракти й область підтримки: `sources/README.md`.
Журнал видалення: `build/platform-cleanup/removal-manifest.json`.
Тимчасова sandbox-копія видаленого не збереглася після завершення команди;
відновлюваного архіву немає. Це очищення не змінює канонічні 7.1-7.8.
Перевірено: повна чиста GCC 16 Debug-збірка, 48/48 CTest (21 GPU),
9/9 Python tests; шість негативних compiler checks і відмова Linux configure.
Новий повний compiler graph не містить видалених дерев, дубльованих Lua
headers або системного sol2; PE imports п'яти перевірених consumers: `lua55.dll`.
Barrel і woodbox пройшли по 16 lit-циклів із validation 0/0, стабільними
counts `[6,11,10,2,0]`, drained cook users/retirements і completed shutdown.
Усі 40 RGB captures збігаються з system-Lua55 reports; SHA256 усіх 22
оригіналів незмінні. Звіти: `build/real-assets/*-windows-x64-clean-gpu/report.json`.
Woodbox maximum query/reload 8306/8357 ms; як і попередні прогони, це
не контрольований benchmark і не встановлення причини варіативності часу.
GCC Release та повний game/editor/debugger startup цим очищенням не перевірено.

Мета: отримати робочий рендеринг через актуальний facade, resource system та цикл кадру рушія, потім відновити функції існуючого DX11 renderer на Vulkan.

Цільовий C++ toolchain: **GCC / MinGW-w64 + Ninja**, конфігурації Debug і Release. MSVC не входить у scope цього перенесення, не є майбутнім етапом або критерієм приймання. Актуальний чистий `build/windows-x64-clean` використовує `C:/msys64/mingw64/bin/c++.exe` і Debug; `build` у старих журналах/командах є історичним каталогом. DXC використовується окремо для HLSL -> SPIR-V, не як C++ compiler.

Початковий стан збірки до Етапу 0: `cmake --build build --target xray_render_vulkan stalker2 -j 2` завершувався з `ninja: no work to do`. Після Етапів 0-4 проходила загальна Debug-збірка `cmake --build build -j 8` і 12 focused-тестів CTest, включно з реальними scene resource queries, DXC, clear/geometry/material/scene GPU tests та synchronization validation. Збережено журнали й знімки clear-кадру, трикутника, indexed mesh, матеріалів і сцени до/після зміни камери. Оновлена чиста збірка GCC 16/Vulkan 1.4 перевірена 2026-10-08; GCC Release ще не перевірено.

## 1. Вихідний стан і напрям інтеграції

Вже написані RHI-інтерфейс, Vulkan device/surface/swapchain, render pass, depth attachment, command buffers, кадрова синхронізація, початкові pipeline state та DXC wrapper. Цю основу потрібно довести й підключити, а не створювати повторно.

Головні прогалини на момент складання плану:

- `render/base/sources/vulkan_stubs.cpp`: `world::initialize()`, `draw_scene()`, `end_frame()` та більшість операцій сцени порожні; `frame_id()` повертає нуль.
- `render/vulkan/sources/vulkan_backend.cpp`: створення buffers/textures/shaders повертає невалідні handles; binding ресурсів порожній; descriptors не заповнюються; vertex input відсутній.
- `render/core/sources/rhi/rhi_device.cpp`: результат ініціалізації бекенда ігнорується; ownership та звільнення самого бекенда не визначені.
- `render/vulkan/sources/vulkan_resources.h`: перше виділення зі slot 0 і generation 0 утворює handle 0, який дорівнює `RHI_INVALID_HANDLE`.
- `render/engine/sources/render_output_window.cpp` та `render/core/res_render_output.h`: output і його targets залежать від DX11.
- Існуючі scene/model/material cooks та render stages не підключені до Vulkan через RHI.

Актуальний шлях запуску:

```text
engine_world::create_render()
  -> render::create_world(logic_allocator, editor_allocator)
  -> facade render::world
  -> engine::create_world()
  -> engine::world

engine_world::initialize()
  -> engine_renderer().initialize()
  -> engine::world::initialize()

game::query_scene()
  -> scene_class + scene_view_class + render_output_window_class
  -> output cook отримує HWND у user_data

facade scene_renderer / one_way_render_channel
  -> engine::world::draw_scene(...)
  -> engine::world::end_frame()
```

Сам виклик Vulkan зі старої фабрики `dx10::create_render_platform()` не забезпечує інтеграцію цього шляху. Фабрика зараз повертає `nullptr`.

Рішення для реалізації:

- Зберегти зовнішній facade, resource class IDs та порядок команд logic/render threads.
- Використати існуючий RHI як межу GPU API, розширюючи його лише під наступний конкретний milestone.
- Винести реальні методи Vulkan world у власний translation unit. Видаляти відповідні визначення з `vulkan_stubs.cpp` одночасно з їх реалізацією.
- Поступово адаптувати спільні resource wrappers і stages. Не підключати DX11 translation units у Vulkan target без підготовки їхніх залежностей.
- Для першого результату підтримати одне вікно; перед інтеграцією редактора перейти до output handles і окремого swapchain на вікно, зі спільним device.
- Розрізняти три результати: кадр smoke-режиму, робоча сцена гри та повна функціональна відповідність DX11.

Наведені нижче шляхи відносні до `sources/xray/`, якщо не зазначено інше. Нові назви файлів є пропозиціями.

## Етап 0. Узгодити збірку, ownership та контракт кадру

Передумови: немає.

Статус: усі сім підготовчих задач нижче реалізовано. Renderer переведено у shared-модуль: static-бібліотека дублювала globals і RHI singleton між EXE/DLL. Перевірка символів підтвердила єдиного власника в renderer DLL та імпорти в engine, editor_base і stalker2.

Основні файли: `render/vulkan/CMakeLists.txt`, `engine/CMakeLists.txt`, `render/core/sources/rhi/rhi_device.*`, `rhi_backend.h`, `render/vulkan/sources/vulkan_resources.h`, `vulkan_memory.cpp`.

- [x] Визначити єдиного власника facade/base/world translation units. Прибрано дублювання зі списків engine; актуальний facade і world належать renderer DLL, несумісні legacy base implementations виключено.
- [x] Узгодити renderer compile definitions для всіх спільних заголовків і translation units, які їх використовують. Renderer публікує `USE_VULKAN_RENDERER=1`, `XRAY_RENDER_SHARED=1`; building/export flags залишаються private.
- [x] Окремо перевірити `USE_VULKAN_RENDERER=OFF`: CMake явно відхиляє конфігурацію як непідтриману, а не удає DX11 fallback.
- [x] Задати ownership бекенда та порядок `initialize -> shutdown`. RHI володіє `unique_ptr`, повертає bool, прибирає частково ініціалізований backend і блокує кадрові виклики після failure.
- [x] Підключити render/logic/editor та Vulkan allocators. Callbacks забезпечують alignment, збереження даних при realloc і початкового блока при allocation failure; teardown виконується до reset allocator.
- [x] Виправити генерацію handles: перший валідний handle не дорівнює нулю; stale/released handle не приймається після reuse slot або clear.
- [x] Визначити явний контракт `begin_frame -> render commands -> end_frame/submit -> present`. `present()` більше не починає наступний кадр; RHI перевіряє порядок і прибирає backend після фатальної кадрової помилки.

Критерій завершення: існуюча збірка проходить; focused-тести перевіряють перший handle, stale handle і reuse slot; помилка initialize передається до world; порядок ownership і кадрових викликів зафіксований у коді.

Перевірено: загальна Debug-збірка MinGW/Ninja; `xray_render_stage0` і `xray_render_stage0_runtime` (2/2); очікувана configure-помилка з `USE_VULKAN_RENDERER=OFF`. Додатково усунуто DLL-startup crash через CRT overrides у shared core та teardown неініціалізованого fixed-size allocator. Sound command implementations перенесено до sound-модуля, щоб прибрати дублювання зі stubs.

Залишок критерію приймання закрито в Етапі 1: актуальний world/facade повертає bool initialization; CPU-тест перевіряє null/invalid HWND, відсутність ready-state/backend після failure та повторний teardown. GPU smoke перевіряє відхилення подвійної initialization й повторну initialization після cleanup. Контракти та команди перевірки: `sources/xray/render/vulkan/README.md`.

## Етап 1. Перший Vulkan-кадр через актуальний facade

Передумови: етап 0.

Статус: реалізовано та прийнято для перевіреної Debug-конфігурації на RTX 4070. Smoke є окремим мінімальним bootstrap у engine, а не підтвердженням готовності повного запуску гри.

Основні файли: `engine/sources/engine_world_initialize.cpp`, `render/facade/engine_renderer.h`, `render/facade/sources/engine_renderer.cpp`, `render/engine/world_dx11.h`, новий `render/vulkan/sources/vulkan_world.cpp`, `vulkan_backend.*`.

- [x] Додати мінімальний smoke-режим рушія: `xray_vulkan_smoke`, реальне Win32-вікно, актуальний facade та Vulkan world, без запуску повного `game::query_scene()` до готовності cooks. Цей режим перевіряє bootstrap, а не підтверджує запуск гри.
- [x] Передати HWND від engine через facade до Vulkan initialization; не використовувати стару фабрику платформ як єдину точку входу.
- [x] Реалізувати `world::initialize()`, кадрові виклики, `end_frame()`, `frame_id()`, `clear_resources()` і teardown для smoke-шляху. CPU-кадри просуваються під час minimize, GPU present пропускається. Виправлено перевернутий прапорець deferral у двох facade functor commands, який спричиняв `bad_function_call`.
- [x] Додати validation layer і debug messenger у Debug, з перевіркою доступності layer та extensions і зрозумілим повідомленням, якщо їх немає. Strict mode: `XRAY_VULKAN_REQUIRE_VALIDATION=1`; synchronization validation увімкнено через доступний layer extension.
- [x] Перевіряти GPU suitability: Vulkan 1.4 loader/device (початковий baseline був 1.3), graphics/present queues, device extensions, surface formats, color usage та present-fence feature. Вибирати discrete GPU серед придатних кандидатів. CMake також вимагає development files >= 1.4; silent 1.3 fallback немає. Instance target, loader, device та headers мають окрему діагностику. DXC SPIR-V target 1.3 залишається сумісним із runtime 1.4, не є версією instance або драйвера.
- [x] Зробити shutdown і cleanup безпечними після часткової ініціалізації; знищувати pipeline layouts, pools, pipelines та інші вже створені об'єкти до device.
- [x] Перевірити синхронізацію двох frames in flight: окремий depth attachment і present semaphore на swapchain image, submit fences для rendering та maintenance present fences для presentation/teardown.
- [x] Обробити minimize/нульовий extent, resize, `OUT_OF_DATE`/`SUBOPTIMAL`, зміни attachment formats і submit failure без повторного очікування fence від невдалого submit. Перевіряти результати командних Vulkan-викликів. Recreation перебудовує render pass, attachments/framebuffers і залежні pipelines; fence reset відкладено до submit.

Критерій завершення: smoke-режим показує стабільний clear color; лічильник кадрів зростає; resize/minimize/restore і повторний запуск проходять без validation errors та зависань. Зберегти GPU name, конфігурацію, журнал і знімок кадру.

Перевірено: повна поточна Debug-збірка MinGW/Ninja; 4/4 CTest (`stage0`, `stage0_runtime`, `stage1_facade`, opt-in `stage1_gpu`). NVIDIA GeForce RTX 4070, Vulkan 1.4.351, driver 617.14. Перший прогін має 180 представлених кадрів, resize 942x593 -> 1102x673, minimize/restore та перевірку пікселів clear color; після cleanup той самий world ініціалізовано згорнутим, відновлено й представлено ще 4 кадри. В обох сесіях validation errors/warnings: 0/0. Повторні запуски executable також пройшли. Докази: `build/vulkan-stage1-validation.log`, `build/vulkan-stage1-frame.bmp`, `build/Testing/Temporary/LastTest.log`.

Межі приймання: Stage 1 вимагає `VK_EXT_surface_maintenance1` + `VK_KHR_get_surface_capabilities2` і device `VK_EXT_swapchain_maintenance1` з feature; непідтримані GPU явно відхиляються, небезпечного WaitIdle-only fallback немає. Інші GPU, clean GCC Release, фізичний device loss/submit failure та всі WSI error/format-change варіанти не перевірено. Failure rollback покрито mock RHI-тестами; реальні ресурси/геометрія/сцени залишаються Етапом 2+. Команди запуску й portable validation setup: `sources/xray/render/vulkan/README.md`.

## Етап 2. Шейдери, vertex/index buffers та перша геометрія

Передумови: етап 1.

Статус: реалізовано для перевіреної Debug-конфігурації. Геометрія проходить актуальний facade/world smoke-шлях; підключення resource cooks і матеріалів гри ще попереду.

Основні файли: `render/core/sources/rhi/shader_compiler.*`, `rhi_types.h`, `rhi_backend.h`, `render/vulkan/sources/vulkan_backend.*`, `vulkan_resources.h`, `tools/shader_compiler/main.cpp`.

- [x] Додати CMake target `xray_shader_compiler`. DXC runtime береться поруч з EXE або з абсолютного `XRAY_DXC_PATH`; load failure містить Win32 error та спосіб налаштування.
- [x] Узгодити named entry points, stage profiles SM 6.0, source-relative/explicit includes, defines, HLSL 2021 та SPIR-V Vulkan 1.3. VS smoke використовує Y inversion; перевірено winding/viewport.
- [x] Реалізувати shader modules з перевіркою розміру, SPIR-V header/instruction bounds, stage/entry і підтриманих capabilities; зберігати actual entry у pipeline. Destroy анулює handle відразу. Це structural/capability check для trusted DXC output, не повний validator довільних binaries.
- [x] Реалізувати immutable vertex/index buffers, upload і memory allocation: host-visible/coherent або explicit flush non-coherent allocation. Дані готові до submission; transfer/staging для цього шляху не потрібні. Device-local staged uploads/updates залишено подальшим оптимізаціям.
- [x] Додати single-binding vertex layout, stride, locations/formats/offsets і uint16/uint32 index binding до RHI. Bind і draw перевіряють тип, generation, alignment і ranges; indexed draw перевіряє доступні vertices за CPU-копією індексів.
- [x] Pipeline key містить input layout, shader generations, фактичні attachment formats/sample count і всі змінні raster/depth/blend/topology параметри. Cache використовує повну рівність descriptors; forced-collision CPU-тест зберігає різні pipelines окремо.
- [x] Deferred destruction buffers/modules/залежних pipelines за submit serial та frame fences, включно з ресурсами вже записаного, але ще не submitted кадру. Handle pool release більше не алокує, а failed-copy reuse зберігає вільний slot.

Критерій завершення: спочатку кольоровий трикутник, потім indexed mesh зі справжніх buffers. Геометрія має коректні winding, viewport, depth і координати; створення/видалення ресурсів проходить без validation errors.

Перевірено: повна поточна Debug-збірка; 7/7 focused CTest. GPU: RTX 4070 / Vulkan 1.4.351 / driver 617.14; DXC `v1.9.2609`. Geometry smoke представляє 180 кадрів, проходить resize/minimize/restore, чергує uint16/uint32 індекси, перевіряє пікселі орієнтації трикутника й indexed/depth кадру. Destroy/recreate під час recording відхиляє stale handles, потім world повторно ініціалізується згорнутим і представляє ще 4 кадри. Validation errors/warnings: 0/0 в обох сесіях; clear-only Stage 1 також проходить. Докази: `build/vulkan-stage2-validation.log`, `build/vulkan-stage2-frame.bmp`, `build/vulkan-stage2-frame.bmp.triangle.bmp`.

Межі на завершення Етапу 2: один interleaved vertex binding, immutable buffers, базові descriptor-free VS/PS. Текстури/constants/descriptors додано в Етапі 3 нижче; compute/geometry/tessellation, resource cooks і scene assets ще не підключено. GCC Release, інші GPU, full semantic validation довільного SPIR-V та примусове hardware allocation/device loss не перевірено. Команди запуску й DXC setup: `sources/xray/render/vulkan/README.md`.

## Етап 3. Текстури, samplers, constants та descriptors

Передумови: етап 2.

Статус: реалізовано та прийнято для Debug MinGW/Ninja на RTX 4070; всі вісім задач виконано для конкретного тестового material ABI v1. Це ще не підключення матеріалів гри.

Основні файли: `rhi_backend.h`, `rhi_types.h`, `rhi_enums.h`, `vulkan_backend.*`, `vulkan_resources.h`, `shader_compiler.*`.

- [x] Додано `texture_desc`, subresource uploads із mip/layer/row pitch, sampler state, create/update/destroy API та RHI device wrappers. Непідтримані type/usage/layers явно відхиляються.
- [x] Реалізовано sampled 2D RGBA8 UNORM/SRGB images/views, device-local memory, staging copy кожного mip та transitions до shader-read layout. Updates створюють нову GPU-версію; стара залишається до завершення recorded/pending draws. Samplers підтримують nearest/linear і repeat/clamp/mirror.
- [x] Logical constant buffers оновлюються на CPU; кожен draw отримує власні aligned slices у persistently mapped per-frame uniform arena. Non-coherent memory явно flush; storage перевикористовується лише після slot fence.
- [x] HLSL `vk::binding` і register spaces узгоджені з трьома descriptor sets; sampled image і sampler мають окремі bindings. Descriptor-free Stage 2 shaders залишаються підтриманими.
- [x] Frame set 0, material set 1, object set 2; SPIRV-Reflect перевіряє bindings/type/count, 2D image, sizes/offsets/member names та matrix layout constants до створення shader module. Upstream зафіксовано з ліцензією та SHA256.
- [x] Реалізовано constant/texture/sampler bindings, immutable descriptor snapshots, `vkUpdateDescriptorSets` перед draw; handle 0 використовує identity constants, white texture та default sampler.
- [x] Per-frame pools розширюються після exhaustion, reset виконується після GPU fence; images/samplers мають deferred destruction. Stress із 163 draws перевіряє щонайменше три pools і два uniform chunks у кожному slot, потім стабільне перевикористання без накопичення.
- [x] Додано bounded in-memory cache на 64 успішні компіляції для кожного compiler instance. Ключ включає source, stage/profile, entry, include paths, macros, всі flags та cwd; DXC include handler відстежує вміст прямих/вкладених includes і failed probes. Перевірено invalidation, новий include вищого пріоритету, eviction та recovery після compile failure.

Критерій завершення: текстурований mesh рухається через view/projection/world constants; видно зміну текстури та sampler state; кілька матеріалів у кадрі не обмінюються ресурсами помилково.

Перевірено 2026-10-05: повна поточна Debug-збірка та 10/10 focused CTest. Material smoke представляє 240 кадрів через facade/world/channel, проходить resize/minimize/restore, cleanup і повторну initialization згорнутого world із чотирма GPU-кадрами. Pixel checks підтвердили рух, незалежні матеріали зі спільним оновлюваним object buffer, white fallback, зміну texture palette, linear/clamp -> nearest/repeat sampler та mip 1. Texture/sampler destroy/recreate після recorded draw відхиляє stale handles. RTX 4070 / Vulkan 1.4.351 / driver 617.14; DXC v1.9.2609; validation errors/warnings 0/0 в обох сесіях. Етапи 0-2 також пройшли.

Докази: `build/vulkan-stage3-validation.log`, `build/vulkan-stage3-frame.bmp`, `build/vulkan-stage3-frame.bmp.before.bmp`, `build/vulkan-stage3-frame.bmp.mip.bmp`, `build/Testing/Temporary/LastTest.log`. Команди та ABI: `sources/xray/render/vulkan/README.md`.

Межі: один sampled 2D layer, тільки RGBA8 UNORM/SRGB, explicit complete mip uploads, blocking upload fence, fixed material ABI, без anisotropy/storage/RT/arrays/cubes та automatic mip generation. Cache process-local, не disk cache. Vertex/index updates, resource cooks, real scene/material assets і довільні effect layouts залишаються наступним етапам. Clean GCC Debug/Release, інші GPU та hardware failure injection не перевірено.

## Етап 4. Resource cooks, render output та scene lifecycle

Передумови: етапи 1-3.

Статус: завершено для одновіконного тестового профілю. Штатний `query_create_resources()` створює Vulkan scene/view/output, facade передає матриці та draw commands, world показує тестову геометрію етапу 3. Звичайний `game::query_scene()` з terrain/particle flags залишається непідтриманим і завершується resource error; models/material assets належать етапу 5.

Основні файли: `render/engine/sources/render_engine_world_dx11.cpp` як джерело існуючої логіки; `scene_cook.*`, `scene_view_cook.*`, `render_output_window_cook.*`, `render_output_window.*`, `scene_manager.*`; `render/core/res_render_output.h`; `game/sources/game.cpp`.

- [x] Відокремити реєстрацію cooks і спільну scene lifecycle від DX11 device/effect singletons. Реєстрація потрібних cooks має відбуватися до запитів logic thread.
- [x] Адаптувати output wrapper до Vulkan: HWND, dimensions, surface/swapchain ownership, resize і present; прибрати залежність Vulkan-шляху від `DXGI_SWAP_CHAIN_DESC` та `ID3D*`.
- [x] Розділити спільний GPU device та output-specific state. Передати створений для smoke-вікна output у resource ownership або замінити його, не створюючи два активні swapchains для одного HWND.
- [x] Підключити Vulkan-сумісні `scene_class`, `scene_view_class`, `render_output_window_class`. Перевірити constructors спільних scene/view classes, щоб вони не створювали DX11 targets/effects неявно.
- [x] Зберегти class IDs, resource pointer contracts і callbacks `game::query_scene()`. Перевірити фактичні downcasts у споживачів перед заміною concrete classes.
- [x] Відновити deferred command queues для scene/view, матриці камери, viewport, `window_client_size()` та завершення `on_draw_scene` callback.
- [x] Забезпечити коректну помилку або явно обмежену конфігурацію для ще непідтриманих terrain/particle features. Для першої сцени використовувати спеціальний тестовий профіль; не позначати звичайний game startup як готовий, поки його залежності відсутні.
- [x] Перевірити destroy cooks і registry ownership. У поточному `render_output_window_cook::destroy_resource()` викликається `add_render_output_window()`; при підключенні виправити його на видалення з registry.

Критерій завершення: через штатний resource query створюються scene/view/output; facade надсилає команди, Vulkan world їх виконує й показує тестову геометрію. Destroy/recreate сцени та output завершує callbacks і не залишає dangling registry entries.

Реалізація: `render/vulkan/scene_resources.h`, `vulkan_scene_resources.cpp`, `vulkan_scene_runtime.*`, `vulkan_presentation_output.h` та `vulkan_world.cpp`. Resource output ділить bootstrap presentation state з backend; backend одноосібно знищує GPU handles після fences. Видалення/повторний cook output не створює нового swapchain. Registry не володіє resource pointers; закриті cooks залишаються чинними до завершення queries і delayed deletions. `finalize_scene_resources()` викликається на потоці реєстрації до завершення resource manager. Повторний `clear_resources()`/деструктор world повторює спробу unregister після drain.

Виправлено сильні `next_scene`/`next_scene_view` links при зміні порядку малювання і registry removal у legacy output cook. Додано `variant::get_if<T>()`: RTTI-перевірка userdata не залежить від порядку присвоєння числових type IDs у EXE/DLL і не викликає assert на невідповідному типі; layout та legacy `try_get()` не змінено.

Перевірено 2026-10-05: повна поточна Debug MinGW/Ninja-збірка та 12/12 focused CTests. `stage4_resources` використовує справжній resource manager у `-debug_single_thread` режимі, перевіряє типи userdata, unsupported features, duplicate/foreign output, порядок scene/view lists, drain і destroy/recreate. `stage4_gpu` перевіряє resource queries через facade, deferred depth/overlay/view phases (також minimized), camera/viewport pixels, resize, спільний swapchain без дублювання, pending callback при shutdown, відмову reinitialize з live cook users, повторний world та cleanup після детермінованого зникнення backend. На RTX 4070: 0 validation errors / 0 warnings у всіх перевірених GPU sessions. Знімки переглянуто; журнал: `build/vulkan-stage4-validation.log`, кадри: `build/vulkan-stage4-frame.bmp` і `.before.bmp`.

Не перевірено цим milestone: багатопотоковий повний game startup, GCC Release, інші GPU та реальна device-loss ін'єкція. Debug/UI GPU-команди, game assets і multi-window editor залишаються наступними етапами.

## Етап 5. Моделі, матеріали та перша сцена з assets

Передумови: етап 4.

Статус: завершено 5.1-5.9, acceptance tooling 5.10 та явно запитану користувачем пряму підтримку TGA 5.11. Підтриманий unlit static preview перевірено на двох справжніх моделях із `C:\Stalker2_2011` як із DDS, так і без DDS, з оригінальними TGA/options. Геометрія, UV, індекси та diffuse-пікселі справжні, не fixtures. Строге приймання готового legacy cooked pack без offline-адаптації геометрії/матеріалу ще відкрите: повний оригінальний material явно відхиляється. Це зафіксована межа сумісності, не підстава додавати довільні нові підетапи.

### Фіксовані межі завершення Етапу 5

Етап 5 має скінченний обсяг: **cooked static model/material + DDS або TGA texture + opaque/cutout unlit diffuse preview**, а не весь legacy renderer. TGA/options loading додано за прямим запитом користувача, без включення source geometry/material cooker. Він завершується лише після виконання всіх п'яти умов:

1. Обраний зовнішній pack ідентифіковано каталогом, model paths та відбитками файлів. Cooked модель/settings/materials та DDS або TGA/options завантажуються звичайними VFS/resource queries; приймальний запуск не генерує і не змінює вхідні assets. TGA декодується й отримує mip-chain лише в пам'яті, без запису cooked cache. Принаймні одна поверхня використовує справжній material і texture, не лише white fallback.
2. Активний static-шлях використовує RHI для VB/IB, layout, shader/constants, texture і sampler; CPU geometry bounds, locators та facade contracts збережені. Потрібні лише wrappers, які реально використовує цей шлях. Масове перенесення всіх D3D-oriented shared wrappers не є умовою Етапу 5.
3. Працюють facade add/update/remove, named visibility/material assignment, камера, frustum culling і depth. Контрольні кадри підтриманого opaque/cutout preview не порожні та візуально прийняті. Це не DX11 GBuffer/lighting parity.
4. Щонайменше 16 повних unload/recook циклів обраної моделі, unchanged texture reload та наявні mutation/snapshot/shutdown регресії проходять без накопичення cook users і live/retired GPU resources. Baseline враховує документовані renderer constant buffers, скінченний shader cache та per-frame descriptor/uniform capacity після прогріву; наступні цикли не можуть його збільшувати.
5. Повний focused CPU/DXC/GPU suite проходить; приймальний GPU запуск має **0 validation errors і 0 warnings**, звіт та кадри. Fixture self-test інструмента не закриває пункт приймання зовнішнього пакета.

**Поза межами:** MRT/GBuffer, lighting, shadows, PBR/normal maps, postprocess, alpha blending, terrain, skeleton, particles, source geometry/material conversion, запис production texture cook/cache, повний game/editor startup, multi-window та всі невикористані legacy wrappers. Це робота етапів 6-9. Пряме TGA loading і in-memory mip generation є вузьким винятком 5.11 за запитом користувача; інші formats/effects не додаються як безкінечний backlog.

2026-10-07: у `C:\Stalker2_2011\xr2-dsgn\resources` знайдено справжні exported VFS-архіви моделей, Lua settings/materials і TGA textures, але не готовий `platforms/pc_dx11` cooked cache. Generated контрольні packs і далі є лише self-tests. Окремий offline-скрипт підготував два source-derived packs із реальних даних; Vulkan probe їх не змінює. Unlit material є явно задокументованою проєкцією власних diffuse/alpha/cull параметрів оригіналу, а не підтвердженням сумісності всього production material.

Основний checklist (поточний стан; підетапи нижче містять деталі й історію перевірок):

- [x] Підключити підтриманий static model format, input layout і VB/IB через resource system та RHI: 5.1-5.3. Opaque unlit base-color pass реалізований; legacy deferred effect не портований.
- [x] Реалізувати `add_model()`, `update_model()`, `remove_model()`, material assignment і visibility з правильним lifetime команд: 5.1-5.4.
- [x] Додати solid/textured shader permutations, mapping підтриманих material state/constants і reflection ABI перевірки: 5.4 і 5.9. Cutout/opaque permutations активного static HLSL перевірені окремо; повний набір production shaders відсутній і не портований.
- [x] Підключити transforms, camera culling і depth для підтриманих static models: 5.1 і 5.5.
- [x] Model buffer updates із GPU lifetime protection для single-surface static instances: 5.6. Multi-surface unnamed updates і зміна розміру/topology count не підтримуються.
- [x] Texture reload із GPU lifetime protection для підтриманих disk-backed cooked DDS (5.7) і TGA/options (5.11). Memory-created/archived texture reload і запис source-to-cooked cache не включені.
- [x] Legacy DDS DXT1/DXT1a, DXT3 і DXT5 як BC1/BC2/BC3 UNORM/SRGB, block-aware mip upload і format-changing reload: 5.8.
- [x] Alpha-test/UV material constants, solid/textured opaque/cutout variants і opt-in SPIR-V demote capability: 5.9. Це unlit diffuse preview, не full deferred effect чи alpha blending.
- [x] Окремий read-only acceptance probe для наявного cooked каталогу: 5.10. Generated self-tests перевіряють інструмент, не закривають production acceptance.
- [x] Пряма підтримка TGA source textures та options, DDS-пріоритет, in-memory mip-chain, reload/rollback та перевірка справжніх assets без DDS: 5.11, явно запитано користувачем.
- [x] Визначити скінченний supported static-шлях та його RHI/CPU contracts. Невикористані D3D-oriented shared wrappers перенесено за межі Етапу 5; це уточнення обсягу, не твердження про завершення їхнього порту.
- [ ] Зафіксувати зовнішній cooked pack і перевірити, чи всі його обов'язкові inputs належать підтриманому піднабору; відсутність пакета не означає потреби додавати довільні formats/effects.
- [x] Перевірити реальну геометрію, власні diffuse-textures і unlit-проєкцію матеріалів через resource system/facade, camera movement і повторне завантаження без накопичення GPU-ресурсів: скриня та бочка, по 16 циклів. Це source-derived packs, не готовий legacy cook.
- [ ] Пройти строге приймання готового зовнішнього cooked pack без offline-проєкції матеріалу; поточний full-material negative test документує непідтримуваний render stage.

Основні файли активного Vulkan шляху: `render/vulkan/model_resources.h`, `material_resources.h`, `sources/vulkan_model_cook.cpp`, `vulkan_material_cook.cpp`, `vulkan_scene_runtime.cpp`, `vulkan_static_renderer.cpp`. Спільна інтеграція: `render/core/resource_manager.*`, `untyped_buffer.*`, `res_texture.*`, `res_geometry.*`, `res_input_layout.*`, `effect_manager.*`, `effect_compiler.*`; legacy cooks: `render/engine/sources/model_cooker.*`, `render_model_cooker.*`, `texture_cook.*`, `material_cook.*`, `material_effects_instance_cook.*`.

Підетап 5.1: static surface із пам'яті (завершено).

- [x] Безпечне читання наявного single-surface `converted_model`: chunks 3/4, точний `static_decl`, 32-byte packed vertices, uint16 triangle indices, CPU bounds; без D3D API залежності.
- [x] `static_render_model_instance_class` cook у тому самому resource session; delayed deletion і finalization враховують live model cook users.
- [x] `add_model`/`update_model`/`remove_model`, `apply_transform`, visibility поверхні `default`, незмінний snapshot на `draw_scene`; Vulkan VB/IB та opaque unlit shader із reflection ABI v1 і white/default texture.
- [x] CPU rejection tests та GPU/pixel tests через facade, camera movement, visibility/removal, snapshot lifetime і 16 recook cycles без накопичення live/retired GPU resources.

Перевірено 2026-10-05: повна Debug MinGW/Ninja-збірка, 14/14 focused CTests етапів 0-5; RTX 4070, 0 validation errors / 0 warnings. Нові тести використовують справжній resource manager із `query_create_resources`, але fixture генерується у форматі конвертера, а не завантажується з game assets/VFS. Журнал: `build/vulkan-stage5-validation.log`; кадри: `build/vulkan-stage5-frame.bmp` і `.before.bmp`.

Підетап 5.2: mounted disk surface/DDS і descriptor material (завершено).

- [x] Завантаження single converted surface і DDS через справжній physical mount/VFS та `query_resource`, а не лише `query_create_resources`.
- [x] `texture_class` cook: bounded DDS RGBA8/BGRA8 2D, tight rows, mip chain, optional sRGB; malformed/unsupported input завершує query з помилкою. На завершення 5.2 BC/DX10/cube/array/volume DDS не підтримувалися; BC1/2/3 додано в 5.8, решта залишається непідтриманою.
- [x] `material_class` cook із typed Vulkan descriptor: strong texture ownership, tint і sampler; `set_model_material` для `default`, заміна/reset та strong material snapshot на queued draw. На завершення 5.2 legacy `.material` files/effect descriptions ще не читались; обмежений import додано в 5.4.
- [x] Weak CPU-data caches для GPU textures/samplers; GPU upload до begin_frame, deferred retirement; late CPU deletion без звернень до Vulkan backend.
- [x] Виправлено Win32 disk I/O handle/OVERLAPPED lifecycle, raw-buffer unpin, контроль short read/open failure та двобічне очищення `child_resource_ptr`; додано регресії reset/replacement/destruction graph links і файлів, змінених після mount.
- [x] CPU: 474 DDS/assignment rejections, normal resource-query failures і cook lifetime. GPU: textured/tinted/default pixels, material snapshot після відпускання зовнішніх pointers, 16 memory + 16 disk/DDS/material recook cycles без накопичення live/retired GPU resources.

Перевірено 2026-10-05: повна Debug MinGW/Ninja-збірка; 15/15 focused CTests етапів 0-5, RTX 4070, 0 validation errors / 0 warnings. Fixtures генеруються, записуються на диск і монтуються справжнім VFS; це ще не перевірка production game asset pack. Журнал: `build/vulkan-stage5-assets-validation.log`; візуально перевірений textured кадр: `build/vulkan-stage5-frame.bmp.textured.bmp`. GCC Release і багатопотоковий startup не перевірені.

Підетап 5.3: model-root, named submeshes і export_properties (завершено).

- [x] `static_render_model_class` і instance translate cooks; каталог `*.model/render` як manifest поверхонь, сортування імен, окремі `converted_model` та `export_properties` для кожної поверхні. Збережено memory/direct-file single-surface шлях.
- [x] Source folders визначають перелік поверхонь; для `resources/` і `mounts/` готові файли читаються з відповідних `resources.converted/` і `mounts.converted/` trees. Інші mounts допускають cooked-файли на місці. Автоконвертації Lua/source meshes і timestamp-based regeneration ще немає.
- [x] Bounded preflight Windows binary-config layout перед старим reader: offsets/counts/types, CRC/order/duplicate keys, tree cycles/aliasing, payload bounds. Читаються model/surface type, finite ordered bounding boxes і root locators; bounds перевіряються проти geometry з 0.1% relative / 0.001 absolute tolerance.
- [x] Per-instance/per-surface visibility і material assignment/reset за точним іменем; multi-surface модель вимагає явного імені. Draw snapshot тримає окремі mesh/material pointers для кожної поверхні; `get_locator` повертає local metadata, як у legacy instance.
- [x] Ліміти: 128 поверхонь, 64 MiB сумарного cooked input, 1 MiB properties/config, 4096 config nodes, depth 32 і 256 locators. Finalization враховує live model assets/instances і pending queries; late CPU deletion не викликає Vulkan.
- [x] CPU: 396 properties/command rejections, 10 negative root queries, source/converted-tree mapping, instance isolation, pending-query shutdown і asset cook lifetime. GPU: дві іменовані поверхні з різними матеріалами, queued visibility snapshot, незалежне hide/reset, remove із відпусканням instance до завершення кадру, 16 model-root reloads без накопичення live/retired GPU resources.

Перевірено 2026-10-07: повна Debug MinGW/Ninja-збірка; 16/16 focused CTests етапів 0-5, RTX 4070, 0 validation errors / 0 warnings. GPU reload coverage: 16 memory + 16 disk/DDS/material + 16 model-root cycles (in-place і source/converted trees). Журнал: `build/vulkan-stage5-roots-validation.log`; візуально перевірений кадр: `build/vulkan-stage5-frame.bmp.submeshes.bmp`. Дані залишаються згенерованими fixtures, не production game asset pack.

Підетап 5.4: обмежений legacy material/settings import (завершено).

- [x] `material_class` translate cook читає готові Windows binary `.material` через VFS/raw-data queries; typed descriptor шлях збережений. Для logical name пріоритет має `resources.converted/material_instances/<name>.material`, потім `resources.converted/materials/<name>.material`. Невалідний instance не підміняється базовим матеріалом; source instance без cooked config завершує query з помилкою.
- [x] Вузький піднабір `material/g_stage/effect`, `effect_id=g_stage_default`: direct або `{ value = ... }` parameters, diffuse texture / normalized constant color, opaque alpha і `two_sided`. Інші stages/effects, невідомі параметри та активні непідтримані `use_*` options відхиляються явно. Це unlit base-color preview, а не перенесення legacy deferred effect чи освітлення.
- [x] Model `settings/material_settings/<surface>/material_name` автоматично призначає strong material dependencies кожній іменованій поверхні. Source/converted mapping перевірений; відсутні settings або порожнє ім'я матеріалу дають white fallback, але source settings без cooked config, неповні/невідомі surface mappings і помилки dependencies відхиляються. Facade override/reset не змінює незмінні asset defaults; null reset, як раніше, вибирає white fallback.
- [x] Legacy cooked DDS footer (один byte bool sRGB) перевіряється й відокремлюється перед bounded DDS parser. Logical texture names без extension читаються з `resources.converted/textures/<name>.dds`; явні `resources/textures/` paths також відображаються на cooked tree. Повні custom-mount `.dds` paths читаються як звичайний DDS без footer. Обмеження RGBA8/BGRA8 з 5.2 збережені.
- [x] DXC solid/textured pixel shader permutations, material tint, RHI cull state; default/two-sided, back/front і mirrored winding перевірені GPU pixel tests. Спільний binary-config preflight використовується для properties, settings і materials; GPU lifetime залишається в renderer cache/retirement, не в CPU deleters.
- [x] CPU: 549 material parser rejections, 11 negative disk queries, invalid cull descriptor, binary query creation, material-instance priority, texture/sRGB dependencies, source/converted settings, pending-query shutdown та late material/texture deletion. GPU: ще 16 automatic settings/material reloads (in-place і source/converted), queued reset snapshot і stable live/retired counts; загалом 64 model reload cycles.

Перевірено 2026-10-07: повна Debug MinGW/Ninja-збірка; 17/17 focused CTests етапів 0-5, RTX 4070, 0 validation errors / 0 warnings. Журнал: `build/vulkan-stage5-materials-validation.log`; візуально перевірений кадр: `build/vulkan-stage5-frame.bmp.materials.bmp`. Дані все ще generated fixtures через справжній VFS, не production game asset pack. GCC Release і багатопотоковий startup не перевірені.

Підетап 5.5: camera/frustum culling (завершено).

- [x] Консервативний local-AABB тест шести homogeneous clip planes (`-w <= x,y <= w`, `0 <= z <= w`) із row-vector `world * culling_view * projection`; спочатку root geometry bounds, потім видимі named surfaces. Bounds обчислюються з geometry, а не з export metadata, що допускає tolerance.
- [x] Відсічені й приховані поверхні не входять у draw snapshot або GPU preparation. Вже queued visibility/transform/camera/material snapshots залишаються незмінними. CPU assets/dependencies усе ще завантажуються cook-ами; це не streaming чи occlusion culling.
- [x] Окремі render/culling matrices у view snapshot; `set_view_matrix_only` зберігає culling camera. Vulkan facade debug-frustum listener використовує новий opt-in safe math-frustum режим для reversed-Z/infinite-far/singular projections; початковий strict constructor і non-Vulkan listener шлях збережені.
- [x] Double plane composition, scale-relative FP32 error margin, облік intermediate overflow/underflow і conservative fallback для невизначених чисельно bounds/matrices. Clip-plane touching/intersections, negative/nonuniform scale, rotation/shear та perspective/orthographic/asymmetric/reversed-Z/infinite-far volumes покриті тестами. Reversed-Z тут стосується clip volume, не перемикає renderer depth policy (`less`, clear depth 1).
- [x] `diagnostics().static_culling` показує counters останнього успішного `draw_scene` queue: model/root rejection, tested/culled/hidden/submitted surfaces. Це CPU queue statistics, не GPU completion.
- [x] Додано CPU test із 10 000 independent homogeneous-corner comparisons, boundary/projection/numerical cases, safe listener, separate culling camera, geometry-vs-metadata bounds і queue/lifetime без GPU/DXC. GPU test доповнений root/surface rejection, camera/transform snapshots, projection cases, frozen culling camera і 16 never-visible reloads без GPU upload; загалом 80 reload cycles.

Перевірено 2026-10-07: повна Debug MinGW/Ninja-збірка; 18/18 focused CTests етапів 0-5, RTX 4070, 0 validation errors / 0 warnings. CPU culling: 28 boundary/projection cases і 10 000 independent comparisons (7847 culled, 2153 kept). Окремі CPU/GPU тести також пройшли по 3 повторні запуски; GPU coverage становить 80 reload cycles без накопичення ресурсів. Журнали: `build/vulkan-stage5-culling-validation.log` і `build/vulkan-stage5-culling-repeat-validation.log`; візуально перевірений кадр: `build/vulkan-stage5-frame.bmp.culling.bmp`. Assets залишаються generated fixtures через справжній VFS, не production game asset pack. GCC Release і багатопотоковий startup не перевірені.

Підетап 5.6: захищені static model buffer updates (завершено для підтриманого single-surface формату).

- [x] `engine::world::update_model_vertex_buffer` і `update_model_index_buffer` для підтриманих single-surface static instances; обидва шляхи доступні через queued facade commands. Multi-surface без surface selector відхиляється явно. Це не підтримка legacy user/skeletal/terrain model formats.
- [x] Byte-offset fragments: 1..4096 fragments, non-null/non-empty payload, перевірка bounds без integer overflow, 64 MiB aggregate limit. Розміри VB/IB, 32-byte static vertex layout і uint16 triangle topology count не змінюються; overlap виконується в порядку fragments, останній запис перемагає.
- [x] Transactional copy-on-write geometry: повна перевірка finite positions/UV та index range перед публікацією. Помилка залишає mesh/bounds незмінними; instance не змінює immutable cooked asset або sibling instances. Actual geometry bounds для culling перераховуються; export metadata/locators не редагуються.
- [x] Команда володіє копією payload і strong model pointer; throwing constructor звільняє storage. Vulkan шлях використовує allocator свого channel, не editor allocator. Старі queued draw snapshots тримають попередню mesh version; weak renderer cache і submission-serial/fence retirement захищають GPU VB/IB. GPU buffers залишаються immutable: нова версія перевантажує VB/IB, це не in-place/device-local streaming update.
- [x] CPU regression: invalid ranges/counts/null/non-finite/index values, rollback, byte-granular/overlapping updates, immutable old versions, sibling isolation, closed session, updated culling без GPU/DXC. GPU regression: vertex/index pixels, producer payload overwrite, queued snapshot, offscreen/visible bounds transitions, late commands після removal, 32 consecutive versions і 16 update/reload cycles; загалом 96 model reload cycles.

Перевірено 2026-10-07: повна Debug MinGW/Ninja-збірка; 19/19 focused CTests етапів 0-5, RTX 4070, 0 validation errors / 0 warnings. CPU buffer test: 32 rejection cases, transactional patches, isolation/lifetime і culling без GPU/DXC. Окремі CPU/GPU тести пройшли по 3 повторні запуски. Журнали: `build/vulkan-stage5-buffers-validation.log` і `build/vulkan-stage5-buffers-repeat-validation.log`; візуально перевірений кадр: `build/vulkan-stage5-frame.bmp.buffers.bmp`. Fixtures згенеровані, production assets і GCC Release не перевірені; allocation/device-loss failure injection та багатопотоковий startup не перевірені.

Підетап 5.7: versioned cooked texture reload (завершено для підтриманих disk-backed DDS).

- [x] Наявна queued facade-команда `reload_modified_textures()` асинхронно перечитує live disk-backed texture resources активного session. Weak registry не утримує resources; повторний запит для pending texture coalesce-иться. Memory-created/archived textures не відстежуються.
- [x] Перечитування через resource system physical queries із fresh file node та portable separators; виправлено відсутній фактичний file size, assertion на очікуваній open failure і null-node cleanup у старому VFS physical-query шляху. Початковий resolved physical source зберігається; dynamic remount/source-to-cooked regeneration не виконуються.
- [x] File size/type preflight і наявний bounded DDS parser: підтримані RGBA8/BGRA8, resize/mip changes та legacy sRGB footer. Порівняння decoded format/dimensions/mips/bytes зберігає identity незмінних textures і виявляє зміни навіть при однакових size/timestamp. Read/parse failure залишає попередню CPU version; закритий session або видалений resource не приймає callback result.
- [x] Thread-safe immutable texture snapshots у `model_draw`; GPU prepare/draw використовують саме queued version, не поточний resource. Нові GPU textures створюються тільки для прийнятих culling поверхонь; weak cache і submission-serial/fence retirement захищають старі versions. Це не in-place GPU update чи streaming; GPU allocation/device-loss rollback не додано.
- [x] `diagnostics().texture_reload`: requested/reloaded/unchanged/failed/cancelled/pending для поточного session. Це завершення CPU resource queries, не GPU completion. Команда явно сканує tracked sources; автоматичного file watcher/polling немає.
- [x] CPU: unchanged identity, same-size/time content changes, coalescing, resize/mips/sRGB, сім failure modes, missing physical query, memory exclusion, weak/closed/pending/late lifetime без GPU/DXC. GPU: shared materials/users, callback між prepare та GPU draw, unchanged/failed reload, offscreen upload avoidance, 32 consecutive texture versions, 16 texture-reload scene cycles та callback після world/backend destruction.

Перевірено 2026-10-07: повна Debug MinGW/Ninja-збірка; 20/20 focused CTests етапів 0-5, RTX 4070, 0 validation errors / 0 warnings. CPU/GPU reload tests також пройшли по 3 повторні запуски. Coverage: попередні 96 model reload scenarios, 16 texture-reload scene cycles, 32 consecutive texture versions, rollback після read/parse failures і pending callback після shutdown. Журнали: `build/vulkan-stage5-reload-validation.log` та `build/vulkan-stage5-reload-repeat-validation.log`; візуально перевірений кадр: `build/vulkan-stage5-frame.bmp.reload.bmp`. Fixtures згенеровані, не production assets. GCC Release, інші GPU, allocation/device-loss failure injection і багатопотоковий startup не перевірені.

Підетап 5.8: legacy DXT DDS і native BC texture upload (завершено для BC1/BC2/BC3 2D).

- [x] DDS FourCC DXT1/DXT1a -> BC1 RGBA, DXT3 -> BC2, DXT5 -> BC3; UNORM/SRGB вибирається existing userdata або перевіреним legacy footer. Encoded blocks зберігаються без CPU decompression чи повторного стискання. Це diffuse input для opaque unlit pass, не alpha blending, normal-map/DXT5n interpretation чи перенесення lighting effects.
- [x] Спільний block-layout helper для DDS parser і RHI upload: округлення 4x4 blocks, 8/16-byte blocks, повні/tiny/odd/non-square mip chains, padded source block-row repacking, overflow/bounds checks і block-aligned buffer offsets. Ненадійне compressed DDS `dwPitchOrLinearSize` не визначає розмір payload; він обчислюється з dimensions/mips. RGBA/BGRA row-pitch контракт не змінено.
- [x] RHI формати та Vulkan mapping; `textureCompressionBC` вмикається лише за підтримки device. Upload перевіряє capability та sampled/linear-filter/transfer-destination format features до створення image. Unsupported device/format дає явну помилку, CPU fallback не додано.
- [x] Static renderer передає tight mip bytes без припущення `width*4`. Existing immutable snapshots/weak cache/fence retirement працюють при BC <-> RGBA, зміні BC family, dimensions/mips і sRGB. Read/parse rollback збережено; GPU capability/allocation/device-loss rollback не додано.
- [x] CPU: 294 комбінації DDS/packing/zero/alignment для шести форматів, 669 malformed DDS/upload rejections, tiny/odd/non-square mips. GPU: шість BC asset cycles, pixel checks кожного з трьох mip-рівнів 7x5 texture, mid-tone sRGB decode, 1x1 tail, BC <-> RGBA queued snapshots, unchanged/truncated reload та 24 consecutive mixed-format versions без накопичення live/retired resources. Попередня coverage 5.1-5.7 збережена.

Перевірено 2026-10-07: повна Debug MinGW/Ninja-збірка; 21/21 focused CTests етапів 0-5, RTX 4070, 0 Vulkan validation errors / 0 warnings. CPU textures і Stage 5 GPU тести також пройшли по 3 повторні запуски. Журнали: `build/vulkan-stage5-bc-validation.log` і `build/vulkan-stage5-bc-repeat-validation.log`; BC GPU probe: `build/vulkan-stage5-bc-gpu-validation.log`. Візуально перевірений кадр: `build/vulkan-stage5-frame.bmp.bc5-srgb-mip2.bmp`. Fixtures згенеровані, не production assets. GCC Release, інші GPU (зокрема без BC), allocation/device-loss failure injection і багатопотоковий startup не перевірені.

Підетап 5.9: alpha-test, UV material state і активний shader ABI (завершено для unlit static preview).

- [x] `g_stage_default` import підтримує direct/wrapped `use_alpha_test`, `alpha_ref`, парні `constant_tile_u`/`constant_tile_v`; default threshold 0.25 і UV scale (1,1), як у наявному effect descriptor. Alpha reference finite 0..1, UV scale finite у межах +/-4096; negative/zero scale дозволені. Неповна UV pair і неправильні types/values відхиляються. Source alpha <1 дозволена лише для cutout; alpha blending не додано. Typed descriptors перевіряють ті самі нові scalar bounds.
- [x] Чотири DXC pixel variants: solid/textured x opaque/cutout. Cutout обчислює `clip((sample * tint).a - alpha_ref)` або solid tint alpha; рівність порогу зберігає fragment. Texture UV множиться на U/V scale. Depth пишеться лише для fragments, що вижили; shader не використовує early-depth примусово. Object constants ABI v1 лишився 96 bytes; parameters = explicit mip LOD, alpha reference, U scale, V scale. Automatic mip LOD/alpha blending/lighting не додано.
- [x] DXC Vulkan 1.3 `clip` потребує `DemoteToHelperInvocation`: backend запитує та вмикає `shaderDemoteToHelperInvocation` лише за підтримки GPU. Reflection/structural gate дозволяє саме цю opt-in fragment capability; default profile, vertex stage та інші optional capabilities залишаються обмеженими. Cutout shaders створюються ліниво до frame recording, тому opaque rendering не вимагає нового feature. Unsupported cutout shader creation завершується явною помилкою; немає мовчазної підміни opaque variant.
- [x] CPU material tests: defaults, direct/wrapped parameters, RGBA/BC disk texture dependencies, 12 додаткових malformed config rejections (разом 561) та 4 invalid typed states. `stage5_dxc` перевіряє справжній `assets/static_opaque.hlsl`: VS і всі чотири PS ABI/masks, різний alpha code/cache, три ABI mutations та відмову cutout без demote profile. Structural tests перевіряють fragment-only feature gate, не дозволяючи Float64.
- [x] GPU: 8 RGBA/BC cutout asset cycles, foreground-before-background depth rejection, alpha threshold 0/1/equality, tint multiplication, solid cutout, signed/zero U та V tiling, queued alpha/UV/texture reload snapshots, 32 consecutive material-state versions. Weak sampler/texture cache і fence retirement збережені; 2 lazy cutout shaders кешуються до renderer teardown, scene resources/cook users і retired allocations повертаються до очікуваного baseline.

Перевірено 2026-10-07: повна Debug MinGW/Ninja-збірка; 22/22 focused CTests етапів 0-5, RTX 4070, 0 Vulkan validation errors / 0 warnings. Material, Stage 5 DXC/GPU і Stage 2 structural contract tests також пройшли по 3 повторні запуски. Журнали: `build/vulkan-stage5-cutout-validation.log`, `build/vulkan-stage5-cutout-repeat-validation.log`, `build/vulkan-stage5-cutout-cpu-validation.log` та `build/vulkan-stage5-cutout-gpu-validation.log`. Візуально перевірені кадри: `build/vulkan-stage5-frame.bmp.cutout-1.bmp` і `.cutout-tiled.bmp`. GCC Release, інші GPU, unsupported-demote hardware path, allocation/device-loss failure injection і багатопотоковий startup не перевірені.

Межі: це preview semantics над legacy effect parameters, не відтворення production GBuffer shader. Наявний `effect_gstage_default_materials.cpp` задає параметри/defaults, але referenced `vertex_base`/`geometry_pre_pass`/`geometry_material_pass` shader files і production asset pack у checkout відсутні. DX11 pixel parity, shared legacy wrappers, source conversion і реальні game assets ще потребують окремої інтеграції/приймання.

Підетап 5.10: відтворюваний cooked-pack acceptance probe (інструмент реалізовано; реальне приймання відкрите).

- [x] `xray_vulkan_asset_probe --assets ROOT --model VIRTUAL_PATH --output NEW_DIRECTORY [--cycles 16..256]`: читає наявний каталог без fixture generation або зміни textures. Mount `assets` та optional `resources`/`resources.converted`; звичайні model/settings/material/texture cooks, actual geometry bounds і named surfaces.
- [x] Bounded input inventory до 10000 regular files / 1 GiB, без symlinks/junctions; before/after size/FNV-1a-64 fingerprints. Це non-cryptographic change detection, не доказ походження. Output має бути новим каталогом поза input tree; наявні звіти не перезаписуються. JSON serializer із bundled Boost ізольовано у C++17 translation unit; renderer залишається C++26, нової залежності немає.
- [x] GPU preview з автоматичним fit bounds у XY orthographic clip volume; empty/model/camera/moved BMP, pixel differences, facade movement, named visibility, root culling і queued removal snapshot. Model без cooked material/texture або з порожнім preview відхиляється. Depth/material override/texture mutation contracts і надалі перевіряються existing 5.1-5.9 regressions, не видаються за окремий production parity test.
- [x] Один warmup + 16..256 unload/recook циклів з unchanged texture reload; model/asset/material/texture cook users, live/retired GPU counts і descriptor/uniform capacity повертаються до baseline. Warmup допускає рівно 2 static constant buffers і 3..5 shaders; scene VB/IB/textures/samplers не залишаються. Mounts зберігаються до завершення scene/dependency teardown, включно з failure path.
- [x] JSON звіт містить input identity, surface/dependency metadata, completed cycles, GPU baselines, input stability, shutdown і validation counters. `technical_pass` означає лише успіх probe; поле `production_acceptance` завжди вимагає перевірки походження пакета й візуального приймання. Bootstrap deterministic single-thread, не normal game startup; потрібні DXC, Vulkan validation і доступний Windows desktop.
- [x] Окремий generated control pack та self-tests: CLI/path rejection, opaque RGBA та BC1/BC3 cutout (по 16 циклів), відсутня material dependency, white-only fallback, повністю відкинутий cutout preview зі справжнім GPU upload і failure cleanup. Fixture setup відокремлено від probe.
- [x] Запустити цей самий інструмент без змін на двох source-derived cooked packs зі справжніх assets; візуально перевірити model/camera/moved кадри. Геометрія й текстури не підміняються fixtures.
- [ ] Строге приймання готового legacy cooked pack без offline-адаптації залишається окремим відкритим пунктом; повний material не підтримується.

Перевірено 2026-10-07: повна Debug MinGW/Ninja-збірка; **28/28 focused CTests етапів 0-5**, RTX 4070, 0 Vulkan validation errors / 0 warnings. Усі шість нових probe self-tests пройшли по три повтори. Opaque baseline/final = `[2,3,1,1,0]`, BC cutout = `[2,4,1,1,0]` (buffers/shaders/textures/samplers/retired buffers); scene cook users і GPU retirements drained, input fingerprints незмінні, shutdown успішний. Журнали: `build/vulkan-stage5-acceptance-validation.log`, `build/vulkan-stage5-acceptance-repeat-validation.log`. Візуально перевірено `build/stage5-probe-reports/gpu/564a460959cd/model.bmp`, `build/stage5-probe-reports/cutout/b01ab1747cbe/model.bmp` і `camera.bmp`; поруч збережені JSON reports. Це generated pack self-test, не production acceptance. GCC Release, інші GPU, multi-threaded startup та allocation/device-loss failure injection не перевірені.

Перевірка реальних assets 2026-10-07 (доповнення до 5.10, не новий підетап):

- `props/box/woodbox_mil.model`: 595 вершин, 396 трикутників, власний `props/box/wood_box` material/diffuse 2048x2048. Фінальний pack: `build/real-assets/woodbox-mil-final`; звіт: `build/real-assets/woodbox-mil-final-gpu/report.json`, 24221 visible pixels.
- `props/barrel/barrel_metal03_radiation.model`: 380 вершин, 612 трикутників, власний `props/barrel/barrel_1_red_radiation` material і `props/barrel/barrel_rad_1` diffuse 512x512. Pack: `build/real-assets/barrel-radiation-final`; звіт: `build/real-assets/barrel-radiation-final-gpu/report.json`, 49018 visible pixels.
- Обидва: warmup + 16 unload/recook/reload cycles, baseline/final `[2,3,1,1,0]`, descriptor/uniform capacity незмінна, cook users і retirements drained, shutdown успішний, input inventory незмінний, RTX 4070, **0 validation errors / 0 warnings**. Model/camera/moved кадри переглянуто. Повторено 28/28 focused CTests: `build/vulkan-stage5-real-assets-validation.log`.
- Offline `tests/tools/prepare_real_static_pack.py` використовує справжній Lua runtime з обмеженим config environment і Pillow для TGA/DDS. 7/7 окремих unit tests перевіряють bounded archive traversal, malformed inputs/paths, binary config layout, незмінність positions/UV/indices та Lua inheritance/sandbox. Нових runtime/build залежностей renderer не додано.
- `provenance.json` кожного пакета містить SHA-256 шести source files, source mesh payloads, decoded texture pixels, повністю resolved original material і список відкинутих unlit-проєкцією параметрів. TGA->RGBA DDS: exact pixel roundtrip, один mip, оригінальний sRGB footer; це не оригінальне DXT1/mip cooking. `C:\Stalker2_2011` не змінювався.
- Контроль `--material-mode full`: той самий справжній barrel mesh/texture, але повністю resolved original material. Очікувана відмова `Unsupported material render stage`, exit 1, 0 validation errors/warnings, input unchanged і shutdown completed: `build/real-assets/barrel-full-material-gpu/report.json` та `.log` поруч. Тому ці результати не видаються за повну legacy-material/PBR сумісність.

Неперевірений залишок: готовий зовнішній cooked pack без цієї offline-адаптації. Повний legacy material pipeline та source-to-cooked regeneration в runtime залишаються поза фіксованим unlit scope; їх не слід мовчки включати до Етапу 5. DX10 headers, BC4/5/6/7, DXT2/4, cube/array/volume DDS і ghost/skeleton не підтримуються та не додаються автоматично.

Критерій завершення: виконано всі п'ять фіксованих умов вище. Реальний source-derived unlit preview вже перевірено; приймання готового legacy cooked pack без адаптації цим не закрито.

Підетап 5.11: пряме TGA source-loading (завершено; додано за запитом користувача).

- [x] Ізольований memory decoder на vendored `stb_image` 2.30, без DLL decoder або D3D залежності: raw/RLE RGB24/RGBA32, grayscale8/gray-alpha16; усі чотири origin-комбінації; ID, TGA 2 footer, збереження alpha. Історичні descriptor alpha-bits 0/8 для 32-bit storage приймаються зі збереженням четвертого каналу; gray8 із descriptor 8 читається opaque. Palette, packed RGB15/16 та interleaving явно відхиляються. Bounded preflight не допускає truncated/oversized RLE packets; межі 4096x4096 і 64 MiB source.
- [x] `texture_class` має окремий translate cook. Logical name спочатку шукає `resources.converted/textures/NAME.dds` із legacy sRGB footer; тільки відсутність DDS дозволяє fallback до `resources/textures/NAME.tga`. Наявний malformed/unreadable DDS або folder дає помилку, не прихований fallback. Прямі `.tga`/`.dds` та typed memory requests збережені; virtual extensions нормалізуються до lowercase.
- [x] Сусідній `NAME.options` читається звичайним raw-data dependency query: schema `srgb`, `has_mip_levels`, `texture_dimension=1`, metadata `format`/`ver`. Використано вже наявний Lua target, без standard libraries/host APIs, з 64 KiB source, 8 MiB allocator та 100000-instruction limit. Наявний malformed/unsupported options є помилкою. Без options використовуються caller defaults: linear, один mip. `format` є лише production-cooking hint; runtime TGA завжди RGBA8, не BC compression.
- [x] Повний mip-chain до 1x1 за `has_mip_levels`: in-memory box averaging з урахуванням odd dimensions, RGB sRGB->linear->sRGB, alpha усереднюється linear. Автоматичний LOD, alpha-coverage preservation і normal-map-specific filtering не додані.
- [x] Reload читає вибраний physical TGA та свіжі options, включно з options-only changes, появою/видаленням options, зміною dimensions/mips/sRGB. Unchanged snapshot identity, pending coalescing, failure rollback, queued old snapshot і fence retirement збережені. Reload не перемикає вже вибрану TGA на DDS, який з'явився пізніше: DDS-пріоритет повторно застосовується під час recook logical resource.
- [x] CPU: 96 format/origin/RLE/size combinations, exhaustive truncations, RLE literal/repeat, ID/footer, legacy descriptor, gamma/odd mips, schema/sandbox/instruction/memory rejection; resource/material fallback, DDS priority/no malformed fallback, options reload/rollback. GPU: 16 TGA scene cycles, queued old/new pixels, options/dimension changes, failure rollback, 32 consecutive versions, stable cook users/live/retired baseline. Окремий TGA cutout model probe виконує ще 16 циклів через logical material dependency.
- [x] Обидві справжні моделі перевірені без проміжного DDS: `build/real-assets/woodbox-mil-tga` та `build/real-assets/barrel-radiation-tga`. Скрипт `prepare_real_static_pack.py --texture-mode tga` копіює оригінальні TGA/options байт-у-байт; геометрія й обмежений material і далі готуються offline, full-material parity не заявляється.

Перевірено 2026-10-07: повна Debug MinGW/Ninja-збірка, **30/30 focused CPU/DXC/GPU CTests**, RTX 4070, **0 validation errors / 0 warnings**. Журнали: `build/vulkan-stage5-tga-validation.log`, `build/vulkan-stage5-tga-focused.log`. Реальні звіти: `build/real-assets/woodbox-mil-tga-gpu/report.json` і `build/real-assets/barrel-radiation-tga-gpu/report.json`: по 16 циклів, source_format `tga`, 12 mip levels для 2048x2048 та 10 для 512x512, baseline/final `[2,3,1,1,0]`, cook users/retirements drained, input unchanged, shutdown completed. Model кадри переглянуто та порівняно з попередніми DDS runs: піксельна різниця нульова. Зовнішній `C:\Stalker2_2011` не змінювався. GCC Release, інші GPU, full game/editor startup і palette TGA не перевірені/не підтримуються цим milestone.

## Етап 6. Offscreen targets, MRT та GBuffer

Передумови: етап 5.

Статус: завершено 6.1-6.3 і прийнято M6 для static opaque/cutout profile, Debug MinGW/Ninja на RTX 4070. Сцена через facade пише primary MRT GBuffer; normals/depth/tangents/position/stencil переглядаються й перевіряються, resize/minimize/restore та world/output recreate працюють. Обмеження приймання оригінального cooked pack з Етапу 5 залишаються чинними. Це не повна відповідність legacy materials/PBR і не освітлення.

Основні файли: `rhi_backend.h`, `render/core/backend.*`, `render_target.*`, `render/engine/sources/renderer_context.*`, `renderer_context_targets.*`, `stage_gbuffer.cpp`, `renderer.cpp`.

- [x] Додати до RHI offscreen color/depth attachments, MRT configuration, load/store actions, pass boundaries та explicit resource transitions для поточного single-mip/layer graphics-шляху (6.1 нижче).
- [x] Покрити потрібні GBuffer backend/context operations вузьким Vulkan RHI adapter: `static_renderer` + `gbuffer_renderer`. Активний шлях не викликає D3D; весь legacy `render::backend` не переписано і його невикористані операції не заявляються підтриманими.
- [x] Портовати static input layouts, render target binding, constants, stencil replace/reference та geometry binding, які використовує цей GBuffer profile.
- [x] Відтворити формати й primary packing GBuffer за DX11 renderer; перевірити normals, depth reconstruction, coordinate conventions та sRGB/linear conversions для обмеженого profile.
- [x] Узгодити render passes/framebuffers і pipeline compatibility keys; додати бар'єри attachment write -> shader read, а також transitions для depth sampling.
- [x] Відновити створення/resize Vulkan context targets; cleanup виконувати після завершення відповідної GPU-роботи. Це не перенесення всіх legacy renderer_context targets.
- [x] Додати діагностичне виведення кожного color attachment і hardware depth для static facade scene.

Критерій завершення: сцена пишеться у справжній MRT GBuffer; кожен attachment можна переглянути; resize зберігає коректне зображення без validation errors.

Фіксований порядок реалізації, без автоматичного розширення до lighting/postprocess:

1. **6.1, завершено:** RHI attachments/pass/transition contract та Vulkan реалізація, CPU/GPU тести інфраструктури.
2. **6.2, завершено в обмеженому profile:** активний static renderer і Vulkan adapter, primary GBuffer packing, transforms/normals/depth/stencil та opaque/cutout shaders. Не включає всі legacy effects, normal maps, skeleton/terrain або освітлення з Етапів 7-8.
3. **6.3, завершено:** scene debug views і stencil/depth silhouette, pixel checks, реальні source-derived models, resize/minimize/restore, world/output recreate та release/shutdown. Приймання й межі наведено нижче; нові effects не додаються як підетапи.

Результат 6.1:

- `texture_usage::render_target` / `depth_stencil`: 2D, один mip/layer, bounded/device-checked dimensions. Color: RGBA8 UNORM/SRGB, RGBA16 UNORM/FLOAT, RG16 UNORM, RGBA32 FLOAT, R32 FLOAT; depth: D24S8 і D32. Створення без source upload; sampled і attachment views для depth розділені, stencil не потрапляє у sampled view. Нові RGBA16/RG16 UNORM відтворюють формати DX11 context targets, але це ще не гарантія GBuffer packing parity.
- Явні `begin_render_pass` / `end_render_pass`, до чотирьох color attachments та optional depth, або depth-only pass; load/clear/discard і store/discard. Attachments повинні бути різними, однакового розміру та попередньо transitioned. Undefined/discarded contents не можна load/sample; після discard-load повна визначеність консервативно не припускається навіть після draw, бо full-coverage tracking ще немає.
- `transition_texture`: між passes, color attachment write <-> fragment sample, depth write <-> depth sample; same-state barrier підтримує повторні writes. Depth sampling має nearest sampler і read-only layout. Feedback loops, stale handles, unsupported states і знищення активного attachment відхиляються. На момент M6 copy/storage, MSAA, subresources і read-only depth attachment з одночасним depth testing були поза contract; розширення 7.7 додає MSAA та color-resolve transfer states, решта обмежень залишається.
- Старий `begin_frame` відкриває presentation pass як раніше. Для offscreen work його явно закінчують, записують offscreen passes/transitions, потім викликають `begin_presentation_pass`; повторний presentation pass **очищує**, а не завантажує попередній swapchain вміст. `end_frame` вимагає активний presentation pass. Додано залежність late-depth/color writes для безпечного повторного відкриття.
- Pipeline key включає впорядковані MRT formats/count та depth format; load/store, dimensions і image handles не є compatibility key. Dimension-only resize не очищає pipeline cache. Framebuffer/render-pass objects зараз створюються на кожний explicit pass і звільняються за submit fence serial перед відповідними texture views; кешування цих objects не заявляється. Нормальний offscreen pass не викликає device/queue idle; старий swapchain recreation ще використовує `vkDeviceWaitIdle`.
- CPU: 12 invalid contracts, collision-safe ordered MRT/depth pipeline keys. GPU: 16 циклів, 3 MRT outputs, load зі збереженням untouched pixels, discard/recovery, повторне використання тих самих textures між кадрами, D24/D32 sampling та depth-only pass, перестановка форматів, dimension/window resize, pending descriptor/framebuffer retirement, stale/feedback/linear-depth rejection; 238 invalid operations. Контрольні пікселі враховують linear->sRGB swapchain encoding.

Перевірено 2026-10-07: повна Debug MinGW/Ninja-збірка, **32/32 focused CPU/DXC/GPU CTests**, RTX 4070, **0 validation errors / 0 warnings**. Нові CPU/GPU тести додатково пройшли по три повторні запуски. Журнали: `build/vulkan-stage6-focused.log`, `build/vulkan-stage6-validation.log`, `build/vulkan-stage6-repeat-validation.log`; переглянутий діагностичний кадр `build/vulkan-stage6-mrt.bmp`. Це infrastructure acceptance, не кадр справжньої GBuffer-сцени. GCC Release та інші GPU не перевірені.

Результат 6.2 і scene diagnostics 6.3:

- Primary MRT відповідає `renderer_context_targets.cpp`, `stage_gbuffer.cpp` і зовнішньому `geometry_pre_pass.ps` з `C:/Stalker2_2011`: RGBA16 UNORM = sqrt(view_z/far), unused, roughness UV; RGBA16 FLOAT = normal XY, fresnel, normal-Z sign; RGBA8 UNORM = cross(tangent,normal); D24S8 = hardware depth і geometry stencil `0x82`. Не додається вигаданий albedo attachment: legacy material pass є окремою наступною інтеграцією. Roughness UV зараз `(1,1)`, fresnel `0.04`, без normal/roughness textures, тому повний shader/material parity не заявляється.
- `static_gbuffer.hlsl` використовує існуючі scene/model/material queries, VB/IB, culling, transforms, alpha threshold і UV tiling. Disk D3DCOLOR basis розпаковується як BGRA; normals проходять inverse-transpose, tangents orthogonalization і back-face sign. Для GBuffer параметр ObjectConstants.x означає far plane, cutout diffuse читається з LOD 0. ABI v1 (Frame128/Object96, ті самі descriptor sets) не розширено.
- Підтримані finite forward-Z perspective/orthographic projections і identity; singular/non-finite/reversed-Z/infinite-far GBuffer cameras явно відхиляються. Unlit підтримка попередніх камер не змінена. View/model normal transforms мають бути affine, invertible та не ill-conditioned.
- Через facade `scene().set_view_mode`: `normals_view_mode`, `gbuffer_packed_depth_view_mode`, `gbuffer_tangents_view_mode`, `gbuffer_hardware_depth_view_mode`, `gbuffer_reconstructed_position_view_mode`, `gbuffer_overview_view_mode`, `gbuffer_stencil_view_mode`; default лишається unlit. Mode/camera/material/geometry фіксуються у queued draw. Інші legacy modes відхиляються, editor UI для нових modes не додано.
- Full-output context має три color targets і depth, лінивий resize, nearest previews та fence retirement; коли GBuffer не використовується, context звільняється. `begin_presentation_pass(true)` завантажує попередній presentation content лише в тому самому кадрі; default `false` очищує як раніше. Збережено presentation depth і додано read dependencies. Це дозволяє unlit/GBuffer viewports в обох порядках без стирання попереднього viewport.
- CPU/DXC/GPU: forward-Z range і inverse projection roundtrip; stencil pipeline fields з forced hash collisions; справжні GBuffer/preview HLSL і всі opaque/cutout permutations; pixel checks depth/normals/tangents/position, non-uniform shear/mirror, perspective/ortho, queued mode, обидва порядки split views, vertical alpha/depth holes, 16 model reloads, resize/minimize/restore і context release. RHI stencil test окремо перевіряє replace `0x82`, load/preserve, equal/not-equal і відмову без stencil attachment.
- Offline preparer виправлено: normal/tangent/binormal пишуться little-endian D3DCOLOR B,G,R,A, а не X,Y,Z,A. Попередній unlit не читав basis і не виявляв цю помилку. 7 Python tests з точними packed bytes пройшли; positions/UV/indices не змінені. Для GBuffer створено нові packs, старі результати не перезаписувались.
- Реальні source-derived TGA packs: `build/real-assets/woodbox-mil-gbuffer-final` і `build/real-assets/barrel-radiation-gbuffer`. `xray_vulkan_asset_probe --view gbuffer` зберігає overview, normals, packed/hardware depth, tangents, position і diffuse. Обидва пройшли warmup + 16 cycles, GPU baseline/final `[3,7,5,2,0]`, stable descriptor/uniform capacity, drained cook users/retirements, clean shutdown, unchanged inputs і 0 validation errors/warnings. Звіти: `build/real-assets/woodbox-mil-gbuffer-gpu/report.json`, `build/real-assets/barrel-radiation-gbuffer-gpu/report.json`; кадри переглянуті. Після probe SHA-256 усіх 12 оригінальних inputs повторно перевірено. Це приймання геометричного GBuffer на адаптованих справжніх assets, не оригінальний production cook або DX11 image parity.

Фінальне приймання 6.3 і закриття M6 для static opaque/cutout profile:

Перевірено 2026-10-07: повна Debug MinGW/Ninja-збірка, **34/34 focused CPU/DXC/GPU CTests**, **7/7 Python tests**, RTX 4070, **0 validation errors / 0 warnings**. Stage 6 contracts/MRT/GBuffer/lifecycle і static/GBuffer/stencil DXC тести пройшли по три повтори. Журнали: `build/vulkan-stage6-acceptance-focused.log`, `build/vulkan-stage6-acceptance-validation-final.log`, `build/vulkan-stage6-acceptance-repeat-validation.log`; переглянуті captures: `build/vulkan-stage6-gbuffer.bmp.stencil.bmp`, `build/vulkan-stage6-recreated.bmp`.

- [x] Перевірити саме stencil mask scene GBuffer після alpha discard/depth occlusion. `gbuffer_stencil_view_mode` завантажує справжній D24S8 через LOAD, перевіряє EQUAL `0x82` з read mask `0xff` / write mask `0`, без depth test/write. Біло-чорна маска записується у повторно використаний packed-depth target лише в цьому debug-режимі; додаткової texture або CPU readback немає, shader створюється ліниво до recording. Тест порівнює кожний піксель маски із силуетом surviving hardware depth для cutout і перекритої геометрії; окремо перевіряє порядок draw і два GBuffer viewports.
- [x] Додати GBuffer world/output destroy/recreate у тому самому процесі. `xray_render_stage6_lifecycle`: 8 нових world / 32 output-сесії, перевірка stencil/normal pixels після recreate, caller pointers скидаються після queued draw, active/empty resource і descriptor/uniform baselines стабільні, cook users/pass objects/retirements drained. Shutdown відбувається після submitted frame з додатковими unprocessed facade commands; після знищення world перевіряються registry/cooks, порожні retained output leases і validation counters. Це deterministic single-thread lifecycle, не багатопотоковий startup чи ін'єкція справжнього device loss.
- [x] Повторити фінальний regression/acceptance набір і зафіксувати M6. Реальні source-derived TGA packs повторно пройшли warmup + 16 cycles кожний, з окремим `stencil.bmp`: `build/real-assets/woodbox-mil-gbuffer-acceptance-gpu/report.json` і `build/real-assets/barrel-radiation-gbuffer-acceptance-gpu/report.json`. Baseline/final `[3,7,5,2,0]`, unchanged inputs, drained users/retirements, stable capacity, clean shutdown, validation 0/0. Окреме порівняння збережених stencil/hardware-depth captures по всіх пікселях дало 0 розбіжностей для обох моделей (24335 і 49055 marked pixels); кадри переглянуто. SHA-256 усіх 12 оригінальних inputs знову перевірено незмінними.

Етап 6 завершений у зафіксованому обсязі. Material/PBR, normal/roughness textures та освітлення належать Етапу 7; game/editor startup, включно зі штатними потоками, належить Етапу 8; чисті GCC Debug/Release збірки, failure injection і перевірки на погоджених додаткових GPU належать Етапу 9. Це не приховані умови M6. MSVC не планується. Наступний функціональний крок належить Етапу 7, не новому підетапу 6.

## Етап 7. Матеріали, deferred lighting, тіні та фінальний кадр

Передумови: етап 6.

Основні файли: `stage_pre_lighting`, `stage_shadow_direct`, `stage_sun`, `stage_lights`, `stage_skybox`, `stage_forward`, `stage_combine`, `stage_postprocess`, їхні effect descriptions і renderer stage ordering.

Статус: завершено у зафіксованому static-профілі; 7.1-7.8 прийняті. Єдина контрольна сцена перевіряє спільний кадр із mapped normal/roughness, directional/point/spot lighting і shadows, ambient/emission/fog, sky, non-intersecting unlit alpha/additive forward та HDR/bloom/FXAA/MSAA/TAA. Приймання 7.8 включає незалежний CPU expected image, camera/light movement, history isolation/reset, reload/recook/resize baselines, teardown та нові AA режими на двох real-source packs. Lit transparency, refraction, OIT, particles, animated-scene TAA і legacy image parity не заявляються. Повна GCC Debug regression і межі наведені нижче. Наступна функціональна робота належить Етапу 8; нових підетапів M7 немає.

### Єдиний перелік підетапів

Це єдиний checklist Етапу 7. Scalar/maps/detail/specular inputs згруповані за спільним результатом, а не перераховуються повторно як незавершена material integration. ABI, probe modes, повтори тестів, reload fixes і real-asset runs є реалізацією або доказами відповідного пункту, не новими підетапами. Кількість галочок не є відсотком готовності: обсяг пунктів різний.

- [x] **7.1. Вхідні дані матеріалів.** Scalar roughness U/V і Fresnel, base normal/roughness/detail-normal maps, linear albedo/specular material-input pass, specular color/intensity та відповідні cooked dependencies. Сюди належать TGA NORMAL_MAP packing/mips, immutable snapshots, reload/rollback, shader ABI v2-v5 і debug/probe режими матеріалів. Повторно реалізовувати ці inputs у 7.4 не потрібно.
- [x] **7.2. Directional lighting і базовий combine.** Unshadowed directional add/update/remove, transforms/constants, active legacy anisotropic BRDF, два diffuse/specular accumulators і linear `albedo*diffuse + material_specular*specular`. До 16 lights, finite intensity [0,16], influence [0,1], queued light/camera snapshots; lighting ABI v6 і `--view lit`. Shadows, ambient/emission/fog і tonemap не входять у цей виконаний пункт.
- [x] **7.3. Point/spot lighting.** Типи світла додано до чинного light lifecycle зі спільним лімітом 16, attenuation, cone/range і position/direction constants (ABI v7). Використано наявні BRDF, accumulators і combine. Прийнято numeric/pixel tests, рух камери/світла, bounds, atomic type updates та immutable snapshots. Це unshadowed static profile; тіні залишаються у 7.5.
- [x] **7.4. Відсутні material contributions.** Додано per-view gamma-to-linear ambient, сталий linear emission матеріалу та view-Z exponential fog у linear результат. Cook зберігає emission у typed/disk/dependency paths, draw queue копіює environment. Прийнято numeric/pixel, cutout/depth, camera/projection/split snapshots, resize/recook/shutdown і real-asset regression. Active emissive maps явно unsupported; решту provenance omissions перевірено й обмеження зафіксовано нижче та в README. Це обмежений static opaque/cutout profile, не повна material parity; inputs з 7.1 та BRDF/combine з 7.2 не дублюються.
- [x] **7.5. Тіні.** Shadow RHI, depth-only static opaque/cutout casters, directional 1-4 cascades із snapping/PCF/crossfade, шість point faces/маски/analytic receiver і spot perspective map, local size/bias, direct diffuse/specular modulation, immutable snapshots та atlas transitions/LOAD/retirement. Окремі scene fixtures доповнені інтегрованим прийманням: 9 thin-contact і 6 grazing profiles, усі 12 point edges та 8 corners при трьох sizes, partial PCF і dense ray oracle, спільний кадр із normal/roughness materials, трьома типами lights, cutout/ambient/emission/fog, camera/caster snapshots, resize та recook baselines. Прийнято обмежений профіль із явно описаними texel/bias/filter bands, не довільну sub-texel geometry чи DX11 filtering parity. Спільне фінальне M7 приймання описане у 7.8.
- [x] **7.6. Skybox і forward transparency.** Cubemap DDS/cooked RGBA/BC, TGA cube strips із `texture_dimension=3`, GPU upload/ABI v11 та lit-scene skybox прийняті разом із cross-face filtering і snapshots/reload/resize/removal. Forward static unlit alpha/additive profile має typed/binary `f_stage_unlit` cook/dependencies, stable back-to-front surface-center sorting, linear FP16 blending після sky, depth reuse без depth/stencil writes та exclusion із GBuffer/shadow casters. Змішаний opaque/cutout/sky/transparent кадр, reverse insertion/camera, stable ties, queued camera/material/reload snapshots, resize/recook baselines і drain перевірені. Межі: non-intersecting surfaces, straight alpha/alpha-weighted additive; не lit glass/refraction/OIT або legacy particle/sub-UV parity. Пункт завершено в цьому обсязі без нового підетапу; фінальне спільне M7 приймання описане у 7.8.
- [x] **7.7. HDR і postprocess.** Manual exposure EV [-16,16], per-channel Reinhard, opt-in peak-threshold 9-tap binomial bloom та FXAA preset 3 після повного linear scene (combine/environment/sky/forward). На запит додано deferred MSAA 2x/4x/8x, FP16 resolve перед postprocess і opt-in HDR TAA із camera/world reprojection, depth rejection та neighborhood clamp перед bloom. MSAA можна поєднати з TAA або FXAA; TAA та FXAA взаємовиключні. Прийнято immutable queued settings, bypass/reset, numeric/CPU-oracle, split/fractional/tiny viewports, resize/retirement та перевірки lighting/shadows/sky/forward з AA. Real source-derived `--view aa` залишається FXAA probe; нові `msaa`/`taa`/`msaa-taa` прийняті на real-source packs у 7.8. Межі TAA: static-профіль без object motion vectors, зміни сцени скидають history; far-depth/sky не накопичуються. Не заявляються adaptive/filmic tonemap, temporal upscaling, animated-scene або DX11 parity. Повний спільний M7 кадр прийнятий у 7.8.
- [x] **7.8. Інтегроване приймання M7.** Повний stage ordering, inputs/outputs/resource states звірені з renderer і описані в README. Єдина контрольна сцена з усіма прийнятими passes перевірена незалежним analytic CPU expected image, без GPU-seeded colors або заяви DX11 parity. Прийнято 12 профілів 1x/2x/4x/8x без post-AA, із FXAA або TAA; camera/light movement, split history/settings isolation, reset/bypass, вісім live sky reload/model/material recook/resize baselines і validation після teardown. Новий AA перевірено на двох source-derived packs у трьох режимах із 16 активними AA recook cycles кожен. Це фінальне приймання спільного кадру, не нові функціональні підетапи.

Технічні контракти матеріалів, lighting/environment, shadow RHI та skybox і їхні обмеження описані в `sources/xray/render/vulkan/README.md`, розділ Stage 7. Історія нижче пояснює докази виконання й приймання; вона не додає нових пунктів до checklist.

### Докази виконання

Інтегроване 7.8 прийнято 2026-10-11. `xray_render_stage7_integrated_gpu`
рендерить одну сцену з normal/roughness opaque receiver, cutout caster, чотирма
directional cascades, шістьма point faces, spot map, ambient/emission/fog,
constant cubemap sky, unlit alpha/additive та HDR/bloom/AA. Незалежний CPU oracle
генерує зафіксований expected image із geometry/BRDF/ray visibility/composition,
не з GPU captures: 661782 перевірки каналів RGB, max error 1/255 при tolerance
5/255 поза описаними raster/PCF/filter bands. Для bloom використано незалежну
9x9 convolution. Окремі feature tests зберігають строгі edge/PCF/AA oracles.

Усі 12 AA profiles перевірені після 64-frame warmup і ще 32 frames тієї самої
jitter phase, drift не перевищує 2 RGB levels. Split histories/settings точно
збігаються з індивідуальними views. Camera/light movement і вісім actual sky
TGA reload/model/material recook/resize cycles зберігають exact live counts
`[19,24,26,11,0]`, capacity `[1,1,1,1]`, pending pass objects 36 та bounded
retirement tail; після removal tail і pending passes нульові. Validation після
повного renderer/resource teardown: 0 errors/0 warnings.

Фінальна GCC/MinGW-w64 Ninja Debug regression: **78/78 CTest (44 GPU)** за
327.34 s, **10/10 Python**; об'єднаний тест пройшов ще **три повтори**
(35.15/45.74/58.98 s) із тими самими RGB checks/baselines та validation 0/0.
RTX 4070, device Vulkan 1.4.351, system loader/headers 1.4.363. Вікна приховані,
nonactivating, без mouse capture; жоден CTest timeout/resource/pixel bound не
послаблено. Докази: `build/stage7-integrated/summary.json`, build/full/repeat/
Python logs, captures, CPU expected PPM і шість real-source reports.

Barrel material-input і woodbox detail-input packs пройшли `msaa` (4x), `taa`
(1x+TAA), `msaa-taa` (4x+TAA): кожен по 16 active-AA recook cycles, exact reset
і 1x bypass, fixed-phase convergence, unchanged reload, stable counts/capacity,
drained cook users/retirements, clean shutdown, validation 0/0. Changed pixels
понад 2 RGB levels: barrel 1768/23620/24268, woodbox 3400/17083/17086; це доказ
виконання AA, не універсальна оцінка якості. SHA-256/розміри 22 original inputs
і SHA-256 усіх 17/19 pack files незмінні. Оглянуто captures сцени/split і обох
моделей. Звіти у `build/real-assets/barrel-radiation-m7-integrated-aa-final`
та `woodbox-mil-m7-integrated-aa-bounded`. Це source-derived projections із
synthetic directional/bloom controls, не original-scene або production-cook parity.

Repeatable runner `tests/tools/run_m7_real_assets.ps1` перевіряє provenance,
SHA-256, hidden bounded processes і reports; existing/overlapping output
відхиляє до запуску probe (дві manual negative checks). Початковий woodbox run
із зовнішнім лімітом 75 s збережено як timeout; повтор із явним 240 s на mode
пройшов. Query/reload maxima woodbox 3281/3330 ms, barrel 206/268 ms; це Debug
decode measurements, не performance acceptance. Внутрішні waits лишилися 30 s,
CTest AA probes 75 s, integrated fixture 120 s. Команди та pass/resource contract
наведені в README, `Integrated M7 Acceptance (Stage 7.8)`.

MSAA/TAA додано 2026-10-10 як розширення чинного 7.7, без нового підетапу.
`postprocess_settings`: `msaa_samples=1/2/4/8`, `taa_enabled`,
`taa_history_weight` [0,.98], `taa_reset_serial` для явного camera cut.
Типові налаштування не змінено: MSAA=1, TAA/FXAA вимкнені. Global bypass і raw
debug views залишаються single-sample та не виконують TAA. Недоступні sample
counts/formats або sample-rate shading відхиляються явно, без прихованої заміни.

MSAA зберігає per-sample GBuffer/material/lighting/environment/sky/forward;
shadow atlas залишається single-sample. FP16 color resolve виконується тільки
після forward. Додано sample count до attachment/pipeline contract, strict
Texture2DMS reflection/binding та `resolve_texture` із transfer barriers.
GPU oracle перевіряє `k/N` покриття в linear radiance до Reinhard/sRGB,
сталі pixels/resources/capacity, FXAA composition і exact single-sample restore.

TAA використовує восьмикадровий Halton jitter, inverse current/previous VP,
depth rejection .002 NDC, 3x3 RGB clamp і bilinear HDR history до bloom.
Для MSAA depth береться мінімальний sample, color уже resolved. History
ізольована за view/output/viewport, до восьми активних ключів за кадр;
resize/projection/camera cut/gap/minimize/bypass/scene changes скидають її.
Model/material/light/environment/texture/sky reload не переносять stale color:
в поточному static-профілі вони скидають накопичення. Object velocity buffer,
animated geometry, sky reprojection, reactive transparency masks, upscaling
та оцінка продуктивності не входять у цю реалізацію.

Окремий actual-HLSL GPU oracle перевіряє відому вагу history, current-only reset,
disocclusion, RGB clamp, one-pixel reprojection та projected-depth/out-of-bounds/
far-depth rejection із depth 1x/2x/4x/8x. Scene test доводить, що результат
відрізняється від jitter-only, і перевіряє perspective/orthographic camera,
material reset, MSAA+bloom, split/fractional/one-pixel/tiny viewport, заміну восьми history keys,
minimize/restore, вісім resize/drain cycles та shutdown. HDR/shadow fixtures
додатково перевіряють AA із directional/point/spot shadows, normal/roughness,
ambient/emission/fog, sky та alpha/additive composition. Shader permutations,
malformed ABI, mixed-sample attachments і invalid resolve/binding мають тести.
Інтегроване 7.8/M7 цим не закривається.

Фінальний regression MSAA/TAA від 2026-10-11 після виправлення округлення TAA
scissor: GCC/MinGW-w64 Ninja Debug, RTX 4070, Vulkan 1.4.351, system loader/headers
1.4.363; 74/74 CTests (40 GPU), 10/10 Python tests, validation errors/warnings
0/0. Сім focused tests (MSAA/TAA/HDR/RHI/contracts/DXC) пройшли по три повтори.
Дробові та однопіксельні viewport перевірені попіксельно, без пропущеного краю.
GPU-вікна приховані/nonactivating, без mouse capture. Логи, captures і summary:
`build/stage7-msaa-taa`. Попередній timeout `stage5_gpu` не відтворився у двох
фінальних повних прогонах; останній час 26.30 s при незміненому ліміті 45 s.
Причину початкового timeout не встановлено. Новий AA на production/real-source
pack, інші GPU та GCC Release цими fixture runs не перевірені.

FXAA і спільне postprocess-приймання 7.7 перевірено 2026-10-09. Використано
активний алгоритм рушія з `C:/Stalker2_2011/.../sm_4_0/fxaa_common.h`:
2011 NVIDIA FXAA preset 3, 16 one-pixel span-search steps, thresholds 1/8 і 1/24,
subpixel trim 1/4, cap 3/4. Невикористовувані API/preset branches не перенесено;
всі reads clamp до covered pixel centers поточного viewport. `fxaa_enabled=false`
є opt-in; queued draw копіює всі settings. Після HDR bloom два passes виконують
exposure/Reinhard/sRGB encode у RGBA8 UNORM, потім FXAA і sRGB decode у RGBA16F;
presentation не повторює tonemap і виконує hardware sRGB encode один раз.
ABI v12/descriptor layout не змінено; two-target transactional resize, fenced
retirement, exception-safe pass close, warmed toggle/bypass без allocations.

GPU fixture має 39892908 strict RGB oracle checks (max 1/255), 40357548
3x3 convex color bounds і 16976 softened pixels. Перевірено три rotations
при EV -2/0/2, primary colors, thin diagonal, numeric combined radiance/bloom/
exposure/Reinhard/sRGB, sky/alpha/additive-before-postprocess, independent/global
bypass із exact restore, raw debug isolation, queued settings, split/fractional/
one-pixel viewports, вісім resize/recook/toggle baselines, removal/drain.
На soft bloom gradients 8-bit hardware capture не тотожний UNORM shader input:
quantization може змінити дискретний span-search. Зафіксовано 29 reference-channel
outliers понад 4/255, max 11/255; acceptance обмежує їх <0.1% channels на viewport
і mean differential <0.1/255, додатково strict uniform neighborhoods, convex
bounds та незалежний numeric stage-order oracle. Whole-frame bit-exact parity
не заявляється. Pure-blue contrast ранній FXAA luma intentionally не бачить;
temporal history/shimmer guarantee не заявляється для FXAA. MSAA/TAA не входили
у це приймання від 2026-10-09 і додані окремим розширенням 7.7 вище; SMAA не реалізовано.

Real source-derived barrel pack пройшов warmup +16 cycles:
`build/real-assets/barrel-radiation-stage7-aa-gpu/report.json`, 48210 visible pixels,
3719 independent AA-bypass changes при tolerance 2, 47175 exact bloom-bypass і
48208 global bypass changes, exact restore; baseline/final `[8,17,16,4,0]`,
capacity `[1,1,1,1]`, drained cook users/retirements, unchanged inputs, clean
shutdown і RTX 4070 validation 0/0. Captures inspected; raw normals/albedo/
specular/hardware-depth/stencil BMP hashes незмінні щодо accepted bloom run,
AA-bypass BMP точно збігається з його bloom BMP. SHA-256 і bytes усіх десяти
original inputs незмінні before/after. Це adapted geometry/material projection
з original TGA і synthetic controls, не unmodified cooked-pack або DX11 parity.
Evidence: `build/stage7-aa/`; повний GCC Debug regression 71/71 CTest (38 GPU),
10/10 Python. Вісім focused settings/HDR/bloom/FXAA/probe/DXC/RHI tests додатково
пройшли по три повтори (`focused-repeat.log`). Це закриває 7.7 у заявленому
обсязі, не інтегроване 7.8.

Bloom у тому самому 7.7 перевірено 2026-10-09. Opt-in settings мають threshold
[0,65504] pre-exposure linear radiance (default 1), strength [0,4] (default .1),
radius [1,16] output pixels (default 4). Після environment/sky/forward виконується
color-preserving peak extraction, horizontal/vertical 9-tap binomial blur та
`min(scene+strength*blur,65504)` перед exposure/Reinhard/hardware sRGB.
Три RGBA16F targets використовуються як A/B ping-pong і окремий composite C;
кожний pass переходить RT->shader_resource, без attachment feedback/depth/stencil.
Resize атомарний для групи targets, retirement fenced; нульова strength є exact
bypass. UV clamp до covered pixel centers захищає split/fractional/one-pixel
viewports. Це bounded full-resolution sparse kernel, не mip-pyramid/lens bloom
або legacy image parity; великі radii можуть мати дискретні kernel shoulders.

ABI v12: pixel-only postprocess64, viewport/source_bounds/filter/bloom float4
із перевіркою layout/names/bindings, explicit matching constants і textures;
без material fallbacks або змішування VS ABI. CPU settings мають 35 rejects,
DXC перевіряє три entries і 7 нових mutations, RHI GPU має 266 negative checks.
Scene GPU має 8865 numeric RGB checks за незалежним 2D binomial/bilinear oracle,
threshold/radius/strength/EV, visible colored halo, bypass/debug isolation,
sky/alpha/additive ordering, queued settings, split/fractional/one-pixel viewports,
8 resize/recook baselines і drain. Шість focused tests пройшли тричі;
Python tooling 10/10. Halo/split captures переглянуто.

Реальний source-derived barrel pack із оригінальними TGA пройшов warmup +16 cycles
у `build/real-assets/barrel-radiation-stage7-bloom-gpu/report.json`. Є 48092 visible
pixels; independent bloom bypass змінює 47376 exact RGB pixels, відновлення точне.
GPU baseline/final `[7,15,14,3,0]`, capacity `[1,1,1,1]`, drained cook users і
retirements, unchanged pack, clean shutdown, validation 0/0. Кадри переглянуті;
SHA-256/byte sizes усіх 10 original inputs із `C:\Stalker2_2011` незмінні до/після.
Probe прямо повідомляє synthetic threshold .02/strength .25/radius 8, EV0, AA=false;
це не original scene postprocess або повний оригінальний cooked pack.
Evidence: `build/stage7-bloom/`. На момент цього bloom-only run AA та
спільне postprocess-приймання ще очікували; їхнє наступне завершення описано вище.
SHA-256 normals/albedo/specular/hardware-depth/stencil BMP збігається з попереднім
HDR run, а bloom-bypass BMP побітово збігається з прийнятим HDR BMP. Отже probe
ізолює саме bloom, не приховану зміну material/light/GBuffer або tonemap.
Фінальний повний regression після bloom: 69/69 CTest (36 GPU), 10/10 Python,
validation 0/0. Logs і `verification-summary.json` у `build/stage7-bloom/`.
Цей історичний bloom-only run не закривав AA/спільне приймання або 7.8.

HDR exposure/tonemap у 7.7 перевірено 2026-10-09. `postprocess_settings`
має manual EV [-16,16] (default 0) і enabled=true; facade копіює settings,
view валідує їх атомарно, draw queue зберігає snapshot. `enable_post_process`
дійсно перемикає presentation без втрати EV. У lit mode фінальний FP16 scene
після combine/ambient/emission/fog/sky/alpha/additive проходить exp2(EV) та
per-channel `x/(1+x)` перед hardware sRGB. Це спрощена global Reinhard curve,
не photographic adaptation, filmic чи DX11 parity. Нових GPU targets або ABI
не додано: fullscreen preview використовує вільні tint.zw у object96.

CPU перевіряє defaults, immutable/atomic updates, bounds/toggle і 10 rejects.
GPU має 243 numeric RGB checks, radiance 0..16, EV -16..16, direct+emission
вище 1 без early clamp, raw bypass, debug isolation, tonemap після sky і обох
forward blends, queued settings/material/projection, resize, 8 recook baselines
та drain. Чотири focused HDR/probe tests пройшли тричі; Python tooling 10/10.
`--view lit` і старі numeric fixtures явно вимикають postprocess;
`--view hdr` має exact RGB bypass (>64 pixels) і exact restore, а report прямо
позначає bloom=false/aa=false. Resource-backed legacy postprocess config не
інтегрований. На момент HDR-only run bloom/AA/спільне приймання ще очікували;
їхнє наступне завершення у тому самому 7.7 описано вище.

Реальний source-derived barrel із diffuse/normal/specular TGA 512x512 пройшов
warmup + 16 cycles у `build/real-assets/barrel-radiation-stage7-hdr-gpu`.
Є 47406 visible pixels, HDR bypass змінює 41548; повторне ввімкнення відновлює
exact RGB. GPU baseline/final `[6,12,11,2,0]`, capacity `[1,1,1,1]`,
cook users/retirements drained, input_unchanged і clean shutdown; validation 0/0.
HDR/bypass кадри переглянуті. До/після run SHA-256 і byte size усіх 10
оригінальних inputs із `C:\Stalker2_2011` збігаються з provenance.
Це перевірка HDR на справжній геометрії/TGA в адаптованому material pack,
не повний оригінальний cooked pack, legacy scene lighting або DX11 image parity.
Докази: `build/stage7-hdr/` та real-asset `report.json`.
Повний regression після HDR: 67/67 CTest (34 GPU), 10/10 Python; validation
errors=0/warnings=0. Logs: `regression.log`, `focused-repeat.log`, `python.log`,
`verification-summary.json` у `build/stage7-hdr/`. Цим прийнято exposure/tonemap,
але на момент цього HDR-only run bloom/AA та 7.8 ще не були прийняті.

Forward transparency і bounded 7.6 прийнято 2026-10-09. Окрема прозора queue
використовує immutable model/mesh/material/texture snapshots, stable camera-Z
sorting за transformed surface AABB centers; однакові depths зберігають порядок
сцени. Typed `forward_material_request` і binary `material.f_stage.effect`
з `effect_id=f_stage_unlit`, integer blend 1/2 проходять чинний material cook
та texture dependencies. Інші effects/fields, mixed stages, cutout/BRDF inputs,
invalid blend і missing dependencies відхиляються. Не змінено opaque/cutout
`g_stage_default` або constant/descriptor ABI. Forward shader перетворює gamma
tint у linear, sample sRGB декодує view, opacity лишається linear.
Порядок: combine -> sky -> forward -> preview; LOAD FP16/D24S8, LESS,
depth/stencil writes off, transparent surfaces не пишуть GBuffer і не є casters.
CPU: 32 camera sort oracles x 64 affine centers, scale/shear, 64 stable ties,
14 sort/parser/owner/input rejects, typed/binary solid/textured cooks, missing
dependency, immutable copy та late material/texture cleanup. GPU: 111 numeric
RGB checks, exact reverse-insertion image, alpha 0/1, textured alpha/additive,
linear/sRGB options reload, lit opaque/cutout + sky, directional shadow exclusion,
reverse camera sort, same-depth layers у двох orders (виявляють depth writes),
debug isolation, split queued camera/material/reload snapshots, resize,
8 texture reload/material recook baselines із stable capacity і retired drain.
Captures `vulkan-stage7-transparency.bmp` і `.bmp.resized.bmp` переглянуто.
Чотири sky/forward focused tests пройшли тричі; повний regression 64/64 CTest
(32 GPU), 10/10 Python, RTX 4070 validation 0/0. Докази у
`build/stage7-transparency/`: `build.log`, `build-tests.log`, `build-final.log`,
`focused-repeat.log`, `regression.log`, `python.log`, `verification-summary.json`.
Forward прийнято на deterministic fixtures, не оригінальних legacy materials.
Межі: static non-intersecting unlit straight-alpha/alpha-weighted additive;
немає per-triangle/OIT sorting, lit transparent BRDF, fog/shadow reception,
refraction чи particle/sub-UV parity. На момент forward-only run 7.7/7.8 ще
очікували; наступне завершення 7.7 та інтегроване 7.8 описані вище.

TGA cubemap і scene skybox у 7.6 перевірено 2026-10-09. Options dimension 3
означає горизонтальну смугу шести квадратних faces +X/-X/+Y/-Y/+Z/-Z;
окремі межі 12288x2048/128 MiB не змінюють 2D 4096/64 MiB.
Raw/RLE/origins, layer-major gamma-correct face-isolated mips, typed sky material
і texture/options reload використовують наявні cooks. `set_sky_material`
приймає лише cubemap тієї самої сесії; null видаляє небо. Color linear [0,1],
intensity [0,16], rotation у radians. Черга копіює camera/config/texture snapshot;
translation не створює parallax. Sky pass LOAD-ить FP16 combine і scene D24S8,
малює тільки stencil 0/depth 1, без depth/stencil writes, перед sRGB preview.
Debug views не змінюються. CPU: 192 format cases, 125 rejected source/assignment/
owner cases плюс typed schema/cook/reload/late-cleanup checks. GPU: 456 RGB checks,
шість faces, perspective/orthographic/identity, camera translation/rotation,
lit opaque/cutout/background, split camera/material і queued reload snapshots,
resize, 8 recook baselines, removal/drain; 80 edge/corner views із analytic
direction-encoded cube перевіряють cross-face linear filtering. Три focused
тести пройшли тричі, validation 0/0.
Оригінальні `sky_19_cube#small.tga` (32, 6 mips), `sky_20_cube.tga` (1024,
11 mips), `sky_19_cube.tga` (2048, 1 mip, понад 64 MiB) та їхні options
з `C:/Stalker2_2011/xr2-dsgn/resources/sources/textures/test/` завантажено
без конвертації через texture cook. По 426 RGB checks, translation/rotation,
opaque/cutout composition і validation 0/0; зовнішні файли лише читаються,
reload перевіряється на fixtures. SHA-256 усіх шести TGA/options до й після
фінальних запусків збігаються. Resized sky captures переглянуто.
Докази: `build/stage7-skybox/` (`build.log`, `focused-repeat.log`,
`regression.log`, `python.log`, `verification-summary.json`,
`real-small.log`, `real-sky20.log`, `real-sky19.log`, `real-*.bmp.resized.bmp`).
Повний regression: 62/62 CTest (31 GPU), 10/10 Python, validation 0/0.
Це bounded single-cubemap sky, не legacy sky-material script/weather/clouds
або DX11 image parity. На момент skybox run forward transparency ще не було;
наступний forward run вище завершує bounded 7.6, але не M7.

Cubemap texture foundation у 7.6 перевірено 2026-10-09. Complete legacy DDS
RGBA8/BGRA8/BC1/BC2/BC3 linear/sRGB, шість square faces +X/-X/+Y/-Y/+Z/-Z,
layer-major mip storage, повний upload/barriers/CUBE view. ABI v11 додає
TextureCube у binding 0 без зміни constant/set layouts; descriptor dimension
перевіряється до draw, static 2D material maps не приймають cubes. Cooked footer,
immutable snapshots і size/mip/sRGB reload працюють; 2D/cube type changes та
malformed reload зберігають попередню текстуру. CPU: 56 cases і 4332 rejected
inputs. GPU: 32 frames/4608 RGB checks, six face orientations/four mips,
nearest sampling, update між rows одного recorded frame, destroy-before-submit,
resize, stable warmed pools/pipelines/live counts і final retired drain.
Три focused тести повторено тричі; 60/60 CTest (30 GPU), 10/10 Python,
RTX 4070 validation 0/0. Capture `build/windows-x64-clean/vulkan-stage7-cubemap.bmp`
переглянуто; докази у `build/stage7-cubemaps/` (`build.log`, `focused-repeat.log`,
`regression.log`, `python.log`, `verification-summary.json`). Це texture fixtures,
не реальний skybox кадр. У `C:/Stalker2_2011/xr2-dsgn/resources/sources/textures/test/`
знайдено `sky_19_cube.tga`, `sky_20_cube.tga` з options `texture_dimension=3`.
На момент цього foundation run source layout ще відхилявся; наступний skybox
run вище закриває source import, scene integration та linear filtering,
але сам по собі не приймав forward transparency, закритий наступним run вище.

Інтегроване приймання 7.5 завершено 2026-10-09. Новий
`xray_render_stage7_shadow_acceptance_gpu` доповнює per-light fixtures без нового
підетапу і без змін production shadow renderer/shaders. 9 contact profiles:
caster width 0.04, gap 0.02, усі три lights і 256/512/1024 maps; 6 grazing
profiles: 75/82 degrees, width 0.12, gap 0.02, size 1024. Spot outer cone 90°,
local range 4/bias 0; directional receiver bias .0001, raster bias 1.25/1.75.
Широкий 160° cone на size 256 робить цей thin caster sub-texel: така геометрія
не має гарантії стабільної тіні. Профіль і межі приймання описані в README.
Усі 12 point edges і 8 corners x 3 sizes x 4 tie/0.1%-perturbed poses:
240 views, 927360 dense ray RGB checks поза filter band (tolerance 5) та
22334 partial PCF samples. Band 0.04 world при 256, удвічі менший при кожному
подвоєнні resolution; усередині radiance обмежена unshadowed +5. Це не seamless
cross-face cubemap filtering і не заява довільної production/DX11 parity.
Контрольний кадр: cooked normal/roughness dependencies, colored albedo,
directional/point/spot shadows, cutout, ambient/emission/fog. Numeric composition,
full-image reverse light order (max RGB error 1, limit 2), queued camera/caster
snapshots, resize та 8 geometry/material recooks зі зміною map sizes/cascades.
Live baseline `[11,16,15,6,0]`, capacity `[1,1,1,1]`, pending pass objects 22;
active retired bound `[0,0,0,6,0]` через sequential atlas resize, після removal 0.
Чотири GPU shadow tests пройшли по три повтори. Повна GCC/MinGW-w64 Ninja
Debug регресія: 58/58 CTest (29 GPU), 10/10 Python, RTX 4070 validation 0/0.
Логи: `build/stage7-shadow-acceptance/{build-final,focused-repeat,regression,python}.log`,
підсумок `verification-summary.json`; п'ять contact/grazing/corner/control/resize
captures у `build/windows-x64-clean/vulkan-stage7-shadow-*.bmp` inspected.
Vulkan loader системний 1.4.363, GPU API 1.4.351, app-local DLL відсутній.
7.5 закрито у цьому обсязі; M7, shadowed real-asset/DX11 final-frame parity,
performance, GCC Release та інші GPU цим прийманням не оголошуються завершеними.

Point scene shadows у межах того самого незавершеного 7.5 перевірено 2026-10-09:
GCC/MinGW-w64 Ninja Debug, 57/57 CTest (28 GPU), 10/10 Python tests,
RTX 4070 validation 0/0. Логи: `build/stage7-point-shadows/regression.log`,
`python.log`, `focused-repeat.log`, `dxc-repeat.log`; підсумок
`verification-summary.json` у тому самому каталозі. Два CPU/DXC та шість GPU
focused tests пройшли по три повтори. Системний Vulkan loader 1.4.363,
GPU API 1.4.351, app-local `binaries/vulkan-1.dll` відсутній.
Шість 90-degree views (+X/-X/+Y/-Y/+Z/-Z) з reference roll, near=0.025,
far=range; shadowed range > 0.025 + epsilon_5. Local size/bias як у spot.
Face flags копіюються у маску: вимкнена грань не прибирає direct light;
all-false не означає all-enabled. D32 atlas 3x3 має шість зайнятих tiles;
спільний ABI v10 496 bytes не розширено. Matrix 0 перетворює view position
на world light-relative delta, splits.xyz містить projection A/B і face mask;
receiver обчислює dominant-axis face/XY/depth. PCF tile-clamped, не cross-face.
CPU suite: 136 rejections, усі 64 маски x 3 sizes x 2 camera views, face
coverage/packing/atomic updates. GPU: усі шість opaque/cutout faces,
маски та їхні queued snapshots, дві face boundaries, point/spot і directional
atlas reuse в обох orders, рух, near clipping, bias/transparency/specular,
resize, 8 resource/capacity/pending-pass baselines, removal/shutdown.
Знайдено й виправлено стару формулу core `math::mul4x4` [2][3]: `left.e30`
замінено на `left.e20`; незалежний scalar oracle перевіряє всі 16 елементів.
Actual DXC/ABI має 30 mutations, offscreen GPU 261 rejections, як раніше.
Captures `build/windows-x64-clean/vulkan-stage7-point-{shadows,face-0..5}.bmp`:
final/representative faces inspected, усі шість face RGB samples перевірено.
Це fixture acceptance, не новий shadowed real-asset чи DX11 parity run.
На цьому попередньому кроці 7.5 ще залишався відкритим: thin/grazing geometry,
bias та edge/corner acceptance. Інтегроване приймання вище закриває цей залишок.

Spot scene shadows у межах того самого незавершеного 7.5 перевірено 2026-10-09:
GCC/MinGW-w64 Ninja Debug build, 56/56 CTest (27 GPU), 10/10 Python tests,
RTX 4070 validation 0/0. Логи: `build/stage7-spot-shadows/regression-verified.log`,
`python.log`; підсумок: `verification-summary.json` у тому самому каталозі.
Використовується спільний `vulkan_scene_shadows` renderer і D32 atlas, без нового
підетапу чи receiver ABI: v10 залишається 496 bytes. Spot використовує matrix 0,
count/grid 1, без cascade selection; `USE_SCENE_SHADOW` обирає receiver за типом
light. Position/direction копіюються у world space до lighting view transform.
Projection: outer full angle, aspect 1, near=range/1000, far=range; perspective
receiver використовує `math::mul4x4`, а не affine-only `float4x4::operator*`.
`shadow_map_size_index` 0/1/2 => 1024/512/256, як у reference stage_lights;
`shadow_map_size` та directional size/count не керують spot map. Local bias
finite [0,1] масштабується 0.1 і передається у `spot_parameters.w`; нуль дозволено.
Directional bias .0001, PCF і raster bias 1.25/1.75 збережено. Новий profile
не є заявою DX11 filtering/bias parity. На цьому попередньому кроці point
shadow requests ще відхилялися; point integration вище замінює це обмеження.

Новий `xray_render_stage7_spot_shadows_gpu` перевіряє всі три index sizes,
нульовий/масштабований bias, transparency, off-camera/near-clipped/cutout casters,
caster/light/cone motion, diffuse/specular і ambient independence, змішані
point/spot та shadowed directional/spot lights в обох orders із resize/reuse
того самого atlas, queued camera/light/caster/split snapshots, perspective/
orthographic receivers, window resize, 8 resource/capacity/pending-pass baselines,
removal drain і shutdown. CPU light suite тепер має 121 rejections, перевіряє
index/bias/inactive schema, atomic rollback, outer-cone і near/far coverage,
rigid views/translation bounds (+/-1e7) і receiver packing. Actual DXC/ABI лишається 30 mutations,
offscreen GPU відхиляє 261 invalid operation. Два CPU/DXC і п'ять GPU focused
tests пройшли по три повтори із RTX 4070 validation 0/0; лог
`build/stage7-spot-shadows/focused-repeat.log`. Capture
`build/windows-x64-clean/vulkan-stage7-spot-shadows.bmp` inspected.
Після camera-translation guard CPU suite (121 rejections) додатково пройшов
три повтори: `build/stage7-spot-shadows/cpu-bounds-repeat.log`; повний regression
остаточного коду теж повторно пройдено. Системний loader 1.4.363, GPU API
1.4.351, `binaries/vulkan-1.dll` відсутній.
Приймання spot shadows тут fixture-based: нові shadowed real-asset runs,
production cook/DX11 final-frame parity, performance/Release/other GPUs не
оголошуються. Попередні real-asset unshadowed runs нижче є історичними доказами
directional integration, не новим shadowed spot acceptance.

Directional scene shadows у межах незавершеного 7.5 перевірено 2026-10-09:
GCC/MinGW-w64 Ninja Debug-збірка, 55/55 CTest (26 GPU), 10/10 Python tests.
Scene queue копіює всіх видимих static casters, включно з позакамерними,
разом із mesh/material/texture revisions, light/camera і cascade plans.
Кожен cascade окремо cull-ить casters; camera draw statistics не включають їх.
Directional profile: 1-4 cascades (0 => 4), per-cascade power-of-two resolution
64-2048 (0 => 512), linear shadow transparency [0,1]. Perspective splits
50/50 uniform/logarithmic, orthographic uniform; rounded sphere fitting,
texel-snapped XY, overlapping caster depth bounds і 10% cascade crossfade.
Один D32 atlas послідовно повторно використовується різними lights; accumulator
pass завершується перед shadow rendering і відновлюється з LOAD/ONE+ONE.
PCF visibility модулює лише direct diffuse/specular після gamma/BRDF;
ambient/emission не затінюються. Receiver ABI v10 = 496 bytes, старі v1-v9
збережені; DXC/reflection suite відхиляє 30 mutations, offscreen GPU також
відхиляє short/missing v10 constants перед pipeline creation.

Новий `xray_render_stage7_directional_shadows_gpu` перевіряє facade/queue шлях:
off-camera caster, 1-4 cascades і boundary depths, cutout, caster/light motion,
queued camera translation/rotation, transparency, змішані lights в обох orders,
дві різні shadowed lights зі спільним atlas, ambient/emission/direct specular,
perspective/ortho, split snapshots зі зміною resolution/count, window resize,
8 atlas resource/capacity/pending-pass baselines, removal drain і shutdown.
CPU suite перевіряє split coverage/matrix packing/sub-texel stability,
schema/atomic updates та invalid rigid-view/oblique projections (105 rejections
у загальному light suite). Це deterministic fixture acceptance, не performance
або exhaustive production artifact acceptance. На цьому попередньому кроці
point/spot shadow requests ще відхилялися; spot integration вище замінює
це історичне обмеження. Нове shadowed real-asset/DX11 parity не оголошується.
Capture: `build/windows-x64-clean/vulkan-stage7-directional-shadows.bmp`, inspected.
Три CPU/DXC та шість GPU focused tests пройшли по три повтори; offscreen
suite відхиляє 261 invalid operation, validation 0/0. Лог повторів:
`build/stage7-directional-shadows/focused-repeat.log`; повний regression:
`regression-verified.log`, Python: `python.log`, підсумок: `verification-summary.json`.
Системний Vulkan loader 1.4.363, GPU API 1.4.351, app-local DLL відсутня.

Unshadowed compatibility regression після directional integration також пройдено:
ті самі `build/real-assets/{barrel-radiation,woodbox-mil}-stage7-environment-inputs`,
по 16 cycles, baseline/final `[6,12,11,2,0]`, stable capacity, drained cook users/
retirements, clean shutdown і validation 0/0. Reports:
`build/real-assets/{barrel-radiation,woodbox-mil}-stage7-directional-shadows-unshadowed-gpu/report.json`.
Усі 40 RGB captures точно збігаються з 7.4 environment baseline; SHA-256 усіх
22 original inputs незмінний. Lit captures inspected. Перевірка:
`build/stage7-directional-shadows/verify_real_assets.py`, `real-assets-verification.log`.
Це unshadowed compatibility, не shadowed real-asset/production scene acceptance;
provenance omissions залишаються незмінними.

Shadow RHI в межах незавершеного 7.5 перевірено 2026-10-09: повна
GCC/MinGW-w64 Ninja Debug-збірка, 54/54 CTest (25 GPU), 10/10 Python tests.
Новий GPU fixture перевіряє D32/D24S8 depth-only opaque/cutout, усі 8 compare
operators, одночасні ordinary/comparison samplers, linear PCF boundary,
world-transform caster movement, nearest depth у двох draw orders, positive/
negative constant bias і slope bias без pipeline growth при зміні величини.
Window/map resize, sampler destruction після recording, 16 resource/capacity
baselines і shutdown проходять із RTX 4070 validation 0/0; 33 invalid operations
відхилено. Actual DXC компілює 4 caster permutations і comparison shader;
reflection відхиляє 26 ABI/binding/sampling mutations у загальному suite.
Bindings і bias перевіряються перед pipeline creation, тому attachment-feedback
negative test не створює несумісний pipeline. Старі smoke slot-bound tests
оновлено під 6 texture / 2 sampler slots. Capture
`build/windows-x64-clean/vulkan-stage7-shadow-rhi.bmp` inspected.
Три focused CPU/DXC tests і п'ять GPU tests (shadow RHI, directional,
point/spot, environment, offscreen) пройшли по три повтори.
Logs: `build/stage7-shadow-rhi/regression-final.log`, `python.log`,
`cpu-repeat.log`, `gpu-repeat.log`; підсумок `verification-summary.json`.
Використовується системний Vulkan 1.4.363, `binaries/vulkan-1.dll` відсутній.
На цьому попередньому RHI-only кроці parser ще відхиляв `does_cast_shadows`,
а caster shader перевірявся напряму через RHI. Directional scene integration
вище замінює це історичне обмеження; пізніша spot integration описана перед нею.
Пізніша point integration вище також замінює колишній unsupported point profile.

Після shadow RHI обидва незмінені source-derived packs
`build/real-assets/{barrel-radiation,woodbox-mil}-stage7-environment-inputs`
пройшли по 16 cycles, baseline/final `[6,12,11,2,0]`, stable capacity,
drained cook users/retirements і clean shutdown із validation 0/0.
Reports: `build/real-assets/{barrel-radiation,woodbox-mil}-stage7-shadow-rhi-gpu/report.json`.
Усі 40 RGB captures ідентичні 7.4 environment baseline; SHA-256 усіх 22
original inputs незмінний. Lit captures inspected; перевірка збережена в
`build/stage7-shadow-rhi/verify_real_assets.py` і `real-assets-verification.log`.
Це regression unshadowed-render і descriptor plumbing, не shadowed acceptance
цих моделей, production cook або DX11 parity. Provenance omissions залишаються.

Ambient/constant emission/fog прийнято 2026-10-08: повна GCC/MinGW-w64 Ninja
Debug-збірка, 53/53 CTest (24 GPU), 10/10 Python tests. Вісім focused tests
(сім функціональних і fixture setup) пройшли по три повтори; лог
`build/stage7-environment/repeat-final.log`. CPU перевіряє finite schema/bounds,
enable gates, immutable/atomic state та обидва completion paths
material cook. GPU перевіряє direct + linear albedo * pow(ambient,2.2) + linear
emission; fog `1-exp(-max(view_z-near,0)/decay_distance)`; perspective/ortho,
camera translation та queued split view snapshots, включно з fog depth UV.
Cooked textured cutout не світить у holes/background, depth EQUAL відсікає
occluded emission в обох draw orders. Resize, 8 recook baselines і queued
shutdown стабільні; raw diffuse/specular debug accumulators не змінені environment.
Actual DXC відхиляє 19 ABI mutations; GPU відхиляє short/missing/mixed constants.
Material ABI v8 = 160 bytes (emissive at 144); lighting ABI v9 = 208 bytes
(ambient/fog_color_distance/fog_parameters at 160/176/192), старі v4/v6/v7
layouts збережено. Logs: `build/stage7-environment`, фінальний regression:
`regression-final.log`; capture: `build/windows-x64-clean/vulkan-stage7-environment.bmp`.
Окремий emission asset probe перевіряє warmup + 16 cycles, lights-off emission
та рівність нульової інтенсивності з видаленими lights, а не обов'язково чорним кадром.

Нові real packs `build/real-assets/barrel-radiation-stage7-environment-inputs`
та `woodbox-mil-stage7-environment-inputs` зберігають усі п'ять emission keys,
оригінальні TGA/options та попередні scalar/normal/detail/specular inputs.
Reports у відповідних `*-stage7-environment-gpu/report.json`: technical pass,
по 16 cycles, baseline/final `[6,12,11,2,0]`, stable capacity, drained users і
retirements, unchanged inputs, clean shutdown, RTX 4070 validation 0/0.
Усі 40 RGB captures точно збігаються з pre-7.4 system-Vulkan runs; SHA-256
усіх 22 original inputs незмінний. Captures inspected. Перевірка збережена в
`build/stage7-environment/verify_real_assets.py` та `real-assets-verification.log`.
У цих матеріалах emission вимкнений, тому active emission прийнято на fixtures,
а real packs перевіряють compatibility/defaults, не повну original scene parity.

Межа 7.4: constant emission [0,16] linear RGB, ambient gamma RGB [0,1],
fog linear RGB [0,1], near [0,1e6], decay distance [0.001,1e6]. Emissive maps
активного матеріалу відхиляються; inactive map flag дозволений. Складники
anisotropic direction textures, diffuse attenuation/power maps, parallax,
sequence, detail diffuse, Fresnel textures, translucency, reflections/cubemap/mask
та variation processing залишаються явно unsupported, з повними omissions у
provenance. Зокрема barrel має `use_variation_mask=true` з порожньою texture:
це unresolved source feature, а не доведений neutral/default result. Woodbox
cubemap inactive. Reference BRDF alpha_model_parameter зафіксований 0; старий
diffuse-power assignment закоментований. Сталий emission додається без масштабу
0.125 зі старого write path: його додавання в reference combine закоментоване,
тому final DX11/exposure parity не оголошується. Ширша material parity та
інтегроване приймання залишаються за межами цього обмеженого результату.

Point/spot прийнято 2026-10-08: повна GCC/MinGW-w64 Ninja Debug-збірка,
50/50 CTest (22 GPU) і 9/9 Python tests. П'ять focused CPU/GPU/DXC/ABI tests
пройшли по три повтори. CPU перевіряє 90 rejections, legacy spot -Y axis,
half-angle cosines, range/power/falloff bounds, normalization, immutable/atomic
updates та перемикання типу. GPU перевіряє numeric diffuse/specular/combine,
range edge/outside/coincidence, attenuation power, inner/outer/hard cones,
falloff, point/spot queued camera/light snapshots, split views, camera rotation
і translation, perspective/orthographic position reconstruction, змішані типи,
resize і 8 light lifecycle resource/capacity baselines. RTX 4070 validation 0/0.
На прийманні 7.3 lighting ABI v7 мав 160 bytes; старий v6 збережено як 128-byte
compatibility contract. Reflection відхиляє нові field mutations, GPU відхиляє
короткий v6 buffer і відсутні v7 constants. Логи: `build/stage7-local-lighting`.
Параметри profile: range [0.001,65536], power/falloff [0,16], positions
world/view-space +/-1e6; для local lighting потрібен rigid affine camera view.
Повні кути spot: 0 <= umbra <= penumbra, 0.001 <= penumbra < pi.
Приймання виконано на контрольних fixtures; real-asset probe лишається з
directional controls. Нове real-asset point/spot або DX11 final-frame parity
не оголошується; пізніше інтегроване приймання контрольної сцени описане у 7.8 вище.

Перевірено scalar інтеграцію 2026-10-07: GCC/MinGW-w64 Ninja Debug build, 36/36 CTest, 7/7 Python tests; Stage 7 CPU/GPU і shader ABI tests пройшли по три повтори, RTX 4070 validation 0 errors/0 warnings. CPU перевіряє 23 parser rejections, defaults/precedence, disk/dependency і immutable typed query; GPU перевіряє channels/pixels, F0=0 normals, cutout/depth/stencil, mixed v1/v2 snapshots, 8 recook baselines та shutdown. Logs: `build/vulkan-stage7-regression.log`, `build/vulkan-stage7-repeat.log`; capture: `build/vulkan-stage7-material.bmp`. Real original material surface parity і GCC Release тут не перевірені.

Перевірено normal/roughness інтеграцію 2026-10-07: GCC/MinGW-w64 Ninja Debug build, 39/39 CTest, 9/9 Python tests; чотири Stage 7 CPU/GPU tests і DXC permutations/ABI пройшли по три повтори. Покрито linear DDS/TGA vs diffuse sRGB, RGB NORMAL_MAP packing/normalized mips, A/G normal і G roughness/range/floor, mirrored transform/UV basis, cutout/depth/stencil, UV tiling, immutable queued reloads/invalid rollback, 8 map recook baselines, late shutdown. Probe regression перевіряє точний cache baseline трьох static constant buffers і всі GBuffer captures. Logs: `build/vulkan-stage7-maps-regression-final.log`, `build/vulkan-stage7-maps-repeat.log`.

Реальні surface projections `build/real-assets/woodbox-mil-stage7-surface` і `build/real-assets/barrel-radiation-stage7-surface` пройшли по 16 recook cycles із baseline/final `[4,7,5,2,0]`, stable capacity, drained users/retirements, unchanged inputs, clean shutdown та RTX 4070 validation 0/0. Звіти: `woodbox-mil-stage7-surface-gpu-final` і `barrel-radiation-stage7-surface-gpu` у `build/real-assets`. Обидва використовують оригінальні NORMAL_MAP TGA; roughness maps у вихідних матеріалах вимкнені, тому цей map-шлях прийнятий на fixtures, а реальні пакети перевіряють scalar roughness. SHA-256 усіх 16 оригінальних inputs незмінний після GPU runs. Captures inspected: normal-map pixels відрізняються від попередніх geometry-only normals; stencil/depth silhouette differences = 0 для обох. Omissions (зокрема woodbox detail normal/specular intensity) явно збережені у provenance, повної material parity немає. Новий GBuffer probe regression також пройшов три повтори: `build/vulkan-stage7-maps-probe-repeat.log`.

Material-input блок перевірено 2026-10-08: GCC/MinGW-w64 Ninja Debug build, 42/42 CTest, 9/9 Python tests. Material CPU/GPU, TGA/options, probe options, material fixture probe та actual DXC ABI/permutations пройшли по три повтори; RTX 4070 validation 0/0. Logs: `build/vulkan-stage7-material-pass-regression-final.log`, `build/vulkan-stage7-material-pass-repeat-verified.log`. Це історичний результат до directional/combine, не поточна кількість тестів.

Реальні material-input projections перевірено 2026-10-08: `build/real-assets/woodbox-mil-stage7-material-inputs` і `barrel-radiation-stage7-material-inputs`, з оригінальними diffuse/normal/specular TGA/options. По 16 recook cycles, baseline/final `[5,9,7,2,0]`, stable capacity, drained dependencies/retirements, unchanged inputs, clean shutdown, RTX 4070 validation 0/0. Звіти: `woodbox-mil-stage7-material-gpu-run3` і `barrel-radiation-stage7-material-gpu-final` у `build/real-assets`. Albedo/specular captures inspected; albedo/depth silhouette differences = 0, nonblack specular pixels = 24335/49055. SHA-256 усіх 20 оригінальних inputs незмінний. Це не DX11 lighting baseline або повна material parity: detail-normal та інші omissions лишаються в provenance цих ранніх packs; пізніший detail-input pack описано нижче.

Real-asset probe тепер вимірює model query/reload і має для них 30-second bounded wait; звичайні fixtures зберігають 5-second timeout. Woodbox query/reload maxima = 8261/8286 ms, barrel = 541/474 ms; Debug CPU decode cost не є performance acceptance. Попередні timeout reports збережені, не перезаписані. Barrel specular `.options` виявив legacy compression `high_quality` та editor-default `bump_mode=0`: додано строго перевірену metadata compatibility без зміни sRGB/mips або ввімкнення parallax, із positive/negative CPU tests. Початковий failed barrel report також збережений.

Detail-normal перевірено 2026-10-08: GCC/MinGW-w64 Ninja Debug build, 42/42 CTest і 9/9 Python tests. Розширені existing surface CPU/GPU tests перевіряють schema/defaults/ranges, залежність від base normal, linear role, immutable typed inputs, additive AG/RGB normal formula, mirrored/backface basis, cutout, strength/tiling/repeat/base UV, cancellation fallback, queued reload/invalid rollback і 8 detail recook resource/capacity baselines. Actual DXC перевіряє 24 valid GBuffer permutations, відхиляє 8 detail-without-base permutations, зберігає v2/v3 compatibility; reflection відхиляє detail ABI/binding mutations. GPU також відхиляє same-size v5 VS/v4 PS mismatch. П'ять focused CPU/GPU/DXC tests пройшли по три повтори. Logs: `build/vulkan-stage7-detail-regression-final.log`, `build/vulkan-stage7-detail-repeat.log`.

Реальний pack `build/real-assets/woodbox-mil-stage7-detail-inputs` містить оригінальний `tiled/earth/flowerbed_nmap` 512x512 NORMAL_MAP TGA, scale/tile = .1/.1, разом із diffuse/base-normal/specular. Фінальний report `build/real-assets/woodbox-mil-stage7-detail-gpu-final/report.json`: technical pass 16 cycles, baseline/final `[5,9,7,2,0]`, stable capacity, drained cook users/retirements, unchanged inputs, clean shutdown, RTX 4070 validation 0/0. SHA-256 усіх 12 original inputs повторно незмінний. Порівняно з попереднім material-input pack: normal buffer змінив 24246 pixels; albedo/specular/hardware-depth/stencil differences = 0. Captures inspected; це source-derived projection, не production cook або DX11 lighting parity. Інші omissions залишаються у provenance. Початковий real run збережено окремо; фінальний повтор після cancellation guard має ті самі hashes цих captures. Final max query/reload = 4605/4772 ms; це Debug decode cost, не performance acceptance.

Directional/combine перевірено 2026-10-08: GCC/MinGW-w64 Ninja Debug build, повний regression 45/45 CTest, 9/9 Python tests. Шість focused CPU/GPU/probe-options/lit-probe/DXC/offscreen-ABI tests пройшли по три повтори після розширення ABI negative coverage. CPU перевіряє schema, packed color, normalization, limits, IDs, immutable/atomic updates та явні unsupported requests. GPU перевіряє numeric diffuse/specular/combine, light gamma/influence, additive multiple lights, zero Fresnel, anisotropic U/V, perspective off-center vs orthographic rays, queued light/camera snapshots і split viewports, world-to-view light rotation, normal/detail inputs, cutout/resize, 8 recook resource/capacity baselines і shutdown. DXC/reflection перевіряє обидва shaders та malformed matrix/field/binding; GPU відхиляє same-size v3/v6 mismatch і відсутні lighting constants. RTX 4070 validation 0/0. Logs: `build/vulkan-stage7-lighting-regression.log`, `build/vulkan-stage7-lighting-repeat.log`.

Реальні lit probes: `build/real-assets/woodbox-mil-stage7-lit-gpu/report.json` (detail-input pack) та `build/real-assets/barrel-radiation-stage7-lit-gpu/report.json` (material-input pack). Обидва technical pass: 16 cycles, baseline/final `[6,11,10,2,0]`, stable descriptor/uniform capacity, drained cook users/retirements, unchanged inputs, clean shutdown, validation 0/0. Контрольні два протилежні white directional lights мають normalized travel direction +(0.25,-0.4,1)/negative, intensity .5 кожне; report явно відділяє їх від original scene lights. Intensity .125 змінює 24307/47058 pixels понад threshold 8; zero intensity точно відтворює empty frame; відновлення .5 повертає початковий lit кадр. Captures inspected. Normal/albedo/specular/hardware-depth/stencil buffers мають 0 pixel differences із попередніми detail/material-input runs для обох моделей. SHA-256 усіх 22 original inputs повторно незмінний після обох GPU runs. Max query/reload: woodbox 5049/4800 ms, barrel 219/237 ms, лише Debug decode measurements. Це source-derived material projections із provenance omissions та synthetic lights, не production cook, DX11 final-lighting parity чи performance acceptance.

### Поточний порядок passes

Це реалізований шлях 7.1-7.7, не новий підетап.
Geometry writes GBuffer RT + depth_write; material inputs LOAD того самого
depth/stencil з depth EQUAL/stencil EQUAL 0x82 без writes, пишуть linear
albedo/specular/emission+occupancy у три RGBA16F RT. Перед lighting colors і
material targets переходять у shader_resource, D24S8 у depth_read.
Fullscreen directional/point/spot pass читає GBuffer з nearest sampler, пише
два cleared RGBA16F accumulators через ONE/ONE ADD. Перед shadowed light pass
accumulator pass завершується; D32 atlas переходить у depth_write для casters,
потім depth_read для comparison sampler. Accumulators відновлюються з LOAD,
receiver модулює direct light, потім accumulators переходять у shader_resource.
Combine пише третій RGBA16F RT із albedo*diffuse + material_specular*specular.

У lit mode environment pass читає combine/emission/albedo/depth, додає ambient,
emission і fog; спожитий diffuse accumulator повторно служить final linear RT.
Sky LOAD final RT + D24S8, depth LEQUAL/stencil EQUAL 0 без writes; forward LOAD
тих самих attachments, LESS без depth/stencil writes, alpha/additive у linear
FP16. Final color переходить у shader_resource, depth у depth_read. Для MSAA
final HDR color переходить у transfer source, окремий 1x FP16 target у transfer
destination; color resolve повертає обидва у shader_resource, depth лишається MS.
TAA читає current HDR/depth і попередню color/depth history, пише іншу пару
RGBA16F/R32F та переводить її у shader_resource; sampled input не є attachment.
Якщо bloom увімкнений, extraction scene->A, horizontal A->B, vertical B->A, composite scene+A->C
працюють у RGBA16F без depth/stencil; після кожного pass color стає shader_resource.
Якщо FXAA увімкнений, наступний color-only pass читає final scene, застосовує
manual exposure/Reinhard та sRGB encode у RGBA8 UNORM; RT переходить у
shader_resource. FXAA bilinear LOD0 читає encoded RGB без автоматичного decode,
фільтрує та декодує результат у RGBA16F RT, який також стає shader_resource.
Presentation читає цей output без повторного tonemap, потім hardware sRGB encode.
Без FXAA presentation читає final scene і застосовує exposure/Reinhard лише коли
увімкнений lit postprocess. Raw debug accumulators не проходять environment/sky/
forward/bloom/tonemap/FXAA. Inputs не одночасні attachments; resize/release
використовують fenced retirement. Інтегроване 7.8 перевірило всі ці passes
на єдиній контрольній M7 сцені; докладний inputs/outputs/resource-state contract
описаний у README.

### Межа завершення

Критерій завершення M7 виконано: закриті 7.1-7.8 у зафіксованому обсязі на контрольній static opaque/cutout scene з normal/roughness матеріалами, directional/point/spot lights, ambient/emission/fog, shadow maps/cascades, skybox, non-intersecting unlit alpha/additive forward surfaces та HDR/tonemap з погодженим bloom/AA. Є незалежні pixel/expected-image перевірки матеріалів і фінального кадру, camera/light movement, resize/reload/recook, fixed-phase stability, exact history isolation/reset та resource/capacity bounds; validation errors/warnings 0/0 включно з teardown. Це bounded static acceptance на RTX 4070/GCC Debug, не довільна animated-scene temporal quality або DX11/production parity. Звичайний game/editor startup, інші класи сцени, чистий GCC Release та додаткові GPU залишаються в етапах 8/9. Наступний функціональний етап 8; нових підетапів M6/M7 для виконаних passes не створюється.

## Етап 8. Інші типи сцени, UI та редактор

Передумови: етап 7 для повної сцени; UI і debug drawing можна починати після етапу 5.

### Поточний статус і порядок

Стан 2026-10-17: **M8 відкритий**. Нижче є єдиний актуальний чекліст.
Прийняті bounded profiles не означають завершення всього terrain або
game/editor startup і не відкриваються повторно через розширення функцій.
Нових нумерованих підетапів немає. Деталі реалізацій та історія приймання
зберігаються в `sources/xray/render/vulkan/README.md` і звітах нижче;
історичні результати не є окремими активними завданнями.

**Наступний функціональний крок:** copied queued particle play/stop/transform/
visibility/pause/remove, потім scene submission/drain і real supported billboard
proof поверх прийнятих scene-owned worlds, typed game/facade userdata,
bounded cooked templates, material-ready instances та billboard snapshot/RHI.
Наявна simulation використовується без переписування.
World/instance cooks уже утримують adapter/world, мають close/drain та restartable
registration; unsafe raw-binary/source template і preview cooks не реєструються.
Штатний initialize тепер реєструє bounded version-1 template cook; готовність усіх
material dependencies до instance publication уже реалізована: atomic rejection,
утримання pending dependencies, rollback і повторна closed-world перевірка.
Scene cook уже створює окремий world dependency через єдині зовнішні particle
cooks; scene drop/session close закриває його до deferred resource drain.
Game instance query передає typed retained world handle замість raw pointer.
Потрібно інтегрувати copied queued play/stop/transform/visibility/pause/remove,
scene submission/drain та real supported billboard proof.
Material callback lifetime і repeated-play list corruption уже виправлено;
прийняті ownership/callback/snapshot компоненти не повторювати як нові завдання.
Particle scene flag дозволяє лише lifecycle creation за наявності cooks на
owner thread; draw такої сцени, raw `particle_world()` API, particle commands,
preview/mode/slomo лишаються explicit-error gates до відповідної інтеграції.
Terrain wrapper/contact прийнято у зафіксованому профілі: production engine
hook, справжній cooked dependency та rotation-locked sphere drop/translation.
Engine-owned collision cook/deleter і `terrain_model_class` уже реалізовані та
перевірені: renderer лише query/retain ресурсу class 192, не лінкує collision/
physics archives у свою DLL. Wrapper має справжні render instance, Opcode
geometry та owned centered/Z-reflected Bullet BVH з internal-edge metadata.
Прийнято immutable translation-only placement: non-noop buffer/transform edits
явно відхиляються до publication, no-op та renderer-only bounds дозволені.
Game cell використовує authored transform/local center і звільняє collision
object/body перед wrapper; actual rigid-body retention та queued renderer removal
перевірені окремими probes. Це не звичайний threaded game/editor startup.
Resource cooks, wrapper query/lifetime, foundation або contact profile не
виконувати повторно як нові підетапи. Hook зберігає callback chain, фільтрує
зареєстровані terrain meshes, підтримує обидва manifold orders і живе до drain.
Install/finalize потребують quiescent physics callbacks; concurrent lifecycle
та ordinary threaded startup не прийнято. Free-rotation sphere drift не
оголошено виправленим; інші body profiles не додаються приховано до цього пункту.
Mixed decoded-RGBA normalization прийнято нижче: 178 already compatible +
551 normalizable = 729 eligible original cells; 69 відхиляються до allocation
через незмінний 256 MiB budget, 16 мають missing `terrain/junk_industrial`.
Це metadata eligibility, не GPU приймання всіх 729 cells. Прихованого
downsampling/підвищення budget немає. Не відкривати normalization, importer,
cooks, binding integration або skeletal baseline заново. Далі йдуть particles
та інші типи сцени/game/editor workflows з єдиного відкритого списку.

### Прийнято

- [x] Scene-owned particle world lifecycle і typed game/facade userdata: scene translate cook створює окремий class `particle_world_class` dependency через зовнішні production cooks; renderer DLL не лінкує particle archive і не реєструє дубльованих cooks. Scene утримує world, world утримує lifecycle adapter зі weak session owner; scene destruction/session close закриває world перед drop/drain, розриває active self-retention та прибирає live particles навіть за retained instance/userdata handles. `particle_world_handle()` копіює typed ownership через справжній facade; game instance query більше не передає raw pointer. Перевірено production disk template/material chain, окремі worlds для двох scenes, три restart cycles, closed/pending/retained/foreign/type/thread guards і hidden Vulkan renderer shutdown з validation 0/0. Scene flag приймає лише lifecycle creation на owner thread із готовими cooks; queued particle commands і draw particle-enabled scene поки явно відхиляються. Це не scene rendering, ordinary threaded startup або original effect acceptance.
- [x] Bounded production particle templates: class `particle_system_class` читає лише version-1 checked binary config із `resources.converted/particles/<logical-name>.particle`, без Lua/source/raw-vtable loading чи запису cache. Один LOD, 1..64 owned emitters/actions/curves, camera-facing square/rectangle, constant rate/lifetime та initial linear color/size/velocity/radian rotation, local/world-space; explicit per-emitter/template/occupancy bounds, strict fields і рання path validation до VFS assertion. Штатний initialize/finalize координує world/instance/template registration, preflight foreign owners і retained/pending/thread/deleter drain; preview не вмикається. Actual disk template -> ready material/texture -> existing birth/motion/pause/stop/expiry -> immutable snapshots -> hidden GPU proof; local rotated rectangle, TGA/sRGB, alpha/additive, max 64 emitters та capacity 50000 перевірено. Спільний checked-config parser перенесений у core без renderer/particle archive cycle. Це bounded constant-profile acceptance на synthetic cooked fixtures, не original particle effect/assets/parity, arbitrary authored curves/events/bursts/domains, scene submission або ordinary threaded startup.
- [x] Material-ready particle instance publication: production instance cook очікує aggregate material dependencies для всіх emitters, утримує world/template/caller-owned cook data до завершення callback і лише потім створює renderer emitters із точними ready handles без повторного async load. Missing/failed material, unsupported billboard profile, cyclic action chain, adapter allocation/material rejection та close під час pending load або самого constructor callback не публікують instance; partial construction і dependencies drain. Production world/instance/material/texture chain перевірено з controlled parsed template, TGA/sRGB alpha/additive і GPU drawing copied instance snapshots після cook drain. Це не production template parser, birth simulation, scene ownership/submission, original particle effect або ordinary threaded startup.
- [x] Production particle material effects profile: зареєстрований `material_effects_instance_class` для caller-owned billboard cook data, checked `f_stage_unlit` material/texture dependencies та окремий typed effects resource. Alpha/additive, двосторонній unit-UV матеріал, RGBA8 linear/sRGB TGA або authored constant-color white sample; fixed linear clamp LOD0, custom sampler/UV/cull profiles, BC, opaque, subUV/beam та raw payloads відхиляються. Immutable tint/blend/texture generation, old snapshot після reload та explicit recook; actual emitter callbacks, failed-material retention, late callback після deletion, registration ownership і deferred dependency drain. CPU/disk controls і independent GPU blending над cooked snapshots після cook drain; це не production template loader, scene submission, original particle assets/parity або ordinary threaded startup.
- [x] Production particle world/instance cook ownership profile: typed retained world query userdata замість raw world pointer; pending child query та створений instance утримують world, world утримує adapter і aligned bounded arena 1..50000 particles. Explicit owner-thread close розриває active self-retention, closed/foreign/null operations відхиляються, partial construction rollback і metadata-before-resource_ptr; restartable registration та nonblocking cook drain. Trusted cooked-template topology: один LOD, 1..64 emitters, без child/event emitters; source/raw-binary template і unfinished preview cooks не реєструються, direct world preview APIs дають errors. Production world/instance cooks перевірено з controlled template/material fixture dependencies, arena limits, pending close, seeded real particle teardown, registry displacement та чотирма re-registration cycles. Це не production template parser, scene ownership/submission, real particle assets або ordinary threaded startup.
- [x] Existing particle simulation lifetime hardening: окремі retained cook data для кожного material query, latest-generation publication, callback після видалення emitter не звертається до нього, failed query не замінює попередній material, renderer adapter cleanup при constructor rejection. Repeated play не дублює intrusive node; stop/play відновлює creation. Справжні world/emitter/instance і resource manager, controlled delayed fixture cooks, 32 destruction cycles, pause/visibility isolation, active self-retention та remove/replay/drain. Production template/instance/world cooks, raw-world ownership, concurrent simulation mutation та scene/facade integration цим не прийнято.
- [x] Particle billboard snapshot/RHI foundation: copied existing `base_particle` span/intrusive list, camera-facing square/rectangle, rigid local/world transforms, radian rotation, linear tint, stable per-batch depth order, dead/zero filtering. RGBA8 2D LOD0 alpha/additive draw, opaque depth test/no depth writes, shader/sampler/image/geometry lifetime; 4096 input particles/batch, 64 batches і 8 MiB geometry/64 MiB unique textures/frame, retained cache максимум дві bounded generations. CPU limits/facade gates і hidden GPU independent blend/depth/color/rotated-camera UV/resize/recook/drain controls. Це component foundation, не world/resource/facade integration, simulation, real particle assets або scene feature flag.
- [x] Debug indexed lines/triangles: immutable queued batches, depth/overlay, blending, viewport/scissor; unlit і final lit.
- [x] UI/font overlay через RHI: vertices, atlas textures, alpha blending, scissor, byte-font layout і реальний Arial TGA. Це bounded facade profile, не всі старі UI APIs або editor startup.
- [x] Skeletal renderer і game wrapper: штатні cooks, CPU four-weight skinning, atomic pose/bounds snapshots, `update_skeleton()`, animation skeleton dependency і drain/lifetime checks.
- [x] Real-source character/animation/surface profile: neutral_01, два original full clips, root motion/loop, fixed blend і timed transition controls, diffuse/normal/specular materials та scalar roughness. Це supported projection, не повний original actor/PBR parity.
- [x] Terrain CPU import/atomic edits: chunks 102/103, 22-byte samples, slot aliases, geometry/bounds/basis; усі 814 original cells перевірено незалежним CPU oracle.
- [x] Terrain mesh/instance cooks: штатні resource queries, diffuse/normal/specular dependencies, isolated snapshots, missing-resource errors і deferred drain.
- [x] Terrain game-wrapper resource integration: `terrain_model_class` query/retain engine-owned class 192 collision resource із справжніми Opcode/Bullet dependencies, single deleter owner та isolated render instances. Authored translation/local center, game-cell unload order, actual static rigid-body retention, closed-source/pending-wrapper rejection і delayed cook drain; immutable buffers/placement з atomic rejection. Synthetic hidden GPU plain/wrapper/no-op/removal controls та два real mixed-cell CPU geometry/lifecycle proofs. Production moving-body contacts і звичайний threaded startup не входять у цей прийнятий resource profile.
- [x] Terrain game-wrapper contact acceptance: production engine-owned Bullet internal-edge hook для зареєстрованих class 192 meshes, automatic flag на actual game rigid body, callback chain/material/return preservation, обидва manifold orders і point reprojection, registry-before-BVH teardown та displaced-owner rejection. Bounded sphere radius .5/mass 1 із заблокованим обертанням: 600-step translated negative-height flat-grid drops, disabled-flag A/B, game ground friction=100, дві 300-step awake/frictionless translations через шви, retained-body/closed-session lifecycle і чотири re-registration cycles. Correction callback у тесті не підміняється. Free rotation, інші body profiles, live collision edits і ordinary threaded startup не прийнято; цей bounded пункт завершений, не новий підетап.
- [x] Terrain unlit diffuse GPU profile: scene flag, facade add/remove/copied atomic buffer updates, 1..16-layer arrays, weights/IDs/color/shift, queued geometry/texture snapshots, resize та lifecycle controls.
- [x] Terrain bounded deferred material/lighting: GBuffer з normal arrays, albedo/specular layer pass, roughness 50/255, Fresnel .075; synthetic directional/ambient lighting та два real-source albedo/specular/ambient-lit controls. Початковий 1x, LOD0, homogeneous arrays per role; shadows і MSAA/TAA прийнято окремо нижче, original scene parity не прийнято.
- [x] Terrain shadows: immutable terrain geometry/transform у спільному opaque caster list; directional cascades, spot та point atlas path, off-camera casters, terrain/static cast/receive, queued edits/removal/light isolation і resize/drain. Дев'ять synthetic profiles/764 ray checks; два real cells мають directional cast/receive controls. Початковий 1x proof розширено AA controls нижче; не full original scene parity.
- [x] Terrain bounded MSAA/TAA: shared sample-shaded deferred resolve 2x/4x/8x, terrain geometry/transform/role-texture history signatures, no-op history preservation і jitter-expanded culling. Synthetic coverage/reset/shadow/split-view/update/resize/drain та два real-cell AA profiles; не universal TAA quality, motion-vector reprojection або original image parity.
- [x] Renderer `terrain_update_cell_aabb`: copied queued facade/world update, per-instance local hint unioned із current geometry, atomic finite/ordered validation, current camera culling без mesh reupload/TAA reset. Synthetic submission/caller/split/stress/history/off-camera shadows і два real cells; не collision tree/game wrapper.
- [x] Terrain coherent texture reload: immutable per-cell diffuse/normal/specular set для всіх slots, publication після reload drain, утримання попереднього набору при failed role/несумісному array layout, shared validation і coherent TAA/pass snapshots. CPU/disk/synthetic GPU та read-only real-cell controls; не binding add/remove/exchange, options hot reload або scene-global/disk-writer transaction.
- [x] Terrain texture binding add/remove/exchange: transactional renderer core та async world/game facade path, спільний із наявними editor commands; FIFO copied add-slot -> buffer/AABB updates, batch exchange усіх affected cells/aliases, no-op identities, stale/foreign/closed/changed-dependency rejection. Sparse slots без renumbering, empty-tail trim та direct/interpolated/zero-weight hole validation; immutable queued pairs і mesh-keyed cache без binding-only upload. Per-scene failure cancels remaining commands, membership add/remove та shutdown скасовують stream; weak callbacks і observable status/drain. Межі: 128 commands, 128 in-flight triplets включно з cancelled, 8 MiB copied sample/fragment storage. Це bounded renderer/facade acceptance, не звичайний threaded editor startup.
- [x] Terrain bounded mixed decoded-RGBA normalization: same-format power-of-two/full-mip sources до найбільшого dimension кожної role, без downsampling або BC transcoding; authored mips byte-exact, нові finer levels із periodic linear-light/alpha та packed AG-normal filtering. 256 MiB array preflight до pixel allocation, alias dedup/reuse, coherent initial/reload capture, raw-vs-derived binding batch checks, immutable queued pairs, no-op/TAA/resource drain. CPU oracles та synthetic GPU controls, два real mixed cells/чотири material modes і real budget rejection; повторний inventory 178+551 eligible / 69 budget / 16 missing. Це accepted bounded profile, не всі original cells/scenes, mixed formats, missing-mip synthesis або original image parity.

### Залишається виконати

- [ ] Particles: copied queued play/stop/transform/visibility/pause/remove, scene submission/drain і real supported billboard acceptance. Scene-owned close-before-drop, typed game/facade query userdata, bounded cooked template/constant simulation profile, material-ready publication, material effects profile, world/instance cook ownership, callback/replay repairs та snapshot/RHI foundation прийнято вище, не дублювати їх. Scene flag тепер підтримує lifecycle creation через зовнішні cooks на owner thread; draw particle-enabled scene, raw particle_world API, facade commands і previews поки explicit-error gates. Unsafe source/raw-binary template та preview registrations вимкнені; bounded version-1 template cook ввімкнений штатним initialize. Перший target profile: camera-facing unlit square/rectangle RGBA8 LOD0 billboards, alpha/additive; locked axes/subUV/beam/trail/mesh/decal particles, lighting/soft particles/AA та cross-emitter sorting не оголошено готовими і не додаються приховано як нескінченні підетапи. Розширення за потрібними real assets/workflows фіксується явно до приймання.
- [ ] Рослинність: перенести потрібний vegetation/SpeedTree resource і rendering workflow; наявні no-ops не є реалізацією.
- [ ] Decals: resource/material integration, add/update/remove та перевірка кадру/lifetime; наявні no-ops не є реалізацією.
- [ ] Capability/no-op audit для game/editor шляхів: кожна ввімкнена можливість реалізована і перевірена, кожна непідтримана дає явну помилку. Це аудит інтеграції, не повторне виконання попередніх renderer блоків.
- [ ] Зафіксувати перелік додаткових effects/editor modes для цільової сцени: distortion, SSAO, GI/LPV та розширений postprocess. Для кожного явно визначити «потрібен у M8» або «відкладено»; реалізувати й перевірити потрібні. Сам пріоритет без погодженого scope не закриває цей пункт.
- [ ] Перевірити geometry/tessellation/compute requirements саме потрібних effects; реалізувати stages/capability checks до їх увімкнення. Усі можливі shader stages не є автоматичною умовою M8.
- [ ] Звичайний game startup: штатна scene configuration з потрібними terrain/particles та іншими прийнятими типами, logic/render/resource threads, повний query/load/unload/shutdown lifecycle. Single-thread probes не замінюють цю перевірку.
- [ ] Editor renderer startup: підключити facade/editor renderer у поточній GCC конфігурації. Renderer уже є DLL, але глобальний `XRAY_STATIC_LIBRARIES` у facade досі залишає `m_editor_renderer = 0`; це не вимога створити ще одну static-library конфігурацію.
- [ ] Editor outputs/previews: незалежні outputs/swapchains, viewport/targets/frame state, resize/закриття одного view без пошкодження іншого та спільні device resources.

### Межа завершення M8

Етап 8 завершується після виконання відкритого списку у заздалегідь погодженому
функціональному обсязі. Пункти не можна мовчки вилучати або перетворювати
на нові нескінченні підетапи. Перелік додаткових effects/editor modes ще
потребує фіксації; до цього повний scope M8 не оголошується закритим.

Приймання має включати звичайну game scene з потрібними типами об'єктів,
UI/debug, завантаження/оновлення/вивантаження та штатний threaded shutdown;
окремо editor main view і preview з незалежним resize/закриттям.
Потрібні logs, незалежні image/update controls, resource/drain checks,
validation 0 errors/0 warnings через teardown та незмінні original assets.
Автоматичні GPU windows лишаються hidden/nonactivating/input-transparent.

GPU skinning, інші skeletal layouts, additive/довільні animation graphs,
event callbacks, motion vectors і повний original actor/terrain image parity
не додаються приховано до прийнятих профілів. Якщо вони потрібні цільовому
workflow, зміну scope слід погодити окремо. GCC Release, додаткові GPU,
performance profiling та failure injection належать Етапу 9, не M8.

### Докази та межі прийнятих профілів

Це індекс доказів, не ще один план або чекліст.

**Scene-owned particles, 2026-10-17:**
`build/stage8-particle-scene/summary.json` та `verify_acceptance.py`.
Production scene/world/template/instance/material cooks і lifecycle adapter:
45 CPU / 51 GPU negative controls, три restart cycles, справжній game/facade
typed handle і retained/pending renderer shutdown. Повний фінальний прогін
**113/113 CTest, включно з 59 GPU**, 303.77 с; **54 CPU x3 = 162** виконання,
37.87 с, **10 particle x3 = 30** виконань, 12.92 с; **14 Python**.
Усі 56 validation summaries мають 0 errors / 0 warnings. Старі 7 billboard,
4 material і 4 simulation captures побайтово незмінні; SHA256 підтверджує
незмінність 913 original files, трьох absent paths і 63+49 pack inputs.
Перший повний прогін дав 109/110: старий integrated M7 перевищив 120 с.
Ті самі 12 AA profiles тепер розподілені між base matrix та трьома serial
8x cases без зміни 64 convergence / 32 stability frames, pixel/lifecycle
oracles або timeout. Їхні 12 captures побайтово незмінні; фінальні cases
зайняли 18.37 / 10.55 / 10.52 / 10.50 с. Це partition test harness, не
виправлення renderer performance; повний all-profile CLI лишився доступним,
profiling і довгий same-process stress належать M9. Scene rendering,
copied queued commands, real effects та ordinary threaded startup цим
lifecycle proof не прийняті. Нових нумерованих підетапів немає.

**Debug/UI:** `build/stage8-debug/summary.json`,
`build/stage8-ui/summary.json`. Debug працює в scene phases; UI виконується
після AA/tonemap, з display-sRGB RGB і linear alpha blending. Byte-font layout
не заявляє Unicode shaping/kerning. Перевірено real Arial atlas/metrics,
independent pixels, split viewports, immutable reloads і bounded retirement.

**Skeletal/animation/character:** `build/stage8-skeletal/summary.json`,
`build/stage8-skeleton-wrapper/summary.json`,
`build/stage8-real-skeletal/summary.json`,
`build/stage8-animation-player/summary.json`,
`build/stage8-character-materials/summary.json`.
Підтримано raw 76-byte та converted 48-byte four-weight vertices, 1..80
render bones, до 128 surfaces/64 MiB aggregate import; palette
`inverse_bind * absolute_pose`, rigid final transforms без scale/shear/reflection.
Animation skeleton cook належить animation subsystem, movement root не входить
в renderer pose array. Real neutral_01 має 62 render bones/3 surfaces; два camp
clips покривають усі 63 animation bones. Real clip hierarchy/replay перевіряються
окремо від synthetic Head translation control; той control не є прийнятою
animation pose. Surface profile використовує 9 original TGA; roughness scalar,
бо source materials вимикають roughness maps. Remap coefficients [0,16]
зберігаються без clamp, результат насичується при записі UNORM.
Це не всі animation/controllers/material features або normal startup.

**Terrain CPU:** `build/stage8-terrain-import/summary.json`,
`build/stage8-terrain-import/real-summary.json`.
Limits: 2 MiB compressed/inflated payload, row 2..256, physical size .001..65536,
1..16 slots; exact 22-byte samples, slot order/aliases збережені.
Atomic nonoverlapping rectangles: 1..4096 fragments, tight rows, rollback,
unchanged identity. Два scans 814/814: 3439150 vertices/6668288 triangles кожний;
independent struct/zlib oracle та SHA-256 усіх original cells. Це CPU import,
не image acceptance всіх 814 cells і не legacy quantized GPU height parity.

**Terrain cooks:** `build/stage8-terrain-cooks/summary.json`,
`build/stage8-terrain-cooks/real-summary.json`,
`build/stage8-terrain-cooks/real-pack-final/provenance.json`.
Canonical/create/project queries; aliases зберігають user indices і ділять
dependency handles. Existing DDS має пріоритет, corrupt DDS не запускає TGA
fallback; source options/normal force-linear збережено. До 48 texture queries,
square 2D <=4096, 256 MiB published unique decoded mip bytes на mesh;
це post-decode cap, не peak-allocation guarantee. Real alex, night_test та
a03_bomb_shelter перевірені через cooks; missing junk_industrial є expected
negative control. `terrain_model_class` досі explicit-error gate до collision.

**Terrain GPU:** `build/stage8-terrain-gpu/summary.json`.
Unlit, explicit LOD0, homogeneous diffuse arrays 1..16 layers; 64 cells/
262144 vertices на scene, 256 MiB diffuse upload bytes на queued view.
Формула `(sum(diffuse_i * weight_i) + (1-sum(weights))*.5) * color * 2`;
weights не нормалізуються, tiling 8, shift (-X,+Y)/physical_size.
38-byte facade adapter приймає лише три однакові UV sets/shared shift;
ABI не змінено, independent UV sets unsupported. Старі queued geometry/images
утримуються immutable, caller data копіюються власним facade allocator.
Два real cells: alex та a03_bomb_shelter, по 864 independent RGB checks на run,
два final runs із byte-identical captures, tolerance 3/255 із фактичним
`subPixelPrecisionBits`; synthetic tolerance 2/255. Усі 49 source/pack files
незмінні. Night_test змішує 1024/2048 textures: CPU прийнято, цей homogeneous
GPU profile його не приймає. Snapshot reload тут не означає приймання
coherent layer reload/texture operations. Deferred maps/lighting мають окремий
proof нижче; shadows/AA та game wrapper у цей unlit proof не входять.

**Terrain deferred:** `build/stage8-terrain-deferred/summary.json`.
Спільні scene GBuffer/material/lighting passes, не другий terrain scene pipeline.
Array dimensions явно відображаються у reflection/binding для diffuse slot0,
normal slot1 і specular slot3; unsupported slots/descriptor arrays лишаються
помилками. Normal AG/Y-flip, normalized per-layer blend з neutral remainder;
zero-vector cancellation має neutral fallback. N/T/B transform враховує
nonuniform scale; albedo зберігає weights/grey/color/shift, specular використовує
weighted red channel без grey remainder, як original terrain shaders.
Scalar roughness 50/255, Fresnel .075; emission zero, stencil 0x82, EQUAL depth
у material pass. Окремі exact-size 128/144-byte constant buffers; ABI не послаблено.
Homogeneous dimensions/format/mips потрібні всередині кожного role array,
між diffuse/normal/specular roles вони можуть різнитися. Спільний queued-view
cap 256 MiB тепер рахує всі terrain arrays, не тільки diffuse.
Synthetic controls охоплюють шість weight sets, normal/GBuffer/material,
directional diffuse/specular/final-lit, ambient-lit, queued normal/specular
reload isolation та unlit+lit recooks. На момент цього deferred proof
shadows/MSAA/TAA давали capability errors; їх прийнято окремими proofs нижче.
Два real runs: по 3456 RGB checks у unlit/albedo/specular/ambient-lit,
8 captures/run byte-identical; попередні unlit captures теж незмінні.
Real-cell normal/directional pixel oracles не заявляються: ці controls synthetic.
49 original/pack files SHA-256 незмінні. Це source-derived 1x/LOD0 profile,
не legacy parameter-map/detail/parallax/full scene image parity.

**Terrain shadows:** `build/stage8-terrain-shadows/summary.json`.
Opaque cell mesh/transform потрапляють у спільний caster list до shadow
preparation/planning, навіть коли camera frustum їх не бачить. Наявний static
depth-only mesh path використовується тільки для shadow maps; main terrain
layer/GBuffer/material path не замінюється static material. Directional depth
fitting враховує terrain bounds; кожний queued draw утримує власний immutable
mesh і transform. Використано чинні atlas/PCF/bias/fences без нових shaders,
texture dependencies або послаблення наявних shadow checks.
Synthetic: terrain/terrain, terrain/static, static/terrain для directional,
spot і point (9 profiles, 764 independent ray checks); окрема bounded PCF
edge exclusion .04 NDC у новому oracle, незмінний RGB tolerance 2/255.
Перевірено off-camera casters, cascades 1..4, point +Z face-mask control,
linear transparency, queued geometry/removal/light snapshots, copied caller
edits, 8 resize/atlas-size/capacity cycles та retirement/pass drain.
Два real-source runs: alex і a03_bomb_shelter отримують directional shadow
від static mesh і самі відкидають його на static receiver поза camera frustum;
по 120 visibility RGB checks/run, transparency=1 точно відновлює baseline.
12 captures/run byte-identical, попередні 8 unlit/deferred captures теж незмінні;
49 original/pack files SHA-256 незмінні. Real spot/point controls не заявляються
(вони synthetic). Контрольний real shadow-receive кадр переглянуто. Це bounded
1x integration на момент цього shadow proof; AA прийнято нижче. Game/editor
startup або original image parity цим не заявляються.

**Terrain MSAA/TAA:** `build/stage8-terrain-aa/summary.json`.
Terrain використовує чинні sample-shaded GBuffer/material/lighting і resolve,
без нових shaders. TAA signature включає cell/transform та snapshots усіх трьох
texture roles, навіть для off-camera casters; незмінний edit не скидає history.
Terrain і static models використовують спільний jitter-expanded frustum.
Synthetic: 584496 independent k/N linear-radiance coverage pixels, 2621 partial
pixels у 2x/4x/8x; tolerance 2/255. TAA history відрізняється від jitter-only;
8 no-op phases byte-exact, 11 automatic reset controls exact проти first jitter.
Перевірено weights/transform/direct CPU geometry, diffuse/normal/specular reload,
add/remove, camera cut/projection і off-camera caster edit; 24 directional/spot/
point AA shadow profiles, 4x+TAA+bloom, split-view history/queued edits і jitter-only
frustum edge. 128 dynamic edits: active resources/capacity незмінні, retired
buffers peak 2 при bound 4 (два buffers x два frame slots), final drain exact.
8 resize cycles, minimize/restore та retirement/pass drain пройдено.
Два real runs: по 8 AA profiles/16 exact resets/60 AA-shadow RGB checks,
плюс попередні material/shadow controls. 30 captures/run byte-identical,
попередні 12 captures незмінні; 49 original/pack SHA-256 перевірено.
Це bounded LOD0 integration, не full original parameter-map/detail/parallax,
universal TAA quality/motion vectors, game/editor startup або весь M8.

**Terrain local AABB:** `build/stage8-terrain-bounds/summary.json`.
`terrain_update_cell_aabb` працює через world/editor та copied queued scene
facade. Per-instance optional local hint не змінює asset/cell/mesh. Усі шість
координат finite/ordered у +/-1e6, degenerate axes дозволені; validation atomic.
Camera frustum використовує union із current geometry, також після наступних
buffer edits. Старі queued draws вже мають власну visibility/mesh; bounds-only
edit не скидає TAA або GPU cache. Shadows використовують exact mesh bounds,
а не metadata для depth fitting; collision tree/game wrapper не заявляються.
CPU resource/adapter: 55 rejections та positive isolation/conservative controls.
GPU: реальна зміна submission для off-screen cell (два VB/IB buffers), copied
caller/queued shrink/split, 128 exact pixel/live/capacity updates, 8 exact 4x+TAA
phases, directional/spot/point off-camera shadow images/rays і final drain.
Два real runs: по 64 bounds updates/16 exact TAA phases, без зміни mesh/resources.
32 captures/run byte-identical; попередні 30 AA/material/shadow captures незмінні;
49 original/pack SHA-256 незмінні. Це renderer integration, не весь terrain/M8.

**Terrain coherent reload:** `build/stage8-terrain-reload/summary.json`.
Instance утримує immutable set diffuse/normal/specular для всіх user slots.
Registry mutex + state mutexes у чинному lock order виключають torn capture;
до завершення pending queries або за наявності failed member лишається старий set.
Compatibility/linear-normal/256 MiB checks спільні з GPU array validator;
несумісний candidate не потрапляє у renderer, повторні tuple errors coalesced.
Один captured set на queued cell живить TAA signature і обидва terrain passes.
Generic texture publication/status не перетворено на глобальну transaction;
instance має окремі accepted/layout-rejected counters. Geometry/AABB не змінюються.
CPU: partial completion, malformed disk normal, recovery, aliases, per-role resize.
GPU: queued old/new, normal/specular oracles, invalid layout hold, 8 all-role
resize/resource/capacity cycles, 8 exact failed-reload 4x+TAA phases та accepted
first-jitter reset. Два real runs: по 16 controlled pending/failed/incompatible/
recovery phases без запису source files; 34 captures/run byte-identical,
попередні 32 captures незмінні, 49 original/pack SHA-256 незмінні.
Це bounded per-cell image-data reload, не binding operations, heterogeneous
arrays, options/material-graph hot reload або on-disk authoring atomicity.

### Transactional Terrain Binding Core (2026-10-15)

Evidence: `build/stage8-terrain-bindings-core/summary.json`.
Preflight для вже завантажених dependencies не публікує нічого; commit
перевіряє generation інстанса, source cell і current coherent image tuple.
Усі allocations передують publication. Підтримано aliases, sparse slots до 15,
interior remove і tail trim без renumbering. Direct та triangle-interpolated
IDs не можуть вказувати на hole, навіть при нульовій вазі; disk import лишається
строгим. Binding-only metadata ділить mesh; GPU cache keyed by mesh, sample
edits завжди отримують нову mesh identity. No-op зберігає TAA history.
CPU: 28 нових binding rejections; 57 resource/adapter rejections включають
closed preflight/commit. GPU: queued old/new triplets, normal/specular oracles,
sparse updates, no geometry reupload, 8 resource/capacity cycles, 8 exact no-op
4x+TAA phases, accepted first-jitter reset і drain. Два real runs: по 3456
додаткових binding RGB checks у чотирьох modes, exact restored image/mesh/buffers,
38 captures/run byte-identical, попередні 34 незмінні, 49 original/pack hashes
незмінні. Exchange/restored captures переглянуто візуально.
Це core-level операції над завантаженими ресурсами, не facade/world integration:
async queries, editor ordering, scene-global atomic exchange та remove/close
cancellation лишаються наступним функціональним кроком у канонічному списку.

### Async Terrain Binding Facade/World (2026-10-15)

Evidence: `build/stage8-terrain-bindings/summary.json`.
Три world stubs замінено; copied game facade та наявні editor commands
використовують одну per-scene FIFO implementation. New-slot buffer/AABB edits
чекають async diffuse/normal/specular query; render утримує last complete pair.
Exchange preflights усі affected cells/aliases; batch capture і всі allocations
передують nonthrowing publication. Failure скасовує решту scene stream, але не
раніше committed prefix. Membership add/remove та close скасовують pending work;
weak callbacks не тримають scene/model/world, late results лише drain queries.
Status requested/committed/failed/cancelled/pending, queries/in_flight та
queued_bytes доступний через registry; після live/closed drain counters balanced.
CPU: 21 command rejections, independent/shared-instance streams, stale cell та
same-cell generation, last-cell rollback, 128 commands/128 queries/8 MiB storage.
GPU: two-cell queued atomic exchange, copied caller data, ordered sparse edits,
8 exact failed + 8 no-op TAA phases, accepted reset, 8 query/resource/capacity
cycles, remove та pending world shutdown. Два real runs додають по 4320 RGB
checks через actual facade queries і new-slot vertex edits; restored image/samples
exact. 44 captures/run byte-identical, попередні 38 та 49 original/pack hashes
незмінні. Captures переглянуто. Це bounded renderer/facade profile, не звичайний
threaded editor startup або mixed layouts усередині role array.

### Регресія Async Binding (2026-10-15)

2026-10-15: GCC 16.2 / MinGW-w64 Ninja Debug, RTX 4070; повний build успішний,
**94/94 CTest (51 GPU)** за 308.41 s, **14/14 Python tests**.
Focused 15 tests x 3: 45 executions за 112.74 s; CPU rejections:
18 array / 57 terrain resource+adapter / 28 binding core / 21 binding commands /
468 terrain import.
33 validation summaries: 0 errors/0 warnings через teardown.
Logs/XML/captures: `build/stage8-terrain-bindings`. Це async binding facade/world
acceptance, не benchmark або закриття всього M8. Початкові fixture failures
збережено: backslash virtual_path_string assert перед renderer API; query-pump
під час cook завершував «pending» control; помилкове округлення 5*65536*22 до
7 MiB; real buffer baseline порівнював unlit із lit constants (same mesh,
buffers 12 -> 14). Raw validator test, pre-cooked cancellation target, exact
sample/fragment/vector-entry bytes та повернення в unlit виправляють controls.
Tolerance, stress counts і timeouts не послаблено.
Integrated M7 GPU test цього разу пройшов за 29.64 s; попередні 118.55 s при
timeout 120 s лишаються timing risk, root cause не оголошено виправленим.
Python tests використовують workspace TEMP/TMP. Попередні coherent reload, bounds, AA retirement-
tail, shadow tonemap/deferred ABI/normal-oracle та sandbox-temp failures лишаються
у датованих звітах, не новими збоями фінального прогону.

Test pacing evidence: `build/stage8-test-stability/summary.json`;
фіксовані покадрові Sleep прибрано з test harness без зміни pixel tolerances,
stress iterations або CTest timeouts. Попередні failed logs не переписано.
Одноразові історичні TAA mismatch і Stage2 SegFault після logged NVIDIA ICD
failure не оголошуються виправленими через успішні наступні прогони.
Stage2 пройшов 10 повторів; missing-ICD GDB control завершився error exit 1,
але не відтворив той vendor failure. Startup stability/failure injection
залишаються ризиками Етапу 9; докази збережено у звітах stability/wrapper/GPU.

### Terrain Target Profile Decision (2026-10-16)

Evidence: `build/stage8-terrain-profile/inventory-profile-decision.json`.
Два source-TGA scans усіх 814 cells byte-identical: 178 supported,
620 mixed arrays, 16 missing dependencies; 17 names, 48 decoded role images,
без invalid imports/options/images. У 603 complete cells mixed усі три roles,
у 17 лише specular. 913 existing source files SHA-256 unchanged, три missing
`junk_industrial` TGA залишилися відсутніми. Це capability inventory через
production importer/options/decoder/shared validator, не GPU приймання всіх
cells або перевірка converted DDS precedence.

На цьому історичному checkpoint було визначено потребу в normalization;
реалізація та приймання зафіксовані в наступному звіті, це не активний підетап.
Early rejection сам по собі не був реалізацією mixed terrain.
Largest-per-role RGBA/full-mip estimate: 729 complete cells вкладаються
у 256 MiB, 69 перевищують ліміт, maximum 671088600 bytes; це не memory benchmark
і не вже реалізований normalizer. Initial instance query тепер відхиляє mixed
arrays до submission; reload/binding/draw використовують той самий contract.
Layout errors містять role/slot та expected/actual dimensions/format/mips;
legal mip count, sampled format і exact mip bytes також перевіряються.
CPU rollback fixture тепер починається з valid instance і змінює private
dependency через incompatible reload, а invalid initial query перевіряється
окремо. Bounds, pixel tolerances, stress iterations і timeouts не послаблено.

### Регресія Target Profile Decision (2026-10-16)

2026-10-16: GCC 16.2 / MinGW-w64 Ninja Debug, RTX 4070; повний build успішний,
final Ninja no work. **95/95 CTest (51 GPU)** за 326.18 s,
**14/14 Python tests**; focused 16 tests x 3 = 48 executions за 108.97 s.
CPU: 31 array / 61 resource+adapter / 28 binding core / 22 binding commands /
468 import rejections. 33 validation summaries: **0 errors/0 warnings** через
teardown. Logs/XML та підсумок: `build/stage8-terrain-profile`.
Два real GPU runs: по 11232 independent material RGB checks і 44 captures;
усі кадри byte-identical між runs та з попереднім async binding прийманням.
913 original inventory files і 49 original/pack provenance entries SHA-256
незмінні після regression. Missing assets не підмінялися.
Це regression/target-profile decision, не завершення mixed normalization або M8.
Initial namespace/fixture-options/early-query failures збережено в цьому каталозі;
старі M9 startup/TAA/near-timeout risks не оголошено виправленими.

### Bounded Mixed RGBA Normalization (2026-10-16)

Evidence: `build/stage8-terrain-normalization/summary.json`.
Нормалізатор підключений до initial instance, coherent reload та binding
preflight; raw source tuples відокремлені від derived GPU images. Initial capture
виконується coherent під registry lock, pending/failed input відхиляється.
Batch commit повторно перевіряє raw dependencies і публікує всі cells після
allocations, не порівнює raw snapshots із promoted images. Mesh identity
не змінюється, no-op та незмінні promoted sources зберігають identities.

Профіль: однаковий RGBA8 format у межах role, square power-of-two/full-mip
sources <=4096; target dimension найбільший у role. Усі authored mips
зберігаються byte-exact, додаються лише finer levels із source base:
periodic bilinear linear-light RGB/linear alpha та AG normal vector filtering/
renormalization. Budget усіх GPU array layers/roles <=256 MiB перевіряється
до promoted pixel allocation; aliases CPU-dedup. Немає автоматичного
downsampling, BC transcoding, missing-mip synthesis або підвищення budget.
Source/previous/queued snapshots та metadata overhead додаткові, не RSS claim.

CPU перевіряє незалежні numeric color/alpha/normal/repeat/multiple-level
oracles, exact authored mips, alias/reuse/no-op identities, actual mixed RGBA
cooks, initial pending rejection, stale raw dependency batch rollback/commit
та reload failure/recovery. Synthetic GPU: queued async exchange/new-slot edit,
normal/specular oracles, 8 resource/capacity cycles, 8 exact no-op і 8 budget-
rejection 4x+TAA phases, accepted reset і exact drain. Unsupported partial-mip
negative controls збережено; supported full-mip resize cases стали positive.

Два read-only original inventories byte-identical: 178 native + 551 normalizable
=729 eligible, 69 budget rejected, 16 missing. Це metadata planner, не виконання
pixel promotion/GPU приймання всіх cells. Два реальні mixed cells (усі roles
та specular-only) пройшли actual query/promotion, authored-mip comparisons,
чотири material-mode oracles (3456 RGB checks/run), no-op та real budget-query
rejection. По 8 captures у двох final runs byte-identical; попередні 44
homogeneous captures також незмінні. 913 original files і 63 new-pack / 49
previous-pack provenance entries SHA-256 unchanged; missing assets не підмінено.
Приймання переглянутих bounded controls завершено; next: wrapper/collision.

### Остання Зафіксована Регресія

2026-10-16: GCC 16.2 / MinGW-w64 Ninja Debug, RTX 4070; повний build успішний,
final Ninja no work. **96/96 CTest (51 GPU)** за 376.06 s,
**14/14 Python tests**. Broad focused 17 x 3 (51 executions, 135.69 s) пройшов
до final coherent-initial-capture refinement; після нього пройшли 6 CPU tests x 3
(18 executions, 5.74 s), повний 96-test suite та обидва final real mixed runs і
попередній homogeneous GPU profile. 33 validation summaries: **0 errors/0
warnings** через teardown. GPU windows hidden/nonactivating/input-transparent.
CPU: 10 normalization / 31 arrays / 60 resource+adapter / 28 binding core /
22 binding commands / 468 importer rejections. Logs/XML/captures та автоматичні
hash/status/control checks: `build/stage8-terrain-normalization`.
Initial include/auto-deduction compilation failures збережено. Tolerances,
stress iterations, timeouts і budget не послаблено. M8 відкритий; historical
TAA/vendor-ICD/near-timeout risks не оголошено виправленими; це не benchmarks.

### Terrain Collision Foundation (2026-10-16)

У межах відкритого terrain-wrapper пункту додано `xray_terrain_collision`:
owned immutable copy тієї самої local triangulation, що й renderer, реальні
Opcode geometry/instance, local bounds/center та non-owning game-object flag.
Окремий `xray_physics_shapes` не залежить від renderer/world і зберігає наявні
factory contracts; нова owned factory копіює buffers, центрує, відбиває Z,
зберігає winding та володіє Bullet interface/BVH/internal-edge metadata.
BVH видаляється до backing store. Intrusive resource_ptr потребуватиме
справжнього cook/deleter, а не ручного resource без metadata.

Виправлено collision target contracts для animation/debug drawing та вибір
ray implementation: duplicate false-return multi variants більше не потрапляють
до archive; bounds queries використовують наявний `math::aabb::intersect`.
Дві ray/AABB functions мають по одному definition. Renderer DLL не підтягує
collision archive: experiments із duplicate renderer/engine/game exports
збережено в build logs; permissive linker flags не застосовано.

Contact acceptance обмежено translation-only sphere із заблокованим обертанням
в обох A/B phases, тим самим seam impact та 600 physics steps/phase.
Bullet callback/flag тут лише в test world і відновлюється після тесту;
factory не змінює process-global contact callback. Free-rotation sphere drift
залишається неприйнятим, його failure logs збережено. Це не універсальна
physics quality або game startup. `terrain_model_class` усе ще unsupported;
DLL ownership, shape-resource cook/deleter, engine hook, coherent edits і
wrapper query/load/unload/lifetime залишаються в єдиному відкритому пункті.

Приймання foundation: **97/97 CTest (51 GPU)**, 294.86 s;
**10 focused tests x3 = 30/30 executions**, 9.51 s; **14/14 Python**.
Після фінального exception-safe backing-store refinement повторено всі
**814 original cells**: 99 308 ray calls; разом із п'ятьма synthetic profiles
**99 918**, окремо 31 oblique/bounds controls, 32 lifetime cycles і 9 malformed
rejections. Це local collision geometry/physics ray proof без texture queries,
не 814 game-wrapper або GPU acceptances. Contact A/B має 239 callback calls,
3 corrected normals; negative control дає 0.390354 lateral drift, positive
утримує settled X/Y/Z у незміненому .02 tolerance. Обертання заблоковано в
обох phases; free-rotation failure не приховано і не оголошено виправленим.

Після фінальних source edits повторено real GPU profiles: **8 mixed + 44
homogeneous captures byte-identical** до попереднього приймання. Усі **35
validation summaries = 0 errors / 0 warnings**, включно з teardown.
**913 originals / 3 absent paths / 63+49 pack inputs** незмінні за SHA256;
фінальний Ninja без роботи. Build/early runtime failures і пропущений DXC env
в одному focused command збережено; фінальні commands мають повне runtime/
DXC/validation environment. Timeouts, stress counts, GPU focus behavior і
budget не послаблено. Evidence та автоматичні hashes/status/label перевірки:
`build/stage8-terrain-collision-foundation/summary.json`.
M8 і M9 залишаються відкритими; прийняту foundation не виконувати повторно
як новий підетап замість integration.

### Terrain game-wrapper resource integration: 2026-10-16

Engine-owned cook/deleter class 192 і `terrain_model_class` query/retain
інтегровані без collision/physics archives у renderer DLL. Залежність має
реальний unmanaged-resource metadata, Opcode snapshot, owned centered/Z-reflected
Bullet BVH та retained render instance. Register/finalize перевіряють owner,
allocator/thread, pending queries та resource users; збережені ресурси не
втрачають deleter при закритті renderer session.

Прийнято immutable translation-only wrapper: authored transform/local center
для game cell, collision object і body; non-noop buffer/placement edits дають
atomic rejection. Actual rigid body звільняє native body/motion state до shape;
game-cell unload звільняє wrapper останнім. Окремо перевірено retention лише
body після закриття session, failed closed-source query та pending wrapper.
Hidden GPU proof має empty/removed controls, 48 independent RGB checks,
byte-identical plain/wrapper/no-op frames і queued removal/drain.

**99/99 CTest (52 GPU)** пройшли за 545.65 s; **11 focused CPU tests x3 =
33 executions**, 11.67 s; **2 wrapper tests x3 = 6 executions**, 3.33 s;
**14/14 Python**. Два real mixed cells пройшли wrapper query/Opcode/Bullet/
rigid-body lifetime; real budget negative залишився rejection при 256 MiB.
Повторено foundation на **814 original cells**, **99 918 ray calls** разом із
п'ятьма synthetic profiles; це не 814 wrapper/GPU acceptances. **8 mixed +
44 homogeneous real GPU captures byte-identical** до baseline. Усі **39
validation summaries: 0 errors / 0 warnings** через teardown; **913 originals,
3 absent paths, 63+49 pack inputs** незмінні за SHA256. Final Ninja без роботи.
Evidence: `build/stage8-terrain-wrapper/summary.json` та автоматичний verifier.

Збережено початкові namespace/declaration/link-order build failures і CPU test,
який помилково вважав synchronous creation query pending. Остаточний тест
відокремлює closed-source rejection від pending wrapper; умови не послаблено.
Цей звіт уточнює попередній foundation status, не створює новий numbered stage.
Production contact hook, погоджений moving-body seam profile та ordinary
threaded game/editor startup ще не прийнято. Free-rotation drift не оголошено
виправленим; довільні transform/live collision edits не приховано ввімкнені.
Наступним лишається contact acceptance з єдиного відкритого checklist.

### Terrain production contact acceptance: 2026-10-16

Engine collision cook тепер встановлює/restores production Bullet contact hook,
зберігає callback chain/material/return і коригує лише зареєстровані class 192
meshes із custom-material flag та valid triangle ID. Actual game rigid body
встановлює flag автоматично. Обидва manifold orders мають independent normal/
point-reprojection controls; registry очищається до BVH deletion. Install/finalize
потребують quiescent physics callbacks. Displaced owner блокує registration/new
queries/finalize; чужий callback не перезаписується.

Bounded profile: sphere radius .5/mass 1, rotation locked, flat 5x5 grid size 9,
local height -3, translation (11,4,-7), 600 steps/drop при 1/120 s. Сім positive
drops: 239 mesh contacts/3 corrected normals кожен, settled XYZ у .02. Один
disabled-flag negative при незмінному production hook: 0 callbacks і lateral
drift 0.390354. Дві 300-step awake/frictionless translations: displacement
1.24998 проти oracle 1.25, 363 production contacts кожна, збережені height/Z.
Окремо game ground friction=100, body-only retention після renderer shutdown,
чотири re-registration cycles та exact prior/null callback restoration.
Шість unsupported dispatch controls не змінюють geometry contact; previous
observer лише перевіряє chain/material, не виконує normal correction.

**100/100 CTest (52 GPU)**, 331.84 s; **12 focused CPU tests x3 = 36
executions**, 13.52 s; **3 wrapper/contact/GPU tests x3 = 9 executions**,
5.22 s; **14/14 Python**. Повторно два real wrapper positives і budget negative,
**814 original collision cells / 99 918 rays** із synthetic controls; це не
contacts на всіх original cells. **8 mixed + 44 homogeneous captures**
byte-identical; **39 validation summaries = 0 errors / 0 warnings** через
teardown. **913 originals / 3 absent paths / 63+49 pack inputs** незмінні за
SHA256. Final Ninja без роботи. Evidence/verifier:
`build/stage8-terrain-contacts/summary.json`.

Збережено initial translation-fixture failure: fixed-velocity oracle не
враховував cached friction/low-speed sleeping. Фінальна prescribed translation
явно refreshes contacts і забороняє deactivation; counts/tolerances не
послаблено. Foreign-mesh fixture widening u16 -> u32 зроблено копіюванням,
build failure збережено. Немає змін GPU focus/window behavior або asset budget.

Terrain wrapper/contact завершено лише у цьому bounded профілі. Free-rotation
drift/інші body shapes, live collision edits, arbitrary transforms та ordinary
threaded startup не оголошено прийнятими. M8 відкритий; наступний пункт
єдиного checklist: particles. Новий numbered substage не створюється.

### Particle billboard component foundation: 2026-10-16

Реалізовано copied immutable snapshots над наявними `base_particle` span/list:
camera-facing square/rectangle, rigid local/world placement, radian rotation,
linear tint, stable per-batch depth order, dead/zero filtering та bounded cycle
rejection. RHI unlit RGBA8 2D LOD0 renderer має alpha/additive, opaque depth test
без depth writes, immutable texture generations, explicit resource retirement.
Camera-dependent snapshots потрібно paired із captured view; cross-batch sort
не заявляється. Simulation не переписувалась і ще не інтегрована з renderer.

Межі: 4096 source particles/batch; 64 batches, 8 MiB copied geometry і 64 MiB
unique textures/frame. Retained cache до двох bounded generations: 128 geometry
та 128 image entries, 16 MiB geometry/128 MiB textures до collect. Preflight
rejects до GPU allocations; aliases deduplicated. 16 particle facade/preview/
mode/slomo no-ops стали explicit errors; scene flag/world лишаються gates.

CPU: independent geometry/tint/camera/rotation/topology, stable ties, list/span
equivalence, source independence, limits/aliases та actual engine-world gates;
27 negative controls. Hidden GPU: 507 independent RGB checks, 7 captures,
alpha/additive/texture alpha, linear/sRGB, opaque depth і окремий no-depth-write
control, rotated-camera UV/filter oracle, repeat identity, translated camera,
8 resize/texture-generation cycles, malformed/cache preflight і live/retired
drain. Rotated UV capture візуально перевірено.

**102/102 CTest (53 GPU)**, 496.41 s; **13 focused CPU tests x3 = 39
executions**, 13.51 s; **2 particle tests x3 = 6 executions**, 6.53 s;
**14/14 Python**. **40 validation summaries = 0 errors / 0 warnings** через
teardown. Existing regression: 2 real wrapper positives, budget negative,
**814 collision cells / 99 918 rays**, **8 mixed + 44 homogeneous captures**
byte-identical. Це не real particle assets acceptance. **913 originals / 3
absent paths / 63+49 pack inputs** незмінні за SHA256. Final Ninja без роботи.
Evidence/verifier: `build/stage8-particle-billboards/summary.json`.

Initial fixture API-owner/cycle injection/Windows macro failures збережено;
остаточні source updates перевірені повними/повторними та real regression
прогонами. Умови тестів, budgets і background-window behavior не послаблено.
Particles і M8 відкриті: наступний залишок того самого checklist пункту є
scene-owned world/resources/cooks над наявною simulation, потім copied queued
lifecycle, scene submission/drain і real supported billboard proof. Виявлені
excluded cook registration та raw-world-pointer lifetime потребують адаптації,
не прихованого ввімкнення flag. Snapshot/RHI foundation не повторювати як
новий numbered substage; unsupported particle/effect profiles перелічено вище.

### Existing particle simulation lifetime: 2026-10-16

Виправлено material callback після знищення emitter та спільний mutable
cook-data pointer для overlapping queries: кожен query утримує власні дані,
callback перевіряє live target/latest generation. Failed latest query зберігає
попередній material, stale success його не підміняє. Constructor rejection
звільняє renderer adapter. Repeated world play не додає intrusive node вдруге;
stop/play відновлює creation без обіцянки rewind/reset simulation time.

Новий CPU test використовує справжні world/emitter/instance і resource manager,
але controlled delayed fixture cooks, не production template/world cooks.
Перевірено 32 destruction/late-callback cycles, stale/failed/latest responses,
100 repeated plays, tick/pause/visibility isolation, stop/play continuation,
active self-retention і remove/replay/drain. Concurrent simulation mutation
та raw-world-pointer ownership цим не прийнято.

**103/103 CTest (53 GPU)**, 356.32 s; **50 CPU tests x3 = 150 executions**,
39.82 s; **3 particle tests x3 = 9 executions**, 3.00 s; **14/14 Python**.
**38 validation summaries = 0 errors / 0 warnings** через teardown; сім
billboard captures byte-identical до попереднього приймання. **913 originals
і 3 absent paths** незмінні за SHA256. Final Ninja без роботи. Умови тестів,
budgets, таймаути і background-window behavior не послаблено.
Evidence/verifier: `build/stage8-particle-lifetime/summary.json`.

Це history приймання lifetime repairs, не новий numbered substage. Particles
і M8 відкриті: production scene-owned world/resources/cooks, queued lifecycle,
scene submission/drain і real supported billboard proof ще потрібно виконати.
Scene flag/world/facade gates не ввімкнено. Уже перевірені callbacks/replay та
snapshot/RHI foundation не відкривати повторно як окремі активні завдання.

### Production particle world/instance ownership: 2026-10-16

Штатні world/instance cooks тепер мають retained typed userdata замість raw
world pointer, owned adapter і aligned arena 1..50000 particles. Async child
query та instance утримують world до emitter teardown; metadata встановлюється
до resource_ptr. Explicit owner-thread close розриває active-instance cycle;
closed/foreign/null operations відхиляються, remove після close дозволений.
Restartable registration перевіряє class-ID ownership; nonblocking finalize
залишає cooks до pending/resource/deleter drain. Unsafe template/preview cooks
не реєструються; direct world preview methods тепер explicit errors.

Це production world/instance cooks із controlled template/material fixture
dependencies: trusted topology один LOD, 1..64 emitters, без child/event emitters.
Перевірено arena 1/4/50000 та exhaustion/alignment, pending close, partial/null
adapter construction, uninitialized arena rejection, seeded real particle
teardown, foreign-thread/world controls, registry displacement/recovery і
чотири re-registration cycles. Це не production template/config/raw-binary
parser, material loading, birth simulation або real particle assets acceptance.

**104/104 CTest (53 GPU)**, 350.52 s; **51 CPU tests x3 = 153 executions**,
37.25 s; **4 particle tests x3 = 12 executions**, 3.24 s; **14/14 Python**.
**54 negative controls**, **38 validation summaries = 0 errors / 0 warnings**
через teardown. Сім billboard captures byte-identical; **913 originals / 3
absent paths / 63+49 pack inputs** незмінні за SHA256. Final Ninja без роботи.
Збережено попередній green full run та початкові build/fixture failures;
остаточні прогони виконано після фінальних guards і backing-buffer correction.
Умови тестів, budgets, таймаути і background-window behavior не послаблено.
Evidence/verifier: `build/stage8-particle-resources/summary.json`.

Це history приймання ownership profile, не новий numbered substage. Particles
і M8 відкриті: bounded production template/material loading, scene-owned
close-before-drop і typed game/facade userdata, queued lifecycle/submission/drain
та real supported billboard proof. Прийняті world/instance ownership, callback/
replay repairs і snapshot/RHI foundation не повторювати як активні завдання.

### Production particle material effects: 2026-10-16

Активний scene cook group реєструє `material_effects_instance_class` для
caller-owned billboard cook data. Typed effects resource утримує checked
`f_stage_unlit` material/texture dependencies та immutable tint/blend/texture
generation. RGBA8 linear/sRGB TGA і authored constant white sample підтримані;
missing texture не підміняється білим. Профіль: двосторонній unit-UV unlit
alpha/additive із fixed linear clamp LOD0; unsupported vertex/sampler/cull/UV,
BC/opaque/raw-payload та ownership-transfer requests дають errors.

Actual emitter callbacks перевірено без material cook mocks: failed-query
retention, вісім delete-before-callback cycles, missing-cook cleanup. Reload
зберігає старий material snapshot, explicit recook захоплює нову texture
generation. Registration preflight/displacement та closed/pending/deleter drain
перевірені. Hidden GPU малює copied cooked snapshots після cook graph drain:
108 незалежних RGB checks для textured sRGB alpha/tint, additive, recooked color
і empty control; чотири distinct captures. Additive capture оглянуто візуально.

**106/106 CTest (54 GPU)**, 353.24 s; **52 CPU tests x3 = 156 executions**,
38.61 s; **6 particle tests x3 = 18 executions**, 6.75 s; **14/14 Python**.
**25 negative controls**, **42 validation summaries = 0 errors / 0 warnings**
через teardown. Сім попередніх billboard captures byte-identical; **913 originals
/ 3 absent paths / 63+49 pack inputs** незмінні за SHA256. Final Ninja без роботи.
Початкові fixture build failures збережено; остаточні прогони після corrections
та всіх guards. Умови тестів, budgets і background-window behavior не послаблено.
Evidence/verifier: `build/stage8-particle-materials/summary.json`.

Це history приймання material effects profile, не новий numbered substage.
Automatic material refresh після texture reload, original particle effects/
assets/parity та production birth simulation не прийнято. Particles і M8
відкриті: bounded template loader має використати готовий material cook як
ready dependency перед instance publication; далі scene ownership, typed game/
facade userdata, queued lifecycle/submission/drain і real supported billboards.
Scene flag/facade/preview gates не ввімкнено; прийнятий material cook не
відкривати повторно як активне завдання.

### Material-ready particle instances: history приймання 2026-10-17

Production instance cook тепер очікує всі material effects dependencies і лише
після їхньої готовності створює emitters із retained ready handles, без повторного
async load. Aggregate callback утримує world/template/caller-owned cook data;
partial failure, unsupported billboard profile/cyclic actions, adapter material
rejection та closed world, включно з close у constructor callback, не публікують
instance. Весь partial graph має rollback/drain. Production world/instance/
material/effects/texture chain перевірено з controlled parsed template; готові
TGA/sRGB alpha та constant additive snapshots малюються після cook graph drain.

**106/106 CTest, 54 GPU**, 270.64 s; **52 CPU x3 = 156 executions**, 35.29 s;
**6 particle tests x3 = 18 executions**, 6.53 s; **14/14 Python**. World/instance
має **66 negative controls**, material chain **30**; **108 independent RGB
checks**, **42 validation summaries = 0 errors / 0 warnings**. Сім billboard і
чотири material captures byte-identical; **913 originals / 3 absent paths /
63+49 pack inputs** незмінні за SHA256. Final Ninja без роботи. Початкові build
corrections передують остаточним прогонам і final publication guard; умови,
budgets, timeouts і hidden/nonactivating/input-transparent windows не послаблено.
Evidence/verifier: `build/stage8-particle-ready/summary.json`.

Це history завершеної умови instance readiness, не новий numbered substage.
Production template parser і birth simulation цим не прийнято: наступним
лишається bounded template loading, далі scene ownership, typed game/facade
userdata, queued lifecycle/submission/drain і real supported billboard proof.
Particles та M8 відкриті; scene/facade/preview capability gates залишаються errors.
Прийняті readiness/material/ownership компоненти не відкривати повторно.

### Bounded particle templates: history приймання 2026-10-17

Production `particle_system_class` тепер завантажує лише version-1 checked
config з converted tree. Один LOD, 1..64 emitters, explicit constant-profile
schema і числові/path/capacity bounds; source Lua/raw object cache, невідомі поля
та unsupported profiles відхиляються. Resource володіє emitters/actions/curves,
використовує наявну simulation та готовий material/texture cook graph.
Штатний initialize координує всі три particle cooks із foreign-owner preflight
і retained/pending/thread-safe lifecycle rejection; preview не реєструється.
Це owner-thread lifecycle, не concurrent simulation mutation чи ordinary startup.
Спільний checked parser винесено в core, ключі preflight не копіюються;
request path guard спрацьовує до VFS assertion. Lifetime на immortal threshold
відхиляється, nextafter threshold проходить actual expiry; initial rate береться
з curve, first-tick та 64 zero-rate emitters перевірено без warm-up.

Два повні **108/108 CTest, 55 GPU**, пройшли після parser extraction/key views:
427.12 і 574.13 s. Після фінальних particle-only lifetime/initial-rate fixes
перелінковано лише чотири particle test executables, інші binaries/core/renderer
незмінні. Остаточні **53 CPU x3 = 159 executions**, 45.63 s, та **8 particle
tests x3 = 24 executions**, 15.21 s, покривають усі affected executables/GPU.
**14/14 Python**, **59 template negative controls**, **2904 independent RGB
checks**, **46 validation summaries = 0 errors / 0 warnings** через teardown.
Сім billboard і чотири material captures незмінні; чотири simulation captures
byte-identical між full/repeat. **913 originals / 3 absent paths / 63+49 pack
inputs** незмінні за SHA256. Final Ninja без роботи; initial failures/backtraces
збережені. Умови, timeouts, budgets і background-window behavior не послаблено.
Integrated GPU мав slow pass **117.967/120 s**; root cause/timing margin не
оголошено виправленими, performance/stability належать наявному M9 scope.
Evidence/verifier: `build/stage8-particle-templates/summary.json`.

Це history accepted bounded constant-profile loader/simulation, не numbered
substage і не original particle assets/parity. Далі scene-owned world із
close-before-drop, typed game/facade userdata, queued lifecycle/submission/drain
та real supported billboard proof. Original graphs/projection, довільні curves,
bursts/events/domains, finite loops та ordinary threaded startup цим не прийнято.
Particles і M8 відкриті; прийняті loader/readiness/material/ownership компоненти
не відкривати повторно як активні завдання.

## Етап 9. Стабілізація та приймання перенесення

Передумови: етапи 0-8 у погодженому функціональному обсязі.

- [ ] Провести чисті GCC / MinGW-w64 Debug і Release збірки в окремих build directories; перевірити запуск з задокументованими assets і runtime dependencies. MSVC не вимагається.
- [ ] Автоматизувати focused CPU-тести handles/cache/reflection та GPU smoke-сцену для clear, geometry, textures, MRT і resize. GPU-тести запускати лише на runner з потрібною підтримкою.
- [ ] Перевірити reload scene/shaders/textures, зміну resolution, minimize/restore, закриття output і shutdown під час незавершених кадрів; на додаткових конфігураціях повторити background timing та exact MSAA+TAA reload comparison. Поточний GCC Debug / RTX 4070 test pacing виправлено 2026-10-13, два повні прогони зелені; історичний одноразовий TAA mismatch зберігається як regression oracle, не як активний збій останніх прогонів.
- [ ] Перевірити GPU capability/allocation failures і device loss через контрольовану failure injection: потрібні діагностика та завершення без deadlock; автоматичне відновлення device оцінювати окремо. Дослідити одноразовий Stage2 SegFault після NVIDIA ICD initialization failure: десять наступних успішних прогонів і missing-ICD error exit не доводять виправлення root cause; evidence у `build/stage8-terrain-gpu/summary.json`. Перевірки на додаткових GPU виконувати для погодженої доступної матриці, не трактувати всі GPU як неявну умову приймання.
- [ ] Перевірити CPU/GPU memory growth, descriptor usage, allocations per frame, pipeline creation stalls і час CPU/GPU кадру.
- [ ] Оптимізувати за результатами профілювання; уникати `vkDeviceWaitIdle` у звичайному кадрі. Persistent pipeline cache та GPU memory suballocation додавати з вимірюваним ефектом.
- [ ] Видалити застарілі render-заглушки та стару Vulkan bootstrap-фабрику, якщо вони більше не використовуються. Нерендерні sound-заглушки з цього файлу розглянути окремою задачею.
- [ ] Зафіксувати список підтриманих і відкладених features, контрольні сцени, драйвер/GPU, validation logs та результати порівняння з baseline.

Критерій завершення: цільові сцени та workflows проходять на узгоджених GPU/configurations; у перевіреному наборі немає невирішених validation errors, зависань, resource leaks або критичних візуальних регресій. Відкладені features явно перелічені, а не приховані заглушками.

## Порядок виконання та контроль прогресу

Основна залежність: `0 -> 1 -> 2 -> 3 -> 4 -> 5 -> 6 -> 7 -> 8 -> 9`.

Паралельна робота можлива над build/DXC tooling після етапу 0 і аудитом scene/model cooks до етапу 4. UI/debug drawing можна переносити після етапу 5, поки розробляються GBuffer і lighting. Інтегрувати ці частини слід після стабілізації їхніх RHI contracts.

Контрольні результати:

1. M1: реальний Vulkan clear/present через актуальний facade, resize і teardown.
2. M2: indexed geometry з shader modules та GPU buffers.
3. M3: textures, samplers, constants та descriptors.
4. M4: штатні scene/view/output queries і команди facade.
5. M5: реальна static model з assets і матеріалом.
6. M6: MRT GBuffer; прийнято 2026-10-07 для static opaque/cutout profile, Debug MinGW/Ninja / RTX 4070.
7. M7: освітлення, тіні та фінальний кадр.
8. M8: звичайна ігрова сцена з потрібними типами об'єктів, UI і editor main/preview workflows; єдиний checklist, межа завершення та невизначений effects scope наведені в Етапі 8.
9. M9: чисті збірки, перевірений runtime і прийнятий функціональний обсяг.

Кожен milestone закривати лише після збірки, runtime-перевірки та збереження доказів: log, screenshot, validation result і короткий список залишкових обмежень. Наявність declarations або викликів `vkCmdDraw` не є критерієм завершення.

Перший пакет змін: етап 0 та етап 1. Він має дати перший перевірений кадр і стабільний lifecycle. Далі реалізовувати ресурси та геометрію, зберігаючи цей smoke-режим як регресійну перевірку.

Терміни повного перенесення варто оцінювати повторно після M3 і M5: саме тоді стане відомий реальний обсяг несумісностей HLSL, resource cooks та матеріалів. Старий прогноз на 30-45 днів не вважати підтвердженим.
