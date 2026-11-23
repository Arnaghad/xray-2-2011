"""Build a reproducible, self-contained Blender extension ZIP."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import tomllib
import zipfile

HERE = Path(__file__).resolve().parent
PACKAGE = HERE / "xray_blender"
SHARED = HERE.parent / "dcc_common"
SHARED_MODULES = ("__init__.py", "constants.py", "cook_bridge.py", "exchange.py", "logical_names.py",
                  "legacy_codec.py", "ma_reader.py", "dcc_math.py")


def build(output: Path | None = None) -> Path:
    manifest = tomllib.loads((PACKAGE / "blender_manifest.toml").read_text(encoding="utf-8"))
    if output is None:
        output = HERE.parents[2] / "build/blender/extension" / f"{manifest['id']}-{manifest['version']}.zip"
    output = output.resolve()
    if output.is_relative_to(PACKAGE):
        raise ValueError("Build output must be outside the extension sources")
    files = {p.name: p for p in PACKAGE.iterdir()
             if p.is_file() and (p.suffix == ".py" or p.name in {"blender_manifest.toml", "LICENSE", "README.md"})}
    for name in SHARED_MODULES:
        files[f"_dcc_common/{name}"] = SHARED / name
    output.parent.mkdir(parents=True, exist_ok=True)
    temporary = output.with_name(output.name + ".tmp")
    hashes = {}
    with zipfile.ZipFile(temporary, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for name, path in sorted(files.items()):
            if path.is_symlink():
                raise ValueError(f"Extension sources must be regular files: {path}")
            content = path.read_bytes()
            hashes[name] = hashlib.sha256(content).hexdigest()
            info = zipfile.ZipInfo(name, date_time=(1980, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o100644 << 16
            archive.writestr(info, content, compresslevel=9)
    temporary.replace(output)
    digest = hashlib.sha256(output.read_bytes()).hexdigest()
    output.with_name(output.name + ".sha256").write_text(f"{digest}  {output.name}\n", encoding="utf-8")
    provenance = {"id": manifest["id"], "version": manifest["version"], "archive_sha256": digest,
                  "shared_source": "sources/tools/dcc_common", "members_sha256": hashes}
    output.with_name(output.name + ".json").write_text(json.dumps(provenance, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    return output


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    print(build(args.output))
