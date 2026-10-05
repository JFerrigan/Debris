# Implementation status

Current handoff: **V2-0C offline packed Float64 reference and separate double V2 comparison are implemented but unverified.** The thin-strip opposing-face rule is explicit, and finite solid–solid patch pairs are now gathered. The user deferred all tests/builds/solver runs pending a revised test plan and physical review. The nine exact V2-0A inputs remain read-only under `Logs/b3r-v2-0a/20260925T005106Z-f0c73f7-3c17c6d83c514f76b0db7db4c9f06ce1/`; no post-correction nine-case result exists. See [V2 evidence](evidence/B3R-v2-convergence.md). V2-0C, full V2-0, R1 and B.GATE remain open. No GPU V2 solver or qualified throughput exists; damage/fuel stays paused.

Limited candidate flight, drilling, effective door collision, GPU cavity classification/capacity admission and mounted suction exist under `-debrisParallelGameplay`; legacy remains the default. Candidate startup has 96 grains. Terrain/door topology changes are fenced, obstructed closure stays open, and classification accepts only whole oriented squares.

## Latest validation

Latest accepted checks remain V2-0B focused EditMode geometry 5/5 and contact/archive 9/9. V2-0C had an isolated 8-square finite-boundary check before a discovered perpendicular-contact geometry defect was fixed; that check passed, but no final V2-0C Editor result or nine-case replay followed. Earlier nine-case nonconvergence observations used the defective geometry and are invalid for a V2-0 decision. No tests, builds or player runs were made after the user's no-tests direction. The prior fast suite passed 140 with one explicit scale skip (2026-09-24 04:58 UTC); its matching player still failed combined 4/2, 8/4 and 12/6 after 0, 0 and 1 committed ticks. Physics/total GPU and frame/CPU p95 remain unmeasured.

## Remaining limitation and next work

Next, after the user sets the test plan, compile and run focused V2-0C Editor cases with unique logs/XML, then replay all nine exact archives. Record normal/friction and position residuals, local rank and global rank limit, endpoint error, first failing feature, nonconvergence and physical gaps; an inconclusive solve is not infeasibility. The separate V2 double comparison now has bounded restarted GMRES(16), two search-only regularization retries and a 20-body chain regression case, all unrun. Global packed numerical rank and a full packed V2-0 decision remain unresolved. The opt-in legacy runner still rejects packed correctness before throughput sampling. Its 720-tick extension never reaches the unforced phase, so long-run residual and fragment participation remain unverified. Physics GPU source-frame validation remains unexercised.

The [candidate damage/fuel transaction](CANDIDATE_DAMAGE_FUEL_PLAN.md) remains paused until V2-3 qualification and V2-4 candidate integration pass. V2 C1/C2/C3 profiles and the 111 MiB buffer allowance are design values, with convergence and all costs unmeasured. Damage, fuel transfer, persistence, travel and streaming remain unavailable on the candidate. R1 and B.GATE remain open.

## Preserved unfinished work

Matter.compute, ShipMatter.hlsl, MatterSession.cs and ContactIsland.hlsl retain the unfinished contact gatherer; [baseline patch](evidence/B3R-redesign-baseline/unfinished-contacts.patch). Presentation, ship/content, rigid-terrain exclusion, persistence, package/settings changes and DebrisV1.app remain unfinished workspace work. ParallelGameplayGrains.compute is an unused pre-existing duplicate; the solver now loads canonical ParallelGrains.compute. The built application includes existing workspace changes. Do not rerun the 100,000-site fixture for this handoff.

ParallelGrains.compute, ParallelGrainSolver.cs and ParallelGameplaySession.cs still contain the user's uncommitted strongest-impulse/feature capture. The measured build included it; preserve it. It is not a completed energy-based damage event pipeline.

## Commands

Unity 6000.3.11f1: `bash tools/unity.sh test`, `build`, `open`. Candidate gameplay: `Builds/Debris.app/Contents/MacOS/Debris -debrisParallelGameplay`. Viability reproduction: `Builds/Debris.app/Contents/MacOS/Debris -debrisParallelProof -debrisProofViability -debrisProofOutput Logs/b3r-viability-repro.txt -logFile Logs/b3r-viability-repro-player.log`. Documentation changes need no rebuild.
