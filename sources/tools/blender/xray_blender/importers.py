# SPDX-License-Identifier: GPL-3.0-or-later
"""Transactional exchange/legacy import; all host math stays in this frontend."""

import json
import math
from pathlib import Path
import tempfile

import bpy
from mathutils import Euler, Matrix, Vector
from . import exporters as ex
from ._dcc_common.exchange import validate_bundle, unpack_span, validate_identifier
from ._dcc_common.legacy_codec import decompile_asset
from ._dcc_common.ma_reader import decode_ma_to_exchange

U = Matrix(((1, 0, 0, 0), (0, 0, -1, 0), (0, 1, 0, 0), (0, 0, 0, 1)))


class Bundle:
    def __init__(self, directory=None, *, result=None):
        if result:
            self.manifest, self.payload = result.manifest, memoryview(result._buf)
        else:
            self.manifest = validate_bundle(directory)
            self.payload = memoryview((Path(directory)/"payload.bin").read_bytes())
        self.space = self.manifest["coord_space"]
        self.system = self.space["system"]
        self.unit = self.space["unit_scale_to_meters"]
        self.engine = self.system == "xray_left_handed_y_up"
        self.native = self.system == "blender_right_handed_z_up"
        if "root" in self.manifest:
            hierarchy(self.manifest["root"])
        for key in ("bind_pose",):
            if key in self.manifest:
                records = self.manifest[key]
                ex.require(isinstance(records, list) and len(records) <= 127, "Invalid imported bind pose")
                seen = set()
                for bone in records:
                    ex.require(isinstance(bone, dict), "Imported bind entries must be objects")
                    name = validate_identifier(bone.get("name"), "Bind bone")
                    ex.require(name not in seen, "Duplicate imported bind bone")
                    seen.add(name)
                    for field in ("bind_translation", "bind_rotation", "bind_scale"):
                        values = bone.get(field)
                        ex.require(isinstance(values, list) and len(values) == 3 and all(isinstance(v, (int, float)) and not isinstance(v, bool) and math.isfinite(v) for v in values), "Invalid imported "+field)
                    ex.require(all(v > 0 for v in bone["bind_scale"]), "Imported bind scale must be positive")

    def span(self, span):
        return unpack_span(span, self.payload, None, None, "Import stream")

    def vector(self, value):
        v = Vector(value)
        return U.inverted().to_3x3() @ v if self.native else Vector((v.x, v.y, -v.z)) if self.engine else v

    def matrix(self, values):
        t = values.get("position", values.get("bind_translation", (0, 0, 0)))
        r = values.get("rotation", values.get("bind_rotation", (0, 0, 0)))
        s = values.get("scale", values.get("bind_scale", (1, 1, 1)))
        if self.engine:
            t, r = (t[0], t[1], -t[2]), (r[0], r[1], -r[2])
        matrix = Matrix.LocRotScale(Vector(t), Euler(r, "XYZ"), Vector(s))
        return U.inverted() @ matrix @ U if self.native else matrix

    def for_rig(self, rig, matrix):
        matrix = matrix.copy()
        matrix.translation *= self.unit/rig.xray_unit_scale
        return U @ matrix @ U.inverted() if rig.xray_axis_space == "Z_UP_RH" else matrix

    def point_for_rig(self, rig, point):
        value = self.vector(point)*(self.unit/rig.xray_unit_scale)
        return U.to_3x3() @ value if rig.xray_axis_space == "Z_UP_RH" else value


def new_object(collection, name, data=None, parent=None, matrix=None):
    obj = bpy.data.objects.new(name, data)
    collection.objects.link(obj)
    obj.xray_export_name = name
    obj.parent = parent
    if matrix is not None:
        obj.matrix_local = matrix
    return obj


def imported_root(context, collection, bundle, role):
    obj = new_object(collection, bundle.manifest["name"].split("/")[-1])
    obj.xray_kind = role
    obj.xray_axis_space, obj.xray_unit_scale, obj.xray_preserve_space = "Y_UP_RH", bundle.unit, True
    obj.matrix_world = U @ Matrix.Diagonal((bundle.unit/context.scene.unit_settings.scale_length,)*3+(1,))
    if "rigid_body" in bundle.manifest:
        obj["xray_rigid_body"] = json.dumps(bundle.manifest["rigid_body"])
    return obj


