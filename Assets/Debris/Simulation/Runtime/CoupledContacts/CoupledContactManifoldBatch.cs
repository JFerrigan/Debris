using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Debris.Simulation.CoupledContacts
{
    [StructLayout(LayoutKind.Sequential, Pack=4, Size=52)]
    public struct CoupledContactManifold
    {
        public uint Count,ReferenceFace;
        public Vector2 Normal,Point0,Point1;
        public float Gap0,Gap1;
        public uint IncidentFace,Provenance0,Provenance1;
    }

    // Diagnostic GPU-only broad-phase -> narrow-phase -> ordered compaction
    // relative to the broad-phase candidate array.
    // Readback happens only after every GPU stage has completed.
    public sealed class CoupledContactManifoldBatch : IDisposable
    {
        public const int MaxCandidates=CoupledContactBroadPhase.MaxPairs;
        public const int MaxActive=65536;
        const int BlockSize=256;
        readonly CoupledContactBroadPhase broadPhase;
        readonly ComputeShader shader;
        readonly ComputeShader unionShader;
        readonly CommandBuffer commands=new CommandBuffer{name="V2 compact manifolds"};
        readonly List<GraphicsBuffer> owned=new List<GraphicsBuffer>();
        readonly CoupledContactArena arena;
        readonly string arenaOwner;
        int allocationIndex;
        readonly GraphicsBuffer unionManifolds,unionManifoldSlots,unionManifoldCount,
            masks,localStarts,blockSums,
            blockStarts,compactPairs,compactIndices,compactManifolds,compactCount,compactFault,
            markArgs,scanArgs,scatterArgs,validationFacts,validationArgs;
        readonly GraphicsBuffer unionPatches,unionFaces,unionGroups,unionDegrees,unionSlots,
            unionBounds,unionCoveragePatches,unionCoverageRanges,firstContained;
        readonly int prepare,mark,scanLocal,scanBlocks,scatter,
            resetValidation,prepareValidation,gatherValidation,validateClasses;
        readonly int clearUnion,countUnion,buildUnion,resolveUnion,validateUnion,
            resetContainment,copyUnionPoses,buildUnionBounds;
        readonly bool validateContainment;
        readonly int unionGroupCount;
        bool disposed;
        public long BufferBytes { get; private set; }
        internal GraphicsBuffer UnionGroupsBuffer=>unionGroups;
        internal int UnionGroupCount=>unionGroupCount;
        internal bool HasUnion=>unionShader!=null;
        internal GraphicsBuffer CompactPairs=>compactPairs;
        internal GraphicsBuffer CompactIndices=>compactIndices;
        internal GraphicsBuffer CompactManifolds=>compactManifolds;
        internal GraphicsBuffer CompactCounter=>compactCount;
        internal GraphicsBuffer PrimitivesBuffer=>broadPhase.Primitives;
        internal GraphicsBuffer EndpointBindingsBuffer=>broadPhase.EndpointBindings;
        internal int PrimitiveCount=>broadPhase.PrimitiveCount;
        internal GraphicsBuffer BroadFaultBuffer=>broadPhase.FaultBuffer;
        internal GraphicsBuffer CompactFaultBuffer=>compactFault;
        internal GraphicsBuffer CandidateCounter=>broadPhase.CandidateCount;
        internal GraphicsBuffer FirstContainedBuffer=>firstContained;
        internal GraphicsBuffer ValidationFactsBuffer=>validationFacts;
        internal CoupledContactArena Arena=>arena;
        internal bool ContainmentEnabled=>validateContainment;

        public sealed class Result
        {
            public uint CandidateCount,ActiveCount,BroadPhaseFault,CompactionFault;
            public uint FirstContainedPrimitive;
            public CoupledContactPair[] Pairs;
            public CoupledContactManifold[] Manifolds;
            public bool Success=>BroadPhaseFault==0&&CompactionFault==0;
        }

        public struct FinalGapFacts
        {
            public float MaximumGrainPenetration,MaximumSolidPenetration;
            public uint FirstManifold,FirstHardFailure,APrimitive,BPrimitive;
            public uint AEndpoint,BEndpoint,AFeature,BFeature;
            public uint NegativePoints,HardFailurePoints,InvalidRecords,TotalPoints;
            public uint BroadPhaseFault,CompactionFault,FirstContainedPrimitive;
            public uint FirstContainedEndpoint,FirstContainedFeature;
            public bool GeometryComplete=>BroadPhaseFault==0&&CompactionFault==0&&
                InvalidRecords==0;
            public bool HardBoundsPass=>GeometryComplete&&HardFailurePoints==0;
        }

        public CoupledContactManifoldBatch(CoupledContactPrimitive[] primitives,int bodyCount,
            Vector2 origin,float cellSize,int width=128,int height=128,float margin=.25f,
            CoupledUnionGeometry union=null,bool validateContainment=false,
            CoupledEndpointBinding[] endpointBindings=null,
            CoupledContactArena arena=null)
        {
            unionGroupCount=union?.Groups.Length??0;
            this.arena=arena??new CoupledContactArena();
            arenaOwner=this.arena.NewOwner("Manifolds");
            try
            {
            if(validateContainment&&union==null)
                throw new ArgumentException("Containment validation requires union geometry");
            this.validateContainment=validateContainment;
            var asset=Resources.Load<ComputeShader>("CoupledManifoldProbe");
            if(asset==null)throw new InvalidOperationException("Missing CoupledManifoldProbe compute resource");
            var unionAsset=union==null?null:Resources.Load<ComputeShader>("CoupledUnionManifold");
            if(union!=null&&unionAsset==null)
                throw new InvalidOperationException("Missing CoupledUnionManifold compute resource");
            if(primitives!=null)foreach(var p in primitives)if(p.Kind==1)
            {
                if(union==null||p.FaceIndex>=union.Faces.Length||
                   p.UnionIndex>=union.Groups.Length||
                   union.Groups[(int)p.UnionIndex].Reserved!=p.Endpoint)
                    throw new ArgumentException("Exterior proxy lacks its union generation");
            }
            broadPhase=new CoupledContactBroadPhase(primitives,bodyCount,origin,
                cellSize,width,height,margin,endpointBindings,this.arena);
            shader=UnityEngine.Object.Instantiate(asset);
            if(union!=null)unionShader=UnityEngine.Object.Instantiate(unionAsset);
            int blocks=(MaxCandidates+BlockSize-1)/BlockSize;
            unionManifolds=Buffer(union==null?1:MaxActive,52);
            unionManifoldSlots=Buffer(union==null?1:MaxCandidates,4);
            unionManifoldCount=Buffer(1,4);
            masks=Buffer(MaxCandidates,4);localStarts=Buffer(MaxCandidates,4);
            blockSums=Buffer(blocks,4);blockStarts=Buffer(blocks,4);
            compactPairs=Buffer(MaxActive,48);compactIndices=Buffer(MaxActive,8);
            compactManifolds=Buffer(MaxActive,52);
            compactCount=Buffer(1,4);compactFault=Buffer(1,4);
            unionGroups=Buffer(Math.Max(1,union==null?0:union.Groups.Length),32);
            var indirect=GraphicsBuffer.Target.Structured|GraphicsBuffer.Target.IndirectArguments;
            markArgs=Buffer(4,4,indirect);scanArgs=Buffer(4,4,indirect);
            scatterArgs=Buffer(4,4,indirect);
            validationFacts=Buffer(8,4);
            validationArgs=Buffer(4,4,indirect);
            BufferBytes+=broadPhase.BufferBytes;
            shader.SetInt("_CandidateCapacity",MaxCandidates);
            shader.SetInt("_CompactCapacity",MaxActive);
            shader.SetInt("_PrimitiveCount",broadPhase.PrimitiveCount);
            shader.SetInt("_EnableUnion",union==null?0:1);
            shader.SetInt("_UnionGroupCount",union==null?0:union.Groups.Length);
            shader.SetInt("_UnionManifoldCapacity",union==null?0:MaxActive);
            shader.SetFloat("_Margin",margin);
            prepare=shader.FindKernel("PrepareCandidateArgs");
            mark=shader.FindKernel("MarkCandidates");
            scanLocal=shader.FindKernel("ScanCandidateLocal");
            scanBlocks=shader.FindKernel("ScanCandidateBlocks");
            scatter=shader.FindKernel("ScatterCandidates");
            resetValidation=shader.FindKernel("ResetValidationFacts");
            prepareValidation=shader.FindKernel("PrepareValidationArgs");
            gatherValidation=shader.FindKernel("GatherValidationFacts");
            validateClasses=shader.FindKernel("ValidatePrimitiveClasses");
            Bind(prepare,("_CandidateCount",broadPhase.CandidateCount),
                ("_CompactFault",compactFault),
                ("_UnionManifoldCount",unionManifoldCount),
                ("_CandidateMarkArgs",markArgs),("_CandidateScanArgs",scanArgs),
                ("_CandidateScatterArgs",scatterArgs));
            Bind(mark,("_CandidateCount",broadPhase.CandidateCount),
                ("_CandidateIndices",broadPhase.CandidateIndices),
                ("_SourcePrimitives",broadPhase.Primitives),
                ("_UnionManifoldSlots",unionManifoldSlots),
                ("_CandidateMasks",masks),("_CompactFault",compactFault));
            Bind(scanLocal,("_CandidateCount",broadPhase.CandidateCount),
                ("_CandidateMasks",masks),
                ("_CandidateLocalStarts",localStarts),("_CandidateBlockSums",blockSums));
            Bind(scanBlocks,("_CandidateCount",broadPhase.CandidateCount),
                ("_CandidateBlockSums",blockSums),("_CandidateBlockStarts",blockStarts),
                ("_CompactCount",compactCount),("_CompactFault",compactFault));
            Bind(scatter,("_CandidateCount",broadPhase.CandidateCount),
                ("_CandidateIndices",broadPhase.CandidateIndices),
                ("_SourcePrimitives",broadPhase.Primitives),
                ("_UnionGroups",unionGroups),
                ("_ScatterUnionManifolds",unionManifolds),
                ("_ScatterUnionManifoldSlots",unionManifoldSlots),
                ("_ScatterCandidateMasks",masks),
                ("_ScatterCandidateLocalStarts",localStarts),
                ("_ScatterCandidateBlockStarts",blockStarts),
                ("_CompactPairs",compactPairs),("_CompactIndices",compactIndices),
                ("_CompactManifolds",compactManifolds),("_CompactFault",compactFault));
            Bind(resetValidation,("_ValidationFacts",validationFacts));
            Bind(prepareValidation,("_CompactCount",compactCount),
                ("_ValidationArgs",validationArgs));
            Bind(gatherValidation,("_CompactCount",compactCount),
                ("_CompactManifolds",compactManifolds),
                ("_CompactIndices",compactIndices),
                ("_SourcePrimitives",broadPhase.Primitives),
                ("_ValidationFacts",validationFacts));
            Bind(validateClasses,("_SourcePrimitives",broadPhase.Primitives),
                ("_ValidationFacts",validationFacts));
            if(union!=null)
            {
                unionPatches=Buffer(Math.Max(1,union.Patches.Length),20);
                unionFaces=Buffer(Math.Max(1,union.Faces.Length),32);
                unionBounds=Buffer(Math.Max(1,union.WorldBounds.Length),16);
                unionCoveragePatches=Buffer(Math.Max(1,union.CoveragePatches.Length),20);
                unionCoverageRanges=Buffer(Math.Max(1,union.CoverageRanges.Length),8);
                unionDegrees=Buffer(Math.Max(1,broadPhase.PrimitiveCount),4);
                unionSlots=Buffer(Math.Max(1,broadPhase.PrimitiveCount*64),4);
                firstContained=Buffer(1,4);
                if(union.Patches.Length>0)unionPatches.SetData(union.Patches);
                if(union.Faces.Length>0)unionFaces.SetData(union.Faces);
                if(union.Groups.Length>0)unionGroups.SetData(union.Groups);
                if(union.WorldBounds.Length>0)unionBounds.SetData(union.WorldBounds);
                if(union.CoveragePatches.Length>0)
                    unionCoveragePatches.SetData(union.CoveragePatches);
                if(union.CoverageRanges.Length>0)
                    unionCoverageRanges.SetData(union.CoverageRanges);
                unionShader.SetInt("_PatchCount",union.Patches.Length);
                unionShader.SetInt("_FaceCount",union.Faces.Length);
                unionShader.SetInt("_GroupCount",union.Groups.Length);
                unionShader.SetInt("_IndexedPrimitiveCount",broadPhase.PrimitiveCount);
                unionShader.SetInt("_IndexedCapacity",MaxCandidates);
                unionShader.SetInt("_UnionManifoldCapacity",MaxActive);
                unionShader.SetFloat("_Margin",margin);
                clearUnion=unionShader.FindKernel("ClearIndexedUnionDegrees");
                countUnion=unionShader.FindKernel("CountIndexedUnionAdjacency");
                buildUnion=unionShader.FindKernel("BuildIndexedUnionManifolds");
                resolveUnion=unionShader.FindKernel("ResolveIndexedUnionExits");
                validateUnion=unionShader.FindKernel("ValidateIndexedContainment");
                resetContainment=unionShader.FindKernel("ResetFirstContained");
                copyUnionPoses=unionShader.FindKernel("CopyUnionPoses");
                buildUnionBounds=unionShader.FindKernel("BuildUnionWorldBounds");
                UnionBind(resetContainment,("_FirstContainedPrimitive",firstContained));
                UnionBind(copyUnionPoses,("_Groups",unionGroups),
                    ("_WritableGroups",unionGroups),("_Fault",compactFault));
                UnionBind(buildUnionBounds,("_Groups",unionGroups),
                    ("_Patches",unionPatches),
                    ("_WritableUnionBounds",unionBounds),("_Fault",compactFault));
                UnionBind(clearUnion,("_UnionDegrees",unionDegrees));
                UnionBind(countUnion,("_IndexedCount",broadPhase.CandidateCount),
                    ("_IndexedPairIndices",broadPhase.CandidateIndices),
                    ("_IndexedPrimitives",broadPhase.Primitives),
                    ("_UnionDegrees",unionDegrees),("_UnionSlots",unionSlots),
                    ("_Fault",compactFault));
                UnionBind(buildUnion,("_IndexedCount",broadPhase.CandidateCount),
                    ("_IndexedPairIndices",broadPhase.CandidateIndices),
                    ("_IndexedPrimitives",broadPhase.Primitives),
                    ("_UnionManifolds",unionManifolds),
                    ("_UnionManifoldSlots",unionManifoldSlots),
                    ("_UnionManifoldCount",unionManifoldCount),
                    ("_IndexedMasks",masks),
                    ("_Patches",unionPatches),("_Faces",unionFaces),
                    ("_Groups",unionGroups),("_Fault",compactFault));
                UnionBind(resolveUnion,("_IndexedCount",broadPhase.CandidateCount),
                    ("_IndexedPairIndices",broadPhase.CandidateIndices),
                    ("_IndexedPrimitives",broadPhase.Primitives),
                    ("_UnionManifolds",unionManifolds),
                    ("_UnionManifoldSlots",unionManifoldSlots),
                    ("_IndexedMasks",masks),
                    ("_UnionDegrees",unionDegrees),("_UnionSlots",unionSlots),
                    ("_Patches",unionPatches),("_Groups",unionGroups),
                    ("_Fault",compactFault));
                UnionBind(validateUnion,("_IndexedPrimitives",broadPhase.Primitives),
                    ("_IndexedPairIndices",broadPhase.CandidateIndices),
                    ("_IndexedMasks",masks),("_UnionDegrees",unionDegrees),
                    ("_UnionSlots",unionSlots),("_UnionBounds",unionBounds),
                    ("_CoveragePatches",unionCoveragePatches),
                    ("_CoverageRanges",unionCoverageRanges),
                    ("_Patches",unionPatches),("_Groups",unionGroups),
                    ("_FirstContainedPrimitive",firstContained),("_Fault",compactFault));
            }
            }
            catch { Dispose();throw; }
        }

        GraphicsBuffer Buffer(int count,int stride,
            GraphicsBuffer.Target target=GraphicsBuffer.Target.Structured)
        {
            var buffer=arena.Allocate(arenaOwner+"/"+(allocationIndex++),count,stride,target);
            owned.Add(buffer);BufferBytes+=(long)count*stride;return buffer;
        }
        void Bind(int kernel,params (string Name,GraphicsBuffer Buffer)[] bindings)
        {foreach(var binding in bindings)shader.SetBuffer(kernel,binding.Name,binding.Buffer);}
        void UnionBind(int kernel,params (string Name,GraphicsBuffer Buffer)[] bindings)
        {foreach(var binding in bindings)unionShader.SetBuffer(kernel,binding.Name,binding.Buffer);}

        public Result Build()
        {
            if(disposed)throw new ObjectDisposedException(nameof(CoupledContactManifoldBatch));
            commands.Clear();Record(commands);
            Graphics.ExecuteCommandBuffer(commands);
            var candidate=new uint[1];var active=new uint[1];
            var broadFault=new uint[1];var compactionFault=new uint[1];
            broadPhase.CandidateCount.GetData(candidate);
            broadPhase.FaultBuffer.GetData(broadFault);
            compactCount.GetData(active);compactFault.GetData(compactionFault);
            var first=new uint[1]{uint.MaxValue};
            if(validateContainment)firstContained.GetData(first);
            var result=new Result{CandidateCount=candidate[0],ActiveCount=active[0],
                BroadPhaseFault=broadFault[0],CompactionFault=compactionFault[0],
                FirstContainedPrimitive=first[0],
                Pairs=Array.Empty<CoupledContactPair>(),
                Manifolds=Array.Empty<CoupledContactManifold>()};
            if(result.Success&&active[0]>0)
            {
                result.Pairs=new CoupledContactPair[(int)active[0]];
                result.Manifolds=new CoupledContactManifold[(int)active[0]];
                compactPairs.GetData(result.Pairs,0,0,result.Pairs.Length);
                compactManifolds.GetData(result.Manifolds,0,0,result.Manifolds.Length);
            }
            return result;
        }

        // Diagnostic readback after the caller executes RecordRequery.
        // Classification-specific hard bounds remain a later solver decision.
        public FinalGapFacts ReadFinalGapFacts()
        {
            if(disposed)throw new ObjectDisposedException(nameof(CoupledContactManifoldBatch));
            var values=new uint[8];validationFacts.GetData(values);
            var broadFault=new uint[1];var narrowFault=new uint[1];
            broadPhase.FaultBuffer.GetData(broadFault);
            compactFault.GetData(narrowFault);
            var facts=new FinalGapFacts{
                MaximumGrainPenetration=BitConverter.ToSingle(BitConverter.GetBytes(values[0]),0),
                MaximumSolidPenetration=BitConverter.ToSingle(BitConverter.GetBytes(values[1]),0),
                FirstManifold=values[2],FirstHardFailure=values[5],
                APrimitive=uint.MaxValue,BPrimitive=uint.MaxValue,
                AEndpoint=uint.MaxValue,BEndpoint=uint.MaxValue,
                AFeature=uint.MaxValue,BFeature=uint.MaxValue,
                NegativePoints=values[3],HardFailurePoints=values[6],
                InvalidRecords=values[4],TotalPoints=values[7],
                BroadPhaseFault=broadFault[0],
                CompactionFault=narrowFault[0],FirstContainedPrimitive=uint.MaxValue,
                FirstContainedEndpoint=uint.MaxValue,
                FirstContainedFeature=uint.MaxValue};
            if(validateContainment)
            {
                var containedIndex=new uint[1];firstContained.GetData(containedIndex);
                facts.FirstContainedPrimitive=containedIndex[0];
                if(broadPhase.TryGetSourcePrimitive(containedIndex[0],out var contained))
                {
                    facts.FirstContainedEndpoint=contained.Endpoint;
                    facts.FirstContainedFeature=contained.FeatureId;
                }
            }
            uint first=facts.FirstHardFailure<MaxActive?
                facts.FirstHardFailure:facts.FirstManifold;
            var active=new uint[1];compactCount.GetData(active);
            // Containment faults can coexist with a valid compact manifold.
            // Other geometry/capacity faults make its index untrustworthy.
            bool compactValid=facts.BroadPhaseFault==0&&facts.InvalidRecords==0&&
                (facts.CompactionFault&~(32u|64u))==0&&active[0]<=MaxActive;
            if(compactValid&&first<active[0])
            {
                var pair=new Vector2Int[1];
                compactIndices.GetData(pair,0,(int)first,1);
                facts.APrimitive=(uint)pair[0].x;
                facts.BPrimitive=(uint)pair[0].y;
                if(broadPhase.TryGetSourcePrimitive(facts.APrimitive,out var a))
                {facts.AEndpoint=a.Endpoint;facts.AFeature=a.FeatureId;}
                if(broadPhase.TryGetSourcePrimitive(facts.BPrimitive,out var b))
                {facts.BEndpoint=b.Endpoint;facts.BFeature=b.FeatureId;}
            }
            return facts;
        }

        internal void Record(CommandBuffer target)
        {
            if(disposed)throw new ObjectDisposedException(nameof(CoupledContactManifoldBatch));
            if(target==null)throw new ArgumentNullException(nameof(target));
            broadPhase.Record(target);
            RecordNarrowPhase(target);
        }

        // Independent candidate discovery and union bounds from corrected GPU
        // poses. Topology spans and endpoint identity must stay unchanged.
        public void RecordRequery(CommandBuffer target,GraphicsBuffer updatedPrimitives,
            GraphicsBuffer updatedUnionGroups=null)
        {
            if(disposed)throw new ObjectDisposedException(nameof(CoupledContactManifoldBatch));
            if(target==null)throw new ArgumentNullException(nameof(target));
            if(unionShader!=null&&
               (updatedUnionGroups==null||updatedUnionGroups==unionGroups||
                updatedUnionGroups.stride!=32||updatedUnionGroups.count<unionGroups.count))
                throw new ArgumentException("Final-pose union groups are required");
            if(unionShader==null&&updatedUnionGroups!=null)
                throw new ArgumentException("No union groups exist for this query");
            if(unionShader!=null&&!validateContainment)
                throw new InvalidOperationException(
                    "Final-pose union requery requires containment validation");
            broadPhase.RecordRequery(target,updatedPrimitives);
            RecordNarrowPhase(target,updatedUnionGroups,true);
        }

        void RecordNarrowPhase(CommandBuffer target,GraphicsBuffer updatedUnionGroups=null,
            bool gatherFinalFacts=false)
        {
            target.DispatchCompute(shader,prepare,1,1,1);
            if(updatedUnionGroups!=null)
            {
                target.SetComputeBufferParam(unionShader,copyUnionPoses,
                    "_UpdatedGroups",updatedUnionGroups);
                target.DispatchCompute(unionShader,copyUnionPoses,
                    (unionGroups.count+63)/64,1,1);
                target.DispatchCompute(unionShader,buildUnionBounds,
                    (unionGroups.count+63)/64,1,1);
            }
            target.DispatchCompute(shader,mark,markArgs,0u);
            if(unionShader!=null)
            {
                if(validateContainment)
                    target.DispatchCompute(unionShader,resetContainment,1,1,1);
                target.DispatchCompute(unionShader,clearUnion,
                    (Math.Max(1,broadPhase.PrimitiveCount)+63)/64,1,1);
                target.DispatchCompute(unionShader,countUnion,markArgs,0u);
                target.DispatchCompute(unionShader,buildUnion,markArgs,0u);
                target.DispatchCompute(unionShader,resolveUnion,markArgs,0u);
                if(validateContainment)
                    target.DispatchCompute(unionShader,validateUnion,
                        (Math.Max(1,broadPhase.PrimitiveCount)+63)/64,1,1);
            }
            target.DispatchCompute(shader,scanLocal,scanArgs,0u);
            target.DispatchCompute(shader,scanBlocks,1,1,1);
            target.DispatchCompute(shader,scatter,scatterArgs,0u);
            if(gatherFinalFacts)
            {
                target.DispatchCompute(shader,resetValidation,1,1,1);
                target.DispatchCompute(shader,validateClasses,
                    (Math.Max(1,broadPhase.PrimitiveCount)+63)/64,1,1);
                target.DispatchCompute(shader,prepareValidation,1,1,1);
                target.DispatchCompute(shader,gatherValidation,validationArgs,0u);
            }
        }

        public void Dispose()
        {
            if(disposed)return;disposed=true;
            commands.Release();foreach(var buffer in owned)arena.Release(buffer);
            broadPhase?.Dispose();
            if(shader!=null)
            {
                if(Application.isPlaying)UnityEngine.Object.Destroy(shader);
                else UnityEngine.Object.DestroyImmediate(shader);
            }
            if(unionShader!=null)
            {if(Application.isPlaying)UnityEngine.Object.Destroy(unionShader);
             else UnityEngine.Object.DestroyImmediate(unionShader);}
        }
    }
}
