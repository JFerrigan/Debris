# Implementation status

Current priority: **B.3R mass-based contact physics**, before further B.5 scale work. Phase A passed; B.1/B.2 and bounded B.4 have verified checkpoints. B.3 is reopened; isolated pixel response now passes, while coupled contacts and persistence completion remain. B.GATE and phases C–E remain incomplete. Current commit: `git log -1`.

## Next implementation batch

Continue B.3R.3: coupled piles, free cargo, fragment torque, anchored contacts and wake-up. Then B.3R.4 physics persistence/migration, bounded contacts and the full measured player gate. B.3R.1–B.3R.2 passed isolated ship–cell acceptance. Historical efficiency records: `evidence/feature-batches.csv`.

## Latest verified checkpoint

43 tests passed; explicit scale fixture skipped. Mac build/player passed: mass-1 pixel reduces starter speed 10 → 9.994825; thrust continues; zero pose fallbacks; non-overlap and moving save/load pass. Frame p95 17.595 ms, GPU p95 2.394 ms. Evidence: `evidence/B3R-single-cell-tests.xml`, `evidence/B3R-single-cell-player.txt`. This does not validate dense piles, rotating free cargo, fragment/anchor impulses or high-speed/budget handling. The legacy displacement API remains for historical fixtures; normal gameplay submits forces.

## Unfinished work to preserve

`Assets/Debris/Persistence/Runtime/WorldStore.cs` has an uncommitted streaming transaction change. `Assets/Debris/Persistence/Tests/Editor/WorldScaleTests.cs` and its meta are uncommitted. The explicit 100,000-site test **failed the 180-second runner timeout**, despite logging successful internal checks. Provisional timings: write 113,238 ms, open 23 ms, 27 representative reads/regeneration 729 ms, full collection 33,257 ms. Fix timeout/setup/cleanup accounting and schedule maintenance incrementally/off the ordinary save path when B.5 resumes. Do not rerun or claim this gate during the physics correction. No Unity test/player process remained running when this handoff was checked.

Preserve unrelated package/Unity Assistant edits, GraphicsSettings/QualitySettings changes, untracked ProjectSettings files and `DebrisV1.app`. They are outside implementation commits.

## Commands and scope

Unity 6000.3.11f1: `bash tools/unity.sh test`, `build`, `open`. Focused tests use `-testFilter` with separate output paths. F5/F9 save/load World, T changes salvage sites, R restores saved state. Legacy salvage.debris imports via F9 when no world exists. Benchmarks use temporary verification slots. Windows/Linux execution is unverified.

Read this handoff plus the current execution-plan block; open further documents only as needed. Keep this file current and concise rather than appending session history.
