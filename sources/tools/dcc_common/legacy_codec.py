"""Host-independent legacy readers. Original decode APIs retain v1 defaults."""

from __future__ import annotations

import re
import struct
import json
import math
import tempfile
import zlib
from pathlib import Path
from typing import Any

from .exchange import (ANIMATION_CHANNELS, ExchangeBundleWriter, SCHEMA_V2_ID,
                       validate_bundle, validate_manifest_and_payload)

_LUA_TOKEN_RE = re.compile(
    r"""
    \s+
    | --[^\n]*
    | (?P<STR>"(?:\\(?:\r\n|[\s\S])|[^"\\\r\n])*")
    | (?P<NUM>-?(?:\d+\.\d*|\.\d+|\d+)(?:[eE][+-]?\d+)?)
    | (?P<ID>[A-Za-z_][A-Za-z0-9_]*)
    | (?P<PUNCT>[{}\[\]()=,;])
    """,
    re.VERBOSE,
)

_VERT_STATIC = struct.Struct("<14f")
_VERT_SKIN_4W = struct.Struct("<4H17f")
_FLOAT3 = struct.Struct("<3f")


def _lua_string(literal: str) -> str:
    # X-Ray configs use Lua 5.1 strings, including decimal byte escapes. JSON
    # decoding rejects those and interprets some escapes differently.
    escapes = {"a": "\a", "b": "\b", "f": "\f", "n": "\n", "r": "\r",
               "t": "\t", "v": "\v", "\\": "\\", '"': '"', "'": "'"}
    value, i = [], 1
    while i < len(literal)-1:
        char = literal[i]
        i += 1
        if char != "\\":
            value.append(char)
            continue
        char = literal[i]
        i += 1
        if "0" <= char <= "9":
            digits = char
            while i < len(literal)-1 and len(digits) < 3 and "0" <= literal[i] <= "9":
                digits += literal[i]
                i += 1
            if int(digits) > 255:
                raise ValueError("Lua decimal escape exceeds byte range")
            value.append(chr(int(digits)))
        elif char in "\r\n":
            if char == "\r" and literal[i:i+1] == "\n": i += 1
            value.append("\n")
        elif char in escapes:
            value.append(escapes[char])
        else:
            raise ValueError("Unsupported Lua string escape: "+repr(char))
    return "".join(value)


def parse_lua_config(text: str) -> dict[str, Any]:
    """Parse an X-Ray Lua text config (tables, float2/float3/float4, primitives) safely."""
    tokens: list[tuple[str, str]] = []
    pos = 0
    while pos < len(text):
        m = _LUA_TOKEN_RE.match(text, pos)
        if not m:
            raise ValueError(f"Unexpected Lua token near offset {pos}: {text[pos : pos + 32]!r}")
        pos = m.end()
        if m.lastgroup:
            tokens.append((m.lastgroup, m.group(m.lastgroup)))
    idx = 0

    def peek() -> tuple[str, str] | None:
        return tokens[idx] if idx < len(tokens) else None

    def eat(expected: str | None = None) -> tuple[str, str]:
        nonlocal idx
        tok = peek()
        if tok is None or (expected is not None and tok[1] != expected):
            raise ValueError(f"Expected {expected!r}, got {tok!r}")
        idx += 1
        return tok

    def parse_value() -> Any:
        tok = peek()
        if tok is None:
            raise ValueError("Unexpected EOF in Lua config")
        kind, val = tok
        if kind == "STR":
            eat()
            return _lua_string(val)
        if kind == "NUM":
            eat()
            return int(val) if "." not in val and "e" not in val.lower() else float(val)
        if kind == "ID":
            if val == "true":
                eat()
                return True
            if val == "false":
                eat()
                return False
            if val in ("float2", "float3", "float4"):
                eat()
                eat("(")
                vec = []
                while peek() != ("PUNCT", ")"):
                    vec.append(float(parse_value()))
                    if peek() == ("PUNCT", ","):
                        eat(",")
                eat(")")
                if len(vec) != int(val[-1]) or not all(math.isfinite(v) for v in vec):
                    raise ValueError(f"Invalid {val} literal")
                return vec
        if tok == ("PUNCT", "{"):
            return parse_table()
        raise ValueError(f"Unsupported Lua value token {tok!r}")

    def parse_table() -> dict[str, Any] | list[Any]:
        eat("{")
        named: dict[Any, Any] = {}
        seq: list[Any] = []
        while peek() != ("PUNCT", "}"):
            tok = peek()
            if tok is None:
                raise ValueError("Unexpected EOF in Lua table")
            if tok == ("PUNCT", "["):
                eat("[")
                key_val = parse_value()
                eat("]")
                eat("=")
                if not isinstance(key_val, (str, int)) or isinstance(key_val, bool):
                    raise ValueError("Lua keys must be strings or integer indices")
                if key_val in named:
                    raise ValueError(f"Duplicate Lua key {key_val!r}")
                named[key_val] = parse_value()
            elif tok[0] == "ID" and idx + 1 < len(tokens) and tokens[idx + 1] == ("PUNCT", "="):
                key = eat()[1]
                eat("=")
                if key in named:
                    raise ValueError(f"Duplicate Lua key {key}")
                named[key] = parse_value()
            else:
                seq.append(parse_value())
            if peek() == ("PUNCT", ","):
                eat(",")
        eat("}")
        if seq and not named:
            return seq
        for i, value in enumerate(seq, 1):
            if i in named:
                raise ValueError(f"Duplicate Lua index {i}")
            named[i] = value
        return named

    root: dict[str, Any] = {}
    while peek() is not None:
        key = eat()[1]
        eat("=")
        root[key] = parse_value()
        if peek() == ("PUNCT", ";"):
            eat(";")
    return root


