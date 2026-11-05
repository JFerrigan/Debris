# Physics playground debugging

Logging starts automatically when the rebuilt playground opens. Reopen the app after a rebuild. In the left panel enable **Diagnostics**, then choose **Copy debug log path**. The same path is printed once in Unity's Player.log at startup.

The current file is `PhysicsPlayground/debug.jsonl` below Unity's application-specific persistent data directory. `debug.jsonl.previous` preserves the preceding segment or run. Each segment rotates at 4 MiB; older segments are replaced. Lines are flushed immediately. File errors appear in Diagnostics and disable disk output while retaining the in-memory tail; they do not change run intent or stop edits.

To capture the reported issue:

1. Open the rebuilt app and select Run and a tool.
2. Perform the click that unexpectedly pauses or blocks an action.
3. Enable Diagnostics and copy the log path. Preserve both the current and previous files before more restarts.
4. Optionally choose **Export failure** for the accepted world, last input, GPU facts, build identity and the last sixteen debug events. Export deliberately pauses the world and logs that reason. The export remains `PhysicsPlayground/failure.json`.

Each JSONL record has schema version, UTC, session ID, monotonically increasing sequence, event name, detail and context. Controller context contains frame, tool, pointer, selected object, run intent, focus, text editing, busy/pending flags, revision, generation and attempted/accepted tick. Context is a JSON string so the generic logger can also store edit and solver contexts. Detail/context strings are bounded; long messages explicitly end in `[truncated]`.

| Event | Interpretation |
| --- | --- |
| `startup` | Unity/application version, assembly identity and embedded source manifest prefix (including source hash). |
| `input.left_down`, `input.left_up`, `input.space`, `ui.run_pause` | Actual input, including HUD hit, screen/world position and focus/busy state. A HUD click is logged even when controls are disabled. |
| `tool.change`, `pick.result`, `tool.begin`, `tool.end`, `tool.ignored` | Selected tool, picked object, edit request/outcome or busy refusal. |
| `run.request` | Exact reason and before/after run intent. Find `before=True after=False` to identify a pause. Reasons distinguish focus loss, component disable, explicit reset/load, stepping, export and solver failure. |
| `edit.begin` through `edit.end` | Edit ID, command, revision/generation and elapsed milliseconds. Stages include drain, snapshot, proposal, freeze, retire, construct, geometry validation, publish and recovery. |
| `edit.exception`, `edit.*_failed`, `edit.view_exception`, `operation.exception` | Exception type, message and stack (bounded), including otherwise UI-only failures. |
| `tick.submit`, `tick.submit_refused` | Exact input command; active manipulation commands are recorded, idle submissions sampled every sixty command IDs. |
| `tick.heartbeat`, `waiting` | Accepted-tick heartbeat every sixty ticks; busy/pending/pick waits at most once per five seconds. No extra GPU reads or world snapshots. |
| `solver.fault`, `tick.rejected` | Rejection/readback/submission failure, attempted and accepted tick, compact diagnostics, first-failure words and step clock. Submission exceptions also have the attempted input in `tick.submit_refused`; the solver's command field is the last successfully submitted ID. |
| `unity.Warning`, `unity.Error`, `unity.Exception`, `unity.Assert` | Unity diagnostics and stacks, including shader/kernel messages. |
| `status`, `busy.changed`, `application.*`, `log.closed` | Visible status changes, edit fence changes and lifecycle. |

Look for the input event, following edit ID, then `run.request` or `solver.fault`. `RUNNING` is the user's intent; an edit can be busy while that intent remains true. The log distinguishes this fence from a pause.

Verification: nine focused EditMode checks pass, including escaped JSON round-trip, sequence/context, file rotation, bounded history/export tail, unwritable-file behavior during edits and run-state regressions. Empty worlds dispatch no physics. The separate Mac app builds successfully; no player was launched by the agent. Real pointer interaction, GPU failure recording and the independent `QualifyCache` support issue remain unverified/unresolved.
