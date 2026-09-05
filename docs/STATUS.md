# Implementation status

Current phase: Phase A / M2–M3. Planning baseline d46f7a9 is pushed. Current commit: `git log -1`.

## Documented

Phases A–E and locked interpretations are recorded in [EXECUTION_PLAN](EXECUTION_PLAN.md). Existing uncommitted design work is preserved in the planning baseline. Basic hub walking is Phase C.

## Implemented, unverified

URP showcase, GPU chunk rendering/cutting/loose motion, diagnostics, benchmark runner, and Mac build entry point. A.GATE awaits actual player run, visual inspection, measured scaling and active-budget selection.

## Verified

Unity 6000.3.11f1 (3000ef702840), URP 17.3.0, Input System 1.19.0 imported successfully on arm64 macOS 15.1. `bash tools/unity.sh setup` passed. `bash tools/unity.sh test`: 15/15 EditMode tests pass, including GPU cut/reference accounting, saturation, moving non-overlap, async inspection, and page eviction/resume preserving cells and damage. Evidence: [A-tests](evidence/A-tests.txt).

The GPU fixture caught and fixed a loop counter update problem: cutter allocation/throttle totals are now accumulated locally and written once per command. No gate was advanced with failing conservation tests.

## Blocked

Unity MCP unavailable; direct editor commands work. No Mac performance certification yet. Windows/Linux execution and Steam integration are not yet attempted.

## Exact next task

Complete the running Mac build (`bash tools/unity.sh build`), run `Builds/Debris.app/Contents/MacOS/Debris -debrisBenchmark -logFile Logs/player.log`, inspect screenshot and benchmark report, repair any rendering/performance issue. Then verify A.2/A.4/A.5 and A.GATE before Phase B. Commands use bash with no user shell startup.

## Resumption

Read PROJECT_PLAN.md → EXECUTION_PLAN.md → this file → TECHNICAL_ROADMAP.md and relevant subsystem docs. Preserve working changes. Run relevant checks and record evidence in the same commit as completed checklist entries; push origin/main at coherent checkpoints. Continue phases automatically after passing gates.
