"""Maintainer Maya worker. Open working copies with script nodes disabled."""

import argparse
import json
import math
from pathlib import Path
import shutil
import sys
import traceback

TOOLS = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(TOOLS))
sys.path.insert(0, str(TOOLS/"blender"))
sys.path.insert(0, str(Path(__file__).resolve().parent))
from blender.migration_state import write_json, safe_path
from dcc_common.exchange import validate_bundle
import skeletal_extractor as sk
from static_extractor import extract_static_scene_to_exchange


def direct_curves(joints, fps, om, oa, c):
    result, authoring, warnings = [], {}, []
    orders = ("XYZ", "YZX", "ZXY", "XZY", "YXZ", "ZYX")
    infinity = {0: "CONSTANT", 1: "LINEAR", 3: "CYCLE", 4: "CYCLE_RELATIVE", 5: "OSCILLATE"}
    for joint in joints:
        path = joint.fullPathName()
        name = sk._short_joint_name(joint, om)
        channels = {}
        for ch, attr in zip(("tx", "ty", "tz", "rx", "ry", "rz", "sx", "sy", "sz"),
                            ("translateX", "translateY", "translateZ", "rotateX", "rotateY", "rotateZ", "scaleX", "scaleY", "scaleZ")):
            plugs = c.listConnections(path+"."+attr, source=True, destination=False, plugs=True) or []
            if len(plugs) != 1 or not c.nodeType(plugs[0].split(".")[0]).startswith("animCurveT"):
                continue  # Driven channels are sampled; do not claim controller keys are direct joint keys.
            selection = om.MSelectionList(); selection.add(plugs[0].split(".")[0])
            fn = oa.MFnAnimCurve(selection.getDependNode(0))
            if not fn.numKeys:
                continue
            keys = []
            for i in range(fn.numKeys):
                time = fn.input(i).asUnits(om.MTime.uiUnit())
                interpolation = "CONSTANT" if fn.outTangentType(i) == oa.MFnAnimCurve.kTangentStep else "LINEAR" if (
                    i+1 < fn.numKeys and fn.outTangentType(i) == oa.MFnAnimCurve.kTangentLinear and
                    fn.inTangentType(i+1) == oa.MFnAnimCurve.kTangentLinear) else "BEZIER"
                if fn.outTangentType(i) == oa.MFnAnimCurve.kTangentStepNext:
                    warnings.append(f"{name}.{ch}: step-next retained through sampled fit")
                    keys = []; break
                key = dict(time=time, value=fn.value(i), interpolation=interpolation)
                for incoming, field in ((True, "handle_left"), (False, "handle_right")):
                    x, y = fn.getTangentXY(i, incoming)
                    sign = -1 if incoming else 1
                    if fn.isWeighted:
                        dx, dy = x*fps/3, y/3
                    else:
                        adjacent = i-1 if incoming else i+1
                        dt = abs(time-fn.input(adjacent).asUnits(om.MTime.uiUnit())) if 0 <= adjacent < fn.numKeys else 1
                        dx = dt/3
                        dy = dx*y/(x*fps) if abs(x) > 1e-12 else 0
                    key[field] = [time+sign*dx, key["value"]+sign*dy]
                keys.append(key)
            if keys:
                channels[ch] = dict(keys=keys, pre_infinity=infinity.get(fn.preInfinityType, "CONSTANT"),
                                    post_infinity=infinity.get(fn.postInfinityType, "CONSTANT"))
        if channels:
            result.append(dict(bone_name=name, rotation_order=orders[c.getAttr(path+".rotateOrder")], channels=channels))
        # Bind rotations are explicit: nonzero channels deliberately fall back to fit.
        authoring[name] = dict(translation=list(c.getAttr(path+".translate")[0]),
            joint_orient=[math.radians(v) for v in c.getAttr(path+".jointOrient")[0]],
            rotate_axis=[math.radians(v) for v in c.getAttr(path+".rotateAxis")[0]],
            bind_rotation=[math.radians(v) for v in c.getAttr(path+".rotate")[0]], scale=list(c.getAttr(path+".scale")[0]))
    return {"time_unit": "frames", "tracks": result}, authoring, warnings


