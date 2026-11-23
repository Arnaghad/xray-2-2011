"""Physics body, collision shape, constraint, and ragdoll extractors using Maya Python API 2.0."""

from __future__ import annotations

import math
import tempfile
from pathlib import Path
from typing import Any

if __package__:
    from . import constants, cook_bridge
    from .skeletal_extractor import _matrix_to_trs, _resolve_om2_anim
    from .static_extractor import (
        _get_dag_path,
        _import_exchange_writer,
        _resolve_om2,
        _scene_internal_unit_to_meters,
        _walk_subtree_shapes,
    )
else:
    import constants
    import cook_bridge
    from skeletal_extractor import _matrix_to_trs, _resolve_om2_anim
    from static_extractor import (
        _get_dag_path,
        _import_exchange_writer,
        _resolve_om2,
        _scene_internal_unit_to_meters,
        _walk_subtree_shapes,
    )


def _short_name(dag_or_node: Any, om2: Any) -> str:
    fn = om2.MFnDagNode(dag_or_node)
    return str(fn.name()).split("|")[-1].split(":")[-1]


def _resolve_cmds(cmds: Any | None) -> Any:
    if cmds is not None:
        return cmds
    import maya.cmds as maya_cmds  # type: ignore

    return maya_cmds


def _mat4_identity() -> list[list[float]]:
    return [
        [1.0, 0.0, 0.0, 0.0],
        [0.0, 1.0, 0.0, 0.0],
        [0.0, 0.0, 1.0, 0.0],
        [0.0, 0.0, 0.0, 1.0],
    ]


def _to_rows(mat: Any) -> list[list[float]]:
    if hasattr(mat, "rows"):
        return [[float(v) for v in row] for row in mat.rows]
    return [[float(mat[r * 4 + c]) for c in range(4)] for r in range(4)]


def _rows_mul(a: list[list[float]], b: list[list[float]]) -> list[list[float]]:
    out = [[0.0] * 4 for _ in range(4)]
    for i in range(4):
        for j in range(4):
            out[i][j] = sum(a[i][k] * b[k][j] for k in range(4))
    return out


def _rows_inv_rigid_or_affine(m: list[list[float]]) -> list[list[float]]:
    aug = [m[r][:] + [1.0 if r == c else 0.0 for c in range(4)] for r in range(4)]
    for col in range(4):
        pivot = max(range(col, 4), key=lambda r: abs(aug[r][col]))
        if abs(aug[pivot][col]) < 1e-12:
            return _mat4_identity()
        if pivot != col:
            aug[col], aug[pivot] = aug[pivot], aug[col]
        div = aug[col][col]
        for j in range(8):
            aug[col][j] /= div
        for r in range(4):
            if r == col:
                continue
            factor = aug[r][col]
            if factor != 0.0:
                for j in range(8):
                    aug[r][j] -= factor * aug[col][j]
    return [row[4:] for row in aug]


def _trs_to_rows_maya(pos: list[float], rot_xyz: list[float], scale: list[float]) -> list[list[float]]:
    rx, ry, rz = rot_xyz
    cx, sx = math.cos(rx), math.sin(rx)
    cy, sy = math.cos(ry), math.sin(ry)
    cz, sz = math.cos(rz), math.sin(rz)
    # Row-vector Maya convention: v' = v * (S * Rx * Ry * Rz) + T
    m00 = scale[0] * (cy * cz)
    m01 = scale[0] * (cy * sz)
    m02 = scale[0] * (-sy)
    m10 = scale[1] * (sx * sy * cz - cx * sz)
    m11 = scale[1] * (sx * sy * sz + cx * cz)
    m12 = scale[1] * (sx * cy)
    m20 = scale[2] * (cx * sy * cz + sx * sz)
    m21 = scale[2] * (cx * sy * sz - sx * cz)
    m22 = scale[2] * (cx * cy)
    return [
        [m00, m01, m02, 0.0],
        [m10, m11, m12, 0.0],
        [m20, m21, m22, 0.0],
        [float(pos[0]), float(pos[1]), float(pos[2]), 1.0],
    ]


def _rows_to_trs(m: list[list[float]]) -> tuple[list[float], list[float], list[float]]:
    def _norm(v: list[float]) -> float:
        return math.sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2])

    r0, r1, r2 = m[0][:3], m[1][:3], m[2][:3]
    sx = _norm(r0) or 1.0
    sy = _norm(r1) or 1.0
    sz = _norm(r2) or 1.0
    u0 = [c / sx for c in r0]
    u1 = [c / sy for c in r1]
    u2 = [c / sz for c in r2]
    det = (
        u0[0] * (u1[1] * u2[2] - u1[2] * u2[1])
        - u0[1] * (u1[0] * u2[2] - u1[2] * u2[0])
        + u0[2] * (u1[0] * u2[1] - u1[1] * u2[0])
    )
    if det < 0.0:
        sz = -sz
        u2 = [-c for c in u2]

    s_y = max(-1.0, min(1.0, -u0[2]))
    ry = math.asin(s_y)
    if abs(abs(s_y) - 1.0) > 1e-6:
        rx = math.atan2(u1[2], u2[2])
        rz = math.atan2(u0[1], u0[0])
    else:
        rx = math.atan2(-u2[1], u1[1])
        rz = 0.0
    return (
        [float(m[3][0]), float(m[3][1]), float(m[3][2])],
        [float(rx), float(ry), float(rz)],
        [float(sx), float(sy), float(sz)],
    )


