"""Static model, collision, and locator extractor using Maya Python API 2.0."""

from __future__ import annotations

import math
import sys
from pathlib import Path
from typing import Any

if __package__:
    from .shader_utils import extract_shader_metadata, sanitize_surface_name
else:
    from shader_utils import extract_shader_metadata, sanitize_surface_name


def _import_exchange_writer() -> Any:
    tools_dir = Path(__file__).resolve().parents[2]
    if str(tools_dir) not in sys.path:
        sys.path.insert(0, str(tools_dir))
    from dcc_common.exchange import DccExchangeBundleWriter

    return DccExchangeBundleWriter


def _resolve_om2(om2: Any | None) -> Any:
    if om2 is not None:
        return om2
    import maya.api.OpenMaya as maya_om2  # type: ignore

    return maya_om2


def _get_dag_path(name: str, om2: Any) -> Any | None:
    sel = om2.MSelectionList()
    try:
        sel.add(name)
        if sel.length() != 1:
            return None
        return sel.getDagPath(0)
    except Exception:
        return None


def _matrix_det3(m: Any) -> float:
    return (
        m.getElement(0, 0) * (m.getElement(1, 1) * m.getElement(2, 2) - m.getElement(1, 2) * m.getElement(2, 1))
        - m.getElement(0, 1) * (m.getElement(1, 0) * m.getElement(2, 2) - m.getElement(1, 2) * m.getElement(2, 0))
        + m.getElement(0, 2) * (m.getElement(1, 0) * m.getElement(2, 1) - m.getElement(1, 1) * m.getElement(2, 0))
    )


def _transform_point(pt: Any, mat: Any, om2: Any) -> tuple[float, float, float]:
    res = om2.MPoint(pt.x, pt.y, pt.z, 1.0) * mat
    return (float(res.x), float(res.y), float(res.z))


def _transform_normal(n: Any, normal_mat: Any, om2: Any) -> tuple[float, float, float]:
    v = om2.MVector(n.x, n.y, n.z) * normal_mat
    nx, ny, nz = float(v.x), float(v.y), float(v.z)
    length = math.sqrt(nx * nx + ny * ny + nz * nz)
    if length > 1e-12 and math.isfinite(length):
        return (nx / length, ny / length, nz / length)
    return (0.0, 1.0, 0.0)


def _walk_subtree_shapes(root_dag: Any, fn_type: Any, om2: Any) -> list[Any]:
    results: list[Any] = []
    seen: set[str] = set()

    def visit(dag: Any) -> None:
        node = dag.node()
        fn = om2.MFnDagNode(dag)
        if node.hasFn(fn_type):
            try:
                is_interm = bool(fn.isIntermediateObject)
            except Exception:
                is_interm = False
            if not is_interm:
                full = str(dag.fullPathName())
                if full not in seen:
                    seen.add(full)
                    results.append(dag)
        for i in range(int(fn.childCount())):
            child_obj = fn.child(i)
            child_dag = om2.MDagPath(dag)
            child_dag.push(child_obj)
            visit(child_dag)

    visit(om2.MDagPath(root_dag))
    return results


def _scene_internal_unit_to_meters(om2: Any) -> float:
    try:
        if hasattr(om2.MDistance, "internalUnit"):
            unit = om2.MDistance.internalUnit()
            return float(om2.MDistance(1.0, unit).asUnits(om2.MDistance.kMeters))
        return float(om2.MDistance(1.0, om2.MDistance.kCentimeters).asMeters())
    except Exception:
        return 0.01


def _extract_locators(
    loc_root: Any,
    root_inv: Any,
    om2: Any,
    *,
    bone_index_map: dict[str, int] | None = None,
) -> list[dict[str, Any]]:
    locators: list[dict[str, Any]] = []
    seen_names: set[str] = set()
    for dag in _walk_subtree_shapes(loc_root, om2.MFn.kLocator, om2):
        fn = om2.MFnDagNode(dag)
        raw_name = str(fn.name()).split(":")[-1]
        if raw_name.endswith("Shape") and len(raw_name) > 5:
            # Prefer transform name when Maya uses LocatorName + LocatorNameShape
            try:
                parent_fn = om2.MFnDagNode(fn.parent(0))
                raw_name = str(parent_fn.name()).split(":")[-1]
            except Exception:
                pass
        loc_name = sanitize_surface_name(raw_name)[:31]
        if loc_name in seen_names:
            continue
        seen_names.add(loc_name)

        bone_name = ""
        bone_idx = -1
        parent_inv = root_inv
        if bone_index_map:
            # Check parent transform/joint chain for an owning skeletal bone
            try:
                cur_dag = om2.MDagPath(dag)
                cur_dag.pop()
                for _ in range(8):
                    cur_fn = om2.MFnDagNode(cur_dag)
                    cand = str(cur_fn.name()).split(":")[-1]
                    if cand in bone_index_map:
                        bone_name = cand
                        bone_idx = bone_index_map[cand]
                        parent_inv = cur_dag.inclusiveMatrix().inverse()
                        break
                    if cur_fn.parentCount() == 0:
                        break
                    cur_dag.pop()
            except Exception:
                pass

        offset = dag.inclusiveMatrix() * parent_inv
        pos = (
            float(offset.getElement(3, 0)),
            float(offset.getElement(3, 1)),
            float(offset.getElement(3, 2)),
        )
        m_xform = om2.MTransformationMatrix(offset)
        rot = m_xform.rotation(asQuaternion=False)
        locators.append(
            {
                "name": loc_name,
                "bone_name": bone_name,
                "bone_idx": bone_idx,
                "position": [pos[0], pos[1], pos[2]],
                "rotation": [float(rot.x), float(rot.y), float(rot.z)],
            }
        )
    return locators


