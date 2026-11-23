# B7 — перевірка Blender pipeline та CI

Єдиний запуск: [run_ci.py](run_ci.py). Він використовує встановлений Blender
5.3.0, не завантажує portable runtime і не повторює невдалі тести автоматично.
Потрібні Python 3.11+, CMake 4.3+, Ninja та C++26 toolchain; поточний Windows
toolchain — MSYS2 MinGW x64 у `C:/msys64/mingw64`. Full engine configuration
також потребує встановлених залежностей SDK, включно з .NET із `global.json`.
Тести генерують власні fixtures й не потребують оригінального corpus.

Поточний документаційний пакет — 0.3.2; [B8 acceptance](b8-acceptance.json)
містить його фінальний Release CI 22/22 і native SDK reimport. Історичні
результати B7 нижче зберігають provenance пакета 0.3.1. Відтворені під час B8
Windows installer rename failures і порядок діагностики описані в
[maintainer runbook](MAINTAINER.md).

## Локальні та CI-команди

```powershell
# Математичне ядро без DCC; вказаний executable навмисно не існує.
python -X utf8 sources/tools/blender/run_ci.py --standalone --tier t1 --maya off --blender-exe no-dcc/blender.exe --build-root build/ci/T1 --artifacts build/ci-evidence/t1

# T1 + обов'язкові Blender і no-Maya; Maya suite потрібний у цьому прогоні.
python -X utf8 sources/tools/blender/run_ci.py --build-root build/sdk-native-20261108-release --configuration Release --maya required --artifacts build/ci-evidence/release

# Окрема конфігурація з вимкненими Maya host tests.
python -X utf8 sources/tools/blender/run_ci.py --build-root build/sdk-native-20261108-debug --configuration Debug --maya off --artifacts build/ci-evidence/debug
```

Для кожного прогону задавайте новий `--artifacts`. `--blender-exe` та
`--expected-version` змінюють host явно; відсутній або інший host робить T2
невдалим. `--toolchain-prefix` налаштовує compiler/dependencies для нової збірки.
`--cmake-arg=-DNAME=VALUE` дозволяє додати параметр CMake. Наявний build cache
зберігає власний generator і compiler. Два CI runners не можуть писати в один
build directory одночасно.

Режим Maya керується `--maya auto|required|off`, який передається в
`XRAY_DCC_MAYA_MODE=AUTO|REQUIRED|OFF`. AUTO додає всі три host tests за наявності
mayapy. REQUIRED завершується помилкою, коли Maya недоступна; OFF не реєструє
Maya host tests навіть зі старим cache. Звичайні Python/C++ тести без Maya
залишаються активними.

## Покриття

- **T1:** осі, Euler/фізичні обертання, `Rcorr`, fitter та precision, schema v2,
  legacy decompile/check-data, MA curves/handles/tangents, state/resume/rollback.
  Standalone job компілює ядро та виконує Python/C++ contracts без запуску DCC.
  Full build додає справжні engine loaders і playback contracts.
- **T2 Blender:** runtime identity, slotted Actions, встановлений ZIP і його
  lifecycle, усі п'ять exporters/importers, save/reopen, byte parity, fitted та
  exact keys, NLA/constraints, engine poses, migration sample, resume/repair.
- **No-Maya:** окремий `xray_blender_no_maya` виконує A + C1 import/migration
  з недоступним `mayapy` у PATH, без Autodesk Maya Python та Maya environment.
  Blender перевіряє це зсередини процесу. `.mb` має явну діагностику; `.ma`,
  legacy assets, fresh reopen та engine playback проходять без Maya.
- **Zero-root policy:** regression імпорту відхиляє нульовий масштаб кореневого
  суглоба з точною причиною, зберігає вихідні bytes, попередній Action і кількість
  scene datablocks. Прихованого repair або часткового імпорту немає.
- **Maya:** реальний mayapy export suite, Maya / Blender Y-up / Blender Z-up
  parity, reference/controller migration і незалежна перевірка skin vertices.

CI звіряє CTest inventory з обов'язковим переліком. Відсутній тест, порожній
JUnit, duplicate cases, failure/error або skipped case не зараховуються як PASS.
Ці умови мають власний T1 contract. Єдине опційне покриття — весь Maya host suite
в AUTO, із причиною відсутності у звіті. Blender T2 не пропускається.

Локальне приймання 2026-11-15: [B7 acceptance](b7-acceptance.json). T1 без DCC
7/7, Release CI 22/22 включно з чистою збіркою, Debug/OFF 19/19, повний Release
CTest 29/29. Один діагностичний no-Maya worker завершився з code 1 до створення
asset receipt; причина не відтворилася в наступному окремому прогоні та серії
до першої відмови 5/5. Початковий failure збережено. Причину цього збою не
вважаємо усунутою; майбутні worker logs зберігаються перед temporary cleanup.

`result.json` містить фактичний executable/version/SHA-256, конфігурацію,
заплановані та виконані тести, їх статуси й hashes збережених звітів. Поруч
лежать configure/build logs, CTest JUnit/stdout та receipts exporter/importer,
no-Maya і parity. Невдалий прогін лишається невдалим; logs не перезаписуються
іншим CI run. Private profiles зберігаються всередині artifacts і не змінюють
налаштування художника.

Якщо migration contract падає, JSON і worker logs із тимчасового каталогу
зберігаються в окремому `xray_migration_*-failure` перед cleanup. Діагностичні
прогони до першої відмови запускаються вручну й не змінюють статус початкового
невдалого CI run.

## GitHub Actions

[Workflow](../../../.github/workflows/blender-ci.yml) запускає T1, потім
Release/AUTO та Debug/OFF. Потрібен self-hosted Windows x64 runner з label
`xray-dcc`, поточним Actions Runner, встановленим Blender 5.3 і build dependencies.
Він не інсталює Blender/Maya і не переносить license settings. Artifact upload
зберігає JUnit та діагностику також після failure. Workflow використовує
`contents: read`; fork PR не виконується на цьому provisioned runner.

Цей checkout не має Git remote: віддалений GitHub run не запускався.
Локально виконується той самий runner і ті самі команди; YAML/expressions
перевірено actionlint. Це реалізація й локальне приймання CI, а не підтвердження
запуску на неналаштованому GitHub runner.

Джерела конфігурації: [runner labels](https://docs.github.com/en/actions/how-tos/manage-runners/self-hosted-runners/use-in-a-workflow),
[checkout releases](https://github.com/actions/checkout/releases),
[upload-artifact releases](https://github.com/actions/upload-artifact/releases),
[actionlint](https://github.com/rhysd/actionlint/releases/tag/v1.7.12).

Повний corpus із 23 погодженими zero-root failures не ремонтується CI. Окрема
перевірка реальних проблемних сцен лишається у
`build/blender-5.3-validation/result.json`; B7 fixtures не підміняють ці джерела.
