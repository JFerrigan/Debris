# Technical Roadmap and Documentation Plan

## Rule of use

Each milestone has a demonstrable outcome, owner modules, tests, and documents that must be updated before advancing. The roadmap is deliberately vertical: it proves the intended final architecture through the starter salvage loop instead of accumulating disconnected systems.

## Milestones

| Milestone | Implement | Verify | Documentation to update |
|---|---|---|---|
| M0 — project/editor baseline | Unity project settings, URP, asmdefs, Bootstrap/DevShowcase, input actions, diagnostics shell | editor opens; assemblies compile; empty showcase launches | `UNITY_INTEGRATION`, `STATUS` |
| M1 — deterministic content | IDs/RNG, content keys, material catalog, asteroid profile, coordinate/chunk helpers, validators | deterministic tests; catalog/profile validation | `ARCHITECTURE`, `MATERIAL_SYSTEM`, `RESOURCE_AND_SITE_CONTENT` |
| M2 — fixed material field | chunk pool, seeded asteroid upload, palette/emission field shader, hover readback | generated showcase and one-cell inspection | `GPU_SIMULATION`, `PERFORMANCE` |
| M3 — loose-cell core | GPU cell buffer, spawn accounting, occupancy/collision, render path, sleep/stream representation | cut cells reconcile with loose cells; no silent overflow | `IMPLEMENTATION_RESEARCH`, `PERSISTENCE`, `PERFORMANCE` |
| M4 — starter ship | blueprint authoring, free-drawn hull/structural prefabs/atomic units, force-driven flight/fuel and consistent mass accounting, drill, suction, cavity/door, physical cargo/spill | controlled cargo showcase | `SHIP_SYSTEM`, `COMPONENT_SYSTEM`, `UNITY_INTEGRATION`, `GAME_DESIGN` |
| M5 — damage/structure | component anchors, support dirty regions, detach events, manual patch data path | cutting a thruster disables it; fragments persist; isolated cells transfer negligible momentum to a heavy ship; piles/anchors behave correctly | `SHIP_SYSTEM`, `GPU_SIMULATION`, `PERSISTENCE` |
| M6 — persistence loop | changed chunks, loose-cell/fragment codec, atomic saves, site lifecycle | deterministic leave/revisit hash fixture | `PERSISTENCE`, `PERFORMANCE` |
| M7 — strategic/home loop | Frontier Count, contacts, persistent/temporal encounter split, entry/exit transition, home landing/hub, basic Arcturus walking/exit/return, storage/sale/debt, fuel risk UI | full first-playable loop | `ARCHITECTURE`, `GAME_DESIGN`, `STATUS` |
| M8 — Steam readiness | reproducible macOS/Windows/Linux builds, SteamPipe scripts, cloud-save staging, input manifest preparation | private/beta install and cloud stress test on all targets | `IMPLEMENTATION_RESEARCH`, `UNITY_INTEGRATION`, `PERFORMANCE` |
| M9 — field operations | Advanced EVA/booster, welding, misc storage, boarding (basic hub walking/exit is M7) | leave ship, patch hull, return without inventory duplication | `FIELD_OPERATIONS_AND_COMBAT`, `INPUT_AND_CAMERA`, `PERSISTENCE` |
| M10 — careers and combat | temporal encounters, trade/boarding/capture contracts, late tools/weapons, station safety response | temporal expiry and durable outcomes; controlled boarding/combat fixture | `FIELD_OPERATIONS_AND_COMBAT`, `PROGRESSION_AND_TEMPORAL_WORLD`, `STRATEGIC_SYSTEM` |
| M11 — alien escalation | artifact threshold, government contact branch, drone encounter escalation, Cepheus stages | deterministic narrative-threshold and encounter-weight fixtures | `NARRATIVE_FOUNDATIONS`, `PROGRESSION_AND_TEMPORAL_WORLD`, `HOME_STATION_CHARACTERS` |

## Design documents and implementation triggers

