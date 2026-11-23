# SPDX-License-Identifier: GPL-3.0-or-later
"""Blender scene -> neutral exchange v2. No coordinate conversion lives here."""

from contextlib import contextmanager
import math
import json
from pathlib import Path
import tempfile

import bpy
from mathutils import Matrix
from ._dcc_common.exchange import (DccExchangeBundleWriter, ANIMATION_CHANNELS,
    JOINT_DOF_NAMES, validate_identifier, validate_manifest_and_payload)
from ._dcc_common.cook_bridge import run_cook_cli, run_fit_collision_cli

MOVER = "object_movement"
# Imported bone frame = original joint frame @ RCORR (Blender Y = joint X).
RCORR = Matrix(((0, 1, 0, 0), (-1, 0, 0, 0), (0, 0, 1, 0), (0, 0, 0, 1)))


def require(condition, message):
    if not condition:
        raise ValueError(message)


def trs(matrix, label, previous=None):
    t, q, s = matrix.decompose()
    require(all(math.isfinite(v) for row in matrix for v in row), f"{label}: non-finite transform")
    require(all(v > 1e-8 for v in s) and matrix.determinant() > 0, f"{label}: negative or zero scale is unsupported")
    rebuilt = Matrix.LocRotScale(t, q, s)
    require(max(abs(matrix[i][j] - rebuilt[i][j]) for i in range(4) for j in range(4)) < 2e-4,
            f"{label}: sheared transform cannot be represented as TRS")
    e = q.to_euler("XYZ", previous) if previous is not None else q.to_euler("XYZ")
    return {"position": list(t), "rotation": list(e), "scale": list(s)}, e


def coord_space(context, rig=None):
    return {"system": "right_handed_y_up" if rig and rig.xray_axis_space == "Y_UP_RH" else "blender_right_handed_z_up",
            "unit_scale_to_meters": rig.xray_unit_scale if rig else context.scene.unit_settings.scale_length,
            "uv_origin": "bottom_left", "front_face_winding": "ccw"}


def writer(context, kind, name, rig=None):
    validate_identifier(name, "Asset name", allow_slash=True, max_len=220)
    context.view_layer.update()
    return DccExchangeBundleWriter(kind, name, coord_space(context, rig), source_dcc="blender")


def validate(result):
    validate_manifest_and_payload(result.manifest, bytes(result._buf))
    return result


def descendants(root):
    return [root, *root.children_recursive]


def logical_helper(obj):
    return obj.xray_export_name or obj.name


def ancestors(obj):
    while obj.parent:
        obj = obj.parent
        yield obj


def rig_bones(rig):
    require(rig and rig.type == "ARMATURE", "Select an armature")
    roots = [b for b in rig.data.bones if b.parent is None]
    require(len(roots) == 1, "Armature must have exactly one root bone")
    bones = []
    def visit(bone):
        validate_identifier(bone.name, "Bone name", max_len=127)
        require(bone.name != MOVER, "object_movement is reserved for the synthetic root motion track")
        bones.append(bone)
        for child in sorted(bone.children, key=lambda b: b.name):
            visit(child)
    visit(roots[0])
    require(len(bones) + 1 <= 128, f"{len(bones)} bones + object_movement exceeds the 128-track limit")
    return bones


def joint_matrix(rig, matrix, bone=None):
    if bone and bone.xray_has_joint_correction:
        values = bone.xray_joint_correction
        return matrix @ Matrix([values[i:i+4] for i in range(0, 16, 4)])
    return matrix @ RCORR.inverted() if rig.xray_legacy_bone_frames else matrix


def skeleton(context, rig, name):
    bones = rig_bones(rig)
    def node(b):
        return {"name": b.name, "children": [node(c) for c in sorted(b.children, key=lambda b: b.name)]}
    result = writer(context, "skeleton", name, rig)
    result.manifest["root"] = {"name": MOVER, "children": [node(bones[0])]}
    return validate(result)


