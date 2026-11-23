# SPDX-License-Identifier: GPL-3.0-or-later
"""B5 batch worker: installed ZIP -> editable .blend -> cook -> engine playback."""

import argparse
import hashlib
import importlib
import json
import math
import re
from pathlib import Path
import subprocess
import sys
import traceback

import bpy
import addon_utils
from mathutils import Matrix

sys.path.insert(0, str(Path(__file__).resolve().parent))
from migration_state import write_json, family_record, fingerprint


def asset_name(label):
    safe=re.sub(r"[^A-Za-z0-9_.-]","_",label).strip(".") or "asset"
    return safe[:50]+"_"+fingerprint(label)[:8] if safe!=label or len(safe)>50 else safe


def expected_samples(bundle, output, order):
    manifest = bundle.manifest
    tracks = {t["bone_name"]: t for t in manifest["tracks"]}
    lines = [f'{len(order)} {manifest["frame_count"]} {30/manifest["fps"]:.12g} {int(manifest.get("is_additive", False))}']
    reflection = Matrix.Diagonal((1, 1, -1, 1))
    for name in order:
        lines.append(name)
        channels = [bundle.span(tracks[name]["channels"][ch]) for ch in ex.ANIMATION_CHANNELS]
        initial, previous = None, None
        for frame in range(manifest["frame_count"]):
            matrix = bundle.matrix(dict(position=[c[frame] for c in channels[:3]],
                rotation=[c[frame] for c in channels[3:6]], scale=[c[frame] for c in channels[6:]]))
            matrix = reflection @ matrix @ reflection
            matrix.translation *= bundle.unit
            if name == ex.MOVER and not manifest.get("root_already_relative", bundle.engine):
                if initial is None: initial = matrix.inverted()
                matrix = initial @ matrix
            t, q, s = matrix.decompose()
            e = q.to_euler("XYZ", previous) if previous else q.to_euler("XYZ")
            previous = e
            lines.append(" ".join(f"{v:.12g}" for v in (*t, *(-v for v in e), *s)))
    output.with_suffix(".clip_samples").write_text("\n".join(lines)+"\n", encoding="utf-8")
    precisions = [manifest.get("precision_"+k, 0.005) for k in "trs" for _ in range(3)]
    output.with_suffix(".clip_precision").write_text(" ".join(str(p+2e-6) for p in precisions)+"\n", encoding="utf-8")


def clear():
    if bpy.context.mode != "OBJECT": bpy.ops.object.mode_set(mode="OBJECT")
    for owner in ("objects", "collections", "meshes", "armatures", "materials", "actions"):
        data = getattr(bpy.data, owner)
        for block in list(data): data.remove(block, do_unlink=True)
    bpy.context.scene.unit_settings.scale_length = 1
    bpy.context.scene.render.fps, bpy.context.scene.render.fps_base = 30, 1
    bpy.context.scene.frame_set(0)