def read_vfs_archive(raw: bytes) -> dict[str, bytes]:
    """Read an X-Ray VFS packed archive ('little-endian\\0\\0\\0' header)."""
    if len(raw) < 24 or raw[:16] != b"little-endian\x00\x00\x00":
        raise ValueError("Unsupported or truncated X-Ray VFS archive")
    count, fat_size = struct.unpack_from("<II", raw, 16)
    fat = raw[24 : 24 + fat_size]
    if len(fat) != fat_size or not 0 < count <= 100000:
        raise ValueError("Truncated or excessive archive FAT")
    roots = []
    for i in range(16, fat_size - 51, 8):
        flags, zero = struct.unpack_from("<HH", fat, i + 48)
        parent, sib = struct.unpack_from("<QQ", fat, i + 24)
        child_or_start = struct.unpack_from("<Q", fat, i - 16)[0]
        if flags == 13 and zero == 0 and parent == 0 and sib == 0 and 0 < child_or_start < fat_size:
            roots.append(i)
    if len(roots) != 1:
        raise ValueError("Archive does not have a unique root node")
    out: dict[str, bytes] = {}
    emitted = 0
    file_cache = {}

    def walk(node: int, prefix: str, ancestors=(), alias=None) -> None:
        nonlocal emitted
        if node in ancestors or node < 8 or node+51 >= len(fat) or emitted >= 100000 or len(ancestors) >= 256:
            raise ValueError("Invalid or cyclic archive node")
        emitted += 1
        flags = struct.unpack_from("<H", fat, node + 48)[0]
        end = fat.index(0, node + 51)
        name = fat[node + 51 : end].decode("utf-8") if alias is None else alias
        if name and (name in {".", ".."} or any(c in name for c in "/\\:\x00")):
            raise ValueError("Unsafe archive member name")
        path = name if not prefix else f"{prefix}/{name}"
        if flags & 512:
            # hard_link_node::referenced is the pointer immediately preceding
            # base_node. HQ collision commonly aliases an entire collision folder.
            target = struct.unpack_from("<Q", fat, node - 8)[0]
            walk(target, prefix, ancestors+(node,), alias=name)
        elif flags & 1:
            if node < 16:
                raise ValueError("Truncated archive folder node")
            child = struct.unpack_from("<Q", fat, node - 16)[0]
            siblings = set()
            while child:
                if child in siblings:
                    raise ValueError("Cyclic archive sibling list")
                siblings.add(child)
                walk(child, path, ancestors+(node,))
                child = struct.unpack_from("<Q", fat, child + 24)[0]
        else:
            if flags != 4:
                raise ValueError(f"Unsupported archive file flags: {flags:#x}")
            if node < 24:
                raise ValueError("Truncated archive file node")
            length = struct.unpack_from("<I", fat, node - 24)[0]
            start = struct.unpack_from("<Q", fat, node - 16)[0]
            if start < 24+fat_size or start+length > len(raw) or path in out:
                raise ValueError("Invalid archive file span or duplicate path")
            if node not in file_cache:
                file_cache[node] = raw[start : start + length]
            out[path] = file_cache[node]

    walk(roots[0], "")
    if len(out) == 0 or count == 0:
        raise ValueError("Archive contains no files")
    return out


def normalized_legacy_weights(indices, weights):
    """Repair only f32 rounding drift; reject materially invalid legacy weights."""
    if any(not math.isfinite(w) or w < -1e-6 or w > 1+1e-6 for w in weights):
        raise ValueError("Legacy skin weight lies outside the f32 repair tolerance")
    adjusted = [max(0.0, min(1.0, w)) for w in weights]
    for slot, index in enumerate(indices):
        if index == 0xFFFF:
            if adjusted[slot] > 1e-6:
                raise ValueError("Unused legacy skin slot carries a nonzero weight")
            adjusted[slot] = 0.0
    total = sum(adjusted)
    if abs(total-1) > 2e-6:
        raise ValueError("Legacy skin weights require more than a rounding repair")
    return tuple(w/total for w in adjusted), adjusted != list(weights)


