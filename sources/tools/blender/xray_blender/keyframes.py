# SPDX-License-Identifier: GPL-3.0-or-later
"""Editable key transfer with source keys/knots as seeds and pose reconciliation."""

import copy
import json
import math
import bpy
from mathutils import Euler, Matrix, Vector
from . import exporters as ex
from ._dcc_common.dcc_math import curve_value


def transformed(curve, factor=1.0, offset=0.0):
    result = copy.deepcopy(curve)
    for key in result["keys"]:
        key["value"] = key["value"]*factor+offset
        for handle in ("handle_left", "handle_right"):
            if handle in key:
                key[handle][1] = key[handle][1]*factor+offset
    return result


def exact_channels(rig, bundle):
    manifest = bundle.manifest
    exact, modes = {}, {}
    if bundle.engine or bundle.native or rig.xray_axis_space != "Y_UP_RH" or abs(bundle.unit-rig.xray_unit_scale) > 1e-8:
        return exact, modes
    authoring = manifest.get("authoring", {})
    if not isinstance(authoring, dict):
        return exact, modes
    for track in manifest.get("curves", {}).get("tracks", []):
        name = track["bone_name"]
        meta = authoring.get(name)
        if not isinstance(meta, dict) or name not in rig.data.bones:
            continue
        if any(not isinstance(meta.get(k), list) or len(meta[k]) != 3 or not all(isinstance(v, (int, float)) and not isinstance(v, bool) and math.isfinite(v) for v in meta[k])
               for k in ("translation", "joint_orient", "rotate_axis", "bind_rotation", "scale")):
            continue
        bone = rig.data.bones[name]
        correction = bone.matrix_local.inverted() @ ex.joint_matrix(rig, bone.matrix_local, bone)
        if max(abs(correction[i][j]-ex.RCORR.inverted()[i][j]) for i in range(4) for j in range(4)) > 2e-5:
            continue
        if any(abs(v) > 1e-7 for key in ("rotate_axis", "bind_rotation") for v in meta[key]) or any(abs(v-1) > 1e-7 for v in meta["scale"]):
            continue
        mapping = {"X": "Y", "Y": "X", "Z": "Z"}
        modes[name] = "".join(mapping[c] for c in track["rotation_order"])
        channels = track["channels"]
        for source, target, sign in (("rx", 4, 1), ("ry", 3, -1), ("rz", 5, 1), ("sx", 7, 1), ("sy", 6, 1), ("sz", 8, 1)):
            if source in channels:
                exact[name, target] = transformed(channels[source], sign)
        rotation = ex.RCORR.inverted().to_3x3() @ Euler(meta["joint_orient"], "XYZ").to_matrix().inverted()
        for row in range(3):
            contributors = [i for i in range(3) if abs(rotation[row][i]) > 1e-7 and "t"+"xyz"[i] in channels]
            if not contributors:
                continue
            template = channels["t"+"xyz"[contributors[0]]]
            def signature(curve):
                return [(k["time"], k["interpolation"], k.get("handle_left", [None])[0], k.get("handle_right", [None])[0]) for k in curve["keys"]]
            if any(signature(channels["t"+"xyz"[i]]) != signature(template) for i in contributors):
                continue
            if any((channels["t"+"xyz"[i]].get("pre_infinity", "CONSTANT"), channels["t"+"xyz"[i]].get("post_infinity", "CONSTANT")) !=
                   (template.get("pre_infinity", "CONSTANT"), template.get("post_infinity", "CONSTANT")) for i in contributors):
                continue
            curve = copy.deepcopy(template)
            for index, key in enumerate(curve["keys"]):
                def value(field=None):
                    total = 0
                    for axis in range(3):
                        if abs(rotation[row][axis]) < 1e-7:
                            continue
                        source = channels.get("t"+"xyz"[axis])
                        source_value = source["keys"][index][field][1] if source and field else source["keys"][index]["value"] if source else meta["translation"][axis]
                        total += rotation[row][axis]*(source_value-meta["translation"][axis])
                    return total
                key["value"] = value()
                for handle in ("handle_left", "handle_right"):
                    if handle in key:
                        key[handle][1] = value(handle)
            exact[name, row] = curve
    return exact, modes


