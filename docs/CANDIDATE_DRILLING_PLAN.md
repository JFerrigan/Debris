# Candidate drilling implementation plan

Status: proposed implementation, grounded in `029b84d` and the current workspace. This document specifies the next bounded B.3R.R2 feature: safe, opt-in terrain drilling. No implementation or gameplay acceptance is claimed. The current request authorizes planning; it does not resume unrestricted feature expansion.

## 1. Observable result and scope

With `-debrisParallelGameplay`, holding LMB at reachable asteroid material accumulates drill damage and releases physical grains. A successful release removes exactly one terrain cell, creates exactly one same-material grain, updates collision geometry, and updates the rendered terrain. A rejected release changes none of those states. Reset recreates the deterministic 96-grain layout and original terrain.

First integration is deliberately bounded: one selected cell per accepted drill sample, at most one sample per simulation tick, and one topology transaction in flight. Use the existing mounted tool geometry (local centre `(58,0)`, radius `6`) and power `120`, with `dt=1/60`. Accumulate damage against material durability. This is a limited candidate drill, not a claim of legacy area-cut throughput. Suction, doors, damage to ships, physical fuel transfer, persistence and travel remain unavailable.

Keep 8,192 grain slots, ship endpoint `GrainCapacity`, fragment endpoints immediately after the ship, and the final anchored terrain endpoint. Reserve 4,096 boundary slots for the complete ship/fragment/terrain cache, including enclosure faces; this is separate from the 4,096 rigid-contact limit.

## 2. Findings that constrain implementation

| Current location | Finding | Required consequence |
|---|---|---|
| `ParallelGrainSolver` constructor | `boundaries` allocates exactly `patches.Length` | Allocate fixed candidate capacity and separate capacity from active count. |
| `ParallelGrainSolver.TryAppend` | Writes live buffers and increments `_N` immediately | Never compose this public call with a separate terrain upload to implement drilling. |
| `ParallelGameplaySession.Submit` | Up to 16 unresolved GPU ticks | Stop new submissions while draining a requested edit; otherwise held thrust can starve topology changes. |
| `ParallelGameplaySession.AddBody` | Combines mask-to-boundary extraction with mass/COM construction | Extract reusable boundary construction; never recreate body motion during terrain edits. |
| `ParallelGameplaySession.Import` | Terrain revision is `uint.MaxValue`; source snapshot is retained by reference | Give terrain a real revision sequence and own copies of mutable terrain data. |
| `MatterSession.UploadChunk` / `Matter.compute.Upload` | Separate dispatch; clears the entire chunk's damage | Do not use this for a candidate cell transaction. |
| `MatterView.DrawCandidate` | Draws legacy `Field` plus candidate GPU grains and bodies | Update the same field texture in the same ordered publication as solver geometry and grain admission. |
| `ProofMetrics` / `ParallelMetrics.compute` | Uses initial grain count and contiguous endpoint indices | Fix count growth and body indexing before using metrics as transaction evidence. |
| `Showcase.Update` | Candidate tools are disabled and flight uses acknowledged fuel reservations | Integrate drilling without making a rejected edit consume flight fuel or lose a physics tick. |

Read these findings against [contact physics](CONTACT_PHYSICS.md), [execution plan](EXECUTION_PLAN.md), and [current status](STATUS.md). Preserve all unrelated dirty source, content, packages, settings and application bundles.

## 3. Transaction contract

Use a small session-owned state machine: `Idle -> Draining -> ValidatingPlacement -> ReadyToPublish -> Idle`. Any ordinary rejection returns to `Idle`; a GPU/readback/device failure enters the existing faulted-session path. No next physics submission is permitted between placement validation and publication.

