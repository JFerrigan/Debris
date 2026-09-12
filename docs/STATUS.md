# Implementation status

Current priority: **B.3R parallel contact redesign**. R0 is complete. The R1 necessary packed experiment failed all three approved profiles, triggering the plan's stop condition. B.GATE, B.5 and later phases remain blocked. `git log -1` is the current commit.

## Next task and exact failure

Integration is stopped pending a convergence/design decision. After the delegated proof improvements, all profiles still reject the resting packed cavity at tick 3: wall penetration .001186/.001123/.001034 for 4/2, 8/4, 12/6 (limit .001). Shared rigid motion also fails. Do not add passes, relax tolerance, switch gameplay or resume serial optimization.

## Latest evidence

Source `5898f8d`: three authorized GPT-5.6-luna workers contributed geometry, rigid manifolds and GPU diagnostics, integrated and corrected by the parent. **30 focused tests and one Mac development build passed.** The matching player passed the off-centre rigid impact, then failed all six packed cases (exit 2). [Canonical evidence](evidence/B3R-parallel-pieces.md), [raw player](evidence/B3R-parallel-pieces-player.txt), [test result](evidence/B3R-parallel-pieces-tests.xml). GPU p95 remains unmeasured. Full R1 workload/performance coverage and R2–R4 remain incomplete; normal gameplay uses the legacy path.

## Preserved unfinished work

The old uncommitted contact gatherer remains in Matter.compute, ShipMatter.hlsl, MatterSession.cs and ContactIsland.hlsl; its [baseline patch](evidence/B3R-redesign-baseline/unfinished-contacts.patch) records it against `f3aa5c9`. WorldStore.cs and WorldScaleTests.cs/meta are unrelated unfinished persistence work. The 100,000-site test previously failed its 180-second runner timeout; do not rerun it during this correction. Preserve package/settings changes and DebrisV1.app outside implementation commits.

## Commands

Unity 6000.3.11f1: `bash tools/unity.sh test`, `build`, `open`. Opt-in player: `Builds/Debris.app/Contents/MacOS/Debris -debrisParallelProof -debrisProofOutput /private/tmp/B3R-parallel-proof.txt`. The proof uses a temporary output file, not saves. Final tests/build/player have finished; completion-note changes need no rebuild. Windows/Linux remain unverified.
