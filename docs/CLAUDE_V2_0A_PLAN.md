# Next Claude session — V2-0A implementation plan

Status: planned, not executed. Starting documentation checkpoint: `d2eb84c1917fc1ead21023d54aa9b1fd90738815`. This handoff divides V2-0 into smaller deliverables for the user's selected lower-tier Claude model. It changes neither the solver design nor its acceptance criteria. Use one agent and the model/effort selected by the user; do not change configuration or delegate.

## 1. Complete this observable result

Produce immutable, exact **committed pre-step inputs** for the failing legacy cases and prove that loading each input into a fresh unchanged comparator reproduces its rejected attempt without altering committed physical state. A later reference implementation must be able to consume these inputs without reversing force increments or reading undocumented trace offsets.

Stop after this deliverable, even when it passes. Do not implement contact geometry, SVD, block Gauss–Seidel, Newton/GMRES, new compute kernels, timing plugins or candidate features in this session. These are subsequent V2-0/V2-1 work. Passing replay proves input provenance and reproduction; it does not prove acceptable physics or complete V2-0.

### Remaining sequence

| Assignment | Deliverable | Exit |
|---|---|---|
| **V2-0A — this session** | Immutable inputs, exact roundtrip, rejected-attempt replay and rollback evidence | Stop with archive/reproduction decision |
| V2-0B | Independent Float64 geometry, exposed-face/containment tests and tiny direct contact reference | Stop with geometry/analytical decision |
| V2-0C | Independent converged packed reference and separate double V2 residual/solve comparisons | Complete the full V2-0 gate only if every required case passes |
| V2-1 onward | GPU operator, scheduling feasibility, coupled solver and qualification | Follow the existing implementation gates |

The [V2 implementation plan](CONTACT_SOLVER_V2_IMPLEMENTATION.md) remains authoritative for all three sub-batches together. Numerical ambiguity in later work requires an evidence-backed design decision, not an improvised simplification by the next session.

## 2. Read and preserve before editing

1. Run `git status --short`. Read AGENTS.md explicitly; do not assume Claude automatically loads it. Read STATUS and the current B.3R block of EXECUTION_PLAN. Read PROJECT_PLAN once.
2. Read this plan, CONTACT_PHYSICS, V2 implementation sections 1–3 and 11–12, and the legacy checkpoint's source/archived-state sections. The full solver equations are unnecessary for this archive-only assignment.
3. Record current HEAD, UTC start, tracked dirty paths and untracked source paths. Save the initial tracked patch outside the repository and hash affected source before editing. Preserve unrelated source/content/settings, the untracked application bundle and unfinished impact capture. Never use `git reset --hard`, `git clean`, broad checkout or `git add .`.
4. Inventory and hash surviving archives in `/private/tmp/b3r-diagnostic-tests`, `/private/tmp/b3r-current-diagnostic-tests`, and `/private/tmp/b3r-overrelax-tests`. Record missing paths as missing. The older shared-4/2 original was overwritten; the separate surviving original is identified in the [checkpoint](evidence/B3R-physics-viability.md). Directory membership alone is insufficient to call a file original: preserve source/prototype provenance and hashes.

Protected runtime hashes are in [design evidence](evidence/B3R-v2-design.md). `ParallelGrains.compute`, `ParallelGrainSolver.cs` and `ParallelGameplaySession.cs` contain user changes and must remain byte-identical during V2-0A. If a required input cannot be obtained through existing APIs, report that concrete missing field before expanding into runtime changes. Do not silently introduce a debug setter into the solver.

## 3. Source map and allowed edits

| File under `Assets/Debris/Simulation/` | Use/change |
|---|---|
| `Runtime/ParallelProof/ProofRecords.cs` | Read exact public grain/body/boundary fields and snapshot semantics |
| `Runtime/ParallelProof/ParallelGrainSolver.cs` | Read constructor, `Step`, `SnapshotAsync`; no edits |
| `Runtime/ParallelProof/ProofFixtures.cs`, `ProofDiagnosticFixtures.cs`, `ProofViabilityFixture.cs` | Reuse existing fixture definitions without changing population, geometry, force or mass |
| `Runtime/ParallelProof/ProofTraceArchive.cs` | Read old format/provenance only; its writer uses `File.Create`, so do not use it on old paths |
| `Tests/Editor/ParallelDiagnosticTests.cs` | Read replay/rollback examples; replace its fixed export directory with a unique per-run directory before running that fixture or the fast suite |
| **new** `Tests/Editor/CoupledReplayArchive.cs` | Editor-only DTOs, bounded binary reader/writer, hashing and immutable manifest publication |
| **new** `Tests/Editor/CoupledReplayCapture.cs` | Editor-only fixture capture and fresh-comparator replay helpers |
| **new** `Tests/Editor/CoupledReplayArchiveTests.cs` | Archive validation and exact roundtrip tests |
| **new** `Tests/Editor/CoupledReplayCaptureTests.cs` | Explicit GPU capture/replay matrix and one small ordinary GPU replay regression |

