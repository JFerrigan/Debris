# Performance

Unity 6000.3.11f1 (3000ef702840) is installed. No project benchmarks have been run yet. This document is the recording location for measured hardware, build configuration, scene preset, FPS/frame-time percentile, active/visible/sleeping chunks, debris count, buffer use, draw calls, save payload size, and simulation/render timing.

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
