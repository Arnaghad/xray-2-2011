"""Verify legacy VFS archive decoding, hard-link traversal, and Lua/binary config parsing."""

from __future__ import annotations

import struct
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO_ROOT / "sources/tools"))

from dcc_common.legacy_codec import parse_config_bytes, parse_lua_config, read_vfs_archive  # noqa: E402


def build_synthetic_archive(with_hardlink: bool = False) -> bytes:
    """Build a minimal little-endian X-Ray VFS archive with optional hard-link node."""
    # Node layout matches legacy_codec.read_vfs_archive:
    # dir prefix: 16B (first_child at -16)
    # file prefix: 24B (length at -24, payload_offset at -16)
    # hardlink prefix: 16B (referenced at -8, flags & 512)
    # body: 51B header (+24 sibling, +32 parent, +48 flags u16, +50 zero u16) + NUL-terminated name
    def align8(n: int) -> int:
        return (n + 7) & ~7

    root_off = 16
    root_end = align8(root_off + 51 + 1)

    dir_off = root_end + 16
    dir_end = align8(dir_off + 51 + len(b"collision\0"))

    file_off = dir_end + 24
    file_end = align8(file_off + 51 + len(b"shape.bin\0"))

    if with_hardlink:
        link_off = file_end + 16
        fat_size = align8(link_off + 51 + len(b"hq_collision\0"))
        node_count = 4
    else:
        link_off = 0
        fat_size = align8(file_end)
        node_count = 3

    payload = b"\xAA\xBB\xCC\xDD"
    data_offset = 24 + fat_size
    buf = bytearray(24 + fat_size + len(payload))
    buf[:16] = b"little-endian\x00\x00\x00"
    struct.pack_into("<II", buf, 16, node_count, fat_size)

    fat_base = 24
    # Root
    struct.pack_into("<Q", buf, fat_base + root_off - 16, dir_off)
    struct.pack_into("<HH", buf, fat_base + root_off + 48, 13, 0)
    buf[fat_base + root_off + 51 : fat_base + root_off + 52] = b"\x00"

    # Dir 'collision'
    struct.pack_into("<Q", buf, fat_base + dir_off - 16, file_off)
    if with_hardlink:
        struct.pack_into("<Q", buf, fat_base + dir_off + 24, link_off)
    struct.pack_into("<Q", buf, fat_base + dir_off + 32, 0)
    struct.pack_into("<HH", buf, fat_base + dir_off + 48, 5, 0)
    buf[fat_base + dir_off + 51 : fat_base + dir_off + 51 + 10] = b"collision\x00"

    # File 'shape.bin'
    struct.pack_into("<I", buf, fat_base + file_off - 24, len(payload))
    struct.pack_into("<Q", buf, fat_base + file_off - 16, data_offset)
    struct.pack_into("<Q", buf, fat_base + file_off + 32, 0)
    struct.pack_into("<HH", buf, fat_base + file_off + 48, 4, 0)
    buf[fat_base + file_off + 51 : fat_base + file_off + 51 + 10] = b"shape.bin\x00"

    if with_hardlink:
        # Hard link 'hq_collision' -> dir_off (flags 512 | 4)
        struct.pack_into("<Q", buf, fat_base + link_off - 8, dir_off)
        struct.pack_into("<Q", buf, fat_base + link_off + 32, 0)
        struct.pack_into("<HH", buf, fat_base + link_off + 48, 512 | 4, 0)
        buf[fat_base + link_off + 51 : fat_base + link_off + 51 + 13] = b"hq_collision\x00"

    buf[data_offset : data_offset + len(payload)] = payload
    return bytes(buf)


def expect_value_error(fn, label: str) -> None:
    try:
        fn()
    except ValueError:
        return
    raise AssertionError(f"Expected ValueError for {label}")


def main() -> None:
    raw = build_synthetic_archive(with_hardlink=True)
    decoded = read_vfs_archive(raw)
    assert decoded["collision/shape.bin"] == b"\xAA\xBB\xCC\xDD"
    assert decoded["hq_collision/shape.bin"] == b"\xAA\xBB\xCC\xDD"

    # Negative archive controls
    expect_value_error(lambda: read_vfs_archive(b"short"), "truncated header")
    expect_value_error(lambda: read_vfs_archive(b"big-endian\x00\x00\x00\x00\x00\x00" + raw[16:]), "bad magic")

    # Unsafe '..' member name
    unsafe = bytearray(build_synthetic_archive(with_hardlink=False))
    idx = unsafe.index(b"shape.bin\x00")
    unsafe[idx : idx + 10] = b"..\x00\x00\x00\x00\x00\x00\x00\x00"
    expect_value_error(lambda: read_vfs_archive(bytes(unsafe)), "unsafe '..' member name")

    # Out-of-bounds payload span
    oob = bytearray(build_synthetic_archive(with_hardlink=False))
    file_node_off = oob.index(b"shape.bin\x00") - 51
    struct.pack_into("<I", oob, file_node_off - 24, 999999)
    expect_value_error(lambda: read_vfs_archive(bytes(oob)), "out-of-bounds payload")

    # Lua config & mixed table parsing
    lua_text = r'''
    settings = {
        position = float3(1.0, 2.5, -3.0),
        start_time = 0.25,
        "chan0",
        "chan1\033",
    }
    '''
    parsed = parse_lua_config(lua_text)
    assert parsed["settings"]["position"] == [1.0, 2.5, -3.0]
    assert parsed["settings"]["start_time"] == 0.25
    assert parsed["settings"][1] == "chan0"
    assert parsed["settings"][2] == "chan1!"
    assert parse_config_bytes(lua_text.encode("utf-8")) == parsed

    print("PASS: xray_vfs_legacy_python_decode")


if __name__ == "__main__":
    main()
