# Delegated parallel proof pieces

Source `5898f8d`, following `8532323`. Three explicitly authorized GPT-5.6-luna workers drafted square contact geometry, rigid manifolds, and GPU diagnostics. The parent integrated and reviewed these pieces, replacing the draft rigid Jacobi path with bounded sequential rigid-only sweeps, adding behavioral GPU tests, and correcting geometry, capacity rejection, metric layout and timing collection defects. Normal gameplay and persistence remain unchanged.

## Verification

[Focused runner result](B3R-parallel-pieces-tests.xml): **30 passed, zero failed/skipped** (8 grain, 6 budget, 5 geometry, 6 metrics, 5 rigid). One matching Mac development build succeeded. One [player run](B3R-parallel-pieces-player.txt) exited 2 because packed correctness failed. M4 Pro, Metal, Unity 6000.3.11f1, 1280×800, v-sync off. The off-centre rigid impact passed with two manifold points and reported zero linear/angular momentum error and energy gain.

| Profile | Shared-motion rejected tick | Maximum wall penetration | Rest-under-force rejected tick | Maximum wall penetration |
|---|---:|---:|---:|---:|
| 4/2 | 28 | .00101184845 | 3 | .00118595362 |
| 8/4 | 39 | .0010048151 | 3 | .00112265348 |
| 12/6 | 43 | .00100898743 | 3 | .001034379 |

Every case exceeds the locked .001-cell solid limit. Grain maxima stayed below .004; no search-envelope violation was reported. Each case retained 2,500 committed grains. Identity sum/XOR are compact diagnostics, not a uniqueness proof. Shared-motion normalized momentum error peaked at 1.24e-6 and angular error at 9.79e-6. Forced-case momentum and energy deltas include external work and are not unexplained conservation errors.

GPU reductions sample committed tick boundaries, including spin energy; they do not establish every intermediate substep maximum. Penetration diagnostics include the rejected attempt. Explicit buffers were 25,994,372 bytes for this proof. CPU submission maxima are not p95. Physics/total GPU p95 and the 8,192/10,000-grain workloads remain unmeasured; no performance pass or speedup is claimed.

## Validation cost and rework

Nine editor launches: two C# compilation failures (missing test namespace, then legacy LooseCell name shadowing), one reopened-editor lock, and six fixture runs. Fixture results in order: smoke 1 pass; 25/30 pass (Metal invalid Abs modifier); rigid 5/5 pass; 29/30 pass (undersized manifold capacity); rigid 4/5 pass to isolate the capacity failure; final 30/30 pass after a post-gather capacity check. Warning cleanup occurred before final verification. One build and one player run; neither was repeated. No full repository suite was run. Final source was frozen during verification; the subsequent metadata edit only removes trailing whitespace.

The batch ledger uses the previous commit boundary, 2026-09-10T03:32:50Z, through completion notes. This wall interval includes multi-day user-resume/approval gaps and is not continuous implementation time. Client-reported usage is unavailable. Delegation reduced no measured cost claim; parent correction and verification costs are included. The first-three-batch review was already recorded and is not repeated.

## Stop and remaining scope

All three approved profiles still fail the necessary packed gate. Integration remains stopped: do not relax tolerances, add profiles/passes, or switch gameplay. Full R1 workload/performance coverage, production mask boundary caching/patch merging, complete diagnostic coverage, and R2–R4 gameplay, save migration and old-path removal remain incomplete. These independent proof pieces do not close B.GATE.
