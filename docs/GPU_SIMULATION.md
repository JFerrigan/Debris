# GPU Simulation Architecture

## Selected approach

Use ComputeShaders with texture-backed, chunked material fields and GraphicsBuffer-backed loose debris/effect particles. URP shaders render the same field data. This fits mostly-static, destructible, visually dense sites while keeping the CPU out of per-cell work.

`RenderTexture`/texture arrays are preferred for chunk-local material state because 2D neighbourhood kernels, palette rendering, and chunk sampling map naturally to them. `GraphicsBuffer` is preferred for sparse moving debris, command queues, effect events, and indirect draw arguments. Burst/Jobs assist deterministic generation, command packing, compression, and save preparation—not high-volume cell simulation.

## Rejected primary designs

- CPU array + GameObject/Rigidbody per cell: unacceptable allocation, physics, and rendering cost.
- Whole-site monolithic textures: unsuitable for effectively huge sites, streaming, and persistence.
- Render-only shader damage: cannot be authoritative or persist reliable material changes.
- Fully general falling-sand simulation: not required for initial space salvage and would spend the budget on behavior the game does not need.

## Data and execution

Each active chunk has material/flag, state, and derived collision/SDF textures. A material LUT buffer carries palette, durability, density, value, emissive, tool-gate, and future properties. GPU kernels process only active chunks and receive batched command buffers for cutting, forces, intake, and state change. Damage modifies the field; the starter drill releases individual material-aware loose cells, with tool power controlling material-durability break rate. Future saws use wider damage volumes; future lasers use narrow line/beam commands. Loose cells use a fixed GPU pool and non-overlapping local collision/occupancy resolution. Inactive/sleeping groups may compact only through a lossless encoding that retains every cell's material, position, velocity, and other authoritative state before reactivation, suction, cargo intake, or saving.

Cargo cells stay in the same loose-cell simulation after passing an open cargo door, constrained by the ship cavity boundary rather than snapped into storage slots. Their inertia, collision, and the open-door boundary make spills possible. GPU structural-connectivity/stress passes identify unsupported hull regions, disable components whose anchors are severed, and emit detached-fragment work. The initial implementation must bound these passes by dirty regions/chunks; full stress/support collapse is a target system, not a reason to run whole-ship scans every frame.

Chunks activate inside a simulation radius and render inside a larger rendering radius only when needed; chunks outside both sleep. Dirty flags, command influence bounds, and neighbour border dependencies drive dispatch. Chunk size begins as a measured configuration candidate (128×128) and must be benchmarked across target hardware before being fixed.

## GPU to CPU boundary

The CPU writes commands and reads only compact facts: cargo transfer candidates, effect events, one inspected cell, metrics, and dirty chunks at eviction/save. Readbacks use `AsyncGPUReadback`, are rate-limited and cancellable with site lifetime. The main loop must never synchronously fetch a field texture to make normal gameplay decisions.

Save readback copies only altered chunk state into versioned compressed payloads. Static unmodified chunks regenerate from seed. The renderer reads field textures directly; shader variation derives from deterministic per-cell seeds and material data, preserving sharp pixels while adding shading, emissive masks, heat, outlines, and scan effects.

## Validation requirements

The debug scene must expose active/visible/sleeping chunks, field/debris occupancy, buffer capacity, dispatch count, readback queue, simulation milliseconds, draw calls, and dirty/save payload size. Stress cases cover large fields, sustained cutting, debris saturation, rapid streaming, and save/unload. Performance values are recorded only after actual measurement in `docs/PERFORMANCE.md`.

## Implementation checklist

IDs refer to [EXECUTION_PLAN](EXECUTION_PLAN.md); only verified work is checked.

- [ ] A.4 Chunk fields/rendering/inspection.
- [ ] A.5 Fixed-step loose simulation, collision, sleep, overflow.
- [x] A.6 CPU/GPU fixtures.

## Phase A implementation details

`MatterSession` owns 128×128 R32_UInt material and R32_SFloat damage texture-array slices. The page is 2×2 or 4×4 chunks for measured presets. Loose cells use a 32-byte structured record: float2 lower-left position (0), float2 velocity (8), uint material (16), uint identity (20), uint step (24), uint sleep/boundary flags (28). CPU/GPU fixture asserts stride and material offset. The initial float motion encoding is an explicit prototype choice; fixed-point save encoding is not yet implemented or cross-GPU certified.

Cut commands have maximum radius 16 and execute in order, accumulating allocation and throttle counters locally. Capacity is reserved before clearing the fixed cell. This bounded serial tool pass is separate from parallel loose-cell work. Durability accumulates at 60 Hz; saturated work keeps material intact.

Loose motion uses 16 spatial colors. Each source bucket updates once per tick, moves at most .2 cell per step, and checks exact square overlap against nearby fixed and loose cells. Separated colors avoid conflicting occupancy writes. Collision damping can sleep a cell without deleting its record or velocity; boundary suspension retains velocity. This is a conservative prototype solver with a speed cap, not certification for moving/rotating hulls (B.3).

`SnapshotAsync` fences mutation, reads authoritative fields/damage/cells/counters, and supports disposing then restoring a page exactly. Inspection and compact metrics are asynchronous; normal gameplay never synchronously reads a field. Page-level eviction is tested; large-world incremental dirty-chunk streaming remains B.4/B.5. Unity's [AsyncGPUReadback API](https://docs.unity3d.com/6000.0/ScriptReference/Rendering.AsyncGPUReadback.html) defines the readback boundary.