def parse_config_bytes(raw):
    """Read Lua or the current SDK's checked 64-bit binary config, with bounds."""
    entry = struct.Struct("<QQIHH")
    if len(raw) < entry.size or entry.unpack_from(raw)[3] not in {3, 4}:
        return parse_lua_config(raw.decode("utf-8-sig"))
    if len(raw) > 64*1024*1024:
        raise ValueError("Binary config exceeds 64 MiB")
    visited, blob_offsets = set(), []
    def string(offset, count=None):
        if offset >= len(raw):
            raise ValueError("Binary config string offset is out of bounds")
        end = raw.index(0, offset)
        if count is not None and end-offset+1 != count:
            raise ValueError("Binary config string length mismatch")
        blob_offsets.append(offset)
        return raw[offset:end].decode("utf-8")
    def node(offset, depth=0):
        if depth > 64 or offset % 24 or offset < 0 or offset+24 > len(raw) or offset in visited or len(visited) >= 500000:
            raise ValueError("Invalid or cyclic binary config entry")
        visited.add(offset)
        identifier, value, crc, kind, count = entry.unpack_from(raw, offset)
        name = string(identifier) if identifier else None
        if name is not None and zlib.crc32(name.encode("utf-8")) != crc:
            raise ValueError("Binary config key CRC mismatch")
        if kind in {3, 4}:
            if value % 24 or value+count*24 > len(raw):
                raise ValueError("Binary config table span is out of bounds")
            children = [node(value+i*24, depth+1) for i in range(count)]
            if kind == 4:
                if any(k is not None for k, _ in children):
                    raise ValueError("Indexed binary table contains names")
                result = [v for _, v in children]
            else:
                if any(k is None for k, _ in children) or len({k for k, _ in children}) != len(children):
                    raise ValueError("Invalid named binary table")
                result = dict(children)
        elif kind in {0, 1, 2}:
            if count != 4 or value > 0xffffffff:
                raise ValueError("Invalid binary scalar")
            bits = struct.pack("<I", value)
            result = bool(value) if kind == 0 else struct.unpack("<i" if kind == 1 else "<f", bits)[0]
            if kind == 0 and value not in {0, 1} or kind == 2 and not math.isfinite(result):
                raise ValueError("Invalid binary bool/float")
        elif kind == 5:
            result = string(value, count)
        elif kind in {6, 7, 8}:
            components = kind-4
            if count != components*4 or value+count > len(raw):
                raise ValueError("Invalid binary vector span")
            blob_offsets.append(value)
            result = list(struct.unpack_from("<"+str(components)+"f", raw, value))
            if not all(math.isfinite(v) for v in result):
                raise ValueError("Non-finite binary vector")
        else:
            raise ValueError(f"Unsupported binary config type {kind}")
        return name, result
    _, result = node(0)
    if blob_offsets and min(blob_offsets) < max(visited)+24:
        raise ValueError("Binary config blob overlaps its entry table")
    return result


def load_config(path):
    return parse_config_bytes(Path(path).read_bytes())


def load_model_files(source: str | Path, settings_override: str | Path | None = None) -> dict[str, bytes]:
    """Load all relative files from either an unpacked .model directory or a packed .model VFS archive."""
    src = Path(source)
    if src.is_dir():
        if not (src/"render/export_properties").is_file():
            raise ValueError("Select a model directory containing render/export_properties")
        files = {
            p.relative_to(src).as_posix(): p.read_bytes()
            for p in sorted(src.rglob("*"))
            if p.is_file()
        }
    elif src.is_file():
        files = read_vfs_archive(src.read_bytes())
    else:
        raise FileNotFoundError(f"Model source not found: {src}")
    if settings_override is not None:
        ov = Path(settings_override)
        settings_file = ov / "settings" if ov.is_dir() else ov
        files["settings"] = settings_file.read_bytes()
    for key in list(files):
        if key.startswith("render/") and key.endswith("/converted_model") and key.rsplit("/", 1)[0]+"/vertices" not in files:
            raw, offset = files[key], 0
            while offset < len(raw):
                if offset+8 > len(raw):
                    raise ValueError("Truncated converted mesh chunk")
                kind, size = struct.unpack_from("<II", raw, offset); offset += 8
                if offset+size > len(raw) or kind not in {3, 4}:
                    raise ValueError("Invalid converted mesh chunk")
                body = raw[offset:offset+size]
                if kind == 3 and body.startswith(bytes((0, 0, 0, 0, 2, 0, 0, 0))):
                    # Current Vulkan static surface: 48-byte D3D declaration,
                    # count, then P3 + three packed BGRA bases + float2 UV.
                    if len(body) < 52:
                        raise ValueError("Truncated converted static vertex declaration")
                    vertices = struct.unpack_from("<I", body, 48)[0]
                    if len(body) != 52+vertices*32:
                        raise ValueError("Unsupported converted static vertex layout")
                    restored = bytearray(struct.pack("<I", vertices))
                    for index in range(vertices):
                        start = 52+index*32
                        values = list(struct.unpack_from("<3f", body, start))
                        for basis in range(3):
                            packed = body[start+12+basis*4:start+16+basis*4]
                            normal = [packed[i]/127.5-1 for i in (2, 1, 0)]
                            length = math.sqrt(sum(v*v for v in normal))
                            values.extend(v/length for v in normal)
                        values.extend(struct.unpack_from("<2f", body, start+24))
                        restored.extend(struct.pack("<14f", *values))
                    body = bytes(restored)
                files[key.rsplit("/", 1)[0]+("/vertices" if kind == 3 else "/indices")] = body
                offset += size
    return files


