# xray-dcc-exchange/v2

The bundle layout, binary spans, asset kinds and existing safety limits are
inherited from [v1](../maya/exchange/SPEC.md). Both CLI entry points read v1 and
v2. The Python entry points are `DccExchangeBundleWriter` (explicit v2) and
`ExchangeBundleWriter` (the unchanged v1 default). `validate_bundle` reads both.

## Header

```json
{
  "schema": "xray-dcc-exchange/v2",
  "asset_kind": "animation",
  "name": "character/walk",
  "source_dcc": "blender",
  "coord_space": {
    "system": "blender_right_handed_z_up",
    "unit_scale_to_meters": 1.0,
    "uv_origin": "bottom_left",
    "front_face_winding": "ccw"
  }
}
```

`source_dcc` is required: `blender`, `maya`, `legacy_clip`, `legacy_ma`,
`legacy_mb`, or `legacy_asset` (decompiled model/skeleton/physics). It records
provenance and does not select coordinate conversion.
Migrated Blender armatures may therefore declare Y-up, as required by the
`Y_UP_RH` armature convention in the pipeline plan.

Supported coordinate systems:

- `right_handed_y_up`: canonical name for the existing Maya Y-up convention.
- `maya_right_handed_y_up`: exact alias of `right_handed_y_up` in v2; the v1 name
  and arithmetic remain supported.
- `blender_right_handed_z_up`: Blender-native XYZ transforms and Z-up geometry.
- `xray_left_handed_y_up`: already converted engine coordinates.

For right-handed input the winding must be `ccw` or its legacy alias `ccw_maya`.
Engine-space input must use `cw_xray`. Inconsistent declarations are rejected.
`uv_origin` independently accepts `bottom_left` (flip V) or `top_left` (keep V).
The unit scale must remain positive and finite after conversion to float32.
The new names are rejected in a v1 bundle, preventing accidental mislabelling.

## Coordinate and frame conversion

All conversion is in C++ `coordinate_transform.cpp`; Python writes source data.

For Y-up input, the existing formulas are preserved exactly:

- Position: `(x, y, -z) * unit_scale_to_meters`.
- Direction: `(x, y, -z)`.
- XYZ rotation channels: `(rx, ry, -rz)`.
- Scale axes remain `(sx, sy, sz)`.

For Blender-native input, the basis map is `C(x,y,z) = (x,z,y)`:

- Position: `(x, z, y) * unit_scale_to_meters`.
- Direction: `(x, z, y)`.
- Dimensionless TRS scale: `(sx, sz, sy)`.
- Rotation: construct the full source RH XYZ matrix, conjugate by `C`, then
  decompose into the engine's LH XYZ Euler representation. Swapping Euler
  channel values is incorrect for compound rotations because their order changes.

In column-vector notation the rotation is `C * R_source * inverse(C)`. With
engine row-vector matrices it is `inverse(C) * R_source * C`. Both maps here
are their own inverse. Engine matrices are reconstructed by the real
`xray::math::create_rotation` contract; Blender's independent `mathutils` supplies
the source matrix in the host contract. No fixed armature object rotation or
bone `Rcorr` is baked into this coordinate conversion. Those belong to the B3/B4
frontend's armature and joint-frame conventions.

Both RH maps have negative determinant, so triangle winding is reversed once.
Positions, normals, tangents, binormals, collision vertices, locators, bind-pose
TRS, sampled animation TRS, physics bodies, mass centers, joints and explicit
collision primitives use this common map. Physics `scale` fields represent
lengths and additionally multiply by the unit scale. Bind/animation scale is
dimensionless and does not multiply by the unit scale. A native Z-up cylinder's
long axis is source Z; in Y-up and engine space the long axis is Y.

Physics DOF dictionaries retain the legacy **engine joint-local parameter**
convention: their axis names, enable flags and scalar limits are copied. They
are not raw Blender/Maya transform channels. Frontends must author them using
that convention, rather than expecting the geometry coordinate map to reinterpret
joint limits.

