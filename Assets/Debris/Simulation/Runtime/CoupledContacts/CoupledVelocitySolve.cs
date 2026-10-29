using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Debris.Simulation.CoupledContacts
{
    public enum CoupledSolveProfile { C1,C2,C3 }

    // Bounded velocity Newton schedule for one frozen contact graph. Its GPU
    // control word masks all later updates after acceptance or a fault.
    // Position correction and whole-tick publication remain separate stages.
    public sealed class CoupledVelocitySolve : IDisposable
    {
        readonly CoupledContactArena arena;
        readonly ComputeShader shader;
        readonly CoupledVelocityNewtonUpdate update;
        readonly bool positionSystem;
        readonly GraphicsBuffer control;
        readonly int begin,advance;
        bool disposed;
        public GraphicsBuffer Control=>control;
        public long BufferBytes=>32;

        public CoupledVelocitySolve(CoupledContactOperator op,
            CoupledVelocityResidual velocity,CoupledArnoldiWorkspace arnoldi,bool? positionMode=null)
        {
            if(op==null)throw new ArgumentNullException(nameof(op));
            if(velocity==null)throw new ArgumentNullException(nameof(velocity));
            if(arnoldi==null)throw new ArgumentNullException(nameof(arnoldi));
            update=new CoupledVelocityNewtonUpdate(op,velocity,arnoldi);
            positionSystem=positionMode??velocity.PositionSystem;
            arena=op.Arena;
            string owner=arena.NewOwner("VelocitySolveControl");
            try
            {
                var asset=Resources.Load<ComputeShader>(
                    "CoupledVelocitySolveControl");
                if(asset==null)throw new InvalidOperationException(
                    "Missing CoupledVelocitySolveControl compute resource");
                shader=UnityEngine.Object.Instantiate(asset);
                control=arena.Allocate(owner+"/0",8,4);
                begin=shader.FindKernel("BeginVelocitySolve");
                advance=shader.FindKernel("AdvanceVelocitySolve");
                shader.SetBuffer(begin,"_Control",control);
                shader.SetBuffer(begin,"_InitialGate",control);
                shader.SetBuffer(advance,"_Control",control);
                shader.SetBuffer(advance,"_ProjectionStatus",
                    velocity.ProjectionStatus);
                shader.SetBuffer(advance,"_TrialSelection",
                    velocity.TrialSelection);
                shader.SetBuffer(advance,"_WorkControl",arnoldi.WorkControl);
                shader.SetBuffer(advance,"_Facts",velocity.Facts);
            }
            catch { Dispose();throw; }
        }

        public void Record(CommandBuffer target,CoupledSolveProfile profile,
            GraphicsBuffer activityGate=null)
        {
            if(disposed)throw new ObjectDisposedException(nameof(CoupledVelocitySolve));
            if(target==null)throw new ArgumentNullException(nameof(target));
            if(target==null)throw new ArgumentNullException(nameof(target));
            if(activityGate!=null&&(activityGate.stride!=4||activityGate.count<1))
                throw new ArgumentException("A GPU uint activity gate is required",nameof(activityGate));
            int maxUpdates,krylovCap;
            switch(profile)
            {
                case CoupledSolveProfile.C1:maxUpdates=4;krylovCap=16;break;
                case CoupledSolveProfile.C2:maxUpdates=6;krylovCap=24;break;
                case CoupledSolveProfile.C3:maxUpdates=8;krylovCap=32;break;
                default:throw new ArgumentOutOfRangeException(nameof(profile));
            }
            // All position profiles allow four Newton updates per fresh geometry.
            if(positionSystem)maxUpdates=4;
            target.SetComputeIntParam(shader,"_UseInitialGate",activityGate==null?0:1);
            target.SetComputeBufferParam(shader,begin,"_InitialGate",activityGate??control);
            target.SetComputeIntParam(shader,"_MaxUpdates",maxUpdates);
            target.DispatchCompute(shader,begin,1,1,1);
            for(int iteration=0;iteration<maxUpdates;iteration++)
            {
                update.Record(target,krylovCap,control);
                target.DispatchCompute(shader,advance,1,1,1);
            }
        }

        public void Dispose()
        {
            if(disposed)return;
            disposed=true;
            if(control!=null)arena.Release(control);
            if(shader!=null)
            {
                if(Application.isPlaying)UnityEngine.Object.Destroy(shader);
                else UnityEngine.Object.DestroyImmediate(shader);
            }
        }
    }
}
