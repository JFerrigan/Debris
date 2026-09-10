# Contact physics — B.3R parallel redesign

Status: replacement required; the opt-in packed experiment failed all three profiles and integration is stopped. See [canonical results](evidence/B3R-parallel-experiment.md). Earlier checkpoints below are historical scoped evidence, not packed-contact acceptance. B.GATE and B.5 remain blocked. Baseline is `f3aa5c9`; the unfinished gatherer is preserved in [baseline evidence](evidence/B3R-redesign-baseline/unfinished-contacts.patch).

## Locked architecture

GPU parallel, mass-split Jacobi square-grain contacts couple to a small GPU sequential rigid-body solver. `MatterSession` owns resources and one ordered graphics-queue command buffer. No CPU contacts, float atomic reactions, graph coloring, contact islands, general physics interface, automatic anchoring or serial grain movement retries.

All grains (cargo and fuel included) have world-space centres/velocities and independent angle/spin. The final record is 48 bytes: float2 centre, float2 velocity, float angle, float spin, uint material/identity/flags and three reserved uints. Unit-square mass is density and inertia is mass/6. Cargo classification changes no physical state. Body state is a 32-byte COM pose/motion record; 32-byte body parameters carry inverse mass/inertia, local COM, boundary range, mobility and revision. Ship slot 0 and fragment slots 1–16 are transient indices, never persistent IDs. Hull origin = COM position − Rotate(local COM, angle).

CPU owns structure, machinery, tank inventories, cached attached mass and explicit mobility. GPU owns grain/body motion. ShipRuntime pose values are presentation mirrors. Independent cargo is excluded from attached mass. Topology and inventory changes remain fenced transactions. Cache exposed boundary cells/faces on shape revision or effective door changes; terrain refreshes dirty chunks and border strips only.

## Numerical contract

Fixed dt 1/60, 4–16 adaptive substeps: max(4, ceil(2 * maximumSurfaceSpeed * dt / .25)). Include spin, rigid surface motion and submitted acceleration. Exceeding 16 faults the whole tick without velocity clamping.

Only three profiles: 4/2, 8/4, 12/6 velocity/position iterations, with two rigid-only sweeps after each grain iteration. Restitution 0, Coulomb friction .3 (0 for analytical fixtures). Position targets .002 grain–grain and .0001 solid. No warm starting across substeps, springs or compliance.

For normal A→B, vn = dot(surfaceVelocityB − surfaceVelocityA, normal). kA = invMassA + invInertiaA * cross(rA, normal)^2; similarly kB. Ksplit = degreeA*kA + degreeB*kB, where degree counts incident candidate constraints and is at least one. Accumulate unilateral normal impulses against Ksplit; reduce equal/opposite increments using actual inverse mass/inertia, without endpoint averaging. Speculative contacts allow closing only through the remaining gap. Touching friction is clamped to μ times accumulated normal impulse. Separate mass-split position correction never changes physical velocity.

Maximum penetration over the entire run: .01 cell grain–grain, .001 grain/rigid–solid; after 120 unforced steps residual grain penetration ≤ .002. These bounded errors never authorize extra cargo capacity. Normalized linear momentum error ≤ 1e-4 using max(1, sum(initial mass*speed)); isolated angular momentum error ≤ 1e-3. Energy, including all spin, may not exceed initial energy by more than max(.1%, .0001) without external work. Keep the stricter 10000:1 analytical speed assertion (100000/10001 ± .0001).

## GPU scheduling and bounds

Copy committed state to working state, choose substeps on GPU, apply local forces using current GPU angle, gather swept candidates once per substep, solve velocities, predict, correct positions, validate, then commit only if every substep succeeded. Cutting runs after commit, reserves a grain slot and validates placement before removing terrain. Publish compact tick/fault/pose/cargo/door/impact facts.

