"""Cook the original test range assets into an isolated copy of the SDK asset pack."""

import argparse
import math
import shutil
import struct
import subprocess
import sys
import tempfile
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(REPO / "sources/tools"))
from dcc_common import legacy_codec
from dcc_common.exchange import ExchangeBundleWriter


MODELS = (
    "props/shooting_gallery/target_color",
    "props/shooting_gallery/target_bw",
    "props/shooting_gallery/canister_yellow",
    "generic/construction/concrete_block_simple",
    "item/weapon/ak74/ak74_4tir1",
)

# Resolve the legacy material instances to the inputs used by the Vulkan g-buffer.
# Their original texture paths are kept in the standalone definitions.
MATERIALS = {
    "props/military/target_color": ("props/military/target_color", None, None),
    "props/military/target_bw": ("props/military/target_bw", None, None),
    "props/garage/canister_yellow": ("props/garage/canister_yellow", "props/garage/canister_nmap", "props/garage/canister_spec"),
    "props/concrete_block_simple/block_simple": (
        "props/concrete_block_simple/concrete_block_simple",
        "props/concrete_block_simple/concrete_block_simple_nmap",
        "props/concrete_block_simple/concrete_block_simple_spec",
    ),
    "test_range/sleeves": ("character/human/neutral/neutral_01/neutral_torso", None, None),
}
MATERIALS.update({
    f"test_object/test_char_new_weapon_{i}": (
        f"item/weapon/ak74/ak74_{i:02}",
        f"item/weapon/ak74/ak74_{i:02}_nmap",
        f"item/weapon/ak74/ak74_{i:02}_spec",
    ) for i in range(1, 7)
})


def prepare_materials(original, assets):
    for logical, (diffuse, normal, specular) in MATERIALS.items():
        for texture in (diffuse, normal, specular):
            if texture is None:
                continue
            source = original / "sources/textures" / Path(texture)
            destination = assets / "resources/textures" / Path(texture)
            destination.parent.mkdir(parents=True, exist_ok=True)
            for extension in (".tga", ".options"):
                shutil.copy2(source.with_suffix(extension), destination.with_suffix(extension))

        effect = [
            'effect_id = "g_stage_default"',
            "constant_diffuse = float4(1.0, 1.0, 1.0, 1.0)",
            "use_tdiffuse = true",
            f'texture_diffuse = "{diffuse}"',
        ]
        if normal:
            effect.extend(("use_nmap = true", f'texture_normal = "{normal}"'))
        if specular:
            effect.extend(("use_tspecular_inensity = true", f'texture_specular_intensity = "{specular}"'))
        if logical.startswith("props/military/"):
            effect.extend(("two_sided = true", "use_alpha_test = true", "alpha_ref = 0.4"))
        is_held = logical == "test_range/sleeves" or logical.startswith("test_object/test_char_new_weapon_")
        material = assets / "resources/material_instances" / (logical + ".material")
        material.parent.mkdir(parents=True, exist_ok=True)
        shadow = "is_cast_shadow = false, " if is_held else ""
        material.write_text("material = { g_stage = { " + shadow + "effect = {\n    "
                            + ",\n    ".join(effect) + "\n} } }\n", encoding="utf-8")


def basis(forward):
    def unit(v):
        return tuple(x / math.hypot(*v) for x in v)

    forward = unit(forward)
    right = unit((forward[2], 0.0, -forward[0]))
    up = (forward[1] * right[2], forward[2] * right[0] - forward[0] * right[2], -forward[1] * right[0])
    return right, up, forward


