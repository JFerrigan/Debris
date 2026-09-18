using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace Debris.Simulation.ParallelProof
{
    public enum ProofTraceStage : uint
    {
        SubstepStart = 0,
        BeforeVelocity = 1,
        VelocityEvaluated = 2,
        VelocityApplied = 3,
        Predicted = 4,
        PositionEvaluated = 5,
        PositionApplied = 6,
        Validation = 7
    }

    // The defaults cover the packed acceptance case without allowing a trace
    // to consume an unbounded amount of persistent GPU memory. A caller which
    // deliberately needs all sixteen substeps can opt into that explicitly.
    public sealed class ProofTraceConfiguration
    {
        public int SubstepCapacity = 4;
        public bool CaptureAppliedContacts;
        public int ContactCapacity = ProofTrace.MaxContactCapacity;

        internal void Validate(int grains, int endpoints, int boundaries, int sourceContacts, int velocity, int position)
        {
            if (SubstepCapacity < 1 || SubstepCapacity > 16 || ContactCapacity < 1 || ContactCapacity > ProofTrace.MaxContactCapacity)
                throw new ArgumentOutOfRangeException("Invalid diagnostic trace capacity");
            long checkpoints = (long)SubstepCapacity * (4 + 2 * velocity + 2 * position);
            long contactCheckpoints = (long)SubstepCapacity * (2 + velocity + position + (CaptureAppliedContacts ? velocity + position : 0));
            long bytes = checkpoints * (40 + 64 + 32 + Math.Max(1, endpoints) * 48L) +
                Math.Min(sourceContacts, ContactCapacity) * contactCheckpoints * 96 + Math.Max(1, grains) * 4L +
                Math.Max(1, endpoints) * 32L + Math.Max(1, boundaries) * 32L + SubstepCapacity * 4L;
            if (bytes > ProofTrace.MaxTraceBytes) throw new ArgumentException("Trace allocation exceeds the diagnostic GPU budget");
        }
    }

    [Serializable]
    [StructLayout(LayoutKind.Sequential, Pack = 4, Size = 40)]
    public struct ProofTraceCheckpoint
    {
        public uint Tick;
        public uint Substep;
        public ProofTraceStage Stage;
        public uint Iteration;
        public uint ContactCount;
        public uint CapturedContactCount;
        public uint StateOffset;
        public uint ContactOffset;
        public uint Flags;
        public uint Reserved;

        public bool HasContacts => CapturedContactCount != 0;
        public bool Truncated => (Flags & (1u | 2u | 8u)) != 0;
    }

    [Serializable]
    [StructLayout(LayoutKind.Sequential, Pack = 4, Size = 48)]
    public struct ProofTraceState
    {
        public Vector2 Center;
        public Vector2 Velocity;
        public float Angle;
        public float AngularVelocity;
        public uint Identity;
        public uint Endpoint;
        public uint Degree;
        public uint Flags;
        public uint Reserved0;
        public uint Reserved1;
    }

    // This is intentionally a trace-only record. It preserves the endpoint
    // identities, the geometry used by the evaluation, both effective-mass
    // terms, and the incremental impulse/torque values in one stable record.
    [Serializable]
    [StructLayout(LayoutKind.Sequential, Pack = 4, Size = 96)]
    public struct ProofTraceContact
    {
        public uint A;
        public uint B;
        public uint Feature;
        public uint Flags;
        public uint IdentityA;
        public uint IdentityB;
        public uint EndpointFlags;
        public uint Reserved;
        public Vector2 Normal;
        public float Separation;
        public float NormalLambda;
        public Vector2 ArmA;
        public Vector2 ArmB;
        public float TangentLambda;
        public float PositionLambda;
        public float EffectiveMassA;
        public float EffectiveMassB;
        public Vector2 IncrementalImpulse;
        public float IncrementalTorqueA;
        public float IncrementalTorqueB;
    }

    [Serializable]
    [StructLayout(LayoutKind.Sequential, Pack = 4, Size = 32)]
    public struct ProofTraceSummary
    {
        public uint ContactCount;
        public uint CapturedContactCount;
        public uint GrainContactCount;
        public uint SolidContactCount;
        public float MaximumGrainPenetration;
        public float MaximumSolidPenetration;
        public uint MaximumDegree;
        public uint Flags;
    }

    // Readback compacts valid checkpoints. Diagnostic archives preserve every scalar
    // without changing authoritative serialization or including stale slots.
    [Serializable]
    public sealed class ProofTraceReadback
    {
        public uint Tick;
        public int GrainCount;
        public int EndpointCount;
        public int ContactCapacity;
        public int SubstepCapacity;
        public int PositionIterations;
        public int VelocityIterations;
        public float Dt = 1f / 60f;
        public float Friction = .3f;
        public uint[] Identities = Array.Empty<uint>();
        public BodyParameters[] Parameters = Array.Empty<BodyParameters>();
        public Boundary[] Boundaries = Array.Empty<Boundary>();
        public ProofTraceCheckpoint[] Checkpoints = Array.Empty<ProofTraceCheckpoint>();
        public ProofTraceState[] States = Array.Empty<ProofTraceState>();
        public ProofTraceContact[] Contacts = Array.Empty<ProofTraceContact>();
        public ProofTraceSummary[] Summaries = Array.Empty<ProofTraceSummary>();
        public uint[] Diagnostics = Array.Empty<uint>();

        public bool Truncated
        {
            get
            {
                for (int i = 0; i < Checkpoints.Length; i++)
                    if (Checkpoints[i].Truncated) return true;
                return false;
            }
        }

        public string Export(string pathPrefix) => ProofTraceArchive.Write(this, pathPrefix);
    }

    public sealed class ProofTrace : IDisposable
    {
        public const int MaxContactCapacity = 16384;
        public const long MaxTraceBytes = 512L * 1024L * 1024L;
        const int DiagnosticWords = 16;
        const int StateStride = 48;
        const int ContactStride = 96;
        const int CheckpointStride = 40;
        const int Threads = 64;

        readonly ComputeShader shader;
        readonly int resetKernel, clearSlotKernel, captureKernel;
        readonly GraphicsBuffer identityBuffer, parameterBuffer, boundaryBuffer;
        readonly GraphicsBuffer checkpointBuffer, stateBuffer, contactBuffer, diagnosticBuffer;
        readonly GraphicsBuffer summaryBuffer, markerBuffer;
        readonly int grainCount, endpointCount, boundaryCount, contactCapacity, substepCapacity;
        readonly int positionIterations, velocityIterations, slotsPerSubstep, checkpointCapacity;
        readonly bool captureAppliedContacts;
        readonly float friction;
        readonly uint[] stableIdentities;
        readonly BodyParameters[] traceParameters;
        readonly Boundary[] traceBoundaries;
        uint tick;
        bool disposed;

        public long AllocatedBytes { get; private set; }
        public int CheckpointCapacity => checkpointCapacity;
        public int SubstepCapacity => substepCapacity;
        public int SlotsPerSubstep => slotsPerSubstep;
        public int GetCheckpointSlot(int substep, ProofTraceStage stage, int iteration)
        {
            if (substep < 0 || substep >= substepCapacity) return -1;
            int offset = SlotOffset(stage, iteration);
            return offset < 0 ? -1 : substep * slotsPerSubstep + offset;
        }
        public GraphicsBuffer Checkpoints => checkpointBuffer;
        public GraphicsBuffer States => stateBuffer;
        public GraphicsBuffer Contacts => contactBuffer;

        public ProofTrace(int grainCount, int endpointCount, int contactCapacity,
            int positionIterations, int velocityIterations, LooseCell[] identities,
            BodyParameters[] parameters, Boundary[] boundaries)
            : this(grainCount, endpointCount, contactCapacity, positionIterations,
                velocityIterations, identities, parameters, boundaries, null, .3f) { }

        public ProofTrace(int grainCount, int endpointCount, int contactCapacity,
            int positionIterations, int velocityIterations, LooseCell[] identities,
            BodyParameters[] parameters, Boundary[] boundaries, ProofTraceConfiguration traceConfiguration,
            float friction = .3f)
        {
            if (grainCount < 0 || endpointCount < grainCount || endpointCount > 10017)
                throw new ArgumentOutOfRangeException(nameof(endpointCount));
            if (contactCapacity < 1)
                throw new ArgumentOutOfRangeException(nameof(contactCapacity));
            if (positionIterations < 1 || positionIterations > 128 || velocityIterations < 1 || velocityIterations > 128)
                throw new ArgumentOutOfRangeException(nameof(positionIterations));
            if (identities == null || identities.Length < grainCount) throw new ArgumentException("Grain identities are required", nameof(identities));
            if (parameters == null || parameters.Length != endpointCount) throw new ArgumentException("Endpoint parameters are required", nameof(parameters));
            if (boundaries == null) throw new ArgumentNullException(nameof(boundaries));
            for (int i = 0; i < boundaries.Length; i++)
                if (boundaries[i].Body >= endpointCount)
                    throw new ArgumentException("Boundary body is outside the endpoint range", nameof(boundaries));
            traceConfiguration = traceConfiguration ?? new ProofTraceConfiguration();
            traceConfiguration.Validate(grainCount, endpointCount, boundaries.Length, contactCapacity, velocityIterations, positionIterations);
            contactCapacity = Math.Min(contactCapacity, traceConfiguration.ContactCapacity);
            if (traceConfiguration.SubstepCapacity < 1 || traceConfiguration.SubstepCapacity > 16)
                throw new ArgumentOutOfRangeException(nameof(traceConfiguration.SubstepCapacity));
            if (float.IsNaN(friction) || float.IsInfinity(friction) || friction < 0)
                throw new ArgumentOutOfRangeException(nameof(friction));

            this.grainCount = grainCount;
            this.endpointCount = endpointCount;
            boundaryCount = boundaries.Length;
            this.contactCapacity = contactCapacity;
            this.substepCapacity = traceConfiguration.SubstepCapacity;
            this.positionIterations = positionIterations;
            this.velocityIterations = velocityIterations;
            this.friction = friction;
            captureAppliedContacts = traceConfiguration.CaptureAppliedContacts;
            stableIdentities = new uint[grainCount];
            traceParameters = (BodyParameters[])parameters.Clone();
            traceBoundaries = (Boundary[])boundaries.Clone();
            var knownIdentities = new HashSet<uint>();
            for (int i = 0; i < grainCount; i++)
            {
                uint identity = identities[i].Identity;
                if (identity == 0 || !knownIdentities.Add(identity))
                    throw new ArgumentException("Grain identities must be nonzero and unique", nameof(identities));
                stableIdentities[i] = identity;
            }
            slotsPerSubstep = 4 + velocityIterations * 2 + positionIterations * 2;
            long checkpointCount = (long)substepCapacity * slotsPerSubstep;
            if (checkpointCount > Int32.MaxValue) throw new ArgumentException("Trace checkpoint capacity is too large", nameof(traceConfiguration));
            checkpointCapacity = (int)checkpointCount;
            long estimatedBytes = checkpointCount * (CheckpointStride + DiagnosticWords * 4L + 32L) +
                checkpointCount * Math.Max(1, endpointCount) * StateStride +
                (long)contactCapacity * ContactCheckpointCount() * ContactStride +
                Math.Max(1, grainCount) * 4L + Math.Max(1, endpointCount) * 32L +
                Math.Max(1, boundaryCount) * 32L + substepCapacity * 4L;
            if (estimatedBytes > MaxTraceBytes)
                throw new ArgumentException("Trace allocation exceeds the bounded GPU budget", nameof(contactCapacity));

            var asset = Resources.Load<ComputeShader>("ParallelTrace");
            if (asset == null) throw new InvalidOperationException("ParallelTrace.compute is unavailable");
            shader = UnityEngine.Object.Instantiate(asset);
            resetKernel = shader.FindKernel("Reset");
            clearSlotKernel = shader.FindKernel("ClearSlot");
            captureKernel = shader.FindKernel("Capture");

            identityBuffer = Buffer(Math.Max(1, grainCount), 4);
            parameterBuffer = Buffer(Math.Max(1, endpointCount), 32);
            boundaryBuffer = Buffer(Math.Max(1, boundaries.Length), 32);
            checkpointBuffer = Buffer(checkpointCapacity, CheckpointStride);
            stateBuffer = Buffer(checked(checkpointCapacity * Math.Max(1, endpointCount)), StateStride);
            contactBuffer = Buffer(checked(contactCapacity * ContactCheckpointCount()), ContactStride);
            diagnosticBuffer = Buffer(checkpointCapacity * DiagnosticWords, 4);
            summaryBuffer = Buffer(checkpointCapacity, 32);
            markerBuffer = Buffer(substepCapacity, 4);

            var ids = new uint[Math.Max(1, grainCount)];
            for (int i = 0; i < grainCount; i++)
                ids[i] = stableIdentities[i];
            identityBuffer.SetData(ids);
            parameterBuffer.SetData(parameters);
            if (boundaries.Length > 0) boundaryBuffer.SetData(boundaries);
            checkpointBuffer.SetData(new ProofTraceCheckpoint[checkpointCapacity]);
            diagnosticBuffer.SetData(new uint[checkpointCapacity * DiagnosticWords]);
            summaryBuffer.SetData(new ProofTraceSummary[checkpointCapacity]);
            markerBuffer.SetData(new uint[substepCapacity]);
            BindStatic();
            AllocatedBytes = allocationBytes;
        }

        int ContactCheckpointCount()
        {
            int perSubstep = 2 + velocityIterations + positionIterations;
            if (captureAppliedContacts) perSubstep += velocityIterations + positionIterations;
            return checked(substepCapacity * perSubstep);
        }

        long allocationBytes;
        GraphicsBuffer Buffer(int count, int stride)
        {
            var buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Math.Max(1, count), stride);
            allocationBytes += (long)Math.Max(1, count) * stride;
            return buffer;
        }

        void BindStatic()
        {
            shader.SetInt("_GrainCount", grainCount);
            shader.SetInt("_EndpointCount", endpointCount);
            shader.SetInt("_SubstepCapacity", substepCapacity);
            shader.SetInt("_BoundaryCount", boundaryCount);
            shader.SetInt("_CheckpointCapacity", checkpointCapacity);
            shader.SetInt("_DiagnosticWords", DiagnosticWords);
            shader.SetBuffer(captureKernel, "_Identities", identityBuffer);
            shader.SetBuffer(captureKernel, "_Parameters", parameterBuffer);
            shader.SetBuffer(captureKernel, "_Boundaries", boundaryBuffer);
            shader.SetBuffer(captureKernel, "_TraceCheckpoints", checkpointBuffer);
            shader.SetBuffer(captureKernel, "_TraceStates", stateBuffer);
            shader.SetBuffer(captureKernel, "_TraceContacts", contactBuffer);
            shader.SetBuffer(captureKernel, "_TraceDiagnostics", diagnosticBuffer);
            shader.SetBuffer(captureKernel, "_TraceSummaries", summaryBuffer);
            shader.SetBuffer(captureKernel, "_TraceMarkers", markerBuffer);
            shader.SetBuffer(resetKernel, "_TraceCheckpoints", checkpointBuffer);
            shader.SetBuffer(resetKernel, "_TraceSummaries", summaryBuffer);
            shader.SetBuffer(resetKernel, "_TraceMarkers", markerBuffer);
            shader.SetBuffer(clearSlotKernel, "_TraceSummaries", summaryBuffer);
        }

        public void BeginTick(CommandBuffer commands)
        {
            if (disposed) throw new ObjectDisposedException(nameof(ProofTrace));
            if (commands == null) throw new ArgumentNullException(nameof(commands));
            tick = tick == UInt32.MaxValue ? 1u : tick + 1u;
            commands.SetComputeIntParam(shader, "_Tick", unchecked((int)tick));
            commands.SetComputeIntParam(shader, "_CheckpointCapacity", checkpointCapacity);
            commands.DispatchCompute(shader, resetKernel, (checkpointCapacity + Threads - 1) / Threads, 1, 1);
        }

        public void Record(CommandBuffer commands, GraphicsBuffer state, GraphicsBuffer contacts,
            GraphicsBuffer degrees, GraphicsBuffer increments, GraphicsBuffer diagnostics,
            int substep, ProofTraceStage stage, int iteration)
        {
            if (disposed) throw new ObjectDisposedException(nameof(ProofTrace));
            if (commands == null) throw new ArgumentNullException(nameof(commands));
            if (state == null || contacts == null || degrees == null || increments == null || diagnostics == null)
                throw new ArgumentNullException("Trace source buffers cannot be null");
            if (state.stride != 32 || state.count < endpointCount) throw new ArgumentException("State buffer does not match the trace endpoint layout", nameof(state));
            if (contacts.stride != 64) throw new ArgumentException("Contact buffer does not match the solver layout", nameof(contacts));
            if (degrees.stride != 4 || degrees.count < endpointCount) throw new ArgumentException("Degree buffer does not match the trace endpoint layout", nameof(degrees));
            if (increments.stride != 16) throw new ArgumentException("Increment buffer does not match the solver layout", nameof(increments));
            if (diagnostics.stride != 4 || diagnostics.count < DiagnosticWords) throw new ArgumentException("Diagnostic buffer does not contain the proof words", nameof(diagnostics));
            if (substep < 0 || substep >= substepCapacity) return;
            int offset = SlotOffset(stage, iteration);
            if (offset < 0 || offset >= slotsPerSubstep) return;
            int slot = checked(substep * slotsPerSubstep + offset);
            bool fullContacts = HasContactStage(stage);
            commands.SetComputeIntParam(shader, "_Tick", unchecked((int)tick));
            commands.SetComputeIntParam(shader, "_Substep", substep);
            commands.SetComputeIntParam(shader, "_Stage", (int)stage);
            commands.SetComputeIntParam(shader, "_Iteration", iteration);
            commands.SetComputeIntParam(shader, "_Slot", slot);
            commands.SetComputeIntParam(shader, "_ContactBase", ContactOrdinalForSlot(slot) * contactCapacity);
            commands.SetComputeIntParam(shader, "_StateSourceCount", state.count);
            commands.SetComputeIntParam(shader, "_SourceContactCapacity", contacts.count);
            commands.SetComputeIntParam(shader, "_ContactCapacity", contactCapacity);
            commands.SetComputeIntParam(shader, "_DegreeSourceCount", degrees.count);
            commands.SetComputeIntParam(shader, "_IncrementSourceCount", increments.count);
            commands.SetComputeIntParam(shader, "_CaptureContacts", fullContacts ? 1 : 0);
            commands.DispatchCompute(shader, clearSlotKernel, 1, 1, 1);
            commands.SetComputeBufferParam(shader, captureKernel, "_State", state);
            commands.SetComputeBufferParam(shader, captureKernel, "_Contacts", contacts);
            commands.SetComputeBufferParam(shader, captureKernel, "_Degrees", degrees);
            commands.SetComputeBufferParam(shader, captureKernel, "_Increments", increments);
            commands.SetComputeBufferParam(shader, captureKernel, "_Diagnostics", diagnostics);
            commands.DispatchCompute(shader, captureKernel,
                (Math.Max(endpointCount, contacts.count) + Threads - 1) / Threads, 1, 1);
        }

        int SlotOffset(ProofTraceStage stage, int iteration)
        {
            switch (stage)
            {
                case ProofTraceStage.SubstepStart: return iteration < 0 ? 0 : -1;
                case ProofTraceStage.BeforeVelocity: return iteration < 0 ? 1 : -1;
                case ProofTraceStage.VelocityEvaluated: return iteration >= 0 && iteration < velocityIterations ? 2 + 2 * iteration : -1;
                case ProofTraceStage.VelocityApplied: return iteration >= 0 && iteration < velocityIterations ? 3 + 2 * iteration : -1;
                case ProofTraceStage.Predicted: return iteration < 0 ? 2 + velocityIterations * 2 : -1;
                case ProofTraceStage.PositionEvaluated: return iteration >= 0 && iteration < positionIterations ? 3 + velocityIterations * 2 + 2 * iteration : -1;
                case ProofTraceStage.PositionApplied: return iteration >= 0 && iteration < positionIterations ? 4 + velocityIterations * 2 + 2 * iteration : -1;
                case ProofTraceStage.Validation: return iteration < 0 ? slotsPerSubstep - 1 : -1;
                default: return -1;
            }
        }

        bool HasContactStage(ProofTraceStage stage)
        {
            if (captureAppliedContacts) return stage != ProofTraceStage.SubstepStart && stage != ProofTraceStage.Predicted;
            return stage == ProofTraceStage.BeforeVelocity || stage == ProofTraceStage.VelocityEvaluated ||
                   stage == ProofTraceStage.PositionEvaluated || stage == ProofTraceStage.Validation;
        }

        int ContactOrdinalForSlot(int slot)
        {
            int ordinal = 0;
            for (int i = 0; i < slot; i++)
                if (HasContactStage(StageAt(i % slotsPerSubstep))) ordinal++;
            return ordinal;
        }

        public async Task<ProofTraceReadback> ReadAsync()
        {
            if (disposed) throw new ObjectDisposedException(nameof(ProofTrace));
            Task<ProofTraceCheckpoint[]> c = Read<ProofTraceCheckpoint>(checkpointBuffer);
            Task<ProofTraceState[]> s = Read<ProofTraceState>(stateBuffer);
            Task<ProofTraceContact[]> p = Read<ProofTraceContact>(contactBuffer);
            Task<ProofTraceSummary[]> m = Read<ProofTraceSummary>(summaryBuffer);
            Task<uint[]> d = Read<uint>(diagnosticBuffer);
            await Task.WhenAll(c, s, p, m, d);

            var result = new ProofTraceReadback
            {
                Tick = tick,
                GrainCount = grainCount,
                EndpointCount = endpointCount,
                ContactCapacity = contactCapacity,
                SubstepCapacity = substepCapacity,
                PositionIterations = positionIterations,
                VelocityIterations = velocityIterations,
                Dt = 1f / 60f,
                Friction = friction,
                Identities = (uint[])stableIdentities.Clone(),
                Parameters = (BodyParameters[])traceParameters.Clone(),
                Boundaries = (Boundary[])traceBoundaries.Clone()
            };
            var checkpoints = new List<ProofTraceCheckpoint>(checkpointCapacity);
            var states = new List<ProofTraceState>(checkpointCapacity * endpointCount);
            var contacts = new List<ProofTraceContact>(ContactCheckpointCount() * contactCapacity);
            var summaries = new List<ProofTraceSummary>(checkpointCapacity);
            var diagnostics = new List<uint>(checkpointCapacity * DiagnosticWords);
            var rawContacts = p.Result;
            for (int i = 0; i < c.Result.Length; i++)
            {
                ProofTraceCheckpoint header = c.Result[i];
                if (header.Tick != tick || (header.Flags & 0x80000000u) == 0) continue;
                int rawContactStart = checked((int)header.ContactOffset);
                header.StateOffset = (uint)states.Count;
                ProofTraceSummary summary = m.Result[i];
                header.ContactOffset = (uint)contacts.Count;
                int stateStart = checked((int)((uint)i * (uint)endpointCount));
                for (int j = 0; j < endpointCount; j++) states.Add(s.Result[stateStart + j]);
                // Preserve the GPU offset before compacting the checkpoint arrays.
                int copied = Math.Min((int)header.CapturedContactCount, Math.Max(0, rawContacts.Length - rawContactStart));
                for (int j = 0; j < copied; j++) contacts.Add(rawContacts[rawContactStart + j]);
                int diagStart = checked(i * DiagnosticWords);
                for (int j = 0; j < DiagnosticWords; j++) diagnostics.Add(d.Result[diagStart + j]);
                header.CapturedContactCount = (uint)copied;
                summaries.Add(summary);
                checkpoints.Add(header);
            }
            result.Checkpoints = checkpoints.ToArray();
            result.States = states.ToArray();
            result.Contacts = contacts.ToArray();
            result.Summaries = summaries.ToArray();
            result.Diagnostics = diagnostics.ToArray();
            return result;
        }

        ProofTraceStage StageAt(int offset)
        {
            if (offset == 0) return ProofTraceStage.SubstepStart;
            if (offset == 1) return ProofTraceStage.BeforeVelocity;
            int predicted = 2 + 2 * velocityIterations;
            if (offset < predicted) return (offset & 1) == 0 ? ProofTraceStage.VelocityEvaluated : ProofTraceStage.VelocityApplied;
            if (offset == predicted) return ProofTraceStage.Predicted;
            if (offset == slotsPerSubstep - 1) return ProofTraceStage.Validation;
            return ((offset - predicted - 1) & 1) == 0 ? ProofTraceStage.PositionEvaluated : ProofTraceStage.PositionApplied;
        }

        static Task<T[]> Read<T>(GraphicsBuffer buffer) where T : struct
        {
            var source = new TaskCompletionSource<T[]>();
            AsyncGPUReadback.Request(buffer, request =>
            {
                if (request.hasError) source.TrySetException(new InvalidOperationException("Proof trace GPU readback failed"));
                else source.TrySetResult(request.GetData<T>().ToArray());
            });
            return source.Task;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            identityBuffer.Dispose(); parameterBuffer.Dispose(); boundaryBuffer.Dispose();
            checkpointBuffer.Dispose(); stateBuffer.Dispose(); contactBuffer.Dispose(); diagnosticBuffer.Dispose();
            summaryBuffer.Dispose(); markerBuffer.Dispose();
            if (Application.isPlaying) UnityEngine.Object.Destroy(shader); else UnityEngine.Object.DestroyImmediate(shader);
        }
    }
}
