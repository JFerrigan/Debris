# Interactive V2 physics playground

Planning checkpoint: 2026-11-05 UTC. **Plan only; playground implementation has not started.** The user stopped gameplay integration and requested a separate place to manipulate the new physics with mouse and keyboard. This is the current bounded deliverable. Do not resume Debris gameplay integration after writing this plan.

## 1. What we will build

A separate 2D sandbox application opens directly into a simple physics world. You can select and pull objects, push or spin them with keys, throw them, add grains or rigid shapes, erase objects, place fixed obstacles, pause, and advance one tick at a time. Objects visibly rotate and exchange momentum according to their mass and inertia. A rejected physics step leaves the last accepted world visible and explains what failed.

Use the existing GPU V2 solver, not Unity Rigidbody2D or the old Jacobi solver. Keep the world visually simple: colored squares, rectangles, a grid, selection outlines and a small tool panel. No ship, asteroid generation, fuel, cargo menus, game saves or travel is required to test contact physics.

Build a dedicated scene and application at `Builds/PhysicsPlayground/Debris Physics Playground.app`. Do not overwrite `Builds/Debris.app`. Use one manager, camera and UI; never one GameObject per grain or contact. Opening the playground must not initialize `Showcase`, `MatterSession` or the game's persistence system.

**First usable target:** a small bounded arena, one heavy movable rectangle, a few loose unit-square grains, anchored walls, mouse dragging, keyboard forces, spawn/delete, and pause/single-step. Start paused so the world can be inspected before it moves. A separate free-space preset allows momentum experiments without wall reactions.

## 2. What already exists, and what does not

| Existing source | Reuse | Qualification still needed |
|---|---|---|
| [CoupledTickStepper](../Assets/Debris/Simulation/Runtime/CoupledContacts/CoupledTickStepper.cs) | Complete fixed-topology tick recording: force/torque integration, velocity solve, bounded prediction, position correction, final facts and cache publication | Current integrated GPU execution, shader bindings, numerical behavior and recording cost |
| [CoupledTickState](../Assets/Debris/Simulation/Runtime/CoupledContacts/CoupledTickState.cs) and [transaction](../Assets/Debris/Simulation/Runtime/CoupledContacts/CoupledTickTransaction.cs) | Committed/working state, publication, complete rollback, FIFO completion slots | Full tick and failure cases on the GPU; lifecycle for playground edits |
| [CoupledContactOperator](../Assets/Debris/Simulation/Runtime/CoupledContacts/CoupledContactOperator.cs) | Generated convex/union contacts, finite endpoint reactions and coupled products | Final geometry coverage and full arena admission with the session |
| [Endpoint identities](../Assets/Debris/Simulation/Runtime/CoupledContacts/CoupledEndpointIdentityTable.cs) | Full identities, slot generations, physical revisions | World reconstruction and stale input/readback rejection |
| [Arena](../Assets/Debris/Simulation/Runtime/CoupledContacts/CoupledContactArena.cs) | Named allocations and 128 MiB explicit GPU allocation limit | Account for picking, force inputs, rendering and edit scratch as well as solving |
| [Integrated tick tests](../Assets/Debris/Simulation/Tests/Editor/CoupledTickStepperTests.cs) | Forced motion, speed rejection, page failure and rollback definitions | These tests compile but have not run |

The complete contact runtime and eight focused fixtures compile directly against pinned Unity assemblies. That is C# evidence only. The latest Unity attempt exited 198 before compilation/tests because no valid Editor license was found; see [qualification evidence](evidence/B3R-v2-tick-qualification.md). Restore the pinned Editor license before runtime work. Do not describe the current solver as physically accepted or performant.

Missing playground pieces are the standalone scene/build, runtime owner, shape definitions, renderer, picking, external-force tools, edit transactions, UI and interaction tests. The interrupted gameplay import/session/force-shader files were removed; they are not a usable starting implementation.

## 3. Tools and controls

Use a visible toolbar with labels and shortcuts. Show the active tool, selected object, brush size, placement preview and whether simulation is running. Tooltips and buttons provide every action without requiring memorized shortcuts. Keyboard shortcuts fire only when a text field is not focused. UI clicks never also act on the world.

