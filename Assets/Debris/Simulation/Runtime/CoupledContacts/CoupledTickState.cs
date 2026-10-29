using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Debris.Simulation.ParallelProof;
using ProofGrain = Debris.Simulation.ParallelProof.LooseCell;
using UnityEngine;
using UnityEngine.Rendering;

namespace Debris.Simulation.CoupledContacts
{
    // Concrete committed/working buffers retain the existing public renderer
    // layouts. Fixed capacity and topology prevent matter insertion/deletion
    // during a physics tick. All explicit slices count against the same arena.
    public sealed class CoupledTickState : IDisposable
    {
        internal enum Kind { Endpoints,Grains,Parameters,Bindings,Cache,Inventory,Cargo,Terrain }
        internal sealed class Pair
        {
            public Kind Kind;
            public int Count;
            public GraphicsBuffer Committed,Working;
        }
        readonly CoupledContactArena arena;
        readonly List<GraphicsBuffer> owned=new List<GraphicsBuffer>();
        readonly List<Pair> pairs=new List<Pair>();
        internal IReadOnlyList<Pair> Pairs=>pairs;
        internal CoupledContactArena Arena=>arena;
        bool transactionClaimed;
        internal void CheckTransactionAvailable()
        {
            if(disposed||transactionClaimed)throw new InvalidOperationException("Tick state already has a transaction owner or is disposed");
        }
        internal void ClaimTransaction()
        {CheckTransactionAvailable();transactionClaimed=true;}
        internal CoupledTickHeader InitialHeader { get; }
        internal GraphicsBuffer HeaderBuffer { get; private set; }
        public GraphicsBuffer WorkingHeader { get; private set; }
        bool disposed;
        public long BufferBytes { get; private set; }
        public int EndpointCount { get; }
        public int GrainCount { get; }
        public int CacheCount=>pairs[4].Count;
        public int InventoryWordCount=>pairs[5].Count;
        public int CargoFactCount=>pairs[6].Count;
        public int TerrainRevisionCount=>pairs[7].Count;
        public GraphicsBuffer CommittedHeader=>HeaderBuffer;
        public GraphicsBuffer CommittedEndpoints=>pairs[0].Committed;
        public GraphicsBuffer WorkingEndpoints=>pairs[0].Working;
        public GraphicsBuffer CommittedGrains=>pairs[1].Committed;
        public GraphicsBuffer WorkingGrains=>pairs[1].Working;
        public GraphicsBuffer CommittedParameters=>pairs[2].Committed;
        public GraphicsBuffer WorkingParameters=>pairs[2].Working;
        public GraphicsBuffer CommittedBindings=>pairs[3].Committed;
        public GraphicsBuffer WorkingBindings=>pairs[3].Working;
        public GraphicsBuffer CommittedCache=>pairs[4].Committed;
        public GraphicsBuffer WorkingCache=>pairs[4].Working;
        public GraphicsBuffer CommittedInventoryWords=>pairs[5].Committed;
        public GraphicsBuffer WorkingInventoryWords=>pairs[5].Working;
        public GraphicsBuffer CommittedCargoFacts=>pairs[6].Committed;
        public GraphicsBuffer WorkingCargoFacts=>pairs[6].Working;
        public GraphicsBuffer CommittedTerrainRevisions=>pairs[7].Committed;
        public GraphicsBuffer WorkingTerrainRevisions=>pairs[7].Working;

