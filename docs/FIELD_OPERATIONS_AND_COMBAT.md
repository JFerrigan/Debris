# Field Operations and Late-Game Combat

## Scope

Arcturus can leave the ship at any time: in a station, at a persistent salvage site, in open space, or aboard another ship. As a robot, Arcturus needs no airlock and no oxygen. A basic booster pack provides zero-g movement; higher-tier personal equipment expands reach, maneuverability, durability, tool use, and combat capability. The player remains mostly solitary, but the option to leave the hull makes boarding, repairs, and station life physical rather than menu-only.

This is not part of the first mining vertical slice. Combat becomes a late-game pillar after the core salvage, construction, and debt-to-freedom loops are reliable. Its implementation must nevertheless reserve the identity, state, input, and transition boundaries now.

## Player equipment and repair

Arcturus has a capacity-limited **misc-storage unit** mounted in the ship. Its contents exist in a conventional menu inventory and do not occupy physical cargo-cavity cells. The storage unit itself occupies real ship footprint and can be damaged/detached. Misc storage carries small equipment and consumables: raw hull-patch material, repair kits, personal tools, booster upgrades, and future weapons.

Field repair has two intentionally different paths:

- A welding tool consumes raw patch material from misc storage to manually restore eligible hull/structural cells. It is spatial work and must reseal an actual breach.
- Repair kits restore premade functional units—drills, weapons, propulsion, tanks, command centers, and similar machines—when their whole-unit failure state permits repair. They do not recreate a unit destroyed beyond recovery.
- Later autonomous repair equipment may perform the same authorized repair operations, using the same inventory and material limits; it never grants unrestricted shipyard construction.

## Personal movement and interaction

The player controller supports walking in pressurized/station environments and booster movement in vacuum. It owns personal position, velocity, suit/robot condition, equipped tool, misc inventory, and boarding/interact state. It submits the same semantic tool/repair commands as ship components where practical. The personal body is a low-count actor, not a material cell; its collision samples the material field and its destruction cannot turn the simulation into per-cell GameObjects.

Boarding is a close-up site operation: breach or enter a valid opening, bring Arcturus inside, and interact with people, cargo, components, locks, and terminals. Crewed ships may defend themselves. Arcturus can also undertake external repairs or recover loose material without re-entering the ship.

## Combat and piracy

Late-game combat uses the same physical principles as salvage. Ship weapons/tools can damage hull cells and components; defenders use comparable ships, tools, weapons, boosters, and boarding behavior. Winning may mean destroying a ship, disabling it sufficiently to take cargo, boarding it to confront its captain/crew, or taking the entire ship. Combat must preserve clear non-lethal interaction options where content permits.

Piracy is a supported long-term career after freedom from EE Inc. It includes stealing cargo, salvaging still-active cargo vessels, taking ships, and attacking or negotiating with transient crews. It is a choice with faction, market, debt/history, and station-safety consequences; it is not an isolated combat arena.

## Station safety and destructibility

Every station—including home—is material-cell construction and can be damaged. The home hub will discourage violence through warning, security response, character reactions, and the practical cost of losing its services. If the player destroys critical home-station structure or permanently removes/key-disables essential traders, that hub can become unusable for trade, repair, and related services. It is not silently reset.

Other stations/planetary hubs follow the same rule. The world provides multiple trade locations, so losing one location is a lasting consequence rather than a hard save failure. Critical story progression must retain alternative contacts/routes or explicitly communicate the player-created loss.

## Temporal encounters

Persistent sites are saved indefinitely. **Temporal encounters** are explicitly different, non-permanent strategic contacts such as cargo shipments, independent salvagers, crews, traveling traders, and roaming shipkeepers. They can be hailed, traded with, boarded, attacked, robbed, or ignored. Their cargo and rare inventory may include valuables, blueprints, alien materials, and unusual gear.

Temporal encounters have generated identity, faction/crew profile, inventory, route, and expiry/despawn conditions. They are not entered into the permanent site-delta save index and do not promise revisit reconstruction after departure or calendar advancement. The player’s durable consequences still persist: gained/lost cargo, ship damage, money/debt, relationship/reputation changes, discovery flags, and any ship deliberately captured and made persistent.

## Validation boundaries

- Leaving/re-entering ship preserves the player and ship states without duplicating cargo or misc inventory.
- A welded cell restores only valid structure and consumes exactly one recorded patch resource.
- Repair kits restore only eligible damaged unit instances.
- Boarding/return transitions preserve actor, ship, and site ownership.
- A temporal encounter expires without adding a permanent site record; its durable transaction consequences survive save/load.
- Home-hub destruction disables the affected services and does not silently respawn essential NPCs.

## Implementation checklist

IDs refer to [EXECUTION_PLAN](EXECUTION_PLAN.md); only verified work is checked.

- [ ] C.2 Basic hub walking.
- [ ] D.1 EVA/repair/pressure.
- [ ] D.2 Automation.
- [ ] D.3 Weapons/crews.
- [ ] D.5 Piracy/capture/security.
