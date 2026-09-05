# Debris — Game Design

## Product premise

Debris is a 2D space-salvage and mining game. A salvager leaves a colony, explores an effectively unbounded strategic space map, chooses unknown contacts, and enters close-up sites where asteroids, wrecks, and ships are made of physical material pixels. The core loop is **find → dismantle → collect real material → return → sell, store, or build → improve → reach farther**.

The first playable focus is asteroid mining. The player pilots a small industrial starter ship, cuts an asteroid, gathers the released material into an actual cargo cavity, returns home, and can revisit the asteroid in its altered state.

The opening should establish the player as a working salvager through brief in-world text dispatches: a few practical pointers, the job context, and a first assignment before free exploration. It should feel like receiving work instructions, not an oversized tutorial system. Exact writing, UI treatment, and onboarding pacing remain open.

## Design pillars

### Physical matter

Asteroids, wrecks, and ship structure are material fields, not decorative sprites. A material is identifiable when inspected and has visual, physical, and economic properties. The material catalog is extensible; early examples are rock, iron, copper, ice, carbonaceous matter, steel, composites, rare metals, and exotic materials.

**One material pixel is one physical unit everywhere.** Its size never changes between an asteroid, a ship hull, a cut-off loose fragment, and cargo storage. Material pixels cannot overlap. Cargo capacity is the literal visible unoccupied volume inside the ship: as material is collected it occupies those same-size cells, and a larger physical cargo cavity carries more because the player can see more cells fit inside it. There are no invisible inventory stacks that compress, resize, or exceed the displayed cavity.

Material cells are square simulation cells rendered as crisp pixels; they are not literal display pixels. The initial ship is approximately 100 cells long, with an approximately 50×50-cell cargo cavity. The architecture must grow to ships around 1000×1000 cells and beyond through chunking and streaming.

Common resources are readable and restrained. Rare resources use controlled palette, emissive, animation, and particles without turning the field into visual noise.

### Physical ships

Ships combine material structure with discrete functional components. Hull, braces, armor, cavities, and walls use the same material representation that can be salvaged. Components such as drills, thrusters, cargo doors, batteries, tanks, scanners, and suction assemblies are data-driven entities connected to structure. Prebuilt and future player-built ships share this representation wherever practical.

Components are premade functional machines and need not be rectangular. Large functional equipment—propulsion, doors, drills, saws, and collection tools—occupies genuine physical space. Some components can be embedded in structure; non-spatial upgrades sit on top of existing pixel structure without claiming cargo/hull volume. Operation can depend on structural support, power, fuel, wiring/connection, and proximity as defined by that component.

Power is deliberately simple at the player-facing level: a component is powered when it is intact and connected to the ship's continuous hull/support network, provided the ship has an operating power source. Players do not route individual cables or manage cell-level batteries. The underlying component graph remains explicit for damage and future content, but normal ship construction reads as “attach it to the hull.”

Cavities have an atmosphere state. An enclosed, sealed cavity may retain gas; a hole or open door connects it to vacuum and depressurizes it. Arcturus does not need oxygen, but atmosphere matters for oxygen supplies, fire, pressure-sensitive equipment, and living organisms. This state is part of the simulated world from the outset, even where its gameplay consequences arrive later.

### Industrial salvage

Mining is spatial: approach a target, use a front cutting tool, create loose material, maneuver, and collect it through a rear cargo opening. Cargo is physically bounded by a cavity and has no overlapping occupancy; it is not a direct “mine into inventory” abstraction. Later equipment can change collection techniques without replacing the core material/cargo model.

The initial ship deliberately makes collection industrial and positional: its cutter/drill is mounted forward; the cargo cavity occupies the middle; the door opens from the rear; and upper/lower rear-facing thrusters propel it. The player can back the ship toward loose material, use suction to pull it across the open aperture, then close the door before travelling. Cargo cells remain individual physical cells inside the cavity: in zero gravity they drift, collide, settle, and tumble under inertia instead of snapping into an organizing grid. Opening the door can spill cargo during thrust, turns, collision, or deliberate maneuvers. A later robotic organizer may arrange cargo, but only within the same visible fixed volume.

