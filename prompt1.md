# Goal

Build the foundation of a polished 2D space salvage and mining game in **Unity (latest stable release)** from this project.

The game is a modern pixel-simulation game at roughly **Noita-like pixel scale**, but set in a large open space frontier.

The player is a salvager. They leave colonies/stations, travel through a zoomed-out space overworld, discover asteroids, wrecks, ships, and other sites, then enter a close-up pixel simulation where those structures can be physically cut apart and harvested.

The central fantasy is:

**Find something → physically dismantle it → collect its actual material → bring it home → sell/store/build with it → improve your ship → travel farther → salvage increasingly complex things.**

The visual bar is extremely high for an indie pixel game: dense colored materials, clean pixel edges, GPU particles, emissive rare resources, atmospheric lighting, bloom where appropriate, sparks, glowing machinery, polished UI, and responsive physical effects.

This must never look like programmer art.

The simulation and rendering architecture must be designed for **large amounts of destructible material and GPU-heavy effects from the beginning**. Do not build a temporary CPU-per-pixel architecture that will later need to be replaced.

---

# Product principles

These are core design constraints. Preserve them unless explicitly changed later.

## 1. Matter is physical

Asteroids, wrecks, and ships are composed from actual material pixels.

Examples include:

* rock
* iron
* steel
* copper
* ice
* carbonaceous material
* composite hull material
* rare metals
* exotic late-game materials

Do not lock the material list yet. Build an extensible material system.

Every material has a visual identity.

Pixels may have:

* color
* shade variation
* emissive contribution
* physical properties
* durability
* density
* value
* temperature or other simulation properties later

Hovering or inspecting material must allow the game to identify what the player is looking at.

Common resources should be readable but understated.

Rare materials should visually stand out through controlled techniques such as:

* stronger saturation
* unusual palettes
* subtle emissive light
* animated shader behavior
* sparkling particles
* other polished effects

Do not turn rarity into visual clutter.

---

## 2. Ships are physical constructions

Ships are not single sprites.

A ship consists of:

### Material structure

Pixel material forms things such as:

* hull
* armor
* walls
* braces
* cargo cavities
* structural shapes

### Functional components

Ships also contain discrete machine components.

Examples:

* drills
* saws
* cutters
* suction systems
* cargo doors
* cargo containers
* thrusters
* engines
* fuel tanks
* scanners
* batteries
* generators
* grapplers
* mechanical arms
* processors
* docking systems

This is an extensible component framework, not a fixed component list.

Prebuilt ships must use the same construction rules available to player-built ships wherever practical.

Do not create fake NPC ship geometry that cannot participate in the same salvage system.

---

## 3. Salvage should be physical and satisfying

The starter ship demonstrates the basic interaction.

It has approximately:

* a simple propulsion system
* limited fuel
* a drill/cutting tool on the front
* an open cargo cavity toward the rear
* a cargo door that can open and close

Early asteroid mining should involve physically maneuvering the ship.

The player may:

1. approach an asteroid
2. drill/cut material loose
3. create loose pixels or chunks
4. maneuver the ship
5. use suction or another collection mechanism
6. physically pull material toward the cargo area
7. get the material into the cargo cavity
8. close the cargo door
9. return home

Backing the ship toward material in order to load cargo is acceptable and should feel intentionally industrial rather than inconvenient.

Future equipment may dramatically change how salvage works.

Possible future equipment includes saws, crushers, grapplers, tractor systems, stronger suction systems, sorting systems, detachable cargo containers, drones, explosive tools, or stranger technology.

Do not implement all of these now.

The architecture must make them possible.

---

## 4. Space has two major scales

### Strategic space view

The player can zoom far outward and travel through a large space environment.

Home base remains identifiable.

The player detects objects throughout space.

Early detection may identify targets only as:

**UNKNOWN OBJECT**

Later scanner technology may reveal information such as:

* asteroid
* wreck
* ship
* material signatures
* approximate composition
* size
* danger
* value

Do not decide the full scanner progression yet.

Space should conceptually allow travel indefinitely in any direction.

Practical exploration distance is constrained through systems such as:

* ship speed
* fuel
* current technology
* cargo mass
* return distance

The game should communicate clearly when the player is pushing beyond a reasonable return range.

