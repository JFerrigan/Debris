# Performance

Unity 6000.3.11f1 (3000ef702840) is installed. Phase A and starter-ship measurements are recorded below. This document is the recording location for measured hardware, build configuration, scene preset, FPS/frame-time percentile, active/visible/sleeping chunks, debris count, buffer use, draw calls, save payload size, and simulation/render timing.

Targets are 60 FPS and a 50 FPS minimum during normal development-machine gameplay, with no ordinary chunk-load spikes. The diagnostic and stress-scene requirements are defined in `ARCHITECTURE.md` and will be implemented before performance claims are made.

## Implementation checklist

IDs refer to [EXECUTION_PLAN](EXECUTION_PLAN.md); only verified work is checked.

- [x] A.GATE Mac frame time, GPU time, memory, scaling.
- [ ] B.5 Large-ship/streaming/site-index stress.
- [ ] E.5 Full regression performance.

## Phase A Mac gate — 2026-09-05

Development player, Unity 6000.3.11f1, Metal, Apple M4 Pro (Mac16,8), macOS 15.1, 1440×900 window. Target 60 FPS, v-sync off. Each preset warms for 120 steps then samples 300 frames; the report includes a few snapshot-wait frames. GPU timings are reported by FrameTimingManager. Timing collection adds overhead; no release-build or other-platform claim.

| Loose capacity | Active chunks | Actual loose cells | Frame p95 ms | CPU p95 ms | GPU p95 ms | Explicit GPU buffers MiB |
|---|---|---|---|---|---|---|
| 1,024 | 4 | 1,024 | 17.331 | 17.334 | 2.161 | 0.84 |
| 8,192 | 4 | 8,192 | 17.577 | 17.576 | 1.876 | 1.06 |
| 32,768 | 16 | 13,437 | 17.029 | 17.027 | 2.082 | 4.06 |

All three reconcile fixed + loose = 22,400. Saturated presets report 280,154 and 47,897 throttled attempts without deleting matter. Unity allocated memory was ~84 MiB. The 32,768-capacity preset did **not** fill the entire pool; it measures capacity/field scaling with 13,437 active cells.

Approve **8,192 active loose cells / four 128² chunks** as the initial Phase B development budget. This is a measured prototype budget, not a permanent site limit. Larger ship/streaming workloads must be measured independently. The live diagnostic draw path is two instanced calls (field and loose matter), plus UI.

Evidence: [raw report](evidence/A-mac-benchmark.txt), [inspected screenshot](evidence/A-showcase.png), [15 passing tests](evidence/A-tests.txt). The standalone starts successfully, renders distinct material palettes, performs ordered cuts, and displays asynchronous inspection/metrics. Full rotating-cavity verification remains B.3.

## Phase B starter GPU checkpoint — 2026-09-06

Same Mac/Metal development configuration as Phase A. Controlled preset seeds 576 cargo cells, warms 60 steps, then samples 300 rotating steps (0.001 rad/step), 16 active chunks, 8,192 loose capacity. This is a cargo workload, not proof of an earned mining trip. Frame p95 17.576 ms, CPU p95 17.570 ms, GPU p95 16.408 ms; 3,605,060 explicit buffer bytes, 34 simulation dispatches, three matter draw calls plus UI. Snapshot validates conservation and non-overlap at 0.360 radians. The 16-chunk starter preset fits 60 FPS here, but does not establish a large-ship/full-cargo budget.

Evidence: `evidence/B-ship-benchmark.txt`, `evidence/B-ship-showcase.png` (inspected), `evidence/B-gpu-tests.xml` (25/25). No Windows/Linux execution claim.

## Persistence/index checkpoint — 2026-09-07

Mac EditMode evidence (`evidence/B-persistence-tests.xml`): 100,000-site index is 4,000,048 bytes; initial streamed write 154 ms; integrity-checked open 19 ms; 1,031 binary-search lookups 22 ms. Payloads loaded: zero. The fixture also updates existing/new entries, rejects revision rollback and recovers a corrupt index from backup. This is an index-only measurement, not 100,000 heavy site payloads or an active-world streaming result.

The mixed damaged-site save fixture encodes 112 released cells (five cargo cells), 16 chunks, ship/finite fuel and partial damage into 18,423 bytes. Encode time is not separately measured. Exact IEEE-754 GPU restoration, content-table reordering and interrupted/corrupt-write recovery pass. Do not infer final cloud quotas or save-growth budgets from this single preset.

Standalone save/load verification (`evidence/B-persistence-player.txt`): 576 rotating cargo cells, 16 chunks, 25,891-byte checkpoint, exact disk roundtrip. Sampled movement frame p95 17.068 ms, CPU p95 17.067 ms, GPU p95 16.576 ms; save/load wait frames excluded. Screenshot inspected after the restored session rendered.

## B.2 fuel/identity checkpoint — 2026-09-07

Mac standalone at explicitly requested 1440×900 window, Apple M4 Pro, Metal, Unity 6000.3.11f1. Same 576-cell rotating starter, 16 chunks/8,192 capacity: frame p95 17.572 ms, CPU p95 17.560 ms, GPU p95 16.309 ms; 3,605,252 explicit GPU-buffer bytes, 34 dispatches. A partly consumed tank released and recovered eight cells without changing remaining energy, then saved and loaded the site exactly (25,998 bytes, schema 2).

