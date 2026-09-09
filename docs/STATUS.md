# Implementation status

Current priority: **B.3R mass-based contact physics**, before further B.5 scale work. Phase A passed; B.1/B.2 and bounded B.4 have verified checkpoints. B.3 is reopened; isolated pixel response now passes, while coupled contacts and persistence completion remain. B.GATE and phases C–E remain incomplete. Current commit: `git log -1`.

## Next implementation batch

R0 is committed; the opt-in parallel square-grain proof is implemented. Eight focused GPU/layout tests pass, including 10000:1, two grains, off-centre spin/conservation, independent spin, 120-cell/s anchor impact, speed rejection and packed rollback. Run one Mac build and `-debrisParallelProof -debrisProofOutput <path>` to sweep packed shared-motion and thrust/torque cases for 4/2, 8/4, 12/6. The 4/2 development case rejects tick 3 at .001183 solid penetration. If all profiles fail, stop integration as specified in [CONTACT_PHYSICS](CONTACT_PHYSICS.md). This is an early necessary convergence experiment, not a completed R1 benchmark. Rigid-only manifolds, full workload/performance suite and R2–R4 remain unimplemented. Normal gameplay stays on the preserved legacy path.

## Latest verified checkpoint

49 tests passed; explicit scale fixture skipped. Mac build/player physics passed: combined workload preserves 201 cells/non-overlap, momentum error 0.323917, energy decreases, fragment spins; frame p95 324.382 ms / GPU p95 324.505 ms **fails performance**. Evidence: `evidence/B3R-island-tests.xml`, `evidence/B3R-island-player.txt`. Isolated pixel response remains covered. Fragment mass/mobility and pending solver phase are not yet serialized; legacy displacement fixtures remain. No claim of full B.3R completion.

## Unfinished work to preserve

`Assets/Debris/Persistence/Runtime/WorldStore.cs` has an uncommitted streaming transaction change. `Assets/Debris/Persistence/Tests/Editor/WorldScaleTests.cs` and its meta are uncommitted. The explicit 100,000-site test **failed the 180-second runner timeout**, despite logging successful internal checks. Provisional timings: write 113,238 ms, open 23 ms, 27 representative reads/regeneration 729 ms, full collection 33,257 ms. Fix timeout/setup/cleanup accounting and schedule maintenance incrementally/off the ordinary save path when B.5 resumes. Do not rerun or claim this gate during the physics correction. No Unity test/player process remained running when this handoff was checked.

Preserve unrelated package/Unity Assistant edits, GraphicsSettings/QualitySettings changes, untracked ProjectSettings files and `DebrisV1.app`. They are outside implementation commits.

## Commands and scope

Unity 6000.3.11f1: `bash tools/unity.sh test`, `build`, `open`. Focused tests use `-testFilter` with separate output paths. F5/F9 save/load World, T changes salvage sites, R restores saved state. Legacy salvage.debris imports via F9 when no world exists. Benchmarks use temporary verification slots. Windows/Linux execution is unverified.

Read this handoff plus the current execution-plan block; open further documents only as needed. Keep this file current and concise rather than appending session history.
