# Vulkan profiles and evidence

The [active migration plan](../../../../vulkan_integration_plan.md) owns task
status. This index describes implementation scope and where to inspect each
dated result. A report's source/runtime SHA-256 identifies the code it tested;
later reports do not rewrite earlier evidence.

## Renderer and assets

The supported production path is the facade/engine world with one RHI-owned
Vulkan backend, explicit HWND initialization, independent main/preview outputs
and owner-thread cleanup. The integration matrix selects Windows x64, isolated
Debug/Release builds and the GPU/loader/compiler versions recorded in its runtime
receipts. Vulkan 1.4 and the selected presentation capabilities are checked at
startup; failed readiness does not fall through to a legacy renderer.

The [M8 manifest](m8_acceptance_manifest.json) defines the game/editor control
scene. Its [input inventory](m8_acceptance_inputs.json), prepared-pack provenance
and source hashes bind the actual terrain, static/skeletal actors, particles,
vegetation, decals, distortion and SSAO content. The explicit terrain profile
keeps `chaz_mir_part`; its accepted per-cell/per-view budgets remain distinct
from measured Vulkan allocation and driver heap bytes.

[Editor main hosting](m8_editor_host.json), [model preview](m8_editor_preview.json)
and [paired outputs](m8_editor_outputs.json) define their DLL/host lifetime,
independent scene/view/output ownership, resize/minimize/visibility/reopen
workflows and capture oracles. The [capability contract](m8_capabilities.json)
maps required operations to implemented routes and keeps deferred legacy calls
as named errors before queue payloads or GPU allocation.

M5-C uses the user-approved original exported/source static-asset route with
native runtime cooking and a disjoint workspace cache. Its supported material,
archive and Lua bounds are documented in the [implementation README](README.md).
The woodbox acceptance binds original geometry/material/texture inputs and
repeated unload/recook results. It does not claim compatibility with an original
legacy cooked pack, which was unavailable.

GI/LPV, unsupported legacy particle/editor APIs and the other deferred operations
retain the gates in the capability contract. Compiler-cache invalidation and
recovery are tested independently of the deferred legacy world/scene shader
reload entry points. Tree wind/LOD and unsupported original material/geometry
features remain explicit profile errors. This index covers renderer migration;
unrelated sound work is outside its completion criteria.

## Measurement contract

The [M9 performance profile](m9_performance_profile.json) fixes the inspection
camera/content phase, resource arena, warmup/stress durations, background CPU
workers, near-timeout margin and byte/frame/capacity budgets before canonical
runs. It covers game and editor main plus preview in both configurations.
Capture readback and actual synchronization validation stay enabled. Per-output
CPU/GPU distributions and exact application allocation requests are recorded
alongside process commit/working-set peaks and driver heap usage estimates.
The README defines these measurement boundaries and the bounded cache changes.

## Dated evidence

- [M8 capability audit](../../../../build/stage8-capability-audit/summary.json):
  manifest routes, full game/editor/paired workflows, capture oracles, capability
  rejection controls and earlier M8 input/capture identities.
- [M5 original runtime cooking](../../../../build/stage5-original-runtime/summary.json):
  native source/exported cooking, repeated recooks, cache/input identities and
  the unsupported cooked-pack compatibility boundary.
- [M9 build/lifecycle matrix](../../../../build/stage9-matrix/summary.json):
  Debug/Release contracts and lifecycle workflows with isolated binaries.
- [M9 controlled failures](../../../../build/stage9-failures/summary.json):
  startup/creation/allocation/device-loss/facade controls and the historical
  NVIDIA startup incident investigation. Its cause remains unproven; only an
  explicit risk decision or proven cause/fix can close that separate item.
- [M9 instrumentation checkpoint](../../../../build/stage9-performance/summary.json):
  allocation accounting, timestamp/descriptor/capacity controls and complete
  regressions; synthetic controls do not substitute for manifest stress.
- [M9 manifest measurements and final matrix](../../../../build/stage9-performance-manifest/summary.json):
  source/runtime/contract identities, measurement rows, budgets, captures,
  validation, controlled load/deadline coverage and final regression results.
  The report and active plan determine acceptance; the existence of this link
  alone grants no milestone completion.

Accepted roots, captures, caches and binaries remain frozen. Reproduction uses
fresh build/evidence roots; original assets remain read-only.

After final M9 acceptance, the migration test sources and targets were retired
on 2026-10-22 at the user's request. Historical reports retain their original
identities and results. The recovery archive and current build verification
are indexed by `build/vulkan-test-retirement/summary.json`; SDK-specific checks
are planned separately in `sdk_migration_plan.md`.
