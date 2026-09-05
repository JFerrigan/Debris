# Implementation status

Current phase: Phase B / M4. Phase A gate passed. Planning baseline d46f7a9 is pushed. Current commit: `git log -1`.

## Documented

Phases A–E and locked interpretations are recorded in [EXECUTION_PLAN](EXECUTION_PLAN.md). Existing uncommitted design work is preserved in the planning baseline. Basic hub walking is Phase C.

## Implemented, unverified

Phases B–E are not implemented yet. Large-world dirty-chunk streaming, ship physics, and player career progression remain unchecked.

## Verified

Planning baseline d46f7a9 and GPU implementation 1134125 pushed to origin/main. Unity setup and Mac build succeed. 15/15 EditMode tests pass: deterministic content, GPU/reference cutting, saturation, non-overlap, async inspection, and page eviction/resume with exact damage/cells.

Phase A Mac player gate passed on Apple M4 Pro: 8,192-capacity preset frame p95 17.577 ms, GPU p95 1.876 ms; all three presets conserve 22,400 initial material cells. Captured image inspected. Initial active budget: 8,192 loose cells/four 128² chunks. See PERFORMANCE and evidence/A-* for exact results and limitations.

## Blocked

Unity MCP remains unavailable; direct editor works. Windows/Linux execution and Steam integration not attempted. No blocker to Phase B.

## Exact next task

B.1 / M4: read SHIP_SYSTEM, COMPONENT_SYSTEM, STRUCTURAL_SIMULATION, PERSISTENCE and SAVE_FORMAT. Implement blueprint structural cells and whole machinery, starter ship, then inertial flight/fuel and moving-hull cargo integration. Verify B.3 rotating cavity before advancing to career features.

Commands: `bash tools/unity.sh setup`, `test`, `build`, `open`. Player: `Builds/Debris.app/Contents/MacOS/Debris -debrisBenchmark -logFile Logs/player.log`; benchmark artifacts currently write under Builds/Logs. Close only the Debris editor before batch execution.

## Resumption

Read PROJECT_PLAN.md → EXECUTION_PLAN.md → this file → TECHNICAL_ROADMAP.md and relevant subsystem docs. Preserve working changes. Run relevant checks and record evidence in the same commit as completed checklist entries; push origin/main at coherent checkpoints. Continue phases automatically after passing gates.
