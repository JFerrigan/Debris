using System;
using System.Runtime.InteropServices;
using Debris.Simulation.ParallelProof;
using ProofGrain = Debris.Simulation.ParallelProof.LooseCell;
using UnityEngine;

namespace Debris.Simulation.CoupledContacts
{
    [StructLayout(LayoutKind.Sequential,Pack=4,Size=64)]
    public struct CoupledCachePoint
    {
        public CoupledContactKeyWords Key;
        public Vector4 ImpulseNormal,LocalAnchors;
    }
    [StructLayout(LayoutKind.Sequential,Pack=4,Size=32)]
    public struct CoupledTickHeader
    {
        public ulong CompletedTick;
        public uint Generation,TopologyRevision,CacheGeneration,IdentityRevision,Version,ActiveCacheCount;
    }
    [StructLayout(LayoutKind.Sequential,Pack=4,Size=64)]
    public struct CoupledTickCompletion
    {
        public ulong AttemptedTick,ExpectedPreviousTick,CompletedTick;
        public uint Generation,Status,Fault,Stage,Substeps,PassedSubsteps;
        public uint TopologyRevision,CacheGeneration,IdentityRevision,Version;
    }
    public enum CoupledTickStatus : uint { Committed=1,Rejected=2,DependencyFailed=3 }
    // Shared with the named offsets in CoupledTickTransaction.compute.
    // The first word is also the activity gate accepted by the solver stages.
    public enum CoupledTickControlWord
    {
        Active=0,Accepted=1,Fault=2,FaultStage=3,TickLow=4,TickHigh=5,
        Generation=6,Topology=7,CacheGeneration=8,IdentityRevision=9,
        NextStage=10,StageCalls=11,Slot=12,Substeps=13,PassedSubsteps=14,
        FinalSeen=15,PreviousLow=16,PreviousHigh=17,SourceFault=20,
        InvalidCount=21,ImmutableCount=22,Version=63
    }
    public enum CoupledTickStage : uint
    { Input=1,Velocity=2,Prediction=3,Position=4,Substep=5,Final=6,Publication=7 }
    public enum CoupledTickFault : uint
    { None=0,Dependency=1,StageOrder=2,StageRejected=3,InvalidState=4,ImmutableState=5,Generation=6 }

    // Fixed topology for a physics transaction. Inventory/fuel words are an
    // opaque owner-supplied payload; this does not implement transfers or burn.
    public sealed class CoupledTickInitialState
    {
        public BodyState[] Endpoints=Array.Empty<BodyState>();
        public BodyParameters[] Parameters=Array.Empty<BodyParameters>();
        public ProofGrain[] Grains=Array.Empty<ProofGrain>();
        public CoupledEndpointBinding[] Bindings=Array.Empty<CoupledEndpointBinding>();
        public CoupledCachePoint[] Cache=Array.Empty<CoupledCachePoint>();
        public uint[] InventoryWords=Array.Empty<uint>(),CargoFacts=Array.Empty<uint>(),
            TerrainRevisions=Array.Empty<uint>();
        public CoupledTickHeader Header=new CoupledTickHeader{
            Generation=1,TopologyRevision=1,CacheGeneration=1,IdentityRevision=1,Version=1};
    }
    public readonly struct CoupledTickTicket
    {
        public readonly ulong Tick;
        public readonly uint Slot,ExpectedGeneration;
        internal readonly CoupledTickTransaction Owner;
        internal CoupledTickTicket(CoupledTickTransaction owner,ulong tick,uint slot,uint generation)
        {Owner=owner;Tick=tick;Slot=slot;ExpectedGeneration=generation;}
    }
}
