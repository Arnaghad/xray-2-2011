"""Skeleton, skinned model, and animation extractors using Maya Python API 2.0."""

from __future__ import annotations

import math
from pathlib import Path
from typing import Any

if __package__:
    from . import constants
    from .static_extractor import (
        _extract_locators,
        _extract_render_surfaces,
        _get_dag_path,
        _import_exchange_writer,
        _resolve_om2,
        _scene_internal_unit_to_meters,
        _walk_subtree_shapes,
    )
else:
    import constants
    from static_extractor import (
        _extract_locators,
        _extract_render_surfaces,
        _get_dag_path,
        _import_exchange_writer,
        _resolve_om2,
        _scene_internal_unit_to_meters,
        _walk_subtree_shapes,
    )


def _resolve_om2_anim(om2_anim: Any | None) -> Any:
    if om2_anim is not None:
        return om2_anim
    import maya.api.OpenMayaAnim as maya_om2_anim  # type: ignore

    return maya_om2_anim


def _short_joint_name(dag: Any, om2: Any) -> str:
    fn = om2.MFnDagNode(dag)
    return str(fn.name()).split("|")[-1].split(":")[-1]


def _matrix_to_trs(mat: Any, om2: Any) -> tuple[list[float], list[float], list[float]]:
    xform = om2.MTransformationMatrix(mat)
    t = xform.translation(om2.MSpace.kTransform)
    rot = xform.rotation(asQuaternion=False)
    xyz_order = getattr(om2.MEulerRotation, "kXYZ", None)
    if xyz_order is not None and hasattr(rot, "order") and rot.order != xyz_order and hasattr(rot, "reorder"):
        rot = rot.reorder(xyz_order)
    scale = xform.scale(om2.MSpace.kTransform)
    return (
        [float(t.x), float(t.y), float(t.z)],
        [float(rot.x), float(rot.y), float(rot.z)],
        [float(scale[0]), float(scale[1]), float(scale[2])],
    )


def _top_joint_from_dag(joint_dag: Any, om2: Any) -> Any:
    cur = om2.MDagPath(joint_dag)
    for _ in range(128):
        parent_obj = None
        try:
            fn = om2.MFnDagNode(cur)
            if fn.parentCount() > 0:
                p = fn.parent(0)
                if p.hasFn(om2.MFn.kJoint):
                    parent_obj = p
        except Exception:
            parent_obj = None
        if parent_obj is None:
            return cur
        cur = om2.MDagPath(cur)
        cur.pop()
    return cur


def _discover_joint_root(root_name: str | None, om2: Any) -> Any:
    if root_name:
        dag = _get_dag_path(root_name, om2)
        if dag is None:
            raise RuntimeError(f"Joint root node not found: {root_name}")
        if dag.node().hasFn(om2.MFn.kJoint):
            return _top_joint_from_dag(dag, om2)
        joints = _walk_subtree_shapes(dag, om2.MFn.kJoint, om2)
        if not joints:
            raise RuntimeError(f"No joints found under root: {root_name}")
        return _top_joint_from_dag(joints[0], om2)

    sel = om2.MGlobal.getActiveSelectionList() if hasattr(om2.MGlobal, "getActiveSelectionList") else om2.MSelectionList()
    if sel.length() > 0:
        dag = sel.getDagPath(0)
        if dag.node().hasFn(om2.MFn.kJoint):
            return _top_joint_from_dag(dag, om2)
        joints = _walk_subtree_shapes(dag, om2.MFn.kJoint, om2)
        if joints:
            return _top_joint_from_dag(joints[0], om2)

    # Fall back to scene-wide joint scan (e.g. skinCluster influence joints)
    it = om2.MItDag(om2.MItDag.kDepthFirst, om2.MFn.kJoint)
    while not it.isDone():
        dag = it.getPath()
        return _top_joint_from_dag(dag, om2)
    raise RuntimeError("No skeleton joints found in Maya scene")


