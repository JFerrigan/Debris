# Standalone physics playground — source checkpoint

2026-11-05 UTC. Latest user scope: implement the [playground plan](../PHYSICS_PLAYGROUND_PLAN.md), do not run the program, and use source work efficiently. No Unity process, test runner, build or player was launched in this assignment. Runtime gates remain open; the existing game path and its earlier black screen were not changed.

## Source mapping

| Requirement | Source |
|---|---|
| Isolated startup, camera, visible error UI, controls and tool settings | [PhysicsPlaygroundBootstrap](../../Assets/Debris/Presentation/Runtime/PhysicsPlaygroundBootstrap.cs) and [dedicated scene](../../Assets/Scenes/PhysicsPlayground.unity) |
| GPU state ownership, full V2 tick, compact completion/diagnostics, stale command rejection, snapshots | [CoupledPlaygroundSession](../../Assets/Debris/Simulation/Runtime/Playground/CoupledPlaygroundSession.cs) |
| Exact rotated picking, placement preview, force-based grabbing, world/local thrust, off-center torque, gravity and one-tick push | [CoupledPlaygroundInput.compute](../../Assets/Debris/Simulation/Resources/CoupledPlaygroundInput.compute) |
| Direct committed-buffer rendering, grid, orientations, anchors, selection/COM/local axes, brush/force overlays, retained edit image | [View](../../Assets/Debris/Presentation/Runtime/CoupledPlaygroundView.cs) and [shader](../../Assets/Debris/Presentation/Resources/CoupledPlayground.shader) |
| Unit grains, density/mass/inertia, editable rectangles, independent placement SAT, full IDs, bounds/caps and six presets | [World definition](../../Assets/Debris/Simulation/Runtime/Playground/PlaygroundWorldDefinition.cs) |
| Grains-first solver records, persistent identity bindings and cold reconstruction | [Seed compiler](../../Assets/Debris/Simulation/Runtime/Playground/PlaygroundSeed.cs) |
| Single-arena edit fence, accepted snapshot, validation, cold replacement/recovery, generation invalidation and edit mass/momentum ledger | [Edit transaction](../../Assets/Debris/Simulation/Runtime/Playground/PlaygroundEditTransaction.cs) |
| Full convex-path buffer reservation with actual-ledger guard; bounded edit/render reserve | [Budget](../../Assets/Debris/Simulation/Runtime/Playground/PlaygroundBudget.cs) |
| Versioned atomic sandbox scene files and rejected-file preservation | [Scene file](../../Assets/Debris/Simulation/Runtime/Playground/PlaygroundSceneFile.cs) |
| Accepted-world/attempted-command failure export, source/binary identity and explicit replay | [Failure file](../../Assets/Debris/Simulation/Runtime/Playground/PlaygroundFailureFile.cs), bootstrap export/replay actions and build identity resource |
| Separate build output and source hashes; no automatic launch or game build overwrite | [PhysicsPlaygroundBuild](../../Assets/Debris/Editor/PhysicsPlaygroundBuild.cs) |

The render/pick path does not read full arrays during ordinary play. Selected-object inspection reads one body record; full motion snapshots occur only at edit/export fences. Input is sampled once per fixed tick, forces stay immutable until its completion, and only accepted buffers render. User edits may remove objects or reset motion explicitly; solver failures never do that implicitly.

The first version supports convex rectangles and 256 grains/16 non-grain endpoints as proposed soft caps, not measured capability. Allocation arithmetic covers the current fixed-size solver pools even for tiny worlds, plus force/shape/pick buffers; the 64 KiB reserve covers bounded frozen display and initial-geometry scratch. Constructor admission compares predicted and actual arena bytes and rejects a mismatch. Driver and command-buffer memory are separately unmeasured.

## Source checks actually performed

Direct Roslyn compilation used pinned Unity 6000.3.11f1 `Resources/Scripting/NetCoreRuntime/dotnet` with `DotNetSdkRoslyn/csc.dll`, engine/reference assemblies and cached Input System/Test Runner references. Final outcomes:

| Compiled unit | Result |
|---|---|
| All 24 contact runtime sources, seven playground runtime sources and `ProofRecords.cs` | Exit 0, no diagnostics |
| New presentation controller and view against current runtime APIs | Exit 0, no diagnostics |
| New Editor scene/build entry against pinned Editor API | Exit 0, no diagnostics |
| Runtime sources and ten fixtures: the two playground fixtures plus eight affected V2 fixtures | Exit 0, no diagnostics |

Response/output files are `/private/tmp/debris-playground-{source,presentation,editor,tests}.rsp` / `.dll`. Final runtime/presentation/test compiler logs use the matching `-final.log` suffix. Compilation is not test execution or shader compilation.

Fifteen direct compiler invocations preceded the final audit. The initial runtime compile warned about a serialization-populated version field; the initializer was made explicit. One Editor harness attempt referenced both the monolithic Editor API and its duplicate module API; removing the duplicate harness references produced exit 0 without an Editor source change. Other compiles followed implementation/API/regression changes. No expensive runtime reruns occurred.