1. Latch one mounted-drill request, including its fixed sample duration. Do not queue repeated samples while held input is waiting.
2. Stop new `Submit` calls, but continue `TryAcknowledge` and apply existing acknowledged flight/fuel normally.
3. Once `PendingTicks == 0`, derive drill centre from the last acknowledged solver ship COM, angle and local COM. Import supplies the initial committed pose for an edit before the first tick. Do not use an older presentation pose.
4. Select a terrain cell deterministically: exposed occupied cells whose centres are in the drill disk; nearest to tool centre, then world Y, then world X. If none, return `NoTarget`. This deliberately avoids drilling arbitrary buried cells through a wall.
5. Prepare a private edit: old/new damage, material, cell address, expected terrain revision, optional new grain and replacement boundaries. Clone only affected terrain data. The live terrain arrays, damage, identity counter and GPU buffers remain unchanged.
6. For damage below durability, publish damage only. For release, validate grain capacity, identity availability, material density, world/page bounds and boundary capacity before requesting GPU placement validation.
7. Validate the proposed unit square against committed grains and solid ship/fragment cells on GPU. Return only a small status record. Do not download the grain population. Use oriented-square SAT, including rotated grains and hull cells; outline patches alone cannot detect containment inside a solid hull.
8. Prepare all uploads, command recording, managed identity storage and render bindings before publication. Verify mirror geometry/lifetime and the expected revision again.
9. Submit one ordered GPU command buffer which installs the new boundary payload/body ranges, new grain slot/state/mass, and matching terrain material/damage. The same graphics queue orders this before candidate drawing and subsequent physics. No simulation dispatch or draw may observe an intermediate command.
10. After successful enqueue, publish CPU terrain/damage, active counts, revision and identity together, without allocation or callbacks. New grains are world-space, non-cargo, awake, with mass `density`, inertia `mass/6`, angle/spin zero. Initially use zero release velocity: this preserves the anchored terrain's rest state and avoids an unaccounted launch impulse. Mining ejection/recoil is a later explicit behavior decision.

A release uses the removed cell's exact world centre, `cell + (.5,.5)`; do not teleport it to arbitrary free space. Existing residual overlap is grounds to reject placement, not permission to create more overlap. Specify one small numerical tolerance in the placement helper and test touching versus penetrating separately; do not use the relaxed contact solver gate as spawn clearance.

Identity and terrain revision increment only on successful release; neither wraps. A damage-only edit changes neither. Repeated release of the same empty cell does nothing. Grain-capacity, patch-capacity and placement rejection preserve the pre-attempt damage as well as terrain. Distinguish `DamageApplied`, `Released`, `Busy`, `NoTarget`, `GrainCapacity`, `BoundaryCapacity`, `PlacementBlocked`, `IdentityExhausted`, `RevisionExhausted`, `StaleEdit`, `Unavailable` and `Faulted`.

“All failures change nothing” applies to application-level rejection before publication. A device loss or an exception during GPU submission cannot truthfully promise hardware rollback. Fault and pause the session, suppress further candidate drawing/submission, retain the error, and require reset; never report that case as an ordinary rejected edit. This limitation must be explicit in evidence.

## 4. Exact source changes

Names and signatures below are the implementation targets. Private visibility may be narrowed further; do not expose mutable prepared arrays to callers.

### 4.1 Add `Runtime/CandidateTerrainState.cs`

Namespace `Debris.Simulation`; sealed CPU owner of terrain structure and staged edits, not loose-body motion.

Fields: immutable side/chunk size/origin; owned `uint[][] fields`, `float[][] damage`; `uint revision`, `nextIdentity`; terrain endpoint; cached terrain outline. Validate import dimensions, valid material indices, finite nonnegative damage, identity uniqueness/exhaustion and page support before allocating solver resources.

Add:

- `CandidateTerrainState(MatterSnapshot imported, int terrainEndpoint)` — clone mutable terrain arrays; initialize revision to `1`; normalize a zero `NextIdentity` only with a checked maximum-existing-ID calculation.
- `bool TryAddress(Vector2Int worldCell, out int slice, out int index)` — centralize translated/chunk-border addressing.
- `uint MaterialAt(Vector2Int worldCell)` and `float DamageAt(Vector2Int worldCell)` — bounded read access for targeting/tests.
- `bool TrySelectDrillCell(Vector2 toolCenter, float radius, out Vector2Int cell)` — deterministic exposed-cell selection.
- `CandidateEditStatus TryPrepareCell(Vector2Int cell, float power, float dt, MaterialCatalog catalog, out CandidateTerrainEdit edit)` — produce damage-only or release proposal without changing live state.
- `bool IsCurrent(CandidateTerrainEdit edit)` — revision plus expected old cell material/damage check; revision alone does not catch a stale damage-only proposal.
- `void Publish(CandidateTerrainEdit edit)` — internal, allocation-free installation of a validated edit and successful-release identity/revision changes.

In the same file add `CandidateEditStatus`, internal sealed `CandidateTerrainEdit`, and readonly `CandidateEditResult`. The edit owns its payload and old-state expectation. The result contains status, request ID, cell, material, released identity, committed revision and active grain count; no full snapshot.

