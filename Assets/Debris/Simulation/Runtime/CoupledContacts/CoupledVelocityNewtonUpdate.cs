using System;
using UnityEngine.Rendering;

namespace Debris.Simulation.CoupledContacts
{
    // Records one bounded velocity Newton update on a frozen V2 contact graph.
    // All convergence, restart, retry and projection choices remain on GPU.
    // The caller must inspect the final GPU status before accepting a tick.
    public sealed class CoupledVelocityNewtonUpdate
    {
        readonly CoupledContactOperator op;
        readonly CoupledVelocityResidual velocity;
        readonly CoupledArnoldiWorkspace arnoldi;

        public CoupledVelocityNewtonUpdate(CoupledContactOperator op,
            CoupledVelocityResidual velocity,CoupledArnoldiWorkspace arnoldi)
        {
            this.op=op??throw new ArgumentNullException(nameof(op));
            this.velocity=velocity??throw new ArgumentNullException(nameof(velocity));
            this.arnoldi=arnoldi??throw new ArgumentNullException(nameof(arnoldi));
            if(velocity.Operator!=op||arnoldi.Operator!=op||
               arnoldi.Velocity!=velocity)
                throw new ArgumentException("A Newton update needs one frozen graph");
        }

        // C1/C2/C3 caps include every Arnoldi and true-check product across
        // two search-only retries. Recording a masked slot still has driver
        // cost; WorkControl counts only products that actually execute.
        public void Record(CommandBuffer target,int krylovCap,
            UnityEngine.GraphicsBuffer activityGate=null)
        {
            if(target==null)throw new ArgumentNullException(nameof(target));
            if(krylovCap!=16&&krylovCap!=24&&krylovCap!=32)
                throw new ArgumentOutOfRangeException(nameof(krylovCap));
            if(activityGate==null)velocity.RecordResidual(target);
            else velocity.RecordResidualMasked(target,activityGate);
            arnoldi.RecordInitialize(target,0,krylovCap,activityGate);
            RecordCycles(target,krylovCap);
            RecordCandidate(target,activityGate);
            arnoldi.RecordRetry(target,1e-5f);
            RecordCycles(target,krylovCap);
            RecordCandidate(target,activityGate);
            arnoldi.RecordRetry(target,1e-3f);
            RecordCycles(target,krylovCap);
            RecordCandidate(target,activityGate);
        }

        void RecordCycles(CommandBuffer target,int krylovCap)
        {
            int cycles=krylovCap==16?1:2;
            for(int cycle=0;cycle<cycles;cycle++)
            {
                if(cycle!=0)arnoldi.RecordRestart(target);
                for(int column=0;column<CoupledArnoldiWorkspace.RestartLength;
                    column++)
                    arnoldi.RecordStep(target,column);
                arnoldi.RecordDirectionAndVerify(target);
            }
        }

        void RecordCandidate(CommandBuffer target,UnityEngine.GraphicsBuffer activityGate)
        {
            velocity.RecordLineSearch(target,arnoldi.AcceptedDirectionGate);
            velocity.RecordProjectionIfPhysical(target,activityGate);
            velocity.RecordProjectedResidualIfPhysical(target);
        }
    }
}
