# Implementation status

Current handoff: **R2a startup Page fault corrected; candidate integration remains in progress**. The single permitted player launch reproduced the old failure. Grain-grid checks now exclude rigid terrain, retain safe grain rejection at the page edge, and empty populations read back as empty. See [startup diagnosis](evidence/B3R-page-startup.md). Legacy remains the default; the original .001-cell target remains a TODO against the interim .002 gate.

## Latest validation

Startup/boundary fixtures passed 11/11. The final fast EditMode suite passed 110 with one explicit scale test skipped. The Mac rebuild succeeded (runner exit 0). The user allowed one game launch, used to reproduce the original Page fault; no post-fix player launch is authorized in this test. Windows/Linux are unverified.

## Remaining limitation and next work

The generated startup world now imports and acknowledges three thrust ticks in an automated GPU test without a Page fault or fake loose cell. Candidate tools, doors, cargo capacity/classification, fragment acceptance, saved-world import and player presentation still require acceptance. The existing unfinished rigid-terrain exclusion and pose conversion need review before collision acceptance; this startup fix does not certify them. Full R1 and B.GATE remain open.

## Preserved unfinished work

Matter.compute, ShipMatter.hlsl, MatterSession.cs and ContactIsland.hlsl retain the unfinished contact gatherer; [baseline patch](evidence/B3R-redesign-baseline/unfinished-contacts.patch). Presentation, ship/content, rigid-terrain exclusion, persistence, package/settings changes and DebrisV1.app remain unfinished workspace work. ParallelGameplayGrains.compute is an unused pre-existing duplicate; the solver now loads canonical ParallelGrains.compute. The built application includes existing workspace changes. Do not rerun the 100,000-site fixture for this handoff.

## Commands

Unity 6000.3.11f1: `bash tools/unity.sh test`, `build`, `open`. Candidate gameplay: `Builds/Debris.app/Contents/MacOS/Debris -debrisParallelGameplay`. Documentation changes need no rebuild. Respect the one-player-run limit for this investigation.
