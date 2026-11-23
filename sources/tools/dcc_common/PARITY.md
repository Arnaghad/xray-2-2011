# Live Maya / Blender parity (B3-M)

Both frontends write `xray-dcc-exchange/v2`. Maya declares `source_dcc: maya`
and `right_handed_y_up`; Blender declares its actual armature/scene space.
The shared legacy `ExchangeBundleWriter` remains v1 by default.

`xray_dcc_live_parity` is registered in the main engine build when `mayapy` is
available. It runs actual Maya 2027 and pinned Blender 5.0.1, using private Maya
and Blender profiles. Blender installs the built extension ZIP; there is no
fallback import of the extension sources. Neither host reads the original game
corpus. Missing/failing hosts fail the registered test.

The fixture in `parity_scene.py` defines centimeter geometry, per-corner UV seams,
nonuniform mesh transforms, a five-bone branching hierarchy with jointOrient,
five skin influences per vertex, locators, two physics bodies and box/sphere/
cylinder shapes. The animation spans frames 3–14 at 24 fps, includes multiple
authoring Euler orders, animated translation/rotation/scale and a moving local
center. It is authored separately in each host:

- Maya keys its native joint channels and verifies evaluated global matrices
  against the independent fixture math before export.
- Blender creates one legacy Y-up rig with the fixed bone-frame correction and
  one native Z-up rig. It keys pose TRS to the same physical matrices, using the
  real slotted Action API. Native axis conversion runs in the C++ cooker.

The tests compare static, skeleton, skin, standard clip, additive clip with local
center, and physics output for both Blender representations against Maya:

- Mesh triangles are matched independently of vertex/triangle ordering, retaining
  winding, P/N/tangent/binormal/UV and named skin influences. Reversing a triangle
  or changing a weight fails a separate negative comparator test.
- Bind matrices, named hierarchy, skeleton reference, locators, bounds, material
  settings, collision geometry, physics shapes, masses, body links and all six
  DOF dictionaries are checked. DCC helper IDs and mesh provenance paths may
  differ; those are not runtime geometry/pose differences.
- Rotations are compared as matrices. Geometry/config tolerance is `8e-5`
  (rotation matrix components `1.6e-4`). Each clip is fitted at precision 0.005,
  so two independently fitted clips may differ by twice that; each is also
  checked against the fixture at every frame in actual engine cubic playback
  with a 0.0002 floating-point allowance. Different knot counts are allowed.
- Both hosts' cooked binary resources load through engine mesh, skinning, bind
  pose, hierarchy/config, locator and collision loaders. Root/linked physics
  joint ordering and body links are checked in the binary config.
- The actual Maya payloads are recooked with v1 headers. All **34 raw/converted
  resource files** are byte-identical to their v2 equivalents for this fixture.
  This checks header compatibility of the current extraction; it does not assert
  that unrelated authoring fixes preserve every older frontend's bytes.

The fixture covers the stated contracts, not all existing game content. Legacy
asset import, Maya exchange import, decompile, scene migration and reconciliation
with the original corpus are B4/B5 work. The optional Maya exchange importer is
not required for B3-M and has not been added here.

## Reproduce

From the repository root, with the cooker and engine contract executables built:

```powershell
python -X utf8 sources/tools/dcc_common/run_dcc_parity.py --mayapy-exe "C:/Program Files/Autodesk/Maya2027/bin/mayapy.exe" --blender-exe build/blender/5.0.1/blender-5.0.1-windows-x64/blender.exe --cli-exe build/sdk-native-20261108-debug/binaries/xray_asset_cook_cli.exe --contract-exe build/sdk-native-20261108-debug/binaries/xray_maya_cook_engine_contract_tests.exe --playback-exe build/sdk-native-20261108-debug/binaries/xray_animation_math_engine_contract_tests.exe --artifacts build/blender-b3m/smoke
```

`result.json`, host/engine logs, `.ma`/`.blend` scenes, exchange bundles and cooked
resources remain in the artifact folder. Only temporary host profiles are
removed. In CTest, artifacts are isolated per build under `dcc-parity/`.

The physics comparison also identified two frontend conventions that matter to
the engine: slot zero is reserved unlinked root metadata and linked authoring
Rigid is an engine Fixed joint. Blender now puts the root first even when a
limb sorts before it; both frontends normalize linked Rigid without dropping
either body. Maya's existing explicit Fixed-child collision flattening remains.
