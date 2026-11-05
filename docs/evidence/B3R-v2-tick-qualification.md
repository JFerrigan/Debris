# V2 tick qualification checkpoint — 2026-11-05 UTC

The user requested continued work after being asked to lift the previous source-only restriction for validation. Runtime qualification resumed; this checkpoint does not accept V2 physics or change gameplay defaults.

## Execution blocker

One pinned Unity 6000.3.11f1 EditMode attempt selected the transaction, position correction/search/solve, Arnoldi, velocity residual and preconditioner fixtures. Unity exited **198 before project compilation or test execution**. No result XML was created. The log ends with:

```text
[Licensing::Module] Error: 'com.unity.editor.headless' was not found.
No valid Unity Editor license found. Please activate your license.
```

Canonical attempt log: `/private/tmp/debris-v2-resume-focused.log`. No second Unity attempt, full suite, build or player run followed. A valid Editor license is required before retrying; no account or licensing settings were changed.

## Direct source compilation

The installed Roslyn compiler was run directly with the pinned Editor's `Resources/Scripting/NetCoreRuntime/dotnet` and `Resources/Scripting/DotNetSdkRoslyn/csc.dll`. Final result: **exit 0**, including all 24 `Runtime/CoupledContacts/*.cs` sources, `ParallelProof/ProofRecords.cs` and these eight fixtures:

- `CoupledTickStepperTests`
- `CoupledTickTransactionTests`
- `CoupledPositionCorrectionTests`
- `CoupledPositionSearchTests`
- `CoupledPositionSolveTests`
- `CoupledArnoldiWorkspaceTests`
- `CoupledVelocityResidualTests`
- `CoupledManifoldPreconditionerTests`

Compiler references came from the pinned Editor's `NetStandard/ref/2.1.0`, `Managed/UnityEngine`, the bundled NUnit assembly, and `NetStandard/compat/2.1.0/shims/netfx` for NUnit's framework references. Final response file: `/private/tmp/debris-v2-resume-tests.rsp`; output: `/private/tmp/debris-v2-resume-tests.dll`.

Four direct compiler invocations occurred: initial runtime reference-path setup failed to find UnityEngine; corrected runtime references passed; adding fixtures exposed missing NUnit framework facades; adding the installed compatibility facades passed. These were compiler-harness setup failures, with no C# source fix or expensive runtime rerun. This validates C# syntax and referenced API contracts only. It does not compile compute shaders, allocate the live arena, validate resource bindings or execute tests.

## New integrated regression cases

The new [tick fixture](../../Assets/Debris/Simulation/Tests/Editor/CoupledTickStepperTests.cs) records the real `RecordTick` path with a classified grain and explicit anchored counterpart sharing the default 128 MiB arena:

1. Force/torque-driven free flight selects four substeps and must publish analytical semi-implicit position/angle and physical velocity/spin. Forces on the anchor must leave it fixed. Grain state, inventory/cargo/terrain, header/cache generations, empty active cache, final geometry and physical verdict are checked. The twelve inactive slots must preserve four checked substeps.
2. A 121-cell/s initial speed requires more than sixteen substeps and must reject at input without publication. Committed/working headers and every payload, plus retained graph centers, must match the initial state.
3. A grain starting at x=511.99 with vx=4 must cross the page boundary after prediction and reject at substep acceptance. Every payload and graph center must restore; the persistent first failure must retain the grain-page bit; a second queued tick must report dependency failure.

Each case has a ten-minute timeout covering the full fixture. All three compile but remain **unrun**, including construction-time resource admission. After license restoration, include `Debris.Simulation.Tests.CoupledTickStepperTests` with the seven fixtures selected by the initial attempt. Diagnose actual runner results before considering a build. Exact packed references, physical contact/cache cases, shader acceptance, resource/schedule measurements and every V2/B.GATE acceptance remain open.
