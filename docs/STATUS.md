# Implementation status

Current handoff: **the [B.3R physics viability checkpoint](evidence/B3R-physics-viability.md) still fails physical acceptance.** Full-graph Jacobi position relaxation at fourfold and eightfold sweep counts improved some saved states but failed the same packed and 8,192-grain acceptance; it was rejected and the solver restored to source hash `3798fe07…`. Next: isolate the archived wall residual and test a genuinely different coupled position solve, with a defined mass/iteration contract and measured cost. Candidate damage/fuel stays paused; R1 and B.GATE remain open.

Limited candidate flight, drilling, effective door collision, GPU cavity classification/capacity admission and mounted suction exist under `-debrisParallelGameplay`; legacy remains the default. Candidate startup has 96 grains. Terrain/door topology changes are fenced, obstructed closure stays open, and classification accepts only whole oriented squares.

## Latest validation

The restored final fast EditMode suite passed 140 with one explicit scale skip (141 total, 2026-09-24 04:58 UTC). The matching Mac build succeeded; its 1280×800/Metal player exited 2: combined 4/2, 8/4 and 12/6 faulted after 0, 0 and 1 committed ticks. The eightfold prototype passed only 1, 2 and 3 ticks before faults. Both build/player cycles are in the [evidence](evidence/B3R-physics-viability.md). Physics/total GPU and frame/CPU p95 remain unmeasured because no profile reached warmup. The build includes preserved dirty candidate impact capture and other user changes; [checkpoint source hashes](evidence/B3R-physics-viability-source-hashes.txt) distinguish them. Windows/Linux and candidate suction input remain unverified.

## Remaining limitation and next work

The opt-in runner creates 8,192 active grains, 16 dynamic fragments and 16 allocated terrain chunks, but correctness rejects before throughput sampling. All canonical packed profiles cross the original .001 target and later reject at the interim .002 grain/solid limit; rigid/solid remains .001. The 720-tick extension never reaches its unforced phase, so long-run residual and fragment participation remain unverified. Scalar over-relaxation and deeper full-graph sweeps did not solve coupled convergence. The candidate renderer uses procedural grain, rigid-patch and sparse terrain draws. Physics GPU samples now require submitted-frame identity and one marker block; this check remains unexercised, and unmatched total GPU timing is explicitly unmeasured.

The [candidate damage/fuel transaction](CANDIDATE_DAMAGE_FUEL_PLAN.md) remains paused pending a packed-convergence decision. Damage, fuel transfer, persistence, travel and streaming remain unavailable on the candidate. R1 and B.GATE remain open.

## Preserved unfinished work

Matter.compute, ShipMatter.hlsl, MatterSession.cs and ContactIsland.hlsl retain the unfinished contact gatherer; [baseline patch](evidence/B3R-redesign-baseline/unfinished-contacts.patch). Presentation, ship/content, rigid-terrain exclusion, persistence, package/settings changes and DebrisV1.app remain unfinished workspace work. ParallelGameplayGrains.compute is an unused pre-existing duplicate; the solver now loads canonical ParallelGrains.compute. The built application includes existing workspace changes. Do not rerun the 100,000-site fixture for this handoff.

ParallelGrains.compute, ParallelGrainSolver.cs and ParallelGameplaySession.cs still contain the user's uncommitted strongest-impulse/feature capture. The measured build included it; preserve it. It is not a completed energy-based damage event pipeline.

## Commands

Unity 6000.3.11f1: `bash tools/unity.sh test`, `build`, `open`. Candidate gameplay: `Builds/Debris.app/Contents/MacOS/Debris -debrisParallelGameplay`. Viability reproduction: `Builds/Debris.app/Contents/MacOS/Debris -debrisParallelProof -debrisProofViability -debrisProofOutput Logs/b3r-viability-repro.txt -logFile Logs/b3r-viability-repro-player.log`. Documentation changes need no rebuild.