Flight is accessible arcade-style 2D thrust: the player can thrust and rotate in any direction. It still has enough inertia for cargo movement, spills, impacts, and momentum to matter. Cargo mass affects acceleration, handling, turning, and fuel use.

The starter drill breaks individual cells directly ahead of its fixed mount; tool power determines break rate against material durability. Suction likewise acts from its mounted intake. Neither freely aims away from the ship. Articulated arms are later physical components: a player may mount a tool at a controllable arm end and steer that articulation independently, trading footprint, cost, and vulnerability for reach and precision. A circular saw is a larger-area cutting tool, while a future laser acts along a narrow beam. Initial mining releases individual cells only. The data/simulation model must leave room for future connected loose chunks without making the initial rules depend on them.

### Two explicit scales

The strategic map holds stations, the player, contacts, discovery, distance, fuel, and travel. Visiting a target loads its close-up site simulation through a separate scene/loading transition. Distant contacts are not simulated at material-pixel resolution. During a site visit the strategic ship is anchored at that contact and strategic travel/time is paused; the active site simulation alone advances. On departure, the persisted ship state is returned to the same strategic contact. This is the initial assumption, chosen to avoid simulating an entire universe while salvaging and may later be revisited if systemic strategic time is needed.

### Persistent consequences

A site is generated deterministically from world seed + site ID. Changes become sparse persisted deltas: removed material, changed chunks, extracted components, and persistent loose debris/cargo-relevant state. Every loose material cell and detached hull section persists for revisits; dust may have a visual representation but cannot be used as a reason to erase authoritative salvaged material. Returning must reconstruct the site as it was left. There is no designed player-facing cap on sites; the save architecture targets at least 100,000 indexed, modified sites and must degrade through streaming/storage pressure gracefully rather than deleting history.

## Initial vertical slice

Strategic view: home marker, starter ship, fuel-limited movement, seeded UNKNOWN OBJECT contacts, asteroid selection, and entry.

Salvage view: one generated multi-material asteroid, material hover inspection, ship propulsion, a front cutter, released loose material, a rear cargo cavity and animated door, suction, cargo transfer, exit, home storage/sale, and revisiting the modified asteroid.

## Content model

Materials, components, tools, ship blueprints, construction recipes, asteroid profiles, stations, and scanner signatures are data assets. Ships may be prebuilt or player-constructed; both are material-pixel structures populated with functional components, with prebuilt ships exercising the same underlying rules wherever practical. Components can support cutting, destruction, collection, propulsion, power, scanning, storage, and other future functions; the production catalog is intentionally not fixed yet.

The economy supports selling, buying, and storing physical commodities. Materials and money can buy components and blueprints; blueprints can permit manufacture from appropriate material. Construction occurs at shipyards and combines direct material-pixel drawing, drawing tools, prebuilt structural sections (such as cargo modules), and placement of premade components. Field repairs are possible through manual patches and later robotic-repair upgrades; full ship construction is not.

The company landing area at home is a physical hangar and company service counter: land, unload/sell cargo, buy approved cargo/components, and use a ship-customization station. Beyond it is a walkable station hub of independent shops, people, robots, and ships. Other stations and planets each have local inventory, supply, demand, and prices, creating a legitimate commodity-hauling path. It needs specialized equipment and substantial cargo volume to overcome fuel cost; early progression instead takes several mining trips and better target selection, not one lucky asteroid.

Arcturus begins with company debt for the starter ship. The company can extend bounded, approval-gated credit for components, with interest and increasing operational pressure. Debt buys reach at the cost of freedom: indebted contractors are limited to approved mining and basic contract work, and cannot take piracy, independent hauling, ancient-remains, or alien-ship salvage work. Repaying the balance makes Arcturus a free agent—a major progression and narrative turning point. See `docs/ECONOMY_AND_LOGISTICS.md`.

