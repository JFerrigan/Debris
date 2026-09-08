# Contact physics — Phase B correction

Status: B.3R.1–B.3R.2 verified for isolated ship–cell contacts; B.3R.3–B.3R.4 remain open ahead of B.5 expansion. The user's 2026-09-07 report supersedes the earlier conclusion that collision admission alone completed B.3.

## Problem and decision

`ShipMatter.hlsl::MoveShip` rejects a whole pose and sets all velocity to zero when any hull/cargo query is blocked. `FragmentMatter.hlsl::MoveFragment` does likewise. `Showcase.Update` then copies the collision flag into a CPU hard stop. This prevents overlap but makes one loose cell behave like anchored terrain. Raising thrust or ignoring small contacts would not fix momentum exchange.

Keep the custom GPU material engine. Add finite-mass rigid ship/fragment bodies and finite-mass loose cells, driven by forces and resolved with contact impulses. Use explicitly anchored bodies for planets, home bases and designated giant mining bodies. Storage in a material field does not itself imply infinite mass. This uses established rigid-body concepts; it does not require adding Unity Rigidbody objects for cells. [Body types, forces and impulses](https://box2d.org/documentation/md_simulation.html).

## Body classes and ownership

| Mode | Response | Intended use |
|---|---|---|
| Dynamic | Finite mass/inertia; responds to thrust, torque and contacts | Ships, loose material, salvageable fragments and movable asteroids |
| Anchored | Zero inverse mass/inertia; fixed site-relative transform | Planets, home bases, generator-designated giant bodies |
| Scripted/kinematic | Prescribed motion; contacts use its surface velocity | Explicit future mechanisms, not an automatic performance fallback |

Choose mobility once from an authored/generator policy when a body is created; persist that choice. A mass/extent threshold may classify eligible generated giant bodies, but it must not silently anchor a player ship, a growing cargo load or an existing dynamic fragment. Set numerical thresholds from measured active budgets. Sleeping preserves finite mass and wakes on meaningful contact/thrust. It is distinct from anchoring.

Anchored matter remains mineable and destructible. Removed cells always become dynamic. Disconnected regions become finite-mass fragments when admitted; oversized regions retain their full records while streaming/admission catches up. Do not turn them into permanent immovable obstacles to hide a budget problem. A fragment leaves a moving parent with inherited linear and angular surface velocity. Anchored bodies exchange momentum with the external world; momentum conservation assertions apply to closed dynamic systems, not to the moving subset beside an anchor.

## Mass, thrust and cargo

- Use consistent cell-distance, second and material-mass units throughout. Cell mass is density × canonical cell volume (with a declared effective thickness for the 2D model). Do not mix render metres with cell-space physics.
- Derive rigid-body mass, centre of mass and moment of inertia from attached structural material, whole-unit definitions and tank contents. Cache structural sums; update on topology/inventory changes. Whole-unit footprints contribute their unit mass once, not a second mass for every rendered machinery pixel.
- Mass/centre-of-mass changes must preserve the intended world transform and account for transferred material momentum. Fuel spills and detached matter inherit local surface velocity; updating a mass cache must not invent an impulse.
- Thrusters submit force at mount points and torque: `dv = F * dt / M`, `dω = τ * dt / I`. The GPU owns the authoritative fixed-step pose/velocity. CPU flight code stops prescribing candidate translations and zeroing velocity from delayed readbacks.
- Each free cargo cell contributes its mass once as a dynamic cell. Do not also add it to the rigid hull's inertia and then apply equal-and-opposite cargo impulses: that double-counts the load. Cargo affects the ship through wall/cell contacts; packed cargo exhibits greater effective inertia once coupled. A HUD may still show total carried mass. Future explicitly secured cargo can join a rigid aggregate only after removing its independent dynamic contribution.
- Ship-local cargo coordinates remain an optimization. Compute world contact positions and velocities consistently, including the hull's angular surface velocity. A frame conversion must not accelerate a free cell or change its momentum.

## Contact solution

For contact normal `n` from A to B, offsets `rA/rB` from their centres of mass, and surface relative normal velocity `vn = dot(vB + ωB×rB - vA - ωA×rA, n)`:

```text
k = invMassA + invMassB
    + (rA × n)² * invInertiaA + (rB × n)² * invInertiaB
j = -(1 + restitution) * vn / k       # isolated approaching contact, vn < 0
impulseA = -j*n; impulseB = +j*n
```

Cells may initially remain non-spinning unit squares; their rotational term is zero. Ships and fragments need rotational response. Use accumulated, non-negative normal impulses, bounded Coulomb friction and a separate penetration correction that does not inject arbitrary kinetic energy. Restitution is low by default for salvage; thresholds prevent low-speed chatter. Persistent contacts must not repeatedly receive a fresh impact bounce. [Sequential impulse and substep implementation principles](https://box2d.org/posts/2024/02/solver2d/).

Gather unique contacts from swept hull/fragment surfaces and existing spatial cell buckets. Merge redundant hull-pixel contacts for the same physical cell/normal; do not multiply force by the number of sampled hull cells. Resolve dense cell chains and anchored contacts in the same local contact island. A free pile can be pushed; a pile trapped against a station can legitimately resist or stop the ship.

Start with a CPU reference oracle and a bounded GPU sequential-impulse implementation with stable contact ordering and exclusive ownership of body updates. Process contacts in tiles; avoid competing float writes to ship velocity. Merely summing thousands of independently computed impulses against the same stale ship velocity can overshoot and is not an acceptable dense-pile solver. Evaluate graph coloring or relaxed parallel solving only if the measured implementation needs it. Warm starting and deterministic reduction are refinements, not a prerequisite for the first single-cell proof.

Use fixed steps, swept tests and adaptive substeps to prevent tunnelling. The current per-step movement cap must not clamp away a cell's new momentum when a much heavier ship pushes it. At an anchored wall remove the approaching normal component through the solver; preserve tangential motion subject to friction. Reserve hard pose rejection for unresolved overlap/exhaustion recovery, expose that fallback diagnostically, and fail the ordinary free-pixel gate if it activates there. Bounded contact-buffer overflow retains cells and unresolved island work; it must not silently discard collisions.

Apply damage after impulses using effective impact energy/impulse and material/unit thresholds, rather than ship speed alone. A slow grain should not destroy a thruster. A sufficiently fast small projectile can still cause damage. Deduplicate contact damage across substeps and update mass/ownership after actual breakage.

## Implementation batches and acceptance

| ID | Deliverable | Required proof |
|---|---|---|
| B.3R.1 | Body state, mass/inertia and CPU force/contact oracle | Analytical two-body impact; anchored/glancing contact; attached-mass accounting |
| B.3R.2 | Ship ↔ free-cell GPU impulses and force-driven flight | One free cell moves aside; ship keeps momentum/thrust; no overlap or tunnelling |
| B.3R.3 | Piles, cargo, fragments, anchoring and sleep | Dense contacts stay stable; load counted once; off-centre impacts turn fragments; anchored chips become dynamic |
| B.3R.4 | Save/restore and final player gate | Body modes, velocities, inertia inputs and solver phase restore consistently; measured player scenario passes |

Use a frictionless, zero-restitution reference collision with ship mass 10,000, one stationary cell of mass 1 and initial ship speed 10. Expected shared normal speed is `100000/10001 ≈ 9.9990001`; ship slowdown is approximately 0.01%, not a full stop. With a compact pile, compare transfer against the mass actually coupled by contacts over the measured interval; simply counting nearby cells is not a drag law. Verify equal/opposite impulses and no unexplained energy increase with external forces disabled.

The final fixture covers isolated cells, 100/1,000-cell piles, a pile against an anchor, a loaded rotating cavity, glancing walls, a dynamic fragment and a fast small projectile. Check low/high frame rates against the same fixed-step sequence, save/load during motion, contact-budget saturation and wake-up. Record mass ratios, thrust, momentum error, penetration tolerance, contact count, solver iterations/substeps, fallback/overflow counts and frame/GPU p95. Target the existing 60 FPS goal / 20 ms minimum-budget threshold on the documented Mac preset; choose numerical tolerances before running the oracle, not after observing failures.

Persist authoritative body mobility, linear/angular velocity, structural/inventory inputs to mass and any required unfinished solver work. A save occurs at a completed fixed-step boundary. If warm-start caches are omitted, rebuild consistently and document/test the resume tolerance; do not claim bit-identical continuation with a different hidden solver state. Legacy terrain retains its anchored generator policy, while old loose cells migrate to finite dynamic mass. Original files remain intact on unsupported migrations.

## B.3R.1–B.3R.2 checkpoint

Attached mass uses material density, whole-machine mass once, and fuel-grade density. Cached structure/machinery sums produce COM and inertia; free cargo is excluded. Gameplay submits local engine forces and mount/control torque; GPU fixed steps own motion. The CPU collision-readback hard stop is removed. The analytical oracle covers the 10,000:1 inelastic collision, anchored/glancing contacts and off-centre torque.

The GPU single-writer solver wakes and exchanges equal/opposite impulses with free cells before integration. Four substeps and a 0.0001-cell geometric skin avoid the observed translated starter-hull rounding fallback; skin correction does not change velocities. This is a bounded first contact proof, not the final island/budget solver. The legacy displacement API remains for historical fixtures.

Canonical evidence: [43 passing tests](evidence/B3R-single-cell-tests.xml), [standalone acceptance and rerun reasons](evidence/B3R-single-cell-player.txt). A mass-1 pixel reduced starter speed from 10 to 9.994825; thrust continued, zero fallbacks, no overlap, motion survived save/load. Mac frame p95 17.595 ms; GPU p95 2.394 ms. Remaining work: cell chains, free-cargo coupling, fragment/anchor impulses, high-speed substeps and physics persistence/migration.