Two-cell dense bins use integer counts, hierarchical exclusive scan and index spans. Multiple grains may occupy a bin. Swept bounds include angular extent and .25-cell correction margin. An escaped envelope faults; stale candidate coverage is never accepted. Each grain has at most 64 sorted candidate slots. Deduplicate pairs by identity, compact contacts, build endpoint degree/adjacency spans once per substep. Contact threads write only their own increments; grains gather adjacency and bodies use workgroup reductions. Use indirect dispatch for active counts/substeps. Bind only each kernel's resources within Metal writable limits.

Use oriented-square SAT and clipped contact points. Merge coplanar hull contacts into physical patches, preserving corner normals. Rigid patches have at most two points, 64 points/body pair and 4096 total. Capacity, nonfinite, page boundary, excessive speed and search-envelope failures preserve all committed state and identify the failed limit. Sleeping retains finite mass and wakes on meaningful contact.

## Gameplay and submission cutover

Upload blueprint CargoCavity. A grain is cargo only when its whole oriented square fits within boundary tolerance. Defer travel for passage straddlers. Reduce door overlap to one obstruction flag; obstructed closure stays open without changing grains. Suction uses current body pose and cannot cross intact walls.

Retain up to 16 submitted ticks and provisional fuel changes in FIFO order. Later preparation uses provisional fuel; compact completion commits acknowledged changes. Failure discards that tick and later changes. Backpressure preserves time and fuel. Saves drain the queue. MatterStepInput contains tools/door/local force/torque/attached mass, never displacement. Mass-only updates preserve body motion.

Impact events contain tick, body/feature, local point, accumulated normal impulse and effective energy = .5*preContactClosingSpeed^2/actualEffectiveInverseMass. Keep strongest event per body/tick pending until acknowledgement. Damage uses equivalent speed sqrt(2*energy); update topology, boundaries and mass together. Detached grains/fragments inherit surface velocity and spin.

## Migration and removal

After proof and gameplay gates, checkpoint schema 5 and sparse-site schema 3 store world grains and complete stable body/mass/mobility/impact state. Write only new formats. Read checkpoint 1–4 and sparse 1–2 through legacy DTOs; convert sparse grains after loading both core and blobs. External centre = old lower-left + .5. Cargo centre transforms old local centre; preserve stored world velocity, inherit ship angle/spin for old cargo, zero these for old external grains. Legacy GPU linear motion is interpreted as COM velocity unchanged; old rotating trajectories are not promised. Resolve content before fragment mass reconstruction. Keep old files until atomic publication succeeds.

Spatial buckets are world chunks; preserve order, identity, material keys and fuel energy. Save no derived solver scratch. Exact new snapshot restore, continued-step centre .0001/velocity .001, 120-step bounded conservation, legacy schema-1/schema-4/sparse-2, reordered catalogs, corrupt-primary recovery, interrupted writes and future-version rejection are required.

Travel geometrically selects cargo and rotates its centre/angle with relocation. Relative velocity subtracts old ship surface velocity and is rotated before adding new surface velocity; spin subtracts old ship spin and adds new ship spin. Deposits/fragments stay at departure.

After default cutover delete IntegratePhysical/64 retries, serial SolveShipCells/gatherer, dual-coordinate TransferCargo/_CargoOccupancy, Free/CargoFree admission, pose rejection, physicalShip mode, legacy displacement stepping/Tick integration, retry Step and unused CargoGrid. Keep SAT, mass, materials, transactions and support helpers.

## Phase order and stop gate

0. Freeze this contract and preserve changes; documentation checks only.
1. Opt-in runnable GPU proof using production-intended kernels; normal gameplay stays unchanged. Test two grains, 10000:1, 100/1000 dense piles, 2500 tightly packed grains in a 50×50 cavity (shared initial rigid motion and initially resting cargo under thrust/torque), free spinning cargo, off-centre fragment, anchored glancing wall, existing 201 layout, 8192 combined and 10000 exploratory. Sweep only 4/2, 8/4 and 12/6. **If none passes locked correctness and performance, preserve evidence and stop integration.** Isolate the failing assumption; never soften tolerances or add arbitrary passes.
2. Complete masks/boundaries, mining/suction/cargo/door/rendering, fuel/damage/topology acknowledgement and exceptional limits on candidate path.
3. Migrate persistence/travel, port fixtures, then switch default.
4. Remove legacy paths; stable fast suite and one matching final Mac build/player batch; close gate only with every acceptance case verified.

