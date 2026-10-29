using Debris.Simulation.CoupledContacts;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace Debris.Simulation.Tests
{
    public sealed class CoupledUnionContainmentTests
    {
        [Test] public void FinalRequeryNamesTheBuriedFeatureWithoutAPair()
        {
            Assume.That(SystemInfo.supportsComputeShaders,Is.True);
            var patches=new[]{new CoupledUnionPatch{Center=Vector2.zero,
                Half=new Vector2(3,3),Feature=1}};
            var faces=CoupledExteriorFaces.Build(patches);
            var geometry=new CoupledUnionGeometry(patches,faces,new[]{
                new CoupledUnionQueryGroup{PatchCount=1,
                    QueryCount=(uint)faces.Length,Reserved=1}});
            var grain=new CoupledContactPrimitive{Center=Vector2.zero,
                Half=Vector2.one*.5f,Endpoint=0,ContactClass=1,FeatureId=77};
            var pose=new GraphicsBuffer(GraphicsBuffer.Target.Structured,1,56);
            var groupPose=new GraphicsBuffer(GraphicsBuffer.Target.Structured,1,32);
            var commands=new CommandBuffer();
            try
            {
                pose.SetData(new[]{grain});groupPose.SetData(geometry.Groups);
                using(var batch=new CoupledContactManifoldBatch(new[]{grain},2,
                    new Vector2(-8,-8),1,16,16,.25f,geometry,true))
                {
                    batch.RecordRequery(commands,pose,groupPose);
                    Graphics.ExecuteCommandBuffer(commands);
                    var facts=batch.ReadFinalGapFacts();
                    Assert.That(facts.HardBoundsPass,Is.False);
                    Assert.That(facts.FirstContainedPrimitive,Is.Zero);
                    Assert.That(facts.FirstContainedEndpoint,Is.Zero);
                    Assert.That(facts.FirstContainedFeature,Is.EqualTo(77));
                }
            }
            finally{commands.Release();groupPose.Release();pose.Release();}
        }

        [Test] public void BuriedSquareFaultsWithNoExteriorCandidate()
        {
            Assume.That(SystemInfo.supportsComputeShaders,Is.True);
            var patches=new[]{new CoupledUnionPatch{Center=Vector2.zero,
                Half=new Vector2(3,3),Feature=1}};
            var faces=CoupledExteriorFaces.Build(patches);
            var geometry=new CoupledUnionGeometry(patches,faces,new[]{
                new CoupledUnionQueryGroup{PatchCount=1,QueryCount=(uint)faces.Length,
                    Reserved=1}});
            var grain=new CoupledContactPrimitive{Center=Vector2.zero,
                Half=Vector2.one*.5f,Endpoint=0};
            using(var batch=new CoupledContactManifoldBatch(new[]{grain},2,
                new Vector2(-8,-8),1,16,16,.25f,geometry,true))
            {
                var result=batch.Build();
                Assert.That(result.CandidateCount,Is.Zero);
                Assert.That(result.ActiveCount,Is.Zero);
                Assert.That(result.CompactionFault&32u,Is.EqualTo(32u));
                Assert.That(result.CompactionFault&64u,Is.EqualTo(64u));
                Assert.That(result.FirstContainedPrimitive,Is.Zero);
            }
        }

        [Test] public void EmptyCavityCenterDoesNotClaimSolidContainment()
        {
            Assume.That(SystemInfo.supportsComputeShaders,Is.True);
            var patches=new[]{
                new CoupledUnionPatch{Center=new Vector2(0,-1),Half=new Vector2(1.5f,.5f),Feature=1},
                new CoupledUnionPatch{Center=new Vector2(0,1),Half=new Vector2(1.5f,.5f),Feature=2},
                new CoupledUnionPatch{Center=new Vector2(-1,0),Half=Vector2.one*.5f,Feature=3},
                new CoupledUnionPatch{Center=new Vector2(1,0),Half=Vector2.one*.5f,Feature=4}
            };
            var faces=CoupledExteriorFaces.Build(patches);
            var geometry=new CoupledUnionGeometry(patches,faces,new[]{
                new CoupledUnionQueryGroup{PatchCount=(uint)patches.Length,
                    QueryCount=(uint)faces.Length,Reserved=1}});
            var grain=new CoupledContactPrimitive{Center=Vector2.zero,
                Half=Vector2.one*.5f,Endpoint=0};
            using(var batch=new CoupledContactManifoldBatch(new[]{grain},2,
                new Vector2(-8,-8),1,16,16,.25f,geometry,true))
            {
                var result=batch.Build();
                Assert.That(result.CompactionFault,Is.Zero);
                Assert.That(result.FirstContainedPrimitive,Is.EqualTo(uint.MaxValue));
            }
        }

        [Test] public void BuriedSquareAcrossPatchSeamFaultsWithoutAnyFacePair()
        {
            Assume.That(SystemInfo.supportsComputeShaders,Is.True);
            var patches=new[]{
                new CoupledUnionPatch{Center=new Vector2(-.5f,0),
                    Half=new Vector2(.5f,1),Feature=1},
                new CoupledUnionPatch{Center=new Vector2(.5f,0),
                    Half=new Vector2(.5f,1),Feature=2}
            };
            var faces=CoupledExteriorFaces.Build(patches);
            var geometry=new CoupledUnionGeometry(patches,faces,new[]{
                new CoupledUnionQueryGroup{PatchCount=2,QueryCount=(uint)faces.Length,
                    Reserved=1}});
            var grain=new CoupledContactPrimitive{Center=Vector2.zero,
                Half=new Vector2(.4f,.4f),Endpoint=0};
            using(var batch=new CoupledContactManifoldBatch(new[]{grain},2,
                new Vector2(-8,-8),1,16,16,.25f,geometry,true))
            {
                var result=batch.Build();
                Assert.That(result.CandidateCount,Is.Zero);
                Assert.That(result.CompactionFault&32u,Is.EqualTo(32u));
                Assert.That(result.FirstContainedPrimitive,Is.Zero);
            }
        }
    }
}
