# Vulkan Integration Foundation

## Document Status

The [migration plan](../../../../vulkan_integration_plan.md) is the only active
source for task status, dependencies, scope decisions and completion criteria.
This README documents implementation contracts, commands and dated evidence.
Limits, test counts, unsupported gates and "next work" in a dated acceptance
record describe that run; later records may supersede them. They do not reopen
accepted components or create an additional backlog. The
[previous plan](../../../../vulkan_integration_history.md) is archived for
traceability and is not an execution checklist.

## Migration Test Retirement (2026-10-22)

The completed migration's stage suites, fixtures, Python/PowerShell runners,
probe executables and engine smoke launcher were removed from active sources
at the user's request. Their CMake targets/options and private failure-injection
hooks were removed as well. The general source/configuration checks remain.
Runtime geometry/material shaders formerly under `tests/shaders` now live in
`assets/basic`; the shader compiler and renderer runtime remain build targets.

The commands and test counts in the dated sections below describe historical
acceptance runs. They are not commands for the current source tree. Accepted
M5/M8/M9 evidence/build roots remain frozen. The verified recovery archive is
`build/vulkan-test-retirement/retired-migration-sources.zip`; cleanup and current
build verification are recorded in `build/vulkan-test-retirement/summary.json`.
Future SDK checks follow `sdk_migration_plan.md` and cover the actual SDK work.

## M9 Manifest Performance Profile (2026-10-21)

The versioned [performance contract](m9_performance_profile.json) owns durations
and budgets. Runs bind its SHA-256 before starting; changing a budget after a run
invalidates that run. The selected scenes are the full M8 inspection view in the
game and the editor's main inspection view plus its independent model preview.
The initial overview/preview captures still pass the ordinary M8 host/oracle
checks. After sixteen content frames, the producer holds actor time at 240 ms
and pauses particles before it can advance past that phase. The inspection
camera then remains fixed. Final main and preview captures are taken after the
timed window; preview must match its initial independent oracle byte for byte.

`run_m9_performance.py` runs one scene/configuration/mode, using a fresh output
directory and isolated runtime. GPU processes stay serial. The contract requires
a five-minute baseline stress after warmup, a separate run with four bounded CPU
workers, and three near-timeout repetitions with two CPU workers. Near-timeout
means the process watchdog starts from the native ready receipt with five
seconds of teardown margin; it does not inject a Vulkan API timeout. The native
watchdog is a later fallback. CPU-worker PIDs, lifetime bounds and actual process
CPU times are recorded, and the wrapper terminates its own workers on every exit.

The optional `--pilot` runs two seconds of warmup and five seconds of stress.
It checks instrumentation and cleanup, and never grants manifest acceptance.
Configure/build a fresh root before replaying; accepted build roots and their
runtime caches remain frozen. For example:

```powershell
python sources/xray/render/vulkan/tests/tools/run_m9_performance.py `
  --binary-dir build/stage9-performance-manifest/debug/bin --configuration Debug `
  --scene editor-preview --mode baseline --output build/stage9-performance-manifest/runs/debug-editor-baseline
```

Native timing starts with backend initialization when the child-only
`XRAY_VULKAN_PERFORMANCE=1` is supplied by the wrapper. First-use pipeline cost
is accumulated through warmup; shader/compiler and driver caches are retained
between runs, so this is not a cold-machine measurement. Per-output CPU and
completed GPU samples, descriptor work/capacity, live/retired resources,
allocation counts and bytes are written to `performance.jsonl` on the render
owner. Its polling interval is reported separately from backend CPU frame time
and includes producer pacing and measurement I/O. The analyzer rejects missing
frame samples, early completion and unverified validation. Capture readback and
synchronization validation remain enabled throughout the measured workload.

Process private commit and working set come from `K32GetProcessMemoryInfo`.
Its lifetime peak commit and working set counters also cover startup, as defined
by [PROCESS_MEMORY_COUNTERS_EX](https://learn.microsoft.com/en-us/windows/win32/api/psapi/ns-psapi-process_memory_counters_ex).
Once per second the supported physical device's `VK_EXT_memory_budget` query
provides each heap's size, current process usage estimate and changing budget,
following the [memory-budget definitions](https://docs.vulkan.org/refpages/latest/refpages/source/VkPhysicalDeviceMemoryBudgetPropertiesEXT.html).
These estimates are distinct from exact application allocation requests and
physical residency. Unsupported heap queries reject this measurement profile.
Budgets cover absolute bytes, growth after warmup, capacity, per-frame allocation
work, pipeline creation, ordinary-frame device idle and CPU/GPU distributions.
The teardown receipt additionally requires an empty device-memory ledger,
matching allocation/free counts and zero Vulkan host-callback bytes.

The profile pins the existing `-max_resources_size` option to the resource-arena
size in the contract. The discovery pilot used the engine's RAM-dependent arena
default and observed about 35 GB of private commit. Version 2 keeps the byte
budgets and fixes a reproducible 4 GiB resource arena. Each run preserves the
exact contract bytes alongside its SHA-256; earlier pilots retain version 1.

The same pilot measured nine successful device-memory allocations per frame.
Mixed light types repeatedly replaced one differently sized shadow atlas;
unchanged leaf cards and particle snapshots also received fresh GPU geometry.
Shadow rendering now keeps three atlas extents with LRU replacement and ordinary
fence retirement. Leaf cards preserve identical mesh bytes, and particle geometry
shares identical immutable VB/IB bytes while advancing the CPU snapshot to its
current texture dependency. Distinct geometry still observes the two-generation
retained-cache bound. GPU controls check reuse, old texture release, mixed-light
steady allocation counts and a fourth extent's bounded eviction. The discovery
and corrected pilots are preserved separately; long-run budgets remain mandatory.

The first long Debug baseline exposed process-memory growth and an access
violation. A separate late-attach GDB replay caught SIGSEGV in a worker inside
`VkLayer_khronos_validation.dll` after a fence-state timeout. The stripped layer
does not provide a source-level stack, so this localizes the fault without
proving its underlying cause. An isolated 1.4.363 layer/SPIR-V control with the
same application binaries also exceeded the budgets and stopped progressing;
the original 1.4.357 runtime remains untouched. These are failed diagnostics,
not acceptance or proof about the separate historical NVIDIA startup incident.

Completed command buffers now reset before descriptor/retired-resource cleanup.
That ordering alone did not remove the measured memory growth. Each output's
two frame slots now retain their allocated descriptor-set triples instead of
resetting pools and allocating every draw again. A slot reuses sets only after
its fence completes and its command buffer resets; every draw rewrites all ten
bindings before binding the sets. Storage grows to the peak draw count of that
slot and is released with its output. Allocation counters count actual new
sets; descriptor-write counters still measure per-draw work. This follows the
[descriptor reuse contract](https://docs.vulkan.org/refpages/latest/refpages/source/vkUpdateDescriptorSets.html)
and the [Khronos descriptor-management guidance](https://docs.vulkan.org/samples/latest/samples/performance/descriptor_management/README.html).
The preserved diagnostics and subsequent measurements determine whether this
change resolves the observed long-run behavior; native contracts alone cannot.

The descriptor-only five-minute control finished with validation 0/0, balanced
teardown and the same final image, but still exceeded the 128 MiB process-growth
budget. Offscreen rendering also created a render-pass/framebuffer pair on every
pass. Those immutable pairs now use a per-output LRU cache capped at 64 entries.
Its key includes the actual image views, extent, formats, sample counts and
load/store/layout operations; clear values remain per invocation. Attachment
destruction invalidates matching entries before image-view retirement. Output
close removes that output's entries; LRU eviction uses each pair's last use
serial and ordinary fences, and shutdown drains the whole cache before textures.
No cache path adds device idle to an ordinary frame.

Version 3 adds zero-growth/cache-creation budgets without relaxing the prior
byte/frame budgets. Samples distinguish retained cached pairs, retired pairs
and successful pair creations. The native GPU contract exercises 1024 frames
per output with 65 draws, real process-memory growth, changing descriptor data,
64-entry eviction and attachment/output invalidation. Full manifest measurements
and the final matrix remain the acceptance criteria.

The first Debug pilot exposed a producer-clock bug in the new profiling harness:
setting the hold only after capture attempted to rewind an already queued actor
from 272 to 240 ms. The producer now prepares the hold during control-world
construction and applies it at the sixteenth content frame. The assertion and
failed pilot remain in `build/stage9-performance-manifest/pilot-debug-game`.
The [active plan](../../../../vulkan_integration_plan.md) remains authoritative
for acceptance; adding profiling tools alone does not complete M9-PERFORMANCE.

## M9 Renderer Ownership and Profile Index (2026-10-21)

The unused empty `vulkan::renderer` class, its singleton constructor/destructor
factory and exported `vulkan::initialize/finalize` declarations were removed.
The active path remains facade world -> engine world -> RHI-owned Vulkan backend
with explicit HWND initialization. `vulkan_stubs.cpp` remains at its frozen
capability-contract route: its deferred operations throw named errors and its
world factory/type methods are used by the active facade. Their guard inventory
continues to be checked by native CPU and real game/editor Lua controls.

The [profile and evidence index](profiles.md) links supported/deferred contracts,
scenes, workflows, configurations and dated reports. The active plan owns task
completion, including the unresolved historical NVIDIA startup-risk decision.

## M9 Performance Instrumentation (2026-10-21)

`vulkan_backend::performance()` returns an owner-thread snapshot. Successful
`vkAllocateMemory` calls and every matching free are accounted from bootstrap
through retirement and shutdown, including staging and attachments. The counters
report requested allocation bytes, live/peak bytes and successful allocation/free
counts. Failed Vulkan allocation calls publish neither a handle nor accounting.
Failure to allocate the tracking entry frees the successful candidate before
returning host OOM; that allocation and its immediate free remain counted.
These bytes exclude swapchain-owned images and do not measure physical residency
or a Vulkan heap budget. Process-wide host counters cover requested bytes and
allocation/free calls through `VkAllocationCallbacks`; they exclude alignment
headers, engine/STL allocations, internal driver notifications and allocations
the driver makes outside the callbacks. Reallocation preserves accounting on
failure and replaces the old block on success. Host counters do not reset when a
new backend session starts.

`enable_performance_timestamps()` explicitly enables CPU/GPU timing between
frames. Each output owns a four-query pool for its two frame slots. The GPU
pool is allocated before swapchain acquire/command recording, so allocation
failure can be retried explicitly without leaving an acquired frame. The GPU
interval spans TOP_OF_PIPE through BOTTOM_OF_PIPE in that output's command
buffer, including capture copies when enabled and any intervening dependencies.
It measures elapsed command-buffer time, not isolated shader occupancy. Results
are read only after the slot's existing submission-fence wait, without WAIT_BIT
or another queue/device wait, following the
[Vulkan query lifetime rules](https://docs.vulkan.org/spec/latest/chapters/queries.html).
The actual queue's valid timestamp bits and the device's nanoseconds-per-tick
period determine the duration, including one counter wrap. Pending slots are
not reported as completed samples. Closing an output destroys its query pool
after its submissions retire; shutdown also destroys pools on the existing idle
path. Timing is disabled again on reinitialization.

CPU frame time covers successful backend begin-frame through present, including
fence/acquire/present waits and recording; it excludes engine producer/queue work
outside those calls. CPU and completed GPU samples have independent serials and
counts per output. Counters expose successful descriptor sets, descriptor writes,
retained descriptor-pool/set/descriptor capacity and uniform-chunk/byte capacity
across both slots of each output. Pipeline calls are counted for the session;
CPU time inside `vkCreateGraphicsPipelines` is accumulated only while timing is
enabled. Existing device-idle calls are counted, with no new ordinary-frame idle.

`xray_render_stage9_timestamp_math` checks valid-bit wrap/high-bit masking and
actual DLL host callback allocation/reallocation/free accounting. The GPU
contract checks independent main/preview samples, byte-identical timing-on/off
images, stable warm allocation/pipeline/capacity counters, descriptor growth
attributed to preview, poisoned allocation/query-pool failures, fence-delayed
free, output close and repeated shutdown/reinitialization. Existing controlled
failure tests additionally require an empty device-memory ledger after cleanup.

New work uses isolated `build/stage9-performance/{debug,release}` trees; the
earlier M9 failure/matrix and M8/M5 evidence stays frozen. Instrumentation and its
small synthetic GPU contract do not accept M9-PERFORMANCE. Manifest-scene
warmup/stress duration and budgets, process-memory growth, sustained game/editor
main/preview measurements, background timing and near-timeout variability still
require their own controlled evidence. CTest wall time is not a frame benchmark.

## M9 Controlled Failure Contracts (2026-10-20)

Failure controls are private owner-thread, one-shot hooks compiled only with
`BUILD_TESTING`. They have no production environment or command-line switch.
`xray_render_stage9_failure_tests` exercises startup capabilities, poisoned
creation/allocation outputs, partial bootstrap objects, buffer/texture/attachment/
shader/sampler allocation, texture-update rollback, additional-output rollback,
and device loss at frame wait, acquire, submit and present. The facade cases
also check that the world drops readiness, the RHI backend and allocator owners.
Each case requires cleanup without growing tracked GPU resource counts or
material/capture capacity;
separate controls explicitly create a fresh instance/device and render again.
Automatic device recovery and physical GPU reset are outside this contract.

Instance, device, single-object creation and device-memory allocation publish
handles only after success. Frame command-buffer arrays are also published only
after successful allocation. Graphics-pipeline creation retains its separate
partial-success cleanup, and descriptor-array failures never publish their sets.
This follows the [Vulkan runtime-error output contract](https://docs.vulkan.org/refpages/latest/refpages/source/VkResult.html):
failed-call outputs can be undefined, while existing objects survive OOM. Frame
wait/acquire loss invalidates readiness before the RHI/facade handles the error;
failed submit does not advance the submitted serial. Shutdown still destroys
child objects after the device wait, as required by the
[device-loss lifetime contract](https://docs.vulkan.org/spec/latest/chapters/devsandqueues.html#devsandqueues-lost-device).

Pre-instance failures cannot claim an enabled instance validation layer. Their
successful reinitialization controls require actual validation. The other
controlled cases keep validation enabled through teardown and reject unexpected
errors or warnings. Injected OOM results do not exhaust host/device memory, and
injected device loss does not disconnect or reset the physical GPU.

`xray_stage9_failing_icd` is an unregistered test DLL, selected only for a child
smoke process through its private `VK_DRIVER_FILES` manifest. It advertises the
required instance extensions, then returns `VK_ERROR_INITIALIZATION_FAILED` (-3)
from the actual loader's ICD `vkCreateInstance` call. The loader returns -9 and
the facade rejects startup. The negative test requires normal process exit 1
and retains its expected one loader warning and one loader error separately
from successful-GPU validation 0/0. It does not replace the real NVIDIA driver
or reproduce the historical NVIDIA SegFault. The frozen pre-fix M9 Debug smoke
also exits 1 under this control in GDB. No original crash backtrace/dump has
been found; M9-FAILURES still needs a proven historical cause/fix or an explicit
residual-risk decision from the user.

New build/evidence roots are `build/stage9-failures/{debug,release}` and
`run-accepted-{debug,release}`. Earlier M8/M5/M9 matrix evidence remains frozen.
Configure isolated binaries with testing, DXC and GPU tests enabled, then build
all targets. Run the configurations serially:

```powershell
pwsh -File build/stage9-failures/run_configuration.ps1 -Configuration debug
pwsh -File build/stage9-failures/run_configuration.ps1 -Configuration release
```

The recorder binds failure receipts, the repeated complete matrix/workflows,
source/runtime identities and unchanged earlier evidence in the
[failure checkpoint](../../../../build/stage9-failures/summary.json).
For another run copy the helpers to a new root; never overwrite frozen runs.

## Original Static Asset Runtime Cooking (2026-10-20)

The user explicitly changed M5-C to the available original exported/source
assets because no original cooked cache is available. The opt-in
`original_static_model_request` goes through the existing static model/instance
resource cooks. `xray_vulkan_asset_probe --input original` mounts original
`sources/` as `resources` and `exported/` as `original.exported`; ordinary cooked
requests keep their existing route. Inputs can be staged as byte-identical
copies with original SHA-256 provenance. No Python adapter, Lua subprocess,
offline geometry conversion or texture transcoding participates in loading.

The native reader validates the complete bounded LE64 uncompressed export
archive graph before accessing payloads. It avoids historical native VFS pointer
fixups. Original static position/UV/index bytes are preserved; basis packing
and vertex layout follow the existing static disk contract. Exported bounds
are checked, without fitting or rewriting them. Preview fitting changes only
the rendered model transform.

A separate bounded Lua state resolves property tables, references and source
module inheritance. Source environments expose only property/reference/vector
helpers. They have no filesystem/process APIs or standard libraries. Limits
include 128 source modules / 8 MiB total, 4 MiB per config, 32 MiB Lua memory,
one million instructions, 100,000 expanded values and depth 64. Model settings,
export properties and materials are encoded into the existing checked binary
config ABI in runtime. Numeric material scalars use their declared float ABI.

The original material profile accepts a single `g_stage_default` with default
shadow-casting/non-emissive stage metadata and the renderer's supported
opaque/cutout diffuse, normal, detail-normal and scalar/specular inputs.
Inheritance metadata is resolved rather than mistaken for an extra render
stage. Inputs of explicitly disabled legacy branches may remain unused; the
complete resolved material is retained in cache. Unsupported enabled branches
fail explicitly, including original barrel `use_variation_mask`. No active
feature is silently projected away, and original scene/DX11 parity is not
claimed.

The caller supplies a separate workspace cache. Canonical, case-insensitive
checks reject cache/source overlap. Runtime cooking writes checked geometry,
properties, settings, supported material data and the complete resolved
material. Recooks verify identical existing cache bytes; they do not use a
stale cache as an import oracle. Changed source versions require a fresh cache.
The VFS physical-path conversion handles both an exact mount root and an empty
virtual root; the previous unconditional suffix offset could read beyond the
path or drop its first character. Native CPU controls cover these cases as
well as damaged archives, malformed mesh records, Lua inheritance/cycles,
instruction limits, filesystem/process rejection and cache isolation.

Example after byte-identical staging into the bounded input directory:

```powershell
.\binaries\xray_vulkan_asset_probe.exe --assets build/stage5-original-runtime/original-inputs `
    --input original --model resources/models/props/box/woodbox_mil.model/render `
    --output build/new-original-woodbox --view material --cycles 16
```

The generated cache is under the new output's `runtime-cache/`. The regular
validation/DXC environment and fresh-output requirements still apply. Original
cooked-pack compatibility remains unverified; it is no longer the criterion
for the user-approved M5-C scope.

Accepted evidence is `build/stage5-original-runtime/summary.json`: original
`props/box/woodbox_mil.model` and `props/box/wood_box`, 595 vertices / 1188 indices,
17 native recooks (warmup plus 16 complete unload/recook cycles), 24,335 visible
pixels and 15 BMP captures. Diffuse, normal, detail-normal and specular texture
dependencies are present. Camera/model movement, named visibility, culling,
queued removal, unchanged reload, stable live counts/capacities and complete
drain pass through shutdown. All 36 original/staged file SHA-256 identities
are unchanged. A post-run independent geometry oracle matches the native
converted bytes; it supplies no runtime inputs. Model/normals/camera images
were visually reviewed using RGB PNG copies preserving the native BMP RGB bytes.

Full validation passes **124/124 CTest (66 GPU), 35/35 Python**, with 52 CTest
validation summaries and two original-input probe reports at **0 errors /
0 warnings** through teardown. The original barrel negative control rejects
`use_variation_mask` explicitly with clean shutdown and unchanged inputs.
The 1141 earlier accepted M8 captures, immutable pack, 913 original files and
three absent paths remain unchanged. The user-approved M5-C scope is accepted
on GCC Debug / RTX 4070; M9 has not started.

## M5-C Input Discovery and Material Diagnostics (2026-10-20)

The supplied `C:/Stalker2_2011/xr2-dsgn` tree contains 12,762 files. A read-only
inventory finds no `resources/platforms/pc_dx11`, `resources.converted`, DDS or
`converted_model`; the original `resources.mount` names the absent PC cooked
cache. The selected original barrel archive contains exported geometry, and
its material/settings are Lua source. Original TGA/options dependencies are
supported, but cannot replace the absent cooked geometry and binary materials.
The user confirmed that `Stalker2_2011` is the only available external asset set.

Material failures now identify the requested logical material and the rejected
stage/metadata key. The old source-derived full-material barrel negative
control fails at `props/barrel/barrel_1_red_radiation`, `material.instance`,
with successful teardown, unchanged inputs and validation 0 errors / 0 warnings.
This is the first rejection, not an exhaustive list of unsupported original
material features. No material fields were ignored or new profiles enabled.
All 11 focused material/probe CTests pass, including seven GPU tests.

Evidence is `build/stage5-legacy-compatibility/summary.json`, the complete
path/size inventory, six unchanged original SHA-256 identities, bounded archive
payload identities and fresh native negative-control reports. Discovery does
not load an original cooked pack or produce accepted M5-C images/cycles. M5-C
remains open: accepting runtime loading/cooking of the available original assets
instead requires an explicit scope change. M9 has not started.

## M8 Capability Audit and Bounded Acceptance (2026-10-20)

`m8_capabilities.json` binds all 41 required manifest items to implementation
routes, passed CTest cases and actual game/editor/output image proofs. The
checker rejects omitted/duplicate operations, failed or missing tests, absent
native images, missing source symbols, and required routes into legacy gates.
It also accounts for every legacy engine/producer guard. The original manifest,
input inventory, host/preview/output contracts and earlier evidence stay frozen;
their status text describes the scope/checkpoint in which they were created.
The active plan and this aggregate acceptance record close the bounded M8 scope.

Legacy APIs that previously silently succeeded now report an explicit
`Vulkan unsupported operation` error naming the API. Producer guards run before
assertions, payload copies or command insertion; engine guards cover direct
calls. These include global stage switches, legacy postprocess resources,
renderer configuration/shader reload, screen/grid/gizmo helpers, selection and
live Lua material edits. Use the documented typed per-view postprocess settings,
scene-owned particle handles and commands, scene debug/UI, and model instance
APIs. No new legacy panel, particle graph preview, GI/LPV or rendering profile is
published. The retired DX9/DX10 factories reject before allocating a backend.

The CPU fixture exercises 75 real negative calls, exact diagnostics, usable
command queues afterward, no legacy UI callback, no backend and no frame
advance. It covers 39 public engine guards and 36 producer guards together with
six Lua-dependent entries exercised in the actual editor core VM (three shared
engine entries also run in game). Four private configuration callbacks sit
behind rejected configuration entry points. `--audit` enables only those native
Lua controls; the launcher explicitly clears the flag for other runs. Rejection
receipts must match the actual loaded scene owner. This avoids constructing a
partial Lua VM/mutex solely for a test.

Evidence is `build/stage8-capability-audit/summary.json`: **123/123 CTest
(66 GPU), 35 Python**, **64 validation summaries (0 errors, 0 warnings)** through
teardown. Two editor replays preserve 49 images and control metrics; two output
replays preserve all 28 paired images; game preserves all 37 images. Initial
preview and output fixture replays also remain exact. All 934 earlier accepted
captures, the immutable pack, 913 original files and three absent paths are
unchanged. M8 is accepted for this GCC Debug/RTX 4070 manifest profile. M5-C
legacy cooked-pack parity and M9 stabilization remain separate.

```powershell
python sources/xray/render/vulkan/tests/tools/run_m8_control_game.py --workflow --audit --output build/new-audit-game
python sources/xray/render/vulkan/tests/tools/run_m8_control_game.py --editor --outputs --audit --output build/new-audit-editor
```

## M8 Combined Native Main/Preview Workflow (2026-10-19)

`m8_editor_outputs.json` refines the frozen preview contract without changing
ABI 2. `run_m8_control_game.py --editor --outputs --output build/new-outputs-run`
drives the real managed toolbar, independent HWNDs, resource cooks and editor
facade. Sixteen actions produce 28 fenced BMP captures. The editor waits for
the exact render-frame acknowledgement before changing the next output state.
After 16 content warmup frames, actor time stays at 240 ms and particles pause;
main continues presenting that accepted inspection state during preview cooks.

Each view resizes independently to 1102x673 and restores its initial extent.
Minimizing either window stops only its presentation clock; the other advances.
Main retains a finite cached projection while its child client is suspended.
Preview reopen creates a fresh output whose first presented frame is 1. Preview
visibility changes only its own instance; main prop visibility does not change
the preview despite their shared immutable woodbox asset. Restored main and
preview images match the accepted hashes. Independent original-bounds CPU
projection contains all 23,802 resized preview pixels (14,398 colors), with zero
changes outside the projected model region. Two processes replay all 28 images
and control metrics exactly.

Preview close follows another uncaptured paired frame and retains its HWND
until GPU and scene/view/output retirement is acknowledged. Host shutdown opens
a third preview with its scene/view/output query batch deliberately held. After
two main ticks, host WM_CLOSE on the third tick closes the render session; the batch is rejected
on the editor owner and drains to zero while both HWNDs are alive. Managed
disposal follows Vulkan validation totals; the native module outlives file
producer TLS cleanup. Automation keeps windows hidden or alpha-zero minimized,
nonactivating and input-transparent.

Evidence is `build/stage8-native-outputs/summary.json`: **122/122 CTest
(66 GPU), 32 Python**, with **64 validation summaries (0 errors, 0 warnings)**
through teardown. Two full editor runs preserve all 49 baseline images and
control metrics; game preserves all 37 images. The two initial preview smokes
and three output-fixture replays remain exact. All 727 previously accepted
captures, the immutable pack, 913 original files and three absent paths remain
unchanged. This closes M8-OUTPUTS; M8-AUDIT remains open. Earlier dated records
below retain their original checkpoint scope.

## M8 Initial Managed Model Preview (2026-10-19)

`m8_editor_preview.json` refines the frozen main-host deployment contract. The
same-deployment native and managed bridge now use ABI 2 (80-byte callbacks,
112-byte world bridge); the factory rejects other versions/sizes. The added
preview callback carries a native-width HWND and open/close operation. Managed
toolbar command 9 opens an independent top-level WinForms model window. A
typed `static_model_clone_request` uses the registered resource cook to make
a distinct instance sharing the main `woodbox_mil` immutable asset/mesh/material
data. Instance overrides, scene/view and output targets remain independent.

Preview `WM_CLOSE` requests retirement and retains the HWND. The native owner
waits for the renderer's atomic closed acknowledgement and scene/view/output
lease deletion before acknowledging managed disposal. Closing before first
backend adoption also acknowledges safely without creating GPU objects. Host
shutdown retains both HWNDs through Vulkan teardown. Actual hostfxr/WinForms
tests cover hidden input-transparent windows, independent resize, close-before-
ACK retention, reopen, version/size/owner guards and four restart cycles. The
GPU scene fixture also uses shared-asset clones and tests unadopted output close.

Two fresh `--editor --preview` native processes render the real immutable-pack
model at 640x480 through a separate surface on the same logical device. Both
main and preview images replay exactly; the main matches the accepted overview.
An independent CPU projection of original woodbox bounds contains all 12,106
model pixels (8,132 colors), with zero changes outside that region. Capture
publishes on the actual sixteenth main content frame, allowing asynchronous
preview cooks between frames. During these cooks, main keeps presenting its
last content phase with particles paused, then resumes simulation. Preview
opens after the first ready main frame,
so its capture has 15 presented content frames. Both runs prove validation 0/0
and managed destruction after Vulkan teardown.

Evidence is `build/stage8-managed-preview-live/summary.json`: **122/122 CTest
(66 GPU), 30 Python**, with **62 validation summaries (0 errors, 0 warnings)**
through teardown. Two full editor replays preserve all 49 accepted images and
control metrics; the game replay preserves all 37. The 576 earlier accepted
captures, immutable control pack, 913 original files and three absent original
paths remain unchanged. The acceptance recorder verifies these identities and
refuses to overwrite a different frozen source/evidence snapshot.

This checkpoint verifies initial preview integration. Combined native
main/preview resize/minimize, close/reopen while main continues, and host close
with pending preview cooks remain required before M8-OUTPUTS acceptance.

```powershell
python sources/xray/render/vulkan/tests/tools/run_m8_control_game.py --editor --preview --output build/new-preview-run
```

## M8 Cooked Output Routing Checkpoint (2026-10-19)

Scene resource cooks and the game/editor facade now route distinct HWND leases
through one backend device. CPU cooks do not create GPU objects; the renderer
adopts each lease on its owner thread. Every output has separate GBuffer,
shadow and TAA targets, presentation clock and history. Shared model/terrain
geometry, material and backend resource caches remain device-wide. Scene/model
content revisions invalidate only histories which use those resources.

`xray_render_stage8_output_scene_gpu` exercises real cooks and facade queues:
independent RGB/extent oracles, resize/minimize in both directions (including
a child view's minimized parent), stable target counts, preview-only frames,
visibility edits, and nine-frame TAA accumulation interleaved with preview
edits. It closes either output first, discards queued work for a closing output,
automatically retires an abandoned secondary lease, reopens previews and drains
pending cooks during shutdown. Untouched views retain their image and history.

Evidence is `build/stage8-output-routing/summary.json`: **122/122 CTest (66 GPU),
29 Python**, with **60 validation summaries (0 errors, 0 warnings)** through
teardown. Four output images match across two fresh processes and CTest. Two
fresh editor workflows reproduce all 49 accepted main-view images; the game
workflow reproduces all 37. All 429 prior captures, original inputs and the
control pack remain unchanged. This verifies routing and target isolation;
the actual managed `woodbox_mil` preview and its UI lifecycle remain required
before M8-OUTPUTS can be accepted.

```powershell
ctest --test-dir build/windows-x64-clean -R xray_render_stage8_output_scene_gpu --output-on-failure
```

## M8 Output Backend Checkpoint (2026-10-19)

`vulkan_backend::create_output/select_output/close_output` support additional
HWNDs on one device. Each output owns its surface, swapchain, per-image depth,
presentation semaphores/fences, command pool/buffers, two submission slots,
descriptor/uniform arenas and opted-in capture buffers. Resource handle pools,
shaders, textures, samplers and pipeline cache remain device-wide. Retirement
serials remain global because submissions use the same ordered graphics queue.
Selection/creation require the renderer owner between frames. Close presents a
pending submission, waits the closing output's submission/presentation fences,
then destroys its GPU objects. Retained leases stay closed across backend restart.

`xray_render_stage8_outputs_gpu` checks both resize/minimize directions and close
orders, pending submission close, output recreation, zero-output recovery and
backend restart. The same shaders and texture render different per-view material
constants at different extents. Full-image numeric checks cover every pixel of
the two initial images; exact comparisons cover restore and isolation. Capture
storage and frame arenas must converge, and duplicate/invalid/foreign/closed
operations fail. A window without capture opt-in cannot inherit another view's
permission. Automated windows stay transparent, hidden and nonactivating.

An exploratory editor replay (`build/stage8-output-backend/run-editor-repeat`)
captured all 49 images and passed Vulkan teardown, then hit an access violation
while retiring file producer 2. Native editor module release previously preceded
file-producer TLS cleanup. World/managed HWND destruction still follows Vulkan
teardown; the loader reference now survives resource shutdown and every file
producer's final queue-node destruction. It is released after the file devices.
The runtime receipt checker rejects missing, duplicate or premature module-release
events for workflow, missing-project and startup-cancel editor runs.

Checkpoint evidence is `build/stage8-output-backend/summary.json`: **121/121 CTest
(65 GPU), 29 Python**, with **61 validation summaries (0 errors, 0 warnings)**
through teardown. Two fresh editor workflows reproduce all 49 accepted images
and control metrics; the game workflow reproduces all 37 accepted images.
Three backend images match across two independent processes and CTest. Original
inputs and the previous 285 accepted captures remain unchanged. This record
verifies the backend checkpoint and main-view regression; M8-OUTPUTS stays open.

Run in the standard validation environment:

```powershell
cmake --build build/windows-x64-clean --target xray_render_stage8_output_tests -j8
ctest --test-dir build/windows-x64-clean -R xray_render_stage8_outputs_gpu --output-on-failure
```

At this checkpoint, scene cooks/facade still routed only the main HWND.
Per-output scene targets/history and the real `woodbox_mil` editor preview were
not yet integrated. Later checkpoints above supersede those limitations. Status
is tracked only in `vulkan_integration_plan.md`.

## M8 Managed Editor Main View (2026-10-19)

The deployment contract is `m8_editor_host.json`, linked to the frozen M8 input
manifest. CMake generates an SDK project from `editor/base/EditorHost.csproj.in`
inside the build directory and builds only `Bridge.cs` and `EditorWorld.cs`.
Restore has no NuGet package sources. The verified SDK is .NET 10.0.203 with
Windows Desktop runtime 10.0.7; native C++ remains GCC/MinGW-w64. Building the
`stalker2` target also builds/deploys its native and managed editor dependencies.

`xray_editor_world.dll` exports `create_world`, `destroy_world` and
`set_memory_allocator`. It resolves managed artifacts beside the executable and
uses the configured system hostfxr to obtain
`xray.editor_base.Bridge.CreateWorld` through
`load_assembly_and_get_function_pointer`. `xray_editor_create_world` is its
`UnmanagedCallersOnly` entry annotation; this ordinary managed DLL is loaded
through hostfxr, without assuming a PE export. CMake's Ninja commands record the
selected hosting pack/hostfxr paths; the acceptance recorder checks those paths
and hashes both files. `libxray_editor_base.dll` retains the native adapter dependency.

The single shared C ABI in `editor/world/host_bridge.h` checks version and byte
size: callbacks 72 bytes, world bridge 96 bytes. HWNDs use `uintptr_t`/`nuint`,
opaque contexts retain pointer width, booleans use 32-bit integers and matrices
cross as sixteen finite float32 values copied before the native callback.
Managed exceptions return status codes. Contexts are rooted until explicit
disposal and stale/foreign-owner calls fail. CoreCLR/hostfxr live for the process;
worlds, native callbacks and HWNDs are released independently. The host test
checks a failed missing deployment followed by successful loading, malformed
ABI/flags, actual matrix/frame/context delivery, all eight toolbar commands,
foreign/nonfinite/stale rejection and four create/run/clear/destroy cycles.

Engine readiness initializes the renderer/session/cooks and editor channel
before `run()` can submit the first level query. The managed STA owns distinct
main and render HWNDs. `editor_world_proxy` routes native -> managed -> native
render callbacks on that owner, and the shared `facade/control_level.h` uses
`editor_renderer().scene()/draw_scene()/end_frame()`. The ordinary logic worker
stays present with an empty disabled game world, avoiding a second content/cook
owner. Reapplying an unchanged view matrix preserves the stored camera instead
of accumulating inversion roundoff during asynchronous loading. The editor
console delegate belongs to its renderer instance and unregisters at destruction.

The main toolbar drives opaque-prop visibility/translation, terrain binding
edits, a copied UI quad, a depth-disabled debug triangle and explicit bar-cell
isolation. Both original cells remain retained. Their combined arrays already
fill the 576 MiB queued-view profile, so a new unused bar slot is added/removed
while the other cell is hidden. The bar stays within 256 MiB; both cells return
after removal. Global texture exchange and restoration of the original slot
tables run with both cells present. Completion waits for the expected command
count, committed statistics, zero pending/in-flight work and zero queued bytes.
Unavailable toolbar operations return an error without tearing down the host.

The automated editor workflow has 49 shots: the original 37 game operations
plus twelve shots driven through the real managed toolbar handlers. Twenty-two
restored inspection frames must be exact; unused-layer add/remove images must
equal the isolated-cell image. Overlay checks independently project the debug
triangle and test interior/exterior coverage, fixed UI RGB and zero changes
outside the prescribed regions. UI is display-space RGB `(64,128,192)`. In lit
mode debug is linear HDR: green `85/255 = 1/3` maps through Reinhard to `1/4`,
then sRGB byte 137, with zero RGB tolerance. The initial display-space-debug
assumption and the initial extra-layer budget failure remain in discovery logs.

Clear/drain releases facade membership and resource handles on the editor
owner. After render-session close, its hook rejects/drains the deliberately held
particle query and removes native level/cooks. Managed HWNDs/callbacks remain
alive through Vulkan teardown, then the bridge disposes its UI and the native
module unloads. Every automated window is hidden, nonactivating and
input-transparent; the render child is explicitly opted into fenced readback.
The separate `--editor --startup-cancel` control holds the first project query
until render close, then drains its callback before controller destruction,
without loading content or producing a frame. Its child-only environment flag
is reset to zero for ordinary runs. Nonzero failures during close remain failures.

```powershell
cmake --build build/windows-x64-clean --target stalker2 -j8
python sources/xray/render/vulkan/tests/tools/run_m8_control_game.py --editor --workflow --output build/m8-editor/workflow-001
python sources/xray/render/vulkan/tests/tools/verify_m8_game_workflow.py build/m8-editor/workflow-001
```

Final acceptance passed **120/120 CTest cases (64 GPU)** in **312.80 seconds**,
**28 Python tests**, and **54 validation summaries (0 errors, 0 warnings)**
through teardown. Two independent editor processes reproduced all 49 BMPs and
control metrics exactly, with 22 restored inspection frames each. Their 37
common frames and the unchanged game runtime's full workflow match the frozen
game images. The startup-cancel control exited 0 without content/capture;
missing-project exited 2 with clean teardown. All 150 prior accepted captures,
the common pack and original inputs remain byte-identical. Source, binaries,
deployment/runtime files and evidence hashes are frozen in
`build/stage8-editor-host/summary.json`. This accepts M8-EDITOR main view.
Earlier receipts and the first regression are preserved in discovery folders;
`before-startup-drain` predates the final pending-load cleanup refinement.

The independent model preview and output resize/close isolation belong to
M8-OUTPUTS. Unported legacy panels and particle graph editing are excluded by
the manifest. This profile does not claim legacy xprj/cooked parity or M9 work.

## M8 Production Game Workflow (2026-10-18)

`run_m8_control_game.py --workflow` runs the unchanged 175-file common pack in
the ordinary hidden `stalker2.exe` host. It retains the stored overview camera
and adds an explicit inspection camera from `(24,9,-13)` toward `(23,1,-33)`,
then moves the eye three metres in X and restores it. These are prescribed
camera operations, with no runtime bounds fit or legacy input/UI dependency.

The workflow captures 37 shots. After sixteen content frames it holds the
animation player at 240 ms and pauses both particle instances through the
facade. It independently disables/restores SSAO, distortion, decals, particles,
the tree, the actor, opaque props and terrain. Fixed phase and exact restoration
keep simulation changes out of these comparisons. A single packed HWND property
publishes shot/frame together; the render owner acknowledges that exact token
only after readback. No second request overlaps it. The ordinary nonactivating,
input-transparent capture-window ownership checks apply to every shot.

Additional shots isolate alpha/additive emitters and exercise the manifest's
mutation operations: particles translate by `(1,.5,0)`, the tree by `(2,0,0)`
and the decal by `(1,0,0)`, with exact transform restoration. The actor changes
from idle to play using a 250 ms linear transition beginning at 500 ms, sampled
at 625 ms, then restores the original blend at 240 ms. Both particle instances
are stopped and removed; new real instance queries replay them for exactly
sixteen fixed steps and must restore the original particle image.

`verify_m8_game_workflow.py` requires a visible contribution from all eight
controls, bounds local effects/meshes with independently projected descriptor
ROIs, and checks that disabling SSAO never darkens the image. Seventeen restored shots
per run must match the inspection BMP byte-for-byte. The camera operation must
change the image. The empty output after unload must be uniformly the analytic
sRGB encoding of the renderer's linear `.025/.025/.04` clear: `(44,44,56)`.

Unload releases game handles and facade membership, then waits for thread-safe
registry counts `(scene,view,output)=(0,0,0)` before recreating the scene. Reload
must reproduce both overview and inspection captures exactly. At shutdown a
real selected particle query remains held in a query transaction with retained
scene/world handles. `engine_user::world::on_render_session_closed()` runs on
the logic owner after the render owner closes its session, while resource/cook
workers and channels still exist. Its default implementation is empty. The M8
world uses it to release the transaction, require rejection on the original
logic owner, and drain the query before game destruction and cook removal.
It submits no commands into the closed render session.

Native workflow receipts, independent image metrics, source/binary identities
and the evidence recorder are in `build/stage8-game-workflow`. Acceptance requires
two independent processes to reproduce all 37 BMPs byte-for-byte, with the
original overview matching the frozen initial game capture. The first verifier's black-empty-frame
assumption was corrected to the explicit analytic clear contract; its original
discovery run and failure log remain preserved. Earlier 23-shot runs and their
regression are archived in `before-manifest-operations`; they do not claim the
additional transform/transition/stop/replay coverage.

The first 37-shot pair exposed fifteen one-level RGB differences in the moved
camera's alpha billboard. Single-point constant particle curves evaluated
`a*(1-alpha)+a*alpha`, introducing seed-dependent rounding even with equal
bounds. Equal bounds now return the exact constant for valid range alpha;
nonconstant curves retain their interpolation. The regression checks scalar
and XYZ curves at `1.6`, `.7` and `-.3` over 65,536 seeds each: it failed before
the correction and passed on CPU and GPU afterward. The original differing
pair remains in `before-constant-curve-fix`; replay still requires exact BMP
identity, without adding pixel tolerance.

```powershell
python sources/xray/render/vulkan/tests/tools/run_m8_control_game.py --workflow --output build/m8-game/workflow-001
python sources/xray/render/vulkan/tests/tools/verify_m8_game_workflow.py build/m8-game/workflow-001
```

Final acceptance on GCC 16.2 Debug, RTX 4070 / driver 617.42 passed
**119/119 CTest cases (64 GPU)** in **400.45 seconds**, **27 Python tests**, and
**53 validation summaries (0 errors, 0 warnings)** through teardown. Both full
native workflows exited 0 with all 37 BMPs and control metrics identical;
the ordinary initial-frame run also exited 0 and retained the frozen overview.
The missing-project control exited 2 without producing a frame. The common
pack, original inputs and all 75 prior accepted captures remain byte-identical.
The source snapshot, binary hashes and evidence hashes are frozen in
`build/stage8-game-workflow/summary.json`. This accepts M8-GAME.

This is the bounded source-derived manifest render level. Legacy NPC/RTP/UI,
original xprj/cooked parity (M5-C), editor main/preview (the remaining M8 gates),
and final matrix/failure/performance work (M9) retain their separate scope.
GI/LPV remain deferred.

## M8 Common Pack and Production Game Level (2026-10-18, Partial M8-GAME)

`tests/tools/prepare_m8_control_pack.py` regenerates the accepted component
projections from the manifest's read-only originals and assembles a common pack.
It verifies the frozen 195-input/four-provenance inventory before and after
preparation, permits shared outputs only when their hashes agree, and records
all 175 output files and its 130 consumed source inputs. Original terrain bytes,
heights, translations, layers, textures and authored mips are retained. The
stored camera fits aggregate placed bounds for both manifest aspect ratios;
props/actor/tree sit above sampled ground and the decal volume intersects its
receiver. Three stored lights, ambient, actor clips, fixed particle profiles and
the distortion volume use the same common descriptor. No runtime camera fit is
performed. Preparation refuses an existing destination; `--verify` checks a
prepared pack without refreshing hashes.

The native binary descriptor is
`resources.converted/projects/vulkan_m8_control/render_scene`; `scene.json` is
its inspectable counterpart, checked byte-for-byte against native serialization.
This explicit render-level format is consumed by `m8_control_world` in the
ordinary game module, selected by `-level=vulkan_m8_control`. It uses real
scene/view/output, static/terrain/skeleton game wrappers, terrain collision,
two animation clips through `animation_player`, particle, tree and material
cooks and facade submission. Legacy NPC/RTP/UI worlds are outside this level;
their accessors reject unsupported use. M5-C original project compatibility
and the rest of M8-GAME remain separate acceptance gates.

`-render_assets=<prepared-root>` mounts its `resources` and `resources.converted`
trees through the engine's ordinary filesystem and resource workers, with mount
completion before game creation. `-render_capture=<new-directory>` creates a
hidden, nonactivating, input-transparent 960x640 window, suppresses splash/input
activation, and captures the sixteenth content frame (actor time 240 ms) on the
render owner before orderly exit. It checks window/input ownership. Explicit
`-user_data_path=<writable-directory>` isolates logs from the legacy relative
user-data path. `tests/tools/run_m8_control_game.py` verifies the pack, supplies
these arguments, requires validation, enforces a timeout and refuses old output.
The ordinary logic/render/resource/cook/HDD/DVD threads remain distinct; the
launcher does not enable `-debug_single_thread`.

Production startup exposed an empty filesystem-device initializer and six
asynchronous command methods that returned before queue publication. Both are
repaired. The file response channel now destroys its drained forward queue and
clears each producer's TLS queue after consumer finalization. Engine shutdown
keeps producers quiescent in APC until resource/VFS destruction and device
consumer drain, then finalizes their file response queues on their own threads.
The CPU file-device regression exercises all six APIs, real disk bytes, two
callback owners, events, failure controls and consumer-first TLS teardown.
The initial logger null-file dereference is guarded; legacy sound command dummy
nodes and world users are retired during shutdown. These lifecycle fixes do not
expand the sound feature profile or resolve the separate M9 NVIDIA ICD case.

The full-clip animation lexeme overload now destroys its temporary intervals
after the expression has cloned them; explicit caller-provided intervals remain
borrowed. Mixing-tree assignment retires the old placement-constructed nodes
before replacing their buffer reference. The player regression checks clip
reference counts after single playback, blending, target replacement and reset
following a timed transition, in addition to its independent pose oracle.
The mimalloc adapter preserves a non-null initialized-state sentinel for the
base allocator lifecycle. Its former zero lower bound caused a shutdown assert
even after all resources and devices had drained. The CPU regression checks two
initialize/allocate/free/finalize cycles with the real allocator.

Reproduction from the repository root (Python with Pillow and Lua are required):

```powershell
python sources/xray/render/vulkan/tests/tools/prepare_m8_control_pack.py --output build/m8-control/assets --lua C:/msys64/mingw64/bin/lua.exe
python sources/xray/render/vulkan/tests/tools/prepare_m8_control_pack.py --output build/m8-control/assets --verify
python sources/xray/render/vulkan/tests/tools/run_m8_control_game.py --output build/m8-game/run-001
python sources/xray/render/vulkan/tests/tools/run_m8_control_game.py --missing-project --output build/m8-game/missing-001
```

The missing-project control uses separate empty mount directories, requires
failure exit 2, validation through teardown and no content/capture, and verifies
the original common pack before and after. Each report records the executable
and three production DLL identities; an old run cannot stand in for changed
binaries.

Native evidence in `build/stage8-control-pack/run-final` and `run-repeat` has
two clean exit-0 processes, six distinct logic/render/resource/cook/HDD/DVD
owners per run, validation 0/0 through render teardown and byte-identical
960x640 BMP captures at content frame 16 / actor time 240 ms. Each full run took
about 46 seconds. `run-missing-project` proves the intended failure exit 2 and
complete teardown without producing a frame. The overview capture demonstrates
replay; it is not an analytic control for individual feature contributions.

Final regression for these binaries: **119/119 CTest** (64 GPU, 366.16 s),
**24 Python tests**, and **52 validation summaries with zero errors/warnings**,
including the three production-host runs. The frozen summary, source snapshot,
binary/log identities and recorder are in `build/stage8-control-pack`. All 73
previous captures, the archived plan and the frozen original input inventories
remain unchanged.

This initial mixed scene/load/frame/shutdown evidence does not close M8-GAME. Full-scene
feature controls, camera movement, unload/reload and shutdown with deliberately
pending work still need their own production-host evidence. Editor main/preview,
capability audit and M9 remain open. GI/LPV remain deferred.

## M8 Explicit Terrain Memory Amendment (2026-10-18)

The manifest had selected `chaz_mir_part/-1_-1.terr`, which was the negative
control for the existing 256 MiB terrain array limit. The user explicitly kept
that cell and authorized a larger memory profile and boundary checks. The
manifest records this amendment; its original 195-input/four-provenance
inventory and all content selections remain unchanged. Historical inventories
still describe the standard profile and have not been relabeled or overwritten.

`terrain_memory_profile::standard_256_mib` remains the default. The explicit
`extended_384_mib` profile permits 384 MiB per cell and 576 MiB of visible terrain
arrays in a queued view. Scene configuration and typed terrain cook userdata
must agree; a standard scene rejects an extended query or instance. Source
dependency accounting, normalization, binding transactions, reload/no-op
identities and copied draw snapshots retain the selected profile. Unknown
profiles fail. There is no automatic escalation, downsampling or format change.
These are decoded-source/array accounting limits, not a bound on total process
RAM or device memory including staging and fenced retirements; M9 physical memory
and performance profiling remain open.

The normalizer still plans complete array layers, including aliases, before
allocating promoted pixels. The real selected cells require 201,326,580 and
402,653,160 array bytes. Together they require **603,979,740 bytes**, just below
the explicit 576 MiB view limit. Visible terrain preflight now precedes GPU
renderer/target preparation, including resuming a suspended GBuffer. The new
three-cell negative control found and fixed the former ordering, which could
prepare GBuffer resources before rejecting the view budget.

The extended real terrain run covers the original standard-profile rejection,
both cells with all authored mips preserved, four independent material RGB
oracles per cell, mixed standard/extended submission in one deferred view,
eight stable frames, three-cell rejection with unchanged GPU counters, exact
valid-frame recovery and retirement/cook drain. The paired view uses explicitly
flat control geometry while retaining original weights/IDs/color/UV shifts and
texture data; it does not claim original scene or complete M8-GAME acceptance.
CPU tests retain the 256 MiB boundary and add the exact 384 MiB boundary,
seven-layer overflow, invalid profiles and cache/validation downgrade rejection.

Final regression: **118/118 CTest, including 64 GPU**, 303.90 seconds, plus
**20 Python tests**. The real profile run adds **3,456 independent RGB channel
checks**. Vulkan validation reports zero errors and zero warnings. All 64
previous recorded captures remain byte-identical. Evidence, original-input
verification and the client-date scope amendment are in `build/stage8-terrain-extended`.
The current game project preparation, actual `stalker2.exe` mixed-scene launch,
editor outputs and final audit remain in the migration plan.

## M8 Game Thread Bootstrap (2026-10-17, Partial M8-GAME)

Production engine startup now initializes the Vulkan renderer/session and the
external particle cooks before creating the game world. The logic channel owner
initializes through an APC with completion acknowledgement before
`initialize_logic_modules()` can reach `game::query_scene()`. `initialize_game()`
registers particle cooks on the render owner. The particle archive is linked
privately into the Vulkan DLL; particle tests use its exports instead of owning
another archive copy. Existing component-level `initialize()` remains available
for tests that deliberately control cook registration.

Shutdown first quiesces producers, processes their commands, closes scene-owned
worlds on the render owner and tears down GPU state. Retained logic objects and
late resource callbacks can still hold dependencies. After logic module
destruction and the existing final callback drain, production requires
`finalize_game_resources()` to succeed before destroying the renderer or stopping
resource workers. A pending query, live cook user, foreign registration owner or
open session prevents premature finalization. Logic's frame-throttling counter
is atomic on the Vulkan path.

`xray_render_stage8_game_threads_gpu` uses the real APC implementation, separate
logic/resource/cook threads, production game renderer initialization and a hidden
nonactivating/input-transparent output. It runs three complete renderer restarts
with real cooked scene/view/output, static material/model and particle instance
dependencies. It checks callback affinity, foreign-thread simulation rejection,
copied facade play/submission, 81 independently calculated RGB channels, pending
query failure after close, retained references blocking unregister and full cook
drain before restart. It does not select `-debug_single_thread`. The resource
harness uses native synchronous disk access with separate resource/cook workers;
it does not launch the production file-device threads or game subsystems.

Final regression: **118/118 CTest, including 64 GPU**, 364.33 seconds, plus
**20 Python tests**. Vulkan validation reports zero errors and zero warnings;
the new threaded case checks each restart before diagnostics reset. The recorded
manifest/inventory and 61 earlier captures remain unchanged. Evidence, binary
hashes, the source snapshot and verifier are in `build/stage8-game-threading`.

This is an intermediate threading proof, not acceptance of
`stalker2.exe -level=vulkan_m8_control`: preparing and
mounting the mixed project, all manifest workflows and the production host
load/unload/reload/shutdown still belong to M8-GAME. Editor main/preview and audit
remain open. Distortion and SSAO remain required; GI/LPV remain deferred.

## M8 Scope And Particle Scene Integration (2026-10-17)

The [acceptance manifest](m8_acceptance_manifest.json) fixes the target scene,
profiles, independent controls, game/editor launch contracts and pass ordering.
The user selected distortion and SSAO; GI/LPV are explicitly deferred.
The [input inventory](m8_acceptance_inputs.json) identifies 195 original inputs
and four prior provenance records. Verify it without changing stored hashes:

```powershell
C:/msys64/mingw64/bin/python.exe sources/xray/render/vulkan/tests/tools/verify_m8_manifest.py
```

The verifier checks metadata dependency closure, prior provenance hashes and
scope requirements without executing source Lua. `--capture` creates a new
inventory only; it refuses to overwrite an existing one. A defined launch
contract does not mean that its prepared project or editor host already runs.

The existing facade now delivers copied play/stop/update/remove commands to the
scene-owned particle world on its owner thread. Updates include transform,
visibility and pause. Placement shares the snapshot proper-rigid validation
(finite bounded affine matrix, no scale/shear/reflection) before mutation.
World/type/thread guards remain active; stop stops births and lets existing
particles expire, remove releases active membership, and replay resumes the
existing simulation without promising a time rewind.

The channel retains its last processed command and pending payloads until they
are retired or the owner finalizes it. Shutdown closes worlds before dropping
scene dependencies, releases channel payloads, then requires cook/deleter drain.
Tests exercise actual facade queues and production cooked instances, including
copied placement, combined update rejection, pause/resume, 100 repeated plays,
FIFO remove/replay, foreign/type/thread guards and pending shutdown.

Particle-enabled scenes now draw through the production resource/facade path.
The first submission of each scene world in a CPU frame ticks it on its owner
thread; further views capture separate snapshots without another tick. Minimized
submissions still tick without creating particle GPU snapshots/uploads; a frame
submitted while minimized omits particles if restored before presentation.
Unsubmitted scenes do not simulate. The frame shares a
steady-clock delta (initially zero, capped at 0.25 s, no catch-up); typed
`scene_configuration.particle_fixed_timestep` selects a deterministic step in
(0, 0.25], with zero retaining wall time. Invalid or unused fixed steps fail
before dependency queries. Each draw owns copied billboard vertices/indices,
retained immutable texture data and its camera. Later mutations cannot alter it.

The pass follows opaque/terrain and lit forward geometry, before debug/UI.
Unlit views blend into the color target; final lit views blend in linear HDR
before bloom/tonemap with the existing opaque depth, depth test on and depth
writes off. Particle-enabled lit scenes require 1x and post-AA off, including
empty scenes. Diagnostic GBuffer modes fail explicitly. HDR and existing bloom
remain available; ordinary game/editor integration is still M8-GAME/M8-EDITOR.
All submitted views share 64 batches / 8 MiB geometry / 64 MiB texture budgets,
and at most 64 scene worlds per frame. The renderer cache retains at most two
bounded generations and uses existing fenced retirement. Enumeration avoids
legacy center-only bounds; expanded quads are clipped by the GPU so a center
outside the view cannot hide a partially visible billboard.

`xray_render_stage8_particle_submission_gpu` uses production cooks and facade
commands to verify empty opaque content, independent alpha/additive/depth/HDR
pixels, paired cameras, immutable queued remove, local rotated/edge placement,
once-per-world ticks, visibility/pause/stop/expiry, world isolation, resize and
minimize/restore, clock/profile/budget rejection and GPU retirement drain.
The earlier scene tests still verify foreign/type/thread and pending/retained
shutdown behavior. Raw-world, preview, mode and slomo APIs remain errors.

`tests/tools/prepare_real_particle_pack.py` prepares a new read-only-source
candlelight projection with byte-identical original TGA/options and inherited
emissive/transparency constants. Its provenance lists original SHA-256, outputs,
fixed simulation/shape parameters and omitted graph/shader behavior. The full
original atlas is sampled at unit UV; subUV animation is not implied. The
supported shader multiplies texture alpha; the original disables that input,
but this texture's alpha is uniformly 255. Both alpha and additive controls
are explicit projections, not original effect parity. A Pillow-decoded RGBA
oracle feeds independent CPU bilinear/sRGB/blend checks in the scene test.

```powershell
python sources/xray/render/vulkan/tests/tools/prepare_real_particle_pack.py --source-root C:/Stalker2_2011/xr2-dsgn/resources --output build/stage8-particle-submission/real-pack --lua C:/msys64/mingw64/bin/lua.exe
binaries/xray_render_stage8_particle_submission_tests.exe build/stage8-particle-submission/real-captures --real build/stage8-particle-submission/real-pack
```

Use Python with Pillow, the existing strict validation/DXC environment and a new
output directory for preparation. Capture output must not overlap the mounted
pack. Evidence is in `build/stage8-particle-submission`; previous queued-command
evidence remains in `build/stage8-particle-commands`.

Acceptance: GCC 16.2.0 Debug, RTX 4070 / driver 617.42; 114/114 CTest cases
(60 GPU) in 300.67 s and 17 Python tests passed. The real projection passed
594 independent RGB checks across alpha/additive captures. Validation reports
zero errors/warnings through teardown. The 15 prior billboard/material/simulation
captures are byte-identical. Original input inventories and archived plan bytes
are unchanged; `summary.json` records the source snapshot and exact evidence.
Only bounded M8-PARTICLES is accepted here; M8 as a whole remains open.

## M8 Fixed Highest-LOD Vegetation (2026-10-17)

`speedtree_class` and `speedtree_instance_class` are owned by the renderer's
scene cook group. Named requests load a versioned prepared `.tree` descriptor,
existing static mesh/material dependencies and checked `VSTCARD1` leaf data.
Raw SRT/source Lua are not interpreted by the runtime. The offline
`tests/tools/prepare_real_vegetation_pack.py` reads the bounded SRT 05.2.0
profile with `read_speedtree52.py`, records input/output SHA-256 and explicit
material omissions, and copies original TGA/options byte-for-byte. It refuses
existing output directories and overlap with original resources. The public
reader used to cross-check the serialized layout is pinned in its provenance;
no external SDK or downloaded parser code is linked or executed.

The eastern_white_pine selection has branch/frond/leafcard components (no
leafmesh), totaling 7639 vertices / 5642 triangles / 657 cards at highest LOD.
Packed normals/tangents/binormals, positions and UV are retained. Card centers,
dimensions, pivots, initial LOD scale, planar offsets and UV produce a fresh
camera-facing mesh for each submitted view. Expanded geometry and bounds are
captured with the view and retained through queued drawing. The same captured
mesh participates in existing shadow passes. Public asset bounds conservatively
contain every card orientation; the real fixture checks three camera bases.
No wind, morph, distance or
billboard LOD transitions, vertex AO, collision or SDK forest acceleration is
implied; complete original effect/shadow parity is not claimed.

The existing facade copies add/update transforms and surface names. Vulkan
implements add/update/remove/material, and the new
`set_speedtree_instance_visible(instance, bool)` queues visibility. Scene and
resource ownership, owner thread, proper affine rotation/translation and
positive uniform scale are checked before mutation. Materials retain the
existing opaque/cutout contract. Tree membership is current immediately for
both values of the legacy populate hint; `populate_speedtree_forest` validates
the current membership list without a separate acceleration rebuild. Selection
highlighting now reports an unsupported error. Limits are 64 trees / 262144
vertices per scene and 16 MiB expanded leaf geometry per CPU frame across views.
Final lit rendering requires 1x with post-AA off.

Minimized submissions process membership/transform commands without preparing
tree GPU geometry. Particle simulation also continues without GPU uploads.
Their queued frame omits tree/particle draws even if the window is
restored before presentation; the next drawable submission captures the latest
camera/placement. A repeated minimize/update control checks stable GPU buffer
counts and the restore-before-end-frame transition.

`xray_render_stage8_vegetation_gpu` verifies production cooks/facade, exact
synthetic alpha-cutout/depth/material/HDR pixels, copied transforms, two cameras,
queued remove, visibility/culling, malformed profiles/data/transforms, owner
thread, resize/minimize, retained pending close and cook/GPU retirement drain.
The real fixture uses 128 MiB of managed raw-file test arena for the original
multi-megabyte atlases. An independent CPU triangle raster checks 485 samples
(133 foreground); 7884 comparisons check source leaf-card placement. Authored
surface textures and lit directional-shadow captures are retained separately
from the analytic constant-color geometry control.

```powershell
python sources/xray/render/vulkan/tests/tools/prepare_real_vegetation_pack.py --source-root C:/Stalker2_2011/xr2-dsgn/resources --output build/stage8-vegetation/real-pack --lua C:/msys64/mingw64/bin/lua.exe
binaries/xray_render_stage8_vegetation_tests.exe build/stage8-vegetation/real-captures --real build/stage8-vegetation/real-pack
```

Use the strict validation/DXC environment above. Capture output cannot overlap
the mounted input pack. Evidence and final source identities are recorded in
`build/stage8-vegetation`; ordinary threaded game/editor startup remains open.

Final verification: 115/115 CTest tests passed (61 GPU, 311.39 seconds), plus
20 Python tests without skips. The real tree passed 10794 checks. Validation
reported zero errors/warnings through teardown. Prior particle captures remain
byte-identical; original resource inventories and archived plan bytes are
unchanged. `summary.json` records the source snapshot, input/output hashes and
the bounded acceptance result for M8-VEGETATION.

## M8 Projected Decals (2026-10-17)

The renderer's existing material cook accepts an exclusive `decals` stage with
`decals_default`, diffuse/normal maps, tint, normal multiplier and geometry-normal
blending. It publishes a distinct `decal_material_resource` after both texture
dependencies succeed; static/terrain/particle materials cannot substitute for it.
The runtime reads prepared binary material configs. The bounded offline
`tests/tools/prepare_real_decal_pack.py` resolves the selected original material,
records its retained/omitted fields and hashes, and copies TGA/options unchanged.
Existing output paths and overlap with original resources are rejected.

Scene facade add/update/remove copy `decal_properties` and retain the material.
IDs are scene-local; duplicate add and unknown update/remove fail before mutation.
Projectors use proper rigid transforms and centered stored half extents in
[0.001,128]. The legacy convenience constructor already halves full dimensions;
callers setting fields directly supply half extents. Static/terrain receiver flags
are honored independently. Other receiver flags, angle fading/clipping and
reflection report unsupported errors. Camera views must be rigid; finite forward-Z
perspective and orthographic projections use the existing GBuffer camera contract.
Rendering requires a deferred view, 1x and post-AA off.

Packed-depth G now identifies static (1/4), terrain (1/2), or excluded (0) receivers;
other depth/material channels keep their meanings. Each full-screen bounded-volume
projection samples immutable diffuse/normal snapshots and the preceding albedo,
normal and packed-depth targets. View positions are reconstructed from the
existing 16-bit square-root linear-depth R channel and the inverse projection.
Two separate ping-pong targets receive the next
albedo/normal values before lighting. No attachment feedback or depth writes occur.
Normal-map XY use the existing AG packing convention; the projector tangent is
orthogonalized against the receiver normal when geometry blending is selected.
Normalized normal interpolation and diffuse alpha blending preserve the receiver
outside the volume and at zero alpha. Roughness, Fresnel, specular and emission
remain receiver properties. Decals apply in insertion order; update keeps order,
remove/re-add moves the decal to the end. Minimized submissions prepare no decal
images or projections. Queued views retain their own properties and texture versions.

Limits are 16 decals per scene, 64 projections / 64 MiB unique textures per CPU
frame across views, and 128 cached image versions / 128 MiB retained uploads.
Unused image handles retire by the existing GPU serial contract. The accepted
source pack has six original inputs and five outputs; it is a supported material
projection, not complete original effect/scene parity.

`xray_render_stage8_decals_gpu` checks analytic alpha/normal/HDR pixels, copied
commands, moved/removed decals, perspective and occluder depth, actual terrain
receivers, class flags, scene/frame limits, malformed/foreign/thread failures,
split-view snapshots, successful/failed texture reload, resize, repeated minimized
updates, GPU retirement drain, and retained/pending close callbacks.
`verify_real_decal_capture.py` independently samples the original RGBA TGAs with
Pillow and computes the receiver basis, alpha, normal and ambient/Reinhard output.
Its fixed 255 positions include 50 authored foreground samples and 3060 channel
checks; maximum error is one byte (removed identity zero).

```powershell
python sources/xray/render/vulkan/tests/tools/prepare_real_decal_pack.py --source-root C:/Stalker2_2011/xr2-dsgn/resources --output build/stage8-decals/real-pack-validated --lua C:/msys64/mingw64/bin/lua.exe
binaries/xray_render_stage8_decal_tests.exe build/stage8-decals/real-captures --real build/stage8-decals/real-pack-validated
python sources/xray/render/vulkan/tests/tools/verify_real_decal_capture.py --pack build/stage8-decals/real-pack-validated --captures build/stage8-decals/real-captures --report build/stage8-decals/real-oracle.json
```

Use the strict validation/DXC environment above and the bundled Python with
Pillow. Input pack and captures must not overlap. Evidence is retained in
`build/stage8-decals`; threaded game/editor startup remains separate M8 work.

Verification accepted all 116 unique CTest cases (62 GPU): 115 passed in the
full run; one negative DXC fixture needed to rename the new `surface.w` use too.
The corrected fixture and three new decal ABI mutation checks passed on rerun;
the renderer implementation was unchanged by that test-only repair. All 20 Python
tests passed without skips. GPU validation reported zero errors/warnings through
teardown. `summary.json` reconciles the runs and records source/input/output and
capture identities, including byte-identical earlier vegetation/particle controls.

## M8 SSAO and Distortion (2026-10-17)

`postprocess_settings` adds disabled-by-default `ssao_enabled` and
`distortion_enabled`. The existing facade copies the complete settings per queued
view. Both effects require final lit rendering, 1x and post-AA off. Validation runs
when settings are assigned and again at scene submission, so enabling the master
postprocess switch cannot bypass the profile. Existing AA/material/lighting paths
with both new flags disabled retain their behavior.

SSAO reconstructs view positions from sampled hardware depth and inverse
projection, using post-decal GBuffer normals. Its fixed eight axial/diagonal
neighbors are `ssao_pixel_radius` output pixels away (1..32). The angular bias is
`ssao_bias` [0,1), the view-space range is `ssao_world_radius` [.01,128], and strength
is `ssao_strength` [0,4]. Samples outside the queued viewport or at background depth
do not occlude. A separate 3x3 filter accepts neighbors within
`min(.1, range*.1)` view-depth units and normal dot >= .8. Raw and filtered AO use
separate RGBA16F targets. Only the existing ambient albedo term is multiplied by
AO; direct diffuse/specular, emission, fog and sky retain their own contracts.
Zero strength and disabled SSAO have independent identity controls.

The production material cook accepts an exclusive four-field `distortion` stage:
`distortion_panner`, `texture_distortion`, `distortion_scale` and `panner`.
Both vectors are float3; only XY are used as in the source panner shader.
Scale channels must be [0,64], panner channels [-16,16]. Texture dependencies are
linear RGBA8, repeat addressing and LOD0; original TGA/options are copied unchanged,
while the runtime deliberately treats sampled RG as vectors rather than sRGB color.
The material remains owned by the existing material class/cook and is assigned to
immutable static-model geometry using the existing facade. Distortion surfaces
are excluded from opaque/GBuffer, lighting and shadow-caster lists. Vegetation
and skeletal distortion geometry report unsupported errors.

The field graphics pass uses the existing static VS/buffers and a new fragment
shader with opaque depth testing and no depth writes. XY are sampled at
`UV + panner.xy * distortion_time`; alpha is sampled at unmoved UV. Signed offsets
are `((RG*2-1) * scale.xy / 255) * alpha`. The copied host phase
`distortion_time` is [0,3600] seconds; runtime wall time is not implicit. Later
overlapping geometry replaces the preceding field value. A separate HDR composite
runs after the existing HDR forward pass and before bloom/tonemap. It clamps to
`distortion_max_pixels` [0,64] and the queued viewport's covered pixel centers,
and retains the original pixel if the destination sample leaves the visible field.
HDR source, field and output are distinct targets. UI remains in its existing
display-space pass. Geometry commands, texture reload and settings changes cannot
rewrite already queued views.

Distortion budgets across all queued views are 64 surfaces, 8 MiB unique geometry
and 64 MiB unique texture bytes per CPU frame. Minimized submissions capture no
field geometry or effect work, even if restored before GPU end-frame. Target
resize is transactional; shader/target creation failures report unsupported or
unavailable resources and clean partial allocations. Required capabilities are
the baseline sampled depth plus sampled/renderable single-sample RGBA16F and
ordinary VS/PS. No geometry/tessellation/compute stage is enabled. GI/LPV remain
outside the selected scope.

`xray_render_stage8_effects_gpu` checks known offsets, panner phases, zero/disabled
identity, opaque occlusion, queued settings/remove/reload, SSAO corner/filter/normal
response, orthographic/perspective, direct-light isolation, invalid modes/parameters,
submission limits, resize/minimize, pending close and GPU/cook drain. The DXC suite
checks both effect ABIs and rejects malformed SSAO constants/descriptors.
`prepare_real_effects_pack.py` retains the original named distortion material,
TGA/options and reference shader identities (12 original inputs). Its five outputs
include two explicitly synthetic gradient controls. `verify_real_effects_capture.py`
uses Pillow and an analytic receiver to check 1755 RGB channels at 195 fixed points
in each of three captured phases; maximum error is one byte. This is bounded
effect projection, not original SSAO random/noise/kernel or original scene parity.

```powershell
python sources/xray/render/vulkan/tests/tools/prepare_real_effects_pack.py --source-root C:/Stalker2_2011/xr2-dsgn/resources --output build/stage8-effects/real-pack --lua C:/msys64/mingw64/bin/lua.exe
binaries/xray_render_stage8_effects_tests.exe build/stage8-effects/real-captures --real build/stage8-effects/real-pack
python sources/xray/render/vulkan/tests/tools/verify_real_effects_capture.py --pack build/stage8-effects/real-pack --captures build/stage8-effects/real-captures --report build/stage8-effects/real-oracle.json
```

Use the strict validation/DXC environment above and bundled Python with Pillow.
The preparer refuses existing outputs and overlap with originals. Evidence is in
`build/stage8-effects`; full mixed threaded game/editor workflows remain M8 work.

Final verification passed 117/117 CTest cases (63 GPU, 761.87 seconds), 20 Python
tests without skips, and 67 DXC compile/ABI mutation rejections. Strict validation
reported zero errors/warnings through teardown. The original input inventories,
archived plan and earlier vegetation/particle/decal capture identities are
unchanged. `summary.json` records the source snapshot and exact evidence.

## Build Ownership

The target C++ toolchain is GCC / MinGW-w64 with Ninja, for Debug and Release.
MSVC support is outside this migration's scope, not a deferred acceptance task.
DXC separately compiles HLSL to SPIR-V; it does not replace GCC for C++.

### Windows x64 Source Cleanup (2026-10-08)

The maintained platform is Windows x64 only. CMake rejects unimplemented
operating systems and incompatible pointer ABIs; engine/CS headers also reject
other CPU architectures, including GCC `-m32`. Linux is a future port, not a
current option. Platform selection branches and obsolete project metadata have
been removed from project-owned code. Upstream vendor portability internals
are not engine build choices. See `sources/README.md` for the dependency scope.

Compiler dependencies were audited before removing unused source trees.
Bundled Boost, BugTrap, FastDelegate, ODE, OPCODE, Ogg and Vorbis remain active.
System Boost library discovery does not replace the bundled Boost headers.
At the user's request, unported DX/editor SDK adapters remain reference code;
they are not additional supported targets. Archive readers retain data-layout
compatibility, while archive writers select only the PC format.

The audit also found seven auxiliary targets consuming system sol2 with
duplicate Lua headers. They now explicitly link the project `sol2::sol2`
interface, as does fs. Script/config headers reject missing usage requirements.
The clean compiler graph contains neither removed trees nor system sol2 or
duplicate Lua headers. CS/core/renderer/binding-test PE imports use `lua55.dll`.

The clean GCC 16 Debug build is `build/windows-x64-clean`. All 48 CTest tests
pass, including 21 GPU tests and the new source/mutex contracts; all nine
Python tests pass. Six negative compiler checks and a negative OS configure
check confirm the platform guards. Logs and deletion manifests are in
`build/platform-cleanup`.

7351 files from unused source trees, project metadata and stale Lua artifacts
were removed, including 742 obsolete project files.
The temporary sandbox copy of those files was not retained; no recoverable
source backup archive exists. A separate set of 1523 obsolete build artifacts
was archived and verified before removal. At the user's subsequent request,
that archive, legacy `.orig`/`.old` copies and a Visual Studio backup layout
were permanently deleted: 23 files, with no replacement backups created.
The deletion record is `build/platform-cleanup/backup-removal-manifest.json`.
Working sources, retained reference code, tests and current dependency
distribution archives were not removed.

Both real source-derived lit packs passed 16 cycles, with validation 0/0,
stable GPU baseline/final counts `[6,11,10,2,0]` and complete cleanup. All 40
RGB captures match the preceding system-Lua55 reports; all 22 original SHA256
values remained unchanged. Reports are in
`build/real-assets/{barrel-radiation,woodbox-mil}-windows-x64-clean-gpu/report.json`;
pixel comparisons and hashes are recorded in `build/platform-cleanup`.
Woodbox maximum query/reload was 8306/8357 ms; this is not a controlled
performance benchmark or a diagnosis of the previously observed timing variance.
GCC Release and full game/editor/debugger startup were not verified here.

## Vulkan API Baseline

The application targets Vulkan 1.4. CMake requires Vulkan development files
1.4 or newer; runtime initialization requires both a Vulkan 1.4 loader and a
suitable Vulkan 1.4 GPU. There is no silent 1.3 fallback. Presentation-fence,
queue, format and optional shader/texture feature checks remain in effect.
Requesting 1.4 does not enable every 1.4 feature or implement new rendering passes.

Diagnostics distinguish the application's instance target, loaded loader API,
physical-device API and compile-time headers. Asset probe reports expose these
as `vulkan.instance`, `vulkan.loader`, `vulkan.device` and `vulkan.headers`.
Header/loader package patch versions need not equal the GPU driver's patch.
Updating MSYS2 does not update the NVIDIA/AMD/Intel graphics driver.
Windows uses the installed system runtime, `%SystemRoot%/System32/vulkan-1.dll`.
CMake does not deploy an app-local Vulkan loader. MSYS2 development headers and
the import library remain required for GCC; its package DLL is not deployed.
Update the Windows runtime with the signed
[LunarG runtime installer](https://vulkan.lunarg.com/sdk/home/), independently of
MSYS2 and the graphics driver. A full Vulkan SDK is not required for this update.
The `xray_vulkan_system_loader` test requires API 1.4 and verifies the actual
loaded module path against the Windows system directory, rejecting shadow DLLs.

On 2026-10-08 the official x64 runtime installer updated this machine from
1.4.341.0 to 1.4.363.0, with SHA256 and Authenticode verification, exit code zero
and no reboot required. The app-local DLL and its CMake copy rule were removed;
the obsolete `XRAY_VULKAN_LOADER_RUNTIME` cache setting is no longer used.
Installation and regression records are in `build/system-vulkan`.
The GCC Debug build, 49/49 CTest tests (21 GPU) and 9/9 Python tests passed.
An isolated app-local shadow DLL was correctly rejected by the new loader test
and removed. Both real source-derived lit packs passed 16 cycles with validation
0/0 and stable GPU counts `[6,11,10,2,0]`; their actual probe processes loaded
the System32 DLL. All 40 RGB captures match the preceding clean-build reports,
and all 22 original SHA256 values remained unchanged. Reports are in
`build/real-assets/{barrel-radiation,woodbox-mil}-system-vulkan-gpu/report.json`.
These checks do not extend acceptance to full game/editor startup or GCC Release.

DXC still uses `-fspv-target-env=vulkan1.3`: its SPIR-V 1.6 is also valid on
Vulkan 1.4. This deliberately preserves the tested shader capability contract;
no compiler flag or optional feature is enabled solely to change a version label.
See the [Khronos version guide](https://docs.vulkan.org/guide/latest/versions.html).

### MSYS2 Toolchain Refresh (2026-10-08)

A full MSYS2 upgrade updated 120 installed packages and added nine dependencies.
The verified package baseline is GCC 16.2.0-4, Boost 1.92.0-3, CMake 4.4.4-3,
Vulkan headers/loader 1.4.363.0-1, sol2 3.5.0-2 and mimalloc 3.4.4-1.
SDL2 and Ninja were already current. Package manifests and transaction logs are
in `build/msys2-packages-{before,after}-vulkan14.txt` and
`build/msys2-upgrade-vulkan14-*.log`. The final full upgrade found nothing to do;
`pacman -Dk` found no database errors and key toolchain packages passed `-Qk`.

Mimalloc was absent from the synchronized MINGW64 database. Its official
MINGW64 archive was installed locally with pacman, after checking its published
SHA256 and fully trusted MSYS2 development-key signature. It consequently still
appears in `pacman -Qm`; future full upgrades may not update it automatically.
The package is not a UCRT64/CLANG64 substitute. See the
[official package entry](https://packages.msys2.org/packages/mingw-w64-x86_64-mimalloc).
Post-install hooks affected by an incomplete PowerShell PATH were rerun by
reinstalling the affected packages inside MSYS bash with `/usr/bin` on PATH;
`build/msys2-upgrade-vulkan14-hooks-repair.log` records successful completion.

The initial toolchain refresh retained bundled Lua 5.4.7; the subsequent
system-Lua migration below supersedes that arrangement.
Updated Asio needs Windows NLS declarations for UTF-8 file paths; the network
PCH now removes `NONLS` before including Windows headers, locally to that module.

Do not reuse GCC 15 object files or cached Boost 1.90 paths after this upgrade.
The current clean GCC 16/system Lua build directory is `build/lua55-gcc16`:

```powershell
$env:PATH="C:/msys64/mingw64/bin;$env:PATH"
cmake -S sources -B build/lua55-gcc16 -G Ninja `
  -DCMAKE_BUILD_TYPE=Debug -DBUILD_TESTING=ON `
  -DCMAKE_C_COMPILER=C:/msys64/mingw64/bin/cc.exe `
  -DCMAKE_CXX_COMPILER=C:/msys64/mingw64/bin/c++.exe `
  -DCMAKE_PREFIX_PATH=C:/msys64/mingw64 `
  -DXRAY_ENABLE_DXC_TESTS=ON -DXRAY_ENABLE_VULKAN_GPU_TESTS=ON
cmake --build build/lua55-gcc16 -j 8
# Set the validation/DXC environment documented below before GPU tests.
# Process-local overlay isolation on this machine; Khronos validation stays active.
$env:DISABLE_RTSS_LAYER='1'
$env:DISABLE_VULKAN_OBS_CAPTURE='1'
ctest --test-dir build/lua55-gcc16 -j 1 --output-on-failure
```

All build directories share `binaries/`; do not build or test different
toolchains concurrently. Older verification commands below use the historical
`build` directory; substitute `build/lua55-gcc16` for current CMake/CTest runs.
Portable DXC and validation layers remain under `build/dxc` and
`build/vulkan-validation`, independently of the new CMake binary directory.

### System Lua 5.5 (2026-10-08)

Lua is now the system MSYS2 MINGW64 Lua 5.5.1 shared runtime (`lua55.dll`),
linked through `liblua.dll.a`, not the bundled Lua C sources. CMake 4.3+ is
required for Lua 5.5 discovery. Lua 5.5.1+ within the 5.5 series is accepted;
future minor versions and MinGW static Lua libraries are rejected.
Keep `C:/msys64/mingw64/bin` on PATH when running the engine or tests. CMake
does not copy a Lua DLL into `binaries/` or change system packages/settings.

The project owns a complete sol2 3.5.0-2 header snapshot with a small Lua 5.5
compatibility patch in `sources/third_party/sol2`; the installed sol2 headers
are untouched and are no longer a build dependency. `sol2::sol2` propagates
the same system Lua headers/import library to every consumer. CS wrappers
also use those headers. The regression test includes CS wrappers before sol2
so their header guards cannot hide a mismatch. `sources/lua` contains only
the system-runtime CMake interface and regression tests.

The patch supplies the new state hash seed, prevents legacy-header discovery,
and adapts the changed garbage collector API. GC mode switches on 5.5 have
no tuning arguments; tune via `LUA_GCPARAM` instead. `step_gc` retains sol2's
kilobyte input and passes `size_t` bytes to Lua. See
`sources/third_party/sol2/README.md` for patch ownership and refresh rules.

`xray_lua_sol_binding` covers the header/runtime and numeric ABI, 5.5 syntax,
config tables, protected/type errors, userdata finalization, coroutine resume
across GC, instruction hooks, custom allocator cleanup and both GC modes.
Texture-options tests retain their no-standard-libraries, memory and instruction
bounds. This does not certify full game/editor script startup or debugger UI.
Lua 5.4 bytecode must be regenerated and native Lua modules rebuilt for 5.5;
source compatibility is not universal
(reserved `global`, read-only loop variables). See the
[Lua 5.5 incompatibilities](https://www.lua.org/manual/5.5/manual.html#8).

Verified with a clean GCC 16 Debug build in `build/lua55-gcc16`: all targets
build, 46/46 CTest tests pass (21 GPU), and 9/9 Python tests pass using system
Lua 5.5.1. PE imports of CS, core, renderer and the binding test use `lua55.dll`.
The subsequent source cleanup found seven auxiliary targets still consuming
system sol2 and duplicate Lua headers. Their missing `sol2::sol2` usage
requirements were corrected before removing those duplicate headers.
The static-Lua configure guard was also exercised. Logs are in that build
directory (`build-system-profile.log`, `build-verified.log`,
`ctest-system-lua55.log`, `python-pillow-tests.log`, `reject-static.log`).

Both real source-derived lit packs passed 16 cycles with validation 0/0,
unchanged GPU baseline/final counts `[6,11,10,2,0]`, drained retirements/cook
users, completed shutdown and unchanged inputs/reload. All 40 RGB captures
match the preceding Vulkan 1.4/bundled-Lua reports, and 22 original SHA256s
were checked before/after. Reports:
`build/real-assets/{barrel-radiation,woodbox-mil}-system-lua55-gpu/report.json`.
Woodbox maximum query/reload was 5341/3274 ms in this run; this is not a
controlled benchmark and does not establish a cause for earlier timing changes.
GCC Release and full game/editor/debugger startup remain unverified here.

On this machine, RTSS and OBS implicit-layer manifests advertise Vulkan 1.3.
The initial 1.4 GPU run therefore reported layer-version warnings and failed
the zero-warning acceptance checks. Use the manifests' `disable_environment`
switches above for isolated tests, not global registry/settings changes.
`VK_LOADER_LAYERS_DISABLE` is not used for this zero-warning run: the loader
reports forced filtering as warnings too. Validation warnings are never ignored
or excluded from the renderer's counters. See the
[Khronos loader layer interface](https://github.com/KhronosGroup/Vulkan-Loader/blob/main/docs/LoaderLayerInterface.md).

Verification: the full clean Debug build (including stalker2 and editor DLLs)
succeeded; all 46 CTests passed in 92.30 seconds, including 21 GPU tests, and
all nine preparation-tool Python tests passed. Logs:
`build/vulkan14-gcc16-build*.log`, `build/vulkan14-msys2-regression-final.log`,
`build/vulkan14-python-tools-tests.log`. GCC Release and game/editor startup
were not newly verified by this toolchain refresh.

Both source-derived real packs passed 16 lit cycles with validation 0 errors /
0 warnings, unchanged inputs, drained cooks/retirements, stable capacity and
matching baseline/final GPU counts `[6,11,10,2,0]`. Reports:
`build/real-assets/barrel-radiation-vulkan14-msys2-gpu/report.json` and
`build/real-assets/woodbox-mil-vulkan14-msys2-gpu/report.json`. They record
instance 1.4.0, loader/headers 1.4.363 and RTX 4070 device API 1.4.351.
All 40 BMP captures match the previous lit reports pixel-for-pixel in RGB
(GDI alpha bytes excluded); both lit captures were visually inspected.
See `build/vulkan14-real-assets-frame-comparison.log`. All 22 original input
SHA256 hashes matched provenance before and after these probes; this remains
a source-derived preview verification, not production-cook/PBR acceptance.

Timing caveat: woodbox maximum Debug model-query/unchanged-reload times were
8427/8254 ms versus 5049/4800 ms in the previous lit report. Barrel measured
530/461 ms. These runs are not a controlled benchmark and the cause of the
woodbox slowdown has not been established. The 30-second bounded contracts
passed; no Debug checks were disabled to improve the timing. Performance
profiling and Release measurement remain separate work, not a closed result.

## Renderer Module Ownership

`xray_render_vulkan` is a shared library. It owns the current facade, the
world implementation in `vulkan_world.cpp`, Vulkan scene cooks/lifecycle, remaining feature stubs,
all render allocator
globals, and the RHI singleton. Engine/editor consumers link this target rather
than compiling those translation units again. Legacy base/platform facade
implementations are deliberately excluded.

The target publishes `USE_VULKAN_RENDERER=1` and `XRAY_RENDER_SHARED=1`.
Building/export definitions remain private. The current CMake build explicitly
rejects `USE_VULKAN_RENDERER=OFF`; this is not a working DX11 fallback.

Deploy `libxray_render_vulkan.dll` alongside the executable on MinGW (the DLL
name may differ on other toolchains). The existing output directory is `binaries`.

## Lifetime and Allocation

Call `render::set_memory_allocator(render, logic, editor)` before creating the
facade world or initializing Vulkan. The render allocator also supplies Vulkan
allocation callbacks. All allocators must outlive the renderer and its resources.
Callbacks preserve alignment and realloc contents, handle null/zero-sized
operations, and leave the original allocation intact on allocation failure.

`rhi_device::initialize(std::unique_ptr<rhi_backend>, HWND)` takes ownership of
a backend created with standard `new`/`std::make_unique`, not `XRAY_NEW`.
The caller must check its boolean result. Initialization failure cleans up the
candidate without exposing it through `get_backend()`. Reinitializing a live
device is rejected. `shutdown()` releases the backend and is idempotent.

Destroy the facade world before the allocators. World destruction shuts down
RHI before clearing allocator globals and Vulkan callbacks. The standalone RHI
user must explicitly shut it down before resetting the Vulkan allocator.

For the shared core, legacy global CRT overrides are disabled: DLL CRT startup
allocates before engine allocators exist. Explicit X-Ray allocation and ogg
bridges remain active. An uninitialized fixed-size allocator can be destroyed
without attempting to destroy its unconstructed implementation.

## Frame Contract

```text
configure clear values
begin_frame() -> record commands -> end_frame() [submit] -> present()
```

Use the RHI device's frame methods so its state checks apply. `present()` never
begins the next frame. Invalid ordering returns false without executing backend
operations. A false `begin_frame()` can indicate a skipped frame (for example,
swapchain recreation); do not issue commands or submit that frame. Exceptions
in frame operations and failed submit/present shut down the device; inspect
`get_backend()` before retrying or reinitializing. Backend shutdown must remain
safe after partial initialization and repeated calls.

Resource handles carry a nonzero generation and slot index. Releasing or
clearing a pool invalidates its old handles; generations survive `clear()`.

## Verification

From the repository root, using the existing configured build:

```powershell
cmake --build build -j 4
ctest --test-dir build -R '^xray_render_stage(0|1_facade)' --output-on-failure
```

`xray_render_stage0` tests handles, mock backend ownership/failure rollback,
frame ordering, and aligned allocation edge cases without a GPU.
`xray_render_stage0_runtime` loads the actual renderer DLL and checks shared
allocator identity, real Vulkan callbacks, and the exported RHI singleton. It
does not create a Vulkan instance or render a frame. Tests require
`BUILD_TESTING=ON` and the runtime dependencies on PATH.

`xray_render_stage1_facade` checks null/invalid HWND failure propagation through
the actual facade/world, command-channel execution, command deferral flags,
initial counters and repeated cleanup without creating a Vulkan instance.

## Stage 1 Smoke Bootstrap

`xray_vulkan_smoke` is a minimal engine bootstrap with a real Win32 window. It
creates the current facade/world and submits `game_renderer().end_frame()`
through the real logic command channel. No game assets, resource cooks, sound,
network or full `game::query_scene()` are started. The normal engine also passes
its render HWND through `engine_renderer().initialize(HWND)` and checks failure.
This does not make normal game startup or scene rendering complete.

```powershell
./binaries/xray_vulkan_smoke.exe --frames=180 --self-test --capture=build/vulkan-stage1-frame.bmp --log=build/vulkan-stage1-validation.log
```

Without `--frames`, the window runs until closed. `--self-test` resizes,
minimizes/restores, verifies CPU progress while presentation is skipped, clears
resources, reinitializes the same world while minimized, restores it and presents
four more frames. `--capture` saves and checks the visible client clear color;
keep the window unobscured. Captures are BMP files. Exit codes are 0 for pass,
1 for failure, 2 for final-session validation errors, 64 for invalid arguments,
and 74 for an unavailable log file. First-session validation errors also fail.

Requirements: Vulkan 1.4 loader/device, graphics and present queues, usable color
surface formats and depth format, `VK_KHR_swapchain`, instance
`VK_KHR_get_surface_capabilities2` + `VK_EXT_surface_maintenance1`, and device
`VK_EXT_swapchain_maintenance1` with its `swapchainMaintenance1` feature. This
Stage 1 path deliberately rejects devices without presentation fences instead
of treating `vkDeviceWaitIdle` as proof of presentation completion. Supporting
older WSI through deferred swapchain retirement is future work.

There are two CPU frame slots, per-image depth attachments and present
semaphores, submit fences and presentation fences. Minimized/zero-extent frames
advance `frame_id()` without GPU work; `presented_frame_id()` counts successful
frame submit/present cycles (not display-engine scanout). Resize and recoverable
`OUT_OF_DATE`/`SUBOPTIMAL` invalidate the swapchain for the next begin. Recreation
waits for rendering and presentation and rebuilds render-pass-dependent objects.
Submit fences reset only immediately before submission. Fatal recording/submit/
present errors release the RHI backend; no subsequent frame waits on a fence
that a failed submission cannot signal. Automatic device recovery is not added.

Synchronization follows the Khronos guidance on
[per-image semaphore reuse](https://docs.vulkan.org/guide/latest/swapchain_semaphore_reuse.html)
and [presentation-safe swapchain recreation](https://docs.vulkan.org/samples/latest/samples/api/swapchain_recreation/README.html).

## Validation and GPU Tests

Debug requests `VK_LAYER_KHRONOS_validation`, `VK_EXT_debug_utils` and, when
advertised by the layer, synchronization validation through
`VK_EXT_validation_features`. Missing validation produces a visible warning,
not a claim of validation success. Set `XRAY_VULKAN_REQUIRE_VALIDATION=1` to make
missing/unloadable validation a startup failure, also in Release. GPU CTest
enables that strict mode. Diagnostics are written to stderr and the debugger
before core logging is initialized; `diagnostics()` exposes the most recent
session's GPU, API, validation counters, swapchain count and last error.

GPU tests are opt-in and require a Windows session supporting Win32/Vulkan WSI.
Automated windows run hidden without activation; desktop visibility and unoccluded
windows are not prerequisites. Windows services/headless sessions were not tested.

```powershell
cmake -S sources -B build -DXRAY_ENABLE_VULKAN_GPU_TESTS=ON
cmake --build build -j 4
ctest --test-dir build -R '^xray_render_stage' --output-on-failure
```

Use an installed Vulkan SDK validation layer, or set `VK_LAYER_PATH` to a local
directory containing the layer manifest/DLL and put its compatible dependency
DLLs on PATH. For the verified portable setup on this machine:

```powershell
$env:VK_LAYER_PATH = (Resolve-Path build/vulkan-validation/mingw64/bin).Path
$env:PATH = "$env:VK_LAYER_PATH;$env:PATH"
$env:XRAY_VULKAN_REQUIRE_VALIDATION = '1'
```

These are process-local environment changes, not system installation. The
portable layer packages came from the official MSYS2 repository, with SHA256
checked against its package pages: validation layers `1.4.357.0-1`, SPIRV-Tools
`3~1.4.363.0-1` and libstdc++ `16.2.0-4`. Their runtime DLLs and licenses were
extracted only under `build/vulkan-validation`; the GCC 15 build toolchain was
not upgraded. That local test directory is not a distributable engine dependency.

Verified 2026-10-04: full existing Debug MinGW/Ninja build, all four focused CTests,
NVIDIA GeForce RTX 4070 / Vulkan 1.4.351 / driver 617.14. The first GPU session
presents 180 frames, creates two swapchains (942x593 then 1102x673), captures
clear color, and reports 0 validation errors / 0 warnings. The same-world
minimized reinitialization and four subsequent frames also report 0 / 0.
Evidence: `build/vulkan-stage1-validation.log`, `build/vulkan-stage1-frame.bmp`,
and `build/Testing/Temporary/LastTest.log`.

Not verified: clean GCC Release builds, other GPUs/drivers, real hardware
device-loss/submit-failure injection, or all WSI error/format-change variants.
RHI failure rollback is covered with mock CPU tests. Stage 3 adds textures and
descriptors below; Stage 4 adds scene lifecycle and Stage 5 will add model assets.

## Stage 2 Geometry

The renderer now creates immutable vertex/index buffers and VS/PS shader modules.
Shader handles retain their entry names; factories reject bad byte counts,
SPIR-V headers/instruction bounds, mismatched names/stages, push-constant
declarations and capabilities outside basic Shader/Matrix. Stage 3 reflection
checks descriptor declarations against the fixed material ABI. This is a structural
and capability check, not a security validator for arbitrary untrusted binaries.
Use trusted, semantically validated DXC output. Compute/geometry/tessellation
modules and non-solid rasterization are explicitly unsupported here.

Buffers use host-visible memory, preferring coherent memory and flushing the
whole mapped allocation when non-coherent. Data is copied once before first
submission, then unmapped; the API exposes no in-place update. Host-write
visibility comes from queue submission, so this upload path does not require a
transfer/staging barrier. Device-local staged uploads, updates and allocation
suballocation are future work. See the official
[Vulkan upload synchronization examples](https://docs.vulkan.org/guide/latest/synchronization_examples.html).

Vertex input supports one interleaved per-vertex binding, explicit location,
format, byte offset and stride. Supported attributes: float1/2/3/4 and RGBA8
UNORM. Index bindings specify uint16/uint32 and aligned byte offsets. Draws check
bound resource types, stale handles and buffer ranges; indexed draws also check
the immutable CPU index copy against available vertices. Base vertex is currently
nonnegative (the legacy RHI parameter is unsigned).

Pipeline cache lookup compares complete descriptors, not only hashes: shader
handles/generations, vertex layout, topology, raster/depth/blend state, color/depth
formats and sample count. The current pass uses one color attachment, one depth
attachment and one sample. The Stage 2 fixtures are descriptor-free; the current
pipeline layout also supports the Stage 3 material sets. Its ABI version is in
the pipeline key.
Destroy invalidates handles/cache lookups immediately, while buffers, modules
and dependent pipelines retire after the last referencing submission completes.
Recording-time destruction includes the upcoming submit in that lifetime.
Recreation/shutdown drain retired resources after GPU completion.

### DXC Runtime and Tool

The tool is built as `binaries/xray_shader_compiler.exe`; `dxcapi.h` and WRL
headers are needed at build time (already present in this MinGW environment).
For another toolchain, install compatible DXC headers or add their directory to
the compiler include path. The compiler dynamically loads `dxcompiler.dll`,
releases COM objects before unloading it, and reports the Win32 load error on
failure. Put the runtime beside the executable or set the absolute DLL path:

```powershell
$env:XRAY_DXC_PATH = (Resolve-Path build/dxc/bin/x64/dxcompiler.dll).Path
./binaries/xray_shader_compiler.exe sources/xray/render/vulkan/tests/shaders/geometry.hlsl build/mesh.spv vertex --entry=mesh_vs -fvk-invert-y
./binaries/xray_vulkan_smoke.exe --geometry --frames=180 --self-test --capture=build/vulkan-stage2-frame.bmp --log=build/vulkan-stage2-validation.log
```

The command supports `--entry=name`, `--include=directory`, `--define=NAME=value`
and DXC optimization/debug flags. Compilation fixes SPIR-V, Vulkan 1.3,
Shader Model 6.0 stage profiles and HLSL 2021; flags cannot override the entry,
profile or environment. Source-relative includes are added automatically.
VS smoke compilation uses `-fvk-invert-y`, a positive viewport and clockwise
Vulkan front faces, preserving the fixture's Y-up coordinates. See
[official DXC SPIR-V options](https://github.com/microsoft/DirectXShaderCompiler/blob/main/docs/SPIR-V.rst).

The verified portable DXC is Microsoft's release `v1.9.2609`, archive
`dxc_2026_09_29.zip` (SHA256
`ad31b1fc8443175d204f77a611fdb3ef2ec42759bdc2f1167368de24a4a7e7f1`).
Only x64 runtime/tools and licenses were extracted under `build/dxc`.
No system SDK was installed/updated. This is local test tooling, not a committed
runtime bundle.

### Stage 2 Verification

Geometry mode renders a procedural colored triangle for the first 20 presented
frames, then an indexed quad with alternating 16/32-bit index buffers, nonzero
vertex/index binding offsets and back-face culling. A near orange triangle and
a far black triangle drawn last verify depth rejection. At frame 90, resources
and cached pipelines are destroyed/recreated while draws are already recorded;
stale shader/vertex/index handles must fail immediately. Pixel checks cover
triangle orientation, indexed geometry and depth after recreation. The mode
uses the same facade/world/channel path as Stage 1, not full scene cooks.

```powershell
cmake -S sources -B build -DXRAY_ENABLE_DXC_TESTS=ON -DXRAY_ENABLE_VULKAN_GPU_TESTS=ON
cmake --build build -j 4
# Set the validation environment above and XRAY_DXC_PATH before this command.
ctest --test-dir build -R '^xray_render_stage' --output-on-failure
```

`stage2_contracts` requires no GPU/DXC: layouts, forced hash collisions, SPIR-V
stage/entry/bounds/capabilities and exception-safe pool reuse. `stage2_dxc`
(opt-in) checks real named entry/include/macro compilation, invalid HLSL,
missing entries/files, environment overrides and a missing runtime.
`stage2_gpu` (opt-in) checks geometry plus the Stage 1 window/lifetime workflow.
It requires Win32/Vulkan WSI, a working validation layer and DXC runtime; automated
runs use hidden windows without activation.

Verified 2026-10-04 on the same RTX 4070 / Debug configuration: full existing
build, 7/7 focused CTests, 180 geometry frames plus minimized reinitialize/
restore/four more frames, 0 validation errors and 0 warnings in both sessions.
The clear-only GPU test also still passes. Evidence:
`build/vulkan-stage2-validation.log`, `build/vulkan-stage2-frame.bmp`,
`build/vulkan-stage2-frame.bmp.triangle.bmp` and the CTest log.
This Stage 2 verification did not establish asset loading, textures, lighting, full game startup,
other GPUs or GCC Release readiness.

## Stage 3 Materials

`--materials` is a separate smoke mode, mutually exclusive with `--geometry`.
It uses the production facade/world/channel, not scene cooks or game assets.

### Resources and Lifetime

RHI exposes texture descriptions (type, dimensions, format, usage, mip count,
layers), subresource data (mip, layer, bytes, size, row pitch), texture updates,
sampler creation/destruction/binding and constant buffer updates. Supported:
sampled 2D, one layer, RGBA8 UNORM/SRGB (extended with BC1/BC2/BC3 in Stage 5.8). Every declared mip must be supplied
exactly once; padded source rows are repacked. Empty upload data initializes
all mips to zero. Unsupported descriptions return an invalid creation handle;
invalid bindings/updates throw before recording invalid Vulkan commands.
The legacy texture overload remains a zero-initializing wrapper, not an
automatic mip generator.

Images use device-local allocations and a host-visible staging buffer. A
one-shot graphics submission transitions undefined -> transfer destination ->
fragment shader read; upload fence completion precedes staging cleanup.
Uploads block the CPU in this milestone. Updating a texture creates a new
image/view, retaining the logical handle and retiring the old GPU version by
submission serial. Previously recorded descriptor snapshots remain valid.
Destroy invalidates handles immediately and defers GPU destruction, including
when a referencing frame has not been submitted yet. Samplers follow the same
retirement rule. Nearest/linear filters and repeat/clamp/mirror addressing are
supported; anisotropy other than 1 is explicitly rejected.

Constant buffers are CPU-side logical data. `update_buffer` requires a live
constant buffer, nonnull data and its exact size. In-place RHI vertex/index updates remain
unsupported. Each draw copies constants into fresh slices of a per-slot mapped
uniform arena, aligned to both uniform-offset and non-coherent atom limits.
Chunks start at 32 KiB and grow as needed; non-coherent allocations are flushed.
Changing one logical object buffer between draws cannot overwrite earlier
draws, even within the same frame. Two frame slots reset arenas/pools only
after their corresponding submit fences complete.

### Material ABI v1

The pipeline layout has three sets, compatible with descriptor-free shaders:

- Set 0, binding 0: vertex uniform buffer, FrameConstants, 128 bytes.
  `row_major float4x4 view` at 0, `projection` at 64.
- Set 1, binding 0: pixel sampled Texture2D, separate from its sampler.
- Set 1, binding 1: pixel SamplerState.
- Set 2, binding 0: vertex/pixel uniform buffer, ObjectConstants, 96 bytes.
  `row_major float4x4 world` at 0, `float4 tint` at 64,
  `float4 parameters` at 80. `parameters.x` selects the smoke LOD.

The fixture uses explicit `vk::binding` plus b0/space0, t0/s0/space1 and
b0/space2 registers. RHI constant slots 0/1 map to frame/object respectively;
texture and sampler slots are both 0. Binding handle 0 selects identity
constants with white tint, a white 1x1 texture or the default linear sampler.

Pinned Khronos SPIRV-Reflect validates the entry's descriptor sets/bindings,
types, count, image dimensions, constant sizes/offsets/member names, float types
and matrix packing before Vulkan module creation. HLSL row_major matrices map
to SPIR-V ColumnMajor decorations in DXC. Wrong layouts, arrays, combined
samplers and push constants are rejected. Member names are part of this ABI;
stripped reflection names are not supported. Provenance, hashes and license:
`sources/third_party/spirv_reflect/README.xray.md`.

Descriptor sets are immutable per-draw snapshots. Each draw allocates three
sets and writes frame/object buffers, sampled image and sampler. Pools hold
64 draws each; exhausted/fragmented pools advance to another retained pool.
A freshly created pool failing to hold one draw is treated as an error, not
an unbounded allocation loop. Pool reset occurs only after slot completion.

### Shader Cache

Each compiler owns a bounded, process-local FIFO cache of 64 successful results,
serialized with its DXC calls. It compares full source, stage profile, entry,
include paths, macros, flags, fixed compilation options and working directory.
There is no hash-only equality or persistent disk cache. Runtime identity is
scoped by the compiler instance's loaded DXC library.

A tracking DXC include handler records absolute include probes and exact bytes,
including unsuccessful probes. Cache hits re-read every dependency/probe;
same-length edits, nested include edits, removed/broken includes and newly
created higher-priority includes invalidate the cached success. Failed
compilations are not cached. `compilation_result.cache_hit` exposes reuse.

### Stage 3 Verification

```powershell
# Use the validation environment and XRAY_DXC_PATH documented above.
./binaries/xray_vulkan_smoke.exe --materials --frames=240 --self-test --capture=build/vulkan-stage3-frame.bmp --log=build/vulkan-stage3-validation.log
ctest --test-dir build -R '^xray_render_stage' --output-on-failure
```

`stage3_contracts` checks padded rows, explicit mip packing, zero initialization
and 13 invalid upload descriptions without GPU/DXC. `stage3_dxc` checks actual
material reflection, seven rejected layouts, source/macros/profile/flags keys,
direct/nested/new-priority include invalidation, failed-compile recovery and
bounded cache eviction. `stage3_gpu` requires DXC, validation and a desktop.

The GPU mode updates the same object buffer between two independent materials
and a fallback draw. It changes the left texture after recording its draw,
destroys/recreates resources while pending references exist, changes the right
sampler from linear/clamp to nearest/repeat, then samples the supplied mip 1.
Stress records 163 draws per frame: both slots must grow to at least three
descriptor pools and two uniform chunks, then reuse the same capacity.
Pixel checks at frames 65/120/180 assert palette isolation, white fallback,
matrix-driven movement, sampler filtering (31 quantized colors -> 2) and mip
selection (solid cyan). Captures were also visually inspected.

Verified 2026-10-05: full existing Debug MinGW/Ninja build and 10/10 focused
CTests, RTX 4070 / Vulkan 1.4.351 / driver 617.14, DXC v1.9.2609. Material smoke
presents 240 frames, resizes/minimizes/restores, cleans up, reinitializes the
same world while minimized and presents four more frames. Both sessions have
0 validation errors / 0 warnings. Earlier clear/geometry GPU tests still pass.
Evidence: `build/vulkan-stage3-validation.log`, `build/vulkan-stage3-frame.bmp`,
its `.before.bmp` and `.mip.bmp` captures, and the CTest log.

Not implemented in Stage 3: arrays/cubes/storage/render-target textures, additional
asset formats, automatic mip generation, asynchronous streaming, anisotropy,
arbitrary effect layouts or real scene/material asset loading. Stage 4 adds the
three scene/view/output cooks below, not model/material asset cooks.
Other GPUs, clean GCC Release and hardware failure injection are not verified.

## Stage 4 Scene Lifecycle

`world::initialize(HWND)` registers Vulkan unmanaged cooks for `scene_class`,
`scene_view_class`, and `render_output_window_class` when the real resource
manager is initialized. Registration completes before logic can issue its
queries. Clear/geometry/material smoke still works without a resource manager;
that mode has no scene query path. Existing cook ID ownership is never replaced.

The concrete resources derive from `base_scene`, `base_scene_view` and
`base_output_window`, preserving the facade resource pointers and query callback
casts used by `game::query_scene()`. They do not construct the legacy DX11
scene manager, effects, terrain, particles, targets or device. Legacy concrete
scene/view/output downcasts remain in excluded DX11 engine code, not the active
facade. Full game startup is not implied by this pointer compatibility.

Scene userdata accepts the existing `editor_renderer_configuration` with both
feature flags false, or `vulkan::scene_configuration`. Its `test_geometry=true`
selects the Stage 3 material geometry fixture, lazily created once per scene
runtime. Terrain/particle requests fail with a diagnostic and `result_error`;
the normal game's configuration currently requests both and is unsupported.
Output userdata must contain the initialized HWND. Duplicate output resources,
foreign/invalid HWNDs and unknown userdata types are rejected. The new
`variant::get_if<T>()` checks the existing helper's RTTI across DLL boundaries,
without numeric type ID ordering or the assertions of legacy `try_get()`.
The variant's layout and existing accessor behavior are unchanged.

### Output Ownership

`presentation_output` separates HWND, surface, swapchain images/views, extent,
render pass/framebuffers, depth attachments, per-image presentation fences and
clear color from the shared device/queues and resource pools. A cooked output
shares the bootstrap's existing presentation output. Cooking, deleting and
recooking that output does not create a second swapchain. This milestone allows
only one active cooked output for the initialized window; multi-window routing
is not implemented.

The backend alone performs GPU destruction, after submit/presentation completion,
and zeros/clears the shared state. A resource lease may survive world shutdown,
but cannot keep dead GPU objects usable. Resize/present keep the existing fenced
backend path. Dropping an output resource while the world is alive leaves the
bootstrap output available for a replacement resource. HWND itself is owned by
the caller, not by the resource cook.

### Commands and Teardown

View/projection commands store finite CPU matrices. `draw_scene` rejects foreign
or closed-session resources, validates a nonempty normalized viewport, invokes
both depth and overlay callbacks, and snapshots camera/viewport plus strong
resource pointers for the GPU frame. X-Ray's row-vector matrices are transposed
for the fixture's column-vector HLSL. Viewport/scissor clip the draw to the output.
The fixture's white fallback marker deliberately retains identity camera data.

Callbacks are CPU render phases, not GPU completion notifications. They execute
while minimized too; skipped GPU frames discard queued snapshots and advance
the CPU frame. The channel retires deferred scene/view commands and clears
intrusive `next_scene`/`next_scene_view` links, including when draw order changes.
This restores queue lifecycle, not the still-unimplemented debug/UI draw features.
`window_client_size(nonnull_output)` uses the actual active cooked output;
closed, invalid or minimized output resources report zero dimensions.

`clear_resources()` closes registry admission, drops queued snapshots/fixtures,
and shuts down RHI. Backend disappearance closes the scene session as well.
Metadata destructors remove non-owning registry entries and never touch Vulkan.
Cooks retain the allocator captured at registration so delayed resource deletion
does not depend on renderer globals after teardown. Allocators still must outlive
all cooked resources and their deferred deletions.

Call `finalize_scene_resources()` on the registration/resource-dispatch thread
after closing the session and draining callbacks, resource pointers and queries,
and before finalizing the resource manager. It returns false while registration
is active, queries/cook users remain, or the thread/registered owners differ.
World cleanup/destruction retries this finalization; reinitialization refuses to
replace a group whose users have not drained. The legacy DX11 output cook's
destroy callback was also corrected to remove, rather than add, its registry entry.

### Stage 4 Tests

```powershell
# Configure with BUILD_TESTING=ON and XRAY_ENABLE_VULKAN_GPU_TESTS=ON.
# The GPU test also needs the validation environment and XRAY_DXC_PATH above.
cmake --build build --target xray_render_stage4_resource_tests xray_render_stage4_gpu_tests -j 6
ctest --test-dir build -R '^xray_render_stage4_' --output-on-failure
```

`stage4_resources` uses the actual resource manager in deterministic
`-debug_single_thread` mode and native disk interfaces, without mounts, workers,
game assets or mock cooks. It checks successful scene/view/output queries,
camera state, checked userdata/copy/type rejection, unsupported features,
duplicate output, alternating scene/view list order, closed admission, delayed
deleter ownership, and cook/resource destroy/recreate.

`stage4_gpu` drives queried resources through the real facade/channel/world.
It checks the shared bootstrap output/swapchain identity, per-frame deferred
depth/overlay/view callbacks, camera movement and viewport clipping by captured
pixels, resize/minimize, resource recooking without another swapchain, pending
query failure on shutdown, blocked reinitialization with live cook users, a new
world session, and deterministic backend disappearance cleanup. The latter is
not a real driver/device-loss injection. Evidence images are
`build/vulkan-stage4-frame.bmp` and its `.before.bmp` companion.

This milestone does not establish multi-threaded full game startup, model or
material asset cooks, terrain/particles, editor multi-window support, or GCC
Release compatibility. Those remain subsequent work or additional verification.

Verified 2026-10-05: full current Debug MinGW/Ninja build and 12/12 focused
Stage 0-4 CTests. RTX 4070 scene sessions, including shutdown, refused
reinitialization and backend disappearance cleanup, report 0 validation errors
and 0 warnings. Camera captures contain over 60,000 colored pixels with none
outside the viewport; the color centroid moves by more than 80 pixels. Both
captures were visually inspected. The retained CTest/validation evidence is
`build/vulkan-stage4-validation.log`; earlier GPU smoke tests still pass.

## Stage 5.1: Static Surface Resource Path

Stage 5 is in progress, not complete. This first slice loads the existing
`model_converter::read_static_surface` output: sequential chunks 3/4, the exact
48-byte `static_decl`, 32-byte packed vertices and uint16 triangle indices.
The bounded loader rejects unsupported declarations/chunks, duplicates,
truncation, non-finite positions/UVs, invalid counts and out-of-range indices
before entering the assertion-based core chunk reader. Imports are limited to
64 MiB and 65,536 vertices. Packed normal/tangent/binormal bytes are retained;
the initial unlit pass consumes POSITION and TEXCOORD only. Bounds are derived
from vertex data; no DX11 hardware objects or API calls are used.

`static_render_model_instance_class` now has a Vulkan unmanaged cook, owned by
the scene resource session. Its input is a single converted surface's bytes,
not the legacy `.model/render` directory contract. Cook finalization waits for
model users as well as scene/view/output users. Models retain CPU geometry and
session identity; late CPU deletion never calls a destroyed Vulkan backend.

Facade add/update/remove and `set_model_visible` are implemented. The only
surface name is `default` (empty also selects it). Null/foreign/closed resources,
duplicate adds, update/remove outside the scene, unknown surface names and
non-finite/non-affine transforms fail explicitly. `apply_transform=false`
preserves the instance's previous matrix. The instance transform/visibility
are render-thread state, shared when that instance belongs to multiple scenes.
Each queued draw snapshots them and holds strong model pointers, so a later
command cannot invalidate or mutate that draw.

GPU geometry is prepared before begin_frame. `static_renderer` owns RHI VB/IB,
shader handles and frame/object constant buffers. Its weak CPU-mesh cache does
not retain model resources; expired entries retire GPU buffers through the
backend's existing submission-serial/fence path. World cleanup drops this cache
before RHI shutdown; backend disappearance skips stale-pointer GPU deletion.
The initial asset shader is `assets/static_opaque.hlsl`, compiled by DXC and
validated against reflection ABI v1. It uses depth testing/writing, no blending,
no backface culling, and the existing white/default texture and sampler.
It is not a material asset cook or a port of the deferred effect system.

`stage5_models` checks CPU metadata, 223 malformed/command rejections, normal
cook failure, command contracts and cook lifetime after session close.
`stage5_gpu` uses resource queries and the real facade, verifies nonblank pixels,
model/camera movement, hide/remove, removal after a queued draw, and 16 repeated
recook cycles. Live buffer/shader/texture/sampler counts and retired-buffer
counts return to a stable baseline after each cycle. Both tests use generated
converter-format data via `query_create_resources`, not mounted game assets.

Verified 2026-10-05: full Debug MinGW/Ninja build; 14/14 focused Stage 0-5 CTests;
RTX 4070 GPU validation reports 0 errors and 0 warnings. Model centroid movement
is 353 pixels and camera movement 88.5 pixels. Evidence is retained in
`build/vulkan-stage5-validation.log`, `build/vulkan-stage5-frame.bmp`, and its
`.before.bmp` companion.

## Stage 5.2: Mounted DDS And Descriptor Materials

Single converted surfaces and DDS textures now load through actual physical
mounts, VFS and `query_resource`. The texture cook accepts uncompressed RGBA8/
BGRA8 2D DDS, tightly packed rows, mip chains and optional sRGB userdata. It
rejects malformed/truncated files, dimensions above 4096, imports above 64 MiB,
inconsistent payloads and unsupported formats. BC was unsupported at this stage;
BC1/BC2/BC3 support was added in Stage 5.8; complete legacy DDS cubemaps were
added in Stage 7.6 below. DX10 headers, arrays/volumes and padded-row DDS remain unsupported.

`material_class` accepts typed `material_configuration` userdata through
`query_create_resources`, not legacy `.material` file contents. The descriptor
holds a strong texture resource pointer, normalized tint and sampler settings;
anisotropy is currently limited to 1. Resource type/session and descriptor
checks fail the query normally. Device-specific sampler limits are checked
when creating the GPU sampler. `set_model_material` assigns/replaces the
material for `default`; null restores the white fallback. Queued draws retain
an immutable material snapshot, including its texture, even when external
pointers are released before the frame completes.

The static renderer uploads all DDS mips before begin_frame and caches textures
and samplers by weak CPU-data identity. Expired entries retire through the
existing submission-serial/fence mechanism; CPU resource destructors remain
GPU-free. Cook finalization now also waits for texture/material users.

Disk coverage exposed shared core bugs: Win32 handles were passed to the FILE
close path, stale handles/OVERLAPPED state survived queries, raw reads leaked a
managed-buffer pin, and child pointer reset retained its parent and reverse
graph link. These paths are fixed, including normal open/short-read failure
completion. The test bootstrap initializes managed/link arenas only for disk
tests; existing memory-only tests keep their previous bootstrap.

```powershell
# The GPU test needs the validation environment and XRAY_DXC_PATH above.
cmake --build build --target xray_render_stage5_asset_tests xray_render_stage5_gpu_tests -j 6
ctest --test-dir build -R '^xray_render_stage5_' --output-on-failure
```

`stage5_assets` checks 474 DDS/assignment rejections, mounted model/texture
queries, missing/invalid/deleted/shortened files, child graph reset/replacement/
destruction, material assignment/reset and delayed cook finalization.
`stage5_gpu` additionally checks sampled DDS pixels, tint replacement, queued
material lifetime and fallback reset. All live buffer/shader/texture/sampler
counts and retired buffer/shader/pipeline/texture/sampler counts return to the
baseline after each of 16 disk/DDS/material reloads, alongside the existing
16 memory-model reloads.

Verified 2026-10-05: full Debug MinGW/Ninja build and 15/15 focused Stage 0-5
CTests, RTX 4070, 0 validation errors and 0 warnings. Evidence:
`build/vulkan-stage5-assets-validation.log` and the visually inspected
`build/vulkan-stage5-frame.bmp.textured.bmp`. Assets are generated fixtures
written to disk and mounted, not a production game asset pack.

## Stage 5.3: Model Roots And Named Surfaces

The Vulkan resource session now owns both `static_render_model_class` and
`static_render_model_instance_class` translate cooks. A directory request must
end with `/render`. Its child folders are the surface manifest, sorted by name;
the cook reads root `export_properties`, then each surface's `converted_model`
and `export_properties` through real resource queries. Memory/direct-file
single-surface requests remain supported and use the name `default`.

For paths containing a `resources/` or `mounts/` tree, source folders provide
the manifest and cooked bytes come from `resources.converted/` or
`mounts.converted/`, respectively. Already-converted or other custom mounts
read cooked files in place. Only preconverted Windows binary properties and
converted static geometry are supported: no Lua evaluation, source vertices/
indices conversion, timestamp comparison or generated-file saving is performed.
Missing or invalid cooked files fail normally. Stage 5.3 used white fallback
surfaces until descriptor materials were assigned through the facade; the
limited `.material` and model `settings` import is documented in Stage 5.4 below.

Properties use the existing Windows binary-config layout and core reader,
preceded by bounded validation of relative offsets, types/counts, CRC-sorted
unique keys, tree structure and payload ranges. Cycles, shared node aliases,
payload/node overlap and malformed input are rejected before pointer fixup.
Model/surface types, finite ordered bounding boxes and optional root locators
are retained. Geometry must fit surface bounds, and surface bounds must fit
root bounds, allowing max(0.001, 0.001 * largest absolute bound) per axis.
`get_locator` returns local name/bone/offset metadata, matching the legacy
static instance contract. Limits are 128 surfaces, 64 MiB aggregate cooked
input, 1 MiB per properties config, 4096 config nodes, tree depth 32 and
256 locators; locator names must fit the existing 32-byte field.

Each instance owns independent visibility/material state per named surface.
Empty surface names are accepted only for single-surface models. Multi-surface
models require an exact name, with no implicit `default` or wildcard. Draw
snapshots expand scene membership into individual mesh/material draws, keeping
their geometry and material pointers stable across later commands. The existing
weak mesh cache and deferred GPU retirement also cover these surfaces. Live
model-asset users and pending queries block cook finalization; queries completed
after session close fail without creating resources.

`stage5_roots` covers 396 properties/command rejections, ten negative root
queries, metadata/locator import, source/converted-tree mapping, isolated
instance state, pending-query shutdown and delayed asset deletion. The GPU
test renders two differently colored named surfaces, hides only one, resets
the other material and checks queued visibility snapshots. It releases removed
instances before completing a queued frame, then checks every live/retired GPU
counter against the baseline across 16 model-root reloads, alternating in-place
and source/converted layouts. Earlier 16 memory and 16 disk/DDS/material reload
checks remain in place.

```powershell
cmake --build build --target xray_render_stage5_root_tests xray_render_stage5_gpu_tests -j 6
# GPU execution also requires the validation environment and XRAY_DXC_PATH.
ctest --test-dir build -R '^xray_render_stage5_' --output-on-failure
```

Verified 2026-10-07: full Debug MinGW/Ninja build and 16/16 focused Stage 0-5
CTests, RTX 4070, 0 validation errors and 0 warnings. Evidence:
`build/vulkan-stage5-roots-validation.log` and the visually inspected
`build/vulkan-stage5-frame.bmp.submeshes.bmp`. Test assets are generated fixtures
mounted through VFS, not a production game asset pack.

## Stage 5.4: Disk Materials And Model Settings

`material_class` is now a translate cook supporting both the existing typed
descriptor and preconverted Windows binary-config material bytes/files. Logical
names first resolve `resources.converted/material_instances/<name>.material`,
then `resources.converted/materials/<name>.material`. An existing invalid
instance fails without falling back to the base; a source instance without its
cooked config also fails. Explicit `.material` virtual paths use the same
source/converted-tree mapping as model properties. No Lua evaluation, source
conversion, timestamp regeneration or legacy effect compiler is invoked.

The accepted schema is deliberately narrow: `material/g_stage/effect` with
`effect_id = g_stage_default`, boolean `use_tdiffuse`, float4 `constant_diffuse`,
optional `two_sided` and string `texture_diffuse` for textured variants.
Parameters may be direct values or legacy `{ value = ... }` wrappers. Color
channels must be finite and normalized; Stage 5.4 required alpha exactly one
(Stage 5.9 also permits cutout alpha). Other stages,
effects, unknown parameters and active unsupported `use_*`/anisotropic options
fail explicitly. This is an opaque unlit base-color preview, not the legacy
deferred material or lighting pipeline; texture color is multiplied by tint.

Extensionless texture names map to `resources.converted/textures/<name>.dds`.
Explicit `resources/textures/` or `resources.converted/textures/` paths, with or
without `.dds`, also use cooked textures. These contain the legacy one-byte
boolean sRGB footer, checked and stripped before DDS validation. A full custom
mount `.dds` path instead requests ordinary DDS bytes without a footer. All
texture dependencies have strong CPU ownership. Stage 5.4 supported only the
existing RGBA8/BGRA8 2D mip-chain subset; Stage 5.8 adds BC1/BC2/BC3.
Stage 7.6 adds complete legacy DDS cubemaps below. DX10 headers and arrays/volumes remain unsupported.

Model roots optionally read the sibling binary `settings` file and require
`material_settings/<surface>/material_name` for exactly every named surface.
Material dependencies must all load successfully before publishing the model.
Missing settings or empty material names select white fallback; source settings
without a cooked config, invalid/missing mappings and failed dependencies are
errors. Instances copy immutable asset defaults into independent surface state.
Facade overrides do not mutate those defaults; null reset retains its previous
meaning of white fallback, not restoration of the disk-assigned material.

`static_opaque.hlsl` initially had DXC solid/textured pixel variants, extended
with cutout variants in Stage 5.9. Material `two_sided`
maps to no culling, otherwise back-face culling; descriptor materials also allow
explicit front culling. Both shader variants use the reflected RHI ABI, depth
testing/writing and no blending. The common bounded binary-config preflight now
protects properties, settings and materials. CPU deletion remains GPU-free, and
model/material/texture dependencies participate in delayed cook finalization.

`stage5_materials` checks 549 parser rejections, eleven negative disk queries,
invalid cull descriptors, binary query creation, wrapped values, instance
priority, texture dependencies/sRGB, source/converted settings and late deletion
after session close. `stage5_gpu` adds sixteen automatic settings/material
reloads, queued reset snapshots, textured/solid pixel checks and front/back/
two-sided rendering with mirrored transforms. These bring reload coverage to
64 cycles; every live and retired GPU resource count returns to baseline.
Test teardown drains resource callbacks before destroying the renderer allocator.

Verified 2026-10-07: full Debug MinGW/Ninja build and 17/17 focused Stage 0-5
CTests, RTX 4070, 0 validation errors and 0 warnings. Evidence:
`build/vulkan-stage5-materials-validation.log` and the visually inspected
`build/vulkan-stage5-frame.bmp.materials.bmp`. Assets remain generated fixtures
mounted through the real VFS, not a production game asset pack.

## Stage 5.5: Static Frustum Culling

The draw queue tests model-root geometry bounds, then visible named surfaces,
against six homogeneous clip half-spaces: `-w <= x,y <= w`, `0 <= z <= w`.
Planes are expressed in model-local space using row-vector
`world * culling_view * projection`, matching the transposed shader constants.
The shader's Y inversion only swaps the two vertical planes. Bounds come from
actual mesh positions, not tolerated export metadata. Touching or straddling
planes remains visible; this conservative test may keep false positives and is
not an occlusion test or a spatial scene hierarchy.

Composition/support calculations use double precision and a scale-relative
FP32 rounding margin, including cancellation magnitudes and propagated flush
error. Numerically uncertain inputs or potential intermediate overflow stay
visible. No perspective divide, matrix inverse, determinant assumption or
plane normalization is needed for asset culling. Tests cover mirrored,
nonuniform and sheared worlds, perspective/orthographic/asymmetric volumes,
near-plane crossings, reversed Z, infinite far and singular projections.
Reversed-Z coverage concerns the clip volume only: the static renderer still
uses `less` and clear depth 1, not a complete reversed-Z depth pipeline.

View snapshots atomically capture render view, projection and culling view.
`set_view_matrix_only` preserves the culling view while moving the rendered
camera; ordinary `set_view_matrix` updates both. The Vulkan facade's debug
listener opts into `math::frustum(matrix, true)` and safe AABB-plane
normalization, avoiding legacy determinant/zero-normal assertions for these
projections. The original one-argument strict constructor and non-Vulkan facade
behavior are preserved.

Culled/hidden surfaces never enter the queued draw list or GPU preparation.
Only accepted surfaces retain draw snapshot references; later camera/transform/
visibility/material changes do not mutate them. Existing weak caches and GPU
retirement remain unchanged. Cooked CPU assets and dependencies still load
normally, even for an offscreen model; this does not implement asset streaming.

`diagnostics().static_culling` describes the last successful CPU draw queue,
not presentation or GPU completion. `models_tested` counts scene instances;
`models_culled` counts root rejections. Only surviving roots visit surfaces:
hidden surfaces increment `surfaces_hidden`; visible ones increment
`surfaces_tested`, then either `surfaces_culled` or `surfaces_submitted`.

`stage5_culling` adds 10,000 independent long-double corner-transform reference
comparisons, explicit boundary/projection/numerical cases, safe-listener
regressions, geometry-vs-metadata bounds, frozen culling camera and offscreen/
hidden queues without a GPU backend or DXC. The GPU test verifies root/surface
rejections using both queue counters and captured pixels, camera/transform
snapshots, projection cases, frozen culling camera and sixteen never-visible
material-model reloads without GPU uploads. Total reload coverage is 80 cycles.
Verified 2026-10-07: full Debug MinGW/Ninja build and 18/18 focused Stage 0-5
CTests passed on RTX 4070, with zero validation errors or warnings. The CPU
test passed 28 explicit boundary/projection cases and 10,000 reference
comparisons (7,847 culled, 2,153 kept). Separate CPU/GPU tests also passed three
consecutive runs each. Logs: `build/vulkan-stage5-culling-validation.log` and
`build/vulkan-stage5-culling-repeat-validation.log`. The captured surface-culling
frame was visually inspected: `build/vulkan-stage5-frame.bmp.culling.bmp`.
Assets remain generated fixtures through the real VFS, not production assets.

## Stage 5.6: Versioned Static Model Buffer Updates

The engine vertex/index buffer update entry points now accept the supported
single-surface static model instances. The existing vertex facade command and
new Vulkan-only `scene_renderer::update_model_index_buffer` command copy their
payloads and retain the model until channel execution. Constructor failures
release command storage; Vulkan commands use the channel allocator rather than
requiring an editor allocator. CPU ownership/session checks remain on the render
thread. Unnamed multi-surface updates fail explicitly; this is not support for
legacy user, skeletal or terrain models.

Fragments use byte offsets and may be unaligned or overlap. Later fragments win.
Updates require 1..4096 nonempty, nonnull fragments, valid existing-buffer ranges
and at most 64 MiB aggregate payload. Vertex/index counts and buffer sizes do not
change: vertices retain the 32-byte static layout, indices remain uint16, and
triangle count is fixed. A candidate CPU mesh is copied, patched, then checked
for finite positions/UVs and valid indices before publication. Any validation or
allocation failure leaves the instance's mesh and bounds unchanged. Packed
normal/tangent/binormal bytes retain their existing opaque representation.

Each instance has an immutable mesh-version pointer per surface, initially
sharing the cooked asset. Updates do not mutate that asset or sibling instances.
Render/culling queues capture the current version; an update cannot change an
older queue. Actual geometry bounds are recomputed, while export metadata and
locators remain asset metadata. Hidden/offscreen updated meshes need no GPU
upload until accepted by a later draw queue.

The renderer uploads new immutable VB/IB pairs keyed by mesh version and retires
expired versions using existing submission-serial/fence protection. CPU deleters
never touch Vulkan. The cooked asset can keep its initial version/cache alive
while an instance exists, but discarded intermediate versions do not accumulate.
This deliberately copies/revalidates the CPU mesh and uploads both buffers; it
is not an in-place RHI update, resizing API or optimized streaming path.

`stage5_buffers` covers transactional rejection, byte-granular/overlapping
patches, immutable old versions/assets, sibling isolation, updated culling bounds
and closed-session/cook lifetime without GPU/DXC. GPU coverage adds vertex and
index pixel changes, overwritten producer payloads, queued snapshots, bounds
transitions, removal before command execution, 32 consecutive versions and 16
buffer-update reloads. Total model reload coverage is now 96 cycles.
Verified 2026-10-07: full Debug MinGW/Ninja build and 19/19 focused Stage 0-5
CTests passed on RTX 4070 with zero validation errors/warnings. The CPU buffer
test passed 32 rejection cases plus transactional patch/isolation/lifetime
checks. Separate CPU/GPU tests passed three consecutive runs each. Logs:
`build/vulkan-stage5-buffers-validation.log` and
`build/vulkan-stage5-buffers-repeat-validation.log`. The captured frame was
visually inspected: `build/vulkan-stage5-frame.bmp.buffers.bmp`. Production
assets, GCC Release, allocation/device-loss failure injection and multi-threaded
startup have not been verified.

## Stage 5.7: Versioned Cooked Texture Reload

The existing queued `scene_renderer::reload_modified_textures()` command now
asynchronously rereads live disk-backed textures in the active resource session.
A weak registry does not retain resources; overlapping requests coalesce while
a texture is pending. This is an explicit scan of tracked physical sources, not
an automatic watcher or background poll. Memory-created and archived textures
are not tracked. The initially resolved physical path is retained; dynamic VFS
remapping and source-to-cooked conversion are not implemented.

Reload uses ordinary resource queries with physical paths and fresh file metadata,
not cached raw/VFS input. Physical query temporary nodes now receive the measured
file size, handle expected open failures without assertions and tolerate null-node cleanup, fixing old VFS behavior exposed by this
path. Requests use portable separators even for native Windows sources. A file
type/size preflight precedes the bounded DDS parser (RGBA8/BGRA8 at Stage 5.7,
also BC1/BC2/BC3 since Stage 5.8); resize,
mip-count changes and legacy sRGB footer changes are supported.

Decoded dimensions, format and all mip bytes are compared, preserving the old
version identity for unchanged content, including encoding-only differences.
Same-size/timestamp edits are detected. Read/parse failures retain the previous
valid CPU version; callbacks cannot publish after session close or resource
deletion. Callbacks retain session state, never world/backend pointers. Pending
resource queries and live texture cook users still prevent premature finalization.

`texture_resource::snapshot()` returns an immutable, mutex-protected CPU version.
Each accepted `model_draw` captures that version before GPU preparation, so reload
completion between prepare and GPU recording cannot mutate a queued draw. New
versions use the existing weak texture cache and submission-serial/fence
retirement; hidden/offscreen-only updates do not upload until a later accepted
draw. This is not an in-place GPU update or streaming system. GPU allocation or
device-loss rollback is not added.

`diagnostics().texture_reload` reports session counters: `requested`, `reloaded`,
`unchanged`, `failed`, `cancelled`, `pending`. These describe CPU query completion,
not GPU completion/presentation. Expected bad reloads report errors while retaining
valid content; Vulkan validation counters are separate.

`stage5_reload` covers unchanged identity, same-size/time edits, coalescing,
size/mip/sRGB changes, seven failure modes, missing physical queries, memory
exclusion, weak registry and closed/pending/late lifetime without GPU/DXC. GPU
coverage adds shared material users, a callback between prepare/draw, unchanged
and malformed reloads, offscreen upload avoidance, 32 consecutive texture versions,
16 texture-reload scene cycles and pending callbacks after world/backend teardown.
Existing 96 model reload scenarios remain covered.

Verified 2026-10-07: full Debug MinGW/Ninja build and 20/20 focused Stage 0-5
CTests passed on RTX 4070 with zero Vulkan validation errors/warnings. Separate
CPU/GPU reload tests also passed three consecutive runs each. Logs:
`build/vulkan-stage5-reload-validation.log` and
`build/vulkan-stage5-reload-repeat-validation.log`. The shared-texture frame was
visually inspected: `build/vulkan-stage5-frame.bmp.reload.bmp`. Fixtures are
generated, not production assets. GCC Release, other GPUs, allocation/device-loss
failure injection and multi-threaded startup remain unverified.

## Stage 5.8: Legacy DXT DDS And Native BC Upload

The DDS cook now accepts legacy FourCC DXT1/DXT1a, DXT3 and DXT5 as BC1 RGBA,
BC2 and BC3, respectively. UNORM/SRGB comes from the existing texture userdata
or checked legacy cooked footer. Encoded blocks remain compressed through CPU
snapshots, staging and GPU sampling; there is no CPU decoder or recompression.
The existing 4096 dimension / 64 MiB input limits still apply. DXT2/4 premultiplied
formats, DX10 headers, BC4/5/6/7 and arrays/volumes remain explicitly unsupported.
Complete legacy DDS cubemaps are supported by the Stage 7.6 texture path below,
not as diffuse inputs to this static 2D material pass.
This only extends diffuse input to the opaque unlit material pass; it does not
add alpha blending, normal-map/DXT5n interpretation or legacy lighting effects.

`texture_format.h` centralizes supported block extent/size and overflow-checked
mip layouts. BC rows round up to whole 4x4 blocks, including sub-block tail mips
and odd/non-square extents. DDS payloads must exactly match the derived tight
mip chain; compressed `dwPitchOrLinearSize` is ignored because writer values are
unreliable. RHI uploads may contain padded block rows, which are repacked. Mip
offsets remain aligned to the format's block size; Vulkan copies use actual texel
extents including image edges. RGBA8/BGRA8 DDS row-pitch validation is unchanged.
References: [Microsoft DDS guide](https://learn.microsoft.com/en-us/windows/win32/direct3ddds/dx-graphics-dds-pguide)
and [Vulkan image copies](https://docs.vulkan.org/guide/latest/image_copies.html).

The backend enables `textureCompressionBC` only if supported and checks sampled,
linear-filter and transfer-destination format features before creating an image.
Devices without BC retain the RGBA path and report unsupported compressed uploads;
there is no CPU fallback. CPU cooks are device-independent. Existing immutable
draw snapshots and deferred retirement protect BC/RGBA and BC-family transitions,
dimension/mip changes and legacy sRGB changes. CPU read/parse rollback remains;
GPU capability/allocation/device-loss rollback is not implemented.

`stage5_textures` checks 294 format/dimension combinations, independent encoded
payload sizes, padded-row repacking, zero uploads, block alignment and 669 malformed
DDS/upload rejections without GPU/DXC. GPU coverage adds six BC asset cycles,
all three mips of 7x5 textures, 1x1 textures, mid-tone sRGB decoding, BC/RGBA
queued snapshots, unchanged/truncated reloads and 24 mixed-format versions.
Previous Stage 5 coverage remains active; each cycle drains cook users and
live/retired GPU allocations back to baseline.

Verified 2026-10-07: full Debug MinGW/Ninja build and 21/21 focused Stage 0-5
CTests passed on RTX 4070 with zero Vulkan validation errors/warnings. CPU texture
and Stage 5 GPU tests also passed three consecutive runs each. Logs:
`build/vulkan-stage5-bc-validation.log`, `build/vulkan-stage5-bc-repeat-validation.log`
and `build/vulkan-stage5-bc-gpu-validation.log`. The BC3 sRGB tail-mip capture was
visually inspected: `build/vulkan-stage5-frame.bmp.bc5-srgb-mip2.bmp`. Fixtures
remain generated, not production assets. GCC Release, other GPUs (including
devices without BC), allocation/device-loss failure injection and multi-threaded
startup have not been verified.

## Stage 5.9: Alpha Test, UV State And Active Shader ABI

The bounded `g_stage_default` import now reads direct or `{ value = ... }`
`use_alpha_test`, `alpha_ref`, `constant_tile_u` and `constant_tile_v` parameters.
Defaults (alpha test off, threshold 0.25, UV scale 1,1) follow the existing
`effect_gstage_default_materials.cpp` descriptor. Thresholds must be finite in
0..1; both UV fields must be present together, finite and within +/-4096.
Signed and zero scales are supported. Invalid/mistyped state fails normally,
including inactive scalar parameters. Non-opaque source tint alpha requires
cutout; typed descriptors retain their existing normalized tint contract.

The active static shader has four solid/textured x opaque/cutout variants.
Cutout computes `clip((texture * tint).a - alpha_ref)`, or solid tint alpha,
preserving fragments equal to the threshold. Surviving fragments write depth;
discarded fragments do not occlude later geometry. Texture UVs are multiplied
by U/V scale, using the material sampler's existing addressing. Alpha blending
and automatic mip-LOD selection are not introduced. Object ABI v1 remains
96 bytes: `parameters` holds explicit mip LOD, alpha reference, U scale, V scale.
UV defaults are explicitly initialized to 1,1, including the white fallback.

DXC targeting Vulkan 1.3 emits `DemoteToHelperInvocation` for `clip`. The backend
queries/enables `shaderDemoteToHelperInvocation` only when supported. Reflection
accepts that capability only for the fragment stage with an explicit enabled
profile; unsupported capabilities (including Float64) remain rejected. Default
structural/reflection callers do not implicitly opt in. Vulkan requires queried
features to be enabled before use; see the
[Vulkan feature contract](https://docs.vulkan.org/refpages/latest/refpages/source/VkPhysicalDeviceShaderDemoteToHelperInvocationFeatures.html).
Cutout shaders are compiled/created lazily before frame recording, so ordinary
opaque scenes do not require this feature. Unsupported cutout creation reports
an error, never silently substitutes an opaque shader. At most two additional
cutout shaders stay cached until renderer teardown.

`stage5_materials` adds direct/wrapped/default state, RGBA/BC cooked dependencies,
12 malformed config cases (561 total) and four invalid typed states.
`stage5_dxc` compiles the actual `assets/static_opaque.hlsl` VS and all four PS
permutations, verifies descriptor/constant ABI masks and distinct/cached alpha
code, rejects three ABI mutations and cutout without a demote-enabled profile.
Structural tests cover fragment-only demote gating and continued Float64 rejection.
No GPU is needed for these CPU/DXC checks.

GPU coverage adds eight RGBA/BC1/BC2/BC3 cutout asset cycles. The foreground draws
before a blue background to test discarded-fragment depth behavior. Pixel checks
cover threshold zero/one/equality, tint-alpha multiplication, solid cutout,
signed/zero U and V tiling, queued alpha/UV changes, texture-alpha reload snapshots
and 32 consecutive material-state versions. Existing weak caches/fence retirement
drain scene resources and cook users back to baseline, with the two lazy shader
variants deliberately retained for the renderer lifetime.

Verified 2026-10-07: full Debug MinGW/Ninja build and 22/22 focused Stage 0-5
CTests passed on RTX 4070 with zero Vulkan validation errors/warnings. Material,
Stage 5 DXC/GPU and Stage 2 structural contract tests also passed three consecutive
runs each. Logs: `build/vulkan-stage5-cutout-validation.log`,
`build/vulkan-stage5-cutout-repeat-validation.log`,
`build/vulkan-stage5-cutout-cpu-validation.log` and
`build/vulkan-stage5-cutout-gpu-validation.log`. BC1 cutout and U tiling captures
were visually inspected: `build/vulkan-stage5-frame.bmp.cutout-1.bmp` and
`.cutout-tiled.bmp`. GCC Release, other GPUs, the unsupported-demote hardware
path, allocation/device-loss failure injection and multi-threaded startup remain
unverified.

This defines unlit preview semantics, not production GBuffer parity. The legacy
descriptor supplies parameters/defaults, but the referenced `vertex_base`,
`geometry_pre_pass` and `geometry_material_pass` shader assets and a production
static asset pack are absent from this checkout. No lighting/normal mapping,
alpha blending, production shader port or source-to-cooked conversion is claimed.

Stage 5 has a fixed cooked-static/unlit acceptance scope; see the five completion
gates in `vulkan_integration_plan.md`. It remains unaccepted on external assets,
not an open-ended port of all shared legacy wrappers or texture/effect formats.
Unused D3D-oriented wrappers belong to later renderer stages. Additional input
support requires an explicit need from the selected acceptance pack.
Source-to-cooked regeneration is not implemented.
At this Stage 5 acceptance, ghost/skeleton and unnamed multi-surface buffer
updates reported explicit unsupported errors. The Stage 8 skeletal renderer
baseline below adds pose updates, not raw skeletal buffer-fragment updates.
Full game startup, terrain/particles and multi-window outputs remain
unsupported; GCC Release and multi-threaded startup have not been verified.

## Stage 5.10: External Cooked-Pack Probe

`xray_vulkan_asset_probe` loads an existing, dedicated cooked directory through
the real VFS/resource cooks and facade/RHI/Vulkan path. It never generates fixture
assets, edits textures, or overwrites reports. It is built with `BUILD_TESTING`
because it deliberately uses the deterministic single-thread resource harness
(128 MiB managed arena), not normal game/editor startup.

From the repository root in PowerShell:

```powershell
$env:VK_LAYER_PATH=(Resolve-Path build/vulkan-validation/mingw64/bin).Path
$env:PATH="$env:VK_LAYER_PATH;$env:PATH"
$env:XRAY_DXC_PATH=(Resolve-Path build/dxc/bin/x64/dxcompiler.dll).Path
$env:XRAY_VULKAN_REQUIRE_VALIDATION='1'
.\binaries\xray_vulkan_asset_probe.exe --assets "D:\SelectedCookedPack" `
    --model "resources/models/example.model/render" `
    --output "$PWD\build\stage5-acceptance-run-01" --cycles 16
```

Mounts: the entire input root is `assets/`; existing `resources/` and
`resources.converted/` subdirectories are also mounted under those logical names.
An in-place root can use `assets/example.model/render`. Dependency logical names
must resolve within this tree. The selected model must contain at least one cooked
material and disk texture; all-white fallback alone is not acceptance evidence.
Source folders may describe the surface list, but the geometry/material converter
is not invoked. Stage 5.11 also permits direct TGA/options texture dependencies;
decoding/mip generation is memory-only and never modifies source assets.

Bounds are fitted uniformly into an orthographic XY preview with identity camera
and projection. Select a model with visible, non-degenerate geometry from that
direction; this is not an interactive scene viewer or an authored game camera.
The tool checks nonblank pixels against an empty reference, camera/model movement,
named visibility, offscreen root rejection and queued-removal ownership. Existing
Stage 5 regressions remain the depth, material-override and changed-texture oracles.
The probe runs in a hidden, no-activate, input-transparent window. Pixel checks
read the rendered swapchain image through a fenced GPU-to-host copy, not the
desktop; other applications may cover the entire screen. A blank/fully discarded
preview fails rather than passing a geometry-submission count alone.

One warmup precedes 16..256 complete unload/recook and unchanged-texture-reload
cycles. After each cycle, model/asset/material/texture cook users and GPU
retirements must drain. Live resource counts and descriptor/uniform capacities
must match warmup. Warmup retains exactly two renderer constant buffers, three
opaque shaders and at most two lazy cutout shaders; geometry/material resources
are not allowed to remain. VFS mounts outlive scene/dependency teardown, including
failures after GPU preparation. Validation errors/warnings are checked after
renderer shutdown, not only after the last visible draw.

The output directory must not exist and must not overlap the input tree.
Input inventory is bounded to 10000 regular files / 1 GiB, rejects symlinks and
junctions, and compares sorted sizes/FNV-1a-64 fingerprints before/after the run.
This detects changes, not cryptographic provenance or concurrent change-and-revert.
`report.json` records every input file, model/surface/texture metadata, counts,
shutdown result and validation counters; BMP captures are `empty`, `model`,
`camera` and `moved`. JSON scalars are strings (bundled Boost Property Tree);
readers must convert numeric counters. The old Boost serializer is isolated in
a C++17 helper; renderer/resource code remains C++26 with no new dependency.
Exit codes: 0 technical pass, 1 runtime/acceptance failure, 2 invalid CLI/path.
Valid runtime failures retain a failed report; unsafe CLI/path failures do not
write to the rejected output. A process crash or output I/O failure may prevent
report creation and must itself be treated as failure.

Self-tests generate a separate marked pack in `build/stage5-probe-assets` before
starting the probe in another process. They cover opaque RGBA, BC1/BC3 cutout,
missing material dependencies, white-only fallback and fully discarded cutout
failure after actual GPU upload. Reports use fresh random directories under
`build/stage5-probe-reports`. These are generated controls, NOT production assets.

Real exported/source assets were subsequently found in `C:\Stalker2_2011`, but
no ready `platforms/pc_dx11` cooked cache. The source-derived unlit runs below
verify genuine geometry/textures, not the original complete material pipeline.
`technical_pass` never automatically closes production acceptance: the report's
`production_acceptance` still requires provenance and visual review. A ready
legacy cooked pack without the offline projection remains unverified.

Verified 2026-10-07: full Debug MinGW/Ninja build, 28/28 focused Stage 0-5 CTests
on RTX 4070, zero Vulkan validation errors/warnings. All six new probe self-tests
passed three consecutive runs, including negative cleanup after GPU upload.
Opaque baseline/final counts were `[2,3,1,1,0]`, cutout `[2,4,1,1,0]`
(buffers/shaders/textures/samplers/retired buffers); cook users and GPU retirements
drained and input inventories remained unchanged. Logs:
`build/vulkan-stage5-acceptance-validation.log` and
`build/vulkan-stage5-acceptance-repeat-validation.log`. Visually inspected:
`build/stage5-probe-reports/gpu/564a460959cd/model.bmp`,
`build/stage5-probe-reports/cutout/b01ab1747cbe/model.bmp` and `camera.bmp`.
Adjacent JSON reports retain the explicit production-acceptance caveat.
GCC Release, other GPUs, multi-threaded startup and allocation/device-loss
failure injection remain unverified.

### Real Source-Asset Verification

Verified 2026-10-07 using assets read from
`C:\Stalker2_2011\xr2-dsgn\resources`, without modifying that tree:

- `props/box/woodbox_mil.model`: 595 vertices / 396 triangles, original
  `props/box/wood_box` material's diffuse texture, 2048x2048. Final pack:
  `build/real-assets/woodbox-mil-final`; report/captures:
  `build/real-assets/woodbox-mil-final-gpu`, 24221 visible pixels.
- `props/barrel/barrel_metal03_radiation.model`: 380 vertices / 612 triangles,
  original `props/barrel/barrel_1_red_radiation` material's
  `props/barrel/barrel_rad_1` diffuse texture, 512x512. Final pack:
  `build/real-assets/barrel-radiation-final`; report/captures:
  `build/real-assets/barrel-radiation-final-gpu`, 49018 visible pixels.

Both unchanged probe runs passed one warmup plus 16 full unload/recook/reload
cycles on RTX 4070, with zero validation errors/warnings, stable live baseline
`[2,3,1,1,0]`, stable material capacities, drained cook users/retirements,
successful shutdown and unchanged inputs. Model/camera/moved captures were
visually inspected. The full 28/28 focused CTests passed again; log:
`build/vulkan-stage5-real-assets-validation.log`.

The optional offline adapter `tests/tools/prepare_real_static_pack.py` requires
Python with Pillow and Lua 5.3+; it is not a renderer/build dependency and is not
called by the probe. It reads only bounded LE64 uncompressed archive nodes,
validates the complete child/sibling graph and payload bounds, and never performs
unsafe native pointer fixups on the historical mount-root layout. Historical
parent pointers may all reference the root (legacy writer behavior); they are
validated but not followed. Historical archive hashes are not claimed to be
today's CRC; provenance uses SHA-256 instead. Compressed/inlined/link/other archive
formats are explicitly rejected, not guessed.

Source config inheritance is evaluated by Lua in a restricted environment with
only property/reference/vector helpers, without filesystem/process APIs exposed
to configs, and with subprocess timeouts. Consumed config bytes are checked
against the input inventory. Conversion preserves position/UV/index bytes;
only vertex layout/basis packing changes. Pillow decodes real TGA pixels and
writes uncompressed RGBA DDS with an exact decoded-pixel roundtrip, one mip and
the original sRGB footer. This does not reproduce original DXT1/mip cooking.

Each `provenance.json` contains six source-file SHA-256 fingerprints, mesh payload
and decoded-pixel hashes, the resolved original material and every omitted effect
parameter. The unlit projection retains original diffuse/alpha/culling/tiling
values, not fixture materials, but explicitly omits normal/specular/other full
renderer features. No full material/PBR parity is claimed.

The negative `--material-mode full` pack keeps the complete resolved original
barrel material. It fails as expected with `Unsupported material render stage`,
exit 1, clean shutdown, unchanged input and zero validation errors/warnings:
`build/real-assets/barrel-full-material-gpu/report.json` and adjacent `.log`.
Thus ready legacy cooked-pack compatibility is not established by the unlit run.

Reproduce with new output directories (existing results are never overwritten):

```powershell
$python='C:/Users/hturu/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe'
& $python sources/xray/render/vulkan/tests/tools/prepare_real_static_pack.py `
    --resources C:/Stalker2_2011/xr2-dsgn/resources `
    --model props/box/woodbox_mil.model --output build/real-assets/woodbox-new `
    --lua C:/msys64/mingw64/bin/lua.exe
# Set the validation/DXC environment shown above before running the probe.
.\binaries\xray_vulkan_asset_probe.exe --assets build/real-assets/woodbox-new `
    --model resources/models/props/box/woodbox_mil.model/render `
    --output build/real-assets/woodbox-new-gpu --cycles 16
$env:XRAY_LUA_PATH='C:/msys64/mingw64/bin/lua.exe'
& $python sources/xray/render/vulkan/tests/tools/test_prepare_real_static_pack.py -v
```

The seven offline unit tests passed: archive bounds/flags/cycles and path
rejections, binary-config layout, byte-preserving geometry conversion and Lua
inheritance/false-value/environment checks. These synthetic parser unit fixtures
are independent of, and never supplied to, the real-asset GPU runs above.

## Stage 5.11: Direct TGA Source Textures

Requested explicitly after discovery of the real source pack; this is a bounded
source-texture adapter, not a geometry/material cooker or full renderer extension.

Logical `texture_class` names (also used by materials) resolve in this order:

1. `resources.converted/textures/NAME.dds`, with the existing cooked sRGB footer.
2. Only when that file is absent: `resources/textures/NAME.tga`, plus optional
   `resources/textures/NAME.options`.

Malformed/unreadable existing DDS, directories and invalid existing options fail
the query rather than silently falling back. Explicit `.tga` paths work through
VFS or physical queries; `.dds` and existing typed creation requests remain
supported. Virtual extensions are normalized to lowercase for VFS. Memory TGA
requests set `texture_configuration::source_tga`; typed `srgb`/`generate_mips`
provide defaults. No DDS is written by this path.

The pinned, vendored [stb_image v2.30](https://github.com/nothings/stb) decoder is
compiled with only TGA/memory support, private symbols and no extra runtime DLL.
Supported: raw/RLE RGB24, RGBA32, gray8 and gray-alpha16, all origin combinations,
ID and bounded TGA 2 metadata footer. Stored 32-bit alpha is preserved for both
descriptor alpha-bit declarations 0 and 8; legacy gray8 descriptor 8 is opaque.
Palette, packed RGB15/16 and interleaving are rejected. A bounded preflight checks
packet lengths/counts before stb decoding, including truncations stb itself can
tolerate. 2D source limit: 64 MiB, dimensions 1..4096 per axis. Stage 7.6 adds
separately bounded six-face cube strips, described under Scene Skybox below.

Options are evaluated using the existing Lua library in a fresh state with no
standard libraries/host callbacks. Limits: 64 KiB source, 8 MiB allocations,
100000 VM instructions; bytecode is forbidden. Present files must specify boolean
or 0/1 `srgb` and `has_mip_levels`, and `texture_dimension=1` (2D) or `3`
(cube strip, added in 7.6). Only optional string
metadata `format`/`ver` is additionally accepted; unknown fields fail. The format
hint does not trigger runtime BC compression: TGA uploads as RGBA8 UNORM/SRGB.
Absent options use caller defaults (linear, one mip by default).

Requested mips form a complete chain to 1x1 using box averaging, including odd
dimensions. sRGB RGB channels are averaged in linear space, alpha independently
in linear space. This does not implement alpha-coverage preservation, normal-map
filtering or automatic shader LOD; existing explicit LOD behavior is unchanged.

Physical TGA reload rereads both image and current adjacent options using fresh
resource queries. Options-only changes, additions/deletions, dimensions, mip count
and sRGB changes are reflected. Unchanged identity, pending coalescing, immutable
queued snapshots, failed-read/decode rollback and fence retirement use the same
contract as DDS. Reload stays on the selected physical source; a newly introduced
cooked DDS wins on the next logical resource recook, not by changing an existing
TGA resource's source during reload. Archived/memory-created textures are not
tracked for physical reload.

Verified 2026-10-07: full Debug MinGW/Ninja build, **30/30 focused CTests**,
RTX 4070, zero validation errors/warnings. CPU covers 96 decoder combinations,
exhaustive truncations, packet/ID/footer checks, legacy descriptors, gamma/odd
mips, Lua schema/sandbox/memory/instruction bounds, material fallback, DDS priority
and rollback. GPU covers 16 TGA scene cycles, queued old/new pixels, malformed
image/options rollback and 32 consecutive texture/options versions. A separate
TGA cutout model/material probe runs 16 recook/reload cycles. Logs:
`build/vulkan-stage5-tga-validation.log`, `build/vulkan-stage5-tga-focused.log`.

The real crate and barrel also passed 16 cycles each without any DDS in their
packs; source TGA/options were copied unchanged. Reports and inspected frames:
`build/real-assets/woodbox-mil-tga-gpu` (2048x2048, 12 mips) and
`build/real-assets/barrel-radiation-tga-gpu` (512x512, 10 mips). Both retain stable
baseline/final `[2,3,1,1,0]`, drained cook users/retirements, unchanged inputs and
clean shutdown. Model captures match their previous DDS captures pixel-for-pixel.
Preparation: add `--texture-mode tga` to the offline command above; the geometry
and restricted unlit material still require preparation. This is not original
full-material/PBR parity. GCC Release, other GPUs and normal game/editor startup
remain unverified.

## Stage 6.1: Offscreen/MRT Foundation

The RHI supports explicit offscreen passes. This section records the foundation;
the active facade GBuffer added in Stage 6.2 is described below.

Attachments use `texture_desc::usage=render_target` or `depth_stencil`, with one
2D mip/layer and no initial uploads. Color formats: RGBA8 UNORM/SRGB, RGBA16
UNORM/FLOAT, RG16 UNORM, RGBA32 FLOAT and R32 FLOAT. Depth formats: D24S8/D32.
Dimensions and optimal-tiling capabilities are checked against device limits.
Depth attachment and depth-only sampling views have distinct Vulkan handles.

```text
begin_frame()                       [opens/clears presentation pass]
end_render_pass()
transition_texture(color, render_target)
transition_texture(depth, depth_write)
begin_render_pass(desc)             [0..4 colors + optional depth, nonempty]
draw / draw_indexed
end_render_pass()
transition_texture(color, shader_resource)
transition_texture(depth, depth_read)
begin_presentation_pass(true)       [loads this frame's earlier presentation]
draw attachment previews
end_frame() -> present()
```

Use the backend's pass methods inside an RHI device frame. Existing callers that
only use begin/end frame are unchanged. A frame must finish in a presentation
pass. Transitions occur only outside passes, including same-state write barriers.
Attachments must be distinct, equally sized, live and explicitly transitioned.
Load/clear/discard and store/discard are supported; loading/sampling undefined or
discarded contents fails. Discard-load is conservative: draws do not establish
whole-image initialization; use clear/load before sampling. Active attachments
cannot be destroyed or sampled as feedback. Depth sampling requires a nearest
sampler and a read-only depth/stencil layout. Read-only depth attachments for
concurrent depth testing, copies/storage, MSAA and mip/layer passes were not part
of Stage 6 acceptance. Stage 7.7 now adds multisample color/depth attachments and
color resolve transfer states, as documented below; storage/subresources and
read-only depth attachments remain outside the contract.

Pipeline compatibility includes the ordered MRT format list and depth format,
not image handles, dimensions or load/store actions. Pipelines survive
dimension-only swapchain recreation. Each explicit pass currently allocates a
render pass/framebuffer, retained until its submit serial completes; collection
destroys those objects before retired texture views/images. No pass-object cache
or performance claim is made. Offscreen recording does not wait for device/queue
idle; existing swapchain recreation still waits for device idle.

Verified 2026-10-07: full Debug MinGW/Ninja build, **32/32 focused CTests** on
RTX 4070, zero validation errors/warnings. New tests: `xray_render_stage6_contracts`
(12 rejected contracts, collision-safe MRT keys), `xray_render_stage6_gpu`
(16 cycles, 3 MRT outputs, load/discard, cross-frame reuse, D24/D32 sampling,
depth-only rendering, changed format order, resize, deferred destruction and 238
rejected operations). Screen pixels are checked with swapchain sRGB encoding.
Both new CPU/GPU tests also passed three repeated runs. Logs:
`build/vulkan-stage6-focused.log`, `build/vulkan-stage6-validation.log`,
`build/vulkan-stage6-repeat-validation.log`.
Inspected capture: `build/vulkan-stage6-mrt.bmp`. This uses synthetic fullscreen
shader outputs, not reconstructed positions/normals from a real model. Current
active static-scene GBuffer acceptance is recorded below. GCC Release and other
GPUs remain unverified.

## Stage 6.2: Static-Scene GBuffer

The existing facade static scene can now write the primary legacy GBuffer.
`static_renderer` and `gbuffer_renderer` provide the narrow Vulkan adapter for
geometry/constants/targets/context operations; the whole legacy D3D backend and
all renderer_context targets have not been ported.

Primary targets follow `renderer_context_targets.cpp`, `stage_gbuffer.cpp` and
the external `geometry_pre_pass.ps` under `C:/Stalker2_2011`:
- RGBA16 UNORM: sqrt(view_z / far), unused, roughness UV.
- RGBA16 FLOAT: normal XY mapped to 0..1, fresnel, normal-Z sign.
- RGBA8 UNORM: normalized cross(tangent, normal) mapped to 0..1.
- D24S8: hardware depth, static geometry stencil marker `0x82`.

This is a restricted geometric prepass. Roughness UV is fixed at (1,1), fresnel
at 0.04; normal/roughness textures and the separate legacy material pass are not
implemented. There is no extra albedo target or full material/PBR parity claim.
The existing opaque/cutout material selection, alpha threshold, UV scale,
two-sided culling, resource queries and immutable queued snapshots are reused.
Cutout samples diffuse alpha at LOD 0. ObjectConstants ABI remains 96 bytes;
its first parameter is the far plane in this shader, not the unlit mip LOD.

Packed normal/tangent bytes use legacy little-endian D3DCOLOR B,G,R,A. Normals
use inverse-transpose view/model transforms, with back-face sign correction;
tangents are orthogonalized. GBuffer cameras require finite forward-Z
perspective/orthographic projections (identity is also supported), and finite
invertible affine view/model transforms. Reversed-Z/infinite-far GBuffer cameras
are explicitly rejected; the previous unlit camera profile is unchanged.

Use the existing facade `game_renderer().scene().set_view_mode(view, mode)`:
`normals_view_mode`, `gbuffer_packed_depth_view_mode`,
`gbuffer_tangents_view_mode`, `gbuffer_hardware_depth_view_mode`,
`gbuffer_reconstructed_position_view_mode`, `gbuffer_overview_view_mode` or
`gbuffer_stencil_view_mode`.
The overview shows packed depth, decoded normals, tangent basis and hardware
depth. Default `unlit_view_mode` is unchanged. Other legacy modes are explicitly
unsupported; no editor UI integration is claimed.

The full-output four-target context is lazily allocated/resized and retired by
fence serial; an unlit-only frame releases it. Previews sample nearest and account
for the queued normalized viewport. `begin_presentation_pass(true)` preserves
earlier presentation color/depth in the same frame, so mixed unlit/GBuffer views
work in either order. The default argument remains false (clear); preserving
without an earlier completed presentation pass is rejected. Stencil reference,
masks, comparison and pass operation are part of the pipeline key; fail and
depth-fail operations currently remain KEEP for this bounded API.

Tests include actual DXC shader permutations/ABI, CPU camera roundtrips and
rejections, and the new `xray_render_stage6_gbuffer` GPU scene test. It checks
expected linear-to-sRGB pixels for all debug channels, nonuniform shear/mirrored
normals, perspective/orthographic depth, queued modes, split views in both orders,
vertical cutout/depth occlusion, 16 reloads, resize/minimize/restore and context
release. The RHI MRT test additionally verifies stencil replace/load/equal/not-equal
and rejection on a non-stencil depth format.

The stencil view LOADs the scene's actual D24S8 attachment and uses stencil
EQUAL 0x82 with read mask 0xff / write mask 0 to produce a white-on-black mask.
Depth testing/writes are disabled for the diagnostic pass; the packed-depth
color target is cleared/reused only in this mode, without adding another texture.
The mask shader is prepared lazily before frame recording. The scene test compares
every stencil-mask pixel with the surviving hardware-depth silhouette, first for
vertical alpha holes and then for overlapping foreground/background geometry;
draw-order and mixed GBuffer viewport checks are also covered. This is not a
mask synthesized from depth/color or a CPU stencil readback.

`xray_render_stage6_lifecycle` uses eight fresh worlds and 32 cooked output
sessions in one process. It checks recreated stencil/normal pixels, queued
output leases after caller pointers are dropped, stable active/empty GPU counts
and descriptor/uniform capacity, drained cook users/pass objects/retirements.
Each world shuts down after a submitted frame with additional unprocessed facade
commands. It verifies closed/empty scene registries, unregistered cooks and
retained output leases with cleared Vulkan handles, including validation
counters after world destruction. This is deterministic single-thread startup,
not a claim of multi-threaded startup or real device-loss recovery.

The offline preparer was corrected to write BGRA basis bytes, covered by exact
byte assertions in its seven Python tests. Earlier unlit captures did not read
the basis; regenerate a new pack for normal inspection instead of overwriting
earlier evidence. Actual source-derived TGA packs passed GBuffer acceptance:
`build/real-assets/woodbox-mil-gbuffer-final` and
`build/real-assets/barrel-radiation-gbuffer`. Add `--view gbuffer` to the asset
probe command to save individual `normals.bmp`, `packed-depth.bmp`, `tangents.bmp`,
`hardware-depth.bmp`, `position.bmp`, `stencil.bmp` and `diffuse.bmp`, as well as
the overview.
The final reports are under `build/real-assets/woodbox-mil-gbuffer-acceptance-gpu`
and `build/real-assets/barrel-radiation-gbuffer-acceptance-gpu`. Each passed 16 recook cycles,
baseline/final `[3,7,5,2,0]`, stable capacity, drained users/retirements, unchanged
inputs and clean shutdown, zero validation errors/warnings on RTX 4070. Captures
were inspected; SHA-256 of all 12 original input files was rechecked unchanged.
Materials still use the restricted offline projection, not original PBR data.
Separate pixel comparisons of the saved stencil and hardware-depth captures
found zero silhouette differences for both models (24335/49055 marked pixels).

Stage 6.1-6.3 and M6 are complete for the bounded static opaque/cutout profile.
Lighting, normal maps, full material parity and normal game/editor startup remain
outside this bounded stage.

Verified 2026-10-07: full Debug MinGW/Ninja build, **34/34 focused CTests**,
**7/7 Python preparer tests**, zero Vulkan validation errors/warnings on RTX 4070.
Stage 6 contracts/MRT/GBuffer/lifecycle and actual static/GBuffer/stencil DXC tests
each passed three repeats. Logs: `build/vulkan-stage6-acceptance-focused.log`,
`build/vulkan-stage6-acceptance-validation-final.log`,
`build/vulkan-stage6-acceptance-repeat-validation.log`. Inspected captures:
`build/vulkan-stage6-gbuffer.bmp.stencil.bmp`, `build/vulkan-stage6-recreated.bmp`.
This completes the bounded M6 implementation. GCC Release, other GPUs, allocation/device-loss injection
and multi-threaded startup remain unverified.

## Stage 7: Materials, Lighting And Final Frame (Accepted Static Profile)

The only Stage 7 task checklist is 7.1-7.8 in `vulkan_integration_plan.md`.
Material inputs (7.1), unshadowed directional/combine (7.2), point/spot (7.3)
and ambient/constant emission/fog (7.4) are implemented; shadow RHI and
directional/point/spot shadows (7.5) passed bounded integrated acceptance below.
7.6 is accepted for single-cubemap sky and non-intersecting static unlit
alpha/additive forward surfaces, including typed/binary material queries,
sorting, depth reuse and mixed-frame acceptance below. 7.7 is accepted for manual
exposure/Reinhard, opt-in bloom and FXAA preset 3, extended with deferred MSAA and
bounded static-scene TAA below. Final integrated M7 acceptance (7.8) passed in
that bounded static profile, including new AA on two real-source projections.
The subsections here document contracts and dated evidence,
not additional milestones. Older test counts describe their dated runs; the
latest regression is recorded in Integrated M7 Acceptance below and in the plan.

### Scalar Material Inputs

The static GBuffer now consumes immutable per-material scalar parameters instead
of hardcoded roughness/Fresnel. Legacy disk semantics follow
`effect_gstage_default_materials.cpp` and `geometry_pre_pass.ps`:

- `constant_specular_power` is validated in [0,255] and stored /255 in both U/V
  channels for isotropic materials; `is_anisotropic_material=true` selects
  `constant_roughness_v_direction /255` for V. Missing disk constants default to 0.
- `fresnel_at_0_degree` is [0,1], default .04. The optional integer material type
  overrides it when `use_fresnel_material_type` is true (default true): metal=.99,
  wood=.1, velvet=.04. Unknown types and non-finite/mistyped values fail the cook.
- Typed legacy `material_configuration` and absent-material fallback retain
  roughness=(1,1), F0=.04. For explicit values use `surface_material_request`
  with a shared base configuration and normalized `surface_material_parameters`.
  The cook copies both; mutating caller data cannot change an existing material.

The legacy <=64-byte userdata format remains intact. Object ABI v2 adds one
float4 `surface` at offset 96, total 112 bytes. ABI v3 adds float4 `map_parameters`
at offset 112, total 128 bytes. Active GBuffer shaders now use v5 (144 bytes)
with detail-normal parameters, described below.
Unlit and fullscreen previews keep v1 (96 bytes), frame constants remain 128.
Material-input shaders use v8 (160 bytes), extending the v4 inputs below with emission.
Reflection validates the material layouts plus lighting v6/v7/v9/v10 below; draw submission rejects shader-stage
version disagreement or a bound object buffer with the wrong size. Constants
are still copied into frame-fenced uniform slices per draw.

`gbuffer_roughness_view_mode` displays stored U/V as R/G; B=0.
`gbuffer_fresnel_view_mode` displays stored F0 as grayscale. Presentation performs
the usual linear-to-sRGB conversion. Normal preview no longer uses F0 as its
occupancy sentinel, so zero-Fresnel materials retain their normal visualization.

`xray_render_stage7_materials` covers parser defaults/ranges/types, material-type
precedence, unsupported active map features, disk cooks with/without diffuse
dependency, typed input validation and immutable copies.
`xray_render_stage7_material_gpu` checks expected pixels for actual GBuffer
channels, zero-F0 normals, cutout/depth/stencil, queued material snapshots, mixed
v1/v5 viewports, fallback, eight reload baselines and shutdown.

This GBuffer block alone is not material/lighting integration or original-material
parity. Specular color/intensity and material-input extraction are described below.
Fresnel maps and anisotropic direction maps remain unsupported. The active legacy
anisotropic BRDF and unshadowed directional lighting/combine are implemented in
7.2, documented below; they are not part of this scalar-input block.
The complete M7 acceptance boundary is in `vulkan_integration_plan.md`.

Verified 2026-10-07: GCC/MinGW-w64 Ninja Debug build, **36/36 CTests** and
**7/7 Python preparer tests**. Stage 7 CPU/GPU and static/GBuffer shader ABI
tests each passed three repeats. RTX 4070 validation errors=0, warnings=0.
Logs: `build/vulkan-stage7-regression.log`, `build/vulkan-stage7-repeat.log`.
Inspected capture: `build/vulkan-stage7-material.bmp`.
GCC Release and real original-material surface parity were not tested here.

### Normal And Roughness Maps

Disk `g_stage_default` supports `use_nmap`/`texture_normal` and
`use_tspecular_power`/`texture_specular_power`, independently of diffuse/cutout.
Normal decoding follows `psf_normal.h`: A/G mapped to [-1,1], invert Y, reconstruct
positive Z and transform through the per-vertex basis. The disk binormal determines
handedness (including mirrored UV islands); two-sided flipping applies to the
geometric normal axis. Tangent output is crossed with the mapped normal, with a
finite fallback for a degenerate cross product. The normal basis is orthonormalized;
this is not an exact reproduction of legacy nonuniform/sheared TBN interpolation.
Roughness follows `psf_tspecular_power.h`: both U/V =
`min_roughness + (max_roughness-min_roughness)*max(sample.g,.01)`.
The remap coefficients are finite [0,16], ordered, default [0,1]; they apply to maps only,
not byte-unit scalar roughness. Maps use the material sampler and UV tiling.
The mapped result saturates at RGBA16 UNORM GBuffer storage, not by clamping
the remap coefficients. The [0,16] project bound supersedes the earlier [0,1]
schema restriction; normalized scalar roughness and Fresnel are unchanged.

Normal/roughness dependency queries force linear RGBA8 or BC1/2/3 UNORM, overriding
the sRGB flag in source options or cooked DDS footer (malformed metadata still
fails). Diffuse queries retain their independent sRGB semantics even for the same
physical path. Typed map resources must belong to the session and be linear;
queued GBuffer draws revalidate their immutable versions after any reload.
Material maps use five image slots (0 diffuse, 1 normal, 2 roughness, 3 specular,
4 detail normal) and ordinary sampler 0. Shadow RHI below adds image slot 5 and
comparison sampler 1. Unused map descriptors bind fallback images; a shader
requiring a missing map fails before drawing. Pools remain bounded at 64 draws;
the current descriptor counts are documented below. All map snapshots/uploads
retire through the existing fenced caches.

Source TGA `format="NORMAL_MAP"` is RGB-normal input, matching the old NVTT cooker.
The direct TGA adapter normalizes RGB normals and mipmaps in linear space, then
swizzles to R=1, G=Y, B=0, A=X without lossy BC compression. This ignores source
alpha, as DXT5n did. Without that format, TGA is assumed already A/G packed for
normal sampling; it is not automatically repacked by filename. Cooked DDS is
never swizzled again. GPU tests cover both packed TGA and RGB24 NORMAL_MAP TGA.

`xray_render_stage7_surface_textures` covers map schema/ranges, missing/corrupt
dependencies, typed invalid inputs, DDS/TGA color-space independence, normalized
normal mip generation, immutable reload/invalid rollback and late dependency
shutdown. `xray_render_stage7_surface_texture_gpu` checks expected A/G normal and
G roughness pixels, independent maps, mirrored transform/UV handedness, cutout
depth/stencil, queued normal/roughness reloads, invalid options retention, zero
UV tiling, eight recook/resource/capacity baselines and shutdown. Actual DXC tests
now cover twenty-four GBuffer permutations plus four unlit permutations and v2/v3
compatibility, with eight rejected detail-without-base permutations.

The offline preparer defaults to the previous `unlit` projection. Use
`--material-mode surface --texture-mode tga` to retain supported scalar/normal/
roughness/specular/detail-normal semantics and unchanged original texture bytes/options. Surface mode
rejects raw DDS conversion because it would lose RGB NORMAL_MAP source packing.
The provenance lists omitted parameters for each prepared pack; it is
not a full material parity claim. `--view gbuffer` probes now save `roughness.bmp`
and `fresnel.bmp` and report each map's format, mips, physical-relative path and
forced-linear semantic, alongside scalar parameters.

Verified 2026-10-07: full GCC/MinGW-w64 Ninja Debug build, **39/39 CTests**,
**9/9 Python preparer tests**. All four Stage 7 material CPU/GPU tests and actual
DXC ABI/permutation tests each passed three repeats. The new
`xray_render_stage7_probe_gbuffer` also checks all diagnostic captures and the
exact three-buffer static-renderer cache baseline (frame, v1 object, v3 object).
Final log: `build/vulkan-stage7-maps-regression-final.log`; repeated tests:
`build/vulkan-stage7-maps-repeat.log`. Inspected fixture capture:
`build/vulkan-stage7-normal-map.bmp`.
The GBuffer probe regression also passed three repeats:
`build/vulkan-stage7-maps-probe-repeat.log`.

Real source-derived surface packs:
`build/real-assets/woodbox-mil-stage7-surface` and
`build/real-assets/barrel-radiation-stage7-surface`.
GPU reports/captures:
`build/real-assets/woodbox-mil-stage7-surface-gpu-final` and
`build/real-assets/barrel-radiation-stage7-surface-gpu`.
Both passed 16 recook cycles, stable baseline/final `[4,7,5,2,0]`, unchanged
inputs, drained dependencies/retirements, stable descriptor capacity and clean
shutdown; RTX 4070 validation errors=0, warnings=0. Each contains an original
NORMAL_MAP TGA dependency (one normal-mapped surface). Both original materials
disable roughness maps, so real runs validate scalar roughness only; roughness
map acceptance is fixture-based. Original file SHA-256 rechecks passed for all
16 inputs. Compared with prior geometry-only normal captures, 4740 woodbox and
48448 barrel pixels changed by more than 3 channel units. New stencil vs
hardware-depth silhouettes had zero differing pixels for both models.

These earlier packs remain restricted projections: woodbox detail normal and
specular intensity, among other parameters listed in their provenance, are omitted. No full
production cooked-material parity, lighting, GCC Release or other-GPU claim.
The first woodbox run correctly stopped on an outdated two-buffer probe baseline;
its failed report is preserved in `woodbox-mil-stage7-surface-gpu`. The corrected
exact GBuffer baseline and repeated fixture probe prevent hiding resource growth.

### Material Inputs And Specular Intensity

`material_albedo_view_mode` and `material_specular_view_mode` run geometry pre-pass,
then a second static geometry pass that extracts linear albedo/specular into RGBA16F
targets (and emission/occupancy in 7.4, below). This is NOT the complete legacy
`geometry_material_pass.ps`, which consumes light accumulators and adds ambient,
emission and fog. No lit/final-frame or full PBR parity is claimed.

Pass contract:
- Pre-pass clears/stores the three GBuffer colors and D24S8 static marker 0x82.
- Material pass clears/stores linear albedo/specular targets, LOADs the same depth
  attachment, uses depth EQUAL and stencil EQUAL 0x82, and disables depth/stencil
  writes. Depth remains in the attachment `depth_write` layout with read-only
  pipeline operations. Position math, culling, alpha test and UV tiling match the
  pre-pass. Viewport/scissor are restored after the render-pass boundary.
- Before preview, material colors transition to `shader_resource`; GBuffer colors
  also transition to `shader_resource` and depth to `depth_read`. Presentation LOAD
  preserves preceding viewports. Material targets are allocated lazily, replaced
  on resize and destroyed/retired with the GBuffer context.

Albedo is `pow(constant_diffuse.rgb,2.2) * diffuse_sample.rgb` (sample defaults to
white). Specular is `pow(color.rgb * multiplier.rgb,2.2) * intensity_sample.rgb`,
following the legacy gamma helper and `psf_tspecular_intensity.h`. Disk keys are
`constant_specular_color` (float4), `constant_specular_color_multiplier` (float3)
and the original misspelled `use_tspecular_inensity`/`texture_specular_intensity`.
Color and multiplier must appear together; color channels are [0,1], multiplier
channels [0,16]. Missing pairs deterministically default to white, not an inferred
legacy unbound-uniform value. Typed `surface.specular_color` is the product before
gamma, bounded [0,16]. Its value is copied, not shared mutable caller state.

Intensity maps sample RGB with the material sampler/UVs and retain DDS footer or
TGA options sRGB semantics. They are not forced linear and not treated as roughness
maps. Specular dependencies, versions and GPU uploads use the existing queued
snapshots and fenced caches. Typed map bundles now use a shared immutable request
pointer to stay inside the legacy 64-byte userdata limit; the resource copies the
bundle so later caller mutation does not change an existing material.

Real barrel specular options additionally contain legacy `high_quality` and
`bump_mode`. Direct RGBA decoding validates/ignores compression quality (boolean
or 0/1) and accepts only numeric default `bump_mode=0`, which the old texture
converter did not consume. This does not enable material parallax; active
`use_parallax` still fails. Malformed or non-default metadata is rejected.

Original object ABI v4 appends float4 `specular` at offset 128 (144 bytes total) to v3.
The current material shader uses v8, described in 7.4 below; v4 remains accepted.
Material VS/PS must agree exactly; older GBuffer/unlit layouts remain valid.
Set 1 binding 4 is the specular image (texture slot 3); bindings 0..3 keep their
previous meanings. At 7.1 pools contained 320 sampled images for 64 draws, including
the detail-normal image described below. All eight
material PS permutations are reflected and compiled with actual DXC.

The two debug modes display linear target RGB through the presentation sRGB
conversion; values above one clip in this diagnostic preview, not in the FP16
targets. This is not tonemapping. `--view material` adds `albedo.bmp`/`specular.bmp`
and per-surface specular metadata to the existing probe captures, with an exact
four-buffer static cache baseline (frame, v1/v5/v4 objects).

`xray_render_stage7_material_pass` covers schema/ranges/types/paths, RGB texture
metadata, missing/corrupt dependencies and immutable typed inputs.
`xray_render_stage7_material_pass_gpu` checks scalar gamma/product, linear/sRGB RGB
intensity, cutout/depth/stencil occlusion in both draw orders, immutable reloads,
invalid reload rollback, perspective/ortho/transforms, split viewports, resize, nearest
RGB sampling and zero UV tiling,
eight recook/resource/capacity baselines and shutdown. The partial-frame shutdown
test explicitly retires facade CPU scene leases after clearing the GPU world;
automatic multi-thread game/editor teardown is still Stage 8 work.
The reload fixture changes source dimensions through fresh physical reads, then
restores original bytes before independent recook loops: the deterministic VFS
mount has fixed sizes and no file-watcher remount.

Real material-input packs:
`build/real-assets/woodbox-mil-stage7-material-inputs` and
`build/real-assets/barrel-radiation-stage7-material-inputs`.
Each retains original diffuse/normal/specular TGA/options. Both passed 16 recook
cycles in `--view material` with stable baseline/final `[5,9,7,2,0]`, drained
dependencies/retirements, unchanged inputs, stable descriptor capacity and clean
shutdown; RTX 4070 validation errors=0, warnings=0.
Reports/captures: `build/real-assets/woodbox-mil-stage7-material-gpu-run3` and
`build/real-assets/barrel-radiation-stage7-material-gpu-final`.
Albedo/specular captures were inspected; albedo vs hardware-depth silhouettes
have zero differing pixels for both. Specular targets have 24335/49055 nonblack
pixels respectively. These are input-pass checks, not a DX11 lighting baseline.
SHA-256 rechecks passed for all 20 original inputs. Detail-normal and other
provenance omissions remain explicit; there is no full material/lighting parity.

The two earlier woodbox reports preserve the original 5-second harness timeout
failures. Real-asset model queries and reloads now use a bounded 30-second probe
wait, while ordinary fixtures keep five seconds. Measured maxima: woodbox query
8261 ms/reload 8286 ms, barrel 541/474 ms. These Debug CPU decode costs are not a
performance acceptance claim. The initial barrel report also preserves its
unsupported-metadata failure before the validated compatibility extension.

Verified 2026-10-08: full GCC/MinGW-w64 Ninja Debug build, **42/42 CTests** and
**9/9 Python preparer tests**. Material CPU/GPU, TGA/options, probe options,
material fixture probe and actual DXC ABI/permutations passed three repeats;
RTX 4070 validation errors=0, warnings=0. Logs:
`build/vulkan-stage7-material-pass-regression-final.log` and
`build/vulkan-stage7-material-pass-repeat-verified.log`.
Inspected fixture capture: `build/vulkan-stage7-material-pass.bmp`.
The input pass itself does not implement lighting. Directional/point/spot,
combine and ambient/emission/fog are documented below. At this material-only run,
shadows, sky/forward and final HDR/postprocess acceptance were still open; their
later acceptance is documented below, ending with integrated 7.8.

### Detail Normal

Static GBuffer supports `use_detail_nmap`/`texture_detail_normal` with optional
float `scale_detail_normal` and `tile_detail_normal` (legacy defaults: 1).
Supported finite ranges are scale [0,16], tile [0,4096], including zero.
An active detail map requires a base normal map; absent/mistyped/invalid paths,
unsupported formats and cross-session resources fail rather than silently disable
the feature. Disabled map paths are not queried, but their value types are checked.

The formula follows `geometry_pre_pass.ps` and `psf_tnormal_detailed`: sample
linear AG at base UV * detail tile, decode [-1,1], scale XY, invert Y, set Z=0,
add to the normalized reconstructed base tangent normal, then transform/normalize
with the existing handed TBN. It is not RNM blending. If the sum is nearly zero,
retain the base direction to avoid NaNs. The existing orthonormalized TBN behavior
under nonuniform/sheared transforms is unchanged, not exact raw legacy TBN parity.

Detail DDS/TGA dependencies are forced linear; RGB `NORMAL_MAP` TGA uses the same
normalized AG packing/mips as base normals. Diffuse/specular semantics remain
unchanged. Typed detail parameters travel with the immutable map bundle, which is
copied into the material, keeping the request within 64-byte legacy userdata.
Queued draws retain immutable texture versions across reload and fenced retirement.

GBuffer object ABI v5 appends float4 `detail_normal_parameters` at offset 128,
144 bytes total; XY holds repeated scale, ZW repeated tile. Material ABI v4 also
has 144 bytes, but uses `specular` at offset 128: VS/PS must agree by version,
not only size. Older v1/v2/v3/v4 contracts remain accepted. Detail uses texture
slot 4, set 1 binding 5; at 7.1 descriptor pools kept 64-draw capacity with 320 images.
Actual DXC covers 24 valid GBuffer permutations plus eight rejected detail-only
permutations, v2/v3 compatibility, and malformed field/binding rejection.

Existing surface CPU/GPU tests now cover detail schema/defaults/ranges/dependencies,
linear role, immutable inputs, AG/RGB packing, additive formula, mirrored/backface
basis, cutout, strength/tiling/repeat/base UV, cancellation fallback, queued reload,
invalid options rollback and eight detail recook resource/capacity baselines.
The offscreen GPU test rejects same-size v5 VS/v4 PS combinations.

Verified 2026-10-08: GCC/MinGW-w64 Ninja Debug build, 42/42 CTests, 9/9 Python tests.
Logs: `build/vulkan-stage7-detail-regression-final.log`,
`build/vulkan-stage7-detail-repeat.log`. No GCC Release/additional GPU claim.

Real pack: `build/real-assets/woodbox-mil-stage7-detail-inputs`, prepared from the
read-only original sources, with `tiled/earth/flowerbed_nmap` 512x512 NORMAL_MAP TGA
and original scale/tile .1/.1. Final report:
`build/real-assets/woodbox-mil-stage7-detail-gpu-final/report.json`.
Sixteen cycles passed; baseline/final `[5,9,7,2,0]`, stable capacity, drained users
and retirements, unchanged inputs and clean shutdown; RTX 4070 validation 0/0.
All twelve original input SHA-256 hashes were rechecked unchanged. Compared with
the preceding material-input capture, 24246 normal pixels changed; albedo,
specular, hardware-depth and stencil had zero differences. Normal capture inspected.
The initial real report remains separately preserved; final capture hashes match
after the cancellation guard, also verified on a dedicated GPU fixture. Maximum
final query/reload times were 4605/4772 ms (Debug decode cost, not performance
acceptance). Five focused CPU/GPU/DXC tests passed three repeats.
This is not full material/production-cook/lighting parity;
other omissions remain in provenance. Directional lighting/combine is implemented
below. M7 acceptance boundaries in the integration plan remain unchanged.

### Directional Lighting And Combine

The static opaque/cutout profile accepts unshadowed `light_type_parallel` through
the existing facade add/update/remove commands. A scene owns at most 16 lights;
duplicate IDs and update/remove of missing IDs fail. World travel direction is
normalized from `transform.k` and transformed into the immutable queued view.
Light color is packed RGB/255, intensity is finite [0,16], diffuse/specular
influence finite [0,1]. Non-affine/non-finite/degenerate directional transforms
fail. Point/spot extend this lifecycle as documented below; directional/point/spot
shadows are integrated in 7.5 below. Shadowers, LPV and automatic sun-location requests
still fail explicitly.
Legacy lighting_model values 0..3 share the active Szirmay-Kalos path, matching the
original shader whose model switch is commented out; other values fail.

`lit_view_mode`, `lighting_diffuse_view_mode`, and `lighting_specular_view_mode`
run geometry, material inputs, shared light accumulation, combine and preview.
The BRDF reads roughness U/V, tangent and Fresnel from the actual GBuffer, with
roughness floor .01, specular clamp [0,4] and guards for degenerate/grazing half
vectors. Hardware depth/inverse projection reconstructs the perspective viewing
ray; orthographic rays remain parallel. Normal/detail maps affect illumination.
The legacy asymmetric gamma placement is preserved: diffuse influence and NdotL
are inside pow(2.2), specular influence/BRDF are outside, intensity outside both.
No lights and default environment/emission means black. Explicit environment and
material contributions are described in 7.4 below.

Pass inputs/outputs and states:
- Geometry writes three GBuffer RTs and D24S8 depth_write; material inputs LOAD
  the same depth/stencil, using depth EQUAL without depth/stencil writes.
- GBuffer colors/material inputs become shader_resource; depth becomes depth_read.
  Directional/point/spot fullscreen draws sample four GBuffer images with nearest sampling,
  use tangent alpha for occupancy, and ONE/ONE ADD into two cleared RGBA16F RTs
  (diffuse/specular), without depth/stencil attachments or tests.
- Both accumulators become shader_resource. Combine samples linear albedo,
  material specular and both accumulators, writing a third RGBA16F RT with
  albedo*diffuse + material_specular*specular, no blending. Finite FP16 output is
  clamped to [0,65504]. It becomes shader_resource before environment composition
  in lit mode (7.4 below), or presentation of raw debug accumulators.
Targets resize atomically within each target group and retire behind frame fences.
Queued lights/cameras and each viewport retain their own snapshots. Raw diagnostic
preview converts via the sRGB swapchain but clips above 1. Production lit views
now use the exposure/tonemap path in 7.7 below; the raw probe remains explicit.

The original lighting constant ABI v6 is 128 bytes: inverse_projection at 0, viewport at 64,
light_direction at 80, light_color at 96, light_factors at 112; version bit 4096.
It is pixel-only and requires explicit constants, never material fallback data.
Same-size mapped material v3 and lighting v6 cannot be mixed across VS/PS.
Reflection checks fields/layout/bindings; existing v1..v5 remain accepted.
The pass reuses the fullscreen VS/nearest sampler, and slots 0..3 with the
then-existing five-image descriptor layout. The original 7.2 pass added two PS and one
constant buffer; 7.4 adds one environment PS and extends that same buffer.

`xray_vulkan_asset_probe --view lit` keeps all GBuffer/material captures and adds
lit, lighting-diffuse, lighting-specular, lights-off and lights-dimmed BMPs.
It uses two white opposite unshadowed directional controls, normalized travel
directions +(0.25,-0.4,1) and its negative, intensity .5 each. The report identifies
these as synthetic controls, not the original scene's lighting. Zero intensity
must match the empty frame; intensity .125 must change over 64 pixels; restoring
.5 must reproduce the lit frame. Existing camera/model/culling, queued removal,
unchanged reload, 16 recooks and drained baseline checks remain active.

Verified 2026-10-08: GCC/MinGW-w64 Ninja Debug build, full regression 45/45 CTests
and 9/9 Python tests. Six focused lighting CPU/GPU, probe options/lit probe, DXC
and offscreen ABI tests passed three repeats after adding v3/v6 mismatch and
missing-lighting-constants negative coverage. GPU tests assert numeric
diffuse/specular/combine, packed light color and asymmetric influence gamma,
multiple additive lights, zero Fresnel, anisotropic U/V, perspective off-center
vs parallel orthographic rays, queued light/camera snapshots, split views,
world-to-view rotation, normal/detail inputs, cutout, resize, eight recook
resource/capacity baselines and shutdown. Actual DXC checks both PS and mutated
matrix/field/bindings. RTX 4070 validation 0 errors/0 warnings.
Logs: `build/vulkan-stage7-lighting-regression.log`,
`build/vulkan-stage7-lighting-repeat.log`.

Real reports: `build/real-assets/woodbox-mil-stage7-lit-gpu/report.json` using the
detail-input pack, and `build/real-assets/barrel-radiation-stage7-lit-gpu/report.json`
using the material-input pack. Both passed 16 cycles with baseline/final
`[6,11,10,2,0]`, stable capacity, drained cook users/retirements, unchanged inputs,
clean shutdown and validation 0/0. Dimmed controls changed 24307/47058 pixels
(RGB threshold 8); lights-off matched empty exactly, and restoration reproduced
the lit frame. Captures inspected. Normal/albedo/specular/hardware-depth/stencil
all have zero pixel differences against their preceding detail/material-input
captures. All 22 original input SHA-256 hashes were rechecked unchanged after
both runs. Maximum query/reload: woodbox 5049/4800 ms, barrel 219/237 ms, Debug
decode measurements only. These are source-derived projections with provenance
omissions and synthetic lights, not production-cook or DX11 final-lighting parity.

### Point/Spot Lighting

Point and spot lights use the same facade lifecycle, sixteen-light scene limit,
BRDF, two additive accumulators and material combine as directional lights.
Updating a light can change its type atomically; an invalid update leaves the
previous light intact. No extra textures, passes or light-volume meshes are added.
This is the unshadowed static profile of task 7.3, not shadow support or M7 completion.

- Local position is `transform.c.xyz`, finite within +/-1,000,000 per axis.
  Point orientation is irrelevant. Spot travel direction is normalized
  `-transform.j.xyz`, preserving DX11 `fill_light`'s -pi/2 X rotation, not +Z.
- Range is finite [0.001,65536]; attenuation power and spot falloff are [0,16].
  Full cone angles are radians: 0 <= umbra <= penumbra, 0.001 <= penumbra < pi.
  Their half-angle cosines define the inner/outer cone. Equal angles form a hard
  cone without division by zero; zero falloff is constant inside the outer cone.
- Radial attenuation inside range is `pow(max(1-d*d/(range*range),.001),power)`,
  following the legacy point shader. At/outside range, or a coincident light
  without an incident direction, contribution is zero. Non-finite reconstructed
  squared distances are rejected in the shader. Spot attenuation multiplies
  this by `pow(saturate((cosine-outer)/max(inner-outer,.0001)),falloff)` between
  the cones; it is one inside the inner cone and zero outside the outer cone.
- Attenuation multiplies the shared BRDF factors before legacy gamma placement:
  diffuse attenuation is inside pow(2.2), specular attenuation outside it.
  Intensity, RGB color and influence contracts remain unchanged.
- Queueing copies positions and directions into the captured camera view and
  retains per-draw light/camera snapshots. Local lights require an orthonormal
  (rigid) affine view so range/cone retain world-distance/angle meaning; scaled
  or sheared views fail explicitly. View-space positions have the same bounds.

The original 7.3 lighting ABI v7 is 160 bytes. It retains v6 offsets and adds float4
`light_position_range` at 128 and `spot_parameters` at 144; static assertions
and SPIR-V reflection verify size, offsets and names. `light_direction.w` is
0/1/2 for directional/point/spot; `light_factors.w` is attenuation power.
Spot parameters are inner cosine, outer cosine, falloff and reserved zero.
The mask adds bit 8192 to v6's 4096. The current shaders extend this to v9 below;
old v6 shaders remain accepted with exactly 128 bytes, but cannot bind as v7.
RHI uniform capability/binding checks include 160 bytes and reject missing or
short constants. Each draw still gets its own frame-fenced uniform slice.

`xray_render_stage7_lighting` checks local schema, axes, normalization, numeric
bounds, immutable/atomic updates, type switches and shared scene limits.
`xray_render_stage7_local_lighting_gpu` checks numeric diffuse/specular/combine,
range edges, coincidence, attenuation powers, inner/outer/hard cones, falloff,
camera/light movement and snapshots, perspective/orthographic reconstruction,
mixed light types, resize and eight light lifecycle resource/capacity baselines.
Actual DXC rejects mutated v7 fields; offscreen GPU tests reject a short v6
buffer and missing v7 constants. The asset probe still uses its documented
directional controls; these fixtures do not claim real-asset point/spot or DX11
final-frame parity. Later full integrated scene acceptance is documented in 7.8.

Verified 2026-10-08: full GCC/MinGW-w64 Ninja Debug build, 50/50 CTest tests
(22 GPU), 9/9 Python tests, and three repeats of five focused lighting/DXC/ABI
tests. The CPU light test rejected 90 invalid requests; all GPU runs reported
validation 0 errors/0 warnings. Logs: `build/stage7-local-lighting/regression.log`
and `repeat.log`; capture: `build/windows-x64-clean/vulkan-stage7-local-lighting.bmp`.

At this local-lighting-only run, shadow maps/cascades, sky/forward,
HDR/tonemap/bloom/AA and final scene acceptance were still open; their later
completion is documented below. Local lighting alone did not close M7. GCC Release,
additional GPUs and normal game/editor startup retain their Stage 8/9 boundaries.

### Ambient, Constant Emission And Fog (7.4)

`scene_renderer::set_lighting_environment(view, environment)` queues a value copy
of `lighting_environment`; the view validates and atomically replaces its state.
Every queued draw captures another copy alongside its camera/lights/materials.
Defaults are zero ambient, disabled fog, and zero emission for older materials.
The common facade API does not activate normal game/editor startup.

Environment profile:
- `ambient.rgb` is finite gamma-encoded [0,1], converted with pow(rgb,2.2).
- `fog_color.rgb` is finite linear radiance [0,1]. `fog_near` is [0,1e6] view-space
  distance; `fog_distance` is a positive exponential decay distance [0.001,1e6],
  not a far clipping plane. Invalid updates preserve the previous state.
- The occupied static surface result is
  `direct_combine + linear_albedo * pow(ambient,2.2) + linear_emission`.
  Fog then uses `1-exp(-max(view_z-fog_near,0)/fog_distance)` to interpolate this
  result toward linear fog color. Hardware depth and inverse projection recover
  view Z for perspective/orthographic cameras, including split viewports.
- Empty background remains black; sky integration is 7.6. Raw diffuse/specular
  debug accumulators do not include ambient, emission or fog.

Material profile:
- Disk `use_emissive`, `use_emissive_map`, `texture_emissive`, `constant_emissive`
  (float4) and `constant_emissive_multiplier` (float) retain their original names
  and wrapped/unwrapped typed binary-config semantics. Constant RGB is [0,1],
  multiplier [0,16]; both constants must be present together. The stored linear
  RGB product is [0,16], independent of diffuse tint, texture and direct lights.
- Active `use_emissive && use_emissive_map` fails explicitly: emissive texture
  sampling is unsupported. An inactive map flag (`use_emissive=false`) remains
  valid and contributes zero, even when `use_emissive_map=true`.
- `contributing_material_request` wraps the existing immutable surface request
  and emission while respecting the 64-byte resource userdata limit. Cooked
  resources copy configuration/surface/map/emission values; both immediate and
  asynchronous texture-dependency completion preserve the contribution.
- Constant emission is a deliberate linear contribution, not claimed DX11 final
  parity. The reference `psf_emissive_write.h` stores emission times 0.125, while
  the emission addition in `combine_1.ps` is commented out. This implementation
  defines an explicit unscaled emission contract, rather than guessing a hidden
  exposure compensation. Later HDR/tonemap/exposure acceptance is documented in
  7.7 below; final integrated M7 acceptance is documented in 7.8.

Pass/ABI changes:
- Material geometry now writes three cleared RGBA16F targets: linear albedo,
  linear specular, and linear emission with occupancy A=1. The same cutout,
  culling, depth EQUAL and stencil EQUAL 0x82 gates protect all three outputs;
  depth/stencil writes stay disabled. Emission A=0 marks cleared holes/background.
- Direct lighting/combine are unchanged. After combine has consumed both light
  accumulators, `environment_ps` reads combine, emission/occupancy, albedo and
  hardware depth; it reuses diffuse accumulator storage for the final linear
  result in lit mode only. There is no simultaneous attachment/sample feedback,
  extra lighting texture or CPU readback. Every input transitions to
  `shader_resource`/`depth_read` before sampling; the reused output transitions
  RT -> shader_resource before presentation. Resize/release use fenced retirement.
- Material ABI v8 is 160 bytes: v4 plus float4 `emissive` at offset 144, mask bit
  16384. Lighting ABI v9 is 208 bytes: v7 plus float4 `ambient` at 160,
  `fog_color_distance` at 176, `fog_parameters` at 192, mask bit 32768. All three
  lighting/combine/environment PS entries share v9. Reflection checks member
  names/types/offsets; old material and lighting v6/v7 layouts remain supported.
  Missing/short explicit constants and mixed ABI families/versions are rejected.

Source projection/omission audit:
`prepare_real_static_pack.py --material-mode surface` retains all five emission
keys, including inactive map gates; it never invents an emissive texture. Every
other omitted field remains verbatim in provenance alongside the resolved source
material. This is a restricted static profile, not full original material parity.
The barrel and woodbox have zero constant emission with emission disabled, so
active emission acceptance is based on cooked/typed fixtures, not those models.
For these packs the remaining semantics are explicitly unsupported: anisotropic
direction textures, diffuse attenuation/power maps, parallax, sequences, detail
diffuse, Fresnel textures, translucency, reflection/cubemap/mask, and variation
mask/color processing. Their enable flags are false except barrel's
`use_variation_mask=true` with an empty `texture_variation_mask`; this is an
unresolved source feature, not a proven neutral/default shader result. Woodbox's
sky cubemap path is inactive (`use_reflection=false`). The active legacy BRDF fixes
alpha_model_parameter=0 and the old diffuse-power assignment is commented out;
nonzero/other semantics are not implemented. Full configs continue to fail
strict parsing, and projection omissions must not be interpreted as support.

Verification details and real-asset report paths are recorded in the canonical
Stage 7 plan. `xray_render_stage7_environment` covers typed/disk schema, dependency
callbacks, enable gates and immutable/atomic state. The GPU fixture checks linear
composition, fog/camera/projections, split snapshots, cutout/occlusion in both draw
orders, resize, eight recooks and queued shutdown. Actual DXC covers v8/v9 and
mutations; offscreen tests reject short/missing/mixed constants. The emission probe
adds sixteen recook cycles with lights-off radiance: zero-intensity lights must
match removed lights, not an empty black frame when material emission is active.

### Shadow RHI And Scene Shadows (7.5)

Directional, point and spot scene shadows are implemented for the static opaque/cutout profile.
This is the accepted bounded 7.5 profile, not another milestone. Integrated
artifact acceptance is documented below. No shadowed real-asset or DX11 final-frame
parity is claimed by the current fixture acceptance.

`static_shadow.hlsl` reuses `static_vs` and frame/object ABI v1 (128/96 bytes).
Its depth-only `shadow_ps` has no color output. Opaque and alpha-test variants
share world/camera transforms; cutout uses tint alpha, scaled UV, diffuse alpha
at LOD 0 and the material threshold. Both the RHI test and scene caster pass
select this shader; caster geometry/textures/samplers reuse the static cache.

The current set 1 layout retains bindings 0-5 and adds:

- Binding 6: fragment `SamplerComparisonState`, RHI sampler slot 1.
- Binding 7: fragment non-array, single-sample 2D shadow depth, RHI texture slot 5.

Sampler slot 0 stays ordinary; all six texture slots can coexist with both
samplers. Pools hold 384 sampled images, 128 samplers and 128 uniform descriptors
for 64 draws/192 sets; fence-gated reset, immutable per-draw snapshots and
retirement are unchanged. Physical descriptor limits are checked at startup.
Unused entries use existing fallback resources. A used shadow pair requires
explicit live handles; color images, missing/non-comparison samplers, stale
handles, uninitialized depth and attachment feedback are rejected before
pipeline creation. Ordinary sampling rejects a comparison sampler.

`sampler_desc` defaults to `comparison=false`, `compare=less_equal`; all eight
operators are supported. Its size remains 24 bytes, preserving the 64-byte
material userdata bound. Typed diffuse-material descriptors reject comparison
mode; the additional sampler is a renderer resource, not a material option.
The depth format must advertise
`VK_FORMAT_FEATURE_2_SAMPLED_IMAGE_DEPTH_COMPARISON_BIT` through format-properties2/3.
Ordinary depth lookup still requires nearest filtering; comparison sampling can
use linear PCF. The ordinary linear-filter format requirement does not apply
when comparison is enabled. See the official
[draw requirements](https://docs.vulkan.org/refpages/latest/refpages/source/vkCmdDraw.html)
and [sampler contract](https://docs.vulkan.org/refpages/latest/refpages/source/VkSamplerCreateInfo.html).

Reflection adds mask bits 65536/131072 for shadow depth/comparison sampler.
Existing constant ABI versions remain accepted; the scene shadow receiver uses
v10 below. DXC emits `OpTypeImage Depth=2` even for comparison;
the contract follows direct `OpLoad`/`OpSampledImage` operands and requires Dref
sampling to use precisely bindings 7/6, while ordinary sampling uses binding 1.
Both bindings must be present and used for comparison. Projected/gather/fetch
shadow accesses, indirect resource aliases and arbitrary untrusted SPIR-V are
outside this narrow contract. Comparison is an instruction property, not just
the image type's Depth operand; see the
[SPIR-V specification](https://registry.khronos.org/SPIR-V/specs/unified1/SPIRV.html).

`set_depth_bias(constant, slope)` accepts finite constant +/-1e6 and slope +/-16;
nonzero bias requires a depth attachment and enabled depth test. Zero disables
bias; frame start resets it, and pass callers must reset it when appropriate.
The enable bit is part of pipeline equality/hash, but magnitudes are dynamic and
do not create pipelines. Clamp is always 0; the optional `depthBiasClamp` feature
is not enabled. These are Vulkan rasterizer units, not the reference renderer's
receiver z-offset, so no equivalence with its bias values is assumed. See
[vkCmdSetDepthBias](https://docs.vulkan.org/refpages/latest/refpages/source/vkCmdSetDepthBias.html).

`xray_render_stage7_shadow_rhi_gpu` checks D32/D24S8 depth-only opaque/cutout,
all eight comparisons, simultaneous ordinary/comparison sampling, linear PCF,
caster movement, occlusion in both draw orders, positive/negative constant and
slope bias, dynamic-value pipeline reuse, window/map resize, sampler retirement
after recording and sixteen resource/capacity drain baselines. CPU/DXC tests
cover caster permutations, malformed bindings/sampling, typed material rejection
and bias pipeline-key collisions. Verification evidence is in the canonical plan.

Directional scene contract:

- `does_cast_shadows` enables directional shadows. `sun_shadow_map_size` is a
  per-cascade power-of-two resolution 64..2048; 0 defaults to 512.
  `num_sun_cascades` is 1..4; 0 defaults to 4. `shadow_transparency` is finite
  [0,1], with visibility `lerp(comparison,1,transparency)` in linear direct light.
  Disabled shadow fields are inactive; spot uses the local profile below, not
  these directional fields. Shadowers/automatic sun location/LPV stay rejected.
- Camera views must be rigid affine. Standard finite forward-Z perspective,
  off-center perspective and orthographic projections are supported; oblique
  depth/clip-W projections are rejected. Planner points must be finite within
  +/-1e7 world/light/view units. This bound is not a precision guarantee at large
  world coordinates. Nonuniform model scale is allowed via existing transform
  validation. Hidden surfaces are not casters.
- Queue snapshots capture all visible static surfaces independently of camera
  culling, including off-camera casters, together with geometry/material/texture
  revisions, camera, light settings, split depths and receiver matrices. The
  camera culling statistics continue to describe camera draws, not shadow draws.
  Each cascade performs its own conservative local-frustum caster cull.
- Perspective splits blend uniform/logarithmic spacing 50/50; orthographic
  splits are uniform. Each receiver slice fits a rounded bounding sphere with
  texel-snapped light XY and a filter guard. Depth extends to overlapping caster
  bounds, so casters before/behind the camera near/far planes can affect visible
  receivers. Adjacent views overlap the preceding slice's last 10%; the receiver
  crossfades comparisons across that interval.
- One D32 atlas is reused sequentially across all three light types: one tile for
  one directional cascade or spot, 2x2 for 2..4 cascades, 3x3 for six point faces.
  Unused tiles clear to depth 1. Linear PCF
  coordinates clamp half a texel inside the selected tile to avoid tile bleed.
  Raster bias is constant 1.25/slope 1.75, clamp 0, and receiver depth bias .0001.
  These are explicit initial profile values, not legacy bias equivalence or
  tunable per-light controls. Bounded thin/grazing cases are accepted below;
  arbitrary production-scene tuning is not implied.
- Accumulators clear once. Before a shadowed light, end their pass, render the
  atlas in depth_write, transition it to depth_read, then resume both accumulators
  with LOAD and ONE/ONE ADD. Restore viewport, shaders, constants, samplers,
  geometry layout and disabled bias/depth/stencil before fullscreen lighting.
  Apply comparison visibility after the legacy gamma/BRDF evaluation to both
  direct diffuse and specular. Ambient/emission/environment composition is unchanged.
  Atlas resize/destruction follows existing fenced handle retirement. When no
  queued shadow draw uses it, release the atlas/sampler; static shadow shaders and
  receiver PS/CB stay in the existing renderer caches until renderer release.
- Receiver constant ABI v10 is 496 bytes: v9's 208-byte lighting prefix,
  `shadow_matrix0..3` at 208/272/336/400, `shadow_splits` at 464 and
  `shadow_parameters` at 480 (count, atlas grid, inverse atlas extent,
  transparency). Directional/spot matrices transform view positions to light
  clip volumes; point uses the analytic profile below without expanding v10.
  Its mask bit is 262144; the production shadow PS mask is 504526. Ordinary
  lighting/combine/environment continue to use v9. Short/missing constants and
  malformed matrix/member/binding layouts are rejected before pipeline creation.

`xray_render_stage7_directional_shadows_gpu` exercises the real facade/queue
path: off-camera caster, 1..4 cascades and depth boundaries, cutout, caster/light
motion, queued camera translation/rotation, mixed shadowed/unshadowed lights in
both orders and two differently directed shadowed lights reusing the same atlas,
linear transparency, ambient/emission independence and direct specular,
perspective/orthographic receivers, split snapshots with changed map size/count,
window resize, eight atlas resource/capacity/pending-pass baselines, full removal
drain and shutdown. CPU tests cover schema, split ordering/coverage, matrix packing,
sub-texel grid stability, invalid projections and atomic updates. This is a
deterministic fixture result, not performance acceptance or exhaustive artifact
coverage for production scenes. This initial directional fixture alone did not
close 7.5; the later integrated acceptance below completes the bounded task.

Spot scene contract (same 7.5 task):

- `does_cast_shadows` enables one perspective map, stored in the same D32 atlas
  and submitted through the same static opaque/cutout caster pass as directional
  lights. `shadow_map_size_index` 0/1/2 selects 1024/512/256, matching reference
  `stage_lights::index_to_shadow_size`. Other indices fail atomically. The legacy
  `shadow_map_size` is unused, as in that reference path; directional size/count
  fields and point face flags are inactive for spot lights. Disabled shadow
  fields are ignored, including malformed inactive index/bias values.
- Projection uses the outer full cone angle, aspect 1, near=`range/1000`,
  far=`range`, matching reference `make_spot_light_shadowmap`. Position and
  normalized legacy -Y travel direction are captured in world space before
  view-space lighting conversion. A canonical perpendicular basis handles
  vertical directions; spot roll does not rotate the square filter grid.
  The light projection covers the circular cone; existing cone/range/falloff
  attenuation still gates actual direct light. Camera views must be rigid, with
  translation components within +/-1e7; this is a bounded profile, not a large-
  world precision guarantee. Shared shadow planning rejects larger translations
  before inversion/multiplication can overflow receiver constants.
- Receiver transform is full `inverse(camera_view) * light_view * projection`,
  using the existing `math::mul4x4` for perspective multiplication. Legacy
  `float4x4::operator*` is affine-only and cannot multiply a perspective matrix.
  ABI v10 stays 496 bytes: spot uses matrix 0, count/grid 1 and ignores splits.
  The shared `USE_SCENE_SHADOW` shader selects the spot receiver from light type.
  `spot_parameters.w` now carries normalized receiver depth bias for shadow
  draws; ordinary lighting still ignores it. Directional retains .0001.
- `local_light_z_bias` must be finite [0,1] and maps to receiver bias `0.1*value`,
  preserving the reference CPU scaling, not claiming identical DX11 filtering
  or artifacts. Zero is valid; raster bias remains constant 1.25/slope 1.75.
  `shadow_transparency` is finite [0,1] and applies in linear direct visibility.
  Visibility affects direct diffuse/specular only; environment composition is
  unchanged. Atlas resize/reuse, LOAD accumulators, fenced resources and queued
  caster/light/camera/material snapshots use the shared scene shadow lifecycle.

`xray_render_stage7_spot_shadows_gpu` checks all three index sizes, zero/scaled
bias, linear transparency, off-camera and near-clipped casters, textured cutout,
caster/light/cone motion, direct specular and ambient independence, mixed point/
spot and shadowed directional/spot lights in both orders with atlas resizing,
queued camera/light/caster snapshots and split views, perspective/orthographic
receivers, window resize, eight resource/capacity/pending-pass baselines,
removal drain and shutdown. CPU tests check index/bias schema, inactive fields,
atomic updates, near/far and outer-cone coverage, rigid views and receiver
packing. Current spot acceptance is fixture-based, not a shadowed real-asset,
performance, other-GPU or DX11 parity claim. The integrated artifact acceptance
below completes 7.5 without introducing another sub-stage.

Point scene contract (same 7.5 task):

- `does_cast_shadows` enables six 90-degree world-axis perspective views in
  reference order +X/-X/+Y/-Y/+Z/-Z, with the reference face up vectors.
  Light basis rotation/scale does not rotate these views. Near is 0.025,
  far is `range`; shadowed point range must exceed 0.025 + `math::epsilon_5`
  (0.00001), avoiding the projection constructor's assertion at degenerate
  near/far separation. Unshadowed point range bounds stay unchanged.
- `shadow_distribution_sides[0..5]` are copied into an immutable six-bit mask.
  A disabled face contributes unshadowed direct light; all false does not mean
  all enabled. Only enabled faces submit casters. The atlas is still cleared
  when the configured shadow mask is empty. Local size index, receiver bias,
  transparency and inactive directional/legacy size fields follow the spot
  contract. Shared rigid-camera and finite position bounds apply.
- Six tiles use a square 3x3 D32 atlas (three unused tiles); maximum extent is
  3072 for a 1024 face. This preserves the existing 2D descriptor/comparison
  sampler layout. Atlas resizing, per-face frustum culling, opaque/cutout
  casters, LOAD accumulators and fenced retirement use the shared renderer.
- ABI v10 remains 496 bytes. Point matrix 0 is the affine transform from view
  position to world-space light-relative position, not a face clip matrix.
  `shadow_splits` carries perspective A/B and the exact six-bit face mask in
  xyz (w reserved). The receiver selects the dominant absolute world axis,
  with X then Y then Z tie ownership, reconstructs the matching face XY and
  depth `A+B/dominant_distance`, then samples its atlas tile. Other matrices
  are inactive for point receivers. The light-type branch isolates this
  interpretation from directional splits and spot projection.
- PCF is tile-clamped, not cross-face cubemap filtering. The original point
  fixture checks two adjacent-face boundaries; broader bounded edge/corner
  and thin/grazing acceptance is documented below. Neither test claims
  seamless cubemap filtering for arbitrary sub-texel geometry.

`xray_render_stage7_point_shadows_gpu` reuses the local-shadow fixture and adds
all six translated/rotated face orientations with opaque and cutout casters,
individual/empty masks, face-boundary continuity, isolated queued mask snapshots,
and shadowed point/spot atlas resizing in both orders. Shared tests cover all
three sizes, bias/transparency, off-camera/near-clipped casters, caster/light/
camera motion, direct diffuse/specular and ambient independence, directional
mixing, projections, resize, eight resource/capacity/pending-pass baselines,
removal drain and shutdown. CPU checks cover all 64 masks at three sizes and
two camera views, six face near/far/90-degree coverage, receiver packing,
atomic updates and bounds. A scalar oracle checks all 16 elements of full
matrix multiplication: point integration exposed and fixed the old `[2][3]`
formula using `left.e30` instead of `left.e20` in core `math::mul4x4`.

Previous spot-only verification 2026-10-09: GCC/MinGW-w64 Ninja Debug, 56/56 CTests (27 GPU),
10/10 Python tests; two CPU/DXC and five GPU focused tests each passed three
repeats, plus three final CPU camera-bound repeats. RTX 4070 validation 0/0.
Evidence: `build/stage7-spot-shadows/regression-verified.log`,
`focused-repeat.log`, `cpu-bounds-repeat.log`, `python.log` and
`verification-summary.json`. System Vulkan loader 1.4.363, GPU API 1.4.351;
no app-local Vulkan DLL. Spot capture above inspected; no new shadowed real-
asset acceptance is claimed by these fixture runs.

Previous point integration verification 2026-10-09: GCC/MinGW-w64 Ninja Debug, 57/57 CTests
(28 GPU), 10/10 Python tests. Two CPU/DXC and six GPU focused tests each passed
three repeats. RTX 4070 validation 0/0, system Vulkan loader 1.4.363 / GPU API
1.4.351, no app-local Vulkan DLL. Evidence: `build/stage7-point-shadows/`
`regression.log`, `focused-repeat.log`, `dxc-repeat.log`, `python.log` and
`verification-summary.json`. Final and representative face captures inspected;
RGB samples of all six saved cutout faces checked. No new shadowed real-asset,
DX11 parity or complete 7.5/M7 acceptance was claimed by that earlier run alone.

Integrated shadow artifact acceptance:

`xray_render_stage7_shadow_acceptance_gpu` exercises the real facade/resource/
queue path with deterministic analytic expectations, not DX11 screenshots.
It complements the per-light tests rather than adding a new milestone.

- Nine contact profiles: all three light types at 256/512/1024 resolution,
  a 0.04-wide planar caster separated by 0.02 from its receiver, no-caster
  acne samples, bounded shadow footprint and caster movement. Spot uses a
  90-degree outer cone, local range 4, receiver bias 0; directional retains
  receiver bias .0001 and all types retain raster bias 1.25/1.75. A 160-degree
  cone at 256 maps this caster below one texel and cannot guarantee retention.
- Six grazing profiles: all three types at 75/82 degrees, 1024 resolution,
  width 0.12, gap 0.02, lit/no-caster and shadowed samples. These are explicit
  bounded acceptance inputs, not a guarantee for arbitrary grazing geometry,
  map resolution, receiver bias, light range or large-world precision.
- All 12 point-cube edges and eight corners at three resolutions: 60 profiles,
  each at the exact tie and three 0.1% axis perturbations, totaling 240 views.
  Dense CPU ray-silhouette expectations check 927360 RGB samples outside the
  filter/raster band with tolerance 5. The band is 0.04 world units at 256,
  halved per resolution doubling; inside it radiance must stay within the
  unshadowed bound plus 5. Partial PCF coverage must occur. This bounds aliasing
  and face/tile bleeding; it does not assert exact cross-face filter weights.
- One mixed frame uses cooked normal/roughness dependencies, colored albedo,
  directional/point/spot shadows, cutout, ambient, linear emission and fog.
  Numeric samples check direct/environment composition and empty background.
  Reversing light order compares every RGB pixel (tolerance 2), not only spot
  samples. Camera translation and queued caster visibility use the same frame.
- Window resize and eight geometry/material recooks vary local sizes and
  directional size/cascade count. Live resources, material capacity and pending
  pass objects remain at the warmed baseline. Sequential atlas resizing can
  leave fenced textures retired while drawing; this queue must stay bounded
  by the warmed maximum and drain to zero after removal, not be zero mid-frame.
  This accepts correctness/lifetime, not the cost of per-light atlas resizing.

Saved captures: `build/windows-x64-clean/vulkan-stage7-shadow-contact.bmp`,
`vulkan-stage7-shadow-grazing.bmp`, `vulkan-stage7-shadow-corner.bmp`,
`vulkan-stage7-shadow-acceptance.bmp` and its `-resized.bmp` variant.

Accepted 2026-10-09: GCC/MinGW-w64 Ninja Debug, 58/58 CTests (29 GPU),
10/10 Python tests. Four GPU shadow tests passed three repeats each; the
integrated fixture reports 22334 partial PCF samples and full-image reverse-
order maximum RGB error 1. Live baseline [11,16,15,6,0], capacity [1,1,1,1],
pending pass objects 22, active retirement bound [0,0,0,6,0], final drain zero.
RTX 4070 validation 0/0. Evidence: `build/stage7-shadow-acceptance/`
`regression.log`, `focused-repeat.log`, `python.log`, `build-final.log` and
`verification-summary.json`. All five captures above inspected. System Vulkan
loader 1.4.363, GPU API 1.4.351; no app-local Vulkan DLL. This closes bounded
static opaque/cutout shadow task 7.5, not M7, real-asset/DX11 parity, performance,
GCC Release or other-GPU acceptance. Next: 7.6 skybox/forward transparency.

### Cubemap Textures (7.6)

The texture path now accepts complete legacy DDS cubemaps with six square faces,
RGBA8/BGRA8 or DXT1/DXT3/DXT5 (BC1/BC2/BC3), linear or sRGB. The existing
4096 dimension and 64 MiB file limits still apply. Require COMPLEX caps and all
six face flags; partial cubes, volumes, cube arrays, DX10 headers and extra or
truncated payloads fail before publication. Face order is +X,-X,+Y,-Y,+Z,-Z,
with all declared mips of one face before the next, following the
[DDS cubemap layout](https://learn.microsoft.com/en-us/windows/win32/direct3ddds/dds-file-layout-for-cubic-environment-maps).
`texture_data::mips` remains the storage vector, now explicitly layer-major;
2D textures still have one layer and their previous indexing is unchanged.

RHI uploads require `type_cube`, six layers and square faces. Each declared
layer/mip appears exactly once, in arbitrary input order with validated row
pitch. Repacking and zero initialization cover all six layers. Vulkan creates
a CUBE_COMPATIBLE image and CUBE view, copies each layer/mip and transitions the
entire subresource range. Device cube-dimension/array-layer limits are checked.
Updates retain the existing copy-on-update, fence-retired descriptor snapshots.

Descriptor ABI v11 adds mask bit 524288 for non-array `TextureCube` at set 1,
binding 0, alongside the existing image bit 4 and ordinary sampler bit 8.
Constant layouts and descriptor-set layouts are unchanged. Cubes require an
explicit cube texture; missing/2D bindings, cube-to-Texture2D bindings,
comparison sampling, cube arrays and cubes in surface-map slots are rejected.
Static diffuse/specular/normal/roughness/detail materials still require 2D
textures. Cooked cubes retain the checked sRGB footer and immutable snapshots.
Disk reload permits size/mip/color-space changes but rejects a 2D/cube type
change atomically, keeping the previous image and reporting a failed reload.

Coverage: `xray_render_stage7_cubemaps` checks 56 RGBA/BGRA/BC linear/sRGB
cases (sizes 1/2/3/4/7/8/16), reversed/padded/zero uploads and 4332 malformed
DDS/upload rejections. Extended `xray_render_stage5_reload` exercises cooked
cubes, size/mip/footer changes, old snapshots, malformed reload retention,
both type-change directions and static material rejection.
`xray_render_stage7_cubemap_gpu` checks 32 frames and 4608 RGB channels: all
six face orientations, four mip levels, nearest sampling, RGBA/BC1/BC2/BC3 linear/sRGB, an update
between the top and bottom rows of the same recorded frame, and destruction
before submit. Resize preserves warmed pipelines/pools; live counts remain
stable, retired textures stay bounded and drain to zero. The first RGBA capture,
`build/windows-x64-clean/vulkan-stage7-cubemap.bmp`, was inspected visually.

This foundation run was texture-path acceptance on deterministic fixtures,
not scene skybox, cross-face linear filtering or transparency acceptance.
Actual source sky assets found in
`C:/Stalker2_2011/xr2-dsgn/resources/sources/textures/test/` include
`sky_19_cube.tga` and `sky_20_cube.tga` with `texture_dimension=3` options.
That layout was rejected at the time of the foundation run; the following
skybox work adds import and scene integration. The subsequent forward acceptance
below completes bounded 7.6; these texture tests alone do not close M7.

Verified 2026-10-09: GCC/MinGW-w64 Ninja Debug, 60/60 CTests (30 GPU),
10/10 Python tests; the two cube tests and extended reload test each passed
three focused repetitions. RTX 4070 validation errors=0/warnings=0. System
loader/headers 1.4.363, GPU API 1.4.351; no app-local Vulkan DLL.
Evidence: `build/stage7-cubemaps/build.log`, `focused-repeat.log`,
`regression.log`, `python.log` and `verification-summary.json`.

### Scene Skybox (7.6)

TGA options `texture_dimension=3` selects a horizontal 6:1 strip of square
faces in +X,-X,+Y,-Y,+Z,-Z order. It is not inferred from a filename or aspect
ratio. The source limit is 128 MiB and 12288x2048 (2048 per cube face); ordinary
2D TGA remains bounded to 64 MiB and 4096 per axis, DDS to its existing limits.
Raw/RLE and origin normalization use the same bounded stb adapter. Requested
mips are generated per face in layer-major order, with sRGB averaging in linear
space and no atlas-neighbor bleed. RGBA8 GPU upload needs no intermediate DDS,
disk cache or new decoder library. `NORMAL_MAP` packing is invalid for cubes.
Physical reload rereads adjacent options; malformed or type-changing reloads
retain the old immutable texture. Cube/2D dimension changes are not hot-swaps.

`sky_material_configuration` in `sky_resources.h` is accepted by the existing
`material_class` cook through typed user data. It contains a same-session cube
texture, linear RGB color in [0,1], intensity in [0,16] and finite Y rotation
in radians within +/-10000. Configuration is copied immutably, retaining the
texture resource. `scene_renderer::set_sky_material(scene, material)` validates
the typed resource/session; null removes sky. An ordinary static material or
2D texture is not a sky material. Legacy disk sky-material scripts are not
parsed by this typed path.

The lit draw queue freezes texture, color/intensity/rotation and camera. The
shader reconstructs a world ray using inverse projection and inverse affine
view basis, ignoring camera translation. Perspective, orthographic and identity
projections follow the existing forward-Z camera contract. The sampler uses
linear cube filtering at explicit LOD 0; sRGB texture views decode before the
linear color/intensity multiplier. Sky is not lit or fogged again.

Ordering is GBuffer -> material inputs -> shadow/light accumulators -> linear
FP16 combine -> sky -> existing sRGB preview. Sky LOADs the combined target and
scene D24S8, writes only background (depth <= 1 and stencil == 0), and does not
write depth or stencil. Opaque and surviving cutout fragments keep their lit
result; cutout holes reveal sky. The color returns to shader-resource state,
depth to depth-read. Debug and unlit views do not render sky. This is not HDR
tonemapping by itself: raw diagnostic preview clips above 1, while the final lit
presentation now uses the 7.7 exposure/tonemap described below.

GPU cube uploads are cached by immutable CPU snapshot, weakly tracked and
collected when no scene/queued draw uses them. Existing backend fence retirement
protects submitted descriptors/images. Reload, removal and resize use the same
resource lifetime rules as static materials; no separate sky cache is written.

`xray_render_stage7_sky_assets` passes 192 raw/RLE, RGB/RGBA, origin, size and
linear/sRGB cases, isolated gamma mips and 125 invalid source/assignment/owner
cases, plus typed schema rejection, cook snapshots, options/reload rollback and
late material/texture cleanup. `xray_render_stage7_skybox_gpu` passes 456 RGB
checks: all six camera orientations, exact full-image translation invariance,
sky rotation/tint/intensity, perspective/orthographic/identity, lit opaque and
cutout depth/stencil reuse, debug isolation, queued split material/camera and
old/new reload snapshots, resize, eight recook baselines and removal/drain.
A direction-encoded cube supplies an independent analytic RGB oracle for 80
views across all 12 edges and 8 corners, including tie and 0.1% axis perturbations,
with linear cross-face filtering (tolerance 4). Three focused tests, including
the unchanged 2D TGA regression, passed three repetitions with validation 0/0.

Real read-only source runs use `--skybox-real ABSOLUTE_TGA_PATH` on the existing
GBuffer test executable; only this mode expands its raw-resource arena to
256 MiB for the large source file. Original adjacent options are read by the
normal texture cook, without conversion or input rewrites. Verified assets in
`C:/Stalker2_2011/xr2-dsgn/resources/sources/textures/test/`:

- `sky_19_cube#small.tga`: 192x32 strip, 32px faces, six mips.
- `sky_20_cube.tga`: 6144x1024 strip, 1024px faces, eleven mips.
- `sky_19_cube.tga`: 12288x2048 strip, 2048px faces, one mip, source over 64 MiB.

Each real run passes 426 RGB checks and validation 0/0. SHA-256 of all six
external TGA/options inputs matches before/after the final runs. Reload is
tested only on fixtures, never by modifying external assets. Perspective resized captures
were inspected visually. Evidence is in `build/stage7-skybox/`: `build.log`,
`focused-repeat.log`, `regression.log`, `python.log`, `verification-summary.json`,
`real-small.log`, `real-sky20.log`, `real-sky19.log` and `real-*.bmp.resized.bmp`.
Final regression 2026-10-09: GCC/MinGW-w64 Ninja Debug, 62/62 CTests (31 GPU),
10/10 Python tests, RTX 4070 validation errors=0/warnings=0; no app-local Vulkan DLL.
This is single-cubemap sky acceptance, not procedural
weather/clouds, legacy sky-material script parity, a complete real-asset scene
or DX11 image parity. The following forward run completes bounded 7.6;
At that sky-only run 7.7/7.8 and M7 were still open; later 7.7 evidence is below.

### Forward Transparency (7.6)

The static forward profile is unlit, straight-alpha or alpha-weighted additive.
It composites into the lit linear FP16 result after sky and before the current
sRGB preview. It is intentionally separate from opaque/cutout material inputs
and does not claim lit glass, refraction, particle/sub-UV effects or legacy
forward-material parity. Intersecting surfaces, per-triangle sorting and OIT are
outside this center-sorted static profile.

Typed `forward_material_request` retains a shared `material_configuration`
base and selects `material_blend_mode::alpha` or `additive`; the existing
`material_class` cook copies the base immutably. Opaque/unknown blend values,
missing bases and alpha testing are rejected. Texture/session/type, tint,
sampler, cull and UV bounds use the existing validation. Transparent materials
do not accept normal/roughness/specular/detail maps, surface BRDF parameters
or separate emission contributions, since this shader does not consume them.

Cooked binary configs use the normal material query/dependency path with an
exclusive `material.f_stage.effect` table:

- `effect_id="f_stage_unlit"` is the explicit Vulkan static unlit profile.
- Integer `blend_mode=1` selects alpha, `2` additive. Other modes fail.
- `constant_diffuse` is finite float4 [0,1], RGB gamma tint and linear opacity.
- Boolean `use_tdiffuse` requires a valid `texture_diffuse` path when true.
- Optional `two_sided` defaults false; optional `constant_tile_u`/`v` must occur
  together, finite within +/-4096. Existing wrapped `{value=...}` fields work.

Unknown effects/fields, mixed `g_stage`/`f_stage`, unsupported lighting/cutout
inputs and missing texture dependencies fail instead of publishing an opaque
fallback. Existing `g_stage_default` behavior is unchanged; fractional alpha
there still requires cutout. Source Lua forward-material conversion and original
particle effects are not implemented by this binary profile.

Visible forward surfaces are frozen separately from opaque models and shadow
casters. The queue stable-sorts their local mesh AABB centers transformed by the
model and then view matrix, in descending camera Z. Equal depths retain scene
submission order; distinct-depth layers do not depend on model insertion order.
Sorting uses the same immutable geometry, transform, material and texture
snapshots as the draw. Debug/unlit views omit forward surfaces entirely.

The forward pass LOADs the combined color and scene D24S8, tests LESS, disables
depth/stencil writes and stencil testing, and restores color shader-resource /
depth-read states afterward. Static geometry/texture/sampler caches and backend
fence retirement are reused. No new RHI constant layout or descriptor binding
is needed: static VS uses the existing 128-byte frame / 96-byte object ABI,
and the two forward PS variants use the same object and diffuse bindings.
RGB is `pow(tint.rgb,2.2) * sampled_texture.rgb`, with sRGB texture decoding
performed by the texture view. Opacity is `tint.a * sampled_texture.a` without
gamma conversion. Explicit texture LOD is 0. Alpha RGB blend is
`source * alpha + destination * (1-alpha)`; additive is
`source * alpha + destination`. Target alpha is not a coverage buffer.
These unlit surfaces neither receive scene lighting/fog/shadows nor cast shadows.

CPU `xray_render_stage7_transparency` covers 32 camera rotations with 64 affine
model centers each, nonuniform scale/shear, 64 stable equal-depth ties, 14
sort/parser/owner/unsupported-input rejections, typed immutable alpha/additive
and binary solid/textured material cooks, missing dependencies and late cleanup.
GPU `xray_render_stage7_transparency_gpu` covers 111 numeric RGB checks, exact
full-image reverse-insertion equality, alpha 0/1, solid/textured additive,
linear/sRGB option reload, lit opaque/cutout depth with sky, directional shadow
caster exclusion, reverse camera order, queued camera/material/reload snapshots,
resize and eight warmed texture reload/material recook baselines with retirement
drain. Two equal-depth layers in both insertion orders with LESS detect accidental
depth writes. Hardware-depth/stencil/material/unlit debug images are identical
with and without transparent surfaces. Captures
`build/windows-x64-clean/vulkan-stage7-transparency.bmp` and `.bmp.resized.bmp`
were inspected. Four focused sky/forward tests passed three repetitions with
RTX 4070 validation errors=0/warnings=0. Evidence: `build/stage7-transparency/`
(`build.log`, `build-tests.log`, `build-final.log`, `focused-repeat.log`).
Final regression 2026-10-09: GCC/MinGW-w64 Ninja Debug, 64/64 CTests (32 GPU),
10/10 Python tests, validation 0/0. Full logs: `regression.log`, `python.log`,
`verification-summary.json` in that directory. Together with the sky acceptance,
this closed 7.6 in the stated static unlit profile. Subsequent 7.7 HDR/postprocess
acceptance is below, followed by integrated full-frame M7 acceptance in 7.8.

### HDR Presentation (7.7)

`facade/postprocess_settings.h` defines per-view `exposure_ev` (finite [-16,16],
default 0) and `enabled` (default true). `scene_renderer::set_postprocess_settings`
queues a value copy; `enable_post_process` now toggles the real view state without
discarding exposure. Invalid updates, including when disabled, reject atomically.
The draw queue snapshots settings with the scene/camera/material state.

For `lit_view_mode` only, the complete linear FP16 scene passes through
`x=max(scene_rgb,0)*exp2(exposure_ev)` and per-channel `x/(1+x)`. This is a simple
global Reinhard curve applied independently to RGB, not the full photographic
luminance/local/adaptation algorithm, filmic rendering or DX11 postprocess parity.
Reference: [Reinhard et al., Photographic Tone Reproduction](https://w3.impa.br/~lvelho/ip02/papers/reinhard/).
Exposure is manual; no histogram, auto exposure or temporal adaptation is implied.

Ordering is GBuffer/material inputs -> shadowed direct lights -> combine ->
ambient/emission/fog -> sky -> alpha/additive forward -> optional bloom -> exposure/Reinhard ->
hardware sRGB presentation. Compression happens once after blending, never per
light/material/layer. The final scene texture stays linear RGBA16F and is sampled
in `shader_resource` state. Tonemap reuses the fullscreen preview PS and its
96-byte object ABI: tint.xy is viewport scale, tint.z exposure multiplier,
tint.w enabled flag; parameters.zw remains queued viewport offset. It adds no
GPU target, descriptor slot, buffer or shader variant. Hardware sRGB encoding is
not duplicated in HLSL. Non-lit debug/unlit modes ignore both exposure and tonemap.

The deterministic resource test harness explicitly disables postprocess by
default for existing numeric fixtures; HDR fixtures select production defaults.
Likewise `--view lit` is a raw diagnostic view, not the default production lit
presentation. `xray_vulkan_asset_probe --view hdr` enables the production profile
and retains all lighting/material/GBuffer checks. It saves `hdr.bmp` and
`hdr-bypass.bmp`, requires >64 changed pixels on bypass and exact restoration,
and reports EV/curve/enabled, with bloom=false and aa=false. Its real-asset report
still requires external provenance and visual review, not automatic production parity.

CPU `xray_render_stage7_hdr` checks defaults, EV bounds, immutable/atomic updates
and toggles preserving EV (10 rejects). GPU `xray_render_stage7_hdr_gpu` checks
243 RGB channels against an independent double-precision curve oracle,
radiance 0..16, EV -16..16, direct+emission above
one, raw bypass, debug isolation, sky/alpha/additive before tonemap, queued
settings/material/camera, resize, eight recook baselines and retirement drain.
`xray_render_stage7_probe_hdr` exercises the cooked disk/probe path.

Verified 2026-10-09: four focused HDR/probe tests passed three repetitions,
Python tooling 10/10. Real source-derived barrel with 512x512 diffuse/normal/
specular TGA passed warmup +16 cycles in
`build/real-assets/barrel-radiation-stage7-hdr-gpu/report.json`: 47406 visible
pixels, 41548 exact-RGB bypass changes, exact restore, GPU baseline/final
`[6,12,11,2,0]`, capacity `[1,1,1,1]`, drained cook users/retirements, unchanged
pack and clean shutdown, RTX 4070 validation 0/0. HDR/bypass captures were inspected;
SHA-256 and byte sizes of all ten original `C:/Stalker2_2011` inputs match the
preparation provenance before and after. This is a supported source-derived
material projection with synthetic directional controls, not the unmodified
legacy cooked pack or original-scene/DX11 parity. Evidence: `build/stage7-hdr/`.
Full regression after HDR: 67/67 CTests (34 GPU), 10/10 Python, validation 0/0.
Logs: `regression.log`, `focused-repeat.log`, `python.log` and
`verification-summary.json` in that directory. This accepts exposure/tonemap,
not the then-incomplete bloom/AA portion of 7.7 or final integrated 7.8.

At the HDR-only run, bloom, antialiasing and combined acceptance were still pending;
their subsequent evidence appears below. The resource-backed legacy `set_post_process` API remains
unsupported; only the typed settings/toggle above are active. No additional
substage is created; later integrated full-frame M7 acceptance is documented in 7.8.

### Bloom (7.7)

`postprocess_settings` adds `bloom_enabled=false`, `bloom_threshold=1`,
`bloom_strength=.1`, `bloom_radius=4`. All values must be finite, including when
bypassed: threshold [0,65504] in pre-exposure linear scene radiance, strength
[0,4], radius [1,16] output pixels. The view validates atomically and queued
draws copy all fields. The postprocess enable toggle preserves these settings.
Bloom is opt-in; old HDR, raw diagnostic and unlit views retain their contracts.
Zero strength bypasses all bloom work exactly.

The complete lit FP16 scene after environment, sky and forward is extracted via
`color * max(peak-threshold,0)/max(peak,1e-6)`, peak=max(R,G,B). This preserves
highlight color rather than independently thresholding channels. Extraction
precedes manual exposure; EV scales both base radiance and bloom together.
Horizontal and vertical blur use the normalized binomial weights
`[1,8,28,56,70,56,28,8,1]/256`, sampled at offsets [-4,4]*radius/4 with bilinear
LOD0 sampling. Composite is `min(scene + strength*blur,65504)` in linear FP16,
then the existing exposure/Reinhard and hardware sRGB presentation. This is a
bounded sparse 9-tap full-resolution profile, not a wide mip-pyramid/filmic/lens
bloom or legacy image parity. Large radii can show discrete kernel shoulders.

`bloom_renderer` lazily owns three output-sized RGBA16F targets, three pixel
shaders, one 64-byte buffer and one bilinear sampler. Four passes are extraction
scene->A, horizontal A->B, vertical B->A, composite scene+A->C. Every target is
cleared/stored, transitions RT->shader_resource before consumption, and is never
sampled while attached. There is no depth/stencil attachment or blending. Resize
allocates the whole replacement group before retiring old targets behind fences;
toggle/bypass retains warmed resources without per-frame allocation. Failed pass
recording closes that pass before cleanup. Final C is sampled by presentation.

Filter UVs clamp to the first/last covered pixel centers of the queued viewport,
not the output boundary. This prevents adjacent viewport reads, preserves a
uniform sky at fractional viewport edges and handles one covered pixel; an
empty covered interval skips the filter. Kernels stay in output-pixel units.

Shader ABI v12 is pixel-only `PostprocessConstants`, four float4 fields at
0/16/32/48: viewport, source_bounds, filter, bloom; size 64, version bit 1048576.
It uses set2 binding0 and existing ordinary 2D image slots 0 and optionally 1
(set1 bindings0/2), ordinary sampler binding1; the descriptor layout is unchanged.
Reflection validates names, offsets, types, stage and allowed image/sampler set.
RHI requires explicit matching constants and primary/secondary textures; no white
material or constant fallback, cube/shadow bindings or mixed material VS ABI.
Existing ABI v1-v11 remains valid.

CPU HDR/settings coverage now includes bloom defaults, bounds, immutable/atomic
updates and toggles (35 rejects). DXC compiles all three actual bloom entries and
rejects seven mutated field/layout/binding contracts. RHI GPU negatives cover
missing/wrong constants, missing primary texture, attachment feedback and mixed
VS ABI (266 total rejected operations). Bloom scene GPU acceptance has 8865 RGB
channel checks against an independent double-precision 2D binomial/bilinear
oracle seeded from the real stencil silhouette and known radiance. Thresholds
0/1/4/65504, radii 1/4/8/16, strength/EV, colored halo, exact zero bypass,
debug isolation, sky/alpha/additive-before-bloom, immutable queued settings,
split/fractional/one-pixel viewports, eight resize/recook baselines and final
drain are covered. Six focused CPU/GPU/probe/DXC/RHI tests passed three repeats;
Python tooling 10/10. Captures `vulkan-stage7-bloom.bmp` and `.bmp.split.bmp` in
`build/windows-x64-clean/` were inspected. Evidence: `build/stage7-bloom/`.

`--view bloom` retains all HDR/GBuffer/material/lighting checks, plus independent
`bloom.bmp`/`bloom-bypass.bmp`: >64 exact RGB differences and exact restoration.
Synthetic probe settings are threshold .02, strength .25, radius 8 at EV0 to
exercise the effect under the existing dim directional controls. They are
reported explicitly, not claimed as original-scene or production-default values.
`--view hdr` continues to use bloom=false and aa=false; `--view bloom` uses aa=false.
Real source-derived barrel with original 512x512 TGA passed warmup +16 cycles:
`build/real-assets/barrel-radiation-stage7-bloom-gpu/report.json`, 48092 visible
pixels and 47376 exact-RGB independent bloom-bypass changes, exact restore,
GPU baseline/final `[7,15,14,3,0]`, capacity `[1,1,1,1]`, drained cook users and
retirements, unchanged inputs, clean shutdown and validation 0/0. Both captures
were inspected; all ten original source inputs retain their provenance SHA-256
and byte sizes before/after. This remains an adapted material pack with synthetic
lighting/postprocess controls, not original cooked-pack or DX11 image parity.
SHA-256 of normals/albedo/specular/hardware-depth/stencil BMPs is identical to
the earlier accepted HDR run; bloom-bypass BMP exactly matches its HDR BMP.
This bloom-only run accepted the bounded bloom portion; the subsequent AA and
combined acceptance is documented below. Historical bloom regression 2026-10-09:
GCC/MinGW-w64 Ninja Debug, 69/69 CTests (36 GPU),
10/10 Python, validation 0/0. Logs `regression.log`, `focused-repeat.log`,
`python.log`, `real-asset.log` and `verification-summary.json` are in
`build/stage7-bloom/`. No GCC Release, other-GPU, performance or startup acceptance
is implied by this bounded functional run.

### FXAA And Combined Postprocess (7.7)

`postprocess_settings::fxaa_enabled=false` is opt-in and is copied with exposure
and bloom into each queued draw. The global postprocess toggle preserves all
fields and bypasses exposure, bloom and FXAA together. Only final lit presentation
uses these passes; raw lighting/material/GBuffer diagnostics and unlit retain
their old outputs. HDR-only and bloom-only profiles keep FXAA disabled.

The active algorithm is the engine's 2011 NVIDIA FXAA preset 3, not FXAA 3.11,
SMAA, MSAA or TAA. The original `fxaa_common.h` was found under
`C:/Stalker2_2011/xr2-dsgn/resources/sources/shaders/sm_4_0/`; its fixed HLSL
preset was mechanically preprocessed, preserving the span/subpixel algorithm
and attribution while omitting unused API/preset branches. The active
`assets/fxaa_preset3.hlsli` clamps every sample to covered viewport pixel centers.
Preset 3 uses 16 one-pixel span-search steps, edge threshold 1/8, minimum 1/24,
subpixel trim 1/4 and cap 3/4. Its red/green luma approximation intentionally
ignores pure-blue-only contrast; there is no temporal history or shimmer guarantee.
The color-space contract follows the
[NVIDIA FXAA white paper](https://developer.download.nvidia.com/assets/gamedev/files/sdk/11/FXAA_WhitePaper.pdf).

After the complete linear scene and optional bloom, a pass applies exposure and
Reinhard, then IEC sRGB encoding into **RGBA8 UNORM**, not an sRGB texture view.
FXAA bilinearly samples this perceptually encoded LDR image at LOD0, decodes the
filtered result back to linear and writes an RGBA16F output. Presentation reads
that output without another tonemap; its sRGB attachment performs the single
final hardware encode. Sampling coordinates derive from `SV_Position` and output
reciprocals, including fractional/split viewports; a one-pixel region stays constant.

`fxaa_renderer` lazily owns two targets, two pixel shaders, a bilinear clamp sampler
and a 64-byte constants buffer using the existing strict v12 ABI/descriptor layout.
Both passes clear/store their target without depth/stencil/blending; RT transitions
to shader_resource before consumption, with no input/attachment feedback. Resize
allocates both replacements before fenced retirement of the old group. Toggle
and bypass retain warmed resources; exceptions close the active pass. The scene
and bloom targets are not modified by FXAA.

GPU acceptance covers three silhouette rotations at EV -2/0/2, primary colors
and a thin diagonal against a double-precision pixel-space CPU implementation.
For hard edges the capture-based oracle is strict (4/255 tolerance); combined
bloom gradients additionally use per-channel 3x3 convex bounds and independent
known-radiance bloom/exposure/Reinhard/sRGB checks. A hardware sRGB capture is not
a byte-exact readback of the shader's UNORM input: one-LSB input differences can
flip the discontinuous span-search decision on soft gradients. Such combined
reference outliers are reported, bounded below 0.1% of channels per viewport,
with mean absolute differential below 0.1/255 across that viewport; they are not
claimed as exact whole-frame FXAA parity. Uniform neighborhoods remain strict.
The fixture also checks independent/global bypass and exact restoration, raw
debug isolation, sky and alpha/additive composition before bloom/AA, queued
settings, split/fractional/one-pixel viewports, eight alternating resize/recook/
toggle baselines, removal and retirement drain.

`--view aa` extends the existing real-asset probe with bloom + HDR + FXAA and
independent `aa.bmp`/`aa-bypass.bmp` checks (>64 changed pixels at RGB tolerance 2,
then exact restoration). The report identifies the fixed AA algorithm and its
UNORM/sRGB contract. Original scene settings/lights are not inferred.
The source-derived barrel material pack passed warmup +16 cycles in
`build/real-assets/barrel-radiation-stage7-aa-gpu/report.json`: 48210 visible pixels,
3719 AA-bypass changes, 47175 exact-RGB bloom-bypass changes and 48208 global
bypass changes; exact restoration; baseline/final `[8,17,16,4,0]`, capacity
`[1,1,1,1]`, drained cook users/retirements, unchanged inputs, clean shutdown and
RTX 4070 validation 0/0. AA/bypass captures were inspected. Five raw buffer BMP
SHA-256 hashes are identical to the accepted bloom run; AA-bypass BMP exactly
matches its bloom BMP. All ten original input SHA-256 hashes and byte sizes were
verified unchanged before and after. This is an adapted geometry/material pack
with original TGA and synthetic controls, not an unmodified legacy cook or DX11
parity. Evidence and verification totals are recorded in `build/stage7-aa/`.
Full GCC/MinGW-w64 Ninja Debug regression: 71/71 CTests (38 GPU), 10/10 Python,
RTX 4070 validation 0/0. Eight focused settings/HDR/bloom/FXAA/probe/DXC/RHI
tests passed three repetitions each (`focused-repeat.log`). The final fixture
has 39892908 strict RGB checks (max 1/255), 40357548 convex bounds and 16976
softened pixels; combined quantized-reference outliers: 29 channels, max 11/255.
This closes 7.7 in the documented bounded profile;
later integrated full-scene M7 acceptance is documented in 7.8. No GCC Release, other-GPU,
performance, original-scene parity or game/editor startup acceptance is implied.

## Background Test Windows (2026-10-10)

Automated GPU fixtures, the asset probe and finite/capture/window-exercise smoke
runs use `tests/gpu_test_window.h`. Their windows start hidden, strip `WS_VISIBLE`,
and have no-activate, tool-window, layered and input-transparent styles. Layered
alpha stays zero even during the real minimize/restore checks; resizing uses
`SWP_NOACTIVATE`. The tests do not position, clip or capture the user's cursor.
Unbounded interactive `xray_vulkan_smoke` remains visible and interactive.

The helper opts the window into `XRAY_VULKAN_TEST_CAPTURE` before initialization.
Only opted-in backends request swapchain transfer-source usage and disable
swapchain clipping. `sources/vulkan_capture.cpp` copies the final image before
presentation into at most two host buffers, with image/host barriers, submission
fence waits and noncoherent-memory invalidation. Captures retain top-down BGRA
BI_RGB output, normalizing its unused alpha byte to zero. Normal rendering creates no capture buffers and
records no capture commands. Unsupported transfer-source surfaces fail explicitly;
there is no visible-window or desktop-capture fallback.

Pixel checks retain their existing tolerances and actual swapchain/presentation
path. Capture checks reject visible background windows, focus/mouse capture and
wrong output ownership. `xray_render_background_window` checks creation with a
requested visible style, show/minimize/restore/resize, transparent alpha and
rejection of capture before initialization. The MRT GPU regression checks the
two-buffer bound, resized byte count and complete release at shutdown.
This is test infrastructure, not an additional Stage 7 substage or M7 acceptance.

Verified with GCC/MinGW-w64 Ninja Debug on RTX 4070: 72/72 CTests (38 GPU),
10/10 Python tests, validation errors/warnings 0/0. Seven focused background,
MRT, standalone shadow-RHI, FXAA and Stage 1-3 smoke tests passed three repetitions
each. Logs are in `build/background-tests`; the initial failed standalone
shadow-RHI capture-owner check was fixed before the complete final regression.
Windows services/headless sessions, GCC Release and other GPUs were not tested.

The real source-derived barrel material-input pack passed 16 recook cycles with
unchanged input inventory, stable resource/capacity baselines, clean shutdown and
validation 0/0. All 26 BMP captures have exactly the same RGB pixels as the prior
Stage 7 AA run; whole-file hashes differ because desktop alpha was replaced with
the canonical unused zero byte. All ten original files' SHA-256 hashes and byte
sizes were unchanged before/after. Report:
`build/real-assets/barrel-radiation-background-gpu/report.json`; comparison and
verification summary: `build/background-tests`. This remains a source-derived
projection with synthetic controls, not production-cook or original-scene parity.

## MSAA and TAA (2026-10-10, Stage 7.7 Extension)

Both modes are opt-in in `postprocess_settings`; existing defaults remain unchanged:

```cpp
xray::render::postprocess_settings settings;
settings.msaa_samples = 4;       // 1 disables; 2, 4, 8 are capability-gated.
settings.taa_enabled = true;
settings.taa_history_weight = .9f;
scene.set_postprocess_settings(view, settings);
// Increment settings.taa_reset_serial before submitting an application camera cut.
```

MSAA can run alone, with TAA, or with FXAA. TAA and FXAA are exclusive; invalid
settings are rejected atomically. History weight must be finite in [0,.98].
Only final `lit_view_mode` with postprocess enabled uses the requested AA modes.
Global bypass and raw/debug modes render single-sample without temporal history.
The current sample-count/format query also requires `sampleRateShading`; an
unsupported request fails explicitly instead of silently substituting another mode.

Pipeline order:

```text
single-sample shadow atlas
multisample GBuffer + material inputs
per-sample deferred lights + combine/environment
multisample sky + depth-tested alpha/additive forward
FP16 color resolve (when samples > 1)
HDR TAA (when enabled)
HDR bloom -> exposure/Reinhard
LDR FXAA (when enabled, instead of TAA) -> hardware sRGB presentation
```

All attachments in an explicit pass have the same sample count. Sample count is
part of the pipeline key; Texture2DMS reflection and descriptor bindings reject
single/multisample mismatches. Deferred shaders fetch each covered sample rather
than averaging incompatible GBuffer normals/materials before lighting. Color
resolve uses `vkCmdResolveImage`, explicit transfer barriers and a distinct
single-sample FP16 target. Depth stays multisample; the shadow atlas stays 1x.
Changing modes or dimensions replaces targets with fenced retirement.

TAA uses an eight-frame Halton(2,3) pixel jitter, inverse current VP and previous
jittered VP to reproject world positions. A nearest history-depth test (.002 NDC)
rejects disocclusion; a 3x3 RGB neighborhood clamp bounds bilinear history color
before the configured blend in linear HDR. For MSAA, current depth is the minimum
sample and scene color is already resolved. A strict 192-byte constants ABI
keeps current/previous matrices, viewport centers and filter controls explicit.

History is ping-ponged RGBA16F color plus R32F depth, isolated by monotonically
assigned view/output identities and normalized viewport. Up to eight unique
active keys per frame are supported; replacing keys may temporarily retain the
previous frame's group until collection/fence retirement. Disabling TAA releases
history; resize, projection change, frame gap, minimize/restore, explicit reset
serial and large view-matrix changes reset accumulation. Model, visibility,
material, light, environment and texture/sky snapshot changes also reset it.
Queued draw snapshots retain their original settings and scene inputs.

This is a static-scene camera-reprojection implementation, not temporal upscaling
or animated-scene TAA. There is no object velocity buffer or reactive transparency
mask. Far-depth/background pixels, including sky and transparency drawn only over
sky, use current color without history; moving objects and changing lights reset
history rather than reprojecting object motion. MSAA still covers sky/forward
geometry edges. Rapid animation may therefore leave TAA effectively current-only.
No performance, other-GPU, GCC Release, production-asset TAA or DX11 parity claim
is made by this AA-extension run. Later integrated M7 and real-source AA
acceptance is documented in Stage 7.8 below.

`xray_render_stage7_msaa_gpu` checks all available 2x/4x/8x modes against an
independent linear `k/N` radiance -> Reinhard -> sRGB coverage oracle, including
uniform interiors, FXAA composition, exact bypass and stable resources/capacity.
`xray_render_stage7_taa_gpu` compiles the actual TAA shader and checks numeric
history blend, reset, depth rejection, neighborhood clamp, one-pixel reprojection
and outside/background rejection with single/multisample depth. Scene tests
distinguish accumulation from jitter-only and cover camera/projection changes,
material reset, MSAA+bloom, split/fractional/one-pixel/tiny viewports, replacement of eight keys,
minimize/restore, eight resize/drain cycles and shutdown. Existing HDR/shadow
fixtures additionally exercise AA with lighting, shadows, normal/roughness,
ambient/emission/fog, sky and alpha/additive composition. DXC tests cover all
sample-count permutations and malformed temporal ABI; RHI tests reject mixed
samples, invalid resolves and mismatched Texture2D/Texture2DMS bindings.
All automated GPU windows remain hidden and nonactivating.
The fractional/one-pixel checks validate every covered pixel of uniform HDR
radiance. Stored normalized pixel centers are rounded back to integer scissor
bounds, avoiding one-pixel gaps caused by `ceil` after a float roundtrip.

Final verification on 2026-10-11 after the scissor fix: GCC/MinGW-w64 Ninja Debug,
RTX 4070 (Vulkan 1.4.351), system loader/headers 1.4.363; 74/74 CTests (40 GPU),
10/10 Python tests, validation errors/warnings 0/0. Seven focused AA/HDR/RHI/
contract/DXC tests passed three repetitions each. Evidence, captures and summary
are in `build/stage7-msaa-taa`. An earlier `stage5_gpu` timeout was not reproduced
in either final full run; the latest run took 26.30 s with the original 45 s
limit. Its initial cause is unproven. No test timeout or resource bound was
relaxed to obtain acceptance.

## Integrated M7 Acceptance (Stage 7.8)

`xray_render_stage7_integrated_gpu` submits one complete static scene through the
production facade: mapped normal/roughness opaque receiver, off-camera cutout
caster, four directional cascades, six point faces, spot shadow map, scalar
specular, ambient/emission/fog, constant cubemap sky, non-intersecting unlit
alpha/additive surfaces and HDR postprocess. It does not combine screenshots of
separate feature tests. All supported 1x/2x/4x/8x modes run without post-AA, with FXAA
or with TAA, with bloom enabled; TAA and FXAA remain mutually exclusive.

The final-lit pass/resource contract, audited against `vulkan_scene_runtime.cpp`,
`vulkan_gbuffer_renderer.cpp` and the corresponding stage implementations:

1. Immutable scene/material/texture/light/view snapshots and shadow plans are
   prepared before recording. Sampled asset images are shader resources; frame
   and object constants are copied into per-frame uniform storage.
2. GBuffer clears/writes three matching-sample color attachments and D24S8
   depth/stencil (`render_target`, `depth_write`). Surviving opaque/cutout
   fragments write depth and the static stencil marker; forward surfaces do not.
3. Material inputs clear/write FP16 albedo/specular/emission MRT and LOAD the
   same depth/stencil with EQUAL tests and no depth/stencil writes. Colors become
   `shader_resource`, depth becomes `depth_read`.
4. Deferred lighting clears FP16 diffuse/specular accumulators. Before each
   shadowed light it ends the color pass, clears/writes the single-sample shadow
   atlas (`depth_write`) and transitions it to `depth_read`. Directional cascades,
   point faces and spot maps reuse the atlas sequentially, not simultaneously.
   Lighting resumes with accumulator LOAD and additive blending. GBuffer/depth
   and comparison-sampled shadows are inputs; MSAA lighting uses per-sample Load.
5. Combine samples albedo/specular plus both accumulators and writes a separate
   FP16 target, then transitions it to `shader_resource`.
6. Environment samples combine/emission/albedo/depth, adds ambient/emission and
   applies view-Z fog into the now-free diffuse accumulator. The combined input
   and output are distinct; the final HDR target becomes `shader_resource`.
7. Sky LOADs HDR color and depth/stencil, tests clear-depth/stencil without
   writing depth. Forward then LOADs the same attachments and depth-tests sorted
   alpha/additive draws without depth/stencil writes. Between these passes, color
   and depth transition explicitly between attachment and sampled states.
8. For MSAA, final HDR color transitions to transfer source; a distinct 1x FP16
   target becomes transfer destination. `vkCmdResolveImage` resolves color only,
   then returns both to `shader_resource`. Depth remains multisample.
9. TAA samples current HDR/depth and prior color/depth history, writes the other
   RGBA16F/R32F history pair and transitions it to `shader_resource`. First-frame
   histories are initialized; no image is both sampled and attached in this pass.
10. Bloom extracts bright HDR into FP16, performs horizontal/vertical binomial
    blur and composes a distinct FP16 result. Each output transitions from
    `render_target` to `shader_resource` before the next pass samples it.
11. Without FXAA, presentation applies exposure/Reinhard to HDR and hardware sRGB
    encodes once. With FXAA, an intermediate RGBA8 UNORM pass tone-maps/encodes,
    FXAA filters it into FP16 after decoding, and presentation only hardware
    sRGB encodes. TAA is before bloom; FXAA is after tone mapping.
12. Presentation writes the swapchain attachment and transitions it for present.
    Replaced textures, descriptors, framebuffers and passes retire behind fences.
    Empty/unlit frames release unused stage contexts; active shadow atlas reuse
    has a bounded in-flight retirement tail, which drains on removal/shutdown.

An independent analytic CPU oracle generates `vulkan-stage7-integrated.bmp.expected.ppm`
from fixed geometry, mapped normal/roughness, BRDF, ray/cutout visibility,
ambient/emission/fog, sky, forward blend and tone-map inputs. It does not seed
expected colors from a GPU capture. The no-AA/no-bloom reference is checked
densely outside declared .04-world-unit raster/PCF/alpha-filter boundary bands.
Bloom/AA/movement/resize profiles use a 17-pixel grid and .08 boundary bands,
with an independent 9x9 bloom convolution; RGB tolerance is 5/255. Bilinear repeat
of the eight-row cutout alpha, including its narrow wrapped upper-edge survivor
strip, is part of the ray oracle. Local shadow bias is explicitly .01 (.001 NDC),
not a claim about arbitrary zero-bias geometry. Separate stage tests retain their
stricter edge/PCF/AA numerical oracles.

Each AA profile warms for 64 frames and compares after another 32 at the same
eight-frame jitter phase, allowing at most two RGB levels of residual history
convergence with exact live-resource/capacity equality. Split views compare
against their individually rendered fixed-phase frames. Camera/light movement,
eight actual sky TGA reloads, model/material recooks, alternating output sizes,
live/retired/pass/capacity bounds and shutdown are checked in the same scene.

The external probe adds `--view msaa`, `--view taa` and `--view msaa-taa`. These
use 4x, 1x+TAA and 4x+TAA respectively with existing synthetic directional/bloom
controls. Unsupported 4x fails explicitly. They verify changed edge pixels,
64-frame warmup plus 32-frame same-phase convergence, exact reset reproduction
and exact single-sample bypass. Every one of the 16 model recook cycles also
renders the requested AA, checks its active resource/capacity baseline and
verifies an unchanged texture reload against an exact reset fixed-phase frame.

`tests/tools/run_m7_real_assets.ps1` runs all three modes on a source-derived pack
with `provenance.json`. It checks original input SHA-256/byte sizes against the
provenance, hashes the entire pack before/after, starts hidden probe processes
with a bounded wait, validates reports and writes a new summary directory.
Reports never overwrite input packs or existing acceptance directories.
Source-derived geometry/material projection plus synthetic controls is not an
original game scene, production cook or DX11 parity reference. Animated geometry,
lit transparency/refraction/OIT, temporal upscaling, startup, GCC Release and
other-GPU acceptance remain outside the bounded static M7 contract.

Repeat the integrated scene (with the validation/DXC environment configured as
in the GPU setup above), then run external packs into new output directories:

```powershell
ctest --test-dir build/windows-x64-clean -R '^xray_render_stage7_integrated_gpu$' --repeat until-fail:3 --output-on-failure
./sources/xray/render/vulkan/tests/tools/run_m7_real_assets.ps1 -Probe binaries/xray_vulkan_asset_probe.exe -Pack build/real-assets/barrel-radiation-stage7-material-inputs -Output build/real-assets/barrel-m7-new-run
./sources/xray/render/vulkan/tests/tools/run_m7_real_assets.ps1 -Probe binaries/xray_vulkan_asset_probe.exe -Pack build/real-assets/woodbox-mil-stage7-detail-inputs -Output build/real-assets/woodbox-m7-new-run -TimeoutSeconds 240
```

The external runner defaults to 75 seconds **per mode**, configurable up to 300;
the larger woodbox pack needs an explicit 240-second budget for all 16 CPU
recook/reload cycles. This does not change CTest fixture limits (75 seconds for
AA probes, 120 for the integrated scene), per-query/reload 30-second bounds,
pixel tolerances or resource bounds. This Debug run is not a performance target.

Real-source AA acceptance on 2026-10-11 passed all three modes on both packs,
each with 16 active-AA recook cycles, exact reset/bypass, fixed-phase convergence,
stable active live-resource/capacity counts, drained retirements/cook users,
clean shutdown and validation 0 errors/0 warnings. Changed RGB pixels versus
the 1x baseline (tolerance 2) were 1768/23620/24268 for barrel and
3400/17083/17086 for woodbox, in MSAA/TAA/MSAA+TAA order. These counts demonstrate
that AA executes, not a universal image-quality score. Visual inspection covered
the integrated scene, split view, barrel and woodbox captures.

SHA-256 and byte sizes of all 22 original files (10 barrel, 12 woodbox) matched
provenance before/after; all 17/19 pack files were unchanged. Reports:
`build/real-assets/barrel-radiation-m7-integrated-aa-final/summary.json` and
`build/real-assets/woodbox-mil-m7-integrated-aa-bounded/summary.json`.
Maximum query/reload times were 206/268 ms for barrel and 3281/3330 ms for
woodbox, measured in Debug, not a controlled performance comparison. The first
woodbox wrapper run timed out at 75 seconds; its partial output is retained in
`build/real-assets/woodbox-mil-m7-integrated-aa-final`. The bounded 240-second
repeat passed without changing fixture limits or acceptance checks.

Final 7.8 acceptance: GCC/MinGW-w64 Ninja Debug, RTX 4070, device Vulkan 1.4.351,
system loader/headers 1.4.363; **78/78 CTest (44 GPU)** in 327.34 seconds and
**10/10 Python tests**, validation 0 errors/0 warnings. The integrated scene
passed three further repeats (35.15/45.74/58.98 seconds), each with 12 complete
AA profiles and **661782 independent RGB channel checks, maximum error 1/255**.
Exact active stress counts were `[19,24,26,11,0]`, capacity `[1,1,1,1]`, pending
pass objects 36; retirements stayed bounded and drained to zero with pending
passes on removal. Validation is checked again after the renderer and resource
session destructors return. Hidden/nonactivating windows and GPU readback remain
in effect. Timings vary and are not performance acceptance.

The single 7.1-7.8 checklist is now complete for the stated static profile.
Normal game/editor startup, additional scene types, GCC Release and other GPUs
remain Stage 8/9 work, not hidden conditions for reopening M7. Evidence:
`build/stage7-integrated/summary.json`, full/repeat/build/Python logs, expected
PPM, integrated captures, and six real-source reports. Earlier feature-only
evidence above describes its dated scope and does not supersede this acceptance.

## Stage 8 Status And Dated Implementation Reports

The single active checklist, next task and M8 completion boundary live in the
[canonical integration plan](../../../../vulkan_integration_plan.md), Stage 8.
As of 2026-10-16, debug/UI, the bounded skeletal/animation/character profiles,
terrain CPU import/cooks, unlit diffuse, bounded deferred terrain GPU profiles,
terrain shadows, bounded terrain MSAA/TAA, renderer local AABB updates and
per-cell coherent texture reload and bounded async terrain texture binding
add/remove/exchange through facade/world and bounded mixed decoded-RGBA
normalization are accepted. Collision/game wrapper and ordinary threaded
game/editor startup remain open. Current inventory: 178 native + 551 normalizable
=729 eligible original cells, 69 explicit budget rejections and 16 missing-
dependency cells. This is metadata eligibility, not acceptance of 729 GPU scenes.
M8 as a whole remains open. The sections below are dated implementation reports;
their earlier test totals and capability limitations describe those snapshots,
not additional pending substages or the current status of later implementations.

## Stage 8: Debug Primitives (2026-10-12)

Stage 8 is **in progress**, not accepted as a whole. Its first implemented block
replaces the engine `draw_lines`/`draw_triangles` no-ops with indexed RHI draws
through the existing game facade debug renderer. At this debug-only acceptance,
UI vertices and font textures were still open; their implementation follows below.
Editor/screen-space debug APIs, other scene types, startup and multiple outputs
remain open in `vulkan_integration_plan.md`; this adds no new M7 substage.

The facade already copies caller arrays into deferred commands. Each `draw_scene`
CPU callback replays depth and overlay phases into immutable per-draw snapshots,
with the queued camera and viewport retained independently. Direct engine debug
submissions require the matching scene callback; wrong scenes, calls outside the
callback and reentrant `draw_scene` throw. Callback failure discards its partial
batches and restores capture state without consuming the queued-frame budget.

Positions are world-space finite XYZ; indices are uint16 line/triangle lists.
Both arrays may be empty, otherwise both must be populated, counts divisible by
2/3, indices in range. Each batch allows up to 65536 vertices and 196608 indices;
all queued views together allow **8 MiB converted vertex/index payload and 256
nonempty batches per frame**. Exceeding these bounds throws before GPU submission.
These are payload bounds, not a total-process memory or performance guarantee.

Packed `math::color` becomes normalized **linear RGBA**, straight-alpha blending.
Depth-enabled draws use LESS_EQUAL against scene depth; neither phase writes
depth/stencil. Triangles are unculled solid fills, lines are one-pixel line lists.
Unlit debug follows static geometry in the presentation pass. In final lit views
it follows transparent surfaces in the HDR LOAD pass, before MSAA resolve, TAA,
bloom, exposure/Reinhard and hardware sRGB. It is not affected by lighting/fog,
does not cast shadows or contribute GBuffer data. Diagnostic GBuffer views reject
nonempty debug submissions explicitly. This is not a post-tonemap UI overlay.

The shaders reuse the existing 128-byte FrameConstants ABI (VS binding 0;
descriptor-free PS). Geometry buffers are immutable and reused by exact vertex/
index equality while active; unused entries retire behind existing fences.
Removing all debug also releases its shader/constants/cache context. Debug
topology, depth mode, colors, positions and indices enter the TAA signature;
changes or removal conservatively reset the view's history. No motion-vector
support or animated-debug temporal-quality claim is implied.

`xray_render_stage8_debug` checks copied snapshots, color channels, full uint16
range and ten malformed-batch rejections without creating a GPU. The hidden
`xray_render_stage8_debug_gpu` exercises normal facade commands, unlit/1x-lit
occlusion, overlays, no depth writes, independent linear-alpha/tonemap pixel
oracles, caller mutation, transient lifetime, line rasterization and split
viewports. It also checks seven explicit capability/budget failures, recovery,
the accepted 256-batch boundary and a budget shared across queued views.
The active 4x MSAA+TAA+bloom profile warms for 64 frames, checks same-phase RGB
drift after 32 more (at most two levels), performs eight resize cycles with exact
live/capacity equality and bounded retirements, verifies changed/absent debug
history reset, and drains resources with validation checked again after teardown.
4x support is required for this fixture; it does not silently fall back.

With the validation/DXC environment from the GPU setup above:

```powershell
ctest --test-dir build/windows-x64-clean -R 'stage8_debug' --output-on-failure -V
```

Acceptance of this debug block: GCC 16.2/MinGW-w64 Ninja Debug, RTX 4070,
device API 1.4.351 and system loader/headers 1.4.363; **80/80 CTest (45 GPU)**
in 507.68 seconds and **10/10 Python tests**. Both new tests passed three further
repeats; GPU times were 14.58/14.18/7.57 seconds, validation 0 errors/0 warnings
including renderer teardown. Timings are not a performance comparison. The
existing hidden/nonactivating windows and fenced GPU readback stay in use.
The control capture was visually inspected; logs, capture SHA-256, tested scope
and explicit remaining work are recorded in `build/stage8-debug/summary.json`.
This debug-only acceptance left UI/fonts for the next block below. Stage 8 stays
open; GCC Release and other GPUs were not tested here.

## Stage 8: UI And Font Overlay (2026-10-12)

The next Stage 8 block replaces UI vertex/text no-ops through the existing
game facade, without introducing another numbered substage. All scene passes
finish before UI is composited in the presentation pass. UI therefore follows
MSAA resolve, TAA, bloom, exposure, tonemap and FXAA; it is not lit, fogged or
included in temporal history. It never tests or writes scene depth/stencil.
The earlier debug block remains world-space and retains its different ordering.

`ui::renderer::draw_vertices` keeps its legacy numeric contract: primitive 0 is
quads in bottom-left/top-left/bottom-right/top-right order; point 0 is an alpha
font mask, point 1 a textured image, point 2 a solid fill. Primitive 1/point 0 is
a one-pixel line strip expanded to indexed line pairs. Other modes throw.
Positions use viewport-local pixel XY; finite Z/W are accepted but ignored.
Packed vertex RGB is **display sRGB**, decoded to linear before straight-alpha
blending; alpha is linear coverage. This differs intentionally from debug's
linear packed RGB. Image texture sRGB/linear formats retain their existing
loader semantics. Fonts use a linear texture view and sample **alpha**, not red.

`ui::draw_options` adds an optional texture override and optional viewport-local
pixel scissor. Scissor is intersected with the viewport/output using pixel-center
coverage and half-open bounds; an empty intersection draws nothing. Font draws
use the override or the view's explicitly assigned default atlas; image draws
require an override. Fill/line modes reject a texture. Resources must be from
the same renderer session and be single-sample 2D textures, not cubemaps.
There is no implicit asset lookup or process-global font binding.

```cpp
auto& ui = world.game_renderer().ui();
ui.set_font_texture(view, atlas); // Queued on the same channel as the draw.
xray::render::ui::draw_options options;
options.scissor_enabled = true;
options.scissor = {xray::math::float2(0, 0), xray::math::float2(320, 200)};
pcstr text = "Vulkan UI";
ui.draw_text(view, text, font, position, color, selection_color,
             320, true, 0, 0, options);
```

The facade copies vertices/options and retains resource references until its
deferred command runs. Each scene overlay callback then snapshots immutable
texture data, so a reload between queued views does not change the first draw.
Engine submissions require that matching view's overlay callback, not its depth
phase. Geometry caches retain only vertices/indices, not obsolete atlas data;
unused geometry/textures retire through existing fences. No-UI frames release
the context. Each nonempty batch is limited to 65536 vertices; all queued views
share **8 MiB converted vertex/index payload and 256 nonempty batches per frame**,
separate from debug's budget. This is not a bound on texture/process memory.

Text layout uses the existing byte-font metrics, up to 16384 bytes. It emits
only initialized glyph quads, handles CRLF as one break, tabs as four spaces,
word wrap and long-word hard wrap, and applies selection foreground color to
half-open original byte indices. A glyph wider than the wrap width remains
intact; use scissor for strict clipping. Empty or CR/LF/tab-only text emits no
draws and needs no atlas. This is not Unicode shaping, kerning or a new font rasterizer.
The old unscoped engine text entry now throws explicitly; use the scene-view
facade. No normal game/editor startup or legacy R8/red-mask font parity is claimed.

CPU `xray_render_stage8_ui` checks snapshots, uint16 boundaries, sRGB conversion,
quad/line indices, scissor and text layout, including 17 malformed inputs.
Hidden GPU `xray_render_stage8_ui_gpu` checks independent blend/font/image pixel
oracles, copied transient commands, split views, eight capability/budget
rejections, active 4x MSAA+TAA+bloom/exposure isolation, exact same-phase captures,
eight resize cycles, reload snapshot separation, stable resource/capacity bounds
and drained retirements. Validation is checked again after renderer teardown.

```powershell
ctest --test-dir build/windows-x64-clean -R 'stage8_ui' --output-on-failure -V
# Optional real atlas; no machine-specific asset dependency in CTest:
./binaries/xray_render_stage6_gbuffer_tests.exe `
  C:/Backup/Source/xray-2-2011-main/build/stage8-ui/ui-real.bmp --ui-real `
  C:/Stalker2_2011/xr2-dsgn/resources/sources/textures/ui/ui_font_arial_21_1024.tga
```

The real asset is a 256x256 RGBA TGA despite its filename. The original
`xray::ui::ui_font` Arial 21 metrics drive the facade; an independent per-pixel
oracle checks 3213 glyph/background pixels against decoded atlas alpha. The
rendered label was visually inspected. Original TGA and options SHA-256 are
preserved in `build/stage8-ui/summary.json`. That UI acceptance left skeletal models
and `update_skeleton()` for the next block below, followed by the remaining scene/startup/editor
work in the plan. GCC Release, other GPUs and allocation/device-loss injection
are not accepted by this block.

UI-block acceptance: GCC 16.2/MinGW-w64 Ninja Debug, RTX 4070, system loader/
headers 1.4.363 and device 1.4.351; **82/82 CTest (46 GPU)** in 345.43 seconds,
**10/10 Python tests**. All four UI/debug CPU/GPU tests passed three additional
repeats (UI GPU 5.00/4.98/4.96 seconds), validation 0 errors/0 warnings including
teardown. The final build also repeated the real-font check successfully, with
unchanged original TGA/options hashes. Timings are not a benchmark. Full/repeat
CTest logs, final build/Python/real-font logs, visually inspected captures and
provenance are preserved in `build/stage8-ui/summary.json`; earlier debug evidence
in `build/stage8-debug` remains separate.

## Stage 8: Skeletal Renderer Baseline (2026-10-12)

The renderer now owns the existing `skeleton_render_model_class` and
`skeleton_render_model_instance_class` query IDs, alongside static model cooks.
It reuses the checked model-root/material import and common surface-instance
representation, rather than adding another draw pipeline. Static queries still
reject skeletal roots. Resource/cook users participate in normal session-close
and delayed cook unregistration, including live skeletal assets.

Queries require a `/render` directory: root `export_properties` type 40, sorted
surface directories with type 47, `converted_model`, and root `bind_pose`.
The latter is the original u16 count followed by NUL-terminated bone name and
translation/rotation/scale float3 values per bone. Names must be nonempty, unique
and at most 127 bytes; count is **1..80**. Existing X-Ray scale * rotation *
translation helpers reproduce the legacy bind matrix; its checked inverse is
retained. Bind scales must be positive/invertible. Locator bone indices must fit
the bind pose (65535 retains its legacy unbound meaning).

The parser accepts original 76-byte `vert_boned_4w` or compact converted 48-byte
`hardware_4weights_skinning_vertex` records, in sequential vertices/indices
chunks 3/4. No declaration guessing, unknown chunks or trailing bytes are
accepted. Vertex counts are 1..65536 and triangle indices are uint16. Raw absent
secondary bone IDs (65535) fall back to the first bone, as in the existing CPU
path; compact IDs must already be valid. Three stored weights define the fourth
as one minus their sum. Negative/nonfinite weights or meaningful excess over
one fail; only rounding excess up to 1e-5 is normalized. The root retains the
existing 128-surface/64-MiB aggregate disk-import bounds, and expanded per-surface
CPU geometry is bounded at 64 MiB. Positions/UVs/matrix entries are finite and
bounded by absolute 1e6. Malformed input reports resource-query failure, not an
assertion-based chunk-reader overrun.

`scene_renderer::update_skeleton(model, matrices, count)` copies **absolute
model-space bone matrices**, in X-Ray row-vector convention, before returning.
The facade validates the 1..80 count before reading/copying arrays or allocating
a command; the command also checks it before touching its fixed 80-element
buffer. The engine checks active-session ownership, skeletal type and exact
bind-pose count. Palette matrices are `inverse_bind * pose`, without an extra
transpose. This first profile requires a rigid, positive-determinant final
palette, with a .002 orthonormality tolerance; scale/shear/reflection fail
explicitly. Bind SRT itself may contain positive scale when the submitted pose
cancels it into an admissible palette.

Deformation ports the existing **CPU four-weight linear-blend skinning** path;
it is not GPU skinning. Position, normal, tangent and binormal all use their
respective bone transforms. The old CPU implementation's tangent/binormal use
of the first influence's matrix for secondary influences is not repeated. Normals/basis vectors
are checked, normalized and packed to the existing D3DCOLOR/static 32-byte vertex
layout; UVs are retained. Degenerate blended bases fail explicitly. These meshes
then use existing Vulkan unlit/GBuffer/material/shadow/forward passes. This is a
correctness-first CPU baseline, not an allocation-per-frame or performance goal.

All surface meshes and exact posed bounds are computed before publishing any
state. Failure leaves the previous pose and bounds intact; instances remain
independent. Existing queued draws retain immutable mesh snapshots, so updating
a later queued view cannot mutate an earlier draw or its shadow casters.
Identical output geometry retains its previous mesh identity, avoiding buffer
churn/history reset for an unchanged pose. Changed identity is already included
in the TAA signature; no animated motion-vector quality claim is implied.
Existing weak geometry caches and fenced buffer retirement drain obsolete poses.
Raw vertex/index fragment updates are explicitly unsupported for skeletal models.

```powershell
ctest --test-dir build/windows-x64-clean -R 'stage8_skeletal' --output-on-failure -V
```

The CPU fixture checks both disk layouts, truncation bounds, inverse bind pose,
four-weight transforms, N/T/B rotation, instance isolation, unchanged snapshots,
single/multi-surface rollback and asset cook lifetime. The hidden/nonactivating
GPU fixture exercises normal facade updates and caller mutation, queued split
poses, posed frustum bounds and independently transformed static control meshes
in six modes: unlit, normal, tangent, hardware depth, material albedo and final
lit with directional shadows. It also checks active 4x MSAA/TAA/bloom stability,
128 changing-pose frames across eight resize cycles, exact resource/capacity
baselines, bounded retirements and final drain. The changed-pose reset oracle
uses explicit current-only history at the same first jitter, not TAA-off geometry.
Validation is checked again after renderer/resource teardown.

This initial block was synthetic renderer acceptance only. At that point game-level
`skeleton_model_instance_class`, animation skeleton/mixer integration, real
skeletal asset-pack acceptance, 1/2/3-weight or other disk layouts, GPU skinning,
nonrigid palette support, animated motion vectors and production performance
were not accepted. No `bind_pose` file was found by the exact-name inventory under
`C:/Stalker2_2011/xr2-dsgn/resources`; that limited inventory is not evidence that
no usable skeletal assets exist elsewhere or in another form. Stage 8 remains
in progress; evidence for this block is separate in `build/stage8-skeletal`.

Skeletal renderer-block verification: GCC 16.2/MinGW-w64 Ninja Debug, RTX 4070,
system loader/headers 1.4.363 and device 1.4.351; **84/84 CTest (47 GPU)** in
443.88 seconds and **10/10 Python tests**. All six Stage 8 CPU/GPU tests passed
three further repeats (skeletal GPU 8.26/7.56/9.58 seconds), validation 0 errors/
0 warnings including teardown. The CPU fixture has 447 malformed/capability
rejections, specific mixed-bone tangent/binormal regression checks, and accepts
80 bones/index 79. The last CPU-only test strengthening followed its full-run
slot; the final fixture then passed directly and in all three repeats.

The GPU fixture performs **8779428 independent RGB channel comparisons** across
six modes, each within two levels, six explicit capability failures, exact
same-phase/history-reset checks and the animated resize/resource stress above.
The control capture was visually inspected and its SHA-256 matches the full
run and repeats. Timings are not a performance target. Build/full/focused/repeat/
Python logs, the inspected capture, scope and remaining work are recorded in
`build/stage8-skeletal/summary.json`; prior UI/debug evidence is unchanged.

## Stage 8 Game Skeleton Wrapper

The Vulkan cook group now owns `skeleton_model_instance_class`. A logical game
model name expands to `resources/models/<name>.skinned_model`, matching the
existing game API. A checked cooked `skeleton` descriptor identifies the
animation skeleton; one aggregate child query loads `skeleton_class` and the
Vulkan render instance. The resulting public `render::skeleton_model_instance`
contains the real animation skeleton and render instance, without a DX class
downcast. Initial geometry remains in bind pose.

Bind-pose names are retained in disk order. The wrapper requires one movement
root followed by exactly the render bones in the same name/order. Game pose
arrays omit that movement root. Mismatches fail rather than silently assigning
transforms to the wrong bones. Remapping, missing bones and extra animation
bones are not supported in this bounded profile.

Animation owns its cook: register it before issuing wrapper queries and keep
it alive until dependent queries and resources drain. The renderer never
registers/replaces/unregisters `skeleton_class`. Missing cook, creation bytes,
user data, invalid descriptor/config/path, dependency failures and closed
sessions report explicit query failure. Paths are bounded including the
animation cook's `.skeleton` suffix and converted-tree expansion. A single
aggregate callback handles partial failures; live wrappers retain both
dependencies and prevent premature renderer cook teardown.

The CPU test uses the real animation skeleton and binary-config cooks with a
prepared synthetic hierarchy, checking root omission, bone order/count,
skeleton reuse, render-instance isolation, deformation/bounds, negative queries
and lifetime. The existing hidden/nonactivating skeletal GPU test now gets its
primary model through this wrapper; six independent analytic frame comparisons,
4x MSAA/TAA/bloom, queued poses and resize/resource stress remain enabled.

```powershell
ctest --test-dir build/windows-x64-clean -R 'stage8_(skeletal|skeleton_wrapper)' --output-on-failure -V
```

Read-only archive inspection found six real exported `.skinned_model` files
under `C:/Stalker2_2011/xr2-dsgn/resources/exported/models`: animaxis (3 bones),
ak_74 (5), and four humanoids (62 each). All contain `render/bind_pose` and
76-byte raw vertices. Their bone names match the corresponding exported
animation hierarchies in the skeleton cook's CRC-sorted allocation order,
excluding the movement root. This supersedes the limited loose-file inventory
above, but is **not cooked-pack or real-model GPU acceptance**.

Evidence is separate in `build/stage8-skeleton-wrapper`, including source hashes
and bone-order audit. At this wrapper block real skeletal pack acceptance and
animation player/mixer remained next; the later real-source acceptance is below.
Malformed animation hierarchy hardening, ordinary
game/editor startup, other scene types and the rest of M8 remain open.

All seven selected tests passed three repetitions (21 executions), including
the wrapper CPU/GPU path and the three regressions that failed the initial
full run. Validation remained 0 errors/0 warnings. That initial run was 82/85:
M7 integrated and Stage 5 GPU exceeded their unchanged 120/45-second limits;
one MSAA+TAA probe failed exact unchanged-reload capture equality. Neither
timeouts nor pixel tolerances were relaxed.

The probe now counts actual submitted GPU frames for fixed-phase comparisons,
with bounded retries when a CPU tick cannot acquire a swapchain image. A report
counter records skipped ticks; all three probe repeats recorded zero skips, so
the original mismatch's cause is **not proven**. Passing repeats are not a claim
that intermittent background timing/TAA stability is fully resolved. Initial
logs/report are retained, and this risk remains part of Stage 9 stabilization.

On 2026-10-12 the final GCC/MinGW-w64 Ninja Debug build passed. The second full run was
**84/85 CTest (46/47 GPU)** in 526.14 seconds: all Stage 8 tests, M7 integrated
and the MSAA+TAA probe passed, but Stage 5 GPU again exceeded its unchanged
45-second timeout. Its three separate repeats passed in 36.14/34.80/32.70
seconds; the recurring full-suite timeout remained open at that point. **The full regression
was not green**, and the timed-out case had no completed teardown validation
assertion. Python tests passed 10/10.

Skeletal GPU repeats passed in 9.75/8.98/13.10 seconds with validation 0/0
including teardown. The inspected control capture matches the previous
renderer-block SHA-256, and exported archive/skeleton input hashes were checked
again unchanged. Timings are not benchmarks. Final and initial results,
captures, logs and residual risks are recorded in
`build/stage8-skeleton-wrapper/summary.json`; M8 remains open.

## Background Test Pacing (2026-10-13)

An instrumented Stage 5 GPU baseline completed 2013 frames in 28.562 seconds;
16.805 seconds were spent in the unconditional per-frame `Sleep(8)` alone.
The Stage 5, GBuffer/Stage 7/8, and asset-probe facade loops now use a shared
nonblocking Windows message pump without that artificial delay. Existing GPU
frame fences and fenced readback still provide completion guarantees. No
production renderer behavior, stress counts, assertions, pixel tolerances or
CTest timeouts changed. Resource-query polling retains its bounded waits.

The background-window test additionally verifies queued-message dispatch while
retaining hidden/no-activate/input-transparent behavior. Eight focused tests
passed three repetitions each (24 executions, 279.94 seconds), validation 0/0.
Stage 5 GPU passed in 23.22/23.86/20.73 seconds with its existing 45-second limit.
All 32 Stage 5 control captures exactly match the instrumented baseline by
SHA-256. The full GCC/MinGW-w64 Ninja Debug build and 10/10 Python tests passed.
Two consecutive complete runs passed **85/85 CTest (47 GPU) each** in
386.29/470.13 seconds, validation 0/0 including teardown. Stage 5 GPU completed
in 16.47/26.81 seconds with the same 45-second limit. All 32 control captures
were checked again after the second full run and still match the baseline.
The current GCC Debug / RTX 4070 regression is green; the recurring timeout
did not reproduce in either complete run. Results and logs are recorded in
`build/stage8-test-stability/summary.json`.

Evidence is separate in `build/stage8-test-stability`; earlier failed-run logs
remain unchanged. These are test-harness timings, not engine benchmarks or
full M8 acceptance. The original one-off MSAA+TAA mismatch's cause is still
not retrospectively proven; the exact pixel oracle remains enabled for further
Stage 9 configurations.

## Stage 8 Real Skeletal Pack (2026-10-13)

The existing offline `tests/tools/prepare_real_static_pack.py` also accepts
`.skinned_model` exports (root 40/surface 47), preserving original 76-byte
four-weight vertices, indices and bind-pose bytes. It prepares checked skeleton
descriptors and binary animation hierarchies, requiring exact bone order using
the real cook's CRC-sorted sibling-reservation traversal. Original inputs are
read-only and SHA-256 inventoried/rechecked; output must be new and disjoint.
This remains an offline source-derived projection, not the production cooker.

The real `neutral_01` character has 62 non-root bones, 3 textured surfaces and
5134 vertices. Its exported [-1,1] bounds omit geometry reaching Y=1.844;
preparation fails unless `--recompute-bounds` is explicit. That option only
rebuilds output box/sphere metadata, recording original and output bounds in
provenance. Geometry, bind pose, original TGA pixels and texture options are
unchanged. At this earlier baseline the head material's source `max_roughness=2.8`
exceeded the then-current [0,1] contract, so that run used `--material-mode unlit`
diffuse/alpha/culling/tiling projection. No silent clamping or original
character-material/PBR parity is claimed.

```powershell
python sources/xray/render/vulkan/tests/tools/prepare_real_static_pack.py `
  --resources C:/Stalker2_2011/xr2-dsgn/resources `
  --model character/human/neutral/neutral_01/neutral_01.skinned_model `
  --output build/stage8-real-skeletal/neutral-unlit-pack `
  --lua C:/msys64/mingw64/bin/lua.exe --texture-mode tga `
  --material-mode unlit --recompute-bounds
# Use the validation/DXC test environment documented above; reports must be new.
./binaries/xray_vulkan_asset_probe.exe `
  --assets build/stage8-real-skeletal/neutral-unlit-pack `
  --model resources/models/character/human/neutral/neutral_01/neutral_01.skinned_model/render `
  --kind skeletal --view msaa-taa --output build/stage8-real-skeletal/neutral-first
```

Skeletal kind always checks six view modes (unlit, normal, tangent, depth,
albedo and lit); `--view msaa|taa|msaa-taa` additionally selects its AA stress
profile. It registers the real animation skeleton cook for the query lifetime
and obtains independent render instances through the game wrapper. Synthetic
absolute poses check copied commands/immutable queued geometry, all CPU rigid
positions (1e-4 model-space tolerance) and six GPU rigid controls. The independent
GPU reference transforms an untouched bind instance as a whole, without skinning;
all RGB channels must match within 2/255. Single-bone translation and exact
restoration separately verify deformation.

AA checks include stable unchanged geometry, 128 changing-pose frames across
8 nonactivating resizes, bounded live/capacity/retirement counts and exact
same-first-jitter TAA history reset. Sixteen recooks must drain cook users and
GPU retirements and reproduce the warmed resource baseline. Input inventory
and validation checks include resource/renderer teardown. CTest now includes
`xray_render_stage8_skeletal_asset_probe` with a synthetic prepared pack; its
fixture is not substituted for external real-asset acceptance.

The real captures were visually reviewed for recognizable, intact geometry and
diffuse textures. Lighting uses two synthetic unshadowed directional controls;
this is not original scene lighting or real-character shadow acceptance.
Without `--clip`, `animation_clip_acceptance=false` is intentional: valid skeleton
loading and synthetic absolute poses do not validate animation clips or the
player/mixer. The later optional clip profile below adds bounded playback/mixing;
original character materials, GPU skinning, motion vectors, general animated-
scene temporal quality and normal game/editor startup remain open. M8 is not
complete. Evidence for this earlier baseline is in `build/stage8-real-skeletal`.

The final full GCC 16.2/MinGW-w64 Ninja Debug build passed: **86/86 CTest
(48 GPU)** in 322.21 seconds and **13/13 Python tests**. Five focused
skeletal/options tests passed three repetitions (15 executions, 36.70 seconds).
Three real-character probes passed, including two on the final build with
`Head` single-bone control (2317 changed pixels); all 16 captures from those
final two runs are SHA-256 identical. Each real probe checks 8779428 independent
RGB channels and 15402 CPU position coordinates (maximum error 1.78814e-7 at
1e-4 tolerance), with validation 0 errors/0 warnings through teardown.

An additional real surface-projection pack containing nine original textures
failed under the then-current contract with `Invalid material surface parameter: max_roughness`;
shutdown and validation checks still pass and its input inventory is unchanged.
Preparing the real model without explicit bounds projection also correctly
fails. These expected negative controls do not weaken positive acceptance.
All 25 source files used across both profiles were rechecked unchanged by
SHA-256. An initial full invocation omitted validation/DXC environment and is
retained separately as `full-missing-environment.log`; the corrected complete
run is green. Existing timeouts and pixel tolerances were not relaxed.
Results, limits, logs and hashes: `build/stage8-real-skeletal/summary.json`.
All GPU windows remain hidden/nonactivating with no cursor capture. These
Debug harness durations are not engine benchmarks.

## Stage 8 Animation Player And Mixer (2026-10-13)

The offline preparer accepts repeated `--animation single/<logical-name>`
arguments for skeletal packs. Each original exported `.clip` is copied unchanged
and compiled to the binary-config ABI with explicit float-typed spline numbers.
It validates exact skeleton name coverage, movement root at index zero, 2..81
bones, nine SRT channels, finite/bounded controls, ordered zero-origin knots,
common duration <=60 seconds and rigid scale. The unused historical last control
sentinel is retained; poses/channels are not generated or silently filled.
`.clip_data` and event callbacks are not imported/accepted by this profile.

```powershell
python sources/xray/render/vulkan/tests/tools/prepare_real_static_pack.py `
  --resources C:/Stalker2_2011/xr2-dsgn/resources `
  --model character/human/neutral/neutral_01/neutral_01.skinned_model `
  --output build/stage8-animation-player/neutral-clips-pack `
  --lua C:/msys64/mingw64/bin/lua.exe --texture-mode tga `
  --material-mode unlit --recompute-bounds `
  --animation single/human/camp/sit_high_guitar_idle `
  --animation single/human/camp/sit_high_guitar_play_1
# Use the validation/DXC test environment above. Output directories must be new.
./binaries/xray_vulkan_asset_probe.exe `
  --assets build/stage8-animation-player/neutral-clips-pack `
  --model resources/models/character/human/neutral/neutral_01/neutral_01.skinned_model/render `
  --kind skeletal --view msaa-taa --output build/stage8-animation-player/neutral-first `
  --clip resources/animations/single/human/camp/sit_high_guitar_idle `
  --blend-clip resources/animations/single/human/camp/sit_high_guitar_play_1
```

Both real clips cover all 63 skeleton names, including movement root and `item`.
Some walk clips omit `item` and are rejected rather than receiving fabricated
channels. Scoped real B-spline/cubic cooks feed the existing `animation_player`;
its hierarchical non-root poses go through `update_skeleton`, while its object
movement goes separately through `update_model`. Sixty-four single-clip frames
are compared against independent direct curve sampling plus parent traversal
(63488 matrix coordinates, 1e-4 tolerance). Exact reset/replay, visible animation,
fixed two-clip weights 0.35/0.65, 128 changing MSAA+TAA frames, eight resizes,
same-jitter history reset and resource/drain bounds are checked. Clips are
queried again in every one of the 16 lifecycle cycles.

The new CPU `xray_render_stage8_animation_player` test uses real cooks on a
synthetic spline fixture with independent analytic hierarchy, root-motion/loop,
weighted blend and timed-transition controls, plus exact replay, reuse/missing
queries, binding rejection, CRC lookup collisions/end and cook-user drain.
`xray_render_stage8_animation_asset_probe` adds this path to GPU CTest without
substituting synthetic fixtures for the external real-asset run.

Runtime fixes exposed by these tests: player starts with identity object
transform; bone lookup checks `lower_bound(end)` and complete names across CRC
collisions; Debug runtime cooks no longer require Lua-backed editor event data.
Editable event data is created on demand by the existing editor-thread accessor,
whose multithread/editor-startup acceptance remains separate. Public event
headers no longer include the module's private PCH. The spline cook has a scoped
world-free constructor while retaining the old world overload, and handles cubic
creation failure by freeing intermediate data and reporting query failure.

`clip-last.bmp` is the original hierarchical camp pose, not an artificial Head
translation. Only the character is loaded, without a chair or guitar prop; this
is not original scene parity. Full gameplay/controller graphs, additive clips,
event callbacks, malformed runtime-clip hardening, ordinary game/resource threads,
original character materials, motion vectors and general animated TAA quality
remain open. M8 is not complete. Evidence: `build/stage8-animation-player`.

Final verification: full GCC 16.2/MinGW-w64 Ninja Debug build passed,
**88/88 CTest (49 GPU)** in 231.17 seconds and **14/14 Python tests**.
Seven focused animation/skeletal/options tests passed three repetitions
(21 executions, 39.69 seconds). Three real-source runs passed 16 recook cycles
each; all 20 captures are SHA-256 identical across runs. Each single-clip run
changes 14449 pixels and reports maximum matrix error zero over 63488
coordinates at the new oracle's 1e-4 tolerance. Exact replay/history reset,
validation 0 errors/0 warnings through teardown, cook-user and retirement
drains pass. All 15 original input files, including both clips, were rechecked
unchanged by SHA-256. Existing CTest timeouts and pixel tolerances were not
relaxed. Initial failed CPU logs/backtraces are preserved for the eager
editor-Lua allocation and uninitialized object-transform fixes; the regression
controls pass the full suite and repeats. GPU windows stay hidden/nonactivating
without cursor capture. Timings are Debug harness durations, not benchmarks.
Results, limits and hashes: `build/stage8-animation-player/summary.json`.

## Stage 8 Character Surface Materials (2026-10-13)

The original `max_roughness=2.8` is a valid remap coefficient, not evidence of a
broken character asset. `psf_tspecular_power.h` computes
`min + (max-min)*max(sample.g,.01)`, and `geometry_pre_pass.ps` writes it to
the RGBA16 UNORM target selected by `renderer_context_targets.cpp`. Vulkan
already uses that target format. Parser and typed material resources now retain
finite ordered remap coefficients in a bounded [0,16] profile, without clamping
their values. The mapped result saturates when stored as UNORM. This is a project
bound, not a claim that the source material UI had a limit of sixteen.
Normalized scalar roughness/Fresnel, byte-unit scalar conversion and forward
unlit restrictions are unchanged; no shader ABI/format change was necessary.

CPU tests accept exact 2.8, minima above one and endpoint sixteen while rejecting
out-of-bound, reversed, mistyped and nonfinite coefficients. GPU checks use
green64 with `.1 + 2.7*(64/255)`, green255 with output saturation to one, and
minimum1.5 with saturation to one. They distinguish result saturation from
incorrect coefficient clamping; existing pixel tolerances/timeouts are retained.
The old [0,1] remap schema restriction was explicitly replaced, not treated as
an original-asset error or silently bypassed.

A separate mapped skeletal fixture and CTest
`xray_render_stage8_mapped_animation_probe` exercise diffuse/normal/roughness/
specular maps with clips/blend, MSAA/TAA and recook/resize/drain checks. For mapped
models the probe adds roughness, Fresnel and material-specular rigid controls to
its six original views and enables specular influence in the opposed directional
control lights. Diffuse-only/unmapped probes retain the earlier six-view profile.

The real neutral_01 pack uses `--material-mode surface` plus the two original
camp clips, with nine original TGA textures: three diffuse, three normal and
three specular. All three source materials disable `use_tspecular_power`, so
real roughness remains scalar and no roughness map is artificially enabled.
Original remap metadata, including head max2.8, is retained. Supported normal
packing/linear sampling and specular color/intensity run through the existing
material cooks. The real mapped character is lit with diffuse and specular
controls, rather than accepted only as a diffuse preview.

This is still a source-derived supported surface projection, not full original
actor/PBR/image parity, extra material stages, scene lighting or props. Character
shadow acceptance, motion vectors, general animated TAA quality, full animation
workflows and normal game/editor startup remain separate. This report accepts
only the character profile; terrain reports follow below and the active work
order is in the canonical plan. Evidence: `build/stage8-character-materials`.

Final verification: full GCC 16.2/MinGW-w64 Ninja Debug build passed,
**89/89 CTest (50 GPU)** in 299.47 seconds and **14/14 Python tests**.
Five focused material/mapped-animation/options tests passed three repetitions
(15 executions, 39.37 seconds). Three real-source runs passed 16 recook cycles
each; all 26 captures match across runs by SHA-256. Each run checks 13169142
independent RGB channels over nine views at the unchanged 2/255 tolerance;
clip hierarchy/replay, MSAA/TAA history reset, resize and resource/capacity/
retirement bounds pass with validation 0 errors/0 warnings through teardown.
All 27 original inputs, including nine TGA textures and two clips, were rechecked
unchanged by SHA-256. The three cooked material files are byte-identical to the
earlier negative pack, confirming a runtime fix without parameter substitution.
Lit/animated control frames were visually reviewed. GPU windows stay hidden/
nonactivating without cursor capture. These Debug durations are not benchmarks.
Reports, hashes, source-semantic evidence and limits:
`build/stage8-character-materials/summary.json`. This bounded block is complete;
M8 as a whole remains open; this is the dated character-block acceptance.

### Terrain CPU Foundation (2026-10-13)

`terrain_resources.h` and `vulkan_terrain_resources.cpp` implement bounded
terrain import and immutable atomic rectangle updates, not terrain scene/GPU
rendering. Exactly one chunk 102 (raw) or 103 (zlib) is accepted, with a 2 MiB
input/inflation limit, 2..256 grid rows, physical size .001..65536 and 1..16
texture slots. System `ZLIB::ZLIB` supplies zlib; no bundled copy is added.
Malformed/oversized/truncated/trailing/concatenated compressed streams, unsafe
texture paths, invalid transforms/samples/indices and unsupported strides fail.

The real disk record is 22 bytes: height, one float2 shift, three texture IDs,
three weights and color. The older facade `terrain_data` is 38 bytes with three
UV sets; it is neither ABI-modified nor reinterpreted as the new disk layout.
Aliased texture slots remain distinct user slots in their original order,
matching the legacy texture pool. Original weights/color/shift are retained;
the loader does not silently project layered materials onto one diffuse map.

Diagnostic local geometry follows the original X/-Z grid and triangle winding,
with float-height bounds, finite-difference normals and orthonormal packed
tangent bases. This does not claim parity with legacy quantized GPU height maps
or terrain shading. Updates validate half-open rectangles, tight row strides,
1..4096 fragments, cell budget and nonoverlap before returning a new snapshot.
Invalid edits leave the previous snapshot untouched; unchanged edits retain
its identity. Changed edits copy caller data and regenerate geometry/bounds.

`xray_render_stage8_terrain` covers raw/compressed imports, corrupt inputs,
minimum/maximum grids, independent plane/topology/basis controls, texture-slot
aliases, snapshot isolation and rejected/unchanged/changed updates. At this CPU
checkpoint nine terrain world/facade no-ops became explicit unsupported errors
and the terrain scene flag was still rejected. The 2026-10-15 unlit GPU profile
below enables that flag and implements add/remove/copied buffer updates;
the remaining terrain APIs retain explicit capability errors.

CPU-only real-data probe (no GPU or windows):

```powershell
& ./binaries/xray_vulkan_terrain_probe.exe `
  C:/Stalker2_2011/xr2-dsgn/resources/sources/projects `
  build/new-terrain-report.json
```

The report must be a new file outside the input tree. Two accepted recursive
scans pass all **814 original cells**, including 105 under scenario projects:
3439150 vertices and 6668288 triangles per run. All cells use chunk 103, row65,
size64 and 22-byte records. An independent Python struct/zlib oracle checks
metadata, indices, bounds/counts and all 814 original SHA-256 hashes before and
after. Both C++ reports are identical. The first 813/814 scan is retained: it
exposed a legal duplicate texture name in `night_test/terrain/3_-2.terr`, fixed
by retaining aliases rather than editing or renumbering the source asset.

`terrain/junk_industrial` has source options but no diffuse/normal/specular TGA
in the inspected sources. Texture resource integration must report that missing
input explicitly. Cooks and the bounded facade/unlit GPU path were implemented
after this CPU-only checkpoint, as documented below. Collision/physics and
normal game/editor startup remain open. This proof is CPU import only, not
terrain GPU acceptance. Evidence: `build/stage8-terrain-import/real-summary.json`.

Final verification: GCC 16.2/MinGW-w64 Ninja Debug build passed; **90/90 CTest
(50 GPU)** in 255.94 seconds and **14/14 Python tests**. Four focused terrain,
source, background-window and scene-resource tests passed three repetitions
(12 executions, 5.58 seconds). The terrain CPU test covers 468 malformed/
capability rejections. Existing GPU tests retain validation errors/warnings 0/0;
no terrain GPU acceptance is implied. All 814 source hashes were rechecked
after regression. Hidden/nonactivating/input-transparent GPU windows remain
unchanged; terrain probes create no windows. Summary:
`build/stage8-terrain-import/summary.json`. Debug durations are not benchmarks.

### Terrain Resource Cooks (2026-10-13)

The Vulkan resource group now owns `terrain_mesh_class` and
`terrain_instance_class`. A mesh retains its original CPU cell and layered
diffuse/normal/specular resources; an instance retains the mesh dependency and
an isolated immutable cell snapshot. Queries use `reuse_false`, matching the
existing static path. Changed/unchanged/rejected edits preserve the established
atomic CPU update semantics; closed-session edits explicitly fail.

Supported inputs are canonical `.terr` virtual paths, query-create raw or
compressed bytes, and the legacy project fallback
`resources/projects/<project>/terrain/<cell>`. Optional `terrain_cook_user_data`
must identify a live scene from this registry; the project string is validated
and copied before child queries. File bounds/type are checked before raw-data
queries. Invalid paths/context, missing files and failed dependencies finish
with a resource error rather than an assertion or partial parent resource.

For each unique texture name, the cook queries `<name>`, `<name>_nmap` and
`<name>_spec`, just like the original pool. Aliased user slots retain their
indices and share the same dependency handles within a mesh. Existing cooked
DDS takes precedence; absent DDS uses source TGA/options. Invalid present DDS
does not fall back. Source options preserve sRGB/mips/NORMAL_MAP packing and
normal queries force linear sampling. Missing maps are not substituted.

The bounded resource profile accepts 1..16 slots, at most 48 texture queries,
square 2D textures up to 4096, and at most 256 MiB of published decoded mip data
per mesh, counting unique dependencies. This is post-decode validation, not a
peak-allocation guarantee or allocation-failure acceptance. GPU descriptor/
array limits and scene-wide budgets are documented in the later unlit GPU
profile; coherent layer reload remains open.

Registration preflights all three terrain class IDs. Cooks remain registered
until pending queries and live instances/meshes/textures drain after close.
`terrain_model_class` is an explicit-error capability gate until collision and
physics exist; no dummy game wrapper with null collision is returned. At this
cook-only checkpoint terrain scene flags/facade submission were unsupported.
The later unlit profile enables the terrain flag and dedicated facade methods;
static `add_model` still explicitly rejects terrain instances. These cook
reports do not themselves claim GPU acceptance.

`xray_render_stage8_terrain_assets` covers mesh/instance/texture dependencies,
raw/compressed/create/project queries, aliases, TGA packing/color spaces/mips,
cooked DDS precedence, failures, isolated atomic edits, late resources and
closed pending queries. Its HWND is message-only and no GPU is initialized.

The real CPU resource probe accepts a byte-identical source pack and a new report
outside that pack, followed by cell paths; prefix an expected-error path with
`!`. It recooks each positive cell three times, checking decoded pixels,
texture aliases, atomic edits and complete cook drain:

```powershell
& ./binaries/xray_vulkan_terrain_cook_probe.exe `
  build/terrain-pack build/new-terrain-cook-report.json `
  resources/projects/alex/terrain/0_0.terr
```

Real controls cover alex, night_test with aliases, and a03_bomb_shelter with
nonzero shifts. mine_test with missing junk_industrial is a negative dependency
control. Original terrain/TGA/options are not edited or converted offline.
Reports explicitly set `gpu_acceptance=false`. Evidence and source hashes:
`build/stage8-terrain-cooks`. The unlit GPU layer pass and bounded scene/facade
adapter were implemented after this checkpoint and are documented below.
Collision/game wrapper and normal game/editor startup remain open. This does
not reopen accepted CPU import/cooks or M7.

Final verification: full GCC 16.2/MinGW-w64 Ninja Debug build passed;
**91/91 CTest (50 GPU)** in 301.68 seconds and **14/14 Python tests**.
Five focused source/import/assets/background/scene-resource tests passed three
repetitions (15 executions, 6.89 seconds). The new resource test covers 25
rejected input/capability/lifetime cases; the CPU importer retains 468.
Existing GPU validation summaries report errors/warnings 0/0.

Two real-resource runs each pass nine recook cycles and one expected missing-map
query. Reports and decoded mip fingerprints match exactly. Alex and
a03_bomb_shelter use four slots/twelve unique texture resources each;
night_test uses seven slots/eighteen unique resources, preserving the alias.
All 49 original and corresponding pack files were rechecked unchanged by
SHA-256. Terrain tests/probes use only HWND_MESSAGE without GPU initialization;
existing GPU windows remain hidden/nonactivating/input-transparent. Evidence:
`build/stage8-terrain-cooks/summary.json`, `real-summary.json` and
`real-pack-final/provenance.json`. Debug durations are not benchmarks. This
bounded cook block is complete. It does not accept terrain rendering or all M8;
the separate unlit GPU acceptance follows below.

### Terrain Unlit GPU Profile (2026-10-15)

Terrain now has a real GPU diffuse-layer path. `m_create_terrain` enables a
separate scene list with facade add/remove and copied atomic buffer updates;
particle configuration and the collision/game wrapper remain unsupported.
Limits are 64 cells/262144 vertices per scene. Current geometry bounds drive
frustum culling. Static `add_model` still rejects terrain instances.

RHI sampled `type_2d_array` supports 1..16 layers and layer-major mip uploads.
Vulkan creates explicit array views; shader reflection/binding distinguish
slot0 arrays from ordinary 2D/cube textures. Other array descriptor slots stay
unsupported. Missing arrays do not use fallback textures. Existing 2D/cube/BC
contracts remain intact; new CPU tests cover layer/mip order and invalid uploads.

The shader blends three authored diffuse slots on GPU, not through CPU baking.
The legacy color formula is `(weighted_diffuse + (1-sum(weights))*.5)*color*2`;
weights are not normalized. Tiling is eight, with original (-X,+Y) shift divided
by physical size, rounded/clamped user-slot IDs, sRGB decode and presentation
encode. Explicit LOD0 makes this a deterministic unlit diagnostic profile.
Fields are triangle-interpolated vertex attributes, not a claim of parity with
legacy bilinear parameter maps, quantized height/shift textures, detail maps or
original scene lighting/images.

GPU diffuse slots must have identical dimensions/format/mip topology, square
2D images up to 4096 and at most 256 MiB of array upload data per queued view.
Aliases retain user-slot indices and occupy array slots. Heterogeneous inputs
fail explicitly without resizing/reformatting. The real night_test alias cell
also mixes 1024/2048 textures and is outside this GPU profile; its earlier CPU
import/cook acceptance is unchanged.

`scene_renderer::terrain_update_cell_buffer` deep-copies caller data using the
facade's allocator, with a short-lived caller-allocator regression control. The
render-thread adapter maps 38-byte facade samples to 22-byte disk semantics
only when all three UV sets specify one identical shift; independent UVs fail.
Half-open rectangles, finite values/IDs and transform validate before atomic
publication. Queued draws retain cell/texture snapshots; GPU caches use existing
retirement fences. Synthetic controls cover copied edits, split-view old/new
geometry and texture reloads, 1/16 layers, weights/grey/color, eight resize and
recook cycles, live/capacity/retirement/drain bounds and lit-mode rejection.

The GPU executable also accepts a fourth argument naming a read-only source
pack for two real-cell controls. Each run checks 864 independent RGB channels
against CPU triangle/source-field/wrap-filter/sRGB calculations at 3/255.
Raster positions use the device's reported subPixelPrecisionBits (8 here);
the earlier ideal-coordinate mismatch is retained without widening tolerance.
Synthetic controls retain 2/255. Original alex and shifted a03_bomb_shelter
cells pass with validation 0/0 through teardown; the shifted capture was reviewed.

This bounded unlit profile is complete, not terrain or M8 as a whole. Remaining
capabilities at this unlit checkpoint included GBuffer/material/lighting,
shadows/MSAA/TAA, texture operations/coherent reload and agreed heterogeneous
input handling. Collision/game wrapper and normal game/editor startup remain
open. Evidence: `build/stage8-terrain-gpu`.

The old Stage3 DXC array rejection was explicitly updated for the new capability:
an exact dimension-mask positive now accepts slot0 Texture2DArray; unsupported
Texture3D remains negative. All seven rejection controls remain, along with
wrong-slot arrays and descriptor-array rejection. The initial 92/93 full run
is retained separately; neither test counts nor pixel tolerances were weakened.

A separate regression incident logged NVIDIA ICD vkCreateInstance return -3/no
drivers followed by a Stage2 process SegFault. Its root cause is not established
or claimed fixed. Ten subsequent Stage2 runs passed. A missing-ICD GDB control
exited normally with error code 1, but does not reproduce the vendor failure.
Logs are retained under `build/stage8-terrain-gpu`; startup stability/failure
injection remains an M9 risk, distinct from terrain image/validation acceptance.

Final verification: full GCC 16.2/MinGW-w64 Ninja Debug build passed;
**93/93 CTest (51 GPU)** in 309.37 seconds and **14/14 Python tests**.
Seven focused terrain/import/assets/arrays/background/scene-resource/DXC checks
passed three repetitions (21 executions, 6.01 seconds), including the short-lived
caller allocator control. CPU rejections: 13 arrays, 29 resource/adapter and
468 import cases. All 33 final validation summaries report errors/warnings 0/0.
Two final ownership-fix real-GPU runs each check 864 RGB channels; both captures
are byte-identical across runs and to the earlier accepted images. All 49
original and corresponding pack files were rechecked unchanged by SHA-256.
No automatic retries were added to hide the startup incident. GPU windows stay
hidden/nonactivating/input-transparent. Summary/scope/risk record:
`build/stage8-terrain-gpu/summary.json`; shifted real capture:
`build/stage8-terrain-gpu/real-owned-first-1.bmp`. Debug durations are not
benchmarks. The unlit block is complete; terrain and M8 as a whole remain open.

### Terrain Deferred Material And Lighting Profile (2026-10-15)

Terrain now enters the existing scene GBuffer, material and deferred lighting
passes, alongside static/skeletal surfaces. No second scene pipeline is added.
The unlit profile and its accepted real captures remain byte-identical.
Normal/specular texture snapshots are captured at queue time, uploaded as role
arrays, cached weakly and retired through the existing backend fences.

Reflection/binding adds explicit Texture2DArray dimension bits for normal slot1
and specular slot3, keeping slot0's existing bit. Other array slots, multisample
arrays and descriptor arrays remain unsupported; ordinary 2D/cube contracts
remain exact. Normal arrays use force-linear snapshots, while diffuse/specular
preserve authored color spaces. Each array must be homogeneous in dimensions,
format and mip topology; different roles need not match each other. The queued
view's 256 MiB terrain upload cap includes all role arrays. Cell/vertex/layer
limits are unchanged.

The source-derived profile uses original terrain_pre_pass.ps roughness 50/255
and Fresnel .075. AG normal decoding/Y flip, normalized per-layer normals and
weighted blending retain the neutral normal remainder; exact cancellation
falls back to the neutral tangent-space direction. Packed mesh N/T/B use the
existing cofactor normal transform and orthonormalized handed basis. Material
albedo retains the unlit weights/grey/color/shift formula; specular is weighted
red-channel intensity without a grey remainder, matching terrain_material_pass.ps.
Emission is zero. GBuffer stencil is 0x82 and material uses EQUAL read-only depth
with identical vertex position arithmetic. Exact 128-byte GBuffer and 144-byte
material constant buffers preserve the existing ABI checks.

GPU tests independently check six authored weight sets (including zero and
sum>1), normal/GBuffer/material outputs, directional diffuse/specular/final-lit
and ambient-lit CPU oracles at unchanged 2/255 tolerance. Queued old/new normal
and specular reloads are isolated. Eight unlit+lit recook cycles check bounded
live/capacity/retirement/drain; the previous eight resize and unlit/facade
snapshot controls remain. At this deferred-only checkpoint shadows, MSAA and
TAA requests explicitly failed. The subsequent shadow integration below enables
terrain shadows; the subsequent AA integration also removes the MSAA/TAA gates.

Two real-source runs use original alex and shifted a03_bomb_shelter cells from
the byte-identical pack. Each run checks 3456 RGB channels across unlit, albedo,
specular and ambient-lit views at unchanged 3/255 tolerance; all eight captures
match across runs. The previous unlit captures also match byte-for-byte.
All 49 original files and pack copies retain their provenance SHA-256 hashes.
Real-cell normal/directional pixel oracles are not claimed by these runs;
normal/directional controls above are synthetic. A shifted ambient-lit real
capture was visually reviewed. GPU windows remain hidden/nonactivating/input-
transparent and check focus/capture on readback.

This completes the bounded 1x/LOD0 deferred material/lighting profile, not all
terrain or M8. Legacy bilinear parameter maps, quantized height, detail/parallax,
terrain shadows/MSAA/TAA, heterogeneous textures within an array, independent
legacy UV sets, texture operations/coherent layer reload, collision/game wrapper
and normal game/editor startup remain outside this proof. The active work order
and completion boundary are only in the canonical plan above.
Evidence: `build/stage8-terrain-deferred/summary.json` and adjacent logs/captures.
Initial ABI mismatch and normal-oracle failures are retained: separate exact-
size constant buffers fixed the former; accounting independently for existing
source normal normalization/byte quantization fixed the latter without changing
pixel tolerances. Debug durations are not benchmarks.

Final deferred-profile verification: full GCC 16.2/MinGW-w64 Ninja Debug build
passed, **93/93 CTest (51 GPU)** in 270.00 seconds and **14/14 Python tests**.
Seven focused tests passed three repetitions (21 executions, 16.13 seconds).
Array contract tests now cover 18 rejections, retaining the original unscoped
normal-array rejection and adding mapped normal/specular array positives plus
unsupported roughness-array rejection. Resource/adapter and import tests retain
29 and 468 rejections. All 33 final validation summaries report 0 errors/0
warnings through teardown. The Python run uses workspace TEMP/TMP after the
sandbox temp denied nested mkdir; its initial failure log is preserved.
Summary generation independently rechecks JUnit/log results, capture hashes,
previous unlit captures and all 49 source/pack files. Historical startup/TAA
risks remain in M9; this green regression does not claim their root-cause fixes.

### Terrain Shadows (2026-10-15)

Terrain immutable mesh/transform snapshots now join the existing opaque caster
list before shadow preparation/planning, including cells outside the camera
frustum. The shared depth-only static mesh drawer supplies terrain shadow maps;
the main terrain layer/GBuffer/material path is unchanged, not replaced with
static material rendering. The depth-only path needs no diffuse/normal/specular
arrays. Directional depth fitting and per-shadow frustum culling use the cell
mesh bounds and captured transform; queued geometry edits/removal cannot mutate
an earlier caster snapshot. The existing directional, spot and point atlas,
PCF, bias and fence contracts are reused without shader changes.

The hidden GPU fixture checks terrain-to-terrain, terrain-to-static and
static-to-terrain for all three light types: nine profiles and 764 independent
ray/illumination checks at the unchanged 2/255 RGB tolerance. The new oracle
excludes a fixed .04-NDC PCF/raster edge band rather than claiming exact binary
visibility at filtered silhouettes. It also covers off-camera terrain casters,
directional cascades 1..4, a point +Z face-mask control, linear transparency,
copied geometry edits and old/new queued caster/removal/light snapshots.
Eight output/atlas-size cycles check resource/capacity bounds and final
retirement/pass drain. At this shadow-only checkpoint MSAA/TAA capability
rejections remained; the shadow rejection was replaced with actual acceptance.

Two real-source runs use original alex and shifted a03_bomb_shelter cells.
Each cell receives a directional shadow from an off-camera static mesh and,
after a copied transform update, casts onto a static receiver from outside
the camera frustum. Each run checks 120 shadow-visibility RGB channels plus
the previous 3456 unlit/albedo/specular/ambient-lit channels. Transparency one
restores the receiver baseline byte-for-byte. All twelve captures match across
runs, and the eight earlier unlit/deferred captures also remain byte-identical.
All 49 original inputs and pack copies preserve provenance SHA-256. A real
shadow-receive capture was visually reviewed. Real spot/point shadow pixel
controls are not claimed; those controls are synthetic.

Evidence: `build/stage8-terrain-shadows/summary.json` and adjacent logs/captures.
The initial fixture failure retained default tonemap while comparing raw-light
CPU results; disabling postprocess explicitly in that fixture fixed the mismatch
without changing timeouts, iterations or pixel tolerances. This accepts bounded
1x terrain shadows, not terrain MSAA/TAA, legacy parameter-map/quantized-height/
detail/parallax/full image parity, game/editor startup or all M8. Active next
work and the completion boundary remain only in the canonical plan.

Final shadow-profile verification: GCC 16.2/MinGW-w64 Ninja Debug build passed,
**93/93 CTest (51 GPU)** in 341.67 seconds, **14/14 Python tests** using workspace
TEMP/TMP. Nine focused tests passed three repetitions (27 executions, 60.96
seconds), including the existing directional/spot/point shadow GPU tests.
All 33 final validation summaries report 0 errors/0 warnings through teardown.
The generated summary independently checks JUnit/log results, both real-run
capture sets, eight previous deferred captures and 49 original/pack hashes.

### Terrain MSAA And TAA (2026-10-15)

Terrain now shares the existing sample-shaded deferred GBuffer/material/lighting
and linear-radiance multisample resolve. No new shaders or separate AA engine
are introduced. The TAA scene signature includes immutable terrain cell identity,
captured transform and diffuse/normal/specular texture snapshots, including
off-camera cells that can cast visible shadows. Unchanged facade buffer edits
preserve the cell and temporal revision. Terrain and static model culling share
the jitter-expanded projection so subpixel terrain at the frustum edge survives.

The hidden synthetic fixture checks 2x/4x/8x against an independent k/N linear
radiance, Reinhard and sRGB oracle: 584496 coverage pixels, including 2621 partial
edge pixels, at unchanged 2/255 RGB tolerance. Stable multisample frames and
1x bypass are byte-exact with stable live resources/capacity. TAA accumulation
differs from both aliased output and jitter-only rendering. Eight no-op phases
replay exactly. Eleven automatic-reset controls match an explicit first-jitter
reset: weights, transform, direct CPU geometry, all three role texture reloads,
remove/add, camera cut, projection and off-camera direct CPU caster geometry.

Twenty-four directional/spot/point shadow profiles span 1x/2x/4x/8x with TAA
off/on, with known lit/shadow RGB controls. Combined 4x MSAA/TAA/bloom, split-view
history and queued old/new cell edits remain isolated. A cell beyond the
unjittered frustum is visible at the expected jitter phase. Eight resize cycles,
minimize/restore history reset and teardown check exact resource/capacity drain.
During 128 alternating dynamic edits active resource counts and pool capacity
remain equal to baseline. Retired buffers peak at two, below the two-buffers x
two-frame-slots bound of four, and drain exactly after updates stop.

The initial stress oracle compared the retired-buffer tail to a static zero-tail
baseline. Diagnostics proved active counts and capacity were identical; only two
old vertex/index buffers awaited a frame fence. The final oracle separately
checks exact active/capacity equality, bounded retirement and exact final drain.
Failed logs are retained in `focused-before-dynamic-baseline.*` and
`dynamic-diagnostic.log`; no pixel tolerances or timeouts were relaxed.

Two original-source cells each run stable 1x/2x/4x/8x and TAA accumulation,
eight exact first-frame/history-transform reset controls, and 4x+TAA directional
receive-shadow controls (30 RGB channels/cell). Each run retains the prior
3456 material RGB and 120 shadow RGB controls and adds 60 AA-shadow RGB checks.
Thirty captures match byte-for-byte across two runs; the twelve preceding
shadow/deferred/unlit captures are unchanged. All 49 original inputs and pack
copies preserve provenance SHA-256. The shifted real 4x+TAA capture was visually
reviewed: nonblank terrain with filtered source textures, not original-image
or universal AA-quality parity. Windows remain hidden/nonactivating/input-
transparent and check foreground/capture state at readback.

Evidence: `build/stage8-terrain-aa/summary.json` and adjacent logs/captures.
This completes bounded homogeneous-role-array LOD0 terrain AA, not legacy
parameter maps/quantized height/detail/parallax, motion-vector based dynamic
reprojection, universal TAA quality, heterogeneous arrays, texture operations/
coherent layer reload, collision/game wrapper, game/editor startup or all M8.
The active next task and completion boundary remain only in the canonical plan.
Historical one-off TAA mismatch and NVIDIA-ICD startup failures remain M9 risks;
passing this profile does not establish their root-cause fixes.

Final AA-profile verification: GCC 16.2/MinGW-w64 Ninja Debug build passed,
**93/93 CTest (51 GPU)** in 443.82 seconds, **14/14 Python tests** using workspace
TEMP/TMP. Nine focused tests passed three repetitions (27 executions, 81.53
seconds), including existing static MSAA/TAA and background-window contracts.
All 33 final validation summaries report 0 errors/0 warnings through teardown.
The generated summary independently checks JUnit/log results, both 30-image
real-run sets, twelve previous captures and all 49 original/pack hashes.
Durations describe this Debug harness run, not performance benchmarks.

### Terrain Local AABB Updates (2026-10-15)

The engine world now implements `terrain_update_cell_aabb`; its obsolete Vulkan
stub is removed. The existing editor queued API reaches that implementation,
and the scene facade has a matching copied-value queued command. Runtime checks
the active owner and scene membership before publishing. Bounds are local,
before the per-scene instance transform, matching the editor's `get_local_aabb()`.
All six coordinates must be finite, ordered per axis and within +/-1e6;
degenerate axes are allowed. Validation completes before any state changes.

The optional hint is instance-local, not stored in the shared asset or immutable
mesh. Camera culling uses its union with current geometry bounds, including
geometry edits made after the hint. Stale/under-sized hints cannot hide visible
vertices. Replacing the hint can shrink an earlier expansion. No terrain cell,
vertex/index buffer, texture dependency or temporal signature is replaced by
a bounds-only edit. Culling is resolved when queueing; earlier queued draws
already retain their geometry and visibility. Shadow fitting/culling continues
using exact immutable mesh bounds, avoiding metadata-induced shadow precision
or TAA-history changes. Collision/spatial-tree/game-wrapper integration remains
open and is not claimed by this renderer implementation.

CPU resource/adapter tests now contain 55 rejections, including NaN/Inf on either
side of each axis, inverted/out-of-budget bounds, invalid membership and closed
owner. Positive controls check copied values, instance/asset isolation, degenerate
and under-sized hints, unchanged cell identity and conservative bounds after
direct CPU geometry edits. A secondary scene is created through the normal cook
without requesting another output. The initial raw test scene's missing resource
deleter was diagnosed with a debugger breakpoint, not accepted as a passing test.

The hidden GPU fixture proves actual camera submission: an initially culled cell
queues exactly two vertex/index buffers when its hint expands into the frustum.
Changing caller storage and shrinking the hint after queueing preserve the
captured draw. During 128 bounds-only updates geometry, pixels, active resources
and pool capacity remain exact. Split queued views remain correct. Eight 4x+TAA
phases replay byte-for-byte despite changing bounds. Directional/spot/point
off-camera terrain shadows retain exact images under changed metadata, with
independent receiver-to-caster ray and black interior pixel controls. Final
retirement/pass resources drain. Existing terrain shadow and AA controls remain.

Two original-source cells each check 32 bounds-only updates with exact unlit
pixels, cell identity, active resource counts and pool capacity. Eight 4x+TAA
phases replay exactly while changing hints. Both runs pass with validation 0/0
through teardown: 32 captures/run are byte-identical, and all thirty previous
AA/material/shadow captures remain unchanged. All 49 original inputs and pack
copies retain provenance SHA-256. The shifted real bounds/TAA capture was
visually reviewed. This is not full original terrain image parity.

Initial fixture failures are preserved beside the evidence: a Windows `far`
macro collision, duplicate cooked output, a raw scene without cook deleter,
the wrong empty-unlit clear expectation and a shadow control point outside
the caster's projected interior (receiver Z=.5, caster Z=-.25). Corrections use
normal resource queries, the configured linear clear color and an independently
verified interior ray; pixel tolerances and timeouts are unchanged. The initial
stale GPU executable is not credited as acceptance for the new bounds fixture.

Evidence: `build/stage8-terrain-bounds/summary.json` and adjacent logs/captures.
The active next task and M8 completion boundary remain only in the canonical
plan; this does not create another numbered substage or close all M8.

Final bounds-profile verification: GCC 16.2/MinGW-w64 Ninja Debug build passed,
**93/93 CTest (51 GPU)** in 426.04 seconds, **14/14 Python tests** using workspace
TEMP/TMP. Ten focused tests passed three repetitions (30 executions, 157.15
seconds), including static culling, static MSAA/TAA and background-window
contracts. All 33 final validation summaries report 0 errors/0 warnings through
teardown. The generated summary independently checks JUnit/log results, both
32-image real runs, thirty earlier captures and all 49 original/pack hashes.
Durations describe this Debug harness run, not performance benchmarks.
All GPU windows remain hidden/nonactivating/input-transparent. Debug durations
are not benchmarks; inherited startup/TAA risks remain in M9.

### Terrain Coherent Texture Reload (2026-10-15)

Terrain instances retain an immutable diffuse/normal/specular image set covering
all original user slots, including aliases. The registry captures candidate
images under its mutex and the existing registry-then-state lock order only
after pending reload queries drain. A failed member retains the old whole set,
even if other individual texture resources already accepted new bytes. Generic
static material reload publication and counters remain unchanged.

Shared terrain-array validation checks homogeneous dimensions/format/mips
inside each role, linear normal data and the combined 256 MiB budget before
publication. Different role dimensions remain supported. Incompatible sets
retain the old version; a weak rejected-tuple cache coalesces repeated errors
without retaining failed image bytes. `texture_set_updates()` reports accepted
sets and layout rejections separately from generic disk reload statistics.
No-op captures preserve set identity. Geometry, bounds and buffer identity
are unchanged. Each queue captures one set per cell, including off-camera
casters, then uses it for the TAA signature and both terrain passes. Earlier
queued views retain their own images through completion.

CPU controls cover deterministic partial role completion, failed roles,
immutable old images/cell and isolated instances, incompatible array/linear
normal rejection and coalescing, actual malformed disk normal with changed
diffuse/specular, coalesced queries, recovery, complete per-role resize and
aliased slots. Generic reload tests also verify failure-marker recovery.
Synthetic GPU controls check old-set retention, queued old/new views,
independent normal/specular pixels at unchanged 2/255 tolerance, incompatible
array hold, compatible full-role resize, eight all-role resource/capacity/
retirement cycles, eight byte-exact failed-reload 4x+TAA phases, exact accepted
first-jitter reset and final retirement/pass drain.

Two original-source cells run controlled pending/failed/incompatible/recovery
states in memory, with no source-file writes. Sixteen 4x+TAA phases/run preserve
images, geometry, live resources and capacity exactly. These are controlled
real-cell state tests; actual file failure/recovery tests above use synthetic
fixtures. Thirty-four captures/run match across two runs, all thirty-two prior
captures remain unchanged, and all 49 original inputs/pack copies preserve
provenance SHA-256. A shifted real reload/TAA capture was visually reviewed.

Initial failures are preserved: cooking an alias after fixture file resize hit
cached VFS size, so the alias is now cooked before edits and tested through
physical reload. An unlit-only resize cycle compared against previous normal/
specular uploads; diagnostics measured textures dropping from four to three,
not growth. Every final cycle exercises all three roles before exact baseline
and drain checks. No pixel tolerance, timeout or stress count was weakened.

Evidence: `build/stage8-terrain-reload/summary.json` and adjacent logs/captures.
This accepts bounded per-cell image-data reload, not texture binding add/remove/
exchange (their explicit errors remain), heterogeneous images inside one role
array, options/material-graph hot reload, scene-global atomic publication or a
transactional on-disk authoring snapshot. Normal game/editor startup, collision/
game wrapper and full original image parity remain outside this proof. The
canonical plan is the only active checklist; M8 remains open.

Final coherent-reload verification: GCC 16.2/MinGW-w64 Ninja Debug build passed,
**93/93 CTest (51 GPU)** in 376.78 seconds, **14/14 Python tests** using workspace
TEMP/TMP. Fourteen focused tests passed three repetitions (42 executions,
110.32 seconds), including generic reload, TGA/textures, sky/surface resources,
static AA and background-window contracts. All 33 final validation summaries
report 0 errors/0 warnings through teardown. The generated summary independently
checks JUnit/log results, both 34-image real runs, thirty-two earlier captures
and all 49 original/pack hashes. Debug harness durations are not benchmarks;
inherited startup/TAA risks remain M9 work, not claimed fixed by these passes.

### Terrain Binding Transaction Core (2026-10-15)

`prepare_texture_bindings()` validates already-loaded dependency resources and
stages a complete per-instance slot table, immutable cell metadata and coherent
diffuse/normal/specular image set without publication. `commit_texture_bindings()`
requires the same instance generation, source cell and current dependency
snapshots. Foreign/stale candidates, closed sessions, pending/failed reloads,
changed dependencies, missing roles and incompatible arrays fail before any
publication. Potentially allocating copies precede the final render-thread
swaps. This is not a cross-thread or scene-global transaction.

The table supports 1..16 slots, aliases, sparse append, interior removal and
empty-tail trim without renumbering user IDs. Empty slots must have no retained
resource handles. Their array entries use an occupied compatible layer solely
to make complete uploads; direct sample IDs and triangle-interpolated IDs are
forbidden from reaching a hole, including zero-weight roles. Both shaders
interpolate and round IDs, so checking only vertex references is insufficient.
Cooked files still reject empty texture names. Runtime sample edits revalidate
holes and always replace mesh identity when attributes change.

Binding metadata clones share immutable mesh identity. The terrain geometry
cache now keys that identity, avoiding vertex/index uploads for binding-only
changes. Queued draws retain the old cell/image pair; other instances and the
original asset remain unchanged. No-op binding commits retain cell/set identity
and TAA history; accepted changes enter the existing terrain history signature.
Coherent reload continues using the newly committed resource-state table.

CPU controls add 28 binding rejections, including stale geometry/bindings,
foreign/null candidate, invalid names/counts/roles/layout, sRGB normals, pending/
failed reloads and dependencies changed after preflight. Sparse slot 15,
interior removal, tail trim, direct/interpolated zero-weight references, strict
disk imports, immutable old pairs/isolated instances and sample-vs-metadata
mesh identity are checked. Existing resource/adapter tests now include closed
binding preflight/commit and report 57 rejections.

Synthetic GPU controls check queued old/new triplets, independent normal and
specular pixels at unchanged 2/255 tolerance, sparse slot 15 and removal,
interpolated-hole rejection, no metadata-only buffer reupload, eight exact
resource/capacity/retirement cycles, eight byte-exact no-op 4x+TAA phases,
exact accepted first-jitter reset and retirement/pass drain.

Two original-source cells exchange slot zero for another loaded original
triplet, then check unlit/albedo/specular/ambient-lit pixels using the active
binding table and an independent raster/material oracle. Each run adds 3456
binding RGB checks. Restoring bindings restores the unlit image byte-for-byte,
preserves mesh identity and leaves vertex/index buffer count exact. Both real
runs pass validation 0/0 through teardown: 38 captures/run are byte-identical,
all 34 previous captures remain unchanged, and all 49 original inputs/pack
copies preserve provenance SHA-256. Exchange/restored captures of the shifted
cell were visually reviewed. These are core-level loaded-resource controls,
not async loading or full original terrain image parity.

Final verification: GCC 16.2/MinGW-w64 Ninja Debug build passed; final Ninja has
no work. **93/93 CTest (51 GPU)** passed in 579.14 seconds, **14/14 Python tests**
using workspace TEMP/TMP. Fourteen focused tests passed three repetitions
(42 executions, 177.88 seconds). All 33 final validation summaries report
0 errors/0 warnings through teardown. The acceptance script independently
checks JUnit/log results, both 38-image real runs, thirty-four earlier captures
and the 49 original/pack hashes. GPU windows remain hidden/nonactivating/input-
transparent. Durations describe this Debug harness run, not benchmarks.

Initial failures are preserved in `first.log/xml`: the CPU fixture assumed
independent cook queries shared image pointers, instead of comparing retained
isolated snapshots. The new GPU binding controls passed, but their locally
retained old dependencies affected the subsequent recook cache baseline;
all test-owned references now expire before final drain. Shader review also
caught triangle-interpolated IDs crossing holes; explicit CPU/GPU rejection
controls were added. No tolerance, stress count or timeout was weakened.
The integrated M7 test passed at 118.55 seconds against its unchanged 120-second
timeout; the small timing margin is recorded, not claimed diagnosed or fixed.
Inherited startup/TAA risks remain M9 work. Evidence:
`build/stage8-terrain-bindings-core/summary.json` and adjacent logs/captures.

At this core checkpoint no new dependencies were loaded and world binding
stubs still failed explicitly. The subsequent same-day facade/world integration
report below implements queries, command ordering, scene exchange and
membership/shutdown cancellation. Heterogeneous
images within one role, allocation/device-loss injection, ordinary threaded
game/editor startup, collision wrapper and full original image parity are not
accepted here. No new numbered substage is created; M8 remains open.

### Terrain Async Binding Commands (2026-10-15)

The three world texture-binding stubs are replaced. Game scene facade commands
copy texture names by value; existing editor add/remove/exchange commands use
the same world implementation. This does not initialize the ordinary editor
renderer. Resource callbacks and terrain mutations run on the calling render/
resource-dispatch thread, following the existing cook dispatch model.

Each scene has a FIFO stream. Add/exchange queries one diffuse/normal/specular
triplet with mip generation and forced linear normal data. Subsequent copied
buffer/AABB edits wait behind the query, so a new slot exists before vertex IDs
can reference it. Overlapping adds retain order. Renders keep the last complete
cell/image pair while loading. Independent scenes can progress concurrently;
shared-instance updates from another stream are rejected using source cell and
binding revision. Same-name add/exchange is a no-op with no query or history reset.

Scene-wide exchange captures affected cells and all matching aliases at the
front of the stream. Every candidate preflights before publication. Batch
commit validates generations/cells, captures all role states under one registry
lock and prepares every allocating copy before nonthrowing swaps across all
targets. A failed last candidate cannot partially commit earlier cells. No
change to the shared GPU array, linear-normal or byte-budget contracts is made.

An asynchronous query/layout/stale-state failure increments failure status,
retains the last committed sets and cancels the rest of that scene stream.
Previously committed commands are not rolled back: this is fail-stop FIFO,
not one transaction spanning the whole queue. Cell membership add/remove cancels
the scene stream immediately; remove/re-add cannot receive a late old result.
World shutdown cancels queued work. Pending callbacks retain only weak stream/
command references and a registry lease for query accounting, not scene/model/
world pointers. Late completion drains query/cook lifetime without publication.

`scene_registry::terrain_binding_status()` exposes requested/committed/failed/
cancelled/pending command counts, triplet queries/in_flight and queued_bytes.
Counters include buffer/AABB commands deferred behind bindings, not all ordinary
terrain edits. World-wide limits are 128 pending commands, 128 in-flight triplet
queries including cancelled work and 8 MiB copied sample/fragment/vector-entry
storage. Bounded command bookkeeping is additional, not an RSS claim. Existing
16 slots, 64 cells/262144 vertices and 256 MiB queued-view arrays remain. Errors
before accepting a command do not count as requested; accepted synchronous
preflight failure counts as failed and throws. Async failures report diagnostics
and counters because the existing world APIs return void.

A dedicated CPU CTest checks FIFO caller-copy semantics, overlapping adds,
all-cell/alias exchange, last-cell incompatible-layout rollback, pending/failed
dependencies, external geometry and same-cell generation edits, independent and
shared-instance scene streams, membership/re-add/closed cancellation, exact
128-command/128-query/8-MiB limits, batch rejection and final cook/query/status
drain. This dated run records 21 synchronous rejections. Since 2026-10-16,
initial incompatible instances are rejected by their resource query; the
last-cell rollback control now starts valid and injects an incompatible private
dependency reload. See the later profile report for current counts.

Synthetic GPU controls check copied names/buffers/AABB, queued old/new views of
two cells, atomic aliases/triplets, ordered sparse add/edit/remove, eight exact
failed-query and eight no-op 4x+TAA phases, exact accepted first-jitter reset,
independent normal/specular pixels and eight actual query/resource/capacity/
retirement cycles. Remove and world shutdown with pending work verify weak
callback cancellation and exact command/query/cook/pass drain. Existing pixel
tolerances, stress iterations and timeouts remain unchanged.

Two original-source cells use actual async triplet queries for scene exchange,
four independent material modes and an editor-style new-slot/vertex-edit/remove
sequence. Appended IDs are checked by the independent raster/material oracle;
restored frames and sample bytes match exactly. Exchange preserves mesh identity
and buffer count when compared in the same unlit profile. These controls add
4320 RGB checks/run without original-file writes. Full original image parity,
mixed layouts within one role, runtime normalization/transcoding, options hot
reload, ordinary threaded startup, collision wrapper and failure injection
remain outside this proof. The canonical plan owns the only active checklist.

Final async-binding verification: GCC 16.2/MinGW-w64 Ninja Debug build passed,
final Ninja has no work; **94/94 CTest (51 GPU)** in 308.41 seconds, **14/14 Python
tests** with workspace TEMP/TMP. Fifteen focused tests passed three repetitions
(45 executions, 112.74 seconds). All 33 validation summaries report 0 errors/
0 warnings through teardown. Both real runs have 44 byte-identical captures;
all 38 previous captures remain unchanged, and all 49 original/pack files retain
provenance SHA-256. Facade append/restored captures of the shifted cell were
visually reviewed. GPU windows remain hidden/nonactivating/input-transparent.
Evidence: `build/stage8-terrain-bindings/summary.json` and adjacent logs/XML/
captures; the script independently checks test status, hashes and control counts.

Initial failed controls are preserved. A backslash `virtual_path_string` asserts
before renderer validation; the raw name validator now checks that case without
violating the typed path invariant. A cancellation target was cooked while
queries were pending, so its query-pump completed them; it is now pre-cooked.
The byte-bound test used an incorrect 7 MiB estimate for five 65536*22-byte
payloads; it now asserts exact sample/fragment/vector-entry storage and rejects
the sixth copy. The real exchange compared an unlit baseline with a final lit
profile: diagnostics showed the same mesh and buffers 12 -> 14, accounted for by
GBuffer and lighting constants. It now returns to the same unlit profile before
the exact buffer assertion. No pixel tolerance, stress count or timeout was
weakened. Integrated M7 passed in 29.64 seconds this run; its previous 118.55/
120-second timing margin and inherited startup/TAA risks are not claimed fixed.
Debug durations are not benchmarks. M8 remains open.

## Terrain Source Texture Profile (2026-10-16)

`xray_vulkan_terrain_texture_profile_probe <source-root> <new-report.json>`
scans `projects/**/*.terr` and referenced `textures/<name>{,_nmap,_spec}.tga`
plus optional `.options`. It uses the production terrain importer, sandboxed
Lua options parser, TGA decoder/mip generation, forced-linear normal semantics
and shared array validator. This is a source-TGA capability inventory, not
resource-cook DDS precedence, GPU acceptance or original image parity. It
does not alter source files in inventory mode and refuses an existing report or one inside the
input root. The inventory rejects symlinks and bounds cells/images/file sizes
and retained decoded images (1 GiB probe cache, not a renderer budget).

Two complete scans of `C:/Stalker2_2011/xr2-dsgn/resources/sources` are identical:
814 cells, 17 slot names, 48 decoded role images, no invalid imports/options/
images. **178 cells are array-compatible; 620 have mixed layouts; 16 reference
the three missing `terrain/junk_industrial` TGA roles.** Among complete cells,
603 mix dimensions/mip counts in all roles and 17 in specular only. The source
images decode to RGBA8, at 1024 or 2048 with full mip chains. Different layouts
between roles are supported; slot aliases keep their indices. All 913 existing
original cell/texture/options files retain SHA-256, and three absent TGA paths
remain absent. Evidence: `build/stage8-terrain-profile/inventory-first.json`,
`inventory-second.json`, `inventory-summary.json` and
`inventory-profile-decision.json`.

Decision: mixed source-TGA normalization is needed; this inventory does **not**
close terrain integration. BC/DDS transcoding is not implied by these source
results. A planning estimate that expands each role to its largest source
dimension/full RGBA mip chain fits 729 of 798 complete cells into 256 MiB;
69 exceed it, up to 671088600 bytes. This is a byte estimate, not a working
normalizer or measured memory usage. Normalization must define its budget policy
before allocating and must not silently lower source resolution or relax the
existing limit. Color-space/packed-normal sampling, atomic publication, queued
snapshots, no-op identities, TAA and resource drain require focused controls.
The canonical plan keeps this one implementation item open before game wrapper/
collision; inventory/import/cooks/bindings are not new substages to repeat.

Meanwhile, initial `terrain_instance_class` queries now preflight the same
texture-set contract as reload/binding/draw, so an unsupported array fails at
load time instead of reaching first draw. Mesh resources may still represent
an unsupported source layout for CPU inspection. Rejections identify role,
slot, incoming/reference dimensions/format/mips and unsupported normalization;
the validator also checks sampled format, legal mip counts and exact mip bytes.
CPU controls cover valid cross-role BC/RGBA layouts, every role's dimension/
format/mip mismatch, malformed mip payloads, actual initial source/cooked query
rejection and all-cell rollback after an incompatible late dependency.

The probe has a CTest self-test covering canonical decoding, aliases, different
role layouts, mixed-array rejection, separate missing/invalid dependencies,
repeat determinism and unchanged source bytes. GPU tests retain the existing
hidden/nonactivating/input-transparent windows and pixel tolerances.

Verification: full GCC/MinGW-w64 Ninja Debug build passed; final Ninja has no
work. **95/95 CTest (51 GPU)** passed in 326.18 seconds, 16 focused tests x 3
(48 executions) in 108.97 seconds, and **14/14 Python tests** passed using
workspace TEMP/TMP. All 33 validation summaries have 0 errors/0 warnings through
teardown. Current CPU rejection counts: 31 array, 61 terrain resource/adapter,
28 binding core, 22 binding commands and 468 importer. Two real GPU runs each
check 11232 independent material RGB samples; all 44 captures are byte-identical
between runs and against the previous async binding acceptance. All 913 original
inventory files, three absent paths and 49 original/pack provenance entries are
unchanged. Probe output guards reject input-root/existing reports without writes.
Evidence: `build/stage8-terrain-profile/summary.json` and adjacent logs/XML/
captures. Initial namespace ambiguity, omitted fixture `texture_dimension` and
obsolete initially-invalid rollback fixture failures are retained. No tolerance,
stress count, timeout or renderer budget was relaxed. Historical TAA/vendor-ICD/
near-timeout risks remain M9 work; these Debug timings are not benchmarks.

## Terrain Mixed RGBA Normalization (2026-10-16)

This implementation supersedes the earlier same-day rejection-only inventory
checkpoint above. `plan_terrain_textures()` performs metadata-only source/layout/
budget preflight; `normalize_terrain_textures()` produces immutable array-ready
snapshots. Mixed dimensions are supported for decoded RGBA8, equal format within
each role, power-of-two square sources <=4096 and complete authored mip chains.
Different layouts between diffuse/normal/specular remain legal. Already uniform
RGBA/BC arrays, including uniform partial mip chains, keep their existing profile.
Mixed formats, compressed-layout resizing, non-power-of-two mixed dimensions and
missing-mip synthesis remain explicit capability errors, not silent conversions.

Each role uses its largest source dimension. Smaller images gain only finer
levels above their original base; every authored mip is copied byte-for-byte
at its shifted level. New levels sample the original base independently with
periodic bilinear filtering, not recursive upscaling. RGB filtering decodes and
re-encodes sRGB where appropriate; alpha stays linear. Normals decode packed AG
XY, reconstruct positive Z as the shader does, filter normalized vectors and
renormalize/repack AG (R=255/B=0). The shader's Y handedness conversion remains
unchanged. This is a supported projection, not original image/filter parity;
the terrain shaders still use the accepted LOD0 profile.

All role/slot output bytes, including alias layers uploaded to GPU arrays, must
fit the unchanged **256 MiB** texture-set limit before any promoted pixel
allocation. There is no downsampling or automatic budget increase. CPU promoted
images are deduplicated for aliases within each role. Preflight reports array
bytes and new unique texture bytes; those exclude allocator/metadata overhead,
retained source data, previous sets and queued views, and are not process RSS.
`new_texture_bytes` is cold promoted-image storage; previous-image reuse can
reduce actual allocation. Strict array validators still require already-normalized
snapshots; raw sets go through the planning/normalization API first.
The existing queued-view/scene/command/resource limits also remain in force.
Allocation failure cannot publish a partial set; allocation injection is M9 work.

Initial instance creation captures raw dependencies coherently under the registry
lock and rejects pending/failed reloads before normalization. Reload publication
and binding preflight use the same normalizer. Instances retain raw source tuples
separately from derived images: batch commit checks the captured **raw** identities
again, then publishes sources/images/slots together after all allocating work.
It never compares a raw texture against its promoted output. No-op tuples retain
set/image identities, unchanged promoted sources are reused when target layout
is unchanged, and failed or over-budget reloads keep the last complete set with
coalesced diagnostics. Queued views retain their old immutable images; geometry
and its cache are not rebuilt by binding-only normalization.

CPU tests independently check linear/sRGB/alpha/repeat-edge values, packed-normal
vector filtering, multiple added levels without recursive blur, exact authored
mips, alias/role/source/no-op/reuse identities, unsupported layouts and the exact
268435440-byte accepted boundary. Actual cooks check normalized mixed RGBA DDS/
TGA dependencies, initial pending rejection, normalized two-cell stale raw-state
rollback/atomic batch commit and pending/failed/partial reload retention.
Former full-mip resize failures are now positive normalization cases. Negative
controls explicitly use unsupported partial mip chains; they were not removed.

Synthetic GPU controls add initial mixed roles/aliases, queued async exchange
and FIFO new-slot vertex edits, independent normal/specular pixels, eight stable
binding resource/capacity/retirement cycles, eight exact no-op and eight exact
over-budget 4x+TAA phases, exact accepted reset and final GPU drain. Existing
tests keep their tolerances, stress counts, timeouts and background-only windows.

Two read-only inventories of all 814 original cells now find **178 already
compatible + 551 normalizable = 729 eligible**, with **69 explicit budget
rejections** and **16 missing-dependency cells**. Inventory executes the actual
metadata planner, not pixel promotion for all cells and not 729 GPU acceptances.
All 48 decoded source images match the previous inventory; all 913 existing
original files and three absent TGA paths are unchanged. The missing
`terrain/junk_industrial` assets are not substituted.

Two real controls exercise `bar_shooting_gallery/terrain/0_0.terr` (mixed in all
roles: six promoted slots, 201326580 array bytes/134217720 new pixel bytes) and
`night_test/terrain/-1_-1.terr` (specular-only mix: four promoted slots,
167772140/89478480 bytes). Actual resource queries, exact authored-mip comparisons,
four independent raster/material RGB oracles, no-op identity and a real
over-budget initial query are checked. The raster oracle samples normalized
snapshots; resampling semantics have separate independent CPU controls. This is
not a comparison claiming the original raw-resolution image is unchanged.
Evidence: `build/stage8-terrain-normalization` (inventory reports, 63-input
provenance pack, real captures/logs and regression evidence).

Acceptance: **96/96 CTest (51 GPU)** passed in 376.06 seconds and **14/14 Python
tests** passed. A broad 17-test x3 run (51 executions, 135.69 seconds) preceded
the final coherent-initial-capture refinement; final six CPU tests x3
(18 executions, 5.74 seconds), the complete suite and final real runs follow it.
All 33 validation summaries report 0 errors/0 warnings through teardown. Final
Ninja has no work. Both final real runs have eight byte-identical captures and
3456 independent material RGB checks/run; all 44 previous homogeneous captures
also remain byte-identical. All 913 original files, three absent paths and
63 new-pack/49 previous-pack provenance entries are unchanged. Albedo captures
of both mixed cells were visually inspected and match final captures exactly.
Evidence and automatic verification: `build/stage8-terrain-normalization/summary.json`.
Initial missing-include/const-auto-deduction build failures are retained; no test
tolerance, stress count, timeout or budget was relaxed. Bounded normalization is
complete; the canonical next terrain task is game wrapper/collision, not another
normalization/import/binding substage. M8 and inherited M9 risks remain open.

## Terrain Collision Foundation (2026-10-16)

Historical foundation acceptance; the resource integration below supersedes
this section's former unsupported-wrapper gate and next-cook task.

The open game-wrapper task now has two independently testable building blocks.
`xray_terrain_collision` owns an immutable local copy of an accepted 2..256
renderer grid, an Opcode mesh/instance, bounds and a center offset. Its instance
opts out of `collision_object` ownership; the allocator must outlive the snapshot.
It does not query/retain textures or a scene, and does not follow later renderer
edits. `terrain_model_class` still explicitly rejects requests until real wrapper
resources, their deleters and coherent geometry/transform behavior are integrated.

`xray_physics_shapes` separates existing shape-resource construction/destruction
from physics worlds and renderer dependencies. The new owned static-mesh factory
copies vertices/indices, subtracts the supplied local center, reflects Z and
reverses winding. The Bullet body needs the corresponding reflected center offset
plus world placement. A retained backing store owns the mesh interface, buffers
and internal-edge metadata; `destroy_shape()` deletes the BVH before releasing
that storage. An intrusive resource_ptr still requires a real resource cook and
creation metadata. Raw factory results are explicitly owned and destroyed, not
pretend queried resources. No global physics allocator/world is required.

The collision archive now selects one Windows ray implementation instead of
also compiling duplicate false-return multi stubs. Ray bounds use the existing
core AABB intersection routine, including parallel axes; collision declares its
animation and renderer debug-drawing link dependencies. The latter is LINK_ONLY
to avoid leaking renderer build flags. Terrain collision remains a separate
static target: directly linking the current collision archive into the renderer
DLL exposed duplicate renderer/engine/game exports. Those failed experiments
are retained; no allow-multiple-definition linker option is used. Resolving one
DLL owner is part of wrapper integration, not a reason to return a null collision.

The focused test compares exact renderer topology/positions and independent
authored anti-diagonal heights on non-planar, flat, negative-height, non-unit-size
and maximum grids. It exercises Opcode/tree and reflected/centered Bullet rays,
translated placement, truncated/backward/oblique/parallel-axis rays, bounds,
default owning and wrapper-owned object teardown, caller-independent copies and
shape-before-backing destructor order. A translation-only sphere contact A/B
uses the same seam impact and 600 steps per phase, with rotation locked in both
phases. It checks an actual contact manifold, corrected normals and settled X/Y/Z,
and requires the no-adjustment control to show lateral drift. The test installs
and restores Bullet's contact callback locally; production hooks are not added.
Metadata generation and normal adjustment follow the system Bullet API and
[upstream implementation](https://github.com/bulletphysics/bullet3/blob/master/src/BulletCollision/CollisionDispatch/btInternalEdgeUtility.cpp).

This does not accept freely rotating sphere/other-body seam quality: initial
free-rotation drift failures are preserved even with internal-edge adjustment.
Engine contact hooks, one collision/physics DLL owner, resource-cook lifetimes,
atomic live collision edits and ordinary threaded game/editor startup remain
open. Existing no-op AABB/cuboid predicates, closest-point/dispatch APIs and
arbitrary transform behavior are not claimed to be fully ported by this profile.
Evidence directory: `build/stage8-terrain-collision-foundation`.

Acceptance: **97/97 CTest, including 51 GPU**, passed in 294.86 seconds;
**10 focused tests x3 (30 executions)** passed in 9.51 seconds, and **14/14
Python tests** passed. The final backing-store exception-safety refinement is
followed by focused/full tests and another 814-original-cell run: **99,308
original ray calls + 610 synthetic = 99,918**, plus 31 oblique/bounds controls,
32 lifetime cycles and nine malformed-input rejections. This is local geometry
and physics-ray evidence without texture queries, not 814 game-wrapper/GPU
acceptances. The rotation-locked seam A/B exercises 239 callbacks and three
corrected normals; its no-callback control produces 0.390354 lateral drift,
while the adjusted body preserves settled X/Y/Z within the unchanged .02
tolerance. Free-rotation behavior is explicitly not accepted.

Final real GPU reruns retain **eight mixed + 44 homogeneous byte-identical
captures**. All **35 validation summaries** are zero errors/zero warnings
through teardown. All **913 original files, three absent paths and 63+49 pack
entries** are unchanged by SHA256. Final Ninja has no work. Build/export/runtime
experiments and one focused command missing the DXC environment are retained;
final commands use complete runtime/DXC/validation paths. No tolerances, timeouts,
stress counts or budgets were relaxed, and no GPU window/focus behavior changed.
Automatic current CTest inventory/labels/status, validation, image/provenance
hash checks and limits are recorded in
`build/stage8-terrain-collision-foundation/summary.json`.

The preferred next integration direction is an engine-owned collision resource
cook/deleter whose geometry/shape dependency the renderer wrapper queries and
retains, without linking a second static collision copy into the renderer DLL.
That boundary, the production contact hook and coherent geometry/transform
updates still require implementation and lifecycle acceptance. M8 remains open;
accepted collision foundation is not another numbered substage to repeat.

## Terrain Game-Wrapper Resources (2026-10-16)

Historical resource-only acceptance; the production contact profile below
supersedes this section's former next-hook task.

`terrain_model_class` now queries a real terrain instance and an engine-owned
`terrain_collision_shape_class` resource (reserved ID 192, preserving existing
class IDs). The dependency is a real `bt_collision_shape` unmanaged resource,
not a raw factory result disguised as a resource_ptr. It owns the Opcode
snapshot, centered/Z-reflected Bullet BVH/backing/edge metadata and a retained
render instance. A non-resource facade interface exposes those dependencies
without a second unmanaged base. The renderer queries/retains the resource;
its DLL does not link the collision/physics archives. Registration is explicit,
idempotent for the same allocator/thread, and refuses another owner. Nonblocking
finalization keeps the real engine deleter alive while resources/queries remain.
The engine registers after resource initialization and drains before shutdown.

The accepted game profile is immutable, translation-only terrain. Sealing rejects
rotation/scale; non-noop sample or placement updates reject before publication,
including combined geometry/transform edits. No-op snapshots and renderer-only
culling hints remain valid. Ordinary terrain instances retain existing editor
edit behavior. Supporting live game collision edits requires an explicit recook
transaction, not silently mutating a render instance behind a static body.

Game cells now place renderer/Opcode/Bullet using the same authored transform
and local center, including nonzero Y translation and non-unit physical size.
Unload removes/destroys the collision object and rigid body before releasing
the wrapper; the non-owning Opcode instance remains owned by the dependency.
`bt_rigid_body` now destroys its native body and motion state before releasing
its retained shape. Static bodies do not request BVH local inertia. Tests use
the actual game-side rigid-body API, with static physics/collision APIs resolved
before engine imports, rather than adding a second copy to the renderer DLL.

CPU controls cover actual cook metadata/deleters, source identity, independent
instances, exact topology and authored anti-diagonal ray heights, Opcode/Bullet
placement, creation bytes and project fallback, missing/malformed/unsupported
inputs, no partial publication, retained wrapper/shape/body shutdown and pending
wrapper rejection. A separate query with a retained closed source verifies the
engine cook's own rejection. Creation queries on the current thread can execute
synchronously; the first test incorrectly treated one as pending. That failure
and the initial namespace/declaration/link-order failures are retained in logs.

Hidden/nonactivating GPU controls render the same authored terrain via an
ordinary instance and the game wrapper. More than half the frame must differ
from the empty-scene control, and 48 independent RGB checks must pass. Plain,
wrapper and no-op captures are byte-identical; queued removal restores the
byte-identical empty frame. Retained collision prevents premature cook teardown,
then releases/drains normally. Both CPU/GPU tests pass three consecutive runs.
Two real mixed-terrain cells (65x65, physical size 64) also pass wrapper query,
geometry/placement and rigid-body lifetime controls; a real over-budget cell
still fails at the unchanged 256 MiB gate. This is not real-wrapper GPU image
parity or ordinary threaded game/editor startup.

Acceptance: **99/99 CTest, including 52 GPU**, 545.65 seconds; **11 focused CPU
tests x3 (33 executions)**, 11.67 seconds; **two wrapper tests x3 (six executions)**,
3.33 seconds; **14/14 Python tests**. Collision foundation re-verification covers
all **814 original cells**, **99,918 ray calls** including five synthetic grids,
31 oblique/bounds controls, 32 lifetime cycles and nine malformed rejections.
Eight mixed and 44 homogeneous real GPU captures remain byte-identical to prior
acceptance. All **39 validation summaries** report zero errors/zero warnings
through teardown. All 913 original files, three absent paths and 63+49 pack
inputs are unchanged by SHA256. Final Ninja has no work. No tolerance, timeout,
budget, stress count or GPU window/focus behavior was relaxed.

Production Bullet contact-hook integration and seam acceptance of the agreed
moving-body profile remain the next terrain-wrapper work. Internal-edge metadata
alone does not install the hook; free-rotation sphere drift from foundation
experiments is not declared fixed. Arbitrary transforms, live collision edits,
complete collision query parity, ordinary threaded startup and M9 configurations/
failure injection/leak or performance measurements are not accepted here.
M8 remains open with one canonical checklist, not a new numbered substage.
Evidence and automatic inventory/status/validation/image/provenance checks:
`build/stage8-terrain-wrapper/summary.json` and `verify_acceptance.py` beside it.

## Terrain Production Contacts (2026-10-16)

The engine-owned terrain collision cook now installs/restores Bullet's contact
callback for its lifetime. It chains the prior callback first, preserves that
callback's material changes/return value and corrects only registered cooked
terrain meshes with the custom-material flag and valid single-submesh triangle
IDs. The mutex-protected registry does not own resources, and entries are erased
before native BVH destruction. Allocation failure while inserting a new entry
has a real shape/resource cleanup guard, not a dangling registry entry.
Actual game rigid bodies retaining class 192 automatically enable the flag;
unrelated shape resources and raw factory meshes are not automatically opted in.
The renderer DLL still does not link the physics/collision archives.

Both manifold orders are supported. For a triangle on side A, the hook reverses
the contact's geometric sides before edge adjustment, then publishes the normal
and reprojected point back on the original mesh side. Penetration depth and
unrelated manifold/material state are preserved. The integration follows the
system Bullet API and its upstream
[internal-edge implementation](https://github.com/bulletphysics/bullet3/blob/master/src/BulletCollision/CollisionDispatch/btInternalEdgeUtility.cpp)
and [manifold callback order](https://github.com/bulletphysics/bullet3/blob/master/src/BulletCollision/CollisionDispatch/btManifoldResult.cpp).
Monotonic atomic diagnostics report mesh contacts, corrected normals and swapped
contacts; this is lifecycle/behavior evidence, not performance measurement.

Registration/finalization requires quiescent physics callbacks. Resources,
pending queries and registered meshes prevent premature hook/cook teardown.
If another owner replaces the callback, registration/new collision queries and
finalization reject the displaced ownership; finalization never overwrites the
foreign callback. Once ownership is restored and dependencies drain, the prior
callback is restored exactly. This does not accept concurrent hook installation/
removal or replace ordinary threaded game/editor startup acceptance.

The bounded dynamic profile uses the actual cooked terrain dependency and the
actual game rigid-body API in a Bullet dynamics world: 5x5 flat grid, physical
size 9, local height -3 and authored translation (11,4,-7); radius .5, mass 1
sphere with rotation locked. Eight drop phases use 600 steps at 1/120 seconds.
Seven positive drops each exercise 239 production mesh contacts and three
corrected normals, preserving settled X/Y/Z within .02. The identical disabled-
flag negative drop retains the installed production callback but receives no
adjustment and drifts laterally by 0.390354. The test never substitutes a local
normal-correction callback. A prior observer only checks chaining/materials.

Two positive phases add 300 awake, frictionless steps at X velocity .5: measured
displacement 1.24998 versus analytical 1.25, with preserved height/lateral Z
and 363 production contacts per translation. Body reinsertion refreshes cached
friction, and deactivation is explicitly disabled for prescribed low-speed
motion. These are controlled translations, not general rolling/friction quality.
An additional drop uses the game terrain friction of 100. Contacts remain valid
after the renderer session closes with only the actual rigid body retaining the
shape/source. Four cook/runtime re-registration cycles restore the prior callback.

Direct dispatch controls independently check both manifold orders, local/world
point reprojection, unchanged distance, prior material/return value and six
unsupported cases: invalid part/index, out-of-range index, non-triangle wrapper,
disabled terrain flag and an unregistered BVH containing the same valid mesh and
edge metadata. Foreign callback replacement is tested without overwriting it;
ordinary wrapper shutdown also proves restoration of an initially null callback.

Acceptance: **100/100 CTest, including 52 GPU**, 331.84 seconds; **12 focused CPU
tests x3 (36 executions)**, 13.52 seconds; **three wrapper/contact/GPU tests x3
(nine executions)**, 5.22 seconds; **14/14 Python tests**. The real wrapper
regression again accepts two mixed cells and rejects the over-budget cell at
256 MiB. Collision foundation re-verification covers all **814 original cells**
and **99,918 ray calls**, including five synthetic grids. This is not production
contact acceptance on all original terrain cells.

Eight mixed and 44 homogeneous real GPU captures remain byte-identical. All
**39 validation summaries** report zero errors/zero warnings through teardown.
All **913 original files, three absent paths and 63+49 pack inputs** remain
unchanged by SHA256. Final Ninja has no work. Initial translation-fixture failure
(cached friction/low-speed sleeping) and the foreign-mesh fixture's u16-to-u32
index build error are retained. Indices are explicitly copied/widened; no
reinterpretation, tolerance/timeout/budget/stress relaxation or GPU window/focus
change is used to obtain passing tests.

The bounded terrain wrapper/contact item is complete. Freely rotating sphere
drift from foundation experiments is not declared fixed; other body profiles,
arbitrary transforms/live collision edits, complete collision query parity,
ordinary threaded startup and M9 failure/leak/performance/configuration checks
remain outside this acceptance. M8 stays open; the canonical next work is
particles scene/world/resources/submission/lifecycle, not another terrain
substage. Evidence and automatic status/labels/validation/contact/image/provenance
checks: `build/stage8-terrain-contacts/summary.json` and `verify_acceptance.py`.

## Particle Billboard Foundation (2026-10-16)

The first particle component copies the existing `particle::base_particle`
simulation output, either from a contiguous span or its existing intrusive
list. It does not implement another emitter/simulation engine. Source particles
must remain stable during capture; the returned immutable mesh retains no live
particle/list pointers. Bounded list traversal rejects a cycle/oversized list.
Existing `is_dead()` semantics are reused; dead, zero-area and transparent
particles are filtered. Invalid finite/range/options data rejects the complete
snapshot instead of publishing partial geometry.

Supported geometry is camera-facing square/rectangle billboards with full
extent sizes, radian rotation and linear straight-RGBA particle/instance tint.
Camera/emitter matrices must be proper rigid affine transforms. Local positions
receive the emitter transform; world-space positions do not. Camera right/up
axes come from the inverse view, and center depths are stably sorted back to
front within each batch. These snapshots are view-dependent: callers must pair
them with the captured camera and rebuild for another view. Ordering between
separate batches remains the caller's responsibility; cross-emitter sorting is
not accepted by this component proof.

The RHI renderer uploads immutable geometry and sampled RGBA8 2D texture
generations before recording. Its unlit shader uses explicit LOD0, linear tint
and texture alpha. Alpha/additive blend modes use opaque depth testing without
depth writes; culling is disabled for billboards. Texture sampling handles both
linear and sRGB sources. Only the active managed RHI backend is accepted, and
the renderer is noncopyable. All shaders, sampler, buffers and images have
explicit release/retirement paths; unused cached entries are collected.

Limits are 4096 source particles/batch, 64 batches, 8 MiB copied geometry and
64 MiB unique texture bytes/frame. Texture aliases are deduplicated; dimensions
up to 4096 and declared mip storage/layout are validated without duplicating
pixels during CPU preflight. The retained GPU cache permits at most two bounded
frame generations: 128 geometry entries and 128 image entries, 16 MiB geometry and 128 MiB
images before collect. Repeated prepare cannot grow that cache indefinitely.
Malformed/over-limit prepare rejects before GPU allocations.

The particle scene flag and `particle_world()` remain explicit-error gates.
Sixteen formerly silent particle play/stop/update/remove, slomo/render-mode,
preview/statistics and selection controls now also fail explicitly. Enabling
the scene flag before a real world/resource/facade path exists would not be an
implementation. Existing particle cook registration references an excluded
wrapper cook; the legacy instance cook also carries a raw world pointer. The
next integration must adapt those boundaries and retain real scene/world/cook
lifetimes while reusing the existing simulation.

CPU controls cover independent geometry/UV/tint, local/world and rotated-camera
placement, radian rotation, square/rectangle geometry, stable tied-depth order,
list/span equivalence and caller-independent copies. They cover maximum input/
index counts, cycle rejection, geometry/count/unique-texture limits with alias
controls and six actual engine-world capability gates: 27 negative controls.
Hidden/nonactivating GPU controls cover independent alpha/additive/texture-alpha
composition, linear/sRGB color, opaque occlusion and an out-of-depth-order
two-batch proof that transparency does not write depth. A rotated-camera 2x2
texture verifies UV orientation/filtering; the capture was visually inspected.
Repeated immutable draws are byte-identical. Translated camera/emitter placement,
eight resize/texture-generation cycles, malformed preflight, retained-cache limit
and complete live/retired resource drain are also checked. Each GPU run performs
507 independent RGB checks and saves seven distinct controls.

This is a component foundation, not scene/world/resources, particle simulation,
game/editor facade/preview integration, real particle assets or original image
parity. Locked axes, subUV, path/axis alignment, beam/trail/mesh/decal particles,
scale/shear/reflection, HDR colors/textures, BC textures, particle lighting/
shadows/soft particles and AA are not accepted. The particle item and M8 remain
open. Next work is scene-owned world/cooks and real instance lifetimes, then
copied queued play/stop/transform/visibility/pause/remove, scene submission/drain
and a real supported billboard proof. Accepted snapshot/RHI work is not another
numbered substage to repeat. Evidence: `build/stage8-particle-billboards`.

Acceptance: **102/102 CTest, including 53 GPU**, 496.41 seconds; **13 focused CPU
tests x3 (39 executions)**, 13.51 seconds; **two particle tests x3 (six
executions)**, 6.53 seconds; **14/14 Python tests**. Final commands follow the
UV/cache/depth-write and unique-texture budget refinements. All **40 validation
summaries** report zero errors/zero warnings through teardown. Existing real
regression again accepts two mixed terrain wrappers and rejects the unchanged
256 MiB budget negative; all **814 original collision cells / 99,918 ray calls**
pass. Eight mixed and 44 homogeneous GPU captures remain byte-identical. These
are regression proofs of existing terrain paths, not real particle acceptance.
All **913 original files, three absent paths and 63+49 pack inputs** are unchanged
by SHA256. Seven distinct particle controls have recorded hashes. Final Ninja
has no work. Initial CPU fixture owner/API mismatch, incorrectly timed cycle
injection and Windows near/far macro build failures are retained; final fixtures
call the actual engine world and inject the cycle after list insertion. No
tolerance, timeout, budget, stress count or GPU window/focus behavior was relaxed.
Automatic inventory/status/labels/contact/validation/image/provenance verification:
`build/stage8-particle-billboards/summary.json` and `verify_acceptance.py`.

## Existing Particle Simulation Lifetime (2026-10-16)

Integration preparation fixes two existing simulation hazards without adding a
second simulation engine. Material queries no longer capture an emitter's raw
`this`. Each query retains its own material cook data (`delete_in_cook=false`)
until callback destruction. A shared callback state serializes delivery against
emitter invalidation, and only the latest requested generation can publish.
Late/stale results are released without touching the emitter. A failed latest
query preserves the current material and does not allow an older success to
replace it. Failure uses the resource manager's nonasserting error path; this
does not make a missing first material a ready/renderable resource. Renderer
adapter setup/query submission exceptions invalidate callbacks and release an
already-created adapter before the constructor exits.

Repeated world play/add now preserves a single intrusive node and world-held
self reference. Stop/play clears the stop's no-more-create flag. These are
membership/resume fixes, not a promise to rewind particles or reset simulation
time on replay. Remove remains idempotent while the caller retains the instance.

The new CPU test links the actual particle library, instantiates its real
world/emitter/instance classes and uses the actual resource manager. Controlled
fixture material/instance cooks intentionally delay results and provide valid
resource creation/deleter metadata. Controls include distinct overlapping query
userdata, newest-first completion, latest failure followed by older success,
32 emitter destruction/late-success-or-failure cycles, adapter setup rejection,
100 repeated plays, one tick per instance, pause/visibility isolation,
stop/play continuation, active self-retention and remove/replay/resource drain.
No window, Vulkan device or parallel simulation is created by this test.

This is bounded simulation lifetime hardening, not production particle template
or instance cook acceptance. The legacy raw world pointer in the instance cook,
world/adapter retention, excluded template cook registration, bounded template
validation and the unfinished preview cook still need integration work.
Concurrent simulation mutations and ordinary threaded startup are not accepted;
the callback invalidation lock is not a lock for the entire simulation. The
scene flag and particle world/facade gates remain explicit errors. Next work is
production scene-owned world/resources/cooks, then queued lifecycle/submission
and a real supported billboard proof. M8 and its single Particles item stay open.
Evidence: `build/stage8-particle-lifetime`.

Acceptance: **103/103 CTest, including 53 GPU**, 356.32 seconds; **50 CPU
tests x3 (150 executions)**, 39.82 seconds; **three particle tests x3 (nine
executions)**, 3.00 seconds; **14/14 Python tests**. All **38 validation
summaries** report zero errors/zero warnings through teardown. The seven
billboard captures are byte-identical to the previous component acceptance.
All **913 original files and three absent paths** remain unchanged by SHA256.
No new real-particle asset or scene integration acceptance is claimed. Final
Ninja has no work. The initial test build's missing cook-class include was
fixed; no tolerance, timeout, budget or background-window behavior was relaxed.
Automatic inventory/status/validation/image/provenance verification:
`build/stage8-particle-lifetime/summary.json` and `verify_acceptance.py`.

## Particle World/Instance Cook Ownership (2026-10-16)

The production `particle_world_class` and `particle_system_instance_class` cooks
now register through `particle::initialize()` without linking the excluded
template-wrapper implementation or registering the unfinished preview cook.
Registration is idempotent on the owning thread, checks both class-ID owners
before installation, and can restart after drain. `try_finalize()` is
nonblocking: pending queries, retained resources/deleters, a foreign thread or
displaced registry owner leave both cooks installed. `finalize()` reports busy
ownership as an explicit error. Registration/finalization require quiescent
resource lifecycle calls; this is not concurrent registration support.

World creation requires an internal one-byte zero creation request plus typed
`world_configuration` userdata. The copied configuration retains a shared
renderer adapter and specifies 1..50000 particles. The adapter must not own its
world/scene. The cook owns one allocation containing the world and an aligned
fixed-size particle arena, accounts for the complete allocation, and bounds
runtime limits to that capacity. Raw engine pointers, file queries, missing
configuration, malformed creation bytes and out-of-range capacity fail before
world publication. The world retains its adapter through the last instance's
emitter/particle teardown.

Instance queries require typed `world_ptr` userdata, not a raw pointer or generic
unmanaged pointer. The async child callback retains that world; the created
instance also retains it, and retains the cooked template through the existing
LOD resource reference. Construction is unpublished until all emitters succeed;
partial construction destroys already-created emitters. The resource manager
installs creation/deleter metadata before the first intrusive instance pointer.
Missing/failed template dependencies, wrong resource types and closed worlds
finish with errors instead of assertions or dangling world references.

The trusted cooked-template boundary currently requires one LOD and 1..64
emitters with no child/event emitters. This topology check is not a safe parser
for legacy binary/config input. No production template source/raw-binary cook is
registered yet. The test substitutes controlled template/material dependency
cooks only; the world and instance cooks under test are the production ones.
Real particle template/material loading and billboard submission remain open.

`world::close()` is owner-thread-only and idempotent. The scene owner must call
it before dropping its last world handle: active world self-retention plus
instance-to-world retention otherwise forms an intentional play-lifetime cycle.
Close blocks new queries/operations, removes active instances and their particles,
and breaks that cycle. Caller-retained instances can then finish emitter teardown
against the still-retained arena/adapter. Removal remains allowed after close;
play/stop/transform/visibility/pause, tick and allocation reject closed worlds.
Null, wrong-type and foreign-world instances are rejected before list mutation.
Direct world preview methods now fail explicitly, matching the facade gates.
The lock/owner-thread checks do not make simulation concurrently mutable.

Controls exercise typed/raw/missing userdata, internal creation bytes, capacity
1/4/50000 and exhaustion/alignment, unsafe cook non-registration, missing/failed/
wrong/invalid-topology dependencies, partial emitter construction rollback,
null renderer-factory results and allocation without an initialized arena,
foreign-thread calls, foreign-world commands, pending close, instance-held
world/adapter lifetime, real seeded-particle teardown, active cycle drain,
registry displacement/recovery and four full re-registration cycles. Seeding
the existing allocator/list is a teardown fixture, not birth/simulation or real
particle-asset acceptance. Seven direct preview controls are exercised without
creating a Lua/editor session. No window or Vulkan device is created by this
new test; existing GPU tests keep their hidden/nonactivating behavior.

The single Particles item and M8 stay open. Next work is bounded production
template/material loading, scene-owned close-before-drop and migration of game/
facade userdata, then copied queued lifecycle, scene submission/drain and real
supported billboards. Accepted world/instance ownership, callback/replay repairs
and snapshot/RHI components are not new numbered substages to repeat.
Evidence: `build/stage8-particle-resources`.

Acceptance: **104/104 CTest, including 53 GPU**, 350.52 seconds; **51 CPU
tests x3 (153 executions)**, 37.25 seconds; **four particle tests x3 (12
executions)**, 3.24 seconds; **14/14 Python tests**. The new production-cook
test passes **54 negative controls**. All **38 validation summaries** report
zero errors/zero warnings through teardown. Seven billboard captures remain
byte-identical to the preceding component acceptance; **913 original files,
three absent paths and 63+49 pack inputs** remain unchanged by SHA256.
Final Ninja has no work. No new real-particle asset/scene acceptance is claimed.
The pre-refinement full run (104/104, 309.43 seconds) is retained separately.
Final runs follow null-factory/uninitialized-arena guards and a malformed-length
fixture backing-buffer correction. The initial CMake missing-test-source failure
and the fixture's internal-vs-public transform API mismatch are retained. No
tolerance, timeout, budget or background-window behavior was relaxed.
Automatic inventory/status/validation/image/provenance verification:
`build/stage8-particle-resources/summary.json` and `verify_acceptance.py`.

## Production Particle Materials (2026-10-16)

The active scene cook group now owns `material_effects_instance_class` for the
accepted billboard material profile. Previously the existing emitter requested
this unregistered class. Registration checks its class-ID owner before installing
any scene cooks; finalization checks its resource users and registry identity,
and preserves the group through pending queries and retained deleters. An emitter
also explicitly rejects a missing effects cook before submitting a query, cleaning
up its renderer adapter on constructor failure.

The effects request uses the existing caller-owned
`material_effects_instance_cook_data*` contract with `delete_in_cook=false` and
`particle_vertex_input_type`. The pointer must stay alive through its callback;
the existing emitter's per-query shared packet provides that lifetime. The cook
does not carry that raw userdata pointer into child callbacks. It accepts a
retained same-session material resource or queries the existing material cook by
logical name/path. Missing/null/wrong userdata, other vertex profiles, front-cull
requests, ownership-transfer flags and raw creation payloads fail explicitly.
Rejected requests do not adopt/free caller data. The legacy default back-cull
metadata and none are accepted, but this billboard profile is always two-sided,
not an independent request-level culling override.

Production material/texture dependencies reuse checked binary configs and the
existing `f_stage_unlit` asset profile. The typed effects resource retains its
material source and captures immutable linear tint, alpha/additive blend mode
and texture generation. Supported sources have unit UV tiling, no alpha test,
two-sided culling and default material sampler metadata; actual particle sampling
remains the agreed fixed linear clamp LOD0 policy. Custom samplers, UV/cull
variants, opaque/surface-lit materials and BC/HDR particle textures are rejected,
not silently approximated. Shared texture validation enforces the existing RGBA8
2D/mip/dimension/64 MiB limits before particle GPU upload.

RGBA8 TGA color/alpha and sRGB metadata pass through the production texture cook.
An explicitly untextured authored material gets an immutable white 1x1 sample;
missing enabled textures fail rather than falling back to white. Texture reload
does not mutate an existing particle material snapshot. Explicit effects recook
from the retained material captures the newly published texture generation.
Automatic material refresh on texture reload is not claimed.

CPU/disk controls use actual material/effects/texture cooks and the existing
emitter's real callbacks, with only its renderer adapter instrumented. They cover
failed-query material retention, eight delete-before-callback cycles, immutable
reload/recook generations, source retention, foreign registration/displacement,
closed/pending queries and complete cook/dependency drain. The test then draws
copied cooked snapshots after those resource cooks have finalized, demonstrating
resource-independent immutable data lifetime. Hidden/nonactivating GPU controls
perform 108 independent RGB checks for sRGB/alpha texture+tint composition,
two-particle alpha versus additive, reloaded/recooked color and empty background,
with four captures and complete live/retired GPU drain. The additive capture was
visually inspected. No GPU window/focus behavior was changed.

This is material-effects resource acceptance, not a particle template parser or
complete instance readiness. The existing instance constructor still starts
async material loads; the next template/instance integration must resolve the
accepted material dependencies before publishing a ready instance. Original
particle effect IDs/assets, production birth simulation, scene ownership/queued
submission and ordinary threaded game/editor startup remain open. The Particles
item and M8 stay open, and the scene flag/facade/preview gates remain errors.
Next work is bounded template loading and material-ready instance publication,
then scene ownership/lifecycle/submission and a real supported billboard proof.
Evidence: `build/stage8-particle-materials`.

Acceptance: **106/106 CTest, including 54 GPU**, 353.24 seconds; **52 CPU
tests x3 (156 executions)**, 38.61 seconds; **six particle tests x3 (18
executions)**, 6.75 seconds; **14/14 Python tests**. The new material test
passes **25 negative controls**, and GPU acceptance performs **108 independent
RGB checks/four distinct captures**. All **42 validation summaries** report
zero errors/zero warnings through teardown. Seven preceding billboard captures
remain byte-identical; **913 original files, three absent paths and 63+49 pack
inputs** remain unchanged by SHA256. Final Ninja has no work. Initial fixture
build errors (sampler enum spelling and const userdata pointer array) are
retained; final full/repeated runs follow their correction and all guards. No
tolerance, timeout, budget or background-window behavior was relaxed.
Automatic inventory/status/validation/image/provenance verification:
`build/stage8-particle-materials/summary.json` and `verify_acceptance.py`.

## Material-Ready Particle Instances (2026-10-17)

The production instance cook now resolves every emitter's material through the
existing effects cook before constructing or publishing the instance. One
aggregate callback owns completion, including partial failure. It retains the
world, parsed template and separate caller-owned material packets through all
child callbacks. The instance path requires the accepted visible camera-facing
square/rectangle billboard profile without locked axes or subUV, checks a bounded
action chain for cycles/type mismatch, and rejects unterminated material names.
An empty authored name still explicitly requests `default_particle`; a failed
named material does not fall back to it.

Only after all dependencies succeed are renderer emitters constructed, with the
exact retained ready handles. This constructor path does not launch another
material query. The existing direct emitter path and later explicit material
changes retain their generation-guarded asynchronous behavior. Partial adapter
allocation or material-publication failure destroys the constructed emitters and
releases dependencies before returning an error. Owner-thread world checks run
after loading, before each emitter and immediately before publication; even an
adapter callback closing the world while the last emitter is being constructed
cannot publish an instance. No second simulation implementation was introduced.

Resource controls delay the template and both material responses, complete
materials out of order, and prove that neither renderer emitters nor the parent
result appear prematurely. They inspect the retained userdata at delayed
completion, check exact handles/no duplicate material queries, exercise both
partial-failure orders, material-publication rollback, pending close, reentrant
close, missing effects registration and full dependency/adapter drain. Existing
arena, ownership, thread, allocation, registry and re-registration controls remain.

The disk integration uses production world/instance/material/effects/texture
cooks with only the parsed template and renderer adapter controlled. A successful
two-emitter instance has ready TGA/sRGB alpha and authored-constant additive
materials at its completion callback. Missing material, missing texture, opaque,
BC and unsupported culling each reject the entire instance without constructing
either renderer emitter. The GPU test now draws copied snapshots taken from this
ready instance, after its world/template/resource graph has drained, and keeps
the existing independent RGB oracles, four captures and GPU resource drain.
Windows remain hidden/nonactivating/input-transparent.

This closes the instance-readiness gap described in the preceding dated report,
not the production template loader. Its controlled parsed templates do not count
as disk particle loading, production birth simulation or real effect acceptance.
Bounded template parsing is next, followed by scene-owned close-before-drop,
typed game/facade userdata, queued lifecycle/submission and a real supported
billboard proof. M8 stays open; scene/facade/preview capability gates stay errors.
Evidence: `build/stage8-particle-ready`.

Acceptance: **106/106 CTest, including 54 GPU**, 270.64 seconds; **52 CPU
tests x3 (156 executions)**, 35.29 seconds; **six particle tests x3 (18
executions)**, 6.53 seconds; **14/14 Python tests**. World/instance controls now
include **66 negative controls**, and the material chain has **30 negative
controls** plus **108 independent GPU RGB checks**. All **42 validation
summaries** report zero errors/zero warnings through teardown. All seven prior
billboard and four material captures remain byte-identical despite drawing from
ready-instance snapshots. The alpha capture was also visually inspected.
**913 original files, three absent paths and 63+49 pack inputs** remain unchanged
by SHA256. Final Ninja has no work. Initial build corrections (platform-pointer
deduction and test fixture helper/include) preceded the final full/repeated runs
and reentrant-close guard. No tolerance, timeout, budget or background-window
behavior was relaxed. Automatic inventory/status/validation/image/provenance
verification: `build/stage8-particle-ready/summary.json` and
`verify_acceptance.py`.

## Bounded Particle Templates (2026-10-17)

`particle::initialize()` now installs the production template cook together with
the owned world/instance cooks. The template class accepts logical names without
userdata or creation payloads, reading only
`resources.converted/particles/<name>.particle`. It does not execute source Lua,
load serialized objects/vtables or write/overwrite a cooked cache. The old raw
binary/source and unfinished preview cooks remain unregistered. Foreign class
IDs are checked before installation; pending queries, retained templates/worlds/
instances, foreign threads and displaced registry owners prevent finalization
without partially removing the other cooks. Fixture-only resource tests register
the world/instance component explicitly; production initialization is tested with
the actual bounded template cook.

The existing checked-config parser moved from the renderer into core as
`xray/configs_checked_binary.h`. The Vulkan header aliases the same implementation;
there is no second parser or renderer/particle archive dependency. Its existing
1 MiB/depth-32/4096-node preflight protects fix-up of the current Windows x64
binary-config layout. Runtime template parsing validates all descriptors before
constructing simulation arrays/actions/curves. File bytes are still read by the
ordinary managed raw-resource cook before parser preflight; this is not a new
raw-I/O allocation quota. Invalid logical paths are checked before assignment to
the resource manager's virtual path, returning a failed query instead of a VFS
assertion. Invalid config references, types and unknown fields fail explicitly.
Preflight key uniqueness uses views into the still-pinned input, avoiding copied
key amplification when many keys share long overlapping source strings; a
dedicated overlapping-key fixture also reaches strict schema rejection.

The version-1 root contains exactly `version = 1` and an indexed `emitters` table.
Every emitter is a named table with these required fields:

```text
material: logical material path, 1..127 bytes
alignment: "square" or "rectangle"
world_space: boolean
max_particles: integer 1..4096, total <= 50000/template
duration: float .001..3600 seconds, continuous looping only
rate: float 0..4096 particles/second
lifetime: float > .001 and <= 60 seconds, rate*lifetime <= 4094
velocity: float3, each component in [-1000,1000]
size: float3 full extents in [.001,1000], square requires X == Y
color: float4 normalized linear RGBA
rotation: float radians in [-2*pi,2*pi]
```

There is one LOD and 1..64 emitters. No optional/missing fields silently become
defaults. Delays, bursts, finite emitter loops, events/children, arbitrary curves,
domains, inheritance, subUV/movies, locked axes and other particle data types are
not part of this schema. Original editor `.particle` graphs and old raw caches do
not match it. Real-source projection/provenance and real effect acceptance remain
open, rather than discarding unsupported original properties during loading.
Lifetime must strictly exceed `epsilon_3`: the existing simulation treats smaller
durations as immortal. The exact threshold is rejected and its next float value
is checked through actual birth/expiry, without changing simulation semantics.
Emitter construction initializes its rate from the existing curve at time zero,
instead of starting every emitter at hardcoded rate=2. First-tick birth/motion
oracles run without a warm-up tick; all 64 zero-rate emitters are also played and
checked for absence of phantom births on their first tick.

An owning template resource owns all emitter/action arrays and constant curves.
It configures the existing billboard and initial color/velocity/size/rotation
actions plus the existing rate/lifetime curves. No new simulation engine is used.
Curve points now start initialized and reserve value-initializes points before
range calculation, allowing safe partial-construction cleanup and constant setup.
Instance queries retain the template through all ready material dependencies and
use the previously accepted atomic publication/rollback path.

The new CPU/disk test runs production template/world/instance/material/effects/
texture cooks with only a renderer adapter instrumented. It covers malformed and
oversized configs, source/raw-cache rejection, strict fields/numeric/path bounds,
maximum emitter/template capacities, 64 ready emitter construction, TGA/sRGB and
alpha/additive dependencies, existing birth/motion/pause/stop/expiry, local-space
rotated rectangles, retained/pending/thread/registry guards and four restart
cycles. The hidden GPU test draws four copied birth/motion/overlap/empty snapshots
after the resource graph drains, with independent position/blending RGB oracles
and complete live/retired GPU drain. This is a bounded synthetic cooked profile,
not original effect parity, scene ownership/queued submission or ordinary
threaded game/editor startup. Next is scene-owned close-before-drop and typed
game/facade userdata, then queued lifecycle/submission and real supported assets.
M8 remains open and scene/facade/preview gates remain explicit errors.
Evidence: `build/stage8-particle-templates`.

Acceptance: two complete **108/108 CTest runs, including 55 GPU** passed across
the parser extraction/key-view changes (427.12 and 574.13 seconds). After that
full run, the particle-only lifetime threshold/initial-rate fixes relinked only
the four particle test executables; core, renderer and other binaries stayed
unchanged. Final coverage then passed **53 CPU tests x3 (159 executions)**,
45.63 seconds, and **eight particle tests x3 (24 executions)**, 15.21 seconds,
covering every affected executable, including all affected GPU tests. **14/14
Python tests** also passed. The template profile has **59 negative controls**,
**2904 independent GPU RGB checks** and four distinct phase captures. All **46
validation summaries** report zero errors/zero warnings through teardown. Seven
prior billboard and four material captures remain byte-identical; all four new
simulation captures are byte-identical between full/repeated acceptance despite
removing the warm-up tick. The motion capture was visually inspected. **913
original files, three absent paths and 63+49 pack inputs** remain unchanged by
SHA256; final Ninja has no work.

Initial fixture build/transform mistakes and the pre-cook VFS assertion are
retained with logs/backtraces. No tolerance, timeout, budget or window behavior
was relaxed. One final integrated GPU pass took **117.967 seconds of its existing
120-second timeout**; that timing margin is not claimed fixed or performance
acceptance. Profiling/stability remains M9. Automatic inventory, phase/affected
binary coverage, validation, image and provenance verification:
`build/stage8-particle-templates/summary.json` and `verify_acceptance.py`.

## Scene-Owned Particle Lifecycle (2026-10-17)

The scene cook is now a translate cook. A particle-enabled internal scene
creation request queries a separate cooked `particle_world_class` dependency
with a retained, typed configuration and a renderer-owned lifecycle adapter.
The existing external particle module owns world/instance/template cooks;
the renderer neither registers another set nor links the particle archive into
its DLL. Missing cooks, wrong configuration/payload, a closed session or a
foreign creation thread fail before publication. Plain scene/view/output
resource contracts remain covered by their regression tests.

Each scene owns its world. Destroying the scene closes that world before its
handle is released; closing a renderer session closes all its scene worlds
before deferred scene/instance/material deletion. Close runs on the session
owner thread, is idempotent, and performs world cleanup outside the registry
mutex. Active particle self-retention and live particles are removed even when
external instances or query userdata still retain the world. Other scene worlds
are unaffected by dropping one scene. The adapter holds only a weak session
owner, accepts same-session typed ready billboard materials and allows emitter
destruction after session close. This avoids a scene/world/adapter ownership
cycle.

`scene_renderer::particle_world_handle()` returns a retained `particle::world_ptr`
through the real engine/facade path. The game object instance query now copies
that exact type into userdata instead of taking an address from the old raw
world API. Copying a handle is not permission to mutate simulation on another
thread. Particle scene creation/close and the existing simulation still require
their owner thread; ordinary threaded game/editor startup is not accepted.

This is lifecycle integration, not scene rendering. Particle-enabled scenes can
be cooked only with the external cooks installed, but drawing them still fails
explicitly. Play/stop/update/remove facade execution, the old raw world API,
previews, modes and slomo remain explicit-error gates. The lifecycle adapter
does not submit geometry. Next work is copied queued commands, scene snapshot/
submission/drain and supported real-effect acceptance. The one canonical M8
checklist was updated without introducing another numbered substage.

The new production CPU/disk test covers three restart cycles, two independent
scene worlds, ready materials, existing first-tick birth, typed/copied userdata,
raw-pointer rejection, wrong material/profile, foreign instance ownership,
foreign-thread creation/close, close-before-drop, pending queries and full cook
drain. Its hidden GPU variant also exercises the real game/facade handle,
explicit draw/command gates and renderer shutdown with retained handles and a
pending instance. These are lifecycle/validation controls, not new particle
pixel proof. Evidence is in `build/stage8-particle-scene`.

The first shared-scene regression run passed 109/110 tests but the previously
near-timeout integrated M7 case exceeded 120 seconds (120.29). Capture timestamps
localized the large cost to its three 8x MSAA profiles; renderer performance is
not claimed fixed. CTest now partitions the same 12 AA combinations into the
1x/2x/4x matrix and three serial 8x cases. Each retains 64 convergence frames,
32 same-phase stability frames, independent RGB oracles, movement, split-view,
eight reload/recook/resize cycles and teardown checks. Each keeps the existing
120-second limit. The full `--integrated-m7` CLI remains available; unsupported
8x attachment profiles use an explicit CTest skip, not silent lower sampling.
Automatic tests remain hidden/nonactivating/input-transparent. This is a test
harness change, not another M7/M8 functional substage or M9 performance acceptance.

Final acceptance: **113/113 CTest, including 59 GPU**, 303.77 seconds;
**54 CPU tests x3 (162 executions)**, 37.87 seconds; **10 particle tests x3
(30 executions)**, 12.92 seconds; **14 Python tests**. The scene lifecycle test
has **45 CPU / 51 GPU negative controls**. All **56 validation summaries**
report zero errors/zero warnings through teardown. Seven prior billboard,
four material and four simulation captures remain byte-identical. All 12 M7
AA captures also remain byte-identical across partitioning; final case times
were 18.37/10.55/10.52/10.50 seconds. The original long all-profile process is
still available for M9 profiling/stress, not claimed optimized here.

SHA256 confirms **913 original files, three absent paths and 63+49 pack
inputs** are unchanged. Final Ninja has no work. The automatic verifier also
checks complete test inventory/coverage, no skipped tests on this GPU, all 12
AA combinations, retained initial failure evidence and absence of duplicate
particle cook definitions in the renderer DLL. Initial fixture compile/assert
failures and the first integrated timeout are retained in the proof directory.
No production assertion, pixel tolerance, frame count, timeout, budget or
background-window behavior was relaxed. M8 remains open; see
`build/stage8-particle-scene/summary.json` and `verify_acceptance.py`.


## SDK-5 renderer integration

The earlier M8 gate counts above are historical checkpoint statements. SDK-5
adds typed editor view/output leases, selected-mesh masks and silhouette
composition, native gizmo geometry/picking/drag, all 21 viewport modes, source
texture reload and atomic model edit batches. `editor::renderer::select_model`
now selects a scene-owned instance through the real Vulkan runtime; the legacy
global selection-parameter/render-state interfaces remain guarded.

`editor_game_world` consumes an independent authored snapshot on the actual
logic producer and submits game-channel frames to the leased SDK main output.
Editor camera views keep their original scene. Focus pause, explicit pause/step,
return to editing and active-runtime teardown are covered independently of M8.
The runtime supports the restored model/light kinds and rejects unresolved or
unsupported domain objects. Asset authoring and other domain runtimes retain
SDK-6 through SDK-11 acceptance.

Current scope and the final Debug/Release receipt are maintained in
[SDK-5](../../editor/sdk_ui/sdk5/README.md); its runner preserves M8 property/legacy
readbacks and SDK-4 authored transforms as regression controls.
