# Contact solver V2 — implementation, measurements and cutover

Revision: 2026-09-24. **All V2 implementation gates are open.** This plan implements [CONTACT_SOLVER_V2](CONTACT_SOLVER_V2.md); it does not claim that the proposed solver converges or meets its budget. Current runtime evidence remains the failed [legacy checkpoint](evidence/B3R-physics-viability.md). This document supersedes the execution instructions of [PHYSICS_VIABILITY_PLAN](PHYSICS_VIABILITY_PLAN.md), which remains the historical fixture/evidence specification.

## 1. Deliverable and sequencing rules

The next implementation assignment is **V2-0: establish exact replay inputs, independent contact geometry and an independent converged reference**, followed by the bounded batches below when their prerequisites pass. The reference must distinguish an implementation that follows an equation from an equation that satisfies physical constraints. Finish V2-0's decision before writing the GPU Newton solver.

One agent; retain the user's selected model/effort. Start from `git status --short`, STATUS, current EXECUTION_PLAN and PROJECT_PLAN once per fresh context. Read the owning V2 sections and relevant source. Preserve pre-existing tracked and untracked changes, especially impact capture. Create a task-specific manifest of HEAD, dirty patches and SHA-256 hashes before changing dirty source. Stage explicit paths. Commit coherent accepted batches with evidence under existing ordinary push authorization.

Damage/fuel remains blocked until V2-3 and V2-4 pass; persistence/travel then require completed R2, and default cutover requires completed R3 plus the full B.GATE lifecycle. Do not implement a larger active population in these batches. Later sections define their integration contracts now so implementation does not have to invent them later; they are not permission to bypass the solver gate. The 10,000-grain experiment and 100,000-site workload remain deferred.

## 2. Source ownership and modules

Keep the failed original solver as the opt-in comparison path during V2 development. Add one concrete replacement namespace/directory, `Debris.Simulation.CoupledContacts`, rather than extending `ParallelGrainSolver` with solver-family flags in its inner loop. A small construction-time selector in the proof runner and later candidate session chooses one implementation; no shared live state crosses implementations.

| Planned file/subsystem under `Assets/Debris/` | Responsibility and boundary |
|---|---|
| `Simulation/Runtime/CoupledContacts/CoupledContactSolver.cs` | Fixed arena ownership, command schedules, Step, completion, snapshot, topology publication and disposal |
| `CoupledContactTypes.cs` | Explicit layouts, profile values, limits, fault enum and readback DTOs |
| `CoupledContactArena.cs` | Named slices, byte accounting, lifetime/alias assertions; no per-frame allocation |
| `CoupledContactSchedule.cs` | Pre-recorded profile schedules, GPU control offsets, stage dependency validation |
| `CoupledContactCache.cs` | Stable contact keys, full-key matching, committed/provisional cache ownership |
| `Simulation/Resources/CoupledGeometry.hlsl` | SAT reference-face selection, clipped two-point manifold, local anchors, exterior-face validation |
| `CoupledBroadphase.compute` | Swept grain/proxy index, canonical pairs, deduplication and capacity detection |
| `CoupledOperator.compute` | Point rows, CSR spans, segmented endpoint gather and J/W/J-transpose application |
| `CoupledSolve.compute` | Projected residual/derivative, block factors, Arnoldi/Givens/vector operations, line search and GPU control |
| `CoupledValidate.compute` | Independent overlap query, residual/conservation/identity reductions and atomic tick publication |
| `Simulation/Tests/Editor/CoupledContactReference.cs` | Independent double geometry, small direct active-set checks and block sequential reference; Editor only |
| `CoupledReplayArchive.cs` in test/diagnostic scope | Immutable committed pre-step input plus commands, source identity and outputs; not save schema |
| `Presentation/Runtime/CoupledViabilityRunner.cs` | V2 profile matrix using the existing fixture recipe and renderer; source-tagged compact evidence |
| `Presentation/Runtime/ProofGpuFrameSamples.cs` | Frame/tick identity, timestamp ring, completeness and p95 calculations |
| `Plugins/macOS/DebrisGpuTiming` source/build recipe, if required below | Small Metal timestamp bridge only; no authoritative contacts or simulation decisions |

