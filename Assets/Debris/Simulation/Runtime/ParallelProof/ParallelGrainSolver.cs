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
        readonly ProofTrace trace;
        readonly CommandBuffer commands = new CommandBuffer { name="B3R parallel grain proof" };
        readonly List<GraphicsBuffer> buffers = new List<GraphicsBuffer>();
        readonly HashSet<uint> identities = new HashSet<uint>();
        readonly Dictionary<string,int> kernels = new Dictionary<string,int>();
        BodyParameters[] bodyDefinitions;
        readonly GraphicsBuffer rigidContacts,rigidCount,rigidPairs;
        readonly GraphicsBuffer committed,state,grains,parameters,boundaries,diagnostics,args,starts;
        readonly GraphicsBuffer counts,cursors,offsets,sums,blockOffsets,indices;
        readonly GraphicsBuffer rows,rowCounts,rowOffsets,rowSums,rowBlocks,contacts,degrees,adjOffsets,adjSums,adjBlocks,adjCursors,adjacency,increments;
        readonly int capacity,bodies,endpoints,slots,velocityIterations,positionIterations,boundaryCapacity;
        int active;
        int snapshotRequests;
        public long BufferBytes { get; private set; }
        public GraphicsBuffer Grains => grains;
        public GraphicsBuffer Bodies => committed;
        public GraphicsBuffer Boundaries => boundaries;
        public GraphicsBuffer Parameters => parameters;
        public int GrainCount => active;
        public int GrainCapacity => capacity;
        public int BodyStart => capacity;
        public int BodyCount => bodies;
        public int BoundaryCapacity => boundaryCapacity;
        public int BoundaryCount { get; private set; }
        public BodyParameters[] BodyDefinitions => (BodyParameters[])bodyDefinitions.Clone();
        public int SnapshotRequests => snapshotRequests;
        public Task<uint[]> DiagnosticsAsync()=>Read<uint>(diagnostics);
        public Task<ProofMetricsReadback> MetricsAsync()=>metrics.ReadAsync();
        public Task<ProofTraceReadback> TraceAsync()=>trace!=null?trace.ReadAsync():throw new InvalidOperationException("Proof tracing is disabled");
        public ParallelGrainSolver(LooseCell[] initialGrains, BodyState[] initialBodies, BodyParameters[] bodyParameters, Boundary[] patches,
            int velocityIterations=8,int positionIterations=4,float friction=.3f,int candidateSlots=64,int rigidContactCapacity=4096,ProofTraceConfiguration traceConfiguration=null,float[] grainMasses=null,int allocatedGrainCapacity=0,int allocatedBoundaryCapacity=0)
        {
            capacity=allocatedGrainCapacity==0?initialGrains.Length:allocatedGrainCapacity;
            if(capacity<initialGrains.Length||capacity>8192)throw new ArgumentOutOfRangeException(nameof(allocatedGrainCapacity));
            ValidateInput(initialGrains,initialBodies,bodyParameters,patches,velocityIterations,positionIterations,friction,candidateSlots,capacity);
            if(grainMasses!=null&&(grainMasses.Length!=initialGrains.Length||Array.Exists(grainMasses,m=>!Finite(m)||m<=0)))throw new ArgumentException("Grain masses must be finite, positive, and match the grain population.");
            boundaryCapacity=allocatedBoundaryCapacity==0?patches.Length:allocatedBoundaryCapacity;
            if(rigidContactCapacity<1||rigidContactCapacity>4096||patches.Length>4096||boundaryCapacity<patches.Length||boundaryCapacity>4096)throw new ArgumentOutOfRangeException();
            traceConfiguration?.Validate(initialGrains.Length,initialGrains.Length+initialBodies.Length,patches.Length,initialGrains.Length*candidateSlots,velocityIterations,positionIterations);
            active=initialGrains.Length;bodies=initialBodies.Length;endpoints=capacity+bodies;slots=candidateSlots;
            this.velocityIterations=velocityIterations;this.positionIterations=positionIterations;
            shader=UnityEngine.Object.Instantiate(Resources.Load<ComputeShader>("ParallelGrains"));
            // A world may contain no loose grains.  Allocate a harmless backing
            // element because GraphicsBuffer does not accept a zero count, while
            // all dispatches continue to use the real endpoint/grain counts.
            committed=Buffer(Math.Max(1,endpoints),32);state=Buffer(Math.Max(1,endpoints),32);grains=Buffer(Math.Max(1,capacity),48);parameters=Buffer(Math.Max(1,endpoints),32);boundaries=Buffer(Math.Max(1,boundaryCapacity),32);
            diagnostics=Buffer(16,4);starts=Buffer(endpoints,16);
            // 17 dynamic bodies (ship plus 16 fragments) and one anchored
            // terrain endpoint share this bounded manifold table.
            rigidContacts=Buffer(rigidContactCapacity,72);rigidCount=Buffer(1,4);rigidPairs=Buffer(18*18,4);
            args=Buffer(16*10*3,4,GraphicsBuffer.Target.Structured|GraphicsBuffer.Target.IndirectArguments);
            counts=Buffer(65536,4);cursors=Buffer(65536,4);offsets=Buffer(65536,4);sums=Buffer(256,4);blockOffsets=Buffer(256,4);indices=Buffer(Math.Max(1,capacity),4);
            rows=Buffer(Math.Max(1,capacity*slots),64);rowCounts=Buffer(Math.Max(1,capacity),4);rowOffsets=Buffer(Math.Max(1,capacity),4);rowSums=Buffer(256,4);rowBlocks=Buffer(256,4);
            contacts=Buffer(Math.Max(1,capacity*slots),64);degrees=Buffer(Math.Max(1,endpoints),4);adjOffsets=Buffer(Math.Max(1,endpoints),4);adjSums=Buffer(256,4);adjBlocks=Buffer(256,4);adjCursors=Buffer(Math.Max(1,endpoints),4);
            adjacency=Buffer(Math.Max(1,capacity*slots*2),4);increments=Buffer(Math.Max(1,capacity*slots),16);
            var initial=new BodyState[endpoints];var physical=new BodyParameters[endpoints];var initialGrainBuffer=new LooseCell[capacity];
            for(int i=0;i<active;i++)
            {
                var g=initialGrains[i];if(g.Material==0||g.Identity==0||!identities.Add(g.Identity))throw new ArgumentException("Grain material and identities must be valid and unique");
                initial[i]=new BodyState{Center=g.Center,Velocity=g.Velocity,Angle=g.Angle,AngularVelocity=g.AngularVelocity};
                float mass=grainMasses==null?1:grainMasses[i];
                physical[i]=new BodyParameters{InverseMass=1/mass,InverseInertia=6/mass,Mobility=1};
            }
            Array.Copy(initialBodies,0,initial,capacity,bodies);Array.Copy(bodyParameters,0,physical,capacity,bodies);Array.Copy(initialGrains,initialGrainBuffer,active);bodyDefinitions=(BodyParameters[])bodyParameters.Clone();
            foreach(var patch in patches)if(patch.Body<capacity||patch.Body>=endpoints)throw new ArgumentException("Boundary body is an endpoint index");
            committed.SetData(initial);state.SetData(initial);grains.SetData(initialGrainBuffer);parameters.SetData(physical);if(patches.Length>0)boundaries.SetData(patches);diagnostics.SetData(new uint[16]);
            shader.SetInt("_RigidCapacity",rigidContactCapacity);shader.SetFloat("_RigidGatherMargin",.75f);shader.SetFloat("_RigidSolidTarget",.0001f);
            BoundaryCount=patches.Length;
            shader.SetInt("_N",active);shader.SetInt("_BodyStart",capacity);shader.SetInt("_Bodies",bodies);shader.SetInt("_Endpoints",endpoints);shader.SetInt("_BoundaryCount",BoundaryCount);
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
            if(traceConfiguration!=null)
            {
                trace=new ProofTrace(active,endpoints,active*slots,positionIterations,velocityIterations,initialGrains,physical,patches,traceConfiguration,friction);
                BufferBytes+=trace.AllocatedBytes;
            }
            metrics=new ProofMetrics(active,bodies);BufferBytes+=metrics.AllocatedBytes;
            metrics.Record(commands,committed,parameters,grains);Graphics.ExecuteCommandBuffer(commands);commands.Clear();
        }
        static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
        static bool Finite(Vector2 value)=>Finite(value.x)&&Finite(value.y);
        static void ValidateInput(LooseCell[] grains,BodyState[] bodies,BodyParameters[] definitions,Boundary[] patches,int velocity,int position,float friction,int slots,int bodyStart)
        {
            if(grains==null||bodies==null||definitions==null||patches==null||grains.Length>10000||bodies.Length>18||bodies.Length!=definitions.Length)
                throw new ArgumentException("Invalid proof population");
            if(!((velocity==4&&position==2)||(velocity==8&&position==4)||(velocity==12&&position==6)))throw new ArgumentException("Only locked profiles 4/2, 8/4, 12/6 are supported");
            if(slots<1||slots>64||!Finite(friction)||friction<0)throw new ArgumentOutOfRangeException();
            var ids=new HashSet<uint>();
            foreach(var grain in grains)
                if(grain.Material==0||grain.Identity==0||!ids.Add(grain.Identity)||!Finite(grain.Center)||!Finite(grain.Velocity)||!Finite(grain.Angle)||!Finite(grain.AngularVelocity))
                    throw new ArgumentException("Invalid finite grain or duplicate identity");
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
                    if(owned[f]||patch.Body!=bodyStart+i||!Finite(patch.Center)||!Finite(patch.HalfSize)||patch.HalfSize.x<=0||patch.HalfSize.y<=0)throw new ArgumentException("Invalid or multiply owned boundary patch");
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
        void Trace(int substep,ProofTraceStage stage,int iteration=-1)
        {
            trace?.Record(commands,state,contacts,degrees,increments,diagnostics,substep,stage,iteration);
        }
        public void Step(Vector2 localForce=default,float torque=0)
        {
            if(!Finite(localForce)||!Finite(torque))throw new ArgumentException("Flight force and torque must be finite");
            commands.Clear();trace?.BeginTick(commands);commands.BeginSample("B3R.ParallelPhysics");commands.SetComputeVectorParam(shader,"_Force",new Vector4(localForce.x,localForce.y,torque,0));
            Direct("Begin",endpoints);Direct("Speed",endpoints);Direct("SelectSubsteps",1,1);
            for(int s=0;s<16;s++)
            {
                commands.SetComputeIntParam(shader,"_Substep",s);Indirect("Prepare",s,0);Trace(s,ProofTraceStage.SubstepStart);
                commands.BeginSample("B3R.Bins");Indirect("ClearBins",s,4);Indirect("CountBins",s,1);Scan(counts,offsets,sums,blockOffsets,65536,s,4);Indirect("ScatterBins",s,1);commands.EndSample("B3R.Bins");
                commands.BeginSample("B3R.Gather");Indirect("Gather",s,1);Scan(rowCounts,rowOffsets,rowSums,rowBlocks,active,s,5);Indirect("ClearDegrees",s,0);Indirect("Compact",s,1);Indirect("ValidateDegrees",s,0);Scan(degrees,adjOffsets,adjSums,adjBlocks,endpoints,s,6);Indirect("ScatterAdjacency",s,2);if(bodies>1){Indirect("RigidClearContacts",s,7);Indirect("RigidGatherContacts",s,9);Indirect("RigidCheckCapacity",s,7);}commands.EndSample("B3R.Gather");
                Trace(s,ProofTraceStage.BeforeVelocity);
                commands.BeginSample("B3R.Velocity");commands.SetComputeIntParam(shader,"_Position",0);
                for(int i=0;i<velocityIterations;i++)
                {
                    Indirect("EvaluateVelocity",s,2);Trace(s,ProofTraceStage.VelocityEvaluated,i);
                    Indirect("ApplyGrains",s,1);if(bodies>0)Indirect("ApplyBodies",s,3);
                    if(bodies>1){Indirect("RigidSolveVelocity",s,7);Indirect("RigidSolveVelocity",s,7);}
                    Trace(s,ProofTraceStage.VelocityApplied,i);
                }
                commands.EndSample("B3R.Velocity");Indirect("Predict",s,0);Trace(s,ProofTraceStage.Predicted);
                commands.BeginSample("B3R.Position");commands.SetComputeIntParam(shader,"_Position",1);
                for(int i=0;i<positionIterations;i++)
                {
                    Indirect("EvaluatePosition",s,2);Trace(s,ProofTraceStage.PositionEvaluated,i);
                    Indirect("ApplyGrains",s,1);if(bodies>0)Indirect("ApplyBodies",s,3);
                    if(bodies>1){Indirect("RigidSolvePosition",s,7);Indirect("RigidSolvePosition",s,7);}
                    Trace(s,ProofTraceStage.PositionApplied,i);
                }
                commands.EndSample("B3R.Position");Indirect("PrepareValidation",s,7);Indirect("Validate",s,8);if(bodies>1)Indirect("RigidValidate",s,7);
                Trace(s,ProofTraceStage.Validation);
            }
            Direct("Commit",endpoints);Direct("Acknowledge",1,1);metrics.Record(commands,committed,parameters,grains);commands.EndSample("B3R.ParallelPhysics");Graphics.ExecuteCommandBuffer(commands);
        }
        public async Task<ProofSnapshot> SnapshotAsync()
        {
            snapshotRequests++;
            var g=Read<LooseCell>(grains);var s=Read<BodyState>(committed);var d=Read<uint>(diagnostics);
            await Task.WhenAll(g,s,d);
            // The one-element allocation for an empty population is GPU backing
            // storage, not a real grain that callers may count or index.
            if(active==0)return new ProofSnapshot{Grains=Array.Empty<LooseCell>(),Endpoints=s.Result,Diagnostics=d.Result};
            var values=g.Result;Array.Resize(ref values,active);return new ProofSnapshot{Grains=values,Endpoints=s.Result,Diagnostics=d.Result};
        }
        // Topology transactions call this only with no unresolved GPU tick.
        // The allocated grain region never moves, therefore body endpoints and
        // all boundary references remain stable across admission.
        public bool TryAppend(LooseCell grain,float mass)
        {
            if(active>=capacity||grain.Material==0||grain.Identity==0||identities.Contains(grain.Identity)||!Finite(grain.Center)||!Finite(grain.Velocity)||!Finite(grain.Angle)||!Finite(grain.AngularVelocity)||!Finite(mass)||mass<=0)return false;
            grains.SetData(new[]{grain},0,active,1);
            var body=new BodyState{Center=grain.Center,Velocity=grain.Velocity,Angle=grain.Angle,AngularVelocity=grain.AngularVelocity};
            var parametersValue=new BodyParameters{InverseMass=1/mass,InverseInertia=6/mass,Mobility=1};
            committed.SetData(new[]{body},0,active,1);state.SetData(new[]{body},0,active,1);parameters.SetData(new[]{parametersValue},0,active,1);
            identities.Add(grain.Identity);active++;shader.SetInt("_N",active);return true;
        }
        // This cache operation is intentionally available only to the
        // session-owned topology fence, after all submitted ticks have
        // acknowledged. It never reallocates the renderer-bound buffers.
        public bool TryReplaceBoundaryCache(Boundary[] replacement,BodyParameters[] definitions)
        {
            if(replacement==null||definitions==null||definitions.Length!=bodies||replacement.Length>boundaryCapacity)return false;
            try { ValidateInput(Array.Empty<LooseCell>(),new BodyState[bodies],definitions,replacement,velocityIterations,positionIterations,.3f,slots,capacity); }
            catch(ArgumentException) { return false; }
            boundaries.SetData(replacement);parameters.SetData(definitions,0,capacity,bodies);bodyDefinitions=(BodyParameters[])definitions.Clone();BoundaryCount=replacement.Length;shader.SetInt("_BoundaryCount",BoundaryCount);return true;
        }
        // Normal gameplay reads only this compact, ordered completion. Full
        // snapshots remain an explicit inspection/test operation.
        public async Task<ProofCompletion> CompletionAsync(int endpoint)
        {
            if(endpoint<0||endpoint>=endpoints)throw new ArgumentOutOfRangeException(nameof(endpoint));
            var s=Read<BodyState>(committed);var d=Read<uint>(diagnostics);
            await Task.WhenAll(s,d);
            return new ProofCompletion{State=s.Result[endpoint],Diagnostics=d.Result};
        }
        static Task<T[]> Read<T>(GraphicsBuffer b) where T:struct
        {
            var source=new TaskCompletionSource<T[]>();AsyncGPUReadback.Request(b,r=>{if(r.hasError)source.SetException(new InvalidOperationException("Proof GPU readback failed"));else source.SetResult(r.GetData<T>().ToArray());});return source.Task;
        }
        public void Dispose(){trace?.Dispose();metrics.Dispose();commands.Dispose();foreach(var b in buffers)b.Dispose();if(Application.isPlaying)UnityEngine.Object.Destroy(shader);else UnityEngine.Object.DestroyImmediate(shader);}
    }

    public sealed class ProofCompletion
    {
        public BodyState State;
        public uint[] Diagnostics;
        public SolverFault Fault => (SolverFault)Diagnostics[0];
        public uint CompletedTick => Diagnostics[1];
    }
}