@contextmanager
def rest_meshes(context, meshes):
    # Evaluate non-skin modifiers while retaining original vertex-group data.
    modifiers = [(m, m.show_viewport) for obj in meshes for m in obj.modifiers if m.type == "ARMATURE"]
    try:
        for modifier, _ in modifiers:
            modifier.show_viewport = False
        context.view_layer.update()
        yield
    finally:
        for modifier, state in modifiers:
            modifier.show_viewport = state
        context.view_layer.update()


def weights(obj, vertex, indices):
    values = [(indices[obj.vertex_groups[g.group].name], g.weight) for g in vertex.groups
              if obj.vertex_groups[g.group].name in indices and g.weight > 0]
    values.sort(key=lambda v: (-v[1], v[0]))
    values = values[:4]
    total = sum(v[1] for v in values)
    require(total > 1e-8, f"{obj.name}: vertex {vertex.index} has no bone weights")
    return (tuple(v[0] for v in values) + (0xFFFF,) * (4-len(values)),
            tuple(v[1]/total for v in values) + (0.0,) * (4-len(values)))


def model(context, root, name, *, rig=None, skeleton_name=""):
    require(root is not None, "Select a static root or armature")
    bones = rig_bones(rig) if rig else []
    if rig:
        meshes = [o for o in context.scene.objects if o.type == "MESH" and any(
            m.type == "ARMATURE" and m.object == rig for m in o.modifiers)]
        validate_identifier(skeleton_name, "Skeleton resource", allow_slash=True, max_len=220)
    else:
        require(root.xray_static_root, "Mark the selected object as Static Root")
        meshes = [o for o in descendants(root) if o.type == "MESH"]
    require(meshes, "No meshes found under this root / bound to this armature")
    result = writer(context, "skinned_model" if rig else "static_model", name, rig or (root if root.xray_preserve_space else None))
    if "xray_rigid_body" in root:
        result.manifest["rigid_body"] = json.loads(root["xray_rigid_body"])
    space = root.matrix_world.inverted()
    bone_indices = {b.name: i for i, b in enumerate(bones)}
    surfaces, collisions = {}, {"COLLISION": ([], []), "HQ_COLLISION": ([], [])}
    with rest_meshes(context, meshes):
        depsgraph = context.evaluated_depsgraph_get()
        for obj in sorted(meshes, key=lambda o: o.name):
            evaluated = obj.evaluated_get(depsgraph)
            mesh = evaluated.to_mesh(preserve_all_data_layers=True, depsgraph=depsgraph)
            try:
                mesh.calc_loop_triangles()
                transform = space @ obj.matrix_world
                require(abs(transform.determinant()) > 1e-10, f"{obj.name}: singular mesh transform")
                normal_matrix = transform.to_3x3().inverted().transposed()
                for triangle in mesh.loop_triangles:
                    loops = list(triangle.loops)
                    if transform.determinant() < 0:
                        loops.reverse()
                    points = [transform @ mesh.vertices[mesh.loops[i].vertex_index].co for i in loops]
                    require((points[1]-points[0]).cross(points[2]-points[0]).length > 1e-10,
                            f"{obj.name}: degenerate triangle {triangle.index}")
                    if obj.xray_mesh_role != "RENDER":
                        positions, indices = collisions[obj.xray_mesh_role]
                        indices.extend(range(len(positions), len(positions)+3))
                        positions.extend(tuple(p) for p in points)
                        continue
                    require(mesh.uv_layers.active is not None, f"{obj.name}: missing active UV map")
                    require(triangle.material_index < len(obj.material_slots) and obj.material_slots[triangle.material_index].material,
                            f"{obj.name}: triangle has no material")
                    material = obj.material_slots[triangle.material_index].material
                    shader = material.xray_shader or material.name
                    validate_identifier(shader, f"{material.name}: shader", max_len=128)
                    metadata = (material.xray_material, material.xray_texture)
                    surface = surfaces.setdefault(shader, {"metadata": metadata, "vertices": [], "indices": [], "lookup": {}})
                    require(surface["metadata"] == metadata, f"Shader {shader}: conflicting material or texture paths")
                    for loop_index, point in zip(loops, points):
                        loop = mesh.loops[loop_index]
                        normal = (normal_matrix @ mesh.corner_normals[loop_index].vector).normalized()
                        uv = tuple(mesh.uv_layers.active.data[loop_index].uv)
                        skin = weights(obj, mesh.vertices[loop.vertex_index], bone_indices) if rig else ()
                        vertex = (tuple(point), tuple(normal), uv, *skin)
                        index = surface["lookup"].get(vertex)
                        if index is None:
                            index = len(surface["vertices"])
                            surface["lookup"][vertex] = index
                            surface["vertices"].append(vertex)
                        surface["indices"].append(index)
            finally:
                evaluated.to_mesh_clear()
    result.manifest["surfaces"] = []
    for shader, surface in sorted(surfaces.items()):
        vertices = surface["vertices"]
        streams = {key: result.append_span(fmt, [v[i] for v in vertices]) for i, (key, fmt) in enumerate(
            (("positions", "f32x3"), ("normals", "f32x3"), ("uvs", "f32x2")))}
        streams["indices"] = result.append_span("u32", surface["indices"])
        if rig:
            streams["bone_indices"] = result.append_span("u16x4", [v[3] for v in vertices])
            streams["bone_weights"] = result.append_span("f32x4", [v[4] for v in vertices])
        result.manifest["surfaces"].append({"name": shader, "mesh_name": root.name, "shader_name": shader,
            "material_name": surface["metadata"][0], "texture_name": surface["metadata"][1],
            "vertex_count": len(vertices), "index_count": len(surface["indices"]), "streams": streams})
    for role, key in (("COLLISION", "collision"), ("HQ_COLLISION", "hq_collision")):
        positions, indices = collisions[role]
        if positions:
            result.manifest[key] = {"vertex_count": len(positions), "index_count": len(indices),
                "positions": result.append_span("f32x3", positions), "indices": result.append_span("u32", indices)}
    if rig:
        result.manifest["skeleton_name"] = skeleton_name
        result.manifest["bones"] = []
        for bone in bones:
            values, _ = trs(joint_matrix(rig, bone.matrix_local, bone), bone.name)
            result.manifest["bones"].append({"name": bone.name, "bind_translation": values["position"],
                "bind_rotation": values["rotation"], "bind_scale": values["scale"]})
    locators = []
    for obj in sorted(context.scene.objects, key=lambda o: o.name):
        if obj.xray_kind != "LOCATOR":
            continue
        settings = obj.xray_locator
        if rig:
            if settings.armature != rig:
                continue
        elif obj not in descendants(root):
            continue
        matrix = space @ obj.matrix_world
        bone_idx = -1
        if settings.bone_name:
            require(rig and settings.bone_name in bone_indices, f"{obj.name}: unknown locator bone {settings.bone_name}")
            bone_idx = bone_indices[settings.bone_name]
            frame = rig.pose.bones[settings.bone_name].matrix if obj.parent == rig and obj.parent_type == "BONE" else bones[bone_idx].matrix_local
            matrix = joint_matrix(rig, frame, bones[bone_idx]).inverted() @ matrix
        values, _ = trs(matrix, obj.name)
        locators.append({"name": logical_helper(obj), "bone_idx": bone_idx, "position": values["position"], "rotation": values["rotation"]})
    result.manifest["locators"] = locators
    return validate(result)


