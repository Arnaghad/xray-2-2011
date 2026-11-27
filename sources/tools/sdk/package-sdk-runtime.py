"""Assemble a clean, self-contained Windows x64 or Linux x64 Debug/Release SDK runtime deployment."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess

REPO = Path(__file__).resolve().parents[3]
DEFAULT_MINGW_BIN = Path('C:/msys64/mingw64/bin')
DEFAULT_DXC_DIR_WIN = REPO / 'build/dxc/bin/x64'
DEFAULT_DXC_DIR_LINUX = REPO / 'build/dxc/linux-x64'
DEFAULT_VALIDATION_DIR_WIN = REPO / 'build/vulkan-validation/mingw64/bin'
DEFAULT_ASSETS_ROOT = REPO / 'build/sdk-assets'
DEFAULT_SHADERS_ROOT = REPO / 'sources/xray/render/vulkan/assets'
SDK11_FIXTURES_ROOT = REPO / 'sources/xray/editor/sdk_ui/sdk11/fixtures'

NATIVE_BIN_FILES_WIN = (
    'stalker2.exe',
    'xray_editor_world.dll',
    'libxray_core.dll',
    'libxray_engine.dll',
    'libxray_render_vulkan.dll',
    'libxray_editor_base.dll',
    'libcs_core.dll',
    'libcs_script.dll',
    'xray_asset_cook_cli.exe',
    'xray_shader_compiler.exe',
    'xray_maya_cook_cli.exe',
)

NATIVE_BIN_FILES_LINUX = (
    'stalker2',
    'xray_editor_world.so',
    'libxray_core.so',
    'libxray_engine.so',
    'libxray_render_vulkan.so',
    'libxray_editor_base.so',
    'libcs_core.so',
    'libcs_script.so',
    'xray_asset_cook_cli',
    'xray_shader_compiler',
)

NATIVE_OPTIONAL_FILES_LINUX = (
    'xray_maya_cook_cli',
)

MANAGED_BIN_FILES = (
    'xray.editor.base.dll',
    'xray.editor.base.runtimeconfig.json',
    'xray.editor.base.deps.json',
    'xray.editor.core.dll',
    'xray.editor.core.deps.json',
    'xray.editor.avalonia_controls.dll',
    'xray.editor.avalonia_controls.deps.json',
)

DEBUG_OPTIONAL_FILES = (
    'xray.editor.base.pdb',
    'xray.editor.core.pdb',
    'xray.editor.avalonia_controls.pdb',
)

MINGW_RUNTIME_DLLS = (
    'libBulletCollision.dll',
    'libBulletDynamics.dll',
    'libBulletSoftBody.dll',
    'libLinearMath.dll',
    'libgcc_s_seh-1.dll',
    'libstdc++-6.dll',
    'libwinpthread-1.dll',
    'libmimalloc.dll',
    'lua55.dll',
    'zlib1.dll',
    'libogg-0.dll',
    'libvorbis-0.dll',
    'libvorbisenc-2.dll',
    'libvorbisfile-3.dll',
    'SDL3.dll',
    'libiconv-2.dll',
)

DXC_RUNTIME_DLLS = (
    'dxcompiler.dll',
    'dxil.dll',
)

VALIDATION_FILES = (
    'VkLayer_khronos_validation.dll',
    'VkLayer_khronos_validation.json',
    'libSPIRV-Tools.dll',
    'libSPIRV-Tools-opt.dll',
    'libSPIRV-Tools-shared.dll',
)

SYSTEM_DLL_EXACT = {
    'kernel32.dll', 'user32.dll', 'gdi32.dll', 'advapi32.dll', 'ole32.dll', 'oleaut32.dll',
    'shlwapi.dll', 'shell32.dll', 'winmm.dll', 'ws2_32.dll', 'msvcrt.dll', 'mscoree.dll',
    'vulkan-1.dll', 'xaudio2_9.dll', 'ucrtbase.dll', 'rpcrt4.dll', 'imm32.dll',
    'comctl32.dll', 'bcrypt.dll', 'crypt32.dll', 'version.dll', 'dbghelp.dll', 'psapi.dll',
    'ntdll.dll', 'shcore.dll', 'uxtheme.dll', 'dwmapi.dll', 'setupapi.dll', 'cfgmgr32.dll',
    'dxgi.dll', 'fontsub.dll',
}

LINUX_SYSTEM_SO_PREFIXES = (
    'ld-linux-x86-64.so.',
    'libc.so.',
    'libm.so.',
    'libmvec.so.',
    'libdl.so.',
    'libpthread.so.',
    'librt.so.',
    'libstdc++.so.',
    'libgcc_s.so.',
    'libvulkan.so.',
    'libSDL3.so.',
    'libwayland-client.so.',
    'libwayland-egl.so.',
    'libwayland-cursor.so.',
    'libxkbcommon.so.',
    'libX11.so.',
    'libXext.so.',
    'libXcursor.so.',
    'libXi.so.',
    'libXrandr.so.',
    'libXfixes.so.',
    'libxcb.so.',
    'libmimalloc.so.',
    'liblua',
    'libz.so.',
    'libogg.so.',
    'libvorbis.so.',
    'libvorbisenc.so.',
    'libvorbisfile.so.',
    'libBullet',
    'libLinearMath.so.',
    'libboost_',
    'libfontconfig.so.',
    'libfreetype.so.',
    'libexpat.so.',
    'libGL.so.',
    'libEGL.so.',
    'libGLX.so.',
    'libOpenGL.so.',
    'libdrm.so.',
    'libdbus-1.so.',
    'libsystemd.so.',
    'libtinfo.so.',
    'libncurses',
)


def is_windows_system_dll(name: str) -> bool:
    low = name.lower()
    return (
        low in SYSTEM_DLL_EXACT
        or low.startswith('api-ms-win-')
        or low.startswith('ext-ms-win-')
        or low.startswith('vcruntime')
    )


def is_linux_system_so(name: str) -> bool:
    return any(name.startswith(prefix) for prefix in LINUX_SYSTEM_SO_PREFIXES)


def is_elf_binary(path: Path) -> bool:
    try:
        with path.open('rb') as f:
            return f.read(4) == b'\x7fELF'
    except OSError:
        return False


def sha256_file(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def copy_file(src: Path, dst: Path):
    assert src.is_file(), f'Missing required packaging source file: {src}'
    dst.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(src, dst)


def audit_pe_imports(bin_dir: Path, objdump_exe: Path) -> dict:
    assert objdump_exe.is_file(), f'objdump executable not found: {objdump_exe}'
    scan_dirs = (bin_dir, bin_dir / 'vulkan-validation', bin_dir / 'runtimes' / 'win-x64' / 'native')
    packaged_dlls = {
        p.name.lower()
        for folder in scan_dirs
        if folder.is_dir()
        for p in folder.iterdir()
        if p.is_file() and p.suffix.lower() == '.dll'
    }
    audit = {}
    targets = sorted(
        p
        for folder in scan_dirs
        if folder.is_dir()
        for p in folder.iterdir()
        if p.is_file() and p.suffix.lower() in {'.exe', '.dll'}
    )
    for binary in targets:
        proc = subprocess.run(
            [str(objdump_exe), '-p', str(binary)],
            capture_output=True,
            text=True,
            encoding='utf-8',
            errors='replace',
            check=True,
        )
        imports = sorted(set(re.findall(r'DLL Name:\s*([^\r\n]+)', proc.stdout)))
        missing = [
            dll
            for dll in imports
            if dll.lower() not in packaged_dlls and not is_windows_system_dll(dll)
        ]
        assert not missing, f'Unpackaged PE dependencies in {binary}: {missing}'
        rel_bin = binary.relative_to(bin_dir.parent).as_posix()
        audit[rel_bin] = imports
    return audit


def audit_elf_imports(bin_dir: Path) -> dict:
    readelf_exe = shutil.which('readelf')
    ldd_exe = shutil.which('ldd')
    assert readelf_exe, 'readelf executable not found in PATH'
    assert ldd_exe, 'ldd executable not found in PATH'

    scan_dirs = (
        bin_dir,
        bin_dir / 'vulkan-layers',
        bin_dir / 'runtimes' / 'linux-x64' / 'native',
    )
    packaged_sos = {
        p.name: p.resolve()
        for folder in scan_dirs
        if folder.is_dir()
        for p in folder.iterdir()
        if p.is_file() and is_elf_binary(p) and '.so' in p.name
    }

    c_env = os.environ.copy()
    c_env['LC_ALL'] = 'C'
    c_env.pop('LD_LIBRARY_PATH', None)

    targets = sorted(
        p
        for folder in scan_dirs
        if folder.is_dir()
        for p in folder.iterdir()
        if p.is_file() and is_elf_binary(p)
    )
    assert targets, f'No ELF binaries found to audit in {bin_dir}'

    binaries_report = {}
    missing_count = 0
    unallowed_count = 0
    rpath_issues = []
    resolution_issues = []

    for binary in targets:
        rel_bin = binary.relative_to(bin_dir.parent).as_posix()
        re_proc = subprocess.run(
            [readelf_exe, '-d', str(binary)],
            env=c_env,
            capture_output=True,
            text=True,
            encoding='utf-8',
            errors='replace',
            check=True,
        )
        needed = sorted(set(re.findall(r'\(NEEDED\)\s+Shared library:\s*\[([^\]]+)\]', re_proc.stdout)))
        raw_rpaths = re.findall(r'\((?:RUNPATH|RPATH)\)\s+Library (?:runpath|rpath):\s*\[([^\]]+)\]', re_proc.stdout)
        runpath_entries = [entry for raw in raw_rpaths for entry in raw.split(':') if entry]

        needs_packaged = any(
            dep.startswith('libxray_') or dep.startswith('libcs_') or dep in packaged_sos
            for dep in needed
        )
        if binary.name in {'stalker2', 'xray_editor_world.so'} or needs_packaged:
            valid_origin = bool(runpath_entries) and (
                runpath_entries[0] == '$ORIGIN'
                or (binary.name == 'dxc' and runpath_entries[0].startswith('$ORIGIN'))
            )
            if not valid_origin:
                rpath_issues.append(f'{rel_bin}: RUNPATH/RPATH does not start with $ORIGIN ({runpath_entries})')

        unallowed = [dep for dep in needed if dep not in packaged_sos and not is_linux_system_so(dep)]
        unallowed_count += len(unallowed)

        ldd_proc = subprocess.run(
            [ldd_exe, str(binary)],
            env=c_env,
            capture_output=True,
            text=True,
            encoding='utf-8',
            errors='replace',
        )
        ldd_missing = []
        for line in ldd_proc.stdout.splitlines():
            if '=> not found' in line:
                ldd_missing.append(line.strip())
                continue
            m = re.match(r'^\s*(\S+)\s+=>\s+(\S+)\s+\(', line)
            if m:
                soname, resolved_str = m.groups()
                if soname in packaged_sos and binary.parent == bin_dir:
                    resolved = Path(resolved_str).resolve()
                    expected = packaged_sos[soname]
                    if resolved != expected:
                        resolution_issues.append(
                            f'{rel_bin}: {soname} resolved to {resolved} instead of packaged {expected}'
                        )

        missing_count += len(ldd_missing)
        assert not ldd_missing, f'Missing ELF dependencies in {rel_bin}: {ldd_missing}'
        assert not unallowed, f'Unallowlisted external ELF dependencies in {rel_bin}: {unallowed}'

        binaries_report[rel_bin] = {
            'needed': needed,
            'runpath': runpath_entries,
        }

    assert not rpath_issues, f'ELF RUNPATH/$ORIGIN check failed: {rpath_issues}'
    assert not resolution_issues, f'ELF packaged resolution check failed: {resolution_issues}'

    return {
        'auditedBinaries': len(binaries_report),
        'missingCount': missing_count,
        'unallowedCount': unallowed_count,
        'rpathIssues': rpath_issues,
        'resolutionIssues': resolution_issues,
        'binaries': binaries_report,
    }


def write_windows_launchers(output_dir: Path, config: str):
    is_debug = config.lower() == 'debug'
    launch_ps1 = f"""param(
    [switch]$ControlHostHarness,
    [switch]$Validate,
    [switch]$NoValidate
)
$ErrorActionPreference = 'Stop'
$pkg = [IO.Path]::GetFullPath($PSScriptRoot)
$binDir = Join-Path $pkg 'bin'
$validationDir = Join-Path $binDir 'vulkan-validation'
$exe = Join-Path $binDir 'stalker2.exe'
if (!(Test-Path -LiteralPath $exe)) {{ throw "Packaged executable missing: $exe" }}

