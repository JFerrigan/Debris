# Progression, Calendar, and the Expanding Frontier

## Calendar

The world has a single ever-increasing numerical time value, called the **Frontier Count** in player-facing UI. It is not divided into Earth-like days or years. Strategic travel advances it according to traveled distance and travel conditions; ordinary close-up work, menus and offline time never advance it. Interest uses recurring Count intervals. The save stores the authoritative count and all time-derived schedule/market/encounter state.

The Frontier Count drives market refreshes, loan interest, forced-contract/repo pressure, shipments, temporal encounter routes/expiry, and later narrative thresholds. It must remain deterministic and legible: the UI shows current count, travel advance, and the next known deadline/interest change when relevant.

## Endless progression field

Strategic space is endless, without an endgame border. Distance from home is a progression signal, not a procedural wall: farther regions trend toward harder sites, more dangerous encounters, tougher defenders, rarer resources, and more valuable opportunities. Site generation uses distance bands plus local variation so the game does not become a perfectly predictable gradient.

Planets are strategic locations with specific landable/dockable station contacts, not universally landable surfaces. A planet may also expose separately entered, persistent mining areas. These follow the normal station/site transition contracts.

## Story and career milestones

1. **Contractor start:** learn mining/salvage, take small EE Inc. jobs, sell cargo, and upgrade the starter ship.
2. **Freedom milestone:** pay the EE Inc. debt in full. This is the first major story completion and unlocks independent hauling, piracy, restricted salvage, broader jobs, and freedom to tune the ship toward a chosen career.
3. **Open frontier:** pursue mining, commodity hauling, independent salvage, piracy, or exploration. Alien artifacts, resources, functioning devices, and blueprints emerge through multiple jobs/sites rather than one mandatory route.
4. **Alien threshold:** accumulated alien discoveries trigger government attention. Authorities share classified context and offer recruitment; the player may join or refuse.
5. **Escalation:** alien autonomous drones begin rare attacks, then appear more often as alien gear/discovery increases. They attack whether or not the player accepted recruitment.
6. **Unplanned resolution:** the central late-game question remains open: fight, investigate leaders, find the source, survive, or discover another response. Do not prematurely promise a final ending.

## Arcturus and Cepheus

Cepheus is Arcturus’s one enduring companion: a simpler support AI running on the same hardware, with no separate sprite or physical body. Cepheus is present primarily through text/radio-like communication. Early on, he guides the player through core tasks; later he can echo, challenge, or push Arcturus’s thinking about freedom, creativity, sentience, purpose, work, and alien discovery.

After debt freedom, the lack of externally assigned purpose becomes a deliberate character thread. Odd jobs, mining, cargo work, piracy, and exploration can coexist with increasingly focused alien inquiry. Cepheus must remain a relationship, not a quest marker or omniscient answer machine.

## Narrative information boundaries

The luminous material remains a discoverable resource rather than a single mandatory central revelation. Alien materials/artifacts are the later narrative driver. Home-station people and leaders gradually become interested as the player pushes farther than their local station-bound lives; government contact begins only after the alien-discovery threshold. The alien drones’ hostile intention—clearing the frontier population—becomes an unavoidable escalating fact once the plot reaches that stage.

## Implementation rules

- The calendar has stable integer/fixed-point units, never system-clock time.
- Distance progression influences generation weights and encounter tables; it never invalidates a previously generated persistent site.
- Temporal encounters save active session state for exact resume, then retain only durable consequences after resolution/expiry; see `docs/FIELD_OPERATIONS_AND_COMBAT.md`.
- Alien discovery, government recruitment, drone escalation, EE Inc. debt freedom, and Cepheus conversation stages are explicit versioned narrative flags, not inferred from fragile text content.

## Implementation checklist

IDs refer to [EXECUTION_PLAN](EXECUTION_PLAN.md); only verified work is checked.

- [ ] C.1 Count/distance.
- [ ] C.4 Debt freedom.
- [ ] D.GATE Encounter resume/expiry.
- [ ] E.2 Discovery/recruitment.
- [ ] E.3 Drones.
