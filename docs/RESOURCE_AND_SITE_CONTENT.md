# Resources and Site Content

## Purpose

Resources are modular data content, not hard-coded enumerations. Each material can define its look, physical behavior, tool requirements, economic value, construction uses, likely locations, and lore-facing presentation. The catalog will grow substantially; this document establishes the authoring vocabulary without prematurely filling the game with final resources.

## Material definition

Every `MaterialDefinition` has a stable key and includes, where applicable:

- palette, shade variation, emissive behavior, scan/inspection name, and visual rarity treatment;
- density, durability/hardness, thermal/fuel/fluid/oxidizer tags, value, and physical state;
- minimum tool capabilities and break-rate response by tool type;
- valid construction/recipe uses and component compatibility;
- generation tags/weights for asteroid, wreck, station, cargo, planet, and anomalous sites;
- optional lore/flavor-text keys, discovery level, and scanner signature.

Materials primarily differentiate through value, uses, durability, and tool requirements. A weak starting drill cannot break every material. Advanced ship hulls and late-game materials require progressively capable tools. Material physics may expand later, but individual cells always retain universal fixed volume and cannot overlap.

## Initial material families

These are families and examples, not a final locked list:

| Family | Examples | Early role |
|---|---|---|
| Common industrial | iron, silicon, carbonaceous material, steel | familiar income and construction inputs |
| Volatile/utility | ice, low/standard/dense propellant, oxygen supplies | useful cargo; fuel grades trade price against range per visible volume, can leak/spill, and require recovery equipment; fuel cannot combust in vacuum |
| Advanced manufactured | composite hull material, ionic resin | stronger/valuable ship and machinery inputs requiring better tools |
| Rare/exotic | arcanium and later invented materials | distinctive high-value or high-tech material, stronger palette/emission treatment |
| Unidentified luminous material | unrevealed story resource | blue, bright/glowing, collectible advanced-robotics input; its true human-soul origin is discoverable narrative information, not an early UI label |

Rare materials should be striking but controlled: unusual saturation/palette, subtle animated emission, sparkle, and scan treatment—never indiscriminate bloom or visual clutter.

## Site taxonomy

Sites are generated from a profile plus deterministic seed and stable site ID. Profiles define material bands, geometry rules, embedded content, contact/scanner data, and later danger rules. They do not dictate player outcomes.

| Site family | Typical content | Intended progression |
|---|---|---|
| Compact asteroid | common rock with material veins | early mining foundation |
| Variant asteroid | ice pockets, hollows, fuel-bearing pockets, embedded machinery, or rare veins | occasional discovery; hazards deferred for now |
| Crash landing | mixed wreck material, loose recoverable cargo, components, fuel, and clues | early bridge from rocks to wrecks |
| Scavenger wreck | industrial components and its cargo | mid-tier salvage |
| Bounty-hunter/military wreck | advanced structure, intact components, stronger materials, potential tech | higher tool and discovery gate |
| Freighter wreck | large-volume cargo, heavy concentration of one material, logistics value | large-scale collection challenge |
| Space station/industrial structure | very large mixed material field, many systems and compartments | late-game, broad salvage variety |
| Alien/anomalous structure | extremely rare invented materials and end-game technology | late-game mystery and discovery |

Asteroids are initially mostly compact rock with veins. They can occasionally contain ice, hollows, embedded components, or fuel-bearing material. Cavities reserve an explicit vacuum/pressurized state from the outset. Oxygen, fire, pressure effects, and life forms are later content/simulation layers; Arcturus does not require oxygen, but living occupants and some equipment do.

## Scale

Sites are not limited to the starter ship’s dimensions. Asteroids and ships can be much larger, and stations larger still. Chunk streaming, active-radius simulation, GPU resource pools, and persistence must support sites whose visible structure exceeds a 1000×1000-cell player ship without whole-site active simulation.

## Authoring workflow

New resources are added through MaterialDefinition assets and catalog validation, not switch statements. New site types are profile assets plus generation modules. The editor should preview palettes, emissive treatment, density/durability/tool gates, weighted material distributions, and deterministic seeded examples before content is accepted.

## Open content questions

- Exact recipes and material-to-component requirements.
- Exact names/properties of fictional materials beyond the initial examples.
- Which site types introduce particular scanner tiers, blueprints, and upgrades.
- Exact fuel-grade names, energy/range values, and recovery-tool progression.
- Fire/oxygen/pressure simulation depth and their relationship to site type.
