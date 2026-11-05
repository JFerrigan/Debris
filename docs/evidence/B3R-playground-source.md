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

[PlaygroundSessionTests](../../Assets/Debris/Simulation/Tests/Editor/PlaygroundSessionTests.cs) defines three GPU cases: admitted allocation totals and off-center mouse force with mass/inertia, including stale-world refusal; accounted gravity and explicit anchor immobility with hit/miss picking; and rejected edits retaining the owner followed by cold reconstruction preserving accepted motion and clearing to an empty world. Ten-minute timeouts include fixture lifetime.

All seventeen compile but remain unrun. Shader behavior/bindings, live resource admission, edit recovery on GPU allocation failure, final scene rendering, Input System interaction, full-tick scheduling cost, scene IO execution and replay behavior have no execution evidence in this assignment.

## Build and acceptance handoff — do not execute under current scope

When execution is authorized and the pinned Editor is licensed, open `Assets/Scenes/PhysicsPlayground.unity` through **Debris → Physics Playground → Open isolated scene**. The separate **Build Mac (do not launch)** menu calls `Debris.Editor.PhysicsPlaygroundBuild.BuildMac` and targets `Builds/PhysicsPlayground/Debris Physics Playground.app`. The builder records source hashes, restores temporary player-name/window settings, and does not launch its output. No app at that path has been produced in this assignment.

Begin runtime work with P0's integrated solver and tiny-world resource/schedule checks, then the new fixtures, before proceeding to the plan's complete mouse/keyboard acceptance script. Keep the original tolerances and report measured limits. Do not infer a playable build or close B.GATE from this checkpoint. The active goal remains unproven pending the unexecuted requirements; existing user/game changes remain preserved.
