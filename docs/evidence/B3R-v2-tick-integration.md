# Four-step V2 tick integration — source only

User scope: implement four ordered stages and commit after each; do not launch Unity, compile, build or run tests. Source inspection is not runtime acceptance. Legacy remains default.

## Step 1

Accepted projected velocity output now updates transaction working endpoints; external force/torque integration; cold impulses; per-substep geometry rebasing; graph/finiteness rejection before motion application.

Next: Implement bounded prediction and surface-speed checks (step 2).

No compilation, tests, Unity, build or player run. All physical, resource and performance gates remain open.

## Step 2

GPU selects 4–16 substeps from surface speed and full-frame external acceleration; residual and integration share the selected GPU clock; prediction rejects nonfinite or greater-than-.125-cell surface motion; inactive fixed slots do not advance transaction stages.

Next: Connect position correction to predicted working poses (step 3).

No compilation, tests, Unity, build or player run. All physical, resource and performance gates remain open.

## Step 3

Predicted geometry feeds bounded position correction on shared graph/residual/factor/GMRES storage; explicit cumulative angular displacement reaches working pose; surface-envelope and position verdicts gate application; physical velocity and spin stay intact.

Next: Implement derived final conservation/count/geometry facts and accepted velocity-cache publication; wire the complete tick schedule (step 4).

No compilation, tests, Unity, build or player run. All physical, resource and performance gates remain open.

## Step 4

Complete fixed-frame tick recorder connects force integration and accepted velocity; GPU-selected bounded prediction; pose-only position correction; independent final geometry and physical count/conservation/energy/envelope facts; eligible provisional velocity cache; accepted-only publication and retained-geometry rebuild on failure.

Next: Runtime qualification when execution is authorized; then production schedule/resource integration and candidate adoption behind V2 gates. Warm-start cache reuse remains optional and unimplemented.

No compilation, tests, Unity, build or player run. All physical, resource and performance gates remain open.

## Integrated call and ownership

Construct one `CoupledTickStepper(state, transaction, graph, friction)` with a generated velocity graph in the same arena and at the state's initial bind poses. Union graphs require containment checking. The stepper borrows state, transaction and graph; it owns its bridge buffers, residual/preconditioner/Arnoldi workspace and stage controllers. Position shares that workspace and graph rather than allocating another Krylov basis. One stepper may claim a transaction. Dispose only after the queue is drained, before disposing the borrowed owners.

```csharp
var ticket = stepper.RecordTick(commands, nextTick, CoupledSolveProfile.C1, forces);
// The session submits commands once, in FIFO order on the same graphics queue.
// Retire ticket only from its completed GPU completion record.
```

`forces` is an optional immutable GPU float4 per endpoint: xy force, z torque. The caller keeps it alive and unchanged until the recording completes. Null submits zero force. Frame duration is fixed at 1/60; the GPU chooses h from 4–16 substeps using initial surface speed plus the full-frame acceleration bound. The residual reads the same clock. Selection above 16 rejects the tick. Prediction independently checks contact-modified surface speed. The sixteen recorded slots retain command/query overhead; only physical work and transaction stage advances are gated. These schedules are recorded per call, not yet cached production command lists.

Position search now retains cumulative angular displacement explicitly per endpoint, publishes it with selected centers, and zeros it on stage rollback. Applying accepted position output preserves accepted velocity and spin. Normal/tangent and normal-only graph construction modes are recorded in queue order; residual and local factors use the matching mode on shared storage.

## Final facts and provisional cache

Physical reduction uses compensated fixed-order endpoint sums about the fixed initial dynamic COM. It compares linear momentum at normalized 1e-4 and angular momentum at normalized 1e-3, and bounds kinetic-energy growth by max(.001 * initial energy, .0001) after accounting for the discrete force integration work. The operator exposes unscaled endpoint contact reactions from its existing final product; the accounting subtracts anchor reactions from expected dynamic momentum and reports the anchor impulse/torque separately. No extra contact product is added to obtain these facts. COM movement due to position correction is recorded separately.

The physical step checks the .375-cell combined surface envelope and [-512,512) grain page, retains compact maxima and checks dynamic/endpoint/grain counts. Transaction publication still independently validates immutable per-grain material/identity, bindings, parameters, terrain revisions and anchored motion. Integrated physics leaves opaque inventory/fuel words unchanged and rejects mutations; this does not implement fuel burn or transfers. Cargo classification remains a separate integration stage, and the physical step preserves the existing cargo payload.

Cache records contain the exact eight-word session key, world impulse xy/world normal zw, and both local surface anchors. Draft entries come only from the projected, rechecked accepted velocity solution before position scratch reuse. Full-key bucket lookup validates duplicate/corrupt chains. Corrected normal rows must match the complete key, dot(normal) >= .98, each local anchor within .01 cell and gap <= .005. Retained impulses are projected into the corrected unilateral/friction bounds; separated friction is zero. A deterministic scan compacts eligible entries and sets working header active-cache count. Optional cache capacity keeps a deterministic prefix of velocity points; omissions restart cold and do not remove physical contacts. The entire provisional cache/header publishes only with successful tick payloads.

`Diagnostics`, `InitialPhysicalFacts`, `ExternalAccounting`, `FinalGeometryDecision` and `FinalDecision` provide compact GPU records for later acceptance work. Diagnostic offsets/fault bits are named in `CoupledTickRecords.cs`. The persistent first-failure buffer now has a sixth word containing source-stage facts so dependent queued ticks cannot overwrite the original physical rejection details. Inactive substep slots do not overwrite accepted facts. Derived graph poses are rebuilt from retained working state after transaction publication or restoration.

No compile or execution evidence exists for these changes. Cold solving is the current path; warm-start reuse, production query masking/cached schedules, exact memory/timing qualification, shader binding/layout checks and physical matrix acceptance remain open. Legacy and candidate gameplay sessions are unchanged; B.GATE remains open.