| Input | Action | Behavior |
|---|---|---|
| `1` / Select tool | Select, grab and pull | Left click selects. Hold left mouse on a movable object to pull at the clicked local point. Release stops applying the pull; acquired velocity/spin remains |
| `2` / Grain brush | Add loose matter | Click places a unit square. Hold paints a bounded batch on a unit-cell lattice inside the brush. Preview occupied cells in red; never spawn overlapping grains |
| `3` / Body tool | Add a rigid rectangle | Drag out a rectangle, then release to place it if clear. Inspector sets density, angle and initial velocity/spin; preview stays separate from authoritative state |
| `4` / Eraser | Remove objects | Click removes the picked object. A grain brush removes whole grains in its radius. Rigid bodies are removed as whole bodies in the first version |
| `5` / Push tool | Apply a force at a point | Drag to choose direction and strength; show an arrow. On release, apply the requested impulse over one fixed tick as an accounted external force/torque |
| `6` / Anchor tool | Pin or unpin a body | Explicit edit changes mobility. Anchors are visually hatched; they remain selectable but cannot be pulled or driven |
| `W A S D` | Drive the selected movable body | Apply bounded world-axis force; a panel toggle can later enable body-local axes |
| `Q / E` | Spin the selected movable body | Apply bounded negative/positive torque, not direct angle changes |
| `Delete` / `Backspace` | Delete selected object | Same transactional path as the eraser; ignore while editing text |
| `Space` | Pause/resume | Resume is disabled after a solver fault until the world is rebuilt or reset |
| `.` / Step button | Advance one physics tick | Exactly one accepted or rejected 1/60-second attempt; no hidden catch-up ticks |
| Middle drag / `Alt` + left drag | Pan camera | Camera control takes precedence over physics tools |
| Wheel / trackpad scroll | Zoom about pointer | Keep the same world point under the cursor; do not alter the simulation |
| Right click / `Esc` | Cancel a preview or release grab | Does not delete an object or change its velocity |
| Reset button / `R` | Restore current preset | Fence outstanding work and reconstruct the named initial world |
| Clear button | Empty the world | Explicit edit; retain configurable arena walls unless “remove walls” is checked |

Brush radius, rigid size, force strength and density have labeled controls. Do not overload the scroll wheel with tool settings. Releasing focus or closing a panel cancels held forces and grabs. Opening a menu cannot leave thrust stuck on.

If Clear also removes the walls, represent a genuinely empty world without allocating a solver owner until the first object is placed. The current tick state requires at least one endpoint. Do not add an invisible dummy grain or count an empty world as a successful physical tick.

## 4. Physics behavior of manipulation

### Selection and GPU picking

Run a compact GPU picking query against committed endpoint poses and the same shape definitions used for collision geometry. Inverse-transform the pointer into each candidate shape's local coordinates. Report stable object identity, world generation, accepted tick, local grab point and mobility in a small asynchronous result. Never read the full world every mouse move.

For coincident boundaries, choose the nearest visible hit with a stable identity tie break. Provide an optional anchored-object filter so walls do not block selecting loose objects. Render an immediate cursor indicator while the asynchronous pick is pending; start manipulation only after a matching result arrives.

Every pick and selection carries the world's generation. Discard results from a deleted/rebuilt world and selections whose identity no longer resolves. A late callback must not select a new object that reused the old slot.

### Pulling, thrust and rotation

The mouse does not teleport a live object or turn it kinematic. Store the clicked local anchor. Once per physics tick, a GPU command derives its world offset `r`, point position and surface velocity from committed center/angle/velocity/spin. A bounded proportional/damping controller computes the pull force:

`F = clampMagnitude(kp * (cursor - grabPoint) - kd * surfaceVelocity, maximumForce)`

Apply `F` to the endpoint and `cross(r, F)` as torque about its COM. Sampling once per fixed tick produces one immutable world-force/torque buffer for that tick. This is an external manipulation tool, not contact compliance or a modification to the solver's contact equations. Off-center pulling should rotate the object; center pulling should not introduce artificial torque.

