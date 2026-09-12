using System;
using System.Collections;
using Debris.Simulation.ParallelProof;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Grain=Debris.Simulation.ParallelProof.LooseCell;
namespace Debris.Simulation.Tests
{
    public sealed class ParallelRigidTests
    {
        static ProofFixture Pair()=>new ProofFixture{
            Grains=new[]{new Grain{Center=new Vector2(100,100),Material=1,Identity=1}},
            Bodies=new[]{new BodyState{Center=new Vector2(-.5f,0),Velocity=Vector2.right*10},new BodyState{Center=new Vector2(.5f,0)}},
            Parameters=new[]{new BodyParameters{InverseMass=1,InverseInertia=6,BoundaryCount=1,Mobility=1},new BodyParameters{InverseMass=1,InverseInertia=6,BoundaryStart=1,BoundaryCount=1,Mobility=1}},
            Boundaries=new[]{new Boundary{Body=1,HalfSize=Vector2.one*.5f},new Boundary{Body=2,HalfSize=Vector2.one*.5f}}};
        [UnityTest,Timeout(120000)] public IEnumerator FaceManifoldCouplesEqualBodiesWithoutSpuriousSpin()
        {
            using(var solver=Pair().Create(8,4,0))
            {
                solver.Step();var t=solver.SnapshotAsync();while(!t.IsCompleted)yield return null;var s=t.Result;
                Assert.That(s.Fault,Is.EqualTo(SolverFault.None));Assert.That(s.Diagnostics[13],Is.EqualTo(2));
                for(int i=1;i<=2;i++){Assert.That(s.Endpoints[i].Velocity.x,Is.EqualTo(5).Within(.0001));Assert.That(s.Endpoints[i].AngularVelocity,Is.EqualTo(0).Within(.0001));}
            }
        }
        [UnityTest,Timeout(120000)] public IEnumerator OffCentreRigidImpactConservesLinearAngularMomentum()
        {
            var f=Pair();f.Bodies[0]=new BodyState{Center=new Vector2(-1,1.5f),Velocity=Vector2.right};f.Bodies[1]=new BodyState();
            f.Parameters[1].InverseMass=.25f;f.Parameters[1].InverseInertia=3f/17;f.Boundaries[1].HalfSize=new Vector2(.5f,2);
            using(var solver=f.Create(8,4,0))
            {
                solver.Step();var t=solver.SnapshotAsync();while(!t.IsCompleted)yield return null;var s=t.Result;var a=s.Endpoints[1];var b=s.Endpoints[2];
                Assert.That(s.Fault,Is.EqualTo(SolverFault.None));Assert.That(b.AngularVelocity,Is.LessThan(0));
                Assert.That(Vector2.Distance(a.Velocity+4*b.Velocity,Vector2.right),Is.LessThan(.0001));
                double angular=a.Center.x*a.Velocity.y-a.Center.y*a.Velocity.x+a.AngularVelocity/6.0+4*(b.Center.x*b.Velocity.y-b.Center.y*b.Velocity.x)+17.0/3*b.AngularVelocity;
                Assert.That(angular,Is.EqualTo(-1.5).Within(.0015));
                double energy=.5*a.Velocity.sqrMagnitude+a.AngularVelocity*a.AngularVelocity/12.0+2*b.Velocity.sqrMagnitude+17.0/6*b.AngularVelocity*b.AngularVelocity;
                Assert.That(energy,Is.LessThanOrEqualTo(.5001));
            }
        }
        [UnityTest,Timeout(120000)] public IEnumerator RigidAnchorPreservesTangentialMotion()
        {
            var f=Pair();f.Bodies[0].Velocity=new Vector2(10,3);f.Parameters[1].InverseMass=0;f.Parameters[1].InverseInertia=0;f.Parameters[1].Mobility=0;f.Boundaries[1].HalfSize=new Vector2(.5f,10);
            using(var solver=f.Create(8,4,0))
            {
                solver.Step();var t=solver.SnapshotAsync();while(!t.IsCompleted)yield return null;var s=t.Result;
                Assert.That(s.Fault,Is.EqualTo(SolverFault.None));Assert.That(s.Endpoints[1].Velocity.x,Is.EqualTo(0).Within(.001));Assert.That(s.Endpoints[1].Velocity.y,Is.EqualTo(3).Within(.0001));
                Assert.That(s.Endpoints[2].Center,Is.EqualTo(f.Bodies[1].Center));Assert.That(s.Endpoints[2].Angle,Is.EqualTo(0));
            }
        }
        [UnityTest,Timeout(120000)] public IEnumerator UndersizedManifoldBufferRejectsAtomically()
        {
            var f=Pair();using(var solver=new ParallelGrainSolver(f.Grains,f.Bodies,f.Parameters,f.Boundaries,8,4,0,64,1))
            {
                solver.Step();var t=solver.SnapshotAsync();while(!t.IsCompleted)yield return null;var s=t.Result;
                Assert.That(s.Fault.HasFlag(SolverFault.RigidCapacity),$"fault={s.Fault} completed={s.CompletedTick} points={s.Diagnostics[13]} pairMax={s.Diagnostics[14]}");Assert.That(s.CompletedTick,Is.Zero);
                for(int i=0;i<2;i++){Assert.That(s.Endpoints[i+1].Center,Is.EqualTo(f.Bodies[i].Center));Assert.That(s.Endpoints[i+1].Velocity,Is.EqualTo(f.Bodies[i].Velocity));}
            }
        }
        [UnityTest,Timeout(120000)] public IEnumerator TooManyDistinctBodyPairPatchesRejectsAtomically()
        {
            var f=Pair();f.Boundaries=new Boundary[34];f.Boundaries[0]=new Boundary{Body=1,HalfSize=new Vector2(.5f,20)};
            for(int i=1;i<34;i++)f.Boundaries[i]=new Boundary{Body=2,Center=new Vector2(0,i-17),HalfSize=new Vector2(.5f,.4f),Feature=(uint)i};
            f.Parameters[1].BoundaryCount=33;
            using(var solver=f.Create(8,4,0))
            {
                solver.Step();var t=solver.SnapshotAsync();while(!t.IsCompleted)yield return null;var s=t.Result;
                Assert.That(s.Fault.HasFlag(SolverFault.RigidPairCapacity));Assert.That(s.CompletedTick,Is.Zero);Assert.That(s.Endpoints[1].Center,Is.EqualTo(f.Bodies[0].Center));
            }
        }
    }
}