def _collect_joint_tree(joint_dag: Any, om2: Any, included_joints=None) -> tuple[dict[str, Any], list[Any]]:
    ordered: list[Any] = []

    def visit(dag: Any) -> dict[str, Any]:
        ordered.append(dag)
        fn = om2.MFnDagNode(dag)
        children_nodes: list[dict[str, Any]] = []
        for i in range(int(fn.childCount())):
            child_obj = fn.child(i)
            if not child_obj.hasFn(om2.MFn.kJoint):
                continue
            cdag = om2.MDagPath(dag)
            cdag.push(child_obj)
            if included_joints is not None and cdag.fullPathName() not in included_joints:
                # Migration supplies an ancestor-closed skin influence set. A
                # missing interior joint would change the meaning of local TRS.
                def has_included(descendant):
                    fn_child = om2.MFnDagNode(descendant)
                    for ci in range(fn_child.childCount()):
                        child = fn_child.child(ci)
                        if child.hasFn(om2.MFn.kJoint):
                            path = om2.MDagPath(descendant); path.push(child)
                            if path.fullPathName() in included_joints or has_included(path): return True
                    return False
                if has_included(cdag):
                    raise RuntimeError("Selected joints omit an animated interior parent: "+cdag.fullPathName())
                continue
            children_nodes.append(visit(cdag))
        return {"name": _short_joint_name(dag, om2), "children": children_nodes}

    tree = visit(om2.MDagPath(joint_dag))
    return tree, ordered


def _find_skin_cluster_for_mesh(mesh_dag: Any, om2: Any) -> Any | None:
    if hasattr(mesh_dag, "skin_cluster") and mesh_dag.skin_cluster is not None:
        return mesh_dag.skin_cluster
    it = om2.MItDependencyGraph(
        mesh_dag.node(),
        om2.MFn.kSkinClusterFilter,
        om2.MItDependencyGraph.kUpstream,
        om2.MItDependencyGraph.kDepthFirst,
        om2.MItDependencyGraph.kNodeLevel,
    )
    while not it.isDone():
        return it.currentNode()
    return None


def _restore_bind_pose(joints: list[Any], om2: Any, cmds: Any | None = None) -> None:
    if cmds is None:
        try:
            import maya.cmds as maya_cmds  # type: ignore

            cmds = maya_cmds
        except ImportError:
            return
    if hasattr(cmds, "dagPose") and joints:
        paths = [str(j.fullPathName()) for j in joints]
        try:
            cmds.dagPose(paths[0], restore=True, g=True, bindPose=True)
        except Exception:
            pass


def _skin_cluster_bind_info(
    sc_obj: Any,
    sc_fn: Any,
    infs: list[Any],
    om2: Any,
) -> tuple[dict[str, Any] | None, Any | None, str | None]:
    """Read exact world bind matrices from skinCluster.bindPreMatrix and geomMatrix."""
    try:
        if not hasattr(sc_fn, "indexForInfluenceObject") or not hasattr(om2, "MFnDependencyNode"):
            return None, None, None
        dp = om2.MFnDependencyNode(sc_obj)
        sc_name = str(dp.name())
        bpm_plug = dp.findPlug("bindPreMatrix", False)
        bind_mats: dict[str, Any] = {}
        for j_dag in infs:
            lidx = int(sc_fn.indexForInfluenceObject(j_dag))
            elem = bpm_plug.elementByLogicalIndex(lidx)
            inv_obj = elem.asMObject()
            if inv_obj.isNull():
                return None, None, sc_name
            inv_mat = om2.MFnMatrixData(inv_obj).matrix()
            if abs(inv_mat.det4x4()) <= 1e-10:
                return None, None, sc_name
            bind_mats[str(j_dag.fullPathName())] = inv_mat.inverse()
        geom_mat = None
        try:
            gm_obj = dp.findPlug("geomMatrix", False).asMObject()
            if not gm_obj.isNull():
                geom_mat = om2.MFnMatrixData(gm_obj).matrix()
        except Exception:
            geom_mat = None
        return bind_mats, geom_mat, sc_name
    except Exception:
        return None, None, None


