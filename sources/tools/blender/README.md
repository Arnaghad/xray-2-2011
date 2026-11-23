# Blender pipeline — X-Ray 2.0 SDK

Blender is the default DCC; Maya is a supported alternative for authoring and
binary/complex legacy scenes. Extension **0.3.2** includes the artist guide;
the Windows verification host is the installed **Blender 5.3.0**. The pipeline
supports static, skeleton, skin, animation and physics assets, exchange v1/v2,
legacy/basic MA import, editable exact/fitted keys, migration, cook and SDK
source/reimport with native playback.

- [Artist guide and first export](xray_blender/README.md): installation, rig,
  materials, locators, Actions, physics, editable keys and common errors.
- [SDK workflow](SDK.md): associations, workspace master, DCC options, Reimport,
  watcher, live model reload and skeleton playback.
- [Import details](IMPORT.md): supported data, bind frames, key modes and limits.
- [Maintainer runbook](MAINTAINER.md): package delivery, sample/full migration,
  resume, diagnostics and accepted zero-root policy.
- [Migration internals](MIGRATION.md), [CI commands](CI.md),
  [Maya alternative](../maya/README.md), [release notes](CHANGELOG.md).

B0–B8 implementation and local documentation acceptance are recorded in the
[pipeline plan](../../../blender_pipeline_plan.md), [B7](b7-acceptance.json) and
[B8](b8-acceptance.json) receipts. GitHub execution awaits remote/runner setup;
full SDK8 graph/retargeting/actor integration remains separate work. Known
Windows installer rename failures and the retained failed results are documented
in the maintainer runbook.

## Build and install the extension

```powershell
python sources/tools/blender/build_extension.py
```

Install `build/blender/extension/xray_blender-0.3.2.zip` using Blender's
**Preferences → Get Extensions → Install from Disk**, then enable **X-Ray 2.0**.
The archive includes GPL-3.0-or-later code/license and the needed `dcc_common`
modules. It does not depend on the repository's Python path or a Maya install.
Build from these sources rather than copying the source package directory alone:
the `_dcc_common` dependency package is inserted during ZIP assembly.

Each build writes an adjacent `.sha256` and `.json` with deterministic member
hashes. ZIP ordering, timestamps and permissions are fixed, and the headless
contract checks repeated builds for byte-identical archives.

See the [artist instructions](xray_blender/README.md) for the X-Ray menus,
resources/cooker preferences, body/collision/constraint Empties, static roots,
locators, material metadata, armature axis space and export/cook operators.

## Select the Blender host

Windows tests and SDK defaults use the user's installed **Blender 5.3.0** at
`C:/Program Files/Blender Foundation/Blender 5.3/blender.exe`. The old local
portable 5.0.1 extraction and download were removed. Select another host explicitly
with `-DXRAY_BLENDER_EXECUTABLE` and its exact `-DXRAY_BLENDER_EXPECTED_VERSION`.
The runtime contract checks that version; coordinate/extension tests exercise the
selected host rather than comparing it to a historical download lock.

The historical [runtime lock](runtime-lock.json) records the old 5.0.1 provenance.
Its local portable runtime/download were removed at the user's request. Current
Windows instructions use the installed host; Linux runtime has not been executed
in this workspace.

## Inventory the original corpus

```powershell
python -X utf8 sources/tools/blender/legacy_corpus.py --root C:/Stalker2_2011 --output build/blender-b0/legacy-corpus.json
```

The scanner opens originals only for reading, refuses output inside the original
tree, rejects linked sources, and fails on unreadable directories or a file that
changes during hashing. Results have stable ordering and no timestamps. Every
asset has a relative path, size, SHA-256, kind and temporary-scene classification.
Directory assets include sorted member hashes. See the JSON's `directory_hash`
field for the exact hash encoding. Scene-to-clip links are stem-based candidates;
ambiguity and missing links are explicit and require validation during migration.

[legacy-corpus-lock.json](legacy-corpus-lock.json) records the current full corpus
fingerprint and the inventory file hash. The 2026-11-13 inventory contains 3,836
assets: 975 `.mb`, 5 `.ma`, 347 `.clip`, 346 `.clip_check_data`, 2,144 `.model`,
11 `.skinned_model`, 3 `.skeleton`, and 5 physics assets. The extra 25 `.mb` outside
`xr2-dsgn/resources` are in `s2_2011_documentation`. All are included.

The copied clip `walk_01_turn_left_small - êîïèÿ.clip` has no matching check data.
Do not count it as a baked pair. The importer evaluates its spline with explicit
fallback fps. B5 measured 3291 saved `.blend` files at approximately 1.61 GB,
without packed textures; the earlier B0 size estimate remains historical in the
lock. See the maintainer runbook for the actual corpus outcomes and distribution.

## Build and verify

```powershell
cmake -S sources/xray/maya_cook -B build/blender-b1/cook -G Ninja -DCMAKE_BUILD_TYPE=Release
cmake --build build/blender-b1/cook --parallel 8
ctest --test-dir build/blender-b1/cook --output-on-failure
```

Both the standalone cooker build and the full engine build register:

- `xray_dcc_common_unit`: host independence, legacy Python compatibility, CLI
  discovery, v2 validation in Python/C++, curve metadata, all asset kinds and
  byte parity between v1/v2 Y-up aliases and engine-space input.
