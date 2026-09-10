# Parallel packed-contact experiment

Source: `2a61ff2`. [Player output](B3R-parallel-player.txt), [focused runner result](B3R-parallel-tests.xml). Build succeeded once; one Mac development player run completed normally with exit 2 for failed correctness. Device: Apple M4 Pro / Metal / Unity 6000.3.11f1 / 1280×800 / v-sync off.

## Decision

**Stop integration under the approved R1 condition.** Every locked profile failed a necessary packed-cavity case. No profile can pass the combined correctness/performance gate. This is an early convergence experiment, not completed Phase 1, a performance benchmark or proof that every possible implementation of mass splitting fails.

| Profile | Shared rigid motion: first rejected tick | Maximum wall penetration | Resting cargo under force/torque: first rejected tick | Maximum wall penetration |
|---|---:|---:|---:|---:|
| 4/2 | 28 | 0.00101912022 | 3 | 0.00118285418 |
| 8/4 | 39 | 0.00101149082 | 3 | 0.00111538172 |
| 12/6 | 46 | 0.00100064278 | 3 | 0.00102245808 |

Locked wall limit: **0.001 cell**. Grain penetration remained below .01 (observed maxima .003612–.003996). Each case used four adaptive substeps, 9,902 canonical candidates and no reported envelope fault. No profile reached 120 accepted ticks. The initially resting case fails by more than the near-threshold shared-motion rounding differences.

## Implemented and verified scope

The opt-in helper uses 48-byte world-space rotating unit-density grain records and 32-byte body/parameter records, integer two-cell bins with hierarchical prefix scans, sorted canonical candidate rows and compact endpoint adjacency. Contact threads compute accumulated mass-split normal/friction increments; grains gather and bodies reduce reactions. Position correction is separate from physical velocity. Committed buffers update only after validation succeeds. Rendering consumes the same 48-byte square layout, with finite-mass box boundary patches.

Eight focused tests pass: layouts, two-grain exchange, 10000:1 analytical speed, off-centre fragment linear/angular momentum and energy, free square spin, 120-cell/s anchored impact, whole-tick excessive-speed rejection, and exact packed rollback. Packed rollback compares grain centres/velocities/angles/spins and body centre/velocity against the previous completed state. These tests do not certify every outstanding exceptional case.

The packed fixture fills a 50×50 cavity exactly with 2,500 unit squares. Body mass 10,000; inertia 5,000,000. Shared-motion case starts at linear speed 3 and spin .06 with matching grain surface velocities/spin. Resting case starts all matter at rest and submits local force (60,000, 0), torque 300,000 each tick. Friction .3; restitution zero. Boundaries are four cached box patches. There is only one rigid body in this necessary experiment, so rigid-only contact sweeps would have no contacts to solve.

## Limits and interpretation

This isolates inadequate packed-load convergence in the implemented parallel grain/body coupling at the locked iteration budgets. It does not establish the full target solver or a production-ready failure protocol. Full rigid-only manifolds, arbitrary mask/terrain boundary caches, all candidate/envelope exceptional cases, GPU conservation reductions, GPU timing collection and the remaining workload matrix were not implemented or exercised before the early stop. The proof uses direct dispatch for some fixed-size scan/validation work and indirect dispatch for active grain/contact/body work; full inactive-substep dispatch optimization remains unfinished.

CPU submission maxima in the raw output are untimed diagnostic observations, not p95 benchmark evidence. Physics and total GPU timing are **unmeasured**. No 8,192/10,000-grain workload, 120-frame warmup/600-frame sampling or 16-chunk terrain workload was run after all profiles failed the necessary correctness case. The proof allocates 25,553,652 explicit bytes at 2,500 grains; this is not an approved full-session budget measurement.

Normal gameplay, mining, fuel acknowledgement, doors, travel and persistence still use the previous implementation. R2–R4 and B.GATE remain open. No schema migration, removal of legacy code or tolerance relaxation occurred. The unfinished pre-existing gatherer remains in place and in the baseline patch; unrelated work is preserved.

## Validation/rework accounting

Ten focused editor launches: three did not reach fixtures (sandbox cache access, existing Debris editor lock, stale licensing mutex); seven ran fixtures. Fixture reruns addressed: reserved HLSL `point`; non-uniform reduction barriers; EditMode disposal API; float ceiling roundoff at exactly 120 cells/s; repeated signed/unsigned Metal warning flood; and validation scheduling so one faulting thread cannot suppress another thread's penetration maximum. The final runner result is eight passed, zero failed. One successful build and one player run; no full-suite, unrelated scale run or build/player rerun.

The speed selector subtracts 1e-5 from the dimensionless substep count before ceil solely to remove floating-point roundoff at an integer boundary; it does not clamp velocity. The 120-cell/s case requires 16; the 121-cell/s case rejects and preserves both bodies and grains.

## Timing and usage

Implementation start UTC: 2026-09-09T02:50:24+00:00. Player output completed UTC: 2026-09-09T03:57:56.268652+00:00. Evidence/handoff recorded UTC: 2026-09-10T03:31:49.015030+00:00. The wall interval spans a later resumed session and must not be interpreted as continuous execution time or usage. Start to player-output completion: 67.54 minutes; full batch wall interval through this handoff: 1481.42 minutes. Client-reported usage: unavailable.
