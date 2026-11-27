"""Verify repository source, preset, toolchain manifest, and .NET contracts."""

from __future__ import annotations

import argparse
import json
import re
from pathlib import Path


def check_no_bundled_lua(sources_dir: Path) -> None:
    forbidden = {"lua.h", "lauxlib.h", "lualib.h", "luaconf.h"}
    for sub in ("lua", "xray"):
        root = sources_dir / sub
        if not root.is_dir():
            continue
        for path in root.rglob("*.h"):
            if path.name.lower() in forbidden:
                raise AssertionError(f"Forbidden bundled Lua header found: {path}")


def check_root_cmake(cmake_path: Path) -> None:
    text = cmake_path.read_text(encoding="utf-8")
    for required in (
        'CMAKE_SYSTEM_NAME STREQUAL "Windows"',
        "CMAKE_SIZEOF_VOID_P EQUAL 8",
        "USE_VULKAN_RENDERER",
        "CMAKE_CXX_STANDARD 26",
        "include(CTest)",
    ):
        if required not in text:
            raise AssertionError(f"Missing required contract '{required}' in {cmake_path}")


def check_dotnet_alignment(repo_root: Path) -> None:
    global_json = json.loads((repo_root / "global.json").read_text(encoding="utf-8"))
    sdk = global_json["sdk"]
    sdk_version = sdk["version"]
    if sdk_version != "11.0.100" or sdk.get("rollForward") != "latestPatch" or sdk.get("allowPrerelease") is not False:
        raise AssertionError(f"Unexpected global.json SDK pinning: {sdk!r}")
    major = sdk_version.split(".", 1)[0]
    expected_tfm = f"net{major}.0"
    expected_lock_tfm = f"net{major}.0"

    if (repo_root / "sources/xray/wpf_controls").exists():
        raise AssertionError("Legacy sources/xray/wpf_controls directory must be removed")

    editor_csproj = repo_root / "sources/xray/editor/base/EditorHost.csproj.in"
    editor_text = editor_csproj.read_text(encoding="utf-8")
    for prop, expected_val in (
        ("TargetFramework", expected_tfm),
        ("RuntimeFrameworkVersion", f"{major}.0.0"),
        ("RestorePackagesWithLockFile", "true"),
        ("RestoreLockedMode", "true"),
        ("NuGetLockFilePath", "@CMAKE_CURRENT_SOURCE_DIR@/EditorHost.packages.lock.json"),
    ):
        m = re.search(rf"<{prop}>([^<]+)</{prop}>", editor_text)
        if not m or m.group(1) != expected_val:
            raise AssertionError(f"{editor_csproj} <{prop}>={m.group(1) if m else None!r}, expected {expected_val!r}")
    for forbidden in ("<UseWindowsForms>", "<UseWPF>", "DockPanelSuite", "Avalonia.Win32.Interoperability", "wpf_controls"):
        if forbidden in editor_text:
            raise AssertionError(f"{editor_csproj} must not reference {forbidden!r}")

    for csproj_rel, req_props in (
        ("sources/xray/editor/core/xray.editor.core.csproj", (("TargetFramework", expected_tfm),)),
        (
            "sources/xray/editor/avalonia_controls/xray.editor.avalonia_controls.csproj",
            (("TargetFramework", expected_tfm), ("RestorePackagesWithLockFile", "true"), ("RestoreLockedMode", "true")),
        ),
        (
            "sources/xray/editor/avalonia_controls.tests/xray.editor.avalonia_controls.tests.csproj",
            (("TargetFramework", expected_tfm), ("RestorePackagesWithLockFile", "true"), ("RestoreLockedMode", "true")),
        ),
    ):
        csproj_path = repo_root / csproj_rel
        csproj_text = csproj_path.read_text(encoding="utf-8")
        for prop, expected_val in req_props:
            m = re.search(rf"<{prop}>([^<]+)</{prop}>", csproj_text)
            if not m or m.group(1) != expected_val:
                raise AssertionError(f"{csproj_path} <{prop}>={m.group(1) if m else None!r}, expected {expected_val!r}")

    for lock_path in (
        repo_root / "sources/xray/editor/base/EditorHost.packages.lock.json",
        repo_root / "sources/xray/editor/avalonia_controls/packages.lock.json",
        repo_root / "sources/xray/editor/avalonia_controls.tests/packages.lock.json",
    ):
        if not lock_path.is_file():
            raise AssertionError(f"Missing NuGet lock file: {lock_path}")
        lock_text = lock_path.read_text(encoding="utf-8")
        lock_data = json.loads(lock_text)
        if lock_data.get("version") != 1:
            raise AssertionError(f"{lock_path} has unexpected lock version {lock_data.get('version')!r}")
        deps = lock_data.get("dependencies", {})
        if expected_lock_tfm not in deps or not deps[expected_lock_tfm]:
            raise AssertionError(f"{lock_path} missing dependencies for {expected_lock_tfm!r}")
        for forbidden in ("DockPanelSuite", "WeifenLuo", "Avalonia.Win32.Interoperability"):
            if forbidden in lock_text:
                raise AssertionError(f"{lock_path} must not contain legacy package {forbidden!r}")


