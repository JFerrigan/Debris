# Continuous implementation contract

Authorized scope: implement phases A–E in order through playable alien-drone escalation. Continue automatically when each gate passes. Final story resolution is outside this run. Latest priority: record feature-batch efficiency and implement B.3R before B.5. Follow [AGENTS.md](../AGENTS.md) for validation and concise context loading. Preserve existing work, make coherent subsystem commits, and push ordinary commits to origin/main.

## Locked design interpretations

- Physical commodity cargo is simulated cells. Fuel-tank and capacity-limited misc-storage inventories are explicit menu exceptions.
- Dynamic ships, fragments and loose cells exchange momentum through finite mass/inertia. Anchoring is an explicit persistent policy for planets, home bases and designated giant bodies; sleeping and budget limits never turn a free cell into an immovable obstacle.
- Machinery has whole-component damage. Structural prefabs instantiate individually destructible cells.
- The starter ship is unpressurized. Pressure requires an upgrade/unit and a sealed compartment. No spoilage, refrigeration, or special cargo handling.
- Landed sales remove selected cells atomically, without unloading animation. Organizers may snap cells into valid non-overlapping positions within capacity.
- Temporal encounters expire without permanent site histories. An active encounter save retains its session, identities, inventory, damage, and expiry so resume cannot duplicate or reset it.
- Frontier Count advances only during strategic travel. Interest recurs at Count intervals; offline time, menus, and ordinary close-up work do not advance it.
- Recovery forfeits carried cargo, never unrelated previously deposited site material. Home destruction persists; alternate contacts retain essential recovery/progression services.
- Cepheus and key NPC dialogue is authored. Procedural conversations select authored fragments.

## Checkpoint 0

- [x] P0.1 Preserve this handoff, link project plan, remove Phase-A-only stop rule.
- [x] P0.2 Add stable implementation checklists to subsystem documents; separate documented/implemented/verified/blocked status.
- [x] P0.3 Record environment, reconcile design conflicts, commit/push planning baseline.

## Phase A — engine foundation and simulation proof

- [x] A.1 / M0 Validate Unity, pin packages/editor, fix C# compatibility, reproduce editor/test commands.
- [x] A.2 / M0 Configure URP, input actions, bootstrap, showcase, content validation, diagnostics.
- [x] A.3 / M1 Verify IDs, RNG, coordinates, material catalog, asteroid generation.
- [x] A.4 / M2 Chunk allocation, rendering, dirty updates, asynchronous name/value inspection.
- [x] A.5 / M3 Fixed-step cutting, loose allocation, occupancy/collision, sleeping/streaming, lossless overflow.
- [x] A.6 / M3 Shader layouts and CPU reference fixtures.
- [x] A.GATE Runnable Mac showcase cuts asteroid with material accounting, CPU/GPU frame and memory measurements. Saturation throttles without deleting cells. Record scaling before approving active budget.

## Phase B — physical ship and persistent salvage

- [x] B.1 / M4 Blueprint free-drawing, structural prefabs, whole units, starter command/propulsion/tank/drill/suction/cavity/rear door.
- [x] B.2 / M4 Inertial flight, cargo mass, fuel grades/inventory, spill/pump transfers.
- [ ] B.3 / M5 Moving-hull contact physics, tumbling cargo, spills, component support loss, fragments. Reopened: the verified admission-only solver hard-stops on tiny debris.
- [ ] B.3R.1 Body mass/inertia/mobility, force commands and analytical contact oracle.
- [ ] B.3R.2 GPU ship–cell momentum exchange; eliminate ordinary pixel hard stops and CPU velocity resets.
- [ ] B.3R.3 Dense piles, cargo mass counted once, fragment torque, anchored bodies and dynamic chips.
- [ ] B.3R.4 Physics save migration/resume, bounded contacts and measured player acceptance. See [CONTACT_PHYSICS](CONTACT_PHYSICS.md).
- [x] B.4 / M6 Dirty chunks, loose/fragment records, atomic saves, migrations, interrupted-write recovery.
- [ ] B.5 / M6 Large ships, prolonged cutting, streaming, 100,000-site index stress.
- [ ] B.GATE Mine → collect → spill → save → reload → revisit preserves authoritative state. Controllable force-driven ship; isolated cells barely affect it, piles transfer mass-dependent impulses, anchored bodies remain fixed, and transfers never duplicate matter.