### 4.2 Add `Runtime/CandidateBoundaryBuilder.cs`

Extract the exposed-face loops from `ParallelGameplaySession.AddBody`; use the same path for import and edits.

Add:

- `AppendMaskBoundaries(List<Boundary> output, uint[] mask, int endpoint, int originX, int originY, bool enclosePage)` — preserve exact local coordinates, deterministic order and four enclosure faces.
- `BuildTerrainCache(CandidateTerrainState terrain, int endpoint)` — initial cached outline.
- `TryPrepareTerrainReplacement(CandidateTerrainEdit edit, Boundary[] fixedBodyPatches, BodyParameters[] bodyDefinitions, int capacity, out CandidateBoundaryReplacement replacement)` — rebuild affected terrain faces, flatten complete cache and repair terrain range/count; reject overflow without truncation.
- `ValidateReplacement(...)` — enforce finite positive extents, endpoint ownership, unique physical feature IDs, valid disjoint ranges and full coverage.

Add internal `CandidateBoundaryReplacement` with immutable prepared patches/body definitions and expected/new terrain revision. Dynamic-body definitions and prefix patches remain unchanged. Feature indices may be renumbered deterministically on a terrain revision; no old contacts survive that revision.

Refresh only dirty terrain geometry and the neighboring border faces as required by CONTACT_PHYSICS. Cache exposed face runs by row/column: an edit invalidates its row and adjacent rows, its column and adjacent columns; reuse all other runs, then flatten/merge deterministically. Do not rescan every terrain cell for every held-input sample. CPU concatenation of the bounded patch list is acceptable. Tests must prove incremental output equals a fresh outline rebuild, including chunk seams.

### 4.3 Modify `Runtime/ParallelProof/ParallelGrainSolver.cs`

Add constructor tail parameter `int allocatedBoundaryCapacity=0`; zero preserves exact-allocation proof fixtures. Candidate import passes `4096`. Validate active count <= capacity <= 4096; allocate at least one backing element for an empty proof cache.

Add fields/properties: `boundaryCapacity`, `boundaryCount`, cached body definitions; public `BoundaryCapacity`, `BoundaryCount`; disposal/fault guards for mutation. Keep GPU buffers stable so existing renderer bindings remain valid.

Add internal methods:

- `CandidateEditStatus ValidateAppend(LooseCell grain, float mass)` — shared pure validation, including duplicate ID and supported page.
- `CandidateEditStatus ValidateBoundaryReplacement(CandidateBoundaryReplacement replacement)` — ownership/range/count checks without writes; reject dynamic mass/COM/mobility/revision changes in this terrain-only API.
- `void RecordReplaceBoundaryCache(CommandBuffer target, CandidateTerrainTransaction transaction)` — record copies from prepared staging buffers into the live cache and body parameter ranges. Record only; never execute or publish active counts itself.
- `void RecordAppend(CommandBuffer target, CandidateTerrainTransaction transaction)` — write inactive slot in grains, committed state, working state and parameters; no active-count publication.
- `void PublishTerrainEdit(CandidateTerrainTransaction transaction)` — update host active counts, identity set, body definitions and metrics epoch after enqueue; pre-reserve host storage so publication does not allocate.

This is the atomic `ReplaceBoundaryCache` path: it is intentionally an internal participant in the combined terrain transaction, not a separately callable live mutation followed by `TryAppend`.

Modify `Step` to record `_N`, `_BoundaryCount`, `_BodyStart` and endpoint counts into its command buffer for that submission. Do not rely on mutable shader globals changing underneath already recorded work. Ensure all dispatch sizes use active counts, never reserved boundary capacity.

Retain `TryAppend` for existing proof/startup tests; share validation and update metrics correctly, but explicitly forbid traced/growing use unless supported. Candidate gameplay must use the combined transaction. Trace capture currently fixes populations at construction: reject topology mutations while tracing rather than silently writing an incomplete trace. Update `Dispose` for added owned resources only.

### 4.4 Add `Runtime/CandidateTerrainTransaction.cs`

Namespace `Debris.Simulation`; sealed `IDisposable`, owned by the candidate session. Own reusable staging buffers, a placement-status buffer/readback and one command buffer. Reuse buffers sized to bounded capacities; do not allocate a new simulation or duplicate all grain state per cut.

Add:

- `CandidateTerrainTransaction(ParallelGrainSolver solver, MatterSession mirror, ...)` — validate target dimensions/lifetime; upload immutable ship/fragment masks for exact placement tests. Do not own/dispose the mirror itself.
- `CandidateEditStatus Prepare(CandidateTerrainEdit edit, CandidateBoundaryReplacement replacement)` — check all CPU preconditions and stage payloads, with no writes to active buffers/textures.
- `void BeginPlacementValidation()` — dispatch read-only placement query after the session drain.
- `bool TryCompletePlacementValidation(out CandidateEditStatus status)` — compact readback; a failed readback faults the session.
- `CandidateEditStatus TryPublish()` — recheck expected state, record complete publication, enqueue once, then invoke solver/terrain host publication. No recoverable validation is allowed after the first live GPU write.
- `void Dispose()` — drain this owner's pending readbacks before releasing resources; reset/destroy must not let callbacks mutate a replacement session.

Bind the existing public `MatterSession.Field`, `Damage` and `Dirty` buffers directly. Do not call `Restore`, `UploadChunk`, `Step`, or legacy occupancy rebuilding. Legacy loose counters/inventories are not candidate authority and must not be partially reconstructed. Candidate HUD uses candidate facts.

### 4.5 Add `Resources/CandidateTerrainEdits.compute`

Use the existing 32/48-byte solver layouts; add explicit C#/shader layout assertions for transaction payloads. Include `ParallelContactGeometry.hlsl` for SAT helpers.

Kernels:

- `ClearPlacementStatus` — clear the compact rejection bits.
- `ValidatePlacementGrains` — inspect only active committed grains; reject overlap using oriented-square SAT.
- `ValidatePlacementBodies` — inspect occupied ship/fragment mask cells at committed body poses, using `world = COM + Rotate(localCellCentre - LocalCOM, angle)`; use broad bounds to skip irrelevant cells. No CPU snapshot and no boundary-only containment test.
- `InstallBoundaryCache` — copy staged active patches and body definitions to fixed live buffers; stale tail entries are inaccessible by active counts.
- `InstallGrain` — write the reserved inactive slot and its state/mass, only for release edits.
- `PublishTerrainCell` — update exactly one field/damage address and its dirty flag; damage-only edits write damage without a grain or boundary update.

Publication kernels run only after successful CPU checks and placement readback under the exclusive edit fence. They have no fallible admission branches. Split writable resources across kernels to respect Metal binding limits. Record all publication kernels into the same command buffer and graphics queue; do not use async compute for this path.

Terrain overlap is checked against the prepared CPU mask: the removed square occupies precisely its former cell, neighboring intact cells may touch but may not overlap. GPU placement checks add current grain/body occupancy. Include the full square in world/page bounds checks, not merely its centre.

### 4.6 Modify `Runtime/ParallelGameplaySession.cs`

Add owned `CandidateTerrainState`, `CandidateTerrainTransaction`, immutable dynamic boundary prefix/definitions, latest acknowledged ship state, edit state/request ID and last edit result. Expose `TopologyBusy`, `LastEditResult`, `TerrainRevision` and a read-only drawing availability flag.

Add:

- `AttachTerrainMirror(MatterSession mirror)` — one-time attachment after import, before enabling tools; failure leaves drill unavailable.
- `bool RequestMountedDrill(float power=120, float radius=6)` — latch one sample; reject disposed/faulted/unattached/busy requests. No caller-provided pose.
- `bool TryAdvanceTerrainEdit(out CandidateEditResult result)` — progress drain, preparation, query and publication without blocking the main thread.
- `Vector2 MountedDrillCenter()` — use cached committed ship pose and local COM.
- `void SetFault(string message)` — consolidate GPU/edit/readback failures and preserve unresolved flight fuel accounting.

Modify `Import` to initialize owned terrain, stable revision and fixed boundary capacity, validating before constructing GPU resources. Modify `AddBody` to call the extracted builder; retain COM/mass construction. Replace `TerrainMask(MatterSnapshot)` with the terrain owner's validated initial mask path. Modify `Submit` to return false during edit fencing without changing tick or provisional fuel. Modify `TryAcknowledge` to retain the latest ship state. Dispose transaction/readbacks before solver buffers.

Keep imported cargo bookkeeping scoped to imported cargo: released terrain grains have no cargo flag and must not index beyond `source.Cells`/`cargo`. Do not add a second loose inventory to fix this; dynamic cargo classification belongs to the suction/cavity batch.

