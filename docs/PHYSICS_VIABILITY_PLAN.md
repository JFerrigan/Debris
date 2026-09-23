# B.3R physics and performance viability checkpoint

Status: planned, not executed. Prepared for a new Terra implementation context on 2026-09-23 UTC. This checkpoint precedes further candidate damage/fuel implementation. It does not claim a new physics pass, benchmark result, or architecture decision.

## 1. Deliverable and boundary

Deliver a reproducible answer to this question: can the existing parallel square-grain solver sustain representative contact loads within the stated physical and throughput limits, or is a specific change needed before more gameplay integration?

The implementer must build the missing benchmark, measure the current implementation, investigate the known packed-cargo failure, implement a narrowly justified correction if feasible within the locked architecture, and publish a decision supported by recorded results. A documented failure or unavailable measurement is a valid checkpoint outcome; a false pass is not. Finish the checkpoint even if no solver correction succeeds.

Order of work:

1. Preserve the workspace and establish the exact source/fixture baseline.
2. Implement and validate a representative 8,192-active-grain measurement path. Collect a baseline before changing contact mathematics.
3. Investigate the original .001-cell packed solid limit and bounded behavior under the interim .002 limit. Reuse the existing diagnostic machinery and findings.
4. If the evidence identifies a bounded correction, implement it, verify affected behavior, and measure the final implementation.
5. Publish the decision and next task. Stop this assignment at that handoff; do not automatically begin damage/fuel or later phases.

Keep candidate gameplay opt-in and legacy gameplay the default. Do not implement damage/fuel transactions, persistence migration, travel, streaming, default cutover, legacy deletion, or the 100,000-site experiment in this checkpoint. The exploratory 10,000-grain run is also deferred: the current constructor caps allocated capacity at 8,192 even though another input validator mentions 10,000. Do not enlarge that limit for this task.

This is a concrete implementation/measurement assignment when the user supplies [the Terra prompt](TERRA_PHYSICS_CHECKPOINT_PROMPT.md). Creating this plan alone does not execute it.

## 2. Existing facts to preserve

- `Showcase` selects candidate gameplay only with `-debrisParallelGameplay`. Candidate drilling, effective doors, cavity classification/capacity admission, suction and flight exist. Ship damage, fuel transfer, persistence and travel are unavailable on that path.
- The previous packed proof tested 2,500 grains in a 50 by 50 cavity. All three profiles failed the original .001 solid limit. Rest-under-force maxima at rejection were .001185954, .001122653 and .001034379 for 4/2, 8/4 and 12/6. These are historical early-rejection measurements, not long-run bounds at .002.
- Independent double-precision replay checked selected geometry, effective mass, degree, impulse and reduction calculations. It did not establish convergence. Long shared-motion trajectories varied between repeats; diagnostic comparisons must start from identical saved pre-failure states.
- Current docs accept .002 for interim integration. Inspect the code before assuming uniform enforcement: `ParallelGrains.compute` currently rejects grain/solid penetration above .002, while `ParallelRigidContacts.hlsl` still rejects rigid/solid penetration above .001. Report this distinction and preserve the stricter rigid threshold unless evidence justifies tightening other paths. Do not silently relax it to match the prose.
- `ParallelProofRunner` is a short correctness runner, not the required throughput benchmark. It waits for diagnostics during each tick, hard-codes draw counts of 2,500 grains/four boundaries, caps the player at 60 FPS, and prints GPU p95 as unmeasured.
- `ParallelGrainSolver.Step` records `B3R.ParallelPhysics` and stage markers. The outer physics marker currently includes compact metrics recording. `ProofGpuTimingCollector` exists, but its default marker is `B3R.ParallelMetrics`; using the default would not measure all physics. Its assumed three-frame recorder delay must be checked in the actual player.
- Current strongest-impact changes in three modified files are unfinished work. They capture an impulse and feature, not the complete effective-energy/per-body impact contract. Preserve them and identify whether the measured build includes them; completing damage capture is out of scope.
- The 137-pass fast-suite result and successful Mac build are prior evidence. The last candidate suction player lacked telemetry and does not establish player-input acceptance. The 324.505 ms old contact workload and the 1.876 ms loose-only workload are different workloads and cannot establish a replacement speedup.

