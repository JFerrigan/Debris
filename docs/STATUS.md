# Implementation status

Current handoff: **working Mac build delivered; stop here under the latest user scope**. `Builds/Debris.app` contains the current workspace. Diagnostic tracing and independent reference checks are complete. No solver rules were changed. See [checkpoint evidence](evidence/B3R-packed-diagnosis.md). `git log -1` identifies the completion commit.

## Latest validation

52/52 focused tests passed. Final Mac build succeeded without shader errors after shader reimport. The Mac ship scenario passed cargo non-overlap, conservation, fuel/save/load and two-site revisit checks (exit 0); the captured hull/cargo/asteroid/HUD render was inspected. The proof player passed rigid impact, then recorded the known six packed-case rejections (exit 2). GPU p95 in the ship scenario was 74.270 ms, not a performance pass. Windows/Linux are unverified.

## Remaining limitation and next work

The user accepts the measured packed wall penetration for this checkpoint. The unchanged proof limit is .001 cells; resting profiles still reach .001186/.001123/.001034 at tick 3. Identical pre-failure state trace/no-trace comparisons and independent reference checks pass. Long shared-motion runs can vary between repeats. R1, B.GATE and later phases remain open; normal gameplay uses the legacy path. Further solver design or feature expansion requires a new task after this requested stopping point.

## Preserved unfinished work

Matter.compute, ShipMatter.hlsl, MatterSession.cs and ContactIsland.hlsl contain the old unfinished contact gatherer; [baseline patch](evidence/B3R-redesign-baseline/unfinished-contacts.patch). WorldStore.cs and WorldScaleTests.cs/meta are unrelated persistence work. Package/settings changes and DebrisV1.app remain outside this commit. The built application includes these existing workspace changes. Do not rerun the 100,000-site fixture for this handoff.

## Commands

Unity 6000.3.11f1: `bash tools/unity.sh test`, `build`, `open`. Play `Builds/Debris.app`. Proof: `Builds/Debris.app/Contents/MacOS/Debris -debrisParallelProof -debrisProofOutput /private/tmp/B3R-parallel-proof.txt`. Ship verification: `Builds/Debris.app/Contents/MacOS/Debris -debrisShipBenchmark` (temporary verification saves). Final verification is complete; documentation changes need no rebuild.
