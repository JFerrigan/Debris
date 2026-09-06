# Component System

## Purpose

Components are premade functional machines placed on a physical material-cell ship or structure. They are not a monolithic inheritance hierarchy and are not decorative sprites. A component has stable identity, definition data, ports, physical placement rules, and mutable runtime state. Its behavior submits commands to simulation through explicit interfaces.

## Component definition

`ComponentDefinition` is a data asset with an immutable key and version. It defines:

- authoring footprint/mask, visual anchor points, and whether it is external, embedded, or an overlay upgrade;
- structural anchor requirements: required attachment cells, supported material tags, and minimum support strength;
- ports: power, fuel, data/control, mechanical, tool-output, intake/output, and future categories;
- operational requirements: power/fuel draw, connection state, cooldown, durability, heat/future limits;
- command behavior schema: drill volume/power, saw area, suction force, door aperture, thrust vector, scanner volume, and so on;
- construction costs, blueprint requirements, sale/salvage value, and authoring preview.

No component definition stores per-save mutable state. A `ComponentInstance` stores `ComponentId`, definition key/version, ship/fragment owner, local transform, port connections, health, damage state, and runtime settings.

## Physical categories

| Category | Examples | Physical rule |
|---|---|---|
| External | drill, saw, thruster, cargo door, grappler | occupies real outside/edge space and needs exposed anchor geometry |
| Embedded | battery, fuel tank, processor, cable junction | occupies or is enclosed by a valid internal structural footprint |
| Overlay upgrade | software/firmware/robotic-management upgrade | may attach to an existing eligible component/structure without consuming cargo volume |
| Structural prefab | prebuilt cargo section, girder, reinforced hull section | contributes ordinary real material cells and may include component sockets; can be cut/repaired cell by cell |

An overlay never overrides physical constraints of the component it improves. A robotic cargo organizer, for example, can apply organizing forces inside an existing cavity but cannot increase its visible volume.

Unit components—tools/weapons, propulsion jets, command center, fuel tank, and misc-storage—are atomic functional objects. They occupy genuine footprint but are not drawn apart into editable material pixels. They either remain physically whole with a damaged/disabled state that an appropriate repair kit may restore, or are destroyed into non-functional pieces. This is distinct from a structural prefab, whose material cells remain individually destructible.

## Operational state

Every instance resolves a compact state each fixed step:

```text
Intact → supported → connected → supplied → enabled → active
```

- **Intact:** component health and footprint are not destroyed.
- **Supported:** required structural anchors remain connected to valid support.
- **Connected:** required data/mechanical ports resolve to a valid network path.
- **Supplied:** power/fuel/resource requirements are available.
- **Enabled:** player/automation state permits operation.
- **Active:** it produces commands this step.

A state failure is inspectable and UI-readable: “drill: no power,” “upper thruster: anchor severed,” or “cargo door: control line unavailable.” Components never silently retain functionality after their physical conditions fail.

## Network model

The runtime owns sparse port graphs, not a global manager. A `ShipNetworkResolver` works per loaded ship/fragment and receives only topology changes: placed/removed component, severed structure, damaged cable, tank breach, or port connection change.

Initial implementation reserves three independently queryable networks:

1. **Power:** sources, storage, consumers, and optional priority.
2. **Fuel/resource:** tanks, pipes/ports, engines/tools, and breach/output locations.
3. **Control/data:** player controller, processors, door/tool commands, scanner, and future automation.

Power is abstracted for normal play: an intact component structurally connected to the continuous supported hull resolves as connected to ship power when an operating source exists. The resolver may maintain explicit ports/connection IDs internally for damage and content validation, but construction does not require the player to lay visible cables or individually manage power cells. Fuel and control follow the same simple connected-hull presentation unless a future component intentionally adds a local constraint.

## Command contract

Components do not mutate site textures or other component internals. When active, they emit bounded `SiteCommand`s such as:

| Component | Command |
|---|---|
| drill | forward damage volume with tool capability/power |
| circular saw | rotating/wider damage volume |
| laser | narrow line/beam damage query |
| thruster | ship force/torque plus fuel consumption |
| suction | directional force field and intake priority |
| cargo door | collision aperture state transition |
| repair tool | bounded repair/patch command subject to material inventory |
| organizer | cavity-local snap/organizing command without capacity change; may be represented by a visible cargo drone |
| collection drone | player-assigned material pickup/intake commands within its operating range |
| cutting drone | executes a player-authored cutting route; does not choose salvage goals autonomously |
| articulated arm | transforms a mounted tool output within its joint limits under player control |
| misc-storage unit | capacity-limited menu inventory for patch material, repair kits, and personal equipment; consumes ship footprint but not cargo cells |
| fuel tank | atomic tank inventory; accepts pumped recoverable loose fuel and supplies engines/tools |

The component system receives compact outcomes/events from the simulation—tool blocked by material gate, fuel transfer, anchor failure, cargo intake, damage—but does not poll cell fields directly.

## Placement and construction

Shipyards build ships from direct material-cell drawing, drawing tools, prebuilt structural modules, and component placement. The authoring validator rejects overlapping physical footprints, unsupported anchors, invalid cavity boundaries, unavailable ports, invalid material tags, and component overlap with cargo space. Field repair uses a constrained repair operation: manual material patches and later robotic repair equipment can restore cells/components, but cannot provide unrestricted full shipyard construction.

## Initial component set

The first vertical slice requires a player controller, upper/lower rear thrusters, fuel tank, power source/storage as needed, front drill, rear cargo door, suction unit, and cargo-cavity definition. These prove all important placement/anchor/command boundaries. Further components remain content expansion, not one-off code paths.

## Open design questions

- Exact player-facing wiring/cable and fuel-pipe visualization.
- Power priority, battery charge behavior, and generator/fuel conversion detail.
- Whether components can be repaired to partial effectiveness.
- Automation, drone, and processor programming UX.

## Implementation checklist

IDs refer to [EXECUTION_PLAN](EXECUTION_PLAN.md); only verified work is checked.

- [x] B.1 Whole units/structural prefabs.
- [ ] B.3 Support loss.
- [ ] D.1 Pressure/repair.
- [ ] D.3 Articulation/weapons.