def _flip_tri_winding(indices: list[int]) -> list[int]:
    out = list(indices)
    for i in range(0, len(out), 3):
        out[i + 1], out[i + 2] = out[i + 2], out[i + 1]
    return out


def _read_collision_pair(v_bytes: bytes, i_bytes: bytes, *, to_maya: bool, unit_scale: float) -> tuple[list[tuple[float, float, float]], list[int]]:
    vc_id, vc_size = struct.unpack_from("<II", v_bytes, 0)
    ic_id, ic_size = struct.unpack_from("<II", i_bytes, 0)
    if vc_id != 25 or ic_id != 26:
        raise ValueError(f"Unexpected collision chunk IDs: {vc_id}, {ic_id}")
    v_cnt = vc_size // 12
    i_cnt = ic_size // 4
    positions = []
    for k in range(v_cnt):
        x, y, z = _FLOAT3.unpack_from(v_bytes, 8 + k * 12)
        if to_maya:
            z = -z
        if unit_scale != 1.0:
            x, y, z = x / unit_scale, y / unit_scale, z / unit_scale
        positions.append((x, y, z))
    indices = list(struct.unpack_from(f"<{i_cnt}I", i_bytes, 8))
    if to_maya:
        indices = _flip_tri_winding(indices)
    return positions, indices


def _decode_bind_pose(raw: bytes, *, to_maya: bool, unit_scale: float) -> list[dict[str, Any]]:
    count = struct.unpack_from("<H", raw, 0)[0]
    offset = 2
    bones: list[dict[str, Any]] = []
    for _ in range(count):
        end = raw.index(0, offset)
        name = raw[offset:end].decode("utf-8")
        offset = end + 1
        tx, ty, tz = _FLOAT3.unpack_from(raw, offset)
        rx, ry, rz = _FLOAT3.unpack_from(raw, offset + 12)
        sx, sy, sz = _FLOAT3.unpack_from(raw, offset + 24)
        offset += 36
        if to_maya:
            tz, rz = -tz, -rz
        if unit_scale != 1.0:
            tx, ty, tz = tx / unit_scale, ty / unit_scale, tz / unit_scale
        bones.append(
            {
                "name": name,
                "bind_translation": [tx, ty, tz],
                "bind_rotation": [rx, ry, rz],
                "bind_scale": [sx, sy, sz],
            }
        )
    return bones


