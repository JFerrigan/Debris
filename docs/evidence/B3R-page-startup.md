# GPU candidate startup Page fault

2026-09-18 UTC. Scope: investigate the reported startup error, reproduce with one game launch, explain aloud, then fix. No feature expansion or second player launch.

## Evidence and cause

The existing Mac player was launched once with `-debrisParallelGameplay -logFile Logs/page-startup-reproduction.log`. It logged `DEBRIS_PARALLEL_GAMEPLAY active` at line 38 and `Candidate solver fault: Page` at line 75. The diagnostic process was stopped after capture. The explanation was also spoken using macOS `say` before implementation.

`Page` is the solver's own fault bit (2). The earlier `Validate` kernel checked every endpoint's centre plus a conservative bounding radius against the grain bin page. The imported terrain includes a 512-cell world's border walls. A wall centred 256 cells from the origin with a 256-cell half-length produces a conservative radius greater than 512. Rigid patches are gathered directly, so this grain-bin restriction incorrectly rejects the terrain and prevents startup from committing.

The working tree already contained an unfinished workaround that removed page rejection and silently skipped out-of-page grains in `CountBins`. `ScatterBins` still used the invalid `0xffffffff` bin index, so merely suppressing the error was unsafe. The player binary also preceded the latest shader edits. The unused duplicate gameplay shader is preserved; the solver now explicitly loads canonical `ParallelGrains`.

## Fix

- Keep the expanded 256-by-256 grid of four-cell bins, covering grain centres in `[-512,512)`.
- Only loose-grain centres require a valid bin. Rigid terrain does not index that grid.
- Reject invalid grain bins during counting and guard scattering before buffer access. Reject a grain that crosses the page before committing the tick; preserve its prior position and identity.
- Return an empty grain array for a zero-grain population. The GPU's mandatory one-element backing allocation is not a real grain; exposing it caused the candidate acknowledgement to index an empty cargo array after the Page issue was removed.

## Validation

- Focused EditMode runner: exit 0, 11/11 passed (`Logs/page-focused.xml`, `Logs/page-focused.log`). Includes large anchored terrain, empty readback, both initial and moving page escapes, and actual generated startup import with three acknowledged thrust ticks and presentation restore.
- Fast EditMode runner: exit 0, 110 passed, one explicit scale fixture skipped (`Logs/page-regression.xml`, `Logs/page-regression.log`); 40.394 seconds test duration.
- Final Mac build: runner exit 0; `Logs/build.log:6296` reports `Build Finished, Result: Success.` Output: `Builds/Debris.app`. No source edits occurred during final verification.
- Player launches: exactly one, reproducing the original fault. No post-fix player acceptance run, per the user's limit. Automated GPU startup tests supply the post-fix evidence.

## Limits and preserved work

R2a gameplay, saved-world activation, pose conversion, tools, doors and rigid-terrain collisions are not certified by this change. In particular, an existing unfinished `ParallelRigidContacts.hlsl` edit excludes imported terrain from rigid-pair contacts; that is separate from the Page guard and remains outside this fix. The build includes the user's existing unfinished workspace changes. Numerical and performance gates remain open; no usage/cost figures were provided.

The fix commit includes the pre-existing candidate import class and solver population support needed by the new startup regression. Presentation routing and unrelated unfinished work remain unstaged. The first recorded UTC timestamp was 04:42:28, during reproduction after initial inspection; exact investigation start and total elapsed time are unavailable and are not estimated in the batch record.
