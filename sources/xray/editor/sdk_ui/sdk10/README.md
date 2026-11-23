# SDK-10 — Particle Editor & Level Placement

Статус: **Прийнято (2026-11-19)** у Debug та Release. Усі перевірки зібрано в [acceptance.json](acceptance.json), докази запуску — у `build/sdk10-acceptance-20261119/`.

## Охоплена родина (`sources/xray/editor/sdk_ui/sdk0/function-matrix.json`)

1. **`particle-editor`** (`world show_particle_editor -> particle/editor_creator`):
   - **Бібліотека вузлів та палітра** ([SdkParticle.cs](../../../base/SdkParticle.cs)):
     - `SdkParticleLibrary` парсить оригінальний `particle_source.library` та `particle_node_styles.library`: усі `9` категорій `particle_toolbar` (`Emitters`, `Render`, `Initial`, `Velocity`, `Color`, `Size`, `Rotation`, `Events`, `Misc`), `29` активних типів вузлів плюс `Root` (`PGroup`, `PEmitter`, `Billboard`, `Trail`, `Mesh`, `Beam`, `Decal`, `InitColor`, `InitSize`, `InitVelocity`, `InitRotation`, `InitRotRate`, `InitDomain`, `TargetVelocity`, `VelocityLife`, `Acceleration`, `RandomTargetVeloc`, `Orbit`, `TargetColor`, `ColorLife`, `TargetSize`, `SizeVelocity`, `SizeLife`, `TargetRotation`, `RotationVeloc`, `RotationLife`, `onDeath`, `onBirth`, `onCollide`, `onPlay`, `KillVolume`), усі `10` типів доменів `domain_types` (`Point`..`Rectangle`), а також класифікацію runtime capabilities (`SdkParticleCapabilityReport`).
   - **Джерела даних (`data_sources`)** (`SdkParticleDataSource`, `SdkParticleDataSourcesHolder`):
     - Повна підтримка `float`, `float3`, `color` (RGBA ramp) і `color_matrix` кривих/матриць із прив'язкою до параметрів через `__super_tables`/`selected_source` та редагуванням у WPF `curve_editor_panel`.
   - **Граф-документ `.particle` та транзакції** (`SdkParticleDocument`, `SdkParticleGraphNode`):
     - Завантаження/збереження оригінальних `.particle` Lua-конфігів (`LOD 0`, `create_property_table`, `create_reference`, `children`, `children_order`, `data_sources`), валідація зв'язків та заборона циклів і заборонених батьківських пар.
     - `CommandEngine` транзакції з точним Undo/Redo (`AddNode`, `RemoveNode`, `AddGroupNodes`, `RemoveGroupNodes`, `ConnectNode`, `ReorderChild`, `SetNodeEnabled`, `SetGroupEnabled`, copy/cut/paste із новими GUID, редагування властивостей і `DomainType`, timeline `Delay`/`Duration`/`TotalLength`, криві `data_sources`), а також атомарний `Save()`.
   - **Симулятор та компілятор `checked_config` у Vulkan runtime** (`SdkParticleSimulator`, `SdkParticleBinaryCompiler`, `SdkParticleRuntimeRegistry`):
     - Детерміноване оцінювання багатоемітерної ієрархії, часових вікон `Delay`/`Duration`/`TotalLength`, доменів емісії, over-life/curve/color-matrix дій, `KillVolume` та подій (`onPlay`/`onBirth`/`onDeath`/`onCollide`).
     - Бінарний `checked_config` компілятор (`.particle` та forward unlit alpha/additive `.material` під `bounded_particle_template_cook` і `vulkan_particle_material_cook`) із перевіркою `ReadAndVerifyParticleBinary` та поколінним VFS-монтуванням `ISdkResourceBackend.Mount` у `UserDataRoot/sdk10_gen/` для миттєвого оновлення live Vulkan viewport.
   - **Редактор, рівень і безпечне закриття** (`SdkParticleEditor`, [WindowIde.Particle.cs](../../../base/WindowIde.Particle.cs), [SdkScene.cs](../../../base/SdkScene.cs), [WindowIde.Editing.cs](../../../base/WindowIde.Editing.cs), [ProjectEditing.cs](../../../base/ProjectEditing.cs)):
     - Інтеграція WPF `hypergraph_host`, `time_layout_host`/`time_layout_item` (`Delay`/`Duration`/`TotalLength`), `curve_editor_panel`, `property_grid.host` і транспорту прев'ю (`Play`/`Pause`/`Stop`/`Step`) зі скасуванням pending load/preview (`CancelPendingPreviewAndLoads`) при закритті.
     - Дормантний дубль `world/sources/particle_editor.cpp` не вмикався; розміщення у рівні проходить через `tool_particle` (`ProjectParticleProperties`).

## Верифікація та відтворення

- Оригінальні reference fixtures (9 `.particle` файлів) скопійовані без зміни першоджерел у [fixtures/particles](fixtures/particles).
- Unit-перевірки: [SdkParticleChecks.cs](../SdkParticleChecks.cs) у складі `xray_sdk_ui_compatibility` (`31` перевірка у Debug та Release).
- Vulkan GPU-прогін: [EditorWorld.ParticleChecks.cs](../../../base/EditorWorld.ParticleChecks.cs) + [verify-sdk-particle.py](../../../../tools/sdk/verify-sdk-particle.py):
  - Debug та Release: `0 Warning(s)`, `0 Error(s)`, SDK CTest `5/5`, повний CTest `31/31`, Vulkan validation `errors=0 warnings=0`, pending-close drain і впорядкований `M8 shutdown`, `7` переходів сцени і `2` піксельно-точні збіги Undo (`1==3`) та Redo (`2==4`).
- Консолідація і перевірка гешів: `python -X utf8 sources/tools/sdk/collect-sdk10-acceptance.py --verify`.
