using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Debris.Simulation.CoupledContacts
{
    [StructLayout(LayoutKind.Sequential, Pack=4, Size=56)]
    public struct CoupledContactPrimitive
    {
        public Vector2 Center,Half;
        public float Angle;
        public uint Endpoint;
        public Vector2 Sweep;
        public uint Anchored;
        public uint Kind,FaceIndex,UnionIndex;
        // 0 means unclassified diagnostic input; final validation requires
        // 1 for a grain or 2 for rigid/terrain material.
        public uint ContactClass;
        // Unique within an endpoint's current physical revision. A grain's
        // single convex primitive may use zero; exterior faces use face+1.
        public uint FeatureId;
    }

    // Isolated GPU broad phase for V2-1. The GPU keeps candidate primitive
    // indices; the optional diagnostic readback reconstructs full pairs.
    public sealed class CoupledContactBroadPhase : IDisposable
    {
        public const int MaxPrimitives=16384;
        public const int MaxReferences=1048576;
        public const int MaxPairs=524288;
        public const int MaxLongProxies=64;

        public sealed class Result
        {
            public CoupledContactPair[] Pairs;
            public uint PairCount,LongProxyCount,FaultFlags;
            public bool Success=>FaultFlags==0;
        }

        readonly ComputeShader shader;
        readonly CoupledContactPrimitive[] sourcePrimitives;
        readonly CommandBuffer commands=new CommandBuffer{name="V2 indexed broad phase"};
        readonly List<GraphicsBuffer> owned=new List<GraphicsBuffer>();
        readonly CoupledContactArena arena;
        readonly string arenaOwner;
        int allocationIndex;
        readonly GraphicsBuffer primitives,bounds,binCounts,binCursors,binStarts,
            blockCounts,blockStarts,references,longIds,longCount,pairCount,fault;
        readonly GraphicsBuffer pairIndices,pairSortScratch,pairSortArgs,endpointBindings;
        readonly int clear,build,scanBins,scanBlocks,scatter,find,copy,
            prepareSort,sortSegments,mergeRuns,validateSorted;
        readonly int width,height,binCount,blockCount,primitiveCount;
        bool disposed;
        public long BufferBytes { get; private set; }
        internal GraphicsBuffer CandidateIndices=>pairIndices;
        internal GraphicsBuffer Primitives=>primitives;
        internal GraphicsBuffer EndpointBindings=>endpointBindings;
        internal int PrimitiveCount=>primitiveCount;
        internal GraphicsBuffer CandidateCount=>pairCount;
        internal GraphicsBuffer FaultBuffer=>fault;
        internal bool TryGetSourcePrimitive(uint index,out CoupledContactPrimitive primitive)
        {
            if(index>=sourcePrimitives.Length)
            {primitive=default;return false;}
            primitive=sourcePrimitives[index];
            return true;
        }

        public CoupledContactBroadPhase(CoupledContactPrimitive[] source,int bodyCount,
            Vector2 origin,float cellSize,int width=128,int height=128,float margin=.25f,
            CoupledEndpointBinding[] bindings=null,CoupledContactArena arena=null)
        {
            this.arena=arena??new CoupledContactArena();
            arenaOwner=this.arena.NewOwner("BroadPhase");
            try
            {
            if(source==null||source.Length>MaxPrimitives||bodyCount<=0||bodyCount>8210||
                width<=0||height<=0||width>256||height>256||
                !Finite(origin.x)||!Finite(origin.y)||!Finite(cellSize)||cellSize<=0||
                !Finite(margin)||margin<0)
                throw new ArgumentException("Invalid V2 broad-phase arena or grid");
            var featureIds=new HashSet<ulong>();
            foreach(var p in source)
            {
                if(p.Endpoint>=bodyCount||p.Anchored>1||p.Kind>1||p.ContactClass>2||
                   !Finite(p.Center.x)||!Finite(p.Center.y)||
                   !Finite(p.Half.x)||!Finite(p.Half.y)||p.Half.x<=0||p.Half.y<=0||
                   !Finite(p.Angle)||!Finite(p.Sweep.x)||!Finite(p.Sweep.y))
                    throw new ArgumentException("Invalid contact primitive");
                ulong key=((ulong)p.Endpoint<<32)|p.FeatureId;
                if(!featureIds.Add(key))
                    throw new ArgumentException("Duplicate primitive feature within one endpoint");
            }
            sourcePrimitives=(CoupledContactPrimitive[])source.Clone();
            if(bindings!=null)
            {
                if(bindings.Length!=bodyCount)
                    throw new ArgumentException("Identity binding count must match endpoints");
                var identities=new HashSet<CoupledPersistentIdentity>();
                foreach(var binding in bindings)
                {
                    if(binding.Bound!=1||binding.Identity.Kind<1||
                       binding.Identity.Kind>3||
                       (binding.Identity.Low==0&&binding.Identity.High==0)||
                       binding.Generation==0||binding.SlotGeneration==0||
                       binding.Revision==0||!identities.Add(binding.Identity))
                        throw new ArgumentException("Invalid or duplicate endpoint identity binding");
                }
            }
            this.width=width;this.height=height;
            primitiveCount=source.Length;binCount=width*height;
            blockCount=(binCount+255)/256;
            var asset=Resources.Load<ComputeShader>("CoupledBroadPhase");
            if(asset==null)throw new InvalidOperationException("Missing CoupledBroadPhase compute resource");
            shader=UnityEngine.Object.Instantiate(asset);
            primitives=Buffer(Math.Max(1,primitiveCount),56);
            bounds=Buffer(Math.Max(1,primitiveCount),32);
            binCounts=Buffer(binCount,4);binCursors=Buffer(binCount,4);
            binStarts=Buffer(binCount,4);
            blockCounts=Buffer(blockCount,4);blockStarts=Buffer(blockCount,4);
            references=Buffer(MaxReferences,4);longIds=Buffer(MaxLongProxies,4);
            longCount=Buffer(1,4);
            pairIndices=Buffer(MaxPairs,8);
            pairSortScratch=Buffer(MaxPairs,8);
            pairSortArgs=Buffer(3,4,
                GraphicsBuffer.Target.Structured|GraphicsBuffer.Target.IndirectArguments);
            endpointBindings=Buffer(bindings==null?1:bodyCount,36);
            if(bindings!=null)endpointBindings.SetData(bindings);
            pairCount=Buffer(1,4);fault=Buffer(1,4);
            if(primitiveCount>0)primitives.SetData(sourcePrimitives);
            shader.SetInt("_PrimitiveCount",primitiveCount);
            shader.SetInt("_BinCount",binCount);
            shader.SetInt("_BlockCount",blockCount);
            shader.SetInt("_ReferenceCapacity",MaxReferences);
            shader.SetInt("_PairCapacity",MaxPairs);
            shader.SetInt("_Width",width);shader.SetInt("_Height",height);
            shader.SetVector("_Origin",new Vector4(origin.x,origin.y,0,0));
            shader.SetFloat("_CellSize",cellSize);shader.SetFloat("_Margin",margin);
            shader.SetInt("_HasIdentityBindings",bindings==null?0:1);
            clear=shader.FindKernel("Clear");build=shader.FindKernel("BuildBounds");
            scanBins=shader.FindKernel("ScanBins");scanBlocks=shader.FindKernel("ScanBlocks");
            scatter=shader.FindKernel("Scatter");find=shader.FindKernel("FindPairs");
            copy=shader.FindKernel("CopyPrimitives");
            prepareSort=shader.FindKernel("PreparePairSort");
            sortSegments=shader.FindKernel("SortPairSegments");
            mergeRuns=shader.FindKernel("MergePairRuns");
            validateSorted=shader.FindKernel("ValidateSortedPairs");
            Bind(clear,("_BinCounts",binCounts),("_BinCursors",binCursors),
                ("_BinStarts",binStarts),("_LongCount",longCount),
                ("_PairCount",pairCount),("_Fault",fault));
            Bind(build,("_Primitives",primitives),("_Bounds",bounds),
                ("_BinCounts",binCounts),("_LongIds",longIds),
                ("_LongCount",longCount),("_Fault",fault));
            Bind(scanBins,("_BinCounts",binCounts),("_BinStarts",binStarts),
                ("_BlockCounts",blockCounts));
            Bind(scanBlocks,("_BlockCounts",blockCounts),("_BlockStarts",blockStarts),
                ("_Fault",fault));
            Bind(scatter,("_Bounds",bounds),("_BinCursors",binCursors),
                ("_BinStarts",binStarts),("_BlockStarts",blockStarts),
                ("_References",references),("_Fault",fault));
            Bind(find,("_Primitives",primitives),("_ReadBounds",bounds),
                ("_ReadBinCounts",binCounts),("_ReadBinStarts",binStarts),
                ("_ReadBlockStarts",blockStarts),("_ReadReferences",references),
                ("_ReadLongIds",longIds),("_ReadLongCount",longCount),
                ("_PairIndices",pairIndices),
                ("_PairCount",pairCount),("_Fault",fault));
            Bind(copy,("_Primitives",primitives),
                ("_WritablePrimitives",primitives),("_Fault",fault));
            Bind(prepareSort,("_PairCount",pairCount),
                ("_PairSortArgs",pairSortArgs),("_Fault",fault));
            Bind(sortSegments,("_Primitives",primitives),
                ("_EndpointBindings",endpointBindings),
                ("_PairIndices",pairIndices),("_PairSortScratch",pairSortScratch),
                ("_PairCount",pairCount));
            Bind(validateSorted,("_Primitives",primitives),
                ("_EndpointBindings",endpointBindings),
                ("_PairIndices",pairIndices),("_PairCount",pairCount),
                ("_Fault",fault));
            }
            catch { Dispose();throw; }
        }

        static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
        GraphicsBuffer Buffer(int count,int stride,
            GraphicsBuffer.Target target=GraphicsBuffer.Target.Structured)
        {
            var buffer=arena.Allocate(arenaOwner+"/"+(allocationIndex++),count,stride,target);
            owned.Add(buffer);BufferBytes+=(long)count*stride;return buffer;
        }
        void Bind(int kernel,params (string Name,GraphicsBuffer Buffer)[] bindings)
        {foreach(var binding in bindings)shader.SetBuffer(kernel,binding.Name,binding.Buffer);}

        public Result Build()
        {
            if(disposed)throw new ObjectDisposedException(nameof(CoupledContactBroadPhase));
            commands.Clear();
            Record(commands);
            Graphics.ExecuteCommandBuffer(commands);
            var count=new uint[1];var longTotal=new uint[1];var faults=new uint[1];
            pairCount.GetData(count);longCount.GetData(longTotal);fault.GetData(faults);
            var result=new Result{PairCount=count[0],LongProxyCount=longTotal[0],
                FaultFlags=faults[0],Pairs=Array.Empty<CoupledContactPair>()};
            if(result.Success&&count[0]>0)
            {
                result.Pairs=new CoupledContactPair[(int)count[0]];
                var indices=new Vector2Int[result.Pairs.Length];
                pairIndices.GetData(indices,0,0,indices.Length);
                for(int i=0;i<indices.Length;i++)
                {
                    var a=sourcePrimitives[indices[i].x];
                    var b=sourcePrimitives[indices[i].y];
                    result.Pairs[i]=new CoupledContactPair{
                        ACenter=a.Center,AHalf=a.Half,AAngle=a.Angle,AEndpoint=a.Endpoint,
                        BCenter=b.Center,BHalf=b.Half,BAngle=b.Angle,BEndpoint=b.Endpoint};
                }
            }
            return result;
        }

        internal void Record(CommandBuffer target)
        {
            if(disposed)throw new ObjectDisposedException(nameof(CoupledContactBroadPhase));
            target.DispatchCompute(shader,clear,(binCount+63)/64,1,1);
            RecordIndex(target);
        }

        void RecordIndex(CommandBuffer target)
        {
            if(primitiveCount>0)target.DispatchCompute(shader,build,(primitiveCount+63)/64,1,1);
            target.DispatchCompute(shader,scanBins,blockCount,1,1);
            target.DispatchCompute(shader,scanBlocks,1,1,1);
            if(primitiveCount>0)
            {
                target.DispatchCompute(shader,scatter,(primitiveCount+63)/64,1,1);
                target.DispatchCompute(shader,find,(primitiveCount+63)/64,1,1);
                target.DispatchCompute(shader,prepareSort,1,1,1);
                target.DispatchCompute(shader,sortSegments,pairSortArgs,0u);
                for(int pass=0;pass<13;pass++)
                {
                    var input=(pass&1)==0?pairSortScratch:pairIndices;
                    var output=(pass&1)==0?pairIndices:pairSortScratch;
                    target.SetComputeIntParam(shader,"_MergeWidth",64<<pass);
                    target.SetComputeBufferParam(shader,mergeRuns,"_SortInput",input);
                    target.SetComputeBufferParam(shader,mergeRuns,"_SortOutput",output);
                    target.SetComputeBufferParam(shader,mergeRuns,"_Primitives",primitives);
                    target.SetComputeBufferParam(shader,mergeRuns,
                        "_EndpointBindings",endpointBindings);
                    target.SetComputeBufferParam(shader,mergeRuns,"_PairCount",pairCount);
                    target.SetComputeBufferParam(shader,mergeRuns,"_Fault",fault);
                    target.DispatchCompute(shader,mergeRuns,pairSortArgs,0u);
                }
                target.DispatchCompute(shader,validateSorted,pairSortArgs,0u);
            }
        }

        // The solver supplies corrected 56-byte primitive records on the GPU.
        // This runs a fresh spatial query from that pose in the same queue.
        internal void RecordRequery(CommandBuffer target,GraphicsBuffer updatedPrimitives)
        {
            if(disposed)throw new ObjectDisposedException(nameof(CoupledContactBroadPhase));
            if(target==null)throw new ArgumentNullException(nameof(target));
            if(updatedPrimitives==null||updatedPrimitives==primitives||
               updatedPrimitives.count<primitiveCount||updatedPrimitives.stride!=56)
                throw new ArgumentException("Invalid final-pose primitive buffer");
            target.DispatchCompute(shader,clear,(binCount+63)/64,1,1);
            if(primitiveCount>0)
            {
                target.SetComputeBufferParam(shader,copy,"_UpdatedPrimitives",updatedPrimitives);
                target.DispatchCompute(shader,copy,(primitiveCount+63)/64,1,1);
            }
            RecordIndex(target);
        }

        public void Dispose()
        {
            if(disposed)return;disposed=true;
            commands.Release();foreach(var buffer in owned)arena.Release(buffer);
            if(shader!=null)
            {
                if(Application.isPlaying)UnityEngine.Object.Destroy(shader);
                else UnityEngine.Object.DestroyImmediate(shader);
            }
        }
    }
}