def _node_key(obj: Any, om2: Any) -> Any:
    if hasattr(om2, "MObjectHandle"):
        return ("maya", int(om2.MObjectHandle(obj).hashCode()))
    return id(obj)


def _node_and_shape(dag: Any, om2: Any) -> tuple[Any, Any]:
    """Return (transform_or_node_dag, attr_holder_node) supporting transform+locator or direct node."""
    node = dag.node()
    fn = om2.MFnDagNode(dag)
    for attr in ("Mass", "shapeType", "jointType"):
        if om2.MFnDependencyNode(node).hasAttribute(attr):
            return dag, node
    for i in range(fn.childCount()):
        ch = fn.child(i)
        ch_fn = om2.MFnDagNode(ch)
        if getattr(ch_fn, "isIntermediateObject", False):
            continue
        dep = om2.MFnDependencyNode(ch)
        if any(dep.hasAttribute(a) for a in ("Mass", "shapeType", "jointType")):
            return dag, ch
    return dag, node


def _physics_kind(node: Any, om2: Any) -> str | None:
    dep = om2.MFnDependencyNode(node)
    if dep.hasAttribute("Mass") or dep.hasAttribute("AnimationJoint"):
        return "body"
    if dep.hasAttribute("shapeType"):
        return "collision"
    if dep.hasAttribute("jointType"):
        return "constraint"
    type_name = getattr(dep, "typeName", None)
    if callable(type_name):
        type_name = type_name()
    mapping = {
        constants.BODY_NODE_NAME: "body",
        constants.COLLISION_NODE_NAME: "collision",
        constants.CONSTRAINT_NODE_NAME: "constraint",
    }
    return mapping.get(str(type_name)) if type_name else None


def _plug_value(node: Any, attr_name: str, default: Any, om2: Any) -> Any:
    dep = om2.MFnDependencyNode(node)
    if not dep.hasAttribute(attr_name):
        return default
    plug = dep.findPlug(attr_name, False)
    if isinstance(default, bool):
        return bool(plug.asBool()) if hasattr(plug, "asBool") else bool(plug._val)
    if isinstance(default, int):
        if hasattr(plug, "asShort"):
            return int(plug.asShort())
        if hasattr(plug, "asInt"):
            return int(plug.asInt())
        return int(plug._val if plug._val is not None else default)
    if isinstance(default, float):
        if hasattr(plug, "asDouble"):
            return float(plug.asDouble())
        if hasattr(plug, "asFloat"):
            return float(plug.asFloat())
        return float(plug._val if plug._val is not None else default)
    if isinstance(default, str):
        return plug.asString() if hasattr(plug, "asString") else str(plug._val or "")
    return default


def _plug_vec3(node: Any, attr_name: str, default: tuple[float, float, float], om2: Any) -> list[float]:
    dep = om2.MFnDependencyNode(node)
    if not dep.hasAttribute(attr_name):
        return list(default)
    plug = dep.findPlug(attr_name, False)
    if hasattr(plug, "numChildren") and plug.numChildren() == 3:
        vals: list[float] = []
        for idx in range(3):
            c = plug.child(idx)
            vals.append(float(c.asDouble() if hasattr(c, "asDouble") else c._val))
        return vals
    raw = getattr(plug, "_val", None)
    if isinstance(raw, (list, tuple)) and len(raw) == 3:
        return [float(raw[0]), float(raw[1]), float(raw[2])]
    return list(default)


def _transform_trs_local(dag: Any, om2: Any, cmds: Any | None = None) -> tuple[list[float], list[float], list[float]]:
    node = dag.node()
    if hasattr(om2, "MFnTransform"):
        try:
            xform = om2.MFnTransform(dag)
            if hasattr(xform, "transformation"):
                mat = xform.transformation().asMatrix()
                pos, rot, _ = _matrix_to_trs(mat, om2)
                _, _, r_scl = _rows_to_trs(_to_rows(mat))
                return pos, rot, r_scl
        except Exception:
            pass
    if cmds is not None:
        name = dag.fullPathName() if hasattr(dag, "fullPathName") else _short_name(dag, om2)
        if hasattr(cmds, "getAttr"):
            try:
                t = cmds.getAttr(f"{name}.translate")[0]
                r_deg = cmds.getAttr(f"{name}.rotate")[0]
                s = cmds.getAttr(f"{name}.scale")[0]
                return (
                    [float(t[0]), float(t[1]), float(t[2])],
                    [math.radians(float(a)) for a in r_deg],
                    [float(s[0]), float(s[1]), float(s[2])],
                )
            except Exception:
                pass
    if hasattr(node, "matrix"):
        pos, rot, _ = _matrix_to_trs(node.matrix, om2)
        scl = _plug_vec3(node, "scale", (1.0, 1.0, 1.0), om2)
        rows = _to_rows(node.matrix)
        _, _, r_scl = _rows_to_trs(rows)
        if hasattr(node, "attrs") and "scale" in node.attrs:
            r_scl = scl
        return pos, rot, r_scl
    return _rows_to_trs(_to_rows(dag.inclusiveMatrix()))


