"""Build an evidence-based map of active vs. archival source paths in the repository."""

from __future__ import annotations

import argparse
from collections import defaultdict
import json
from pathlib import Path
import re
import subprocess
import sys

REPO_ROOT = Path(__file__).resolve().parents[3]
SOURCES_ROOT = REPO_ROOT / "sources"

COMPATIBILITY_PATHS = {
    "sources/ode",
    "sources/opcode",
    "sources/xray/vfs",
    "sources/xray/fs",
    "sources/tools/dcc_common",
}

TOOL_PATHS = {
    "sources/tools",
    "sources/tests",
    "sources/xray/maya_cook",
    "sources/xray/tools",
    "sources/xray/editor",
    "sources/global.json",
    "sources/Directory.Build.props",
    "sources/NuGet.Config",
    "sources/xray/render/vulkan/assets",
}

ARCHIVAL_EXPLICIT = {
    "sources/asio.zip",
    "sources/BugTrap",
    "sources/build_log.txt",
    "sources/build_log_final.txt",
    "sources/UpgradeLog.htm",
    "sources/vcpkg.json",
    "sources/xray/render/dx9",
    "sources/xray/render/common",
    "sources/xray/maya",
    "sources/xray/maya_animation",
    "sources/xray/graphics_benchmark",
    "sources/xray/rms_generator",
    "sources/xray/rtp_learn",
    "sources/xray/shader_generator",
    "sources/xray/sound_system_usage_example",
    "sources/xray/vfs_pack",
    "sources/xray/finger_printer",
}


def normalize_rel(raw_path: str | Path, base: Path = REPO_ROOT) -> str | None:
    p = Path(raw_path)
    if not p.is_absolute():
        p = (base / p).resolve()
    else:
        p = p.resolve()
    try:
        return p.relative_to(REPO_ROOT).as_posix()
    except ValueError:
        return None


def parse_compile_commands(cc_path: Path) -> tuple[dict[str, set[str]], dict[str, set[str]]]:
    target_units: dict[str, set[str]] = defaultdict(set)
    unit_targets: dict[str, set[str]] = defaultdict(set)
    if not cc_path.is_file():
        return target_units, unit_targets
    data = json.loads(cc_path.read_text(encoding="utf-8"))
    for entry in data:
        rel_file = normalize_rel(entry["file"])
        out = entry.get("output", "")
        tgt = "unknown"
        if "/CMakeFiles/" in out:
            tgt = out.split("/CMakeFiles/", 1)[1].split(".dir/", 1)[0]
        if rel_file:
            target_units[tgt].add(rel_file)
            unit_targets[rel_file].add(tgt)
    return target_units, unit_targets


def parse_ninja_deps(build_dir: Path) -> set[str]:
    if not (build_dir / ".ninja_deps").is_file():
        return set()
    proc = subprocess.run(
        ["ninja", "-C", str(build_dir), "-t", "deps"],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
    )
    deps: set[str] = set()
    for line in proc.stdout.splitlines():
        if not line.startswith("    "):
            continue
        raw = line.strip()
        rel = normalize_rel(raw, build_dir)
        if rel and rel.startswith("sources/"):
            deps.add(rel)
    return deps


def parse_packager_manifest(packager_path: Path) -> dict[str, list[str]]:
    if not packager_path.is_file():
        return {}
    text = packager_path.read_text(encoding="utf-8")
    sections: dict[str, list[str]] = {}
    for name in (
        "NATIVE_BIN_FILES",
        "MANAGED_BIN_FILES",
        "MINGW_RUNTIME_DLLS",
        "DXC_RUNTIME_DLLS",
        "VALIDATION_FILES",
    ):
        match = re.search(rf"{name}\s*=\s*\((.*?)\)", text, re.S)
        if match:
            sections[name] = re.findall(r"'([^']+)'", match.group(1))
    return sections


def parse_python_imports(sources_dir: Path) -> dict[str, list[str]]:
    imports: dict[str, set[str]] = defaultdict(set)
    for py_file in sorted(sources_dir.glob("tools/**/*.py")):
        rel = py_file.relative_to(REPO_ROOT).as_posix()
        text = py_file.read_text(encoding="utf-8", errors="replace")
        for line in text.splitlines():
            m = re.match(r"^\s*(?:from\s+([\w.]+)\s+import|import\s+([\w., ]+))", line)
            if m:
                mod = (m.group(1) or m.group(2)).strip()
                imports[rel].add(mod)
    return {k: sorted(v) for k, v in imports.items()}


def candidate_roots() -> list[str]:
    paths: list[str] = []
    if not SOURCES_ROOT.is_dir():
        return paths
    for child in sorted(SOURCES_ROOT.iterdir()):
        rel = child.relative_to(REPO_ROOT).as_posix()
        if child.name == "xray" and child.is_dir():
            for sub in sorted(child.iterdir()):
                sub_rel = sub.relative_to(REPO_ROOT).as_posix()
                if sub.name == "render" and sub.is_dir():
                    for rsub in sorted(sub.iterdir()):
                        paths.append(rsub.relative_to(REPO_ROOT).as_posix())
                elif sub.name == "stalker2" and sub.is_dir():
                    paths.append(sub_rel)
                else:
                    paths.append(sub_rel)
        else:
            paths.append(rel)
    return paths


