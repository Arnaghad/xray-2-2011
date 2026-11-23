"""Read a bounded Maya ASCII subset as data, never execute MEL or load plugins.

Supports transform/joint TRS, stored animCurve tangents, polygon meshes/UVs and
direct skinCluster connections. Unsupported evaluation dependencies fail loudly.
"""

from dataclasses import dataclass, field
import json
import math
from pathlib import Path
import re

from .dcc_math import trs, mul, inverse, decompose, curve_value
from .exchange import DccExchangeBundleWriter, ANIMATION_CHANNELS

ORDERS = ("XYZ", "YZX", "ZXY", "XZY", "YXZ", "ZYX")
UNITS = {"mm": 0.001, "cm": 0.01, "m": 1.0, "km": 1000.0, "in": 0.0254, "ft": 0.3048, "yd": 0.9144}
UNITS.update({"millimeter": 0.001, "centimeter": 0.01, "meter": 1.0, "kilometer": 1000.0, "inch": 0.0254, "foot": 0.3048, "yard": 0.9144})
FPS = {"game": 15, "film": 24, "pal": 25, "ntsc": 30, "show": 48, "palf": 50, "ntscf": 60}
ALIASES = {"translate": "t", "rotate": "r", "scale": "s", "jointOrient": "jo", "rotateAxis": "ra", "rotateOrder": "ro",
    "output": "o", "input": "i", "keyTimeValue": "ktv", "weightedTangents": "wgt", "vertices": "vt", "edge": "ed",
    "intermediateObject": "io", "segmentScaleCompensate": "ssc"}
for long, short in (("translate", "t"), ("rotate", "r"), ("scale", "s")):
    ALIASES.update({long+axis.upper(): short+axis for axis in "xyz"})


def commands(text):
    # Semicolons and comment markers in quoted script/string attributes are data.
    current, quoted, escaped, i = [], False, False, 0
    while i < len(text):
        ch = text[i]
        if not quoted and text.startswith("//", i):
            i = text.find("\n", i)
            if i < 0:
                break
        elif not quoted and text.startswith("/*", i):
            end = text.find("*/", i+2)
            if end < 0:
                raise ValueError("Unclosed MA comment")
            i = end+1
        elif ch == '"' and not escaped:
            quoted = not quoted
            current.append(ch)
        elif ch == ";" and not quoted:
            value = "".join(current).strip()
            if value:
                yield [json.loads(t) if t.startswith('"') else t for t in re.findall(r'"(?:\\.|[^"\\])*"|[^\s]+', value)]
            current = []
        else:
            current.append(ch)
        escaped = ch == "\\" and not escaped if quoted else False
        i += 1
    if quoted or "".join(current).strip():
        raise ValueError("Unterminated MA command/string")


def atom(value):
    if value in {"yes", "true", "on"}:
        return True
    if value in {"no", "false", "off"}:
        return False
    try:
        result = float(value)
        if not math.isfinite(result):
            raise ValueError("Non-finite MA value")
        return result
    except ValueError:
        if re.match(r"^[+\-.\d]", value):
            raise
        return value