def check_presets(presets_path: Path) -> None:
    data = json.loads(presets_path.read_text(encoding="utf-8"))
    configs = {p["name"]: p for p in data.get("configurePresets", [])}
    for name in ("windows-x64-debug", "windows-x64-release", "linux-x64-debug", "linux-x64-release"):
        if name not in configs:
            raise AssertionError(f"Missing configure preset '{name}'")
        preset = configs[name]
        if not preset.get("binaryDir"):
            raise AssertionError(f"Preset '{name}' missing binaryDir")
        out_dir = preset.get("cacheVariables", {}).get("XRAY_BINARY_OUTPUT_DIRECTORY")
        if not out_dir:
            raise AssertionError(f"Preset '{name}' missing XRAY_BINARY_OUTPUT_DIRECTORY")

    for dbg, rel in (("windows-x64-debug", "windows-x64-release"), ("linux-x64-debug", "linux-x64-release")):
        if configs[dbg]["binaryDir"] == configs[rel]["binaryDir"]:
            raise AssertionError(f"{dbg} and {rel} presets must use distinct binaryDir")
        if (
            configs[dbg]["cacheVariables"]["XRAY_BINARY_OUTPUT_DIRECTORY"]
            == configs[rel]["cacheVariables"]["XRAY_BINARY_OUTPUT_DIRECTORY"]
        ):
            raise AssertionError(f"{dbg} and {rel} presets must use distinct XRAY_BINARY_OUTPUT_DIRECTORY")

    for tp in data.get("testPresets", []):
        if tp.get("execution", {}).get("noTestsAction") != "error":
            raise AssertionError(f"Test preset '{tp.get('name')}' must set execution.noTestsAction='error'")


def check_toolchain_manifest(manifest_path: Path) -> None:
    data = json.loads(manifest_path.read_text(encoding="utf-8"))
    for key in ("platform", "generator", "cxxStandard", "msys2Mingw64Packages", "dotnet", "dxc"):
        if key not in data:
            raise AssertionError(f"Missing '{key}' in {manifest_path}")
    if data["platform"] != "windows-x64" or data["cxxStandard"] != "26":
        raise AssertionError("Unexpected platform/cxxStandard in toolchain manifest")
    if data["dotnet"].get("tfm") != "net11.0":
        raise AssertionError(f"Unexpected dotnet.tfm in toolchain manifest: {data['dotnet']!r}")


def check_stage04_modernized_boundaries(sources_dir: Path) -> None:
    root_cmake = (sources_dir / "CMakeLists.txt").read_text(encoding="utf-8")
    if "find_package(SDL3 CONFIG REQUIRED)" not in root_cmake or "SDL2" in root_cmake:
        raise AssertionError("sources/CMakeLists.txt must use find_package(SDL3 CONFIG REQUIRED) and not SDL2")

    input_cmake = (sources_dir / "xray/input/CMakeLists.txt").read_text(encoding="utf-8")
    if "SDL3::SDL3" not in input_cmake or "xinput" in input_cmake.lower() or "dinput" in input_cmake.lower():
        raise AssertionError("xray/input/CMakeLists.txt must link SDL3::SDL3 without XInput/DirectInput")

    bugtrap_win = (sources_dir / "xray/debug/sources/bugtrap_win.cpp").read_text(encoding="utf-8")
    for forbidden in ("BugTrap/BugTrap.h", "BugTrap-x64.dll", "BugTrapN-x64.dll"):
        if forbidden in bugtrap_win:
            raise AssertionError(f"Legacy BugTrap reference '{forbidden}' found in bugtrap_win.cpp")

    sound_cmake = (sources_dir / "xray/sound/CMakeLists.txt").read_text(encoding="utf-8")
    if "xaudio2_9" not in sound_cmake or "xaudio2_8" in sound_cmake:
        raise AssertionError("xray/sound/CMakeLists.txt must link xaudio2_9 (not xaudio2_8)")

    stalker_cmake = (sources_dir / "xray/stalker2/pc/CMakeLists.txt").read_text(encoding="utf-8")
    if "xinput" in stalker_cmake.lower():
        raise AssertionError("xray/stalker2/pc/CMakeLists.txt must not link XInput directly")

    io_srv = (sources_dir / "xray/network/sources/io_service.cpp").read_text(encoding="utf-8")
    if "test_port" in io_srv or "1001" in io_srv:
        raise AssertionError("io_service.cpp must not hardcode UDP test_port 1001 at startup")

    pkg_py = (sources_dir / "tools/sdk/package-sdk-runtime.py").read_text(encoding="utf-8")
    if "SDL3.dll" not in pkg_py:
        raise AssertionError("package-sdk-runtime.py must package SDL3.dll")

    manifest = (sources_dir / "tools/ci/toolchain-manifest.json").read_text(encoding="utf-8")
    if "mingw-w64-x86_64-sdl3" not in manifest:
        raise AssertionError("toolchain-manifest.json must list mingw-w64-x86_64-sdl3")


def check_stage05_modernized_boundaries(sources_dir: Path) -> None:
    root_cmake = (sources_dir / "CMakeLists.txt").read_text(encoding="utf-8")
    for opt in (
        "XRAY_GAME_PHYSICS_USE_BULLET",
        "XRAY_COLLISION_USE_BULLET_BVH",
        "XRAY_COOK_FITTER_USE_NATIVE",
    ):
        if opt not in root_cmake:
            raise AssertionError(f"sources/CMakeLists.txt missing Stage 05 option {opt}")

    game_init = (sources_dir / "xray/game/sources/game_initialize.cpp").read_text(encoding="utf-8")
    if "XRAY_GAME_PHYSICS_USE_BULLET" not in game_init:
        raise AssertionError("game_initialize.cpp must gate Bullet/ODE world via XRAY_GAME_PHYSICS_USE_BULLET")

    phys_cooker = (sources_dir / "xray/maya_cook/sources/physics_cooker.cpp").read_text(encoding="utf-8")
    if "Wm4" in phys_cooker:
        raise AssertionError("physics_cooker.cpp must stay isolated from WildMagic4 (Wm4)")