def prepare_sleeves(original, output):
    """Keep the original forearm and hand geometry, posed at the two grips."""
    archive = original / "exported/models/character/human/neutral/neutral_01/neutral_01.skinned_model"
    files = legacy_codec.load_model_files(archive)
    bones = legacy_codec._decode_bind_pose(files["render/bind_pose"], to_maya=False, unit_scale=1.0)
    bone_positions = {bone["name"]: bone["bind_translation"] for bone in bones}
    raw_vertices = files["render/neutral_torso_/vertices"]
    raw_indices = files["render/neutral_torso_/indices"]
    vertices = list(legacy_codec._VERT_SKIN_4W.iter_unpack(raw_vertices[4:]))
    indices = [item[0] for item in struct.iter_unpack("<H", raw_indices[4:])]

    def primary_bone(v):
        weights = (*v[16:19], 1.0 - sum(v[16:19]))
        index = v[max(range(4), key=weights.__getitem__)]
        return bones[index]["name"] if index < len(bones) else ""

    def subtract(a, b):
        return tuple(x - y for x, y in zip(a, b))

    names = [primary_bone(vertex) for vertex in vertices]
    positions, normals, uvs, faces = [], [], [], []
    for side, wrist, elbow in (
        ("Left", (-.065, -.035, .30), (-.27, -.25, -.12)),
        ("Right", (.067, -.08, -.09), (.29, -.28, -.36)),
    ):
        bind_wrist = bone_positions[side + "Hand"]
        old = basis(subtract(bind_wrist, bone_positions[side + "ForeArm"]))
        new = basis(subtract(wrist, elbow))

        def rotate(vector):
            coordinates = [sum(a * b for a, b in zip(vector, axis)) for axis in old]
            return tuple(sum(coordinates[j] * new[j][i] for j in range(3)) for i in range(3))

        valid = {i for i, name in enumerate(names)
                 if name == side + "ForeArm" or name.startswith(side + "Hand")}
        remap = {}
        for start in range(0, len(indices), 3):
            triangle = indices[start:start + 3]
            if any(i not in valid for i in triangle):
                continue
            for i in triangle:
                if i not in remap:
                    remap[i] = len(positions)
                    vertex = vertices[i]
                    positions.append(tuple(a + b for a, b in zip(wrist, rotate(subtract(vertex[4:7], bind_wrist)))))
                    normals.append(rotate(vertex[7:10]))
                    uvs.append(vertex[19:21])
                faces.append(remap[i])
    writer = ExchangeBundleWriter("static_model", "test_range/sleeves", {
        "system": "xray_left_handed_y_up", "unit_scale_to_meters": 1.0,
        "uv_origin": "top_left", "front_face_winding": "cw_xray",
    })
    writer.manifest["surfaces"] = [{
        "name": "sleeves", "shader_name": "sleeves", "mesh_name": "sleeves", "material_name": "test_range/sleeves",
        "texture_name": MATERIALS["test_range/sleeves"][0], "vertex_count": len(positions), "index_count": len(faces),
        "streams": {"positions": writer.append_span("f32x3", positions), "normals": writer.append_span("f32x3", normals),
                    "uvs": writer.append_span("f32x2", uvs), "indices": writer.append_span("u32", faces)},
    }]
    writer.write(output)


def prepare_shot_flash(original, assets, bundle):
    texture = "fx/pfx/glow_fire"
    material = "test_range/shot_flash"
    source = original / "sources/textures" / texture
    destination = assets / "resources/textures" / texture
    destination.parent.mkdir(parents=True, exist_ok=True)
    for extension in (".tga", ".options"):
        shutil.copy2(source.with_suffix(extension), destination.with_suffix(extension))

    definition = assets / "resources/material_instances" / (material + ".material")
    definition.parent.mkdir(parents=True, exist_ok=True)
    definition.write_text(f"""material = {{ forward = {{ effect = {{
    effect_id = "forward_default",
    blend_mode = 2,
    two_sided = true,
    constant_emissive = float4(1.0, 1.0, 1.0, 1.0),
    constant_emissive_multiplier = 8.0,
    use_temissive = true,
    texture_emissive = "{texture}"
}} }} }}
""", encoding="utf-8")

    writer = ExchangeBundleWriter("static_model", material, {
        "system": "xray_left_handed_y_up", "unit_scale_to_meters": 1.0,
        "uv_origin": "top_left", "front_face_winding": "cw_xray",
    })
    writer.manifest["surfaces"] = [{
        "name": "shot_flash", "shader_name": "shot_flash", "mesh_name": "shot_flash",
        "material_name": material, "texture_name": texture, "vertex_count": 4, "index_count": 6,
        "streams": {
            "positions": writer.append_span("f32x3", [
                (-.085, .11, 0), (.085, .11, 0), (-.085, -.11, 0), (.085, -.11, 0),
            ]),
            "normals": writer.append_span("f32x3", [(0, 0, -1)] * 4),
            "uvs": writer.append_span("f32x2", [(0, 0), (1, 0), (0, 1), (1, 1)]),
            "indices": writer.append_span("u32", [0, 1, 2, 2, 1, 3]),
        },
    }]
    writer.write(bundle)


