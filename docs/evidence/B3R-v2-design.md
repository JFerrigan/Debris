# B.3R V2 design checkpoint

Scope: documentation and architecture planning only, begun 2026-09-24 UTC and completed 2026-09-25 UTC. The user redirected the previous prototype assignment to a detailed redesign. No simulation source, runtime thresholds, fixtures, archives, model or effort settings were changed. No Unity process, test, build or player was launched for this batch.

## Decision and limits of the evidence

A substantial contact-subsystem replacement is justified by repeated failed packed-convergence experiments. The whole application does not need replacement: GPU matter ownership, material identities, finite mass/inertia, ordered command transactions and renderer boundaries remain useful. Replace the local Jacobi/separate-rigid contact schedule, representative-point manifolds, stale geometric multiplier accumulation and solver scratch with the specified coupled formulation.

The design compared two production approaches: manifold block Gauss–Seidel with coloring, and a global matrix-free Newton/Krylov contact system. The finite ship's high contact degree makes a straightforward colored solve highly sequential. The global formulation was selected for development; it has not earned physical or performance confidence yet. Offline block solving remains an independent reference, with nonconvergence reported honestly.

The new [architecture](../CONTACT_SOLVER_V2.md) specifies the contact law, scaled residual and derivative, GMRES/preconditioner, singular-search handling, bounded iteration/retry accounting, position systems, warm-cache validity, broad-phase coverage, segmented reactions, layouts, memory ceilings and whole-tick rollback. The [implementation plan](../CONTACT_SOLVER_V2_IMPLEMENTATION.md) specifies source ownership, independent reference cases, concrete gates, timing collection, fault taxonomy, tests and later integration. The [next assignment](../CONTACT_SOLVER_V2_PROMPT.md) stops after V2-0's reference decision.

The planned 111 MiB arena and C1/C2/C3 iteration caps are engineering hypotheses. A global solve can still stagnate, encounter incompatible constraints, exceed Float32 precision, or cost too many reductions/dispatches. The plan first requires an independent accepted reference, then measured GPU scheduling feasibility. Neither a literature result nor passing a reference calculation is a GPU performance pass. A failed gate requires a documented design revision; later agents may not quietly change the physical law or expand caps.

## Unchanged runtime result

Starting HEAD: `ceca0a992907497993bd2cb656df2f12e19521f4` (`docs: record B3R coupled sweep decision`), already on origin/main. The earlier checkpoint `12cdcdb` preceded the rejected increased-sweep experiment. The latter increased repetitions of the same local update; it did not implement the new coupling now proposed.

| Legacy profile | Restored attempted fault tick | Restored committed ticks | Eightfold prototype committed ticks | Current timing verdict |
|---|---:|---:|---:|---|
| 4/2 | 1 | 0 | 1 | All throughput p95 unmeasured |
| 8/4 | 1 | 0 | 2 | All throughput p95 unmeasured |
| 12/6 | 2 | 1 | 3 | All throughput p95 unmeasured |

Every profile fails packed solid acceptance. No profile reaches 120-frame warmup; no qualifying 600-frame window exists. The original .001 target and interim .002 grain/solid limit both fail; rigid/solid remains .001. V2 restores .001 as the mandatory grain/solid bound without loosening any gate. The detailed values and archived inputs remain in the [runtime checkpoint](B3R-physics-viability.md).

Latest executed runtime verification remains 140 passing fast tests plus one explicit skip, successful Mac build, and matching player exit 2 on genuine physical faults. No new runtime verification is claimed. The prior explicit buffer measurement was 83,751,624 bytes; it is neither V2 memory use nor a throughput qualification.

## Source identity and preserved work

The following SHA-256 values were rechecked during this documentation batch and match the restored dirty source in the runtime evidence:

| Source | SHA-256 |
|---|---|
| `Assets/Debris/Simulation/Resources/ParallelGrains.compute` | `d41c1a466fa103566f39e28508116b1609d0b9776737a024fd2d4469c7986f0c` |
| `Assets/Debris/Simulation/Runtime/ParallelProof/ParallelGrainSolver.cs` | `3798fe07644d4fcea4693f4bcdaac3a21aca25830213375478375b1e883711aa` |
| `Assets/Debris/Simulation/Runtime/ParallelGameplaySession.cs` | `859d1dc9ba04722f8aac625e5c6f0dd821553bf083def2437b20bb5e2d1eb9a2` |

The user's unfinished impact capture, legacy contact gatherer, content, persistence, project/package settings, untracked duplicate compute file and application bundle remain untouched. The existing [source manifest](B3R-physics-viability-source-hashes.txt) identifies the broader measured runtime: 25 entries still match. Its one superseded entry is `ParallelViabilityRunner.cs`; the later timing-mapping fix is already committed in the starting HEAD and its current SHA-256 is `d71e301e597409043b61703bb127390481859ae25de7a83e244db87a1874b236`, as recorded in the subsequent runtime evidence. This documentation task did not change that runner. Only explicit documentation paths belong to this commit.

No original or prototype trace was exported or overwritten. Future captures use exclusive immutable input creation and separate trace output directories. Approximate reversal of old post-force snapshots cannot be labeled exact pre-step replay.

## Plan verification and remaining work

Documentation review passed 125 local links/anchors across 18 changed/new Markdown files, checklist sequencing, source preservation and memory/stride arithmetic (111 MiB total, 17 MiB headroom). A standalone arithmetic check evaluated the written generalized derivative at 2,000 smooth branch interiors, covering collapsed, stick and both slip branches; maximum absolute centered-difference discrepancy was 4.13e-10. This is an equation check, not a production test or branch-boundary proof. This review is not a numerical-convergence experiment. V2-0 must implement independent reference tests and produce its own source/input-tagged evidence before V2-1 begins. Research links in the design identify primary technical sources; proposed tolerances/caps and the Unity/Metal integration remain project decisions to validate.

R1/B.GATE remain open. Candidate flight/drilling/doors/cargo/suction must be ported and reverified after a V2 profile qualifies. Damage/fuel may resume only after that integration gate, then persistence/travel and eventual default cutover follow their own requirements. The 10,000-grain workload and 100,000-site scale run remain deferred. No larger population or platform claim follows from this plan.