Use namespace `Debris.Simulation.Tests`. Keep new code in the existing Editor test assembly; add the corresponding Unity `.meta` files. Do not add a package, assembly definition, generic serialization framework or runtime dependency. Documentation/evidence updates listed in section 8 are also in scope.

**Observed overwrite hazard:** `ParallelDiagnosticTests.TraceDisabledAndEnabledMatchRejectedPackedCases` currently exports to fixed names beneath `/private/tmp/b3r-current-diagnostic-tests`. Change only its output-location construction to a newly reserved run directory, keeping assertions and physics unchanged. Search other test callers before running the broad suite; do not let validation overwrite evidence while testing immutable capture. The runtime trace writer need not change when every caller in this batch writes inside its own newly reserved output directory.

## 4. Archive contract, revision 1

Create a run directory beneath `Logs/b3r-v2-0a/<UTC>-<HEAD-short>-<GUID>/`, with separate `inputs/` and `outputs/`. Reserve the run with an exclusive-create lock file and reject collisions. Each case has `input.bin`, `manifest.json`, and a separate output report. Existing files are errors, never overwrite targets. Record relative paths and SHA-256 values in committed evidence; keep bulky generated files in Logs. Also commit a small synthetic binary fixture only if needed for a format regression, with its matching `.meta`; do not commit generated large traces by default.

Use explicit little-endian scalar serialization, preserving Float32 bit patterns; do not write the authoritative arrays as decimal JSON or use raw C# struct memory with implicit padding. JSON contains descriptive metadata, not the authoritative physical values. Use magic `B3R_PRESTEP`, format version 1, explicit section IDs/counts/byte lengths and SHA-256 of the complete closed binary in the manifest. Reject unknown versions, missing/duplicate sections, negative/oversized counts, inconsistent lengths, trailing bytes and hash mismatches before constructing a solver. Cap input size at 64 MiB, grains at 8,192, rigid bodies at 18 and boundary records at 4,096; traces are not embedded in this format.

Required contents:

- All 48-byte public grain fields, including reserved words; full committed endpoint state, including unused allocated slots; original body parameters and boundary records, with their exact scalar encoding documented beside the serializer.
- Active count and allocated grain/boundary capacity separately; grain masses as originally supplied to the constructor, including whether the default mass path was used. Do not infer original mass by inverting a rounded inverse mass. Preserve endpoint indexing: body endpoints start at **allocated capacity**, not active grain count.
- Stable grain identities, fixture body ordinal/identity mapping, shape revisions, mobility and page origin. Current proof fixtures have no production persistent body-ID table; label their deterministic fixture identities explicitly rather than inventing production IDs or pretending they were captured.
- Constructor options: velocity/position iterations, friction, candidate slots, rigid contact capacity and allocated capacities. No cache is present in this legacy comparator: record `cacheKind=none`; do not fabricate a V2 warm cache.
- The pending `Step` command: local force, torque, suction force, mounted-suction flag, and fixed dt=1/60. Record fixture recipe, fixture source hash and profile. The source's `ProofViabilityFixture.Revision` is **1**, while the completed player workload/report is **revision 2** because of its measurement setup. Record both meanings; do not edit the fixture constant to make the labels match.
- Original committed/attempted tick ordinals, pre-step diagnostic words for provenance, capture phase `CommittedBeforeStep`, run/editor/backend/source identity and exact source hashes. Diagnostic counters are provenance, not fields to inject into a new solver. The restored comparator starts its own local tick at zero; map local replay attempt 1 to the recorded source attempt.

Write and close the binary, hash it, then publish the JSON manifest last using exclusive creation. Treat a missing final manifest or incomplete set as unusable. Failure cleanup may remove only files created by this failed operation; it must never delete a pre-existing archive. A duplicate-write test must confirm original bytes and hashes remain unchanged.

Historical `.b3rt.gz` traces remain read-only supplemental inputs. `SubstepStart` is after force preparation. Reversing that force increment is approximate and cannot produce an `ExactPreStep` archive. This session captures new exact inputs from the unchanged comparator and records differences from historical trajectories honestly.

## 5. Capture and replay algorithm

For each case, clone its immutable constructor configuration before creating the solver. Read and await the initial `SnapshotAsync` **before the first Step**, so a tick-1 failure also has an exact prior state. Never leave multiple outstanding ticks in this diagnostic harness.

```text
prior = await initial committed snapshot
repeat within the case's attempt limit:
    input = clone prior physical arrays + original configuration + next command
    submit exactly one unchanged legacy Step
    await completed snapshot; fail on readback exception
    if a genuine physics fault occurred:
        verify current committed physical arrays equal prior bit for bit
        write input once, with fault/output in the separate report
        dispose the first solver
        load and validate the archive
        construct a fresh legacy solver from that archive
        verify its initial physical arrays equal the recorded input bit for bit
        execute the archived command once and await completion
        verify rejected attempt and exact physical rollback
        record source tick versus local replay tick and both diagnostics
        stop this case
    prior = current
```

