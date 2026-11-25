"""Assemble a clean, self-contained Windows x64 Debug or Release SDK runtime deployment."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import subprocess

REPO = Path(__file__).resolve().parents[3]
DEFAULT_MINGW_BIN = Path('C:/msys64/mingw64/bin')
DEFAULT_DXC_DIR = REPO / 'build/dxc/bin/x64'
DEFAULT_VALIDATION_DIR = REPO / 'build/vulkan-validation/mingw64/bin'
DEFAULT_ASSETS_ROOT = REPO / 'build/sdk-assets'
DEFAULT_SHADERS_ROOT = REPO / 'sources/xray/render/vulkan/assets'
SDK11_FIXTURES_ROOT = REPO / 'sources/xray/editor/sdk_ui/sdk11/fixtures'

NATIVE_BIN_FILES = (
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


def is_windows_system_dll(name: str) -> bool:
    low = name.lower()
    return (
        low in SYSTEM_DLL_EXACT
        or low.startswith('api-ms-win-')
        or low.startswith('ext-ms-win-')
        or low.startswith('vcruntime')
    )


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


def write_launchers(output_dir: Path, config: str):
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


def package_runtime(
    config: str,
    build_dir: Path,
    output_dir: Path,
    mingw_bin: Path,
    dxc_dir: Path,
    validation_dir: Path,
    assets_root: Path,
    shaders_root: Path,
    clean: bool,
):
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
    val_dst = bin_dst / 'vulkan-validation'
    shaders_dst = output_dir / 'shaders'
    assets_dst = output_dir / 'assets'
    settings_dst = output_dir / 'settings'
    userdata_dst = output_dir / 'user-data'

    # 1. Native & managed binaries
    for name in NATIVE_BIN_FILES:
        copy_file(bins_src / name, bin_dst / name)
    for name in MANAGED_BIN_FILES:
        copy_file(bins_src / name, bin_dst / name)

    # Managed runtime & win-x64 native assets declared in xray.editor.base.deps.json
    deps_data = json.loads((bins_src / 'xray.editor.base.deps.json').read_text(encoding='utf-8-sig'))
    for target_entries in deps_data.get('targets', {}).values():
        for pkg in target_entries.values():
            for rel_asset in pkg.get('runtime', {}).keys():
                file_name = Path(rel_asset).name
                if file_name.lower().endswith('.dll') and not (bin_dst / file_name).exists():
                    copy_file(bins_src / file_name, bin_dst / file_name)
            for rel_asset, meta in pkg.get('runtimeTargets', {}).items():
                if meta.get('rid') == 'win-x64' and meta.get('assetType') == 'native':
                    local_rel = meta.get('localPath') or rel_asset
                    if local_rel.lower().endswith('.pdb') and cfg != 'debug':
                        continue
                    src_asset = bins_src / local_rel
                    dst_asset = bin_dst / local_rel
                    if src_asset.is_file() and not dst_asset.exists():
                        copy_file(src_asset, dst_asset)

    if cfg == 'debug':
        for name in DEBUG_OPTIONAL_FILES:
            p = bins_src / name
            if p.is_file():
                copy_file(p, bin_dst / name)

    # 2. MinGW & third-party runtime DLLs + DXC + Vulkan validation layer
    for name in MINGW_RUNTIME_DLLS:
        copy_file(mingw_bin / name, bin_dst / name)
    for name in DXC_RUNTIME_DLLS:
        copy_file(dxc_dir / name, bin_dst / name)
    for name in VALIDATION_FILES:
        copy_file(validation_dir / name, val_dst / name)

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
        'architecture': 'win-x64',
        'defaultLaunch': {
            'executable': 'bin/stalker2.exe',
            'arguments': [
                '-editor',
                '-windowed',
                '-level=vulkan_m8_control',
                '-render_assets=../assets',
                '-user_data_path=../user-data',
                '-max_resources_size=4096',
            ],
            'opensRestoredIdeByDefault': True,
            'vulkanValidationDefault': cfg == 'debug',
        },
        'controlHostHarness': {
            'description': 'Separate M8 control Vulkan host test harness',
            'env': {'XRAY_SDK_PROPERTY_EDITOR': '0'},
            'launcherFlag': '-ControlHostHarness',
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
            'os': 'Windows 10/11 x64',
            'dotnetRuntime': dotnet_frameworks,
            'vulkan': 'Vulkan 1.3+ ICD GPU driver with shaderDemoteToHelperInvocation',
        },
    }
    (settings_dst / 'sdk-runtime.json').write_text(
        json.dumps(runtime_settings, indent=2, ensure_ascii=False) + '\n',
        encoding='utf-8',
    )
    write_launchers(output_dir, cfg)

    # 6. Automated PE import audit (before recording manifest)
    pe_audit = audit_pe_imports(bin_dst, mingw_bin / 'objdump.exe')

    # 7. Record full file manifest (binaries, dependencies, shaders, assets, settings, launchers)
    files = {}
    for p in sorted(output_dir.rglob('*')):
        if not p.is_file() or p.name == 'package-manifest.json':
            continue
        rel = p.relative_to(output_dir).as_posix()
        files[rel] = {
            'bytes': p.stat().st_size,
            'sha256': sha256_file(p),
        }

    manifest = {
        'schemaVersion': 1,
        'configuration': 'Debug' if cfg == 'debug' else 'Release',
        'packageRoot': output_dir.resolve().relative_to(REPO).as_posix()
        if REPO in output_dir.resolve().parents
        else output_dir.resolve().as_posix(),
        'sourceBuildDir': build_dir.resolve().relative_to(REPO).as_posix()
        if REPO in build_dir.resolve().parents
        else build_dir.resolve().as_posix(),
        'totalFiles': len(files),
        'peAuditedBinaries': len(pe_audit),
        'peImportAuditPassed': True,
        'peImports': pe_audit,
        'files': files,
    }
    (output_dir / 'package-manifest.json').write_text(
        json.dumps(manifest, indent=2, ensure_ascii=False) + '\n',
        encoding='utf-8',
    )
    print(f'PASS: Packaged {manifest["configuration"]} SDK runtime at {output_dir} ({len(files)} files, {len(pe_audit)} PE binaries audited)')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--config', required=True, choices=('debug', 'release'))
    parser.add_argument('--build-dir', required=True)
    parser.add_argument('--output', required=True)
    parser.add_argument('--mingw-bin', default=str(DEFAULT_MINGW_BIN))
    parser.add_argument('--dxc-dir', default=str(DEFAULT_DXC_DIR))
    parser.add_argument('--validation-dir', default=str(DEFAULT_VALIDATION_DIR))
    parser.add_argument('--assets-root', default=str(DEFAULT_ASSETS_ROOT))
    parser.add_argument('--shaders-root', default=str(DEFAULT_SHADERS_ROOT))
    parser.add_argument('--clean', action='store_true')
    args = parser.parse_args()

    def resolve_path(val: str) -> Path:
        p = Path(val)
        return p if p.is_absolute() else (REPO / p).resolve()

    package_runtime(
        args.config,
        resolve_path(args.build_dir),
        resolve_path(args.output),
        resolve_path(args.mingw_bin),
        resolve_path(args.dxc_dir),
        resolve_path(args.validation_dir),
        resolve_path(args.assets_root),
        resolve_path(args.shaders_root),
        args.clean,
    )