def classify_path(rel_path: str, compile_units: set[str], ninja_deps: set[str]) -> tuple[str, int, int]:
    prefix = rel_path + "/"
    cc_count = sum(1 for u in compile_units if u == rel_path or u.startswith(prefix))
    dep_count = sum(1 for d in ninja_deps if d == rel_path or d.startswith(prefix))

    if rel_path in ARCHIVAL_EXPLICIT or (
        cc_count == 0
        and dep_count == 0
        and rel_path.startswith("sources/xray/")
        and not any(
            rel_path == p or rel_path.startswith(p + "/")
            for p in TOOL_PATHS | COMPATIBILITY_PATHS | {"sources/xray/stalker2", "sources/xray/render/base", "sources/xray/render/core", "sources/xray/render/engine", "sources/xray/render/facade", "sources/xray/render/vulkan"}
        )
        and (REPO_ROOT / rel_path).is_dir()
    ):
        # Double check header-only active paths
        if cc_count > 0 or dep_count > 0:
            pass
        elif rel_path in {"sources/xray/sdk"}:
            return "tool", cc_count, dep_count
        elif rel_path in ARCHIVAL_EXPLICIT:
            return "archival", cc_count, dep_count

    if rel_path in {"sources/lua"}:
        return "runtime", cc_count, dep_count
    if rel_path in {"sources/.idea", "sources/.vs"}:
        return "tool", cc_count, dep_count
    if any(rel_path == p or rel_path.startswith(p + "/") for p in ARCHIVAL_EXPLICIT):
        return "archival", cc_count, dep_count
    if any(rel_path == p or rel_path.startswith(p + "/") for p in TOOL_PATHS):
        return "tool", cc_count, dep_count
    if any(rel_path == p or rel_path.startswith(p + "/") for p in COMPATIBILITY_PATHS):
        return "compatibility_format", cc_count, dep_count
    if cc_count > 0 or dep_count > 0:
        return "runtime", cc_count, dep_count

    # Remaining non-build root files/dirs (configs, third_party, sdk headers, docs)
    if rel_path.startswith("sources/third_party"):
        return "runtime", cc_count, dep_count
    if rel_path in {"sources/CMakeLists.txt", "sources/CMakePresets.json", "sources/versions.txt", "sources/THIRD_PARTY_DEPENDENCIES.md"}:
        return "tool", cc_count, dep_count
    if rel_path in {"sources/boost", "sources/fastdelegate", "sources/luabind"}:
        return "runtime" if dep_count > 0 else "archival", cc_count, dep_count
    if (REPO_ROOT / rel_path).is_file():
        return "tool", cc_count, dep_count
    return "archival", cc_count, dep_count


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--preset", default="windows-x64-release")
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()

    build_dir = REPO_ROOT / "build" / "presets" / args.preset
    target_units, unit_targets = parse_compile_commands(build_dir / "compile_commands.json")
    all_units = set(unit_targets.keys())
    ninja_deps = parse_ninja_deps(build_dir)

    baseline_path = REPO_ROOT / "build" / "modernization-baseline" / args.preset / "baseline.json"
    pe_imports: dict[str, list[str]] = {}
    if baseline_path.is_file():
        baseline = json.loads(baseline_path.read_text(encoding="utf-8"))
        pe_imports = baseline.get("peImports", {})

    packager_manifest = parse_packager_manifest(SOURCES_ROOT / "tools" / "sdk" / "package-sdk-runtime.py")
    py_imports = parse_python_imports(SOURCES_ROOT)

    entries = []
    category_counts: dict[str, int] = defaultdict(int)
    for rel_path in candidate_roots():
        cat, cc_count, dep_count = classify_path(rel_path, all_units, ninja_deps)
        category_counts[cat] += 1
        entries.append(
            {
                "path": rel_path,
                "category": cat,
                "compileUnits": cc_count,
                "ninjaDeps": dep_count,
                "exists": (REPO_ROOT / rel_path).exists(),
            }
        )

    payload = {
        "schemaVersion": 1,
        "preset": args.preset,
        "summary": {
            "totalClassifiedPaths": len(entries),
            "categoryCounts": dict(sorted(category_counts.items())),
            "totalCompileUnits": len(all_units),
            "totalNinjaDepsInSources": len(ninja_deps),
            "totalTargets": len(target_units),
            "peBinaryCount": len(pe_imports),
            "pythonModulesScanned": len(py_imports),
        },
        "entries": entries,
        "targetCompileUnitCounts": {k: len(v) for k, v in sorted(target_units.items())},
        "packagerManifest": packager_manifest,
        "peImports": pe_imports,
        "pythonImports": py_imports,
    }

    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    print(f"Wrote {args.output} ({len(entries)} paths, {dict(sorted(category_counts.items()))})")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
