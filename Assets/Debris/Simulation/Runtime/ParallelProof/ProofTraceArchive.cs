using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace Debris.Simulation.ParallelProof
{
    // Diagnostic transport only. Explicit scalar encoding avoids changing any
    // authoritative record or relying on Unity's serialization of those records.
    public static class ProofTraceArchive
    {
        const string Magic = "B3R_TRACE_1";
        static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        public static string Write(ProofTraceReadback trace, string prefix)
        {
            if (trace == null) throw new ArgumentNullException(nameof(trace));
            if (string.IsNullOrEmpty(prefix)) throw new ArgumentException("Trace path is required", nameof(prefix));
            if (prefix.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) prefix = prefix.Substring(0, prefix.Length - 5);
            string path = prefix.EndsWith(".b3rt.gz", StringComparison.OrdinalIgnoreCase) ? prefix : prefix + ".b3rt.gz";
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            using (var file = File.Create(path))
            using (var gzip = new GZipStream(file, System.IO.Compression.CompressionLevel.Fastest))
            using (var buffered = new BufferedStream(gzip, 65536))
            using (var writer = new BinaryWriter(buffered))
            {
                writer.Write(Magic); writer.Write(trace.Tick);
                writer.Write(trace.GrainCount); writer.Write(trace.EndpointCount);
                writer.Write(trace.ContactCapacity); writer.Write(trace.SubstepCapacity);
                writer.Write(trace.Dt); writer.Write(trace.Friction);
                writer.Write(trace.VelocityIterations); writer.Write(trace.PositionIterations);
                WriteArray(writer, trace.Identities, (w, v) => w.Write(v));
                WriteArray(writer, trace.Parameters, WriteParameter);
                WriteArray(writer, trace.Boundaries, WriteBoundary);
                WriteArray(writer, trace.Checkpoints, WriteCheckpoint);
                WriteArray(writer, trace.States, WriteState);
                WriteArray(writer, trace.Contacts, WriteContact);
                WriteArray(writer, trace.Summaries, WriteSummary);
                WriteArray(writer, trace.Diagnostics, (w, v) => w.Write(v));
            }
            WriteSummaries(trace, path.Substring(0, path.Length - 8) + ".summary.csv");
            return path;
        }

        public static ProofTraceReadback Read(string path)
        {
            using (var file = File.OpenRead(path))
            using (var gzip = new GZipStream(file, CompressionMode.Decompress))
            using (var buffered = new BufferedStream(gzip, 65536))
            using (var reader = new BinaryReader(buffered))
            {
                if (reader.ReadString() != Magic) throw new InvalidDataException("Unsupported proof trace archive");
                var result = new ProofTraceReadback
                {
                    Tick = reader.ReadUInt32(), GrainCount = reader.ReadInt32(), EndpointCount = reader.ReadInt32(),
                    ContactCapacity = reader.ReadInt32(), SubstepCapacity = reader.ReadInt32(),
                    Dt = reader.ReadSingle(), Friction = reader.ReadSingle(),
                    VelocityIterations = reader.ReadInt32(), PositionIterations = reader.ReadInt32(),
                    Identities = ReadArray(reader, r => r.ReadUInt32()),
                    Parameters = ReadArray(reader, ReadParameter), Boundaries = ReadArray(reader, ReadBoundary),
                    Checkpoints = ReadArray(reader, ReadCheckpoint), States = ReadArray(reader, ReadState),
                    Contacts = ReadArray(reader, ReadContact), Summaries = ReadArray(reader, ReadSummary),
                    Diagnostics = ReadArray(reader, r => r.ReadUInt32())
                };
                if (result.Parameters.Length != result.EndpointCount || result.Identities.Length != result.GrainCount ||
                    result.Summaries.Length != result.Checkpoints.Length || result.Diagnostics.Length != result.Checkpoints.Length * 16)
                    throw new InvalidDataException("Incomplete proof trace metadata");
                return result;
            }
        }

        static void WriteSummaries(ProofTraceReadback trace, string path)
        {
            using (var writer = new StreamWriter(path))
            {
                writer.WriteLine("tick,substep,stage,iteration,contacts,captured_contacts,grain_contacts,solid_contacts,max_grain_penetration,max_solid_penetration,max_degree,fault,trace_flags");
                for (int i = 0; i < trace.Checkpoints.Length; i++)
                {
                    var c = trace.Checkpoints[i]; var s = trace.Summaries[i];
                    writer.WriteLine(string.Join(",", c.Tick, c.Substep, c.Stage,
                        c.Iteration == uint.MaxValue ? "-1" : c.Iteration.ToString(Invariant), c.ContactCount,
                        c.CapturedContactCount, s.GrainContactCount, s.SolidContactCount,
                        s.MaximumGrainPenetration.ToString("R", Invariant), s.MaximumSolidPenetration.ToString("R", Invariant),
                        s.MaximumDegree, trace.Diagnostics[i * 16], c.Flags));
                }
            }
        }

        static void WriteArray<T>(BinaryWriter writer, T[] values, Action<BinaryWriter, T> write)
        {
            writer.Write(values.Length);
            foreach (var value in values) write(writer, value);
        }
        static T[] ReadArray<T>(BinaryReader reader, Func<BinaryReader, T> read)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > 16000000) throw new InvalidDataException("Invalid diagnostic array length");
            var values = new T[count];
            for (int i = 0; i < count; i++) values[i] = read(reader);
            return values;
        }
        static void V(BinaryWriter w, Vector2 v) { w.Write(v.x); w.Write(v.y); }
        static Vector2 V(BinaryReader r) => new Vector2(r.ReadSingle(), r.ReadSingle());
        static void WriteParameter(BinaryWriter w, BodyParameters p)
        {
            w.Write(p.InverseMass); w.Write(p.InverseInertia); V(w, p.LocalCOM);
            w.Write(p.BoundaryStart); w.Write(p.BoundaryCount); w.Write(p.Mobility); w.Write(p.ShapeRevision);
        }
        static BodyParameters ReadParameter(BinaryReader r) => new BodyParameters
        {
            InverseMass = r.ReadSingle(), InverseInertia = r.ReadSingle(), LocalCOM = V(r),
            BoundaryStart = r.ReadUInt32(), BoundaryCount = r.ReadUInt32(), Mobility = r.ReadUInt32(), ShapeRevision = r.ReadUInt32()
        };
        static void WriteBoundary(BinaryWriter w, Boundary b)
        {
            V(w, b.Center); V(w, b.HalfSize); w.Write(b.Body); w.Write(b.Feature); w.Write(b.Reserved0); w.Write(b.Reserved1);
        }
        static Boundary ReadBoundary(BinaryReader r) => new Boundary
        {
            Center = V(r), HalfSize = V(r), Body = r.ReadUInt32(), Feature = r.ReadUInt32(), Reserved0 = r.ReadUInt32(), Reserved1 = r.ReadUInt32()
        };
        static void WriteCheckpoint(BinaryWriter w, ProofTraceCheckpoint c)
        {
            w.Write(c.Tick); w.Write(c.Substep); w.Write((uint)c.Stage); w.Write(c.Iteration);
            w.Write(c.ContactCount); w.Write(c.CapturedContactCount); w.Write(c.StateOffset); w.Write(c.ContactOffset); w.Write(c.Flags); w.Write(c.Reserved);
        }
        static ProofTraceCheckpoint ReadCheckpoint(BinaryReader r) => new ProofTraceCheckpoint
        {
            Tick = r.ReadUInt32(), Substep = r.ReadUInt32(), Stage = (ProofTraceStage)r.ReadUInt32(), Iteration = r.ReadUInt32(),
            ContactCount = r.ReadUInt32(), CapturedContactCount = r.ReadUInt32(), StateOffset = r.ReadUInt32(), ContactOffset = r.ReadUInt32(), Flags = r.ReadUInt32(), Reserved = r.ReadUInt32()
        };
        static void WriteState(BinaryWriter w, ProofTraceState s)
        {
            V(w, s.Center); V(w, s.Velocity); w.Write(s.Angle); w.Write(s.AngularVelocity); w.Write(s.Identity);
            w.Write(s.Endpoint); w.Write(s.Degree); w.Write(s.Flags); w.Write(s.Reserved0); w.Write(s.Reserved1);
        }
        static ProofTraceState ReadState(BinaryReader r) => new ProofTraceState
        {
            Center = V(r), Velocity = V(r), Angle = r.ReadSingle(), AngularVelocity = r.ReadSingle(), Identity = r.ReadUInt32(),
            Endpoint = r.ReadUInt32(), Degree = r.ReadUInt32(), Flags = r.ReadUInt32(), Reserved0 = r.ReadUInt32(), Reserved1 = r.ReadUInt32()
        };
        static void WriteContact(BinaryWriter w, ProofTraceContact c)
        {
            w.Write(c.A); w.Write(c.B); w.Write(c.Feature); w.Write(c.Flags); w.Write(c.IdentityA); w.Write(c.IdentityB); w.Write(c.EndpointFlags); w.Write(c.Reserved);
            V(w, c.Normal); w.Write(c.Separation); w.Write(c.NormalLambda); V(w, c.ArmA); V(w, c.ArmB);
            w.Write(c.TangentLambda); w.Write(c.PositionLambda); w.Write(c.EffectiveMassA); w.Write(c.EffectiveMassB);
            V(w, c.IncrementalImpulse); w.Write(c.IncrementalTorqueA); w.Write(c.IncrementalTorqueB);
        }
        static ProofTraceContact ReadContact(BinaryReader r) => new ProofTraceContact
        {
            A = r.ReadUInt32(), B = r.ReadUInt32(), Feature = r.ReadUInt32(), Flags = r.ReadUInt32(), IdentityA = r.ReadUInt32(), IdentityB = r.ReadUInt32(), EndpointFlags = r.ReadUInt32(), Reserved = r.ReadUInt32(),
            Normal = V(r), Separation = r.ReadSingle(), NormalLambda = r.ReadSingle(), ArmA = V(r), ArmB = V(r),
            TangentLambda = r.ReadSingle(), PositionLambda = r.ReadSingle(), EffectiveMassA = r.ReadSingle(), EffectiveMassB = r.ReadSingle(),
            IncrementalImpulse = V(r), IncrementalTorqueA = r.ReadSingle(), IncrementalTorqueB = r.ReadSingle()
        };
        static void WriteSummary(BinaryWriter w, ProofTraceSummary s)
        {
            w.Write(s.ContactCount); w.Write(s.CapturedContactCount); w.Write(s.GrainContactCount); w.Write(s.SolidContactCount);
            w.Write(s.MaximumGrainPenetration); w.Write(s.MaximumSolidPenetration); w.Write(s.MaximumDegree); w.Write(s.Flags);
        }
        static ProofTraceSummary ReadSummary(BinaryReader r) => new ProofTraceSummary
        {
            ContactCount = r.ReadUInt32(), CapturedContactCount = r.ReadUInt32(), GrainContactCount = r.ReadUInt32(), SolidContactCount = r.ReadUInt32(),
            MaximumGrainPenetration = r.ReadSingle(), MaximumSolidPenetration = r.ReadSingle(), MaximumDegree = r.ReadUInt32(), Flags = r.ReadUInt32()
        };
    }
}