Running out of fuel should not automatically mean instant game over.

One possible emergency decision is dumping valuable cargo to reduce mass and improve the chance of returning.

Design the underlying systems so choices like this are possible.

### Salvage/site view

Approaching and selecting a target transitions into the close-up pixel simulation.

Here the asteroid, ship, wreck, or structure exists at material-pixel scale.

The player directly interacts with and damages it.

The transition between strategic travel and salvage simulation must be architecturally explicit.

Do not attempt to simulate every distant object at pixel resolution while it is not being visited.

---

## 5. Sites persist

A visited salvage site must preserve its state.

If the player:

* drills halfway through an asteroid
* removes part of a wreck
* cuts a ship in half
* extracts valuable components
* leaves debris
* exposes an internal compartment

then leaves and returns later, the site should remain meaningfully as they left it.

Persistence is a first-class architectural requirement.

Design save representation carefully.

Do not simply serialize enormous raw textures if a better sparse/chunked/delta representation is appropriate.

---

## 6. Asteroids introduce the game

Early gameplay should primarily involve asteroid mining.

Asteroids teach:

* flight
* target selection
* mining
* cutting
* material identification
* cargo collection
* fuel management
* return planning
* selling and storing resources

Later progression can lead into increasingly complicated salvage:

* rich asteroids
* debris fields
* wrecks
* abandoned ships
* large industrial ships
* unusual structures
* other content not yet decided

Do not rush directly into the most complicated salvage scenarios.

---

## 7. Economy feeds construction

Collected materials can generally be:

* sold
* stored
* used for construction

Money can generally be used for:

* purchasing components
* purchasing equipment
* purchasing blueprints
* services
* logistics
* other progression systems later

Blueprints may allow components to be manufactured from appropriate materials.

Do not design a huge crafting tree yet.

Build the data model so blueprints, recipes, materials, and components are data-driven.

---

## 8. Stations, colonies, and logistics

The player initially operates from a home colony/station.

Eventually other locations may include:

* colonies
* orbital stations
* planets
* industrial facilities

At these locations the player may sell or store resources.

Resources stored away from the player's home base may require paid shipping to move between locations.

This logistics system should eventually make geography matter economically.

Do not build the entire economic simulation now.

---

## 9. Custom ship construction is a major long-term feature

The player will eventually be able to construct custom ships.

Ship construction should allow combinations of:

* freely shaped pixel/material structure
* functional components
* cargo cavities
* doors
* propulsion
* salvage equipment
* fuel/storage systems

A ship's physical layout should matter.

A strange ship built around an enormous cargo cavity should genuinely behave differently from a compact ship built around cutting equipment.

Do not fake customization through stat sliders.

The eventual ship editor should manipulate the same underlying ship representation used by gameplay.

Do not implement the full editor during the first architecture pass unless explicitly instructed.

---

# How to work

## 1. Architecture before gameplay code

Before implementing gameplay, inspect the project and write:

`ARCHITECTURE.md`

It must describe the complete intended architecture.

At minimum define separate subsystems for:

* application/core
* world coordinates
* strategic space
* site instances
* pixel materials
* pixel simulation
* GPU simulation
* rendering
* lighting/shaders
* particles/effects
* ship representation
* ship components
* structural damage
* loose material/debris
* cargo
* tools
* propulsion
* fuel
* scanning
* procedural generation
* asteroids
* wrecks/ships
* persistence/save system
* economy
* inventory/storage
* blueprints/construction
* stations/colonies
* UI
* input/camera
* audio
* testing
* diagnostics
* showcase/demo scenes

Define:

* ownership boundaries
* public APIs
* data flow
* events/messages
* update order
* threading assumptions
* CPU/GPU ownership
* serialization boundaries
* world units
* coordinate spaces
* deterministic RNG rules
* IDs for persistent entities
* how site state survives unloading
* how components connect to pixel structures
* how ship material pixels are represented
* how loose material differs from fixed material
* how material enters/leaves cargo
* how future custom ships use the same representation as predefined ships

Avoid giant manager classes.

Avoid global mutable state.

Avoid systems reaching directly into each other's internals.

---

## 2. GPU architecture is mandatory

Treat large pixel simulation as a GPU-compute problem where appropriate.