def controller_seeds(joints,om,oa):
    """Retain times from direct keys and upstream constraint/control animation."""
    cache={}; seeds={}; counts={}
    for joint in joints:
        times=set()
        graph=om.MItDependencyGraph(joint.node(),om.MFn.kAnimCurve,om.MItDependencyGraph.kUpstream,
            om.MItDependencyGraph.kDepthFirst,om.MItDependencyGraph.kNodeLevel)
        while not graph.isDone():
            node=graph.currentNode(); key=om.MObjectHandle(node).hashCode()
            if key not in cache:
                curve=oa.MFnAnimCurve(node)
                values=[]
                for index in range(curve.numKeys):
                    value=curve.input(index)
                    if isinstance(value,om.MTime): values.append(value.asUnits(om.MTime.uiUnit()))
                cache[key]=values
                counts[om.MFnDependencyNode(node).name()]=len(values)
            times.update(cache[key])
            graph.next()
        seeds[sk._short_joint_name(joint,om)]=sorted(times)
    if joints: seeds["object_movement"]=seeds[sk._short_joint_name(joints[0],om)]
    return dict(seed_frames=seeds,seed_source="upstream_maya_anim_curves",source_curve_key_counts=counts)


def _is_dag_visible(node_path, c):
    cur = node_path
    while cur:
        try:
            if c.attributeQuery("visibility", node=cur, exists=True) and not c.getAttr(cur + ".visibility"):
                return False
            if (
                c.attributeQuery("overrideEnabled", node=cur, exists=True)
                and c.getAttr(cur + ".overrideEnabled")
                and c.attributeQuery("overrideVisibility", node=cur, exists=True)
                and not c.getAttr(cur + ".overrideVisibility")
            ):
                return False
            for layer in c.listConnections(cur, type="displayLayer") or []:
                if c.attributeQuery("visibility", node=layer, exists=True) and not c.getAttr(layer + ".visibility"):
                    return False
        except Exception:
            pass
        parents = c.listRelatives(cur, parent=True, fullPath=True) or []
        cur = parents[0] if parents else None
    return True