def extract_skeleton_to_exchange(
    out_dir: str | Path,
    *,
    logical_name: str,
    root_name: str | None = None,
    om2: Any | None = None,
    included_joints=None,
) -> Path:
    """Extract a Maya joint hierarchy into an xray-dcc-exchange/v2 skeleton bundle."""
    om2 = _resolve_om2(om2)
    DccExchangeBundleWriter = _import_exchange_writer()
    top_joint = _discover_joint_root(root_name, om2)
    tree, _ = _collect_joint_tree(top_joint, om2, included_joints)

    if tree["name"] != constants.OBJECT_MOVER_BONE_NAME:
        tree = {"name": constants.OBJECT_MOVER_BONE_NAME, "children": [tree]}

    writer = DccExchangeBundleWriter(
        "skeleton",
        logical_name,
        {
            "system": "right_handed_y_up",
            "unit_scale_to_meters": _scene_internal_unit_to_meters(om2),
            "uv_origin": "bottom_left",
            "front_face_winding": "ccw",
        }, source_dcc="maya",
    )
    writer.manifest["root"] = tree
    return writer.write(out_dir)


def extract_skinned_scene_to_exchange(
    skin_root_name: str,
    skeleton_name: str,
    out_dir: str | Path,
    *,
    logical_name: str,
    om2: Any | None = None,
    om2_anim: Any | None = None,
    cmds: Any | None = None,
    optimize_mesh: bool = True,
    optimize_uv: bool = True,
) -> Path:
    """Extract a Maya rigged/skinned mesh subtree into xray-dcc-exchange/v2 skinned_model."""
    om2 = _resolve_om2(om2)
    om2_anim = _resolve_om2_anim(om2_anim)
    DccExchangeBundleWriter = _import_exchange_writer()

    root_dag = _get_dag_path(skin_root_name, om2)
    if root_dag is None:
        raise RuntimeError(f"Skin root node not found: {skin_root_name}")

    mesh_dags = _walk_subtree_shapes(root_dag, om2.MFn.kMesh, om2)
    if not mesh_dags:
        raise RuntimeError(f"No mesh shapes found under skin root: {skin_root_name}")

    # Collect unique influence joints across all skin clusters in deterministic order
    bones_out: list[dict[str, Any]] = []
    bone_name_to_idx: dict[str, int] = {}
    joint_dags: list[Any] = []
    mesh_skin_fns: dict[str, tuple[Any, list[int]]] = {}
    mesh_xform_overrides: dict[str, Any] = {}
    sc_names_to_mute: list[str] = []

    if cmds is None:
        try:
            import maya.cmds as maya_cmds  # type: ignore

            cmds = maya_cmds
        except ImportError:
            cmds = None

    # Prefer exact skinCluster.bindPreMatrix.inverse() + geomMatrix over dagPose
    # restore, which fails partway when IK/constraints/expressions are active.
    per_mesh_bind_info: dict[str, tuple[Any, Any, list[Any], dict[str, Any] | None]] = {}
    all_have_bpm = True
    pre_infs: list[Any] = []
    for mesh_dag in mesh_dags:
        sc_obj = _find_skin_cluster_for_mesh(mesh_dag, om2)
        if sc_obj is None:
            raise RuntimeError(f"Skinned mesh has no attached skinCluster: {mesh_dag.fullPathName()}")
        sc_fn = om2_anim.MFnSkinCluster(sc_obj)
        infs = list(sc_fn.influenceObjects())
        pre_infs.extend(infs)
        bind_mats, geom_mat, sc_name = _skin_cluster_bind_info(sc_obj, sc_fn, infs, om2)
        if bind_mats is None:
            all_have_bpm = False
        mesh_full = str(mesh_dag.fullPathName())
        if geom_mat is not None:
            mesh_xform_overrides[mesh_full] = geom_mat
        if sc_name and sc_name not in sc_names_to_mute:
            sc_names_to_mute.append(sc_name)
        per_mesh_bind_info[mesh_full] = (sc_obj, sc_fn, infs, bind_mats)

    if not all_have_bpm:
        _restore_bind_pose(pre_infs, om2, cmds)

    for mesh_dag in mesh_dags:
        mesh_full = str(mesh_dag.fullPathName())
        sc_obj, sc_fn, infs, bind_mats = per_mesh_bind_info[mesh_full]
        local_to_global: list[int] = []
        for j_dag in infs:
            bname = _short_joint_name(j_dag, om2)
            if bname not in bone_name_to_idx:
                if len(bones_out) >= 128:
                    raise RuntimeError("Skinned model exceeds 128 bone limit")
                j_mat = (
                    bind_mats[str(j_dag.fullPathName())]
                    if (all_have_bpm and bind_mats is not None)
                    else j_dag.inclusiveMatrix()
                )
                t, r, s = _matrix_to_trs(j_mat, om2)
                g_idx = len(bones_out)
                bone_name_to_idx[bname] = g_idx
                bones_out.append(
                    {
                        "name": bname,
                        "bind_translation": t,
                        "bind_rotation": r,
                        "bind_scale": s,
                    }
                )
                joint_dags.append(j_dag)
            local_to_global.append(bone_name_to_idx[bname])
        mesh_skin_fns[mesh_full] = (sc_fn, local_to_global)

    def read_skin_weights(mesh_dag: Any) -> tuple[list[tuple[int, int, int, int]], list[tuple[float, float, float, float]]]:
        sc_fn, local_to_global = mesh_skin_fns[str(mesh_dag.fullPathName())]
        mesh_fn = om2.MFnMesh(mesh_dag)
        v_cnt = int(mesh_fn.numVertices)
        comp_fn = om2.MFnSingleIndexedComponent()
        comp_obj = comp_fn.create(om2.MFn.kMeshVertComponent)
        comp_fn.addElements(list(range(v_cnt)))
        flat_weights, inf_count = sc_fn.getWeights(mesh_dag, comp_obj)
        inf_count = int(inf_count)

        out_inds: list[tuple[int, int, int, int]] = []
        out_w: list[tuple[float, float, float, float]] = []
        for vi in range(v_cnt):
            base = vi * inf_count
            infs_v: list[tuple[float, int]] = []
            for li in range(inf_count):
                w = float(flat_weights[base + li])
                if w > 1e-6:
                    infs_v.append((w, local_to_global[li]))
            if not infs_v:
                raise RuntimeError(f"Vertex {vi} on skinned mesh '{mesh_dag.fullPathName()}' has zero skin weights")
            infs_v.sort(key=lambda item: (-item[0], item[1]))
            top = infs_v[:4]
            total = sum(item[0] for item in top)
            norm = [(item[1], item[0] / total) for item in top]
            while len(norm) < 4:
                norm.append((0xFFFF, 0.0))
            out_inds.append((norm[0][0], norm[1][0], norm[2][0], norm[3][0]))
            out_w.append((norm[0][1], norm[1][1], norm[2][1], norm[3][1]))
        return out_inds, out_w

    saved_envelopes: list[tuple[str, float]] = []
    try:
        if all_have_bpm and cmds is not None and hasattr(cmds, "getAttr") and hasattr(cmds, "setAttr"):
            for sc_name in sc_names_to_mute:
                try:
                    env_plug = f"{sc_name}.envelope"
                    saved_envelopes.append((env_plug, float(cmds.getAttr(env_plug))))
                    cmds.setAttr(env_plug, 0.0)
                except Exception:
                    pass
        surfaces = _extract_render_surfaces(
            root_dag,
            om2.MMatrix(),
            om2,
            skin_weights_fn=read_skin_weights,
            mesh_xform_overrides=mesh_xform_overrides if all_have_bpm else None,
        )
    finally:
        if cmds is not None and hasattr(cmds, "setAttr"):
            for env_plug, old_val in saved_envelopes:
                try:
                    cmds.setAttr(env_plug, old_val)
                except Exception:
                    pass
    if not surfaces:
        raise RuntimeError(f"No skinned surfaces extracted from {skin_root_name}")

    writer = DccExchangeBundleWriter(
        "skinned_model",
        logical_name,
        {
            "system": "right_handed_y_up",
            "unit_scale_to_meters": _scene_internal_unit_to_meters(om2),
            "uv_origin": "bottom_left",
            "front_face_winding": "ccw",
        }, source_dcc="maya",
    )
    writer.manifest["options"] = {
        "optimize_mesh": bool(optimize_mesh),
        "optimize_uv": bool(optimize_uv),
    }
    writer.manifest["skeleton_name"] = skeleton_name
    writer.manifest["bones"] = bones_out
    writer.manifest["surfaces"] = [
        {
            "name": s["name"],
            "mesh_name": s["mesh_name"],
            "shader_name": s["shader_name"],
            "texture_name": s["texture_name"],
            "material_name": s["material_name"],
            "lod": "LOD0",
            "vertex_count": len(s["positions"]),
            "index_count": len(s["indices"]),
            "streams": {
                "positions": writer.append_span("f32x3", s["positions"]),
                "normals": writer.append_span("f32x3", s["normals"]),
                "uvs": writer.append_span("f32x2", s["uvs"]),
                "indices": writer.append_span("u32", s["indices"]),
                "bone_indices": writer.append_span("u16x4", s["bone_indices"]),
                "bone_weights": writer.append_span("f32x4", s["bone_weights"]),
            },
        }
        for s in surfaces
    ]

    loc_dag = _get_dag_path(f"{skin_root_name}|locators", om2)
    if loc_dag is not None:
        locators = _extract_locators(loc_dag, om2.MMatrix(), om2, bone_index_map=bone_name_to_idx)
        if locators:
            writer.manifest["locators"] = locators

    return writer.write(out_dir)


