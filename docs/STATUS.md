# Implementation status

Current priority: **B.3R mass-based contact physics**, before further B.5 scale work. Phase A passed; B.1/B.2 and bounded B.4 have verified checkpoints. B.3 is reopened: geometric collision admission still makes a single loose pixel hard-stop the ship. B.GATE and phases C–E remain incomplete. Current commit: `git log -1`.

## Next implementation batch

Read [CONTACT_PHYSICS](CONTACT_PHYSICS.md). Implement B.3R.1–B.3R.2: body mass/inertia and force commands, analytical reference tests, GPU ship–cell impulses, and removal of CPU collision-flag velocity resets. Then complete piles/cargo/fragments/anchors and physics save/resume under B.3R.3–B.3R.4. Use [AGENTS.md](../AGENTS.md) for a focused implementation/validation batch. The current run documents batch measurements, then immediately implements the physics correction. Historical efficiency measurements live in `evidence/feature-batches.csv`.

## Latest verified baseline

Code checkpoint `c7da55e`: 38/38 tests, Mac build, damaged-ship fuel/save/leave/revisit loop, and reclamation of unreachable save files while preserving both recovery roots. Standalone: frame p95 18.613 ms; GPU p95 10.721 ms; two-site directory 38,382 bytes. Evidence: `evidence/B-world-collection-tests.xml`, `evidence/B-world-collection-player.txt`. Historical B.3/B.4 results remain in PERFORMANCE and evidence; they do not establish mass-based collision response.

## Unfinished work to preserve

`Assets/Debris/Persistence/Runtime/WorldStore.cs` has an uncommitted streaming transaction change. `Assets/Debris/Persistence/Tests/Editor/WorldScaleTests.cs` and its meta are uncommitted. The explicit 100,000-site test **failed the 180-second runner timeout**, despite logging successful internal checks. Provisional timings: write 113,238 ms, open 23 ms, 27 representative reads/regeneration 729 ms, full collection 33,257 ms. Fix timeout/setup/cleanup accounting and schedule maintenance incrementally/off the ordinary save path when B.5 resumes. Do not rerun or claim this gate during the physics correction. No Unity test/player process remained running when this handoff was checked.

Preserve unrelated package/Unity Assistant edits, GraphicsSettings/QualitySettings changes, untracked ProjectSettings files and `DebrisV1.app`. They are outside implementation commits.

## Commands and scope

Unity 6000.3.11f1: `bash tools/unity.sh test`, `build`, `open`. Focused tests use `-testFilter` with separate output paths. F5/F9 save/load World, T changes salvage sites, R restores saved state. Legacy salvage.debris imports via F9 when no world exists. Benchmarks use temporary verification slots. Windows/Linux execution is unverified.

Read this handoff plus the current execution-plan block; open further documents only as needed. Keep this file current and concise rather than appending session history.
