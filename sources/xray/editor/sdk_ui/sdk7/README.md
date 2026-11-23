# SDK-7 — Редагування світу (Terrain та інші world tools)

Цей документ фіксує прийняття етапу **SDK-7** для всіх 7 родин функцій world tools у `sources/xray/editor/sdk_ui/sdk0/function-matrix.json` (`solid`, `lights`, `composites`, `terrain`, `particle-placement`, `speedtree`, `misc`).

## Прийнятий обсяг SDK-7

- **Родини (`implementationStage == "SDK-7"`):**
  - `solid` — розміщення статичних і скелетних об'єктів та синхронізація у runtime-сцені (`solidVisuals=3`, `skinnedVisuals=1`).
  - `lights` — джерела освітлення у проєкті/сцені, підбір локатора у viewport (`lights=7`).
  - `composites` — композитні об'єкти проєкту/сцени.
  - `terrain` — терен (`terrainCells=1`, `terrainCollisionTriangles=8192`), скульптінг (`sculptedHeight=3.5`), фарбування шарів, вибір незатіненого трикутника терену.
  - `particle-placement` — партиклові емітери у сцені (`particles=2`).
  - `speedtree` — екземпляри SpeedTree (`speedtrees=1`) та перелік бібліотеки дерев (`treeLibraryEnumerated=true`).
  - `misc` — декалі (`decals=1`), небо/sky (`skyActive=1`), службові локатори/зони (`miscLocators=11`) та Unicode/VFS збереження (`SDK7/світ_інструменти`).

## Ключові виправлення під час верифікації SDK-7

- `sources/xray/editor/base/EditorWorld.WorldToolsChecks.cs` (рядки 160–165): розділено перекриті точки pick-перевірки кроку 6 — вибір light locator у `(32f, 30f, -32f)` (`lightHit.Point.Y ~= 6f`) та незатіненого трикутника скульптованого терену (`id=0x7ffffffeu`) у `(32f, 30f, -35f)`.
- `sources/xray/editor/base/ProjectViewportEditing.cs` (рядок 55) і `sources/xray/editor/base/SdkIdePanels.cs` (рядки 419–426): у `SessionChanged()` замінено зайву інверсію `Matrix4x4.Invert` + `set_view_matrix(inverse)` на `ide.ViewWindow.SetCameraWorld(camera)`, а в `SceneViewPanel.set_view_matrix` додано допуск `< 1e-5f` для 4-го стовпця при float32-похибках кофакторів коли вхідна `viewMatrix` має `(0, 0, 0, 1)`, що усунуло `System.ArgumentException: Invalid camera transform` у кроці 7 (`CloseProject()`).
- `sources/xray/editor/base/SdkScene.cs` (рядок 424) і `sources/xray/editor/base/SdkRuntime.cs` (рядок 30): збільшено таймаут підготовки native scene/runtime із `AddSeconds(30)` до `AddSeconds(120)`, оскільки холодний Debug (`-O0`) reload 22+ об'єктів і 5 шарів терену (15 текстур через `texture_cook` `use_current_thread_id`) займав ~41.9 с за даними `stalker2_hturu.log`.

## Верифікація (Debug + Release)

- **Збірка CMake** (`build/sdk-native-20261108-debug`, `build/sdk-native-20261108-release`, цілі `stalker2 xray_editor_world xray_sdk_ui xray_sdk_world_tools_contract`): exit code 0, `0 Error(s)` (`build/sdk7-acceptance-20261115/build-debug.log`, `build/sdk7-acceptance-20261115/build-release.log`).
- **CTest SDK (`-V -R "^xray_sdk_"`)**: Debug `5/5`, Release `5/5` (`xray_sdk_gizmo_contract`, `xray_sdk_ui_compatibility`, `xray_sdk_texture_source`, `xray_sdk_model_physics`, `xray_sdk_world_tools`; `ctest-sdk-debug.log`, `ctest-sdk-release.log`).
- **Managed UI compatibility (.NET 11.0.0)**: `PASS: 24 UI compatibility checks on .NET 11.0.0` у Debug і Release.
- **Повний CTest**: Debug `31/31` (`90.90 sec`, `ctest-debug.log`), Release `31/31` (`74.08 sec`, `ctest-release.log`) на чистому повторному прогоні після транзитного Windows `[WinError 5]` rename lock у blender-тестах на першому прогоні (`30/31` Debug, `29/31` Release).
- **Vulkan GPU `world-tools` (Debug + Release)**:
  - Debug (`build/sdk7-acceptance-20261115/world-tools-debug`) і Release (`build/sdk7-acceptance-20261115/world-tools-release`): `PASS: world-tools; 5 stage transitions captured, validation errors=0 warnings=0`, по 9 `.bmp` кадрів, `world-tools-result.json` містить: `terrainCells=1`, `terrainCollisionTriangles=8192`, `solidVisuals=3`, `skinnedVisuals=1`, `lights=7`, `speedtrees=1`, `particles=2`, `decals=1`, `skyActive=1`, `miscLocators=11`, `sculptedHeight=3.5`, `treeLibraryEnumerated=true`.
- **Регресійні перевірки SDK-6**: `collect-sdk6-acceptance.py --verify` (`1473 verified hashes`), `check-sdk6-texture-checkpoint.py` (`194`), `check-sdk6-material-checkpoint.py` (`215`).

## Обмеження та поза межами доказу

- Профілювання продуктивності великих/повнорозмірних світів і тривалі навантажувальні GPU/desktop сесії не входять до автоматизованого контракту прийняття SDK-7.
- Інкрементальна валідація великих рівнів залишається на рівні проєктно-рівневих тестів поза цим етапом.

## Відтворення

```powershell
# Vulkan GPU world-tools (Debug і Release)
powershell -ExecutionPolicy Bypass -File sources/tools/sdk/test-sdk-property.ps1 -BuildRoot build/sdk-native-20261108-debug -OutputRoot build/sdk7-acceptance-20261115/world-tools-debug -FullIde -ClosePending -WorldToolsCommands -TimeoutSeconds 360
python -X utf8 sources/tools/sdk/verify-sdk-world-tools.py build/sdk7-acceptance-20261115/world-tools-debug

powershell -ExecutionPolicy Bypass -File sources/tools/sdk/test-sdk-property.ps1 -BuildRoot build/sdk-native-20261108-release -OutputRoot build/sdk7-acceptance-20261115/world-tools-release -FullIde -ClosePending -WorldToolsCommands -TimeoutSeconds 360
python -X utf8 sources/tools/sdk/verify-sdk-world-tools.py build/sdk7-acceptance-20261115/world-tools-release

# Повний CTest
ctest --test-dir build/sdk-native-20261108-debug --output-on-failure
ctest --test-dir build/sdk-native-20261108-release --output-on-failure

# Генерація / верифікація маніфесту прийняття SDK-7
python -X utf8 sources/tools/sdk/collect-sdk7-acceptance.py
python -X utf8 sources/tools/sdk/collect-sdk7-acceptance.py --verify
```