Primary evidence: [parallel pieces](evidence/B3R-parallel-pieces.md), [packed diagnosis](evidence/B3R-packed-diagnosis.md), [current status](STATUS.md), [contact contract](CONTACT_PHYSICS.md). Historical evidence files remain historical; append a new checkpoint record rather than rewriting their old verdicts.

## 3. Startup and source ownership

Read `git status --short`, `docs/STATUS.md`, the current block of `docs/EXECUTION_PLAN.md`, `PROJECT_PLAN.md` once, this plan, and `docs/CONTACT_PHYSICS.md`. Read the two linked evidence summaries above. Do not load historical XML or trace archives unless needed to isolate a particular failure.

Record UTC start time, HEAD, branch, hardware/editor versions and modified/untracked paths. Preserve all existing changes. The handoff currently has unrelated content, ship, persistence, legacy-contact, package/settings and application files, plus the three candidate impact files. `ParallelGameplayGrains.compute` is an unused untracked duplicate; production-intended solver edits belong in canonical `ParallelGrains.compute`.

Before modifying an already-dirty file, preserve its current patch or copy in a task-specific temporary directory and record a hash. Compare against that baseline at completion. Do not stash, revert, clean or stage the entire worktree. If a commit would inadvertently include pre-existing unfinished code, isolate the checkpoint changes with a reviewed patch/index workflow or a separate checkout; do not silently absorb the user's work. A build of a dirty workspace must include a source manifest or preserved patch sufficient to identify what was actually measured.

Use one agent. Do not change the selected model, effort or account configuration. Work autonomously within this scope. Choose at most two evidence-supported correction approaches; after two failed attempts with one approach, inspect the isolated failing state before making another patch.

## 4. Source map and likely changes

| File/subsystem | Intended use |
|---|---|
| `Assets/Debris/Presentation/Runtime/ParallelProofRunner.cs` | Reuse correctness runner; add an explicit viability mode or delegate that mode to a small dedicated runner. Preserve existing flags. |
| Presentation bootstrap that recognizes `-debrisParallelProof` | Locate with `rg`; wire the new mode through the existing opt-in entry point. |
| `Simulation/Runtime/ParallelProof/ProofFixtures.cs` | Preserve canonical packed fixtures. Add a separate deterministic combined fixture, preferably in a new focused file. |
| `ProofDiagnosticFixtures.cs`, `ProofTrace.cs`, `ProofTraceArchive.cs` | Bounded failing-tick tracing and identical-state replay; no detailed trace during performance timing. |
| `ProofMetrics.cs`, `Resources/ParallelMetrics.compute` | Compact facts, reductions and delayed timing; extend only for missing acceptance facts. |
| `ParallelGrainSolver.cs`, `Resources/ParallelGrains.compute`, `ParallelRigidContacts.hlsl` | Production solver scheduling, allocation audit, validation and evidence-supported contact correction. |
| `ParallelContactReference.cs`, `ParallelTraceReport.cs` and existing parallel tests | Independent reference checks, failure localization and meaningful regressions. |
| `ParallelGameplaySession.cs`, candidate terrain/cache and rendering code | Reuse representative field/cache/render setup where possible; preserve gameplay behavior. |
| `tools/unity.sh` | Pinned fast-suite/build entry points. No unrelated tooling refactor. |

Paths abbreviated above are under `Assets/Debris/`. New Unity assets need their normal `.meta` files.

## 5. Workload specification

Implement the mode as `-debrisParallelProof -debrisProofViability`. Retain `-debrisProofOutput <path>` and `-logFile <path>`. These are planned semantics: `-debrisProofViability` does not exist at handoff. Mode output must state its fixture revision, thresholds, profiles, source identity and whether tracing is enabled. A normal proof invocation must retain its existing meaning.

### Canonical correctness cases

Preserve `ProofFixtures.Packed(true)` and `Packed(false)` exactly: 2,500 unit-density rotating square grains, 50 by 50 cavity, attached body mass 10,000 and inertia 5,000,000. Preserve initial poses/velocities, shared motion, forces, torque, friction and the three profiles 4/2, 8/4, 12/6. Run each canonical case for 120 accepted ticks or stop it on rejection. A runner that exits early must record both attempted and committed ticks.

Evaluate the original .001 limit separately from the current runtime rejection limit. For the long-run interim study, keep existing runtime thresholds and record the first crossing of .001 plus the maximum by penetration class. This permits observing whether the error grows without changing the current acceptance rule. For exact strict rejection/rollback reproduction, an explicitly selected proof-only stricter validation setting is permissible; keep its default unchanged and apply it consistently to the intended contact classes. Never increase an existing runtime threshold.

