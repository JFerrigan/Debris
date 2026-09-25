# Contact physics — B.3R solver V2 contract

Status, 2026-09-24: **V2 is designed but unimplemented.** The restored Jacobi solver still fails packed physical acceptance. Its 8,192-grain profiles commit 0/0/1 ticks before faults; all throughput p95 values remain unmeasured. The [legacy evidence](evidence/B3R-physics-viability.md) is preserved. R1 and B.GATE remain open, candidate gameplay remains opt-in, and damage/fuel integration is paused.

The authoritative replacement design is [CONTACT_SOLVER_V2](CONTACT_SOLVER_V2.md). The concrete batch sequence, source map, reference methodology, timing implementation and acceptance matrix are in [CONTACT_SOLVER_V2_IMPLEMENTATION](CONTACT_SOLVER_V2_IMPLEMENTATION.md). The immediate next task is V2-0, using the [implementation prompt](CONTACT_SOLVER_V2_PROMPT.md). This supersedes the previous locked Jacobi architecture; it does not claim measured acceptance of the replacement.

## 1. What changes and what remains binding

| Previous prescription | New decision |
|---|---|
| Degree-split Jacobi corrections and a separate rigid-only solver | One matrix-free coupled contact operator for grains, ships, fragments and anchors; semismooth Newton with bounded GMRES directions |
| One representative point for a square/patch contact | Up to two clipped points on the selected physical face, stable feature identities and explicit exterior-face validation |
| Position lambda retained while contact geometry changes | A fresh minimum-displacement contact problem for each geometric linearization; no positional warm start |
| No cross-substep warm starting | Validated velocity-only warm starts, with cold-start acceptance and transactional cache rollback |
| Only 4/2, 8/4 and 12/6 profiles | Preserve those as historical comparators; V2 uses explicitly defined C1/C2/C3 caps and identical physical tolerances |
| Grain/solid interim runtime rejection .002 | Existing comparator keeps its historical setting; V2 requires original .001 for grain/solid and rigid/solid |
| No graph coloring/contact islands as an architectural principle | These are implementation techniques, not product prohibitions. The selected V2 path does not need coloring or islands; adding them later requires measured benefit and preserved finite-body coupling. |
| All-boundary scans and per-frame schedule recording | Indexed boundary proxies, compact active rows, segmented hull reactions and pre-recorded bounded GPU schedules |

User invariants remain binding: no per-cell GameObjects/Rigidbodies, matter deletion, overlapping cargo, duplicate inventories, CPU runtime contact fallback, automatic anchoring or hidden velocity reset. CPU submits commands and reads compact facts. GPU owns high-volume matter and every active body's authoritative motion. Sleeping, rendering class and resource limits never make loose matter immovable.

## 2. State and ownership

A grain is a world/site-space unit square with independent center, velocity, angle and spin. Its 48-byte public GPU record remains compatible with the candidate renderer. Mass is material density and inertia is mass/6. Cargo flags change classification only. Independent cargo mass is not added to attached hull mass.

Body state retains a 32-byte COM pose/motion record; parameters retain inverse mass/inertia, local COM, boundary range, mobility and shape revision. Hull origin is COM position minus rotated local COM. Ship and fragment endpoint slots are temporary runtime indices, never persistent IDs. Explicitly anchored endpoints have zero inverse mass/inertia and motion; dynamic endpoints retain finite positive mass/inertia.

CPU owns structure, units, tank inventories, material catalog, persistent IDs, topology and attached mass. GPU owns active endpoint motion, contact solving and step acceptance. Sessions own resources and one ordered graphics-queue schedule. Topology, door policy, mass and cache revisions publish together after prior work is fenced. Per-frame full-state readback remains forbidden.

## 3. Numerical and physical contract

The exact equations, branch derivatives, line searches, singular-system handling, tolerances, manifold construction and profiles live in the V2 architecture document; implementers must not substitute another contact law while keeping its profile label.