Reuse public grain/body layouts, material lookup, proof fixture constructors, procedural rendering, topology command contracts and compact gameplay completion patterns. Do not import the legacy degree-split effective mass, single representative point, accumulated position lambda across geometry changes, or separate rigid-only iteration schedule.

Runtime tests must instantiate the intended solver explicitly. Existing legacy tests remain recognizable as legacy evidence; changing a constructor default cannot silently relabel them V2 coverage. Shared layout/renderer/session tests run against both implementations where their boundary is unchanged.

## 3. V2-0 — replay, geometry and reference decision

### Inputs and immutable archives

Create a replay input before invoking `Step`, containing every committed grain/body/parameter/boundary record, stable IDs and slot generations, external commands, dt, selected profile, friction, page origin, current committed tick, topology revision and relevant cache state. Include an explicit cold-cache variant. Write a versioned binary archive with SHA-256 and a compact JSON manifest. Name its directory with UTC, solver revision and source hash. Creation uses exclusive create; an existing archive path is an error. Output traces live in another uniquely named directory.

Preserve the original saved traces cited in the checkpoint. Five originals are in `/private/tmp/b3r-diagnostic-tests`; shared 4/2's older archive was overwritten, while a separate original exists in `/private/tmp/b3r-current-diagnostic-tests`. Prototype outputs live in `/private/tmp/b3r-overrelax-tests` and the newer evidence files. Hash available inputs before use and record absent files as absent. Never replace a missing original with a regenerated state without labeling its provenance.

Old `SubstepStart` traces are taken after force preparation. Reversing a floating-point force increment is not guaranteed to recover the committed input bit for bit. Label such reconstruction as approximate. To obtain exact new pre-step inputs, run the unchanged comparator once and capture its committed snapshot before the failing attempt. Reproduce its fault and report trajectory differences. Prefer exact inputs for all future differential comparisons; preserve approximate historical replay as supplemental evidence.

### Independent reference

Use Float64 geometry implemented independently of the shader helper: transform explicit square/rectangle vertices, perform separating-axis checks, clip incident edges and compute polygon intersection/face gaps. Do not translate the shader line for line and call agreement independent geometry validation.

For a single manifold, enumerate each point's four states: open, sticking, sliding positive and sliding negative. At most two points gives 16 combinations. Solve each resulting <=4x4 linear system using a rank-revealing double SVD, test all unilateral/sliding inequalities and select a valid minimum-norm impulse with a stable tie break. Frictionless tiny systems additionally enumerate global active normal sets for up to eight points. These cases establish expected mass response and branch transitions without relying on Newton/GMRES.

For full packed archives, use double block projected Gauss–Seidel over complete manifolds with immediate equal/opposite endpoint updates and the independent local block solve. Permit up to 100,000 sweeps or 600 seconds per input, including geometry refresh, and require normal/friction velocity residual <=1e-8 cell/s and position residual <=1e-8 cell. This is an offline oracle, never linked into player assemblies. A timeout/nonconvergence is an inconclusive reference result, not proof of physical infeasibility and not permission to continue to GPU adoption.

Implement the specified V2 residual and derivative separately in double precision. At smooth branch interiors check analytic H*z against centered finite differences with scale-aware steps; test branch boundaries through one-sided derivatives and direct physical inequalities. Check A symmetry/positive semidefiniteness for frozen geometry, true diagonal mass, common-point torque balance and equivalence of explicit small A with matrix-free products.

### Required diagnostic cases and decision

| Case | Question resolved |
|---|---|
| Flat square on flat anchor, two points; off-center loading | Does the manifold support torque without artificial point rocking? |
| Square/rectangle sliding through an equal-depth diagonal corner | Are tied SAT axes overconstraining the contact? |
| Two grains; mass ratio 10,000:1; anchored and finite counterparts | Are impulse signs, inverse masses and friction branches correct? |
| 8, 32 and 50 touching squares between finite boundaries | Does the reference propagate a boundary displacement without deleting available volume? |
| Canonical 50x50 exact fill under shared rotation/translation and forced motion | Is there a feasible coupled correction under the actual geometry? |
| Original saved shared/resting attempts for all three legacy profiles | Which failures remain with valid manifolds and a converged solve? |
| Redundant flat support, duplicate-feature rejection, two points becoming one | Are singular multipliers handled without changing the physical state? |
| Sliding with friction and no external normal load | Does the model avoid artificial normal lift/dilation? |

