using System;
using System.Collections;
using System.Runtime.InteropServices;
using Debris.Simulation.ParallelProof;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Grain=Debris.Simulation.ParallelProof.LooseCell;
namespace Debris.Simulation.Tests
{
    public sealed class ParallelGrainTests
    {
        [Test] public void CandidateLayoutsMatchShader()
        {
            Assert.That(Marshal.SizeOf<Grain>(),Is.EqualTo(48));Assert.That(Marshal.OffsetOf<Grain>(nameof(Grain.Material)).ToInt32(),Is.EqualTo(24));
            Assert.That(Marshal.SizeOf<BodyState>(),Is.EqualTo(32));Assert.That(Marshal.SizeOf<BodyParameters>(),Is.EqualTo(32));Assert.That(Marshal.SizeOf<Boundary>(),Is.EqualTo(32));
        }
        [UnityTest,Timeout(120000)] public IEnumerator TwoGrainsExchangeMomentumWithoutCorrectionEnergy()
        {
            var grains=new[]{new Grain{Center=Vector2.zero,Velocity=Vector2.right*10,Material=1,Identity=1},new Grain{Center=Vector2.right,Material=1,Identity=2,Flags=1}};
            using(var solver=new ParallelGrainSolver(grains,Array.Empty<BodyState>(),Array.Empty<BodyParameters>(),Array.Empty<Boundary>(),4,2,0))
            {
                solver.Step();var task=solver.SnapshotAsync();while(!task.IsCompleted)yield return null;var s=task.Result;
                Assert.That(s.Fault,Is.EqualTo(SolverFault.None));Assert.That(s.CompletedTick,Is.EqualTo(1));
                foreach(var grain in s.Grains){Assert.That(grain.Velocity.x,Is.EqualTo(5).Within(.0001));Assert.That(grain.AngularVelocity,Is.EqualTo(0).Within(.0001));}
                Assert.That(s.Grains[0].Center.x,Is.EqualTo(5f/60).Within(.0001));Assert.That(s.GrainPenetration,Is.LessThanOrEqualTo(.002));
            }
        }
        [UnityTest,Timeout(120000)] public IEnumerator HeavyBodyAnalyticalSpeed()
        {
            using(var solver=ProofFixtures.IsolatedHeavy().Create(4,2,0))
            {
                solver.Step();var task=solver.SnapshotAsync();while(!task.IsCompleted)yield return null;var s=task.Result;
                Assert.That(s.Fault,Is.EqualTo(SolverFault.None));Assert.That(s.Grains[0].Velocity.x,Is.EqualTo(100000f/10001).Within(.0001));
                Assert.That(s.Endpoints[1].Velocity.x,Is.EqualTo(100000f/10001).Within(.0001));
            }
        }
        [UnityTest,Timeout(120000)] public IEnumerator ExcessiveSpeedRejectsWholeTickExactly()
        {
            var f=ProofFixtures.IsolatedHeavy();f.Grains[0].Velocity=Vector2.right*121;
            using(var solver=f.Create(4,2,0))
            {
                solver.Step(Vector2.up*1000,1000);var task=solver.SnapshotAsync();while(!task.IsCompleted)yield return null;var s=task.Result;
                Assert.That(s.Fault,Is.EqualTo(SolverFault.Speed));Assert.That(s.CompletedTick,Is.Zero);
                Assert.That(s.Grains[0].Center,Is.EqualTo(f.Grains[0].Center));Assert.That(s.Grains[0].Velocity,Is.EqualTo(f.Grains[0].Velocity));
                Assert.That(s.Endpoints[1].Center,Is.EqualTo(f.Bodies[0].Center));Assert.That(s.Endpoints[1].Velocity,Is.EqualTo(f.Bodies[0].Velocity));
            }
        }
        [UnityTest,Timeout(120000)] public IEnumerator AnchoredWallStops120CellSpeedWithoutTunnelling()
        {
            var f=ProofFixtures.IsolatedHeavy();f.Grains[0].Center=new Vector2(-1.25f,0);f.Grains[0].Velocity=new Vector2(120,3);
            // Surface-speed selection uses vector speed; use pure normal 120 for the exact 16-substep budget.
            f.Grains[0].Velocity=new Vector2(120,0);f.Bodies[0]=new BodyState();f.Parameters[0].InverseMass=0;f.Parameters[0].InverseInertia=0;f.Parameters[0].Mobility=0;
            f.Boundaries[0].HalfSize=new Vector2(.5f,10);
            using(var solver=f.Create(4,2,0))
            {
                solver.Step();var task=solver.SnapshotAsync();while(!task.IsCompleted)yield return null;var s=task.Result;
                Assert.That(s.Fault,Is.EqualTo(SolverFault.None));Assert.That(s.Diagnostics[2],Is.EqualTo(16));
                Assert.That(s.Grains[0].Center.x,Is.LessThanOrEqualTo(-.999f));Assert.That(s.Grains[0].Velocity.x,Is.EqualTo(0).Within(.0001));
                Assert.That(s.Endpoints[1].Center,Is.EqualTo(Vector2.zero));
            }
        }
        [UnityTest,Timeout(120000)] public IEnumerator OffCentreFragmentContactConservesMomentumAndEnergy()
        {
            var f=ProofFixtures.IsolatedHeavy();f.Grains[0].Center=new Vector2(-1,1.5f);f.Grains[0].Velocity=Vector2.right;
            f.Bodies[0]=new BodyState();f.Parameters[0].InverseMass=.25f;f.Parameters[0].InverseInertia=3f/17;f.Boundaries[0].HalfSize=new Vector2(.5f,2);
            using(var solver=f.Create(8,4,0))
            {
                solver.Step();var task=solver.SnapshotAsync();while(!task.IsCompleted)yield return null;var s=task.Result;var g=s.Grains[0];var b=s.Endpoints[1];
                Assert.That(s.Fault,Is.EqualTo(SolverFault.None));Assert.That(b.AngularVelocity,Is.LessThan(0));
                Assert.That(Vector2.Distance(g.Velocity+4*b.Velocity,Vector2.right),Is.LessThan(.0001));
                double energy=.5*g.Velocity.sqrMagnitude+g.AngularVelocity*g.AngularVelocity/12.0+2*b.Velocity.sqrMagnitude+17.0/6*b.AngularVelocity*b.AngularVelocity;
                Assert.That(energy,Is.LessThanOrEqualTo(.5001));
                double angular=g.Center.x*g.Velocity.y-g.Center.y*g.Velocity.x+g.AngularVelocity/6.0+4*(b.Center.x*b.Velocity.y-b.Center.y*b.Velocity.x)+17.0/3*b.AngularVelocity;
                Assert.That(angular,Is.EqualTo(-1.5).Within(.0015));
            }
        }
        [UnityTest,Timeout(120000)] public IEnumerator FreeSquareRetainsIndependentWorldSpin()
        {
            var f=ProofFixtures.Packed(true);var g=new Grain{Center=Vector2.zero,Velocity=Vector2.right,Angle=.3f,AngularVelocity=.2f,Material=1,Identity=1};
            for(int i=0;i<f.Boundaries.Length;i++)f.Boundaries[i].Body=1;
            using(var solver=new ParallelGrainSolver(new[]{g},f.Bodies,f.Parameters,f.Boundaries,4,2,0))
            {
                solver.Step();var task=solver.SnapshotAsync();while(!task.IsCompleted)yield return null;var s=task.Result;
                Assert.That(s.Fault,Is.EqualTo(SolverFault.None));Assert.That(s.Grains[0].Velocity,Is.EqualTo(g.Velocity));
                Assert.That(s.Grains[0].AngularVelocity,Is.EqualTo(g.AngularVelocity));Assert.That(s.Grains[0].Angle,Is.EqualTo(.3f+.2f/60).Within(.000001));
            }
        }
        [UnityTest,Timeout(120000)] public IEnumerator PackedLoadFailurePreservesLastCommittedState()
        {
            var f=ProofFixtures.Packed(false);
            using(var solver=f.Create(4,2))
            {
                ProofSnapshot prior=null;bool rejected=false;
                var task=solver.SnapshotAsync();while(!task.IsCompleted)yield return null;prior=task.Result;
                for(int tick=0;tick<120;tick++)
                {
                    solver.Step(f.Force,f.Torque);task=solver.SnapshotAsync();while(!task.IsCompleted)yield return null;var s=task.Result;
                    if(s.Fault!=SolverFault.None)
                    {
                        Assert.That(s.CompletedTick,Is.EqualTo(prior.CompletedTick));
                        for(int i=0;i<s.Grains.Length;i++){
                            Assert.That(s.Grains[i].Center,Is.EqualTo(prior.Grains[i].Center));Assert.That(s.Grains[i].Velocity,Is.EqualTo(prior.Grains[i].Velocity));
                            Assert.That(s.Grains[i].Angle,Is.EqualTo(prior.Grains[i].Angle));Assert.That(s.Grains[i].AngularVelocity,Is.EqualTo(prior.Grains[i].AngularVelocity));
                        }
                        Assert.That(s.Endpoints[2500].Center,Is.EqualTo(prior.Endpoints[2500].Center));Assert.That(s.Endpoints[2500].Velocity,Is.EqualTo(prior.Endpoints[2500].Velocity));
                        Debug.Log($"B3R_PACKED_ROLLBACK tick={tick+1} fault={s.Fault} grain={s.GrainPenetration:R} solid={s.SolidPenetration:R}");rejected=true;break;
                    }
                    prior=s;
                }
                // This regression checks atomicity whether or not the convergence experiment rejects.
                Debug.Log("B3R_PACKED_ROLLBACK observedRejection="+rejected);
            }
        }
    }
}
