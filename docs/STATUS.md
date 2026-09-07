# Implementation status

Current phase: Phase B / M6. Phase A gate passed. Planning baseline d46f7a9 is pushed. Current commit: `git log -1`.

## Documented

Phases A–E and locked interpretations are recorded in [EXECUTION_PLAN](EXECUTION_PLAN.md). Existing uncommitted design work is preserved in the planning baseline. Basic hub walking is Phase C.

## Implemented, unverified

Phase B blueprint/runtime foundation is implemented; starter moving hull/cargo and active-site disk checkpoints are verified. Phases C–E are not implemented yet. Large-world dirty-chunk streaming, ship physics, and player career progression remain unchecked.

## Verified

B.1/B.2 data checkpoint: 8/8 ship EditMode tests passed on 2026-09-06 (`docs/evidence/B-ship-tests.xml`), including atomic prefab rejection, 2,500-cell cavity, mass-dependent flight, fuel exhaustion, support detachment, command-loss inertia and tank failure. This does not complete the physical Phase B gate.

Planning baseline d46f7a9 and GPU implementation 1134125 pushed to origin/main. Unity setup and Mac build succeed. 15/15 EditMode tests pass: deterministic content, GPU/reference cutting, saturation, non-overlap, async inspection, and page eviction/resume with exact damage/cells.

Phase A Mac player gate passed on Apple M4 Pro: 8,192-capacity preset frame p95 17.577 ms, GPU p95 1.876 ms; all three presets conserve 22,400 initial material cells. Captured image inspected. Initial active budget: 8,192 loose cells/four 128² chunks. See PERFORMANCE and evidence/A-* for exact results and limitations.

## Blocked

Unity MCP remains unavailable; direct editor works. Windows/Linux execution and Steam integration not attempted. No blocker to Phase B.

## Exact next task

B.4: sparse dirty-only chunk files and atomic multi-site leave/revisit with portable cargo/fuel separated from deposited site debris. Then B.5 paged larger ship masks and active-region streaming.

B.3 verified: 36/36 EditMode tests, Mac build and standalone damaged rotating starter. Frame p95 17.584 ms, GPU p95 9.254 ms; 579 cells and one solid fragment survive fuel transfers and exact disk resume. Full 2,500-cell rotating cavity and anchor-only unit detachment pass geometric tests. See STRUCTURAL_SIMULATION for the 16-fragment/128² starter limits and deferred stress/further-fragment-cutting work.

B.2 verified: 29/29 EditMode tests, Mac build and standalone partial-fuel spill/recovery/save loop. Schema 2 preserves residual fuel and the next unused cell identity; schema-1 migration uses a retained actual standalone save. B.1/B.2 are checked, the full Phase B gate remains open.

Persistence checkpoint: 27/27 EditMode tests; Mac build; standalone save/load retains all 576 cargo cells, ship pose, fixed cells and partial damage. Standalone save size 25,891 bytes. Index-only fixture verifies 100,000 sites without loading payloads. See PERSISTENCE, SAVE_FORMAT, PERFORMANCE and evidence/B-persistence-* for exact scope. Full B.GATE is still unchecked.

Commands: `bash tools/unity.sh test`, `build`, `open`. Player preset: `Builds/Debris.app/Contents/MacOS/Debris -debrisShipBenchmark -logFile /private/tmp/debris-save-player.log`. Benchmark saves use a temporary verification slot; ordinary F5/F9 uses the persistent player slot. Close only the Debris editor before batch execution.

Pre-existing package/Unity-assistant settings, GraphicsSettings/QualitySettings edits and the untracked `DebrisV1.app` are preserved separately from implementation commits.

## Resumption

Read PROJECT_PLAN.md → EXECUTION_PLAN.md → this file → TECHNICAL_ROADMAP.md and relevant subsystem docs. Preserve working changes. Run relevant checks and record evidence in the same commit as completed checklist entries; push origin/main at coherent checkpoints. Continue phases automatically after passing gates.
