"""xray-migrate-legacy: resumable migration into a separate working copy."""

import argparse
from collections import Counter, defaultdict
from concurrent.futures import ThreadPoolExecutor, as_completed
from contextlib import contextmanager
import json
import os
from pathlib import Path
import subprocess
import sys
import threading
import time

HERE = Path(__file__).resolve().parent
TOOLS = HERE.parent
sys.path.insert(0, str(TOOLS))
from build_extension import build
from legacy_corpus import inventory
from migration_state import SCHEMA, digest, fingerprint, write_json as write_state_json, safe_path, family_record, match_family, output_hashes, reusable
from migration_reconcile import reconcile
from dcc_common.legacy_codec import decompile_asset, load_config
from dcc_common.ma_reader import decode_ma_to_exchange, trax_clip_bones
from dcc_common.exchange import validate_bundle


def write_json(path, value):
    # Windows readers may briefly hold the destination without SHARE_DELETE.
    # Keep atomic replacement, and leave genuine permission errors visible.
    for attempt in range(10):
        try:
            return write_state_json(path,value)
        except PermissionError:
            if attempt==9: raise
            time.sleep(0.1)


def launch(command, log, env, timeout):
    log.parent.mkdir(parents=True, exist_ok=True)
    if log.is_file():
        previous=log.with_name(log.stem+".previous-"+digest(log)[:12]+log.suffix)
        if not previous.exists(): log.replace(previous)
    with log.open("w", encoding="utf-8") as stream:
        try:
            process = subprocess.run([str(p) for p in command], env=env, stdout=stream, stderr=subprocess.STDOUT, timeout=timeout)
            return process.returncode
        except subprocess.TimeoutExpired:
            stream.write("\nMigration worker exceeded its timeout\n")
            return -1


@contextmanager
def workspace_lock(source,output):
    source,output=source.resolve(),output.resolve()
    if source==output or output.is_relative_to(source) or source.is_relative_to(output):
        raise ValueError("Migration output and read-only source must be separate directory trees")
    output.mkdir(parents=True,exist_ok=True)
    with (output/".migration.lock").open("a+b") as stream:
        if stream.tell()==0: stream.write(b"0"); stream.flush()
        stream.seek(0)
        if os.name=="nt":
            import msvcrt
            msvcrt.locking(stream.fileno(),msvcrt.LK_NBLCK,1)
        else:
            import fcntl
            fcntl.flock(stream.fileno(),fcntl.LOCK_EX|fcntl.LOCK_NB)
        try: yield
        finally:
            stream.seek(0)
            if os.name=="nt": msvcrt.locking(stream.fileno(),msvcrt.LK_UNLCK,1)
            else: fcntl.flock(stream.fileno(),fcntl.LOCK_UN)


def profile_env(root, kind):
    root.mkdir(parents=True, exist_ok=True)
    env = dict(os.environ, PYTHONNOUSERSITE="1")
    scratch=root/"temp"; scratch.mkdir(exist_ok=True)
    env.update(TEMP=str(scratch),TMP=str(scratch))
    for key in ("PYTHONPATH", "PYTHONHOME", "MAYA_LOCATION", "MAYA_MODULE_PATH"):
        env.pop(key, None)
    if kind == "maya":
        env.update(MAYA_APP_DIR=str(root), MAYA_SKIP_USERSETUP_PY="1", MAYA_DISABLE_CIP="1", MAYA_DISABLE_CER="1")
    else:
        env["PATH"] = os.pathsep.join(p for p in env.get("PATH", "").split(os.pathsep) if "maya" not in p.lower())
        for suffix in ("CONFIG", "SCRIPTS", "EXTENSIONS", "DATAFILES"):
            folder = root/suffix.lower(); folder.mkdir(exist_ok=True)
            env["BLENDER_USER_"+suffix] = str(folder)
    return env


