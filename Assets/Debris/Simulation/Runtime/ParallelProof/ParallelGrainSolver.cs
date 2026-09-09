using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
namespace Debris.Simulation.ParallelProof
{
    // Bounded proof helper, owned by the verification session. No gameplay cutover until R1 passes.
    public sealed class ParallelGrainSolver : IDisposable
    {
        readonly ComputeShader shader;
        readonly CommandBuffer commands = new CommandBuffer { name="B3R parallel grain proof" };
        readonly List<GraphicsBuffer> buffers = new List<GraphicsBuffer>();
        readonly Dictionary<string,int> kernels = new Dictionary<string,int>();
        readonly GraphicsBuffer committed,state,grains,parameters,boundaries,diagnostics,args,starts;
        readonly GraphicsBuffer counts,cursors,offsets,sums,blockOffsets,indices;
        readonly GraphicsBuffer rows,rowCounts,rowOffsets,rowSums,rowBlocks,contacts,degrees,adjOffsets,adjSums,adjBlocks,adjCursors,adjacency,increments;
        readonly int n,bodies,endpoints,slots,velocityIterations,positionIterations;
        public long BufferBytes { get; private set; }
        public GraphicsBuffer Grains => grains;
        public GraphicsBuffer Bodies => committed;
        public GraphicsBuffer Boundaries => boundaries;
        public Task<uint[]> DiagnosticsAsync()=>Read<uint>(diagnostics);
        public ParallelGrainSolver(LooseCell[] initialGrains, BodyState[] initialBodies, BodyParameters[] bodyParameters, Boundary[] patches,
            int velocityIterations=8,int positionIterations=4,float friction=.3f,int candidateSlots=64)
        {
            if(initialGrains==null||initialGrains.Length==0||initialGrains.Length>10000||initialBodies.Length>17||initialBodies.Length!=bodyParameters.Length)
                throw new ArgumentException("Invalid proof population");
            if(!((velocityIterations==4&&positionIterations==2)||(velocityIterations==8&&positionIterations==4)||(velocityIterations==12&&positionIterations==6)))
                throw new ArgumentException("Only locked profiles 4/2, 8/4, 12/6 are supported");
            if(candidateSlots<1||candidateSlots>64)throw new ArgumentOutOfRangeException(nameof(candidateSlots));
            n=initialGrains.Length;bodies=initialBodies.Length;endpoints=n+bodies;slots=candidateSlots;
            this.velocityIterations=velocityIterations;this.positionIterations=positionIterations;
            shader=UnityEngine.Object.Instantiate(Resources.Load<ComputeShader>("ParallelGrains"));
            committed=Buffer(endpoints,32);state=Buffer(endpoints,32);grains=Buffer(n,48);parameters=Buffer(endpoints,32);boundaries=Buffer(Math.Max(1,patches.Length),32);
            diagnostics=Buffer(16,4);starts=Buffer(endpoints,8);
            args=Buffer(16*4*3,4,GraphicsBuffer.Target.Structured|GraphicsBuffer.Target.IndirectArguments);
            counts=Buffer(65536,4);cursors=Buffer(65536,4);offsets=Buffer(65536,4);sums=Buffer(256,4);blockOffsets=Buffer(256,4);indices=Buffer(n,4);
            rows=Buffer(n*slots,64);rowCounts=Buffer(n,4);rowOffsets=Buffer(n,4);rowSums=Buffer(256,4);rowBlocks=Buffer(256,4);
            contacts=Buffer(n*slots,64);degrees=Buffer(endpoints,4);adjOffsets=Buffer(endpoints,4);adjSums=Buffer(256,4);adjBlocks=Buffer(256,4);adjCursors=Buffer(endpoints,4);
            adjacency=Buffer(n*slots*2,4);increments=Buffer(n*slots,16);
            var initial=new BodyState[endpoints];var physical=new BodyParameters[endpoints];var identities=new HashSet<uint>();
            for(int i=0;i<n;i++)
            {
                var g=initialGrains[i];if(g.Material!=1||g.Identity==0||!identities.Add(g.Identity))throw new ArgumentException("Proof material is unit density; identities must be unique");
                initial[i]=new BodyState{Center=g.Center,Velocity=g.Velocity,Angle=g.Angle,AngularVelocity=g.AngularVelocity};
                physical[i]=new BodyParameters{InverseMass=1,InverseInertia=6,Mobility=1};
            }
            Array.Copy(initialBodies,0,initial,n,bodies);Array.Copy(bodyParameters,0,physical,n,bodies);
            foreach(var patch in patches)if(patch.Body<n||patch.Body>=endpoints)throw new ArgumentException("Boundary body is an endpoint index");
            committed.SetData(initial);state.SetData(initial);grains.SetData(initialGrains);parameters.SetData(physical);if(patches.Length>0)boundaries.SetData(patches);diagnostics.SetData(new uint[16]);
            shader.SetInt("_N",n);shader.SetInt("_Bodies",bodies);shader.SetInt("_Endpoints",endpoints);shader.SetInt("_BoundaryCount",patches.Length);
            shader.SetInt("_BinSide",256);shader.SetInt("_BinCount",65536);shader.SetInt("_Slots",slots);shader.SetFloat("_Dt",1f/60);shader.SetFloat("_Friction",friction);
            Bind("Begin",("_Committed",committed),("_State",state),("_D",diagnostics));
            Bind("Speed",("_State",state),("_Parameters",parameters),("_Boundaries",boundaries),("_D",diagnostics));
            Bind("SelectSubsteps",("_D",diagnostics),("_Args",args));
            Bind("Prepare",("_D",diagnostics),("_State",state),("_Starts",starts),("_Parameters",parameters));
            Bind("ClearBins",("_Counts",counts),("_Cursors",cursors));
            Bind("CountBins",("_State",state),("_D",diagnostics),("_Counts",counts));
            Bind("ScanBlocks");Bind("ScanSums");
            Bind("ScatterBins",("_State",state),("_D",diagnostics),("_Cursors",cursors),("_Offsets",offsets),("_BlockOffsets",blockOffsets),("_Indices",indices));
            Bind("Gather",("_State",state),("_Boundaries",boundaries),("_D",diagnostics),("_Grains",grains),("_Counts",counts),("_Offsets",offsets),("_BlockOffsets",blockOffsets),("_Indices",indices),("_Rows",rows),("_RowCounts",rowCounts));
            Bind("ClearDegrees",("_Degrees",degrees),("_AdjCursors",adjCursors));
            Bind("Compact",("_D",diagnostics),("_Rows",rows),("_RowCounts",rowCounts),("_RowOffsets",rowOffsets),("_RowBlockOffsets",rowBlocks),("_Contacts",contacts),("_Degrees",degrees),("_Args",args));
            Bind("ScatterAdjacency",("_D",diagnostics),("_Contacts",contacts),("_AdjOffsets",adjOffsets),("_AdjBlockOffsets",adjBlocks),("_AdjCursors",adjCursors),("_Adjacency",adjacency));
            Bind("EvaluateVelocity",("_D",diagnostics),("_State",state),("_Parameters",parameters),("_Contacts",contacts),("_Degrees",degrees),("_Increments",increments));
            foreach(string kernel in new[]{"ApplyGrains","ApplyBodies"})Bind(kernel,("_D",diagnostics),("_State",state),("_Parameters",parameters),("_Contacts",contacts),("_Degrees",degrees),("_AdjOffsets",adjOffsets),("_AdjBlockOffsets",adjBlocks),("_Adjacency",adjacency),("_Increments",increments));
            Bind("Predict",("_D",diagnostics),("_State",state));
            Bind("EvaluatePosition",("_D",diagnostics),("_State",state),("_Boundaries",boundaries),("_Parameters",parameters),("_Contacts",contacts),("_Degrees",degrees),("_Increments",increments));
            Bind("PrepareValidation",("_D",diagnostics));
            Bind("Validate",("_D",diagnostics),("_State",state),("_Starts",starts),("_Boundaries",boundaries),("_Contacts",contacts));
            Bind("Commit",("_D",diagnostics),("_State",state),("_CommitOutput",committed),("_Grains",grains));
            Bind("Acknowledge",("_D",diagnostics));
        }
        GraphicsBuffer Buffer(int count,int stride,GraphicsBuffer.Target target=GraphicsBuffer.Target.Structured)
        {var b=new GraphicsBuffer(target,count,stride);buffers.Add(b);BufferBytes+=(long)count*stride;return b;}
        void Bind(string kernel,params (string,GraphicsBuffer)[] bindings)
        {int k=shader.FindKernel(kernel);kernels.Add(kernel,k);foreach(var pair in bindings)shader.SetBuffer(k,pair.Item1,pair.Item2);}
        void Direct(string kernel,int count,int threads=64)=>commands.DispatchCompute(shader,kernels[kernel],(count+threads-1)/threads,1,1);
        void Indirect(string kernel,int substep,int kind)=>commands.DispatchCompute(shader,kernels[kernel],args,(uint)((substep*4+kind)*3*4));
        void Scan(GraphicsBuffer input,GraphicsBuffer output,GraphicsBuffer blockSums,GraphicsBuffer blockStarts,int count)
        {
            int k=kernels["ScanBlocks"],blocks=(count+255)/256;
            commands.SetComputeIntParam(shader,"_ScanCount",count);commands.SetComputeBufferParam(shader,k,"_ScanInput",input);commands.SetComputeBufferParam(shader,k,"_ScanOutput",output);commands.SetComputeBufferParam(shader,k,"_Sums",blockSums);Direct("ScanBlocks",count,256);
            k=kernels["ScanSums"];commands.SetComputeIntParam(shader,"_SumCount",blocks);commands.SetComputeBufferParam(shader,k,"_SumInput",blockSums);commands.SetComputeBufferParam(shader,k,"_SumOutput",blockStarts);Direct("ScanSums",256,256);
        }
        public void Step(Vector2 localForce=default,float torque=0)
        {
            commands.Clear();commands.BeginSample("B3R.ParallelPhysics");commands.SetComputeVectorParam(shader,"_Force",new Vector4(localForce.x,localForce.y,torque,0));
            Direct("Begin",endpoints);Direct("Speed",endpoints);Direct("SelectSubsteps",1,1);
            for(int s=0;s<16;s++)
            {
                commands.SetComputeIntParam(shader,"_Substep",s);Indirect("Prepare",s,0);
                commands.BeginSample("B3R.Bins");Direct("ClearBins",65536,256);Indirect("CountBins",s,1);Scan(counts,offsets,sums,blockOffsets,65536);Indirect("ScatterBins",s,1);commands.EndSample("B3R.Bins");
                commands.BeginSample("B3R.Gather");Indirect("Gather",s,1);Scan(rowCounts,rowOffsets,rowSums,rowBlocks,n);Direct("ClearDegrees",endpoints);Indirect("Compact",s,1);Scan(degrees,adjOffsets,adjSums,adjBlocks,endpoints);Indirect("ScatterAdjacency",s,2);commands.EndSample("B3R.Gather");
                commands.BeginSample("B3R.Velocity");commands.SetComputeIntParam(shader,"_Position",0);
                for(int i=0;i<velocityIterations;i++){Indirect("EvaluateVelocity",s,2);Indirect("ApplyGrains",s,1);if(bodies>0)Indirect("ApplyBodies",s,3);}
                commands.EndSample("B3R.Velocity");Indirect("Predict",s,0);
                commands.BeginSample("B3R.Position");commands.SetComputeIntParam(shader,"_Position",1);
                for(int i=0;i<positionIterations;i++){Indirect("EvaluatePosition",s,2);Indirect("ApplyGrains",s,1);if(bodies>0)Indirect("ApplyBodies",s,3);}
                commands.EndSample("B3R.Position");Direct("PrepareValidation",1,1);Direct("Validate",Math.Max(endpoints,n*slots));
            }
            Direct("Commit",endpoints);Direct("Acknowledge",1,1);commands.EndSample("B3R.ParallelPhysics");Graphics.ExecuteCommandBuffer(commands);
        }
        public async Task<ProofSnapshot> SnapshotAsync()
        {
            var g=Read<LooseCell>(grains);var s=Read<BodyState>(committed);var d=Read<uint>(diagnostics);
            await Task.WhenAll(g,s,d);return new ProofSnapshot{Grains=g.Result,Endpoints=s.Result,Diagnostics=d.Result};
        }
        static Task<T[]> Read<T>(GraphicsBuffer b) where T:struct
        {
            var source=new TaskCompletionSource<T[]>();AsyncGPUReadback.Request(b,r=>{if(r.hasError)source.SetException(new InvalidOperationException("Proof GPU readback failed"));else source.SetResult(r.GetData<T>().ToArray());});return source.Task;
        }
        public void Dispose(){commands.Dispose();foreach(var b in buffers)b.Dispose();if(Application.isPlaying)UnityEngine.Object.Destroy(shader);else UnityEngine.Object.DestroyImmediate(shader);}
    }
}
