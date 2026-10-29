using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Debris.Simulation.CoupledContacts
{
    [StructLayout(LayoutKind.Sequential, Pack=4, Size=24)]
    public struct CoupledContactFeature
    {
        public uint APrimitive,BPrimitive,ReferenceFace,IncidentFace,Provenance,PointAxis;
    }

    [StructLayout(LayoutKind.Sequential,Pack=4,Size=32)]
    public struct CoupledContactKeyWords
    {
        public uint Endpoints,SlotGenerationA,SlotGenerationB;
        public uint RevisionA,RevisionB,FeatureA,FeatureB,Point;
    }

    // Diagnostic source path from GPU primitive index to compact scaled rows.
    // The final readback is for comparison; gameplay will keep these buffers
    // on the GPU and run the contact solver from the count buffer.
    public sealed class CoupledContactRowBatch : IDisposable
    {
        public const int MaxRows=262144;
        const int BlockSize=256;
        readonly CoupledContactManifoldBatch manifolds;
        readonly ComputeShader shader;
        readonly CommandBuffer commands=new CommandBuffer{name="V2 compact contact rows"};
        readonly List<GraphicsBuffer> owned=new List<GraphicsBuffer>();
        readonly CoupledContactArena arena;
        readonly string arenaOwner;
        int allocationIndex;
        readonly GraphicsBuffer bodies,masks,localStarts,blockSums,
            blockStarts,rows,rowManifoldIds,features,rowCount,fault,buildArgs,scanArgs,scatterArgs,keyArgs;
        readonly int prepare,build,scanLocal,scanBlocks,scatter,resetFault,copyBodyPoses,
            prepareKeys,buildKeys;
        readonly int bodyCount;
        bool disposed;
        public long BufferBytes { get; private set; }
        internal CoupledContactManifoldBatch Manifolds=>manifolds;
        internal GraphicsBuffer RowsBuffer=>rows;
        internal GraphicsBuffer RowManifoldIdsBuffer=>rowManifoldIds;
        internal GraphicsBuffer BodiesBuffer=>bodies;
        internal GraphicsBuffer EndpointBindingsBuffer=>manifolds.EndpointBindingsBuffer;
        internal GraphicsBuffer FeaturesBuffer=>features;
        internal GraphicsBuffer PrimitivesBuffer=>manifolds.PrimitivesBuffer;
        internal int PrimitiveCount=>manifolds.PrimitiveCount;
        internal GraphicsBuffer RowCounter=>rowCount;
        internal GraphicsBuffer BroadFaultBuffer=>manifolds.BroadFaultBuffer;
        internal GraphicsBuffer ManifoldFaultBuffer=>manifolds.CompactFaultBuffer;
        internal GraphicsBuffer RowFaultBuffer=>fault;
        internal GraphicsBuffer FirstContainedBuffer=>manifolds.FirstContainedBuffer;
        internal bool ContainmentEnabled=>manifolds.ContainmentEnabled;

        public sealed class Result
        {
            public uint CandidateCount,ManifoldCount,RowCount;
            public uint BroadPhaseFault,ManifoldFault,RowFault;
            public uint FirstContainedPrimitive;
            public CoupledContactRow[] Rows;
            public CoupledContactFeature[] Features;
            public bool Success=>BroadPhaseFault==0&&ManifoldFault==0&&RowFault==0;
        }

        public CoupledContactRowBatch(CoupledContactBody[] sourceBodies,
            CoupledContactPrimitive[] primitives,Vector2 origin,float cellSize,
            int width=128,int height=128,float margin=.25f,
            CoupledUnionGeometry union=null,bool validateContainment=false,
            CoupledEndpointBinding[] endpointBindings=null,
            CoupledContactArena arena=null,bool normalOnly=false)
        {
            this.arena=arena??new CoupledContactArena();
            arenaOwner=this.arena.NewOwner("Rows");
            try
            {
            if(sourceBodies==null||sourceBodies.Length==0||sourceBodies.Length>8210)
                throw new ArgumentException("Invalid V2 body capacity");
            bodyCount=sourceBodies.Length;
            foreach(var b in sourceBodies)
                if(!Finite(b.Center.x)||!Finite(b.Center.y)||
                   !Finite(b.InverseMass)||!Finite(b.InverseInertia)||
                   b.InverseMass<0||b.InverseInertia<0)
                    throw new ArgumentException("Invalid body mass or center");
            var asset=Resources.Load<ComputeShader>("CoupledRowsCompact");
            if(asset==null)throw new InvalidOperationException("Missing CoupledRowsCompact compute resource");
            manifolds=new CoupledContactManifoldBatch(primitives,sourceBodies.Length,
                origin,cellSize,width,height,margin,union,validateContainment,
                endpointBindings,this.arena);
            shader=UnityEngine.Object.Instantiate(asset);
            int blocks=(MaxRows+BlockSize-1)/BlockSize;
            bodies=Buffer(sourceBodies.Length,16);
            masks=Buffer(MaxRows,4);
            localStarts=Buffer(MaxRows,4);blockSums=Buffer(blocks,4);
            blockStarts=Buffer(blocks,4);rows=Buffer(MaxRows,32);
            rowManifoldIds=Buffer(MaxRows,4);
            features=Buffer(MaxRows,24);rowCount=Buffer(1,4);fault=Buffer(1,4);
            var indirect=GraphicsBuffer.Target.Structured|GraphicsBuffer.Target.IndirectArguments;
            buildArgs=Buffer(4,4,indirect);scanArgs=Buffer(4,4,indirect);
            scatterArgs=Buffer(4,4,indirect);
            keyArgs=Buffer(4,4,indirect);
            BufferBytes+=manifolds.BufferBytes;
            bodies.SetData(sourceBodies);
            shader.SetInt("_ManifoldCapacity",CoupledContactManifoldBatch.MaxActive);
            shader.SetInt("_BodyCount",sourceBodies.Length);
            shader.SetInt("_SlotCapacity",MaxRows);
            shader.SetInt("_NormalOnly",normalOnly?1:0);
            prepare=shader.FindKernel("PrepareRowArgs");
            build=shader.FindKernel("MarkRows");
            scanLocal=shader.FindKernel("ScanRowsLocal");
            scanBlocks=shader.FindKernel("ScanRowsBlocks");
            scatter=shader.FindKernel("ScatterRows");
            resetFault=shader.FindKernel("ResetRowFault");
            copyBodyPoses=shader.FindKernel("CopyBodyPoses");
            prepareKeys=shader.FindKernel("PrepareKeyArgs");
            buildKeys=shader.FindKernel("BuildContactKeys");
            Bind(resetFault,("_Fault",fault));
            Bind(copyBodyPoses,("_WritableBodies",bodies),("_Fault",fault));
            Bind(prepare,("_ManifoldCount",manifolds.CompactCounter),
                ("_BuildArgs",buildArgs),("_ScanArgs",scanArgs),
                ("_ScatterArgs",scatterArgs));
            Bind(build,("_Pairs",manifolds.CompactPairs),
                ("_Manifolds",manifolds.CompactManifolds),
                ("_ManifoldCount",manifolds.CompactCounter),("_Bodies",bodies),
                ("_Masks",masks),("_Fault",fault));
            Bind(scanLocal,("_ManifoldCount",manifolds.CompactCounter),
                ("_Masks",masks),("_LocalStarts",localStarts),
                ("_BlockSums",blockSums));
            Bind(scanBlocks,("_ManifoldCount",manifolds.CompactCounter),
                ("_BlockSums",blockSums),
                ("_BlockStarts",blockStarts),("_RowCount",rowCount),("_Fault",fault));
            Bind(scatter,("_ManifoldCount",manifolds.CompactCounter),
                ("_Pairs",manifolds.CompactPairs),("_Bodies",bodies),
                ("_PairIndices",manifolds.CompactIndices),
                ("_Manifolds",manifolds.CompactManifolds),
                ("_Masks",masks),("_Fault",fault),
                ("_LocalStarts",localStarts),("_BlockStarts",blockStarts),
                ("_Rows",rows),("_RowManifoldIds",rowManifoldIds),
                ("_RowFeatures",features));
            Bind(prepareKeys,("_RowCount",rowCount),("_KeyArgs",keyArgs),
                ("_Fault",fault));
            Bind(buildKeys,("_Rows",rows),("_RowFeatures",features),
                ("_RowCount",rowCount),
                ("_SourcePrimitives",manifolds.PrimitivesBuffer),
                ("_Fault",fault));
            }
            catch { Dispose();throw; }
        }

        static bool Finite(float x)=>!float.IsNaN(x)&&!float.IsInfinity(x);
        GraphicsBuffer Buffer(int count,int stride,
            GraphicsBuffer.Target target=GraphicsBuffer.Target.Structured)
        {
            var buffer=arena.Allocate(arenaOwner+"/"+(allocationIndex++),count,stride,target);
            owned.Add(buffer);BufferBytes+=(long)count*stride;return buffer;
        }
        void Bind(int kernel,params (string Name,GraphicsBuffer Buffer)[] bindings)
        {foreach(var item in bindings)shader.SetBuffer(kernel,item.Name,item.Buffer);}

        public Result Build()
        {
            if(disposed)throw new ObjectDisposedException(nameof(CoupledContactRowBatch));
            commands.Clear();Record(commands);
            Graphics.ExecuteCommandBuffer(commands);
            var candidate=new uint[1];var active=new uint[1];var count=new uint[1];
            var broadFault=new uint[1];var manifoldFault=new uint[1];var rowFault=new uint[1];
            manifolds.CandidateCounter.GetData(candidate);
            manifolds.CompactCounter.GetData(active);
            manifolds.BroadFaultBuffer.GetData(broadFault);
            manifolds.CompactFaultBuffer.GetData(manifoldFault);
            rowCount.GetData(count);fault.GetData(rowFault);
            var first=new uint[1]{uint.MaxValue};
            if(ContainmentEnabled)FirstContainedBuffer.GetData(first);
            var result=new Result{CandidateCount=candidate[0],ManifoldCount=active[0],
                RowCount=count[0],BroadPhaseFault=broadFault[0],
                ManifoldFault=manifoldFault[0],RowFault=rowFault[0],
                FirstContainedPrimitive=first[0],
                Rows=Array.Empty<CoupledContactRow>(),
                Features=Array.Empty<CoupledContactFeature>()};
            if(result.Success&&count[0]>0)
            {
                result.Rows=new CoupledContactRow[(int)count[0]];
                result.Features=new CoupledContactFeature[(int)count[0]];
                rows.GetData(result.Rows,0,0,result.Rows.Length);
                features.GetData(result.Features,0,0,result.Features.Length);
            }
            return result;
        }

        internal void Record(CommandBuffer target)
        {
            if(disposed)throw new ObjectDisposedException(nameof(CoupledContactRowBatch));
            if(target==null)throw new ArgumentNullException(nameof(target));
            target.DispatchCompute(shader,resetFault,1,1,1);
            manifolds.Record(target);
            RecordRows(target);
        }

        // Rebuild rows against the same corrected pose used by the independent
        // manifold requery. The supplied body record may change its center only.
        internal void RecordRequery(CommandBuffer target,GraphicsBuffer updatedPrimitives,
            GraphicsBuffer updatedBodies,GraphicsBuffer updatedUnionGroups=null)
        {
            if(disposed)throw new ObjectDisposedException(nameof(CoupledContactRowBatch));
            if(target==null)throw new ArgumentNullException(nameof(target));
            if(updatedBodies==null||updatedBodies==bodies||
               updatedBodies.stride!=16||updatedBodies.count<bodyCount)
                throw new ArgumentException("Corrected body poses are required",nameof(updatedBodies));
            target.DispatchCompute(shader,resetFault,1,1,1);
            manifolds.RecordRequery(target,updatedPrimitives,updatedUnionGroups);
            target.SetComputeBufferParam(shader,copyBodyPoses,"_UpdatedBodies",updatedBodies);
            target.DispatchCompute(shader,copyBodyPoses,(bodyCount+63)/64,1,1);
            RecordRows(target);
        }

        void RecordRows(CommandBuffer target)
        {
            target.DispatchCompute(shader,prepare,1,1,1);
            target.DispatchCompute(shader,build,buildArgs,0u);
            target.DispatchCompute(shader,scanLocal,scanArgs,0u);
            target.DispatchCompute(shader,scanBlocks,1,1,1);
            target.DispatchCompute(shader,scatter,scatterArgs,0u);
        }

        // The output belongs to the caller's arena. Full identity remains in
        // the session table; slot generation makes these exact words safe from
        // compact-slot reuse during that session.
        internal void RecordRowMode(CommandBuffer target,bool normalOnly)
        {target.SetComputeIntParam(shader,"_NormalOnly",normalOnly?1:0);}

        internal void RecordKeys(CommandBuffer target,GraphicsBuffer bindings,
            GraphicsBuffer output)
        {
            if(disposed)throw new ObjectDisposedException(nameof(CoupledContactRowBatch));
            if(target==null)throw new ArgumentNullException(nameof(target));
            if(bindings==null||bindings.stride!=36||bindings.count<bodyCount||
               output==null||output.stride!=32||output.count<=0||
               output.count>MaxRows)
                throw new ArgumentException("Invalid V2 contact-key arena");
            target.SetComputeIntParam(shader,"_PrimitiveCount",manifolds.PrimitiveCount);
            target.SetComputeIntParam(shader,"_KeyCapacity",output.count);
            target.DispatchCompute(shader,prepareKeys,1,1,1);
            target.SetComputeBufferParam(shader,buildKeys,"_EndpointBindings",bindings);
            target.SetComputeBufferParam(shader,buildKeys,"_ContactKeys",output);
            target.DispatchCompute(shader,buildKeys,keyArgs,0u);
        }

        public void Dispose()
        {
            if(disposed)return;disposed=true;
            commands.Release();foreach(var buffer in owned)arena.Release(buffer);
            manifolds?.Dispose();
            if(shader!=null)
            {
                if(Application.isPlaying)UnityEngine.Object.Destroy(shader);
                else UnityEngine.Object.DestroyImmediate(shader);
            }
        }
    }
}