@dataclass(eq=False)
class Node:
    kind: str
    name: str
    parent: object = None
    attrs: dict = field(default_factory=dict)

    @property
    def path(self):
        return (self.parent.path if self.parent else "")+"|"+self.name

    def array(self, base, width=1):
        result = {}
        for attr, values in self.attrs.items():
            if attr == base:
                start = 0
            else:
                match = re.fullmatch(re.escape(base)+r"\[(\d+)(?::(\d+))?\]", attr)
                if not match:
                    continue
                start = int(match[1])
            for i in range(0, len(values), width):
                result[start+i//width] = values[i:i+width] if width != 1 else values[i]
        return result

    def value(self, key, default):
        values = self.attrs.get(key)
        return values[0] if values else default

    def vector(self, key, default):
        values = list(self.attrs.get(key, default))
        return [float(self.value(key+axis, values[i])) for i, axis in enumerate("xyz")]


class Scene:
    def __init__(self, source):
        self.nodes, self.links, self.warnings = [], [], []
        self.unit, self.angle, self.fps = 0.01, math.pi/180, 24.0
        current = None
        source = Path(source)
        if source.stat().st_size > 64*1024*1024:
            raise ValueError("MA files larger than 64 MiB require the maintainer migration route")
        for tokens in commands(source.read_text(encoding="utf-8-sig")):
            op, args = tokens[0], tokens[1:]
            def flag(name, default=None):
                return args[args.index(name)+1] if name in args else default
            if op == "file" and any(v in args for v in ("-r", "-reference", "-rdi", "-rfn")):
                raise ValueError("MA references require a flattened .ma or the maintainer Maya migration route")
            if op == "currentUnit":
                self.unit = UNITS.get(flag("-l", "cm"))
                if self.unit is None:
                    raise ValueError("Unsupported MA distance unit")
                self.angle = 1.0 if flag("-a", "deg") in {"rad", "radian"} else math.pi/180
                unit = flag("-t", "film")
                self.fps = float(FPS[unit]) if unit in FPS else float(unit.removesuffix("fps"))
            elif op == "createNode":
                if args[0] in {"ikHandle", "ikEffector", "expression"} or args[0].endswith("Constraint"):
                    raise ValueError(f"MA evaluation node {args[0]} requires the maintainer Maya migration route")
                parent = self.find(flag("-p")) if flag("-p") else None
                current = Node(args[0], flag("-n", args[0]), parent)
                self.nodes.append(current)
                if len(self.nodes) > 100000:
                    raise ValueError("MA node count exceeds 100000")
            elif op == "select":
                candidates = [a for a in args if not a.startswith("-")]
                current = self.find(candidates[-1], required=False) if candidates else None
            elif op == "setAttr":
                location = next((i for i, a in enumerate(args) if a.startswith(".") or "." in a and not re.fullmatch(r"[-+\d.eE]+", a)), None)
                if location is None:
                    continue
                attr = args[location]
                node = current
                if not attr.startswith("."):
                    owner, attr = attr.split(".", 1)
                    node = self.find(owner, required=False)
                attr = attr.lstrip(".")
                attr = ALIASES.get(attr, attr)
                is_string_attr = any(i + 1 < len(args) and args[i] in {"-type", "-typ"} and args[i + 1] == "string" for i in range(len(args)))
                values, i = [], location+1
                while i < len(args):
                    if args[i] in {"-type", "-typ", "-s", "-size", "-k", "-keyable", "-l", "-lock", "-cb", "-ch"}:
                        i += 2
                    elif args[i].startswith("-") and not re.match(r"^-\d|^-\.", args[i]):
                        i += 1
                    else:
                        values.append(args[i] if is_string_attr else atom(args[i]))
                        i += 1
                if node and values:
                    node.attrs[attr] = values
            elif op == "connectAttr":
                values = [a for a in args if not a.startswith("-")]
                if len(values) >= 2:
                    self.links.append((values[0], values[1]))
            elif op == "parent":
                values = [a for a in args if not a.startswith("-")]
                if len(values) >= 2:
                    self.find(values[0]).parent = self.find(values[-1])
        for node in self.nodes:
            seen, current = set(), node
            while current:
                if current in seen or len(seen) > 256:
                    raise ValueError("Cyclic or excessive MA DAG hierarchy")
                seen.add(current); current = current.parent
            if node.kind in {"joint", "transform"} and any(abs(float(v)) > 1e-8 for key in ("rp", "rpt", "sp", "spt", "sh") for v in node.attrs.get(key, [])):
                raise ValueError(f"{node.name}: bake pivot/shear transforms before basic MA import")
        self.curves = {}
        for src, dst in self.links:
            if "." not in dst or "." not in src:
                continue
            owner, attr = dst.split(".", 1)
            target = self.find(owner, required=False)
            incoming = self.find(src.split(".", 1)[0], required=False)
            attr = ALIASES.get(attr, attr)
            if target and target.kind in {"joint", "transform"} and attr in {"t", "r", "s", "m", "opm", "offsetParentMatrix", *[v+axis for v in "trs" for axis in "xyz"]}:
                if not incoming or incoming.kind not in {"animCurveTA", "animCurveTL", "animCurveTU"}:
                    raise ValueError(f"{target.name}.{attr}: unsupported evaluation connection from {src}")
                self.curves[target, attr] = self.curve(incoming)

    def find(self, name, required=True):
        matches = [n for n in self.nodes if n.path == name or n.path.lstrip("|") == str(name).lstrip("|") or n.name == name]
        if len(matches) == 1:
            return matches[0]
        if required:
            raise ValueError(f"Missing or ambiguous MA node {name!r}")
        return None

    def curve(self, node, *, allow_auto_tangents=False):
        pairs = node.array("ktv", 2)
        if not pairs or sorted(pairs) != list(range(len(pairs))):
            raise ValueError(f"{node.name}: invalid animCurve keys")
        weighted = bool(node.value("wgt", True))
        factor = self.angle if node.kind == "animCurveTA" else 1.0
        tangent_factor = 0.01/self.unit if node.kind == "animCurveTL" else 1.0
        arrays = {name: node.array(name) for name in ("kit", "kot", "kix", "kiy", "kox", "koy")}
        result = []
        default_type = int(node.value("tan", 1))
        for i, (time, value) in sorted(pairs.items()):
            value *= factor
            out_type = int(arrays["kot"].get(i, default_type))
            key = {"time": time, "value": value, "interpolation": "CONSTANT" if out_type == 5 else "BEZIER"}
            if out_type == 17:
                raise ValueError(f"{node.name}: step-next tangents are unsupported")
            for incoming, handle in ((True, "handle_left"), (False, "handle_right")):
                prefix = "ki" if incoming else "ko"
                kind = int(arrays[prefix+"t"].get(i, default_type))
                neighbor = i-1 if incoming else i+1
                interval = abs(time-pairs[neighbor][0]) if neighbor in pairs else (abs(time-pairs[1 if i == 0 and len(pairs)>1 else max(0, i-1)][0]) or 1)
                if i in arrays[prefix+"x"] and i in arrays[prefix+"y"]:
                    x, y = float(arrays[prefix+"x"][i]), float(arrays[prefix+"y"][i])*tangent_factor
                    dx, dy = x*self.fps/3, y/3
                    if not weighted:
                        dx = interval/3
                        dy = y/x*dx/self.fps if abs(x) > 1e-12 else 0
                elif kind == 2 and neighbor in pairs:
                    dx = interval/3
                    dy = (pairs[neighbor][1]*factor-value)/3 * (-1 if incoming else 1)
                elif kind in {1, 3, 5}:
                    dx, dy = interval/3, 0.0
                elif allow_auto_tangents and kind in {9, 10}:
                    dx = interval/3
                    if i == 0 or i == len(pairs)-1:
                        dy = 0.0
                    else:
                        t_prev, v_prev = pairs[i-1][0], pairs[i-1][1]*factor
                        t_next, v_next = pairs[i+1][0], pairs[i+1][1]*factor
                        dv_prev, dv_next = value - v_prev, v_next - value
                        if dv_prev * dv_next <= 0.0 or t_next <= t_prev:
                            dy = 0.0
                        else:
                            slope = (v_next - v_prev) / (t_next - t_prev)
                            max_slope = min(
                                3.0 * abs(dv_prev) / max(time - t_prev, 1e-9),
                                3.0 * abs(dv_next) / max(t_next - time, 1e-9),
                            )
                            if abs(slope) > max_slope:
                                slope = math.copysign(max_slope, slope)
                            dy = slope * dx * (-1 if incoming else 1)
                else:
                    raise ValueError(f"{node.name}: automatic tangents without stored vectors require Maya migration")
                sign = -1 if incoming else 1
                key[handle] = [time+sign*abs(dx), value+sign*dy]
            result.append(key)
        for i in range(len(result)-1):
            if int(arrays["kot"].get(i, default_type)) == 2 and int(arrays["kit"].get(i+1, default_type)) == 2:
                result[i]["interpolation"] = "LINEAR"
        modes = {0: "CONSTANT", 1: "LINEAR", 3: "CYCLE", 4: "CYCLE_RELATIVE", 5: "OSCILLATE"}
        return {"keys": result, "pre_infinity": modes[int(node.value("pre", 0))], "post_infinity": modes[int(node.value("pst", 0))]}

    def channels(self, node, frame=None):
        t, r, s = node.vector("t", (0, 0, 0)), node.vector("r", (0, 0, 0)), node.vector("s", (1, 1, 1))
        r = [v*self.angle for v in r]
        if frame is not None:
            for ci, channel in enumerate(ANIMATION_CHANNELS):
                if (node, channel) in self.curves:
                    (t if ci < 3 else r if ci < 6 else s)[ci % 3] = curve_value(self.curves[node, channel], frame)
        return t, r, s

    def matrix(self, node, frame=None):
        t, r, s = self.channels(node, frame)
        order = ORDERS[int(node.value("ro", 0))]
        local = trs(t)
        if node.kind == "joint":
            if node.parent and node.parent.kind == "joint" and node.value("ssc", True):
                local = mul(local, trs(s=[1/v for v in self.channels(node.parent, frame)[2]]))
            local = mul(local, trs(r=[v*self.angle for v in node.vector("jo", (0, 0, 0))]))
        local = mul(local, mul(trs(r=r, order=order), mul(trs(r=[v*self.angle for v in node.vector("ra", (0, 0, 0))]), trs(s=s))))
        if "opm" in node.attrs:
            values = node.attrs["opm"]
            if len(values) != 16:
                raise ValueError("Invalid MA offsetParentMatrix")
            local = mul([[values[j*4+i] for j in range(4)] for i in range(4)], local)
        return mul(self.matrix(node.parent, frame), local) if node.parent and node.value("it", True) else local

    def hierarchy(self):
        joints = [n for n in self.nodes if n.kind == "joint"]
        roots = [n for n in joints if not n.parent or n.parent.kind != "joint"]
        if len(roots) != 1:
            raise ValueError("Basic MA rig import needs exactly one joint root")
        if len(joints) > 127:
            raise ValueError("MA rig exceeds 127 joints plus object_movement")
        ordered = []
        def visit(node):
            ordered.append(node)
            return {"name": node.name, "children": [visit(n) for n in sorted(joints, key=lambda n: n.name) if n.parent is node]}
        return {"name": "object_movement", "children": [visit(roots[0])]}, ordered


def _load_skeleton_spec(skeleton_bundle):
    if isinstance(skeleton_bundle, dict):
        return skeleton_bundle["root"], skeleton_bundle.get("bind_pose", [])
    path = Path(skeleton_bundle)
    if path.is_file() and path.suffix.lower() == ".skeleton":
        import tempfile
        from .legacy_codec import decode_skeleton_to_exchange
        with tempfile.TemporaryDirectory(prefix="xray_ma_skel_") as tmp:
            decode_skeleton_to_exchange(path, tmp, logical_name=path.stem)
            data = json.loads((Path(tmp)/"manifest.json").read_text(encoding="utf-8"))
            return data["root"], data.get("bind_pose", [])
    manifest_file = path/"manifest.json" if path.is_dir() else path
    data = json.loads(manifest_file.read_text(encoding="utf-8"))
    return data["root"], data.get("bind_pose", [])


def trax_clip_bones(source):
    """Return the set of bone names referenced by curves in a standalone Maya Trax .ma clip."""
    scene = Scene(source)
    return {bone for bone, _ in _extract_trax_curves(scene)[0]}


def _extract_trax_curves(scene):
    slot_to_target = {}
    for lib in (n for n in scene.nodes if n.kind == "clipLibrary"):
        cm = lib.attrs.get("cd[0].cm")
        if cm and len(cm) >= 4:
            i = 1
            while i + 2 < len(cm):
                target, _, slot = str(cm[i]), cm[i+1], int(cm[i+2])
                slot_to_target[slot - 1] = target
                i += 3
    link_to_slot = {}
    for src, dst in scene.links:
        m = re.search(r"\.cev\[(\d+)\]\.cevr$", dst)
        if m:
            link_to_slot[src.split(".", 1)[0]] = int(m[1])
    curves = {}
    for node in scene.nodes:
        if node.kind not in {"animCurveTA", "animCurveTL", "animCurveTU"}:
            continue
        mapped = None
        if node.name in link_to_slot:
            mapped = slot_to_target.get(link_to_slot[node.name])
        if not mapped:
            for suffix in ("translateX", "translateY", "translateZ", "rotateX", "rotateY", "rotateZ", "scaleX", "scaleY", "scaleZ"):
                if node.name.endswith("_" + suffix):
                    mapped = node.name[: -len(suffix) - 1] + "." + suffix
                    break
        if not mapped or "." not in mapped:
            continue
        bone_name, raw_attr = mapped.split(".", 1)
        ch = ALIASES.get(raw_attr, raw_attr)
        if ch in ANIMATION_CHANNELS:
            curves[bone_name, ch] = scene.curve(node, allow_auto_tangents=True)
    return curves, [n for n in scene.nodes if n.kind == "animClip"]


def _decode_trax_clip(scene, output, name, coord, skeleton_bundle):
    curves, clips = _extract_trax_curves(scene)
    if not curves:
        raise ValueError("MA Trax clip contains no supported animCurves")
    if skeleton_bundle is None:
        raise ValueError("Standalone Maya Trax animClip requires a target skeleton")
    root, bind_pose = _load_skeleton_spec(skeleton_bundle)
    if root["name"] == "object_movement":
        if len(root.get("children", [])) != 1:
            raise ValueError("Target skeleton must have one root below object_movement")
        skel_root = root["children"][0]
    else:
        skel_root = root
        root = {"name": "object_movement", "children": [skel_root]}
    ordered_names = []
    parents = {}
    def walk(node, parent):
        ordered_names.append(node["name"])
        parents[node["name"]] = parent
        for child in node.get("children", []):
            walk(child, node["name"])
    walk(skel_root, None)
    known = set(ordered_names)
    unused = sorted({b for b, _ in curves if b not in known})
    if unused:
        scene.warnings.append(f"Trax clip curves omitted for bones outside target skeleton: {', '.join(unused)}")
    bind_map = {b["name"]: b for b in bind_pose}
    rest_trs = {}
    world_mats = {}
    for bname in ordered_names:
        if bname in bind_map:
            b = bind_map[bname]
            world_mats[bname] = trs(b["bind_translation"], b["bind_rotation"], b["bind_scale"])
            p = parents[bname]
            local = mul(inverse(world_mats[p]), world_mats[bname]) if p and p in world_mats else world_mats[bname]
            rest_trs[bname] = decompose(local)
        else:
            rest_trs[bname] = ((0.0, 0.0, 0.0), (0.0, 0.0, 0.0), (1.0, 1.0, 1.0))
    keys = [k["time"] for c in curves.values() for k in c["keys"]]
    first, last = math.floor(min(keys)), math.ceil(max(keys))
    if last - first + 1 > 1000000:
        raise ValueError("MA clip exceeds one million frames")
    anim = DccExchangeBundleWriter("animation", name, coord, source_dcc="legacy_ma")
    anim.manifest.update(
        root=root,
        bind_pose=bind_pose,
        frame_start=first,
        frame_count=last - first + 1,
        fps=scene.fps,
        precision_t=0.005,
        precision_r=0.005,
        precision_s=0.005,
        root_already_relative=False,
        is_additive=False,
        tracks=[],
        curves={"time_unit": "frames", "tracks": []},
        authoring={},
    )
    frame_count = last - first + 1
    zero_rows = [((0.0, 0.0, 0.0), (0.0, 0.0, 0.0), (1.0, 1.0, 1.0))] * frame_count
    anim.manifest["tracks"].append({
        "bone_name": "object_movement",
        "channels": {ch: anim.append_span("f32", [zero_rows[f][i // 3][i % 3] for f in range(frame_count)]) for i, ch in enumerate(ANIMATION_CHANNELS)},
    })
    for bname in ordered_names:
        bt, br, bs = rest_trs[bname]
        bone_curves = {ch: curves[bname, ch] for ch in ANIMATION_CHANNELS if (bname, ch) in curves}
        rows = []
        for frame in range(first, last + 1):
            t, r, s = list(bt), list(br), list(bs)
            for ci, ch in enumerate(ANIMATION_CHANNELS):
                if ch in bone_curves:
                    (t if ci < 3 else r if ci < 6 else s)[ci % 3] = curve_value(bone_curves[ch], frame)
            rows.append((*t, *r, *s))
        anim.manifest["tracks"].append({
            "bone_name": bname,
            "channels": {ch: anim.append_span("f32", [r[ci] for r in rows]) for ci, ch in enumerate(ANIMATION_CHANNELS)},
        })
        if bone_curves:
            anim.manifest["curves"]["tracks"].append({"bone_name": bname, "rotation_order": "XYZ", "channels": bone_curves})
        anim.manifest["authoring"][bname] = dict(
            translation=list(bt),
            joint_orient=[0.0, 0.0, 0.0],
            rotate_axis=[0.0, 0.0, 0.0],
            bind_rotation=list(br),
            scale=list(bs),
        )
    anim.write(output / "animation")
    entries = ["animation"]
    output.mkdir(parents=True, exist_ok=True)
    (output / "scene.json").write_text(json.dumps({"source_dcc": "legacy_ma", "assets": entries, "warnings": scene.warnings}, indent=2) + "\n", encoding="utf-8")
    return output


def decode_ma_to_exchange(source, out_dir, *, logical_name=None, skeleton_bundle=None):
    """Return a scene index with validated skeleton, animation and basic meshes."""
    source, output = Path(source).resolve(), Path(out_dir).resolve()
    if source == output or output.is_relative_to(source):
        raise ValueError("MA output must be outside its source")
    scene = Scene(source)
    name = logical_name or source.stem
    coord = {"system": "right_handed_y_up", "unit_scale_to_meters": scene.unit, "uv_origin": "bottom_left", "front_face_winding": "ccw"}
    entries = []
    def writer(kind):
        return DccExchangeBundleWriter(kind, name, coord, source_dcc="legacy_ma")
    def save(result):
        result.write(output/result.manifest["asset_kind"])
        entries.append(result.manifest["asset_kind"])
    joints = [n for n in scene.nodes if n.kind == "joint"]
    meshes = [n for n in scene.nodes if n.kind == "mesh" and not n.value("io", False)]
    if not joints and not meshes and any(n.kind in {"clipLibrary", "animClip"} for n in scene.nodes):
        return _decode_trax_clip(scene, output, name, coord, skeleton_bundle)
    root, ordered, bind = None, [], []
    if joints:
        root, ordered = scene.hierarchy()
        for joint in ordered:
            t, r, s = decompose(scene.matrix(joint))
            bind.append(dict(name=joint.name, bind_translation=t, bind_rotation=r, bind_scale=s))
        skeleton = writer("skeleton")
        skeleton.manifest.update(root=root, bind_pose=bind)
        save(skeleton)
        if scene.curves:
            keys = [k["time"] for curve in scene.curves.values() for k in curve["keys"]]
            first, last = math.floor(min(keys)), math.ceil(max(keys))
            if last-first+1 > 1000000:
                raise ValueError("MA clip exceeds one million frames")
            animation = writer("animation")
            animation.manifest.update(root=root, bind_pose=bind, frame_start=first, frame_count=last-first+1, fps=scene.fps,
                precision_t=0.005, precision_r=0.005, precision_s=0.005, root_already_relative=False,
                is_additive=False, tracks=[], curves={"time_unit": "frames", "tracks": []}, authoring={})
            samples = {n: [] for n in ordered}
            movement = []
            for frame in range(first, last+1):
                matrices = {n: scene.matrix(n, frame) for n in ordered}
                movement.append(decompose(matrices[ordered[0]]))
                for joint in ordered:
                    samples[joint].append(decompose(mul(inverse(matrices[joint.parent]), matrices[joint])) if joint.parent in matrices else ((0, 0, 0), (0, 0, 0), (1, 1, 1)))
            for bone_name, rows in [("object_movement", movement)]+[(n.name, samples[n]) for n in ordered]:
                flat = [(*t, *r, *s) for t, r, s in rows]
                animation.manifest["tracks"].append({"bone_name": bone_name, "channels": {ch: animation.append_span("f32", [r[i] for r in flat]) for i, ch in enumerate(ANIMATION_CHANNELS)}})
            for joint in ordered:
                channels = {ch: curve for (node, ch), curve in scene.curves.items() if node is joint}
                if channels:
                    animation.manifest["curves"]["tracks"].append({"bone_name": joint.name, "rotation_order": ORDERS[int(joint.value("ro", 0))], "channels": channels})
                animation.manifest["authoring"][joint.name] = dict(translation=joint.vector("t", (0, 0, 0)), joint_orient=[v*scene.angle for v in joint.vector("jo", (0, 0, 0))],
                    rotate_axis=[v*scene.angle for v in joint.vector("ra", (0, 0, 0))], bind_rotation=scene.channels(joint)[1], scale=scene.channels(joint)[2])
            save(animation)
    _decode_meshes(scene, output, name, coord, root, ordered, bind, entries)
    if not entries:
        raise ValueError("MA contains no supported joints, animation or polygon meshes")
    output.mkdir(parents=True, exist_ok=True)
    (output/"scene.json").write_text(json.dumps({"source_dcc": "legacy_ma", "assets": entries, "warnings": scene.warnings}, indent=2)+"\n", encoding="utf-8")
    return output


def _decode_meshes(scene, output, name, coord, root, ordered, bind, entries):
    meshes = [n for n in scene.nodes if n.kind == "mesh" and not n.value("io", False)]
    for mesh in meshes:
        points = mesh.array("vt", 3)
        edges = mesh.array("ed", 3)
        # Direct skinCluster -> mesh connections. Geometry may be on an orig shape.
        cluster = next((scene.find(src.split(".")[0], False) for src, dst in scene.links if dst.split(".")[0] in {mesh.name, mesh.path} and dst.endswith((".inMesh", ".i"))), None)
        skinned = bool(cluster and cluster.kind == "skinCluster")
        if cluster and not skinned:
            raise ValueError(f"{mesh.name}: unsupported mesh history {cluster.kind}; export a history-free mesh")
        if skinned:
            original = next((scene.find(src.split(".")[0], False) for src, dst in scene.links if dst.startswith(cluster.name+".ip") or dst.startswith(cluster.name+".input")), None)
            if not points and original:
                points, edges = original.array("vt", 3), original.array("ed", 3)
                mesh_data = original
            else:
                mesh_data = mesh
        else:
            mesh_data = mesh
        if not points:
            raise ValueError(f"{mesh.name}: mesh has no stored vertices (construction history is unsupported)")
        uv = mesh_data.array("uvst[0].uvsp", 2)
        faces = []
        for attr, values in mesh_data.attrs.items():
            if not attr.startswith("fc["):
                continue
            i = 0
            while i < len(values):
                token = values[i]; i += 1
                if token == "f":
                    count = int(values[i]); i += 1
                    edge_ids = [int(v) for v in values[i:i+count]]; i += count
                    vertices = [int(edges[e if e >= 0 else -e-1][0 if e >= 0 else 1]) for e in edge_ids]
                    faces.append([vertices, None])
                elif token == "mu":
                    set_index, count = int(values[i]), int(values[i+1]); i += 2
                    indices = [int(v) for v in values[i:i+count]]; i += count
                    if set_index == 0:
                        faces[-1][1] = indices
                else:
                    raise ValueError(f"{mesh.name}: unsupported polyFaces item {token}")
        if not faces:
            raise ValueError(f"{mesh.name}: no polygon faces")
        result = DccExchangeBundleWriter("skinned_model" if skinned else "static_model", name+"/"+mesh.name, coord, source_dcc="legacy_ma")
        if skinned:
            if not bind:
                raise ValueError("skinCluster has no joint hierarchy")
            result.manifest.update(root=root, bones=bind, skeleton_name=name)
        positions, normals, uvs, indices, bone_indices, bone_weights = [], [], [], [], [], []
        palette = {}
        if skinned:
            for src, dst in scene.links:
                match = re.match(re.escape(cluster.name)+r"\.ma\[(\d+)\]", dst)
                if match:
                    joint = scene.find(src.split(".")[0])
                    palette[int(match[1])] = ordered.index(joint)
        packed_weights = {}
        if skinned:
            for attr, values in cluster.attrs.items():
                match = re.fullmatch(r"wl\[(\d+)(?::(\d+))?\]\.w", attr)
                if match:
                    cursor = 0
                    for vertex in range(int(match[1]), int(match[2] or match[1])+1):
                        count = int(values[cursor]); cursor += 1
                        packed_weights[vertex] = {int(values[cursor+2*k]): float(values[cursor+2*k+1]) for k in range(count)}
                        cursor += 2*count
        transform = scene.matrix(mesh.parent) if mesh.parent else trs()
        for vertices, uv_ids in faces:
            if len(vertices) not in {3, 4} or not uv_ids:
                raise ValueError(f"{mesh.name}: basic MA reader supports triangles/quads with UV set 0")
            for corner_ids in ((0, 1, 2),) if len(vertices) == 3 else ((0, 1, 2), (0, 2, 3)):
                tri = [vertices[c] for c in corner_ids]
                p = [[sum(transform[a][b]*points[v][b] for b in range(3))+transform[a][3] for a in range(3)] for v in tri]
                a, b = [[p[k][j]-p[0][j] for j in range(3)] for k in (1, 2)]
                normal = [a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0]]
                length = math.sqrt(sum(v*v for v in normal))
                if length < 1e-12:
                    raise ValueError(f"{mesh.name}: degenerate polygon")
                for corner, vertex, point in zip(corner_ids, tri, p):
                    indices.append(len(positions)); positions.append(point); normals.append([v/length for v in normal]); uvs.append(uv[uv_ids[corner]])
                    if skinned:
                        influences = packed_weights.get(vertex, cluster.array(f"wl[{vertex}].w"))
                        values = sorted(((palette[k], float(v)) for k, v in influences.items() if k in palette and v > 0), key=lambda v: (-v[1], v[0]))[:4]
                        total = sum(v for _, v in values)
                        if total <= 0:
                            raise ValueError(f"{mesh.name}: vertex {vertex} has no joint weights")
                        bone_indices.append(tuple(i for i, _ in values)+(65535,)*(4-len(values)))
                        bone_weights.append(tuple(v/total for _, v in values)+(0.0,)*(4-len(values)))
        streams = {key: result.append_span(fmt, values) for key, fmt, values in (("positions", "f32x3", positions), ("normals", "f32x3", normals), ("uvs", "f32x2", uvs), ("indices", "u32", indices))}
        if skinned:
            streams.update(bone_indices=result.append_span("u16x4", bone_indices), bone_weights=result.append_span("f32x4", bone_weights))
        shader = next((dst.split(".")[0] for src, dst in scene.links if src.startswith(mesh.name+".iog")), "ma_default")
        result.manifest["surfaces"] = [dict(name=shader, shader_name=shader, mesh_name=mesh.name, material_name=shader, texture_name="", vertex_count=len(positions), index_count=len(indices), streams=streams)]
        relative = result.manifest["asset_kind"]+"_"+mesh.name
        result.write(output/relative)
        entries.append(relative)
