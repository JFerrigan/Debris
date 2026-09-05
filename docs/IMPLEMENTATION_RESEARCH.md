# Implementation Research: Unity GPU Simulation and Steam Delivery

## Conclusion

Debris should use Unity/URP as the engine and renderer, with compute shaders for active close-up simulation. Steam is the PC distribution/platform layer; it does not replace Unity or own the material simulation. This is a hybrid architecture, deliberately not a direct clone of Noita’s custom general-purpose falling-sand engine.

The primary comparison is Nolla Games’ published Noita technical talk. It describes scaling a continuous destructible pixel world and integrating destructible rigid-body physics in its custom Falling Everything engine. That validates the importance of spatial partitioning and specialised destruction, but Debris has a different workload: mostly fixed space structures, individual loose salvage cells, visible zero-g cargo, very large ships, and lossless revisit persistence. [GDC: Exploring the Tech and Design of Noita](https://www.gdcvault.com/play/1025695/Exploring-the-Tech-and-DesignAt)

## Recommended simulation split

| State | Representation | Owner | Why |
|---|---|---|---|
| Fixed asteroid/hull/station cells | chunk-local GPU textures/texture arrays | `SiteSimulation` ComputeShaders | dense 2D neighbourhood work, direct palette rendering, sparse dirty chunks |
| Individual loose material and fuel cells | structured `GraphicsBuffer` records, spatial buckets/occupancy grid | compute shaders | zero-g velocity, non-overlap, suction, collision, cargo tumbling, and spills |
| Cargo | same loose-cell buffer, bounded by ship-local collision/cavity field | compute shaders | cargo remains physical rather than becoming inventory slots |
| Components | sparse CPU records mirrored to compact GPU lookup/command data | ship/component systems | low count, explicit behavior and save identity |
| Structural support/collapse | dirty-region field masks and iterative GPU connectivity passes | compute shaders | severed anchors and detached regions without whole-site CPU scans |
| Strategic map/economy/save index | CPU data-oriented C# | Core/Strategic/Persistence | low volume, deterministic IDs, menus, and serialization |

## Refined cell representation

A “pixel” is a square simulation cell with fixed world dimensions. Fixed structure occupies integer cell coordinates in a chunk field. A loose cell retains the same dimensions but has fixed-point subcell position and velocity, so it can drift and bounce naturally in zero gravity. It is not a Unity physics body. Each simulation step resolves it to local occupancy buckets; a cell may not enter a bucket/position already occupied by another cell or fixed material.

This gives Debris the visible inertial cargo behavior required by the design without treating every cell as an arbitrary rotating rigid body. Cell rotation is visually irrelevant for a square cell. Larger future loose chunks are a separate body type made of linked cells; they are not an optimisation that silently changes initial individual-cell salvage rules.

Each loose-cell record needs only compact authoritative state: stable runtime ID, material index, fixed-point position, velocity, flags (cargo/fuel/fragment), and optional temperature/damage/variant seed. Do not store a per-cell Unity transform. The exact GPU record stride is selected after a memory/bandwidth benchmark and recorded in `docs/PERFORMANCE.md`.

## Per-step GPU pipeline

1. CPU input/components generate a bounded command list: thrust, drill/saw/laser damage, suction, door state, repair, and chunk activation.
2. Compute kernels apply material/tool gates and damage to active fixed-field chunks. Destroyed cells append one loose-cell record per cell; no initial mining shortcut creates a fake inventory stack.
3. A local occupancy/spatial-hash pass bins loose cells. Integration, collisions, cavity boundaries, suction, thrust acceleration, and fuel/cargo spills run only in active chunks/ship-local regions.
4. Dirty-region connectivity passes identify supported structure, severed component anchors, and newly detached connected regions. The first prototype needs the event/data path; stress/support collapse is expanded behind measured dispatch budgets.
5. Rendering draws fixed fields directly from textures and loose cells through GPU-driven instancing/indirect draws where profiling supports it. Effects consume append-buffer events.
6. Only compact facts return to CPU: cargo/UI events, material hover sample, dirty-chunk/save work, and diagnostics. Unity’s `AsyncGPUReadback` supports asynchronous requests from compute and graphics buffers; do not synchronously read a field texture for normal gameplay. [Unity AsyncGPUReadback](https://docs.unity3d.com/6000.0/ScriptReference/Rendering.AsyncGPUReadback.Request.html)

Unity command buffers can dispatch compute work, including indirect dispatch when dispatch size is GPU-derived. This is useful after profiling proves variable active work is beneficial; it is not an initial requirement. [Unity CommandBuffer.DispatchCompute](https://docs.unity3d.com/6000.0/ScriptReference/Rendering.CommandBuffer.DispatchCompute.html)

### Overflow and determinism rules

No GPU append buffer may silently drop destroyed/loose cells. A command must reserve enough loose-cell capacity before it removes fixed material; if insufficient capacity exists, the tool throttles/halts and records a diagnostic rather than deleting resources. The first prototype can use deterministic fixed-step ordering within an active site. Cross-GPU bit-identical loose-cell motion is not promised until it is measured; save/revisit correctness is authoritative within the same supported build/content version, with versioned migrations for later changes.

## Chunking and scale

- Begin benchmarking a 128×128-cell chunk candidate; retain it as `SimulationSettings`, not a magic constant.
- Maintain separate active-simulation and render radii. Fixed chunks outside those radii sleep as deterministic baseline + changed payload.
- Use a bounded active GPU pool. When a player moves away, asynchronously capture dirty field chunks and losslessly encode every authoritative loose cell/detached fragment into site data before resource reuse.
- Never clear an authoritative loose cell just because it is distant or numerous. A capacity/streaming overflow is a diagnostic failure to solve, not permission to despawn player-caused material.
- Support 100-cell starter ships, 50×50 cargo cavities, and 1000×1000+ ships by streaming their structural chunks; do not allocate a single whole-ship texture.

## Structural and cargo limits

Full support/stress collapse is expensive if treated as a full graph scan every frame. Restrict propagation to modified chunks plus a bounded neighbour border, mark regions dirty from tool/damage commands, and budget iterative passes over several frames when a very large structure is cut. A component works only while its required support/connection/power/fuel conditions are true. A severed thruster loses propulsion; a detached region becomes a persistent fragment.

Cargo cells are not snapped into a grid on intake. They stay in the individual-cell solver and collide against the cavity/door boundary. The cavity dimensions provide true capacity. An open rear door permits escape under inertia; later organizer equipment can exert forces or arrange cells but cannot bypass volume or overlap rules.

Fuel initially uses the same physical-cell pipeline and tank boundary rules. It persists and can spill after a breach. Fire, ignition, pressure, and fluid-specific reactions remain deliberately outside the first prototype; the fuel tag reserves the data/visual path without falsely claiming those mechanics exist.

## Unity implementation sequence

1. Establish URP, pixel camera, compute-resource ownership, and the `DevShowcase` profiler overlay.
2. Implement one active fixed-field chunk, deterministic asteroid upload, palette/emission shader, and hover readback.
3. Implement a fixed-capacity loose-cell buffer with individual cells, deterministic spawn, bounded local collision, and GPU rendering.
4. Add the starter ship’s cavity/door, arcade thrust, drill tool gate, suction, fuel, and physical cargo transfer/spill.
5. Add component anchors/connectivity and first detached-fragment path before broadening collapse simulation.
6. Add save/revisit using changed field chunks plus lossless loose-cell/fragment records.
7. Measure each stress preset on target Steam hardware classes before increasing chunk count, loose-cell capacity, or collapse complexity.

## Prototype gates and stop conditions

| Gate | Demonstration | Do not proceed until |
|---|---|---|
| P0: data foundation | same seed/profile reproduces field and material IDs | EditMode deterministic tests pass |
| P1: one chunk | generated field renders with correct material hover | no per-cell objects/allocations and readback is bounded |
| P2: cutter/debris | each broken cell becomes an individual loose cell | overflow cannot delete material; debug counters reconcile removed/spawned cells |
| P3: cargo physics | rear door, suction, tumbling cargo, and spill | visible capacity and non-overlap reconciliation pass |
| P4: ship damage | anchor cut disables a component and yields a fragment record | support work is limited to dirty region and produces no full-site CPU scan |
| P5: persistence | leave/reload preserves fixed changes and every loose cell/fragment | save/reload state hash matches at a fixed test point |
| P6: strategic loop | select contact, enter, return home, sell/store, revisit | explicit scene state transfer has no hidden scene references |
| P7: stress/polish | sustained mining and streaming showcase | measured targets are entered in `docs/PERFORMANCE.md`; no invented values |

If P2 or P3 cannot sustain an acceptable active loose-cell count on the development GPU, pause feature expansion. Profile buffer bandwidth, occupancy contention, render cost, and command count first; do not “solve” it by turning material into abstract inventory or despawning cells.

## Steam integration

Keep a narrow `Platform.Steam` adapter behind interfaces such as `ICloudSaveProvider`, `IAchievementProvider`, `IPlatformInputGlyphs`, and `IPlatformOverlay`. Gameplay, deterministic saves, and Unity input must work without Steam running. This makes local/editor play and future storefronts possible without conditional logic throughout the game.

### Builds

Create reproducible Unity player builds first, then upload platform-specific depots through SteamPipe scripts. Use private/beta branches for development, automated preview builds to validate depot mapping, and a release branch only after build verification. SteamPipe’s documented process packages depot content through build scripts and supports branches/preview builds. [SteamPipe uploads](https://partner.steamgames.com/doc/sdk/uploading?l=english)

### Saves and Steam Cloud

The all-persistence rule makes save format critical. Store one small world index plus content-addressed/versioned site chunk files, grouped by changed site/chunk rather than a giant monolithic save. Write atomically locally, retain rollback data, and sync only changed files. Steam Cloud offers Auto-Cloud (simple path configuration) and direct Cloud API integration; choose the latter only if product requirements need explicit file selection/conflict UX. Steam advises separating frequently changed state from rarely changed state and notes that very large or numerous files affect player bandwidth and launch/exit time. Configure the actual per-user file/byte quota in Steamworks and validate it with stress saves before shipping. [Steam Cloud](https://partner.steamgames.com/doc/features/cloud?l=english)

Steam Cloud is a replica of local authoritative saves, not an online simulation service. Save conflict handling needs a future product decision: default to timestamp/backup preservation and offer a clear recovery choice rather than silently losing a mined site.

### Input and controller readiness

Use Unity’s action-based input abstraction now: `Flight`, `Aim/Pointer`, `ToolPrimary`, `ToolSecondary`, `Interact`, `Toolbar`, `Map`, and `Menu`. Keyboard/mouse ships first. Add Steam Input only when controller support is actively tested; Steam Input’s native model maps semantic actions and action sets rather than physical buttons, which fits the compact toolbar design. Steam documents action sets/layers specifically for changing context without permanently binding every action. [Steam Input concepts](https://partner.steamgames.com/doc/features/steam_controller/concepts)

When native Steam Input ships, bundle/version the action manifest with the game depot and provide official controller configurations. Valve documents this workflow and controller glyph/action handling. [Steam Input developer setup](https://partner.steamgames.com/doc/features/steam_controller/getting_started_for_devs)

## Explicit non-goals for the first prototype

- No per-cell Unity GameObjects, Rigidbody2D instances, or CPU managed allocations.
- No whole-universe simulation while a salvage site is loaded.
- No general fluid/fire/gas simulation until separately designed; physical fuel persistence/spills do not automatically imply ignition mechanics.
- No online multiplayer, Steam Workshop, achievements, or Steam Input native integration before the core mining/save loop is proven.

## Validation gates

Before claiming the architecture viable, capture actual measurements for: steady mining, full cargo tumbling with open/closed door, fuel spill, component-anchor loss, a large detached region, save/revisit of loose cells, long traversal chunk streaming, and generated save/Steam Cloud candidate size. Record machine/GPU, Unity build settings, driver, preset, frame-time percentile, active chunks/cells, buffer use, dispatch count, readback latency, and save payload in `docs/PERFORMANCE.md`.

## Related documentation

- System boundaries and update order: `ARCHITECTURE.md`
- GPU resource/persistence rationale: `docs/GPU_SIMULATION.md`, `docs/PERSISTENCE.md`
- Unity editor/scenes/content workflow: `docs/UNITY_INTEGRATION.md`
- Production work sequence and documentation ownership: `docs/TECHNICAL_ROADMAP.md`

## Implementation checklist

IDs refer to [EXECUTION_PLAN](EXECUTION_PLAN.md); only verified work is checked.

- [x] A.GATE Measured GPU representation proof.
- [ ] C.6 Steam/cloud integration.
