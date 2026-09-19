# B.3R candidate drilling

Status: partial implementation; not accepted.

Implemented commits: `cf44da6`, `e7ae23f`, `32f5abb`, `5b5138e`, `c626420`, `861244d`, `72a0a92`.

Validation on 2026-09-19 UTC:

- EditMode: 121 passed, 1 explicit scale skip (122 total).
- Mac build: succeeded.
- One bounded `-debrisParallelGameplay` player launch: no candidate activation, drill, Metal, solver, or readback telemetry was emitted before termination. It cannot establish interactive acceptance.

Known gaps: the current GPU placement query checks active grains only; rotated grain and solid hull SAT are not yet implemented. Transaction-level rejection, repeated-cache, reset-race and no-population-snapshot fixtures are absent. The cache and grain publication path therefore has no acceptance claim.