def run(args):
    source, out = args.source_root.resolve(strict=True), args.output.resolve()
    if source == out or out.is_relative_to(source) or source.is_relative_to(out):
        raise ValueError("Migration output and read-only source must be separate directory trees")
    out.mkdir(parents=True, exist_ok=True)
    manifest_path = out/"migration-manifest.json"
    previous = json.loads(manifest_path.read_text(encoding="utf-8")) if manifest_path.is_file() else {}
    if previous and (previous.get("schema") != SCHEMA or previous.get("source_root") != source.as_posix()):
        raise ValueError("Existing migration manifest belongs to a different corpus/schema")
    print("Hashing the read-only corpus", flush=True)
    corpus = inventory(source)
    write_json(out/"legacy-corpus.json", corpus)
    entries = {e["path"]: e for e in corpus["assets"]}
    archive = build(out/"tools/xray_blender.zip")
    tool_files = [HERE/"migration_state.py", HERE/"migration_reconcile.py", HERE/"migration-cache-compatibility.json", HERE/"migrate_legacy.py", HERE/"migrate_blender.py",
                  TOOLS/"dcc_common/legacy_codec.py", TOOLS/"dcc_common/ma_reader.py", TOOLS/"dcc_common/exchange.py",
                  TOOLS/"maya/plugin/migrate_scene.py", *sorted((TOOLS/"maya/plugin").glob("*_extractor.py"))]
    tools = {p.relative_to(TOOLS).as_posix(): digest(p) for p in tool_files}
    tools.update(extension=digest(archive), blender=digest(args.blender_exe), cli=digest(args.cli_exe), playback=digest(args.playback_exe))
    tool_key = fingerprint(tools)
    extraction_tools = {p.relative_to(TOOLS).as_posix(): digest(p) for p in
        [TOOLS/"maya/plugin/migrate_scene.py", *sorted((TOOLS/"maya/plugin").glob("*_extractor.py")),
         TOOLS/"dcc_common/exchange.py", HERE/"migration_state.py"]}
    extraction_tools.update({name:digest(TOOLS/name) for name in ("maya/plugin/shader_utils.py","maya/plugin/constants.py",
        "dcc_common/constants.py","dcc_common/logical_names.py")})
    extraction_tools["mayapy"] = digest(args.mayapy_exe) if args.mayapy_exe else None
    compatibility=json.loads((HERE/"migration-cache-compatibility.json").read_text(encoding="utf-8"))
    def scene_tools(entry):
        return {**extraction_tools,**({name:digest(TOOLS/name) for name in ("dcc_common/ma_reader.py","dcc_common/dcc_math.py")} if entry["kind"]=="maya_ascii" else {})}
    reference_catalog_key=fingerprint({e["path"]:e["sha256"] for e in entries.values() if e["kind"] in {"maya_ascii","maya_binary"}})
    def scene_key(entry,producer,record):
        data=dict(source=entry["sha256"],tools=producer,clip_sets=sorted(clip_sets),references={p:entries[p]["sha256"] for p in record.get("references",[]) if p in entries})
        if args.anim_layer_base:
            data["anim_layer_base"]=entries.get(args.anim_layer_base,{}).get("sha256") or digest(safe_path(source,args.anim_layer_base))
        if any("Reference " in str(error) for error in record.get("errors",[])):
            data["unresolved_reference_catalog"]=reference_catalog_key
        return fingerprint(data)
    records = {p:r for p,r in previous.get("assets",{}).items() if p in entries}
    manifest = dict(schema=SCHEMA, source_root=source.as_posix(), source_access="read_only", corpus_sha256=corpus["content_sha256"],
                    tools=tools, assets=records, retired_assets=sorted(set(previous.get("retired_assets",[]))|{p for p in previous.get("assets",{}) if p not in entries}))
    def save():
        manifest["summary"] = dict(statuses=dict(Counter(r["status"] for r in records.values())),
            inventory_assets=len(entries), recorded_assets=len([p for p in records if p in entries]),
            engine_verified_clips=sum(e["kind"]=="clip" and bool(records.get(p,{}).get("verified")) for p,e in entries.items()))
        manifest["summary"]["reconciliation"]=dict(Counter(r.get("reconciliation",{}).get("status","not_checked") for p,r in records.items() if entries.get(p,{}).get("kind")=="clip"))
        write_json(manifest_path, manifest)
    selected = [e for e in entries.values() if not args.include or any(s.casefold() in e["path"].casefold() for s in args.include)]
    if args.limit: selected = selected[:args.limit]
    reference_index = defaultdict(list)
    clip_sets=set(); clip_names={}; clip_errors={}
    for e in entries.values():
        if e["kind"] in {"maya_binary", "maya_ascii"}: reference_index[Path(e["path"]).name.casefold()].append(str(source/e["path"]))
        if e["kind"]=="clip":
            try:
                names=tuple(sorted(load_config(source/e["path"])["splines"]["bone_names"]))
                clip_sets.add(names); clip_names[e["path"]]=names
            except Exception as error: clip_errors[e["path"]]=str(error)
    def paths(e):
        key = fingerprint(e["path"])[:20]
        work = out/"work"/key
        return dict(path=e["path"], kind=e["kind"], source=str(safe_path(source,e["path"])), temporary=e.get("temporary",False),
                    exchange=str(work/"exchange"), work=str(work), working=str(work/"source"/Path(e["path"]).name),
                    blend=str(safe_path(out/"resources/sources/blender",e["path"]+".blend")), result=str(work/"blender-result.json"))
    def key_for(e, dependencies=None):
        return fingerprint(dict(source=e["sha256"], tools=tool_key, dependencies=dependencies or {}))
    extraction_jobs = []
    trax_ma_jobs = []
    for e in selected:
        if e["kind"] not in {"maya_binary", "maya_ascii"}: continue
        job = paths(e); work=Path(job["work"])
        extraction_result=work/"maya-result.json"
        cache=json.loads(extraction_result.read_text(encoding="utf-8")) if extraction_result.is_file() else {}
        producer=scene_tools(e)
        extraction_key=scene_key(e,producer,cache)
        # The baseline pins the exact unchanged modules whose hashes were absent
        # from early B5 cache keys. Changed dependency code never takes this path.
        baseline=compatibility.get("dependency_baseline",{})
        prior_producer={k:v for k,v in producer.items() if baseline.get(k)!=v}
        candidates=[(prior_producer,dict(dependency_baseline=True))]
        for update in compatibility["advisory_worker_updates"]:
            if update["to_sha256"]!=producer["maya/plugin/migrate_scene.py"] or cache.get("errors"): continue
            for base in (producer,prior_producer):
                candidates.append(({**base,"maya/plugin/migrate_scene.py":update["from_sha256"]},update))
        if cache.get("fingerprint")==extraction_key: candidates=[]
        for old_tools,update in candidates:
            if reusable(cache,scene_key(e,old_tools,cache),out):
                cache.update(fingerprint=extraction_key,source_fingerprint=extraction_key,compatible_cache_update=update,producer_tools=producer)
                write_json(extraction_result,cache)
                break
        if reusable(cache,extraction_key,out) or (cache.get("source_fingerprint")==extraction_key and cache.get("status")=="failed" and not args.retry_failed): continue
        if e["kind"]=="maya_ascii":
            try:
                decode_ma_to_exchange(source/e["path"], Path(job["exchange"])/"ascii", logical_name="scene")
                scene=Path(job["exchange"])/"ascii"
                write_json(Path(job["exchange"])/"scene.json", dict(assets=["ascii/"+p for p in json.loads((scene/"scene.json").read_text())["assets"]]))
                family_data=[]
                for item in json.loads((scene/"scene.json").read_text())["assets"]:
                    m=json.loads((scene/item/"manifest.json").read_text())
                    if m["asset_kind"]=="skeleton": family_data.append(dict(root=m["root"],bind_pose=m.get("bind_pose",[]),coord_space=m["coord_space"]))
                write_json(extraction_result, dict(status="migrated", route="C1", source_fingerprint=extraction_key,fingerprint=extraction_key,producer_tools=producer,
                    outputs=output_hashes(out,[*Path(job["exchange"]).rglob("*.json"),*Path(job["exchange"]).rglob("*.bin")]),
                    references=[], families=family_data, exchange=job["exchange"]))
                continue
            except Exception as error:
                if "Standalone Maya Trax animClip requires a target skeleton" in str(error):
                    trax_ma_jobs.append((e, job, extraction_result, producer, extraction_key))
                    continue
                job["ascii_fallback_reason"]=str(error)
        if not args.mayapy_exe:
            write_json(extraction_result, dict(status="failed", reason="Maintainer Maya unavailable; binary/complex Maya scenes need the Maya route", source_fingerprint=extraction_key))
        else:
            job["result"]=str(extraction_result)
            job["source_fingerprint"]=extraction_key
            if args.anim_layer_base:
                job["anim_layer_base"]=str(safe_path(source,args.anim_layer_base))
            extraction_jobs.append(job)
    def extract_batch(batch_number,jobs):
        for job in jobs: Path(job["result"]).unlink(missing_ok=True)
        plan=out/"jobs"/f"maya-{batch_number:04d}.json"
        workspace=out/"work"/f"maya-batch-{batch_number:04d}"
        env=profile_env(out/"profiles/maya"/f"batch-{batch_number:04d}","maya")
        write_json(plan,dict(source_root=str(source),workspace=str(workspace),reference_index=dict(reference_index),clip_sets=sorted(clip_sets),jobs=jobs))
        print(f"Maya batch {batch_number+1}: {len(jobs)} scenes",flush=True)
        code=launch([args.mayapy_exe,TOOLS/"maya/plugin/migrate_scene.py","--jobs",plan],out/"logs"/f"maya-{batch_number:04d}.log",env,args.worker_timeout)
        # A native crash in one old scene must not mark unattempted later scenes
        # as corrupt. Continue each not-yet-started scene in a fresh host.
        for ji,job in enumerate(jobs):
            if not Path(job["result"]).is_file():
                isolated=plan.with_name(plan.stem+f"-isolated-{ji}.json")
                write_json(isolated,dict(source_root=str(source),workspace=str(workspace),reference_index=dict(reference_index),clip_sets=sorted(clip_sets),jobs=[job]))
                launch([args.mayapy_exe,TOOLS/"maya/plugin/migrate_scene.py","--jobs",isolated],out/"logs"/(isolated.stem+".log"),env,args.worker_timeout)
        completed=[]
        for job in jobs:
            result_path=Path(job["result"])
            result=json.loads(result_path.read_text(encoding="utf-8")) if result_path.is_file() else dict(status="failed",reason=f"Maya worker exited {code} before completing this scene")
            if result["status"]=="running": result=dict(status="failed",reason=f"Maya crashed or timed out while opening/extracting this scene (batch exit {code})")
            result.update(source_fingerprint=job["source_fingerprint"],route="B",ascii_fallback_reason=job.get("ascii_fallback_reason"))
            result["source_fingerprint"]=scene_key(entries[job["path"]],scene_tools(entries[job["path"]]),result)
            result["fingerprint"]=result["source_fingerprint"]
            result["producer_tools"]=scene_tools(entries[job["path"]])
            if result["status"]!="failed":
                result["outputs"]=output_hashes(out,[*Path(job["exchange"]).rglob("*.json"),*Path(job["exchange"]).rglob("*.bin")])
            write_json(result_path,result)
            completed.append((job["path"],dict(status="failed" if result["status"]=="failed" else "pending",phase="maya_extracted",
                                     reason=result.get("reason") or str(result.get("errors",[])),source_fingerprint=result["source_fingerprint"])))
        return completed
    batches=[extraction_jobs[i:i+args.batch_size] for i in range(0,len(extraction_jobs),args.batch_size)]
    with ThreadPoolExecutor(max_workers=args.host_workers) as pool:
        pending={pool.submit(extract_batch,i,jobs):i for i,jobs in enumerate(batches)}
        completed_count=0
        for task in as_completed(pending):
            for path,record in task.result(): records[path]=record; completed_count+=1
            print(f"Maya extraction completed {completed_count} / {len(extraction_jobs)}",flush=True)
            save()
    if args.prepare_only:
        print("Maya/MA extraction cached; Blender migration has not run",flush=True)
        return 0
    families=[]
    # Include cached scenes from earlier partial runs, not just the current selection.
    for e in entries.values():
        job=paths(e); cache=Path(job["work"])/"maya-result.json"
        if e["kind"] in {"maya_binary","maya_ascii"} and cache.is_file():
            result=json.loads(cache.read_text(encoding="utf-8"))
            for f in result.get("families",[]):
                try: families.append(family_record(f["root"],e["path"],bind_pose=f.get("bind_pose"),coord_space=f.get("coord_space")))
                except ValueError: pass
        if e["kind"]=="skeleton":
            try:
                target=out/"families"/fingerprint(e["path"])[:20]
                decompile_asset(source/e["path"],target,logical_name="skeleton")
                f=json.loads((target/"manifest.json").read_text())
                families.append(family_record(f["root"],e["path"],coord_space=f["coord_space"]))
            except Exception as error: print("Skeleton catalog:",e["path"],str(error),flush=True)
    if args.family_catalog:
        catalog=json.loads(args.family_catalog.read_text(encoding="utf-8"))
        rows=catalog["families"] if isinstance(catalog,dict) else catalog
        for f in rows:
            verified=family_record(f["root"],f["evidence"])
            if verified["id"]!=f["id"]: raise ValueError("Imported skeleton catalog has an invalid hierarchy fingerprint")
            families.append(verified)
    write_json(out/"skeleton-families.json",families)
    grouped=defaultdict(list)
    for f in families: grouped[f["id"]].append(f)
    compact=[]
    for key,rows in sorted(grouped.items()):
        chosen=sorted(rows,key=lambda f:(not f["evidence"].endswith(".skeleton"),f["evidence"]))[0]
        compact.append(dict(id=key,root=chosen["root"],evidence=chosen["evidence"],
            evidence_sources=[dict(path=p,sha256=entries.get(p,{}).get("sha256")) for p in sorted({f["evidence"] for f in rows})]))
    write_json(out/"skeleton-catalog.json",dict(schema="xray-skeleton-catalog/v1",families=compact))
    overrides=json.loads(args.family_map.read_text(encoding="utf-8")) if args.family_map else {}
    for e, job, extraction_result, producer, extraction_key in trax_ma_jobs:
        try:
            needed = trax_clip_bones(source/e["path"])
            preferred = overrides.get(e["path"])
            candidates = [f for f in families if needed.issubset(set(f["parents"])) and (not preferred or f["id"] == preferred)]
            skel_candidates = [f for f in candidates if str(f.get("evidence", "")).endswith(".skeleton")]
            pool_cands = skel_candidates or candidates
            variants = {f["id"] for f in pool_cands}
            if len(variants) != 1:
                raise ValueError(f"Trax MA target skeleton {'ambiguous' if variants else 'missing'} for {len(needed)} bones; variants={sorted(variants)}")
            chosen = sorted(pool_cands, key=lambda f: f["evidence"])[0]
            decode_ma_to_exchange(source/e["path"], Path(job["exchange"])/"ascii", logical_name="scene", skeleton_bundle=chosen)
            scene = Path(job["exchange"])/"ascii"
            write_json(Path(job["exchange"])/"scene.json", dict(assets=["ascii/"+p for p in json.loads((scene/"scene.json").read_text())["assets"]]))
            write_json(extraction_result, dict(
                status="with_warnings",
                route="C1",
                source_fingerprint=extraction_key,
                fingerprint=extraction_key,
                producer_tools=producer,
                warnings=[f"Trax animClip bound to skeleton {chosen['evidence']} ({chosen['id'][:12]})"],
                outputs=output_hashes(out, [*Path(job["exchange"]).rglob("*.json"), *Path(job["exchange"]).rglob("*.bin")]),
                references=[],
                families=[],
                exchange=job["exchange"],
            ))
        except Exception as error:
            write_json(extraction_result, dict(status="failed", route="C1", reason=str(error), source_fingerprint=extraction_key))
    blender_jobs=[]
    skipped=0
    for number,e in enumerate(selected):
        p=e["path"]; old=records.get(p,{})
        job=paths(e); work=Path(job["work"])
        dependencies={}
        geometry_source=source/p
        settings_overlay=None
        if e["kind"]=="clip" and e.get("check_data"): dependencies[e["check_data"]]=entries[e["check_data"]]["sha256"]
        job.update(fingerprint=key_for(e,dependencies),warnings=["Temporary source scene"] if e.get("temporary") else [])
        try:
            if e["kind"] in {"maya_binary","maya_ascii"}:
                extraction=json.loads((work/"maya-result.json").read_text(encoding="utf-8"))
                if extraction["status"]=="failed": raise ValueError(extraction.get("reason",str(extraction.get("errors"))))
                for dep in extraction.get("references",[]): dependencies[dep]=entries[dep]["sha256"]
                dependencies["scene_extraction"]=extraction["source_fingerprint"]
                job["warnings"].extend(extraction.get("warnings",[])+extraction.get("errors",[]))
                job["route"]=extraction.get("route","B")
            elif e["kind"]=="clip_check_data":
                if reusable(old,job["fingerprint"],out): skipped+=1; continue
                target=out/"companions"/(fingerprint(p)[:20]+".json")
                write_json(target,dict(source=p,sha256=e["sha256"],role="clip sample companion"))
                records[p]=dict(status="migrated",role="companion",fingerprint=key_for(e),outputs=output_hashes(out,[target]),dependencies={})
                continue
            if e["kind"] in {"model","skinned_model"} and e["storage"]=="directory" and {m["path"] for m in e.get("members",[])}.issubset({"settings","skeleton"}):
                parts=p.split("/")
                position=next((i for i in range(len(parts)-1) if parts[i:i+2]==["sources","models"]),None)
                if position is None: raise ValueError("Settings-only model has no canonical exported geometry path")
                parts[position]="exported"
                counterpart="/".join(parts)
                if counterpart not in entries or entries[counterpart]["kind"]!=e["kind"]:
                    raise ValueError("Settings-only model has no exported geometry counterpart: "+counterpart)
                dependencies[counterpart]=entries[counterpart]["sha256"]
                geometry_source=source/counterpart
                settings_overlay=source/p
                job["warnings"].append("Settings-only source paired with exported geometry: "+counterpart)
            job["fingerprint"]=key_for(e,dependencies)
            # Family identity participates in the key, so newly discovered parent
            # links invalidate only clips which actually use that hierarchy.
            if e["kind"]=="clip":
                if p in clip_errors: raise ValueError(clip_errors[p])
                family=match_family(clip_names[p],families,preferred=overrides.get(p))
                dependencies["skeleton_family"]=family["id"]
                if args.normalize_zero_root_scale:
                    dependencies["normalize_zero_root_scale"]=True
                job["fingerprint"]=key_for(e,dependencies)
                if reusable(old,job["fingerprint"],out): skipped+=1; continue
                decompile_asset(source/p,Path(job["exchange"]),logical_name="animation",normalize_zero_root_scale=args.normalize_zero_root_scale)
                mpath=Path(job["exchange"])/"manifest.json"
                m=json.loads(mpath.read_text(encoding="utf-8"))
                m["root"]=family["root"]
                m["migration_family"]={k:family[k] for k in ("id","evidence")}
                write_json(mpath,m)
                if m.get("import_metadata",{}).get("legacy_zero_root_scale_normalized"):
                    job["warnings"].append("Normalized legacy zero Root.scale (0,0,0) -> (1,1,1); X-Ray 2.0 runtime ignores Root and evaluates children from identity")
                job["warnings"].append("Cooked clip has no bind pose; editable standalone rig uses placeholder rest positions")
                job["family"]=m["migration_family"]
                job["route"]="A"
            elif e["kind"] not in {"maya_binary","maya_ascii"}:
                if e["kind"]!="skinned_model" and reusable(old,job["fingerprint"],out): skipped+=1; continue
                decompile_asset(geometry_source,Path(job["exchange"]),logical_name="asset")
                mpath=Path(job["exchange"])/"manifest.json"; m=json.loads(mpath.read_text())
                if settings_overlay is not None:
                    settings=load_config(settings_overlay/"settings") if (settings_overlay/"settings").is_file() else {}
                    m["migration_source_settings"]=settings
                    m.setdefault("import_metadata",{})["settings_companion"]=p
                    if "rigid_body" in settings: m["rigid_body"]=settings["rigid_body"]
                    for surface in m["surfaces"]:
                        override=settings.get("material_settings",{}).get(surface["name"],{})
                        if "material_name" in override: surface["material_name"]=override["material_name"]
                    if m["asset_kind"]=="skinned_model" and (settings_overlay/"skeleton").is_file():
                        m["skeleton_name"]=load_config(settings_overlay/"skeleton")["skeleton"]
                    write_json(mpath,m)
                    validate_bundle(Path(job["exchange"]))
                if m["asset_kind"]=="skinned_model":
                    # A skin carries influence names, but not parents. Match its
                    # declared skeleton file, rather than inferring from weights.
                    declared=Path(m["skeleton_name"]).name.removesuffix(".skeleton")+".skeleton"
                    candidates=[f for f in families if Path(f["evidence"]).name==declared and {b["name"] for b in m["bones"]}.issubset(f["parents"])]
                    variants={f["id"] for f in candidates}
                    if len(variants)!=1: raise ValueError("Skinned model skeleton is missing or ambiguous: "+declared)
                    m["root"]=candidates[0]["root"]; write_json(mpath,m)
                    dependencies["skeleton_family"]=candidates[0]["id"]
                metadata=m.get("import_metadata",{})
                if metadata.get("skin_weight_rounding_repairs"):
                    job["warnings"].append(f"Repaired f32 skin weight rounding on {metadata['skin_weight_rounding_repairs']} vertices (maximum 1e-6)")
                if metadata.get("generated_joint_names"):
                    job["warnings"].append("Generated stable names for unnamed legacy joints: "+", ".join(metadata["generated_joint_names"]))
                if metadata.get("legacy_root_joint"):
                    job["warnings"].append("Canonicalized ignored legacy root-joint type to Rigid; original metadata retained")
                job["route"]="A"
            job["fingerprint"]=key_for(e,dependencies)
            if reusable(old,job["fingerprint"],out): skipped+=1; continue
            receipt=work/"migration-receipt.json"
            if receipt.is_file():
                recovered=json.loads(receipt.read_text(encoding="utf-8"))
                if reusable(recovered,job["fingerprint"],out):
                    records[p]=recovered
                    skipped+=1
                    continue
            if old.get("fingerprint")==job["fingerprint"] and old.get("status")=="failed" and not args.retry_failed:
                skipped+=1; continue
            job["dependencies"]=dependencies
            records[p]=dict(status="pending",fingerprint=job["fingerprint"],dependencies=dependencies,route=job["route"])
            blender_jobs.append(job)
        except Exception as error:
            records[p]=dict(status="failed",fingerprint=job["fingerprint"],dependencies=dependencies,reason=str(error))
        if number%20==0: save()
    save()
    # Create shared ancestors before pool threads create their distinct profiles;
    # concurrent Windows mkdir of the same parent can report ACCESS_DENIED.
    (out/"profiles/blender").mkdir(parents=True,exist_ok=True)
    def migrate_batch(batch_number,jobs):
        profile=out/"profiles/blender"
        if args.blender_workers>1:
            # A pool thread runs one batch at a time and retains its own profile.
            # Separate workers never install/enable extensions in the same repo.
            profile=profile/("worker-"+threading.current_thread().name)
        for job in jobs: Path(job["result"]).unlink(missing_ok=True)
        plan=out/"jobs"/f"blender-{batch_number:04d}.json"
        write_json(plan,dict(jobs=jobs,archive=str(archive),profile=str(profile),cli=str(args.cli_exe),playback=str(args.playback_exe)))
        offset=batch_number*args.batch_size
        print(f"Blender migration {offset+1}..{offset+len(jobs)} / {len(blender_jobs)}; reused {skipped}",flush=True)
        code=launch([args.blender_exe,"--background","--offline-mode","--disable-autoexec","--factory-startup","--python-exit-code","1","--python",HERE/"migrate_blender.py","--","--jobs",plan],out/"logs"/f"blender-{batch_number:04d}.log",profile_env(profile,"blender"),args.worker_timeout)
        if code==0:
            code=launch([args.blender_exe,"--background","--offline-mode","--disable-autoexec","--factory-startup","--python-exit-code","1","--python",HERE/"migrate_blender.py","--","--jobs",plan,"--phase","verify"],out/"logs"/f"blender-{batch_number:04d}-reopen.log",profile_env(profile,"blender"),args.worker_timeout)
        completed=[]
        for job in jobs:
            result_path=Path(job["result"])
            result=json.loads(result_path.read_text(encoding="utf-8")) if result_path.is_file() else dict(status="failed",reason=f"Blender worker exited {code} before completing this asset")
            if result["status"]!="failed" and not result.get("reopen_verified"):
                result=dict(status="failed",reason=f"Saved output was not verified in a fresh Blender process (exit {code})")
            result.update(fingerprint=job["fingerprint"],dependencies=job["dependencies"],route=job["route"],family=job.get("family"),warnings=job["warnings"])
            if result["status"]!="failed":
                if job["warnings"]: result["status"]="with_warnings"
                files=[Path(job["blend"]),*Path(job["exchange"]).rglob("*.json"),*Path(job["exchange"]).rglob("*.bin"),result_path]
                write_json(result_path,{k:v for k,v in result.items() if k!="outputs"})
                result["outputs"]=output_hashes(out,files)
                # Durable per-asset proof survives a coordinator interruption
                # before its next large manifest checkpoint. It does not hash
                # itself; all scene/exchange/result outputs are covered.
                write_json(Path(job["work"])/"migration-receipt.json",result)
            completed.append((job["path"],result))
        return completed
    batches=[blender_jobs[i:i+args.batch_size] for i in range(0,len(blender_jobs),args.batch_size)]
    with ThreadPoolExecutor(max_workers=args.blender_workers,thread_name_prefix="xray_blender") as pool:
        pending={pool.submit(migrate_batch,i,jobs):i for i,jobs in enumerate(batches)}
        for future in as_completed(pending):
            for path,result in future.result(): records[path]=result
            save()
    print("Reconciling legacy clips with Maya candidate poses",flush=True)
    reconcile(entries,records,paths)
    # Verify the originals again, including directory members, after all hosts exit.
    after=inventory(source)
    manifest["source_unchanged"]=after["content_sha256"]==corpus["content_sha256"]
    manifest["last_run"]=dict(selected=len(selected),reused=skipped,blender_jobs=len(blender_jobs),maya_jobs=len(extraction_jobs))
    save()
    if not manifest["source_unchanged"]: raise RuntimeError("Original corpus changed during migration")
    print(json.dumps(manifest["summary"],indent=2),flush=True)
    return 0 if not any(records[e["path"]]["status"] in {"failed","pending"} for e in selected) else 2


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root",type=Path,required=True)
    parser.add_argument("--output",type=Path,required=True)
    parser.add_argument("--blender-exe",type=Path,required=True)
    parser.add_argument("--cli-exe",type=Path,required=True)
    parser.add_argument("--playback-exe",type=Path,required=True)
    parser.add_argument("--mayapy-exe",type=Path)
    parser.add_argument("--family-map",type=Path,help="JSON mapping source clip paths to explicit catalog hierarchy IDs")
    parser.add_argument("--family-catalog",type=Path,help="Saved hierarchy evidence for cooked-only migration without Maya")
    parser.add_argument("--include",action="append",default=[],help="Case-insensitive path substring; repeatable")
    parser.add_argument("--limit",type=int)
    parser.add_argument("--batch-size",type=int,default=8)
    parser.add_argument("--host-workers",type=int,default=1,help="Independent Maya processes, each with its own profile/reference copies")
    parser.add_argument("--blender-workers",type=int,default=1,help="Independent Blender workers, each with its own extension profile")
    parser.add_argument("--worker-timeout",type=int,default=600)
    parser.add_argument("--retry-failed",action="store_true")
    parser.add_argument("--normalize-zero-root-scale",action="store_true",help="Opt-in normalization of legacy Root.scale=(0,0,0) clips whose Root is otherwise identity")
    parser.add_argument("--anim-layer-base",type=str,help="Corpus-relative base control-rig .mb/.ma scene used when baking standalone Maya AnimLayer scenes")
    parser.add_argument("--prepare-only",action="store_true",help="Cache scene extraction only; do not claim completed migration")
    args=parser.parse_args()
    for key,value in vars(args).items():
        if isinstance(value,Path): setattr(args,key,value.resolve())
    if min(args.batch_size,args.worker_timeout,args.host_workers,args.blender_workers)<1: parser.error("Batch size, worker count and timeout must be positive")
    with workspace_lock(args.source_root,args.output): result=run(args)
    sys.exit(result)


if __name__=="__main__": main()
