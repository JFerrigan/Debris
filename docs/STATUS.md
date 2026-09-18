# Implementation status

Current handoff: **parallel solver may proceed under an interim .002-cell solid-penetration gate**. The original .001-cell target is an explicit B.3R TODO and is not considered fixed. `Builds/Debris.app` contains the current workspace. See [checkpoint evidence](evidence/B3R-packed-diagnosis.md). `git log -1` identifies the completion commit.

## Latest validation

52/52 focused tests passed. The interim-gate Mac rebuild succeeded without shader errors. The prior Mac ship scenario passed cargo non-overlap, conservation, fuel/save/load and two-site revisit checks (exit 0); the captured hull/cargo/asteroid/HUD render was inspected. The post-change proof launch aborted before producing solver output (exit 134), so the relaxed proof has not been claimed as a fresh player pass. GPU p95 in the ship scenario was 74.270 ms, not a performance pass. Windows/Linux are unverified.

## Remaining limitation and next work

The user accepts the measured packed wall penetration for interim integration. The proof gate is now .002 cells; resting profiles still reach .001186/.001123/.001034 at tick 3. TODO(B.3R): revisit the original .001-cell target only with intentional consideration and new acceptance evidence. Identical pre-failure state trace/no-trace comparisons and independent reference checks pass. Long shared-motion runs can vary between repeats. Full R1, B.GATE and later integration checks remain open; normal gameplay still uses the legacy path until the candidate path is explicitly cut over.

## Preserved unfinished work

Matter.compute, ShipMatter.hlsl, MatterSession.cs and ContactIsland.hlsl contain the old unfinished contact gatherer; [baseline patch](evidence/B3R-redesign-baseline/unfinished-contacts.patch). WorldStore.cs and WorldScaleTests.cs/meta are unrelated persistence work. Package/settings changes and DebrisV1.app remain outside this commit. The built application includes these existing workspace changes. Do not rerun the 100,000-site fixture for this handoff.

## Commands

Unity 6000.3.11f1: `bash tools/unity.sh test`, `build`, `open`. Play `Builds/Debris.app`. Proof: `Builds/Debris.app/Contents/MacOS/Debris -debrisParallelProof -debrisProofOutput /private/tmp/B3R-parallel-proof.txt`. Ship verification: `Builds/Debris.app/Contents/MacOS/Debris -debrisShipBenchmark` (temporary verification saves). Final verification is complete; documentation changes need no rebuild.
