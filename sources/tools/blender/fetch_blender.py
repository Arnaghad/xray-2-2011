"""Install the hash-pinned portable runtime into build/blender; no system install."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import shutil
import subprocess
import tarfile
import urllib.request
import zipfile


LOCK_PATH = Path(__file__).with_name("runtime-lock.json")
REPO_ROOT = Path(__file__).resolve().parents[3]


def sha256_file(path: Path) -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def install(destination: Path, archive: Path | None = None) -> Path:
    lock = json.loads(LOCK_PATH.read_text(encoding="utf-8"))
    if platform.system() not in {"Windows", "Linux"} or platform.machine().lower() not in {"amd64", "x86_64"}:
        raise RuntimeError("The pinned runtime supports Windows/Linux x64")
    platform_key = platform.system().lower() + "-x64"
    entry = lock["platforms"][platform_key]
    destination = destination.resolve()
    destination.mkdir(parents=True, exist_ok=True)
    archive_path = archive or destination / Path(entry["url"]).name
    if not archive_path.is_file():
        if archive is not None:
            raise FileNotFoundError(archive)
        partial = archive_path.with_name(archive_path.name + ".part")
        print(f"Downloading {entry['url']}", flush=True)
        with urllib.request.urlopen(entry["url"], timeout=60) as response, partial.open("wb") as out:
            shutil.copyfileobj(response, out, length=1024 * 1024)
        if sha256_file(partial) != entry["sha256"]:
            raise RuntimeError(f"SHA-256 mismatch: {partial}; archive was not extracted")
        partial.replace(archive_path)
    if sha256_file(archive_path) != entry["sha256"]:
        raise RuntimeError(f"SHA-256 mismatch: {archive_path}; archive was not extracted")

    runtime_dir = destination / lock["version"]
    executable = runtime_dir / entry["directory"] / entry["executable"]
    # Re-extract from the verified archive even on repeat runs, repairing altered files.
    runtime_dir.mkdir(exist_ok=True)
    if platform_key == "windows-x64":
        with zipfile.ZipFile(archive_path) as zipped:
            for member in zipped.infolist():
                target = (runtime_dir / member.filename).resolve()
                if not target.is_relative_to(runtime_dir) or not member.filename.startswith(entry["directory"] + "/"):
                    raise RuntimeError(f"Unsafe archive member: {member.filename}")
            zipped.extractall(runtime_dir)
    else:
        with tarfile.open(archive_path, "r:xz") as tar:
            for member in tar.getmembers():
                target = (runtime_dir / member.name).resolve()
                if not target.is_relative_to(runtime_dir) or member.name.split("/")[0] != entry["directory"]:
                    raise RuntimeError(f"Unsafe archive member: {member.name}")
            tar.extractall(runtime_dir, filter="data")
    # Blender recognizes a local portable directory and keeps user settings here.
    (executable.parent / "portable").mkdir(exist_ok=True)
    env = dict(os.environ, PYTHONNOUSERSITE="1")
    result = subprocess.run([str(executable), "--background", "--factory-startup", "--version"],
                            check=True, capture_output=True, text=True, env=env, timeout=60)
    if not re.match(rf"Blender {re.escape(lock['version'])}(?:\s|$)", result.stdout):
        raise RuntimeError(f"Unexpected Blender version: {result.stdout}")
    provenance = {
        "schema": lock["schema"], "version": lock["version"], "platform": platform_key,
        "url": entry["url"], "checksum_url": lock["checksum_url"],
        "archive": archive_path.resolve().as_posix(), "archive_sha256": entry["sha256"],
        "executable": executable.as_posix(), "executable_sha256": sha256_file(executable),
        "version_output": result.stdout.strip(),
    }
    (runtime_dir / "provenance.json").write_text(json.dumps(provenance, indent=2) + "\n", encoding="utf-8")
    print(executable, flush=True)
    return executable


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--destination", type=Path, default=REPO_ROOT / "build/blender")
    parser.add_argument("--archive", type=Path, help="Use an already downloaded official archive (offline)")
    args = parser.parse_args()
    install(args.destination, args.archive)


if __name__ == "__main__":
    main()