$userData = Join-Path $pkg 'user-data'
$tmpDir = Join-Path $pkg 'tmp'
$assetsDir = Join-Path $pkg 'assets'
foreach ($d in @($userData, (Join-Path $userData 'workspace'), (Join-Path $userData 'cache'), $tmpDir)) {{
    New-Item -ItemType Directory -Force -Path $d | Out-Null
}}

$useValidation = {('$true' if is_debug else '$false')}
if ($Validate) {{ $useValidation = $true }}
if ($NoValidate) {{ $useValidation = $false }}

$saved = @{{}}
$vars = @{{
    PATH = $(if ($useValidation) {{ "$validationDir;$env:SystemRoot\\System32;$env:SystemRoot" }} else {{ "$env:SystemRoot\\System32;$env:SystemRoot" }})
    TEMP = $tmpDir
    TMP = $tmpDir
    DISABLE_RTSS_LAYER = '1'
    DISABLE_VULKAN_OBS_CAPTURE = '1'
    XRAY_VULKAN_REQUIRE_VALIDATION = $(if ($useValidation) {{ '1' }} else {{ $null }})
    VK_LAYER_PATH = $(if ($useValidation) {{ $validationDir }} else {{ $null }})
    XRAY_SDK_PROPERTY_EDITOR = $(if ($ControlHostHarness) {{ '0' }} else {{ $null }})
}}
foreach ($k in $vars.Keys) {{
    $saved[$k] = [Environment]::GetEnvironmentVariable($k, 'Process')
    [Environment]::SetEnvironmentVariable($k, $vars[$k], 'Process')
}}
try {{
    $launchArgs = @(
        '-editor',
        '-windowed',
        '-level=vulkan_m8_control',
        ('-render_assets=' + ($assetsDir -replace '\\\\', '/')),
        ('-user_data_path=' + ($userData -replace '\\\\', '/')),
        '-max_resources_size=4096'
    )
    $proc = Start-Process -FilePath $exe -ArgumentList $launchArgs -WorkingDirectory $binDir -PassThru -Wait
    exit $proc.ExitCode
}} finally {{
    foreach ($k in $saved.Keys) {{
        [Environment]::SetEnvironmentVariable($k, $saved[$k], 'Process')
    }}
}}
"""
    (output_dir / 'launch-sdk.ps1').write_text(launch_ps1, encoding='utf-8')

    launch_cmd = """@echo off
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0launch-sdk.ps1" %*
exit /b %ERRORLEVEL%
"""
    (output_dir / 'launch-sdk.cmd').write_text(launch_cmd, encoding='utf-8')


def write_linux_launcher(output_dir: Path, config: str, validation_bundled: bool):
    is_debug = config.lower() == 'debug'
    default_val = '1' if (is_debug and validation_bundled) else '0'
    launch_sh = f"""#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${{BASH_SOURCE[0]}}")" && pwd)"