- Fixed dt 1/60; 4–16 speed-derived substeps, including spin/radius and commanded acceleration. No velocity clamping. Post-contact motion must still fit the declared substep/search envelope.
- Restitution zero; Coulomb friction .3, explicitly zero in analytical fixtures. Hard unilateral normal contact and non-associated friction; no contact springs or compliance.
- Position correction changes poses without writing physical velocities. Target slop remains .002 grain/grain and .0001 solid. V2 validates every substep at <=.01 grain/grain and <=.001 grain/solid and rigid/solid. After 120 unforced ticks residual grain penetration must be <=.002.
- Normalized linear momentum error <=1e-4 using max(1, sum(initial mass*speed)); isolated angular momentum error <=1e-3. Unforced kinetic energy, including spin, may not increase by more than max(.1% initial energy,.0001). Controlled forced tests account for external work separately.
- Preserve the 10,000:1 analytical speed check, 100000/10001 ± .0001, and 120-cell/s anchor/finite-body impacts without tunneling inside the 16-substep cap.
- Capacity, nonfinite, convergence, page, speed, envelope and physical-validation faults roll back the whole tick, including provisional contact caches and dependent gameplay operations.

All physical and throughput acceptance must belong to the same profile. A passing local contact test or reference arithmetic replay does not certify packed convergence. A diagnostic repeat of a failed attempt can measure its cost, but cannot qualify sustained throughput.

## 4. Gameplay and transaction boundaries

Cargo remains individual rotating grains inside real hull geometry. The GPU classifies only whole oriented squares inside a cavity. A 2,501st grain cannot acquire physical room through classification or tolerated compression. Door opening updates effective boundaries after a fence; obstructed closing stays open and preserves the obstructing grain. Suction acts only through the exterior mouth strip using the current GPU body pose.

Flight submits local force/torque and attached mass. Keep up to 16 submitted ticks and provisional fuel operations in FIFO order. Compact acknowledgements commit successful operations; a failed tick and later dependent operations consume no fuel or matter. Backpressure preserves simulation time. Saves and topology changes drain the queue.

Cutting reserves a grain slot/identity and validates placement before removing terrain. Shape, mass/COM, collider proxies, contact-cache generation and renderer views publish atomically. The V2 arena budget forbids keeping two complete solvers alive for routine reconfiguration; use the bounded fenced scratch protocol in the implementation plan.

Pre-existing strongest-impulse/feature capture in the dirty source is unfinished user work. Preserve it. Later damage events need accepted tick, body/feature, local contact point, converged normal impulse and effective energy based on pre-contact closing speed and true effective inverse mass. Retain the later policy `effectiveEnergy=.5*preContactClosingSpeed²/actualEffectiveInverseMass` and damage equivalent speed `sqrt(2*effectiveEnergy)`; this damage input is distinct from the actual contact velocity. Keep the strongest accepted event per body/tick pending until acknowledgement, with deterministic feature tie breaks. Detached matter inherits surface velocity and spin. They are not complete merely because a largest impulse is available.

## 5. Scale and measurement

The mandatory target remains 8,192 active grains, one finite ship, 16 finite fragments and 16 allocated 128² terrain chunks on M4 Pro/Metal/Unity 6000.3.11f1 at 1280x800, v-sync off and one fixed step per frame. Keep the exact existing dense bay/pile/sparse-exterior fixture. The V2 plan budgets 111 MiB of explicit pools under the unchanged 128 MiB ceiling; this is unmeasured planned capacity.

A profile qualifies only after 120 accepted warmup frames and 600 accepted measured frames with physics GPU p95 <=8 ms, total GPU p95 <=12 ms, frame p95 <=20 ms, CPU submission p95 <=2 ms and no steady-state submission allocations. GPU measurements must carry validated frame/tick identity. The implementation plan defines a tagged Metal timestamp bridge if Unity's timing cannot establish that identity. Unavailable timing remains unmeasured.

Boundaries use a spatial proxy index; finite hull reactions use segmented reductions. Global contact-matrix storage, all-site scans and serial work proportional to hull contact degree are excluded from the hot path. Large worlds use lossless stored matter and explicit activation, not hidden deletion or immovable budget-exhausted grains. Larger active counts, giant irregular hulls, streaming and other platforms require their own measured gates. The 10,000-grain exploration and 100,000-site experiment are deferred.

