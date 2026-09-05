# Structural Simulation

## Goal

Ships, wrecks, stations, and large material constructions must react to cuts as actual constructions. Components lose function when their support is severed; isolated regions detach as physical fragments; the long-term system models support/stress failure rather than only cosmetic holes.

## Structural data

Fixed material cells carry material index, occupancy, damage, structural flags, and optional bond/support properties derived from their material definition. Structural materials expose values such as bond strength, density, and support classification. Components declare anchor-cell requirements; selected cells/components are designated structural cores where appropriate.

Structural state is evaluated in the local coordinates of a ship, site, or fragment. It never depends on renderer mesh vertices or Unity colliders as authoritative truth.

## Solver layers

1. **Material destruction:** tool damage removes individual cells only after its capability/durability checks succeed.
2. **Connectivity:** modified chunks identify whether cells remain connected through structural bonds to a core/support region.
3. **Anchor validity:** component anchors are checked against the current support map; invalid anchors disable/detach their component.
4. **Region extraction:** material regions disconnected from valid support become `FragmentInstance`s with their own chunked local field and transform.
5. **Stress propagation:** acceleration, thrust, collision, fuel/cargo mass, and component mass create local load across bonds.
6. **Fracture:** overloaded bonds break, returning to the connectivity pass until the dirty region stabilizes.

The first prototype implements layers 1–4. Layers 5–6 are mandatory later architecture, but are activated only after measured GPU budgets show their local solver is viable.

## Cores, support, and function

A player ship has core/support definitions rather than a magical indestructible root. A cell or component must maintain a valid structural path to its required core/support network to remain part of a functional ship. This combines core connection and material strength as required by design.

If a thruster loses anchors or support it stops producing thrust. If a drill loses its support/power/control path it stops. If a tank is breached, contained fuel cells become loose persistent cells. A cargo cavity breach can turn its boundary into an opening and spill cargo. The player can patch damage in the field, use later robotic repair upgrades, or operate with the consequences.

## Fragment instances

A detached connected region is not converted directly to inventory or discarded. It becomes a `FragmentInstance` with:

- stable `FragmentId`, source site/ship and generator/version metadata;
- transform, linear/angular velocity, mass, and broad-phase bounds;
- material chunk references/deltas and embedded component state;
- local support state and persistent loose-cell interaction boundary.

Fragments are spatially streamed and saved like mini-sites. Large fragments retain their material field; smaller fragments may be represented by a compact field record while inactive, but that representation is lossless. Further damage can split a fragment or convert cells to individual loose cells.

## Performance rules

- Damage marks chunk-local dirty rectangles plus one-cell neighbour borders.
- Connectivity/stress dispatches operate on dirty regions, not every cell of every loaded structure each frame.
- Cross-chunk support propagation uses an explicit border exchange and work queue.
- Very large failures can resolve across several fixed steps with a visible/diagnostic “structural evaluation” state; authoritative cell destruction is never dropped.
- CPU reads compact support/fragment/component events only. It never scans all site cells to find collapse regions.

## Validation

Deterministic fixtures cover: cutting a thin bridge, severing a thruster anchor, isolating a cargo-wall section, extracting a multi-chunk region, fragment save/reload, and stress fracture under known force. Diagnostics report dirty support regions, connectivity iterations, fragment count/cell count, and solver time.

## Open design questions

- Exact load equations and whether visible bending precedes fracture.
- Which materials act as structural support versus decorative/loose fill.
- How fragment-to-fragment collisions scale at late-game station size.
- Player-facing repair tools and safe temporary bracing mechanics.
