using System;
using Debris.Simulation.CoupledContacts;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace Debris.Simulation.Tests
{
    public sealed class CoupledPositionSearchTests
    {
        sealed class Fixture : IDisposable
        {
            public readonly CoupledContactOperator Op;
            public readonly CoupledPositionSolve Solve;
            public readonly CoupledPositionSearch Search;
            public readonly CommandBuffer Commands=new CommandBuffer();
            public Fixture(Vector2[] centers,bool[] anchored,uint[] classes,
                Vector2? firstBodyCenter=null)
            {
                Assume.That(SystemInfo.supportsComputeShaders,Is.True);
                var bodies=new CoupledContactBody[centers.Length];
                var primitives=new CoupledContactPrimitive[centers.Length];
                for(int i=0;i<centers.Length;i++)
                {
                    bodies[i]=new CoupledContactBody{Center=i==0&&firstBodyCenter.HasValue?
                        firstBodyCenter.Value:centers[i],InverseMass=anchored[i]?0:1,
                        InverseInertia=anchored[i]?0:1};
                    primitives[i]=new CoupledContactPrimitive{Center=centers[i],
                        Half=Vector2.one*.5f,Endpoint=(uint)i,ContactClass=classes[i],
                        Anchored=anchored[i]?1u:0u};
                }
                try
                {
                    Op=new CoupledContactOperator(bodies,primitives,
                        Vector2.one*-8,1,16,16,normalOnly:true);
                    Solve=new CoupledPositionSolve(Op);
                    Search=new CoupledPositionSearch(Op,Solve);
                    Op.RecordGraph(Commands);
                    Search.RecordBeginSubstep(Commands);
                    Graphics.ExecuteCommandBuffer(Commands);Commands.Clear();
                }
                catch { Dispose();throw; }
            }
            // Isolate actual-pose acceptance from the Newton solver. This is
            // an injected, declared converged proposal, not solver evidence.
            public uint[] Propose(params Vector4[] delta)
            {
                Solve.Control.SetData(new uint[]{0,1,0,0,4,0,0,0});
                Solve.Displacement.SetData(delta);
                Search.Record(Commands);
                Graphics.ExecuteCommandBuffer(Commands);Commands.Clear();
                var control=new uint[12];Search.Control.GetData(control);return control;
            }
            public CoupledContactBody[] Bodies()
            {
                var bodies=new CoupledContactBody[Op.BodyCount];
                Search.WorkingBodies.GetData(bodies);return bodies;
            }
            public void Dispose()
            {Commands.Dispose();Search?.Dispose();Solve?.Dispose();Op?.Dispose();}
        }

        [Test] public void FirstActualPoseWinnerRebuildsWorkingGraph()
        {
            using(var f=new Fixture(new[]{Vector2.zero,Vector2.right*.95f},
                new[]{false,true},new uint[]{1,2}))
            {
                var control=f.Propose(new Vector4(-.0499f,0,0,0),Vector4.zero);
                Assert.That(control[1],Is.EqualTo(1u));
                Assert.That(control[2],Is.Zero);
                Assert.That(control[3],Is.Zero);
                Assert.That(control[4],Is.EqualTo(1u));
                Assert.That(control[5],Is.EqualTo(1u));
                Assert.That(control[6],Is.EqualTo(1u),"Only the first trial is evaluated for acceptance");
                Assert.That(control[7],Is.EqualTo(1u));
                Assert.That(f.Bodies()[0].Center.x,Is.EqualTo(-.0499f).Within(1e-6));
                var graphBodies=new CoupledContactBody[2];f.Op.BodiesBuffer.GetData(graphBodies);
                Assert.That(graphBodies[0].Center.x,Is.EqualTo(-.0499f).Within(1e-6));
            }
        }

        [Test] public void NewlyEncounteredPairContributesMeritAndIsAddedToSelectedGraph()
        {
            using(var f=new Fixture(new[]{Vector2.zero,Vector2.right*1.3f,Vector2.left*.84f},
                new[]{false,false,true},new uint[]{1,1,2}))
            {
                var control=f.Propose(new Vector4(.16f,0,0,0),new Vector4(-.16f,0,0,0),Vector4.zero);
                Assert.That(control[1],Is.EqualTo(1u));
                Assert.That(control[2],Is.Zero);
                Assert.That(control[4],Is.Zero,"The new pair still needs correction");
                Assert.That(control[5],Is.EqualTo(1u));
                Assert.That(control[7],Is.Zero,"Search progress alone is not final hard-bound acceptance");
                var count=new uint[1];f.Op.ActiveRowCounter.GetData(count);
                var features=new CoupledContactFeature[count[0]];
                f.Op.RowFeatures.GetData(features,0,0,features.Length);
                Assert.That(Array.Exists(features,x=>x.APrimitive==0&&x.BPrimitive==1),Is.True,
                    "The pair began beyond the .25 narrow-phase margin and must be discovered afresh");
            }
        }

        [Test] public void InitiallySatisfiedSolidContactForcesBacktracking()
        {
            using(var f=new Fixture(new[]{Vector2.zero,Vector2.right*.95f,Vector2.left*1.0005f},
                new[]{false,true,true},new uint[]{1,2,2}))
            {
                var control=f.Propose(new Vector4(-.0499f,0,0,0),Vector4.zero,Vector4.zero);
                Assert.That(control[1],Is.EqualTo(1u));
                Assert.That(control[2],Is.EqualTo(6u),
                    "Larger steps improve aggregate merit but break the initially satisfied left wall");
                Assert.That(control[4],Is.Zero);
                Assert.That(f.Bodies()[0].Center.x,Is.EqualTo(-.0499f/64).Within(1e-6));
            }
        }

        [Test] public void EnvelopeIsCumulativeAndFailureCannotBeResurrected()
        {
            using(var f=new Fixture(new[]{Vector2.zero,Vector2.right*.5f},
                new[]{false,true},new uint[]{1,2}))
            {
                var first=f.Propose(new Vector4(-.15f,0,0,0),Vector4.zero);
                Assert.That(first[1],Is.EqualTo(1u));
                var second=f.Propose(new Vector4(-.15f,0,0,0),Vector4.zero);
                Assert.That(second[1],Is.Zero);
                Assert.That(second[3],Is.EqualTo(3u));
                Assert.That(f.Bodies()[0].Center.x,Is.EqualTo(-.15f).Within(1e-6));
                var third=f.Propose(new Vector4(-.01f,0,0,0),Vector4.zero);
                Assert.That(third[1],Is.Zero);
                Assert.That(third[3],Is.EqualTo(3u));
                Assert.That(f.Bodies()[0].Center.x,Is.EqualTo(-.15f).Within(1e-6));
                using(var rejected=new GraphicsBuffer(GraphicsBuffer.Target.Structured,1,4))
                {
                    rejected.SetData(new uint[]{0});
                    f.Search.RecordRollback(f.Commands,rejected);
                    Graphics.ExecuteCommandBuffer(f.Commands);f.Commands.Clear();
                    Assert.That(f.Bodies()[0].Center,Is.EqualTo(Vector2.zero),
                        "Rejecting the complete stage also discards its earlier accepted -.15 correction");
                    var restored=new CoupledContactBody[2];f.Op.BodiesBuffer.GetData(restored);
                    Assert.That(restored[0].Center,Is.EqualTo(Vector2.zero));
                }
            }
        }

        [Test] public void OffsetPrimitiveRotationUsesRadiusAboutBodyCenter()
        {
            using(var f=new Fixture(new[]{Vector2.right*3,Vector2.right*3.95f},
                new[]{false,true},new uint[]{2,2},Vector2.zero))
            {
                var control=f.Propose(new Vector4(0,0,.1f,0),Vector4.zero);
                Assert.That(control[1],Is.Zero);
                Assert.That(control[3],Is.EqualTo(3u));
                Assert.That(f.Bodies()[0].Center,Is.EqualTo(Vector2.zero));
            }
        }

        [Test] public void UnionGroupAndExteriorProxiesMoveTogether()
        {
            Assume.That(SystemInfo.supportsComputeShaders,Is.True);
            var patches=new[]{new CoupledUnionPatch{Half=Vector2.one*.5f,Feature=1}};
            var faces=CoupledExteriorFaces.Build(patches);
            var union=new CoupledUnionGeometry(patches,faces,new[]{
                new CoupledUnionQueryGroup{UnionCenter=Vector2.right,PatchCount=1,
                    QueryCount=(uint)faces.Length,Reserved=1}});
            var primitives=new CoupledContactPrimitive[1+faces.Length];
            primitives[0]=new CoupledContactPrimitive{Center=Vector2.right*.05f,
                Half=Vector2.one*.5f,Endpoint=0,Anchored=1,ContactClass=1};
            for(int face=0;face<faces.Length;face++)
                primitives[face+1]=union.FacePrimitive(face,0,false,Vector2.zero);
            var bodies=new[]{new CoupledContactBody{Center=primitives[0].Center},
                new CoupledContactBody{Center=Vector2.right,InverseMass=1,InverseInertia=1}};
            using(var op=new CoupledContactOperator(bodies,primitives,
                Vector2.one*-8,1,16,16,union,validateContainment:true,normalOnly:true))
            using(var solve=new CoupledPositionSolve(op))
            using(var search=new CoupledPositionSearch(op,solve))
            using(var commands=new CommandBuffer())
            {
                solve.Control.SetData(new uint[]{0,1,0,0,4,0,0,0});
                solve.Displacement.SetData(new[]{Vector4.zero,new Vector4(.0499f,0,0,0)});
                op.RecordGraph(commands);search.RecordBeginSubstep(commands);
                search.Record(commands);Graphics.ExecuteCommandBuffer(commands);
                var control=new uint[12];search.Control.GetData(control);
                Assert.That(control[1],Is.EqualTo(1u));
                Assert.That(control[3],Is.Zero);
                var groups=new CoupledUnionQueryGroup[1];search.WorkingUnionGroups.GetData(groups);
                Assert.That(groups[0].UnionCenter.x,Is.EqualTo(1.0499f).Within(1e-6));
                Assert.That(groups[0].PatchCount,Is.EqualTo(1u));
                var moved=new CoupledContactPrimitive[primitives.Length];
                search.WorkingPrimitives.GetData(moved);
                for(int face=1;face<moved.Length;face++)
                {
                    Assert.That(moved[face].Center.x,
                        Is.EqualTo(primitives[face].Center.x+.0499f).Within(1e-6));
                    Assert.That(moved[face].FeatureId,Is.EqualTo(primitives[face].FeatureId));
                }
            }
        }

        [Test] public void EightWorseningTrialsLeaveWorkingAndGraphPosesUnchanged()
        {
            using(var f=new Fixture(new[]{Vector2.zero,Vector2.right*.95f},
                new[]{false,true},new uint[]{1,2}))
            {
                var control=f.Propose(new Vector4(.01f,0,0,0),Vector4.zero);
                Assert.That(control[1],Is.Zero);
                Assert.That(control[2],Is.EqualTo(uint.MaxValue));
                Assert.That(control[3],Is.EqualTo(5u));
                Assert.That(control[6],Is.EqualTo(8u));
                Assert.That(f.Bodies()[0].Center,Is.EqualTo(Vector2.zero));
                var graph=new CoupledContactBody[2];f.Op.BodiesBuffer.GetData(graph);
                Assert.That(graph[0].Center,Is.EqualTo(Vector2.zero));
            }
        }

        [Test] public void NonfiniteProposalRetainsWorkingPose()
        {
            using(var f=new Fixture(new[]{Vector2.zero,Vector2.right*.95f},
                new[]{false,true},new uint[]{1,2}))
            {
                var control=f.Propose(new Vector4(float.NaN,0,0,0),Vector4.zero);
                Assert.That(control[1],Is.Zero);
                Assert.That(control[3],Is.EqualTo(4u));
                Assert.That(f.Bodies()[0].Center,Is.EqualTo(Vector2.zero));
            }
        }
    }
}
