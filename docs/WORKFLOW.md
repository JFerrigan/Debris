# Implementation efficiency

Decision, 2026-09-07: optimization is needed. Custom GPU collision and persistence require substantive verification, but repeated broad checks, growing context, repeated historical documentation and infrastructure expansion have delayed observable gameplay improvements. The small-debris hard stop remained after earlier occupancy tests passed. Improve acceptance criteria and batch delivery, not just typing speed.

## Researched guidance

- OpenAI's Astra guide explicitly identifies excessive test breadth on small coding tasks as a behavior to calibrate. It recommends repeating/broadening passing checks only when a change, failure or unresolved concern warrants it. Our validation matrix implements that guidance. [Astra testing guidance](https://developers.openai.com/api/docs/guides/latest-model#testing-and-verification).
- Codex loads repository `AGENTS.md` instructions; keeping that file concise makes working rules available without repeatedly reading the design library. The repo now uses the conventional uppercase filename. [Instruction discovery](https://learn.chatgpt.com/docs/agent-configuration/agents-md).
- Included usage depends on model, context, reasoning, tools and task complexity; a five-hour allowance is not five hours of guaranteed agent execution. API dollar prices cannot establish how much of this user's allowance a particular run consumed. Faster execution also need not mean cheaper execution. [Codex usage guidance](https://learn.chatgpt.com/docs/pricing#what-are-the-usage-limits-for-my-plan).
- Astra exposes multiple reasoning-effort levels. Our proposed routing is Astra for difficult physics/architecture and tricky failures; a smaller coding model, if the user selects one, can handle well-specified routine edits. For Astra, trial lower effort on routine work and reserve higher effort for the hard cases. This is a project recommendation to measure, not a documented guarantee of savings. `AGENTS.md` cannot switch the active model or effort, and this change does not alter account settings. [Astra capabilities](https://developers.openai.com/api/docs/models/gpt-6-astra).

## Delivery loop

1. Read the short STATUS handoff and current phase gate. Define the player-visible result and a small acceptance matrix. For physics, include feel/analytic behavior as well as conservation and non-overlap.
2. Inspect only relevant source. Make one short design decision, then implement a coherent feature with its meaningful regression cases. Finish the batch before running final validation.
3. Run focused tests while iterating. Once stable, run the fast suite if shared contracts changed. Build and run the player once when GPU/runtime integration requires it. Repeat only the checks affected by subsequent changes.
4. Update the owning design document and short status/checklist in the same commit. Add one row to [the batch record](evidence/feature-batches.csv); retain detailed validation in one canonical evidence file. Completion notes alone never trigger another Unity build.
5. Commit/push the feature and continue the next authorized gate. If the user redirects to planning/research, finish that bounded request first and retain the implementation handoff.

Use independent tool calls in parallel where safe, but keep a single implementation agent by default. Parallel agents multiply context and verification costs; use them only when explicitly authorized for independently valuable tasks. No recursive reviews, duplicate tests or model-based review of every tiny edit.

## Verification budget

The root AGENTS table is the executable policy. These are defaults, not permission to omit correctness checks:

- Markdown: local diff/link/checklist review only.
- Behavioral iteration: the failing regression and affected fixture.
- Stable shared-code batch: one fast regression run.
- GPU/player integration: one successful final Mac build and matching scenario.
- Expensive scaling: explicit workload for its gate, with setup/cleanup and timeout budget established before launch.

The pending 100,000-site experiment illustrates the distinction: its inner checks logged success, but the runner **failed** its 180-second timeout. It reported 113,238 ms writing and 33,257 ms collecting before overall completion/cleanup. Do not call this verified or rerun it during the contact-physics correction. Preserve the unfinished stream-write/test files; address timeout and maintenance scheduling when B.5 resumes. The measured collection time also rules out treating a full world scan after every save as a final large-world policy.

## Measure whether this helps

Use [evidence/feature-batches.csv](evidence/feature-batches.csv), one row per completed feature batch. Start/end timestamps are UTC ISO 8601; elapsed minutes include validation waits. Counts include failed attempts. For every repeated full suite, build or player run, give the observed failure or relevant subsequent change and evidence path. Use `none` when no rerun or unresolved defect exists. Usage is the actual client-reported figure with its units, otherwise `unavailable`. Never infer allowance use from API prices or elapsed time.

The ending commit may use a unique commit subject (`subject: ...`) in its own completion commit to avoid a self-referential hash; resolve it with `git log --all --fixed-strings --grep`. The starting commit is the hash before batch work. Documentation setup gets its own row with zero Unity runs.

Apply this process immediately to B.3R.1–B.3R.2 (single-cell momentum and forces), B.3R.3 (piles/cargo/fragments/anchors/sleep), then B.3R.4 (persistence/budgets/final player gate). After those three batches, review once: each must deliver its stated behavior and every expensive rerun must have an evidence-based reason. Require no documentation-only Unity launches or unrelated scale tests. Compare elapsed time, expensive checks and rework per accepted batch; compare allowance consumption only if comparable client usage exists. Leave failed acceptance and regressions visible. Reduced testing alone is not improvement. Adjust this workflow once from evidence, without introducing another planning layer.

Keep STATUS current rather than appending a session transcript. Historical measurements belong in the CSV and evidence. After contact acceptance, resume B.5, first correcting the explicit scale fixture timeout, then the salvage-loop gate and C–E in order. Preserve unfinished persistence work throughout.
