"""Content-addressed migration state and evidence-based skeleton matching."""

import hashlib
import json
import os
from pathlib import Path

SCHEMA = "xray-legacy-migration/v1"


def digest(path):
    with Path(path).open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def fingerprint(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False).encode()).hexdigest()


def write_json(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name+".tmp")
    temporary.write_text(json.dumps(value, indent=2, ensure_ascii=False)+"\n", encoding="utf-8")
    os.replace(temporary, path)


def safe_path(root, relative):
    relative = Path(relative)
    if relative.is_absolute() or ".." in relative.parts or not relative.parts:
        raise ValueError(f"Unsafe migration path: {relative}")
    result = (Path(root)/relative).resolve()
    if not result.is_relative_to(Path(root).resolve()):
        raise ValueError(f"Migration path escapes its root: {relative}")
    return result


def parent_map(root):
    result = {}
    def visit(node, parent):
        name = node["name"]
        if name in result:
            raise ValueError(f"Duplicate skeleton bone: {name}")
        result[name] = parent
        for child in node.get("children", []):
            visit(child, name)
    visit(root, None)
    return result


def family_record(root, evidence, *, bind_pose=None, coord_space=None):
    parents = parent_map(root)
    if "object_movement" not in parents:
        root = {"name": "object_movement", "children": [root]}
        parents = parent_map(root)
    return {"id": fingerprint(parents), "root": root, "parents": parents,
            "evidence": evidence, "bind_pose": bind_pose or [], "coord_space": coord_space}


def match_family(names, families, *, preferred=None):
    """Never invent parent links or silently prune animated interior bones."""
    names = set(names)
    candidates = [f for f in families if set(f["parents"]) == names]
    if preferred:
        candidates = [f for f in candidates if f["id"] == preferred]
    variants = {f["id"] for f in candidates}
    if len(variants) != 1:
        raise ValueError(f"Skeleton family {'ambiguous' if variants else 'missing'} for {len(names)} tracks; "
                         f"matching hierarchy variants: {sorted(variants)}")
    # Same hierarchy can have different bind poses. Clips without a mesh do not
    # declare a bind pose; retain only proven parent links, not an arbitrary skin.
    chosen = sorted(candidates, key=lambda f: f["evidence"])[0]
    return {**chosen, "bind_pose": [], "evidence": sorted({f["evidence"] for f in candidates})}


def output_hashes(root, paths):
    return [{"path": Path(p).resolve().relative_to(Path(root).resolve()).as_posix(),
             "size_bytes": Path(p).stat().st_size, "sha256": digest(p)} for p in sorted(paths)]


def reusable(record, key, root):
    if record.get("fingerprint") != key or record.get("status") not in {"migrated", "with_warnings"}:
        return False
    outputs = record.get("outputs", [])
    return bool(outputs) and all((p := safe_path(root, o["path"])).is_file() and
        p.stat().st_size == o["size_bytes"] and digest(p) == o["sha256"] for o in outputs)