def decode_model_to_exchange(
    source: str | Path,
    out_dir: str | Path,
    *,
    logical_name: str,
    coord_system: str = "maya_right_handed_y_up",
    unit_scale_to_meters: float = 1.0,
    settings_override: str | Path | None = None,
) -> Path:
    """Decode a reference static or skinned X-Ray model into an xray-maya-exchange/v1 bundle."""
    files = load_model_files(source, settings_override=settings_override)
    to_maya = coord_system == "maya_right_handed_y_up"
    coord_space = {
        "system": coord_system,
        "unit_scale_to_meters": float(unit_scale_to_meters),
        "uv_origin": "bottom_left" if to_maya else "top_left",
        "front_face_winding": "ccw_maya" if to_maya else "cw_xray",
    }

    settings = parse_config_bytes(files["settings"]) if "settings" in files else {}
    root_props = parse_config_bytes(files["render/export_properties"])
    model_type = int(root_props.get("type", 1))
    is_skinned = model_type == 40
    kind = "skinned_model" if is_skinned else "static_model"

    writer = ExchangeBundleWriter(
        kind,
        logical_name,
        coord_space,
        options={
            "max_verts_per_surface": 65500,
            "optimize_uv": False,
            "optimize_mesh": False,
            "reference_bounds": root_props.get("bounding_box"),
        },
    )
    repaired_weights = 0
    if "rigid_body" in settings:
        writer.manifest["rigid_body"] = settings["rigid_body"]

    if root_props.get("locators"):
        locs = []
        values = root_props["locators"]
        for lv in values if isinstance(values, list) else values.values():
            p = list(lv["position"])
            r = list(lv["rotation"])
            if to_maya:
                p[2] = -p[2]
                r[2] = -r[2]
            if unit_scale_to_meters != 1.0:
                p = [c / unit_scale_to_meters for c in p]
            locs.append(
                {
                    "name": lv["name"],
                    "bone_name": lv.get("bone_name", ""),
                    "bone_idx": int(lv.get("bone_idx", -1)),
                    "position": p,
                    "rotation": r,
                }
            )
        writer.manifest["locators"] = locs

    if is_skinned:
        skel_cfg = parse_config_bytes(files["skeleton"])
        writer.manifest["skeleton_name"] = skel_cfg["skeleton"]
        writer.manifest["bones"] = _decode_bind_pose(
            files["render/bind_pose"], to_maya=to_maya, unit_scale=unit_scale_to_meters
        )

    material_settings = settings.get("material_settings", {})
    surface_names = sorted(
        k.split("/")[1]
        for k in files
        if k.startswith("render/") and k.endswith("/vertices") and len(k.split("/")) == 3
    )

    surfaces_out = []
    for sname in surface_names:
        sprops = parse_config_bytes(files[f"render/{sname}/export_properties"])
        src_meta = sprops.get("source", {})
        v_raw = files[f"render/{sname}/vertices"]
        i_raw = files[f"render/{sname}/indices"]
        v_cnt = struct.unpack_from("<I", v_raw, 0)[0]
        i_cnt = struct.unpack_from("<I", i_raw, 0)[0]

        positions, normals, tangents, binormals, uvs = [], [], [], [], []
        bone_indices, bone_weights = [], []
        packer = _VERT_SKIN_4W if is_skinned else _VERT_STATIC
        for vi in range(v_cnt):
            vals = packer.unpack_from(v_raw, 4 + vi * packer.size)
            if is_skinned:
                m0, m1, m2, m3 = vals[0:4]
                px, py, pz = vals[4:7]
                nx, ny, nz = vals[7:10]
                tx, ty, tz = vals[10:13]
                bx, by, bz = vals[13:16]
                w0, w1, w2 = vals[16:19]
                u, v = vals[19:21]
            else:
                px, py, pz = vals[0:3]
                nx, ny, nz = vals[3:6]
                tx, ty, tz = vals[6:9]
                bx, by, bz = vals[9:12]
                u, v = vals[12:14]
            if to_maya:
                pz, nz, tz, bz = -pz, -nz, -tz, -bz
                v = 1.0 - v
            if unit_scale_to_meters != 1.0:
                px, py, pz = px / unit_scale_to_meters, py / unit_scale_to_meters, pz / unit_scale_to_meters
            positions.append((px, py, pz))
            normals.append((nx, ny, nz))
            tangents.append((tx, ty, tz))
            binormals.append((bx, by, bz))
            uvs.append((u, v))
            if is_skinned:
                w3 = max(0.0, 1.0 - (w0 + w1 + w2))
                bone_indices.append((m0, m1, m2, m3))
                weights, repaired = normalized_legacy_weights((m0, m1, m2, m3), (w0, w1, w2, w3))
                repaired_weights += int(repaired)
                bone_weights.append(weights)

        indices = list(struct.unpack_from(f"<{i_cnt}H", i_raw, 4))
        if to_maya:
            indices = _flip_tri_winding(indices)

        streams = {
            "positions": writer.append_span("f32x3", positions),
            "normals": writer.append_span("f32x3", normals),
            "uvs": writer.append_span("f32x2", uvs),
            "indices": writer.append_span("u32", indices),
            "tangents": writer.append_span("f32x3", tangents),
            "binormals": writer.append_span("f32x3", binormals),
        }
        if is_skinned:
            streams["bone_indices"] = writer.append_span("u16x4", bone_indices)
            streams["bone_weights"] = writer.append_span("f32x4", bone_weights)

        surfaces_out.append(
            {
                "name": sname,
                "mesh_name": src_meta.get("mesh_name", sname),
                "shader_name": sname,
                "texture_name": src_meta.get("texture_name", ""),
                "material_name": material_settings.get(sname, {}).get("material_name", ""),
                "lod": "LOD0",
                "vertex_count": v_cnt,
                "index_count": i_cnt,
                "streams": streams,
            }
        )

    writer.manifest["surfaces"] = surfaces_out
    if repaired_weights:
        writer.manifest["import_metadata"] = {"skin_weight_rounding_repairs": repaired_weights, "maximum_weight_repair": 1e-6}

    if "collision/vertices" in files and "collision/indices" in files:
        c_pos, c_idx = _read_collision_pair(
            files["collision/vertices"], files["collision/indices"],
            to_maya=to_maya, unit_scale=unit_scale_to_meters,
        )
        writer.manifest["collision"] = {
            "from_render_geometry": False,
            "vertex_count": len(c_pos),
            "index_count": len(c_idx),
            "positions": writer.append_span("f32x3", c_pos),
            "indices": writer.append_span("u32", c_idx),
        }
    if "hq_collision/vertices" in files and "hq_collision/indices" in files:
        hq_pos, hq_idx = _read_collision_pair(
            files["hq_collision/vertices"], files["hq_collision/indices"],
            to_maya=to_maya, unit_scale=unit_scale_to_meters,
        )
        writer.manifest["hq_collision"] = {
            "from_render_geometry": True,
            "vertex_count": len(hq_pos),
            "index_count": len(hq_idx),
            "positions": writer.append_span("f32x3", hq_pos),
            "indices": writer.append_span("u32", hq_idx),
        }

    return writer.write(out_dir)