def hierarchy(root):
    ex.require(isinstance(root, dict), "Imported hierarchy must be an object")
    if root["name"] == ex.MOVER:
        ex.require(len(root.get("children", [])) == 1, "Skeleton must have one root below object_movement")
        root = root["children"][0]
    ordered, seen = [], set()
    def visit(node, parent):
        ex.require(isinstance(node, dict) and isinstance(node.get("children", []), list), "Invalid imported hierarchy node")
        name = validate_identifier(node["name"], "Imported bone", max_len=127)
        ex.require(name not in seen and name != ex.MOVER, f"Duplicate or reserved bone {name}")
        seen.add(name)
        ordered.append((name, parent))
        for child in node.get("children", []):
            visit(child, name)
    visit(root, None)
    ex.require(1 <= len(ordered) <= 127, "Imported rig exceeds 127 bones plus object_movement")
    return ordered


def create_rig(context, collection, bundle, root, bind):
    ordered = hierarchy(root)
    matrices = {bone["name"]: bundle.matrix(bone) for bone in bind}
    ex.require(set(matrices).issubset({n for n, _ in ordered}), "Bind pose contains bones outside the skeleton")
    placeholder = not bool(matrices)
    for name, parent in ordered:
        if name not in matrices:
            matrices[name] = matrices[parent] @ Matrix.Translation((0.1/bundle.unit, 0, 0)) if parent else Matrix.Identity(4)
    rig = new_object(collection, bundle.manifest["name"].split("/")[-1]+"_rig", bpy.data.armatures.new("X-Ray Rig"))
    rig.xray_axis_space, rig.xray_unit_scale, rig.xray_legacy_bone_frames = "Y_UP_RH", bundle.unit, True
    rig.matrix_world = U @ Matrix.Diagonal((bundle.unit/context.scene.unit_settings.scale_length,)*3+(1,))
    for obj in context.selected_objects:
        obj.select_set(False)
    rig.select_set(True)
    context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")
    try:
        for name, parent in ordered:
            bone = rig.data.edit_bones.new(name)
            bone.head, bone.tail = (0, 0, 0), (0, 0.1/bundle.unit, 0)
            if parent:
                bone.parent = rig.data.edit_bones[parent]
            t, q, s = matrices[name].decompose()
            bone.matrix = Matrix.LocRotScale(t, q, Vector((1, 1, 1))) @ ex.RCORR
    finally:
        bpy.ops.object.mode_set(mode="OBJECT")
    for name, _ in ordered:
        bone = rig.data.bones[name]
        correction = bone.matrix_local.inverted() @ matrices[name]
        bone.xray_joint_correction = tuple(v for row in correction for v in row)
        bone.xray_has_joint_correction = True
    rig["xray_placeholder_bind"] = placeholder
    return rig


def mesh_object(collection, parent, name, points, indices, normals=None, uvs=None):
    data = bpy.data.meshes.new(name)
    triangles = [indices[i:i+3] for i in range(0,len(indices),3)]
    faces = [face for face in triangles if len(set(face))==3]
    # from_pydata accepts invalid topology; custom normals can then crash native
    # Blender code on legacy triangles with repeated vertex indices. Preserve
    # vertex order (skin weights/UVs/normals) while removing empty triangles.
    data.from_pydata(points, [], faces)
    repaired = data.validate(clean_customdata=False)
    ex.require(len(data.vertices)==len(points), "Mesh validation changed source vertex indexing")
    if uvs:
        data.uv_layers.new(name="UVMap")
        for loop, corner in zip(data.loops, data.uv_layers.active.data):
            corner.uv = uvs[loop.vertex_index]
    if normals and data.loops:
        for polygon in data.polygons:
            polygon.use_smooth = True
        data.normals_split_custom_set([normals[loop.vertex_index] for loop in data.loops])
    obj = new_object(collection, name, data, parent)
    if len(faces)!=len(triangles) or repaired:
        obj["xray_import_topology_repairs"] = json.dumps(dict(
            removed_degenerate_triangles=len(triangles)-len(faces),
            blender_validation_changed=repaired,source_triangles=len(triangles),
            imported_triangles=len(data.polygons)))
    return obj


