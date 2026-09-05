# Performance

Unity 6000.3.11f1 (3000ef702840) is installed. No project benchmarks have been run yet. This document is the recording location for measured hardware, build configuration, scene preset, FPS/frame-time percentile, active/visible/sleeping chunks, debris count, buffer use, draw calls, save payload size, and simulation/render timing.

Targets are 60 FPS and a 50 FPS minimum during normal development-machine gameplay, with no ordinary chunk-load spikes. The diagnostic and stress-scene requirements are defined in `ARCHITECTURE.md` and will be implemented before performance claims are made.

## Implementation checklist

IDs refer to [EXECUTION_PLAN](EXECUTION_PLAN.md); only verified work is checked.

- [ ] A.GATE Mac frame time, GPU time, memory, scaling.
- [ ] B.5 Large-ship/streaming/site-index stress.
- [ ] E.5 Full regression performance.