## Phase C — contractor career, home, freedom

- [ ] C.1 / M7 Navigation, unknown contacts, scanners, waypoints, return risk, routes, Frontier Count, distance bands, planetary contacts.
- [ ] C.2 / M7 Basic Arcturus walking and ship exit/return, company bay, customization, shops, authored dialogue and Cepheus onboarding.
- [ ] C.3 / M7 Landed sales/purchases/storage/repairs/blueprints/shipyard editing.
- [ ] C.4 / M7 Starter debt, approved loans, interval interest, pressure, forced contracts, warnings, payoff/independent unlock.
- [ ] C.5 / M7 Ship-loss recovery, persistent station service availability.
- [ ] C.6 / M8 Local builds and Steam/cloud requirements.
- [ ] C.GATE Fresh game supports several mining trips, meaningful upgrades, save/resume and debt freedom without developer commands.

## Phase D — field work, trade, automation, piracy

- [ ] D.1 / M9 Boosters/EVA/boarding, misc storage, welding, repair kits; pressure units/sealing/breaches/oxygen life support.
- [ ] D.2 / M9 Organizer, assigned-material collection drones, planned cutting routes, repair automation.
- [ ] D.3 / M10 Articulated tools, personal upgrades, weapons, defending crews.
- [ ] D.4 / M10 Temporal traders/salvagers/shipments, rare stock, authored fragments; supply/demand, hauling equipment/transactions.
- [ ] D.5 / M10 Theft, active-ship salvage, boarding combat/capture; station warnings/defense/NPC loss/alternatives.
- [ ] D.GATE Trade and hostile encounter, boarding/capture, active encounter save/resume; expiry preserves durable outcomes.

## Phase E — alien discovery and integration

- [ ] E.1 / M11 Alien artifacts/resources/equipment/material-dependent blueprints across careers.
- [ ] E.2 / M11 Discovery thresholds, home reactions, government recruitment/refusal; authored Cepheus freedom/purpose/exploration/discovery conversations.
- [ ] E.3 / M11 Autonomous drone hostility escalates with discovery/gear independently of recruitment.
- [ ] E.4 / M11 Dark industrial audio, generated visuals, accessibility, replaceable soundtrack assets.
- [ ] E.5 / M11 Destroyed/abandoned home progression, full regression, performance/save-growth, executed platform checks.
- [ ] E.GATE Playable contractor → freedom → alien-drone escalation with open continuation.

## Verification and resumption discipline

Authoritative gameplay is independent of scene objects, through commands/sessions for controls, unit inventories, transactions, encounters, calendar, and narrative. Use persistent identities and atomic physical/inventory transfers.

Verify conservation/non-overlap/exhaustion/rotating cavities; pumping/spilling/welding/sales/loans/capture; interrupted saves/revisits/encounter resume/expiry; debt freedom/recruitment/refusal/drones/destroyed-hub alternatives. Report Windows/Linux only when executed there.

For each coherent feature batch: run checks proportionate to the change under AGENTS.md, update the owning checklist and concise STATUS, preserve one canonical evidence result, explicitly stage intended files, commit and push. Run the fast suite once for a stable shared-code batch; build and run the player when GPU/runtime integration needs it. Repeat passing checks only for a relevant change, failure or unresolved concern. Markdown-only work uses local consistency/link checks, not Unity. Slow scale fixtures remain explicit and separate. `[x]` means verified; code without verification stays unchecked. Diagnose failing gates before advancement. Document external blocks and continue independent work. Stop only for unavailable authority/tooling or material new product decisions.

Resume from AGENTS.md, STATUS and the current block of this file. Read PROJECT_PLAN once per fresh context; open only the relevant roadmap/subsystem sections afterward. STATUS records last checkpoint, exact next task, working commands, and blockers. `git log -1` is the authoritative current commit.