        public CoupledTickState(CoupledTickInitialState initial,CoupledContactArena arena)
        {
            if(initial==null)throw new ArgumentNullException(nameof(initial));
            this.arena=arena??throw new ArgumentNullException(nameof(arena));
            Validate(initial);
            InitialHeader=initial.Header;EndpointCount=initial.Endpoints.Length;GrainCount=initial.Grains.Length;
            long required=64+2*(32L*initial.Endpoints.Length+48L*Math.Max(1,initial.Grains.Length)+
                32L*initial.Parameters.Length+36L*initial.Bindings.Length+64L*Math.Max(1,initial.Cache.Length)+
                4L*(Math.Max(1,initial.InventoryWords.Length)+(long)Math.Max(1,initial.CargoFacts.Length)+
                    Math.Max(1,initial.TerrainRevisions.Length)));
            arena.EnsureCapacity(required);
            string owner=arena.NewOwner("TickState");
            try
            {
                Add(owner,Kind.Endpoints,initial.Endpoints);Add(owner,Kind.Grains,initial.Grains);
                Add(owner,Kind.Parameters,initial.Parameters);Add(owner,Kind.Bindings,initial.Bindings);
                Add(owner,Kind.Cache,initial.Cache);Add(owner,Kind.Inventory,initial.InventoryWords);
                Add(owner,Kind.Cargo,initial.CargoFacts);Add(owner,Kind.Terrain,initial.TerrainRevisions);
                HeaderBuffer=Buffer(owner+"/header",1,32);HeaderBuffer.SetData(new[]{initial.Header});
                WorkingHeader=Buffer(owner+"/working-header",1,32);WorkingHeader.SetData(new[]{initial.Header});
            }
            catch { Dispose();throw; }
        }
        void Add<T>(string owner,Kind kind,T[] data) where T:struct
        {
            int stride=Marshal.SizeOf<T>(),count=Math.Max(1,data.Length);
            var committed=Buffer(owner+"/"+kind+"/committed",count,stride);
            var working=Buffer(owner+"/"+kind+"/working",count,stride);
            var upload=data.Length==0?new T[1]:data;
            committed.SetData(upload);working.SetData(upload);
            pairs.Add(new Pair{Kind=kind,Count=data.Length,Committed=committed,Working=working});
        }
        GraphicsBuffer Buffer(string name,int count,int stride)
        {
            var buffer=arena.Allocate(name,count,stride);owned.Add(buffer);
            BufferBytes+=(long)count*stride;return buffer;
        }
        static bool Finite(float x)=>!float.IsNaN(x)&&!float.IsInfinity(x);
        static void Validate(CoupledTickInitialState s)
        {
            if(Marshal.SizeOf<BodyState>()!=32||Marshal.SizeOf<ProofGrain>()!=48||
               Marshal.SizeOf<BodyParameters>()!=32||Marshal.SizeOf<CoupledEndpointBinding>()!=36||
               Marshal.SizeOf<CoupledCachePoint>()!=64||Marshal.SizeOf<CoupledTickHeader>()!=32||
               Marshal.SizeOf<CoupledTickCompletion>()!=64)
                throw new InvalidOperationException("Coupled tick record layout mismatch");
            if(Marshal.OffsetOf<CoupledCachePoint>(nameof(CoupledCachePoint.ImpulseNormal)).ToInt32()!=32||
               Marshal.OffsetOf<CoupledCachePoint>(nameof(CoupledCachePoint.LocalAnchors)).ToInt32()!=48||
               Marshal.OffsetOf<CoupledTickHeader>(nameof(CoupledTickHeader.Generation)).ToInt32()!=8||
               Marshal.OffsetOf<CoupledTickHeader>(nameof(CoupledTickHeader.Version)).ToInt32()!=24||
               Marshal.OffsetOf<CoupledTickCompletion>(nameof(CoupledTickCompletion.ExpectedPreviousTick)).ToInt32()!=8||
               Marshal.OffsetOf<CoupledTickCompletion>(nameof(CoupledTickCompletion.CompletedTick)).ToInt32()!=16||
               Marshal.OffsetOf<CoupledTickCompletion>(nameof(CoupledTickCompletion.Generation)).ToInt32()!=24||
               Marshal.OffsetOf<CoupledTickCompletion>(nameof(CoupledTickCompletion.Status)).ToInt32()!=28||
               Marshal.OffsetOf<CoupledTickCompletion>(nameof(CoupledTickCompletion.Version)).ToInt32()!=60)
                throw new InvalidOperationException("Coupled tick record offset mismatch");
            if(s.Endpoints==null||s.Grains==null||s.Parameters==null||s.Bindings==null||
               s.Cache==null||s.InventoryWords==null||s.CargoFacts==null||s.TerrainRevisions==null||
               s.Endpoints.Length<1||s.Endpoints.Length>8210||s.Grains.Length>8192||
               s.Grains.Length>s.Endpoints.Length||s.Parameters.Length!=s.Endpoints.Length||
               s.Bindings.Length!=s.Endpoints.Length||s.Cache.Length>131072||
               s.Header.Version!=1||s.Header.Generation==0||s.Header.Generation==uint.MaxValue||
               s.Header.CacheGeneration==0||s.Header.CacheGeneration==uint.MaxValue||
               s.Header.IdentityRevision==0||s.Header.TopologyRevision==0||s.Header.Reserved!=0||
               s.Header.CompletedTick==ulong.MaxValue)
                throw new ArgumentException("Invalid fixed-topology coupled tick state");
            var identities=new HashSet<CoupledPersistentIdentity>();
            for(int i=0;i<s.Endpoints.Length;i++)
            {
                var e=s.Endpoints[i];var p=s.Parameters[i];var binding=s.Bindings[i];
                if(!Finite(e.Center.x)||!Finite(e.Center.y)||!Finite(e.Velocity.x)||!Finite(e.Velocity.y)||
                   !Finite(e.Angle)||!Finite(e.AngularVelocity)||!Finite(p.InverseMass)||
                   !Finite(p.InverseInertia)||!Finite(p.LocalCOM.x)||!Finite(p.LocalCOM.y)||
                   p.InverseMass<0||p.InverseInertia<0||p.Mobility>1||
                   (p.Mobility==0&&(p.InverseMass!=0||p.InverseInertia!=0||
                       e.Velocity!=Vector2.zero||e.AngularVelocity!=0))||
                   (p.Mobility==1&&p.InverseMass<=0)||binding.Bound!=1||
                   binding.Identity.Kind<1||binding.Identity.Kind>3||
                   (binding.Identity.Low==0&&binding.Identity.High==0)||binding.Generation==0||
                   binding.SlotGeneration==0||binding.Revision==0||p.ShapeRevision!=binding.Revision||
                   !identities.Add(binding.Identity))
                    throw new ArgumentException("Invalid coupled tick endpoint definition");
                if(i<s.Grains.Length)
                {
                    var g=s.Grains[i];
                    if(g.Material==0||g.Identity==0||!binding.Identity.Equals(CoupledPersistentIdentity.Grain(g.Identity))||
                       !g.Center.Equals(e.Center)||!g.Velocity.Equals(e.Velocity)||
                       !g.Angle.Equals(e.Angle)||!g.AngularVelocity.Equals(e.AngularVelocity))
                        throw new ArgumentException("Grain records must match their endpoint identities and motion");
                }
            }
            foreach(var point in s.Cache)
            {
                for(int component=0;component<4;component++)
                    if(!Finite(point.ImpulseNormal[component])||!Finite(point.LocalAnchors[component]))
                        throw new ArgumentException("Nonfinite initial coupled cache payload");
            }
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            foreach(var buffer in owned)arena.Release(buffer);
        }
    }
}