Capture error by stable point/feature before prediction, after prediction, after each position linearization and after independent final validation. Distinguish gap, velocity complementarity, tangential slip/friction-bound error and solver equation residual. The reference must demonstrate accepted physical endpoint motion, not merely smaller aggregate residual.

**V2-0 pass:** independent geometry and tiny analytical checks pass; the reference resolves every saved packed input to the strict physical bounds, or identifies a concrete geometry/fixture defect that is corrected without reducing density or changing the intended workload; V2 double residual reproduces that accepted state within 1e-5 cell and 1e-5 cell/s for the one-step comparison. Impulses may differ when nonunique; compare endpoint motion and physical residuals. If no trustworthy reference is obtained, stop with the exact unresolved constraint and revise the formulation. Do not spend a build/player cycle measuring an unvalidated new solver.

## 4. V2-1 — GPU geometry/operator and scheduling feasibility

Implement layouts, indexed broad phase, two-point manifolds, CSR and the J/W/J-transpose product. First run Metal compiler/layout and isolated-contact smoke checks, inspecting repeated warnings as well as errors. Then compare GPU products against explicit double small matrices and frozen full replay products. Verify endpoint degree affects work distribution but never changes effective mass.

Include a synthetic finite hull with 200 and then 4,096 incident point constraints, alongside grain pairs and the 16 fragment endpoints. Validate that segmented reductions have no missing/duplicated reactions, all dynamic endpoints move according to their mass, and the number of sequential product stages is independent of hull degree. Check empty grain populations, zero contacts and exactly full capacities.

Construct the complete masked command schedules for C1–C3 before implementing their full arithmetic. Time submission and GPU processing of converged/zero-work schedules; include all recorded substep/Newton/GMRES/line-search command overhead. Also time repeated active matrix-free products using the 8,192 fixture's frozen contact graph. This determines whether the proposed dispatch strategy is credible on Unity/Metal.

Engineering gate: at least one profile's masked schedule must use <=1 ms GPU p95 and <=.25 ms CPU submission p95 at 1280x800, leaving room within the unchanged 8/2 ms final limits. These tighter scheduling ceilings are allocation targets, not substitute throughput acceptance. Missing valid timing is an instrumentation block. Failure stops before gameplay integration; record whether command processing, reductions or memory traffic dominates. A native compute scheduler, a different Krylov scheme or changed profile caps requires an explicit design amendment, not an unrecorded optimization that changes the algorithm.

Use the timing bridge in section 8 when Unity samples cannot provide verified identity. The source code of a small instrumentation bridge is part of this batch if needed; no platform-wide plugin redesign is authorized.

## 5. V2-2 — complete coupled step and strict correctness

Implement the exact projected residual, branch derivative, manifold preconditioner, GMRES, line search, cache matching, fresh position systems and independent validation from the architecture document. Use the bounded C1–C3 profiles. Write trace stages for residual evaluation, linear solve, accepted candidate, geometry refresh, validation and final publication. Trace capture is opt-in and capacity-bounded; trace truncation cannot masquerade as complete diagnostic evidence.

Run identical exact replay inputs first. Verify branch masks, each operator product, true linear residuals and final endpoint changes against V2-0. Then run canonical initial-state trajectories. A high-quality one-step replay is necessary but insufficient: the unforced 720-tick extension checks accumulated drift and cold/warm start transitions.

The whole tick is the rollback unit. Deliberately fail each stage after nonzero working movement and verify committed grains, identities, material/fuel records, bodies, cache generation, cargo facts, terrain revisions and completed tick remain unchanged. Later queued ticks must return dependency failure without publishing partial state. Keep running diagnostic reductions for the failed stage; do not suppress the largest overlap because an earlier thread latched a fault.

### Fault taxonomy

