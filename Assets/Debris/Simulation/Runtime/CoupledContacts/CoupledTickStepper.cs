using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Debris.Simulation.CoupledContacts
{
    // Fixed-topology GPU bridge between physical solve stages and the FIFO
    // transaction. Geometry must describe State's initial poses at construction.
    // All stages record commands only; the session owns submission and fences.
    public sealed class CoupledTickStepper : IDisposable
    {
        readonly CoupledTickState state;
        readonly CoupledTickTransaction transaction;
        readonly CoupledContactOperator op;
        readonly CoupledContactArena arena;
        readonly CoupledVelocityResidual velocity;
        readonly CoupledManifoldPreconditioner preconditioner;
        readonly CoupledArnoldiWorkspace arnoldi;
        readonly CoupledVelocitySolve velocitySolve;
        readonly CoupledPositionCorrection position;
        readonly ComputeShader shader;
        readonly List<GraphicsBuffer> owned=new List<GraphicsBuffer>();
        readonly GraphicsBuffer bindPoses,templatePrimitives,templateGroups,bodies,primitives,groups,
            zeroForces,stageFacts,velocityVerdict,clock,slotGate,radii,predicted,predictionVerdict,positionVerdict;
        readonly int reset,free,clear,geometryBodies,geometryPrimitives,geometryGroups,
            inspectMotion,finishVelocity,applyVelocity,resetRadii,buildRadii,selectSchedule,beginSlot,
            predict,finishPrediction,applyPrediction,inspectPosition,finishPosition,applyPosition;
        readonly int groupCount;
        readonly float duration;
        bool initialized,disposed,selectedSchedule;
        GraphicsBuffer selectedForces;
        int nextSlot;
        public long BufferBytes { get; private set; }
        public GraphicsBuffer VelocityDecision=>velocityVerdict;
        public GraphicsBuffer PredictionDecision=>predictionVerdict;
        public GraphicsBuffer StepClock=>clock;

        public CoupledTickStepper(CoupledTickState state,CoupledTickTransaction transaction,
            CoupledContactOperator op,float substepDuration,float friction=.3f)
        {
            this.state=state??throw new ArgumentNullException(nameof(state));
            this.transaction=transaction??throw new ArgumentNullException(nameof(transaction));
            this.op=op??throw new ArgumentNullException(nameof(op));
            if(transaction.State!=state||op.Arena!=state.Arena||op.BodyCount!=state.EndpointCount||
               op.Manifolds==null||op.NormalOnlyRows||op.SourcePrimitiveCount<1||
               (op.Manifolds.HasUnion&&!op.Manifolds.ContainmentEnabled)||
               float.IsNaN(substepDuration)||float.IsInfinity(substepDuration)||substepDuration<=0)
                throw new ArgumentException("A velocity graph and one fixed-topology transaction are required");
            duration=substepDuration;arena=state.Arena;groupCount=op.Manifolds.UnionGroupCount;
            string owner=arena.NewOwner("TickStepper");
            try
            {
                var asset=Resources.Load<ComputeShader>("CoupledTickStepper");
                if(asset==null)throw new InvalidOperationException("Missing CoupledTickStepper resource");
                shader=UnityEngine.Object.Instantiate(asset);
                bindPoses=Buffer(owner,state.EndpointCount,32);bindPoses.SetData(state.BindPoseEndpoints);
                templatePrimitives=Buffer(owner,op.SourcePrimitiveCount,56);
                templateGroups=Buffer(owner,Math.Max(1,groupCount),32);
                bodies=Buffer(owner,state.EndpointCount,16);
                primitives=Buffer(owner,op.SourcePrimitiveCount,56);
                groups=Buffer(owner,Math.Max(1,groupCount),32);
                zeroForces=Buffer(owner,state.EndpointCount,16);zeroForces.SetData(new Vector4[state.EndpointCount]);
                stageFacts=Buffer(owner,8,4);velocityVerdict=Buffer(owner,4,4);
                clock=Buffer(owner,4,4);slotGate=Buffer(owner,1,4);
                clock.SetData(new uint[]{0,0,0,0});slotGate.SetData(new uint[]{1});
                radii=Buffer(owner,state.EndpointCount,4);predicted=Buffer(owner,state.EndpointCount,32);
                predictionVerdict=Buffer(owner,2,4);positionVerdict=Buffer(owner,2,4);
                velocity=new CoupledVelocityResidual(op,duration,friction);
                preconditioner=new CoupledManifoldPreconditioner(op,velocity);
                arnoldi=new CoupledArnoldiWorkspace(op,velocity,preconditioner);
                velocitySolve=new CoupledVelocitySolve(op,velocity,arnoldi);
                position=new CoupledPositionCorrection(op,velocity,preconditioner,arnoldi);
                BufferBytes+=position.BufferBytes;
                BufferBytes+=velocity.BufferBytes+preconditioner.BufferBytes+arnoldi.BufferBytes+velocitySolve.BufferBytes;
                shader.SetInt("_BodyCount",state.EndpointCount);
                shader.SetInt("_PrimitiveCount",op.SourcePrimitiveCount);shader.SetInt("_GroupCount",groupCount);
                shader.SetInt("_RowCapacity",op.RowCount);shader.SetFloat("_H",duration);
                reset=Kernel("ResetStage");free=Kernel("IntegrateFreeMotion");clear=Kernel("ClearImpulse");
                geometryBodies=Kernel("GeometryBodies");geometryPrimitives=Kernel("GeometryPrimitives");
                geometryGroups=Kernel("GeometryGroups");inspectMotion=Kernel("InspectVelocity");
                finishVelocity=Kernel("FinishVelocity");applyVelocity=Kernel("ApplyVelocity");
                resetRadii=Kernel("ResetRadii");buildRadii=Kernel("BuildRadii");selectSchedule=Kernel("SelectSchedule");
                beginSlot=Kernel("BeginSlot");predict=Kernel("Predict");finishPrediction=Kernel("FinishPrediction");
                applyPrediction=Kernel("ApplyPrediction");
                inspectPosition=Kernel("InspectPosition");finishPosition=Kernel("FinishPosition");applyPosition=Kernel("ApplyPosition");
                Bind(inspectPosition,("_CorrectedBodies",position.WorkingBodies),("_AngularCorrection",position.AngularDisplacement),
                    ("_Predicted",predicted),("_Radii",radii));
                Bind(finishPosition,("_PositionDecision",position.Decision),("_PredictionVerdict",positionVerdict));
                Bind(applyPosition,("_CorrectedBodies",position.WorkingBodies),("_AngularCorrection",position.AngularDisplacement),
                    ("_Predicted",predicted),("_Endpoints",state.WorkingEndpoints));
                Bind(resetRadii,("_Radii",radii));
                Bind(buildRadii,("_Radii",radii),("_BindPoses",bindPoses),("_TemplatePrimitives",templatePrimitives));
                Bind(selectSchedule,("_Radii",radii),("_Endpoints",state.WorkingEndpoints),
                    ("_Parameters",state.WorkingParameters),("_TickControl",transaction.Control));
                Bind(beginSlot,("_TickControl",transaction.Control));
                Bind(predict,("_Endpoints",state.WorkingEndpoints),("_Parameters",state.WorkingParameters),
                    ("_Radii",radii),("_Predicted",predicted));
                Bind(finishPrediction,("_PredictionVerdict",predictionVerdict));
                Bind(applyPrediction,("_Endpoints",state.WorkingEndpoints),("_Predicted",predicted));
                Bind(free,("_Parameters",state.WorkingParameters),("_Endpoints",state.WorkingEndpoints),
                    ("_FreeMotion",op.FreeMotionInput),("_Forces",zeroForces));
                Bind(clear,("_Impulse",op.ProductInput));
                Bind(geometryBodies,("_Endpoints",state.WorkingEndpoints),("_Parameters",state.WorkingParameters),("_Bodies",bodies));
                Bind(geometryPrimitives,("_Endpoints",state.WorkingEndpoints),("_BindPoses",bindPoses),
                    ("_TemplatePrimitives",templatePrimitives),("_Primitives",primitives));
                Bind(geometryGroups,("_Endpoints",state.WorkingEndpoints),("_BindPoses",bindPoses),
                    ("_TemplateGroups",templateGroups),("_Groups",groups));
                Bind(inspectMotion,("_Motion",op.EndpointOutput));
                Bind(finishVelocity,("_SolveControl",velocitySolve.Control),("_Verdict",velocityVerdict),
                    ("_RowFault",op.RowFault),("_BroadFault",op.Manifolds.BroadFaultBuffer),
                    ("_CompactFault",op.Manifolds.CompactFaultBuffer));
                Bind(applyVelocity,("_Endpoints",state.WorkingEndpoints),("_Parameters",state.WorkingParameters),
                    ("_Motion",op.EndpointOutput));
            }
            catch { Dispose();throw; }
        }
        GraphicsBuffer Buffer(string owner,int count,int stride)
        {
            var b=arena.Allocate(owner+"/"+owned.Count,count,stride);owned.Add(b);
            BufferBytes+=(long)count*stride;return b;
        }
        int Kernel(string name)
        {
            int k=shader.FindKernel(name);
            shader.SetBuffer(k,"_Activity",transaction.ActivityGate);shader.SetBuffer(k,"_Facts",stageFacts);
            shader.SetBuffer(k,"_Clock",clock);shader.SetBuffer(k,"_SlotGate",slotGate);
            return k;
        }
        void Bind(int kernel,params (string Name,GraphicsBuffer Buffer)[] entries)
        {foreach(var e in entries)shader.SetBuffer(kernel,e.Name,e.Buffer);}
        static int Groups(int count)=>Math.Max(1,(count+63)/64);
        void Check(CommandBuffer target)
        {
            if(disposed)throw new ObjectDisposedException(nameof(CoupledTickStepper));
            transaction.CheckRecording(target);
        }
        void RecordTemplates(CommandBuffer target)
        {
            if(!initialized)
            {
                target.CopyBuffer(op.SourcePrimitives,templatePrimitives);
                if(groupCount>0)target.CopyBuffer(op.Manifolds.UnionGroupsBuffer,templateGroups);
                initialized=true;
            }
        }
        void RecordGeometry(CommandBuffer target)
        {
            RecordTemplates(target);
            target.DispatchCompute(shader,geometryBodies,Groups(state.EndpointCount),1,1);
            target.DispatchCompute(shader,geometryPrimitives,Groups(op.SourcePrimitiveCount),1,1);
            if(groupCount>0)target.DispatchCompute(shader,geometryGroups,Groups(groupCount),1,1);
            op.RecordRowMode(target,false);
            op.RecordRequeryGraph(target,primitives,bodies,groupCount>0?groups:null);
        }

        // Inputs are force/torque (xy/z), not velocity increments. The final
        // projected residual product already contains uFree + W J^T lambda.
        // Cold-start every substep; provisional cache reuse is optional.
        public void RecordVelocity(CommandBuffer target,CoupledSolveProfile profile,GraphicsBuffer forces=null)
        {
            Check(target);
            if(!selectedSchedule||nextSlot==0)throw new InvalidOperationException("Begin a selected tick and substep first");
            if(forces!=selectedForces)throw new ArgumentException("Use the same force buffer used for schedule selection");
            if(profile<CoupledSolveProfile.C1||profile>CoupledSolveProfile.C3)
                throw new ArgumentOutOfRangeException(nameof(profile));
            if(forces!=null&&(forces.stride!=16||forces.count!=state.EndpointCount))
                throw new ArgumentException("One force/torque vector per endpoint is required",nameof(forces));
            target.DispatchCompute(shader,reset,1,1,1);
            target.SetComputeBufferParam(shader,free,"_Forces",forces??zeroForces);
            target.DispatchCompute(shader,free,Groups(state.EndpointCount),1,1);
            target.DispatchCompute(shader,clear,Groups(op.RowCount),1,1);
            RecordGeometry(target);
            velocity.RecordSystemMode(target,false);
            velocity.RecordStepClock(target,clock);
            velocitySolve.Record(target,profile,slotGate);
            target.DispatchCompute(shader,inspectMotion,Groups(state.EndpointCount),1,1);
            target.DispatchCompute(shader,finishVelocity,1,1,1);
            transaction.RecordVelocityResult(target,velocityVerdict,slotGate);
            target.DispatchCompute(shader,applyVelocity,Groups(state.EndpointCount),1,1);
        }
        // Select h and the 4..16 active slots entirely on GPU, including
        // maximum submitted acceleration. More than 16 rejects, never clamps.
        public CoupledTickTicket RecordBegin(CommandBuffer target,ulong tick,GraphicsBuffer forces=null)
        {
            if(disposed)throw new ObjectDisposedException(nameof(CoupledTickStepper));
            if(forces!=null&&(forces.stride!=16||forces.count!=state.EndpointCount))
                throw new ArgumentException("One force/torque vector per endpoint is required",nameof(forces));
            var ticket=transaction.RecordBeginSelected(target,tick);
            RecordTemplates(target);
            target.DispatchCompute(shader,resetRadii,Groups(state.EndpointCount),1,1);
            target.DispatchCompute(shader,buildRadii,Groups(op.SourcePrimitiveCount),1,1);
            target.SetComputeBufferParam(shader,selectSchedule,"_Forces",forces??zeroForces);
            target.DispatchCompute(shader,selectSchedule,1,1,1);
            selectedSchedule=true;selectedForces=forces;nextSlot=0;
            return ticket;
        }
        public void RecordBeginSubstep(CommandBuffer target,int slot)
        {
            Check(target);
            if(!selectedSchedule||slot!=nextSlot||slot<0||slot>=16)throw new ArgumentOutOfRangeException(nameof(slot));
            target.SetComputeIntParam(shader,"_Slot",slot);
            target.DispatchCompute(shader,beginSlot,1,1,1);
            nextSlot++;
        }
        public void RecordPrediction(CommandBuffer target)
        {
            Check(target);
            target.DispatchCompute(shader,reset,1,1,1);
            target.DispatchCompute(shader,predict,Groups(state.EndpointCount),1,1);
            target.DispatchCompute(shader,finishPrediction,1,1,1);
            transaction.RecordPredictionResult(target,predictionVerdict,slotGate);
            target.DispatchCompute(shader,applyPrediction,Groups(state.EndpointCount),1,1);
        }

        public void RecordPosition(CommandBuffer target,CoupledSolveProfile profile)
        {
            Check(target);
            target.DispatchCompute(shader,reset,1,1,1);
            RecordGeometry(target);
            position.Record(target,profile,slotGate);
            target.DispatchCompute(shader,inspectPosition,Groups(state.EndpointCount),1,1);
            target.DispatchCompute(shader,finishPosition,1,1,1);
            transaction.RecordPositionResult(target,positionVerdict,slotGate);
            target.DispatchCompute(shader,applyPosition,Groups(state.EndpointCount),1,1);
            transaction.RecordSubstep(target,slotGate);
        }

        public void Dispose()
        {
            if(disposed)return;disposed=true;
            position?.Dispose();velocitySolve?.Dispose();arnoldi?.Dispose();preconditioner?.Dispose();velocity?.Dispose();
            foreach(var b in owned)arena.Release(b);
            if(shader!=null)
            {if(Application.isPlaying)UnityEngine.Object.Destroy(shader);else UnityEngine.Object.DestroyImmediate(shader);}
        }
    }
}
