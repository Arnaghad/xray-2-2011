"""Read-only legacy asset inventory, with deterministic hashes and explicit links."""

from __future__ import annotations

import argparse
from collections import Counter, defaultdict
from concurrent.futures import ThreadPoolExecutor
import hashlib
import json
import os
from pathlib import Path


KINDS = {".mb": "maya_binary", ".ma": "maya_ascii", ".clip": "clip",
         ".clip_check_data": "clip_check_data", ".skeleton": "skeleton",
         ".skinned_model": "skinned_model", ".model": "model", ".physics": "physics", ".ph": "physics"}


def hash_file(path: Path) -> tuple[int, str]:
    before = path.stat()
    with path.open("rb") as stream:
        digest = hashlib.file_digest(stream, "sha256").hexdigest()
    after = path.stat()
    if (before.st_size, before.st_mtime_ns) != (after.st_size, after.st_mtime_ns):
        raise RuntimeError(f"Source changed during inventory: {path}")
    return after.st_size, digest


def inventory(root: Path, *, workers: int = 4) -> dict:
    root = root.resolve(strict=True)
    if not root.is_dir():
        raise ValueError("Corpus root must be a directory")
    assets: list[Path] = []
    members: dict[Path, list[Path]] = defaultdict(list)
    all_files: set[Path] = set()
    def fail_walk(error: OSError) -> None:
        raise error

    for directory, dirs, files in os.walk(root, followlinks=False, onerror=fail_walk):
        current = Path(directory)
        # Do not silently omit linked assets or read outside the source root.
        for name in dirs + files:
            child = current / name
            if child.is_symlink() or getattr(child, "is_junction", lambda: False)():
                raise ValueError(f"Linked source requires an explicit separate inventory: {child}")
        if current.suffix.lower() in {".model", ".skinned_model"}:
            assets.append(current)
        for name in files:
            path = current / name
            containers = [p for p in (current, *current.parents) if p != root and
                          p.is_relative_to(root) and p.suffix.lower() in {".model", ".skinned_model"}]
            if containers:
                for container in containers:
                    members[container].append(path)
                all_files.add(path)
            elif path.suffix.lower() in KINDS:
                assets.append(path)
                all_files.add(path)
    ordered_files = sorted(all_files, key=lambda p: p.relative_to(root).as_posix())
    with ThreadPoolExecutor(max_workers=workers) as pool:
        hashes = dict(zip(ordered_files, pool.map(hash_file, ordered_files)))
    entries = []
    for path in sorted(assets, key=lambda p: p.relative_to(root).as_posix()):
        rel = path.relative_to(root).as_posix()
        entry = {"path": rel, "kind": KINDS[path.suffix.lower()],
                 "temporary": any(p.lower() in {"temp", "tmp"} for p in path.relative_to(root).parts),
                 "classification": "animation_scene" if path.suffix.lower() in {".mb", ".ma"} and
                 any(p.lower() in {"animation", "animations"} for p in path.parts) else KINDS[path.suffix.lower()]}
        if path.is_dir():
            file_entries = [{"path": p.relative_to(path).as_posix(), "size_bytes": hashes[p][0],
                             "sha256": hashes[p][1]} for p in sorted(members[path], key=lambda p: p.relative_to(path).as_posix())]
            canonical = json.dumps(file_entries, sort_keys=True, separators=(",", ":"), ensure_ascii=False).encode("utf-8")
            entry.update(storage="directory", size_bytes=sum(f["size_bytes"] for f in file_entries),
                         sha256=hashlib.sha256(canonical).hexdigest(), members=file_entries)
        else:
            size, digest = hashes[path]
            entry.update(storage="file", size_bytes=size, sha256=digest)
        entries.append(entry)

    by_path = {e["path"]: e for e in entries}
    scenes: dict[str, list[str]] = defaultdict(list)
    for e in entries:
        if e["kind"] in {"maya_binary", "maya_ascii"}:
            scenes[Path(e["path"]).stem.casefold()].append(e["path"])
    for e in entries:
        if e["kind"] == "clip":
            check = str(Path(e["path"]).with_suffix(".clip_check_data")).replace("\\", "/")
            e["check_data"] = check if check in by_path else None
            candidates = scenes.get(Path(e["path"]).stem.casefold(), [])
            status = "ambiguous" if len(candidates) > 1 else "unverified" if candidates else "unmatched"
            e["scene_link"] = {"method": "casefolded_stem_candidates", "status": status,
                               "candidates": candidates}
    scene_bytes = sum(e["size_bytes"] for e in entries if e["kind"] in {"maya_binary", "maya_ascii"})
    counts = dict(sorted(Counter(e["kind"] for e in entries).items()))
    content_hash = hashlib.sha256(json.dumps(entries, sort_keys=True, separators=(",", ":"),
                                             ensure_ascii=False).encode("utf-8")).hexdigest()
    return {"schema": "xray-legacy-corpus/v1", "source_root": root.as_posix(), "source_access": "read_only",
            "content_sha256": content_hash,
            "directory_hash": "SHA-256 of UTF-8 JSON member list, sorted by path; sorted keys, compact separators",
            "summary": {"assets": len(entries), "counts": counts,
                        "size_bytes": sum(e["size_bytes"] for e in entries),
                        "clips_with_check_data": sum(e["kind"] == "clip" and e["check_data"] is not None for e in entries),
                        "temporary_scenes": sum(e["temporary"] and e["kind"] in {"maya_binary", "maya_ascii"} for e in entries),
                        "blend_size_estimate": {"status": "provisional_unmeasured", "source_scene_bytes": scene_bytes,
                                                "low_bytes": scene_bytes, "high_bytes": scene_bytes * 3,
                                                "method": "Planning range of 1-3x source scene bytes; excludes packed textures. Recalibrate after B4/B5 sample migration.",
                                                "distribution_decision": "pending_measured_blend_samples"}},
            "assets": entries}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--workers", type=int, default=4)
    args = parser.parse_args()
    root, output = args.root.resolve(), args.output.resolve()
    if output.is_relative_to(root):
        parser.error("Output must be outside the read-only original corpus")
    result = inventory(root, workers=args.workers)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(json.dumps(result["summary"], indent=2))
    print(f"Corpus SHA-256: {result['content_sha256']}")


if __name__ == "__main__":
    main()