def import_model(context, collection, bundle, rig, skeleton):
    manifest = bundle.manifest
    skinned = manifest["asset_kind"] == "skinned_model"
    if skinned:
        if rig is None:
            root = manifest.get("root") or (skeleton.manifest["root"] if skeleton else None)
            ex.require(root is not None, "Choose a skeleton file or a matching armature for this skinned model")
            rig = create_rig(context, collection, bundle, root, manifest["bones"])
        for bone in manifest["bones"]:
            ex.require(bone["name"] in rig.data.bones, f"Skeleton lacks bone {bone['name']}")
            actual = ex.joint_matrix(rig, rig.data.bones[bone["name"]].matrix_local, rig.data.bones[bone["name"]])
            expected = bundle.for_rig(rig, bundle.matrix(bone))
            ex.require(max(abs(actual[i][j]-expected[i][j]) for i in range(4) for j in range(4)) < 0.001,
                f"{bone['name']}: bind pose differs from the selected armature; import the skin with its skeleton into a new rig")
        root = rig
    else:
        root = imported_root(context, collection, bundle, "STATIC_ROOT")
    for surface in manifest["surfaces"]:
        streams = surface["streams"]
        positions = [bundle.point_for_rig(rig, v) if skinned else bundle.vector(v) for v in bundle.span(streams["positions"])]
        normals = [bundle.vector(v) for v in bundle.span(streams["normals"])]
        if skinned and rig.xray_axis_space == "Z_UP_RH":
            normals = [U.to_3x3() @ v for v in normals]
        uvs = [tuple(v) for v in bundle.span(streams["uvs"])]
        if bundle.space["uv_origin"] == "top_left":
            uvs = [(u, 1-v) for u, v in uvs]
        indices = bundle.span(streams["indices"])
        if bundle.engine:
            indices = [v for i in range(0, len(indices), 3) for v in (indices[i], indices[i+2], indices[i+1])]
        obj = mesh_object(collection, root, surface["name"], positions, indices, normals, uvs)
        material = bpy.data.materials.new(surface["name"])
        material.xray_shader, material.xray_material, material.xray_texture = surface.get("shader_name", surface["name"]), surface.get("material_name", ""), surface.get("texture_name", "")
        obj.data.materials.append(material)
        if skinned:
            groups = [obj.vertex_groups.new(name=b["name"]) for b in manifest["bones"]]
            for index, (bones, values) in enumerate(zip(bundle.span(streams["bone_indices"]), bundle.span(streams["bone_weights"]))):
                for bone, weight in zip(bones, values):
                    if bone != 65535 and weight > 0:
                        groups[bone].add([index], weight, "REPLACE")
            obj.modifiers.new("X-Ray Skin", "ARMATURE").object = rig
    for key, role in (("collision", "COLLISION"), ("hq_collision", "HQ_COLLISION")):
        if key in manifest:
            values = manifest[key]
            indices = bundle.span(values["indices"])
            if bundle.engine:
                indices = [v for i in range(0, len(indices), 3) for v in (indices[i], indices[i+2], indices[i+1])]
            obj = mesh_object(collection, root, key, [bundle.point_for_rig(rig, p) if skinned else bundle.vector(p) for p in bundle.span(values["positions"])], indices)
            obj.xray_mesh_role = role
    for locator in manifest.get("locators", []):
        matrix = bundle.matrix(locator)
        bind_frame = None
        if skinned:
            matrix = bundle.for_rig(rig, matrix)
            index = locator.get("bone_idx", -1)
            name = manifest["bones"][index]["name"] if index >= 0 else ""
            if name:
                bind_frame = ex.joint_matrix(rig, rig.data.bones[name].matrix_local, rig.data.bones[name])
        obj = new_object(collection, locator["name"], parent=root, matrix=matrix)
        if bind_frame is not None:
            obj.matrix_parent_inverse = bind_frame
        obj.xray_kind = "LOCATOR"
        if skinned:
            obj.xray_locator.armature, obj.xray_locator.bone_name = rig, name
    return root


