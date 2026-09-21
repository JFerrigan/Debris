# Implementation status

Current handoff: **limited B.3R candidate drilling, effective door collision, and GPU cavity cargo classification are accepted under `-debrisParallelGameplay`; legacy remains the default.** Candidate terrain owns cloned fields/damage, selects exposed cells deterministically, maintains local boundary-run caches, publishes terrain/cache/grain changes as one GPU submission, and fences topology against compact flight completion. The cargo door changes its ship collision boundaries only after that fence; opening has no snapshot, while closing takes one command-time grain snapshot and stays open if an oriented grain obstructs a door cell. Cavity classification sets grain cargo flags only for whole oriented squares and returns compact per-material facts for count/mass. Candidate startup has 96 deterministic grains. The original .001-cell target remains a TODO against the interim .002 gate.

## Latest validation

The startup fixture covers door fence/open/clear close/obstructed closure and GPU cargo classification for an interior and boundary-straddling grain without normal full snapshots. The fast EditMode suite passed 135 with one explicit scale test skipped (136 total, 2026-09-21 UTC). Drilling retains its accepted focused terrain/transaction/rigid evidence, Mac build and player telemetry. Windows/Linux and candidate door/cargo player acceptance are unverified.

## Remaining limitation and next work

Next deliverable: bounded candidate suction work. Keep the candidate path opt-in; do not advance to damage/fuel transfer, persistence/travel restoration or default cutover until their separate acceptance batches.

Candidate acceptance is limited to terrain damage/release and its physical grain/cache/render publication, effective door collision, and cavity classification. Suction, damage, fuel transfer, persistence, travel and streaming remain unavailable. Packed-cargo performance, R1 convergence and B.GATE remain open.

## Preserved unfinished work

Matter.compute, ShipMatter.hlsl, MatterSession.cs and ContactIsland.hlsl retain the unfinished contact gatherer; [baseline patch](evidence/B3R-redesign-baseline/unfinished-contacts.patch). Presentation, ship/content, rigid-terrain exclusion, persistence, package/settings changes and DebrisV1.app remain unfinished workspace work. ParallelGameplayGrains.compute is an unused pre-existing duplicate; the solver now loads canonical ParallelGrains.compute. The built application includes existing workspace changes. Do not rerun the 100,000-site fixture for this handoff.

## Commands

Unity 6000.3.11f1: `bash tools/unity.sh test`, `build`, `open`. Candidate gameplay: `Builds/Debris.app/Contents/MacOS/Debris -debrisParallelGameplay`. Documentation changes need no rebuild. Respect the one-player-run limit for this investigation.
