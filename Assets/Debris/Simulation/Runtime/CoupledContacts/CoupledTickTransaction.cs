using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Debris.Simulation.CoupledContacts
{
    // One FIFO owner per fixed-topology state. CPU records commands and
    // reserves compact completion slots; only GPU decisions publish payloads.
    // Callers must execute recorded command buffers exactly once in recording order on the
    // same queue and retire tickets only from completed readbacks/fences.
    public sealed class CoupledTickTransaction : IDisposable
    {
        public const int CompletionCapacity=16;
        readonly CoupledTickState state;
        readonly CoupledContactArena arena;
        readonly ComputeShader shader;
        readonly GraphicsBuffer control,failure,completions;
        readonly int begin,stage,substep,validateEndpoints,validateGrains,validateBindings,
            validateParameters,validateTerrain,validateCache,syncGrains,prepare,complete;
        readonly int[] copyKernels=new int[8];
        readonly string[] prefixes={"Endpoint","Grain","Parameter","Binding","Cache","Word","Word","Word"};
        readonly bool[] reserved=new bool[CompletionCapacity];
        readonly CoupledTickTicket[] tickets=new CoupledTickTicket[CompletionCapacity];
        readonly int[] reservedSubsteps=new int[CompletionCapacity];
        ulong lastSubmitted;
        uint expectedGeneration,expectedCache;
        bool disposed,recording;
        CommandBuffer recordingTarget;
        internal CoupledTickState State=>state;
        internal void CheckRecording(CommandBuffer target)=>Check(target,true);
        public GraphicsBuffer ActivityGate=>control;
        public GraphicsBuffer Control=>control;
        // failed, attempted tick low/high, original fault, original stage.
        public GraphicsBuffer FirstFailure=>failure;
        public GraphicsBuffer Completions=>completions;
        public long BufferBytes=>256+20+CompletionCapacity*64;

        public CoupledTickTransaction(CoupledTickState state)
        {
            this.state=state??throw new ArgumentNullException(nameof(state));
            state.CheckTransactionAvailable();
            arena=state.Arena;
            arena.EnsureCapacity(BufferBytes);
            string owner=arena.NewOwner("TickTransaction");
            try
            {
                var asset=Resources.Load<ComputeShader>("CoupledTickTransaction");
                if(asset==null)throw new InvalidOperationException("Missing CoupledTickTransaction resource");
                shader=UnityEngine.Object.Instantiate(asset);
                control=arena.Allocate(owner+"/control",64,4);
                control.SetData(new uint[64]);
                failure=arena.Allocate(owner+"/failure",5,4);failure.SetData(new uint[5]);
                completions=arena.Allocate(owner+"/completions",CompletionCapacity,64);
                completions.SetData(new CoupledTickCompletion[CompletionCapacity]);
                begin=Kernel("BeginTick");stage=Kernel("ValidateStage");substep=Kernel("AcceptSubstep");
                validateEndpoints=Kernel("ValidateEndpoints");validateGrains=Kernel("ValidateGrains");
                validateBindings=Kernel("ValidateBindings");validateParameters=Kernel("ValidateParameters");
                validateTerrain=Kernel("ValidateTerrain");syncGrains=Kernel("SynchronizeGrains");
                validateCache=Kernel("ValidateCache");
                prepare=Kernel("PreparePublication");complete=Kernel("CompleteTick");
                for(int i=0;i<copyKernels.Length;i++)copyKernels[i]=Kernel("Copy"+
                    (i<5?new[]{"Endpoints","Grains","Parameters","Bindings","Cache"}[i]:"Words"));
                shader.SetBuffer(begin,"_Header",state.HeaderBuffer);shader.SetBuffer(begin,"_Failure",failure);
                foreach(int kernel in new[]{begin,prepare,complete})
                    shader.SetBuffer(kernel,"_WorkingHeader",state.WorkingHeader);
                shader.SetInt("_CacheCapacity",state.CacheCount);
                shader.SetBuffer(prepare,"_Header",state.HeaderBuffer);
                shader.SetBuffer(complete,"_Header",state.HeaderBuffer);shader.SetBuffer(complete,"_Failure",failure);
                shader.SetBuffer(complete,"_Completions",completions);
                lastSubmitted=state.InitialHeader.CompletedTick;
                expectedGeneration=state.InitialHeader.Generation;expectedCache=state.InitialHeader.CacheGeneration;
                state.ClaimTransaction();
            }
            catch { Dispose();throw; }
        }
        int Kernel(string name)
        {
            int kernel=shader.FindKernel(name);shader.SetBuffer(kernel,"_Control",control);return kernel;
        }
        static int Groups(int count)=>Math.Max(1,(count+63)/64);
        void Check(CommandBuffer target,bool requireRecording)
        {
            if(disposed)throw new ObjectDisposedException(nameof(CoupledTickTransaction));
            if(target==null)throw new ArgumentNullException(nameof(target));
            if(requireRecording&&!recording)throw new InvalidOperationException("Begin a tick before recording its stages");
            if(requireRecording&&target!=recordingTarget)
                throw new InvalidOperationException("Record a complete tick into the command buffer passed to Begin");
        }
        void UInt(CommandBuffer target,string name,uint value)
        {target.SetComputeIntParam(shader,name,unchecked((int)value));}
        public CoupledTickTicket RecordBegin(CommandBuffer target,ulong tick,int substeps)
        {
            Check(target,false);
            if(recording)throw new InvalidOperationException("Finish the active tick before beginning another");
            if(substeps<1||substeps>16||lastSubmitted==ulong.MaxValue||tick!=lastSubmitted+1||
               expectedGeneration==uint.MaxValue||expectedCache==uint.MaxValue)
                throw new ArgumentException("Invalid FIFO tick, substep count or generation exhaustion");
            int slot=Array.FindIndex(reserved,x=>!x);
            if(slot<0)throw new InvalidOperationException("Coupled completion ring is full");
            var ticket=new CoupledTickTicket(this,tick,(uint)slot,expectedGeneration);
            UInt(target,"_TickLow",(uint)tick);UInt(target,"_TickHigh",(uint)(tick>>32));
            UInt(target,"_PreviousLow",(uint)lastSubmitted);UInt(target,"_PreviousHigh",(uint)(lastSubmitted>>32));
            UInt(target,"_ExpectedGeneration",expectedGeneration);UInt(target,"_ExpectedCache",expectedCache);
            UInt(target,"_Topology",state.InitialHeader.TopologyRevision);UInt(target,"_Identity",state.InitialHeader.IdentityRevision);
            UInt(target,"_Slot",(uint)slot);UInt(target,"_Substeps",(uint)substeps);
            target.DispatchCompute(shader,begin,1,1,1);
            Copy(target,0);
            reserved[slot]=true;tickets[slot]=ticket;reservedSubsteps[slot]=substeps;
            lastSubmitted=tick;expectedGeneration++;expectedCache++;recording=true;recordingTarget=target;
            return ticket;
        }
        void RecordStage(CommandBuffer target,CoupledTickStage expected,GraphicsBuffer result,int passWord,int faultWord)
        {
            Check(target,true);
            if(result==null||result==control||result==failure||result==completions||
               result.stride!=4||result.count<=Math.Max(passWord,faultWord))
                throw new ArgumentException("A distinct stage result with accepted/fault words is required",nameof(result));
            UInt(target,"_Stage",(uint)expected);UInt(target,"_PassWord",(uint)passWord);UInt(target,"_FaultWord",(uint)faultWord);
            target.SetComputeBufferParam(shader,stage,"_StageResult",result);
            target.DispatchCompute(shader,stage,1,1,1);
        }
        public void RecordVelocityResult(CommandBuffer target,GraphicsBuffer solveControl)
        {RecordStage(target,CoupledTickStage.Velocity,solveControl,1,3);}
        public void RecordPredictionResult(CommandBuffer target,GraphicsBuffer verdict)
        {RecordStage(target,CoupledTickStage.Prediction,verdict,0,1);}
        public void RecordPositionResult(CommandBuffer target,CoupledPositionCorrection correction)
        {
            if(correction==null)throw new ArgumentNullException(nameof(correction));
            RecordPositionResult(target,correction.Decision);
        }
        public void RecordPositionResult(CommandBuffer target,GraphicsBuffer verdict)
        {RecordStage(target,CoupledTickStage.Position,verdict,0,1);}
        public void RecordSubstep(CommandBuffer target)
        {Check(target,true);target.DispatchCompute(shader,substep,1,1,1);}
        // The future step owner supplies conservation/count/envelope facts.
        // Publication requires this verdict after all declared substeps.
        public void RecordFinalFacts(CommandBuffer target,GraphicsBuffer verdict)
        {RecordStage(target,CoupledTickStage.Final,verdict,0,1);}

        void BindPair(CommandBuffer target,int kernel,string prefix,GraphicsBuffer source,GraphicsBuffer destination,int count)
        {
            target.SetComputeBufferParam(shader,kernel,"_"+prefix+"Source",source);
            target.SetComputeBufferParam(shader,kernel,"_"+prefix+"Destination",destination);
            UInt(target,"_Count",(uint)count);
        }
        void Copy(CommandBuffer target,int mode)
        {
            UInt(target,"_CopyMode",(uint)mode);
            foreach(var pair in state.Pairs)
            {
                int index=(int)pair.Kind,kernel=copyKernels[index];
                BindPair(target,kernel,prefixes[index],mode==1?pair.Working:pair.Committed,
                    mode==1?pair.Committed:pair.Working,pair.Count);
                target.DispatchCompute(shader,kernel,Groups(pair.Count),1,1);
            }
        }
        void ValidatePair(CommandBuffer target,int kernel,CoupledTickState.Kind kind)
        {
            var pair=state.Pairs[(int)kind];
            BindPair(target,kernel,prefixes[(int)kind],pair.Committed,pair.Working,pair.Count);
            target.DispatchCompute(shader,kernel,Groups(pair.Count),1,1);
        }
        public void RecordEnd(CommandBuffer target)
        {
            Check(target,true);
            UInt(target,"_Count",(uint)state.GrainCount);
            target.SetComputeBufferParam(shader,syncGrains,"_GrainDestination",state.WorkingGrains);
            target.SetComputeBufferParam(shader,syncGrains,"_EndpointSource",state.WorkingEndpoints);
            target.DispatchCompute(shader,syncGrains,Groups(state.GrainCount),1,1);
            BindPair(target,validateEndpoints,"Endpoint",state.CommittedEndpoints,state.WorkingEndpoints,state.EndpointCount);
            target.SetComputeBufferParam(shader,validateEndpoints,"_ParameterSource",state.CommittedParameters);
            target.DispatchCompute(shader,validateEndpoints,Groups(state.EndpointCount),1,1);
            ValidatePair(target,validateGrains,CoupledTickState.Kind.Grains);
            ValidatePair(target,validateBindings,CoupledTickState.Kind.Bindings);
            ValidatePair(target,validateParameters,CoupledTickState.Kind.Parameters);
            ValidatePair(target,validateTerrain,CoupledTickState.Kind.Terrain);
            ValidatePair(target,validateCache,CoupledTickState.Kind.Cache);
            target.DispatchCompute(shader,prepare,1,1,1);
            Copy(target,1);Copy(target,2);
            target.DispatchCompute(shader,complete,1,1,1);
            recording=false;recordingTarget=null;
        }
        // Completion comes from a completed GPU readback, never a submission
        // guess. Releasing the slot cannot steer physics or resume a failed FIFO.
        public void Retire(CoupledTickTicket ticket,CoupledTickCompletion completion)
        {
            if(disposed)throw new ObjectDisposedException(nameof(CoupledTickTransaction));
            if(ticket.Owner!=this||ticket.Slot>=CompletionCapacity||!reserved[ticket.Slot]||
               tickets[ticket.Slot].Tick!=ticket.Tick||tickets[ticket.Slot].ExpectedGeneration!=ticket.ExpectedGeneration||
               completion.Version!=1||completion.AttemptedTick!=ticket.Tick||
               completion.ExpectedPreviousTick!=ticket.Tick-1||completion.Status<1||completion.Status>3||
               completion.Substeps!=reservedSubsteps[ticket.Slot]||
               completion.PassedSubsteps>completion.Substeps||
               completion.TopologyRevision!=state.InitialHeader.TopologyRevision||
               completion.IdentityRevision!=state.InitialHeader.IdentityRevision||
               completion.Stage>7||
               (completion.Status==1&&(completion.Fault!=0||completion.CompletedTick!=ticket.Tick||
                   completion.Generation!=ticket.ExpectedGeneration+1||
                   completion.PassedSubsteps!=completion.Substeps||completion.Stage!=0))||
               (completion.Status!=1&&(completion.Fault==0||completion.Stage==0||completion.CompletedTick>=ticket.Tick))||
               (completion.Status==3&&completion.Fault!=(uint)CoupledTickFault.Dependency))
                throw new InvalidOperationException("Completion does not match the reserved coupled tick");
            reserved[ticket.Slot]=false;
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            if(completions!=null)arena.Release(completions);
            if(failure!=null)arena.Release(failure);
            if(control!=null)arena.Release(control);
            if(shader!=null)
            {if(Application.isPlaying)UnityEngine.Object.Destroy(shader);else UnityEngine.Object.DestroyImmediate(shader);}
        }
    }
}
