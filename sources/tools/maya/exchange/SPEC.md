# `xray-maya-exchange/v1` Specification

Cross-platform exchange bundle format decoupling Maya (Windows/Linux Python API 2.0) from the C++26 X-Ray asset cooker (`xray_maya_cook`).

## Bundle Layout

An exchange bundle is a directory containing:

1. `manifest.json` — UTF-8 JSON document (`schema = "xray-maya-exchange/v1"`).
2. `payload.bin` — raw little-endian IEEE-754 / integer binary streams referenced by byte spans.

All virtual paths and surface/locator/bone names use UTF-8 forward slashes (`/`) without `..`, drive letters, or control characters.

## Coordinate Conventions (`coord_space`)

`manifest.json` explicitly declares the input coordinate frame so `xray_maya_cook` applies deterministic conversions in C++:

```json
{
  "coord_space": {
    "system": "maya_right_handed_y_up",
    "unit_scale_to_meters": 0.01,
    "uv_origin": "bottom_left",
    "front_face_winding": "ccw_maya"
  }
}
```

- When cooking from `maya_right_handed_y_up`:
  - Positions: `(x, y, -z) * unit_scale_to_meters`
  - Normals: normalized `(nx, ny, -nz)`
  - Triangle winding (`ccw_maya`): reorder indices `(i0, i1, i2) -> (i0, i2, i1)`
  - UVs (`bottom_left`): `(u, 1.0f - v)`
  - Rigid transforms/rotations: negate Z translation and Z rotation component (matching `xray::maya::translation_maya_to_xray` / `rotation_maya_to_xray`).
- When cooking from `xray_left_handed_y_up` with `uv_origin: "top_left"` and `front_face_winding: "cw_xray"`:
  - Data is already in X-Ray left-handed meter space (`unit_scale_to_meters` still scales positions if `!= 1.0`).

## Span Objects & Alignments

Every binary stream reference inside `manifest.json` has the form:

```json
{ "offset": 0, "bytes": 7140, "count": 595, "format": "f32x3" }
```

- `offset` MUST be a multiple of 4 (`offset % 4 == 0`) and `offset + bytes <= len(payload.bin)`.
- `bytes == count * element_size(format)`.
- Supported `format` values (little-endian):
  - `f32x3` (12 bytes), `f32x2` (8 bytes), `f32x4` (16 bytes), `f32` (4 bytes)
  - `u32` (4 bytes), `u16` (2 bytes, total span padded to 4-byte boundary for the next span), `u16x4` (8 bytes)

## Supported `asset_kind` Values

- `"static_model"`: `surfaces[]` (1..128), optional `collision` / `hq_collision`, optional `locators[]`, optional `rigid_body` / `options`.
- `"skinned_model"`: `skeleton_name`, `bones[]` (`name`, `bind_translation`, `bind_rotation`, `bind_scale`), `surfaces[]` (with `bone_indices` `u16x4` + `bone_weights` `f32x4`), optional `locators[]`, optional `rigid_body`.
- `"skeleton"`: hierarchical `root` bone node (`{ "name": "object_movement", "children": [...] }`).
- `"animation"`: `fps`, `is_additive`, `precision_t`, `precision_r`, `precision_s`, and per-bone sampled or bi-spline frames.
- `"physics"`: `bodies[]` (`name`, `anim_joint`, `mass`, `mass_center`, `position`, `rotation`, `scale`, `collision[{type, position/rotation/scale or points}]`), `joints[]` (`name`, `type`, optional `body0`/`body1`, `position`, `rotation`, `scale`, 6 DOF dicts `x_translate..z_rotate`), and/or `fit_requests[]` (`name`, `shape`, `points` `f32x3` span for C++ WildMagic4 OBB/sphere/cylinder fitting).

## Bounds & Safety Limits

- `payload.bin` max size: `64 MiB` (`67,108,864` bytes).
- Max surfaces per model: `128`; max bones per skinned model: `80`; max locators: `256`.
- All float values in JSON and `payload.bin` MUST be finite (`isfinite`).
- Every triangle index MUST satisfy `index < vertex_count`, and `index_count % 3 == 0` with `index_count > 0`.