Keyboard forces and the mouse controller combine into that same buffer. Body-local driving, if enabled, rotates the input using the authoritative GPU angle. Anchored endpoints receive no motion-producing command. Releasing the mouse or keys zeros the tool contribution on the next tick.

The push tool converts an impulse `J` into `F = J / dt` and angular impulse into torque over one tick. Apply it exactly once to a matching world generation. Reject or limit the command before submission if it exceeds the tool's published force budget; do not clamp solver velocities afterward. Schedule selection and prediction retain their existing rejection rules.

The existing physical accounting must include all submitted tool forces/torques and their discrete work. If a tool force is rejected with the tick, do not mark it as consumed or advance the displayed accepted state. Anchor reaction is reported separately when walls participate.

### Gravity and timing

Begin with gravity off. Add a gravity toggle and vector only after external-force accounting passes: submit `mass * gravity` for each dynamic endpoint through the same force buffer. Gravity is not a position adjustment. Controlled tests include its momentum/work in expected results.

Keep physics dt at 1/60 s. Slow motion changes how often ticks are requested, not their dt or solver tolerances. Initially allow one outstanding tick, coalesce pointer motion, and cap catch-up work so a slow solver cannot enqueue seconds of stale input. Display actual simulation speed if it cannot keep pace with wall time.

## 5. Spawning, removing and editing without corrupting state

The existing tick state fixes grain/endpoint counts and topology at construction. **Do not implement add/remove by overwriting its live buffers or pretending inactive records are physical objects.** The initial playground uses a stopped-world reconstruction transaction. A later in-place allocator is unnecessary for the first interactive version.

For each edit batch:

1. Queue the edit with a world generation and command ID. Stop new tick submissions and cancel pending manipulation of affected objects.
2. Wait for the current tick's completion/readbacks. Capture the accepted authoritative motion, materials, identities, physical parameters and mobility in one bounded edit-time snapshot. Retain CPU shape definitions and a complete recovery description. Never use stale camera/UI poses as authoritative input.
3. Construct a CPU proposal: add/delete objects, change shape, density or mobility, or relocate an object while paused. Validate finite values, IDs, supported shape sizes, page bounds, occupied geometry and allocation arithmetic. Compute changed masses and inertias from actual shape data.
4. Validate proposed geometry using the same independent acceptance rules, including whole rotated shapes and union containment where applicable. Existing valid contacts within their accepted tolerances remain valid; new placements require a clear gap. A failed proposal leaves the accepted world intact.
5. Admit the complete proposed GPU allocation footprint before retiring old resources. Retain only the bounded CPU recovery snapshot and a budgeted frozen display while replacing GPU resources. Do not allocate a second full solver arena alongside the first.
6. Dispose the old drained owner; construct the proposed state, identity bindings, geometry graph, transaction and stepper cold. If GPU construction/validation fails, dispose every partial allocation and reconstruct the accepted world from the recovery snapshot. If device/resource failure also prevents recovery, remain stopped with an explicit error and retained snapshot; never claim an active rollback succeeded.
7. Publish one new world generation only after success. Rebind renderer/picking, resolve selection by stable identity, clear the grab and resume if the user was running before the edit. Only then update counts and the edit history.

Use small explicit command-time readbacks here; they are allowed at this edit fence, not in the normal solver loop. This approach may briefly display “Applying edit…” and does not promise seamless spraying at thousands of grains. Batch brush strokes at a bounded cadence, cap each batch, and reject further input with a clear capacity message instead of allocating without limit.

Important edit rules:

