"""Collect reproducible build, toolchain, PE import, warning, and CTest baseline metrics."""

from __future__ import annotations

import argparse
from collections import Counter
import json
from pathlib import Path
import re
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET

REPO_ROOT = Path(__file__).resolve().parents[3]
MANIFEST_PATH = Path(__file__).resolve().with_name("toolchain-manifest.json")
WARNING_FLAG_RE = re.compile(r"warning:.*?\[(-W[^\]]+)\]")


def run_capture(cmd: list[str], cwd: Path | None = None) -> tuple[int, str]:
    try:
        proc = subprocess.run(
            cmd,
            cwd=str(cwd) if cwd else None,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
        )
        out = (proc.stdout or "").strip()
        err = (proc.stderr or "").strip()
        return proc.returncode, out or err
    except OSError as exc:
        return 1, str(exc)


def collect_tool_versions(manifest: dict) -> dict:
    versions: dict[str, str | None] = {}
    for key, cmd in (
        ("gcc", ["g++", "--version"]),
        ("cmake", ["cmake", "--version"]),
        ("ninja", ["ninja", "--version"]),
        ("dotnet", ["dotnet", "--version"]),
    ):
        rc, out = run_capture(cmd)
        versions[key] = out.splitlines()[0].strip() if rc == 0 and out else None

    versions["python"] = sys.version.split()[0]

    dxc_exe = REPO_ROOT / manifest["dxc"]["defaultDir"] / "dxc.exe"
    if dxc_exe.is_file():
        rc, out = run_capture([str(dxc_exe), "--version"])
        versions["dxc"] = out.splitlines()[0].strip() if rc == 0 and out else None
    else:
        versions["dxc"] = None

    pacman_exe = Path("C:/msys64/usr/bin/pacman.exe")
    msys2_pkgs: dict[str, str | None] = {}
    if pacman_exe.is_file():
        pkg_names = list(manifest.get("msys2Mingw64Packages", {}).keys())
        rc, out = run_capture([str(pacman_exe), "-Q", *pkg_names])
        installed: dict[str, str] = {}
        if rc == 0:
            for line in out.splitlines():
                parts = line.split()
                if len(parts) >= 2:
                    installed[parts[0]] = parts[1]
        for pkg in pkg_names:
            msys2_pkgs[pkg] = installed.get(pkg)
    versions["msys2Mingw64Packages"] = msys2_pkgs
    return versions


def collect_pe_imports(bin_dir: Path) -> dict[str, list[str]]:
    objdump = shutil.which("objdump") or "C:/msys64/mingw64/bin/objdump.exe"
    if not bin_dir.is_dir() or not Path(objdump).is_file():
        return {}
    imports_by_bin: dict[str, list[str]] = {}
    binaries = sorted(
        p for p in bin_dir.iterdir() if p.is_file() and p.suffix.lower() in {".exe", ".dll"}
    )
    for binary in binaries:
        rc, out = run_capture([objdump, "-p", str(binary)])
        if rc == 0:
            dlls = sorted(set(re.findall(r"DLL Name:\s*([^\r\n]+)", out)))
            imports_by_bin[binary.name] = dlls
    return imports_by_bin


def read_text_auto(path: Path) -> str:
    raw = path.read_bytes()
    if raw.startswith((b"\xff\xfe", b"\xfe\xff")):
        return raw.decode("utf-16", errors="replace")
    if raw.startswith(b"\xef\xbb\xbf"):
        return raw.decode("utf-8-sig", errors="replace")
    return raw.decode("utf-8", errors="replace")


def collect_warnings(build_log: Path | None) -> dict:
    if not build_log or not build_log.is_file():
        return {"present": False, "totalWarnings": 0, "byFlag": {}}
    counts: Counter[str] = Counter()
    total = 0
    for line in read_text_auto(build_log).splitlines():
        if ": warning:" in line:
            total += 1
            m = WARNING_FLAG_RE.search(line)
            counts[m.group(1) if m else "unclassified"] += 1
    return {
        "present": True,
        "totalWarnings": total,
        "byFlag": dict(sorted(counts.items(), key=lambda kv: (-kv[1], kv[0]))),
    }


def collect_junit(junit_path: Path | None) -> dict:
    if not junit_path or not junit_path.is_file():
        return {"present": False}
    tree = ET.parse(junit_path)
    root = tree.getroot()
    cases = []
    for tc in root.iter("testcase"):
        status = "passed"
        if tc.find("failure") is not None:
            status = "failed"
        elif tc.find("error") is not None:
            status = "error"
        elif tc.find("skipped") is not None:
            status = "skipped"
        cases.append({
            "name": tc.attrib.get("name", ""),
            "timeSeconds": float(tc.attrib.get("time", "0") or 0.0),
            "status": status,
        })
    return {
        "present": True,
        "tests": int(root.attrib.get("tests", len(cases))),
        "failures": int(root.attrib.get("failures", 0)),
        "errors": int(root.attrib.get("errors", 0)),
        "skipped": int(root.attrib.get("skipped", 0)),
        "timeSeconds": float(root.attrib.get("time", "0") or 0.0),
        "cases": cases,
    }


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--preset", required=True)
    parser.add_argument("--build-dir", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--build-log", default="")
    parser.add_argument("--ctest-junit", default="")
    args = parser.parse_args()

    manifest = json.loads(MANIFEST_PATH.read_text(encoding="utf-8"))
    build_dir = Path(args.build_dir).resolve()
    output_path = Path(args.output).resolve()
    output_path.parent.mkdir(parents=True, exist_ok=True)

    _, git_head = run_capture(["git", "rev-parse", "HEAD"], cwd=REPO_ROOT)
    _, git_status = run_capture(["git", "status", "--short"], cwd=REPO_ROOT)

    report = {
        "schemaVersion": 1,
        "preset": args.preset,
        "git": {
            "head": git_head,
            "dirtyFiles": [line for line in git_status.splitlines() if line.strip()],
        },
        "toolchain": collect_tool_versions(manifest),
        "peImports": collect_pe_imports(build_dir / "binaries"),
        "warnings": collect_warnings(Path(args.build_log) if args.build_log else None),
        "ctest": collect_junit(Path(args.ctest_junit) if args.ctest_junit else None),
    }
    output_path.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"Wrote build baseline report: {output_path}")


if __name__ == "__main__":
    main()
