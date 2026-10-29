using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Debris.Simulation.CoupledContacts
{
    // One fresh frozen normal-only minimum-displacement problem. This owns
    // search scratch, never physical pose/velocity or committed session state.
    // Actual-pose line search and independent final geometry remain mandatory
    // before the caller applies Displacement to a working pose.
    public sealed class CoupledPositionSolve : IDisposable
    {
        readonly CoupledContactOperator op;
        readonly CoupledVelocityResidual residual;
        readonly CoupledManifoldPreconditioner preconditioner;
        readonly CoupledArnoldiWorkspace arnoldi;
        readonly CoupledVelocitySolve solve;
        bool disposed;
        readonly bool ownsWorkspace=true;

        // Same control layout as CoupledVelocitySolve: accepted is word 1,
        // fault is word 3. Acceptance here covers the frozen equations only.
        internal CoupledContactOperator Operator=>op;
        public GraphicsBuffer Control=>solve.Control;
        public GraphicsBuffer Facts=>residual.Facts;
        public GraphicsBuffer Multipliers=>residual.ImpulseInput;
        // xy translation and z angular displacement. Valid only if accepted.
        // The final projected residual product already reconstructed it, so
        // exposing this buffer adds no uncounted operator product.
        public GraphicsBuffer Displacement=>op.EndpointOutput;
        public long BufferBytes=>(ownsWorkspace?residual.BufferBytes+preconditioner.BufferBytes+
            arnoldi.BufferBytes:0)+solve.BufferBytes;

        public CoupledPositionSolve(CoupledContactOperator op)
        {
            this.op=op??throw new ArgumentNullException(nameof(op));
            if(op.Manifolds==null)
                throw new ArgumentException("Position solve requires normal-only geometry",nameof(op));
            try
            {
                residual=new CoupledVelocityResidual(op,1,0,1e-5f,true);
                preconditioner=new CoupledManifoldPreconditioner(op,residual);
                arnoldi=new CoupledArnoldiWorkspace(op,residual,preconditioner);
                solve=new CoupledVelocitySolve(op,residual,arnoldi);
            }
            catch { Dispose();throw; }
        }

        // Velocity and position do not overlap on the ordered queue. Reuse
        // the same residual/factor/Krylov storage rather than a second basis.
        internal CoupledPositionSolve(CoupledContactOperator op,CoupledVelocityResidual residual,
            CoupledManifoldPreconditioner preconditioner,CoupledArnoldiWorkspace arnoldi)
        {
            this.op=op;this.residual=residual;this.preconditioner=preconditioner;this.arnoldi=arnoldi;
            ownsWorkspace=false;
            if(op.Manifolds==null||residual.Operator!=op||preconditioner.Velocity!=residual||arnoldi.Velocity!=residual)
                throw new ArgumentException("Position needs the same generated graph and reusable workspace");
            solve=new CoupledVelocitySolve(op,residual,arnoldi,true);
        }

        // Call after this operator's graph (or refreshed graph) is recorded.
        // Reset p even on repeated calls; never carry a multiplier into newly
        // rotated normals. C1/C2/C3 all allow four position Newton updates,
        // with 16/24/32 counted Krylov products per update respectively.
        public void Record(CommandBuffer target,CoupledSolveProfile profile,
            GraphicsBuffer activityGate=null)
        {
            if(disposed)throw new ObjectDisposedException(nameof(CoupledPositionSolve));
            if(target==null)throw new ArgumentNullException(nameof(target));
            if(profile!=CoupledSolveProfile.C1&&profile!=CoupledSolveProfile.C2&&
               profile!=CoupledSolveProfile.C3)
                throw new ArgumentOutOfRangeException(nameof(profile));
            if(activityGate!=null&&(activityGate.stride!=4||activityGate.count<1))
                throw new ArgumentException("A GPU uint activity gate is required",nameof(activityGate));
            residual.RecordSystemMode(target,true);
            residual.RecordPositionInitialize(target,activityGate);
            solve.Record(target,profile,activityGate);
        }

        public void Dispose()
        {
            if(disposed)return;
            disposed=true;
            solve?.Dispose();
            if(ownsWorkspace){arnoldi?.Dispose();preconditioner?.Dispose();residual?.Dispose();}
        }
    }
}
