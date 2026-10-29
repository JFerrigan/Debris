# V2-2 GPU solve source handoff

Current checkpoint: bounded position stage plus separate whole-tick transaction/publication source; no integrated physical solver tick or V2-2 gate pass. The latest user direction is to write code without launching Unity or the game. Prior focused GPU evidence remains in [STATUS](../STATUS.md); it does not cover the new position path.

## Position behavior added on 2026-10-29 UTC

`CoupledPositionSolve` owns a fresh normal-only minimum-displacement solve over a generated contact graph. Each `Record` clears all position multipliers and free displacement before starting. It never binds committed physical velocities or poses. Slop is .002 cell for two classified grains and .0001 cell if either endpoint is classified rigid/terrain. Missing classification, nonfinite values, invalid provenance and nonpositive scaling fault the equation evaluation.

The implementation reuses the existing projected-residual search machinery through an internal position mode of `CoupledVelocityResidual`; its public velocity constructor still rejects normal-only graphs. Position evaluates `F=p-max(0,p-(B*p+D^-1/2*(gap+slop)))` and its selected normal branch derivative. Normal-only dispatches and snapshots support odd row counts. Local manifold factors support one/two position rows as well as two/four velocity rows. Position uses no tangent row, velocity bias or friction law.

All profiles permit four position Newton updates per geometry update. C1/C2/C3 select 16/24/32 Krylov products per Newton update. The shared Arnoldi implementation preserves its GMRES(16), true linear-residual verification, counted restarts and search-only regularization retries. Eight frozen-equation trials use one stored direction/product. GPU selection chooses the first Armijo or physical-residual trial, then projects multipliers nonnegative and recomputes the true residual before setting acceptance. Position acceptance uses the specified 1e-5-cell projected residual; diagnostic multiplier/gap products do not inherit velocity-unit inequality tolerances.

The output exposes scaled multipliers and mass/inertia-weighted endpoint displacement, valid only when control word 1 indicates acceptance. The final projected residual product already reconstructs that displacement; exposing it adds no operator product. This is an equation solution, not permission to publish a pose: actual-pose search must discover new contacts and enforce the motion envelope first.

All allocations use the shared arena and unwind on constructor failure. Normal-only branch and pivot arrays hold one entry per row. Velocity mode binds two minimal unused geometry records for Metal resource completeness. Position and velocity production scratch aliasing/lifetime admission is still open; separate full-capacity operators/workspaces must not be assumed to fit together.

## Validation for this checkpoint

Direct Roslyn C# compilation against the pinned Unity 6000.3.11f1 reference and engine assemblies exited 0. The compiled set includes every contact runtime source plus `CoupledPositionSolveTests`, `CoupledVelocityResidualTests`, `CoupledArnoldiWorkspaceTests` and `CoupledManifoldPreconditionerTests`. The command used the installed `DotNetSdkRoslyn/csc.dll` directly, with an output assembly in `/private/tmp`; no Unity process was launched. Final compile response: `/private/tmp/debris-position-source.rsp`.

Six new position regression cases are defined and compiled, but unrun:

- Three mass/slop cases cover finite mass-1/mass-4 endpoints, rigid-contact slop, explicit anchoring, all profiles and their four-update cap. Each repeats with poisoned initial scratch to check fresh-start semantics.
- A single-corner normal row checks odd active-row counts and zero-displacement early acceptance.
- An unclassified contact must fault instead of accepting a displacement.
- A velocity graph must be rejected before allocating position scratch.

Shader import/Metal compilation, GPU tests, builds and player runs were not executed. No physical convergence, conservation, production memory or timing acceptance is claimed. Earlier velocity/operator GPU results cover their earlier source revisions only.

## Actual-pose search source added on 2026-10-29 UTC

`CoupledPositionSearch` snapshots private working bodies, primitives and union frames at the predicted substep pose. It snapshots the converged displacement before reusing the same manifold scratch for baseline geometry and eight alpha=1/2^i trial queries. Every query repeats broad phase, convex/union geometry, containment when unions exist, and class/hard-bound facts. Its merit is the fixed-order sum of squared positive actual gap violations beyond class slop. The first Armijo or target+2e-5-cell winner is retained. A new pair contributes to the trial merit and the final selected graph; a search winner can still report hardBoundsPass=false, requiring more geometry updates and final validation.

The baseline records exact primitive-pair/reference-face/incident-face/provenance/point words for initially violated solid points in a bounded linked hash. Atomic insertion needs no within-dispatch spinning; only later dispatches perform lookup. Trial solid penetration beyond .001 is forbidden unless its exact point was already violated at baseline. Thus aggregate merit cannot hide a newly broken solid point. Hash collisions compare complete words, and point nodes/bucket heads are arena-counted at full capacity.

Every proposed primitive uses the body's COM transform; union frames and exterior proxies move together with topology unchanged. The conservative surface bound is cumulative body-center translation plus radius-about-COM times absolute accumulated rotation, measured from the original predicted substep pose. It includes offset primitive centers and faults above .25 cell. It does not reset on later searches. The velocity prediction allowance of .125 and complete .375 substep travel still belong to the forthcoming step controller.

After selection, the query/normal graph is rebuilt at the selected pose and its faults are inspected before changing private working buffers. If no trial wins, the graph returns to the prior working pose. Invalid proposals, geometry faults and envelope failures remain latched until `RecordBeginSubstep`. Accepted corrections earlier in the substep remain private; this is not whole-tick rollback or committed publication. Bodies without active collision matter still receive translation/nonfinite/immobility checks.

