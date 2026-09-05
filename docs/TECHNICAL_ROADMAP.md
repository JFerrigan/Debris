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
| M4 — starter ship | blueprint authoring, arcade flight/fuel mass, drill, suction, cavity/door, physical cargo/spill | controlled cargo showcase | `SHIP_SYSTEM`, `UNITY_INTEGRATION`, `GAME_DESIGN` |
| M5 — damage/structure | component anchors, support dirty regions, detach events, manual patch data path | cutting a thruster disables it; fragment persists | `SHIP_SYSTEM`, `GPU_SIMULATION`, `PERSISTENCE` |
| M6 — persistence loop | changed chunks, loose-cell/fragment codec, atomic saves, site lifecycle | deterministic leave/revisit hash fixture | `PERSISTENCE`, `PERFORMANCE` |
| M7 — strategic/home loop | contacts, entry/exit transition, home storage/sale, fuel risk UI | full first-playable loop | `ARCHITECTURE`, `GAME_DESIGN`, `STATUS` |
| M8 — Steam readiness | reproducible builds, SteamPipe scripts, cloud-save staging, input manifest preparation | private/beta install and cloud stress test | `IMPLEMENTATION_RESEARCH`, `UNITY_INTEGRATION`, `PERFORMANCE` |

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
| `docs/STEAM_RELEASE.md` | M8 Steamworks setup begins | AppID handling, depot/branch process, Cloud quotas/conflicts, Steam Input, release checklist |
| `docs/CONTENT_AUTHORING_GUIDE.md` | content creation becomes recurring | material/site/ship authoring, validators, previews, review checklist |
| `docs/NARRATIVE_DELIVERY.md` | encounter/log implementation begins | spoiler levels, dispatch/log formats, lore data keys, localization, content boundaries |

These documents are now established as architecture/design contracts. Their implementation sections remain inactive until the listed trigger milestone begins. Do not create additional large speculative mechanics documents before their trigger; use existing `Open Design Questions` to preserve ideas without pretending they are decisions.

## Test plan by layer

- **EditMode:** deterministic generation, material/tool gates, chunk coordinates, blueprint/cavity validation, save codecs, content keys, economy transaction invariants.
- **Compute fixture tests:** known small field inputs and compact result hashes for cutting, occupancy, suction, cargo-door spills, and support masks. Tolerances/versioning are documented with each kernel.
- **PlayMode:** Bootstrap/Strategic/Salvage transitions, starter ship flight, damaged component state, return-to-home transaction, leave/revisit state reconstruction.
- **Visual/manual:** deterministic DevShowcase camera presets for material readability, rare-material emission, cargo movement, sparks, UI, and large-site composition.
- **Stress:** max configured active chunks, sustained cutting, full cargo, rapid door cycling, large detachment, long site traversal, save/Steam Cloud candidate size, and a 100,000-site indexed-save fixture.

## Required diagnostic counters

Every performance/stress result records frame time percentiles; active, visible, and sleeping chunks; fixed and loose cell counts; loose-cell capacity/overflow attempts; dirty chunks; structural work iterations; GPU dispatches and readback queue; draw calls; loaded site count; save size and encode time. This is the evidence needed to decide whether to increase simulation scope.

## Immediate next work

1. Install/open the selected Unity editor and let packages resolve.
2. Finish M0 with source-controlled settings, base scenes, input actions, and diagnostics shell.
3. Complete M1 tests/content assets and baseline deterministic generated asteroid preview.
4. Begin M2 only after the editor can run the DevShowcase; do not claim GPU or visual performance before then.
