# Playground reassessment / fresh-session handoff

Current user report (2026-11-12 UTC): Run causes long stalls and almost no motion. User has lost confidence and is considering a fresh session and a rewrite. This is a bounded assessment/handoff; do not infer authorization for a wholesale rewrite, further builds or automatic player launches.

## What the latest session proves

Canonical user log: `/Users/jakeferrigan/Library/Application Support/com.JFerrigan.Debris-Physics-Playground/PhysicsPlayground/debug.jsonl`, session starting 2026-11-12 04:14:29Z. Build source identity `722df31422bfecb3324c33ecf29fcf621e6a80ec811eda89516f02ddf6dd539a`, committed rendering fix `8cf3578`.

- Revision 36 contained 131 objects, accepted before Run at 04:15:18.776Z.
- Run resumed at 04:15:32.287Z, frame 3331, accepted tick 1. At the Pause click at 04:15:42.220Z, frame 3339, accepted tick was only 2 and tick 3 was pending. About ten seconds produced eight rendered frames and one additional completed tick.
- The session ultimately reached five accepted ticks. There were no recorded solver faults. Accepted ticks alone do not imply usable throughput.
- User pauses and single-step requests occur in the session, so five ticks over total session time must not be presented as a steady-state benchmark.
- The grid covering objects was a separate, reproduced and repaired rendering defect. CopyBuffer capabilities and the Metal cache-kernel compilation failure were separate repaired defects. None established interactive player acceptance.

## Why the current test evidence was insufficient

The 39 affected checks verified narrow physics/transaction cases; the 11 rendering/controller checks verified visible pixels and local state transitions. They did not verify usable Run behavior for the starting scene plus edited populations in a source-matched player. Reporting each repair as readiness overstated the evidence. Runtime responsiveness is now the blocker.

`CoupledTickStepper.RecordTick` records sixteen complete slot schedules per submitted tick, including velocity solving, geometry, prediction and position work. Graph/cache machinery includes large fixed-capacity buffers and dispatches. This is a concrete profiling target, not yet a measured attribution of all delay. The log lacks per-stage GPU timings; do not claim a precise GPU bottleneck from it.

## Concrete source diagnosis added after the initial handoff

Read the [Run cost audit](evidence/B3R-playground-run-cost-audit.md). C1 records at least 138,240 direct compute dispatches per tick, with fixed-capacity dot reductions submitting roughly 321 million workgroups. Inactive zero-valued reductions still have unconditional shared-memory loops/barriers in source. Audit capacity versus live counts and actual dispatch elimination before replacing solver mathematics or rewriting unrelated code. These counts are not measured stage timings.

## Recommended restart scope

Preserve this repository, commits, unrelated dirty files and physics contracts. A fresh session should begin on an isolated branch/worktree and audit the execution path before deciding what to replace. Do not repeat a full UI or game rewrite by default.

1. Build a minimal harness: visible body, Run/Pause/Step and one force input. Start with one free body, then two contacting bodies with an analytical expected result.
2. Measure cold first-step latency, command-recording time, completion latency, frame time and GPU stages. Keep UI rendering independent of simulation completion. Avoid recording large inactive workloads for tiny scenes.
3. Require responsive continuous operation before proceeding: start/stop, repeated steps and dragging must remain usable; distinguish compilation/initialization stalls from sustained per-tick cost.
4. Scale through small explicit populations (for example 1, 16 and the observed 131), recording live-body/row counts and dispatched work. Preserve mass/inertia, identity, anchoring and no-matter-loss invariants. Do not jump to the 100,000-site workload.
5. Reuse the current solver only if these gates pass. If its scheduling/capacity design prevents them, design and review a bounded solver replacement against the same analytical, conservation and measured runtime gates.
6. Add spawn/delete, brushes, presets and persistence after the minimal continuous demonstration passes. Finish with a source-matched player scenario; agree on launch authorization before automatic GUI execution.

Suggested prompt for a fresh session:

> Read AGENTS.md, docs/STATUS.md and docs/PLAYGROUND_RESTART_HANDOFF.md. The playground is not interactive: its latest user run advanced one tick and eight rendered frames in about ten seconds with 131 objects. Preserve existing work. Audit and measure a minimal continuous solver harness before further tools or builds. Treat passing isolated tests as insufficient. Propose the smallest repair or replacement based on measured evidence; do not launch the player automatically or rewrite the whole project without authorization.