def put_curve(curve, keys, pre="CONSTANT", post="CONSTANT"):
    curve.keyframe_points.clear()
    for modifier in list(curve.modifiers):
        curve.modifiers.remove(modifier)
    for key in keys:
        point = curve.keyframe_points.insert(key["time"], key["value"], options={"FAST"})
        point.interpolation = key["interpolation"]
        point.handle_left_type = point.handle_right_type = "FREE"
        point.handle_left = key.get("handle_left", (key["time"], key["value"]))
        point.handle_right = key.get("handle_right", (key["time"], key["value"]))
    curve.extrapolation = "LINEAR" if "LINEAR" in {pre, post} else "CONSTANT"
    cycle_modes = {"CYCLE": "REPEAT", "CYCLE_RELATIVE": "REPEAT_OFFSET", "OSCILLATE": "MIRROR"}
    if pre in cycle_modes or post in cycle_modes:
        modifier = curve.modifiers.new("CYCLES")
        modifier.mode_before, modifier.mode_after = cycle_modes.get(pre, "NONE"), cycle_modes.get(post, "NONE")
    if "LINEAR" in {pre, post} and "CONSTANT" in {pre, post}:
        modifier = curve.modifiers.new("LIMITS")
        modifier.use_min_x, modifier.min_x = pre == "CONSTANT", keys[0]["time"]
        modifier.use_max_x, modifier.max_x = post == "CONSTANT", keys[-1]["time"]
    curve.update()


def sample_at(values, first, time):
    location = max(0, min(len(values)-1, time-first))
    left = math.floor(location)
    if left == len(values)-1:
        return values[-1]
    return values[left]+(values[left+1]-values[left])*(location-left)


def fitted_keys(values, first, times):
    result = []
    for index, time in enumerate(times):
        value = sample_at(values, first, time)
        dt = min(0.5, max(0.001, (len(values)-1)/2))
        a, b = max(first, time-dt), min(first+len(values)-1, time+dt)
        slope = (sample_at(values, first, b)-sample_at(values, first, a))/(b-a) if b > a else 0
        left = (time-times[index-1])/3 if index else 1/3
        right = (times[index+1]-time)/3 if index+1 < len(times) else 1/3
        result.append(dict(time=time, value=value, interpolation="BEZIER",
            handle_left=[time-left, value-left*slope], handle_right=[time+right, value+right*slope]))
    return result


