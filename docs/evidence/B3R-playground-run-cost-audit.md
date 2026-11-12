# Source audit of the Run stall (2026-11-12 UTC)

This is a source-derived execution-count audit, not a GPU timing profile. No source changes, Unity execution, rebuild or player launch were needed. It explains why the 131-object user scene can submit a huge workload despite a tiny physical population.

## Recorded schedule in the cheapest profile, C1

- `CoupledTickStepper.RecordTick`: records sixteen complete substep slots, independent of the GPU-selected active count.
- Each slot: velocity solve records four Newton updates. Position correction records two geometry iterations, each with four Newton updates. Total: twelve Newton updates per recorded slot.
- `CoupledVelocityNewtonUpdate.Record`: records three attempts (initial, regularization retries 1e-5 and 1e-3).
- Each C1 attempt records sixteen Arnoldi steps. Convergence and retry/activity gates are GPU state; they do not stop these C# recording loops.

Thus `16 × (4 + 2×4) × 3 × 16 = 9,216` Arnoldi steps are recorded per tick. `CoupledArnoldiWorkspace.RecordStep` directly issues fifteen compute dispatches per step, including two passes of three reduction/projection dispatches. This excludes preconditioner and derivative dispatches called by that method, initialization, true verification, line search, graph work, cache work and final validation.

**Lower bound: 138,240 direct compute dispatch commands per tick from these Arnoldi steps alone.** This is a count of recorded commands; not a count of useful solver iterations or a measured dispatch-time total.

## Capacity-sized reductions

The generated graph sets `CoupledContactOperator.rowCount` to `CoupledContactRowBatch.MaxRows = 262144`. Its `RowCount` property therefore represents capacity on this path, not the current compact GPU row count. `CoupledArnoldiWorkspace` derives `rowCapacity`, `rowGroups` and `blockCapacity` from that property. With 256 threads per block this is 1,024 blocks, even for the tiny scene.

Each `DotBlocks` dispatch uses `(1024, 17, 1)` groups. Two such dispatches per recorded step produce:

`9,216 × 2 × 1,024 × 17 = 320,864,256` submitted workgroups per tick, nominally `82,141,249,536` thread invocations at 256 threads/group, for dot blocks alone.

These are submitted launch dimensions, not measured executed instruction counts. Compiler optimization and device scheduling affect physical instruction cost. However, the source does not explicitly skip inactive dot/norm groups: `DotBlocks` and `NormBlocks` set their input to zero when disabled, then execute shared-memory reduction loops and group barriers unconditionally. GPU gates therefore do not make this recorded path free. Other product paths do use zero-group indirect dispatches; the reduction path does not consistently use that approach.

## Why the window stalls

Commands are recorded synchronously during controller Update, then submitted as one large tick. Simulation compute and display rendering use the same GPU. The next tick and manipulation/picking are also fenced by completion/readback state. This provides two plausible stall paths: CPU recording/driver overhead and GPU queue saturation that delays display and readbacks. The latest user log independently confirms poor responsiveness (one extra completed tick and eight rendered frames in roughly ten seconds at 131 objects). A timing capture is still needed to apportion wall time among recording, driver, compute stages and presentation; no percentage attribution is claimed.

## Engineering conclusion

This is not evidence that a GPU cannot simulate 131 bodies. It is evidence that the current custom solver's worst-case recorded schedule and capacity-sized reduction work are inappropriate for that scene. Correctness checks and accepted ticks did not validate interactive throughput. Small-scene performance was not gated before tool integration; that was a development/acceptance failure.

The first repair/replacement decision should target scheduling and workload sizing: separate capacity from live row count, dispatch only live blocks/columns, make inactive/converged work actually skip dispatch/reduction, and avoid unconditional worst-case retries/substeps. Replacing UI code or merely reducing body count does not address this source-derived multiplication. Preserve physical acceptance rules while measuring each change against a minimal continuous scene. This audit does not authorize implementation or a wholesale rewrite.