Static checks cover scene-to-script GUID identity, asset metadata uniqueness, local documentation links, declared/new shader kernel names, isolated build paths, absence of game-session initialization, unchanged existing gameplay presentation hashes and preserved pre-existing tracked source edits. Diff checks passed.

## Regression definitions, not executed results

[PlaygroundWorldTests](../../Assets/Debris/Simulation/Tests/Editor/PlaygroundWorldTests.cs) defines fourteen cases: six presets; rotated/contained placement rejection; capacity refusal preserving identities; analytical mass/inertia; grains-first reconstruction preserving full body IDs; genuinely empty world; invalid density/mobility/identity/bounds; versioned scene roundtrip/rejection; and invalid save preserving the old file.

[PlaygroundSessionTests](../../Assets/Debris/Simulation/Tests/Editor/PlaygroundSessionTests.cs) defines six session cases. Three GPU cases cover: admitted allocation totals and off-center mouse force with mass/inertia, including stale-world refusal; accounted gravity and explicit anchor immobility with hit/miss picking; and rejected edits retaining the owner followed by cold reconstruction preserving accepted motion and clearing to an empty world. Three empty-world transaction cases additionally cover throwing post-publication subscribers, throwing pre-retirement freeze subscribers, and disposal during freeze. Ten-minute timeouts include fixture lifetime.

All twenty compile but remain unrun. Shader behavior/bindings, live resource admission, edit recovery on GPU allocation failure, final scene rendering, Input System interaction, full-tick scheduling cost, scene IO execution and replay behavior have no execution evidence in this assignment.

## Build and acceptance handoff — do not execute under current scope

When execution is authorized and the pinned Editor is licensed, open `Assets/Scenes/PhysicsPlayground.unity` through **Debris → Physics Playground → Open isolated scene**. The separate **Build Mac (do not launch)** menu calls `Debris.Editor.PhysicsPlaygroundBuild.BuildMac` and targets `Builds/PhysicsPlayground/Debris Physics Playground.app`. The builder records source hashes, restores temporary player-name/window settings, and does not launch its output. No app at that path has been produced in this assignment.

Begin runtime work with P0's integrated solver and tiny-world resource/schedule checks, then the new fixtures, before proceeding to the plan's complete mouse/keyboard acceptance script. Keep the original tolerances and report measured limits. Do not infer a playable build or close B.GATE from this checkpoint. The active goal remains unproven pending the unexecuted requirements; existing user/game changes remain preserved.

## Follow-up source audit

The next goal turn found a concrete publication exception path: a throwing view notification could enter rollback after the new owner was already published, allowing a second live arena. Publication now precomputes revision/history/recovery data, transfers ownership once, and isolates each view subscriber's exception. Pre-retirement failures retain the old owner. Disposal during freeze stops reconstruction. Uncertain GPU cleanup/recovery requires restart rather than allowing another arena allocation. Loaded/reset gravity controls now reflect the current world's vector.

Nine additional direct compiler invocations followed these implementation/regression additions; runtime, presentation and ten-fixture source compilation all end at exit 0 with no diagnostics. No tests, Unity, build or player ran. The three new transaction regression definitions bring the playground total to twenty unrun cases. Existing game source remains untouched.

| Plan scope | Current evidence | What still prevents acceptance |
|---|---|---|
| Sections 1–2: isolated application using V2 | Dedicated scene, matching script GUID, build entry and source-only V2 ownership | No imported scene, built app or live solver evidence |
| Section 3: every tool/shortcut | Controller branches and UI controls compile; Input System API references resolve | Actual mouse/key/focus behavior untested |
| Section 4: picking and physical manipulation | GPU kernels and generation/command guards present; force/torque/gravity regressions defined | Shader compilation, bindings and physical results unverified |
| Section 5: safe editing | Fenced reconstruction, recovery and publication fixes; failure cases defined | Live allocation/recovery/cleanup tests unrun |
| Section 6: geometry/caps | Convex model, SAT, mass/inertia and allocation arithmetic; validation cases defined | Native resource admission and supported workload not measured |
| Section 7: rendering/failures | Dedicated shader/view and retained-state/fault UI source | Visual startup, Metal behavior and failure display untested |
| Section 8: presets/scene files | Six definitions, atomic codec and strict versions; roundtrip cases defined | Preset execution, filesystem IO and cold continuation untested |
| Section 9: lifecycle/data flow | Explicit buffer ownership; compact normal readbacks; stopped snapshots; stale input rejection | Runtime lifecycle and leak checks unrun |
| Section 10: P0–P5 gates | Source implementations and measurement fields present | All runtime/measurement gates and P5 binary remain open |
| Section 11: all ten manual actions | Corresponding controller/editor/export/replay paths present | No actual player interaction evidence |
| Sections 12–13: handoff | Source checkpoint and plan updated, ordinary commits pushed | Full goal completion is not proven by compilation |