- Spawning never evicts another grain or relaxes penetration limits. A partial brush result shows the number placed and why other sites were blocked. Proposed grains must also be disjoint from one another.
- Deleting is an explicit sandbox command, not simulation loss or overflow handling. Count the removed mass/momentum in the edit ledger. Conservation checks restart from the newly accepted world's baseline rather than comparing across an intentional edit.
- Anchoring is an explicit policy change. Pinning zeros motion as part of the recorded user edit; retain the body's physical mass/inertia separately. Unpinning restores finite inverse mass/inertia with zero velocity/spin, rather than resurrecting hidden old velocity.
- Rectangle density determines mass from area; COM is its local center and inertia is `mass * (width² + height²) / 12`. A unit square grain uses density as mass and `mass / 6` inertia.
- Rigid bodies remain material geometry, not a cluster of independently overlapping grains. Later cut/paint tools must recompute shape, COM and inertia through this same edit fence.
- An object's full identity survives reconstruction. New objects receive new identities; stale slots never identify an object. World generation invalidates all old callbacks/cache keys, and rebuilt solves start cold. Keep an explicit identity-to-current-slot map.
- The current source packs live grains before bodies. Count changes can relocate body slots only during this stopped reconstruction; all bindings and render indexes must be rebuilt together. Do not claim this implements the later production sparse-slot/topology allocator.
- A new owner resets solver-local transaction history. Keep world revision and playground command/tick history separately, so a reset/rebuild never looks like an old completion from the previous owner.

## 6. Shapes, world bounds and limits

Ship the first sandbox with unit-square grains and convex rigid rectangles, including fixed rectangles for walls/platforms. Those use existing convex manifold generation and avoid making unqualified irregular-union handling a dependency for basic interaction. Show a grid with fixed cell units and a visible arena boundary.

All dynamics use the same finite-mass coupled operator. Anchors are explicit endpoints; many dynamic contacts with one heavy body must not split that body's mass or make it static. Do not introduce a Unity physics fallback.

Keep the existing [-512,512) grain-center page and 128 MiB explicit GPU allocation ceiling. Offer a smaller central workspace, with walls sized to contain the displayed test area. Preflight shape extents, broad-phase coverage, angular radius and initial surface speed. A collider is not supported merely because its COM fits on screen.

Start with a **proposed soft cap of 256 grains and 16 non-grain endpoints including walls**, then lower it if measured admission/interaction cost requires it. The first preset uses substantially fewer. These are playground UX targets, not demonstrated capacity or a change to the solver contract. Fixed-size solver pools may dominate allocation even for a tiny world; measure that before promising any population.

Retain existing hard pool bounds: at most 8,192 grains/8,210 endpoints, 16,384 indexed primitives, 4,096 union patches/exterior faces where applicable, 524,288 candidate pairs, 65,536 manifolds and 262,144 velocity rows. The session must check every applicable bound, including long-proxy and spatial-reference limits, rather than exposing just a grain counter.

Defer circles, arbitrary polygons, articulated joints, automatic fracture, fluid forces and huge populations. A drawn circle would otherwise misrepresent square collision geometry. Add compound material shapes only after exterior-face/seam/concavity/containment tests pass for them; then reuse `CoupledUnionGeometry` and `CoupledExteriorFaces`.

## 7. Rendering, UI and failure handling

Render committed GPU buffers directly with instancing/procedural drawing. Use a dedicated simple shader and camera; do not depend on the game's field textures, starter ship or `MatterView`. Grains show orientation with a small edge mark. Rigid bodies have a COM marker; selected objects show local axes and the grab point. Anchors have distinct color/hatching, not just an inspector flag.

The normal panel contains tools, run/pause/step/reset, preset selection and selected-object properties. An expandable diagnostics panel shows accepted/attempted tick, world revision, body/grain count, explicit allocation bytes, solve profile, selected substeps, rejection reason and measured CPU/GPU timing where available. Missing timing is “unmeasured.” Allocation and solve limits have readable explanations.

On a physics rejection, pause once and keep drawing the retained committed state. Show the first failing stage and physical fault bits, with “Reset preset,” “Edit paused world” and “Export failure.” Repeated renders must not overwrite the first failure or spam logs. A transaction's persistent first-failure state means merely toggling Run cannot recover it; a validated edit/reset constructs a fresh owner.

Capture source/build identity, configuration and the last accepted world plus the attempted command in a failure export. A solver freeze, missing shader or constructor exception must still leave camera/UI and an error message visible. The dedicated scene must explicitly create/verify its camera and render setup; a black window is a failed startup check.

## 8. Presets and scene files

Provide small presets that teach and exercise the solver:

