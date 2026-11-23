# DCC-independent animation mathematics

`xray_animation_math` is a header-only CMake target and the canonical home of the
portable cubic B-spline fitter. It uses only the C++ standard library. Both the
standalone asset cooker and `xray_animation` link this target; neither requires
Maya, Blender, SISL, engine allocator globals or a DCC world to use the fitter.

The original fitter implementation moved from `maya_cook/sources/bspline_fit.hpp`
without arithmetic changes. That old include remains a compatibility adapter.
Before/after comparisons for five asset kinds in both v1 and v2 produced 64
byte-identical raw/converted output files.

## Sample and fitting API

`discrete_data.hpp` provides:

- `discrete_data`: value-owned per-bone arrays of nine TRS sample channels,
  per-channel precisions, a sample interval and per-bone start times.
- `fit_track` / `fit_animation`: the shared fitter plus an explicit check that
  the stored result meets the requested precision at every input sample.
- `compare_samples`: a callback-based validator reporting count mismatches,
  non-finite output and the worst bone/channel/frame error. It compares signed
  values, so opposite-sign values cannot pass by matching magnitudes.

All input transforms are already in engine coordinates. Time is in engine
30-fps frames: `sample_interval = 30 / source_fps`, and a bone's first sample is
at its `start_time`. Dimensionless scale and additive TRS are treated as numeric
channels; coordinate conversion, root-relative transforms and additive-base
construction remain frontend/cooker responsibilities.

`legacy_check_data.hpp` decodes the original `.clip_check_data` structure through
a small configuration-value interface. It supports both the mixed table with
nine numeric channels plus named `start_time`, and the older named `data` table.
It validates dimensions, finite values, positive precision/fps and consistency
with the header's sample frequency. All 346 check-data files in the original
corpus have been decoded and validated. This is a loading check; it does not
claim that B4/B5 migration or corpus-wide pose reconciliation has run.

## Engine integration

`xray/animation/track_processing.h` exposes the native engine adapter through the
`xray_animation` CMake target:

- `process_track(samples, names, output, is_additive)` fits sampled tracks into
  `bi_spline_skeleton_animation`. It replaces existing points, retains clip type,
  validates engine bone-name limits and builds the CRC lookup index.
- `check_animation_samples(samples, cubic_animation)` evaluates the actual engine
  curves and returns a structured error report.
- `read_check_data(lua_config)` adapts engine Lua configuration values to the
  same neutral decoder, without creating a Maya world.

The previously disabled `track_process.cpp` now builds this adapter. The
`check_animation_b_spline_approximation` diagnostic uses real sample checking;
missing, malformed or mismatched check data fails the resource query instead of
being ignored. The compatibility diagnostic retains its historical 2x precision
allowance; the new native validation API defaults to 1x.

The legacy channel-document source also consumes the neutral decoder. Its old
C++/CLI UI remains outside the modern SDK build; the SDK-8 editor UI and tools
are separate work. This stage supplies the compiled fitting/validation API.

The `xray/maya_animation` directory remains an archive of the 2011 Maya/SISL
implementation for the legacy plugin sources. Active engine animation code and
the channel-document consumer no longer include or link that world API.

## Verification

- `xray_animation_math_unit`: fitting at nonzero offsets and non-unit intervals,
  all TRS channels, constant/single-frame curves, invalid dimensions, signed
  errors and non-finite evaluator output. Runs in standalone builds without any
  engine or DCC host.
- `xray_animation_math_source_contract`: prevents DCC/engine dependencies in the
  mathematical headers and retired Maya-world references in engine consumers.
- `xray_animation_math_engine_contract`: sample data -> shared fitting -> actual
  engine cubic playback -> precision check; repeated processing, additive type,
  CRC bone lookup, corrupted sample diagnostics and both legacy table layouts.

To re-run the read-only corpus decode check after building the engine contract:

```powershell
& build/sdk-native-20261108-debug/binaries/xray_animation_math_engine_contract_tests.exe --corpus C:/Stalker2_2011
```

Stage logs and the byte regression fingerprints are in `build/blender-b1-math/`.