def decode_skeleton_to_exchange(skeleton_file: str | Path, out_dir: str | Path, *, logical_name: str) -> Path:
    """Decode an X-Ray .skeleton Lua config into an xray-maya-exchange/v1 skeleton bundle."""
    parsed = load_config(skeleton_file)
    if len(parsed) != 1:
        raise ValueError("Expected single root bone in .skeleton config")
    root_name, root_table = next(iter(parsed.items()))

    def convert(name: str, tbl: dict[str, Any]) -> dict[str, Any]:
        return {
            "name": name,
            "children": [convert(ck, cv) for ck, cv in tbl.items()],
        }

    writer = ExchangeBundleWriter(
        "skeleton",
        logical_name,
        {
            "system": "xray_left_handed_y_up",
            "unit_scale_to_meters": 1.0,
            "uv_origin": "top_left",
            "front_face_winding": "cw_xray",
        },
    )
    writer.manifest["root"] = convert(root_name, root_table)
    return writer.write(out_dir)


def evaluate_cubic_bspline(knots: list[float], coefs: list[float], t: float) -> float:
    """Evaluate a clamped open cubic (order 4) Cox-de Boor B-spline at parameter t."""
    n = len(coefs)
    if n == 0:
        return 0.0
    if len(knots) < n + 4:
        raise ValueError(f"Expected at least {n + 4} knots for {n} control points, got {len(knots)}")
    p = 3
    t_min = knots[p]
    t_max = knots[n]
    if t <= t_min:
        return float(coefs[0])
    if t >= t_max:
        return float(coefs[-1])

    # Find knot span k such that knots[k] <= t < knots[k + 1]
    k = p
    for idx in range(p, n):
        if knots[idx] <= t < knots[idx + 1]:
            k = idx
            break

    d = [float(coefs[k - p + j]) for j in range(p + 1)]
    for r in range(1, p + 1):
        for j in range(p, r - 1, -1):
            left = knots[k - p + j]
            right = knots[k + 1 + j - r]
            denom = right - left
            alpha = (t - left) / denom if denom > 1e-12 else 0.0
            d[j] = (1.0 - alpha) * d[j - 1] + alpha * d[j]
    return d[p]


def indexed(value):
    """Read numeric Lua entries without losing fields in mixed tables."""
    if isinstance(value, list):
        return value
    if not isinstance(value, dict):
        raise ValueError("Expected a Lua table")
    numeric = {int(k): v for k, v in value.items() if isinstance(k, int) or str(k).isdigit()}
    if set(numeric) != set(range(1, len(numeric)+1)):
        raise ValueError("Lua sequence indices must be contiguous and start at one")
    return [numeric[i] for i in range(1, len(numeric)+1)]


def load_check_data(path):
    parsed = parse_lua_config(Path(path).read_text(encoding="utf-8"))
    root = parsed.get("discrete_data", parsed)
    fps = float(root["time_scale"])
    precision = [float(v) for v in indexed(root["precision"])]
    if not math.isfinite(fps) or fps <= 0 or len(precision) != 9 or not all(math.isfinite(v) and v > 0 for v in precision):
        raise ValueError("Invalid check-data fps or nine precisions")
    bones = root["bones"]
    if "header" in bones:
        header = bones["header"]
        if header["type"] != 1 or abs(float(header["frequency"])*fps-1) > 1e-4:
            raise ValueError("Check-data frequency/type disagrees with fps")
    samples, starts = [], []
    for track in indexed(bones["animation_data"]):
        channels = indexed(track.get("data", track) if isinstance(track, dict) else track)
        starts.append(float(track.get("start_time", 0)) if isinstance(track, dict) else 0.0)
        if len(channels) != 9:
            raise ValueError("Check data needs nine channels per bone")
        samples.append([[float(v) for v in indexed(c)] for c in channels])
    if not samples or len(samples) > 128:
        raise ValueError("Check data needs 1..128 tracks")
    count = len(samples[0][0])
    if not 1 <= count <= 1000000 or any(len(c) != count or not all(math.isfinite(v) for v in c) for t in samples for c in t):
        raise ValueError("Check-data channel lengths differ or contain non-finite samples")
    if not all(math.isfinite(v) and abs(v-starts[0]) < 1e-5 for v in starts):
        raise ValueError("Clip tracks must have matching finite start_time values")
    return dict(fps=fps, precisions=precision, samples=samples, frame_start=starts[0]*fps/30)