def import_physics(context, collection, bundle):
    root = imported_root(context, collection, bundle, "NONE")
    if bundle.manifest.get("import_metadata"):
        root["xray_legacy_import_metadata"] = json.dumps(bundle.manifest["import_metadata"])
    bodies = {}
    for body in bundle.manifest.get("bodies", []):
        obj = new_object(collection, body["name"], parent=root, matrix=bundle.matrix(body))
        obj.xray_kind = "BODY"
        obj.xray_body.anim_joint, obj.xray_body.mass, obj.xray_body.mass_center = body["anim_joint"], body["mass"], bundle.vector(body["mass_center"])
        bodies[body["name"]] = obj
        for i, shape in enumerate(body["collision"]):
            ex.require("points" not in shape, "Fit collision point requests before importing physics helpers")
            col = new_object(collection, body["name"]+f"_shape{i}", parent=obj, matrix=bundle.matrix(shape))
            col.xray_kind, col.xray_collision.shape_type, col.empty_display_size = "COLLISION", shape["type"], 0.5
    for index, joint in enumerate(bundle.manifest.get("joints", [])):
        if index == 0 and not joint.get("body0") and not joint.get("body1"):
            ex.require(bundle.manifest["bodies"], "Physics root slot has no body")
            obj = bodies[bundle.manifest["bodies"][0]["name"]]
        else:
            obj = new_object(collection, joint["name"], parent=root, matrix=bundle.matrix(joint))
            obj.xray_kind = "CONSTRAINT"
            ex.require(joint.get("body0") in bodies and joint.get("body1") in bodies, "Physics joint references a missing body")
            obj.xray_constraint.body0, obj.xray_constraint.body1 = bodies[joint["body0"]], bodies[joint["body1"]]
        settings = obj.xray_constraint
        settings.export_name = joint["name"]
        settings.joint_type = joint["type"]
        for key in ex.JOINT_DOF_NAMES:
            for field, value in joint[key].items():
                setattr(getattr(settings, key), field, value)
    return root


def load_path(source, temporary, fps=30, skeleton_bundle=None):
    source = Path(source).resolve()
    if source.name in {"manifest.json", "scene.json"}:
        source = source.parent
    if source.is_dir() and ((source/"manifest.json").is_file() or (source/"scene.json").is_file()):
        return source
    if source.suffix.lower() == ".mb":
        raise ValueError("Binary .mb requires maintainer Maya migration; ask the author for .ma, FBX or USD")
    if source.suffix.lower() == ".ma":
        return decode_ma_to_exchange(source, temporary, skeleton_bundle=skeleton_bundle)
    return decompile_asset(source, temporary, fps=fps)


