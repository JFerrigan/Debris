# Implementation status

Current handoff: **limited B.3R candidate drilling is accepted under `-debrisParallelGameplay`; legacy remains the default.** Candidate terrain owns cloned fields/damage, selects exposed cells deterministically, maintains local boundary-run caches, publishes terrain/cache/grain changes as one GPU submission, and fences topology against compact flight completion. Candidate startup has 96 deterministic grains. The original .001-cell target remains a TODO against the interim .002 gate.

## Latest validation

Focused terrain, transaction and rigid fixtures passed 8/8, 3/3 and 7/7. The fast EditMode suite passed 132 with one explicit scale test skipped (133 total, 2026-09-21 UTC). The Mac build succeeded. Final player telemetry recorded activation with 96 grains, flight/turn, two releases to 98 grains and terrain revision 3, no-target feedback and reset/reactivation with no Metal, solver or readback fault. [Evidence](evidence/B3R-candidate-drilling.md). Windows/Linux are unverified.

## Remaining limitation and next work

Next deliverable: bounded candidate cargo classification, suction and effective-door collision work. Keep the candidate path opt-in; do not advance to damage/fuel transfer, persistence/travel restoration or default cutover until their separate acceptance batches.

Candidate drilling acceptance is limited to terrain damage/release and its physical grain/cache/render publication. Suction, doors, damage, fuel transfer, persistence, travel and streaming remain unavailable. Packed-cargo performance, R1 convergence and B.GATE remain open.

## Preserved unfinished work

Matter.compute, ShipMatter.hlsl, MatterSession.cs and ContactIsland.hlsl retain the unfinished contact gatherer; [baseline patch](evidence/B3R-redesign-baseline/unfinished-contacts.patch). Presentation, ship/content, rigid-terrain exclusion, persistence, package/settings changes and DebrisV1.app remain unfinished workspace work. ParallelGameplayGrains.compute is an unused pre-existing duplicate; the solver now loads canonical ParallelGrains.compute. The built application includes existing workspace changes. Do not rerun the 100,000-site fixture for this handoff.

## Commands

Unity 6000.3.11f1: `bash tools/unity.sh test`, `build`, `open`. Candidate gameplay: `Builds/Debris.app/Contents/MacOS/Debris -debrisParallelGameplay`. Documentation changes need no rebuild. Respect the one-player-run limit for this investigation.
