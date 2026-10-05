# Implementation status

Current handoff: **V2-1 GPU work has begun as an isolated frozen-contact operator; it is not connected to gameplay.** The user explicitly prioritized tangible GPU progress while retaining the no-tests direction. A separate compute shader now evaluates scaled `J W Jᵀ` contact products with fixed-size segmented body reactions; an Editor diagnostic caller and focused cases are defined. It has not been compiled, run or timed. V2-0C's packed Float64 reference and separate double comparison remain unverified, including the post-correction nine exact inputs under `Logs/b3r-v2-0a/20260925T005106Z-f0c73f7-3c17c6d83c514f76b0db7db4c9f06ce1/`. See [GPU operator handoff](evidence/B3R-v2-gpu-operator.md) and [V2 reference evidence](evidence/B3R-v2-convergence.md). V2-0/V2-1, R1 and B.GATE remain open; no qualified GPU V2 solver or throughput exists.

Limited candidate flight, drilling, effective door collision, GPU cavity classification/capacity admission and mounted suction exist under `-debrisParallelGameplay`; legacy remains the default. Candidate startup has 96 grains. Terrain/door topology changes are fenced, obstructed closure stays open, and classification accepts only whole oriented squares.

## Latest validation

Latest accepted checks remain V2-0B focused EditMode geometry 5/5 and contact/archive 9/9. V2-0C had an isolated 8-square finite-boundary check before a discovered perpendicular-contact geometry defect was fixed; that check passed, but no final V2-0C Editor result or nine-case replay followed. Earlier nine-case nonconvergence observations used the defective geometry and are invalid for a V2-0 decision. No tests, builds or player runs were made after the user's no-tests direction. The prior fast suite passed 140 with one explicit scale skip (2026-09-24 04:58 UTC); its matching player still failed combined 4/2, 8/4 and 12/6 after 0, 0 and 1 committed ticks. Physics/total GPU and frame/CPU p95 remain unmeasured.

## Remaining limitation and next work

Next GPU work is V2-1 manifold generation and GPU-owned incidence construction, then a complete operator comparison against the double reference and the required scheduling/cost gate. The new diagnostic operator currently accepts CPU-supplied frozen rows; it does not implement broad phase, manifold construction, the Newton solve, tick rollback, candidate gameplay selection or async production readback. After the user sets the test plan, focused V2-0C checks and all nine exact archives still need to establish whether the formulation satisfies physical bounds. The separate V2 double comparison has bounded restarted GMRES(16) and a 20-body chain regression case, both unrun. Global packed numerical rank remains unresolved. The opt-in legacy runner still rejects packed correctness before throughput sampling; physics GPU source-frame validation remains unexercised.

The [candidate damage/fuel transaction](CANDIDATE_DAMAGE_FUEL_PLAN.md) remains paused until V2-3 qualification and V2-4 candidate integration pass. V2 C1/C2/C3 profiles and the 111 MiB buffer allowance are design values, with convergence and all costs unmeasured. Damage, fuel transfer, persistence, travel and streaming remain unavailable on the candidate. R1 and B.GATE remain open.

## Preserved unfinished work

Matter.compute, ShipMatter.hlsl, MatterSession.cs and ContactIsland.hlsl retain the unfinished contact gatherer; [baseline patch](evidence/B3R-redesign-baseline/unfinished-contacts.patch). Presentation, ship/content, rigid-terrain exclusion, persistence, package/settings changes and DebrisV1.app remain unfinished workspace work. ParallelGameplayGrains.compute is an unused pre-existing duplicate; the solver now loads canonical ParallelGrains.compute. The built application includes existing workspace changes. Do not rerun the 100,000-site fixture for this handoff.

ParallelGrains.compute, ParallelGrainSolver.cs and ParallelGameplaySession.cs still contain the user's uncommitted strongest-impulse/feature capture. The measured build included it; preserve it. It is not a completed energy-based damage event pipeline.

## Commands

Unity 6000.3.11f1: `bash tools/unity.sh test`, `build`, `open`. Candidate gameplay: `Builds/Debris.app/Contents/MacOS/Debris -debrisParallelGameplay`. Viability reproduction: `Builds/Debris.app/Contents/MacOS/Debris -debrisParallelProof -debrisProofViability -debrisProofOutput Logs/b3r-viability-repro.txt -logFile Logs/b3r-viability-repro-player.log`. Documentation changes need no rebuild.