Mining counts identities once; fuel grade/residual energy survives failed placement and transfers; capacity test attempts a 2501st grain without hiding compression behind classification. High speed: mass-1 grain at 120 cells/s against one-cell anchor and dynamic body without tunnelling within 16 substeps. Deliberately test contact/manifold capacity, density, envelope and speed faults with no partial state, cutting or fuel commit.

## Benchmark protocol

M4 Pro, Metal, pinned Unity 6000.3.11f1, 1280×800 development player, v-sync off, 16 active 128² chunks. Warm 120 frames, sample 600 with one fixed step/frame; identical geometry, seed, forces, renderer and camera across profiles. Keep page boundaries and snapshots outside timing windows. GPU stage timings must account for delayed samples; invalid/missing GPU timing cannot pass.

8192 grains including packed bay/dense pile/up to 16 fragments: physics GPU p95 ≤ 8 ms; total GPU p95 ≤ 12 ms; frame p95 ≤ 20 ms; CPU submission p95 ≤ 2 ms; explicit buffers ≤ 128 MiB; no steady-state submission allocations. 10000 is exploratory only. Publish named tick/fault, counts, bin maximum, candidate/contact degree/manifold, substeps/profile, penetration classes, envelope/overflow, cargo/volume, sleep/wake and stage timing diagnostics. Reduce conservation/energy/maxima on GPU; no full-array readback per measured frame.

The previous combined 201-cell result is 324.505 ms GPU p95 and 29 fallbacks. The 1.876 ms loose-only result is a different workload, not a speedup comparison. The historical rotating fixture had only 100 widely spaced cells, not packed acceptance.

## Historical scoped evidence

## B.3R.1–B.3R.2 checkpoint

Attached mass uses material density, whole-machine mass once, and fuel-grade density. Cached structure/machinery sums produce COM and inertia; free cargo is excluded. Gameplay submits local engine forces and mount/control torque; GPU fixed steps own motion. The CPU collision-readback hard stop is removed. The analytical oracle covers the 10,000:1 inelastic collision, anchored/glancing contacts and off-centre torque.

The GPU single-writer solver wakes and exchanges equal/opposite impulses with free cells before integration. Four substeps and a 0.0001-cell geometric skin avoid the observed translated starter-hull rounding fallback; skin correction does not change velocities. This is a bounded first contact proof, not the final island/budget solver. The legacy displacement API remains for historical fixtures.

Canonical evidence: [43 passing tests](evidence/B3R-single-cell-tests.xml), [standalone acceptance and rerun reasons](evidence/B3R-single-cell-player.txt). A mass-1 pixel reduced starter speed from 10 to 9.994825; thrust continued, zero fallbacks, no overlap, motion survived save/load. Mac frame p95 17.595 ms; GPU p95 2.394 ms. Remaining work: cell chains, free-cargo coupling, fragment/anchor impulses, high-speed substeps and physics persistence/migration.

## B.3R.3 checkpoint

Cell pairs/piles now exchange equal/opposite impulses; serial retries advance packed trailing cells after leaders. Free cargo uses an exact next-pose frame conversion with world velocity unchanged; contacts supply load, without adding cargo to hull mass. Fragments have finite material/unit mass and inertia, exchange impulses with cells/ship/anchors, and inherit surface motion. Anchored walls remove approaching normal velocity while preserving tangent; cutter-released cells remain dynamic. Damage/save synchronization no longer zeroes motion from collision flags.

[49 tests](evidence/B3R-island-tests.xml) and the [combined Mac player](evidence/B3R-island-player.txt) pass scoped physics acceptance. The 201-cell workload conserved momentum within 0.323917 mass-cell/s, reduced energy, rotated the impacted fragment and remained non-overlapping. **Performance fails:** frame p95 324.382 ms; GPU p95 324.505 ms. It also records 29 pose fallbacks during convergence. That checkpoint proposed gathered serial contacts; the parallel redesign contract above supersedes that proposal.