def decode_clip_to_exchange(clip_file, out_dir, *, logical_name, fps=30.0, check_data=None, prefer_check_data=True, normalize_zero_root_scale=False):
    clip_file = Path(clip_file)
    splines = load_config(clip_file).get("splines")
    if not isinstance(splines, dict):
        raise ValueError("Missing splines table in clip")
    names, data = indexed(splines.get("bone_names", [])), indexed(splines.get("data", []))
    if not names or len(names) != len(data):
        raise ValueError("Mismatch between clip bone names and tracks")
    companion = Path(check_data) if check_data else clip_file.with_suffix(".clip_check_data")
    checked = load_check_data(companion) if prefer_check_data and companion.is_file() else None
    if check_data and not companion.is_file():
        raise FileNotFoundError(companion)
    fps = checked["fps"] if checked else float(fps)
    if not math.isfinite(fps) or fps <= 0:
        raise ValueError("Clip fps must be positive finite")
    end = max(float(ch[-1][0]) for track in data for ch in indexed(track) if ch)
    count = len(checked["samples"][0][0]) if checked else max(1, round(end*fps/30)+1)
    if checked and len(checked["samples"]) != len(names):
        raise ValueError("Check-data bone count disagrees with clip bone names")
    writer = ExchangeBundleWriter("animation", logical_name, {"system": "xray_left_handed_y_up",
        "unit_scale_to_meters": 1.0, "uv_origin": "top_left", "front_face_winding": "cw_xray"})
    precision = checked["precisions"] if checked else [0.005]*9
    prec_t, prec_r, prec_s = min(precision[:3]), min(precision[3:6]), min(precision[6:])
    writer.manifest.update(fps=fps, frame_count=count, frame_start=checked["frame_start"] if checked else 0,
        root_already_relative=True, is_additive=bool(splines.get("is_additive", False)),
        precision_t=prec_t, precision_r=prec_r, precision_s=prec_s,
        import_metadata={"sample_source": "check_data" if checked else "clip_splines", "seed_frames": {}})
    bone_channels, bone_seeds = [], []
    raw_root_scale_coefs = None
    for ti, (name, channels) in enumerate(zip(names, data)):
        channels = indexed(channels)
        if len(channels) != 9:
            raise ValueError(f"{name}: expected nine clip channels")
        if ti == 1:
            raw_root_scale_coefs = [indexed(channels[ci]) for ci in range(6, 9)]
        ch_values, seeds = [], set()
        for ci, pairs in enumerate(channels):
            knots = [float(pair[0]) for pair in pairs]
            seeds.update(writer.manifest["frame_start"]+k*fps/30 for k in knots)
            if checked:
                values = list(checked["samples"][ti][ci])
            elif len(pairs) < 5:
                raise ValueError(f"{name}.{ANIMATION_CHANNELS[ci]}: truncated cubic spline")
            else:
                values = [evaluate_cubic_bspline(knots+[knots[-1]]*3, [float(p[1]) for p in pairs[:-1]], f*30/fps) for f in range(count)]
            ch_values.append(values)
        bone_channels.append(ch_values)
        bone_seeds.append(sorted(seeds))
    if normalize_zero_root_scale and len(names) >= 2 and names[0] == "object_movement":
        root_ch = bone_channels[1]
        spline_zero = raw_root_scale_coefs is not None and all(
            float(pair[1]) == 0.0 for ch in raw_root_scale_coefs for pair in ch[:-1]
        )
        samples_zero = all(v == 0.0 for ci in range(6, 9) for v in root_ch[ci])
        root_t_ok = max((abs(v) for ci in range(0, 3) for v in root_ch[ci]), default=0.0) <= prec_t / 2 + 1e-7
        root_r_ok = max((abs(v) for ci in range(3, 6) for v in root_ch[ci]), default=0.0) <= prec_r / 2 + 1e-7
        children_pos_scale = all(
            math.isfinite(v) and v > 0.0
            for ti, ch_list in enumerate(bone_channels) if ti != 1
            for ci in range(6, 9) for v in ch_list[ci]
        )
        if spline_zero and samples_zero and root_t_ok and root_r_ok and children_pos_scale:
            for ci in range(6, 9):
                bone_channels[1][ci] = [1.0] * count
            writer.manifest["import_metadata"]["legacy_zero_root_scale_normalized"] = {
                "bone": names[1],
                "original_scale": [0.0, 0.0, 0.0],
                "frame_count": count,
                "check_data_used": bool(checked),
            }
    tracks = []
    for name, ch_values, seeds in zip(names, bone_channels, bone_seeds):
        spans = {channel: writer.append_span("f32", values) for channel, values in zip(ANIMATION_CHANNELS, ch_values)}
        tracks.append({"bone_name": name, "channels": spans})
        writer.manifest["import_metadata"]["seed_frames"][name] = seeds
    writer.manifest["tracks"] = tracks
    return writer.write(out_dir)