def check_stage06_modernized_boundaries(repo_root: Path) -> None:
    sources_dir = repo_root / "sources"
    root_cmake = (sources_dir / "CMakeLists.txt").read_text(encoding="utf-8")
    for required in (
        "option(XRAY_BUILD_EDITOR_HOST",
        "if(XRAY_BUILD_EDITOR_HOST AND NOT XRAY_HEADLESS_BUILD)",
    ):
        if required not in root_cmake:
            raise AssertionError(f"sources/CMakeLists.txt missing Stage 06 EditorHost decoupling '{required}'")

    stalker_cmake = (sources_dir / "xray/stalker2/pc/CMakeLists.txt").read_text(encoding="utf-8")
    if "if(TARGET xray_editor_world)" not in stalker_cmake:
        raise AssertionError("xray/stalker2/pc/CMakeLists.txt must guard xray_editor_world dependency with if(TARGET ...)")

    lock_path = sources_dir / "tools/blender/legacy-corpus-lock.json"
    if not lock_path.is_file():
        raise AssertionError(f"Missing legacy corpus lock file: {lock_path}")
    lock_data = json.loads(lock_path.read_text(encoding="utf-8"))
    if lock_data.get("schema") != "xray-legacy-corpus-lock/v1":
        raise AssertionError(f"Unexpected corpus lock schema: {lock_data.get('schema')!r}")
    for key in ("inventory_sha256", "content_sha256"):
        digest = lock_data.get(key, "")
        if not isinstance(digest, str) or len(digest) != 64 or not re.fullmatch(r"[0-9a-f]{64}", digest):
            raise AssertionError(f"{lock_path} invalid {key}: {digest!r}")
    if lock_data.get("summary", {}).get("assets", 0) <= 0:
        raise AssertionError(f"{lock_path} summary.assets must be > 0")

    manifest_path = sources_dir / "xray/stalker2/pc/stalker2.manifest"
    manifest_text = manifest_path.read_text(encoding="utf-8")
    if "{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}" not in manifest_text:
        raise AssertionError(f"{manifest_path} must declare Win10/11 supportedOS for Avalonia NativeControlHost")


def check_stage07_modernized_boundaries(sources_dir: Path) -> None:
    vk_dir = sources_dir / "xray/render/vulkan/sources"
    corpus = "\n".join(p.read_text(encoding="utf-8", errors="replace") for p in sorted(vk_dir.glob("*.cpp")))
    for required in (
        "vkCmdPipelineBarrier2",
        "vkQueueSubmit2",
        "vkCmdWriteTimestamp2",
        "vkCmdBeginRendering",
        "vkCmdEndRendering",
        "VK_STRUCTURE_TYPE_PIPELINE_RENDERING_CREATE_INFO",
    ):
        if required not in corpus:
            raise AssertionError(f"Vulkan backend missing Stage 07 API '{required}'")

    for forbidden in (
        "vkCmdPipelineBarrier(",
        "vkQueueSubmit(",
        "vkCmdWriteTimestamp(",
        "vkCmdBeginRenderPass(",
        "vkCmdEndRenderPass(",
        "vkCreateRenderPass(",
        "vkCreateFramebuffer(",
    ):
        if forbidden in corpus:
            raise AssertionError(f"Vulkan backend still calls legacy API '{forbidden}'")


def check_stage08_modernized_boundaries(repo_root: Path) -> None:
    sources_dir = repo_root / "sources"
    archival_removed = (
        "asio.zip",
        "BugTrap",
        "build_log.txt",
        "build_log_final.txt",
        "UpgradeLog.htm",
        "vcpkg.json",
        "xray/render/dx9",
        "xray/render/common",
        "xray/maya",
        "xray/maya_animation",
        "xray/graphics_benchmark",
        "xray/rms_generator",
        "xray/rtp_learn",
        "xray/shader_generator",
        "xray/sound_system_usage_example",
        "xray/vfs_pack",
        "xray/finger_printer",
    )
    for rel in archival_removed:
        if (sources_dir / rel).exists():
            raise AssertionError(f"Archival path '{rel}' should have been removed from active build tree")

    dbg_init = (sources_dir / "cs/core/sources/debug_initialize.cpp").read_text(encoding="utf-8")
    if "#\tdefine CS_USE_BUG_TRAP\t\t\t\t1" in dbg_init or "#define CS_USE_BUG_TRAP 1" in dbg_init:
        raise AssertionError("cs/core/sources/debug_initialize.cpp must keep CS_USE_BUG_TRAP disabled (0)")

    formalized_cmakes = (
        "xray/render/vulkan/CMakeLists.txt",
        "xray/collision/CMakeLists.txt",
        "xray/physics/CMakeLists.txt",
        "xray/core/CMakeLists.txt",
        "xray/animation/CMakeLists.txt",
        "xray/debug/CMakeLists.txt",
        "xray/input/CMakeLists.txt",
        "xray/logging/CMakeLists.txt",
        "xray/particle/CMakeLists.txt",
        "xray/sound/CMakeLists.txt",
        "xray/rtp/CMakeLists.txt",
        "xray/fs/CMakeLists.txt",
        "cs/core/CMakeLists.txt",
        "cs/script/CMakeLists.txt",
        "opcode/CMakeLists.txt",
        "xray/engine/CMakeLists.txt",
        "xray/network/CMakeLists.txt",
        "xray/stalker2/pc/CMakeLists.txt",
    )
    for rel in formalized_cmakes:
        text = (sources_dir / rel).read_text(encoding="utf-8")
        if "file(GLOB" in text or "list(FILTER" in text or "list(REMOVE_ITEM" in text:
            raise AssertionError(f"CMakeLists '{rel}' must use explicit source lists without GLOB/FILTER/REMOVE_ITEM")

    for doc_rel in (
        "THIRD_PARTY_DEPENDENCIES.md",
        "versions.txt",
        "tools/ci/build_source_map.py",
        "tools/ci/dependencies-manifest.json",
    ):
        p = sources_dir / doc_rel
        if not p.is_file() or p.stat().st_size == 0:
            raise AssertionError(f"Missing required Stage 08 documentation/tool file: {doc_rel}")

    manifest = json.loads((sources_dir / "tools/ci/dependencies-manifest.json").read_text(encoding="utf-8"))
    if len(manifest.get("vendoredDependencies", [])) < 8:
        raise AssertionError("dependencies-manifest.json missing vendoredDependencies entries")