| Class | Required fault examples | Recovery policy |
|---|---|---|
| Input/definition | invalid mass, mobility, duplicate identity, invalid topology revision | reject command before GPU mutation |
| Capacity | candidates, incident degree, manifold/point/CSR pool, rigid pair, proxy references, cache/control ring | reject whole tick/admission; preserve matter; explicit reconfiguration needed |
| Numerical | nonfinite, invalid diagonal, linear breakdown, Newton stagnation, geometry line-search exhaustion, residual above tolerance | reject whole tick; retain compact failure facts and optional trace |
| Coverage/time | speed >16-substep contract, post-contact SubstepBound, page escape, correction/gather envelope | reject whole tick; no clamping/freeze/tunneling |
| Physical | grain penetration, grain/solid penetration, rigid/solid penetration, invalid conservation/energy in controlled fixtures | reject and fail acceptance; do not continue into timing qualification |
| Infrastructure | shader error, readback error, missing/duplicate frame timing, native timestamp unsupported | mark measurement/session failed; never count as a physics pass |

Keep runtime conservation faults limited to quantities with known external-work accounting. Isolated-fixture momentum/energy acceptance is mandatory; force-driven gameplay cannot compare final energy against a no-work bound. Record external impulses and the discrete integrator's work estimate so diagnosis can separate input work from numerical growth.

## 6. Mandatory physical acceptance matrix

All R1 proof rows must pass under the **same V2 profile**. C1/C2/C3 each run the proof matrix and receive separate correctness/throughput verdicts. The candidate-integration row is executed at V2-4 using a qualifying profile; it is not a circular prerequisite for V2-3. Analytical tests may use friction=0 as explicitly specified; workload tests use .3. Float tolerances below are physical gates, not arbitrary assertion margins.

| Family | Required observations |
|---|---|
| Single/two-body analytic | Mass-10,000 body initially at 10 cells/s hitting a stationary mass-1 grain centrally, friction=0 and restitution=0: shared normal speed `100000/10001 ± .0001`; two equal grains; explicit anchor; off-center spin; no loose-body anchoring or velocity reset |
| Conservation | normalized linear momentum error <=1e-4 using `max(1,sum initial mass*speed)`; isolated angular error <=1e-3; unforced kinetic energy increase <=max(.1% initial energy,.0001), including spin |
| Flat support and sliding | true two-point contact; stable flat support; friction stick/slide/zero-friction transitions; no artificial frictional normal lift |
| Dense transfer | 100- and 1,000-grain piles; force transfer through 8/32/50-cell rows; finite hull/fragment reaction and torque |
| Canonical packed | unchanged 2,500 grains in 50x50 cavity; original shared and forced inputs; 120 accepted ticks each; .001 solid maximum over every validated substep |
| Extended packed | shared motion through 720 ticks; separate forced case with force/torque only ticks 1–120, then 600 unforced ticks; grain residual <=.002 after 120 unforced ticks and at completion |
| Fast impacts | mass-1 grain at 120 cells/s against one-cell anchor and finite mass-4 body; no tunneling; finite momentum exchange; <=16 substeps; deliberate over-speed rollback |
| Capacity/cargo | 2,500 whole grains accepted physically; a 2,501st full grain cannot be hidden by overlap/classification; test clear rejection with conserved identities and an exterior grain retained |
| Candidate integration | flight, drill release, clear/open/obstructed/queued door closure, rotated whole-square cavity classification, compact capacity facts and mounted suction using current GPU pose |
| Faults | every class in section 5; unchanged committed state and cache on failure; no tool/fuel commit from failed or dependent ticks |
| Long-contact graph | all 16 fragments actually participate in contact in the unchanged combined run; exact active identity count remains 8,192 |

For isolated angular momentum, use the fixed initial system COM as origin and include orbital `cross(position-origin,mass*velocity)` plus `inertia*spin`. Normalize error by `max(1,sum(abs(initial orbital contribution)+abs(initial spin contribution)))`; the <=1e-3 bound applies to that normalized error. Never move the measurement origin between before/after samples. For linear momentum, use the vector difference norm and the denominator in the table. Anchor fixtures report reaction to the anchor separately and are not mislabeled closed-system conservation.

