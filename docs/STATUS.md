# Implementation status

Current handoff: **V2-0A exact pre-step capture/replay passed; next task is V2-0B independent Float64 geometry and tiny direct contact reference.** The unchanged legacy comparator reproduced all nine rejected attempts with bitwise physical rollback; see [V2-0A evidence](evidence/B3R-v2-convergence.md). Generated archives are under `Logs/b3r-v2-0a/20260925T005106Z-f0c73f7-3c17c6d83c514f76b0db7db4c9f06ce1/`. V2-0C packed reference, the full V2-0 decision, R1 and B.GATE remain open. No new solver or qualified throughput exists; damage/fuel stays paused.

Limited candidate flight, drilling, effective door collision, GPU cavity classification/capacity admission and mounted suction exist under `-debrisParallelGameplay`; legacy remains the default. Candidate startup has 96 grains. Terrain/door topology changes are fenced, obstructed closure stays open, and classification accepts only whole oriented squares.

## Latest validation

V2-0A focused EditMode results: archive codec 4/4, ordinary accepted/rejected replay 1/1, explicit nine-case matrix 1/1, and diagnostic export-path coverage 1/1, all passed. No Mac build/player run was needed. The prior fast suite passed 140 with one explicit scale skip (2026-09-24 04:58 UTC); its matching player still failed combined 4/2, 8/4 and 12/6 after 0, 0 and 1 committed ticks. Physics/total GPU and frame/CPU p95 remain unmeasured. Windows/Linux and candidate suction input remain unverified.

## Remaining limitation and next work

The opt-in runner creates 8,192 active grains, 16 dynamic fragments and 16 allocated terrain chunks, but correctness rejects before throughput sampling. All canonical packed profiles cross the original .001 target and later reject at the interim .002 grain/solid limit; rigid/solid remains .001. The 720-tick extension never reaches its unforced phase, so long-run residual and fragment participation remain unverified. Scalar over-relaxation and deeper full-graph sweeps did not solve coupled convergence. The candidate renderer uses procedural grain, rigid-patch and sparse terrain draws. Physics GPU samples now require submitted-frame identity and one marker block; this check remains unexercised, and unmatched total GPU timing is explicitly unmeasured.

The [candidate damage/fuel transaction](CANDIDATE_DAMAGE_FUEL_PLAN.md) remains paused until V2-3 qualification and V2-4 candidate integration pass. V2 C1/C2/C3 profiles and the 111 MiB buffer allowance are design values, with convergence and all costs unmeasured. Damage, fuel transfer, persistence, travel and streaming remain unavailable on the candidate. R1 and B.GATE remain open.

## Preserved unfinished work

Matter.compute, ShipMatter.hlsl, MatterSession.cs and ContactIsland.hlsl retain the unfinished contact gatherer; [baseline patch](evidence/B3R-redesign-baseline/unfinished-contacts.patch). Presentation, ship/content, rigid-terrain exclusion, persistence, package/settings changes and DebrisV1.app remain unfinished workspace work. ParallelGameplayGrains.compute is an unused pre-existing duplicate; the solver now loads canonical ParallelGrains.compute. The built application includes existing workspace changes. Do not rerun the 100,000-site fixture for this handoff.

ParallelGrains.compute, ParallelGrainSolver.cs and ParallelGameplaySession.cs still contain the user's uncommitted strongest-impulse/feature capture. The measured build included it; preserve it. It is not a completed energy-based damage event pipeline.

## Commands

Unity 6000.3.11f1: `bash tools/unity.sh test`, `build`, `open`. Candidate gameplay: `Builds/Debris.app/Contents/MacOS/Debris -debrisParallelGameplay`. Viability reproduction: `Builds/Debris.app/Contents/MacOS/Debris -debrisParallelProof -debrisProofViability -debrisProofOutput Logs/b3r-viability-repro.txt -logFile Logs/b3r-viability-repro-player.log`. Documentation changes need no rebuild.