def _exclusive_rows(dag: Any) -> list[list[float]]:
    if hasattr(dag, "exclusiveMatrix"):
        return _to_rows(dag.exclusiveMatrix())
    parent_dag = type(dag)(dag) if hasattr(dag, "nodes") else None
    if parent_dag is not None and len(getattr(parent_dag, "nodes", [])) > 1:
        parent_dag.pop()
        return _to_rows(parent_dag.inclusiveMatrix())
    return _mat4_identity()


def _inclusive_rows(dag: Any) -> list[list[float]]:
    return _to_rows(dag.inclusiveMatrix())


def _enum_str_from_node(node: Any, attr_name: str, enum_names: tuple[str, ...], default: str, om2: Any) -> str:
    dep = om2.MFnDependencyNode(node)
    if not dep.hasAttribute(attr_name):
        return default
    plug = dep.findPlug(attr_name, False)
    raw = getattr(plug, "_val", None)
    if isinstance(raw, str) and raw in enum_names:
        return raw
    idx = _plug_value(node, attr_name, 0, om2)
    if 0 <= idx < len(enum_names):
        return enum_names[idx]
    return default


def _connected_body_name(attr_holder: Any, plug_name: str, all_bodies: dict[Any, str], om2: Any) -> str | None:
    dep = om2.MFnDependencyNode(attr_holder)
    if not dep.hasAttribute(plug_name):
        return None
    plug = dep.findPlug(plug_name, True)
    conns = plug.connectedTo(True, True)
    if not conns:
        raw = getattr(plug, "_val", None)
        if isinstance(raw, str) and raw:
            return raw
        return None
    target = conns[0].node()
    key = _node_key(target, om2)
    if key in all_bodies:
        return all_bodies[key]
    return _short_name(target, om2)


def _walk_dag_nodes(om2: Any) -> list[Any]:
    out: list[Any] = []
    it = om2.MItDag(om2.MItDag.kDepthFirst) if hasattr(om2.MItDag, "kDepthFirst") else om2.MItDag()
    while not it.isDone():
        out.append(it.getPath())
        it.next()
    return out


def _find_skin_cluster_for_joint(joint_node: Any, om2: Any) -> Any | None:
    dep = om2.MFnDependencyNode(joint_node)
    if not dep.hasAttribute("lockInfluenceWeights"):
        return None
    plug = dep.findPlug("lockInfluenceWeights", True)
    for c in plug.connectedTo(False, True):
        n = c.node()
        if n.hasFn(om2.MFn.kSkinClusterFilter):
            return n
    return None


def _all_mesh_points_world(mesh_dag: Any, om2: Any) -> list[tuple[float, float, float]]:
    mfn = om2.MFnMesh(mesh_dag)
    world_rows = _inclusive_rows(mesh_dag)
    pts = mfn.getPoints(om2.MSpace.kObject)
    out: list[tuple[float, float, float]] = []
    for p in pts:
        x = float(p.x) * world_rows[0][0] + float(p.y) * world_rows[1][0] + float(p.z) * world_rows[2][0] + world_rows[3][0]
        y = float(p.x) * world_rows[0][1] + float(p.y) * world_rows[1][1] + float(p.z) * world_rows[2][1] + world_rows[3][1]
        z = float(p.x) * world_rows[0][2] + float(p.y) * world_rows[1][2] + float(p.z) * world_rows[2][2] + world_rows[3][2]
        out.append((x, y, z))
    return out