def migrate(job, settings):
    clear()
    archive, work = Path(job["exchange"]), Path(job["work"])
    work.mkdir(parents=True, exist_ok=True)
    index = json.loads((archive/"scene.json").read_text(encoding="utf-8")) if (archive/"scene.json").is_file() else {"assets": ["."]}
    reports, errors, verified, objects, rigs = [], [], [], [], {}
    import_warnings=[]
    prepared=[]
    for ai,entry in enumerate(index["assets"]):
        path = (archive/entry).resolve()
        if not path.is_relative_to(archive.resolve()): raise ValueError("Unsafe archived scene entry")
        prepared.append((ai,entry,path,imp.Bundle(path)))
    animation_count=sum(b.manifest["asset_kind"]=="animation" for _,_,_,b in prepared)
    def rig_key(rig):
        return fingerprint({ex.MOVER:None,**{b.name:b.parent.name if b.parent else ex.MOVER for b in ex.rig_bones(rig)}})
    def family_key(bundle):
        return family_record(bundle.manifest["root"],"")["id"] if "root" in bundle.manifest else None
    def remember(imported):
        objects.extend(imported["roots"])
        import_warnings.extend(imported.get("warnings",[]))
        if (rig:=imported["rig"]) is not None:
            group=rigs.setdefault(rig_key(rig),[])
            if rig not in group: group.append(rig)
    def bind_matches(bundle,rig,records):
        for bone in records:
            if bone["name"] not in rig.data.bones: return False
            actual=ex.joint_matrix(rig,rig.data.bones[bone["name"]].matrix_local,rig.data.bones[bone["name"]])
            expected=bundle.for_rig(rig,bundle.matrix(bone))
            if max(abs(actual[i][j]-expected[i][j]) for i in range(4) for j in range(4))>=0.001: return False
        return True
    # Establish actual skin rest frames before importing sampled animation.
    # Meshes with compatible binds share a rig; distinct binds receive their own
    # matching animation rather than a disconnected static mesh armature.
    priority={"skinned_model":0,"skeleton":1,"static_model":2,"physics":2,"animation":3}
    for ai,entry,path,bundle in sorted(prepared,key=lambda item:priority[item[3].manifest["asset_kind"]]):
        try:
            kind = bundle.manifest["asset_kind"]
            key=family_key(bundle)
            candidates=rigs.get(key,[])
            if kind=="skeleton" and candidates: continue
            if kind!="animation":
                target=next((r for r in candidates if bind_matches(bundle,r,bundle.manifest["bones"])),None) if kind=="skinned_model" else None
                remember(imp.import_asset(bpy.context,path,rig=target))
                continue
            for target_index,target in enumerate(candidates or [None]):
                original = bundle.manifest
                import_path=path
                if target is not None and not bind_matches(bundle,target,original.get("bind_pose",[])):
                    # The source samples are evaluated joint poses, independent
                    # of the authoring rest pose. Refit those poses on the proven
                    # same hierarchy's skin bind, retaining the original archive.
                    adjusted=json.loads(json.dumps(original))
                    adjusted["migration_source_bind_pose"]=adjusted.pop("bind_pose",[])
                    adjusted["migration_bind_policy"]="evaluated_poses_on_matching_skin_hierarchy"
                    import_path=work/"bind_adapted"/f"{ai:03d}_{target_index:03d}"
                    write_json(import_path/"manifest.json",adjusted)
                    (import_path/"payload.bin").write_bytes(bundle.payload)
                imported=imp.import_asset(bpy.context,import_path,rig=target)
                remember(imported)
                rig=imported["rig"]
                label=Path(job["path"]).stem
                if animation_count>1: label+="_"+ex.rig_bones(rig)[0].name
                action=rig.animation_data.action
                action.name=asset_name(label)
                action["xray_source_display_name"]=label
                for report in imported["reports"]:
                    report["action"]=action.name
                    report["source_keys_before"]={t["bone_name"]:{name:len(curve["keys"]) for name,curve in t["channels"].items()}
                        for t in original.get("curves",{}).get("tracks",[])}
                    report["source_seed_counts"]={name:len(times) for name,times in original.get("import_metadata",{}).get("seed_frames",{}).items()}
                action["xray_import_report"]=json.dumps(imported["reports"][0])
                imported["collection"]["xray_import_report"]=json.dumps(imported["reports"])
                reports.extend(imported["reports"])
                result = ex.animation(bpy.context, rig, "animation", action=rig.animation_data.action,
                    **{"precision_"+k: original.get("precision_"+k, 0.005)/10 for k in "trs"})
                destination = work/f"animation_{ai:03d}_{target_index:03d}"
                ex.export_and_cook(result, destination, Path(settings["cli"]))
                clip = destination/"raw/animation.clip"
                expected_samples(bundle, clip, [t["bone_name"] for t in result.manifest["tracks"]])
                process = subprocess.run([settings["playback"], "--clip-samples", str(clip.with_suffix(".clip_samples"))],
                    capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=120)
                (destination/"playback.log").write_text(process.stdout+process.stderr, encoding="utf-8")
                if process.returncode: raise ValueError("Engine playback failed: "+process.stdout+process.stderr)
                verified.append(dict(asset=entry, frames=original["frame_count"], fps=original["fps"], engine_playback="PASS"))
        except Exception as error:
            errors.append(dict(asset=entry, reason=str(error), traceback=traceback.format_exc()))
            if job["kind"] == "clip": raise
    if not objects: raise ValueError("No assets imported: "+str(errors))
    blend = Path(job["blend"]); blend.parent.mkdir(parents=True, exist_ok=True)
    bpy.context.scene["xray_migration_source"] = job["path"]
    bpy.context.scene["xray_migration_fingerprint"] = job["fingerprint"]
    bpy.context.scene["xray_migration_report"] = json.dumps(reports)
    bpy.context.scene.xray_export.name=asset_name(Path(job["path"]).stem)
    bpy.context.preferences.filepaths.save_version = 0
    temporary = blend.with_name(blend.stem+".pending.blend")
    bpy.ops.wm.save_as_mainfile(filepath=str(temporary), check_existing=False, compress=True)
    temporary.replace(blend)
    channels = [c for report in reports for c in report["channels"]]
    return dict(status="with_warnings" if errors or import_warnings or job.get("warnings") else "migrated", blend=str(blend),
        reports=reports, errors=errors, import_warnings=import_warnings, verified=verified,
        key_counts={"exact_channels": sum(c["mode"]=="exact" for c in channels),
                    "fit_channels": sum(c["mode"]=="fit" for c in channels),
                    "source_direct_keys":sum(len(curve["keys"]) for _,_,_,b in prepared for t in b.manifest.get("curves",{}).get("tracks",[]) for curve in t["channels"].values()),
                    "source_controller_keys":sum({name:count for _,_,_,b in prepared for name,count in b.manifest.get("import_metadata",{}).get("source_curve_key_counts",{}).items()}.values()),
                    "keys_after": sum(c["keys"] for c in channels)})


