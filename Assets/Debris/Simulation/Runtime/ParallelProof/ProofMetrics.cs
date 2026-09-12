using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

namespace Debris.Simulation.ParallelProof
{
    // The only per-sample data returned by the GPU metrics pass. The baseline and
    // running maxima are kept on the GPU; this remains a compact 80-byte readback.
    [StructLayout(LayoutKind.Sequential, Pack = 4, Size = 80)]
    public struct ProofMetricsReadback
    {
        public Vector2 LinearMomentum;
        public float AngularMomentum;
        public float KineticEnergy;
        public Vector2 MaximumMomentumError;
        public float MaximumAngularMomentumError;
        public float MaximumEnergyGain;
        public uint GrainCount;
        public uint IdentitySum;
        public uint IdentityXor;
        public uint NonfiniteCount;
        public uint SampleCount;
        public uint MaximumNonfiniteCount;
        public uint MaximumGrainCountError;
        public uint InitialGrainCount;
        public uint InitialIdentitySum;
        public uint InitialIdentityXor;
        public float InitialMomentumScale;
        public float InitialAngularMomentumScale;

        public float MaximumMomentumErrorMagnitude => MaximumMomentumError.magnitude;
    }

    // Named view of the existing 16-word solver facts. Sum/XOR identity checks
    // are diagnostics only and do not prove uniqueness.
    public readonly struct ProofDiagnostics
    {
        public const int WordCount = 16;

        public SolverFault Fault { get; }
        public uint CompletedTick { get; }
        public uint Substeps { get; }
        public float MaximumSpeed { get; }
        public uint MaximumBinOccupancy { get; }
        public uint CandidateCount { get; }
        public uint MaximumContactsPerGrain { get; }
        public uint MaximumIncidentContactsPerGrain { get; }
        public float MaximumGrainPenetration { get; }
        public float MaximumSolidPenetration { get; }
        public uint EnvelopeViolations { get; }
        public uint RejectedTicks { get; }
        public uint ValidationActive { get; }
        public uint RigidManifoldCount { get; }
        public uint MaximumRigidPointsPerPair { get; }
        public float MaximumRigidPenetration { get; }

        ProofDiagnostics(uint[] words)
        {
            Fault = (SolverFault)words[0];
            CompletedTick = words[1];
            Substeps = words[2];
            MaximumSpeed = BitsToFloat(words[3]);
            MaximumBinOccupancy = words[4];
            CandidateCount = words[5];
            MaximumContactsPerGrain = words[6];
            MaximumIncidentContactsPerGrain = words[7];
            MaximumGrainPenetration = BitsToFloat(words[8]);
            MaximumSolidPenetration = BitsToFloat(words[9]);
            EnvelopeViolations = words[10];
            RejectedTicks = words[11];
            ValidationActive = words[12];
            RigidManifoldCount = words[13];
            MaximumRigidPointsPerPair = words[14];
            MaximumRigidPenetration = BitsToFloat(words[15]);
        }

        public static ProofDiagnostics From(uint[] words)
        {
            if (words == null) throw new ArgumentNullException(nameof(words));
            if (words.Length < WordCount) throw new ArgumentException("Expected 16 proof diagnostic words", nameof(words));
            return new ProofDiagnostics(words);
        }

        static float BitsToFloat(uint bits) => BitConverter.Int32BitsToSingle(unchecked((int)bits));
    }

    // GPU reductions for one fixed population. Construct once with the grain and
    // body counts, call Record after a state update, and read only the compact result.
    public sealed class ProofMetrics : IDisposable
    {
        [StructLayout(LayoutKind.Sequential, Pack = 4, Size = 48)]
        struct MetricsBaseline
        {
            public Vector2 Momentum;
            public float AngularMomentum;
            public float Energy;
            public float MomentumScale;
            public float AngularScale;
            public uint GrainCount;
            public uint IdentitySum;
            public uint IdentityXor;
            public uint Reserved;
            public uint Reserved1;
            public uint Reserved2;
        }

        const int Threads = 256;
        const int PartialStride = 48;
        const int BaselineStride = 48;
        const int ResultStride = 80;

        readonly ComputeShader shader;
        readonly int contributeKernel, reduceKernel, finalizeKernel;
        readonly GraphicsBuffer partials, groups, baseline, result;
        readonly bool ownsResult;
        readonly int grainCount, bodyCount, totalCount, groupCount;
        bool disposed;