def import_animation(context, rig, bundle):
    from .importers import Bundle
    manifest = bundle.manifest
    bones = ex.rig_bones(rig)
    for bind in manifest.get("bind_pose", []):
        ex.require(bind["name"] in rig.data.bones, "Clip bind pose refers to a missing bone")
        bone = rig.data.bones[bind["name"]]
        actual = ex.joint_matrix(rig, bone.matrix_local, bone)
        target = bundle.for_rig(rig, bundle.matrix(bind))
        ex.require(max(abs(actual[i][j]-target[i][j]) for i in range(4) for j in range(4)) < 0.001,
            f"{bone.name}: source bind pose differs from the selected skeleton family")
    tracks = {t["bone_name"]: t for t in manifest["tracks"]}
    ex.require(set(tracks) == {ex.MOVER, *[b.name for b in bones]}, "Clip tracks must match the selected skeleton, including object_movement")
    count, first = manifest["frame_count"], manifest.get("frame_start", 0.0)
    source = {name: [bundle.span(t["channels"][ch]) for ch in ex.ANIMATION_CHANNELS] for name, t in tracks.items()}
    expected = {}
    for name, channels in source.items():
        expected[name] = [bundle.matrix(dict(position=[channels[c][f] for c in range(3)], rotation=[channels[c][f] for c in range(3, 6)], scale=[channels[c][f] for c in range(6, 9)])) for f in range(count)]
    root_noise = [0.0, 0.0, 0.0]
    for matrix in expected[bones[0].name]:
        t, q, s = matrix.decompose()
        rotation_error = max(abs(matrix[i][j]-float(i == j)) for i in range(3) for j in range(3))
        errors = (max(abs(v)*bundle.unit for v in t), rotation_error, max(abs(v-1) for v in s))
        root_noise = [max(a, b) for a, b in zip(root_noise, errors)]
    ex.require(all(error <= manifest.get("precision_"+key, 0.005)/2+1e-7 for key, error in zip("trs", root_noise)),
        "Clip root-joint track must be identity within its numerical precision; object_movement carries its motion")
    exact, modes = exact_channels(rig, bundle)
    values = {b.name: [[] for _ in range(9)] for b in bones}
    previous = {b.name: None for b in bones}
    for bone in bones:
        rig.pose.bones[bone.name].rotation_mode = modes.get(bone.name, "XYZ")
    for frame in range(count):
        joint_frames, bone_frames = {}, {}
        for bone in bones:
            joint = expected[ex.MOVER][frame] @ expected[bone.name][frame] if bone.parent is None else joint_frames[bone.parent.name] @ expected[bone.name][frame]
            joint_frames[bone.name] = joint
            correction = bone.matrix_local.inverted() @ ex.joint_matrix(rig, bone.matrix_local, bone)
            bone_frames[bone.name] = bundle.for_rig(rig, joint) @ correction.inverted()
            kwargs = {"parent_matrix": bone_frames[bone.parent.name], "parent_matrix_local": bone.parent.matrix_local} if bone.parent else {}
            basis = bone.convert_local_to_pose(bone_frames[bone.name], bone.matrix_local, invert=True, **kwargs)
            ex.trs(basis, f"{bone.name}: imported pose at {first+frame}")
            t, q, s = basis.decompose()
            mode = rig.pose.bones[bone.name].rotation_mode
            e = q.to_euler(mode, previous[bone.name]) if previous[bone.name] else q.to_euler(mode)
            previous[bone.name] = e
            for channel, value in zip(values[bone.name], (*t, *e, *s)):
                channel.append(value)
    action = bpy.data.actions.new(manifest["name"].split("/")[-1])
    action.use_fake_user = True
    action.xray_export, action.xray_additive = True, manifest.get("is_additive", False)
    action.xray_sample_count, action.xray_frame_start, action.xray_fps = count, first, manifest["fps"]
    action.xray_root_already_relative = manifest.get("root_already_relative", bundle.engine)
    for key in "trs":
        setattr(action, "xray_precision_"+key, manifest.get("precision_"+key, 0.005))
    action["xray_source_curves"] = json.dumps(manifest.get("curves", {}))
    slot = action.slots.new("OBJECT", rig.name)
    bag = action.layers.new("X-Ray Import").strips.new(type="KEYFRAME").channelbag(slot, ensure=True)
    data = rig.animation_data_create()
    data.action, data.action_slot = action, slot
    data.use_nla = False
    curves, seeds, descriptions = {}, {}, []
    eps = [manifest.get("precision_t", 0.005)/rig.xray_unit_scale/20]*3+[manifest.get("precision_r", 0.005)/20]*3+[manifest.get("precision_s", 0.005)/20]*3
    for bone in bones:
        original_times = manifest.get("import_metadata", {}).get("seed_frames", {}).get(bone.name, [])
        if bone.parent is None:
            original_times = list(original_times)+manifest.get("import_metadata", {}).get("seed_frames", {}).get(ex.MOVER, [])
        for track in manifest.get("curves", {}).get("tracks", []):
            if track["bone_name"] == bone.name:
                original_times += [k["time"] for c in track["channels"].values() for k in c["keys"]]
        initial = {first, first+count-1, *[t for t in original_times if first <= t <= first+count-1]}
        for ci, channel in enumerate(values[bone.name]):
            key = bone.name, ci
            path = rig.pose.bones[bone.name].path_from_id(("location", "rotation_euler", "scale")[ci//3])
            curve = bag.fcurves.new(path, index=ci % 3)
            curves[key] = curve
            seeds[key] = set(initial)
            if key in exact:
                source_curve = exact[key]
                put_curve(curve, source_curve["keys"], source_curve.get("pre_infinity", "CONSTANT"), source_curve.get("post_infinity", "CONSTANT"))
            else:
                # Retain every seed; add worst-error samples until channel error is bounded.
                for _ in range(count+1):
                    put_curve(curve, fitted_keys(channel, first, sorted(seeds[key])))
                    errors = [abs(curve.evaluate(first+f)-v) for f, v in enumerate(channel)]
                    worst = max(range(count), key=lambda f: errors[f])
                    if errors[worst] <= eps[ci]+1e-6:
                        break
                    seeds[key].add(first+worst)
    fps = manifest["fps"]
    context.scene.render.fps, context.scene.render.fps_base = max(1, round(fps)), max(1, round(fps))/fps

    def reconcile():
        actual = Bundle(result=ex.animation(context, rig, manifest["name"], action=action))
        worst, bad, maximum = 0, None, 0
        for track in actual.manifest["tracks"]:
            channels = [actual.span(track["channels"][ch]) for ch in ex.ANIMATION_CHANNELS]
            for f in range(count):
                matrix = actual.matrix(dict(position=[c[f] for c in channels[:3]], rotation=[c[f] for c in channels[3:6]], scale=[c[f] for c in channels[6:]]))
                target = expected[track["bone_name"]][f]
                t, q, s = matrix.decompose(); et, eq, es = target.decompose()
                angle = q.rotation_difference(eq).angle
                errors = (max(abs(a*actual.unit-b*bundle.unit) for a, b in zip(t, et)), min(angle, math.tau-angle), max(abs(a-b) for a, b in zip(s, es)))
                tolerances = (manifest.get("precision_t", 0.005), manifest.get("precision_r", 0.005), manifest.get("precision_s", 0.005))
                severity = max(error/(tolerance+2e-5) for error, tolerance in zip(errors, tolerances))
                maximum = max(maximum, *errors)
                if severity > worst:
                    worst, bad = severity, (bones[0].name if track["bone_name"] == ex.MOVER else track["bone_name"], f)
        return worst, bad, maximum
    fallen_back = set()
    for _ in range(count+len(bones)+1):
        severity, bad, maximum = reconcile()
        if severity <= 1:
            break
        name, frame = bad
        if any(key[0] == name for key in exact):
            exact = {k: v for k, v in exact.items() if k[0] != name}
            fallen_back.add(name)
        for key, curve in curves.items():
            if key in exact:
                continue
            seeds[key].add(first+frame)
            put_curve(curve, fitted_keys(values[key[0]][key[1]], first, sorted(seeds[key])))
    else:
        raise ValueError(f"Imported poses cannot meet clip precision: {bad}")
    for key, curve in curves.items():
        descriptions.append(dict(bone=key[0], channel=ex.ANIMATION_CHANNELS[key[1]], mode="exact" if key in exact else "fit",
            keys=len(curve.keyframe_points), reason="sample_reconciliation" if key[0] in fallen_back else "joint_frame_mapping" if key in exact else "sampled_pose"))
    report = dict(action=action.name, frames=count, max_pose_error=maximum, channels=descriptions, root_identity_noise=root_noise)
    action["xray_import_report"] = json.dumps(report)
    context.scene.frame_start, context.scene.frame_end = math.floor(first), math.ceil(first+count-1)
    context.scene.frame_set(math.floor(first), subframe=first % 1)
    return report