def check_pal0_portability_boundaries(sources_dir: Path) -> None:
    inc_rx = re.compile(r'^\s*#\s*include\s*([<"])([^>"]+)[>"]')
    all_files = {
        p.relative_to(sources_dir).as_posix().lower(): p.relative_to(sources_dir).as_posix()
        for p in sources_dir.rglob("*")
        if p.is_file()
    }
    excluded_prefixes = ("third_party/", "ode/", "opcode/")
    for ext in ("*.cpp", "*.c", "*.h", "*.hpp", "*.inl"):
        for p in sorted(sources_dir.rglob(ext)):
            rel = p.relative_to(sources_dir).as_posix()
            if rel.startswith(excluded_prefixes):
                continue
            text = p.read_text(encoding="utf-8", errors="replace")
            for lineno, line in enumerate(text.splitlines(), 1):
                m = inc_rx.match(line)
                if not m:
                    continue
                delim, inc = m.groups()
                if "\\" in inc:
                    raise AssertionError(f"Backslash #include directive in {rel}:{lineno}: {inc}")
                if delim == '"' or inc.startswith(("xray/", "cs/", "fastdelegate/")):
                    try:
                        cand = (p.parent / inc).resolve().relative_to(sources_dir.resolve()).as_posix()
                    except Exception:
                        cand = None
                    if cand and cand.lower() in all_files:
                        actual = all_files[cand.lower()]
                        if not actual.endswith(inc) and inc.lower() == actual[-len(inc) :].lower():
                            raise AssertionError(f"Case-mismatch #include in {rel}:{lineno}: {inc} (expected {actual})")
                    elif inc.lower() in all_files:
                        actual = all_files[inc.lower()]
                        if actual != inc:
                            raise AssertionError(f"Case-mismatch #include in {rel}:{lineno}: {inc} (expected {actual})")

    type_ext = (sources_dir / "xray/type_extensions.h").read_text(encoding="utf-8")
    if "#include <cstdint>" not in type_ext or "namespace std" in type_ext:
        raise AssertionError("xray/type_extensions.h must use <cstdint> without redefining types in namespace std")

    macro_plat = (sources_dir / "xray/macro_platform.h").read_text(encoding="utf-8")
    for req in ("XRAY_PLATFORM_LINUX", "XRAY_PLATFORM_POSIX"):
        if req not in macro_plat:
            raise AssertionError(f"xray/macro_platform.h missing '{req}'")

    thr_fn = (sources_dir / "xray/threading_functions.h").read_text(encoding="utf-8")
    if re.search(r"typedef\s+long\s+atomic32_value_type", thr_fn):
        raise AssertionError("xray/threading_functions.h must not define atomic32_value_type as 'long' (LP64 hazard)")


def check_pal1_core_os_boundaries(sources_dir: Path) -> None:
    thr_fn = (sources_dir / "xray/threading_functions.h").read_text(encoding="utf-8")
    if "threading_functions_inline.h" not in thr_fn:
        raise AssertionError("xray/threading_functions.h must include threading_functions_inline.h")
    if re.search(r"\bMemoryBarrier\b", thr_fn):
        raise AssertionError("xray/threading_functions.h must not reference Win32 MemoryBarrier")

    for rel in ("xray/os_platform.h", "xray/crt_compat.h"):
        path = sources_dir / rel
        if not path.is_file():
            raise AssertionError(f"Missing PAL-1 header: {rel}")
        text = path.read_text(encoding="utf-8", errors="ignore")
        if re.search(r"#\s*include\s*[<\"](?:windows\.h|xray/os_include\.h)[>\"]", text, re.IGNORECASE):
            raise AssertionError(f"PAL-1 portable header must not include Win32/os_include.h: {rel}")

    forbidden_win_include_files = (
        "xray/logging/sources/log_file.cpp",
        "xray/core/sources/threading_functions.cpp",
        "xray/core/sources/resources_manager_user_thread.cpp",
        "xray/core/sources/os_functions.cpp",
        "xray/core/sources/memory_video.cpp",
        "xray/engine/sources/engine_world_sound.cpp",
        "xray/render/vulkan/sources/vulkan_smoke_geometry.cpp",
        "xray/render/vulkan/sources/vulkan_smoke_materials.cpp",
    )
    win_inc_re = re.compile(r"#\s*include\s*[<\"](?:windows\.h|xray/os_include\.h|objbase\.h)[>\"]", re.IGNORECASE)
    for rel in forbidden_win_include_files:
        text = (sources_dir / rel).read_text(encoding="utf-8", errors="ignore")
        if win_inc_re.search(text):
            raise AssertionError(f"PAL-1 cleaned file must not include Windows/COM headers directly: {rel}")