def verify_saved(job):
    result_path=Path(job["result"])
    result=json.loads(result_path.read_text(encoding="utf-8"))
    if result["status"]=="failed": return result
    bpy.ops.wm.open_mainfile(filepath=job["blend"],load_ui=False,use_scripts=False)
    if bpy.context.scene.get("xray_migration_fingerprint")!=job["fingerprint"]:
        raise ValueError("Saved .blend has a different migration fingerprint")
    keys=sum(len(c.keyframe_points) for a in bpy.data.actions for layer in a.layers for strip in layer.strips
             for slot in a.slots if (bag:=strip.channelbag(slot)) for c in bag.fcurves)
    if keys!=result["key_counts"]["keys_after"]: raise ValueError("Action keys did not survive save/reopen")
    for rig in (o for o in bpy.data.objects if o.type=="ARMATURE"):
        if not all(b.xray_has_joint_correction for b in rig.data.bones):
            raise ValueError("Imported joint corrections did not survive save/reopen")
    result["reopen_verified"]=True
    return result


parser = argparse.ArgumentParser()
parser.add_argument("--jobs", type=Path, required=True)
parser.add_argument("--phase",choices=("migrate","verify"),default="migrate")
args = parser.parse_args(sys.argv[sys.argv.index("--")+1:])
data = json.loads(args.jobs.read_text(encoding="utf-8"))
profile = Path(data["profile"])
module_name = "bl_ext.xray_migration.xray_blender"
repo_dir = profile/"repo"
repo_dir.mkdir(parents=True, exist_ok=True)
repo = next((r for r in bpy.context.preferences.extensions.repos if r.module == "xray_migration"), None)
if repo is None:
    repo = bpy.context.preferences.extensions.repos.new(name="X-Ray Migration", module="xray_migration", custom_directory=str(repo_dir))
stamp = profile/"archive.sha256"
archive_hash = hashlib.sha256(Path(data["archive"]).read_bytes()).hexdigest()
if not (repo_dir/"xray_blender/blender_manifest.toml").is_file() or not stamp.is_file() or stamp.read_text()!=archive_hash:
    shutil_mod = __import__("shutil")
    time_mod = __import__("time")
    if (repo_dir/"xray_blender@").exists():
        shutil_mod.rmtree(repo_dir/"xray_blender@", ignore_errors=True)
    assert bpy.ops.extensions.package_install_files(filepath=data["archive"], repo=repo.module, enable_on_install=True, overwrite=True) == {"FINISHED"}
    if not (repo_dir/"xray_blender/blender_manifest.toml").is_file() and (repo_dir/"xray_blender@/blender_manifest.toml").is_file():
        for _ in range(10):
            time_mod.sleep(0.1)
            try:
                if (repo_dir/"xray_blender").exists():
                    shutil_mod.rmtree(repo_dir/"xray_blender")
                (repo_dir/"xray_blender@").replace(repo_dir/"xray_blender")
                bpy.ops.extensions.repo_refresh_all(use_active_only=False)
                addon_utils.enable(module_name, default_set=True)
                break
            except PermissionError:
                pass
    if not (repo_dir/"xray_blender/blender_manifest.toml").is_file(): raise RuntimeError("Blender installer did not extract the ZIP")
    stamp.write_text(archive_hash)
    bpy.ops.wm.save_userpref()
else:
    # Factory startup intentionally uses fresh preferences. Register the already
    # installed repository, refresh its local metadata, then enable its module.
    assert bpy.ops.extensions.repo_refresh_all(use_active_only=False) == {"FINISHED"}
    addon_utils.enable(module_name, default_set=True)
module = importlib.import_module(module_name)
assert module.__addon_enabled__
assert Path(module.__file__).resolve().is_relative_to(profile.resolve())
imp = importlib.import_module(module_name+".importers")
ex = importlib.import_module(module_name+".exporters")
for job in data["jobs"]:
    try:
        result = migrate(job, data) if args.phase=="migrate" else verify_saved(job)
    except Exception as error:
        result = dict(status="failed", reason=str(error), traceback=traceback.format_exc())
    write_json(job["result"], result)
    print(json.dumps(dict(path=job["path"], status=result["status"])), flush=True)