def extract_animation_to_exchange(
    out_dir: str | Path,
    *,
    logical_name: str,
    root_name: str | None = None,
    local_center_node: str | None = None,
    is_additive: bool = False,
    precision_t: float = 0.005,
    precision_r: float = 0.005,
    precision_s: float = 0.005,
    om2: Any | None = None,
    om2_anim: Any | None = None,
    included_joints=None,
) -> Path:
    """Sample a Maya joint hierarchy across MAnimControl range into xray-dcc-exchange/v2."""
    om2 = _resolve_om2(om2)
    om2_anim = _resolve_om2_anim(om2_anim)
    DccExchangeBundleWriter = _import_exchange_writer()

    top_joint = _discover_joint_root(root_name, om2)
    _, joints = _collect_joint_tree(top_joint, om2, included_joints)
    if len(joints) + 1 > 128:
        raise RuntimeError(f"Skeleton joint count {len(joints)} + object_movement exceeds 128 bone limit")

    anim_ctrl = om2_anim.MAnimControl
    saved_time = anim_ctrl.currentTime()
    start_time = anim_ctrl.minTime()
    end_time = anim_ctrl.maxTime()
    ui_unit = om2.MTime.uiUnit()
    fps = float(om2.MTime(1.0, om2.MTime.kSeconds).asUnits(ui_unit))
    if not (fps > 0.0) or not math.isfinite(fps):
        fps = 30.0

    frame_first = int(round(start_time.asUnits(ui_unit)))
    frame_last = int(round(end_time.asUnits(ui_unit)))
    if frame_last < frame_first:
        frame_last = frame_first
    frame_count = (frame_last - frame_first) + 1

    # Resolve parent mapping among joints
    joint_paths = [str(j.fullPathName()) for j in joints]
    path_to_idx = {p: idx for idx, p in enumerate(joint_paths)}
    parent_idx: list[int] = []
    for idx, j in enumerate(joints):
        p_idx = -1
        try:
            p_dag = om2.MDagPath(j)
            p_dag.pop()
            p_idx = path_to_idx.get(str(p_dag.fullPathName()), -1)
        except Exception:
            p_idx = -1
        parent_idx.append(p_idx)

    # Track 0 is object_movement + tracks 1..len(joints) for joints[0..n-1]
    bone_names = [constants.OBJECT_MOVER_BONE_NAME] + [_short_joint_name(j, om2) for j in joints]
    per_bone_samples: list[dict[str, list[float]]] = [
        {ch: [] for ch in ("tx", "ty", "tz", "rx", "ry", "rz", "sx", "sy", "sz")}
        for _ in bone_names
    ]

    local_center_dag = _get_dag_path(local_center_node, om2) if local_center_node else None
    if local_center_node and local_center_dag is None:
        raise RuntimeError(f"Local center node (-l) not found: {local_center_node}")

    try:
        for frame in range(frame_first, frame_last + 1):
            anim_ctrl.setCurrentTime(om2.MTime(float(frame), ui_unit))
            center_inv = local_center_dag.inclusiveMatrix().inverse() if local_center_dag is not None else om2.MMatrix()

            global_mats = [j.inclusiveMatrix() for j in joints]
            # Track 0 ("object_movement"): root joint global transform relative to -l node
            t0, r0, s0 = _matrix_to_trs(global_mats[0] * center_inv, om2)
            for ci, val in enumerate((*t0, *r0, *s0)):
                ch = ("tx", "ty", "tz", "rx", "ry", "rz", "sx", "sy", "sz")[ci]
                per_bone_samples[0][ch].append(val)

            # Track 1 (root joint): identity per anim_export_util.cpp:809-810
            for ci, val in enumerate((0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 1.0, 1.0, 1.0)):
                ch = ("tx", "ty", "tz", "rx", "ry", "rz", "sx", "sy", "sz")[ci]
                per_bone_samples[1][ch].append(val)

            # Child joints i >= 1 -> Track i + 1: child relative to parent joint
            for i in range(1, len(joints)):
                p_i = parent_idx[i]
                rel_mat = global_mats[i] * (global_mats[p_i].inverse() if p_i >= 0 else om2.MMatrix())
                ti, ri, si = _matrix_to_trs(rel_mat, om2)
                for ci, val in enumerate((*ti, *ri, *si)):
                    ch = ("tx", "ty", "tz", "rx", "ry", "rz", "sx", "sy", "sz")[ci]
                    per_bone_samples[i + 1][ch].append(val)
    finally:
        anim_ctrl.setCurrentTime(saved_time)

    writer = DccExchangeBundleWriter(
        "animation",
        logical_name,
        {
            "system": "right_handed_y_up",
            "unit_scale_to_meters": _scene_internal_unit_to_meters(om2),
            "uv_origin": "bottom_left",
            "front_face_winding": "ccw",
        }, source_dcc="maya",
    )
    writer.manifest["frame_start"] = frame_first
    writer.manifest["root_already_relative"] = False
    writer.manifest["fps"] = fps
    writer.manifest["frame_count"] = frame_count
    writer.manifest["is_additive"] = bool(is_additive)
    writer.manifest["precision_t"] = float(precision_t)
    writer.manifest["precision_r"] = float(precision_r)
    writer.manifest["precision_s"] = float(precision_s)
    writer.manifest["tracks"] = [
        {
            "bone_name": bname,
            "channels": {
                ch: writer.append_span("f32", samples[ch])
                for ch in ("tx", "ty", "tz", "rx", "ry", "rz", "sx", "sy", "sz")
            },
        }
        for bname, samples in zip(bone_names, per_bone_samples)
    ]
    return writer.write(out_dir)
