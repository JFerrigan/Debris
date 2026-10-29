using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Debris.Simulation.CoupledContacts
{
    [StructLayout(LayoutKind.Sequential, Pack=4, Size=16)]
    public struct CoupledContactBody
    {
        public Vector2 Center;
        public float InverseMass,InverseInertia;
    }

    [StructLayout(LayoutKind.Sequential, Pack=4, Size=32)]
    public struct CoupledContactRow
    {
        public uint A,B;
        public Vector2 Point,Direction;
        public float InverseRoot,Gap;
    }

    [StructLayout(LayoutKind.Sequential, Pack=4, Size=48)]
    public struct CoupledContactPair
    {
        public Vector2 ACenter,AHalf;
        public float AAngle;
        public uint AEndpoint;
        public Vector2 BCenter,BHalf;
        public float BAngle;
        public uint BEndpoint;
    }

    // Isolated frozen-graph GPU product for V2-1. The caller supplies contact
    // rows; no gameplay state or legacy solver buffer is modified by this probe.
    public sealed class CoupledContactOperator : IDisposable
    {
        public sealed class Result
        {
            public Vector4[] EndpointMotion;
            public float[] RowVelocity;
            public CoupledContactRow[] Rows;
            public CoupledContactFeature[] Features=Array.Empty<CoupledContactFeature>();
            public int ActiveRows;
            public uint BroadPhaseFault,ManifoldFault,RowFault;
            public uint FirstContainedPrimitive=uint.MaxValue;
            public bool Success=>BroadPhaseFault==0&&ManifoldFault==0&&RowFault==0;
        }

        readonly ComputeShader shader;
        readonly ComputeShader manifoldShader;
        readonly CoupledContactRowBatch generatedRows;
        readonly CoupledEndpointIdentityTable endpointIdentities;
        readonly uint identityTableRevision;
        readonly CommandBuffer commands=new CommandBuffer{name="V2 frozen contact operator"};
        readonly List<GraphicsBuffer> owned=new List<GraphicsBuffer>();
        readonly CoupledContactArena arena;
        readonly string arenaOwner;
        int allocationIndex;
        readonly GraphicsBuffer bodyBuffer,rowBuffer,rowManifoldIds,xBuffer,freeBuffer,adjacencyBuffer,
            sortedAdjacencyBuffer,
            segmentBuffer,segmentStartBuffer,sideBuffer,partialBuffer,motionBuffer,yBuffer,
            degreeBuffer,cursorBuffer,adjLocalBuffer,segmentLocalBuffer,adjBlockSums,
            segmentBlockSums,adjBlockStarts,segmentBlockStarts,segmentArgs,rowArgs,
            singleRowArgs,singleSegmentArgs,singleBodyArgs,
            pairBuffer,manifoldBuffer,actualRowCountBuffer,identityBuffer;
        readonly int prepareRows,prepareMasked,prepareSingleMasked,prepareStages,stageNoOp,
            expand,clear,count,scanLocal,scanBlocks,scatter,buildSegments,
            sortSegments,mergeRuns,
            reduce,applyBodies,applyRows,buildManifolds,buildRows;
        readonly int bodyCount,rowCount,blockCount,pairCount;
        readonly bool gpuPairs,gpuGeneratedRows;
        readonly bool normalOnlyRows;
        bool disposed;
        public int BodyCount=>bodyCount;
        public int RowCount=>rowCount;
        public int ActualRowCount { get; private set; }
        public int SegmentCount { get; private set; }
        public int MaxSegmentsPerBody { get; }
        public long BufferBytes { get; private set; }
        public CoupledContactArena Arena=>arena;
        internal CoupledContactManifoldBatch Manifolds=>generatedRows?.Manifolds;
        internal GraphicsBuffer RowFault=>generatedRows?.RowFaultBuffer;
        internal GraphicsBuffer ProductInput=>xBuffer;
        internal GraphicsBuffer RowsBuffer=>rowBuffer;
        internal GraphicsBuffer RowManifoldIds=>rowManifoldIds;
        internal GraphicsBuffer BodiesBuffer=>bodyBuffer;
        internal GraphicsBuffer FreeMotionInput=>freeBuffer;
        internal GraphicsBuffer ProductOutput=>yBuffer;
        internal GraphicsBuffer EndpointOutput=>motionBuffer;
        internal GraphicsBuffer ActiveRowCounter=>actualRowCountBuffer;
        internal GraphicsBuffer EndpointIdentities=>identityBuffer;
        internal bool NormalOnlyRows=>normalOnlyRows;
        internal GraphicsBuffer RowFeatures=>generatedRows?.FeaturesBuffer;
        internal GraphicsBuffer SourcePrimitives=>generatedRows?.PrimitivesBuffer;
        internal int SourcePrimitiveCount=>generatedRows?.PrimitiveCount??0;

        public CoupledContactOperator(CoupledContactBody[] bodies,CoupledContactRow[] rows)
            :this(bodies,rows,null,null,Vector2.zero,1,128,128,null,false,null,null){}

        public CoupledContactOperator(CoupledContactBody[] bodies,CoupledContactPair[] pairs)
            :this(bodies,null,pairs,null,Vector2.zero,1,128,128,null,false,null,null){}

        public CoupledContactOperator(CoupledContactBody[] bodies,
            CoupledContactPrimitive[] primitives,Vector2 origin,float cellSize,
            int width=128,int height=128,CoupledUnionGeometry union=null,
            bool validateContainment=false,
            CoupledEndpointIdentityTable endpointIdentities=null,
            CoupledContactArena arena=null,bool normalOnly=false)
            :this(bodies,null,null,primitives,origin,cellSize,width,height,union,
                validateContainment,endpointIdentities,arena,normalOnly){}

        CoupledContactOperator(CoupledContactBody[] bodies,CoupledContactRow[] rows,
            CoupledContactPair[] pairs,CoupledContactPrimitive[] primitives,
            Vector2 origin,float cellSize,int width,int height,CoupledUnionGeometry union,
            bool validateContainment,CoupledEndpointIdentityTable endpointIdentities,
            CoupledContactArena arena,bool normalOnly=false)
        {
            this.arena=arena??new CoupledContactArena();
            arenaOwner=this.arena.NewOwner("Operator");
            try
            {
            long initialArenaBytes=this.arena.UsedBytes;
            int sources=(rows!=null?1:0)+(pairs!=null?1:0)+(primitives!=null?1:0);
            if(bodies==null||bodies.Length==0||bodies.Length>8210||sources!=1||
                (rows!=null&&rows.Length>262144)||(pairs!=null&&pairs.Length>65536))
                throw new ArgumentException("Frozen graph exceeds the V2-1 endpoint or row limit");
            bodyCount=bodies.Length;gpuPairs=pairs!=null;gpuGeneratedRows=primitives!=null;
            normalOnlyRows=normalOnly;
            if(normalOnly&&!gpuGeneratedRows)
                throw new ArgumentException("Normal-only rows require GPU generated geometry");
            this.endpointIdentities=endpointIdentities;
            CoupledEndpointBinding[] endpointBindings=null;
            if(endpointIdentities!=null)
            {
                if(!gpuGeneratedRows||endpointIdentities.Capacity!=bodyCount)
                    throw new ArgumentException("Identity table must match generated endpoint slots");
                identityTableRevision=endpointIdentities.TableRevision;
                endpointBindings=endpointIdentities.Snapshot();
                for(int i=0;i<endpointBindings.Length;i++)
                    if(endpointBindings[i].Bound==0)
                        throw new ArgumentException(
                            "Every active V2 endpoint needs a persistent identity");
            }
            pairCount=gpuPairs?pairs.Length:0;
            rowCount=gpuGeneratedRows?CoupledContactRowBatch.MaxRows:
                gpuPairs?pairCount*4:rows.Length;
            var degrees=new int[bodyCount];
            for(int i=0;i<bodyCount;i++)
            {
                var b=bodies[i];
                if(!Finite(b.Center.x)||!Finite(b.Center.y)||!Finite(b.InverseMass)||!Finite(b.InverseInertia)||
                    b.InverseMass<0||b.InverseInertia<0)throw new ArgumentException("Invalid body mass or center");
            }
            if(gpuPairs)foreach(var pair in pairs)
            {
                if(pair.AEndpoint>=bodyCount||pair.BEndpoint>=bodyCount||pair.AEndpoint==pair.BEndpoint||
                    !Finite(pair.ACenter.x)||!Finite(pair.ACenter.y)||!Finite(pair.AHalf.x)||!Finite(pair.AHalf.y)||
                    !Finite(pair.BCenter.x)||!Finite(pair.BCenter.y)||!Finite(pair.BHalf.x)||!Finite(pair.BHalf.y)||
                    !Finite(pair.AAngle)||!Finite(pair.BAngle)||pair.AHalf.x<=0||pair.AHalf.y<=0||
                    pair.BHalf.x<=0||pair.BHalf.y<=0)
                    throw new ArgumentException("Invalid convex pair");
            }
            for(int i=0;!gpuPairs&&!gpuGeneratedRows&&i<rowCount;i++)
            {
                var row=rows[i];
                if(row.A>=bodyCount||row.B>=bodyCount||row.A==row.B||
                    !Finite(row.Point.x)||!Finite(row.Point.y)||!Finite(row.Direction.x)||!Finite(row.Direction.y)||
                    !Finite(row.InverseRoot)||row.InverseRoot<=0||
                    Math.Abs(row.Direction.sqrMagnitude-1)>1e-3f)
                    throw new ArgumentException("Invalid frozen contact row");
                var a=bodies[row.A];var b=bodies[row.B];
                Vector2 armA=row.Point-a.Center,armB=row.Point-b.Center;
                float torqueA=armA.x*row.Direction.y-armA.y*row.Direction.x;
                float torqueB=armB.x*row.Direction.y-armB.y*row.Direction.x;
                double diagonal=a.InverseMass+b.InverseMass+
                    a.InverseInertia*torqueA*torqueA+b.InverseInertia*torqueB*torqueB;
                if(!(diagonal>0)||Math.Abs(row.InverseRoot*Math.Sqrt(diagonal)-1)>1e-4)
                    throw new ArgumentException("Row scaling does not match physical mass and inertia");
                degrees[row.A]++;degrees[row.B]++;
            }
            int segments=0,maxSegments=0;
            for(int body=0;body<bodyCount;body++)
            {
                int count=(degrees[body]+63)/64;
                segments+=count;maxSegments=Math.Max(maxSegments,count);
            }
            SegmentCount=gpuPairs||gpuGeneratedRows?-1:segments;
            MaxSegmentsPerBody=gpuPairs||gpuGeneratedRows?-1:maxSegments;
            blockCount=(bodyCount+255)/256;
            var asset=Resources.Load<ComputeShader>("CoupledContactOperator");
            if(asset==null)throw new InvalidOperationException("Missing CoupledContactOperator compute resource");
            var manifoldAsset=gpuPairs?Resources.Load<ComputeShader>("CoupledManifoldProbe"):null;
            if(gpuPairs&&manifoldAsset==null)throw new InvalidOperationException("Missing CoupledManifoldProbe compute resource");
            if(gpuGeneratedRows)
            {
                generatedRows=new CoupledContactRowBatch(bodies,primitives,origin,cellSize,
                    width,height,.25f,union,validateContainment,endpointBindings,
                    this.arena,normalOnly);
                BufferBytes+=generatedRows.BufferBytes;
            }
            shader=UnityEngine.Object.Instantiate(asset);
            if(gpuPairs)manifoldShader=UnityEngine.Object.Instantiate(manifoldAsset);
            // Generated rows and the operator must read the same body pose.
            bodyBuffer=gpuGeneratedRows?generatedRows.BodiesBuffer:Buffer(bodyCount,16);
            if(endpointIdentities!=null)
                identityBuffer=generatedRows.EndpointBindingsBuffer;
            rowBuffer=gpuGeneratedRows?generatedRows.RowsBuffer:Buffer(Math.Max(1,rowCount),32);
            rowManifoldIds=gpuGeneratedRows?generatedRows.RowManifoldIdsBuffer:
                Buffer(Math.Max(1,rowCount),4);
            if(!gpuGeneratedRows&&rowCount>0)
            {
                var manifoldIds=new uint[rowCount];
                for(int i=0;i<rowCount;i++)
                    manifoldIds[i]=(uint)(gpuPairs?i/4:i/2);
                rowManifoldIds.SetData(manifoldIds);
            }
            actualRowCountBuffer=gpuGeneratedRows?generatedRows.RowCounter:Buffer(1,4);
            if(!gpuGeneratedRows)actualRowCountBuffer.SetData(new[]{(uint)rowCount});
            if(gpuPairs)
            {
                pairBuffer=Buffer(Math.Max(1,pairCount),48);
                manifoldBuffer=Buffer(Math.Max(1,pairCount),52);
                if(pairCount>0)pairBuffer.SetData(pairs);
                manifoldShader.SetInt("_PairCount",pairCount);
                manifoldShader.SetInt("_BodyCount",bodyCount);
                manifoldShader.SetFloat("_Margin",.25f);
                buildManifolds=manifoldShader.FindKernel("BuildManifolds");
                buildRows=manifoldShader.FindKernel("BuildRows");
                manifoldShader.SetBuffer(buildManifolds,"_Pairs",pairBuffer);
                manifoldShader.SetBuffer(buildManifolds,"_Manifolds",manifoldBuffer);
                manifoldShader.SetBuffer(buildRows,"_Pairs",pairBuffer);
                manifoldShader.SetBuffer(buildRows,"_Manifolds",manifoldBuffer);
                manifoldShader.SetBuffer(buildRows,"_Bodies",bodyBuffer);
                manifoldShader.SetBuffer(buildRows,"_Rows",rowBuffer);
            }
            xBuffer=Buffer(Math.Max(1,rowCount),4);freeBuffer=Buffer(bodyCount,16);
            adjacencyBuffer=Buffer(Math.Max(1,rowCount*2),4);
            sortedAdjacencyBuffer=Buffer(Math.Max(1,rowCount*2),4);
            segmentBuffer=Buffer(Math.Max(1,bodyCount+(rowCount*2+63)/64),16);
            segmentStartBuffer=Buffer(bodyCount+1,4);
            degreeBuffer=Buffer(bodyCount,4);cursorBuffer=Buffer(bodyCount,4);
            adjLocalBuffer=Buffer(bodyCount,4);segmentLocalBuffer=Buffer(bodyCount,4);
            adjBlockSums=Buffer(blockCount,4);segmentBlockSums=Buffer(blockCount,4);
            adjBlockStarts=Buffer(blockCount,4);segmentBlockStarts=Buffer(blockCount,4);
            segmentArgs=Buffer(4,4,GraphicsBuffer.Target.Structured|GraphicsBuffer.Target.IndirectArguments);
            rowArgs=Buffer(4,4,GraphicsBuffer.Target.Structured|GraphicsBuffer.Target.IndirectArguments);
            var indirect=GraphicsBuffer.Target.Structured|GraphicsBuffer.Target.IndirectArguments;
            singleRowArgs=Buffer(4,4,indirect);
            singleSegmentArgs=Buffer(4,4,indirect);
            singleBodyArgs=Buffer(4,4,indirect);
            sideBuffer=Buffer(Math.Max(1,rowCount*2),16);
            partialBuffer=Buffer(Math.Max(1,bodyCount+(rowCount*2+63)/64),16);
            motionBuffer=Buffer(bodyCount,16);yBuffer=Buffer(Math.Max(1,rowCount),4);
            bodyBuffer.SetData(bodies);
            if(!gpuPairs&&!gpuGeneratedRows&&rowCount>0)rowBuffer.SetData(rows);
            shader.SetInt("_BodyCount",bodyCount);shader.SetInt("_RowCount",rowCount);
            shader.SetInt("_BlockCount",blockCount);
            prepareRows=shader.FindKernel("PrepareRowDispatch");
            prepareMasked=shader.FindKernel("PrepareMaskedProducts");
            prepareSingleMasked=shader.FindKernel("PrepareSingleMaskedProduct");
            prepareStages=shader.FindKernel("PrepareMaskedStages");
            stageNoOp=shader.FindKernel("MaskedStageNoOp");
            expand=shader.FindKernel("ExpandRows");clear=shader.FindKernel("ClearIncidence");
            count=shader.FindKernel("CountIncidence");scanLocal=shader.FindKernel("ScanIncidenceLocal");
            scanBlocks=shader.FindKernel("ScanIncidenceBlocks");scatter=shader.FindKernel("ScatterIncidence");
            buildSegments=shader.FindKernel("BuildSegments");reduce=shader.FindKernel("ReduceSegments");
            sortSegments=shader.FindKernel("SortAdjacencySegments");
            mergeRuns=shader.FindKernel("MergeAdjacencyRuns");
            applyBodies=shader.FindKernel("ApplyBodies");applyRows=shader.FindKernel("ApplyRows");
            Bind(prepareRows,("_ActualRowCount",actualRowCountBuffer),("_RowArgs",rowArgs));
            Bind(prepareSingleMasked,("_RowArgs",rowArgs),
                ("_SegmentArgs",segmentArgs),
                ("_SingleRowArgs",singleRowArgs),
                ("_SingleSegmentArgs",singleSegmentArgs),
                ("_SingleBodyArgs",singleBodyArgs));
            Bind(expand,("_Rows",rowBuffer),("_ActualRowCount",actualRowCountBuffer),
                ("_Bodies",bodyBuffer),("_X",xBuffer),("_SideReactions",sideBuffer));
            Bind(clear,("_Degrees",degreeBuffer),("_Cursors",cursorBuffer));
            Bind(count,("_Rows",rowBuffer),("_ActualRowCount",actualRowCountBuffer),
                ("_Degrees",degreeBuffer));
            Bind(scanLocal,("_Degrees",degreeBuffer),("_AdjLocalStarts",adjLocalBuffer),
                ("_SegmentLocalStarts",segmentLocalBuffer),("_AdjBlockSums",adjBlockSums),
                ("_SegmentBlockSums",segmentBlockSums));
            Bind(scanBlocks,("_AdjBlockSums",adjBlockSums),("_SegmentBlockSums",segmentBlockSums),
                ("_AdjBlockStarts",adjBlockStarts),("_SegmentBlockStarts",segmentBlockStarts),
                ("_SegmentArgs",segmentArgs));
            Bind(scatter,("_Rows",rowBuffer),("_ActualRowCount",actualRowCountBuffer),
                ("_Cursors",cursorBuffer),("_AdjLocalStarts",adjLocalBuffer),
                ("_AdjBlockStarts",adjBlockStarts),("_Adjacency",adjacencyBuffer));
            Bind(buildSegments,("_Degrees",degreeBuffer),("_AdjLocalStarts",adjLocalBuffer),
                ("_AdjBlockStarts",adjBlockStarts),("_SegmentLocalStarts",segmentLocalBuffer),
                ("_SegmentBlockStarts",segmentBlockStarts),("_Segments",segmentBuffer),
                ("_BodySegmentStarts",segmentStartBuffer),("_SegmentArgs",segmentArgs));
            Bind(sortSegments,("_Adjacency",adjacencyBuffer),
                ("_SortedAdjacency",sortedAdjacencyBuffer),
                ("_Segments",segmentBuffer),("_SegmentArgs",segmentArgs));
            Bind(mergeRuns,("_Segments",segmentBuffer),
                ("_BodySegmentStarts",segmentStartBuffer),
                ("_Degrees",degreeBuffer),("_SegmentArgs",segmentArgs));
            Bind(reduce,("_Adjacency",sortedAdjacencyBuffer),("_Segments",segmentBuffer),
                ("_SideReactions",sideBuffer),("_SegmentReactions",partialBuffer),("_SegmentArgs",segmentArgs));
            Bind(applyBodies,("_Bodies",bodyBuffer),("_BodySegmentStarts",segmentStartBuffer),
                ("_SegmentReactions",partialBuffer),("_FreeMotion",freeBuffer),("_Motion",motionBuffer));
            Bind(applyRows,("_Rows",rowBuffer),("_ActualRowCount",actualRowCountBuffer),
                ("_Bodies",bodyBuffer),("_Motion",motionBuffer),("_Y",yBuffer));
            if(this.arena.UsedBytes-initialArenaBytes!=BufferBytes)
                throw new InvalidOperationException("V2 graph allocation ledger mismatch");
            }
            catch { Dispose();throw; }
        }

        static bool Finite(float x)=>!float.IsNaN(x)&&!float.IsInfinity(x);
        GraphicsBuffer Buffer(int count,int stride,GraphicsBuffer.Target target=GraphicsBuffer.Target.Structured)
        {
            var buffer=arena.Allocate(arenaOwner+"/"+(allocationIndex++),count,stride,target);
            owned.Add(buffer);BufferBytes+=(long)count*stride;return buffer;
        }
        void Bind(int kernel,params (string Name,GraphicsBuffer Buffer)[] bindings)
        {foreach(var binding in bindings)shader.SetBuffer(kernel,binding.Name,binding.Buffer);}

        // Build the frozen contact graph once per geometry generation. A
        // Krylov solve records products against these buffers without a scan.
        internal void RecordGraph(CommandBuffer target)
        {
            if(disposed)throw new ObjectDisposedException(nameof(CoupledContactOperator));
            if(target==null)throw new ArgumentNullException(nameof(target));
            CheckIdentityGeneration();
            if(gpuGeneratedRows)generatedRows.Record(target);
            RecordIncidence(target);
        }

        // Rebuild the frozen graph at a corrected pose while retaining the
        // operator's body mass/inertia and using its shared row-builder buffer.
        internal void RecordRequeryGraph(CommandBuffer target,GraphicsBuffer updatedPrimitives,
            GraphicsBuffer updatedBodies,GraphicsBuffer updatedUnionGroups=null)
        {
            if(disposed)throw new ObjectDisposedException(nameof(CoupledContactOperator));
            if(target==null)throw new ArgumentNullException(nameof(target));
            CheckIdentityGeneration();
            if(!gpuGeneratedRows)throw new InvalidOperationException(
                "Corrected-pose requery requires generated contact rows");
            generatedRows.RecordRequery(target,updatedPrimitives,updatedBodies,
                updatedUnionGroups);
            RecordIncidence(target);
        }

        void RecordIncidence(CommandBuffer target)
        {
            if(pairCount>0)
            {
                target.DispatchCompute(manifoldShader,buildManifolds,(pairCount+63)/64,1,1);
                target.DispatchCompute(manifoldShader,buildRows,(pairCount+63)/64,1,1);
            }
            target.DispatchCompute(shader,prepareRows,1,1,1);
            target.DispatchCompute(shader,clear,(bodyCount+63)/64,1,1);
            target.DispatchCompute(shader,count,rowArgs,0u);
            target.DispatchCompute(shader,scanLocal,blockCount,1,1);
            target.DispatchCompute(shader,scanBlocks,1,1,1);
            target.DispatchCompute(shader,scatter,rowArgs,0u);
            target.DispatchCompute(shader,buildSegments,(bodyCount+64)/64,1,1);
            target.DispatchCompute(shader,sortSegments,segmentArgs,0u);
            // Degree <= row capacity (262,144). Twelve merges grow sorted
            // runs from 64 to that cap; the last result is in the scratch.
            for(int pass=0;pass<12;pass++)
            {
                bool even=(pass&1)==0;
                target.SetComputeIntParam(shader,"_MergeSpan",64<<pass);
                target.SetComputeBufferParam(shader,mergeRuns,"_MergeInput",
                    even?sortedAdjacencyBuffer:adjacencyBuffer);
                target.SetComputeBufferParam(shader,mergeRuns,"_MergeOutput",
                    even?adjacencyBuffer:sortedAdjacencyBuffer);
                target.DispatchCompute(shader,mergeRuns,segmentArgs,0u);
            }
        }

        void CheckIdentityGeneration()
        {
            if(endpointIdentities!=null&&
               endpointIdentities.TableRevision!=identityTableRevision)
                throw new InvalidOperationException(
                    "Endpoint identity generation changed; rebuild the frozen graph");
        }

        // The caller writes ProductInput and FreeMotionInput before recording
        // this sequence. It writes the endpoint and row results on the GPU.
        internal void RecordProduct(CommandBuffer target)
        {
            if(disposed)throw new ObjectDisposedException(nameof(CoupledContactOperator));
            if(target==null)throw new ArgumentNullException(nameof(target));
            CheckIdentityGeneration();
            target.DispatchCompute(shader,expand,rowArgs,0u);
            target.DispatchCompute(shader,reduce,segmentArgs,0u);
            target.DispatchCompute(shader,applyBodies,bodyCount,1,1);
            target.DispatchCompute(shader,applyRows,rowArgs,0u);
        }

        // One gate is read immediately before these four indirect dispatches.
        // A zero gate writes zero groups and does not change product outputs.
        internal void RecordProductMasked(CommandBuffer target,GraphicsBuffer gate)
        {
            if(disposed)throw new ObjectDisposedException(nameof(CoupledContactOperator));
            if(target==null)throw new ArgumentNullException(nameof(target));
            if(gate==null||gate.stride!=4||gate.count<1)
                throw new ArgumentException("A GPU uint product gate is required",nameof(gate));
            CheckIdentityGeneration();
            target.SetComputeBufferParam(shader,prepareSingleMasked,"_SingleGate",gate);
            target.DispatchCompute(shader,prepareSingleMasked,1,1,1);
            target.DispatchCompute(shader,expand,singleRowArgs,0u);
            target.DispatchCompute(shader,reduce,singleSegmentArgs,0u);
            target.DispatchCompute(shader,applyBodies,singleBodyArgs,0u);
            target.DispatchCompute(shader,applyRows,singleRowArgs,0u);
        }

        // Record after graph construction. The caller supplies a 32-byte key
        // arena and checks the row fault before consuming its contents.
        public void RecordContactKeys(CommandBuffer target,GraphicsBuffer output)
        {
            if(disposed)throw new ObjectDisposedException(nameof(CoupledContactOperator));
            if(target==null)throw new ArgumentNullException(nameof(target));
            CheckIdentityGeneration();
            if(!gpuGeneratedRows||identityBuffer==null)
                throw new InvalidOperationException(
                    "GPU contact keys require generated rows and endpoint identities");
            generatedRows.RecordKeys(target,identityBuffer,output);
        }

        // Record after graph construction. The matched index is advisory until
        // the caller checks row and matcher faults and V2-2's normal/anchor/gap
        // warm-start conditions. Neither buffer is copied to the CPU here.
        public void RecordContactKeyMatches(CommandBuffer target,
            CoupledContactKeyMatcher matcher,GraphicsBuffer previousKeys,
            GraphicsBuffer previousCount,GraphicsBuffer currentKeys)
        {
            if(matcher==null)throw new ArgumentNullException(nameof(matcher));
            RecordContactKeys(target,currentKeys);
            matcher.Record(target,previousKeys,previousCount,currentKeys,
                actualRowCountBuffer);
        }

        public enum SolveProfile { C1,C2,C3 }

        static void ProfileCaps(SolveProfile profile,out int velocity,
            out int products,out int geometry)
        {
            switch(profile)
            {
                case SolveProfile.C1:velocity=4;products=16;geometry=2;break;
                case SolveProfile.C2:velocity=6;products=24;geometry=3;break;
                case SolveProfile.C3:velocity=8;products=32;geometry=4;break;
                default:throw new ArgumentOutOfRangeException(nameof(profile));
            }
        }

        // Frozen-graph product slots for the profile's worst permitted update
        // count. The eight line-search trials and geometry refreshes are not
        // represented here; they require their own schedule stages.
        public sealed class MaskedProductSchedule : IDisposable
        {
            readonly CoupledContactOperator owner;
            readonly string arenaOwner;
            readonly GraphicsBuffer rowDispatch,segmentDispatch,bodyDispatch;
            bool disposed;
            public GraphicsBuffer Gates { get; }
            public int ProductSlots { get; }
            public int Substeps { get; }
            public long BufferBytes { get; }
            public int RecordedDispatches=>checked(ProductSlots*4+1);

            internal MaskedProductSchedule(CoupledContactOperator owner,SolveProfile profile,
                int substeps)
            {
                if(substeps<4||substeps>16)
                    throw new ArgumentOutOfRangeException(nameof(substeps));
                this.owner=owner;Substeps=substeps;
                try
                {
                ProfileCaps(profile,out int velocity,out int products,out int geometry);
                // Four position Newton updates per geometry refresh; three
                // residual B products are separate from the Krylov cap.
                ProductSlots=checked(substeps*(velocity+geometry*4)*(products+3));
                var indirect=GraphicsBuffer.Target.Structured|
                    GraphicsBuffer.Target.IndirectArguments;
                BufferBytes=(long)ProductSlots*52;
                owner.arena.EnsureCapacity(BufferBytes);
                arenaOwner=owner.arena.NewOwner("ProductSchedule");
                Gates=owner.arena.Allocate(arenaOwner+"/gates",ProductSlots,4);
                rowDispatch=owner.arena.Allocate(arenaOwner+"/rows",ProductSlots*4,4,indirect);
                segmentDispatch=owner.arena.Allocate(arenaOwner+"/segments",ProductSlots*4,4,indirect);
                bodyDispatch=owner.arena.Allocate(arenaOwner+"/bodies",ProductSlots*4,4,indirect);
                Gates.SetData(new uint[ProductSlots]);
                }
                catch { Dispose();throw; }
            }

            // Record after RecordGraph. The caller's GPU control writes Gates
            // before this sequence; no CPU readback chooses an iteration.
            public void Record(CommandBuffer target)
            {
                RecordPreparation(target);
                for(int slot=0;slot<ProductSlots;slot++)RecordSlot(target,slot);
            }

            internal void RecordPreparation(CommandBuffer target)
            {
                if(disposed||owner.disposed)throw new ObjectDisposedException(
                    nameof(MaskedProductSchedule));
                if(target==null)throw new ArgumentNullException(nameof(target));
                owner.CheckIdentityGeneration();
                var shader=owner.shader;
                int kernel=owner.prepareMasked;
                target.SetComputeIntParam(shader,"_ProductSlotCount",ProductSlots);
                target.SetComputeBufferParam(shader,kernel,"_ProductGates",Gates);
                target.SetComputeBufferParam(shader,kernel,"_RowArgs",owner.rowArgs);
                target.SetComputeBufferParam(shader,kernel,"_SegmentArgs",owner.segmentArgs);
                target.SetComputeBufferParam(shader,kernel,"_MaskedRowArgs",rowDispatch);
                target.SetComputeBufferParam(shader,kernel,"_MaskedSegmentArgs",segmentDispatch);
                target.SetComputeBufferParam(shader,kernel,"_MaskedBodyArgs",bodyDispatch);
                target.DispatchCompute(shader,kernel,(ProductSlots+63)/64,1,1);
            }

            internal void RecordSlot(CommandBuffer target,int slot)
            {
                if(disposed||owner.disposed)throw new ObjectDisposedException(
                    nameof(MaskedProductSchedule));
                if(target==null)throw new ArgumentNullException(nameof(target));
                if(slot<0||slot>=ProductSlots)
                    throw new ArgumentOutOfRangeException(nameof(slot));
                uint offset=checked((uint)(slot*16));
                var shader=owner.shader;
                target.DispatchCompute(shader,owner.expand,rowDispatch,offset);
                target.DispatchCompute(shader,owner.reduce,segmentDispatch,offset);
                target.DispatchCompute(shader,owner.applyBodies,bodyDispatch,offset);
                target.DispatchCompute(shader,owner.applyRows,rowDispatch,offset);
            }

            public void Dispose()
            {
                if(disposed)return;disposed=true;
                if(Gates!=null)owner.arena.Release(Gates);
                if(rowDispatch!=null)owner.arena.Release(rowDispatch);
                if(segmentDispatch!=null)owner.arena.Release(segmentDispatch);
                if(bodyDispatch!=null)owner.arena.Release(bodyDispatch);
            }
        }

        public MaskedProductSchedule CreateMaskedProductSchedule(SolveProfile profile,
            int substeps=16)
        {
            if(disposed)throw new ObjectDisposedException(nameof(CoupledContactOperator));
            return new MaskedProductSchedule(this,profile,substeps);
        }

        // Immutable profile command-count probe. Geometry, scalar reductions,
        // line search and publication are masked dispatch placeholders here;
        // V2-2 supplies their arithmetic without changing the bounded slots.
        public sealed class MaskedProfileSchedule : IDisposable
        {
            const int GraphDispatchSlots=51;
            readonly CoupledContactOperator owner;
            readonly string arenaOwner;
            readonly MaskedProductSchedule products;
            readonly GraphicsBuffer stageArgs;
            readonly int velocityUpdates,krylovProducts,geometryUpdates;
            bool disposed;
            public GraphicsBuffer StageGates { get; }
            public GraphicsBuffer ProductGates=>products.Gates;
            public int StageSlots { get; }
            public int ProductSlots=>products.ProductSlots;
            public int Substeps=>products.Substeps;
            public long BufferBytes=>products.BufferBytes+(long)StageSlots*20;
            public int RecordedDispatches=>checked(ProductSlots*4+StageSlots+2);

            internal MaskedProfileSchedule(CoupledContactOperator owner,
                SolveProfile profile,int substeps)
            {
                this.owner=owner;
                try
                {
                if(substeps<4||substeps>16)
                    throw new ArgumentOutOfRangeException(nameof(substeps));
                ProfileCaps(profile,out int velocity,out int krylov,
                    out int geometry);
                velocityUpdates=velocity;krylovProducts=krylov;
                geometryUpdates=geometry;
                int newton=checked(14+2*krylovProducts);
                StageSlots=checked(substeps*(106+52*geometryUpdates+
                    (velocityUpdates+4*geometryUpdates)*newton)+2);
                long productBytes=(long)substeps*(velocityUpdates+geometryUpdates*4)*
                    (krylovProducts+3)*52;
                owner.arena.EnsureCapacity(productBytes+(long)StageSlots*20);
                products=new MaskedProductSchedule(owner,profile,substeps);
                arenaOwner=owner.arena.NewOwner("ProfileSchedule");
                StageGates=owner.arena.Allocate(arenaOwner+"/gates",StageSlots,4);
                stageArgs=owner.arena.Allocate(arenaOwner+"/args",StageSlots*4,4,
                    GraphicsBuffer.Target.Structured|
                    GraphicsBuffer.Target.IndirectArguments);
                StageGates.SetData(new uint[StageSlots]);
                }
                catch { Dispose();throw; }
            }

            // Record once after the frozen graph. The same CommandBuffer can be
            // submitted repeatedly with GPU-updated gate words.
            public void Record(CommandBuffer target)
            {
                if(disposed||owner.disposed)throw new ObjectDisposedException(
                    nameof(MaskedProfileSchedule));
                if(target==null)throw new ArgumentNullException(nameof(target));
                owner.CheckIdentityGeneration();
                products.RecordPreparation(target);
                var shader=owner.shader;
                target.SetComputeIntParam(shader,"_StageCount",StageSlots);
                target.SetComputeBufferParam(shader,owner.prepareStages,
                    "_StageGates",StageGates);
                target.SetComputeBufferParam(shader,owner.prepareStages,
                    "_StageArgs",stageArgs);
                target.DispatchCompute(shader,owner.prepareStages,
                    (StageSlots+63)/64,1,1);
                int stage=0,product=0;
                void Stage()
                {
                    uint offset=checked((uint)(stage++*16));
                    target.DispatchCompute(shader,owner.stageNoOp,stageArgs,offset);
                }
                void Graph()
                {
                    for(int i=0;i<GraphDispatchSlots;i++)Stage();
                }
                void Newton()
                {
                    Stage(); // branch and base residual control
                    for(int i=0;i<krylovProducts+3;i++)
                    {
                        products.RecordSlot(target,product++);
                        if(i<krylovProducts)
                        {Stage();Stage();} // batched scalar reduction, vector update
                        else Stage(); // separate physical residual product handling
                    }
                    Stage(); // true linear residual / projection decision
                    for(int i=0;i<8;i++)Stage(); // line-search trials
                    Stage(); // accepted direction or fault
                }
                for(int substep=0;substep<Substeps;substep++)
                {
                    Stage();Stage(); // force integration and speed bound
                    Graph(); // candidate/manifold/row/incidence construction
                    for(int i=0;i<velocityUpdates;i++)Newton();
                    Stage(); // pose prediction
                    for(int geometry=0;geometry<geometryUpdates;geometry++)
                    {
                        Graph(); // fresh position geometry
                        for(int i=0;i<4;i++)Newton();
                        Stage(); // geometry acceptance
                    }
                    Graph(); // independent final-pose query
                    Stage(); // provisional substep result
                }
                Stage();Stage(); // conservation/fault reduction, publication
                if(stage!=StageSlots||product!=ProductSlots)
                    throw new InvalidOperationException("V2 profile schedule count mismatch");
            }

            public void Dispose()
            {
                if(disposed)return;disposed=true;
                products?.Dispose();
                if(StageGates!=null)owner.arena.Release(StageGates);
                if(stageArgs!=null)owner.arena.Release(stageArgs);
            }
        }

        public MaskedProfileSchedule CreateMaskedProfileSchedule(SolveProfile profile,
            int substeps=16)
        {
            if(disposed)throw new ObjectDisposedException(nameof(CoupledContactOperator));
            return new MaskedProfileSchedule(this,profile,substeps);
        }

        // Synchronous readback is intentional here: this is a diagnostic
        // product, not the eventual frame submission or completion path.
        public Result Apply(Vector4[] freeMotion,float[] scaledImpulse)
        {
            if(disposed)throw new ObjectDisposedException(nameof(CoupledContactOperator));
            ActualRowCount=0;
            if(freeMotion==null||scaledImpulse==null||freeMotion.Length!=bodyCount||scaledImpulse.Length!=rowCount)
                throw new ArgumentException("Input length does not match the frozen graph");
            foreach(var value in freeMotion)
                if(!Finite(value.x)||!Finite(value.y)||!Finite(value.z)||!Finite(value.w))throw new ArgumentException("Nonfinite free motion");
            foreach(float value in scaledImpulse)if(!Finite(value))throw new ArgumentException("Nonfinite scaled impulse");
            freeBuffer.SetData(freeMotion);if(rowCount>0)xBuffer.SetData(scaledImpulse);
            commands.Clear();
            RecordGraph(commands);
            RecordProduct(commands);
            Graphics.ExecuteCommandBuffer(commands);
            var args=new uint[4];segmentArgs.GetData(args);
            if(!gpuPairs&&!gpuGeneratedRows&&args[3]!=(uint)SegmentCount)
                throw new InvalidOperationException("GPU incidence segment count mismatch");
            SegmentCount=(int)args[3];
            int actual=rowCount;
            var result=new Result();
            if(gpuGeneratedRows)
            {
                var countValue=new uint[1];var broadFault=new uint[1];
                var manifoldFault=new uint[1];var rowFault=new uint[1];
                actualRowCountBuffer.GetData(countValue);
                generatedRows.BroadFaultBuffer.GetData(broadFault);
                generatedRows.ManifoldFaultBuffer.GetData(manifoldFault);
                generatedRows.RowFaultBuffer.GetData(rowFault);
                result.BroadPhaseFault=broadFault[0];
                result.ManifoldFault=manifoldFault[0];result.RowFault=rowFault[0];
                if(generatedRows.ContainmentEnabled)
                {
                    var first=new uint[1];
                    generatedRows.FirstContainedBuffer.GetData(first);
                    result.FirstContainedPrimitive=first[0];
                }
                if(countValue[0]>(uint)rowCount)result.RowFault|=2;
                if(!result.Success)
                {
                    result.EndpointMotion=Array.Empty<Vector4>();
                    result.RowVelocity=Array.Empty<float>();
                    result.Rows=Array.Empty<CoupledContactRow>();
                    result.Features=Array.Empty<CoupledContactFeature>();
                    return result;
                }
                actual=(int)countValue[0];
            }
            ActualRowCount=actual;
            result.EndpointMotion=new Vector4[bodyCount];
            result.RowVelocity=new float[actual];
            result.Rows=new CoupledContactRow[actual];
            if(gpuGeneratedRows)result.Features=new CoupledContactFeature[actual];
            motionBuffer.GetData(result.EndpointMotion);
            if(actual>0)yBuffer.GetData(result.RowVelocity,0,0,actual);
            if(actual>0)rowBuffer.GetData(result.Rows,0,0,actual);
            if(gpuGeneratedRows&&actual>0)
                generatedRows.FeaturesBuffer.GetData(result.Features,0,0,actual);
            foreach(var row in result.Rows)if(row.InverseRoot>0)result.ActiveRows++;
            return result;
        }

        public void Dispose()
        {
            if(disposed)return;disposed=true;
            commands.Release();foreach(var buffer in owned)arena.Release(buffer);
            if(generatedRows!=null)generatedRows.Dispose();
            if(shader!=null)
            {if(Application.isPlaying)UnityEngine.Object.Destroy(shader);else UnityEngine.Object.DestroyImmediate(shader);}
            if(manifoldShader!=null)
            {if(Application.isPlaying)UnityEngine.Object.Destroy(manifoldShader);else UnityEngine.Object.DestroyImmediate(manifoldShader);}
        }
    }
}
