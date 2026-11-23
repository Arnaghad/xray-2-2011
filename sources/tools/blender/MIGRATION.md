# Legacy corpus migration (B5)

For a new run, package delivery and failure triage, start with the current
[maintainer runbook](MAINTAINER.md). It includes the Blender 5.3 commands,
accepted corpus counts and retained zero-root policy. This document describes
the migration implementation and evidence contracts.

`migrate_legacy.py` is the `xray-migrate-legacy` maintainer command. It inventories
and hashes a read-only source tree, extracts scenes into a separate working copy,
imports the archived exchange with the installed Blender ZIP, saves editable
sources, then checks the saved files in a fresh Blender process. Maya is needed
only for binary/complex Maya source scenes. Cooked assets and basic MA use the
neutral readers.

## Run

Use Python 3.11+, the configured Blender host (Windows installation: 5.3.0), the asset cooker, and the main build's
`xray_animation_math_engine_contract_tests`. The engine test executable supplies
the real cubic playback verifier, including per-source clip precision.

```powershell
python -X utf8 sources/tools/blender/migrate_legacy.py --source-root C:/Stalker2_2011 --output build/my-new-migration --blender-exe "C:/Program Files/Blender Foundation/Blender 5.3/blender.exe" --cli-exe build/sdk-native-20261108-release/binaries/xray_asset_cook_cli.exe --playback-exe build/sdk-native-20261108-release/binaries/xray_animation_math_engine_contract_tests.exe --mayapy-exe "C:/Program Files/Autodesk/Maya2027/bin/mayapy.exe" --host-workers 4 --blender-workers 4
```

Run the same command again to resume. `--retry-failed` also retries previously
failed assets whose inputs/tools have not changed. Changed sources, companions,
resolved references, hierarchy identity or tool code invalidate the relevant
result. A missing or modified output checksum also forces reconstruction.
Unchanged `.blend` files keep their modification times. Failed outputs are never
accepted as cache hits for successful migration.

Each verified host asset also gets a `work/<id>/migration-receipt.json` with the
same fingerprint and complete output hashes. If the coordinator stops before its
next manifest checkpoint, a valid receipt restores that record without repeating
host work. Corrupt or changed outputs still rebuild. Atomic writes briefly retry
Windows sharing errors caused by concurrent readers, then report persistent
permission errors normally.

`migration-cache-compatibility.json` records narrowly audited early B5 producer
upgrades by exact SHA-256, including the exact unchanged dependency baseline.
Only verified output hashes and eligible error-free extraction snapshots can use
the listed advisory worker upgrades. Changes to sampled geometry, animation or
an unpinned dependency require extraction again.

Known zero-root source failures can be explained after the run with
`python sources/tools/blender/annotate_zero_root_failures.py --output OUTPUT`.
This command holds the migration lock, checks original source hashes and verifies
zero scale in the original splines and every available check-data companion. It
only enriches failed records with the exact cause and `retain_failed` policy; it
does not repair source data or turn failures into successful migrations.

Settings-only `sources/models/*.model` directories borrow geometry only from
their exact canonical `exported/models` counterpart. Source material/rigid-body
settings remain authoritative, and the counterpart hash becomes a dependency.
Missing geometry is reported explicitly. The same pairing applies to skin
settings, including their declared skeleton reference.

`--include SUBSTRING` selects relative source paths; repeat it to combine
selections. `--limit N` bounds that selection. `--prepare-only` caches scene
extraction without claiming completed Blender migration. `--batch-size` bounds
host batches and `--worker-timeout` bounds their wall time. `--host-workers`
controls independent Maya processes with separate profiles and reference copies.
`--blender-workers` controls Blender concurrency (default 1). Each pool worker
retains its own isolated extension profile and runs its batches serially, including
fresh-process verification. The coordinator checkpoints after every finished batch.

Exit code 0 means all selected entries have migrated or carry explicit warnings;
2 means at least one selected entry failed. Setup/argument errors return an error.
Warnings and failed source scenes require review before distributing the corpus.

## Outputs and state

The output must be a separate directory tree. The runner rejects an output inside
the original tree, or an output that contains it. A process-held workspace lock
prevents concurrent writers to one migration destination. Original content hashes
are compared before and after the run.

- `migration-manifest.json`: per-source status (`migrated`, `with_warnings`,
  `failed`, or `pending` while running), input/dependency fingerprints, output
  hashes, per-channel key reports, playback/reopen checks and explicit reasons.
- `resources/sources/blender/<corpus-relative-source-path>.blend`: editable scene
  sources. Source suffixes are retained to distinguish same-stem MA/MB/assets.
- `work/<stable-path-id>/exchange/`: archived v2 samples, curves and scene index;
  Maya working copies and per-clip re-export/cook/playback evidence live nearby.
- `legacy-corpus.json`: the actual inventory used for the run.
- `skeleton-families.json` and `skeleton-catalog.json`: hierarchy evidence from
  original skeleton configs and evaluated Maya scene DAGs.
- `jobs/`, `logs/`, `profiles/`: reproducible host inputs, diagnostics and private
  application profiles. Previous differing logs are retained.

Writes use temporary files and atomic replacement. A `.blend` becomes a
successful manifest output only after saving, reopening in a new process,
checking its migration identity, key count and per-bone joint corrections.
The manifest hashes archived exchange files as well as `.blend` outputs.

## Hierarchy and clip verification

Joint names alone do not establish a skeleton family. The catalog records actual
parent links. Source clip ordering may differ; lookup and pose comparison use
bone names. Missing or conflicting hierarchy variants fail explicitly.
`--family-map overrides.json` accepts a mapping of corpus-relative clip paths to
specific hierarchy IDs when the maintainer has resolved that ambiguity.

