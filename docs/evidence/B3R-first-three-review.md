# First three B.3R implementation-batch review

This is the single required review of the first two recorded physics batches and the new R1 proof batch. R0 was a documentation batch, not a third physics implementation. Source: [feature-batches.csv](feature-batches.csv) and each batch's canonical evidence.

| Batch | Accepted behavior / unresolved result | Elapsed minutes | Validation cost |
|---|---|---:|---|
| B.3R.1–2 | Isolated mass-dependent pixel response; broader coupling pending | 14.20 | 5 focused, 3 full suites, 2 builds, 2 players |
| B.3R.3 | Scoped piles/cargo/fragment response; combined performance failed at 324.505 ms GPU p95 with 29 fallbacks | 28.84 | 10 focused, 1 full suite, 1 build, 1 player |
| B.3R.R1 proof | Analytical/rollback prerequisites pass; all packed profiles fail, so no production cutover | 67.54 through player output; 1481.42 through resumed handoff | 10 editor launches (3 pre-fixture failures; 7 fixture runs), 0 full suites, 1 build, 1 player |

The last row includes a long resumed-session gap in its full wall interval. Neither wall time nor fewer broad checks establishes usage savings. Client usage is unavailable in all three records. The new batch is not an accepted replacement solver: it produced a runnable experiment and a measured reason to stop before migration.

The first batch reran broad checks after starter rounding and fuel-density fixes. The second avoided build/player repetition but failed its central performance goal and did not cover genuinely packed rotation. The third isolated packed convergence early and avoided migration rework, but compiler/lifecycle setup and a precise substep-boundary defect required repeated focused launches. The final validation latch fixes the observation of maxima when multiple threads detect failure. Each rerun reason is retained in [the experiment](B3R-parallel-experiment.md); no documentation-only Unity launch or unrelated scale fixture ran.

## One workflow adjustment

For the next newly introduced Metal solver kernel batch, use a compiler/layout plus isolated-contact smoke fixture first, inspect shader errors **and repeated warnings** together, then expand to packed-load fixtures after the basic pipeline is clean. Check editor ownership and stale test processes before launching. This is an ordering adjustment to focused verification, not removal of packed, conservation, player or performance gates. Keep one final stable build/player cycle and retain failed gates explicitly. No model/effort/account change or additional agent is authorized by this review.