For articulated physics shells, `joints[0]` is the reserved unlinked root-body
metadata slot (`type: rigid`); the engine constructs constraints starting at
index 1. Put the free root body first and retain all linked joints after that
slot. Linked authoring `rigid` joints use the engine `fixed` type. Blender
validates that there is one free root and orders it explicitly, so alphabetical
helper names cannot cause the engine to skip a limb joint.

`--fit-collision` v1 keeps its existing `maya` and `xray` result fields. For v2 the
fields are `source` and `xray`: `source` is obtained by the inverse coordinate,
rotation and dimensional-scale maps, in the declared source units. It can be
written back as an explicit primitive and cooked again. Point fitting itself
always runs on engine-space meter coordinates. The legacy C++ API taking
`input_is_maya_right_handed` is preserved.

## Animation samples

The existing nine float32 sample streams remain authoritative for cooking.
Rotation streams are XYZ-decomposed TRS in the declared coordinate space,
regardless of the original authoring rotation order in optional curves.
Blender-to-engine Euler output is unwrapped continuously before B-spline fitting,
including when the source marks root motion as already relative.

`root_already_relative` defaults to false for RH source input and true for
engine-space input, preserving v1 behavior and extending it to Blender. False
removes the first pose from the `object_movement` track. Explicit true preserves
the root samples. `fps` retains its v1 meaning; engine spline time remains in
30-fps units.

Optional `frame_start` is a finite source-scene frame number (default 0), for
reconstructing authoring time. It is valid only on animations and does not shift
the engine clip's local time.

## Optional editable curves

This animation-only metadata preserves authoring keys for future DCC import.
Both validators check it; the cooker uses only sampled tracks and does not fit
or evaluate these curves. Importers must not substitute the curves for the
archived validation samples without verifying the resulting poses.

```json
{
  "curves": {
    "time_unit": "frames",
    "tracks": [{
      "bone_name": "Hips",
      "rotation_order": "XYZ",
      "channels": {
        "rx": {
          "pre_infinity": "CONSTANT",
          "post_infinity": "CYCLE",
          "keys": [{
            "time": 1.0,
            "value": 0.25,
            "interpolation": "BEZIER",
            "handle_left": [0.5, 0.2],
            "handle_right": [1.5, 0.3]
          }]
        }
      }
    }]
  }
}
```

- Times and handle X values are source frames at the animation's `fps`.
  Translation values use source units; rotation values are radians; scale is
  dimensionless. Curve values are source authoring channels, before joint-frame
  correction, and may require exact remapping or fitting during import.
- Tracks must refer to distinct bones that exist in sampled animation tracks.
  There are at most 80 curve tracks and 1,000,000 keys in total.
- `rotation_order` is required per track and accepts `XYZ`, `XZY`, `YXZ`, `YZX`,
  `ZXY`, or `ZYX`.
- Channels are any non-empty subset of `tx, ty, tz, rx, ry, rz, sx, sy, sz`.
- Keys are non-empty and strictly increasing in time, including subframes and
  negative frames. Times, values and handles must be finite numbers, not bools.
- Interpolation is `CONSTANT`, `LINEAR`, or `BEZIER` (for the segment after a key).
  BEZIER requires both explicit `[time,value]` handles. An incoming handle must
  be at or before its key, an outgoing handle at or after it. Stored handle
  positions can be restored with Blender FREE handles without automatic edits.
- `pre_infinity` and `post_infinity` independently accept `CONSTANT`, `LINEAR`,
  `CYCLE`, `CYCLE_RELATIVE`, or `OSCILLATE`; both default to `CONSTANT`.

Both frontends now emit v2. B4 adds neutral legacy decompile, basic MA reading and
Blender exact/fitted key transfer. Optional import metadata (`root`, `bind_pose`,
`authoring`, `import_metadata`) carries hierarchy/bind frames and fitting seeds;
the importer validates the fields it uses. Cookers still use only sampled tracks.
Corpus migration/reconciliation remains B5. See [the import guide](../blender/IMPORT.md).
