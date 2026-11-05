using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Debris.Simulation.CoupledContacts;
using Debris.Simulation.ParallelProof;
using UnityEngine;
using UnityEngine.Rendering;

namespace Debris.Simulation.Playground
{
    [Serializable] public struct PlaygroundInput
    {
        public uint WorldGeneration;
        public ulong CommandId;
        public string SelectedId;
        public Vector2 Cursor,LocalAnchor,Drive,Impulse;
        public float Turn,MaximumForce;
        public bool Pull,LocalAxes;
    }
    [StructLayout(LayoutKind.Sequential,Pack=4,Size=32)]
    struct PlaygroundPickRecord
    {
        public uint Found,Endpoint,TickLow,TickHigh;
        public Vector2 LocalPoint;
        public uint Generation,Anchored;
    }
    public readonly struct PlaygroundPick
    {
        public readonly string Id;
        public readonly Vector2 LocalPoint;
        public readonly uint Generation;
        public readonly bool Anchored;
        public PlaygroundPick(string id,Vector2 local,uint generation,bool anchored)
        {Id=id;LocalPoint=local;Generation=generation;Anchored=anchored;}
    }

    public sealed class CoupledPlaygroundSession : IDisposable
    {
        readonly CoupledContactArena arena=new CoupledContactArena();
        readonly PlaygroundSeed seed;
        readonly CoupledTickState state;
        readonly CoupledTickTransaction transaction;
        readonly CoupledContactOperator graph;
        readonly CoupledTickStepper stepper;
        readonly ComputeShader shader;
        readonly GraphicsBuffer forces,shapes,pick;
        readonly CommandBuffer commands=new CommandBuffer{name="Physics playground tick"};
        readonly int prepare,pickKernel,placement;
        CoupledTickTicket ticket;
        CoupledTickCompletion completion;
        uint[] diagnostics=new uint[16],failure=new uint[6],clock=new uint[4];
        int readbacks;
        bool readbackError,pending,picking,snapshotting,disposed;
        ulong lastCommandId;
        public PlaygroundDebugLog Log { get; set; }
        [Serializable] sealed class FailureFacts
        {
            public uint Generation;
            public ulong CommandId,AttemptedTick,AcceptedTick;
            public uint[] Diagnostics,FirstFailure,Clock;
        }
        void TraceFault(string detail)=>Log?.Write("solver.fault",detail,JsonUtility.ToJson(new FailureFacts{
            Generation=Generation,CommandId=lastCommandId,AttemptedTick=AttemptedTick,AcceptedTick=AcceptedTick,
            Diagnostics=diagnostics,FirstFailure=failure,Clock=clock}));
        public uint Generation { get; }
        public bool Pending=>pending||picking||snapshotting;
        public bool Faulted { get; private set; }
        public string Fault { get; private set; }
        public ulong AcceptedTick { get; private set; }
        public ulong AttemptedTick { get; private set; }
        public double RecordingMilliseconds { get; private set; }
        public long BufferBytes=>arena.UsedBytes;
        public int Count=>seed.Ordered.Length;
        public GraphicsBuffer Endpoints=>state?.CommittedEndpoints;
        public GraphicsBuffer Shapes=>shapes;
        public uint[] Diagnostics=>(uint[])diagnostics.Clone();
        public uint SelectedSubsteps=>clock[0];
        public bool GravityEnabled=>seed.World.GravityEnabled;
        public Vector2 Gravity=>seed.World.Gravity;
        public CoupledSolveProfile Profile=>seed.World.Profile;
        public PlaygroundWorldDefinition Definition=>seed.World.Copy();
        public CoupledPlaygroundSession(PlaygroundWorldDefinition world,uint generation)
        {
            if(generation==0)throw new ArgumentOutOfRangeException(nameof(generation));
            PlaygroundBudget.Admit(world);Generation=generation;seed=PlaygroundSeed.Build(world);
            if(Count==0)return;
            try
            {
                if(!SystemInfo.supportsComputeShaders||!SystemInfo.supportsAsyncGPUReadback)
                    throw new NotSupportedException("Compute shaders and asynchronous GPU readback are required");
                state=new CoupledTickState(seed.Initial,arena);transaction=new CoupledTickTransaction(state);
                graph=new CoupledContactOperator(seed.Bodies,seed.Primitives,Vector2.one*-512,4,256,256,
                    endpointIdentities:seed.Identities,arena:arena);
                stepper=new CoupledTickStepper(state,transaction,graph);
                string owner=arena.NewOwner("Playground");
                forces=arena.Allocate(owner+"/forces",Count,16);
                shapes=arena.Allocate(owner+"/shapes",Count,32);shapes.SetData(seed.Shapes);
                pick=arena.Allocate(owner+"/pick",1,32);
                var asset=Resources.Load<ComputeShader>("CoupledPlaygroundInput");
                if(asset==null)throw new InvalidOperationException("Missing CoupledPlaygroundInput compute resource");
                shader=UnityEngine.Object.Instantiate(asset);prepare=shader.FindKernel("PrepareForces");pickKernel=shader.FindKernel("Pick");placement=shader.FindKernel("Placement");
                shader.SetInt("_Count",Count);shader.SetInt("_Generation",unchecked((int)Generation));
                foreach(int kernel in new[]{prepare,pickKernel,placement})
                {
                    shader.SetBuffer(kernel,"_Endpoints",state.CommittedEndpoints);
                    shader.SetBuffer(kernel,"_Shapes",shapes);
                }
                shader.SetBuffer(prepare,"_Forces",forces);
                shader.SetBuffer(prepare,"_Parameters",state.CommittedParameters);
                shader.SetBuffer(pickKernel,"_Pick",pick);shader.SetBuffer(pickKernel,"_Header",state.CommittedHeader);
                shader.SetBuffer(placement,"_Pick",pick);shader.SetBuffer(placement,"_Header",state.CommittedHeader);
                long expected=PlaygroundBudget.RequiredBytes(Count,state.GrainCount);
                if(arena.UsedBytes!=expected)throw new InvalidOperationException($"Playground admission arithmetic changed: reserved {expected}, actual {arena.UsedBytes}");
            }
            catch { Dispose();throw; }
        }
        public int IndexOf(string id)=>Array.FindIndex(seed.Ordered,o=>o.Id==id);
        public string IdAt(int index)=>index>=0&&index<Count?seed.Ordered[index].Id:null;
        public bool Submit(PlaygroundInput input)
        {
            if(disposed||Faulted||Pending||Count==0||input.WorldGeneration!=Generation||input.CommandId==0||input.CommandId<=lastCommandId)return false;
            if(!PlaygroundWorldDefinition.Finite(input.Cursor)||!PlaygroundWorldDefinition.Finite(input.LocalAnchor)||
               !PlaygroundWorldDefinition.Finite(input.Drive)||!PlaygroundWorldDefinition.Finite(input.Impulse)||
               !PlaygroundWorldDefinition.Finite(input.Turn)||!PlaygroundWorldDefinition.Finite(input.MaximumForce)||
               input.MaximumForce<0||input.MaximumForce>100000)
                throw new ArgumentException("Invalid playground force command");
            var watch=Stopwatch.StartNew();commands.Clear();
            try
            {
                int selected=IndexOf(input.SelectedId);
                commands.SetComputeIntParam(shader,"_Selected",selected);
                commands.SetComputeIntParam(shader,"_Pull",input.Pull?1:0);commands.SetComputeIntParam(shader,"_LocalAxes",input.LocalAxes?1:0);
                commands.SetComputeVectorParam(shader,"_CursorAnchor",new Vector4(input.Cursor.x,input.Cursor.y,input.LocalAnchor.x,input.LocalAnchor.y));
                commands.SetComputeVectorParam(shader,"_Drive",new Vector4(input.Drive.x,input.Drive.y,input.Turn,input.MaximumForce));
                commands.SetComputeVectorParam(shader,"_Impulse",new Vector4(input.Impulse.x,input.Impulse.y,0,0));
                var gravity=seed.World.GravityEnabled?seed.World.Gravity:Vector2.zero;
                commands.SetComputeVectorParam(shader,"_Gravity",new Vector4(gravity.x,gravity.y,0,0));
                commands.DispatchCompute(shader,prepare,(Count+63)/64,1,1);
                ticket=stepper.RecordTick(commands,AcceptedTick+1,seed.World.Profile,forces);AttemptedTick=ticket.Tick;
                readbacks=4;readbackError=false;
                commands.RequestAsyncReadback(transaction.Completions,64,(int)ticket.Slot*64,r=>{
                    if(disposed)return;if(r.hasError)readbackError=true;else completion=r.GetData<CoupledTickCompletion>()[0];readbacks--;
                });
                commands.RequestAsyncReadback(stepper.Diagnostics,r=>{
                    if(disposed)return;if(r.hasError)readbackError=true;else diagnostics=r.GetData<uint>().ToArray();readbacks--;
                });
                commands.RequestAsyncReadback(transaction.FirstFailure,r=>{
                    if(disposed)return;if(r.hasError)readbackError=true;else failure=r.GetData<uint>().ToArray();readbacks--;
                });
                commands.RequestAsyncReadback(stepper.StepClock,r=>{
                    if(disposed)return;if(r.hasError)readbackError=true;else clock=r.GetData<uint>().ToArray();readbacks--;
                });
                pending=true;Graphics.ExecuteCommandBuffer(commands);commands.Clear();lastCommandId=input.CommandId;
                RecordingMilliseconds=watch.Elapsed.TotalMilliseconds;return true;
            }
            catch(Exception e){Faulted=true;Fault="Tick submission failed: "+e.Message;TraceFault(e.ToString());commands.Clear();return false;}
        }
        public bool Poll()
        {
            if(disposed||!pending||readbacks!=0)return false;
            pending=false;
            if(readbackError){Faulted=true;Fault="GPU completion readback failed; accepted buffers retained";TraceFault(Fault);return true;}
            try { transaction.Retire(ticket,completion); }
            catch(Exception e){Faulted=true;Fault="Completion mismatch: "+e.Message;TraceFault(e.ToString());return true;}
            AcceptedTick=completion.CompletedTick;
            if(completion.Status!=(uint)CoupledTickStatus.Committed)
            {
                Faulted=true;Fault=$"Tick {completion.AttemptedTick}: {(CoupledTickFault)completion.Fault} at {(CoupledTickStage)completion.Stage}; physical {(CoupledTickPhysicalFault)diagnostics[0]}, source {failure[5]}";TraceFault(Fault);
            }
            return true;
        }
        public async Task<PlaygroundPick> PickAsync(Vector2 point,bool includeAnchors=true)
        {
            if(disposed||Pending||Count==0)return default;
            picking=true;
            try
            {
                commands.Clear();commands.SetComputeVectorParam(shader,"_Pointer",new Vector4(point.x,point.y,0,0));
                commands.SetComputeIntParam(shader,"_IncludeAnchors",includeAnchors?1:0);
                commands.DispatchCompute(shader,pickKernel,1,1,1);
                var result=Read<PlaygroundPickRecord>(commands,pick);Graphics.ExecuteCommandBuffer(commands);commands.Clear();
                var record=(await result)[0];
                if(disposed||record.Generation!=Generation)return default;
                if(record.Found==0)return new PlaygroundPick(null,Vector2.zero,Generation,false);
                if(record.Endpoint>=Count)return default;
                return new PlaygroundPick(seed.Ordered[record.Endpoint].Id,record.LocalPoint,Generation,record.Anchored!=0);
            }
            finally { picking=false; }
        }
        public async Task<PlaygroundWorldDefinition> SnapshotAsync()
        {
            if(disposed)throw new ObjectDisposedException(nameof(CoupledPlaygroundSession));
            if(Pending)throw new InvalidOperationException("Drain tick and picking before a world edit or export");
            var world=seed.World.Copy();if(Count==0)return world;snapshotting=true;
            try
            {
                commands.Clear();var task=Read<BodyState>(commands,state.CommittedEndpoints);
                Graphics.ExecuteCommandBuffer(commands);commands.Clear();var motion=await task;
                if(disposed)throw new ObjectDisposedException(nameof(CoupledPlaygroundSession));
                foreach(var o in world.Objects)
                {
                    var e=motion[IndexOf(o.Id)];o.Center=e.Center;o.Velocity=e.Velocity;o.Angle=e.Angle;o.Spin=e.AngularVelocity;
                }
                return world;
            }
            finally { snapshotting=false; }
        }
        public async Task<PlaygroundObject> InspectAsync(string id)
        {
            int index=IndexOf(id);if(disposed||Pending||index<0)return null;picking=true;
            try
            {
                var source=new TaskCompletionSource<BodyState>();commands.Clear();
                commands.RequestAsyncReadback(state.CommittedEndpoints,32,index*32,r=>{
                    if(r.hasError)source.TrySetException(new InvalidOperationException("Inspection readback failed"));
                    else source.TrySetResult(r.GetData<BodyState>()[0]);
                });Graphics.ExecuteCommandBuffer(commands);commands.Clear();
                var e=await source.Task;if(disposed)return null;
                var o=seed.Ordered[index].Copy();o.Center=e.Center;o.Velocity=e.Velocity;o.Angle=e.Angle;o.Spin=e.AngularVelocity;return o;
            }
            finally { picking=false; }
        }
        public async Task<bool> CheckPlacementAsync(PlaygroundObject value)
        {
            if(disposed||Pending)return false;
            if(Count==0)return true;picking=true;
            try
            {
                commands.Clear();commands.SetComputeVectorParam(shader,"_Placement",new Vector4(value.Center.x,value.Center.y,value.Half.x,value.Half.y));
                commands.SetComputeFloatParam(shader,"_PlacementAngle",value.Angle);
                commands.DispatchCompute(shader,placement,1,1,1);var task=Read<PlaygroundPickRecord>(commands,pick);
                Graphics.ExecuteCommandBuffer(commands);commands.Clear();var result=(await task)[0];
                return !disposed&&result.Generation==Generation&&result.Found==1;
            }
            finally { picking=false; }
        }
        public async Task ValidateInitialAsync()
        {
            if(disposed||Pending)throw new InvalidOperationException("Initial geometry validation requires an idle owner");
            if(Count==0)return;snapshotting=true;GraphicsBuffer proposal=null;
            try
            {
                proposal=arena.Allocate(arena.NewOwner("PlaygroundAdmission")+"/primitives",Count,56);
                proposal.SetData(seed.Primitives);
                using(var gate=new CoupledFinalGeometryGate(graph.Manifolds))
                {
                    commands.Clear();gate.RecordRequery(commands,proposal);
                    var result=Read<uint>(commands,gate.Decision);Graphics.ExecuteCommandBuffer(commands);commands.Clear();
                    var facts=await result;
                    if(facts[0]!=1||facts[1]!=0)throw new InvalidOperationException($"Proposed geometry rejected: {facts[1]}");
                }
            }
            finally { if(proposal!=null)arena.Release(proposal);snapshotting=false; }
        }
        static Task<T[]> Read<T>(CommandBuffer commands,GraphicsBuffer buffer) where T:struct
        {
            var source=new TaskCompletionSource<T[]>();commands.RequestAsyncReadback(buffer,r=>{
                if(r.hasError)source.TrySetException(new InvalidOperationException("Playground readback failed"));
                else source.TrySetResult(r.GetData<T>().ToArray());
            });return source.Task;
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            // Explicit stopped-session boundary only; never used for normal stepping.
            if(state!=null)AsyncGPUReadback.WaitAllRequests();
            commands.Dispose();if(forces!=null)arena.Release(forces);if(shapes!=null)arena.Release(shapes);if(pick!=null)arena.Release(pick);
            if(shader!=null){if(Application.isPlaying)UnityEngine.Object.Destroy(shader);else UnityEngine.Object.DestroyImmediate(shader);}
            stepper?.Dispose();graph?.Dispose();transaction?.Dispose();state?.Dispose();arena.AssertEmpty();
        }
    }
}
