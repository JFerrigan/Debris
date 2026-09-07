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