End-to-end asynchronous spill/pump latency: 258/219 ms. Simulation is fenced during these explicit commands. These numbers include GPU readback waits and CPU occupancy verification; they are not suitable per-frame transfer costs. Moving bounded transfer admission onto GPU is a performance follow-up. `evidence/B-fuel-player.txt` and `evidence/B-fuel-tests.xml` (29/29) record this checkpoint.

Correction to the earlier persistence standalone configuration: its inspected image was 3024×1890, inherited from player settings. Its measurements remain valid for that resolution; the fuel checkpoint explicitly pins 1440×900.

## B.3 damaged starter checkpoint — 2026-09-07

Same Mac/Metal development player at 1440×900. The preset seeds 576 cargo cells, severs three lower-rail cells to detach the lower thruster, then samples 300 rotating steps. Final state: 579 loose cells, one rigid fragment, 16 active chunks, 8,192 capacity, 38 dispatches and 4,654,876 explicit buffer bytes. Frame p95 17.584 ms, CPU p95 17.578 ms, GPU p95 9.254 ms. Serial candidate-hull scanning initially measured 23.388 ms/frame; parallel hull/cargo collision checks removed that bottleneck while retaining the same geometric tests.

All matter is conserved/non-overlapping and fragment/cargo/fuel state survives disk restoration (26,358 bytes). Spill/pump transaction latency is 325/279 ms; these explicit snapshot waits are excluded from sampled movement. Four instanced matter draws plus UI. Screenshot inspected: severed lower rail/thruster is separated visibly and cargo is spilling from the open boundary. Evidence: `evidence/B-fragment-player.txt`, `B-fragment-showcase.png`, `B-fragment-tests.xml` (36/36). The full 2,500-cell cavity is correctness-tested, but its standalone frame-time budget and large-region streaming remain B.5.

## B.4 sparse world checkpoint — 2026-09-07

EditMode: 38/38 tests. The changed-site fixture persists four dirty chunks, loose cells in spatial buckets (including a partly consumed fuel cell), ship and one detached thruster. Initial state uses 12 immutable blobs; first preparation/write/verification takes 58 ms. Repeating an unchanged checkpoint adds no blobs. Across repeated saves, an interrupted candidate, two sites and leave/revisit, the fixture retains 49 files / 49,140 bytes including historical dependencies. These are bounded mixed-state measurements, not the 100,000 heavy-site target.

Mac development player, Metal, Apple M4 Pro, 1440×900: 579 cells/one fragment, 16 chunks/8,192 capacity. Frame p95 18.602 ms, CPU p95 18.606 ms, GPU p95 11.752 ms. Physical fuel spill/pump takes 335/285 ms. Departure → reload at site 02 → return to site 01 takes 1,269 ms total across explicit paused transactions. The resulting two-site directory is 54,104 bytes, including index generations/backups. Terrain, loose cells, cargo and deposited fragment validate exactly after return; the player ship uses a physically clear arrival berth. There is no per-frame site payload loading.

Evidence: `evidence/B-world-tests.xml`, `B-world-player.txt`, `B-world-showcase.png` (inspected). These workloads still seed cargo for the controlled benchmark; the earned mine/collect/spill playable gate and B.5 scaling remain unchecked. Whole active-page GPU readbacks, serial fragment collision, transition resource peaks and retained historical generations need measurement/optimization under B.5.

## B.5 save-growth checkpoint — 2026-09-07

The 38-test suite's two-site fixture reclaimed 15 unreachable files / 10,235 bytes while retaining all four site revisions referenced by the current and recovery roots. Both worlds still reconstruct exactly; damaged inactive metadata defers cleanup without deleting a file. The fixture ends with 35 files / 38,954 bytes, including an intentionally retained unrelated note.

Mac standalone repeats damage, fuel transfers and two-site travel/resume/return with post-publication collection enabled. Final world directory: 38,382 bytes, down from the prior run's 54,104 bytes. Frame p95 18.613 ms, CPU p95 18.621 ms, GPU p95 10.721 ms; travel/resume/return 1,318 ms. Conservation and exact site/cargo/fragment checks pass. Evidence: `evidence/B-world-collection-tests.xml`, `B-world-collection-player.txt`. Large mixed-world collection time and scheduling still need a 100,000-site payload workload.

## B.3R parallel proof — correctness stop

Source `2a61ff2`, M4 Pro/Metal/Unity 6000.3.11f1, 1280×800 development player, v-sync off. All three profiles failed the necessary 2,500-grain packed-cavity correctness case before performance sampling. At rest under thrust/torque, all rejected tick 3; wall maxima .001183/.001115/.001022 for 4/2, 8/4, 12/6 exceed .001. Shared-motion cases also fail. Physics GPU p95, total GPU p95, frame p95 and 8,192-grain performance are unmeasured; no speedup or performance pass is claimed. [Canonical experiment](evidence/B3R-parallel-experiment.md) records scope, raw results, one build/player run and all rerun reasons. Historical occupancy-based packed-capacity claims do not satisfy this replacement contract.

Delegated proof checkpoint `5898f8d`: geometry, rigid manifolds and compact GPU metrics now have 30 passing focused tests and a successful matching Mac build. The player rigid impact passes; all packed profiles still fail (rest-under-force wall maxima .001186/.001123/.001034). GPU p95 remains unmeasured and integration stays stopped. [Updated canonical evidence](evidence/B3R-parallel-pieces.md).