| Preset | Interaction | What it tests |
|---|---|---|
| Empty workbench | Spawn, select, delete and pin shapes | Editing and identity lifecycle |
| Heavy block and loose grains | Drive a heavy block into isolated grains | Mass-dependent response without pixel hard stops |
| Off-center impact | Throw a grain/rectangle at a corner | Torque, spin and inertia |
| Stack and floor | Toggle gravity and pull supports | Two-point support, friction and correction |
| Packed pocket | Push a finite wall into a small dense pack | Coupled reactions, rollback and budget reporting |
| Free-space pair | Push two free bodies with walls/gravity off | Linear/angular momentum and energy diagnostics |

Add playground-specific scene export/import after basic editing works. Store versioned shape definitions, identities/material densities, accepted poses/velocities/spins, physical mass/mobility, page metadata, gravity, profile and tool-independent settings. Never serialize Krylov scratch, contact manifolds or warm-start cache. This is a sandbox format, not a Debris save migration.

Save/export only after a fence. Write atomically and retain the old file on failure. Loading validates the entire proposal before replacing the world; corrupted/future-version/overlapping files fail visibly. A roundtrip should retain authoritative values, and continuation starts cold. Reset remains available even without scene files.

## 9. Runtime structure and data flow

Use a small concrete implementation, not a general physics plugin framework. Proposed responsibilities:

| New component | Responsibility |
|---|---|
| `PhysicsPlaygroundBootstrap` / dedicated scene | Camera, UI, renderer, input ownership and visible startup errors |
| `CoupledPlaygroundSession` | Own arena/state/graph/transaction/stepper; submit one tick; collect compact completions; fence and dispose |
| `PlaygroundWorldDefinition` | CPU shape/material/identity/mobility definitions; validated presets and edit proposals |
| `PlaygroundEditTransaction` | Capture accepted state, preflight/edit/reconstruct/recover and publish one world revision |
| `CoupledPlaygroundInput.compute` | GPU pick and sampled force/torque generation from committed poses |
| `CoupledPlaygroundView` / shader | Direct GPU rendering, selection/force overlays and frozen accepted frame during edits |
| `PhysicsPlaygroundBuild` | Dedicated build settings/output; no modification to the existing game build target |

Put solver/session logic in the existing Simulation assembly and presentation in Presentation. A body is a buffer record, not a scene component. Keep the new entry point isolated; reuse mathematical/identity/layout helpers, not the game session.

Normal tick flow: sample/coalesce input -> validate world/selection token -> record GPU tool force generation -> record V2 `RecordTick` with immutable force input -> submit on the same graphics queue -> read only compact completion/selection facts -> update UI and render committed buffers. GPU decisions determine acceptance; CPU camera following and UI never write physics poses.

Keep every force/pick/output buffer alive until its command/readback completes. Bind all shader resources for Metal, including disabled branches. Dispose after a fence; callbacks from a disposed or previous world must not access released resources. Count these buffers and edit/render scratch in the same resource report.

Report driver/command-buffer memory separately when it cannot be enumerated; record process-memory observations and mark unavailable measurements explicitly. The 128 MiB buffer ledger alone does not prove the entire app's memory cost.

## 10. Implementation batches and acceptance

Implement in this order; each batch ends with reviewable source, focused evidence, an explicit commit and a current handoff. A plan checkbox is completed only from executed evidence.

| Batch | Complete observable result | Required checks / stopping rule |
|---|---|---|
| P0 — solver smoke and schedule cost | A tiny V2 world advances or rejects correctly; actual full-tick resource/recording cost is known | Restore license; run integrated tick plus affected residual/position/transaction fixtures; verify isolated contact mass ratios, off-center torque and rollback. Measure CPU recording and GPU/frame cost for tiny C1 before building lots of UI. Stop and fix demonstrated failures |
| P1 — standalone window | Dedicated playground opens into a visible paused world, draws committed grain/rigid state, supports Run/Step/Reset | Empty population and one-body rendering; camera/shader/Metal bindings; successful tick versus failed-tick retained display; separate Mac build and visible player scenario |
| P2 — direct manipulation | Select/pull, throw, drive and spin objects with mouse/keyboard | Exact picking for rotated shapes, stale-token rejection, center/off-center pulls, anchor immobility, one-shot impulse, external-work accounting, focus-loss/key-release behavior. Run the controls in the player and record input/accepted ticks |
| P3 — add/remove/edit | Spawn grains/rectangles, erase, clear and pin/unpin using paused transactions | Rotated placement/containment, mutually disjoint brush additions, deletion of selected/grabbed object, preserved remaining identities/motion, cold rebuild, capacity refusal, partial-allocation cleanup and recovery. Verify no duplicate inventory/matter and no second full arena |
| P4 — usable sandbox | Presets, gravity, selected-property edits, clear fault panel and scene export/import | Gravity work/conservation; rectangular inertia; paused reposition validation; invalid-file preservation; exact roundtrip/cold continuation; repeated reset/edit/disposal without leaks; small packed/support cases |
| P5 — packaged playground | Separate app with documented controls and measured small-workload limits | One stable affected regression suite, final matching Mac build and actual mouse/keyboard playthrough on final source; publish limits and unresolved faults |

