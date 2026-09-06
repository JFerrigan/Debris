# Input and Camera

## Principles

Keyboard/mouse ships first. Input is action-based, compact, contextual, and controller-ready; gameplay systems consume semantic actions rather than keyboard keys. A visible toolbar selects equipment instead of assigning every future component a permanent binding.

## Action set

| Action | Initial use |
|---|---|
| Flight thrust | arcade directional thrust in strategic and salvage flight |
| Rotate | ship rotation in any direction |
| Exit/return ship | place/recover Arcturus’s body at a valid ship boundary or external position |
| Personal movement/booster | walk in stations/interiors and move in zero-g vacuum |
| Aim/pointer | material hover inspection; controls an articulated tool arm when one is fitted |
| Tool primary/secondary | active tool behavior or alternate function |
| Interact | contact/site/station/context actions |
| Toolbar previous/next/select | choose equipped tool without key sprawl |
| Cargo door | deliberate open/close action with clear state feedback |
| Map/scanner | strategic context and contact information |
| Pause/menu | settings, save, accessibility |

The exact keyboard defaults, rebinding UX, and controller layout remain open. Fixed tools act directly along their mounted forward axis; they do not aim independently of ship rotation. An articulated-arm component adds a contextual arm-control mode that steers the tool mounted at its end. Unity Input System action assets are the source for initial mappings; Steam Input can later map the same action vocabulary.

When outside the ship, the same action vocabulary maps to Arcturus’s equipped personal tool, welding, interaction/boarding, and booster controls. Personal field operation is deferred until after the starter ship loop; its full scope is in `docs/FIELD_OPERATIONS_AND_COMBAT.md`.

## Camera modes

### Strategic

Zoomed-out camera communicates home, ship, contacts, travel direction, scanner information, and return-risk range. It needs smooth pan/zoom, clear unknown-contact selection, and a reliable home marker. Strategic space is effectively unbounded, so coordinates and camera origin shifting must avoid floating-point presentation issues.

### Salvage

Default camera follows the active ship with smoothing that preserves crisp cells. It supports zoom appropriate for fine cutting/material inspection and wider local-site awareness. The camera never determines simulation activation; the session’s streaming policy does.

Free inspection, temporary detached camera, and automatic cinematic framing are deferred. If introduced, they must retain a clear ship location, tool target, and return-to-control action.

## UI feedback required for physical systems

- Material hover: early name and value only; database/scanner upgrades progressively add tool requirement, hardness, composition, construction uses, and scanner confidence.
- Tool feedback: blocked material, insufficient power/fuel, overheated/future condition, no loose-cell capacity diagnostic in development.
- Cargo: visibly open/closed door, cavity fill/loose-cell motion, spill direction, mass/handling impact.
- Damage: disabled component reason, breached tank/cavity, lost power/fuel/control/support.
- Strategic range: distance from home, fuel/mass return estimate, and clear escalating risk.

## Accessibility and controller readiness

Support rebinding, hold/toggle alternatives for sustained tools, adjustable pointer/camera sensitivity, readable palette/outline alternatives, and scalable UI from the first usable interface. Controller support should use action sets such as Flight, Salvage, Strategic, and Menu rather than incompatible one-off controls.

## Implementation checklist

IDs refer to [EXECUTION_PLAN](EXECUTION_PLAN.md); only verified work is checked.

- [x] A.2 Semantic actions.
- [ ] C.2 Basic walking/ship exit.
- [ ] D.1 EVA/tools.
- [ ] E.4 Accessibility.

## Starter playable checkpoint

W/S forward/reverse thrust, A/D strafe, Q/E turn, left mouse mounted drill, right mouse suction, G rear cargo door, scroll zoom, Escape pause, R reset. Camera follows the ship. The HUD shows finite fuel energy, physical cargo count and actual door state. Controller/rebinding remains unverified.