def action_slot(action, rig):
    slots = [s for s in action.slots if s.target_id_type == "OBJECT"]
    require(len(slots) == 1, f"Action {action.name}: expected one Object slot; split multi-object Actions before export")
    return slots[0]


def animation(context, rig, name, *, action=None, local_center=None, additive=None,
              precision_t=None, precision_r=None, precision_s=None):
    bones = rig_bones(rig)
    result = writer(context, "animation", name, rig)
    precision_t, precision_r, precision_s = [value if value is not None else getattr(action, "xray_precision_"+ch) if action and action.xray_sample_count else 0.005
        for value, ch in zip((precision_t, precision_r, precision_s), "trs")]
    scene = context.scene
    first, last = (math.floor(action.frame_range[0]), math.ceil(action.frame_range[1])) if action else (scene.frame_start, scene.frame_end)
    count = last-first+1
    if action and action.xray_sample_count:
        first, count = action.xray_frame_start, action.xray_sample_count
        last = first+count-1
    require(1 <= last-first+1 <= 1_000_000, "Clip frame range is empty or exceeds one million frames")
    for value in (precision_t, precision_r, precision_s):
        require(math.isfinite(value) and value > 0, "Clip precisions must be finite and positive")
    slot = action_slot(action, rig) if action else None
    had_data = rig.animation_data is not None
    data = rig.animation_data_create()
    old = (data.action, data.action_slot, data.use_nla, scene.frame_current, scene.frame_subframe)
    samples = [[] for _ in range(len(bones)+1)]
    previous = [None] * len(samples)
    try:
        if action:
            data.action = action
            data.action_slot = slot
            data.use_nla = False
        for index in range(count):
            frame = first+index
            scene.frame_set(math.floor(frame), subframe=frame % 1)
            depsgraph = context.evaluated_depsgraph_get()
            evaluated = rig.evaluated_get(depsgraph)
            matrices = [joint_matrix(rig, evaluated.pose.bones[b.name].matrix, b) for b in bones]
            center = Matrix.Identity(4)
            if local_center:
                center = evaluated.matrix_world.inverted() @ local_center.evaluated_get(depsgraph).matrix_world
            local = [center.inverted() @ matrices[0], Matrix.Identity(4)]
            by_name = {b.name: m for b, m in zip(bones, matrices)}
            local.extend(by_name[b.parent.name].inverted() @ m for b, m in zip(bones[1:], matrices[1:]))
            for i, matrix in enumerate(local):
                values, previous[i] = trs(matrix, f"Frame {frame}, track {i}", previous[i])
                samples[i].append((*values["position"], *values["rotation"], *values["scale"]))
    finally:
        data.action = old[0]
        if old[0]:
            data.action_slot = old[1]
        data.use_nla = old[2]
        scene.frame_set(old[3], subframe=old[4])
        if not had_data:
            rig.animation_data_clear()
    result.manifest.update(fps=action.xray_fps if action and action.xray_sample_count else scene.render.fps/scene.render.fps_base, frame_count=count, frame_start=first,
        is_additive=bool(action.xray_additive if additive is None and action else additive),
        precision_t=precision_t, precision_r=precision_r, precision_s=precision_s,
        root_already_relative=bool(action and action.xray_root_already_relative))
    result.manifest["tracks"] = [{"bone_name": name, "channels": {
        ch: result.append_span("f32", [row[i] for row in values]) for i, ch in enumerate(ANIMATION_CHANNELS)}}
        for name, values in zip([MOVER]+[b.name for b in bones], samples)]
    return validate(result)


