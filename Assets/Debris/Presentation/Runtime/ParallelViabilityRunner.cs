using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Debris.Simulation.ParallelProof;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace Debris.Presentation
{
    // Separate opt-in checkpoint runner. A fault ends useful work; delayed no-op frames never pass.
    public sealed class ParallelViabilityRunner : MonoBehaviour
    {
        sealed class CaseResult
        {
            public string Name;
            public int Attempted, Committed, FirstFaultTick, FirstStrictCrossing;
            public uint[] Facts;
            public float GrainMaximum, SolidMaximum;
            public long Buffers;
            public int ValidPhysics, InvalidPhysics, ValidTotal, FrameGaps;
            public double PhysicsP95 = double.NaN, TotalP95 = double.NaN, FrameP95 = double.NaN,
                SubmissionP95 = double.NaN;
            public long SubmissionAllocationMaximum;
            public bool Complete, Strict, Interim, TimingMatched;
        }

        ParallelGrainSolver solver;
        Material grainsMaterial, boundariesMaterial, terrainMaterial;
        GraphicsBuffer terrainCells;
        readonly List<GraphicsBuffer> terrainChunks = new List<GraphicsBuffer>(16);
        long terrainBytes;
        readonly List<string> lines = new List<string>();
        readonly FrameTiming[] frameTiming = new FrameTiming[1];
        string output;
        int exitCode = 3;
        bool infrastructureFailed;
        int drawnGrains, drawnBoundaries;

        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs();
            int outputArg = Array.IndexOf(args, "-debrisProofOutput");
            output = outputArg >= 0 && outputArg + 1 < args.Length ? args[outputArg + 1]
                : Path.Combine(Application.temporaryCachePath, "B3R-physics-viability.txt");
            Application.logMessageReceived += OnUnityLog;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            Screen.SetResolution(1280, 800, false);
            // Screen.SetResolution is applied after the current frame.
            for (int i = 0; i < 3; i++) yield return null;
            Camera.main.orthographic = true;
            Camera.main.orthographicSize = 256;
            Camera.main.transform.position = new Vector3(0, 0, -10);
            grainsMaterial = new Material(Resources.Load<Shader>("ParallelProof"));
            boundariesMaterial = new Material(grainsMaterial);
            boundariesMaterial.SetInt("_DrawBoundaries", 1);
            terrainMaterial = new Material(grainsMaterial);
            CreateTerrainField();
            Record($"B3R_VIABILITY revision=2 utc={DateTime.UtcNow:O} unity={Application.unityVersion} device={SystemInfo.graphicsDeviceName} api={SystemInfo.graphicsDeviceType} screen={Screen.width}x{Screen.height} vsync={QualitySettings.vSyncCount} cap={Application.targetFrameRate} dt=1/60");
            Record("thresholds grain/solid=.002 runtime, rigid/solid=.001 runtime, original solid=.001; profiles=4/2,8/4,12/6; trace=off");
            Record($"fixture bay=2500 pile=1000 exterior=4692 active=8192 fragments=16 anchoredPlatform=1 boundaries=21 material=1 density=1 camera=(0,0) ortho=256 renderer=ParallelProof procedural; terrainChunks=16 chunkSize=128 filledTerrainCells=42 terrainBufferBytes={terrainBytes} solverPlatform=one equivalent rigid patch");
            Flush();
            try
            {
                int qualified = 0;
                foreach (int velocity in new[] { 4, 8, 12 })
                {
                    int position = velocity / 2;
                    var shared = new CaseResult { Name = "canonical-shared" };
                    yield return RunCorrectness(ProofFixtures.Packed(true), velocity, 120, 120, shared);
                    var forced = new CaseResult { Name = "canonical-forced" };
                    yield return RunCorrectness(ProofFixtures.Packed(false), velocity, 120, 120, forced);
                    var extended = new CaseResult { Name = "extended-shared" };
                    yield return RunCorrectness(ProofFixtures.Packed(true), velocity, 720, 120, extended);
                    var combined = new CaseResult { Name = "combined-8192" };
                    yield return RunCombined(velocity, combined);
                    bool accepted = shared.Complete && forced.Complete && extended.Complete && combined.Complete
                        && shared.Strict && forced.Strict && extended.Strict && combined.Strict
                        && combined.ValidPhysics == 600 && combined.ValidTotal == 600
                        && combined.PhysicsP95 <= 8 && combined.TotalP95 <= 12
                        && combined.FrameP95 <= 20 && combined.SubmissionP95 <= 2
                        && combined.SubmissionAllocationMaximum == 0 && combined.Buffers <= 128L * 1048576
                        && Screen.width == 1280 && Screen.height == 800 && combined.TimingMatched;
                    if (accepted) qualified++;
                    Record($"PROFILE_VERDICT profile={velocity}/{position} qualified={accepted} canonicalShared={shared.Complete} canonicalForced={forced.Complete} extended={extended.Complete} combined={combined.Complete}");
                    Flush();
                }
                exitCode = infrastructureFailed ? 3 : qualified > 0 ? 0 : 2;
                Record($"CHECKPOINT_RESULT qualifiedProfiles={qualified} exit={exitCode} fullR1=false");
            }
            finally
            {
                solver?.Dispose(); solver = null; drawnGrains = drawnBoundaries = 0;
                Flush();
                Debug.Log("B3R_VIABILITY_OUTPUT " + output);
                Application.Quit(exitCode);
            }
        }

        IEnumerator RunCorrectness(ProofFixture fixture, int velocity, int tickLimit, int forcedTicks, CaseResult result)
        {
            solver = fixture.Create(velocity, velocity / 2);
            Bind(fixture.Grains.Length, fixture.Boundaries.Length);
            result.Buffers = solver.BufferBytes + terrainBytes;
            uint[] facts = null;
            for (int tick = 1; tick <= tickLimit; tick++)
            {
                solver.Step(tick <= forcedTicks ? fixture.Force : Vector2.zero,
                    tick <= forcedTicks ? fixture.Torque : 0);
                result.Attempted++;
                var task = solver.DiagnosticsAsync();
                while (!task.IsCompleted) yield return null;
                if (task.IsFaulted) throw task.Exception;
                facts = task.Result;
                result.Committed = (int)facts[1];
                if (result.FirstStrictCrossing == 0 && BitConverter.Int32BitsToSingle((int)facts[9]) > .001f)
                    result.FirstStrictCrossing = tick;
                if (facts[0] != 0) { result.FirstFaultTick = tick; break; }
                yield return null;
            }
            var snapshot = solver.SnapshotAsync();
            while (!snapshot.IsCompleted) yield return null;
            if (snapshot.IsFaulted) throw snapshot.Exception;
            result.Facts = facts;
            result.GrainMaximum = snapshot.Result.GrainPenetration;
            result.SolidMaximum = snapshot.Result.SolidPenetration;
            result.Complete = result.FirstFaultTick == 0 && result.Committed == tickLimit;
            result.Strict = result.Complete && result.SolidMaximum <= .001f;
            result.Interim = result.Complete && result.SolidMaximum <= .002f;
            Record($"CASE profile={velocity}/{velocity / 2} name={result.Name} attempted={result.Attempted} committed={result.Committed} fault={(SolverFault)(facts?[0] ?? 0)} firstFault={result.FirstFaultTick} firstStrictCrossing={result.FirstStrictCrossing} maxGrain={result.GrainMaximum:R} maxSolid={result.SolidMaximum:R} strict={result.Strict} interim={result.Interim} identityCount={snapshot.Result.Grains.Length} buffers={result.Buffers}");
            Flush();
            solver.Dispose(); solver = null; drawnGrains = drawnBoundaries = 0;
            yield return null;
        }

        IEnumerator RunCombined(int velocity, CaseResult result)
        {
            var fixture = ProofViabilityFixture.Create();
            solver = fixture.Create(velocity, velocity / 2);
            Bind(fixture.Grains.Length, fixture.Boundaries.Length);
            result.Buffers = solver.BufferBytes + terrainBytes;
            var pending = new Queue<(int tick, Task<uint[]> task)>();
            var physics = new double[600]; var total = new double[600];
            var frame = new double[600]; var submission = new double[600];
            int physicsCount = 0, totalCount = 0, frameCount = 0, submissionCount = 0;
            int previousFrame = -1;
            using (var gpu = new ProofGpuTimingCollector("B3R.ParallelPhysics"))
            {
                for (int tick = 1; tick <= 720; tick++)
                {
                    if (result.FirstFaultTick != 0) break;
                    if (previousFrame >= 0 && Time.frameCount != previousFrame + 1) result.FrameGaps++;
                    previousFrame = Time.frameCount;
                    long allocated = GC.GetAllocatedBytesForCurrentThread();
                    long started = Stopwatch.GetTimestamp();
                    solver.Step();
                    double elapsed = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
                    long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
                    pending.Enqueue((tick, solver.DiagnosticsAsync()));
                    result.Attempted++;
                    if (tick == 120) gpu.ResetWindow();
                    if (tick > 120)
                    {
                        int i = tick - 121;
                        submission[submissionCount++] = elapsed;
                        frame[frameCount++] = Time.unscaledDeltaTime * 1000;
                        result.SubmissionAllocationMaximum = Math.Max(result.SubmissionAllocationMaximum, bytes);
                        gpu.MarkSubmittedFrame();
                        FrameTimingManager.CaptureFrameTimings();
                        if (FrameTimingManager.GetLatestTimings(1, frameTiming) > 0 && frameTiming[0].gpuFrameTime > 0)
                            total[totalCount++] = frameTiming[0].gpuFrameTime;
                    }
                    while (pending.Count > 0 && pending.Peek().task.IsCompleted)
                    {
                        var item = pending.Dequeue();
                        if (item.task.IsFaulted) throw item.task.Exception;
                        var facts = item.task.Result;
                        result.Facts = facts;
                        result.Committed = (int)facts[1];
                        if (result.FirstStrictCrossing == 0 && BitConverter.Int32BitsToSingle((int)facts[9]) > .001f)
                            result.FirstStrictCrossing = item.tick;
                        if (facts[0] != 0 && result.FirstFaultTick == 0) result.FirstFaultTick = item.tick;
                    }
                    if (gpu.TryRead(out var sample) && sample.Valid && sample.DelayedFrame >= 0 && physicsCount < 600)
                        physics[physicsCount++] = sample.GpuElapsedNanoseconds / 1000000.0;
                    yield return null;
                }
                while (pending.Count > 0)
                {
                    var item = pending.Peek();
                    if (!item.task.IsCompleted) { yield return null; continue; }
                    pending.Dequeue();
                    if (item.task.IsFaulted) throw item.task.Exception;
                    var facts = item.task.Result;
                    result.Facts = facts; result.Committed = (int)facts[1];
                    if (facts[0] != 0 && result.FirstFaultTick == 0) result.FirstFaultTick = item.tick;
                }
                for (int i = 0; i < 4; i++)
                {
                    if (gpu.TryRead(out var sample) && sample.Valid && physicsCount < 600)
                        physics[physicsCount++] = sample.GpuElapsedNanoseconds / 1000000.0;
                    yield return null;
                }
                result.ValidPhysics = physicsCount;
                result.InvalidPhysics = gpu.InvalidSampleCount + gpu.PendingDelayedSamples;
            }
            var snapshot = solver.SnapshotAsync();
            while (!snapshot.IsCompleted) yield return null;
            if (snapshot.IsFaulted) throw snapshot.Exception;
            result.GrainMaximum = snapshot.Result.GrainPenetration;
            result.SolidMaximum = snapshot.Result.SolidPenetration;
            result.ValidTotal = totalCount;
            result.Complete = result.FirstFaultTick == 0 && result.Committed == 720 && result.FrameGaps == 0;
            result.Strict = result.Complete && result.SolidMaximum <= .001f;
            result.Interim = result.Complete && result.SolidMaximum <= .002f;
            result.PhysicsP95 = Percentile(physics, physicsCount);
            result.TotalP95 = Percentile(total, totalCount);
            result.FrameP95 = Percentile(frame, frameCount);
            result.SubmissionP95 = Percentile(submission, submissionCount);
            Record($"CASE profile={velocity}/{velocity / 2} name=combined-8192 attempted={result.Attempted} committed={result.Committed} fault={(SolverFault)(result.Facts?[0] ?? 0)} firstFault={result.FirstFaultTick} firstStrictCrossing={result.FirstStrictCrossing} maxGrain={result.GrainMaximum:R} maxSolid={result.SolidMaximum:R} buffers={result.Buffers} physicsValid={physicsCount}/600 physicsInvalid={result.InvalidPhysics} physicsP95={result.PhysicsP95:F3} totalGpuObservations={totalCount}/600 totalGpuP95={result.TotalP95:F3} frameObservations={frameCount}/600 frameP95={result.FrameP95:F3} submissionObservations={submissionCount}/600 submissionP95={result.SubmissionP95:F3} submissionAllocMax={result.SubmissionAllocationMaximum} frameGaps={result.FrameGaps} timingFrameMatch=unverified terrainChunks=16 filledTerrainCells=42 result={(result.Complete ? "COMPLETE" : "INCOMPLETE_CORRECTNESS")}");
            Flush();
            solver.Dispose(); solver = null; drawnGrains = drawnBoundaries = 0;
            yield return null;
        }

        static double Percentile(double[] values, int count)
        {
            if (count == 0) return double.NaN;
            Array.Sort(values, 0, count);
            return values[(int)Math.Ceiling(.95 * count) - 1];
        }

        void Bind(int grainCount, int boundaryCount)
        {
            drawnGrains = grainCount; drawnBoundaries = boundaryCount;
            foreach (var m in new[] { grainsMaterial, boundariesMaterial })
            {
                m.SetBuffer("_Grains", solver.Grains); m.SetBuffer("_Bodies", solver.Bodies);
                m.SetBuffer("_Boundaries", solver.Boundaries); m.SetBuffer("_Parameters", solver.Parameters);
            }
            terrainMaterial.SetBuffer("_Grains", terrainCells);
            terrainMaterial.SetBuffer("_Bodies", solver.Bodies);
            terrainMaterial.SetBuffer("_Boundaries", solver.Boundaries);
            terrainMaterial.SetBuffer("_Parameters", solver.Parameters);
        }

        void CreateTerrainField()
        {
            var cells = new LooseCell[42];
            for (int i = 0; i < 42; i++)
                cells[i] = new LooseCell { Center = new Vector2(-160.5f + i, 67), Material = 1, Identity = (uint)(8193 + i) };
            terrainCells = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cells.Length, 48);
            terrainCells.SetData(cells);
            terrainBytes += cells.Length * 48;
            // Four by four 128² fields cover [-256,256) on each axis.
            for (int chunk = 0; chunk < 16; chunk++)
            {
                var buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 128 * 128, 4);
                terrainChunks.Add(buffer);
                terrainBytes += 128L * 128 * 4;
                if (chunk != 8 && chunk != 9) continue;
                var field = new uint[128 * 128];
                foreach (var cell in cells)
                {
                    int x = Mathf.FloorToInt(cell.Center.x) + 256;
                    int y = Mathf.FloorToInt(cell.Center.y) + 256;
                    if (y / 128 * 4 + x / 128 == chunk) field[(y % 128) * 128 + x % 128] = 1;
                }
                buffer.SetData(field);
            }
        }

        void Update()
        {
            if (solver == null) return;
            Graphics.DrawProcedural(grainsMaterial, new Bounds(Vector3.zero, Vector3.one * 1024), MeshTopology.Triangles, 6, drawnGrains);
            Graphics.DrawProcedural(boundariesMaterial, new Bounds(Vector3.zero, Vector3.one * 1024), MeshTopology.Triangles, 6, drawnBoundaries);
            Graphics.DrawProcedural(terrainMaterial, new Bounds(Vector3.zero, Vector3.one * 1024), MeshTopology.Triangles, 6, 42);
        }
        void Record(string value) { lines.Add(value); Debug.Log(value); }
        void Flush() { Directory.CreateDirectory(Path.GetDirectoryName(output) ?? "."); File.WriteAllLines(output, lines); }
        void OnUnityLog(string message, string stack, LogType type)
        {
            if (type != LogType.Exception) return;
            infrastructureFailed = true;
            exitCode = 3;
            lines.Add("INFRASTRUCTURE_EXCEPTION " + message + " " + stack);
            Flush();
            Application.Quit(3);
        }
        void OnDestroy() { Application.logMessageReceived -= OnUnityLog; solver?.Dispose(); terrainCells?.Dispose(); foreach (var chunk in terrainChunks) chunk.Dispose(); if (grainsMaterial) Destroy(grainsMaterial); if (boundariesMaterial) Destroy(boundariesMaterial); if (terrainMaterial) Destroy(terrainMaterial); }
    }
}
