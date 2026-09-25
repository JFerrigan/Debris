# Next implementation assignment — V2-0 reference and replay

Use this assignment after the documentation-only V2 design checkpoint. Retain the user's selected model and effort. Do not inherit the archived Terra prompt's assignment or any model preference from its name.

## Assignment

Work in `/Users/jakeferrigan/Documents/Debris`, using one agent. Implement **V2-0 only**, as defined in [CONTACT_SOLVER_V2_IMPLEMENTATION](CONTACT_SOLVER_V2_IMPLEMENTATION.md#3-v2-0--replay-geometry-and-reference-decision). Stop at the reference/formulation decision before implementing the GPU Newton solver or V2-1.

Start with `git status --short`, `docs/STATUS.md`, the current B.3R block of `docs/EXECUTION_PLAN.md`, `docs/CONTACT_PHYSICS.md`, and `docs/evidence/B3R-physics-viability.md`. Read PROJECT_PLAN once. Then read `docs/CONTACT_SOLVER_V2.md` and implementation-plan sections 1–3, 6, 11–12. Preserve all pre-existing tracked and untracked changes, especially unfinished impact capture. Capture HEAD, dirty patches and exact source hashes before editing any dirty file. The documentation checkpoint changes no simulation source; `ceca0a9` plus the source hashes in the evidence identifies the restored runtime baseline.

The selected replacement is a global matrix-free semismooth Newton contact solve with GMRES, two-point clipped manifolds and fresh mass-weighted position systems. This is planned architecture, not an accepted implementation. Scalar over-relaxation and 4x/8x Jacobi sweeps were rejected. All legacy 4/2, 8/4 and 12/6 profiles fail packed correctness; their throughput remains unmeasured. Do not perform another sweep-count experiment.

## Required result

1. Preserve and hash every available original/prototype archive separately. An old shared-4/2 archive was overwritten; use only the surviving originals identified by the checkpoint. Label old post-force `SubstepStart` reconstructions as approximate. Add immutable exact committed pre-`Step` capture, commands and source identity with exclusive file creation; reproduce the unchanged comparator's fault. Do not overwrite inputs with traces.
2. Implement independent Float64 vertex/SAT/clipped-edge geometry, exterior-feature ownership and containment checks. Prove one/two-point contacts, diagonal ties, seams and concave corners. Preserve the exact dense fixture.
3. Implement the independent tiny direct active-state reference and packed block sequential reference with the explicit limits in section 3. Add meaningful analytical mass/inertia, friction and torque checks. An offline Editor reference is authorized; a runtime CPU contact fallback is prohibited.
4. Implement the specified double-precision V2 residual/derivative and bounded solve as a separate reference path. Check derivatives, matrix-free products and physical endpoint results against the independent reference. Do not treat matching shader arithmetic as proof of convergence or require unique impulses in a redundant contact system.
5. Resolve every saved packed input to the strict `.001` solid and `.01` grain bounds, with the declared residual and conservation checks, or stop with the specific unresolved geometry/reference issue. Record one-step endpoint differences, cold-cache behavior, true residuals, iteration counts and reference timeouts. No GPU-speed claim follows from CPU reference timing.
6. Create `docs/evidence/B3R-v2-convergence.md` with exact inputs, source identities, failed attempts and the advance/stop decision. Update STATUS, EXECUTION_PLAN, PERFORMANCE and the feature-batch ledger. Stage only explicit task paths, commit and push.

No threshold relaxation, density reduction, automatic anchoring, hidden overlap, disabled faults or matter deletion. Retain all three legacy profiles in the comparison record. C1–C3 are separate V2 contracts. Damage/fuel, persistence, travel, default cutover and the exploratory 10,000-grain workload remain deferred.

Run focused meaningful Editor tests. Run one fast suite if shared runtime/replay contracts change. Pure offline-reference work needs no Mac build; any GPU/player integration change requires its relevant verification before acceptance. Never continue after a timed-out or zero-test run as though it passed. Do not rebuild the unchanged solver solely to repeat stored performance failure.

Final response: reference/geometry result, exact source/input identity, analytical and saved-state outcomes, failed or timed-out attempts, tests, commit, remaining uncertainty and decision. The next task is V2-1 GPU geometry/operator plus scheduling-cost feasibility **only if V2-0 passes**; otherwise name the precise formulation issue to resolve.