Position maximums are <=.01 grain/grain and <=.001 grain/solid and rigid/solid. Check all manifold points and an independent geometry pass after every substep. Separately report predicted penetration and final post-correction maximum. Historical legacy runtime .002 remains a comparison fact; it cannot qualify V2.

The complete R1 proof also needs the existing 201-cell mixed layout, free spinning cargo and cold snapshot restoration. Save-format implementation remains later, but constructing a new solver from an exact in-memory committed snapshot must reproduce initial state and continue within .0001 cell/.001 cell/s after one tick, with acceptable conservation through 120 ticks. Warm cache is not serialized or required for this test.

## 7. V2-3 — unchanged combined fixture and measurement modes

Preserve `ProofViabilityFixture` revision 2: 2,500 bay + 1,000 touching pile + 4,692 sparse exterior grains; one finite ship, 16 finite fragments, anchored 42-cell platform; 16 allocated 128² terrain chunks; material 1/density 1; original positions, velocities, mass/inertia, forces and camera. Use the existing procedural grain/rigid/sparse-terrain renderer at 1280x800, Metal, v-sync off, frame cap -1, one fixed tick per presented frame. Record that terrain rendering is a controlled equivalent, not full production terrain rendering.

New entry point: `-debrisCoupledProof -debrisProofViability`, with existing absolute `-debrisProofOutput` and `-logFile` semantics. Legacy `-debrisParallelProof` retains its meaning. Runner manifests record solver family and full profile fields; a label such as `8/4` cannot conceal 32 position sweeps or new Newton limits.

There are three distinct measurements:

1. **Operator/schedule cost:** V2-1 masked schedule and repeated active products. Records exact contacts, dispatches, bytes and timing. It establishes instrumentation/implementation cost only.
2. **Failed-attempt replay cost:** repeatedly restore the same saved committed GPU input and execute one genuine attempted tick, preserving faults and their observed results. Warm 120 independent attempts and sample 600 if instrumentation allows. Report reset/upload cost separately and total replay cost including reset. Mark every sample as replay; no successful tick progression or throughput qualification is claimed. This permits measuring an expensive failed solve instead of calling all solver cost unknowable merely because the live trajectory faults early.
3. **Live qualifying workload:** 120 consecutive accepted warmup ticks followed by 600 consecutive accepted measured ticks in the unchanged combined fixture. Any fault, missing sample, pause, unacknowledged tick or frame discontinuity invalidates the profile's qualifying window. A delayed fault cannot leave later no-op frames in the timing array.

The same profile must pass all physical cases and every live budget: physics GPU p95 <=8 ms, total GPU p95 <=12 ms, frame p95 <=20 ms, CPU submission p95 <=2 ms, explicit buffers <=128 MiB and zero steady-state managed submission allocations. Calculate p95 by nearest rank `sorted[ceil(.95*N)-1]` with N=600. Missing timing is `unmeasured`, never zero, NaN-as-pass or CPU-derived GPU timing. Report the worst accepted and attempted cases even if another profile qualifies.

Run the old three profiles again only if the runner, geometry they use or shared measurement infrastructure changed materially; otherwise cite the source-matched stored results from `ceca0a9`. Preserve all three legacy rows in the comparison table. Never combine legacy cost with V2 correctness or C3 correctness with C1 cost.

## 8. Frame identity and GPU timing implementation

Every submitted frame carries a 64-bit run ID, frame ordinal, tick ID, solver/profile revision and topology generation. Keep a preallocated 2,048-entry CPU sample table and a 32-slot GPU/native ring. A slot cannot be reused until its completion and timestamps have been consumed. Full rings backpressure the benchmark and invalidate that timing window rather than overwrite evidence. Collect at most 16 queued simulation ticks as the existing session contract permits; the qualifying runner still submits exactly one per presented frame.

Retain the Unity physics Recorder cross-check: its documented three-frame delay is accepted only when the retrieved sample maps to the recorded source frame, has exactly one marker block and corresponds to an acknowledged successful tick. Detect duplicated/stale samples. [Unity Recorder API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Profiling.Recorder-gpuSampleBlockCount.html).