Report all three profiles, but one profile may qualify the architecture: the **same profile** must pass the required physical cases and combined timing/resource budgets. Do not combine correctness from 12/6 with timing from 4/2 into a pass. A slower or failing alternative profile does not disqualify a fully passing profile. Recommend the least costly fully qualifying profile and state that recommendation separately from the current candidate gameplay default; this checkpoint need not change that default.

Add a 720-tick extension of shared-motion packed cargo at the interim thresholds to check whether a 120-tick result hides drift. For the forced extension, apply the canonical force/torque for ticks 1–120, then zero both for ticks 121–720. Label this as a separate diagnostic case, not the canonical constant-force fixture. Check residual grain penetration after 120 unforced ticks. Stop on a genuine runtime fault and preserve its last committed state.

### Combined throughput case

The mandatory run has exactly 8,192 active grains, not just 8,192 allocated slots. Use 2,500 packed bay grains, a separate 1,000-grain dense pile, and 4,692 exterior grains. Include one finite-mass ship and all 16 finite-mass fragment slots, plus explicit anchored terrain if required by the existing endpoint representation. Cargo mass must not be counted again in attached body mass.

Use deterministic geometry and initialization; no runtime random seed, topology changes, fuel transfer or mining during the measurement window. Default layout recipe:

- Place the canonical shared-motion packed bay at the origin. Preserve its 2,500 grain positions, body mass/inertia and initial shared translation/spin. No external ship force during this combined benchmark.
- Place the 1,000 pile grains on a touching 40 by 25 grid with centers `(-159.5 + x, 68 + y)`, for integer x in 0–39 and y in 0–24. Unit mass, zero angle/spin and initial velocity `(0,-1)`.
- Add an anchored one-cell-thick platform centered at `(-140,67)` with half-size `(21,.5)`. The initial pile just touches its top face. This supplies dense contact work without relying on gravity.
- Put 16 unit-square fragments at `(-158.5 + 2.5*i,95)` for i in 0–15, each mass 4, inertia `4/6`, velocity `(0,-2)`, zero angle/spin. They must contact the pile during the run; report actual fragment contacts rather than only body allocation.
- Place 4,692 exterior grains on a 68 by 69 grid with centers `(100 + 2*x,-140 + 2*y)`, x in 0–67 and y in 0–68. Unit mass, zero velocity/angle/spin. These are the explicitly sparse portion of the workload; do not describe all 8,192 as dense contacts.
- All identities are positive and unique, assigned in the order bay, pile, exterior. Document material mapping. All matter lies inside the solver page with sufficient margin for 720 ticks.

Represent the platform in the same fixed-field/cache/render system used by the candidate. Allocate and render 16 active 128 by 128 chunks covering `[-256,256)` on both axes. Report filled cells and boundary counts: allocation alone must not be described as 16 densely populated terrain chunks. This controlled layout is the checkpoint's representative combined workload, not proof for arbitrary asteroid complexity.

Use the existing candidate-compatible grain, ship, fragment and terrain rendering where feasible. The camera and renderer must include actual grain/body/terrain work and remain identical across profiles. Fix the camera at the origin with orthographic size 256 and 1280 by 800 resolution; report visible/culled geometry. Do not claim total-GPU acceptance from a solver-only or four-wall debug view.

Validate initial non-overlap, mass/inertia, boundary ownership, identity uniqueness, chunk placement and page margins before timing. A genuine fixture-construction error may be corrected before freezing the workload; explain and version that correction. Once the baseline is collected, do not reduce density, remove fragments, change forces, or replace terrain/rendering to obtain a pass. If the existing field representation cannot express this exact recipe without an unrelated rewrite, use the nearest equivalent translation and record the reason and full fixture manifest before any profile comparison.

## 6. Timing and measurement rules

Use Apple M4 Pro / Metal / Unity 6000.3.11f1 and a 1280 by 800 development player, v-sync disabled. Set `Application.targetFrameRate = -1` for the measured mode, log the actual setting and retain fixed simulation dt 1/60. Do not report a 60-FPS cap as solver performance. Run one submitted fixed tick per rendered frame, independent of elapsed render time, with no catch-up loop.

