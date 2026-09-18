using System;
using System.Globalization;
using System.IO;
using Debris.Simulation.ParallelProof;
using Ref = Debris.Simulation.Tests.ParallelContactReference;

namespace Debris.Simulation.Tests
{
    public static class ParallelTraceReport
    {
        static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

        public static void WriteSelectedWall(string path, ProofTraceReadback trace)
        {
            ProofTraceContact selected = default;
            double worst = -1;
            foreach (var checkpoint in trace.Checkpoints)
            {
                if (checkpoint.Stage != ProofTraceStage.Validation) continue;
                for (uint i = 0; i < checkpoint.CapturedContactCount; i++)
                {
                    var contact = trace.Contacts[checkpoint.ContactOffset + i];
                    if (contact.Feature == uint.MaxValue || -contact.Separation <= worst) continue;
                    selected = contact; worst = -contact.Separation;
                }
            }
            if (worst < 0) return;
            using (var writer = new StreamWriter(path))
            {
                writer.WriteLine("tick,substep,stage,iteration,grain_identity,body_endpoint,feature,reference_wall_penetration,gpu_max_solid_penetration,degree_a,degree_b,k_split,position_lambda,normal_increment,wall_grain_dx,net_grain_dx,net_body_dx");
                for (int index = 0; index < trace.Checkpoints.Length; index++)
                {
                    var checkpoint = trace.Checkpoints[index];
                    int start = (int)checkpoint.StateOffset;
                    var a = trace.States[start + (int)selected.A]; var b = trace.States[start + (int)selected.B];
                    var patch = trace.Boundaries[selected.Feature]; var bp = trace.Parameters[selected.B];
                    var centerB = new Ref.Double2(b.Center.x, b.Center.y) + Ref.Double2.Rotate(
                        new Ref.Double2(patch.Center.x - (double)bp.LocalCOM.x, patch.Center.y - (double)bp.LocalCOM.y), b.Angle);
                    var geometry = Ref.OBBContacts(new Ref.ReferenceObb(new Ref.Double2(a.Center.x, a.Center.y), new Ref.Double2(.5, .5), a.Angle),
                        new Ref.ReferenceObb(centerB, new Ref.Double2(patch.HalfSize.x, patch.HalfSize.y), b.Angle));
                    bool found = false; ProofTraceContact current = default;
                    for (uint i = 0; i < checkpoint.CapturedContactCount; i++)
                    {
                        var c = trace.Contacts[checkpoint.ContactOffset + i];
                        if (c.A == selected.A && c.B == selected.B && c.Feature == selected.Feature) { current = c; found = true; break; }
                    }
                    string lambda = found ? Number(current.PositionLambda) : "";
                    string k = found ? Number(current.EffectiveMassA + (double)current.EffectiveMassB) : "";
                    string increment = "", ownDx = "", netA = "", netB = "";
                    if (found && checkpoint.Stage == ProofTraceStage.PositionEvaluated)
                    {
                        increment = Number(current.IncrementalImpulse.x * (double)current.Normal.x + current.IncrementalImpulse.y * (double)current.Normal.y);
                        ownDx = Number(-trace.Parameters[selected.A].InverseMass * (double)current.IncrementalImpulse.x);
                        foreach (var applied in trace.Checkpoints)
                        {
                            if (applied.Substep != checkpoint.Substep || applied.Stage != ProofTraceStage.PositionApplied || applied.Iteration != checkpoint.Iteration) continue;
                            netA = Number(trace.States[applied.StateOffset + selected.A].Center.x - (double)a.Center.x);
                            netB = Number(trace.States[applied.StateOffset + selected.B].Center.x - (double)b.Center.x);
                            break;
                        }
                    }
                    writer.WriteLine(string.Join(",", checkpoint.Tick, checkpoint.Substep, checkpoint.Stage,
                        checkpoint.Iteration == uint.MaxValue ? "-1" : checkpoint.Iteration.ToString(CultureInfo.InvariantCulture),
                        selected.IdentityA, selected.B, patch.Feature, Number(Math.Max(0, -geometry.Contact0.Separation)),
                        Number(trace.Summaries[index].MaximumSolidPenetration), a.Degree, b.Degree, k, lambda, increment, ownDx, netA, netB));
                }
            }
        }
    }
}