def extract_points_for_collision(
    target_name: str | None = None,
    *,
    om2: Any | None = None,
    om2_anim: Any | None = None,
) -> list[tuple[float, float, float]]:
    """Collect world-space Maya points (in scene internal units) for WildMagic4 primitive fitting."""
    om2 = _resolve_om2(om2)
    target_dag: Any | None = None
    if target_name:
        target_dag = _get_dag_path(target_name, om2)
        if target_dag is None:
            raise RuntimeError(f"Auto-fit target node not found: {target_name}")
    else:
        sel = om2.MGlobal.getActiveSelectionList() if hasattr(om2.MGlobal, "getActiveSelectionList") else None
        if sel is not None and sel.length() > 0:
            target_dag = sel.getDagPath(0)

    if target_dag is None:
        return []

    node = target_dag.node()
    if node.hasFn(om2.MFn.kJoint):
        skin_obj = _find_skin_cluster_for_joint(node, om2)
        if skin_obj is not None:
            om2_anim = _resolve_om2_anim(om2_anim)
            skin_fn = om2_anim.MFnSkinCluster(skin_obj)
            infl_dags = skin_fn.influenceObjects()
            joint_idx = -1
            target_full = target_dag.fullPathName()
            for idx, id_dag in enumerate(infl_dags):
                if id_dag.fullPathName() == target_full:
                    joint_idx = idx
                    break
            if joint_idx >= 0:
                geoms = skin_fn.getOutputGeometry()
                pts: list[tuple[float, float, float]] = []
                infl_count = len(infl_dags)
                for geom_obj in geoms:
                    mesh_dags = [d for d in _walk_dag_nodes(om2) if d.node() == geom_obj]
                    if not mesh_dags:
                        continue
                    mesh_dag = mesh_dags[0]
                    world_pts = _all_mesh_points_world(mesh_dag, om2)
                    weights = None
                    if hasattr(skin_fn, "getWeights"):
                        try:
                            w_res = skin_fn.getWeights(mesh_dag, om2.MObject())
                            weights = w_res[0] if isinstance(w_res, tuple) else w_res
                        except Exception:
                            weights = None
                    if weights is None:
                        continue
                    for v_idx, wp in enumerate(world_pts):
                        base = v_idx * infl_count
                        w_j = float(weights[base + joint_idx])
                        if w_j <= 0.0:
                            continue
                        if all(float(weights[base + k]) <= w_j for k in range(infl_count)):
                            pts.append(wp)
                if pts:
                    return pts

        # Fallback: joint world position + child joint positions (ensures valid capsule/box for empty weightless bone)
        p0 = _inclusive_rows(target_dag)[3][:3]
        fallback_pts = [(p0[0], p0[1], p0[2])]
        fn = om2.MFnDagNode(target_dag)
        for i in range(fn.childCount()):
            ch = fn.child(i)
            if ch.hasFn(om2.MFn.kJoint):
                ch_dag = om2.MDagPath(target_dag)
                ch_dag.push(ch)
                pc = _inclusive_rows(ch_dag)[3][:3]
                fallback_pts.append((pc[0], pc[1], pc[2]))
        return fallback_pts

    meshes = [target_dag] if node.hasFn(om2.MFn.kMesh) else _walk_subtree_shapes(target_dag, om2.MFn.kMesh, om2)
    points: list[tuple[float, float, float]] = []
    for m_dag in meshes:
        points.extend(_all_mesh_points_world(m_dag, om2))
    return points


def fit_collision_shape_via_cli(
    shape_type: str,
    points_maya: list[tuple[float, float, float]],
    *,
    unit_scale_to_meters: float = 0.01,
    name: str = "fit0",
    cli_path: str | Path | None = None,
) -> dict[str, list[float]]:
    """Run portable C++26 WildMagic4 primitive fitting via xray_maya_cook_cli --fit-collision."""
    if shape_type not in constants.PHYSICS_SHAPES:
        raise ValueError(f"Unsupported collision shape type: {shape_type!r}")
    if not points_maya:
        return {"position": [0.0, 0.0, 0.0], "rotation": [0.0, 0.0, 0.0], "scale": [10.0, 10.0, 10.0]}

    DccExchangeBundleWriter = _import_exchange_writer()
    with tempfile.TemporaryDirectory(prefix="xray_fit_col_") as tmp:
        writer = DccExchangeBundleWriter(
            "physics",
            "fit_request",
            {
                "system": "right_handed_y_up",
                "unit_scale_to_meters": float(unit_scale_to_meters),
                "uv_origin": "bottom_left",
                "front_face_winding": "ccw",
            }, source_dcc="maya",
        )
        pts_span = writer.append_span("f32x3", points_maya)
        writer.manifest["bodies"] = []
        writer.manifest["joints"] = []
        writer.manifest["fit_requests"] = [
            {
                "name": name,
                "shape": shape_type,
                "points": pts_span,
            }
        ]
        writer.write(tmp)
        results = cook_bridge.run_fit_collision_cli(tmp, cli_path=cli_path)
        if not results:
            raise RuntimeError("xray_maya_cook_cli --fit-collision returned empty results")
        return results[0]["source"]


def _create_physics_xform_and_shape(node_type: str, desired_name: str, cmds: Any) -> tuple[str, str]:
    shape = str(cmds.createNode(node_type, name=f"{desired_name}Shape"))
    parents = cmds.listRelatives(shape, parent=True, fullPath=False) if hasattr(cmds, "listRelatives") else []
    if not parents:
        if shape != desired_name and hasattr(cmds, "rename"):
            renamed = str(cmds.rename(shape, desired_name))
            return renamed, renamed
        return shape, shape
    xform_name = str(parents[0])
    if xform_name != desired_name and hasattr(cmds, "rename"):
        xform_name = str(cmds.rename(xform_name, desired_name))
    children = cmds.listRelatives(xform_name, shapes=True, fullPath=False) if hasattr(cmds, "listRelatives") else []
    shape_name = str(children[0]) if children else shape
    return xform_name, shape_name


def _attr_owner_name(node_name: str, attr_name: str, cmds: Any) -> str:
    if hasattr(cmds, "attributeQuery") and cmds.attributeQuery(attr_name, node=node_name, exists=True):
        return node_name
    if hasattr(cmds, "listRelatives"):
        shapes = cmds.listRelatives(node_name, shapes=True, fullPath=False) or []
        for s in shapes:
            if not hasattr(cmds, "attributeQuery") or cmds.attributeQuery(attr_name, node=s, exists=True):
                return str(s)
    return node_name