For each profile, reconstruct the identical initial fixture, warm 120 submitted frames, sample the next 600, then drain delayed timing/readback results outside the window. Keep initialization, file output, full snapshots, detailed traces and fixture reset outside timing. Normal compact GPU reductions and any required production submission work remain included. Preallocate measurement arrays/queues.

Collect and report:

| Metric | Acceptance | Required interpretation |
|---|---|---|
| Physics GPU p95 | <= 8 ms | Entire `B3R.ParallelPhysics` scope, including its actual current contents. |
| Total GPU p95 | <= 12 ms | Full rendered frame, not the sum of selected nested markers. |
| Frame p95 | <= 20 ms | Actual player frame time, not fixed dt or CPU submission duration. |
| CPU submission p95 | <= 2 ms | Command preparation and submission; no asynchronous wait time disguised as submission. |
| Explicit GPU buffers | <= 128 MiB | Total live solver, terrain, rendering and compact diagnostic buffers; give subtotals. |
| Steady-state submission allocations | 0 bytes | Measured submission path after warmup; distinguish harness/log/readback allocations. |
| Accepted measured work | 600 ticks | No rejected, skipped, duplicate or merely fault-short-circuited ticks. Warmup must also succeed. |

Use nearest-rank p95: sort N valid samples and select index `ceil(.95*N)-1`. Record N, invalid/missing sample count and the method. Every required metric needs 600 valid frame-matched samples for a throughput pass. Do not fill missing samples with zero, interpolate, reuse a stale value, or call a partial window a pass.

Map delayed GPU timings to their submitted frame IDs. Inspect the existing timing collector instead of assuming its three-frame delay or default marker is suitable. Poll every rendered frame, including drain frames, and verify marker availability on Metal. For total GPU/frame timing use the platform-supported player timing mechanism already present where possible; demonstrate valid, distinct samples and state its delay. If a backend cannot supply required timing, report `UNMEASURED` and finish the other checks. Do not substitute CPU time for GPU time.

Report stage p95/counts for bins, gather, velocity, position and metrics where valid, plus unaccounted time. Nested/overlapping marker durations cannot simply be summed into a total. Include active count, substeps, maximum bin occupancy, candidate/contact degrees, rigid manifold counts, page/envelope faults and total rejected ticks. Do not claim a bottleneck based only on a marker name.

Do not await a new full-state or diagnostics task between every measured tick. Use bounded compact asynchronous facts, preferably persistent/preallocated readback storage when needed, and perform final validation after draining. A necessary queue stall invalidates the ideal one-tick-per-frame throughput result; report it and its cause rather than dropping the slow frame. Audit hot-path allocations, including parameter-binding helpers, delegates/tasks and per-frame collections. Fix an observed submission-allocation defect within scope, without redesigning unrelated gameplay APIs.

### When correctness fails before 600 samples

A faulted solver can become cheap because kernels return early. Therefore never continue timing its no-op frames as useful simulation. Record the first faulty attempted tick, last committed tick, diagnostic bits, penetration maxima and the valid prefix. Mark the full benchmark `INCOMPLETE_CORRECTNESS`, with prefix timing explicitly diagnostic and not representative steady-state p95. Move on to the other profiles instead of allowing one failure to suppress all evidence.

If no profile can complete the workload, that is already a decisive checkpoint result: throughput at valid steady state remains unproven because correctness prevents it. Use bounded trace replay of the first failure to investigate it. Do not disable faults, reset repeatedly inside the timing window, loosen tolerances, freeze the ship, or lower active counts merely to manufacture 600 samples.

## 7. Packed diagnosis and permitted corrections

First establish whether current code reproduces the known behavior and whether the .001/.002 discrepancy is limited to grain contacts. Preserve separately: grain–grain, grain–rigid/terrain, and rigid–solid maxima; attempted versus committed state; first .001 crossing; first actual fault; residual penetration after settling.

Maxima must cover every validation substep throughout the run, not just final committed tick positions. Label them as post-correction validation maxima; they do not prove that no transient overlap occurred during an earlier prediction/iteration. If necessary add compact running reductions for the relevant classes and tick/substep of the maximum. Keep detailed traces out of throughput measurement.

Use the existing identical-pre-failure-state method to compare GPU and reference. Start with one earliest failing rest-under-force case and one shared-motion case. Test a concrete hypothesis: for example, contact coverage/degree error, incorrect geometry/contact patch accounting, reaction application, or insufficient convergence at the permitted iterations. These are alternatives to investigate, not assumed diagnoses. Do not rerun the entire old reference project without a new discrepancy.