Replay uses archived arrays/options, not regenerated fixture values or the original live arrays. Separate input-bit identity from attempted working-state reductions: current GPU adjacency reductions can differ in low bits. Require the same fault classification on repeated identical input, exact input/rollback physical state, and report actual maxima rather than inventing a tolerance that hides a mismatch. If fault classification changes across repeats, retain both outputs and mark reproduction unstable; do not tune the solver to stabilize the report.

Do not restore old accumulated diagnostics into a fresh solver. Compare physical arrays bitwise, compare fault classes, and normalize only tick bookkeeping through the explicitly recorded source-to-local mapping. Historical running maxima and a fresh single-attempt maximum need not be equal.

## 6. Required matrix and limits

| Family | Inputs | Maximum attempts to obtain failure |
|---|---|---:|
| Canonical shared packed | Existing diagnostic shared-motion fixture; each 4/2, 8/4, 12/6 | 120 per case |
| Canonical resting/forced packed | Existing diagnostic resting/forced fixture; each profile | 120 per case |
| Combined 8,192 | Existing `ProofViabilityFixture.Create()` and its original commands; each profile | 8 per case |

Total: nine new exact inputs. Retain the 2,500 + 1,000 + 4,692 population, finite ship/16 fragments and anchor. This Editor replay does not render the player fixture or measure its 16 terrain fields; report it as physical-input reproduction, not another throughput run. Expected historical combined faults are attempted 1/1/2 with committed 0/0/1. Record observed values; a different trajectory requires diagnosis, not editing expected results to force a pass.

Mark the full nine-case capture test explicit so routine fast tests never recapture large archives. Allow 30 minutes including shader compilation, setup, GPU readbacks, exports and cleanup; each canonical case remains bounded by 120 attempts. Record timeouts as failures. If the limit is inadequate, retain elapsed/stage evidence and stop rather than automatically launching the same expensive run again.

Stop this batch on missing required constructor state, shader/readback failure, invalid archive, no expected rejection within the limit, unstable fault class, or changed committed physical state. Save partial evidence and state the exact unfinished cases. Absent historical temporary files alone do not block newly captured exact inputs; report that those historical comparisons are unavailable.

## 7. Verification to run after implementation

- Pure archive tests: exact field/bit roundtrip (including signed zero and nonzero reserved words), malformed/truncated/version/length/hash rejection, capacity versus active indexing, missing manifest and duplicate-write preservation. Invalid physical masses/nonfinite motion must be rejected by the replay validator even if the codec can preserve their bits.
- One small ordinary GPU regression: a successful analytical fixture has identical physical output after archive reload; a deliberately rejected attempt preserves its input. This avoids relying solely on a failure that could be caused by malformed replay input.
- The explicit nine-case matrix: input identity, genuine physics fault, fresh replay and rollback for every case, with separate output paths. Run once when archive/capture tests are stable. Repeat only a case affected by a diagnosed defect and record the reason.
- If changing the existing diagnostic test's export helper, run its relevant output-path coverage without reading/writing historical directories. Existing numerical assertions must remain intact.

Use the pinned `/Applications/Unity/Hub/Editor/6000.3.11f1/Unity.app/Contents/MacOS/Unity` with `-batchmode -projectPath /Users/jakeferrigan/Documents/Debris -runTests -testPlatform EditMode -testFilter <fully-qualified-test> -testResults <unique-absolute-xml> -logFile <unique-absolute-log>`. Resolve the actual implemented test name before launch; inspect fresh XML for a nonzero executed test count and successful runner completion. Do not copy a placeholder literally. Close only the Debris editor if batch mode requires it.

Editor-only archive/test changes need focused checks, not a Mac build/player rerun. Runtime changes are outside this bounded session; stop with the proposed missing API rather than evading the repository's broader verification requirements. No Unity run is required merely to prepare these documents.

## 8. Evidence and completion

Create `docs/evidence/B3R-v2-convergence.md` with a **V2-0A only** section: source manifest, archive paths/hashes/provenance, nine-row attempt/commit/fault/rollback table, historical comparisons, all test invocations and failures, elapsed time, and unavailable client usage unless actually reported. Leave V2-0B/V2-0C and full V2-0 unchecked. All GPU throughput remains unmeasured.

Update STATUS to V2-0B only after V2-0A passes; otherwise name the exact reproduction defect. Update EXECUTION_PLAN, PERFORMANCE's current handoff and the feature-batch ledger without changing historical runtime measurements. Commit only explicit new test/helper/meta and necessary documentation paths plus the narrow diagnostic export-path edit. Inspect the staged diff to ensure protected user source is absent; recheck its hashes; commit and push origin/main under existing authorization.

Final response: implemented replay behavior, nine-case result, source/archive identity, tests and failed attempts, commit, unresolved limits, and **next task V2-0B independent geometry/tiny direct reference** if this batch passes. Do not describe V2 as implemented, converged, fast or qualified.
