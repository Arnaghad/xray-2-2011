"""DCC-independent exchange v1/v2 writers and strict bundle validation."""

from __future__ import annotations

import json
import math
import struct
from pathlib import Path
from typing import Any

SCHEMA_ID = "xray-maya-exchange/v1"
SCHEMA_V2_ID = "xray-dcc-exchange/v2"
SOURCE_DCCS = frozenset({"blender", "maya", "legacy_clip", "legacy_ma", "legacy_mb", "legacy_asset"})
MAX_PAYLOAD_BYTES = 64 * 1024 * 1024
MAX_SURFACES = 128
MAX_BONES = 128
MAX_LOCATORS = 256
MAX_ANIM_FRAMES = 1_000_000
MAX_BODIES = 256
MAX_JOINTS = 512
ANIMATION_CHANNELS: tuple[str, ...] = ("tx", "ty", "tz", "rx", "ry", "rz", "sx", "sy", "sz")
PHYSICS_SHAPES = frozenset({"box", "cylinder", "sphere"})
JOINT_TYPES = frozenset({"fixed", "ball", "generic", "wheel", "slider", "rigid"})
JOINT_DOF_NAMES: tuple[str, ...] = (
    "x_translate",
    "y_translate",
    "z_translate",
    "x_rotate",
    "y_rotate",
    "z_rotate",
)

ASSET_KINDS = frozenset(
    {"static_model", "skinned_model", "skeleton", "animation", "physics"}
)
COORD_SYSTEMS = frozenset({"maya_right_handed_y_up", "xray_left_handed_y_up"})
UV_ORIGINS = frozenset({"bottom_left", "top_left"})
WINDINGS = frozenset({"ccw_maya", "cw_xray"})
V2_COORD_SYSTEMS = COORD_SYSTEMS | {"right_handed_y_up", "blender_right_handed_z_up"}
V2_WINDINGS = WINDINGS | {"ccw"}
ROTATION_ORDERS = frozenset({"XYZ", "XZY", "YXZ", "YZX", "ZXY", "ZYX"})
CURVE_INTERPOLATIONS = frozenset({"CONSTANT", "LINEAR", "BEZIER"})
CURVE_INFINITY = frozenset({"CONSTANT", "LINEAR", "CYCLE", "CYCLE_RELATIVE", "OSCILLATE"})

STREAM_FORMATS: dict[str, tuple[str, int]] = {
    "f32": ("f", 4),
    "f32x2": ("2f", 8),
    "f32x3": ("3f", 12),
    "f32x4": ("4f", 16),
    "u16": ("H", 2),
    "u16x4": ("4H", 8),
    "u32": ("I", 4),
}


class ExchangeValidationError(ValueError):
    """Raised when an exchange bundle violates schema or safety bounds."""


def _require(cond: bool, msg: str) -> None:
    if not cond:
        raise ExchangeValidationError(msg)


def _check_finite_number(val: Any, label: str) -> float:
    _require(
        isinstance(val, (int, float)) and not isinstance(val, bool),
        f"{label} must be numeric",
    )
    f = float(val)
    _require(math.isfinite(f), f"{label} must be finite")
    return f


def _check_vec3(val: Any, label: str) -> list[float]:
    _require(isinstance(val, list) and len(val) == 3, f"{label} must be a 3-element list")
    return [_check_finite_number(v, f"{label}[{i}]") for i, v in enumerate(val)]


def validate_identifier(name: Any, label: str, *, allow_slash: bool = False, max_len: int = 128) -> str:
    _require(isinstance(name, str) and len(name) > 0, f"{label} must be a non-empty string")
    _require(len(name) <= max_len, f"{label} exceeds {max_len} chars")
    _require("\x00" not in name and "\\" not in name and ":" not in name, f"{label} contains forbidden characters")
    _require(not name.startswith("/"), f"{label} must be relative")
    parts = name.split("/") if allow_slash else [name]
    if not allow_slash:
        _require("/" not in name, f"{label} must be a single path segment")
    for part in parts:
        _require(part not in ("", ".", ".."), f"{label} contains invalid segment '{part}'")
    return name