def check_pal2_fs_logging_boundaries(sources_dir: Path) -> None:
    required_files = (
        "xray/fs/native_hdd_file_system.h",
        "xray/fs/posix_hdd_file_system.h",
        "xray/fs/case_insensitive_path.h",
        "xray/fs/sources/posix_hdd_file_system.cpp",
        "xray/fs/sources/posix_hdd_file_system_iteration.cpp",
        "xray/fs/sources/path_string_utils_posix.cpp",
        "xray/fs/sources/folder_watcher_posix.cpp",
        "xray/core/sources/fs_file_system_init.cpp",
        "xray/core/sources/fs_utils_posix.cpp",
        "xray/core/sources/fs_path_posix.cpp",
        "xray/core/sources/fs_file_system_impl_mount_disk_posix.cpp",
        "xray/logging/sources/logging_posix.cpp",
        "xray/logging/sources/helper_posix.cpp",
    )
    for rel in required_files:
        if not (sources_dir / rel).is_file():
            raise AssertionError(f"Missing PAL-2 file: {rel}")

    folder_watcher_h = (sources_dir / "xray/fs/folder_watcher.h").read_text(encoding="utf-8")
    if "#if XRAY_PLATFORM_WINDOWS" not in folder_watcher_h:
        raise AssertionError("xray/fs/folder_watcher.h must gate Windows includes/members under XRAY_PLATFORM_WINDOWS")

    no_win_hdd_headers = (
        "xray/fs/simple_asynchronous_interface.h",
        "xray/fs/simple_synchronous_interface.h",
        "xray/engine/sources/engine_world.h",
        "xray/vfs/sources/test.cpp",
    )
    for rel in no_win_hdd_headers:
        text = (sources_dir / rel).read_text(encoding="utf-8", errors="ignore")
        if "windows_hdd_file_system.h" in text:
            raise AssertionError(f"PAL-2 file must use native_hdd_file_system.h instead of windows_hdd_file_system.h: {rel}")

    dev_mgr = (sources_dir / "xray/core/sources/resources_device_manager.cpp").read_text(encoding="utf-8", errors="ignore")
    if re.search(r"#\s*include\s*[<\"](?:windows\.h|xray/os_include\.h)[>\"]", dev_mgr, re.IGNORECASE):
        raise AssertionError("xray/core/sources/resources_device_manager.cpp must not include Windows headers")


def check_pal3_debug_boundaries(sources_dir: Path) -> None:
    required_files = (
        "xray/debug/sources/bugtrap_stub.cpp",
        "xray/debug/sources/call_stack_posix.cpp",
        "xray/debug/sources/debug_posix.cpp",
        "xray/debug/sources/initialize_posix.cpp",
        "xray/debug/sources/unhandled_posix.cpp",
        "tests/test_diagnostics_pal3_contract.cpp",
    )
    for rel in required_files:
        if not (sources_dir / rel).is_file():
            raise AssertionError(f"Missing PAL-3 file: {rel}")

    dbg_cmake = (sources_dir / "xray/debug/CMakeLists.txt").read_text(encoding="utf-8")
    if "DEBUG_LINUX_SOURCES" not in dbg_cmake:
        raise AssertionError("xray/debug/CMakeLists.txt must define DEBUG_LINUX_SOURCES")

    dbg_h = (sources_dir / "xray/debug/debug.h").read_text(encoding="utf-8")
    for req in ("write_crash_report", "scoped_fault_tracer", "format_system_error", "show_error_dialog", "debug_break"):
        if req not in dbg_h:
            raise AssertionError(f"xray/debug/debug.h missing PAL-3 symbol '{req}'")

    eng_fs = (sources_dir / "xray/engine/sources/engine_world_file_system_device.cpp").read_text(encoding="utf-8")
    if "scoped_fault_tracer" not in eng_fs:
        raise AssertionError("engine_world_file_system_device.cpp must use xray::debug::scoped_fault_tracer")
    for forbidden in ("AddVectoredExceptionHandler", "RemoveVectoredExceptionHandler", "CaptureStackBackTrace"):
        if forbidden in eng_fs:
            raise AssertionError(f"engine_world_file_system_device.cpp must not call Win32 '{forbidden}' directly")

    core_dbg_eng = (sources_dir / "xray/core/sources/core_debug_engine.cpp").read_text(encoding="utf-8")
    if re.search(r"#\s*include\s*[<\"](?:windows\.h|xray/os_include\.h)[>\"]", core_dbg_eng, re.IGNORECASE):
        raise AssertionError("xray/core/sources/core_debug_engine.cpp must not include Windows headers")


