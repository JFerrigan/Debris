# Unity Integration and Editor Workflow

## Purpose

Unity is the presentation, authoring, orchestration, and platform layer for Debris. It does not turn simulation cells into GameObjects. High-volume material fields, loose cells, structural evaluation, and GPU effects remain chunked data/GPU systems; Unity scenes contain cameras, lighting, UI, authoring assets, and a small number of runtime roots.

The detailed GPU implementation and Steam build/cloud/input boundary is documented in `docs/IMPLEMENTATION_RESEARCH.md`.

The project targets the current Unity LTS/stable editor with URP. Exact package/editor versions are pinned in `ProjectSettings/ProjectVersion.txt` and `Packages/manifest.json` once an editor version is selected and verified. Release targets are macOS, Windows, and Linux through Steam, with macOS as the primary development/validation platform. Platform-specific code and asset choices must preserve all three targets from the outset; performance certification begins on macOS before cross-platform validation.

## Project layout

```text
Assets/
  Debris/
    Core/                 Bootstrap, IDs, state transitions, shared contracts
    World/                Coordinates, seeds, strategic world records
    Strategic/            Contacts, travel, scanner, strategic UI/controller
    Sites/                Site session, chunk streaming, generation, commands
    Materials/            Material definitions, catalog, runtime LUT upload
    Simulation/           Compute shaders, GPU resources, cell/debris simulation
    Rendering/            URP shaders, render features, palette/field drawing
    Ships/                Blueprints, structural fields, components, cargo
    Player/               Arcturus controller, boosters, boarding, personal tools
    Encounters/           Temporal contacts, crews, routes, expiry, capture conversion
    Persistence/          Save models, codecs, repository implementations
    Economy/              Markets, EE Inc. debt, storage, sale/shipment transactions
    UI/                   UI Toolkit documents/controllers and HUD
    Effects/              GPU particles, semantic audio/effect cue producers
    Audio/                FMOD service, banks, snapshots, audio diagnostics
    Diagnostics/          Overlay, debug tools, deterministic showcase presets
    Tests/                EditMode/PlayMode test assemblies near their modules
  Content/
    Materials/            MaterialDefinition assets
    Components/           ComponentDefinition assets
    Ships/                ShipBlueprint assets and authored starter ship
    Asteroids/            AsteroidProfile assets
    Presets/              Deterministic demo/stress presets
  Scenes/
    Bootstrap.unity
    Strategic.unity
    Salvage.unity
    Hub.unity
    DevShowcase.unity
  Settings/               URP assets, input actions, simulation settings
```

Runtime C# is split into assembly definitions by module. Dependencies follow `ARCHITECTURE.md`; an asmdef may reference contracts/content it needs, but never a presentation module merely for convenience. Editor-only tools use a separate `*.Editor.asmdef`, live in an `Editor/` folder, and never enter a player build.

## Scenes and game-state flow

`Bootstrap` is the only always-loaded scene. It creates the composition root, loads persistent services, and moves the game between state-owned scenes:

```text
Bootstrap → Strategic → loading transition → Salvage → loading transition → Strategic
                                  ↘ DevShowcase (development only)
```

`Strategic` owns the zoomed-out camera, player/contact markers, home station, Frontier Count, and strategic HUD. Selecting a persistent contact serializes the strategic ship snapshot, records it as anchored at that contact, and opens `Salvage`. `Salvage` creates a `SiteSession` from a `SiteId`, loads/generates chunks, ship, and (when present) Arcturus, then commits the modified site and ship snapshot before returning to `Strategic`. `Hub` is a physical walkable station presentation/session entered after landing. Temporal encounters use a separate non-persistent session contract. Strategic travel is paused while a close-up session is active.

Scenes must not use hidden cross-scene object references as game state. Scene objects receive state through explicit bootstrap/session interfaces. Additive scene loading is acceptable for common presentation layers later, but never changes which system owns authoritative data.

## Editor authoring model

ScriptableObjects are content definitions, not mutable runtime save state:

- `MaterialDefinition`: immutable key, palette, emission, density, durability, value, tags.
- `ComponentDefinition`: immutable key, dimensions, ports/anchors, fuel/power needs, behavior parameters, authoring preview.
- `ShipBlueprint`: structural material-cell layout, cargo-cavity boundary, component placements, named anchors, initial state.
- `AsteroidProfile`: deterministic-generation parameters and weighted material bands.
- `ShowcasePreset`: deterministic seed, camera, ship, site profile, and diagnostic mode.

Input uses keyboard/mouse first, with actions organized around flight, pointer inspection, context action, and compact toolbar selection. Define Input System actions semantically so controller bindings can be added later; do not bind gameplay to hard-coded keyboard keys inside components.