def unpack_span(span: Any, payload: memoryview, expected_format: str | None, expected_count: int | None, label: str) -> list[Any]:
    _require(isinstance(span, dict), f"{label} must be an object")
    fmt = span.get("format")
    _require(fmt in STREAM_FORMATS, f"{label}.format '{fmt}' is unsupported")
    if expected_format is not None:
        _require(fmt == expected_format, f"{label}.format expected '{expected_format}', got '{fmt}'")
    offset = span.get("offset")
    byte_len = span.get("bytes")
    count = span.get("count")
    _require(
        isinstance(offset, int) and not isinstance(offset, bool) and offset >= 0 and offset % 4 == 0,
        f"{label}.offset must be a non-negative 4-byte-aligned integer",
    )
    _require(
        isinstance(count, int) and not isinstance(count, bool) and count >= 0,
        f"{label}.count must be a non-negative integer",
    )
    _require(
        isinstance(byte_len, int) and not isinstance(byte_len, bool) and byte_len >= 0,
        f"{label}.bytes must be a non-negative integer",
    )
    if expected_count is not None:
        _require(count == expected_count, f"{label}.count expected {expected_count}, got {count}")
    struct_code, elem_size = STREAM_FORMATS[fmt]
    _require(byte_len == count * elem_size, f"{label}.bytes ({byte_len}) != count * {elem_size}")
    _require(offset + byte_len <= len(payload), f"{label} span exceeds payload.bin bounds")

    raw = payload[offset : offset + byte_len]
    values = list(struct.iter_unpack("<" + struct_code, raw))
    if struct_code.endswith("f"):
        for idx, tup in enumerate(values):
            for c_idx, fv in enumerate(tup):
                _require(math.isfinite(fv), f"{label}[{idx}][{c_idx}] is non-finite")
    return [v[0] if len(v) == 1 else v for v in values]


def _validate_coord_space(cs: Any, *, v2: bool = False) -> None:
    _require(isinstance(cs, dict), "coord_space must be an object")
    systems = V2_COORD_SYSTEMS if v2 else COORD_SYSTEMS
    _require(isinstance(cs.get("system"), str) and cs["system"] in systems, f"invalid coord_space.system: {cs.get('system')}")
    scale = _check_finite_number(cs.get("unit_scale_to_meters"), "coord_space.unit_scale_to_meters")
    _require(scale > 0.0, "coord_space.unit_scale_to_meters must be > 0")
    origin = cs.get("uv_origin")
    _require(isinstance(origin, str) and origin in UV_ORIGINS, f"invalid coord_space.uv_origin: {origin}")
    windings = V2_WINDINGS if v2 else WINDINGS
    winding = cs.get("front_face_winding")
    _require(isinstance(winding, str) and winding in windings, f"invalid coord_space.front_face_winding: {winding}")
    if v2:
        try:
            scale32 = struct.unpack("<f", struct.pack("<f", scale))[0]
        except OverflowError:
            raise ExchangeValidationError("coord_space.unit_scale_to_meters must fit positive finite float32") from None
        _require(math.isfinite(scale32) and scale32 > 0.0,
                 "coord_space.unit_scale_to_meters must fit positive finite float32")
        expected = {"cw_xray"} if cs["system"] == "xray_left_handed_y_up" else {"ccw", "ccw_maya"}
        _require(winding in expected, "coord_space.front_face_winding disagrees with coordinate system")