def _set_node_trs_maya(node_name: str, pos: list[float], rot_rad: list[float], scale: list[float], cmds: Any) -> None:
    cmds.setAttr(f"{node_name}.translate", float(pos[0]), float(pos[1]), float(pos[2]), type="double3")
    cmds.setAttr(
        f"{node_name}.rotate",
        math.degrees(float(rot_rad[0])),
        math.degrees(float(rot_rad[1])),
        math.degrees(float(rot_rad[2])),
        type="double3",
    )
    cmds.setAttr(f"{node_name}.scale", float(scale[0]), float(scale[1]), float(scale[2]), type="double3")


def create_body(
    *,
    name: str | None = None,
    anim_joint: str | None = None,
    links: list[str] | None = None,
    edit_target: str | None = None,
    om2: Any | None = None,
    cmds: Any | None = None,
) -> str:
    """Create or edit an xray_body transform+locator pair with joint metadata."""
    om2 = _resolve_om2(om2)
    cmds = _resolve_cmds(cmds)
    if edit_target:
        xform_name = edit_target
        shape_name = _attr_owner_name(xform_name, "AnimationJoint", cmds)
    else:
        desired = name or "xray_body1"
        xform_name, shape_name = _create_physics_xform_and_shape(constants.BODY_NODE_NAME, desired, cmds)
        # Body inherits rigid joint defaults just like legacy body_node
        create_joint(edit_target=xform_name, joint_type="rigid", om2=om2, cmds=cmds)

    if anim_joint:
        aj_dag = _get_dag_path(anim_joint, om2)
        aj_short = _short_name(aj_dag, om2) if aj_dag else anim_joint.split("|")[-1].split(":")[-1]
        cmds.setAttr(f"{shape_name}.AnimationJoint", aj_short, type="string")
        if aj_dag is not None and not edit_target:
            pos, rot, _ = _rows_to_trs(_inclusive_rows(aj_dag))
            _set_node_trs_maya(xform_name, pos, rot, [1.0, 1.0, 1.0], cmds)
    if links:
        create_joint(edit_target=xform_name, links=links, om2=om2, cmds=cmds)
    return xform_name


def create_collision(
    *,
    name: str | None = None,
    shape_type: str = "box",
    parent: str | None = None,
    auto_target: str | None = None,
    cli_path: str | Path | None = None,
    om2: Any | None = None,
    om2_anim: Any | None = None,
    cmds: Any | None = None,
) -> str:
    """Create an xray_collision node and optionally auto-fit it with WildMagic4 via CLI."""
    om2 = _resolve_om2(om2)
    cmds = _resolve_cmds(cmds)
    if shape_type not in constants.PHYSICS_SHAPES:
        raise ValueError(f"Invalid collision shape type: {shape_type!r}")
    desired = name or "xray_collision1"
    xform_name, shape_name = _create_physics_xform_and_shape(constants.COLLISION_NODE_NAME, desired, cmds)
    cmds.setAttr(f"{shape_name}.shapeType", constants.PHYSICS_SHAPES.index(shape_type))

    fitted_world: dict[str, list[float]] | None = None
    if auto_target:
        pts = extract_points_for_collision(auto_target, om2=om2, om2_anim=om2_anim)
        unit_scale = _scene_internal_unit_to_meters(om2)
        fitted_world = fit_collision_shape_via_cli(
            shape_type, pts, unit_scale_to_meters=unit_scale, name=xform_name, cli_path=cli_path
        )

    if parent:
        cmds.parent(xform_name, parent)

    if fitted_world is not None:
        w_rows = _trs_to_rows_maya(fitted_world["position"], fitted_world["rotation"], [1.0, 1.0, 1.0])
        if parent:
            p_dag = _get_dag_path(parent, om2)
            if p_dag is not None:
                local_rows = _rows_mul(w_rows, _rows_inv_rigid_or_affine(_inclusive_rows(p_dag)))
                l_pos, l_rot, _ = _rows_to_trs(local_rows)
                _set_node_trs_maya(xform_name, l_pos, l_rot, fitted_world["scale"], cmds)
            else:
                _set_node_trs_maya(xform_name, fitted_world["position"], fitted_world["rotation"], fitted_world["scale"], cmds)
        else:
            _set_node_trs_maya(xform_name, fitted_world["position"], fitted_world["rotation"], fitted_world["scale"], cmds)

    return xform_name


def create_joint(
    *,
    name: str | None = None,
    joint_type: str | None = None,
    links: list[str] | None = None,
    edit_target: str | None = None,
    om2: Any | None = None,
    cmds: Any | None = None,
) -> str:
    """Create or edit an xray_constraint (or joint attributes on an xray_body)."""
    om2 = _resolve_om2(om2)
    cmds = _resolve_cmds(cmds)
    if edit_target:
        xform_name = edit_target
        shape_name = _attr_owner_name(xform_name, "jointType", cmds)
    else:
        desired = name or "xray_constraint1"
        xform_name, shape_name = _create_physics_xform_and_shape(constants.CONSTRAINT_NODE_NAME, desired, cmds)
        if joint_type is None:
            joint_type = "rigid"

    if joint_type is not None:
        if joint_type not in constants.JOINT_TYPES:
            raise ValueError(f"Unsupported joint type: {joint_type!r}")
        cmds.setAttr(f"{shape_name}.jointType", constants.JOINT_TYPES.index(joint_type))

    if links:
        if len(links) >= 1 and links[0]:
            b0_owner = _attr_owner_name(links[0], "joint", cmds)
            cmds.connectAttr(f"{b0_owner}.joint", f"{shape_name}.body0", force=True)
        if len(links) >= 2 and links[1]:
            b1_owner = _attr_owner_name(links[1], "joint", cmds)
            cmds.connectAttr(f"{b1_owner}.joint", f"{shape_name}.body1", force=True)
    return xform_name


