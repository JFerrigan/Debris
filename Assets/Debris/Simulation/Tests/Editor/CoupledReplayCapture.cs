using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using Debris.Simulation.ParallelProof;
using ProofGrain = Debris.Simulation.ParallelProof.LooseCell;
using NUnit.Framework;
using UnityEngine;

namespace Debris.Simulation.Tests
{
    internal static class CoupledReplayCapture
    {
        internal static string Root => Directory.GetParent(Application.dataPath).FullName;
        internal static string ReserveRun()
        {
            string parent=Path.Combine(Root,"Logs/b3r-v2-0a"); Directory.CreateDirectory(parent);
            string head=GitHead();
            string path=Path.Combine(parent,DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ")+"-"+head.Substring(0,7)+"-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            using(var ignored=new FileStream(Path.Combine(path,".reserved"),FileMode.CreateNew,FileAccess.Write,FileShare.None)){}
            Directory.CreateDirectory(Path.Combine(path,"inputs")); Directory.CreateDirectory(Path.Combine(path,"outputs"));
            return path;
        }
        static string GitHead()
        {
            string git=File.ReadAllText(Path.Combine(Root,".git/HEAD")).Trim();
            if(git.StartsWith("ref: ")) git=File.ReadAllText(Path.Combine(Root,".git",git.Substring(5))).Trim();
            return git;
        }
        static string SourceHash(string relative) => CoupledReplayArchive.HashFile(Path.Combine(Root,relative));
        static CoupledReplayManifest Manifest(string family,int velocity,int position)
        {
            string fixture=family=="combined" ? "Assets/Debris/Simulation/Runtime/ParallelProof/ProofViabilityFixture.cs" :
                "Assets/Debris/Simulation/Runtime/ParallelProof/ProofDiagnosticFixtures.cs";
            return new CoupledReplayManifest { fixture=family, profile=velocity+"/"+position, sourceHead=GitHead(),
                solverSha256=SourceHash("Assets/Debris/Simulation/Runtime/ParallelProof/ParallelGrainSolver.cs"),
                shaderSha256=SourceHash("Assets/Debris/Simulation/Resources/ParallelGrains.compute"), fixtureSha256=SourceHash(fixture),
                baseFixtureSha256=SourceHash("Assets/Debris/Simulation/Runtime/ParallelProof/ProofFixtures.cs"),
                recordsSha256=SourceHash("Assets/Debris/Simulation/Runtime/ParallelProof/ProofRecords.cs"),
                editorVersion=Application.unityVersion, backend=SystemInfo.graphicsDeviceType.ToString(),runUtc=DateTime.UtcNow.ToString("O"),
                fixtureRevision=family=="combined" ? ProofViabilityFixture.Revision : 0,
                workloadRevision=family=="combined" ? 2 : 0,
                bodyEndpointStart=family=="combined" ? ProofViabilityFixture.GrainCount : 2500,
                bodyOrdinals=Enumerable.Range(0,family=="combined" ? 18 : 1).ToArray() };
        }
        internal static ParallelGrainSolver Create(CoupledReplayInput a)
        {
            var bodies=new BodyState[a.Parameters.Length];
            Array.Copy(a.Endpoints,a.GrainCapacity,bodies,0,bodies.Length);
            return new ParallelGrainSolver(a.Grains,bodies,a.Parameters,a.Boundaries,a.VelocityIterations,a.PositionIterations,
                a.Friction,a.CandidateSlots,a.RigidContactCapacity,null,a.DefaultMasses ? null : a.Masses,a.GrainCapacity,a.BoundaryCapacity);
        }
        internal static IEnumerator Snapshot(ParallelGrainSolver solver, Action<ProofSnapshot> receive)
        {
            var task=solver.SnapshotAsync(); while(!task.IsCompleted)yield return null;
            if(task.IsFaulted || task.IsCanceled) Assert.Fail("GPU snapshot failed: "+task.Exception);
            receive(task.Result);
        }
        internal static void AssertPhysical(ProofSnapshot expected,ProofSnapshot actual,string label)
        {
            Assert.That(actual.Grains.Length,Is.EqualTo(expected.Grains.Length),label+" grain count");
            Assert.That(actual.Endpoints.Length,Is.EqualTo(expected.Endpoints.Length),label+" endpoint count");
            for(int i=0;i<expected.Grains.Length;i++)
            {
                var a=expected.Grains[i]; var b=actual.Grains[i];
                Assert.That(Bits(b.Center.x),Is.EqualTo(Bits(a.Center.x)),label+" grain center.x "+i);
                Assert.That(Bits(b.Center.y),Is.EqualTo(Bits(a.Center.y)),label+" grain center.y "+i);
                Assert.That(Bits(b.Velocity.x),Is.EqualTo(Bits(a.Velocity.x)),label+" grain velocity.x "+i);
                Assert.That(Bits(b.Velocity.y),Is.EqualTo(Bits(a.Velocity.y)),label+" grain velocity.y "+i);
                Assert.That(Bits(b.Angle),Is.EqualTo(Bits(a.Angle)),label+" grain angle "+i);
                Assert.That(Bits(b.AngularVelocity),Is.EqualTo(Bits(a.AngularVelocity)),label+" grain spin "+i);
                Assert.That(b.Material,Is.EqualTo(a.Material)); Assert.That(b.Identity,Is.EqualTo(a.Identity));
                Assert.That(b.Flags,Is.EqualTo(a.Flags)); Assert.That(b.Reserved0,Is.EqualTo(a.Reserved0));
                Assert.That(b.Reserved1,Is.EqualTo(a.Reserved1)); Assert.That(b.Reserved2,Is.EqualTo(a.Reserved2));
            }
            for(int i=0;i<expected.Endpoints.Length;i++)
            {
                var a=expected.Endpoints[i]; var b=actual.Endpoints[i];
                Assert.That(Bits(b.Center.x),Is.EqualTo(Bits(a.Center.x)),label+" endpoint center.x "+i);
                Assert.That(Bits(b.Center.y),Is.EqualTo(Bits(a.Center.y)),label+" endpoint center.y "+i);
                Assert.That(Bits(b.Velocity.x),Is.EqualTo(Bits(a.Velocity.x)),label+" endpoint velocity.x "+i);
                Assert.That(Bits(b.Velocity.y),Is.EqualTo(Bits(a.Velocity.y)),label+" endpoint velocity.y "+i);
                Assert.That(Bits(b.Angle),Is.EqualTo(Bits(a.Angle)),label+" endpoint angle "+i);
                Assert.That(Bits(b.AngularVelocity),Is.EqualTo(Bits(a.AngularVelocity)),label+" endpoint spin "+i);
                Assert.That(b.Reserved0,Is.EqualTo(a.Reserved0)); Assert.That(b.Reserved1,Is.EqualTo(a.Reserved1));
            }
        }
        static int Bits(float x)=>BitConverter.SingleToInt32Bits(x);
        static ProofSnapshot Physical(CoupledReplayInput a) => new ProofSnapshot { Grains=a.Grains,Endpoints=a.Endpoints,Diagnostics=a.Diagnostics };
        internal static CoupledReplayInput Input(ProofFixture fixture,ProofSnapshot prior,int attempt,int velocity,int position)
        {
            return new CoupledReplayInput { Grains=(ProofGrain[])prior.Grains.Clone(),Endpoints=(BodyState[])prior.Endpoints.Clone(),
                Parameters=(BodyParameters[])fixture.Parameters.Clone(),Boundaries=(Boundary[])fixture.Boundaries.Clone(),
                Diagnostics=(uint[])prior.Diagnostics.Clone(),GrainCapacity=fixture.Grains.Length,BoundaryCapacity=fixture.Boundaries.Length,
                VelocityIterations=velocity,PositionIterations=position,CandidateSlots=64,RigidContactCapacity=4096,
                Friction=.3f,Dt=1f/60f,Force=fixture.Force,Torque=fixture.Torque,DefaultMasses=true,
                SourceAttempt=attempt,SourceCommitted=(int)prior.CompletedTick };
        }
        static void WriteReport(string path,string family,int velocity,int position,ProofSnapshot source,ProofSnapshot replay,int sourceAttempt)
        {
            string result="{\n  \"family\": \""+family+"\", \"profile\": \""+velocity+"/"+position+"\",\n"+
                "  \"sourceAttempt\": "+sourceAttempt+", \"sourceCommitted\": "+source.CompletedTick+", \"localReplayAttempt\": 1, \"localReplayCommitted\": "+replay.CompletedTick+",\n"+
                "  \"sourceFault\": \""+source.Fault+"\", \"replayFault\": \""+replay.Fault+"\",\n"+
                "  \"sourceGrainMaximum\": \""+source.GrainPenetration.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"\", \"sourceSolidMaximum\": \""+source.SolidPenetration.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"\",\n"+
                "  \"replayGrainMaximum\": \""+replay.GrainPenetration.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"\", \"replaySolidMaximum\": \""+replay.SolidPenetration.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"\",\n"+
                "  \"sourceDiagnostics\": ["+string.Join(",",source.Diagnostics)+"], \"replayDiagnostics\": ["+string.Join(",",replay.Diagnostics)+"],\n"+
                "  \"sourceRollback\": true, \"replayRollback\": true\n}\n";
            using(var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            using(var writer=new StreamWriter(stream,new UTF8Encoding(false)))writer.Write(result);
        }
        internal static IEnumerator Capture(string run,string family,int velocity,int position,int limit)
        {
            var fixture=family=="combined" ? ProofViabilityFixture.Create() :
                ProofDiagnosticFixtures.Packed(family=="shared",PackedDiagnosticMotion.Combined);
            ProofSnapshot prior=null, rejected=null;
            CoupledReplayInput input=null;
            int attempt=0;
            using(var solver=new ParallelGrainSolver(fixture.Grains,fixture.Bodies,fixture.Parameters,fixture.Boundaries,velocity,position,.3f,64,4096))
            {
                yield return Snapshot(solver,x=>prior=x);
                for(attempt=1;attempt<=limit;attempt++)
                {
                    input=Input(fixture,prior,attempt,velocity,position);
                    solver.Step(input.Force,input.Torque,input.SuctionForce,input.MountedSuction);
                    ProofSnapshot current=null; yield return Snapshot(solver,x=>current=x);
                    if(current.Fault!=SolverFault.None)
                    {
                        AssertPhysical(prior,current,"source rollback");
                        Assert.That(current.CompletedTick,Is.EqualTo(prior.CompletedTick));
                        rejected=current; break;
                    }
                    prior=current;
                }
            }
            Assert.That(rejected,Is.Not.Null,family+" "+velocity+"/"+position+" had no rejection within "+limit+" attempts");
            string name=family+"-"+velocity+"-"+position;
            string archive=Path.Combine(run,"inputs",name);
            CoupledReplayArchive.Write(archive,input,Manifest(family,velocity,position));
            CoupledReplayManifest manifest;
            var loaded=CoupledReplayArchive.Read(archive,out manifest);
            Assert.That(CoupledReplayArchive.Encode(loaded),Is.EqualTo(CoupledReplayArchive.Encode(input)),"archive roundtrip bits");
            ProofSnapshot replay=null, replayInitial=null;
            using(var solver=Create(loaded))
            {
                yield return Snapshot(solver,x=>replayInitial=x);
                AssertPhysical(Physical(loaded),replayInitial,"fresh input");
                solver.Step(loaded.Force,loaded.Torque,loaded.SuctionForce,loaded.MountedSuction);
                yield return Snapshot(solver,x=>replay=x);
            }
            WriteReport(Path.Combine(run,"outputs",name+".json"),family,velocity,position,rejected,replay,attempt);
            Assert.That(replay.Fault,Is.EqualTo(rejected.Fault),"unstable fault classification; both outputs retained");
            Assert.That(replay.Fault,Is.Not.EqualTo(SolverFault.None));
            Assert.That(replay.CompletedTick,Is.EqualTo(0u),"fresh local rollback tick");
            AssertPhysical(replayInitial,replay,"fresh replay rollback");
            Debug.Log("V2_0A "+name+" attempt="+attempt+" committed="+rejected.CompletedTick+" fault="+rejected.Fault+" archive="+archive);
        }
    }
}