The remaining acceptance work requires execution, which the active user instruction forbids. Keep the full goal intact; do not mark it achieved or launch programs to manufacture evidence. Source review/fixes in this audit are progress, not a runtime pass.

## HUD follow-up and native build evidence

The user reported that the HUD was too small. Text now defaults to 16 px, headings to 20 px, with larger button/toggle/field padding and scrollbars. A GUI transform scales the complete HUD by resolution and DPI, bounded to keep controls on screen. Top-level −/+ controls persist the chosen zoom. Raw pointer coordinates use the inverse transform for panel hit testing. GUI state is restored after drawing.

Direct C# compilation passed after correcting an unsupported GUIStyle property. Arithmetic checks covered 960×600, 1440×900, 1920×1080, 3024×1890 and 3840×2160 windows and 75–175% user zoom; panel extents and inverse hit mapping remain consistent. This does not substitute for a visual check.

Native build 1 returned Success but logged HLSL errors for reserved `line` / `point` identifiers in the renderer, input, position-search and tick-stepper shaders. Build 2 exposed the remaining reserved `linear` identifier. Renaming these identifiers preserves equations and binary layouts. The tick-test meta file also contained a 33-character GUID; it is now valid 32-character hex.

After two failed build-level shader results, diagnosis moved to three isolated shader checks, with no simulation dispatch. `ValidateShaders` now compiles/checks resources synchronously. The probes still fail on `CoupledTickStepper.QualifyCache`: IsSupported returns false, and querying its group size reports invalid index 10 despite a stated kernel count of 33. This remains an unresolved solver-support issue. The standalone build checks reported shader compilation errors but does not claim this separate hardware/kernel-support gate passes.

Final HUD build: process exit 0, `Build Finished, Result: Success`, zero C#/shader errors, executable present. All manifest source hashes match the final source; source hash `c6e3965caa4da59cf9c96657c04d238b6969acc0f821f69dafc70dd1bd273b69`. Canonical log: `/private/tmp/debris-playground-hud-build.log`; failed build logs use `-hud-shader-errors.log` and `-hud-linear-errors.log`; isolated probe log: `/private/tmp/debris-playground-shader-validation.log`.

No player was launched by the agent, no physics test was executed and no physical acceptance is claimed. The user must close/reopen the app to load the rebuilt HUD. The source-only checkpoint's absent-binary statements above are historical; the separate playground app now exists.

## Tool pause-state investigation and fix

The user reported a PAUSED flicker and lost tool actions, and confirmed that only the PAUSED state appeared rather than an error. Source inspection found `Mutate` unconditionally setting `running=false` before every edit and restoring the old value only on success. This visibly changed pause state while the edit snapshot/rebuild awaited completion; a refused edit kept a valid retained world paused, and restoration could overwrite a newer user pause choice. The existing `Busy` fence already prevented physics submission during edits, so changing run intent was unnecessary.

`Mutate` now retains run intent unless the operation explicitly requests a stop or no valid live owner remains. Pause controls stay available during the edit. Scene export also uses the fence without toggling run intent. Input focus is now sampled/cleared within OnGUI; clicking the world releases a previously focused text field, which otherwise suppressed world-tool input.

Five focused EditMode regressions in [PlaygroundRunStateTests](../../Assets/Debris/Presentation/Tests/Editor/PlaygroundRunStateTests.cs) passed: successful edit keeps running; refused placement preserves running and owner; paused edit stays paused; user pause during edit is not overwritten; explicit stop still pauses. These use empty worlds, allocate no solver buffers and dispatch no physics. Result: `/private/tmp/debris-playground-runstate.xml`, 5 passed / 0 failed, process exit 0. No broader solver tests were rerun.

One final Mac build passed, with no C#/shader errors, at `/private/tmp/debris-playground-runstate-build.log`. The output executable exists and every source-manifest hash matches the current player code; source hash `ec10c928daad7e129d1802d08b2024e33b949a52b76985f7d31e12d501dd8ec9`. No player launch was performed. The larger HUD remains. Actual pointer/focus visuals still need the user's check; this does not close the independent QualifyCache solver-support or physical acceptance issues.

## Debug logging follow-up (2026-11-05 UTC)

Automatic bounded JSONL logging now traces actual click/run/pause/focus events, edit stages and elapsed times, input command IDs, solver failures and Unity stacks. File errors retain an in-memory tail without changing actions/run intent. Diagnostics copies the file path; failure exports include the last sixteen events. See [usage and event schema](../PLAYGROUND_DEBUGGING.md).

Two focused runs passed 9/9 each; the second followed bounded failure-export history and direct solver-exception records. Final result `/private/tmp/debris-playground-debug.xml` (06:01:52Z, 0.0478794 seconds). One final separate Mac build exited 0 with Success and no C#/shader errors; `/private/tmp/debris-playground-debug-build.log`. All manifest source hashes match and the executable exists. Source hash `af38bb7c0ffb1130635f4f1a1bb563fa782d6d1dab0aa27abd5d95f8d82a1f84`. No app launch or physics dispatch; actual interaction/GPU acceptance and QualifyCache support remain open.
