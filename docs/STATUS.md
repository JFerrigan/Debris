# Implementation status

Current phase: Phase B / M4. Phase A gate passed. Planning baseline d46f7a9 is pushed. Current commit: `git log -1`.

## Documented

Phases A–E and locked interpretations are recorded in [EXECUTION_PLAN](EXECUTION_PLAN.md). Existing uncommitted design work is preserved in the planning baseline. Basic hub walking is Phase C.

## Implemented, unverified

Phase B blueprint/runtime foundation is implemented; moving hull/cargo integration is in progress. Phases C–E are not implemented yet. Large-world dirty-chunk streaming, ship physics, and player career progression remain unchecked.

## Verified

B.1/B.2 data checkpoint: 8/8 ship EditMode tests passed on 2026-09-06 (`docs/evidence/B-ship-tests.xml`), including atomic prefab rejection, 2,500-cell cavity, mass-dependent flight, fuel exhaustion, support detachment, command-loss inertia and tank failure. This does not complete the physical Phase B gate.

Planning baseline d46f7a9 and GPU implementation 1134125 pushed to origin/main. Unity setup and Mac build succeed. 15/15 EditMode tests pass: deterministic content, GPU/reference cutting, saturation, non-overlap, async inspection, and page eviction/resume with exact damage/cells.

Phase A Mac player gate passed on Apple M4 Pro: 8,192-capacity preset frame p95 17.577 ms, GPU p95 1.876 ms; all three presets conserve 22,400 initial material cells. Captured image inspected. Initial active budget: 8,192 loose cells/four 128² chunks. See PERFORMANCE and evidence/A-* for exact results and limitations.

## Blocked

Unity MCP remains unavailable; direct editor works. Windows/Linux execution and Steam integration not attempted. No blocker to Phase B.

## Exact next task

B.4: persist the verified GPU snapshots and ship runtime with integrity checks, atomic replacement and previous-save recovery; expose save/load in the playable showcase. Continue B.2 fuel transfers and B.3 physical fragment/breach behavior, then B.5 paged large-ship/streaming/index stress. Do not advance to Phase C until B.GATE passes.

B.1 GPU checkpoint: 25/25 EditMode tests, Mac build and standalone rotating starter benchmark pass. See SHIP_SYSTEM/PERFORMANCE and evidence/B-* for measurements. B.1 is checked; B.2/B.3 are only partially implemented.

Commands: `bash tools/unity.sh test`, `build`, `open`. Player preset: `Builds/Debris.app/Contents/MacOS/Debris -debrisShipBenchmark -logFile /private/tmp/debris-ship-player.log`. Benchmark artifacts write under Builds/Logs. Close only the Debris editor before batch execution.

## Resumption

Read PROJECT_PLAN.md → EXECUTION_PLAN.md → this file → TECHNICAL_ROADMAP.md and relevant subsystem docs. Preserve working changes. Run relevant checks and record evidence in the same commit as completed checklist entries; push origin/main at coherent checkpoints. Continue phases automatically after passing gates.
