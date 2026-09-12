using System.Runtime.InteropServices;
using UnityEngine;
namespace Debris.Simulation.ParallelProof
{
    // Candidate production layout; the legacy runtime stays intact until the proof gate passes.
    [StructLayout(LayoutKind.Sequential, Pack=4, Size=48)]
    public struct LooseCell
    {
        public Vector2 Center, Velocity;
        public float Angle, AngularVelocity;
        public uint Material, Identity, Flags, Reserved0, Reserved1, Reserved2;
    }
    [StructLayout(LayoutKind.Sequential, Pack=4, Size=32)]
    public struct BodyState
    {
        public Vector2 Center, Velocity;
        public float Angle, AngularVelocity;
        public uint Reserved0, Reserved1;
    }
    [StructLayout(LayoutKind.Sequential, Pack=4, Size=32)]
    public struct BodyParameters
    {
        public float InverseMass, InverseInertia;
        public Vector2 LocalCOM;
        public uint BoundaryStart, BoundaryCount, Mobility, ShapeRevision;
    }
    // Cached rectangular physical patches for the proof box/fragment; not authoritative masks.
    [StructLayout(LayoutKind.Sequential, Pack=4, Size=32)]
    public struct Boundary
    {
        public Vector2 Center, HalfSize;
        public uint Body, Feature, Reserved0, Reserved1;
    }
    public enum SolverFault : uint
    {
        None=0, Speed=1, Page=2, Candidates=4, Adjacency=8, Envelope=16,
        Nonfinite=32, GrainPenetration=64, SolidPenetration=128, RigidCapacity=256, RigidPairCapacity=512, RigidNonfinite=1024, RigidPenetration=2048, RigidTopology=4096
    }
    public sealed class ProofSnapshot
    {
        public LooseCell[] Grains;
        public BodyState[] Endpoints;
        public uint[] Diagnostics;
        public SolverFault Fault => (SolverFault)Diagnostics[0];
        public uint CompletedTick => Diagnostics[1];
        public float GrainPenetration => System.BitConverter.Int32BitsToSingle((int)Diagnostics[8]);
        public float SolidPenetration => System.BitConverter.Int32BitsToSingle((int)Diagnostics[9]);
    }
}