def _validate_curves(curves: Any, sampled_bones: set[str]) -> None:
    _require(isinstance(curves, dict), "curves must be an object")
    _require(curves.get("time_unit") == "frames", "curves.time_unit must be 'frames'")
    tracks = curves.get("tracks")
    _require(isinstance(tracks, list) and len(tracks) <= MAX_BONES, f"curves.tracks must be a list of <= {MAX_BONES}")
    seen: set[str] = set()
    total_keys = 0
    for ti, track in enumerate(tracks):
        label = f"curves.tracks[{ti}]"
        _require(isinstance(track, dict), f"{label} must be an object")
        bone = validate_identifier(track.get("bone_name"), f"{label}.bone_name", max_len=127)
        _require(bone in sampled_bones, f"{label}.bone_name has no sampled track")
        _require(bone not in seen, f"duplicate curves bone '{bone}'")
        seen.add(bone)
        order = track.get("rotation_order")
        _require(isinstance(order, str) and order in ROTATION_ORDERS, f"{label}.rotation_order invalid")
        channels = track.get("channels")
        _require(isinstance(channels, dict) and 1 <= len(channels) <= 9, f"{label}.channels must contain 1..9 channels")
        for name, curve in channels.items():
            cl = f"{label}.channels.{name}"
            _require(name in ANIMATION_CHANNELS, f"{cl} is unsupported")
            _require(isinstance(curve, dict), f"{cl} must be an object")
            for infinity in ("pre_infinity", "post_infinity"):
                value = curve.get(infinity, "CONSTANT")
                _require(isinstance(value, str) and value in CURVE_INFINITY, f"{cl}.{infinity} invalid")
            keys = curve.get("keys")
            _require(isinstance(keys, list) and len(keys) > 0, f"{cl}.keys must be a non-empty list")
            total_keys += len(keys)
            _require(total_keys <= MAX_ANIM_FRAMES, f"curves exceeds {MAX_ANIM_FRAMES} total keys")
            previous = -math.inf
            for ki, key in enumerate(keys):
                kl = f"{cl}.keys[{ki}]"
                _require(isinstance(key, dict), f"{kl} must be an object")
                time = _check_finite_number(key.get("time"), f"{kl}.time")
                _require(time > previous, f"{cl}.keys times must strictly increase")
                previous = time
                _check_finite_number(key.get("value"), f"{kl}.value")
                interp = key.get("interpolation")
                _require(isinstance(interp, str) and interp in CURVE_INTERPOLATIONS, f"{kl}.interpolation invalid")
                for handle in ("handle_left", "handle_right"):
                    if interp == "BEZIER" or handle in key:
                        values = key.get(handle)
                        _require(isinstance(values, list) and len(values) == 2, f"{kl}.{handle} must be [time, value]")
                        for i, value in enumerate(values):
                            _check_finite_number(value, f"{kl}.{handle}[{i}]")
                        _require(values[0] <= time if handle == "handle_left" else values[0] >= time,
                                 f"{kl}.{handle} is on the wrong side of the key")


def _validate_collision_mesh(col: Any, payload: memoryview, label: str) -> None:
    _require(isinstance(col, dict), f"{label} must be an object")
    v_cnt = col.get("vertex_count")
    i_cnt = col.get("index_count")
    _require(isinstance(v_cnt, int) and not isinstance(v_cnt, bool) and 3 <= v_cnt <= 2_000_000, f"{label}.vertex_count invalid")
    _require(isinstance(i_cnt, int) and not isinstance(i_cnt, bool) and i_cnt >= 3 and i_cnt % 3 == 0, f"{label}.index_count invalid")
    unpack_span(col.get("positions"), payload, "f32x3", v_cnt, f"{label}.positions")
    indices = unpack_span(col.get("indices"), payload, "u32", i_cnt, f"{label}.indices")
    for idx in indices:
        _require(idx < v_cnt, f"{label}.indices contains out-of-range index {idx} >= {v_cnt}")


def _validate_locators(locators: Any, bone_count: int | None) -> None:
    if locators is None:
        return
    _require(isinstance(locators, list), "locators must be a list")
    _require(len(locators) <= MAX_LOCATORS, f"locators exceed {MAX_LOCATORS}")
    seen: set[str] = set()
    for i, loc in enumerate(locators):
        _require(isinstance(loc, dict), f"locators[{i}] must be an object")
        name = validate_identifier(loc.get("name"), f"locators[{i}].name", max_len=31)
        _require(name not in seen, f"duplicate locator name '{name}'")
        seen.add(name)
        bone_idx = loc.get("bone_idx", -1)
        _require(isinstance(bone_idx, int) and not isinstance(bone_idx, bool) and bone_idx >= -1, f"locators[{i}].bone_idx invalid")
        if bone_count is not None and bone_idx >= 0:
            _require(bone_idx < bone_count, f"locators[{i}].bone_idx out of range")
        _check_vec3(loc.get("position"), f"locators[{i}].position")
        _check_vec3(loc.get("rotation"), f"locators[{i}].rotation")