def physics(context, root, name):
    require(root is not None, "Select the root containing physics helpers")
    context.view_layer.update()
    objects = descendants(root)
    # A body root is itself a physical transform; a container root defines model space.
    space = Matrix.Identity(4) if root.xray_kind == "BODY" else root.matrix_world.inverted()
    bodies = [o for o in objects if o.xray_kind == "BODY"]
    require(bodies, "No physics bodies found under the selected root")
    rig = next((o for o in [root, *ancestors(root)] if o.type == "ARMATURE"), None) if root.xray_kind != "BODY" else None
    if root.xray_preserve_space:
        rig = root
    result = writer(context, "physics", name, rig)
    result.manifest["bodies"], result.manifest["joints"] = [], []
    for obj in sorted(bodies, key=lambda o: o.name):
        values, _ = trs(space @ obj.matrix_world, obj.name)
        cols = []
        for child in obj.children:
            if child.xray_kind == "COLLISION":
                value, _ = trs(obj.matrix_world.inverted() @ child.matrix_world, child.name)
                cols.append({"type": child.xray_collision.shape_type, **value})
        require(cols, f"{obj.name}: body has no collision child")
        result.manifest["bodies"].append({"name": logical_helper(obj), **values, "anim_joint": obj.xray_body.anim_joint,
            "mass": obj.xray_body.mass, "mass_center": list(obj.xray_body.mass_center), "collision": cols})
    constraints = [o for o in objects if o.xray_kind == "CONSTRAINT"]
    explicit_bodies = {o.xray_constraint.body1 for o in constraints}
    joints = constraints + [b for b in bodies if b not in explicit_bodies]
    for obj in sorted(joints, key=lambda o: o.name):
        settings = obj.xray_constraint
        body1 = settings.body1 or (obj if obj.xray_kind == "BODY" else None)
        body0 = settings.body0 or (obj.parent if obj.xray_kind == "BODY" and obj.parent in bodies else None)
        require(body1 in bodies, f"{obj.name}: Body 1 must reference an exported body")
        require(body0 is None or body0 in bodies, f"{obj.name}: Body 0 is outside this physics asset")
        require(body0 != body1, f"{obj.name}: joint connects a body to itself")
        value, _ = trs(space @ obj.matrix_world, obj.name)
        joint = {"name": settings.export_name or (logical_helper(obj) + "_joint" if obj.xray_kind == "BODY" else logical_helper(obj)),
                 "type": "fixed" if body0 and settings.joint_type == "rigid" else settings.joint_type,
                 **value, "body1": logical_helper(body1)}
        if body0:
            joint["body0"] = logical_helper(body0)
        for key in JOINT_DOF_NAMES:
            dof = getattr(settings, key)
            require(not dof.limits_enable_state or dof.low_limit <= dof.hi_limit, f"{obj.name}: {key} lower limit exceeds upper limit")
            joint[key] = {k: getattr(dof, k) for k in ("enable_state", "limits_enable_state", "low_limit", "hi_limit")}
        result.manifest["joints"].append(joint)
    names = [j["name"] for j in result.manifest["joints"]]
    require(len(names) == len(set(names)), "Duplicate physics joint names; rename the conflicting helper")
    # The engine skips joints[0]: it is metadata for the free root body, not
    # an articulated joint. Alphabetical helper order can otherwise drop a limb.
    root_joints = [j for j in result.manifest["joints"] if "body0" not in j]
    require(len(root_joints) == 1, "Physics needs one root body; connect all other bodies with Body 0 links")
    root_joint = root_joints[0]
    require(root_joint["type"] == "rigid", "The root body joint must use the reserved Rigid type")
    root_body = root_joint.pop("body1")
    result.manifest["joints"] = [root_joint]+[j for j in result.manifest["joints"] if j is not root_joint]
    result.manifest["bodies"].sort(key=lambda b: (b["name"] != root_body, b["name"]))
    return validate(result)


def fit_collision(context, objects, shape, cli):
    context.view_layer.update()
    points = []
    depsgraph = context.evaluated_depsgraph_get()
    for obj in objects:
        if obj.type != "MESH":
            continue
        evaluated = obj.evaluated_get(depsgraph)
        mesh = evaluated.to_mesh()
        try:
            points.extend(tuple(obj.matrix_world @ v.co) for v in mesh.vertices)
        finally:
            evaluated.to_mesh_clear()
    require(points, "Select at least one mesh to fit")
    result = writer(context, "physics", "fit")
    result.manifest["fit_requests"] = [{"name": "fit", "shape": shape, "points": result.append_span("f32x3", points)}]
    with tempfile.TemporaryDirectory(prefix="xray_fit_") as directory:
        result.write(directory)
        return run_fit_collision_cli(directory, cli_path=cli)[0]["source"]


def export_and_cook(result, directory, cli=None):
    directory = Path(directory)
    exchange = directory / "exchange"
    result.write(exchange)
    if cli:
        run_cook_cli(exchange, directory / "raw", converted_dir=directory / "converted", cli_path=cli)
    return directory
