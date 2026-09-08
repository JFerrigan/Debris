# Debris working instructions

## Scope and continuity

- Follow the user's latest priority. Continue authorized implementation autonomously, in phase order, without routine permission questions. A request to research or update plans is a bounded deliverable; finish it before resuming feature expansion.
- Start with `git status --short`, `docs/STATUS.md`, and the current block of `docs/EXECUTION_PLAN.md`. Read PROJECT_PLAN once per fresh context, then only the subsystem sections and source needed for the task. Do not reread the entire design library or historical test XML.
- Preserve unrelated changes and unfinished work. Stage explicit paths. Commit coherent, reviewable features and their completion notes; push ordinary commits to origin/main under the existing authorization.
- Current priority: B.3R mass-based contact physics. Previously passing occupancy tests do not prove acceptable collision behavior. B.GATE stays open until the new physical acceptance cases pass.

## Work in complete feature batches

- State the intended observable result, affected subsystem and acceptance checks briefly, then implement. Prefer one complete feature over a series of infrastructure-only commits.
- Keep design exploration short: choose between at most two credible approaches unless evidence requires more. Do not implement later-phase machinery or speculative abstractions to solve the current defect.
- Batch independent searches/reads; use `rg` and targeted excerpts. Return error summaries and relevant lines instead of whole logs, documents or generated artifacts.
- Use one agent by default. Additional agents require explicit user authorization; they can reduce elapsed time while increasing total usage. Never duplicate implementation or verification across agents.
- If the same approach fails twice, isolate the failing case and inspect evidence before another patch. Expand scope only to address an observed dependency or defect.

## Validation proportional to the change

| Change | Required verification |
|---|---|
| Markdown/plans only | Diff, links and checklist consistency; no Unity launch |
| Local C# behavior | Focused meaningful tests, including the reported regression |
| Shared simulation/save contracts | Affected tests during development; fast regression suite once after the batch is stable |
| GPU/render/input/player integration | Relevant tests, then one Mac build and matching player scenario on the final implementation |
| Scale/performance | The relevant explicit workload when that gate is being worked; keep it out of routine tests |

- Repeat a passing check only after a relevant change, failure or unresolved concern. Never run a build after a failed test/compile without first diagnosing it.
- Do not edit source while its final verification is running. Finish the implementation and its regression cases before paying for a build/player cycle.
- Set realistic timeouts for slow fixtures, including setup and cleanup. A success log inside a timed-out test is a failed test. Inspect the runner result, not a stale report.
- Keep one canonical evidence result per checkpoint. Reuse existing physics/performance evidence for unaffected code. Do not rebuild for completion notes or test-file-only edits.
- Unity: `bash tools/unity.sh test`, `build`, `open`. Focused runs use the pinned editor with `-runTests -testPlatform EditMode -testFilter <fully-qualified-test-or-fixture>` and separate log/result paths. Close only the Debris editor if batch execution needs it.

## Cost, reporting and handoff

- Do not silently change the user's model, effort or account configuration. AGENTS.md cannot set those controls. Use the selected model efficiently; model/effort suggestions and their limits are in `docs/WORKFLOW.md`.
- Keep STATUS a short current handoff: next task, exact failure, unfinished files and latest relevant evidence. Historical detail belongs in git and evidence, not repeated status appendices.
- At completion report behavior changed, validation, commit and remaining limitation. During work give concise updates on findings and decisions, not narration of every tool call.
- Track checks/builds repeated and why, elapsed time and the feature delivered. Record actual token usage only when exposed by the client; never invent savings or equate tool waiting time with token cost.

## Simulation invariants

No per-cell GameObjects/Rigidbodies, matter deletion, overlapping cargo or duplicate inventories. GPU owns high-volume matter and contact solving; CPU submits commands and reads compact facts. Dynamic bodies exchange momentum according to mass/inertia. Anchoring is an explicit persistent body policy; sleeping and GPU budget limits must not turn a loose pixel into an immovable obstacle. See `docs/CONTACT_PHYSICS.md`.
