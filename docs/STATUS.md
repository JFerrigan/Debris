# Implementation status

Current handoff: **the [B.3R physics viability checkpoint](evidence/B3R-physics-viability.md) failed physical acceptance.** The next task is a reviewed packed-position convergence change against identical saved failing states, then the same 8,192-grain fixture and valid timing protocol. Candidate damage/fuel stays paused; R1 and B.GATE remain open.

Limited candidate flight, drilling, effective door collision, GPU cavity classification/capacity admission and mounted suction exist under `-debrisParallelGameplay`; legacy remains the default. Candidate startup has 96 grains. Terrain/door topology changes are fenced, obstructed closure stays open, and classification accepts only whole oriented squares.

## Latest validation

The final fast EditMode suite passed 140 with one explicit scale skip (141 total, 2026-09-24 UTC); focused fixture/forced-rollback/high-speed tests passed 3/3 and identical-state trace replay passed. The corrected Mac build succeeded. Its opt-in benchmark player ran at 1280×800/Metal and exited 2: combined 4/2, 8/4 and 12/6 faulted after 0, 0 and 1 committed ticks. Physics/total GPU and frame/CPU p95 are unmeasured because no profile reached warmup. The measured build includes preserved dirty candidate impact capture and other user changes; [source hashes](evidence/B3R-physics-viability-source-hashes.txt) identify them. Windows/Linux and candidate suction input remain unverified.

## Remaining limitation and next work

The new opt-in runner creates 8,192 active grains, 16 dynamic fragments and 16 allocated terrain chunks, but correctness rejects before throughput sampling. All canonical packed profiles cross the original .001 target and later reject at the interim .002 grain/solid limit; rigid/solid remains .001. The 720-tick extension never reaches its unforced phase, so long-run residual and fragment participation remain unverified. Trace/reference parity supports a coupled-convergence limitation rather than an isolated arithmetic defect. The candidate renderer is approximated by procedural grain, rigid-patch and sparse terrain draws; delayed GPU-frame matching remains unvalidated until a complete window can run.

The [candidate damage/fuel transaction](CANDIDATE_DAMAGE_FUEL_PLAN.md) remains paused pending a packed-convergence decision. Damage, fuel transfer, persistence, travel and streaming remain unavailable on the candidate. R1 and B.GATE remain open.

## Preserved unfinished work

Matter.compute, ShipMatter.hlsl, MatterSession.cs and ContactIsland.hlsl retain the unfinished contact gatherer; [baseline patch](evidence/B3R-redesign-baseline/unfinished-contacts.patch). Presentation, ship/content, rigid-terrain exclusion, persistence, package/settings changes and DebrisV1.app remain unfinished workspace work. ParallelGameplayGrains.compute is an unused pre-existing duplicate; the solver now loads canonical ParallelGrains.compute. The built application includes existing workspace changes. Do not rerun the 100,000-site fixture for this handoff.

ParallelGrains.compute, ParallelGrainSolver.cs and ParallelGameplaySession.cs still contain the user's uncommitted strongest-impulse/feature capture. The measured build included it; preserve it. It is not a completed energy-based damage event pipeline.

## Commands

Unity 6000.3.11f1: `bash tools/unity.sh test`, `build`, `open`. Candidate gameplay: `Builds/Debris.app/Contents/MacOS/Debris -debrisParallelGameplay`. Viability reproduction: `Builds/Debris.app/Contents/MacOS/Debris -debrisParallelProof -debrisProofViability -debrisProofOutput Logs/b3r-viability-repro.txt -logFile Logs/b3r-viability-repro-player.log`. Documentation changes need no rebuild.
