# Candidate damage and fuel transactions

Status: planned from the accepted candidate drilling, door, cargo and suction code. This is not an acceptance record.

## Observed boundary

`ParallelGameplaySession` owns an allocated grain region and fixed body endpoints. `ShipDamage.CutHull` and `FuelTransfers.Pump`/`Spill` instead produce a new legacy `MatterSnapshot`: they can remove grains, append grains, alter hull structure, change fragments and update tank inventory. Calling those legacy helpers against the presentation mirror would leave the candidate GPU buffers authoritative for a different world.

Damage and fuel therefore need one fenced candidate reconfiguration transaction. It starts only after all submitted completions acknowledge, reads the current candidate state once, builds and validates the proposed ship/grain/body topology without live-buffer mutation, then publishes all replacement buffers and CPU facts in one ordered submission. Any rejected proposal keeps current candidate buffers, identities, tank contents and topology unchanged. A submission/readback failure faults the candidate until reset.

## Required transaction inputs

- Current candidate grain centres, velocity, angle, spin, material, identity and cargo flags from an explicit topology snapshot.
- Current committed ship/fragment poses and motion, terrain state/cache and door-effective hull mask.
- Authoritative `ShipRuntime` structure, units, fuel inventory and material catalog.

## Required outputs

- A new solver with the same allocated grain capacity, current terrain cache and refreshed ship/fragment body definitions.
- Fuel pump removes only qualifying physical fuel grains and their residual-energy records after tank admission; spill appends material-matched physical fuel grains only after capacity, identity and placement admission.
- Hull damage releases mass-matched physical grains and refreshes the ship/fragment boundary cache, body mass and inertia together.
- Candidate terrain transaction and presentation binding are recreated only after the replacement solver is live.

## Acceptance cases

- Rejected pump/spill/damage leaves candidate grain identities, tank energy, terrain and body definitions unchanged.
- Successful pump/spill preserves total fuel energy and does not duplicate or overlap matter.
- Successful hull damage preserves released material/identity accounting and publishes the same shape revision/cache/body mass together.
- A queued flight completion fences all three operations; normal completions resume with compact facts afterward.
- Final implementation requires focused GPU transaction tests, fast EditMode suite, Mac build and an opt-in player scenario with `-logFile` telemetry.
