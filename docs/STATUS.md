# Implementation status

Current handoff: **the next implementation task is the [physics/performance viability checkpoint](PHYSICS_VIABILITY_PLAN.md), before more damage/fuel integration.** The [new-context Terra prompt](TERRA_PHYSICS_CHECKPOINT_PROMPT.md) specifies the bounded assignment. Planning is complete; the new benchmark and solver investigation have not run.

Limited candidate flight, drilling, effective door collision, GPU cavity classification/capacity admission and mounted suction exist under `-debrisParallelGameplay`; legacy remains the default. Candidate startup has 96 grains. Terrain/door topology changes are fenced, obstructed closure stays open, and classification accepts only whole oriented squares.

## Latest validation

The startup fixture covers door fence/open/clear close/obstructed closure, GPU cargo classification/capacity rejection, and exterior-vs-intact-hull suction. The fast EditMode suite passed 137 with one explicit scale test skipped (138 total, 2026-09-21 UTC); the Mac build succeeded. An opt-in player process launched, but did not emit Unity telemetry to the redirected stream, so player suction input remains unverified. Windows/Linux are unverified.

## Remaining limitation and next work

Next deliverable: measure the representative 8,192-active-grain workload, investigate packed .001 convergence and bounded interim behavior, apply an evidence-supported correction if feasible, and publish a decision. The existing proof runner does not implement that throughput workload. Historical strict packed cases reject; their early-failure maxima do not establish long-run .002 bounds. Current grain/solid rejection is .002, but rigid/solid rejection remains .001.

The [candidate damage/fuel transaction](CANDIDATE_DAMAGE_FUEL_PLAN.md) is deferred pending this checkpoint. Damage, fuel transfer, persistence, travel and streaming remain unavailable on the candidate. R1 and B.GATE remain open; a limited checkpoint pass alone does not close them.

## Preserved unfinished work

Matter.compute, ShipMatter.hlsl, MatterSession.cs and ContactIsland.hlsl retain the unfinished contact gatherer; [baseline patch](evidence/B3R-redesign-baseline/unfinished-contacts.patch). Presentation, ship/content, rigid-terrain exclusion, persistence, package/settings changes and DebrisV1.app remain unfinished workspace work. ParallelGameplayGrains.compute is an unused pre-existing duplicate; the solver now loads canonical ParallelGrains.compute. The built application includes existing workspace changes. Do not rerun the 100,000-site fixture for this handoff.

ParallelGrains.compute, ParallelGrainSolver.cs and ParallelGameplaySession.cs also contain uncommitted strongest-impulse/feature capture. Preserve it and identify its inclusion in measured builds; it is not a completed energy-based damage event pipeline.

## Commands

Unity 6000.3.11f1: `bash tools/unity.sh test`, `build`, `open`. Candidate gameplay: `Builds/Debris.app/Contents/MacOS/Debris -debrisParallelGameplay`. The viability flag and run protocol are specified in the plan and still need implementation. Historical one-player limits applied to prior investigations; the new Terra assignment defines its baseline/final measurement budget. Documentation changes need no rebuild.