def _validate_surface(surf: Any, payload: memoryview, skinned: bool, bone_count: int, idx: int) -> str:
    prefix = f"surfaces[{idx}]"
    _require(isinstance(surf, dict), f"{prefix} must be an object")
    name = validate_identifier(surf.get("name"), f"{prefix}.name", max_len=128)
    v_cnt = surf.get("vertex_count")
    i_cnt = surf.get("index_count")
    _require(isinstance(v_cnt, int) and not isinstance(v_cnt, bool) and 3 <= v_cnt <= 2_000_000, f"{prefix}.vertex_count invalid")
    _require(isinstance(i_cnt, int) and not isinstance(i_cnt, bool) and i_cnt >= 3 and i_cnt % 3 == 0, f"{prefix}.index_count invalid")

    streams = surf.get("streams")
    _require(isinstance(streams, dict), f"{prefix}.streams must be an object")
    unpack_span(streams.get("positions"), payload, "f32x3", v_cnt, f"{prefix}.streams.positions")
    unpack_span(streams.get("normals"), payload, "f32x3", v_cnt, f"{prefix}.streams.normals")
    unpack_span(streams.get("uvs"), payload, "f32x2", v_cnt, f"{prefix}.streams.uvs")

    idx_span = streams.get("indices")
    _require(isinstance(idx_span, dict) and idx_span.get("format") in ("u16", "u32"), f"{prefix}.streams.indices must be u16 or u32")
    indices = unpack_span(idx_span, payload, None, i_cnt, f"{prefix}.streams.indices")
    for tri_idx in indices:
        _require(tri_idx < v_cnt, f"{prefix} triangle index {tri_idx} >= vertex_count {v_cnt}")

    if "tangents" in streams:
        unpack_span(streams["tangents"], payload, "f32x3", v_cnt, f"{prefix}.streams.tangents")
    if "binormals" in streams:
        unpack_span(streams["binormals"], payload, "f32x3", v_cnt, f"{prefix}.streams.binormals")

    if skinned:
        b_idx = unpack_span(streams.get("bone_indices"), payload, "u16x4", v_cnt, f"{prefix}.streams.bone_indices")
        b_w = unpack_span(streams.get("bone_weights"), payload, "f32x4", v_cnt, f"{prefix}.streams.bone_weights")
        for v_i, (inds, weights) in enumerate(zip(b_idx, b_w)):
            w_sum = 0.0
            for slot in range(4):
                bi = inds[slot]
                w = weights[slot]
                _require(bi == 0xFFFF or bi < bone_count, f"{prefix} vertex {v_i} bone {bi} >= {bone_count}")
                _require(0.0 <= w <= 1.0001, f"{prefix} vertex {v_i} weight {w} out of range")
                w_sum += w
            _require(abs(w_sum - 1.0) <= 1e-3, f"{prefix} vertex {v_i} weight sum {w_sum} != 1")
    return name


def _validate_skeleton_node(node: Any, seen: set[str], label: str) -> None:
    _require(isinstance(node, dict), f"{label} must be an object")
    name = validate_identifier(node.get("name"), f"{label}.name", max_len=127)
    _require(name not in seen, f"duplicate skeleton bone '{name}'")
    seen.add(name)
    children = node.get("children", [])
    _require(isinstance(children, list), f"{label}.children must be a list")
    for ci, child in enumerate(children):
        _validate_skeleton_node(child, seen, f"{label}.children[{ci}]")


