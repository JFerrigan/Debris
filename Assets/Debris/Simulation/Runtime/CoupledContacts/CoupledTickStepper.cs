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
        readonly ComputeShader shader;
        readonly List<GraphicsBuffer> owned=new List<GraphicsBuffer>();
        readonly GraphicsBuffer bindPoses,templatePrimitives,templateGroups,bodies,primitives,groups,
            zeroForces,stageFacts,velocityVerdict;
        readonly int reset,free,clear,geometryBodies,geometryPrimitives,geometryGroups,
            inspectMotion,finishVelocity,applyVelocity;
        readonly int groupCount;
        readonly float duration;
        bool initialized,disposed;
        public long BufferBytes { get; private set; }
        public GraphicsBuffer VelocityDecision=>velocityVerdict;

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
                velocity=new CoupledVelocityResidual(op,duration,friction);
                preconditioner=new CoupledManifoldPreconditioner(op,velocity);
                arnoldi=new CoupledArnoldiWorkspace(op,velocity,preconditioner);
                velocitySolve=new CoupledVelocitySolve(op,velocity,arnoldi);
                BufferBytes+=velocity.BufferBytes+preconditioner.BufferBytes+arnoldi.BufferBytes+velocitySolve.BufferBytes;
                shader.SetInt("_BodyCount",state.EndpointCount);
                shader.SetInt("_PrimitiveCount",op.SourcePrimitiveCount);shader.SetInt("_GroupCount",groupCount);
                shader.SetInt("_RowCapacity",op.RowCount);shader.SetFloat("_H",duration);
                reset=Kernel("ResetStage");free=Kernel("IntegrateFreeMotion");clear=Kernel("ClearImpulse");
                geometryBodies=Kernel("GeometryBodies");geometryPrimitives=Kernel("GeometryPrimitives");
                geometryGroups=Kernel("GeometryGroups");inspectMotion=Kernel("InspectVelocity");
                finishVelocity=Kernel("FinishVelocity");applyVelocity=Kernel("ApplyVelocity");
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
        void RecordGeometry(CommandBuffer target)
        {
            if(!initialized)
            {
                target.CopyBuffer(op.SourcePrimitives,templatePrimitives);
                if(groupCount>0)target.CopyBuffer(op.Manifolds.UnionGroupsBuffer,templateGroups);
                initialized=true;
            }
            target.DispatchCompute(shader,geometryBodies,Groups(state.EndpointCount),1,1);
            target.DispatchCompute(shader,geometryPrimitives,Groups(op.SourcePrimitiveCount),1,1);
            if(groupCount>0)target.DispatchCompute(shader,geometryGroups,Groups(groupCount),1,1);
            op.RecordRequeryGraph(target,primitives,bodies,groupCount>0?groups:null);
        }

        // Inputs are force/torque (xy/z), not velocity increments. The final
        // projected residual product already contains uFree + W J^T lambda.
        // Cold-start every substep; provisional cache reuse is optional.
        public void RecordVelocity(CommandBuffer target,CoupledSolveProfile profile,GraphicsBuffer forces=null)
        {
            Check(target);
            if(profile<CoupledSolveProfile.C1||profile>CoupledSolveProfile.C3)
                throw new ArgumentOutOfRangeException(nameof(profile));
            if(forces!=null&&(forces.stride!=16||forces.count!=state.EndpointCount))
                throw new ArgumentException("One force/torque vector per endpoint is required",nameof(forces));
            target.DispatchCompute(shader,reset,1,1,1);
            target.SetComputeBufferParam(shader,free,"_Forces",forces??zeroForces);
            target.DispatchCompute(shader,free,Groups(state.EndpointCount),1,1);
            target.DispatchCompute(shader,clear,Groups(op.RowCount),1,1);
            RecordGeometry(target);
            velocitySolve.Record(target,profile,transaction.ActivityGate);
            target.DispatchCompute(shader,inspectMotion,Groups(state.EndpointCount),1,1);
            target.DispatchCompute(shader,finishVelocity,1,1,1);
            transaction.RecordVelocityResult(target,velocityVerdict);
            target.DispatchCompute(shader,applyVelocity,Groups(state.EndpointCount),1,1);
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            velocitySolve?.Dispose();arnoldi?.Dispose();preconditioner?.Dispose();velocity?.Dispose();
            foreach(var b in owned)arena.Release(b);
            if(shader!=null)
            {if(Application.isPlaying)UnityEngine.Object.Destroy(shader);else UnityEngine.Object.DestroyImmediate(shader);}
        }
    }
}
