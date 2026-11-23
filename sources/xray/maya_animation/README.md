# Legacy Maya/SISL animation reference

This directory preserves the 2011 implementation used by the archived C++ Maya
plugin sources. It is not part of the current CMake engine or cooker build.

Current animation sampling, B-spline fitting and sample validation use
[`xray/animation_math`](../animation_math/README.md) and the native engine adapter
`xray/animation/track_processing.h`. New code must use those value-owned APIs
instead of `maya_animation::create_world` or the SISL-backed factories here.

The supported Maya Python plugin continues to invoke the separate asset cooker;
it does not load this legacy module. The Blender frontend uses the same cooker
and neutral mathematical implementation.