Custom inspectors and authoring windows are warranted for the ship blueprint painter, component placement/anchor validation, material palette preview, asteroid-profile preview, and showcase launcher. They must edit serializable asset data through `SerializedObject`/Undo, mark assets dirty correctly, and validate before play. They do not write directly into runtime GPU resources.

The starter ship is authored as an actual blueprint: roughly 100 cells long, a middle ~50×50 cargo cavity, rear door, upper/lower rear-facing thrusters, front drill, and suction. The editor must render its same-size cells and physical cavity rather than a separate illustrative sprite.

## URP, shaders, and compute

URP provides 2D/renderer integration, post-processing, bloom, and camera composition. Custom shaders render chunk textures with palette variation, material emissive masks, heat, scan highlighting, and crisp pixel presentation. The texture/buffer data belongs to `SiteSession`/simulation; shaders are consumers only.

ComputeShaders allocate and update active chunk textures, loose-cell buffers, structural-connectivity/stress work buffers, command queues, and effect events. All compute resources have a documented owner, capacity, disposal point, and diagnostics counter. Use `AsyncGPUReadback` only for inspected cells, compact events, save/eviction snapshots, and bounded diagnostics—not ordinary per-cell gameplay.

URP assets, render features, quality settings, and shader variants are project settings/assets under `Assets/Settings`. Do not rely on editor-local quality settings or default Unity materials for final presentation.

## Play mode and debugging

`DevShowcase` is the daily verification scene. A preset selector reproduces material palette, asteroid generation, drill/cutting, loose-cell physics, suction, cargo spill/intake, component-anchor detachment, strategic contacts, save/revisit, lighting, and stress cases. The on-screen overlay reports FPS/frame time, active/visible/sleeping chunks, simulated cell count, loose-cell count, buffer capacity, GPU readback queue, simulation time, render time where available, draw calls, loaded sites, and save payload size.

Use gizmos for chunk bounds, active radii, component anchors, cutter/suction volumes, cargo door boundaries, and structural support diagnostics. Gizmos are development-only and must never be the only way simulation state is represented.

## Tests and validation

EditMode tests cover deterministic RNG, coordinate math, generation invariants, content validation, serialization codecs, cargo non-overlap, and transaction rules. PlayMode tests cover scene transitions, deterministic site reconstruction, cutter-to-debris-to-cargo flow, component disablement after anchor loss, and save/revisit behavior. GPU-specific checks use deterministic small fixtures and compare compact readback hashes/tolerances rather than screenshots alone.

Editor validation runs on content changes and before play/build: unique content keys, valid material references, non-overlapping blueprint structure/components, closed cargo boundaries except intended apertures, anchor connectivity, valid asteroid weights, and no unsupported save format versions.

## Source control and collaboration

Version-control all `Assets`, `Packages`, and `ProjectSettings` files, including `.meta` files. Use visible text serialization and force text asset metadata in Unity project settings. Ignore `Library/`, `Temp/`, `Logs/`, `obj/`, generated build outputs, and local IDE caches. Do not move/rename assets outside Unity without preserving their `.meta` files, because GUIDs are references.

Prefer small changes scoped to one module/content asset. Avoid editing a broad shared scene for data that belongs in a ScriptableObject. Before merging, open affected scenes, run relevant EditMode/PlayMode tests, use the matching `DevShowcase` preset, and record measured performance rather than estimated values.

## Initial editor setup checklist

1. Open/create the project with the pinned Unity editor and let Package Manager resolve the manifest.
2. Configure URP, 2D renderer/camera defaults, input actions, text serialization, and version-control metadata mode.
3. Create the assembly definitions and the four base scenes.
4. Create initial material, component, starter-ship, asteroid-profile, and showcase-preset assets.
5. Implement content validators and the DevShowcase overlay before expanding gameplay.
6. Add compute resource lifecycle diagnostics before activating high-volume pixel simulation.

## Implementation checklist

IDs refer to [EXECUTION_PLAN](EXECUTION_PLAN.md); only verified work is checked.

- [x] A.1 Editor/packages/test commands.
- [x] A.2 URP/input/bootstrap/showcase/validation.
- [ ] C.6 Platform builds.

## Reproducible commands (installed editor)

Run from the repository using bash, avoiding unrelated user shell startup scripts:

```sh
bash tools/unity.sh setup
bash tools/unity.sh test
bash tools/unity.sh build
bash tools/unity.sh open
```

The script pins Unity 6000.3.11f1. Setup is idempotent and retains existing authored assets/scenes. Tests write Logs/editmode.xml; setup/tests/build each have a log under Logs. The standalone benchmark is `Builds/Debris.app/Contents/MacOS/Debris -debrisBenchmark -logFile Logs/player.log`. Close the Debris editor before running batch commands against this project; other projects are independent.

The showcase uses camera scale in simulation-cell units. Presentation conversion to the planned 0.125-metre physical scale remains explicit work for ship integration. FMOD integration is not installed; audio remains Phase E.
