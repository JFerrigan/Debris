# Implementation status

Current handoff: **B.3R opt-in GPU flight/collision implementation is automated-verified; player acceptance remains unverified.** Candidate masks now use their local [-64,64) coordinates, terrain patches use their terrain-local origin, and 17 dynamics plus anchored terrain use 18 endpoints. Compact completion records drive ship motion and direct GPU-buffer rendering; full snapshots are inspection-only. Legacy remains the default; the original .001-cell target remains a TODO against the interim .002 gate.

## Latest validation

Focused startup validation passed 3/3, including completion-ring saturation, rotated COM conversion and translated terrain origin. The final fast EditMode suite passed 114 with one explicit scale test skipped (115 total, 2026-09-18 05:25 UTC), and the Mac build succeeded. A 25-second Mac player run with `-debrisParallelGameplay` activated the candidate and produced no Metal binding or solver faults after the direct-buffer binding correction. Windows/Linux are unverified.

## Remaining limitation and next work

Candidate mining, suction, doors, damage, fuel transfer, persistence, travel and streaming remain gated and report unavailable. Player collision/render acceptance beyond startup, packed-cargo/performance, R1 convergence and B.GATE remain open. The test suite does not replace the required terrain-contact, high-speed, completion-ordering and render acceptance cases specified for the next verification pass.

## Preserved unfinished work

Matter.compute, ShipMatter.hlsl, MatterSession.cs and ContactIsland.hlsl retain the unfinished contact gatherer; [baseline patch](evidence/B3R-redesign-baseline/unfinished-contacts.patch). Presentation, ship/content, rigid-terrain exclusion, persistence, package/settings changes and DebrisV1.app remain unfinished workspace work. ParallelGameplayGrains.compute is an unused pre-existing duplicate; the solver now loads canonical ParallelGrains.compute. The built application includes existing workspace changes. Do not rerun the 100,000-site fixture for this handoff.

## Commands

Unity 6000.3.11f1: `bash tools/unity.sh test`, `build`, `open`. Candidate gameplay: `Builds/Debris.app/Contents/MacOS/Debris -debrisParallelGameplay`. Documentation changes need no rebuild. Respect the one-player-run limit for this investigation.
