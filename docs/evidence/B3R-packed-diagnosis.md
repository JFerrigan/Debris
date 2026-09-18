# B.3R.R1 packed-cargo diagnosis

Diagnostic batch starting at `c4e114c`. Three explicitly authorized GPT-5.6-luna workers at high reasoning effort own independent reference, trace resources and reproduction fixtures. The parent owns shared integration, review and all Unity runs. Client-reported usage: unavailable.

## Locked scope

The canonical `ProofFixtures.Packed(bool)` remains unchanged: 2,500 unit-density squares in a 50×50 cavity, body mass 10,000, inertia 5,000,000; shared translation/spin or resting cargo under thrust/torque. Profiles remain 4/2, 8/4 and 12/6; timestep selection, contact equations, iteration counts, physical tolerances and commit behavior are unchanged. This batch does not modify gameplay, persistence or the existing unfinished contact gatherer.

`-debrisParallelProof -debrisProofTrace -debrisProofOutput <path>` enables bounded latest-attempted-tick tracing and exports after each case. The six canonical cases keep their original 120-tick bound and stop at the first rejection. Stationary, translation-only and rotation-only variants keep mass, geometry, density and profiles fixed; each diagnostic variant runs up to 12 ticks or rejection. Instrumented runs do not establish performance acceptance.

## Findings and acceptance

The user accepted the measured penetration as a limitation for this diagnostic checkpoint and requested only a working build. No solver tolerance, equation, iteration count or gameplay cutover was changed. R1 and B.GATE remain open.

- All six identical-pre-failure-state comparisons preserve identical committed positions, velocities, angles, spins and rejection behavior with tracing enabled or disabled. Independent untraced repeats also match for these single attempts.
- The independent double-precision replay passes the checked geometry, effective-mass, degree, impulse and reduction comparisons within their numerical tolerances. This does not prove convergence or exact agreement for ambiguous SAT axes.
- Resting packed cargo still fails at tick 3: wall penetration 0.001185954, 0.001122653 and 0.001034379 cells for profiles 4/2, 8/4 and 12/6. The unchanged limit is 0.001.
- Long shared-motion trajectories varied between untraced repeats during development. Comparisons therefore start from identical committed states immediately before rejection. The final player rejects shared-motion cases at ticks 28, 39 and 43.

## Final working build

`Builds/Debris.app` was rebuilt with Unity 6000.3.11f1 on Apple M4 Pro / Metal. The final build log reports success and contains no shader errors. The ship screenshot was inspected: hull, cargo, asteroid and HUD render correctly.

- [Focused test result](B3R-packed-diagnosis-tests.xml): **52/52 passed**, 29.757 seconds, 2026-09-18 00:06:44–00:07:14 UTC.
- [Reference and parity facts](B3R-packed-diagnosis-reference.txt): six valid reference replays and identical committed trace/no-trace results.
- [Mac proof player](B3R-packed-diagnosis-player.txt): rigid impact passes with two manifold points; all six packed cases retain the known rejection, exit 2. This is recorded failure evidence, not a physics acceptance pass.
- [Mac ship player](B3R-packed-diagnosis-ship.txt): exit 0; cargo non-overlap, material conservation, fuel/disk round trips and two-site leave/revisit pass. GPU p95 74.270 ms; this is not a performance acceptance pass.

Normal gameplay still uses the legacy solver. The build includes the user's preserved unfinished gatherer, persistence and package/settings work; those files are deliberately outside this diagnostic commit. Their recorded baseline hashes are unchanged. Windows/Linux and the full R1 performance matrix remain unverified.

## Validation ledger

- Initial working tree and Debris editor ownership checked; no running Debris editor. Unrelated tracked-file hashes captured outside the repository for preservation verification.
- First-three-batch workflow review already completed in [its canonical record](B3R-first-three-review.md); not repeated.

- Prior diagnostic attempts (nine available logs): initial startup/license failure; ambiguous CompressionLevel compile error; wrong LooseCell namespace compile error; 17-test smoke pass; 51/52 full focused run; isolated parity investigation failure; parity correction pass; another 51/52 run; final 52/52 pass. Earlier long-run parity assumptions and reference checks were corrected before completion. The user-reported disk/cache cleanup and licensing delay belong to this interrupted batch; exact prior elapsed active time is unavailable.
- Resume: a fresh 52/52 run followed shader reimport because the previous player failed rigid impact and its build log contained ParallelGrains shader syntax errors despite a successful build result. Touching the source forced reimport without a content change. The rebuilt player passes rigid impact, consistent with stale shader build data; the exact cache cause was not isolated.
- One resumed build startup was stopped after sandbox database/licensing access errors; the next build ran outside the sandbox and passed. No source was edited during verification. Two resumed player runs checked the proof and the ship/save scenario. No full suite or scale workload was rerun for these diagnostic-only changes.
- Final build log: `Logs/build.log`; final runtime logs: `/private/tmp/b3r-wrapup-player.log` and `/private/tmp/b3r-wrapup-ship.log`. Earlier raw logs and large trace archives remain in `/private/tmp`; compact canonical results are committed above.
