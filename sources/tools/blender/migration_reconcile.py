"""Independent legacy/Maya pose reconciliation; filenames only propose candidates."""

import math
import json
from pathlib import Path
import sys

sys.path.insert(0,str(Path(__file__).resolve().parent.parent))
from dcc_common.exchange import validate_bundle, unpack_span
from dcc_common.dcc_math import trs, mul, inverse, decompose
from migration_state import parent_map

CHANNELS=("tx","ty","tz","rx","ry","rz","sx","sy","sz")


def read_animation(path):
    path=Path(path)
    manifest=validate_bundle(path)
    payload=(path/"payload.bin").read_bytes()
    tracks={t["bone_name"]:[unpack_span(t["channels"][ch],payload,None,None,ch) for ch in CHANNELS] for t in manifest["tracks"]}
    return manifest,tracks


def local_frames(manifest,tracks,names):
    unit=manifest["coord_space"]["unit_scale_to_meters"]
    engine=manifest["coord_space"]["system"]=="xray_left_handed_y_up"
    if manifest["coord_space"]["system"]=="blender_right_handed_z_up":
        raise ValueError("Reconciliation requires an archived Y-up source")
    result={}
    for name in names:
        rows=[]
        channels=tracks[name]
        for frame in range(manifest["frame_count"]):
            t=[channels[c][frame]*unit for c in range(3)]
            r=[channels[c][frame] for c in range(3,6)]
            if engine: t[2]=-t[2]; r[2]=-r[2]
            rows.append(trs(t,r,[channels[c][frame] for c in range(6,9)]))
        if name=="object_movement" and not manifest.get("root_already_relative",engine):
            initial=inverse(rows[0]); rows=[mul(initial,m) for m in rows]
        result[name]=rows
    return result


def compare_sources(legacy_path,maya_path,*,headers=None):
    # A range/hierarchy mismatch needs only metadata. Do not repeatedly validate
    # and unpack large sampled payloads which cannot form a matching candidate.
    a,b=headers or tuple(json.loads((Path(p)/"manifest.json").read_text(encoding="utf-8")) for p in (legacy_path,maya_path))
    if a["frame_count"]!=b["frame_count"] or abs(a["fps"]-b["fps"])>1e-5:
        return dict(status="range_mismatch",legacy_frames=a["frame_count"],maya_frames=b["frame_count"],legacy_fps=a["fps"],maya_fps=b["fps"])
    parents=parent_map(a["root"]); other=parent_map(b["root"])
    if not {t["bone_name"] for t in a["tracks"]}.issubset(t["bone_name"] for t in b["tracks"]) or any(other.get(n)!=p for n,p in parents.items()):
        return dict(status="family_mismatch")
    # A positive pose claim still requires complete bundle validation.
    a,at=read_animation(legacy_path); b,bt=read_animation(maya_path)
    aa,bb=local_frames(a,at,at),local_frames(b,bt,at)
    maxima=[0.0]*3; worst=None
    for frame in range(a["frame_count"]):
        ag,bg={},{}
        for name,parent in parents.items():
            ag[name]=mul(ag[parent],aa[name][frame]) if parent else aa[name][frame]
            bg[name]=mul(bg[parent],bb[name][frame]) if parent else bb[name][frame]
            t,r,s=decompose(ag[name]); tt,rr,ss=decompose(bg[name])
            rotation=mul(inverse(trs(r=r)),trs(r=rr))
            angle=math.acos(max(-1,min(1,(sum(rotation[i][i] for i in range(3))-1)/2)))
            errors=[max(abs(x-y) for x,y in zip(t,tt)),angle,max(abs(x-y) for x,y in zip(s,ss))]
            if any(v>old for v,old in zip(errors,maxima)): worst=dict(bone=name,frame=frame)
            maxima=[max(x,y) for x,y in zip(maxima,errors)]
    tolerances=[a.get("precision_"+k,0.005)+2e-5 for k in "trs"]
    return dict(status="matched" if all(e<=p for e,p in zip(maxima,tolerances)) else "pose_mismatch",
                max_translation=maxima[0],max_rotation=maxima[1],max_scale=maxima[2],worst=worst,
                alignment="clip_local_frame",legacy_frame_start=a.get("frame_start",0),maya_frame_start=b.get("frame_start",0))


def reconcile(entries,records,paths):
    for e in entries.values():
        if e["kind"]!="clip" or e["path"] not in records: continue
        source=Path(paths(e)["exchange"])
        if not (source/"manifest.json").is_file(): continue
        source_header=json.loads((source/"manifest.json").read_text(encoding="utf-8"))
        candidates=[]
        for scene in e.get("scene_link",{}).get("candidates",[]):
            scene_entry=entries.get(scene)
            if not scene_entry: continue
            archive=Path(paths(scene_entry)["exchange"])
            for manifest in sorted(archive.rglob("manifest.json")):
                try:
                    header=json.loads(manifest.read_text(encoding="utf-8"))
                    if header.get("asset_kind")!="animation": continue
                    result=compare_sources(source,manifest.parent,headers=(source_header,header))
                    candidates.append(dict(scene=scene,asset=manifest.parent.relative_to(archive).as_posix(),**result))
                except Exception as error:
                    candidates.append(dict(scene=scene,status="unavailable",reason=str(error)))
        matches=[c for c in candidates if c["status"]=="matched"]
        status="matched" if matches else "mismatch" if candidates else "unavailable" if e.get("scene_link",{}).get("candidates") else "no_candidates"
        records[e["path"]]["reconciliation"]=dict(status=status,candidates=candidates,
            policy="Filename matches are candidates only; a match requires every sampled global pose within source precision")
