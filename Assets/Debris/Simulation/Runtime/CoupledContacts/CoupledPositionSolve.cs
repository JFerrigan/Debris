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
        public long BufferBytes=>residual.BufferBytes+preconditioner.BufferBytes+
            arnoldi.BufferBytes+solve.BufferBytes;

        public CoupledPositionSolve(CoupledContactOperator op)
        {
            this.op=op??throw new ArgumentNullException(nameof(op));
            if(!op.NormalOnlyRows)
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

        // Call after this operator's graph (or refreshed graph) is recorded.
        // Reset p even on repeated calls; never carry a multiplier into newly
        // rotated normals. C1/C2/C3 all allow four position Newton updates,
        // with 16/24/32 counted Krylov products per update respectively.
        public void Record(CommandBuffer target,CoupledSolveProfile profile)
        {
            if(disposed)throw new ObjectDisposedException(nameof(CoupledPositionSolve));
            if(target==null)throw new ArgumentNullException(nameof(target));
            if(profile!=CoupledSolveProfile.C1&&profile!=CoupledSolveProfile.C2&&
               profile!=CoupledSolveProfile.C3)
                throw new ArgumentOutOfRangeException(nameof(profile));
            residual.RecordPositionInitialize(target);
            solve.Record(target,profile);
        }

        public void Dispose()
        {
            if(disposed)return;
            disposed=true;
            solve?.Dispose();arnoldi?.Dispose();preconditioner?.Dispose();residual?.Dispose();
        }
    }
}