## Exploration and operating constraints

Strategic space permits travel indefinitely in every direction, but practical range is limited by current propulsion technology, fuel, ship/cargo mass, and return distance. Home remains clearly identifiable and the UI should communicate rising return risk before a player is stranded. Running low on fuel is a recovery problem rather than automatic failure: jettisoning valuable physical cargo to reduce mass can be a meaningful emergency choice.

Fuel is a physical stored material with multiple grades. Higher-grade propellant carries more range per visible stored volume and costs more. Tank damage or jettison creates a persistent recoverable spill; recovery requires suitable tools/equipment. Fuel does not ignite in vacuum—combustion needs oxygen—though fuel, oxygen, and fire may matter inside pressurized cavities later. Hull and component damage are real: a breached drill stops it working, broken power systems or anchors disable dependent equipment, and the player must patch, repair, adapt, or suffer the consequences.

If Arcturus is destroyed or stranded beyond repair, the company recovery contract returns the robot/operating shell to the home hub and restores a workable ship path. It does not recover cargo: all cargo at the loss site is forfeited and remains physically in the world. This is a severe economic setback, not a permanent character death or erased save.

Early scanners report targets as **UNKNOWN OBJECT**. Scanner upgrades may classify contacts as asteroids, ships, wrecks, or other categories and later expose useful signatures such as composition, size, danger, or value. Navigation combines a map, player waypoints and breadcrumb trails, optional autopilot route following, scanner range/signature confidence, and local hazards. It should make planning and risk assessment engaging without taking away manual flight.

## Visual direction

Modern pixel simulation at a small, crisp pixel scale, using `Reference/noita.png` as a scale/readability reference rather than a style template: dense material palettes, sharp edges, controlled shade variation, atmospheric space contrast, emissive rare resources, sparks, dust, attractive thrust, polished UI, and restrained bloom. Arcturus is smaller on screen than Noita's player reference, while ships and environments can use similarly dense cells. The same functional material-cell simulation builds asteroids, ships, home station, and planets; the hub is therefore destructible by collision and tools, not a painted exception. Effects decorate authoritative simulation data; they never replace it.

The tone is lonely, dark industrial science fiction. The player is Scavenger Arcturus B-2328, a sentient robot subcontractor with constrained freedom. Narrative foundations and the discoverable luminous-material mystery are in `docs/NARRATIVE_FOUNDATIONS.md`; material/site content is in `docs/RESOURCE_AND_SITE_CONTENT.md`.

## Controls and camera

Keyboard/mouse is the initial supported control scheme, designed so controller support can be added without redesign. Controls remain compact and readable: a contextual tool bar selects active equipment rather than assigning a separate arbitrary binding to every system. Early material hover inspection reveals only name and value; scanner/database upgrades reveal tool requirements, hardness, composition, construction uses, and confidence. Salvage camera behavior and free-inspection specifics remain to be tested, but material hover inspection is always available.

Automation preserves the player as pilot and salvage planner. A cargo-organizer upgrade can make cells snap efficiently into valid cavity positions, presented as a small working cargo drone rather than a costly physical packing solver. Collection drones can retrieve player-assigned material classes to cargo. Cutting drones can execute precisely planned routes to open access or remove a designated chunk; they do not replace route planning, ship positioning, or the value of manual cutting. Social encounters occur face-to-face in the hub or over radio, wreck logs, and recovered ship transmissions, all through concise text-box conversations with mostly tonal nice/rude choices and occasional consequential choices.

## Open Design Questions

- Recovery-contract price/response time and exact rebuilt-ship condition after loss.
- Scanner progression and exact unknown-contact reveal rules.
- Exact market formula, loan rates/approval thresholds, and shipping economics.
- Factions, consequential dialogue branches, and late automation/drone depth.
- Full ship-editor UX and late-game progression.
- Opening-dispatch writing, tutorial pacing, and UI presentation.
- Field-repair limits and atmosphere/fire simulation depth.
- Narrative encounters, factions, moral choices, and revelation pacing.