BIN_DIR="$ROOT/bin"
EXE="$BIN_DIR/stalker2"
if [[ ! -x "$EXE" ]]; then
  echo "Packaged executable missing or not executable: $EXE" >&2
  exit 1
fi

USER_DATA="$ROOT/user-data"
TMP_DIR="$ROOT/tmp"
ASSETS_DIR="$ROOT/assets"
mkdir -p "$USER_DATA/workspace" "$USER_DATA/cache" "$TMP_DIR"

LEVEL="vulkan_m8_control"
USE_VALIDATION={default_val}
CONTROL_HOST_HARNESS=0
EXTRA_ARGS=()

while [[ $# -gt 0 ]]; do
  case "$1" in
    --level)
      LEVEL="$2"
      shift 2
      ;;
    --level=*)
      LEVEL="${{1#*=}}"
      shift
      ;;
    --validate)
      USE_VALIDATION=1
      shift
      ;;
    --no-validate)
      USE_VALIDATION=0
      shift
      ;;
    --control-host-harness)
      CONTROL_HOST_HARNESS=1
      shift
      ;;
    *)
      EXTRA_ARGS+=("$1")
      shift
      ;;
  esac
done

export TMPDIR="$TMP_DIR"
export XRAY_SDK_SETTINGS_PATH="$ROOT/settings/sdk-runtime.json"
if [[ -f "$BIN_DIR/libdxcompiler.so" ]]; then
  export XRAY_DXC_PATH="$BIN_DIR/libdxcompiler.so"
