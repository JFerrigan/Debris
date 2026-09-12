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
        readonly ProofMetrics metrics;
        readonly CommandBuffer commands = new CommandBuffer { name="B3R parallel grain proof" };
        readonly List<GraphicsBuffer> buffers = new List<GraphicsBuffer>();
        readonly Dictionary<string,int> kernels = new Dictionary<string,int>();
        readonly GraphicsBuffer rigidContacts,rigidCount,rigidPairs;
        readonly GraphicsBuffer committed,state,grains,parameters,boundaries,diagnostics,args,starts;
        readonly GraphicsBuffer counts,cursors,offsets,sums,blockOffsets,indices;
        readonly GraphicsBuffer rows,rowCounts,rowOffsets,rowSums,rowBlocks,contacts,degrees,adjOffsets,adjSums,adjBlocks,adjCursors,adjacency,increments;
        readonly int n,bodies,endpoints,slots,velocityIterations,positionIterations;
        public long BufferBytes { get; private set; }
        public GraphicsBuffer Grains => grains;
        public GraphicsBuffer Bodies => committed;
        public GraphicsBuffer Boundaries => boundaries;
        public GraphicsBuffer Parameters => parameters;
        public Task<uint[]> DiagnosticsAsync()=>Read<uint>(diagnostics);
        public Task<ProofMetricsReadback> MetricsAsync()=>metrics.ReadAsync();
        public ParallelGrainSolver(LooseCell[] initialGrains, BodyState[] initialBodies, BodyParameters[] bodyParameters, Boundary[] patches,
            int velocityIterations=8,int positionIterations=4,float friction=.3f,int candidateSlots=64,int rigidContactCapacity=4096)
        {
            ValidateInput(initialGrains,initialBodies,bodyParameters,patches,velocityIterations,positionIterations,friction,candidateSlots);
            if(rigidContactCapacity<1||rigidContactCapacity>4096||patches.Length>4096)throw new ArgumentOutOfRangeException();
            n=initialGrains.Length;bodies=initialBodies.Length;endpoints=n+bodies;slots=candidateSlots;
            this.velocityIterations=velocityIterations;this.positionIterations=positionIterations;
            shader=UnityEngine.Object.Instantiate(Resources.Load<ComputeShader>("ParallelGrains"));
            committed=Buffer(endpoints,32);state=Buffer(endpoints,32);grains=Buffer(n,48);parameters=Buffer(endpoints,32);boundaries=Buffer(Math.Max(1,patches.Length),32);
            diagnostics=Buffer(16,4);starts=Buffer(endpoints,16);
            rigidContacts=Buffer(rigidContactCapacity,72);rigidCount=Buffer(1,4);rigidPairs=Buffer(17*17,4);
            args=Buffer(16*10*3,4,GraphicsBuffer.Target.Structured|GraphicsBuffer.Target.IndirectArguments);
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
            shader.SetInt("_RigidCapacity",rigidContactCapacity);shader.SetFloat("_RigidGatherMargin",.75f);shader.SetFloat("_RigidSolidTarget",.0001f);
            shader.SetInt("_N",n);shader.SetInt("_Bodies",bodies);shader.SetInt("_Endpoints",endpoints);shader.SetInt("_BoundaryCount",patches.Length);
            shader.SetInt("_BinSide",256);shader.SetInt("_BinCount",65536);shader.SetInt("_Slots",slots);shader.SetFloat("_Dt",1f/60);shader.SetFloat("_Friction",friction);
            Bind("Begin",("_Committed",committed),("_State",state),("_D",diagnostics));
            Bind("Speed",("_State",state),("_Parameters",parameters),("_Boundaries",boundaries),("_D",diagnostics));
            Bind("SelectSubsteps",("_D",diagnostics),("_Args",args));
            Bind("Prepare",("_D",diagnostics),("_State",state),("_Starts",starts),("_Parameters",parameters),("_Boundaries",boundaries));
            Bind("ClearBins",("_Counts",counts),("_Cursors",cursors));
            Bind("CountBins",("_State",state),("_D",diagnostics),("_Counts",counts));
            Bind("ScanBlocks");Bind("ScanSums");
            Bind("ScatterBins",("_State",state),("_D",diagnostics),("_Cursors",cursors),("_Offsets",offsets),("_BlockOffsets",blockOffsets),("_Indices",indices));
            Bind("Gather",("_State",state),("_Boundaries",boundaries),("_Parameters",parameters),("_D",diagnostics),("_Grains",grains),("_Counts",counts),("_Offsets",offsets),("_BlockOffsets",blockOffsets),("_Indices",indices),("_Rows",rows),("_RowCounts",rowCounts));
            Bind("ClearDegrees",("_Degrees",degrees),("_AdjCursors",adjCursors));
            Bind("Compact",("_D",diagnostics),("_Rows",rows),("_RowCounts",rowCounts),("_RowOffsets",rowOffsets),("_RowBlockOffsets",rowBlocks),("_Contacts",contacts),("_Degrees",degrees),("_Args",args));
            Bind("ValidateDegrees",("_D",diagnostics),("_Degrees",degrees));
            Bind("ScatterAdjacency",("_D",diagnostics),("_Contacts",contacts),("_AdjOffsets",adjOffsets),("_AdjBlockOffsets",adjBlocks),("_AdjCursors",adjCursors),("_Adjacency",adjacency));
            Bind("EvaluateVelocity",("_D",diagnostics),("_State",state),("_Parameters",parameters),("_Contacts",contacts),("_Degrees",degrees),("_Increments",increments));
            foreach(string kernel in new[]{"ApplyGrains","ApplyBodies"})Bind(kernel,("_D",diagnostics),("_State",state),("_Parameters",parameters),("_Contacts",contacts),("_Degrees",degrees),("_AdjOffsets",adjOffsets),("_AdjBlockOffsets",adjBlocks),("_Adjacency",adjacency),("_Increments",increments));
            Bind("Predict",("_D",diagnostics),("_State",state));
            Bind("EvaluatePosition",("_D",diagnostics),("_State",state),("_Boundaries",boundaries),("_Parameters",parameters),("_Contacts",contacts),("_Degrees",degrees),("_Increments",increments));
            Bind("PrepareValidation",("_D",diagnostics));
            Bind("Validate",("_D",diagnostics),("_State",state),("_Starts",starts),("_Boundaries",boundaries),("_Parameters",parameters),("_Contacts",contacts));
            Bind("Commit",("_D",diagnostics),("_State",state),("_CommitOutput",committed),("_Grains",grains));
            Bind("Acknowledge",("_D",diagnostics));
            Bind("RigidClearContacts",("_D",diagnostics),("_RigidPairCounts",rigidPairs),("_RigidContactCount",rigidCount));
            Bind("RigidGatherContacts",("_D",diagnostics),("_State",state),("_Parameters",parameters),("_Boundaries",boundaries),("_RigidContacts",rigidContacts),("_RigidPairCounts",rigidPairs),("_RigidContactCount",rigidCount));
            Bind("RigidCheckCapacity",("_D",diagnostics),("_RigidContactCount",rigidCount));
            Bind("RigidSolveVelocity",("_D",diagnostics),("_State",state),("_Parameters",parameters),("_RigidContacts",rigidContacts),("_RigidContactCount",rigidCount));
            foreach(string kernel in new[]{"RigidSolvePosition","RigidValidate"})Bind(kernel,("_D",diagnostics),("_State",state),("_Parameters",parameters),("_Boundaries",boundaries),("_RigidContacts",rigidContacts),("_RigidContactCount",rigidCount));
            metrics=new ProofMetrics(n,bodies);BufferBytes+=metrics.AllocatedBytes;
            metrics.Record(commands,committed,parameters,grains);Graphics.ExecuteCommandBuffer(commands);commands.Clear();
        }
        static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
        static bool Finite(Vector2 value)=>Finite(value.x)&&Finite(value.y);
        static void ValidateInput(LooseCell[] grains,BodyState[] bodies,BodyParameters[] definitions,Boundary[] patches,int velocity,int position,float friction,int slots)
        {
            if(grains==null||bodies==null||definitions==null||patches==null||grains.Length==0||grains.Length>10000||bodies.Length>17||bodies.Length!=definitions.Length)
                throw new ArgumentException("Invalid proof population");
            if(!((velocity==4&&position==2)||(velocity==8&&position==4)||(velocity==12&&position==6)))throw new ArgumentException("Only locked profiles 4/2, 8/4, 12/6 are supported");
            if(slots<1||slots>64||!Finite(friction)||friction<0)throw new ArgumentOutOfRangeException();
            var ids=new HashSet<uint>();
            foreach(var grain in grains)
                if(grain.Material!=1||grain.Identity==0||!ids.Add(grain.Identity)||!Finite(grain.Center)||!Finite(grain.Velocity)||!Finite(grain.Angle)||!Finite(grain.AngularVelocity))
                    throw new ArgumentException("Invalid finite unit-density grain or duplicate identity");
            var owned=new bool[patches.Length];
            for(int i=0;i<bodies.Length;i++)
            {
                var state=bodies[i];var body=definitions[i];
                if(!Finite(state.Center)||!Finite(state.Velocity)||!Finite(state.Angle)||!Finite(state.AngularVelocity)||!Finite(body.LocalCOM)||!Finite(body.InverseMass)||!Finite(body.InverseInertia)||body.InverseMass<0||body.InverseInertia<0||body.Mobility>1)
                    throw new ArgumentException("Invalid body state or mass");
                if(body.Mobility==0&&(body.InverseMass!=0||body.InverseInertia!=0||state.Velocity!=Vector2.zero||state.AngularVelocity!=0))throw new ArgumentException("Anchored bodies must have zero inverse mass and motion");
                if(body.Mobility==1&&(body.InverseMass<=0||body.InverseInertia<=0))throw new ArgumentException("Dynamic bodies require finite positive mass and inertia");
                if((ulong)body.BoundaryStart+body.BoundaryCount>(ulong)patches.Length)throw new ArgumentException("Boundary range outside cache");
                for(uint f=body.BoundaryStart;f<body.BoundaryStart+body.BoundaryCount;f++)
                {
                    var patch=patches[f];
                    if(owned[f]||patch.Body!=grains.Length+i||!Finite(patch.Center)||!Finite(patch.HalfSize)||patch.HalfSize.x<=0||patch.HalfSize.y<=0)throw new ArgumentException("Invalid or multiply owned boundary patch");
                    owned[f]=true;
                }
            }
            foreach(bool assigned in owned)if(!assigned)throw new ArgumentException("Unowned boundary patch");
        }
        GraphicsBuffer Buffer(int count,int stride,GraphicsBuffer.Target target=GraphicsBuffer.Target.Structured)
        {var b=new GraphicsBuffer(target,count,stride);buffers.Add(b);BufferBytes+=(long)count*stride;return b;}
        void Bind(string kernel,params (string,GraphicsBuffer)[] bindings)
        {int k=shader.FindKernel(kernel);kernels.Add(kernel,k);foreach(var pair in bindings)shader.SetBuffer(k,pair.Item1,pair.Item2);}
        void Direct(string kernel,int count,int threads=64)=>commands.DispatchCompute(shader,kernels[kernel],(count+threads-1)/threads,1,1);
        void Indirect(string kernel,int substep,int kind)=>commands.DispatchCompute(shader,kernels[kernel],args,(uint)((substep*10+kind)*3*4));
        void Scan(GraphicsBuffer input,GraphicsBuffer output,GraphicsBuffer blockSums,GraphicsBuffer blockStarts,int count,int substep,int kind)
        {
            int k=kernels["ScanBlocks"],blocks=(count+255)/256;
            commands.SetComputeIntParam(shader,"_ScanCount",count);commands.SetComputeBufferParam(shader,k,"_ScanInput",input);commands.SetComputeBufferParam(shader,k,"_ScanOutput",output);commands.SetComputeBufferParam(shader,k,"_Sums",blockSums);Indirect("ScanBlocks",substep,kind);
            k=kernels["ScanSums"];commands.SetComputeIntParam(shader,"_SumCount",blocks);commands.SetComputeBufferParam(shader,k,"_SumInput",blockSums);commands.SetComputeBufferParam(shader,k,"_SumOutput",blockStarts);Indirect("ScanSums",substep,7);
        }
        public void Step(Vector2 localForce=default,float torque=0)
        {
            if(!Finite(localForce)||!Finite(torque))throw new ArgumentException("Flight force and torque must be finite");
            commands.Clear();commands.BeginSample("B3R.ParallelPhysics");commands.SetComputeVectorParam(shader,"_Force",new Vector4(localForce.x,localForce.y,torque,0));
            Direct("Begin",endpoints);Direct("Speed",endpoints);Direct("SelectSubsteps",1,1);
            for(int s=0;s<16;s++)
            {
                commands.SetComputeIntParam(shader,"_Substep",s);Indirect("Prepare",s,0);
                commands.BeginSample("B3R.Bins");Indirect("ClearBins",s,4);Indirect("CountBins",s,1);Scan(counts,offsets,sums,blockOffsets,65536,s,4);Indirect("ScatterBins",s,1);commands.EndSample("B3R.Bins");
                commands.BeginSample("B3R.Gather");Indirect("Gather",s,1);Scan(rowCounts,rowOffsets,rowSums,rowBlocks,n,s,5);Indirect("ClearDegrees",s,0);Indirect("Compact",s,1);Indirect("ValidateDegrees",s,0);Scan(degrees,adjOffsets,adjSums,adjBlocks,endpoints,s,6);Indirect("ScatterAdjacency",s,2);if(bodies>1){Indirect("RigidClearContacts",s,7);Indirect("RigidGatherContacts",s,9);Indirect("RigidCheckCapacity",s,7);}commands.EndSample("B3R.Gather");
                commands.BeginSample("B3R.Velocity");commands.SetComputeIntParam(shader,"_Position",0);
                for(int i=0;i<velocityIterations;i++){Indirect("EvaluateVelocity",s,2);Indirect("ApplyGrains",s,1);if(bodies>0)Indirect("ApplyBodies",s,3);if(bodies>1){Indirect("RigidSolveVelocity",s,7);Indirect("RigidSolveVelocity",s,7);}}
                commands.EndSample("B3R.Velocity");Indirect("Predict",s,0);
                commands.BeginSample("B3R.Position");commands.SetComputeIntParam(shader,"_Position",1);
                for(int i=0;i<positionIterations;i++){Indirect("EvaluatePosition",s,2);Indirect("ApplyGrains",s,1);if(bodies>0)Indirect("ApplyBodies",s,3);if(bodies>1){Indirect("RigidSolvePosition",s,7);Indirect("RigidSolvePosition",s,7);}}
                commands.EndSample("B3R.Position");Indirect("PrepareValidation",s,7);Indirect("Validate",s,8);if(bodies>1)Indirect("RigidValidate",s,7);
            }
            Direct("Commit",endpoints);Direct("Acknowledge",1,1);metrics.Record(commands,committed,parameters,grains);commands.EndSample("B3R.ParallelPhysics");Graphics.ExecuteCommandBuffer(commands);
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
        public void Dispose(){metrics.Dispose();commands.Dispose();foreach(var b in buffers)b.Dispose();if(Application.isPlaying)UnityEngine.Object.Destroy(shader);else UnityEngine.Object.DestroyImmediate(shader);}
    }
}
