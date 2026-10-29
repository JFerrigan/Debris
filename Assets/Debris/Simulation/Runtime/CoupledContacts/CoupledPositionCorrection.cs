using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Debris.Simulation.CoupledContacts
{
    // Complete bounded position stage for one predicted substep. All outputs
    // remain provisional. Failure restores every pose from the stage snapshot;
    // whole-tick velocity/cache/material publication belongs to the tick owner.
    public sealed class CoupledPositionCorrection : IDisposable
    {
        readonly CoupledContactOperator op;
        readonly CoupledContactArena arena;
        readonly CoupledPositionSolve solve;
        readonly CoupledPositionSearch search;
        readonly CoupledFinalGeometryGate final;
        readonly ComputeShader shader;
        readonly GraphicsBuffer control,decision;
        readonly int begin,advance,finish;
        bool disposed;
        public GraphicsBuffer Control=>control;
        // pass, fault, target reached, geometry attempts, Krylov products,
        // maximum grain/solid penetration float bits, independent fault bits.
        public GraphicsBuffer Decision=>decision;
        public GraphicsBuffer WorkingBodies=>search.WorkingBodies;
        public GraphicsBuffer AngularDisplacement=>search.AngularDisplacement;
        public GraphicsBuffer WorkingPrimitives=>search.WorkingPrimitives;
        public GraphicsBuffer WorkingUnionGroups=>search.WorkingUnionGroups;
        public long BufferBytes=>80+solve.BufferBytes+search.BufferBytes+final.BufferBytes;

        public CoupledPositionCorrection(CoupledContactOperator op):this(op,null,null,null){}

        internal CoupledPositionCorrection(CoupledContactOperator op,CoupledVelocityResidual residual,
            CoupledManifoldPreconditioner preconditioner,CoupledArnoldiWorkspace arnoldi)
        {
            this.op=op??throw new ArgumentNullException(nameof(op));
            if(op.Manifolds==null)
                throw new ArgumentException("Position correction requires generated normal geometry",nameof(op));
            arena=op.Arena;
            string owner=arena.NewOwner("PositionCorrection");
            try
            {
                solve=residual==null?new CoupledPositionSolve(op):new CoupledPositionSolve(op,residual,preconditioner,arnoldi);
                search=new CoupledPositionSearch(op,solve);
                final=new CoupledFinalGeometryGate(op.Manifolds);
                var asset=Resources.Load<ComputeShader>("CoupledPositionCorrection");
                if(asset==null)throw new InvalidOperationException("Missing CoupledPositionCorrection resource");
                shader=UnityEngine.Object.Instantiate(asset);
                control=arena.Allocate(owner+"/control",12,4);
                decision=arena.Allocate(owner+"/decision",8,4);
                begin=shader.FindKernel("BeginCorrection");
                advance=shader.FindKernel("AdvanceCorrection");
                finish=shader.FindKernel("FinishCorrection");
                foreach(int kernel in new[]{begin,advance,finish})shader.SetBuffer(kernel,"_Control",control);
                Bind(begin,("_InitialGate",control),("_Decision",decision),("_RowFault",op.RowFault),
                    ("_BroadFault",op.Manifolds.BroadFaultBuffer),("_CompactFault",op.Manifolds.CompactFaultBuffer));
                Bind(advance,("_SolveControl",solve.Control),("_SearchControl",search.Control));
                Bind(finish,("_FinalDecision",final.Decision),("_Decision",decision));
            }
            catch { Dispose();throw; }
        }
        void Bind(int kernel,params (string Name,GraphicsBuffer Buffer)[] bindings)
        {foreach(var binding in bindings)shader.SetBuffer(kernel,binding.Name,binding.Buffer);}

        // Inputs are this operator's current predicted body/primitive/union
        // poses. No forces, physical velocity, spin or inventories are bound.
        // RecordBeginSubstep takes exactly one correction-envelope snapshot.
        public void Record(CommandBuffer target,CoupledSolveProfile profile,GraphicsBuffer activityGate=null)
        {
            if(disposed)throw new ObjectDisposedException(nameof(CoupledPositionCorrection));
            if(target==null)throw new ArgumentNullException(nameof(target));
            int cap;
            switch(profile)
            {
                case CoupledSolveProfile.C1:cap=2;break;
                case CoupledSolveProfile.C2:cap=3;break;
                case CoupledSolveProfile.C3:cap=4;break;
                default:throw new ArgumentOutOfRangeException(nameof(profile));
            }
            if(activityGate!=null&&(activityGate.stride!=4||activityGate.count<1))
                throw new ArgumentException("Invalid position activity gate");
            op.RecordRowMode(target,true);
            op.RecordGraph(target);
            search.RecordBeginSubstep(target);
            target.SetComputeIntParam(shader,"_GeometryCap",cap);
            target.SetComputeIntParam(shader,"_UseInitialGate",activityGate==null?0:1);
            target.SetComputeBufferParam(shader,begin,"_InitialGate",activityGate??control);
            target.DispatchCompute(shader,begin,1,1,1);
            for(int geometry=0;geometry<cap;geometry++)
            {
                solve.Record(target,profile,control);
                search.Record(target,control);
                target.DispatchCompute(shader,advance,1,1,1);
            }
            final.RecordRequery(target,search.WorkingPrimitives,search.WorkingUnionGroups);
            target.DispatchCompute(shader,finish,1,1,1);
            search.RecordRollback(target,decision);
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            if(decision!=null)arena.Release(decision);
            if(control!=null)arena.Release(control);
            if(shader!=null)
            {if(Application.isPlaying)UnityEngine.Object.Destroy(shader);else UnityEngine.Object.DestroyImmediate(shader);}
            final?.Dispose();search?.Dispose();solve?.Dispose();
        }
    }
}