| Future document | Trigger | Required contents |
|---|---|---|
| `docs/INPUT_AND_CAMERA.md` | M4 camera/control prototype | exact actions, toolbar behavior, pointer/tool aiming, controller mapping, accessibility |
| `docs/COMPONENT_SYSTEM.md` | M4 component catalog expands beyond starter ship | definition schema, ports, power/fuel/anchor/wiring contracts, upgrade categories |
| `docs/STRUCTURAL_SIMULATION.md` | M5 support/collapse work begins | support semantics, solver scope/budgets, detach conditions, fragment lifecycle, tests |
| `docs/SAVE_FORMAT.md` | M6 stable codec begins | binary/file layout, schema versions, hashes, atomic writes, migration and recovery |
| `docs/STRATEGIC_SYSTEM.md` | M7 strategic prototype begins | coordinate scale, contacts/scanners, range/fuel UI, transition contract |
| `docs/ECONOMY_AND_LOGISTICS.md` | M7 economy implementation | local markets, debt/freedom gates, recovery loss, transaction invariants, station storage, shipment cost/timing |
| `docs/HOME_STATION_CHARACTERS.md` | M7 hub/dialogue implementation | shop roles, relationship data, dialogue format, AI/company dispositions, progression gates |
| `docs/AUDIO_ARCHITECTURE.md` | M0 audio integration decision; M4 implementation | FMOD boundary, events/parameters, buses, snapshots, banks, accessibility, budgets |
| `docs/FIELD_OPERATIONS_AND_COMBAT.md` | M9 field-operation work begins | player body/equipment, misc inventory, repair, boarding, temporal piracy/combat, hub loss rules |
| `docs/PROGRESSION_AND_TEMPORAL_WORLD.md` | M7 Frontier Count work begins | calendar, distance bands, temporal expiry, milestone flags, alien escalation |
| `docs/STEAM_RELEASE.md` | M8 Steamworks setup begins | AppID handling, depot/branch process, Cloud quotas/conflicts, Steam Input, release checklist |
| `docs/CONTENT_AUTHORING_GUIDE.md` | content creation becomes recurring | material/site/ship authoring, validators, previews, review checklist |
| `docs/NARRATIVE_DELIVERY.md` | encounter/log implementation begins | spoiler levels, dispatch/log formats, lore data keys, localization, content boundaries |

These documents are now established as architecture/design contracts. Their implementation sections remain inactive until the listed trigger milestone begins. Do not create additional large speculative mechanics documents before their trigger; use existing `Open Design Questions` to preserve ideas without pretending they are decisions.

## Test plan by layer

- **EditMode:** deterministic generation, material/tool gates, chunk coordinates, blueprint/cavity validation, save codecs, content keys, economy transaction invariants.
- **Compute fixture tests:** known small field inputs and compact result hashes for cutting, occupancy, suction, cargo-door spills, and support masks. Tolerances/versioning are documented with each kernel.
- **PlayMode:** Bootstrap/Strategic/Salvage/Hub transitions, starter ship flight, damaged component state, return-to-home transaction, leave/revisit state reconstruction, player exit/return, and temporal-encounter expiry.
- **Visual/manual:** deterministic DevShowcase camera presets for material readability, rare-material emission, cargo movement, sparks, UI, and large-site composition.
- **Stress:** max configured active chunks, sustained cutting, full cargo, rapid door cycling, large detachment, long site traversal, save/Steam Cloud candidate size, and a 100,000-site indexed-save fixture.

## Required diagnostic counters

Every performance/stress result records frame time percentiles; active, visible, and sleeping chunks; fixed and loose cell counts; loose-cell capacity/overflow attempts; dirty chunks; structural work iterations; GPU dispatches and readback queue; draw calls; loaded site count; save size and encode time. This is the evidence needed to decide whether to increase simulation scope.

## Immediate next work

1. B.3R.1–B.3R.2: establish finite body mass/inertia and the analytical single-cell impulse case, then replace GPU/CPU hard-stop paths with force-driven ship motion and momentum exchange.
2. B.3R.3–B.3R.4: verify dense piles, free cargo without double-counted mass, fragment torque, anchors, save/resume and the player scenario. Detailed criteria: [CONTACT_PHYSICS](CONTACT_PHYSICS.md).
3. Resume B.5 large masks/streaming and explicit scale validation afterward. The pending 100,000-site run failed the runner timeout despite internal success logs; fix its runtime budget and large-world maintenance scheduling before claiming that gate.
4. Follow [AGENTS.md](../AGENTS.md): focused tests during iteration, applicable regression/build/player checks once the feature batch is stable. No engine launch for documentation-only changes.

## Implementation checklist

IDs refer to [EXECUTION_PLAN](EXECUTION_PLAN.md); only verified work is checked.

- [x] A.GATE Engine proof.
- [ ] B.GATE Physical salvage.
- [ ] C.GATE Contractor career.
- [ ] D.GATE Field careers.
- [ ] E.GATE Alien escalation.
