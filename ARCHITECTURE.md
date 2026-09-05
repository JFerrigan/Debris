# Debris — Architecture

## Scope and guiding decisions

Unity LTS/latest stable with URP. The authoritative close-up site is a chunked, texture-backed material field, with GPU compute owning high-volume per-pixel simulation. CPU code owns lifecycle, player/input, strategic state, saves, content data, coarse site queries, and low-frequency commands. No pixel is a GameObject or Rigidbody.

The first prototype is deliberately a narrow implementation of these production boundaries; it is not a disposable physics demo.

## Modules and ownership

| Module | Owns | Public boundary |
|---|---|---|
| `Core` | composition root, game state transitions, service interfaces | `IGameState`, events only |
| `World` | seed, persistent IDs, coordinate conversion | `WorldId`, `SiteId`, conversion APIs |
| `Strategic` | contacts, Frontier Count, arcade flight, physical fuel/cargo mass, station position | `IStrategicWorld`, `EnterSiteRequest` |
| `Sites` | loaded site lifecycle and chunk streaming | `ISiteSession`, commands, read-only snapshots |
| `Materials` | material definitions and packed GPU lookup table | `MaterialDefinition`, `MaterialCatalog` |
| `PixelSimulation` | material fields, damage, debris cells, active chunks | batched `SiteCommand`, sampled readback |
| `Rendering` | terrain/debris draw resources and camera culling | read-only GPU resources |
| `Effects` | GPU particles, semantic audio/effect cues | `EffectEvent`/`AudioCue` stream |
| `Audio` | FMOD bank lifecycle, cue playback, mix/snapshots, accessibility | `IAudioService`; presentation only |
| `Ships` | structure field, component placement, cargo topology | `ShipDefinition`, `ShipRuntime` |
| `Components` | component behavior/data | component command producers |
| `Player` | Arcturus body, zero-g booster, equipment, boarding, misc inventory | `IPlayerRuntime`, semantic field commands |
| `Encounters` | temporal contacts, crew/trader profiles, expiry, capture conversion | `IEncounterService`, durable outcome events |
| `Persistence` | world/save records and lossless site deltas | `ISaveRepository` |
| `Economy` | inventory, station storage, sale ledger | transactions/events |
| `UI` | HUD, inspection, menus | subscribes to snapshots/events |
| `Diagnostics` | counters, debug modes, stress fixtures | read-only metrics |

Dependencies point inward through contracts: UI/Input → Core; Core → interfaces; strategic/sites/ships → materials and world; rendering/effects only consume simulation outputs. Systems do not reach into another module’s buffers or private state.

## Coordinates, IDs, and units

Strategic coordinates use `double` kilometres relative to the world origin. A loaded site has a stable `SiteId` and a site-local, signed integer **cell** coordinate. One material cell is the canonical simulation unit: a square simulation cell rendered as a crisp pixel, not a literal display pixel. Initial presentation is 1 cell = 0.125 Unity metres (8 cells/metre), configurable in `SimulationSettings` rather than embedded in gameplay code. The starter ship targets roughly 100 cells in length with a 50×50 cargo cavity; chunking/streaming must accommodate 1000×1000-cell ships and larger. `ChunkCoord` uses floor division, including negative positions.

`WorldId`, `SiteId`, `ShipId`, `ComponentId`, `StationId`, `EncounterId`, and `ActorId` are stable 128-bit serialized values. The world additionally stores a fixed-point/integer **Frontier Count** rather than wall-clock days/years. Content assets use immutable string keys. Persistent generation derives independent deterministic streams from `(worldSeed, stableId, purposeKey)`; runtime visual randomness never uses Unity global random and never changes generated geometry.

## Site and pixel model

Each site streams fixed-size chunks (initial benchmark candidate: 128×128 cells, not a permanent promise). A chunk contains authoritative material/flags fields and transient simulation fields in GPU textures. Static generated chunks are reconstructed from the seed. A dirty chunk has a compact CPU-side persistence mirror only when needed for saving; this is updated via batched GPU readback on unload/save, never continuously.

Cells encode material index, occupancy/state flags, damage/heat channels, and optional variant seed. Sealed cavities additionally have a sparse compartment record (vacuum/pressurized, gas composition/pressure as needed) derived from boundary/topology changes; this is not a per-cell GameObject simulation. **A cell is the universal indivisible material unit:** fixed terrain, hull structure, loose debris, fuel, and cargo all use exactly one cell's volume and material mass. Occupancy is exclusive—two material cells cannot share a position. Fixed matter belongs to the structural/material field. Loose material initially simulates as individual GPU particles/cells with material ID, velocity, temperature, and site-local position. It can collide with coarse field samples and is eligible for suction/cargo transfer. The authoritative state of every loose cell and detached fragment persists; sleeping/compacted representations must be lossless and restore individual state, never despawn material. This distinction prevents cutting from requiring structural fields to move as one giant rigid body.

## Simulation and commands

The simulation runs fixed steps after input/ship control and before presentation. CPU systems emit bounded batched commands: cutter strokes, damage circles, suction volumes, cargo intake volumes, component state changes, and chunk activation. Compute kernels apply commands to active chunks, release loose matter, integrate loose matter, resolve local interactions, and mark dirty chunks. The GPU emits compact append-buffer events for effects and a limited inspection/readback request path.

Update order:

1. input sampled; state transitions and strategic movement update;
2. active site streaming/activation resolves;
3. ship components emit simulation commands;
4. fixed GPU site steps execute (possibly multiple capped steps);
5. cargo intake consumes qualifying loose material and emits transfer records;
6. CPU applies compact transfer/events, UI snapshots, and persistence dirty marks;
7. rendering/effects consume GPU outputs; diagnostics records timings.

CPU does not poll every pixel. Hover inspection is a one-cell asynchronous readback, cached and rate-limited. Collision starts as SDF/coarse occupancy queries generated per active chunk; precise expensive queries stay local to tools and ship contact points.

## Ships and cargo

A ship uses a site-local structural field and discrete component instances. Components have transform/ports, definition data, health, and connections to structural anchors. Whole-unit components (command centers, tanks, tools/weapons, jets, misc-storage) are atomic runtime entities: they can fail whole, be repaired with a kit, or be destroyed, but they are not editable cell fields. Structural prefabs (cargo bays, girders, hull modules) instantiate ordinary material cells and are therefore cuttable/repairable like free-drawn hull. The starter ship is authored from the same blueprint/field form planned for future construction. Cargo cavities are explicit hull-space collision volumes plus an intake aperture controlled by a door component. Their dimensions are immutable at runtime except through actual ship construction/destruction. Loose cargo cells enter through the open aperture and continue GPU physics inside this volume: they have individual position/velocity, collide without overlap, and can spill through an open door. Capacity is the physical available cavity volume, never an abstract stack. A later organizing component may influence these cells but cannot resize or bypass the cavity.

Components require structural anchors. Loss of a supporting connection disables or detaches a component; a severed thruster therefore removes its propulsion. For normal construction, intact components connected to the continuous supported hull receive abstract ship power from a valid source—no visible cable routing or per-cell power management. Structural connectivity is evaluated over the material field. Detached connected regions become loose physical ship fragments, and the long-term target includes stress/support failure and collapse rather than only simplistic attachment checks. The first slice establishes the connectivity data and detached-fragment event path, then expands stress simulation under measured GPU budgets.

## Persistence and serialization

World-level save stores player ship/body/misc-inventory state, Frontier Count, strategic contacts/discovery, station inventories, economy/debt ledger, relationships, and a paged site record index designed for at least 100,000 modified sites. Site save data stores generator revision/seed and only state that differs from deterministic generation: changed chunk payloads, component/atmosphere state, and every loose material/fuel cell and detached-fragment record. Temporal encounters are not site records: active encounters save temporary session snapshots for exact inventory/damage/expiry resume; resolved/expired encounters retain only durable outcomes, including captured ships converted to persistent ships. It does not serialize render textures. Details and versioning rules are in `docs/PERSISTENCE.md`.

## Threading and GPU ownership

Unity main thread owns Unity object lifecycle and public orchestration. Jobs/Burst may prepare generation, save compression, spatial indexes, and command packing; they never touch Unity GPU resources directly. Compute shaders own active chunk fields and loose-particle buffers during a frame. `AsyncGPUReadback` is used for chunk persistence on eviction/save, limited inspection samples, and diagnostics only. GPU resource lifetime is owned by `SiteSession` and released on session disposal.

## First vertical-slice implementation plan

1. Create the Unity URP project, assembly definitions, content folders, bootstrap scene, and data definitions.
2. Implement deterministic IDs/RNG, coordinate helpers, material catalog, asteroid profile, and unit tests.
3. Implement a CPU-authored deterministic asteroid field generator that uploads chunks into the GPU field; establish the compute command interface and chunk activation.
4. Render material fields with a palette/variation shader and add material inspection readback.
5. Implement starter ship blueprint, movement/fuel, cutter commands, GPU removal into loose debris, suction, cargo-door state, and cargo transfers.
6. Add strategic contacts, transition orchestration, home station inventory/sale, save/load delta persistence, and revisit verification.
7. Add deterministic debug showcase modes, diagnostics/stress scene, tests, visual polish, and measured performance documentation.

## Major architectural risks and mitigations

| Risk | Mitigation/decision |
|---|---|
| GPU mutation persistence can stall | read back only dirty chunks at save/eviction; compress off-frame; cap work per frame |
| pixel-level collisions are costly | chunk activity/sleeping plus SDF/coarse occupancy; no general Rigidbody-per-pixel approach |
| loose debris can explode in count | bounded active GPU pool plus streamed, losslessly encoded sleeping cells/fragments; no authoritative cell despawn or lossy aggregation |
| cargo appears to violate physical capacity | cargo field shares the universal cell grid; only empty visible cavity cells accept material and capacity is derived from their count |
| visual scale conflicts with simulation scale | one canonical cell unit; camera/pixel-perfect presentation configured separately |
| generator changes break saves | generator revision in each site record and migration/fallback snapshots |
| custom ships diverge from starter ships | one structural field + component/blueprint model from day one |
| CPU/GPU data races or excess readback | explicit command/event ownership and async, rate-limited readback contracts |
| chunk seams cause artifacts | padded neighbor sampling and deterministic border refresh protocol |

## Approved interpretation and implementation tracking

The [continuous execution contract](docs/EXECUTION_PLAN.md) governs phases A–E and resolves inventory, pressure, recovery, encounter resume, and calendar rules.

- [ ] A.GATE Foundation proof.
- [ ] B.GATE Physical salvage persistence.
- [ ] C.GATE Contractor career and freedom.
- [ ] D.GATE Field careers and encounters.
- [ ] E.GATE Alien-drone escalation.