### 4.7 Modify metrics as an observed dependency

`Runtime/ParallelProof/ProofMetrics.cs`:

- Preserve existing constructors; add an overload accepting allocated grain capacity and body start.
- Allocate reduction storage for capacity + body count, while recording only active grains + body count.
- Add `SetPopulation(int activeGrainCount, int bodyStart)` and `BeginTopologyEpoch(CommandBuffer target)`; record an ordered baseline/result reset after an accepted addition.
- Update `SetCounts` and `Record` for active counts and `_BodyStart`; preserve the existing 80-byte readback layout.

`Resources/ParallelMetrics.compute`:

- Add `_BodyStart`; in `contribution`, map logical body index `id - _GrainCount` to `_BodyStart + id - _GrainCount` before reading state/parameters.
- Add `ResetEpoch` kernel for ordered baseline/result reset. Existing closed-system proof runs remain one epoch.

Document that metrics conservation applies within an epoch. A mining transaction changes the measured free-grain population; check terrain-plus-grain material conservation separately. Do not reset the baseline every physics tick or portray post-mining energy measurements as an unchanged closed system.

### 4.8 Modify presentation

`Presentation/Runtime/Showcase.cs`:

- `ActivateParallelAfterLoad`: attach mirror before binding/enabling the candidate; update mode text only after attachment succeeds.
- `Update`: acknowledge flight, advance an outstanding edit, then permit physics submissions. On held LMB, request at most one edit after an accepted physics tick and stop further tick submissions until it completes. A waiting edit must not accumulate synthetic drill damage, drain extra fuel, or run an unbounded catch-up burst.
- On completion resume flight; on reset cancel the old session through existing generation/disposal handling. On fault pause and avoid drawing potentially inconsistent candidate data.
- `OnGUI`: show LMB limited drilling, active grains/capacity, and concise last rejection reason; retain unavailable suction/door/save/travel labels. Remove candidate reliance on `session.Stats[2]` for saturation text.

`Presentation/Runtime/MatterView.cs`:

- Retain session reference in `BindCandidate` as needed for availability.
- `DrawCandidate`: respect fault/draw availability; otherwise stable buffers need no rebinding. A frame after publication must draw the removed terrain and incremented grain count together.

### 4.9 Explicit removals and unchanged architecture

Remove the old inline boundary-extraction loops from `AddBody` once shared-builder parity passes. Remove the old private `TerrainMask` implementation once callers use the owned terrain path. Remove candidate-only “drill unavailable” strings/guard only when the combined feature passes.

Do not delete `TryAppend`, the legacy solver, `CpuCutReference`, save formats, contact kernels or the unused dirty `ParallelGameplayGrains.compute` in this batch. `MatterSession.cs`, `Matter.compute` and `ShipMatter.hlsl` need no planned edits. The canonical `ParallelGrains.compute` continues collision solving; transaction kernels live separately. Every new Unity asset needs its `.meta` committed.

## 5. Acceptance tests by fixture and method

Add `Simulation/Tests/Editor/CandidateTerrainTests.cs` for pure CPU tests:

- `TranslatedChunkAddressesRoundTrip` — negative/transformed origins, seams and out-of-bounds.
- `ImportOwnsTerrainArrays` — editing cannot mutate caller snapshots.
- `DrillSelectionIsExposedBoundedAndDeterministic` — radius, burial and tie ordering.
- `DamageAccumulatesWithoutPrematureRelease` — finite sample duration and material durability.
- `PreparingEditDoesNotChangeLiveState` — fields, damage, revision and identity unchanged.
- `StaleDamageAndRevisionEditsAreRejected` — damage-only proposals cannot overwrite newer damage.
- `IncrementalBoundaryCacheMatchesFullRebuild` — holes, concave corners, split/merged runs and chunk-border edits.
- `EnclosureAndDynamicBoundaryPrefixSurviveEdits` — translated geometry and stable endpoint ownership.
- `BoundaryCapacityRejectsWithoutTruncation` — 4,096 accepted, 4,097 rejected.
- `IdentityAndRevisionExhaustionNeverWrap`.

Add `Simulation/Tests/Editor/CandidateTerrainTransactionTests.cs` with focused GPU coroutine tests and realistic fixture timeouts:

- `OneReleaseConservesMaterialAndPublishesTerrainAndGrain` — same material/mass, one identity, field/damage and boundary revision, independent test readback.
- `NewlyExposedFaceCollidesAfterRelease` — probe a remaining neighbor face; prove geometry behavior, not only patch counts.
- `RemovedFaceNoLongerBlocksMotion` — probe the hole after publication; no stale face.
- `RepeatedCutsGrowThenShrinkBoundaryCache` — active count shrinks correctly; stale tail patches never collide.
- `GrainCapacityRejectionPreservesCompleteState`.
- `BoundaryOverflowRejectionPreservesCompleteState`.
- `DuplicateIdentityAndStaleEditPreserveCompleteState`.
- `RotatedGrainAndHullOverlapRejectPlacement` — include a point inside a solid hull but away from its outline, plus touching accepted controls.
- `PendingTicksDrainBeforeEditAndFuelCommitsOnce` — request with multiple queued ticks; reject new submissions without changing tick/fuel reservations; resume monotonically.
- `PlacementReadbackFailureFaultsWithoutPublication` — inject failure through a narrow internal test seam, not a general transaction framework.
- `ResetDuringPlacementCannotPublishIntoNewSession`.
- `NormalDrillingDoesNotRequestPopulationSnapshots`.

Each rejection test compares material, damage, active grain count/identities, boundary contents/counts, body parameters/motion, revision, next identity and mirror field before/after. Ignore expected diagnostic/status changes. Tests may read full state; runtime may not.

Extend existing fixtures:

- `ParallelStartupTests`: preserve all existing five tests; add `ReservedBoundaryCapacityKeepsGpuBindingsStable` and zero-start/repeated growth coverage through transactions.
- `ParallelMetricsTests`: add `ReservedGrainRegionStillCountsShipMomentum`, `AcceptedAppendUpdatesPopulationAndIdentityMetrics`, `TopologyEpochPreservesSubsequentConservationChecks`.
- Reuse `CandidateStarterLayoutTests` for reset layout; add one integration check proving reset restores terrain, revision, 96 grains and original identity sequence.

## 6. Implementation order and completion gates

One coherent feature batch, with local checkpoints rather than an infrastructure-only player release:

1. Terrain ownership and boundary extraction/refresher; pure CPU tests and import parity.
2. Fixed boundary capacity, staged mutation hooks and metrics indexing/population correction; affected startup/metrics tests.
3. Placement query and atomic terrain/grain publication; rejection and repeated-release GPU tests. Keep the input guard in place.
4. Session drain scheduling, mounted damage semantics, input/HUD and reset/fault handling.
5. Focused fixtures on final code; then one fast EditMode suite, one Mac build and one matching bounded player run. No source edits during final verification. A failure is diagnosed before rebuilding.

Use the pinned editor with `-runTests -testPlatform EditMode -testFilter Debris.Simulation.Tests.CandidateTerrainTransactionTests`, unique result/log paths, and equivalent focused filters for the other fixtures. Check the actual runner exit/report. Final suite/build use `bash tools/unity.sh test` and `bash tools/unity.sh build`. Keep explicit scale fixtures out of this batch.

The single final player run must demonstrate startup with 96 grains; thrust/turn; drill damage then several releases; visible hole/collision agreement; movable released grains; rejection/throttle feedback; reset; no Metal/solver/readback fault. Capture compact edit/tick/pose/count/revision telemetry so activation alone cannot be mistaken for physical acceptance. Deliberate capacity/overflow states belong in automated fixtures, not a second expensive player run. If the run cannot establish the behavior, record acceptance as unverified rather than spending an unplanned rerun.

## 7. Completion record and remaining gates

Update STATUS, the B.3R.R2 note in EXECUTION_PLAN, CONTACT_PHYSICS implementation checklist and `evidence/feature-batches.csv` with actual results and unresolved limits. Preserve one canonical evidence report `docs/evidence/B3R-candidate-drilling.md`; record actual test/build/player counts, every expensive rerun reason, elapsed UTC times and usage `unavailable` unless client usage is supplied. Stage explicit paths, commit the coherent feature and notes, then push under existing authorization.

This batch closes only limited candidate terrain drilling acceptance. It does not close R1 convergence, packed-cargo/performance, the original `.001` target, B.GATE, persistence or default cutover. After acceptance, plan the next bounded cavity/suction/door transaction work against the resulting code; do not invent exact later-phase APIs in advance of those dependencies.
