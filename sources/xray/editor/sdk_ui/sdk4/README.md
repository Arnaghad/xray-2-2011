# SDK-4 — basic scene editing

Реалізація та фінальні докази: [acceptance.md](acceptance.md) і
[acceptance.json](acceptance.json). Native authored scenes, selection/picking,
translation/rotation/scale, coordinate spaces, snap, preview, dirty markers та
save/reopen використовують спільний `WindowIde.Commands`.

`CommandEngine` зіставлено з original `command_engine.{cpp,h,inline.h}`,
`command_transaction.cpp` і `command_apply_control_transform.cpp`. Commands
утримують operation-specific before/after data; whole-scene history snapshots не
використовуються. Native candidate scene — тимчасовий ресурс атомарного commit,
а не окрема undo history.

На native side викликаються existing `transform_control_*::one_step_execute`
і `calculate`. C# transform placeholders залишаються поза build. Translation
враховує local basis; native one-step scaling обмежує вибрану axis. Pivot,
mirrored scale і original Euler convention перевірено окремо. Mesh picking та
vertex/edge/face snap працюють із cooked geometry.

Версійний scene bridge готує незалежні instances зі shared immutable asset data.
History/cursor публікуються після прийняття scene. Commit/undo/redo/preview failure
компенсує managed state; failed transitions зберігають поточну session. Persistent
GUID ordering зберігає draw order після reopen. Recovery чекає native cook readiness;
pending preview drain дозволяє projection update і scene detach до HWND teardown.

Поточний scope і відокремлені SDK-5/domain dependencies наведені в acceptance.
Повні gizmo geometry/draw/axis picking, navigation, overlays/render modes, preview
services і оптимізація великих scene належать SDK-5. Specialized tool objects та
missing models мають selectable locators; terrain/root scene залишаються protected.

## Історичні checkpoints

- [Command foundation](command-foundation.json): 12 compatibility groups; original
  command contracts, nested transactions, redo preservation, saved markers, previews,
  bounded history та спільна native property history.
- [Project editing](project-commands.md) / [JSON](project-commands.json): 13 groups;
  create/delete/duplicate, typed clipboard, grouping/reparenting, properties,
  visibility, tree/menu/text input і project lifecycle. На момент цього checkpoint
  загальні native scene bindings ще не були прийняті.
- Фінальний evidence root: `build/sdk4-native-20261110/`; 14 groups додають atomic
  scene-participant failures, session safety та deferred recovery. GPU scenarios
  окремо перевіряють authored scenes, попередній property slice і legacy host.

Історичні JSON/source hashes не перераховуються за новими implementation sources.
