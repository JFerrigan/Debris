using System;
using System.Collections;
using Debris.Simulation.ParallelProof;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Debris.Simulation.Tests
{
    public sealed class CoupledReplayCaptureTests
    {
        static void RequireGpu()
        {
            if(!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback)
                Assert.Ignore("V2-0A capture requires GPU compute and async readback");
        }
        [UnityTest,Timeout(180000)]
        public IEnumerator OrdinaryAcceptedAndRejectedAttemptsReplayFromArchive()
        {
            RequireGpu();
            string run=CoupledReplayCapture.ReserveRun();
            foreach(bool reject in new[]{false,true})
            {
                var fixture=ProofFixtures.IsolatedHeavy();
                if(reject){fixture.Bodies[0].Velocity=Vector2.zero;fixture.Grains[0].Center=fixture.Bodies[0].Center;}
                ProofSnapshot initial=null,source=null,replayInitial=null,replay=null;
                CoupledReplayInput input;
                using(var solver=fixture.Create(4,2,0))
                {
                    yield return CoupledReplayCapture.Snapshot(solver,x=>initial=x);
                    input=CoupledReplayCapture.Input(fixture,initial,1,4,2); input.Friction=0;
                    solver.Step(); yield return CoupledReplayCapture.Snapshot(solver,x=>source=x);
                }
                string path=System.IO.Path.Combine(run,"inputs",reject?"ordinary-reject":"ordinary-accept");
                CoupledReplayArchive.Write(path,input,new CoupledReplayManifest { fixture="ordinary",profile="4/2" });
                CoupledReplayManifest manifest;
                var loaded=CoupledReplayArchive.Read(path,out manifest);
                using(var solver=CoupledReplayCapture.Create(loaded))
                {
                    yield return CoupledReplayCapture.Snapshot(solver,x=>replayInitial=x);
                    CoupledReplayCapture.AssertPhysical(initial,replayInitial,"ordinary initial");
                    solver.Step(loaded.Force,loaded.Torque);
                    yield return CoupledReplayCapture.Snapshot(solver,x=>replay=x);
                }
                Assert.That(replay.Fault,Is.EqualTo(source.Fault));
                Assert.That(replay.CompletedTick,Is.EqualTo(source.CompletedTick));
                CoupledReplayCapture.AssertPhysical(source,replay,"ordinary output");
                if(reject){Assert.That(source.Fault,Is.EqualTo(SolverFault.Envelope));CoupledReplayCapture.AssertPhysical(initial,source,"ordinary rollback");}
                else Assert.That(source.Fault,Is.EqualTo(SolverFault.None));
            }
            Debug.Log("V2_0A_ORDINARY run="+run);
        }

        [Explicit("Nine-case V2-0A GPU capture/replay; run only as a named checkpoint"),UnityTest,Timeout(1800000)]
        public IEnumerator CaptureNineCanonicalRejectedAttempts()
        {
            RequireGpu();
            string run=CoupledReplayCapture.ReserveRun();
            Debug.Log("V2_0A_MATRIX_RUN "+run);
            foreach(string family in new[]{"shared","resting","combined"})
            foreach(var profile in new[]{new[]{4,2},new[]{8,4},new[]{12,6}})
                yield return CoupledReplayCapture.Capture(run,family,profile[0],profile[1],family=="combined"?8:120);
        }
    }
}