def validate_manifest_and_payload(manifest: Any, payload_bytes: bytes) -> dict[str, Any]:
    _require(isinstance(manifest, dict), "manifest.json root must be a JSON object")
    schema = manifest.get("schema")
    _require(schema in (SCHEMA_ID, SCHEMA_V2_ID), f"unsupported exchange schema '{schema}'")
    v2 = schema == SCHEMA_V2_ID
    if v2:
        source = manifest.get("source_dcc")
        _require(isinstance(source, str) and source in SOURCE_DCCS, "source_dcc must identify a supported DCC or legacy route")
    _require(len(payload_bytes) <= MAX_PAYLOAD_BYTES, f"payload.bin exceeds {MAX_PAYLOAD_BYTES} bytes")

    kind = manifest.get("asset_kind")
    _require(kind in ASSET_KINDS, f"unsupported asset_kind '{kind}'")
    validate_identifier(manifest.get("name"), "name", allow_slash=True, max_len=220)
    _validate_coord_space(manifest.get("coord_space"), v2=v2)
    if v2:
        if "frame_start" in manifest:
            _require(kind == "animation", "frame_start is only valid for animation")
            _check_finite_number(manifest["frame_start"], "frame_start")
        if "root_already_relative" in manifest:
            _require(isinstance(manifest["root_already_relative"], bool), "root_already_relative must be boolean")
        if "curves" in manifest:
            _require(kind == "animation", "curves are only valid for animation")

    payload = memoryview(payload_bytes)
    bone_count: int | None = None

    if kind in ("static_model", "skinned_model"):
        skinned = kind == "skinned_model"
        if skinned:
            validate_identifier(manifest.get("skeleton_name"), "skeleton_name", allow_slash=True, max_len=220)
            bones = manifest.get("bones")
            _require(isinstance(bones, list) and 1 <= len(bones) <= MAX_BONES, f"skinned_model requires 1..{MAX_BONES} bones")
            bone_names: set[str] = set()
            for bi, bone in enumerate(bones):
                _require(isinstance(bone, dict), f"bones[{bi}] must be an object")
                bname = validate_identifier(bone.get("name"), f"bones[{bi}].name", max_len=127)
                _require(bname not in bone_names, f"duplicate bone '{bname}'")
                bone_names.add(bname)
                _check_vec3(bone.get("bind_translation"), f"bones[{bi}].bind_translation")
                _check_vec3(bone.get("bind_rotation"), f"bones[{bi}].bind_rotation")
                bscale = _check_vec3(bone.get("bind_scale"), f"bones[{bi}].bind_scale")
                _require(all(s > 0.0 for s in bscale), f"bones[{bi}].bind_scale must be positive")
            bone_count = len(bones)

        surfaces = manifest.get("surfaces")
        _require(isinstance(surfaces, list) and 1 <= len(surfaces) <= MAX_SURFACES, f"surfaces must contain 1..{MAX_SURFACES} entries")
        surf_names: set[str] = set()
        for si, surf in enumerate(surfaces):
            sname = _validate_surface(surf, payload, skinned, bone_count or 0, si)
            _require(sname not in surf_names, f"duplicate surface name '{sname}'")
            surf_names.add(sname)

        if manifest.get("collision") is not None:
            _validate_collision_mesh(manifest["collision"], payload, "collision")
        if manifest.get("hq_collision") is not None:
            _validate_collision_mesh(manifest["hq_collision"], payload, "hq_collision")
        _validate_locators(manifest.get("locators"), bone_count)

    elif kind == "skeleton":
        _validate_skeleton_node(manifest.get("root"), set(), "root")

    elif kind == "animation":
        fps = _check_finite_number(manifest.get("fps", 30.0), "fps")
        _require(fps > 0.0, "fps must be > 0")
        frame_count = manifest.get("frame_count")
        _require(
            isinstance(frame_count, int) and not isinstance(frame_count, bool) and 1 <= frame_count <= MAX_ANIM_FRAMES,
            f"animation.frame_count must be int in 1..{MAX_ANIM_FRAMES}",
        )
        is_additive = manifest.get("is_additive", False)
        _require(isinstance(is_additive, bool), "animation.is_additive must be boolean")
        for pname in ("precision_t", "precision_r", "precision_s"):
            if pname in manifest:
                pval = _check_finite_number(manifest[pname], pname)
                _require(pval > 0.0, f"{pname} must be > 0")
        tracks = manifest.get("tracks")
        _require(isinstance(tracks, list) and 1 <= len(tracks) <= MAX_BONES, f"animation.tracks must contain 1..{MAX_BONES} bones")
        seen_bones: set[str] = set()
        for ti, tr in enumerate(tracks):
            _require(isinstance(tr, dict), f"tracks[{ti}] must be an object")
            bname = validate_identifier(tr.get("bone_name"), f"tracks[{ti}].bone_name", max_len=127)
            _require(bname not in seen_bones, f"duplicate animation bone '{bname}'")
            seen_bones.add(bname)
            channels = tr.get("channels")
            _require(isinstance(channels, dict), f"tracks[{ti}].channels must be an object")
            for ch in ANIMATION_CHANNELS:
                unpack_span(channels.get(ch), payload, "f32", frame_count, f"tracks[{ti}].channels.{ch}")

    elif kind == "physics":
        _require(
            any(k in manifest for k in ("bodies", "joints", "collision_primitives", "fit_requests")),
            "physics manifest must provide bodies, joints, collision_primitives, or fit_requests",
        )
        bodies = manifest.get("bodies")
        if bodies is not None:
            _require(isinstance(bodies, list) and len(bodies) <= MAX_BODIES, f"physics.bodies must be list of <= {MAX_BODIES}")
            seen_bodies: set[str] = set()
            for bi, b in enumerate(bodies):
                _require(isinstance(b, dict), f"bodies[{bi}] must be an object")
                bname = validate_identifier(b.get("name"), f"bodies[{bi}].name", max_len=128)
                _require(bname not in seen_bodies, f"duplicate physics body '{bname}'")
                seen_bodies.add(bname)
                aj = b.get("anim_joint", "")
                _require(isinstance(aj, str) and len(aj) <= 128, f"bodies[{bi}].anim_joint must be string <= 128 chars")
                mass = _check_finite_number(b.get("mass"), f"bodies[{bi}].mass")
                _require(mass >= 0.0, f"bodies[{bi}].mass must be >= 0")
                _check_vec3(b.get("mass_center"), f"bodies[{bi}].mass_center")
                _check_vec3(b.get("position"), f"bodies[{bi}].position")
                _check_vec3(b.get("rotation"), f"bodies[{bi}].rotation")
                _check_vec3(b.get("scale"), f"bodies[{bi}].scale")
                cols = b.get("collision")
                _require(isinstance(cols, list) and len(cols) <= MAX_BODIES, f"bodies[{bi}].collision must be list")
                for ci, c in enumerate(cols):
                    _require(isinstance(c, dict), f"bodies[{bi}].collision[{ci}] must be object")
                    _require(c.get("type") in PHYSICS_SHAPES, f"bodies[{bi}].collision[{ci}].type invalid")
                    if "points" in c:
                        span = c["points"]
                        _require(isinstance(span, dict) and span.get("count", 0) >= 1, f"bodies[{bi}].collision[{ci}].points count >= 1")
                        unpack_span(span, payload, "f32x3", span.get("count"), f"bodies[{bi}].collision[{ci}].points")
                    else:
                        _check_vec3(c.get("position"), f"bodies[{bi}].collision[{ci}].position")
                        _check_vec3(c.get("rotation"), f"bodies[{bi}].collision[{ci}].rotation")
                        _check_vec3(c.get("scale"), f"bodies[{bi}].collision[{ci}].scale")

        joints = manifest.get("joints")
        if joints is not None:
            _require(isinstance(joints, list) and len(joints) <= MAX_JOINTS, f"physics.joints must be list of <= {MAX_JOINTS}")
            for ji, j in enumerate(joints):
                _require(isinstance(j, dict), f"joints[{ji}] must be object")
                validate_identifier(j.get("name"), f"joints[{ji}].name", max_len=128)
                _require(j.get("type") in JOINT_TYPES, f"joints[{ji}].type invalid")
                for key in ("body0", "body1"):
                    if key in j and j[key] is not None and j[key] != "":
                        validate_identifier(j[key], f"joints[{ji}].{key}", max_len=128)
                _check_vec3(j.get("position"), f"joints[{ji}].position")
                _check_vec3(j.get("rotation"), f"joints[{ji}].rotation")
                _check_vec3(j.get("scale"), f"joints[{ji}].scale")
                for dof_name in JOINT_DOF_NAMES:
                    dof = j.get(dof_name)
                    _require(isinstance(dof, dict), f"joints[{ji}].{dof_name} must be object")
                    _require(isinstance(dof.get("enable_state"), bool), f"joints[{ji}].{dof_name}.enable_state must be bool")
                    _require(isinstance(dof.get("limits_enable_state"), bool), f"joints[{ji}].{dof_name}.limits_enable_state must be bool")
                    _check_finite_number(dof.get("low_limit"), f"joints[{ji}].{dof_name}.low_limit")
                    _check_finite_number(dof.get("hi_limit"), f"joints[{ji}].{dof_name}.hi_limit")

        fit_reqs = manifest.get("fit_requests")
        if fit_reqs is not None:
            _require(isinstance(fit_reqs, list) and 1 <= len(fit_reqs) <= MAX_BODIES, "physics.fit_requests must be non-empty list")
            for ri, r in enumerate(fit_reqs):
                _require(isinstance(r, dict), f"fit_requests[{ri}] must be object")
                validate_identifier(r.get("name"), f"fit_requests[{ri}].name", max_len=128)
                _require(r.get("shape") in PHYSICS_SHAPES, f"fit_requests[{ri}].shape invalid")
                span = r.get("points")
                _require(isinstance(span, dict) and span.get("count", 0) >= 1, f"fit_requests[{ri}].points count >= 1")
                unpack_span(span, payload, "f32x3", span.get("count"), f"fit_requests[{ri}].points")

    if v2 and "curves" in manifest:
        _validate_curves(manifest["curves"], {track["bone_name"] for track in manifest["tracks"]})
    return manifest