def prepare_sky(original, assets):
    source = original / "sources/textures/test/sky_20_cube.tga"
    raw = source.read_bytes()
    width, height, bits = struct.unpack_from("<HHB", raw, 12)
    if raw[2] != 2 or width != 6 * height or bits != 24 or height % 256:
        raise ValueError("Unexpected original sky cubemap format")
    step = height // 256
    start = 18 + raw[0]
    header = bytearray(raw[:18])
    header[0] = 0
    struct.pack_into("<HH", header, 12, 6 * 256, 256)
    pixels = bytearray()
    for y in range(0, height, step):
        for x in range(0, width, step):
            offset = start + 3 * (y * width + x)
            pixels.extend(raw[offset:offset + 3])
    target = assets / "resources/textures/test/range_sky.tga"
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_bytes(header + pixels)
    shutil.copy2(source.with_suffix(".options"), target.with_suffix(".options"))


def cook_model(cooker, bundle, assets, relative):
    subprocess.run([str(cooker), "--input", str(bundle),
                    "--output", str(assets / "resources/models" / relative),
                    "--converted-output", str(assets / "resources.converted/models" / relative)],
                   check=True)


def prepare(base, original, cooker, output):
    if output.exists():
        raise FileExistsError(f"Choose a new output directory: {output}")
    if not (base / "scene.json").is_file():
        raise FileNotFoundError(f"Prepared SDK assets are missing: {base}")
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="range-cook-", dir=output.parent) as directory:
        staging = Path(directory)
        assets = staging / "assets"
        shutil.copytree(base, assets)
        exported = original / "exported/models"
        sources = original / "sources"
        for logical in MODELS:
            relative = Path(logical + ".model")
            bundle = staging / relative.stem
            legacy_codec.decode_model_to_exchange(exported / relative, bundle, logical_name=logical,
                                                  coord_system="xray_left_handed_y_up",
                                                  settings_override=sources / "models" / relative / "settings")
            cook_model(cooker, bundle, assets, relative)

        sleeves = staging / "sleeves"
        prepare_sleeves(original, sleeves)
        cook_model(cooker, sleeves, assets, "test_range/sleeves.model")

        flash = staging / "shot_flash"
        prepare_shot_flash(original, assets, flash)
        cook_model(cooker, flash, assets, "test_range/shot_flash.model")

        prepare_materials(original, assets)
        prepare_sky(original, assets)
        assets.rename(output)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base", type=Path, default=REPO / "build/sdk-assets")
    parser.add_argument("--original", type=Path, required=True, help="Original xr2-dsgn/resources directory")
    parser.add_argument("--cooker", type=Path, default=REPO / "build/presets/windows-x64-release/binaries/xray_asset_cook_cli.exe")
    parser.add_argument("--output", type=Path, default=REPO / "build/test-range/assets-shot-fx")
    args = parser.parse_args()
    prepare(args.base.resolve(), args.original.resolve(), args.cooker.resolve(), args.output.resolve())
    print(f"Prepared test range assets in {args.output.resolve()}")