C# compilation exited 0 for all contact runtime sources and five fixtures: the four above plus `CoupledPositionSearchTests`. Eight new unrun search cases define first-winner graph rebuild, a previously absent pair, preservation-driven backtracking, cumulative envelope failure and failure latching, offset-center rotation bounds, coordinated union frame/proxy movement, eight worsening trials retaining working/graph poses, and nonfinite rejection. Search tests inject declared converged proposals to isolate geometry acceptance; they are not Newton convergence evidence.

No Unity launch, shader compilation, GPU fixture run, build or player run occurred. The fixed command schedule still records all eight trial queries after a winner; only subsequent selection decisions stop. GPU query masking, cached production schedules, scratch lifetime/aliasing and all costs remain unverified. No throughput or full solver acceptance is claimed.

## Bounded position-stage controller added on 2026-10-29 UTC

`CoupledPositionCorrection` records one original predicted snapshot and C1/C2/C3 geometry-update caps of 2/3/4. Each live geometry update runs the fresh four-update frozen position solve, actual-pose search and selected graph refresh. The GPU activity word stops later physical solving after target cleanup or failure. It retains counted active geometry attempts, Krylov products, the first solve/search fault and last frozen-system acceptance across inactive slots. At the cap, targetReached=false remains distinct from hard-bound acceptance.

An independent final query supplies the original .01 grain/grain and .001 solid hard bounds, class validation and union containment. Acceptance requires the last live frozen system to have passed its projected residual and the final actual geometry to pass independently. It does not claim that p=0 solves a new final-pose equation when accepting a hard-bound-only result. Earlier geometry-stage faults remain authoritative even if the final query itself passes.

Failure restores the complete position-stage snapshot: body COMs/mobility records, primitive poses and metadata, and union frames. It discards corrections accepted earlier in the same stage and rebuilds the graph at the restored pose. Diagnostic acceptance/fault/max-penetration words are retained separately from the overwritten query scratch. Outputs are provisional positions only; physical velocities, caches, grain/material/fuel records, cargo facts, identities and tick publication are not owned here. Whole-tick rollback is still open.

Source inspection of a cold inactive solve exposed dependencies that needed explicit masking before chaining geometry iterations: free-motion clear/restore previously ran despite an inactive derivative, projection selection could inspect scratch that had never received a base residual, and inactive indirect argument buffers had no initialization contract. The shared residual now gates those stages and initializes indirect argument triplets to [1,1,1]. Later true products remain gated. Activity gates also cover position-input reset, velocity/position controller start and actual-pose search entry.

Direct Roslyn compilation against pinned Unity assemblies exited 0 for all contact runtime sources and six fixtures. Seven new unrun controller cases define the three profile caps with first-target stopping; envelope rejection restoring predicted bodies/primitives/graph; isolated unclassified rejection; a cold inactive solve preserving poisoned free motion, multipliers and endpoint output with zero counted work; and hard-bound acceptance retaining targetReached=false. The prior cumulative-envelope test now explicitly rejects the full stage after nonzero accepted movement and checks restoration to its original pose. The source fixture results are compilation only, not executed physics evidence.

No Unity/game process, shader import, GPU fixture, build or performance workload was run. All eight actual-pose queries, later inactive graph work and rollback graph rebuilds remain in the fixed command schedule. Their driver/GPU cost is unmeasured; production schedule caching and GPU query masking remain open.

## Next behavior

Connect full-tick working-state and provisional-cache ownership, prediction/speed bounds, conservation/count facts, accepted publication and queued-tick dependency failure. Whole-tick rollback must include velocities, identities, material/fuel/cargo facts, topology generations and completion state. V2-0C exact packed references, V2-1 geometry/operator/cost gates, V2-3 throughput, V2-4 candidate integration and B.GATE remain open.


## Whole-tick transaction source (implementation only)

`CoupledTickState` owns fixed-topology committed and working buffers for endpoint motion, grains, body parameters, persistent identity bindings, contact cache, opaque inventory/fuel words, cargo facts and terrain revisions. All buffers and transaction/completion storage are charged to the shared arena. Initial state requires consistent grain/endpoint motion and identities, unique bindings, valid finite physical parameters and finite cache payloads. No transfer, damage or fuel-burn behavior is added.

`CoupledTickTransaction` snapshots committed state at GPU begin, accepts velocity/prediction/position verdicts in order for each declared substep, then requires final facts. Publication independently checks finite endpoint/cache values and immutable anchored motion, endpoint reserved words, grain material/identity, body parameters, identity bindings and terrain revisions. Grain motion is synchronized from endpoints. Successful payload copies precede header/completion publication. Failure restores all working payloads from the last committed state. A persistent first-failure latch rejects subsequently queued ticks as dependency failures without advancing the committed header/cache generation. Consumers must use the same GPU queue and read committed payloads after completion; this is ordered publication, not a cross-queue atomic memory operation.

The CPU reserves sixteen bounded completion slots and accepts only matching completed tick/generation records before reuse. Each tick must be recorded in one command buffer, and buffers must be submitted in recording order. Dropping a recorded schedule, disposing in-flight resources or recreating an owner to recover a failed FIFO is outside this contract. Recovery requires a drained session and an explicit authoritative reconstruction.

Regression source defines stage rejection after earlier movement, dependent ticks, accepted publication, retention of an earlier accepted tick, immutable/nonfinite rejection, stage ordering, completion reuse, low-word tick wrap, duplicate ownership, nonfinite provisional cache, command-buffer ownership and corrupt completion records. These fixtures inject declared verdicts and GPU copies; they are transaction isolation definitions, not physical solver acceptance.

No compilation, tests, Unity launch, shader compilation, build or player run occurred. Source review only. The transaction still needs production solver-stage wiring, physical prediction/speed/envelope and conservation/count facts, converged-only cache construction and warm-start eligibility, topology lifecycle integration and measured schedule/resource qualification. All V2 gates and B.GATE remain open.