fi

if [[ "$USE_VALIDATION" == "1" ]]; then
  if [[ -d "$BIN_DIR/vulkan-layers" ]]; then
    export VK_LAYER_PATH="$BIN_DIR/vulkan-layers"
  fi
  export XRAY_VULKAN_VALIDATION=1
  export XRAY_VULKAN_REQUIRE_VALIDATION=1
fi

if [[ "$CONTROL_HOST_HARNESS" == "1" ]]; then
  export XRAY_SDK_PROPERTY_EDITOR=0
fi

cd "$BIN_DIR"
exec "$EXE" \\
  -editor \\
  -windowed \\
  "-level=$LEVEL" \\
  "-render_assets=$ASSETS_DIR" \\
  "-user_data_path=$USER_DATA" \\
  -max_resources_size=4096 \\
  "${{EXTRA_ARGS[@]}}"
"""
    launcher_path = output_dir / 'launch-sdk.sh'
    launcher_path.write_text(launch_sh, encoding='utf-8')
    launcher_path.chmod(0o755)


def find_linux_validation_files(validation_dir: Path | None) -> tuple[Path, Path] | None:
    json_candidates = []
    if validation_dir is not None:
        json_candidates.append(validation_dir / 'VkLayer_khronos_validation.json')
    json_candidates.extend([
        Path('/usr/share/vulkan/explicit_layer.d/VkLayer_khronos_validation.json'),
        Path('/usr/local/share/vulkan/explicit_layer.d/VkLayer_khronos_validation.json'),
    ])
    for json_path in json_candidates:
        if not json_path.is_file():
            continue
        try:
            data = json.loads(json_path.read_text(encoding='utf-8'))
            lib_rel = data.get('layer', {}).get('library_path', '')
        except Exception:
            lib_rel = ''
        so_candidates = []
        if lib_rel:
            p = Path(lib_rel)
            if p.is_absolute():
                so_candidates.append(p)
            else:
                so_candidates.append((json_path.parent / p).resolve())
        if validation_dir is not None:
            so_candidates.append(validation_dir / 'libVkLayer_khronos_validation.so')
        so_candidates.extend([
            Path('/usr/lib64/libVkLayer_khronos_validation.so'),
            Path('/usr/lib/x86_64-linux-gnu/libVkLayer_khronos_validation.so'),
            Path('/usr/lib/libVkLayer_khronos_validation.so'),
        ])
        for so_path in so_candidates:
            if so_path.is_file():
                return json_path, so_path
    return None


def copy_managed_runtime_closure(bins_src: Path, bin_dst: Path, rid: str, cfg: str):
    deps_file = bins_src / 'xray.editor.base.deps.json'
    if not deps_file.is_file():
        return
    deps_data = json.loads(deps_file.read_text(encoding='utf-8-sig'))
    for target_entries in deps_data.get('targets', {}).values():
        for pkg in target_entries.values():
            for rel_asset in pkg.get('runtime', {}).keys():
                file_name = Path(rel_asset).name
                if file_name.lower().endswith('.dll') and not (bin_dst / file_name).exists():
                    src_dll = bins_src / file_name
                    if src_dll.is_file():
                        copy_file(src_dll, bin_dst / file_name)
            for rel_asset, meta in pkg.get('runtimeTargets', {}).items():
                if meta.get('rid') == rid and meta.get('assetType') == 'native':
                    local_rel = meta.get('localPath') or rel_asset
                    if local_rel.lower().endswith('.pdb') and cfg != 'debug':
                        continue
                    src_asset = bins_src / local_rel
                    dst_asset = bin_dst / local_rel
                    if src_asset.is_file() and not dst_asset.exists():
                        copy_file(src_asset, dst_asset)
    # Ensure runtimes/<rid>/native contents are present even if MSBuild flattened them differently
    native_src = bins_src / 'runtimes' / rid / 'native'
    if native_src.is_dir():
        for item in native_src.iterdir():
            if item.is_file():
                if item.suffix.lower() == '.pdb' and cfg != 'debug':
                    continue
                dst_item = bin_dst / 'runtimes' / rid / 'native' / item.name
                if not dst_item.exists():
                    copy_file(item, dst_item)


def package_runtime(
    platform_name: str,
    config: str,
    build_dir: Path,
    output_dir: Path,
    mingw_bin: Path,
    dxc_dir: Path,
    validation_dir: Path | None,
    assets_root: Path,
    shaders_root: Path,
    clean: bool,
):
    resolved_platform = ('windows-x64' if os.name == 'nt' else 'linux-x64') if platform_name == 'auto' else platform_name
    assert resolved_platform in {'windows-x64', 'linux-x64'}, f'Unsupported platform: {platform_name}'
    cfg = config.lower()
    assert cfg in {'debug', 'release'}, f'Unsupported config: {config}'
    bins_src = build_dir / 'binaries'
    assert bins_src.is_dir(), f'Missing build binaries folder: {bins_src}'
    assert (assets_root / 'scene.json').is_file(), f'Missing prepared assets root: {assets_root}'
    assert shaders_root.is_dir(), f'Missing shaders root: {shaders_root}'

    if output_dir.exists():
        assert clean, f'Output directory already exists (pass --clean to replace): {output_dir}'
        shutil.rmtree(output_dir)

    output_dir.mkdir(parents=True, exist_ok=True)
    bin_dst = output_dir / 'bin'
    shaders_dst = output_dir / 'shaders'
    assets_dst = output_dir / 'assets'
    settings_dst = output_dir / 'settings'
    userdata_dst = output_dir / 'user-data'

    # 1. Native & managed binaries + deps closure
    if resolved_platform == 'windows-x64':
        for name in NATIVE_BIN_FILES_WIN:
            copy_file(bins_src / name, bin_dst / name)
        rid = 'win-x64'
    else:
        for name in NATIVE_BIN_FILES_LINUX:
            copy_file(bins_src / name, bin_dst / name)
        for name in NATIVE_OPTIONAL_FILES_LINUX:
            p = bins_src / name
            if p.is_file():
                copy_file(p, bin_dst / name)
        rid = 'linux-x64'

    for name in MANAGED_BIN_FILES:
        copy_file(bins_src / name, bin_dst / name)
    copy_managed_runtime_closure(bins_src, bin_dst, rid, cfg)

    if cfg == 'debug':
        for name in DEBUG_OPTIONAL_FILES:
            p = bins_src / name
            if p.is_file():
                copy_file(p, bin_dst / name)

    # 2. Platform runtime deps, DXC, Vulkan validation layers
    validation_bundled = False
    if resolved_platform == 'windows-x64':
        val_dir = validation_dir or DEFAULT_VALIDATION_DIR_WIN
        val_dst = bin_dst / 'vulkan-validation'
        for name in MINGW_RUNTIME_DLLS:
            copy_file(mingw_bin / name, bin_dst / name)
        for name in DXC_RUNTIME_DLLS:
            copy_file(dxc_dir / name, bin_dst / name)
        for name in VALIDATION_FILES:
            copy_file(val_dir / name, val_dst / name)
        validation_bundled = True
    else:
        dxc_so = dxc_dir / 'lib/libdxcompiler.so'
        if not dxc_so.is_file():
            dxc_so = dxc_dir / 'libdxcompiler.so'
        copy_file(dxc_so, bin_dst / 'libdxcompiler.so')
        for opt_dxc in ('bin/dxc', 'dxc', 'lib/libdxil.so', 'libdxil.so'):
            cand = dxc_dir / opt_dxc
            if cand.is_file():
                copy_file(cand, bin_dst / cand.name)
        if (bin_dst / 'dxc').is_file() and (bin_dst / 'libdxcompiler.so').is_file():
            # Prebuilt Microsoft dxc sets RPATH=$ORIGIN/../lib relative to bin/dxc.
            lib_dir = output_dir / 'lib'
            lib_dir.mkdir(parents=True, exist_ok=True)
            link_path = lib_dir / 'libdxcompiler.so'
            if link_path.exists() or link_path.is_symlink():
                link_path.unlink()
            link_path.symlink_to('../bin/libdxcompiler.so')

        found_val = find_linux_validation_files(validation_dir)
        if found_val is not None:
            src_json, src_so = found_val
            val_dst = bin_dst / 'vulkan-layers'
            val_dst.mkdir(parents=True, exist_ok=True)
            copy_file(src_so, val_dst / src_so.name)
            layer_meta = json.loads(src_json.read_text(encoding='utf-8'))
            if isinstance(layer_meta.get('layer'), dict):
                layer_meta['layer']['library_path'] = f'./{src_so.name}'
            (val_dst / src_json.name).write_text(
                json.dumps(layer_meta, indent=2, ensure_ascii=False) + '\n',
                encoding='utf-8',
            )
            validation_bundled = True

    # 3. Shaders & read-only assets + SDK-11 reference fixtures
    shutil.copytree(shaders_root, shaders_dst)
    shutil.copytree(assets_root, assets_dst)
    if SDK11_FIXTURES_ROOT.is_dir():
        shutil.copytree(SDK11_FIXTURES_ROOT, assets_dst / 'fixtures' / 'sdk11')

    # 4. Separated writable runtime folders
    for folder in (
        userdata_dst / 'workspace' / 'sources',
        userdata_dst / 'cache',
        output_dir / 'tmp',
        settings_dst,
    ):
        folder.mkdir(parents=True, exist_ok=True)

    # 5. Runtime settings & launch scripts
    runtime_cfg = json.loads((bin_dst / 'xray.editor.base.runtimeconfig.json').read_text(encoding='utf-8-sig'))
    runtime_opts = runtime_cfg.get('runtimeOptions', {})
    dotnet_frameworks = runtime_opts.get('frameworks')
    if dotnet_frameworks is None:
        fw = runtime_opts.get('framework')
        dotnet_frameworks = [fw] if fw is not None else []
    runtime_settings = {
        'schemaVersion': 1,
        'product': 'X-Ray 2.0 Modernized SDK',
        'configuration': 'Debug' if cfg == 'debug' else 'Release',
        'architecture': resolved_platform,
        'defaultLaunch': {
            'executable': 'bin/stalker2.exe' if resolved_platform == 'windows-x64' else 'bin/stalker2',
            'arguments': [
                '-editor',
                '-windowed',
                '-level=vulkan_m8_control',
                '-render_assets=../assets',
                '-user_data_path=../user-data',
                '-max_resources_size=4096',
            ],
            'opensRestoredIdeByDefault': True,
            'vulkanValidationDefault': (cfg == 'debug') and validation_bundled,
        },
        'controlHostHarness': {
            'description': 'Separate M8 control Vulkan host test harness',
            'env': {'XRAY_SDK_PROPERTY_EDITOR': '0'},
            'launcherFlag': '-ControlHostHarness' if resolved_platform == 'windows-x64' else '--control-host-harness',
        },
        'paths': {
            'bin': 'bin',
            'shaders': 'shaders',
            'assetsReadOnly': 'assets',
            'writableUserData': 'user-data',
            'writableWorkspace': 'user-data/workspace',
            'writableCache': 'user-data/cache',
            'temp': 'tmp',
        },
        'prerequisites': {
            'os': 'Windows 10/11 x64' if resolved_platform == 'windows-x64' else 'Linux x86_64 (Wayland / X11)',
            'dotnetRuntime': dotnet_frameworks,
            'vulkan': 'Vulkan 1.3+ ICD GPU driver with shaderDemoteToHelperInvocation',
        },
    }
    (settings_dst / 'sdk-runtime.json').write_text(
        json.dumps(runtime_settings, indent=2, ensure_ascii=False) + '\n',
        encoding='utf-8',
    )
    if resolved_platform == 'windows-x64':
        write_windows_launchers(output_dir, cfg)
    else:
        write_linux_launcher(output_dir, cfg, validation_bundled)

    # 6. Automated PE or ELF import audit (before recording manifest)
    pe_audit = {}
    elf_audit = {}
    if resolved_platform == 'windows-x64':
        pe_audit = audit_pe_imports(bin_dst, mingw_bin / 'objdump.exe')
    else:
        elf_audit = audit_elf_imports(bin_dst)

    # 7. Record full file manifest
    files = {}
    for p in sorted(output_dir.rglob('*')):
        if p.name in {'package-manifest.json', 'sdk-package-manifest.json'}:
            continue
        rel = p.relative_to(output_dir).as_posix()
        if p.is_symlink():
            files[rel] = {'symlinkTo': os.readlink(p)}
            continue
        if not p.is_file():
            continue
        files[rel] = {
            'bytes': p.stat().st_size,
            'sha256': sha256_file(p),
        }

    manifest = {
        'schemaVersion': 1,
        'platform': resolved_platform,
        'configuration': 'Debug' if cfg == 'debug' else 'Release',
        'validationLayerBundled': validation_bundled,
        'packageRoot': output_dir.resolve().relative_to(REPO).as_posix()
        if REPO in output_dir.resolve().parents
        else output_dir.resolve().as_posix(),
        'sourceBuildDir': build_dir.resolve().relative_to(REPO).as_posix()
        if REPO in build_dir.resolve().parents
        else build_dir.resolve().as_posix(),
        'totalFiles': len(files),
        'peAuditedBinaries': len(pe_audit),
        'peImportAuditPassed': resolved_platform == 'windows-x64',
        'peImports': pe_audit,
        'elfAuditPassed': resolved_platform == 'linux-x64',
        'elfAudit': elf_audit,
        'files': files,
    }
    manifest_text = json.dumps(manifest, indent=2, ensure_ascii=False) + '\n'
    (output_dir / 'package-manifest.json').write_text(manifest_text, encoding='utf-8')
    (output_dir / 'sdk-package-manifest.json').write_text(manifest_text, encoding='utf-8')
    audited_cnt = len(pe_audit) if resolved_platform == 'windows-x64' else elf_audit.get('auditedBinaries', 0)
    print(
        f'PASS: Packaged {resolved_platform} {manifest["configuration"]} SDK runtime at {output_dir} '
        f'({len(files)} files, {audited_cnt} binaries audited)'
    )


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--platform', default='auto', choices=('auto', 'windows-x64', 'linux-x64'))
    parser.add_argument('--config', required=True, choices=('debug', 'release'))
    parser.add_argument('--build-dir', required=True)
    parser.add_argument('--output', required=True)
    parser.add_argument('--mingw-bin', default=str(DEFAULT_MINGW_BIN))
    parser.add_argument('--dxc-dir', default=None)
    parser.add_argument('--validation-dir', default=None)
    parser.add_argument('--assets-root', default=str(DEFAULT_ASSETS_ROOT))
    parser.add_argument('--shaders-root', default=str(DEFAULT_SHADERS_ROOT))
    parser.add_argument('--clean', action='store_true')
    args = parser.parse_args()

    def resolve_path(val: str) -> Path:
        p = Path(val)
        return p if p.is_absolute() else (REPO / p).resolve()

    resolved_plat = ('windows-x64' if os.name == 'nt' else 'linux-x64') if args.platform == 'auto' else args.platform
    default_dxc = DEFAULT_DXC_DIR_WIN if resolved_plat == 'windows-x64' else DEFAULT_DXC_DIR_LINUX
    dxc_path = resolve_path(args.dxc_dir) if args.dxc_dir else default_dxc
    val_path = resolve_path(args.validation_dir) if args.validation_dir else None

    package_runtime(
        args.platform,
        args.config,
        resolve_path(args.build_dir),
        resolve_path(args.output),
        resolve_path(args.mingw_bin),
        dxc_path,
        val_path,
        resolve_path(args.assets_root),
        resolve_path(args.shaders_root),
        args.clean,
    )
