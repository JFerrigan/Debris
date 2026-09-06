# Ship System

A ship is a blueprint: free-drawn structural material layout plus cuttable structural-prefab placements and atomic unit-component placements/connections. `ShipRuntime` owns mutable fuel, transform, and component state; it submits commands to a loaded site rather than directly mutating pixel buffers.

Components are data-defined and use stable component IDs. The first slice includes upper/lower rear-facing propulsion, a front drill, a middle cargo cavity, a rear door, and suction. The starter ship is about 100 cells long with an approximately 50×50-cell cavity. A cargo cavity is an explicit fixed ship-local collision volume. Individual loose cells enter only through its open rear intake, retain their size/material/velocity, and naturally collide/tumble in zero gravity. With the door open they can spill back out. Its visible volume is its exact capacity: no resizing, compression, overlap, or hidden stack inventory is permitted. A future robotic organizer may arrange cells within the cavity.

Components attach to structural anchors. A severed anchor disables or detaches the component; cutting off a thruster removes the associated propulsion. Power is player-facingly abstract: an intact component supported by the continuous hull receives ship power from an operating source without visible cable routing. Material connectivity and stress determine supported regions; detached ship regions become physical loose fragments. Future player-built ships use these exact structure, cavity, and component rules, allowing larger ships (1000×1000 cells and beyond) to carry more through larger genuine cavities.

Cavities track sealed/pressurized versus vacuum connection state. Cargo does not need atmosphere, but oxygen stores, fire, pressure-sensitive parts, and life forms do. The starter drill and suction are fixed forward mounts. Later articulated arms provide an independent, player-controlled tool transform and must be modeled as vulnerable, physical components with joint limits.

Fuel is held as an inventory inside atomic fuel-tank units. Loose fuel remains a recoverable physical cell field outside the tank; a suitable pumping tool transfers it into tank inventory. A physical misc-storage unit likewise holds a capacity-limited menu inventory for raw hull-patch material, repair kits, and personal equipment. It does not enlarge cargo capacity. Whole units can fail/repair as units, while cargo bays, girders, and free-drawn hull remain material-cell structure that can be punctured and resealed.

## Implementation checklist

IDs refer to [EXECUTION_PLAN](EXECUTION_PLAN.md); only verified work is checked.

- [x] B.1 Blueprint/starter ship.
- [ ] B.2 Flight/fuel.
- [ ] B.3 Cargo/damage/fragments.
- [ ] D.2 Automation.

## M4 data checkpoint

`ShipBlueprint` supports atomic prefab placement, free-drawn structural cells, validated non-overlapping whole-unit footprints and connected structural support. The starter definition is 100 cells long with a 50×50 empty cargo cavity and seven machinery placements. `ShipRuntime` owns unit damage, support, fragments, inertia and finite tank inventory; cargo mass lowers acceleration/turning. Hull topology changes trigger connectivity work, not a per-frame scan. GPU moving-hull/cargo integration and streaming remain unverified; this data checkpoint alone does not complete B.1/B.2/B.3.

Verified data evidence: `docs/evidence/B-ship-tests.xml` (8/8 passed, 2026-09-06). Command loss retains inertia; destroyed tanks cannot supply propulsion. GPU/cargo gates remain unchecked.

## M4 GPU checkpoint — 2026-09-06

Starter hull and whole units render from a local cell mask. GPU collision uses separating-axis tests across world and rotated ship cells, separate occupancy domains, bounded integration and identity-preserving intake/spill. An obstructed door remains open. Mounted drilling follows the ship transform; suction pulls towards the rear intake. 25/25 EditMode tests pass (`evidence/B-gpu-tests.xml`), including 240 rotating steps with 100 cargo cells and exact snapshot resume. The Mac player benchmark with 576 cargo cells conserves matter and passes geometric validation.

B.2 remains open for physical fuel pumping/spills. B.3 remains open for moving/damageable fragments, hull breach release and full cavity saturation. The current 128² ship mask explicitly rejects larger structures; paged ship masks and large-ship stress remain B.5. Snapshot resume is currently memory-only; durable disk saves are next.