def decode_physics_to_exchange(
    physics_file: str | Path,
    out_dir: str | Path,
    *,
    logical_name: str,
) -> Path:
    """Decode an X-Ray .physics Lua config into an xray-maya-exchange/v1 physics bundle."""
    parsed = load_config(physics_file)
    writer = ExchangeBundleWriter(
        "physics",
        logical_name,
        {
            "system": "xray_left_handed_y_up",
            "unit_scale_to_meters": 1.0,
            "uv_origin": "top_left",
            "front_face_winding": "cw_xray",
        },
    )

    def vec3(v: Any) -> list[float]:
        return [float(v[0]), float(v[1]), float(v[2])]

    bodies_out: list[dict[str, Any]] = []
    for b in parsed.get("bodies", []):
        cols_out: list[dict[str, Any]] = []
        for c in b.get("collision", []):
            cols_out.append(
                {
                    "type": str(c["type"]),
                    "position": vec3(c["position"]),
                    "rotation": vec3(c["rotation"]),
                    "scale": vec3(c["scale"]),
                }
            )
        bodies_out.append(
            {
                "name": str(b["name"]),
                "anim_joint": str(b.get("anim_joint", "")),
                "mass": float(b["mass"]),
                "mass_center": vec3(b["mass_center"]),
                "position": vec3(b["position"]),
                "rotation": vec3(b["rotation"]),
                "scale": vec3(b["scale"]),
                "collision": cols_out,
            }
        )

    dof_names = ("x_translate", "y_translate", "z_translate", "x_rotate", "y_rotate", "z_rotate")
    joints_out: list[dict[str, Any]] = []
    named_joints = {str(j["name"]) for j in parsed.get("joints", []) if j.get("name")}
    generated_names = []
    for ji, j in enumerate(parsed.get("joints", [])):
        name = j.get("name")
        if not name:
            name = bodies_out[0]["name"] if ji == 0 and bodies_out else f"legacy_joint_{ji:03d}"
            while name in named_joints:
                name += "_joint"
            named_joints.add(name)
            generated_names.append(name)
        item: dict[str, Any] = {
            "name": str(name),
            "type": str(j["type"]),
            "position": vec3(j["position"]),
            "rotation": vec3(j["rotation"]),
            "scale": vec3(j["scale"]),
        }
        if "body0" in j and j["body0"]:
            item["body0"] = str(j["body0"])
        if "body1" in j and j["body1"]:
            item["body1"] = str(j["body1"])
        for dn in dof_names:
            d = j[dn]
            item[dn] = {
                "enable_state": bool(d["enable_state"]),
                "limits_enable_state": bool(d["limits_enable_state"]),
                "low_limit": float(d["low_limit"]),
                "hi_limit": float(d["hi_limit"]),
            }
        joints_out.append(item)

    writer.manifest["bodies"] = bodies_out
    writer.manifest["joints"] = joints_out
    if generated_names:
        writer.manifest["import_metadata"] = {"generated_joint_names": generated_names}
    return writer.write(out_dir)


def decompile_asset(source, out_dir, *, logical_name=None, check_data=None, fps=30.0, normalize_zero_root_scale=False):
    """Decode a raw legacy asset to validated v2 without importing any DCC host."""
    source, output = Path(source).resolve(), Path(out_dir).resolve()
    if source == output or source.is_dir() and output.is_relative_to(source):
        raise ValueError("Decompile output must be outside the original asset")
    name = logical_name or source.stem
    extension = source.suffix.lower()
    with tempfile.TemporaryDirectory(prefix="xray_decompile_") as temporary:
        if source.is_dir() or extension in {".model", ".skinned_model"}:
            decode_model_to_exchange(source, temporary, logical_name=name, coord_system="maya_right_handed_y_up", unit_scale_to_meters=0.01)
        elif extension == ".skeleton":
            decode_skeleton_to_exchange(source, temporary, logical_name=name)
        elif extension == ".clip":
            decode_clip_to_exchange(source, temporary, logical_name=name, check_data=check_data, fps=fps, normalize_zero_root_scale=normalize_zero_root_scale)
        elif extension in {".physics", ".ph"}:
            decode_physics_to_exchange(source, temporary, logical_name=name)
        else:
            raise ValueError("Choose an exchange bundle, .model, .skinned_model, .skeleton, .clip, .physics or .ma")
        manifest = validate_bundle(temporary)
        manifest.update(schema=SCHEMA_V2_ID, source_dcc="legacy_clip" if extension == ".clip" else "legacy_asset")
        if manifest["asset_kind"] == "physics" and manifest.get("joints"):
            root_joint = manifest["joints"][0]
            if not root_joint.get("body0") and not root_joint.get("body1") and root_joint["type"] != "rigid":
                # The runtime reserves/skips slot zero. Older files can carry a
                # nominal Ball type there; retain it as provenance and expose a
                # canonical free root which the current authoring exporter accepts.
                manifest.setdefault("import_metadata", {})["legacy_root_joint"] = dict(root_joint)
                root_joint["type"] = "rigid"
        if manifest["coord_space"]["system"] == "maya_right_handed_y_up":
            manifest["coord_space"].update(system="right_handed_y_up", front_face_winding="ccw")
        payload = (Path(temporary)/"payload.bin").read_bytes()
        validate_manifest_and_payload(manifest, payload)
    output.mkdir(parents=True, exist_ok=True)
    (output/"manifest.json").write_text(json.dumps(manifest, indent=2)+"\n", encoding="utf-8")
    (output/"payload.bin").write_bytes(payload)
    return output
