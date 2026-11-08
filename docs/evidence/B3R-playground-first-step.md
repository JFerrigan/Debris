# Playground first-step repair

The user's 2026-11-05 session (`debug.jsonl`, sequence 222 at 06:05:07Z) failed on its first Step command with `CopyBuffer source must have CopySource target`, in `CoupledTickStepper.RecordTemplates`. Command 1 never completed; the following Run click refused the faulted owner. Earlier edits were accepted or rejected by placement policy while run intent was already paused.

The recorder has four GPU copy paths: primitive templates, union-group templates, persistent endpoint bindings, and substep-start endpoint poses. Their sources now have `CopySource` and destinations have `CopyDestination`. Working transaction payloads and their header also accept queued staging copies; committed payloads remain published by the existing transaction shader. Shapes, strides, capacities, byte admission and physics equations are unchanged. The transaction test's staging buffers now declare `CopySource` too.

Once the copy exception was removed, the real off-center mouse-force case reached the separately known invalid Metal `QualifyCache` kernel. Rewriting the bounded traversal alone did not help. Shader-only isolation found:

- Contact extraction and impulse projection compile individually.
- Lookup fails even without projection or the cache-match atomic increment.
- Removing the nested duplicate-key atomic fault/early return makes lookup compile; removing only its atomic also compiles.
- Read-only cache aliases, a constant hash bucket and removing contact extraction do not fix it.

The final kernel counts matches during the same bounded traversal and emits the duplicate-key fault afterward. It retains cycle/index checks, duplicate rejection, impulse projection, and accepted-only cache publication. Direct GPU cases exercise both unique-key eligibility and duplicate-key fault 2048. Temporary diagnostic source/assets were removed.

Validation history and rerun reasons:

1. Buffer-contract regression: 1/1 passed (`/private/tmp/debris-copy-contract.xml`), including actual copies/readbacks for all four paths and full first-tick recording.
2. Actual mouse-force case: failed on the invalid kernel (`/private/tmp/debris-copy-force.xml`). A traversal-only rewrite still failed (`/private/tmp/debris-copy-force-loop.xml`); this rewrite was discarded.
3. Three shader-only probe invocations: initial component isolation; one diagnostic declaration-order mistake caused syntax errors and supplied no support evidence; corrected lookup isolation identified the nested branch. Logs `/private/tmp/debris-kernel-isolation.log`, `debris-kernel-isolation-lookup.log`, and `debris-kernel-isolation-lookup-fixed.log`. No simulation dispatched by these probes.
4. Final kernel's actual mouse-force case: 1/1 passed, including analytical translation/spin and an accepted tick (`/private/tmp/debris-copy-force-final.xml`, 6.732 seconds).
5. Affected regression fixtures: 28/39 passed; all eleven failures were CopyBuffer flag errors in the previously unexecuted transaction staging fixture (`/private/tmp/debris-copy-regression.xml`). Fixed staging-source and working-destination capabilities before repeating validation.

Final affected regression: **39/39 passed**, `/private/tmp/debris-copy-regression-final.xml`, 2026-11-08 23:41:17–23:42:05Z, 47.6565739 seconds. This covers direct unique/duplicate cache qualification, actual GPU steps, gravity/anchors, edits, transaction payload restoration/dependency failure, arena cleanup, and controller pause/logging regressions. Full contact/workload/performance qualification remains separate. The app is not launched automatically.


Final Mac build succeeded, exit 0, with no C#/shader errors (`/private/tmp/debris-first-step-build.log`). The separate app exists at `Builds/PhysicsPlayground/Debris Physics Playground.app`; all manifest source hashes match. Source identity `20543eace4489e46feb479022f505836aee4857f676c1ab219d609bb624f21a5`. No player launch; manual interaction and full contact/scale/performance acceptance remain open.