Before implementation, write:

`docs/GPU_SIMULATION.md`

Compare viable Unity approaches and explicitly select the architecture.

Consider technologies such as:

* ComputeShaders
* GraphicsBuffer / ComputeBuffer
* RenderTexture
* texture-backed material fields
* chunked simulation
* GPU-driven particle systems
* indirect drawing where useful
* AsyncGPUReadback only where necessary
* Burst/Jobs only for CPU tasks that genuinely belong on CPU

Do not blindly implement a Noita clone algorithm.

Design specifically for this game's requirements.

The design must address:

* fixed material pixels
* empty space
* loose pixels/debris
* material destruction
* drilling/cutting
* suction forces
* material transfer
* potentially huge objects
* active vs inactive chunks
* simulation sleeping
* rendering
* persistence
* GPU→CPU synchronization
* save/load
* collision queries
* material inspection under the mouse

CPU/GPU synchronization must be minimized.

Do not perform per-pixel CPU GameObject creation.

Do not create one Unity object per material pixel.

Do not use one Rigidbody per loose pixel.

---

## 3. Chunk everything

The simulation must be spatially chunked.

A site should be capable of containing far more material than is currently active.

Define:

* chunk dimensions
* chunk coordinates
* active simulation radius
* rendering radius
* sleeping rules
* dirty-state tracking
* persistence behavior
* neighboring chunk interaction
* streaming behavior

The exact chunk size should be benchmark-driven rather than guessed permanently.

---

## 4. Separate simulation from presentation

Rendering must not define game state.

The material simulation should expose data that rendering visualizes.

Shaders can add:

* per-material variation
* emissive rare-material glow
* heat effects
* outlines/highlights
* bloom masks
* damage flashes
* scan visualization
* atmospheric effects

but visual effects must not secretly contain authoritative simulation state.

The renderer should be replaceable without rewriting gameplay.

---

## 5. Everything important is data-driven

Materials, components, tools, ships, blueprints, asteroid generation profiles, and similar content should be definable through clean data assets.

Prefer ScriptableObjects or another clearly documented data-driven system where appropriate.

Material definitions should not require modifying switch statements scattered across the project.

Adding a new material or component should be straightforward.

---

## 6. Deterministic procedural generation

Use seeded RNG.

Never rely on uncontrolled `UnityEngine.Random` calls for persistent world generation.

A world seed plus stable object/site identifiers should reproduce generated sites when required.

Once a site has been modified, save only the state necessary to reconstruct its altered version where practical.

Document the strategy.

---

# Verification loop

Build verification infrastructure before expanding gameplay.

Create a development/debug scene capable of spawning controlled test cases.

At minimum provide showcase modes for:

1. material palette
2. asteroid generation
3. cutting/drilling
4. loose material
5. suction
6. cargo intake
7. ship components
8. strategic map
9. site persistence
10. shaders/lighting
11. performance stress test

Create deterministic debug presets so the same scene can always be reproduced.

Add diagnostics displaying at minimum:

* FPS
* frame time
* active pixel count
* simulated pixel/chunk count
* visible chunk count
* sleeping chunk count
* GPU buffer usage where practical
* simulation step time
* render time where practical
* draw calls
* generated debris count
* save size
* loaded site count

If automated screenshots can be reliably implemented for the Unity target, create a screenshot harness with deterministic camera presets.

Do not claim a visual feature is polished without actually launching and inspecting it.

---

# Performance targets

Design toward:

* **60 FPS target**
* **50 FPS minimum acceptable gameplay target on the development machine**
* no frame-time spikes from loading ordinary chunks
* no per-pixel GameObjects
* no per-pixel managed allocations
* no recurring full-site CPU scans
* no unnecessary GPU readbacks
* no unnecessary texture uploads
* no garbage generation in steady-state simulation

Create explicit stress scenes instead of assuming performance.

Document actual measured limits in:

`docs/PERFORMANCE.md`

Never invent benchmark numbers.

---

# Initial implementation milestone

Do not try to build the whole game.

After architecture and infrastructure are complete, build one polished **vertical systems prototype**.

It should contain:

### Strategic view

* a simple home station marker
* the starter ship
* free movement in space
* limited fuel
* several seeded unknown contacts
* selecting an asteroid target
* entering that site