def _extract_collision_meshes(col_root: Any, root_inv: Any, om2: Any) -> tuple[list[tuple[float, float, float]], list[int]]:
    pos_map: dict[tuple[float, float, float], int] = {}
    positions: list[tuple[float, float, float]] = []
    indices: list[int] = []

    for mesh_dag in _walk_subtree_shapes(col_root, om2.MFn.kMesh, om2):
        full_xform = mesh_dag.inclusiveMatrix() * root_inv
        flip_wind = _matrix_det3(full_xform) < 0.0
        mesh_fn = om2.MFnMesh(mesh_dag)
        mesh_pts = mesh_fn.getPoints(om2.MSpace.kObject)
        poly_it = om2.MItMeshPolygon(mesh_dag)
        while not poly_it.isDone():
            _, tri_vtx_ids = poly_it.getTriangles(om2.MSpace.kObject)
            for t in range(0, len(tri_vtx_ids), 3):
                tri = [int(tri_vtx_ids[t]), int(tri_vtx_ids[t + 1]), int(tri_vtx_ids[t + 2])]
                if flip_wind:
                    tri = [tri[0], tri[2], tri[1]]
                for vid in tri:
                    wp = _transform_point(mesh_pts[vid], full_xform, om2)
                    key = (round(wp[0], 5), round(wp[1], 5), round(wp[2], 5))
                    idx = pos_map.get(key)
                    if idx is None:
                        idx = len(positions)
                        pos_map[key] = idx
                        positions.append(wp)
                    indices.append(idx)
            poly_it.next()

    return positions, indices


def _extract_render_surfaces(
    vis_root: Any,
    root_inv: Any,
    om2: Any,
    *,
    skin_weights_fn: Any | None = None,
    mesh_xform_overrides: dict[str, Any] | None = None,
) -> list[dict[str, Any]]:
    # Keyed by (mesh_full_path_or_shared_sg, sg_name) per Maya surface grouping
    surface_buckets: dict[str, dict[str, Any]] = {}
    used_names: set[str] = set()

    def unique_surf_name(base: str) -> str:
        cand = base
        idx = 1
        while cand in used_names:
            cand = sanitize_surface_name(base, idx)
            idx += 1
        used_names.add(cand)
        return cand

    for mesh_dag in _walk_subtree_shapes(vis_root, om2.MFn.kMesh, om2):
        mesh_full_name = str(mesh_dag.fullPathName())
        base_xform = (
            mesh_xform_overrides[mesh_full_name]
            if mesh_xform_overrides and mesh_full_name in mesh_xform_overrides
            else mesh_dag.inclusiveMatrix()
        )
        full_xform = base_xform * root_inv
        normal_mat = full_xform.inverse().transpose()
        flip_wind = _matrix_det3(full_xform) < 0.0
        skin_info = skin_weights_fn(mesh_dag) if skin_weights_fn is not None else None

        mesh_fn = om2.MFnMesh(mesh_dag)
        instance_num = int(mesh_dag.instanceNumber()) if mesh_dag.isInstanced() else 0
        shaders, poly_shader_indices = mesh_fn.getConnectedShaders(instance_num)
        if not shaders:
            continue

        try:
            uv_set = str(mesh_fn.currentUVSetName())
        except Exception:
            uv_set = ""

        mesh_pts = mesh_fn.getPoints(om2.MSpace.kObject)

        # Prepare per-shader accumulators for this mesh/SG pair
        sg_meta: dict[int, dict[str, Any]] = {}
        for si in range(len(shaders)):
            meta = extract_shader_metadata(shaders[si], om2)
            sg_key = f"{mesh_full_name}::{meta['sg_name']}"
            if sg_key not in surface_buckets:
                bucket_dict: dict[str, Any] = {
                    "name": unique_surf_name(meta["surface_name"]),
                    "mesh_name": mesh_full_name,
                    "shader_name": meta["shader_name"],
                    "texture_name": meta["texture_name"],
                    "material_name": meta["material_name"],
                    "positions": [],
                    "normals": [],
                    "uvs": [],
                    "indices": [],
                    "vmap": {},
                }
                if skin_info is not None:
                    bucket_dict["bone_indices"] = []
                    bucket_dict["bone_weights"] = []
                surface_buckets[sg_key] = bucket_dict
            sg_meta[si] = surface_buckets[sg_key]

        poly_it = om2.MItMeshPolygon(mesh_dag)
        while not poly_it.isDone():
            face_id = int(poly_it.index())
            shader_idx = int(poly_shader_indices[face_id]) if face_id < len(poly_shader_indices) else -1
            if shader_idx not in sg_meta:
                poly_it.next()
                continue

            bucket = sg_meta[shader_idx]
            face_verts = [int(v) for v in poly_it.getVertices()]
            vert_to_local = {vid: li for li, vid in enumerate(face_verts)}
            has_uvs = bool(uv_set) and bool(poly_it.hasUVs(uv_set))

            _, tri_vtx_ids = poly_it.getTriangles(om2.MSpace.kObject)
            for t in range(0, len(tri_vtx_ids), 3):
                tri = [int(tri_vtx_ids[t]), int(tri_vtx_ids[t + 1]), int(tri_vtx_ids[t + 2])]
                if flip_wind:
                    tri = [tri[0], tri[2], tri[1]]

                for vid in tri:
                    local_i = vert_to_local[vid]
                    wp = _transform_point(mesh_pts[vid], full_xform, om2)
                    raw_n = poly_it.getNormal(local_i, om2.MSpace.kObject)
                    wn = _transform_normal(raw_n, normal_mat, om2)
                    if has_uvs:
                        u, v = poly_it.getUV(local_i, uv_set)
                        wuv = (float(u), float(v))
                    else:
                        wuv = (0.0, 0.0)

                    skin_key: tuple[Any, ...] = ()
                    if skin_info is not None:
                        v_inds = skin_info[0][vid]
                        v_weights = skin_info[1][vid]
                        skin_key = v_inds + tuple(round(w, 4) for w in v_weights)

                    key = (
                        round(wp[0], 6),
                        round(wp[1], 6),
                        round(wp[2], 6),
                        round(wn[0], 5),
                        round(wn[1], 5),
                        round(wn[2], 5),
                        round(wuv[0], 6),
                        round(wuv[1], 6),
                    ) + skin_key
                    v_out = bucket["vmap"].get(key)
                    if v_out is None:
                        v_out = len(bucket["positions"])
                        bucket["vmap"][key] = v_out
                        bucket["positions"].append(wp)
                        bucket["normals"].append(wn)
                        bucket["uvs"].append(wuv)
                        if skin_info is not None:
                            bucket["bone_indices"].append(v_inds)
                            bucket["bone_weights"].append(v_weights)
                    bucket["indices"].append(v_out)

            poly_it.next()

    return [b for b in surface_buckets.values() if b["indices"]]


