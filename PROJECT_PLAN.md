# Debris — Product and Execution Plan

## Purpose

This is the top-level entry point for making Debris. It states what the game is, the order in which it is built, what proves each phase is complete, and what must not expand before its foundation works. Detailed rules live in the linked documents; this file resolves their order.

## Product in one paragraph

Debris is a 2D industrial space-salvage game in which Scavenger Arcturus B-2328 pilots and eventually leaves a physically destructible pixel-material ship, mines and salvages persistent sites, carries real loose-cell cargo, returns to a living hub, pays off EE Inc. debt, and becomes free to haul, explore, pirate, or pursue alien discoveries. The strategic frontier is endless, time advances through the numerical Frontier Count, and the same material simulation underpins asteroids, ships, stations, and later combat.

## Non-negotiable product contracts

- One material cell has the same fixed volume everywhere; cargo is visible, physical, non-overlapping cells in a real cavity.
- Persistent sites reconstruct exactly after revisits. There is no player-facing site limit; design capacity is at least 100,000 indexed modified sites.
- Fixed hull/terrain is chunked material-field data; loose matter is bounded GPU data. No cell is a GameObject or Rigidbody.
- Components have real footprint and support requirements. Whole unit components fail as units; structural prefabs and free-drawn hull remain cell-destructible.
- The player sees simple hull-connected power, not cable-routing busywork.
- Arcturus can operate personally in vacuum with a booster; they do not need oxygen. Pressurized spaces matter for other life, fire, and equipment.
- Ship loss is recoverable through EE Inc., but cargo is forfeited. Stations, including home, can be permanently damaged or made unusable.
- Unity is the platform/orchestration layer; the bespoke simulation remains isolated within it. Target macOS first, then Windows and Linux.

Authoritative detail: [Game Design](GAME_DESIGN.md), [Architecture](ARCHITECTURE.md), [Field Operations](docs/FIELD_OPERATIONS_AND_COMBAT.md), and [Progression](docs/PROGRESSION_AND_TEMPORAL_WORLD.md).

## Delivery order

### Phase A — foundation and proof of representation

**Milestones M0–M3.** Establish Unity/URP project structure, deterministic IDs/content, chunk coordinates, material catalog, generated asteroid field, rendering, inspection, and a bounded loose-cell GPU pipeline.

**Gate:** a deterministic debug scene renders an asteroid, removes cells through a command, accounts for every released loose cell without overflow, and reports frame/dispatch/buffer metrics. No cargo UI, economy, combat, or open-world content is added before this works.

### Phase B — the physical salvage loop

**Milestones M4–M6.** Build the starter ship from blueprint data; prove flight, fuel, mounted drill, suction, cargo door/cavity, loose-cell cargo, structural support failure, and lossless leave/revisit saving.

**Gate:** the player can cut an asteroid, collect physical cargo, spill it, leave, load, and return to the same altered site with matching authoritative state. This is the first internal playable.

### Phase C — contractor loop and home hub

**Milestones M7–M8.** Add strategic navigation, Frontier Count, contacts, home landing/hub, company sales/storage/debt, starter loan pressure, and release/build plumbing for macOS, Windows, and Linux.

**Gate:** a new player can take an EE Inc. mining job, return, sell from the landed cargo menu, service debt, buy an approved upgrade, save, and continue. The experience should stand on its own before personal movement, combat, or alien story content expands.

### Phase D — field operations and career breadth

**Milestones M9–M10.** Add Arcturus exiting the ship, booster movement, welding and misc storage, hub walking, temporal encounters, trade, boarding/capture boundaries, late tools/weapons, and station security/consequence behavior.

**Gate:** Arcturus can leave the ship, repair a physical breach, return without duplication, and resolve a temporal encounter whose durable result survives while the encounter itself expires.

### Phase E — alien escalation and open-ended endgame

**Milestone M11 and beyond.** Add alien artifacts, discovery thresholds, Cepheus conversation stages, government recruitment choice, autonomous drone escalation, and the later response paths.

**Gate:** narrative triggers are data-driven, deterministic, and compatible with a player who has destroyed or abandoned the home hub. The final resolution is deliberately not designed yet.

## Build discipline

Each autonomous implementation run follows this sequence:

1. Start from a clean, committed design baseline.
2. Work only through the next unresolved phase gate—never silently jump to a later feature because it is interesting.
3. Make small coherent commits: project/configuration, deterministic data/tests, rendering/simulation, gameplay loop, persistence, and documentation. Each commit must build or have a clearly recorded environmental blocker.
4. Run the narrowest relevant tests and the matching deterministic showcase after each subsystem milestone.
5. Record actual measurements in `docs/PERFORMANCE.md`; do not replace benchmarks with estimates.
6. After each verified phase gate, commit/push, record evidence and next task, then continue automatically to the next authorized phase through Phase E.

The authorized execution contract is [Continuous Implementation](docs/EXECUTION_PLAN.md). It supersedes the former Phase-A-only stopping rule. Do not change product contracts or invent the unspecified final resolution.

Basic hub walking and ship exit/return are Phase C requirements; advanced EVA remains Phase D.

## Document map

| Need | Source |
|---|---|
| Player experience, world rules, visual direction | [GAME_DESIGN.md](GAME_DESIGN.md) |
| Module boundaries and authoritative representations | [ARCHITECTURE.md](ARCHITECTURE.md) |
| Milestones, tests, diagnostics | [Technical Roadmap](docs/TECHNICAL_ROADMAP.md) |
| GPU representation and benchmark risks | [GPU Simulation](docs/GPU_SIMULATION.md), [Implementation Research](docs/IMPLEMENTATION_RESEARCH.md) |
| Saves and 100,000-site target | [Persistence](docs/PERSISTENCE.md), [Save Format](docs/SAVE_FORMAT.md) |
| Ships, components, structures | [Ship System](docs/SHIP_SYSTEM.md), [Component System](docs/COMPONENT_SYSTEM.md) |
| Debt, market, recovery | [Economy and Logistics](docs/ECONOMY_AND_LOGISTICS.md) |
| Hub cast and dialogue | [Home Station Characters](docs/HOME_STATION_CHARACTERS.md) |
| Personal operations, combat, temporal contacts | [Field Operations and Combat](docs/FIELD_OPERATIONS_AND_COMBAT.md) |
| Frontier Count, story milestones, Cepheus, alien escalation | [Progression and Temporal World](docs/PROGRESSION_AND_TEMPORAL_WORLD.md) |
| Audio | [Audio Architecture](docs/AUDIO_ARCHITECTURE.md) |