def import_asset(context, source, *, skeleton_path=None, rig=None, fps=30):
    """Import into a fresh collection; roll back every new datablock on failure."""
    ex.require(context.mode == "OBJECT", "Switch to Object Mode before importing")
    owners = ("objects", "meshes", "armatures", "materials", "actions", "collections")
    before = {name: set(getattr(bpy.data, name)) for name in owners}
    old_active, old_selected = context.view_layer.objects.active, list(context.selected_objects)
    old_frame = context.scene.frame_current, context.scene.frame_subframe
    old_fps = context.scene.render.fps, context.scene.render.fps_base
    old_action = (rig.animation_data.action, rig.animation_data.action_slot) if rig and rig.animation_data else None
    old_nla = rig.animation_data.use_nla if rig and rig.animation_data else True
    old_rig = rig
    old_poses = {p.name: (p.rotation_mode, tuple(p.location), tuple(p.rotation_euler), tuple(p.rotation_quaternion), tuple(p.scale)) for p in rig.pose.bones} if rig else {}
    had_animation = bool(rig and rig.animation_data)
    try:
        with tempfile.TemporaryDirectory(prefix="xray_import_") as directory:
            temporary = Path(directory)
            skeleton_spec = skeleton_path
            if skeleton_spec is None and rig is not None:
                def rig_node(b):
                    return {"name": b.name, "children": [rig_node(c) for c in sorted(b.children, key=lambda x: x.name)]}
                root_bone = ex.rig_bones(rig)[0]
                skeleton_spec = {"root": {"name": ex.MOVER, "children": [rig_node(root_bone)]}, "bind_pose": []}
            path = load_path(source, temporary/"asset", fps, skeleton_bundle=skeleton_spec)
            skeleton = Bundle(load_path(skeleton_path, temporary/"skeleton")) if skeleton_path else None
            collection = bpy.data.collections.new("X-Ray Import")
            context.scene.collection.children.link(collection)
            paths = [path]
            if (path/"scene.json").is_file():
                entries = json.loads((path/"scene.json").read_text(encoding="utf-8"))["assets"]
                paths = [path/entry for entry in entries]
                ex.require(all(p.resolve().is_relative_to(path.resolve()) for p in paths), "Scene index contains an invalid path")
            roots, reports = [], []
            for bundle_path in paths:
                bundle = Bundle(bundle_path)
                manifest = bundle.manifest
                kind = manifest["asset_kind"]
                if kind == "skinned_model" and rig is None and skeleton is None and "root" not in manifest:
                    from .preferences import resources_path
                    logical = manifest["skeleton_name"]
                    leaf = Path(logical).name.removesuffix(".skeleton")+".skeleton"
                    candidates = [Path(source).resolve().parent/leaf]
                    resources = resources_path(context)
                    if resources:
                        for prefix in ("exported/animations", "animations", "exported", ""):
                            candidates.append(resources/prefix/(logical if logical.endswith(".skeleton") else logical+".skeleton"))
                        candidates.append(resources/"exported/animations/skeletons"/leaf)
                    candidate = next((p for p in candidates if p.is_file()), None)
                    if candidate:
                        skeleton = Bundle(load_path(candidate, temporary/"related_skeleton"))
                if kind == "skeleton":
                    rig = create_rig(context, collection, bundle, manifest["root"], manifest.get("bind_pose", []))
                    obj = rig
                elif kind in {"static_model", "skinned_model"}:
                    obj = import_model(context, collection, bundle, rig, skeleton)
                    if kind == "skinned_model":
                        rig = obj
                elif kind == "animation":
                    from .keyframes import import_animation
                    if rig is None:
                        root = manifest.get("root") or (skeleton.manifest["root"] if skeleton else None)
                        ex.require(root is not None, "Select a matching armature or choose the clip's skeleton file")
                        rig = create_rig(context, collection, bundle, root, manifest.get("bind_pose", []))
                    report = import_animation(context, rig, bundle)
                    reports.append(report)
                    obj = rig
                elif kind == "physics":
                    obj = import_physics(context, collection, bundle)
                if obj not in roots:
                    roots.append(obj)
            collection["xray_import_report"] = json.dumps(reports)
            warnings=[dict(mesh=o.name,**json.loads(o["xray_import_topology_repairs"]))
                for o in collection.objects if "xray_import_topology_repairs" in o]
            collection["xray_import_warnings"] = json.dumps(warnings)
            for obj in context.selected_objects:
                obj.select_set(False)
            roots[-1].select_set(True)
            context.view_layer.objects.active = roots[-1]
            settings = context.scene.xray_export
            settings.kind, settings.name = kind, manifest["name"]
            if kind == "animation":
                for key in "trs":
                    setattr(settings, "precision_"+key, manifest.get("precision_"+key, 0.005))
            elif kind == "skinned_model":
                settings.skeleton_name = manifest["skeleton_name"]
            return {"roots": roots, "rig": rig, "reports": reports, "collection": collection,"warnings":warnings}
    except Exception:
        if context.mode != "OBJECT":
            bpy.ops.object.mode_set(mode="OBJECT")
        if old_rig and old_rig in before["objects"]:
            if old_action:
                old_rig.animation_data.action, old_rig.animation_data.action_slot = old_action
            elif not had_animation:
                old_rig.animation_data_clear()
        if old_rig:
            if old_rig.animation_data:
                old_rig.animation_data.use_nla = old_nla
            for name, (mode, t, e, q, s) in old_poses.items():
                pose = old_rig.pose.bones[name]
                pose.rotation_mode, pose.location, pose.rotation_euler, pose.rotation_quaternion, pose.scale = mode, t, e, q, s
        for name in owners:
            data = getattr(bpy.data, name)
            for block in list(data):
                if block not in before[name]:
                    data.remove(block, do_unlink=True)
        for obj in old_selected:
            obj.select_set(True)
        context.view_layer.objects.active = old_active
        context.scene.render.fps, context.scene.render.fps_base = old_fps
        context.scene.frame_set(old_frame[0], subframe=old_frame[1])
        raise