def extract_static_scene_to_exchange(
    root_name: str,
    out_dir: str | Path,
    *,
    logical_name: str,
    om2: Any | None = None,
    allow_direct_root: bool = False,
    optimize_mesh: bool = True,
    optimize_uv: bool = True,
) -> Path:
    """Extract a Maya DAG static model subtree into an xray-dcc-exchange/v2 bundle."""
    om2 = _resolve_om2(om2)
    DccExchangeBundleWriter = _import_exchange_writer()

    root_dag = _get_dag_path(root_name, om2)
    if root_dag is None:
        raise RuntimeError(f"Root node not found: {root_name}")

    is_locator = root_dag.hasFn(om2.MFn.kLocator) if hasattr(root_dag, "hasFn") else root_dag.node().hasFn(om2.MFn.kLocator)
    root_inv = root_dag.inclusiveMatrix().inverse() if is_locator else om2.MMatrix()

    vis_dag = _get_dag_path(f"{root_name}|vis", om2)
    if vis_dag is None:
        if allow_direct_root:
            vis_dag = root_dag
        else:
            raise RuntimeError(f"Geometry not found: {root_name}|vis")

    surfaces = _extract_render_surfaces(vis_dag, root_inv, om2)
    if not surfaces:
        raise RuntimeError(f"No render surfaces collected from {vis_dag.fullPathName()}")

    unit_scale = _scene_internal_unit_to_meters(om2)
    writer = DccExchangeBundleWriter(
        "static_model",
        logical_name,
        {
            "system": "right_handed_y_up",
            "unit_scale_to_meters": unit_scale,
            "uv_origin": "bottom_left",
            "front_face_winding": "ccw",
        }, source_dcc="maya",
    )
    writer.manifest["options"] = {
        "optimize_mesh": bool(optimize_mesh),
        "optimize_uv": bool(optimize_uv),
    }

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
            },
        }
        for s in surfaces
    ]

    col_dag = _get_dag_path(f"{root_name}|col", om2)
    if col_dag is not None:
        c_pos, c_idx = _extract_collision_meshes(col_dag, root_inv, om2)
        if c_pos and c_idx:
            writer.manifest["collision"] = {
                "from_render_geometry": False,
                "vertex_count": len(c_pos),
                "index_count": len(c_idx),
                "positions": writer.append_span("f32x3", c_pos),
                "indices": writer.append_span("u32", c_idx),
            }

    loc_dag = _get_dag_path(f"{root_name}|locators", om2)
    if loc_dag is not None:
        locators = _extract_locators(loc_dag, root_inv, om2)
        if locators:
            writer.manifest["locators"] = locators

    return writer.write(out_dir)
