# Candidate damage and fuel transactions

Status: planned from the existing candidate drilling, door, cargo and suction code. **Deferred until V2-3 solver qualification and V2-4 candidate integration pass in the [V2 implementation plan](CONTACT_SOLVER_V2_IMPLEMENTATION.md).** This is not an acceptance record. The failed historical viability checkpoint did not authorize damage/fuel continuation.

Existing uncommitted strongest-impulse/feature capture is preliminary. The contact contract requires effective energy, body/feature/local-point context and acknowledgement semantics as well as impulse; do not treat the current capture as completed damage acceptance.

## Observed boundary

`ParallelGameplaySession` owns an allocated grain region and fixed body endpoints. `ShipDamage.CutHull` and `FuelTransfers.Pump`/`Spill` instead produce a new legacy `MatterSnapshot`: they can remove grains, append grains, alter hull structure, change fragments and update tank inventory. Calling those legacy helpers against the presentation mirror would leave the candidate GPU buffers authoritative for a different world.

Damage and fuel therefore need one fenced candidate reconfiguration transaction. It starts only after all submitted completions acknowledge, reads the current candidate state once, builds and validates the proposed ship/grain/body topology without live-buffer mutation, then publishes one topology generation and CPU facts in one ordered submission. V2 uses one solver arena and its bounded topology scratch allowance; constructing a second full solver would exceed the intended resource budget. Preserve touched old records until proposal validation/publication succeeds, invalidate affected contact keys, and reject proposals that cannot fit the bounded transaction. Any rejected proposal keeps identities, tank contents and topology unchanged. A submission/readback failure faults the candidate until recovery.

## Required transaction inputs

- Current candidate grain centres, velocity, angle, spin, material, identity and cargo flags from an explicit topology snapshot.
- Current committed ship/fragment poses and motion, terrain state/cache and door-effective hull mask.
- Authoritative `ShipRuntime` structure, units, fuel inventory and material catalog.

## Required outputs

- One published V2 topology generation with the same allocated grain capacity, refreshed terrain/boundary spatial index, body definitions and invalidated affected contact cache entries.
- Fuel pump removes only qualifying physical fuel grains and their residual-energy records after tank admission; spill appends material-matched physical fuel grains only after capacity, identity and placement admission.
- Hull damage releases mass-matched physical grains and refreshes the ship/fragment boundary cache, body mass and inertia together.
- Candidate terrain transaction and presentation bindings adopt the new generation only after GPU publication acknowledges; no replacement solver arena is constructed.

## Acceptance cases

- Rejected pump/spill/damage leaves candidate grain identities, tank energy, terrain and body definitions unchanged.
- Successful pump/spill preserves total fuel energy and does not duplicate or overlap matter.
- Successful hull damage preserves released material/identity accounting and publishes the same shape revision/cache/body mass together.
- A queued flight completion fences all three operations; normal completions resume with compact facts afterward.
- Final implementation requires focused GPU transaction tests, fast EditMode suite, Mac build and an opt-in player scenario with `-logFile` telemetry.
