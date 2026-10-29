# V2-2 GPU solve source handoff

Current checkpoint: frozen velocity and position solve source; no complete solver tick or V2-2 gate pass. The latest user direction is to write code without launching Unity or the game. Prior focused GPU evidence remains in [STATUS](../STATUS.md); it does not cover the new position path.

## Position behavior added on 2026-10-29 UTC

`CoupledPositionSolve` owns a fresh normal-only minimum-displacement solve over a generated contact graph. Each `Record` clears all position multipliers and free displacement before starting. It never binds committed physical velocities or poses. Slop is .002 cell for two classified grains and .0001 cell if either endpoint is classified rigid/terrain. Missing classification, nonfinite values, invalid provenance and nonpositive scaling fault the equation evaluation.

The implementation reuses the existing projected-residual search machinery through an internal position mode of `CoupledVelocityResidual`; its public velocity constructor still rejects normal-only graphs. Position evaluates `F=p-max(0,p-(B*p+D^-1/2*(gap+slop)))` and its selected normal branch derivative. Normal-only dispatches and snapshots support odd row counts. Local manifold factors support one/two position rows as well as two/four velocity rows. Position uses no tangent row, velocity bias or friction law.

All profiles permit four position Newton updates per geometry update. C1/C2/C3 select 16/24/32 Krylov products per Newton update. The shared Arnoldi implementation preserves its GMRES(16), true linear-residual verification, counted restarts and search-only regularization retries. Eight frozen-equation trials use one stored direction/product. GPU selection chooses the first Armijo or physical-residual trial, then projects multipliers nonnegative and recomputes the true residual before setting acceptance. Position acceptance uses the specified 1e-5-cell projected residual; diagnostic multiplier/gap products do not inherit velocity-unit inequality tolerances.

The output exposes scaled multipliers and mass/inertia-weighted endpoint displacement, valid only when control word 1 indicates acceptance. The final projected residual product already reconstructs that displacement; exposing it adds no operator product. This is an equation solution, not permission to publish a pose: actual-pose search must discover new contacts and enforce the motion envelope first.

All allocations use the shared arena and unwind on constructor failure. Normal-only branch and pivot arrays hold one entry per row. Velocity mode binds two minimal unused geometry records for Metal resource completeness. Position and velocity production scratch aliasing/lifetime admission is still open; separate full-capacity operators/workspaces must not be assumed to fit together.

## Validation for this checkpoint

Direct Roslyn C# compilation against the pinned Unity 6000.3.11f1 reference and engine assemblies exited 0. The compiled set includes every contact runtime source plus `CoupledPositionSolveTests`, `CoupledVelocityResidualTests`, `CoupledArnoldiWorkspaceTests` and `CoupledManifoldPreconditionerTests`. The command used the installed `DotNetSdkRoslyn/csc.dll` directly, with an output assembly in `/private/tmp`; no Unity process was launched. Final compile response: `/private/tmp/debris-position-source.rsp`.

Six new position regression cases are defined and compiled, but unrun:

- Three mass/slop cases cover finite mass-1/mass-4 endpoints, rigid-contact slop, explicit anchoring, all profiles and their four-update cap. Each repeats with poisoned initial scratch to check fresh-start semantics.
- A single-corner normal row checks odd active-row counts and zero-displacement early acceptance.
- An unclassified contact must fault instead of accepting a displacement.
- A velocity graph must be rejected before allocating position scratch.

Shader import/Metal compilation, GPU tests, builds and player runs were not executed. No physical convergence, conservation, production memory or timing acceptance is claimed. Earlier velocity/operator GPU results cover their earlier source revisions only.

## Next behavior

Implement actual-pose eight-trial line search, candidate requery and gather-envelope enforcement around this frozen correction. Refresh the graph after accepted motion and reset the position multiplier again. Then connect independent final geometry acceptance, cache validity and whole-tick publication/rollback. V2-0C exact packed references, V2-1 geometry/operator/cost gates, V2-3 throughput, V2-4 candidate integration and B.GATE remain open.
