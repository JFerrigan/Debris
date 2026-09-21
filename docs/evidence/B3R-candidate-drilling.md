# B.3R candidate drilling

Status: accepted for the limited opt-in drilling scope. Legacy remains the default.

Implemented commits: `cf44da6` through `b35c4e9`.

Final validation on 2026-09-21 UTC:

- Focused EditMode: `CandidateTerrainTests` 8/8, `CandidateTerrainTransactionTests` 3/3, and `ParallelRigidTests` 7/7.
- Fast EditMode: 132 passed, one explicit scale skip (133 total).
- Mac build: succeeded after the final rigid-contact change.
- Bounded `-debrisParallelGameplay` player acceptance: activation reported 96 grains; thrust advanced the ship from `(-175,0)` to approximately `(-158.11,-0.24)` with turn; two releases advanced grains `96 → 98` and terrain revision `1 → 3`; no-target feedback followed; reset logged and reactivated the deterministic 96-grain layout. The final log contains Metal initialization and no solver/readback fault.

The first player attempt exposed an `Envelope` fault immediately after release. Terrain cache patches had been rebuilt in world coordinates although the terrain body pose already applies its origin. `aa245e8` keeps cache geometry local while retaining world addressing for dirty rows/columns. The next run reached two releases but faulted `RigidPairCapacity`; `b35c4e9` restores the contract's `.25` rigid gather margin in place of `.75`. Both failures were fixed before the final suite, build, and player rerun.

The current GPU placement query uses oriented-square SAT for active grains, ship hull cells and fragment masks. Automated coverage proves damage-only publication, empty-population release, rejection preservation, translated incremental-cache equivalence, duplicate identity/revision exhaustion, and terrain-body revision advancement. R1 convergence, packed-cargo performance, suction, effective doors, damage/fuel transfer, persistence, travel, default cutover and B.GATE remain open.