## 6. Execution and migration order

1. **V2-0:** exact replay capture, independent geometry, analytical/direct small cases and high-accuracy packed reference. Stop if no trustworthy reference exists.
2. **V2-1:** GPU manifolds/operator, high-degree finite hull tests, buffer accounting and schedule/product cost. Stop if the selected schedule cannot fit its engineering budget.
3. **V2-2:** complete Newton/GMRES velocity and fresh geometric solves, caches, strict validation and rollback; all physical fixtures.
4. **V2-3:** one stable fast suite and matching final build/player matrix; a common profile must pass all correctness and throughput budgets before adoption.
5. **V2-4:** port opt-in candidate flight/drilling/doors/cargo/suction through existing transaction boundaries, then verify actual player controls.
6. **R2 remainder, then R3:** complete damage/fuel transactions and exceptional cases; migrate checkpoint schema 5/sparse schema 3, travel and restoration. Save only authoritative physical state; rebuild contact caches cold.
7. **R4:** pass full B.GATE, switch default explicitly, then remove superseded solver/occupancy/retry paths and unused duplicate compute assets. Historical evidence and old schema readers remain.

Schema migration preserves every world-grain identity/material/fuel residual and body pose/motion/mobility. Legacy external lower-left positions become centers by +.5; ship-local cargo transforms into site coordinates with preserved world velocity and inherited ship angle/spin as specified by its old encoding. Do not invent motion for external grains. Travel transforms selected cargo's relative velocity and spin with the ship, preserving deposited matter. Exact restore and bounded continued-step/long-run comparisons are mandatory.

The old gatherer is preserved in [baseline evidence](evidence/B3R-redesign-baseline/unfinished-contacts.patch). Current implementation remains the restored source recorded in the legacy checkpoint. Planning V2 neither deletes that unfinished work nor verifies the new architecture.

## Historical scoped evidence

## B.3R.1–B.3R.2 checkpoint

Attached mass uses material density, whole-machine mass once, and fuel-grade density. Cached structure/machinery sums produce COM and inertia; free cargo is excluded. Gameplay submits local engine forces and mount/control torque; GPU fixed steps own motion. The CPU collision-readback hard stop is removed. The analytical oracle covers the 10,000:1 inelastic collision, anchored/glancing contacts and off-centre torque.

The GPU single-writer solver wakes and exchanges equal/opposite impulses with free cells before integration. Four substeps and a 0.0001-cell geometric skin avoid the observed translated starter-hull rounding fallback; skin correction does not change velocities. This is a bounded first contact proof, not the final island/budget solver. The legacy displacement API remains for historical fixtures.

Canonical evidence: [43 passing tests](evidence/B3R-single-cell-tests.xml), [standalone acceptance and rerun reasons](evidence/B3R-single-cell-player.txt). A mass-1 pixel reduced starter speed from 10 to 9.994825; thrust continued, zero fallbacks, no overlap, motion survived save/load. Mac frame p95 17.595 ms; GPU p95 2.394 ms. Remaining work: cell chains, free-cargo coupling, fragment/anchor impulses, high-speed substeps and physics persistence/migration.

## B.3R.3 checkpoint

Cell pairs/piles now exchange equal/opposite impulses; serial retries advance packed trailing cells after leaders. Free cargo uses an exact next-pose frame conversion with world velocity unchanged; contacts supply load, without adding cargo to hull mass. Fragments have finite material/unit mass and inertia, exchange impulses with cells/ship/anchors, and inherit surface motion. Anchored walls remove approaching normal velocity while preserving tangent; cutter-released cells remain dynamic. Damage/save synchronization no longer zeroes motion from collision flags.

[49 tests](evidence/B3R-island-tests.xml) and the [combined Mac player](evidence/B3R-island-player.txt) pass scoped physics acceptance. The 201-cell workload conserved momentum within 0.323917 mass-cell/s, reduced energy, rotated the impacted fragment and remained non-overlapping. **Performance fails:** frame p95 324.382 ms; GPU p95 324.505 ms. It also records 29 pose fallbacks during convergence. That checkpoint proposed gathered serial contacts; the parallel redesign contract above supersedes that proposal.