def check_pal4_surface_boundaries(sources_dir: Path) -> None:
    required_files = (
        "xray/platform_surface.h",
        "xray/engine/capture_control_bus.h",
        "xray/input/sources/input_world_sdl3.cpp",
        "xray/input/sources/receiver_keyboard_sdl3.cpp",
        "xray/input/sources/receiver_mouse_sdl3.cpp",
        "xray/input/sources/receiver_gamepad_sdl3.cpp",
        "xray/engine/sources/engine_world_platform.cpp",
        "xray/stalker2/pc/sources/stalker2_pc_application_posix.cpp",
    )
    for rel in required_files:
        if not (sources_dir / rel).is_file():
            raise AssertionError(f"Missing PAL-4 file: {rel}")

    hwnd_re = re.compile(r"\bHWND\b")
    hwnd_free_headers = [
        sources_dir / "xray/platform_surface.h",
        sources_dir / "xray/engine/engine_user.h",
        sources_dir / "xray/engine/capture_control_bus.h",
        sources_dir / "xray/engine/sources/engine_world.h",
        sources_dir / "xray/input/api.h",
        sources_dir / "xray/input/sources/input_world.h",
        sources_dir / "xray/input/sources/receiver_keyboard.h",
        sources_dir / "xray/input/sources/receiver_mouse.h",
        sources_dir / "xray/render/vulkan/platform_surface.h",
        sources_dir / "xray/render/vulkan/scene_resources.h",
        sources_dir / "xray/render/vulkan/sources/vulkan_backend.h",
        sources_dir / "xray/render/vulkan/sources/vulkan_presentation_output.h",
        sources_dir / "xray/render/vulkan/sources/vulkan_scene_runtime.h",
        sources_dir / "xray/render/facade/engine_renderer.h",
        sources_dir / "xray/render/facade/platform.h",
        sources_dir / "xray/render/facade/control_level.h",
        sources_dir / "xray/render/core/sources/rhi/rhi_backend.h",
        sources_dir / "xray/render/core/sources/rhi/rhi_device.h",
        sources_dir / "xray/render/engine/world_dx11.h",
        sources_dir / "xray/editor/world/world.h",
        sources_dir / "xray/stalker2/pc/sources/stalker2_pc_application.h",
    ]
    hwnd_free_headers.extend(sorted((sources_dir / "xray/game").rglob("*.h")))
    for header in hwnd_free_headers:
        text = header.read_text(encoding="utf-8", errors="ignore")
        if hwnd_re.search(text):
            raise AssertionError(f"PAL-4 interface header must not reference HWND: {header}")

    vk_backend = (sources_dir / "xray/render/vulkan/sources/vulkan_backend.cpp").read_text(encoding="utf-8")
    if "vkCreateWin32SurfaceKHR" in vk_backend:
        raise AssertionError("vulkan_backend.cpp must delegate surface creation to surface_adapter")

    prop_re = re.compile(r"\b(?:GetPropA|SetPropA|RemovePropA)\b")
    for rel in (
        "xray/render/vulkan/sources/vulkan_backend.cpp",
        "xray/render/facade/control_level.h",
        "xray/engine/sources/engine_world_platform.cpp",
        "xray/engine/sources/m8_output_capture.h",
        "xray/engine/sources/m9_performance_capture.h",
        "xray/engine/sources/engine_world_editor.cpp",
    ):
        text = (sources_dir / rel).read_text(encoding="utf-8", errors="ignore")
        if prop_re.search(text):
            raise AssertionError(f"PAL-4 file must use capture_control_bus instead of Win32 window properties: {rel}")

    input_cmake = (sources_dir / "xray/input/CMakeLists.txt").read_text(encoding="utf-8")
    if "input_world_sdl3.cpp" not in input_cmake:
        raise AssertionError("xray/input/CMakeLists.txt must list input_world_sdl3.cpp")


def check_pal5_shader_compiler_boundaries(sources_dir: Path) -> None:
    required_files = (
        "xray/render/core/sources/rhi/dxc_abi.h",
        "xray/render/core/sources/rhi/shader_disk_cache.h",
        "xray/render/core/sources/rhi/shader_disk_cache.cpp",
        "tests/test_shader_compiler_pal5_contract.cpp",
    )
    for rel in required_files:
        if not (sources_dir / rel).is_file():
            raise AssertionError(f"Missing PAL-5 file: {rel}")

    forbidden = re.compile(
        r"(?:#\s*include\s*[<\"](?:windows\.h|wrl/client\.h|unknwn\.h|dxcapi\.h)[>\"]|"
        r"\b(?:MultiByteToWideChar|WideCharToMultiByte|LoadLibraryExW|GetModuleFileNameW|ComPtr)\b)"
    )
    for rel in (
        "xray/render/core/sources/rhi/shader_compiler.h",
        "xray/render/core/sources/rhi/shader_compiler.cpp",
        "xray/render/core/sources/rhi/shader_disk_cache.h",
        "xray/render/core/sources/rhi/shader_disk_cache.cpp",
        "xray/render/core/sources/rhi/dxc_abi.h",
    ):
        text = (sources_dir / rel).read_text(encoding="utf-8")
        if forbidden.search(text):
            raise AssertionError(f"PAL-5 file must not contain Win32/WRL/dxcapi dependencies: {rel}")

    compiler_cpp = (sources_dir / "xray/render/core/sources/rhi/shader_compiler.cpp").read_text(encoding="utf-8")
    for req in ("i_shader_compiler_backend", "xray::os::dynamic_library", "xray::os::run_process"):
        if req not in compiler_cpp:
            raise AssertionError(f"shader_compiler.cpp missing PAL-5 abstraction '{req}'")

    vk_cmake = (sources_dir / "xray/render/vulkan/CMakeLists.txt").read_text(encoding="utf-8")
    if "shader_disk_cache.cpp" not in vk_cmake:
        raise AssertionError("xray/render/vulkan/CMakeLists.txt must list shader_disk_cache.cpp")


