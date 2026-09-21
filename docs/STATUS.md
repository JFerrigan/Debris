# Implementation status

Current handoff: **limited B.3R candidate drilling, effective door collision, GPU cavity cargo classification/capacity admission, and mounted suction are accepted under `-debrisParallelGameplay`; legacy remains the default.** Candidate terrain owns cloned fields/damage, selects exposed cells deterministically, maintains local boundary-run caches, publishes terrain/cache/grain changes as one GPU submission, and fences topology against compact flight completion. The cargo door changes its ship collision boundaries only after that fence; opening has no snapshot, while closing takes one command-time grain snapshot and stays open if an oriented grain obstructs a door cell. Cavity classification sets grain cargo flags only for whole oriented squares and returns compact per-material facts plus rejected admissions. Suction evaluates the current GPU ship pose and attracts only exterior grains in the suction mouth strip, never through intact hull cells. Candidate startup has 96 deterministic grains. The original .001-cell target remains a TODO against the interim .002 gate.

## Latest validation

The startup fixture covers door fence/open/clear close/obstructed closure, GPU cargo classification/capacity rejection, and exterior-vs-intact-hull suction. The fast EditMode suite passed 137 with one explicit scale test skipped (138 total, 2026-09-21 UTC); the Mac build succeeded. An opt-in player process launched, but did not emit Unity telemetry to the redirected stream, so player suction input remains unverified. Windows/Linux are unverified.

## Remaining limitation and next work

Next deliverable: the fenced [candidate damage/fuel reconfiguration transaction](CANDIDATE_DAMAGE_FUEL_PLAN.md). Keep the candidate path opt-in; do not advance to persistence/travel restoration or default cutover until their separate acceptance batches.

Candidate acceptance is limited to terrain damage/release and its physical grain/cache/render publication, effective door collision, cavity classification, and mounted suction. Damage, fuel transfer, persistence, travel and streaming remain unavailable. Packed-cargo performance, R1 convergence and B.GATE remain open.

## Preserved unfinished work

Matter.compute, ShipMatter.hlsl, MatterSession.cs and ContactIsland.hlsl retain the unfinished contact gatherer; [baseline patch](evidence/B3R-redesign-baseline/unfinished-contacts.patch). Presentation, ship/content, rigid-terrain exclusion, persistence, package/settings changes and DebrisV1.app remain unfinished workspace work. ParallelGameplayGrains.compute is an unused pre-existing duplicate; the solver now loads canonical ParallelGrains.compute. The built application includes existing workspace changes. Do not rerun the 100,000-site fixture for this handoff.

## Commands

Unity 6000.3.11f1: `bash tools/unity.sh test`, `build`, `open`. Candidate gameplay: `Builds/Debris.app/Contents/MacOS/Debris -debrisParallelGameplay`. Documentation changes need no rebuild. Respect the one-player-run limit for this investigation.
