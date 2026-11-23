# SDK-9 — Sound Editor & Sound Placement

Статус: **Прийнято (2026-11-19)** у Debug та Release. Усі перевірки зібрано в [acceptance.json](acceptance.json), докази запуску — у `build/sdk9-acceptance-20261119/`.

## Охоплені родини (`sources/xray/editor/sdk_ui/sdk0/function-matrix.json`)

1. **`sound-editor`** (`world show_sound_editor -> sound/editor_creator`):
   - **WAV/PCM декодування та audio backend** ([SdkSound.cs](../../../base/SdkSound.cs)):
     - `SdkWavAudioData` парсить RIFF/WAVE заголовки (`fmt ` та `data` chunks включно з метаданими), 8/16/24/32-bit PCM та IEEE float, обчислює `SampleRate`, `Channels`, `BitsPerSample`, `DurationSeconds`, `PeakAmplitude`, `RmsAmplitude` та `ComputeWindowRms`.
     - `SdkSoundAudioBackend` перевіряє `winmm.dll` (`waveOutGetNumDevs`) незалежно від UI, розділяючи апаратний вивід (`WinMM/SoundPlayer+PCM`) та детерміноване програмне PCM-мікшування (`SoftwarePCM-Headless`).
   - **Single Sound (`.wav`, `.raw_options`, `.single_sound_options`)**:
     - `SdkSingleSoundDocument` редагує профілі `high_quality` / `medium_quality` / `low_quality` та криву `spl` (звуковий тиск у dB по відстані в метрах), прив'язану до WPF `curve_editor_panel` через `float_curve` і `float_curve_evaluator`.
     - `CommandEngine` Undo/Redo, атомарний workspace `Save()`/`Reload()`, жива зміна відстані слухача без переривання playback (`UpdateTrackSpatial`) та чітка діагностика відсутніх `.wav`.
   - **Sound Collection (`.sound_collection`)**:
     - `SdkSoundCollectionDocument` підтримує `random` (`dont_repeat_sound_successively`), `sequential` (`cyclic_repeat_from_sound`), symbol-посилання `resources.single_sound_type` та цілі `256`/`257`/`258`, вкладені inline sub-collections, перевірку відсутніх кліпів (`ValidateReferences`), Undo/Redo, PCM audition та атомарний `Save()`/`Reload()`.
   - **Composite Sound (`.composite_sound`)**:
     - `SdkCompositeSoundDocument` підтримує `before_playing_pause`, `after_playing_pause`, `offset_type` (`random`, `constant`, legacy `caonstant`), багатошарове мікшування `RenderMixedBuffer`, Undo/Redo та атомарний `Save()`/`Reload()`.
   - **Sound Scene (`.editor_sound_scene`)**:
     - `SdkSoundSceneDocument` керує 3D інстансами (`id`, `filename`, `resource_type`, `looped`, `position`, `rotation`), 3D позою слухача (`SetListenerPose`) з обчисленням SPL, linear gain і stereo pan.
   - **IDE інтеграція та життєвий цикл** (`SdkSoundEditor`, [WindowIde.Sound.cs](../../../base/WindowIde.Sound.cs)):
     - Підключено до `Tools -> Sound editor` і `Resource browser` (`EditSourceRequested`), перевірено детерміновану зупинку playback і звільнення буферів при `Reload()` та закритті редактора під час активного відтворення.

2. **`sound-placement`** (`level_editor tool_sound`):
   - **Level tools & inspector** ([ToolsTab.cs](../../../base/ToolsTab.cs), [ProjectEditing.cs](../../../base/ProjectEditing.cs), [SdkWorldTools.cs](../../../base/SdkWorldTools.cs), [ProjectPropertyAdapters.cs](../../../base/ProjectPropertyAdapters.cs)):
     - Інструмент `sound` завантажує `sound.library` (`point_sound` -> `base:sound`, `volumetric_sound` -> `base:volumetric_sound`) і ініціалізує параметри (`wav`/`sound`, `emitter_type`, `sound_type` `point`/`cone`, `positional`, `looped`, `radius`, cone кути, `collision_geometry`).
     - `ProjectSoundProperties` виконує просторове затухання `EvaluateSpatialAttenuation` та живий PCM preview (`CreatePreviewSession`), візуалізується у Vulkan-сцені через [SdkScene.cs](../../../base/SdkScene.cs), підтримує Undo/Redo та зберігається/перевідкривається у Cyrillic `.xprj` без втрати посилань.

## Верифікація та відтворення

- Reference fixtures скопійовані (без зміни джерел у `C:/Stalker2_2011/xr2-dsgn`) у [fixtures/sounds](fixtures/sounds).
- Unit-перевірки: [SdkSoundChecks.cs](../SdkSoundChecks.cs) у складі `xray_sdk_ui_compatibility` (30 перевірок у Debug та Release).
- Vulkan GPU прогін: [EditorWorld.SoundChecks.cs](../../../base/EditorWorld.SoundChecks.cs) + [verify-sdk-sound.py](../../../../tools/sdk/verify-sdk-sound.py):
  - Debug та Release: `0 Warning(s)`, `0 Error(s)`, SDK CTest `5/5`, повний CTest `31/31`, Vulkan validation `errors=0 warnings=0`, pending-close drain і впорядкований `M8 shutdown`, 4 переходи сцени і 4 точні збіги readback (Undo, Redo, Save/Reopen, Spatial query).
- Консолідація і перевірка гешів: `python -X utf8 sources/tools/sdk/collect-sdk9-acceptance.py --verify`.