def check_pal6_audio_network_boundaries(sources_dir: Path) -> None:
    required_files = (
        "xray/sound/sources/audio_backend.h",
        "xray/sound/sources/audio_backend_sdl3.cpp",
        "xray/sound/sources/audio_backend_xaudio2.cpp",
        "xray/network/sources/platform_initializer_posix.cpp",
        "tests/test_audio_backend_contract.cpp",
    )
    for rel in required_files:
        if not (sources_dir / rel).is_file():
            raise AssertionError(f"Missing PAL-6 file: {rel}")

    forbidden_sound = re.compile(
        r"(?:\b(?:IXAudio2\w*|XAUDIO2_BUFFER|WAVEFORMATEX|__stdcall|CreateProcess|WaitForSingleObject|CoInitializeEx)\b|<xaudio2\.h>)"
    )
    for rel in (
        "xray/sound/sources/voice_bridge.h",
        "xray/sound/sources/voice_bridge.cpp",
        "xray/sound/sources/sound_world.h",
        "xray/sound/sources/sound_world.cpp",
        "xray/sound/sources/sound_buffer.h",
        "xray/sound/sources/sound_buffer.cpp",
        "xray/sound/sources/sound_scene.h",
        "xray/sound/sources/sound_scene.cpp",
        "xray/sound/sources/sound_stream.h",
        "xray/sound/sources/sound_item.h",
        "xray/sound/sources/sound_object.h",
        "xray/sound/sources/ogg_utils.cpp",
    ):
        text = (sources_dir / rel).read_text(encoding="utf-8", errors="ignore")
        if forbidden_sound.search(text):
            raise AssertionError(f"PAL-6 sound file must not reference XAudio2/Win32 API directly: {rel}")

    sdk_core = (sources_dir / "xray/editor/core/SdkSoundCore.cs").read_text(encoding="utf-8")
    for forbidden in ("DllImport", "winmm.dll", "waveOutGetNumDevs"):
        if forbidden in sdk_core:
            raise AssertionError(f"xray/editor/core/SdkSoundCore.cs must not reference '{forbidden}'")
    if "ISdkAudioBackendProbe" not in sdk_core:
        raise AssertionError("xray/editor/core/SdkSoundCore.cs must define ISdkAudioBackendProbe")

    sound_cmake = (sources_dir / "xray/sound/CMakeLists.txt").read_text(encoding="utf-8")
    for req in ("XRAY_AUDIO_BACKEND", "audio_backend_sdl3.cpp", "audio_backend_xaudio2.cpp"):
        if req not in sound_cmake:
            raise AssertionError(f"xray/sound/CMakeLists.txt missing PAL-6 entry '{req}'")


def check_pal7_editor_boundaries(repo_root: Path) -> None:
    sources_dir = repo_root / "sources"
    root_cmake = (sources_dir / "CMakeLists.txt").read_text(encoding="utf-8")
    if "if(WIN32 AND XRAY_BUILD_EDITOR_HOST)" in root_cmake:
        raise AssertionError("sources/CMakeLists.txt must not gate XRAY_BUILD_EDITOR_HOST under WIN32")

    required_files = (
        "xray/editor/avalonia_controls/Controls/Viewport/LinuxX11NativeSurfaceHostBackend.cs",
        "xray/editor/avalonia_controls/Controls/Viewport/LinuxWaylandNativeSurfaceHostBackend.cs",
        "xray/editor/avalonia_controls/Controls/Viewport/AvaloniaOffscreenViewportHost.cs",
        "xray/editor/base/PlatformWindowService.cs",
    )
    for rel in required_files:
        if not (sources_dir / rel).is_file():
            raise AssertionError(f"Missing PAL-7 file: {rel}")

    for cs_file in sorted((sources_dir / "xray/editor/core").rglob("*.cs")):
        cs_text = cs_file.read_text(encoding="utf-8")
        if "[DllImport" in cs_text or "[LibraryImport" in cs_text:
            raise AssertionError(f"xray/editor/core must not contain P/Invoke attributes: {cs_file}")

    host_bridge = (sources_dir / "xray/editor/world/host_bridge.h").read_text(encoding="utf-8")
    for req in ("abi_version=3", "struct surface_descriptor", "struct window_control_request"):
        if req not in host_bridge:
            raise AssertionError(f"xray/editor/world/host_bridge.h missing PAL-7 contract '{req}'")

    forbidden_world = re.compile(
        r"\b(?:LoadLibraryW|GetProcAddress|GetEnvironmentVariableA|GetEnvironmentVariableW|SetWindowPos|ShowWindow|SendMessage|IsWindow|SetLayeredWindowAttributes|from_win32_hwnd|as_win32_hwnd_raw)\b"
    )
    for rel in (
        "xray/editor/world/sources/managed_host.cpp",
        "xray/editor/world/sources/editor_entry_point.cpp",
        "xray/editor/world/sources/sdk_outputs.h",
    ):
        text = (sources_dir / rel).read_text(encoding="utf-8")
        if forbidden_world.search(text):
            raise AssertionError(f"PAL-7 editor world file must not call Win32 APIs directly: {rel}")


