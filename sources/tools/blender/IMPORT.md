# Import and decompile (B4)

Install `build/blender/extension/xray_blender-0.3.2.zip` in the installed Blender
5.3.0. Choose **File → Import →
X-Ray / Maya ASCII**, or **X-Ray 2.0 → Import X-Ray / Maya ASCII**. This workflow
does not use Maya or execute MEL/Python embedded in a source file.

Supported inputs:

- Exchange v1/v2: choose `manifest.json` or its bundle directory. A decoded MA
  scene cache can also be opened through `scene.json`.
- Legacy `.model` / `.skinned_model`: VFS archive or unpacked model directory.
  For an unpacked model, enter the directory containing `render/export_properties`.
- `.skeleton`, `.clip`, `.physics` / `.ph`: original Lua resources and the current
  SDK's 64-bit checked binary config format. Converted model directories are also
  decoded. Static converted vertices have quantized normal/tangent/binormal data;
  the original unquantized basis cannot be recovered from those bytes.
- `.ma`: UTF-8 Maya ASCII with basic joint/transform TRS, stored animCurve tangent
  vectors, triangle/quad meshes with UV set 0, and direct skinCluster connections.
  Scripts/configuration nodes are treated as data and never executed.

The import creates a separate collection. Invalid input removes newly created
objects, meshes, materials, Actions and collections, restores the selected
armature's previous Action/slot/NLA/pose modes, and restores scene time/fps.
Logical helper names are retained separately from Blender's unique object names,
so importing two assets with an `anchor` locator does not rename the resource.

## Skeleton and skin

For a skin, choose **Skeleton File** or select a matching armature. The importer
can also find the skin's logical skeleton beside the input or in the configured
resources folder. It checks names and available bind poses before using an
existing rig. Bind data in an MA scene is imported with its hierarchy.

A standalone `.skeleton` contains hierarchy only. Its preview bone positions are
placeholders. To create a bound rig, import the skin with its skeleton file.
Skeleton-family differences are reported rather than guessed. Corpus-wide
hierarchy evidence and reconciliation are available in [migration](MIGRATION.md).

Imported rigs retain RH Y-up source coordinates, with display rotation/units on
the armature object. Blender bone Y follows original joint X. A saved per-bone
correction maps its rest/pose frame back to the source joint; this also retains
nonuniform bind scale, which Blender edit-bone matrices cannot store directly.
Skin weights, UVs, custom split normals, materials and bone-bound locators are
restored. The cooker reconstructs tangents from the imported geometry/UVs.

Static and physics containers similarly retain their imported source space and
unit scale. Geometry/helper transforms are authored below those containers;
their display placement is excluded from export. Physics root metadata and all
six engine joint-local DOF dictionaries are preserved.

## Clips and editable keys

Select a matching armature or choose the clip's **Skeleton File**. The importer
expects the common `object_movement` convention and an identity root-joint track.
It preserves source fps, frame offset, sample count, precisions, additive type and
whether root motion is already relative. Clips also import onto matching native
Z-up armatures, with unit/frame conversion.

If a `.clip_check_data` companion exists, its nine sampled channels are the
primary source of poses, fps and precision. Mixed Lua tables retain both numeric
channels and `start_time`. Malformed or mismatched check data fails explicitly.
Otherwise the clip's cubic B-splines are sampled using **Fallback Clip FPS**;
their time domain remains engine 30-fps frames. B-spline knot times seed the
editable curves, including object-movement knots for the root bone.

For compatible direct MA channels (zero rotateAxis/bind rotation and the defined
joint-frame convention), rotations and scales are mapped by signed axis
permutation. Translation is transferred exactly when contributing channels have
matching key times, interpolation and handle times. Weighted/unweighted tangent
vectors become FREE Bezier handles; stepped and linear segments are retained.
Authoring rotation orders are mapped with the frame convention. Independent
pre/post infinity uses extrapolation, Cycles and time limits.

Other channels use an editable fit: original keys/knots and endpoints are retained
as seeds, derivatives estimate handles, and worst-error sample times are added
until the clip's pose precision is met at every source frame. The importer samples
the resulting Blender Action through the exporter and reconciles it against the
archived source matrices. It does not accept editable keys solely on appearance.
The Action and collection retain JSON reports with each channel's exact/fit mode,
key count, fallback reason and maximum pose error. Original curve metadata is
also retained on the Action.

The active imported clip becomes the armature's Action with NLA mixing disabled.
Source sample range/fps/precision are retained on the Action for direct re-export.
Imported source range is fixed by its sample count; edit those Action properties
when intentionally changing the clip's duration through the advanced Action RNA
properties (`xray_frame_start`, `xray_sample_count`, `xray_fps`). These are not
separate controls in the current X-Ray sidebar; an evaluated Scene/NLA export
uses scene range/fps and must be reviewed for the intended clip type. The sidebar export precision
fields can override source precision when exporting deliberately.

## Basic MA boundaries

References, IK/constraints/expressions, unsupported evaluation connections,
unbaked pivots/shear, automatic tangents without stored vectors, step-next,
construction-history meshes and unsupported polygon records require a flattened
basic `.ma` or the maintainer Maya migration route. The reader is a data parser,
not a Maya evaluator. Mesh support currently uses a single material and flat
polygon normals; complex history/material/smoothing workflows use the Maya route.
Binary `.mb` needs the [maintainer Maya route](MAINTAINER.md) or an already
verified migrated `.blend`. A flattened basic `.ma` can be read without Maya.
FBX/USD require their own Blender importer and manual preparation to X-Ray rig
conventions; automatic FBX/USD rig adoption is not implemented.

The root-joint track must be identity within source precision. The 23 known
Root.scale=(0,0,0) clips retain `failed` status with an explicit cause. Changing
scale to (1,1,1) would change source poses beyond precision. The importer rejects
that data without changing the previous Action or leaving partial scene data.

## Verification and reproduction

`xray_blender_import` runs the installed ZIP with Maya paths removed. It covers
all five asset kinds, raw and converted legacy resources, nonuniform bind scale,
check-data priority, exact/fitted keys, native Z-up targets, physics/materials,
save/reopen and rollback after a mid-import failure. Six re-exported clips are
checked against independent samples in actual engine cubic playback. The MA
fixture and oracle were generated in Maya 2027; tests require only their saved
data. `test_import_readers.py` independently compares the reader against Maya's
evaluated curves, tangent vectors and world matrices.

```powershell
python -X utf8 sources/tools/blender/run_import_test.py --blender-exe "C:/Program Files/Blender Foundation/Blender 5.3/blender.exe" --cli-exe build/sdk-native-20261108-debug/binaries/xray_asset_cook_cli.exe --contract-exe build/sdk-native-20261108-debug/binaries/xray_maya_cook_engine_contract_tests.exe --playback-exe build/sdk-native-20261108-debug/binaries/xray_animation_math_engine_contract_tests.exe --artifacts build/my-import-run
```

An optional `--legacy-root C:/Stalker2_2011/xr2-dsgn/resources` adds a read-only
animaxis skin/skeleton plus base/additive clip sample. Its outputs remain under
the chosen artifact folder; the originals are not rewritten. This sample passed
60 frames per clip with pose error below 0.000003 and engine playback checks.
All 346 original check-data files also passed the new neutral Python reader.
The dated full-corpus outcomes, including source failures, are recorded in the
[maintainer runbook](MAINTAINER.md); this import test exercises generated fixtures.
