using System;
using System.Collections;
using Debris.Simulation.ParallelProof;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Grain=Debris.Simulation.ParallelProof.LooseCell;
namespace Debris.Simulation.Tests
{
    public sealed class ParallelBudgetTests
    {
        [Test] public void InvalidBodyDefinitionsAndNonfiniteGrainsAreRejectedBeforeAllocation()
        {
            var f=ProofFixtures.IsolatedHeavy();f.Parameters[0].BoundaryCount=2;
            Assert.Throws<ArgumentException>(()=>f.Create(4,2));
            f=ProofFixtures.IsolatedHeavy();f.Grains[0].Angle=float.NaN;
            Assert.Throws<ArgumentException>(()=>f.Create(4,2));
            f=ProofFixtures.IsolatedHeavy();f.Parameters[0].Mobility=0;
            Assert.Throws<ArgumentException>(()=>f.Create(4,2));
            f=ProofFixtures.IsolatedHeavy();f.Boundaries[0].HalfSize=Vector2.zero;
            Assert.Throws<ArgumentException>(()=>f.Create(4,2));
        }
        [UnityTest,Timeout(120000)] public IEnumerator NonzeroLocalCOMKeepsBoundaryAtItsWorldLocation()
        {
            var f=ProofFixtures.IsolatedHeavy();f.Parameters[0].LocalCOM=new Vector2(7,2);f.Boundaries[0].Center=f.Parameters[0].LocalCOM;
            using(var solver=f.Create(4,2,0))
            {
                solver.Step();var task=solver.SnapshotAsync();while(!task.IsCompleted)yield return null;var s=task.Result;
                Assert.That(s.Fault,Is.EqualTo(SolverFault.None));Assert.That(s.Grains[0].Velocity.x,Is.EqualTo(100000f/10001).Within(.0001));
                Assert.That(s.Endpoints[1].Velocity.x,Is.EqualTo(100000f/10001).Within(.0001));
            }
        }
        [UnityTest,Timeout(120000)] public IEnumerator IncidentLimitCountsIncomingAndOutgoingConstraints()
        {
            var grains=new Grain[3];for(int i=0;i<3;i++)grains[i]=new Grain{Center=Vector2.right*i,Velocity=i==0?Vector2.right:Vector2.zero,Material=1,Identity=(uint)i+1};
            using(var solver=new ParallelGrainSolver(grains,Array.Empty<BodyState>(),Array.Empty<BodyParameters>(),Array.Empty<Boundary>(),4,2,0,1))
            {
                solver.Step();var task=solver.SnapshotAsync();while(!task.IsCompleted)yield return null;var s=task.Result;
                Assert.That(s.Fault.HasFlag(SolverFault.Candidates));Assert.That(s.CompletedTick,Is.Zero);
                for(int i=0;i<3;i++){Assert.That(s.Grains[i].Center,Is.EqualTo(grains[i].Center));Assert.That(s.Grains[i].Velocity,Is.EqualTo(grains[i].Velocity));}
            }
        }
        [UnityTest,Timeout(120000)] public IEnumerator ExcessiveDensityFaultsBeforeAnyCommit()
        {
            var grains=new Grain[66];for(int i=0;i<grains.Length;i++)grains[i]=new Grain{Material=1,Identity=(uint)i+1};
            using(var solver=new ParallelGrainSolver(grains,Array.Empty<BodyState>(),Array.Empty<BodyParameters>(),Array.Empty<Boundary>(),4,2))
            {
                solver.Step();var task=solver.SnapshotAsync();while(!task.IsCompleted)yield return null;var s=task.Result;
                Assert.That(s.Fault.HasFlag(SolverFault.Candidates));Assert.That(s.CompletedTick,Is.Zero);Assert.That(s.Grains.Length,Is.EqualTo(66));
                for(int i=0;i<66;i++){Assert.That(s.Grains[i].Center,Is.EqualTo(Vector2.zero));Assert.That(s.Grains[i].Identity,Is.EqualTo((uint)i+1));}
            }
        }
        [UnityTest,Timeout(120000)] public IEnumerator EscapedCorrectionEnvelopeRejectsWholeState()
        {
            var f=ProofFixtures.IsolatedHeavy();f.Bodies[0].Velocity=Vector2.zero;f.Grains[0].Center=f.Bodies[0].Center;
            using(var solver=f.Create(4,2,0))
            {
                solver.Step();var task=solver.SnapshotAsync();while(!task.IsCompleted)yield return null;var s=task.Result;
                Assert.That(s.Fault.HasFlag(SolverFault.Envelope));Assert.That(s.CompletedTick,Is.Zero);
                Assert.That(s.Grains[0].Center,Is.EqualTo(f.Grains[0].Center));Assert.That(s.Endpoints[1].Center,Is.EqualTo(f.Bodies[0].Center));
            }
        }
        [UnityTest,Timeout(120000)] public IEnumerator RotatedGrainAtPageBoundaryRetainsMotion()
        {
            var g=new Grain{Center=new Vector2(255.4f,0),Velocity=Vector2.right,Angle=Mathf.PI/4,Material=1,Identity=1};
            using(var solver=new ParallelGrainSolver(new[]{g},Array.Empty<BodyState>(),Array.Empty<BodyParameters>(),Array.Empty<Boundary>(),4,2))
            {
                solver.Step();var task=solver.SnapshotAsync();while(!task.IsCompleted)yield return null;var s=task.Result;
                Assert.That(s.Fault.HasFlag(SolverFault.Page));Assert.That(s.CompletedTick,Is.Zero);
                Assert.That(s.Grains[0].Center,Is.EqualTo(g.Center));Assert.That(s.Grains[0].Velocity,Is.EqualTo(g.Velocity));
            }
        }
    }
}
