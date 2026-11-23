"""Explain known zero-root source failures without changing source data or status."""

import argparse
import json
from pathlib import Path

from migrate_legacy import workspace_lock, write_json
from migration_state import SCHEMA, digest, safe_path
from dcc_common.legacy_codec import indexed, load_config, load_check_data


def annotate(output):
    output=Path(output).resolve()
    path=output/"migration-manifest.json"
    initial=json.loads(path.read_text(encoding="utf-8"))
    source=Path(initial["source_root"])
    with workspace_lock(source,output):
        manifest=json.loads(path.read_text(encoding="utf-8"))
        if manifest["schema"]!=SCHEMA or manifest["source_root"]!=str(source.as_posix()):
            raise ValueError("Migration manifest identity changed")
        inventory=json.loads((output/"legacy-corpus.json").read_text(encoding="utf-8"))
        entries={e["path"]:e for e in inventory["assets"]}
        changed=[]
        for relative,record in manifest["assets"].items():
            if record["status"]!="failed" or entries[relative]["kind"]!="clip": continue
            if "root-joint track must be identity" not in record.get("reason","") and record.get("source_diagnostics",{}).get("code")!="legacy_zero_root_scale": continue
            original=safe_path(source,relative)
            if digest(original)!=entries[relative]["sha256"]: raise ValueError("Source changed: "+relative)
            splines=load_config(original)["splines"]
            names=indexed(splines["bone_names"])
            if "Root" not in names: continue
            index=names.index("Root")
            channels=indexed(indexed(splines["data"])[index])
            if not all(float(pair[1])==0 for channel in channels[6:] for pair in channel[:-1]): continue
            companion=entries[relative].get("check_data")
            if companion:
                checked=safe_path(source,companion)
                if digest(checked)!=entries[companion]["sha256"]: raise ValueError("Check-data changed: "+companion)
                if not all(v==0 for channel in load_check_data(checked)["samples"][index][6:] for v in channel): continue
            record["reason"]="Legacy Root.scale is (0,0,0) at every frame, confirmed in original spline coefficients"+(
                " and check-data" if companion else "; no check-data companion exists")+(
                ". The importer requires an identity root; substituting unit scale exceeds source precision. Retained as failed; no source repair applied.")
            record["source_diagnostics"]=dict(code="legacy_zero_root_scale",bone="Root",scale=[0,0,0],
                source_sha256=entries[relative]["sha256"],check_data=companion,
                policy="retain_failed",repair_performed=False)
            changed.append(relative)
        write_json(path,manifest)
    return changed


if __name__=="__main__":
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output",type=Path,required=True)
    args=parser.parse_args()
    print(json.dumps(dict(annotated=len(annotate(args.output)),status="failed",source_repair=False)))
