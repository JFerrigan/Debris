# Copy into the next Claude session

```text
Work in /Users/jakeferrigan/Documents/Debris using one agent. Keep the model and effort I selected for this Claude session. Do not delegate or change account/model configuration.

Implement ONLY V2-0A: immutable exact committed-pre-Step archives and rejected-attempt replay, following docs/CLAUDE_V2_0A_PLAN.md. This is the bounded first part of V2-0, not the complete numerical solver assignment. Finish its evidence and commit/push, then stop even if it passes.

Start with git status --short. Explicitly read AGENTS.md, docs/STATUS.md, the current B.3R block of docs/EXECUTION_PLAN.md and PROJECT_PLAN.md once. Then read the V2-0A plan and only its listed source/design sections. Follow its steps in order; do not spend this session reopening solver architecture.

Preserve all pre-existing dirty/untracked work, especially unfinished impact capture. Capture source identity before editing. ParallelGrains.compute, ParallelGrainSolver.cs and ParallelGameplaySession.cs must remain byte-identical. The design checkpoint is d2eb84c; use actual current HEAD plus hashes, never assume the working tree is clean.

Deliver Editor-only archive/capture helpers and meaningful tests. Capture nine cases: canonical shared and resting/forced 2,500-grain fixtures, plus the unchanged combined 8,192-grain fixture, each under legacy 4/2, 8/4 and 12/6. Read the initial committed snapshot before the first Step. Preserve original constructor options, masses, allocated capacity, endpoint indexing and exact pending command. Reload archives into a fresh unchanged legacy solver and verify input bits, genuine fault classification and exact physical rollback. Source tick and fresh local tick are distinct; do not inject diagnostic counters into the solver.

Use versioned bounded binary inputs, SHA-256 manifests, exclusive creation and separate input/output directories. Preserve old traces; post-force SubstepStart reconstructions are approximate. Fix the existing diagnostic test's fixed export directory before running it so validation cannot overwrite surviving archives. Missing old temporary files must be reported, not recreated under an old name.

Use the allowed file list, archive schema, capture algorithm, nine-case matrix, time limits and acceptance checks in the plan. Keep the full capture matrix explicit and outside routine fast tests. Run focused Editor tests with unique fresh logs/XML and verify a nonzero executed count. No Mac build/player run for this Editor-only batch. Stop with evidence if it requires runtime edits, cannot reproduce a stable fault, or fails archive/rollback checks; do not relax checks or invent a workaround.

No independent geometry/SVD/Newton/GMRES implementation yet. No GPU shader changes, threshold relaxation, density reduction, frozen bodies, fault suppression, CPU runtime contact fallback, damage/fuel, persistence/travel, cutover, or larger workload.

Create docs/evidence/B3R-v2-convergence.md with V2-0A scope, exact hashes and per-case results. Update STATUS, EXECUTION_PLAN, PERFORMANCE and the feature-batch ledger. Leave full V2-0 open. Stage explicit paths, inspect the staged diff, verify protected source hashes, commit and push origin/main.

Final response: behavior implemented; all nine capture/replay outcomes; source/archive identity; tests and failed attempts; commit; remaining limitations. If V2-0A passes, the exact next task is V2-0B independent Float64 geometry and tiny direct contact reference. Stop there.
```