        public long AllocatedBytes { get; }
        public GraphicsBuffer ReadbackBuffer => result;

        public ProofMetrics(int grainCount, int bodyCount)
            : this(grainCount, bodyCount, null) { }

        // An owner may provide a one-element, 80-byte structured buffer so the
        // result is included in its existing allocation/lifetime accounting.
        public ProofMetrics(int grainCount, int bodyCount, GraphicsBuffer resultBuffer)
        {
            if (grainCount < 0) throw new ArgumentOutOfRangeException(nameof(grainCount));
            if (bodyCount < 0) throw new ArgumentOutOfRangeException(nameof(bodyCount));
            totalCount = grainCount + bodyCount;
            if (totalCount < grainCount) throw new ArgumentException("Proof population is too large");
            if (resultBuffer != null && (resultBuffer.count != 1 || resultBuffer.stride != ResultStride))
                throw new ArgumentException("The proof metrics result buffer must contain one 80-byte element", nameof(resultBuffer));
            this.grainCount = grainCount;
            this.bodyCount = bodyCount;
            groupCount = Math.Max(1, (totalCount + Threads - 1) / Threads);

            var asset = Resources.Load<ComputeShader>("ParallelMetrics");
            if (asset == null) throw new InvalidOperationException("ParallelMetrics.compute is unavailable");
            shader = UnityEngine.Object.Instantiate(asset);
            contributeKernel = shader.FindKernel("Contribute");
            reduceKernel = shader.FindKernel("Reduce");
            finalizeKernel = shader.FindKernel("Finalize");
            partials = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Math.Max(1, groupCount * Threads), PartialStride);
            groups = new GraphicsBuffer(GraphicsBuffer.Target.Structured, groupCount, PartialStride);
            baseline = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, BaselineStride);
            result = resultBuffer ?? new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, ResultStride);
            ownsResult = resultBuffer == null;
            baseline.SetData(new MetricsBaseline[1]);
            result.SetData(new ProofMetricsReadback[1]);
            AllocatedBytes = (long)Math.Max(1, groupCount * Threads) * PartialStride +
                             (long)groupCount * PartialStride + BaselineStride +
                             (ownsResult ? ResultStride : 0);
        }

        public void Record(CommandBuffer commandBuffer, GraphicsBuffer state, GraphicsBuffer parameters, GraphicsBuffer grains)
        {
            if (disposed) throw new ObjectDisposedException(nameof(ProofMetrics));
            if (commandBuffer == null) throw new ArgumentNullException(nameof(commandBuffer));
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (parameters == null) throw new ArgumentNullException(nameof(parameters));
            if (grains == null) throw new ArgumentNullException(nameof(grains));

            commandBuffer.BeginSample("B3R.ParallelMetrics");
            SetCounts(commandBuffer);
            commandBuffer.SetComputeBufferParam(shader, contributeKernel, "_State", state);
            commandBuffer.SetComputeBufferParam(shader, contributeKernel, "_Parameters", parameters);
            commandBuffer.SetComputeBufferParam(shader, contributeKernel, "_Grains", grains);
            commandBuffer.SetComputeBufferParam(shader, contributeKernel, "_Partials", partials);
            commandBuffer.DispatchCompute(shader, contributeKernel, groupCount, 1, 1);
            SetCounts(commandBuffer);
            commandBuffer.SetComputeBufferParam(shader, reduceKernel, "_Partials", partials);
            commandBuffer.SetComputeBufferParam(shader, reduceKernel, "_Groups", groups);
            commandBuffer.DispatchCompute(shader, reduceKernel, groupCount, 1, 1);
            SetCounts(commandBuffer);
            commandBuffer.SetComputeBufferParam(shader, finalizeKernel, "_Groups", groups);
            commandBuffer.SetComputeBufferParam(shader, finalizeKernel, "_Baseline", baseline);
            commandBuffer.SetComputeBufferParam(shader, finalizeKernel, "_Result", result);
            commandBuffer.DispatchCompute(shader, finalizeKernel, 1, 1, 1);
            commandBuffer.EndSample("B3R.ParallelMetrics");
        }

        void SetCounts(CommandBuffer commandBuffer)
        {
            commandBuffer.SetComputeIntParam(shader, "_GrainCount", grainCount);
            commandBuffer.SetComputeIntParam(shader, "_BodyCount", bodyCount);
            commandBuffer.SetComputeIntParam(shader, "_TotalCount", totalCount);
            commandBuffer.SetComputeIntParam(shader, "_GroupCount", groupCount);
        }

        public Task<ProofMetricsReadback> ReadAsync()
        {
            if (disposed) throw new ObjectDisposedException(nameof(ProofMetrics));
            var source = new TaskCompletionSource<ProofMetricsReadback>();
            AsyncGPUReadback.Request(result, request =>
            {
                if (request.hasError)
                    source.SetException(new InvalidOperationException("Proof metrics GPU readback failed"));
                else
                    source.SetResult(request.GetData<ProofMetricsReadback>()[0]);
            });
            return source.Task;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            partials.Dispose();
            groups.Dispose();
            baseline.Dispose();
            if (ownsResult) result.Dispose();
            if (Application.isPlaying) UnityEngine.Object.Destroy(shader);
            else UnityEngine.Object.DestroyImmediate(shader);
        }
    }

    public readonly struct ProofGpuTimingSample
    {
        public readonly bool Valid;
        public readonly long GpuElapsedNanoseconds;
        public readonly int GpuSampleBlockCount;
        public readonly int DelayedFrame;

        public ProofGpuTimingSample(bool valid, long gpuElapsedNanoseconds, int gpuSampleBlockCount, int delayedFrame)
        {
            Valid = valid;
            GpuElapsedNanoseconds = gpuElapsedNanoseconds;
            GpuSampleBlockCount = gpuSampleBlockCount;
            DelayedFrame = delayedFrame;
        }
    }

    // Unity's GPU Recorder exposes the sample three frames late. A missing or
    // invalid sample is deliberately not converted into a zero or a PASS.
    public sealed class ProofGpuTimingCollector : IDisposable
    {
        public const int RecorderDelayFrames = 3;
        Recorder recorder;
        readonly string marker;
        readonly Queue<int> submittedFrames = new Queue<int>(8);
        int lastSubmittedFrame = -1, lastReadFrame = -1;
        bool disposed;

        public int ValidSampleCount { get; private set; }
        public int InvalidSampleCount { get; private set; }
        public bool IsAvailable => !disposed && recorder.isValid && SystemInfo.supportsGpuRecorder;
        public bool CanPass => IsAvailable && ValidSampleCount > 0 && InvalidSampleCount == 0 && PendingDelayedSamples == 0;
        public int PendingDelayedSamples => submittedFrames.Count;

        public ProofGpuTimingCollector(string marker = "B3R.ParallelMetrics")
        {
            this.marker = marker ?? throw new ArgumentNullException(nameof(marker));
            recorder = Recorder.Get(marker);
            if (recorder.isValid) recorder.enabled = true;
        }

        public void MarkSubmittedFrame()
        {
            if(disposed || lastSubmittedFrame==Time.frameCount)return;
            lastSubmittedFrame=Time.frameCount;submittedFrames.Enqueue(lastSubmittedFrame);
        }

        // Begin the measured window after warmup; keep the recorder enabled.
        public void ResetWindow()
        {
            submittedFrames.Clear();ValidSampleCount=0;InvalidSampleCount=0;lastReadFrame=-1;
        }

        // Poll on every rendered frame, including the final three drain frames.
        // If polling skipped a result frame, that sample is invalid, never reused.
        public bool TryRead(out ProofGpuTimingSample sample)
        {
            sample=default;int frame=Time.frameCount;
            if(disposed || submittedFrames.Count==0 || frame==lastReadFrame)return false;
            int target=frame-RecorderDelayFrames;
            while(submittedFrames.Count>0 && submittedFrames.Peek()<target){submittedFrames.Dequeue();InvalidSampleCount++;}
            if(submittedFrames.Count==0 || submittedFrames.Peek()>target)return false;
            int sourceFrame=submittedFrames.Dequeue();lastReadFrame=frame;
            if(!recorder.isValid){recorder=Recorder.Get(marker);if(recorder.isValid)recorder.enabled=true;}
            long nanoseconds=IsAvailable?recorder.gpuElapsedNanoseconds:0;
            int blocks=IsAvailable?recorder.gpuSampleBlockCount:0;
            bool valid=nanoseconds>0 && blocks>0;
            sample=new ProofGpuTimingSample(valid,nanoseconds,blocks,sourceFrame);
            if(valid)ValidSampleCount++;else InvalidSampleCount++;
            return valid;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (recorder.isValid) recorder.enabled = false;
        }
    }
}