def validate_bundle(bundle_dir: str | Path) -> dict[str, Any]:
    root = Path(bundle_dir)
    manifest_path = root / "manifest.json"
    payload_path = root / "payload.bin"
    _require(manifest_path.is_file(), f"Missing manifest.json in '{root}'")
    _require(payload_path.is_file(), f"Missing payload.bin in '{root}'")
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    payload_bytes = payload_path.read_bytes()
    return validate_manifest_and_payload(manifest, payload_bytes)


class ExchangeBundleWriter:
    """Builds a valid, 4-byte-aligned xray-maya-exchange/v1 directory bundle."""

    def __init__(self, asset_kind: str, name: str, coord_space: dict[str, Any], options: dict[str, Any] | None = None) -> None:
        self._buf = bytearray()
        self.manifest: dict[str, Any] = {
            "schema": SCHEMA_ID,
            "asset_kind": asset_kind,
            "name": name,
            "coord_space": coord_space,
            "options": options or {},
        }

    def append_span(self, fmt: str, items: list[Any]) -> dict[str, Any]:
        struct_code, _ = STREAM_FORMATS[fmt]
        while len(self._buf) % 4 != 0:
            self._buf.append(0)
        offset = len(self._buf)
        packer = struct.Struct("<" + struct_code)
        for item in items:
            if len(struct_code) == 1:
                self._buf.extend(packer.pack(item))
            else:
                self._buf.extend(packer.pack(*item))
        byte_len = len(self._buf) - offset
        while len(self._buf) % 4 != 0:
            self._buf.append(0)
        return {"offset": offset, "bytes": byte_len, "count": len(items), "format": fmt}

    def write(self, out_dir: str | Path, *, validate: bool = True) -> Path:
        root = Path(out_dir)
        root.mkdir(parents=True, exist_ok=True)
        payload = bytes(self._buf)
        if validate:
            validate_manifest_and_payload(self.manifest, payload)
        (root / "manifest.json").write_text(json.dumps(self.manifest, indent=2) + "\n", encoding="utf-8")
        (root / "payload.bin").write_bytes(payload)
        return root


class DccExchangeBundleWriter(ExchangeBundleWriter):
    """Write v2 explicitly; the legacy writer keeps its original v1 default."""

    def __init__(self, asset_kind: str, name: str, coord_space: dict[str, Any],
                 options: dict[str, Any] | None = None, *, source_dcc: str) -> None:
        super().__init__(asset_kind, name, coord_space, options)
        self.manifest["schema"] = SCHEMA_V2_ID
        self.manifest["source_dcc"] = source_dcc