def extract(job, args, c, om, oa):
    source = Path(job["source"])
    out = Path(job["exchange"]); out.mkdir(parents=True, exist_ok=True)
    working = Path(job["working"]); working.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, working)
    c.file(new=True, force=True)
    warnings, errors, families, assets = [], [], [], []
    # Maya resolves references after opening; disable them initially so missing
    # historical drive letters cannot silently load a different local asset.
    try:
        c.file(str(working), open=True, force=True, ignoreVersion=True, prompt=False,
               executeScriptNodes=False, loadReferenceDepth="none")
    except RuntimeError as error:
        if not (c.ls(type="joint") or c.ls(type="mesh") or c.ls(type="animLayer")):
            raise
        warnings.append("Maya open reported: "+str(error))
    layer_baked_only = False
    if not (c.ls(type="joint") or []) and (c.ls(type="animLayer") or []) and job.get("anim_layer_base"):
        base_src = Path(job["anim_layer_base"])
        base_work = working.with_name("base_" + base_src.name)
        shutil.copy2(base_src, base_work)
        c.file(new=True, force=True)
        try:
            c.file(str(base_work), open=True, force=True, ignoreVersion=True, prompt=False,
                   executeScriptNodes=False, loadReferenceDepth="none")
        except RuntimeError as error:
            if not c.ls(type="joint"):
                raise
            warnings.append("Maya base open reported: " + str(error))
        before_curves = set(c.ls(type="animCurve") or [])
        c.file(str(working), i=True, force=True, ignoreVersion=True, prompt=False, executeScriptNodes=False)
        after_curves = set(c.ls(type="animCurve") or []) - before_curves
        times = [t for cv in after_curves for t in (c.keyframe(cv, q=True, timeChange=True) or [])]
        if not times:
            raise ValueError("Imported Maya AnimLayer has no keyframes")
        t0, t1 = min(times), max(times)
        c.playbackOptions(minTime=t0, maxTime=t1, animationStartTime=t0, animationEndTime=t1)
        warnings.append(f"Baked standalone Maya AnimLayer onto base rig ({base_src.name}, frames {t0}..{t1})")
        layer_baked_only = True
    references = []
    for node in c.ls(type="reference") or []:
        if node == "sharedReferenceNode":
            continue
        try:
            original = c.referenceQuery(node, filename=True, withoutCopyNumber=True)
            normalized = original.replace("\\", "/")
            candidates = []
            for marker in ("xr2-dsgn/resources/", "resources/"):
                if marker in normalized:
                    rel = normalized.split(marker, 1)[1]
                    p = safe_path(args.source_root/"xr2-dsgn/resources", rel)
                    if p.is_file(): candidates.append(p)
            if not candidates:
                candidates = [Path(p) for p in args.reference_index.get(Path(normalized).name.casefold(), [])]
            candidates = sorted(set(candidates))
            if len(candidates) != 1:
                raise ValueError(f"Reference {'missing' if not candidates else 'ambiguous'}: {original}")
            resolved = candidates[0]
            target = safe_path(args.workspace/"references", resolved.relative_to(args.source_root))
            target.parent.mkdir(parents=True, exist_ok=True)
            if not target.exists(): shutil.copy2(resolved, target)
            c.file(str(target), loadReference=node, executeScriptNodes=False)
            references.append(resolved.relative_to(args.source_root).as_posix())
        except Exception as error:
            errors.append(str(error))
    missing = c.unknownPlugin(query=True, list=True) or []
    if missing: warnings.append("Unavailable plugins: "+", ".join(missing))
    textures = []
    for node in c.ls(type="file") or []:
        texture = c.getAttr(node+".fileTextureName") or ""
        if texture:
            path=Path(texture)
            # Historical UNC/mapped-drive paths are diagnostics, not network
            # dependencies. Probe only local files inside the source corpus.
            try:
                exists=not texture.startswith(("\\\\","//")) and path.is_absolute() and path.is_relative_to(args.source_root) and path.is_file()
            except OSError:
                exists=False
            if not exists: textures.append(texture)
    if textures: warnings.append(f"Missing/unresolved textures: {len(textures)}")
    joints = c.ls(type="joint", long=True) or []
    roots = [j for j in joints if not c.listRelatives(j, parent=True, type="joint")]
    influences = set()
    weighted_influences = set()
    for cluster in c.ls(type="skinCluster") or []:
        for influence in c.skinCluster(cluster, query=True, influence=True) or []:
            influences.update(c.ls(influence, long=True) or [])
        for influence in c.skinCluster(cluster, query=True, weightedInfluence=True) or []:
            weighted_influences.update(c.ls(influence, long=True) or [])
    rig_entries = []
    for ri, root in enumerate(roots):
        key = f"rig_{ri:03d}"
        try:
            dag = sk._get_dag_path(root, om)
            def _close_ancestors(seed_set):
                closed = {p for p in seed_set if p == root or p.startswith(root + "|")}
                for influence in list(closed):
                    current = influence
                    while current != root:
                        parents = c.listRelatives(current, parent=True, fullPath=True) or []
                        if not parents:
                            break
                        current = parents[0]
                        if current != root and not current.startswith(root + "|"):
                            break
                        if c.nodeType(current) == "joint":
                            closed.add(current)
                return closed

            selected = _close_ancestors(influences)
            if len(selected) > 127 and weighted_influences:
                pruned = _close_ancestors(weighted_influences)
                if pruned and len(pruned) < len(selected):
                    warnings.append(f"{root}: pruned unweighted skinCluster helper joints ({len(selected)} -> {len(pruned)})")
                    selected = pruned
            # Match archived clip names against actual parent links. Extra
            # end/rig-helper joints may be omitted only when no kept child uses them.
            all_tree, all_joints = sk._collect_joint_tree(dag, om)
            candidate_set = set(selected) if selected else {j.fullPathName() for j in all_joints}
            name_counts = {}
            for joint in all_joints:
                jp = joint.fullPathName()
                if jp in candidate_set:
                    sname = sk._short_joint_name(joint, om)
                    name_counts[sname.lower()] = name_counts.get(sname.lower(), 0) + 1
            if name_counts.get("none", 0) > 1:
                dropped_none = set()
                for joint in all_joints:
                    jp = joint.fullPathName()
                    if jp not in candidate_set:
                        continue
                    if sk._short_joint_name(joint, om).lower() != "none" or jp in influences:
                        continue
                    children = c.listRelatives(jp, children=True, type="joint", fullPath=True) or []
                    if not any(ch in candidate_set for ch in children):
                        dropped_none.add(jp)
                if dropped_none and len(candidate_set - dropped_none) >= 1:
                    candidate_set -= dropped_none
                    selected = candidate_set
                    warnings.append(f"{root}: omitted {len(dropped_none)} duplicate unweighted leaf 'none' joint(s)")
            by_name = {}
            for joint in all_joints:
                by_name.setdefault(sk._short_joint_name(joint, om), []).append(joint)
            for names in args.clip_sets:
                wanted = set(names)-{"object_movement"}
                if not wanted or any(len(by_name.get(n,[])) != 1 for n in wanted): continue
                included = {by_name[n][0].fullPathName() for n in wanted}
                if root not in included: continue
                try:
                    candidate, _ = sk._collect_joint_tree(dag, om, included)
                    families.append(dict(root={"name":"object_movement","children":[candidate]},bind_pose=[],coord_space=None))
                except RuntimeError: pass
            if influences and not selected and not _is_dag_visible(root, c):
                continue
            tree, ordered = sk._collect_joint_tree(dag, om, selected or None)
            if tree["name"] != "object_movement": tree = {"name": "object_movement", "children": [tree]}
            coord = dict(system="right_handed_y_up", unit_scale_to_meters=0.01, uv_origin="bottom_left", front_face_winding="ccw")
            bind = []
            for joint in ordered:
                t, r, s = sk._matrix_to_trs(joint.inclusiveMatrix(), om)
                bind.append(dict(name=sk._short_joint_name(joint, om), bind_translation=t, bind_rotation=r, bind_scale=s))
            families.append(dict(root=tree, bind_pose=bind, coord_space=coord))
            if len(ordered) > 127 or tree["children"][0]["name"] == "object_movement":
                raise ValueError(f"{root}: unsupported hierarchy / {len(ordered)} joints (maximum 127)")
            rig_entries.append((root, tree))
            sk.extract_skeleton_to_exchange(out/key/"skeleton", logical_name="skeleton", root_name=root,included_joints=selected or None)
            skeleton = json.loads((out/key/"skeleton/manifest.json").read_text())
            skeleton["bind_pose"] = bind
            write_json(out/key/"skeleton/manifest.json", skeleton)
            assets.append(key+"/skeleton")
            # A scene with direct/indirect animation exports evaluated joint poses;
            # direct animCurves additionally retain original keys and tangents.
            if c.ls(type="animCurve") or c.ls(type="constraint"):
                curves, authoring, notes = direct_curves(ordered, float(om.MTime(1, om.MTime.kSeconds).asUnits(om.MTime.uiUnit())), om, oa, c)
                warnings.extend(notes)
                sk.extract_animation_to_exchange(out/key/"animation", logical_name="animation", root_name=root,included_joints=selected or None)
                animation = json.loads((out/key/"animation/manifest.json").read_text())
                animation.update(root=tree, bind_pose=bind, curves=curves, authoring=authoring, source_dcc="legacy_mb" if source.suffix.lower()==".mb" else "legacy_ma")
                animation["import_metadata"]=controller_seeds(ordered,om,oa)
                write_json(out/key/"animation/manifest.json", animation)
                validate_bundle(out/key/"animation")
                assets.append(key+"/animation")
        except Exception as error:
            errors.append(f"{root}: {error}")
    # Mesh extraction is conservative: a skinned shape must use the same proven
    # joint hierarchy. The existing extractor supplies bind matrices and weights.
    meshes = [] if layer_baked_only else (c.ls(type="mesh", long=True, noIntermediate=True) or [])
    skin_roots, static_roots = set(), set()
    for mesh in meshes:
        transform = (c.listRelatives(mesh, parent=True, fullPath=True) or [])[0]
        history = c.listHistory(mesh) or []
        is_skinned = bool(c.ls(history, type="skinCluster"))
        if not _is_dag_visible(mesh, c) or not _is_dag_visible(transform, c):
            warnings.append(f"Skipped hidden {'skinned' if is_skinned else 'static'} mesh: {transform}")
            continue
        (skin_roots if is_skinned else static_roots).add(transform)
    for si, (skinned, transform) in enumerate([(True,p) for p in sorted(skin_roots)]+[(False,p) for p in sorted(static_roots)]):
        key = f"mesh_{si:03d}"
        try:
            if skinned:
                sk.extract_skinned_scene_to_exchange(transform, "skeleton", out/key, logical_name="mesh")
                manifest = json.loads((out/key/"manifest.json").read_text())
                mesh_infs = set()
                for sc in c.ls(c.listHistory(transform) or [], type="skinCluster") or []:
                    for inf in c.skinCluster(sc, query=True, influence=True) or []:
                        mesh_infs.update(c.ls(inf, long=True) or [])
                dag_matches = [
                    r_tree for r_root, r_tree in rig_entries
                    if mesh_infs and all(inf == r_root or inf.startswith(r_root + "|") for inf in mesh_infs)
                ]
                if len(dag_matches) == 1:
                    manifest["root"] = dag_matches[0]
                else:
                    names = {b["name"] for b in manifest["bones"]}
                    matching = [f for f in families if names.issubset({b["name"] for b in f["bind_pose"]})]
                    if len(matching) != 1: raise ValueError("Skinned mesh does not identify one skeleton")
                    manifest["root"] = matching[0]["root"]
                write_json(out/key/"manifest.json", manifest)
            else:
                extract_static_scene_to_exchange(transform, out/key, logical_name="mesh", allow_direct_root=True)
            assets.append(key)
        except Exception as error:
            errors.append(f"{transform}: {error}")
    if not assets:
        errors.append("Scene contains no supported exportable assets")
    write_json(out/"scene.json", dict(assets=assets, warnings=warnings, errors=errors))
    return dict(status="with_warnings" if warnings or errors or job.get("temporary") else "migrated",
        assets=assets, families=families, warnings=warnings, errors=errors, references=references,
        missing_plugins=missing, missing_textures=textures, exchange=str(out))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--jobs", type=Path, required=True)
    args = parser.parse_args()
    data = json.loads(args.jobs.read_text(encoding="utf-8"))
    args.source_root, args.workspace = Path(data["source_root"]), Path(data["workspace"])
    args.reference_index = data["reference_index"]
    args.clip_sets = data.get("clip_sets",[])
    import maya.standalone
    maya.standalone.initialize(name="python")
    import maya.cmds as c
    import maya.api.OpenMaya as om
    import maya.api.OpenMayaAnim as oa
    try:
        for job in data["jobs"]:
            write_json(job["result"],dict(status="running",path=job["path"]))
            try:
                result = extract(job, args, c, om, oa)
                if not result["assets"]: result["status"] = "failed"
            except Exception as error:
                result = dict(status="failed", reason=str(error), traceback=traceback.format_exc())
            write_json(job["result"], result)
            print(json.dumps(dict(path=job["path"], status=result["status"])), flush=True)
    finally:
        maya.standalone.uninitialize()


if __name__ == "__main__": main()