def build_collision_hierarchy(
    start_joint: str,
    *,
    recurse: bool = True,
    cli_path: str | Path | None = None,
    om2: Any | None = None,
    om2_anim: Any | None = None,
    cmds: Any | None = None,
    _parent_body: str | None = None,
) -> list[str]:
    """Create bodies, auto-fitted box collision shapes, and linked constraints along a skeleton hierarchy."""
    om2 = _resolve_om2(om2)
    cmds = _resolve_cmds(cmds)
    joint_dag = _get_dag_path(start_joint, om2)
    if joint_dag is None:
        raise RuntimeError(f"Joint not found: {start_joint}")

    j_short = _short_name(joint_dag, om2)
    if j_short == "none" or not joint_dag.node().hasFn(om2.MFn.kJoint):
        return []

    created: list[str] = []
    body_name = f"{j_short}_body"
    create_body(name=body_name, anim_joint=joint_dag.fullPathName(), om2=om2, cmds=cmds)
    created.append(body_name)

    col_name = f"{j_short}_c"
    create_collision(
        name=col_name,
        shape_type="box",
        parent=body_name,
        auto_target=joint_dag.fullPathName(),
        cli_path=cli_path,
        om2=om2,
        om2_anim=om2_anim,
        cmds=cmds,
    )
    if _parent_body:
        create_joint(edit_target=body_name, links=[_parent_body, body_name], om2=om2, cmds=cmds)
        cmds.parent(body_name, _parent_body)

    if recurse:
        fn = om2.MFnDagNode(joint_dag)
        for i in range(fn.childCount()):
            ch = fn.child(i)
            if not ch.hasFn(om2.MFn.kJoint):
                continue
            ch_dag = om2.MDagPath(joint_dag)
            ch_dag.push(ch)
            created.extend(
                build_collision_hierarchy(
                    ch_dag.fullPathName(),
                    recurse=True,
                    cli_path=cli_path,
                    om2=om2,
                    om2_anim=om2_anim,
                    cmds=cmds,
                    _parent_body=body_name,
                )
            )
    return created


def _extract_dof(holder: Any, dag: Any, dof_name: str, om2: Any, cmds: Any | None) -> dict[str, Any]:
    enable = _plug_value(holder, f"{dof_name}_enabled", False, om2)
    limit_map = {
        "x_translate": ("minTransXLimitEnable", "maxTransXLimitEnable", "minTransXLimit", "maxTransXLimit", 1.0),
        "y_translate": ("minTransYLimitEnable", "maxTransYLimitEnable", "minTransYLimit", "maxTransYLimit", 1.0),
        "z_translate": ("minTransZLimitEnable", "maxTransZLimitEnable", "minTransZLimit", "maxTransZLimit", 1.0),
        "x_rotate": ("minRotXLimitEnable", "maxRotXLimitEnable", "minRotXLimit", "maxRotXLimit", 1.0),
        "y_rotate": ("minRotYLimitEnable", "maxRotYLimitEnable", "minRotYLimit", "maxRotYLimit", 1.0),
        "z_rotate": ("minRotZLimitEnable", "maxRotZLimitEnable", "minRotZLimit", "maxRotZLimit", 1.0),
    }
    lo_en_attr, hi_en_attr, lo_attr, hi_attr, _ = limit_map[dof_name]
    transform_node = dag.node()
    limits_en = bool(_plug_value(transform_node, lo_en_attr, False, om2) or _plug_value(transform_node, hi_en_attr, False, om2))
    lo = float(_plug_value(transform_node, lo_attr, 0.0, om2))
    hi = float(_plug_value(transform_node, hi_attr, 0.0, om2))
    if cmds is not None and hasattr(cmds, "transformLimits"):
        try:
            name = dag.fullPathName()
            kw_map = {
                "x_translate": ("etx", "tx"),
                "y_translate": ("ety", "ty"),
                "z_translate": ("etz", "tz"),
                "x_rotate": ("erx", "rx"),
                "y_rotate": ("ery", "ry"),
                "z_rotate": ("erz", "rz"),
            }
            et_flag, lim_flag = kw_map[dof_name]
            en_pair = cmds.transformLimits(name, query=True, **{et_flag: True})
            val_pair = cmds.transformLimits(name, query=True, **{lim_flag: True})
            if en_pair:
                limits_en = bool(en_pair[0] or en_pair[1])
            if val_pair:
                lo, hi = float(val_pair[0]), float(val_pair[1])
                if dof_name.endswith("_rotate"):
                    lo = math.radians(lo)
                    hi = math.radians(hi)
        except Exception:
            pass
    return {
        "enable_state": bool(enable),
        "limits_enable_state": bool(limits_en),
        "low_limit": lo,
        "hi_limit": hi,
    }