Allowed without another planning round: fix a demonstrated implementation error; remove redundant work without changing physics; repair a measurement defect; eliminate observed submission allocations; improve contact coverage/patch handling within the locked representation and budgets. Retain GPU authority, finite dynamic mass/inertia, equal/opposite reactions, atomic rollback and compact normal readback.

Retain the three profiles, timestep/substep rule, friction/restitution, position targets, capacity/page bounds and runtime acceptance thresholds. No CPU contact fallback, automatic anchoring, hidden matter deletion, softened spring/compliance model, graph coloring, contact islands or new general physics interface. Do not enlarge the cavity, shrink grains or count interpenetration as extra cargo space.

If the only credible correction needs additional profiles/passes, materially different coupling, new major buffers, changed limits or a different numerical model, quantify the proposal using available stage/capacity evidence and stop with an architecture decision recommendation. More passes or buffers are not inherently disqualifying, but a change outside this checkpoint's locked contract needs a separately reviewed plan. Do not claim such a proposal has passed without measuring it.

## 8. Acceptance and regression coverage

Use focused existing tests during development; add tests for observed defects and the new benchmark's result validity. Necessary coverage includes:

1. Both canonical packed cases at each profile: full 120 ticks or an explicit failure, .001 result and interim-runtime result separately. Preserve canonical inputs.
2. Long interim extension: bound/error evolution, 120-tick unforced residual <= .002 grain penetration, no hidden capacity gain and explicit early faults.
3. Combined 8,192 workload: complete 120/600 protocol or explicit incomplete result; all 16 fragments actually participate; report if they do not.
4. Existing two-grain and 10,000:1 analytical cases after relevant solver changes; retain heavy-body speed `100000/10001 +/- .0001`, normalized linear error <= 1e-4, isolated angular error <= 1e-3 and permitted energy gain `max(.1% of initial energy,.0001)` without external work. Do not apply isolated conservation assertions to external-force or anchored-contact scenes without accounting for external impulse/work.
5. Existing high-speed mass-1 grain at 120 cells/s against a one-cell anchor and a finite-mass dynamic body, with <=16 substeps and no tunnelling. Verify actual coverage; a test name is insufficient. Add the missing counterpart if only the anchor case currently exists.
6. Forced capacity/envelope/speed faults: full committed grain/body pose and motion, identity/material state and tick remain unchanged. Deliberately trigger a fault so the rollback test cannot pass merely because no rejection occurred. The current packed rollback test logs `observedRejection` without requiring it; retain its useful checks but add an unconditional fault-injection case.
7. Candidate door obstruction, whole-square cargo classification/admission and capacity behavior if shared contact/validation code changes. The true 2,501st-grain physical cavity test remains required for full R1/B.GATE even if only the existing compact-admission fixture runs here; do not equate those tests.
8. Timing validity: invalid/missing/delayed samples, wrong marker, insufficient sample count, rejected tick and truncated run cannot produce PASS. Meaningful boundary tests for the result evaluator are appropriate.

Perform full identity uniqueness/material/mass checks on initialization and final/fault snapshots outside timing. Sum/XOR alone is not proof of uniqueness. Rollback, no matter loss and no duplicate inventory remain invariants. A checkpoint pass does not establish save/reload, mining/fuel transactions or full physical capacity acceptance that this assignment did not run.

## 9. Execution and verification budget

Use the pinned editor. Focused command template, with the actual fully-qualified test filter selected from current source:

```sh
/Applications/Unity/Hub/Editor/6000.3.11f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -projectPath /Users/jakeferrigan/Documents/Debris \
  -runTests -testPlatform EditMode \
  -testFilter '<fully-qualified-fixture-or-test>' \
  -testResults /Users/jakeferrigan/Documents/Debris/Logs/b3r-viability-focused.xml \
  -logFile /Users/jakeferrigan/Documents/Debris/Logs/b3r-viability-focused.log
```

Fast suite: `bash tools/unity.sh test`. Mac build: `bash tools/unity.sh build`. Do not launch Unity for documentation-only edits. Inspect runner exit/result and fresh shader/build errors, not just a success line. Close only this project's editor when batch execution requires it.

After the measured mode exists and its focused checks pass, use one baseline Mac build and one player process to run the profile matrix. Suggested invocation:

```sh
Builds/Debris.app/Contents/MacOS/Debris \
  -debrisParallelProof -debrisProofViability \
  -debrisProofOutput /Users/jakeferrigan/Documents/Debris/Logs/b3r-viability-baseline.txt \
  -logFile /Users/jakeferrigan/Documents/Debris/Logs/b3r-viability-baseline-player.log
```

The mode should run independent cases sequentially, write a structured summary plus compact sample data, and exit predictably. Define exit codes: 0 means at least one common profile meets all mandatory checkpoint physical and throughput assertions and required control checks pass; 2 means no profile qualifies because of physical/performance acceptance failure; 3 means infrastructure, invalid required timing or unhandled execution failure prevents a decision. Preserve all per-case verdicts, including failed alternative profiles even when the aggregate exit is zero. Write partial evidence on an early exit. A zero exit for this subset is not a full R1/B.GATE pass.

If the baseline justifies a solver or measurement correction, complete that correction and its focused regressions before one final fast suite, final Mac build and matching player matrix. If no runtime source changed after baseline and its required checks already passed, reuse that build/player evidence rather than rebuilding for notes. A necessary trace-only reproduction is a separate diagnostic run, never performance evidence.

The new assignment budgets a baseline checkpoint and, only after a relevant implementation change, a final checkpoint. Historical one-player limits in prior startup investigations describe those completed tasks. They are not a reason to skip this explicitly assigned benchmark. Every additional expensive run still needs an observed failure/relevant change recorded in the ledger. Set a realistic whole-process timeout including 720-frame windows for three profiles, correctness cases, shader warmup, readback drain and cleanup; a timed-out process is not a pass.

Freeze source during each verification run. Keep logs distinguishable by checkpoint and source; never overwrite baseline evidence with final results. Do not repeatedly rebuild while tuning tests or completion notes.

## 10. Decision and completion artifacts

Publish `docs/evidence/B3R-physics-viability.md` as the single current summary. Include:

- Source commit plus dirty-source manifest/patch, actual UTC interval, hardware, OS, Unity, backend, resolution, cap/v-sync, fixture revision/seed, renderer/camera and material/mass details.
- Per-profile canonical and extended packed results, tick/substep of failure/crossing, penetration classes, rollback/identity checks and uncertainty.
- Per-profile combined sample counts, invalid samples, committed work, physics/total GPU/frame/CPU p95, allocations, buffer subtotals, stage timings, contact statistics and limits.
- Baseline versus final differences using identical workloads; no comparison to an unrelated historical workload as a claimed speedup.
- Compact raw sample/result paths and precise reproduction commands. Keep large diagnostic trace archives outside git and cite their location/checksum if needed.
- Tests/build/player counts including failed attempts, each expensive rerun reason, and client usage `unavailable` unless an actual client figure is provided.
- One decision from the table below, evidence supporting it, unresolved cases and the exact next task.

| Outcome | Decision and next task |
|---|---|
| Original .001 packed target and combined throughput both pass, with relevant regressions | This checkpoint supports continuing the existing architecture. Name any remaining R1 cases. Damage/fuel is the next feature only after required unresolved physics prerequisites are addressed; do not mark all R1 or B.GATE complete from this subset. |
| Interim .002 passes but .001 fails, with valid throughput | Architecture remains provisional. Report error size/growth and cost; recommend a bounded correction or explicit numerical-contract review. Keep damage/fuel paused for this handoff; no silent permanent relaxation. |
| Correctness prevents a complete measured window | Report the first failure and valid diagnostic prefix; throughput remains unproven. Recommend a targeted correction or architecture review supported by the trace. |
| Valid workload completes but a performance/resource budget fails | Identify measured bottleneck and correction already attempted; propose the smallest remaining change and its expected tradeoff, clearly labeling estimates. No default cutover. |
| Required GPU timing is unavailable/invalid | Report correctness independently; throughput is unmeasured. Specify the missing instrumentation/backend capability and next action. No inferred pass. |

Update STATUS concisely, EXECUTION_PLAN checkboxes accurately, CONTACT_PHYSICS only for evidenced decisions, PERFORMANCE with actual measurements, and the feature-batch CSV with all validation attempts. Preserve historical records. Commit coherent task changes and completion notes using explicit paths and the existing ordinary-push authorization. Report behavior changed, measured result, tests/build, commit, unresolved limits and the next task. Stop at the completed checkpoint rather than expanding into damage/fuel automatically.