The Maya route uses actual skin influence sets and includes their real joint
ancestors even when those parents carry no weights. Extra rig/end joints can be
omitted only when no kept descendant depends on the omitted parent. The original
controller rig is represented by evaluated joint animation; this is not a rig
controller conversion. A source exceeding the runtime's bone limit, omitting an
interior parent, or containing unsupported assets receives explicit diagnostics.

Cooked clips contain no bind pose. Standalone clip scenes therefore retain proven
parent links and editable poses with clearly reported placeholder rest positions;
these do not certify an arbitrary character skin's bind pose. Mesh sources retain
their own actual bind transforms and weights.

Meshes with the same hierarchy and compatible bind transforms share an armature.
Distinct skin binds receive their own matching animation. When the evaluated
animation's authoring bind differs, its joint poses are fitted onto the skin bind;
the original exchange snapshot remains archived. Actions use readable source
names, while reports retain the original display names and source paths.

Every migrated clip is sampled from its imported Action, re-exported, cooked at a
tighter fitting budget and compared with the archived source samples in actual
engine cubic playback. Standard/additive type, frame count, source fps, root
policy and source precisions participate. Key reports retain exact/fit modes,
source direct/controller key counts, output keys per channel, fallback reasons
and maximum pose errors. Rotation playback compares physical rotation matrices,
so equivalent Euler branches do not cause false failures. Translation and scale
remain channel comparisons; a recorded 2e-6 numeric allowance covers float storage.
The saved `.blend` is reopened independently before success is recorded.

The shared cubic fitter now checks its sample precision after adaptive fitting.
If that fit reaches its control-point or conditioning limit, a cubic Hermite
interpolation fallback retains every sample and is checked again. This change is
also exercised through the actual engine converter, including repeated knots.
Root-relative normalization also retains the small cosine near gimbal lock;
the previous sine threshold could discard real rotation by about 0.009 radians.
A physical-matrix regression covers both signs of the near-singular pitch, and
the formerly failing original Maya animations pass native engine playback.

The neutral reader follows archive folder/file hard links with bounds and cycle
checks and reads Lua decimal byte escapes. It repairs only bounded f32 skin-weight
rounding drift, retaining repair counts in metadata. Small root-identity rounding
noise may be folded into the movement track only within the source precision;
meaningful unsupported root motion fails explicitly. A reserved legacy physics
root joint becomes an editable Rigid root with its original metadata retained.

Legacy triangles with repeated vertex indices are removed before creating the
Blender mesh; the remaining topology is validated before custom normals are set.
This prevents a native Blender 5.0.1 crash found in real character/weapon meshes.
Vertex order, weights and per-vertex UV/normal mappings remain intact. Repair
counts and validation changes are saved on the mesh and collection and in the
migration record's `import_warnings`; the original indices remain in the archive.

Scene-to-clip filename links only propose Maya reconciliation candidates. The
reconciler compares every sampled global bone pose in meters after applying the
declared root policy, with clip-local frame alignment recorded explicitly. A
different range, hierarchy or pose remains a diagnosed mismatch. An unavailable
candidate does not become a proven relationship. Self-roundtrip verification and
cross-source reconciliation are reported separately.
Different frame ranges are rejected from metadata before unpacking sampled
payloads. Matching candidates still require full bundle validation and pose
comparison; no terminal frame is silently dropped to obtain a match.

For cooked-only migration without Maya, pass an already verified catalog using
`--family-catalog PATH/TO/skeleton-catalog.json` and select cooked asset paths.
Catalog fingerprints are checked against their stored parent trees. This uses
saved hierarchy evidence, without opening Maya scenes.

The checked-in [legacy-skeleton-catalog.json](legacy-skeleton-catalog.json)
contains the five proven hierarchies covering all 347 clips in the B0 corpus lock.
It records the corpus identity, extractor hash and original evidence paths/hashes.
Use `--family-catalog sources/tools/blender/legacy-skeleton-catalog.json
--include .clip`, choose a separate output directory and omit `--mayapy-exe` to
run the complete cooked-clip route without Maya. The filter also includes the
346 check-data companions.

## Maya source handling

Maya opens working copies with `ignoreVersion` and script-node execution disabled.
References are initially unloaded, resolved against the inventoried source tree,
copied into the workspace and then loaded without script execution. Ambiguous or
missing references are reported. Missing plugins/textures, temporary scenes and
individual extraction failures produce warnings or failures in the manifest.

A native Maya crash records the current scene. Later, unattempted scenes in that
batch run in fresh processes; they are not labelled corrupt because a different
scene crashed. Previously completed batches are checkpointed. No source file is
rewritten or saved by Maya.

## Tests

`xray_blender_migration_state` checks checksum-based reuse, output corruption,
path containment and ambiguous parent maps. `xray_blender_migration` uses the
installed ZIP without Maya: all five cooked kinds, basic MA keys, engine clip
playback, fresh-process reopen, unchanged resume, output repair, changed-source
invalidation and a deliberate binary-Maya failure. Test data is generated in a
private subtree; original game resources are not test fixtures.
The state test holds a real Windows reader across a checkpoint, and the host test
simulates a lost coordinator checkpoint before validating receipt recovery.

`xray_maya_migration` generates a real referenced binary Maya scene with indirect
controller animation. It checks that noninteger controller seed times survive in
all saved joint channels and that the saved Blender skin deformation matches
independently sampled Maya world vertices. The independent Blender reader runs
without the extension. Missing-host, incompatible hierarchy and physically wrong
engine rotation cases are tested as failures.