def extract_physics_scene_to_exchange(
    out_dir: str | Path,
    *,
    root_name: str | None = None,
    logical_name: str = "physics/exported",
    om2: Any | None = None,
    cmds: Any | None = None,
) -> Path:
    """Extract xray_body, xray_collision, and xray_constraint scene hierarchy into an exchange/v2 bundle."""
    om2 = _resolve_om2(om2)
    DccExchangeBundleWriter = _import_exchange_writer()
    unit_scale = _scene_internal_unit_to_meters(om2)

    all_dags = _walk_dag_nodes(om2)
    root_prefix: str | None = None
    if root_name:
        rd = _get_dag_path(root_name, om2)
        if rd is None:
            raise RuntimeError(f"Physics root node not found: {root_name}")
        root_prefix = rd.fullPathName()

    def _in_root_subtree(dag: Any) -> bool:
        if not root_prefix:
            return True
        full = str(dag.fullPathName())
        return full == root_prefix or full.startswith(f"{root_prefix}|")

    seen_nodes: set[Any] = set()
    body_records: list[tuple[Any, Any, str]] = []
    standalone_constraints: list[tuple[Any, Any, str]] = []

    for dag in all_dags:
        if not _in_root_subtree(dag):
            continue
        x_dag, holder = _node_and_shape(dag, om2)
        kind = _physics_kind(holder, om2)
        x_key = _node_key(x_dag.node(), om2)
        h_key = _node_key(holder, om2)
        if kind is None or x_key in seen_nodes or h_key in seen_nodes:
            continue
        seen_nodes.add(x_key)
        seen_nodes.add(h_key)
        if kind == "body":
            b_name = _short_name(x_dag, om2)
            body_records.append((x_dag, holder, b_name))
        elif kind == "constraint":
            standalone_constraints.append((x_dag, holder, _short_name(x_dag, om2)))

    if not body_records:
        raise RuntimeError("No xray_body nodes found to export")

    node_to_body_name: dict[Any, str] = {}
    body_dag_by_name: dict[str, Any] = {}
    for x_dag, holder, b_name in body_records:
        node_to_body_name[_node_key(x_dag.node(), om2)] = b_name
        node_to_body_name[_node_key(holder, om2)] = b_name
        body_dag_by_name[b_name] = x_dag

    # Include standalone xray_constraint nodes connected to exported bodies even if created at scene root
    if root_prefix:
        for dag in all_dags:
            if _in_root_subtree(dag):
                continue
            x_dag, holder = _node_and_shape(dag, om2)
            x_key = _node_key(x_dag.node(), om2)
            h_key = _node_key(holder, om2)
            if x_key in seen_nodes or h_key in seen_nodes:
                continue
            if _physics_kind(holder, om2) != "constraint":
                continue
            b0 = _connected_body_name(holder, "body0", node_to_body_name, om2)
            b1 = _connected_body_name(holder, "body1", node_to_body_name, om2)
            if b0 is not None or b1 is not None:
                seen_nodes.add(x_key)
                seen_nodes.add(h_key)
                standalone_constraints.append((x_dag, holder, _short_name(x_dag, om2)))

    # Identify fixed child bodies attached directly to a parent body so their collision shapes merge cleanly
    body_by_name: dict[str, dict[str, Any]] = {}
    flattened_targets: dict[str, tuple[str, list[list[float]]]] = {}
    flattened_constraint_keys: set[Any] = set()
    body_order: list[str] = []
    root_body_name = body_records[0][2]

    for x_dag, holder, b_name in body_records:
        jtype = _enum_str_from_node(holder, "jointType", constants.JOINT_TYPES, "rigid", om2)
        b0 = _connected_body_name(holder, "body0", node_to_body_name, om2)
        b1 = _connected_body_name(holder, "body1", node_to_body_name, om2)
        if jtype == "fixed" and b0 in body_dag_by_name and (b1 is None or b1 == b_name):
            parent_rows = _inclusive_rows(body_dag_by_name[b0])
            rel_rows = _rows_mul(_inclusive_rows(x_dag), _rows_inv_rigid_or_affine(parent_rows))
            flattened_targets[b_name] = (b0, rel_rows)

    for j_dag, holder, _ in standalone_constraints:
        jtype = _enum_str_from_node(holder, "jointType", constants.JOINT_TYPES, "rigid", om2)
        b0 = _connected_body_name(holder, "body0", node_to_body_name, om2)
        b1 = _connected_body_name(holder, "body1", node_to_body_name, om2)
        if (
            jtype == "fixed"
            and b0 in body_dag_by_name
            and b1 in body_dag_by_name
            and b1 != b0
            and b1 != root_body_name
            and b1 not in flattened_targets
        ):
            parent_rows = _inclusive_rows(body_dag_by_name[b0])
            child_rows = _inclusive_rows(body_dag_by_name[b1])
            rel_rows = _rows_mul(child_rows, _rows_inv_rigid_or_affine(parent_rows))
            flattened_targets[b1] = (b0, rel_rows)
            flattened_constraint_keys.add(_node_key(holder, om2))

    joint_records: list[tuple[Any, Any, str]] = []
    for idx, (x_dag, holder, b_name) in enumerate(body_records):
        if b_name in flattened_targets:
            continue

        b_pos, b_rot, b_scale = _rows_to_trs(_inclusive_rows(x_dag))
        body_order.append(b_name)
        body_by_name[b_name] = {
            "name": b_name,
            "anim_joint": _plug_value(holder, "AnimationJoint", "", om2),
            "mass": float(_plug_value(holder, "Mass", 1.0, om2)),
            "mass_center": _plug_vec3(holder, "MassCenter", (0.0, 0.0, 0.0), om2),
            "position": b_pos,
            "rotation": b_rot,
            "scale": b_scale,
            "collision": [],
        }

        if om2.MFnDependencyNode(holder).hasAttribute("jointType"):
            b0 = _connected_body_name(holder, "body0", node_to_body_name, om2)
            b1 = _connected_body_name(holder, "body1", node_to_body_name, om2)
            if idx == 0 or b0 is not None or b1 is not None:
                joint_records.append((x_dag, holder, b_name))

    for j_dag, holder, j_name in standalone_constraints:
        if _node_key(holder, om2) not in flattened_constraint_keys:
            joint_records.append((j_dag, holder, j_name))

    # Collect xray_collision shapes owned by bodies (or flattened fixed child bodies)
    for x_dag, _, b_name in body_records:
        target_body_name = b_name
        disp_rows = _mat4_identity()
        if b_name in flattened_targets:
            target_body_name, disp_rows = flattened_targets[b_name]
        if target_body_name not in body_by_name:
            continue

        fn = om2.MFnDagNode(x_dag)
        for i in range(fn.childCount()):
            ch = fn.child(i)
            ch_dag = om2.MDagPath(x_dag)
            ch_dag.push(ch)
            col_dag, col_holder = _node_and_shape(ch_dag, om2)
            if _physics_kind(col_holder, om2) != "collision":
                continue
            l_pos, l_rot, l_scale = _transform_trs_local(col_dag, om2, cmds)
            l_rows = _trs_to_rows_maya(l_pos, l_rot, [1.0, 1.0, 1.0])
            w_rows = _rows_mul(l_rows, disp_rows)
            c_pos, c_rot, _ = _rows_to_trs(w_rows)
            c_type = _enum_str_from_node(col_holder, "shapeType", constants.PHYSICS_SHAPES, "box", om2)
            body_by_name[target_body_name]["collision"].append(
                {
                    "type": c_type,
                    "position": c_pos,
                    "rotation": c_rot,
                    "scale": l_scale,
                }
            )

    root_joints: list[dict[str, Any]] = []
    linked_joints: list[dict[str, Any]] = []
    for j_dag, j_holder, j_name in joint_records:
        if j_name in flattened_targets:
            continue
        j_type = _enum_str_from_node(j_holder, "jointType", constants.JOINT_TYPES, "rigid", om2)
        b0 = _connected_body_name(j_holder, "body0", node_to_body_name, om2)
        b1 = _connected_body_name(j_holder, "body1", node_to_body_name, om2)
        j_pos, j_rot, j_scale = _rows_to_trs(_inclusive_rows(j_dag))
        entry: dict[str, Any] = {
            "name": j_name,
            # Rigid is the authoring default and reserved root-slot label.
            # The engine dispatches articulated rigid links as fixed joints.
            "type": "fixed" if b0 and b1 and j_type == "rigid" else j_type,
            "position": j_pos,
            "rotation": j_rot,
            "scale": j_scale,
        }
        if b0:
            entry["body0"] = b0
        if b1:
            entry["body1"] = b1
        for dof in constants.JOINT_DOF_NAMES:
            entry[dof] = _extract_dof(j_holder, j_dag, dof, om2, cmds)
        if b0 is None and b1 is None:
            root_joints.append(entry)
        else:
            linked_joints.append(entry)

    # Engine physics shell parser skips joints[0] (Root_body slot), so keep unlinked root entry first.
    if not root_joints and linked_joints:
        first_body = body_by_name[body_order[0]]
        stub: dict[str, Any] = {
            "name": f"{first_body['name']}_root",
            "type": "rigid",
            "position": list(first_body["position"]),
            "rotation": list(first_body["rotation"]),
            "scale": [1.0, 1.0, 1.0],
        }
        for dof in constants.JOINT_DOF_NAMES:
            stub[dof] = {"enable_state": False, "limits_enable_state": False, "low_limit": 0.0, "hi_limit": 0.0}
        root_joints.append(stub)

    bodies_out = [body_by_name[n] for n in body_order]
    joints_out = root_joints + linked_joints

    writer = DccExchangeBundleWriter(
        "physics",
        logical_name,
        {
            "system": "right_handed_y_up",
            "unit_scale_to_meters": float(unit_scale),
            "uv_origin": "bottom_left",
            "front_face_winding": "ccw",
        }, source_dcc="maya",
    )
    writer.manifest["bodies"] = bodies_out
    writer.manifest["joints"] = joints_out
    return writer.write(out_dir)
