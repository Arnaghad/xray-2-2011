# SDK-6 — asset authoring завершено та прийнято (2026-11-15)

Фінальна реалізація та acceptance 2026-11-15 підтверджені у
[acceptance.json](acceptance.json) і evidence-директорії `build/sdk6-acceptance-20261115/`:

- **Texture (`texture`):** 2D TGA, 6-face horizontal cubemap, horizontal volume strip
  (`texture_dimension = 2`) та 2D array strip (`texture_dimension = 4`),
  мультишаровий grid-прев'ю у Vulkan (`type_cube` та `type_2d_array`), legacy `<1.2`
  options з `UpgradeLegacyOptions()`, classification (`TextureType`),
  `stb_dxt` DXT1/1a/3/5 (BC1/BC2/BC3), HighQuality, A8/NORMAL_MAP та RGBA fallback.
- **Materials (`materials`):** `g_stage_default`, `forward/forward_default`,
  `f_stage/f_stage_unlit`, `decals/decals_default` (live projector decal preview),
  `distortion/distortion_default` & `distortion_panner`, явний undoable instance
  schema repair, original palette (9 stages / 25 effects) з явною класифікацією
  `Supported` vs `SchemaOnly` і typed parameter grid.
- **Render options (`render-options`):** 61 original option із classification
  (2 Vulkan-adapted, 59 explicitly blocked), mapped GPU apply, owned history та lossless roundtrip.
- **Models, physics, skeletal, OBJ та Maya/DCC (`model-editor`, `obj-export`, `maya-*`):**
  static/skinned surface assignment, `lod_hierrarchy` (`LOD0`–`LOD2`, `NextLodSurface`),
  `SdkCompositeModelDocument`, `SdkSpeedTreeDocument` (`speedtree/*.options`),
  collision sphere/box/cylinder + rigid-body fields, independent Bullet preview/authored Simulate,
  62-bone та свіжий 3-bone Maya 2027 skeletal playback і Blender 0.3.2 Reimport roundtrip.

CTest Debug та Release: `30/30` (`100% tests passed`).
18 послідовних Vulkan GPU workflows (по 9 у Debug та Release) пройшли перевірки
з `M8 validation errors=0 warnings=0` і коректним teardown.
Додатковий опис проміжних деталей: [model-physics-skeletal.md](model-physics-skeletal.md)
та [material-source.md](material-source.md).

## Історичний checkpoint 2026-11-12

Нижче збережено опис початкового checkpoint. Його codec/model/material обмеження
стосуються того snapshot; поточний стан наведено вище та в migration plan.

Перший checkpoint: source texture/options, 2026-11-12.
Наступний workflow: [material/instance source](material-source.md), включно з
native dependent publication та resource-callback shutdown barrier.
SDK-6 загалом **не завершено**; `runtimeWorkflowAccepted` для повної texture
family залишається false. Перевірки й hashes checkpoint наведено в
[texture-source-checkpoint.json](texture-source-checkpoint.json).

## Реалізований workflow

`Tools → Texture editor` відкриває обраний TGA із configured resource mounts.
У Resource browser команда `Edit texture source` відкриває обраний URI.
Вікно використовує original WPF property grid та окремий shared CommandEngine:
Undo/Redo, saved marker, Revert draft, підтвердження dirty close та скасування
loading не змінюють history level project.

2D TGA/options підтримують sRGB, generation mip levels і вже наявне native
NORMAL_MAP packing. Початкові format/high_quality та інші source metadata
зберігаються. BC compression/import quality поки не редагуються: current TGA
cook декодує RGBA; назва DXT1 у source metadata не є доказом BC recompilation.
Cube/array authoring та інші source codecs явно відхиляються цим вікном.
Native cook відхиляє невідомі import options до публікації ресурсу.

Apply preview перевіряє чернетку через production native texture cook і відкриває
незалежний Vulkan output. Повторний Apply закриває попередній output через
наявний GPU acknowledgement. Draft не змінює texture level до Save.

Save копіює source у `workspace/sources/textures`, атомарно записує options,
прив'язує live texture states і наступні model/material cooks до того самого
джерела та чекає завершення native reload. Source identity однакова для
оригінального TGA та відповідного converted DDS. VFS/dependency browser і
повторне відкриття бачать writable overlay. Під час нового сеансу збережені
texture sources знову публікуються.
Resource cache fingerprint включає workspace texture/material dependencies,
зокрема зміни лише options без зміни байтів TGA.

Malformed source, cancellation, external options/image changes або failed
publication не виставляють saved/success marker. Після failure документ
відновлює попередні options і source binding; попередня GPU image утримується.
Reference asset pack не переписується. Busy operation блокує history edits;
dispose скасовує запит і відкладає звільнення preview mount до його drain.
Preview scratch files зберігаються в user-data/asset-preview поза authored
sources; цей checkpoint не додає автоматичного очищення disk cache.
Play відхиляється, поки texture operation обробляє UI events і ще не завершена.

## Перевірки

Managed contract перевіряє реальні document/UI services із fake native boundary:
history, no-op, numeric flags, canonical TGA suffix, metadata roundtrip,
save/reopen, failure compensation, concurrent options change, cancellation,
runtime lock, owner thread, dirty close та release preview mounts.

Окремий native GPU workflow використовує production OpenTextureSource,
ApplyDraftPreview та SaveSource. Reference wood_box і bullet_hit_concrete
перевіряються як два різні source assets. Fenced captures доводять ізоляцію
draft preview, зміну level/model після Save, точне повернення після Undo/Save,
fresh model cook і збереження зображення після відхилених options.
Це не приймання всіх asset formats або всієї texture family.

Відтворення з кореня repository після build:

```powershell
sources/tools/sdk/test-sdk-property.ps1 -BuildRoot build/sdk-native-20261108-debug -AssetsRoot build/sdk-assets -OutputRoot build/new-sdk6-texture-run -FullIde -TextureCommands -ClosePending
python sources/tools/sdk/verify-sdk-texture.py build/new-sdk6-texture-run
```

Для Release потрібно вибрати відповідний build root. Кожен GPU run потребує
нової OutputRoot; GPU workflows запускаються послідовно.
Snapshot/evidence hashes перевіряються командою
`python sources/tools/sdk/check-sdk6-texture-checkpoint.py`.

Підсумок checkpoint: Debug/Release — CTest 6/6 і по 19 UI groups. Чотири
послідовні GPU workflows (texture у двох конфігураціях, Reload та authored
Play/Simulate regressions) пройшли readback checks і teardown з validation 0/0.

## Статус закриття SDK-6

Усі 9 родин етапу **SDK-6** (`texture`, `materials`, `render-options`,
`model-editor`, `obj-export`, `maya-static`, `maya-skin`, `maya-physics`,
`maya-animation`) прийняті в Debug і Release 2026-11-15 із зафіксованими доказами в
[acceptance.json](acceptance.json) (`accepted = true`, `stageComplete = true`) та
`build/sdk6-acceptance-20261115/`. Перевірка:
- `python sources/tools/sdk/collect-sdk6-acceptance.py --verify`
- `python sources/tools/sdk/check-sdk6-texture-checkpoint.py`
- `python sources/tools/sdk/check-sdk6-material-checkpoint.py`