P1 and P2 give an early interactive build; P3 is required before calling the requested add/remove playground complete. P4/P5 complete the planned deliverable. Do not keep adding presets or UI to avoid a solver failure discovered in P0–P3.

Use the least costly profile that passes the intended playground cases with unchanged tolerances. C1 is the first measured candidate, not an accepted default. Try a higher existing profile only with a recorded reason. No relaxed penetration, changed grain volume, extra damping or hidden velocity reset to make interactions appear stable.

Record CPU submission cost, actual UI/render frame time, accepted simulation ticks per second and GPU physics timing if supported. Target responsive 60 Hz UI for the small workbench; report achieved rates instead of promising them. `RecordTick` currently records all sixteen bounded slots and uncached schedules, so low populations alone may not solve latency. If that cost blocks interaction, implement and verify cached/masked scheduling for this concrete session before widening the feature set. Do not replace the solver with a different algorithm to mask the issue.

The historical 8,192-grain throughput gate remains separate. A small interactive playground is useful before full Debris qualification, but it must be labeled with its tested population/profile and does not close R1, V2-3 or B.GATE. Do not run the 100,000-site workload for this project.

## 11. End-to-end manual acceptance script

The final player check must perform these actions, with accepted ticks, command IDs, counts and faults recorded:

1. Launch the dedicated app. See grid, objects and controls immediately; startup is paused and has no black screen.
2. Run and pause; single-step once. Confirm one tick was attempted and the display reflects only accepted state.
3. Grab the heavy block at its center, then at an edge. Translate first; observe rotation from the off-center pull. Release and observe continued motion.
4. Select it and use W/A/S/D and Q/E. Confirm force-driven response, key-up stop of thrust, and no motion command to an anchored object.
5. Add isolated grains, a small brush group, a rotated rectangle and a fixed wall. Attempt occupied and out-of-page placement; see refusal without changing the accepted world.
6. Push the heavy body into a grain and then a small pack. Observe finite-mass exchange and intact identity/count accounting. A rejection pauses and retains the world with a readable fault.
7. Delete a moving grain and the selected body; clear/reset. Confirm remaining objects retain their identities and accepted motion through edit reconstruction.
8. Pin/unpin a body, toggle gravity, change density while paused, and resume. Confirm explicit mobility, recomputed mass/inertia and accounted external work.
9. Export/import a scene; attempt a corrupt import. Verify the good world survives rejection. Export a deliberate failure and reproduce it from its accepted input/command.
10. Repeat spawn/delete/reset, close the app with work pending, and reopen. Verify clean resource disposal and isolated output paths.

Completion means this flow runs in the separate built app on the final source. C# compilation alone, screenshots, a launched process or pre-existing legacy physics evidence cannot establish it.

## 12. Current handoff

The rollback restored the exact pre-integration workspace status. `Showcase.cs` and `MatterView.cs` hashes match their pre-attempt values; the three untracked gameplay-integration sources were removed. Existing solver commits, tests, unfinished user work and game builds were preserved. No game binary was rebuilt or launched, and the previously reported black screen remains unresolved.

The next implementation assignment should begin with P0, then build the isolated P1–P3 interaction loop. The immediate runtime prerequisite is a valid Unity 6000.3.11f1 Editor license. This planning assignment stops here; no gameplay or playground implementation is authorized by the existence of this document alone.
