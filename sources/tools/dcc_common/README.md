# Shared X-Ray DCC core

This package has no `maya` or `bpy` imports. It contains exchange v1/v2
writers/validators, cooker discovery/subprocess bridge, logical resource naming,
and authoring constants. Maya's original module paths remain compatibility
adapters; its resource `optionVar` handling remains in the Maya adapter.

Keep `dcc_common` alongside `maya` when copying `sources/tools` to an SDK. The
adapters resolve this sibling package when Maya loads a plugin directly.

The preferred executable is `xray_asset_cook_cli`. `xray_maya_cook_cli` remains a
real executable using the same source and library. Set `XRAY_ASSET_COOK_CLI` to
select the neutral CLI; `XRAY_MAYA_COOK_CLI` is still accepted. An explicit path
overrides both, and an existing neutral environment path takes precedence over
the legacy environment path.

Use `DccExchangeBundleWriter(..., source_dcc="blender")` to write
`xray-dcc-exchange/v2`. `ExchangeBundleWriter` keeps its v1 default for existing
callers. [SPEC_V2.md](SPEC_V2.md) defines coordinates, source provenance, sampled
TRS and optional editable curves. Both C++ CLI names read both versions and
validate the v2 metadata; only the sampled data affects cooked output.

The v1 raw and converted outputs have been compared byte-for-byte with a saved
pre-v2 cooker for all five asset kinds. Equivalent v2 Y-up aliases and engine
space are also covered by byte-parity tests. Blender rotation conversion is
checked against real `mathutils` and `xray::math::create_rotation`.

The C++ fitter and sampled-animation APIs now live in
[`xray/animation_math`](../../xray/animation_math/README.md), shared by the cooker
and engine track processing. B1 no longer requires the retired Maya math world.

Maya 2.1 now uses the shared v2 writer directly for every scene exporter and
collision fitting. [Live cross-DCC parity](PARITY.md) covers equivalent Maya,
Blender legacy Y-up and Blender native Z-up scenes, all five asset types,
standard/additive clips, v1 header compatibility and engine playback/loaders.

B4 adds `legacy_codec.py` (safe Lua/mixed-table and checked binary readers,
VFS/model/skeleton/clip/physics decompile), `ma_reader.py` (basic MA data parser)
and independent matrix/curve math. The old Maya `reference_codec` path re-exports
these APIs with v1 defaults. `decompile_asset` writes explicit v2 provenance.
Adjacent clip check data is authoritative; spline knots seed editable import.
See [the Blender import contract](../blender/IMPORT.md) and the saved Maya oracle
in `fixtures/`. These modules have no DCC imports and ship inside the extension.