### Salvage view

* one generated asteroid
* multiple visually distinct materials
* material hover inspection
* a player ship
* basic propulsion
* front mining/drilling functionality
* material pixels physically removed from the asteroid
* loose harvested material
* a rear cargo cavity
* an opening/closing cargo door
* a basic suction/collection mechanism
* actual transfer of collected material into cargo

### Return loop

* leave the asteroid
* preserve its modified state
* return to home
* see collected materials
* sell or store them
* revisit the asteroid
* verify that it remains partially mined

This prototype is not a disposable mockup.

It must exercise the real architecture intended for the full game.

---

# Visual bar

The game should look like a deliberately modern pixel game, not an old game enlarged.

Target qualities:

* crisp small-scale material pixels
* rich but controlled palettes
* subtle per-pixel shade variation
* highly readable silhouettes
* smooth camera behavior
* strong contrast against space
* polished emissive lighting
* GPU particles
* mining sparks
* dust and debris
* subtle glow
* controlled bloom
* attractive thruster effects
* clean UI
* satisfying destruction feedback

Rare resources should be exciting to discover visually.

Do not blur the underlying material pixels into mush.

Avoid excessive bloom.

Avoid generic Unity default materials.

Avoid default particle effects.

Avoid placeholder gradients or primitive programmer UI surviving into polished showcase scenes.

---

# Documentation

Maintain:

`ARCHITECTURE.md`

`GAME_DESIGN.md`

`docs/GPU_SIMULATION.md`

`docs/MATERIAL_SYSTEM.md`

`docs/SHIP_SYSTEM.md`

`docs/PERSISTENCE.md`

`docs/PERFORMANCE.md`

`docs/STATUS.md`

`GAME_DESIGN.md` should preserve the product decisions in this prompt.

Do not silently invent major game mechanics.

When implementation requires a routine low-level decision, make it.

When a potentially major design choice is unresolved, record it under:

`Open Design Questions`

rather than permanently deciding it.

Examples include:

* combat
* hostile ships
* death/failure rules
* exact crafting complexity
* detailed economy
* factions
* automation
* crew
* full ship-editor UX
* late-game progression

The implementation should remain extensible to these ideas without assuming they exist.

---

# Codex working rules

1. Inspect the repository before changing anything.

2. Architecture first.

3. Keep the project compiling and runnable after every substantial change.

4. Do not build throwaway systems that contradict the intended architecture.

5. Never create one GameObject/Rigidbody per pixel.

6. Treat GPU simulation, chunking, persistence, and custom ship construction as foundational constraints.

7. Use seeded deterministic generation.

8. Keep modules isolated and APIs explicit.

9. Prefer composition over giant inheritance trees.

10. Add automated tests for deterministic/data-oriented systems where practical.

11. Build debug visualization for systems that cannot easily be inspected otherwise.

12. Measure performance rather than assuming it.

13. Never report tests, frame rates, screenshots, or visual results that were not actually produced.

14. Preserve design decisions from this document. Do not casually reinterpret them.

15. Do not add significant mechanics simply because they sound fun. Put them in `Open Design Questions`.

16. Make ordinary engineering decisions independently and document assumptions.

17. Do not ask me routine implementation questions.

18. If an architectural assumption proves wrong, update the architecture documentation before restructuring the code.

19. Prefer small, comprehensible systems over premature abstraction, but preserve the major subsystem boundaries.

20. Keep all content and systems ready for extensive future expansion.

---

# First task

Start by inspecting the Unity project.

Then, before implementing the game:

1. write `GAME_DESIGN.md` from this prompt
2. write `ARCHITECTURE.md`
3. write `docs/GPU_SIMULATION.md`
4. write `docs/PERSISTENCE.md`
5. identify the major architectural risks
6. propose the exact first vertical-slice implementation plan
7. only then begin implementing the foundational systems and vertical prototype

Do not attempt to fill in the entire future game design.

The immediate goal is to prove that:

**a pixel asteroid can be generated, physically mined with the starter salvage ship, loose material can be sucked into a real cargo cavity, the player can leave the site, return later, and find the asteroid exactly as they left it — at high performance and with a polished modern pixel presentation.**

Start now.