def check_pal8_portability_gates(repo_root: Path) -> None:
    sources_dir = repo_root / "sources"
    win_inc_re = re.compile(r"#\s*include\s*[<\"](?:windows\.h|xray/os_include\.h)[>\"]", re.IGNORECASE)
    win_guard_re = re.compile(
        r"(?:#\s*if\s+(?:XRAY_PLATFORM_WINDOWS|defined\s*\(\s*(?:_WIN32|XRAY_PLATFORM_WINDOWS)\s*\))|"
        r"#\s*ifdef\s+_WIN32|#\s*if\s+defined\s*\(\s*XRAY_AUDIO_BACKEND_XAUDIO2\s*\))"
    )
    allowed_win_files = {
        "xray/os_include.h",
        "cs/core/os_include.h",
        "xray/core/sources/delayhlp.cpp",
        "cs/core/sources/delayhlp.cpp",
        "xray/core/sources/fs_folder_watcher_win.h",
        "xray/core/sources/fs_impl_mount_disk_win.cpp",
        "xray/fs/sources/windows_hdd_file_system.cpp",
        "xray/editor/world/sources/property_editor_file_name.cpp",
        "xray/engine/pc/sources/engine_pc_entry_point.cpp",
        "xray/input/sources/direct_input_include.h",
        "xray/vfs/sources/test.cpp",
    }

    for base in ("xray", "cs"):
        for ext in ("*.h", "*.hpp", "*.inl", "*.c", "*.cpp"):
            for path in sorted((sources_dir / base).rglob(ext)):
                rel = path.relative_to(sources_dir).as_posix()
                stem = path.stem.lower()
                if (
                    rel in allowed_win_files
                    or "/platform/win/" in f"/{rel}"
                    or stem.endswith(("_win", "_win_shared", "_win32", "_xaudio2"))
                ):
                    continue
                text = path.read_text(encoding="utf-8", errors="ignore")
                if win_inc_re.search(text) and not win_guard_re.search(text):
                    raise AssertionError(
                        f"PAL-8 portability gate failed: unguarded <windows.h>/<xray/os_include.h> in {rel}"
                    )

    x64_test = (sources_dir / "tests/test_platform_x64_contract.cpp")
    if not x64_test.is_file():
        raise AssertionError("Missing PAL-8 contract test: tests/test_platform_x64_contract.cpp")
    x64_text = x64_test.read_text(encoding="utf-8")
    for req in ("atomic32_value_type", "mutex_tasks_unaware", "dynamic_library", "XRAY_PLATFORM_LINUX"):
        if req not in x64_text:
            raise AssertionError(f"tests/test_platform_x64_contract.cpp missing '{req}'")

    packager_text = (sources_dir / "tools/sdk/package-sdk-runtime.py").read_text(encoding="utf-8")
    for req in (
        "linux-x64",
        "launch-sdk.sh",
        "readelf",
        "$ORIGIN",
        "libwayland-client.so.",
        "libwayland-egl.so.",
        "libxkbcommon.so.",
    ):
        if req not in packager_text:
            raise AssertionError(f"tools/sdk/package-sdk-runtime.py missing PAL-8 Linux support '{req}'")
    if "SDL_VIDEODRIVER=x11" in packager_text:
        raise AssertionError("tools/sdk/package-sdk-runtime.py must not hardcode SDL_VIDEODRIVER=x11")

    baseline_text = (sources_dir / "tools/ci/collect_build_baseline.py").read_text(encoding="utf-8")
    if "readelf" not in baseline_text or "elfImports" not in baseline_text:
        raise AssertionError("tools/ci/collect_build_baseline.py missing Linux ELF support")

    manifest_data = json.loads((sources_dir / "tools/ci/toolchain-manifest.json").read_text(encoding="utf-8"))
    if manifest_data.get("linuxPlatform", {}).get("platform") != "linux-x64":
        raise AssertionError("tools/ci/toolchain-manifest.json missing linuxPlatform section")

    ci_yml = (repo_root / ".github/workflows/modernization-ci.yml").read_text(encoding="utf-8")
    for req in ("cpu-linux-x64", "vulkan-wayland-linux-x64", "weston", "lvp_icd"):
        if req not in ci_yml:
            raise AssertionError(f".github/workflows/modernization-ci.yml missing PAL-8 Linux CI entry '{req}'")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo-root", required=True)
    args = parser.parse_args()
    repo_root = Path(args.repo_root).resolve()

    check_no_bundled_lua(repo_root / "sources")
    check_root_cmake(repo_root / "sources/CMakeLists.txt")
    check_dotnet_alignment(repo_root)
    check_presets(repo_root / "sources/CMakePresets.json")
    check_toolchain_manifest(repo_root / "sources/tools/ci/toolchain-manifest.json")
    check_stage04_modernized_boundaries(repo_root / "sources")
    check_stage05_modernized_boundaries(repo_root / "sources")
    check_stage06_modernized_boundaries(repo_root)
    check_stage07_modernized_boundaries(repo_root / "sources")
    check_stage08_modernized_boundaries(repo_root)
    check_pal0_portability_boundaries(repo_root / "sources")
    check_pal1_core_os_boundaries(repo_root / "sources")
    check_pal2_fs_logging_boundaries(repo_root / "sources")
    check_pal3_debug_boundaries(repo_root / "sources")
    check_pal4_surface_boundaries(repo_root / "sources")
    check_pal5_shader_compiler_boundaries(repo_root / "sources")
    check_pal6_audio_network_boundaries(repo_root / "sources")
    check_pal7_editor_boundaries(repo_root)
    check_pal8_portability_gates(repo_root)
    print("PASS: xray_source_contract")


if __name__ == "__main__":
    main()