`FrameTimingManager` reports frame timestamps but does not directly provide the application's tick token. Do not assign its total GPU sample by array position or assume that its delay equals the Recorder delay. Keep those observations as an independent diagnostic until a verified mapping exists. [Unity FrameTiming fields](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/FrameTiming.html).

The selected fallback for the required Mac measurement is an explicitly tagged Metal counter bridge:

1. Load a small native timing plugin using Unity's published native graphics interface. Use `IUnityGraphicsMetal`'s current command buffer on the render thread; never create a separate physics queue. Pin the header/API revision to the installed Unity editor in the source manifest. [Unity Metal interface](https://github.com/Unity-Technologies/NativeRenderingPlugin/blob/master/PluginSource/source/Unity/IUnityGraphicsMetal.h).
2. Query timestamp-counter and sampling-point support. Prefer compute-pass stage-boundary sample attachments, which Apple documents for devices supporting `atStageBoundary`. Use a one-thread token-writing marker dispatch so each marker pass performs real work. End the current encoder only through Unity's supported interface before opening the marker encoder; do not commit Unity's command buffer yourself. [Apple counter sampling](https://developer.apple.com/documentation/metal/sampling-gpu-data-into-counter-sample-buffers).
3. Place tagged marker G0 immediately before all frame simulation/fixture graphics work, G1 after physics validation/metrics, and G2 after the final URP camera, UI and final blit, before presentation. The controlled benchmark uses one graphics queue, one camera and no async compute. Verify renderer ordering with a GPU capture. Then `G1-G0` bounds physics and `G2-G0` bounds the complete controlled frame's GPU work, including intervening queue gaps. Label the latter `totalGpuFrameSpan`, an inclusive timing span, rather than silently presenting it as FrameTimingManager busy time. Applying the unchanged 12 ms limit to this inclusive span is conservative for the covered work.
4. Store run/frame/tick token and sample indices together in the ring. Resolve timestamps only after the associated command buffer completion; consume asynchronously. Check token equality, monotonicity, G0<=G1<=G2, one marker per stage and no slot reuse. Convert counter units with the documented device timestamp conversion; do not assume every backend counter is already nanoseconds. [Apple counter conversion](https://developer.apple.com/documentation/metal/converting-a-gpus-counter-data-into-a-readable-format).
5. Calibrate with deterministic tagged GPU workloads alternating short/long work, intentional skipped frames and two markers in a frame. The collector must attach cost to the originating token and reject the malformed window. Compare physics span with Recorder and total span with a GPU capture. Measure marker overhead; leave it included in qualifying numbers rather than subtract an estimated constant.

If the pinned Unity interface cannot safely bracket all defined GPU work, or timestamps/counter units cannot be validated, report total GPU as unmeasured and block throughput qualification. The plugin must not relabel command-buffer elapsed CPU completion time as GPU time. `GPUStartTime`/`GPUEndTime` are useful command-buffer diagnostics but do not by themselves isolate the physics interval or guarantee coverage of all frame command buffers.

For diagnostic stage attribution, reserve at most 256 timestamp samples per frame: paired markers around broad phase/manifold construction, velocity solve, position solve and validation in each of at most 16 substeps, plus G0/G1/G2. Use the same tagged completion ring and report each stage sum for the originating frame. These diagnostics have a distinct mode/source manifest; if enabled during qualification their overhead remains included. Do not time every inner Krylov dispatch with an unbounded marker stream. Unsupported stage timing remains unmeasured.

Compact raw samples contain original tokens, attempted/committed tick, all timestamps, validity flags, profile counters and rejection reason. Drain outstanding samples after the final frame before computing verdicts. No full-array physics readback occurs during a qualifying timing window.

## 9. V2-4 — candidate integration on the qualified solver

Begin only after one V2 profile passes sections 6–8 on the proof fixture. Use the least costly fully qualifying profile, with its exact constants. Candidate gameplay stays opt-in until its own gates pass.

`ParallelGameplaySession` currently owns completion ordering and topology transactions; adapt that boundary to the concrete V2 solver without copying legacy snapshot mutation code into the hot path. Flight submits force/torque. Drilling reserves a grain identity/slot before terrain removal. Door topology publication waits for prior completions; obstructed closure keeps the door open. Cargo flags require the whole oriented square inside the cavity; capacity facts never hide physical compression. Suction acts through its existing mouth strip using the current GPU ship pose and force-accounted speed bounds.

Every topology transaction drains submissions, creates a validated proposal, invalidates affected contact keys, updates masses/COM/boundary proxies and publishes one generation. Avoid a second full solver arena: build changed compact records in the 8 MiB fenced scratch allowance, preserve a bounded rollback snapshot for touched records and publish after validation. If a transaction cannot fit this allowance, reject it explicitly and retain the old state; larger reconfiguration requires a separately budgeted stopped-session path. No half-published terrain/render/contact cache is permitted.

The user's unfinished impulse/feature capture remains preserved. V2 exports converged contact normal impulse plus body/feature/common local point and pre-solve closing speed for the later impact contract, keyed to the accepted tick. Do not call that damage behavior complete. Full effective-energy policy and fuel transaction implementation remain after this integration gate.

Acceptance requires candidate flight/drilling/doors/classification/suction tests, one final stable fast suite and a matching Mac player scenario on the final source. Candidate controls must actually be exercised and telemetry recorded; a launched process with no input evidence is insufficient. A proof-fixture pass alone does not certify production field rendering or gameplay input.

## 10. V2-5 and later migration/removal gates

R1 completion requires all physics, timing and controlled-fixture cases, including the in-memory cold restore, with no unresolved numerical/capacity loophole. R2 completion additionally requires the candidate gameplay cases, damage/fuel transfers and their exceptional transaction cases under the new solver. These phases remain separately checked.

When R3 begins, implement planned active checkpoint schema 5 and sparse-site schema 3 using public world-grain/body/mobility records; solver scratch and warm starts are not serialized. Preserve old schema migration, stable identities, grain material/fuel residuals, body mass definitions, page origin, topology/door policy and exact authoritative poses/motion. Fence all queued work before save. Load reconstructs caches cold and must meet the existing continuation tolerances. Travel selects whole physical cargo geometrically and transforms relative velocity/spin with the ship, preserving deposited matter. Do not retrofit speculative V2 internals into schema 4 during the proof phase.

Retain these concrete R3 conversion rules: write only checkpoint 5/sparse 3 after migration, but read checkpoint 1–4 and sparse 1–2 through the legacy DTOs. For sparse data, load core and bucket blobs before converting grains. External centers are old lower-left coordinates plus (.5,.5); external angle/spin initialize to zero. Cargo centers transform old local centers into site coordinates, retain stored world velocity, and inherit the old ship angle/spin. Treat legacy GPU linear velocity as COM velocity unchanged; do not promise identical old rotating trajectories. Resolve content IDs before reconstructing fragment mass. Spatial buckets use world chunks and preserve order, stable identities, material keys and residual fuel energy. Keep old files until atomic new-format publication succeeds.

R3 acceptance includes exact new-state roundtrip; cold continued center/velocity differences <=.0001 cell/.001 cell/s after one tick; bounded conservation over 120 ticks; legacy schema-1/schema-4/sparse-2 fixtures; reordered content catalogs; corrupt-primary recovery; interrupted writes; future-version rejection; and door/mobility restoration. Travel defers passage-straddling grains, transforms selected cargo centers and angles with the ship, subtracts old ship surface velocity before rotating relative velocity and adding new surface velocity, and transforms spin by subtracting old ship spin and adding new ship spin. Deposited grains and fragments remain at departure. None of this migration is implemented during R1.

Default cutover requires mine -> collect -> spill -> save -> reload -> revisit, conservation/non-overlap, controllable finite-mass collision behavior and all target-platform claims actually tested. Only then remove the old admission-only and Jacobi production paths, `_CargoOccupancy`, serial grain movement retries, CPU-prescribed ship motion and unused duplicate gameplay compute asset. Removal inventory includes `IntegratePhysical`/64 retries, serial `SolveShipCells`, dual-coordinate `TransferCargo`/`_CargoOccupancy`, `Free`/`CargoFree` admission, pose rejection, `physicalShip` mode, legacy displacement `Tick` integration, retry `Step` and unused `CargoGrid`; keep reusable geometric, mass, material, transaction and support helpers. Search actual call sites before deleting each symbol and preserve historical evidence and migration readers. Remove the preserved unfinished legacy gatherer only after its user's work has been superseded by an accepted implementation, not as incidental cleanup in a solver experiment.

## 11. Work batches, exact exits and verification cost

| Batch | Observable deliverable | Verification before advancing | Stop condition |
|---|---|---|---|
| V2-0 | Archived failures have a trustworthy independent geometric/reference answer | layout-independent double geometry, analytic cases, full saved-state reference results and residual report | missing trustworthy replay/reference; infeasible/unexplained constraints |
| V2-1 | GPU operator includes finite hull and all grains correctly; scheduler cost is known | focused Metal layout/operator tests, 200/4096-contact hull, masked schedule/product timing | incorrect reaction, capacity loophole or scheduling cost above gate |
| V2-2 | Complete coupled steps pass strict physical cases and rollback | focused replay/Newton/geometry/conservation/120-speed/cold-cache/packed tests | residual or geometry failure at declared caps |
| V2-3 | One profile earns simultaneous physical and throughput acceptance | stable fast suite once; one final Mac build and all-profile matching player matrix; valid tokened timing | no common qualifying profile or missing required timing |
| V2-4 | Opt-in candidate gameplay uses qualified V2 and preserves transactions | affected candidate tests; stable suite/build/player on final integration source | doors/cargo/drill/input/topology failure |
| V2-5/R2–R4 | Full physical salvage/persistence lifecycle and eventual cutover | explicit later phase gates above | any unsupported physics, save, travel or migration behavior |

During V2-0/1/2 use focused tests and the necessary diagnostic player/instrumentation run. Do not run a full Mac build for every equation edit or documentation update. Once the V2-3 implementation is stable, run one fast suite, one build and one matching player matrix. A failure is diagnosed before any rerun. A rejected prototype may require a restoration build only when the final shipped build otherwise mismatches restored source. Record each such reason.

Timeouts include setup, shader compilation, solver execution, readback drain, evidence writing and process cleanup. Successful assertions inside a timed-out process do not establish a pass. A zero-test filter is an infrastructure failure and must be corrected. Scope large offline reference runs explicitly instead of adding them to the fast suite.

## 12. Evidence, decision and handoff format

Create `docs/evidence/B3R-v2-convergence.md` when V2-0 executes, then keep that as the new V2 summary. Do not rewrite the historical legacy results. Each batch records:

- HEAD/branch, exact dirty-source manifest and experimental patch; source hashes captured **before** measurement, not reconstructed afterward; binary hashes, UTC duration, OS/hardware/editor/backend and fixture/profile revision.
- Exact replay-input hashes and provenance, including approximate reconstruction limitations, separate immutable output paths and cold/warm state.
- Per-profile physical maxima/residuals, attempted/committed tick, first failed stage/feature, identity/material totals, rigid participation, cache behavior and rollback result.
- Actual Newton/Krylov/line-search/geometry counts, dispatch count including masked work, contact/point/degree/pool maxima, allocation subtotals and diagnostics for all expensive retries.
- Distinct operator cost, failed-attempt replay cost and live qualifying throughput; every missing timing field explicitly unmeasured; raw frame tokens and invalid-sample reasons.
- Focused/full tests, builds, player invocations including unsuccessful/zero-test attempts, each rerun reason and client usage `unavailable` unless actually reported.

Exit 0 means a common profile passes every mandatory case of the current named gate. Exit 2 means a physical, numerical, capacity or measured budget failure. Exit 3 means infrastructure/timing/reference validity prevents a decision. A smaller stage's exit 0 is never full R1/B.GATE completion.

Update STATUS with the next concrete batch, failure, unfinished source and latest relevant evidence. Update EXECUTION_PLAN checkboxes only for executed gates, PERFORMANCE only with measured costs, and the feature-batch ledger for each completed batch. Commit explicit paths and push. An unsuccessful profile is valuable evidence, but it is not permission to continue gameplay expansion. The implementer must state whether to advance, repair a demonstrated defect within this contract, or stop for an explicit design revision.