- `xray_blender_corpus_unit`: stable hashes, directory assets, ambiguous pairs,
  original-tree protection, and rejection of an archive with an incorrect hash.
- `xray_blender_runtime`: real `--background --factory-startup`, exact configured
  version, and creation/evaluation of a curve through slotted Action channelbags.
- `xray_blender_coordinates`: independent `mathutils` checks of cooked v2 mesh
  positions, normals/winding, locators, bind pose, sampled clip poses, gimbal/turn
  continuity, root motion, physics and inverse collision fitting. In the full
  build this also loads all five cooked asset kinds through engine loaders.
- `xray_blender_extension`: builds the actual ZIP, validates it through Blender's
  extension command, installs it in a private offline profile, creates authoring
  data, saves `.blend` and preferences, then checks both in independent Blender
  processes. It also checks repeated enable/disable, saving while disabled,
  callback cleanup and rollback on a conflicting RNA property.
- `xray_blender_live`: installs the ZIP into a private offline profile, creates
  actual scenes and exports/cooks all five asset kinds. Reopening the `.blend` in
  another process must reproduce identical exchange/cooked bytes. It exercises
  batch Actions, additive clips, NLA/constraints, non-XYZ authoring rotations,
  local center, top-four weights, modifiers, both collision meshes, legacy frame
  correction, fitting, branching ragdolls and invalid scenes. Full engine builds
  load the cooked assets and independently compare six cooked clips against
  Blender matrix samples in the actual engine cubic playback implementation.
- `xray_blender_import`: installed-ZIP import/decompile with no Maya paths,
  all five raw/converted types, exact MA keys/handles, editable fitted curves,
  nonuniform bind correction, native Z-up targets, save/reopen and transaction
  rollback. Full builds validate re-exported resources and six clips through
  engine loaders/playback. See the [import guide](IMPORT.md).

The host test fails if Blender is missing. An explicit
`-DXRAY_BLENDER_EXECUTABLE=<path>` and `-DXRAY_BLENDER_EXPECTED_VERSION=<version>`
select another installed host. B0–B5 contracts and full engine playback remain
enabled. B7 adds `xray_dcc_ci_unit` and `xray_blender_no_maya`; the
[CI runner](CI.md) requires complete coverage and records optional Maya absence.

For an inspectable local authoring fixture and retained isolated profile:

```powershell
python -X utf8 sources/tools/blender/run_extension_test.py --blender-exe "C:/Program Files/Blender Foundation/Blender 5.3/blender.exe" --cli-exe build/sdk-native-20261108-debug/binaries/xray_asset_cook_cli.exe --artifacts build/my-extension-run --keep-profile
```

The sample is `build/my-extension-run/profile/authoring.blend`. Its helper data can
be inspected after enabling the ZIP. Test preferences and repositories remain
inside the test profile; the user's Blender preferences are not changed. CTest
uses separate artifact/profile directories for each build configuration and
cleans only its temporary profile. Logs remain under each build's
`xray/maya_cook/blender/extension-contract` (or `blender/extension-contract` for the
standalone cooker).

## Shared mathematical core and completed migration

The cooker and native engine track-processing API now share
[`animation_math`](../../xray/animation_math/README.md). Fitting, legacy sample
decoding and actual engine curve validation no longer create a Maya world.
[Exchange v2](../dcc_common/SPEC_V2.md), C++ coordinate conversion and v1 defaults
remain supported. B3 supplies static, skeleton, skin, animation and physics
exporters with export → cook → engine validation. See the packaged
[artist guide](xray_blender/README.md) for root/rig/material/Action conventions.
The v0.2 extension also exposes cook, collision fitting and editable ragdoll
generation. B3-M now switches the Maya frontend to v2 and proves parity on
equivalent scenes in both hosts. See [DCC parity](../dcc_common/PARITY.md) for the
live CTest, fixture coverage, tolerances and retained artifacts. Version 0.2.1
also fixes physics root/joint ordering. B4 now adds import/decompile; the current
0.3 extension includes the neutral legacy codec and basic MA reader. See
[IMPORT.md](IMPORT.md) for supported formats, editable-key contracts and limits.

B5 adds the [migration runner](MIGRATION.md): hash-based resumption, isolated
Maya extraction, hierarchy evidence, editable Blender sources, per-clip engine
playback, fresh-process reopen and legacy/Maya pose reconciliation. Original
sources remain read-only. `xray_blender_migration_state` and
`xray_blender_migration` cover state and the complete no-Maya route;
`xray_maya_migration` covers referenced binary scenes and upstream controller keys
when Maya is available.

To reproduce the live exporter contract with retained outputs:

```powershell
python -X utf8 sources/tools/blender/run_live_test.py --blender-exe "C:/Program Files/Blender Foundation/Blender 5.3/blender.exe" --cli-exe build/sdk-native-20261108-debug/binaries/xray_asset_cook_cli.exe --contract-exe build/sdk-native-20261108-debug/binaries/xray_maya_cook_engine_contract_tests.exe --playback-exe build/sdk-native-20261108-debug/binaries/xray_animation_math_engine_contract_tests.exe --artifacts build/my-export-run
```

`exporters.blend`, exchange/raw/converted assets and logs remain in the artifact
folder. Temporary extension profiles are removed after the run. A test-only
`.clip_samples` beside each raw clip stores independent matrix expectations for
engine playback; it is not a shipping resource format.
