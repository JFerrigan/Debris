# Implementation status

Current priority: **B.3R parallel contact redesign**. R0 is complete. The R1 necessary packed experiment failed all three approved profiles, triggering the plan's stop condition. B.GATE, B.5 and later phases remain blocked. `git log -1` is the current commit.

## Next task and exact failure

Integration is stopped; retain the opt-in proof and [locked contract](CONTACT_PHYSICS.md) for the next convergence/design decision. In a fully packed 50×50 cavity under thrust/torque, 4/2, 8/4 and 12/6 all reject tick 3 at wall penetrations .001183, .001115 and .001022 cell respectively (limit .001). Shared initial rigid motion also fails all profiles. Do not silently add passes, relax tolerance, switch gameplay or resume serial optimization.

## Latest evidence

Source `2a61ff2`: eight focused GPU/layout tests passed; one Mac development build succeeded; one player sweep completed with exit 2 for failed correctness. [Canonical experiment and limitations](evidence/B3R-parallel-experiment.md), [raw player results](evidence/B3R-parallel-player.txt), [runner result](evidence/B3R-parallel-tests.xml). GPU p95 is unmeasured; no performance gate passed. R1 full manifolds/workloads/diagnostics and R2–R4 gameplay, persistence and cleanup remain unimplemented. Normal gameplay stays on the legacy path.

## Preserved unfinished work

The old uncommitted contact gatherer remains in Matter.compute, ShipMatter.hlsl, MatterSession.cs and ContactIsland.hlsl; its [baseline patch](evidence/B3R-redesign-baseline/unfinished-contacts.patch) records it against `f3aa5c9`. WorldStore.cs and WorldScaleTests.cs/meta are unrelated unfinished persistence work. The 100,000-site test previously failed its 180-second runner timeout; do not rerun it during this correction. Preserve package/settings changes and DebrisV1.app outside implementation commits.

## Commands

Unity 6000.3.11f1: `bash tools/unity.sh test`, `build`, `open`. Opt-in player: `Builds/Debris.app/Contents/MacOS/Debris -debrisParallelProof -debrisProofOutput /private/tmp/B3R-parallel-proof.txt`. The proof uses a temporary output file, not saves. Final tests/build/player have finished; completion-note changes need no rebuild. Windows/Linux remain unverified.
