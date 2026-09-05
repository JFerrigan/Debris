# Audio Architecture

## Audio identity

Debris sounds like dark industrial machinery suspended in huge, indifferent space. Silence is an authored layer, not missing content. Close ship contact emphasizes transmitted vibration—motor whine, stressed hull groans, relay clicks, cargo knocks—while open vacuum strips away ordinary external sound. Radio and UI audio are intimate, narrow-band, and deliberately imperfect. The home hub is the rare dense counterpoint: ventilation, loaders, distant tools, voices, dock traffic, and music leaking from shops.

## Decision: FMOD Studio over raw Unity audio

Use FMOD Studio with the Unity integration for authored events, parameterized loops, routing, snapshots, profiling, and bank streaming. Unity still owns state, scene lifecycle, and the authoritative simulation; FMOD never decides gameplay. This is appropriate because Debris needs one event to adapt continuously to thrust, tool load, hull condition, compartment pressure, distance, and radio treatment. FMOD parameters can be updated from code and drive event automation; bank loading lets site/hub content be streamed rather than held permanently. [FMOD parameters](https://www.fmod.com/docs/tutorials/parameters.html) and [bank loading](https://www.fmod.com/docs/2.03/studio/getting-events-into-your-game.html) support those boundaries.

Use the FMOD Unity integration through a thin `IAudioService`, not direct `StudioEventEmitter` calls scattered across components. The integration exposes event references and full Studio/Core APIs in C#, while event emitters remain suitable for a few authored ambient scene anchors. [FMOD Unity API](https://www.fmod.com/docs/2.03/unity/api.html) [FMOD Unity emitter reference](https://www.fmod.com/docs/2.03/unity/api-studioeventemitter.html)

## Ownership and event flow

```text
Simulation / Components / Narrative / UI
              │ compact, non-authoritative AudioCue
              ▼
      Effects.AudioCueBridge ──► IAudioService ──► FMOD event instance / one-shot
              │                         │
              │                         ├── AudioState parameters + snapshots
              ▼                         ▼
       diagnostics/event budget     buses, VCAs, banks, mix/profiling
```

`AudioCue` is an ephemeral presentation event: event key, emitter/ship/site ID, position, intensity, material key, and optional parameters. It never enters authoritative saves and it must not feed back into simulation. Loop ownership is explicit: the source component/ambient zone creates, updates, and releases its event instance with its lifetime; a pooled one-shot service owns short impact, UI, and debris events.

The `Effects` module remains the boundary named in `ARCHITECTURE.md`; add `Audio` beneath it as a presentation-only submodule. Gameplay modules submit semantic cues such as `ToolCutBlocked`, `HullBreach`, `CargoCellImpact`, `RadioMessageStarted`, or `StationMarketOpened`, never clip names.

## Buses, VCAs, and snapshots

```text
Master
├── UI
├── Dialogue_Radio
├── Music
├── World
│   ├── Ship_Internal
│   ├── Ship_External
│   ├── Tools
│   ├── Debris_Impacts
│   └── Creatures
└── Hub
    ├── Hub_Ambience
    ├── Hub_Machinery
    └── Hub_Crowd
```

Expose user VCAs for Master, Music, Dialogue/Radio, Effects, and Ambience. Snapshots include `Vacuum`, `PressurizedInterior`, `Hub`, `MenuPause`, `DamageCritical`, and `RadioFocus`. `Vacuum` does not mute the game: it substantially removes external air-transmitted effects while preserving suit/ship conduction, UI, radio, and selected stylized low-frequency impacts. `PressurizedInterior` restores air, fire, and creature layers. Snapshot transitions must fade rather than pop.

## Parameters and content contract

All reusable events use a small shared vocabulary; individual events use only the subset they need.

| Parameter | Range/labels | Typical use |
|---|---|---|
| `Medium` | vacuum, pressurized, liquid/future | filter/routing treatment |
| `Load` | 0–1 | drill contact, thruster effort, motor stress |
| `Damage` | 0–1 | crackle, instability, intermittent failure |
| `FuelGrade` | low, standard, dense | ignition/engine color and character |
| `MaterialFamily` | rock, metal, ice, composite, exotic | cut/impact variation |
| `Speed` | normalized | thruster, debris, pass-by behavior |
| `Distance` | built-in/local where useful | attenuation and detail reduction |
| `Signal` | 0–1 | radio static, log corruption |
| `Crowd` | 0–1 | hub ambience density |

FMOD supports local and global parameters, including built-in distance/speed-related values; prefer local event parameters for physical emitters and global parameters only for deliberately world-wide state. [FMOD parameter reference](https://www.fmod.com/docs/2.03/studio/parameters-reference.html)

Event paths use stable semantic names, e.g. `event:/ship/thruster_loop`, `event:/tool/drill_loop`, `event:/impact/metal`, `event:/radio/incoming`, `event:/music/hub`. Content definitions reference event keys; runtime validates missing keys in development. Variation comes from multi-instruments, parameter sheets, random containers, and deterministic cue seeds, never from random selection hidden in gameplay code.

## Banks and loading

- `Master`: routing, shared UI, common radio, global music control; keep small because it stays loaded.
- `CoreGameplay`: starter ship, common tools, material impacts, recovery/alerts.
- `Hub_Home`: home ambience, shops, character/radio content; load on hub approach/entry.
- `Site_Common`: asteroid/wreck ambience and generic debris; load with salvage session.
- `Site_Profile_*` and `Narrative_*`: optional profile/arc content; prefetch before the transition and release after a safe delay.

Banks must be assigned deliberately and loaded before their events are used; FMOD’s documentation notes that an event’s bank must be loaded and that bank allocation controls audio memory. [FMOD Studio concepts](https://www.fmod.com/docs/2.03/studio/fmod-studio-concepts.html)

## Runtime rules and budgets

- Never create an audio instance per material cell. Aggregate cut, thrust, collision, and debris activity into capped emitters around the player/camera.
- Each frame ranks one-shots by audibility, semantic priority, distance, intensity, and recency; deduplicate repeated cell impacts into a representative burst.
- Persistent components own continuous loops; an inactive or unloaded component releases its loop with a short fade.
- A site/session transition fades snapshots and releases its scoped instances before unloading its banks. Persistent radio/dialogue may transfer ownership to the global service.
- Audio sources use site-local coordinates converted through the same origin-shift service as rendering, so long strategic travel cannot drift emitters.
- Capture FMOD voice count, virtualized voices, CPU, memory, bank state, dropped/merged cue counts, and longest callback/frame cost in diagnostics.

## Music and narrative audio

Music is sparse and stateful: near-silence/exterior drone while alone, restrained industrial pulse during sustained work, and warmer but still worn textures in the hub. Music follows long-lived context snapshots, not every drill hit. The soundtrack begins with generated/temporary implementation assets, while its eventual authored music is intended to be made by the project owner; event keys and stems must therefore be stable enough to replace temporary content without code changes. Dialogue is text-first: radio calls, logs, Cepheus’s same-hardware voice, and face-to-face scenes can play voice texture, beeps, filtered breaths, or optional voiced lines, but every message remains readable without sound. Signal strength can alter radio processing without obscuring required text.

## Accessibility and verification

Provide independent VCAs, subtitles/transcripts for all speech/radio/log content, speaker/source labels, visual indicators for critical alarms, dynamic-range modes, tinnitus-safe alternatives for alarms, and no requirement to identify a material or hazard only by audio.

The audio test scene must exercise vacuum/pressurized transitions, drill material variation, full-cargo impacts, fuel grades, damage loops, hub density, radio signal, bank load/unload, pause/menu behavior, and a worst-case cue storm. Verify no stuck loops, abrupt bank-unload silence, duplicate one-shots, or loss of critical cues at the voice limit. Profile on target hardware before setting voice and memory budgets.

## Implementation checklist

IDs refer to [EXECUTION_PLAN](EXECUTION_PLAN.md); only verified work is checked.

- [ ] E.4 Dark industrial audio, replaceable soundtrack.
