# Implementation status

Current handoff: **B.3R candidate drilling has a partial opt-in implementation and focused CPU coverage; interactive acceptance remains unverified.** Candidate terrain owns cloned fields/damage, selects exposed cells deterministically, stages damage/release edits, reserves 4,096 collision-cache entries, and fences candidate submissions while a compact GPU grain-placement query and terrain publication run. Candidate startup still has 96 deterministic grains. Legacy remains the default; the original .001-cell target remains a TODO against the interim .002 gate.

## Latest validation

The current fast EditMode suite passed 121 with one explicit scale test skipped (122 total, 2026-09-19 06:00 UTC), including four candidate-terrain ownership/targeting/stale-edit tests. The current Mac build succeeded. The single bounded player launch supplied no candidate activation, drill, Metal or readback telemetry before termination, so it establishes no interactive acceptance. Windows/Linux are unverified.

## Remaining limitation and next work

Current deliverable: partial implementation of the [candidate drilling plan](CANDIDATE_DRILLING_PLAN.md). Continue with exact rotated grain/hull SAT, complete ordered GPU cache/grain publication, rejection/integration fixtures, and player telemetry before representing drilling as accepted.

Candidate drilling is not accepted: the placement query now uses oriented-square SAT for active grains and ship hull cells, but fragment SAT and transaction-level GPU fixtures remain absent. Suction, doors, damage, fuel transfer, persistence, travel and streaming remain unavailable. Measured player flight/collision trajectories, packed-cargo/performance, R1 convergence and B.GATE remain open.

## Preserved unfinished work

Matter.compute, ShipMatter.hlsl, MatterSession.cs and ContactIsland.hlsl retain the unfinished contact gatherer; [baseline patch](evidence/B3R-redesign-baseline/unfinished-contacts.patch). Presentation, ship/content, rigid-terrain exclusion, persistence, package/settings changes and DebrisV1.app remain unfinished workspace work. ParallelGameplayGrains.compute is an unused pre-existing duplicate; the solver now loads canonical ParallelGrains.compute. The built application includes existing workspace changes. Do not rerun the 100,000-site fixture for this handoff.

## Commands

Unity 6000.3.11f1: `bash tools/unity.sh test`, `build`, `open`. Candidate gameplay: `Builds/Debris.app/Contents/MacOS/Debris -debrisParallelGameplay`. Documentation changes need no rebuild. Respect the one-player-run limit for this investigation.
