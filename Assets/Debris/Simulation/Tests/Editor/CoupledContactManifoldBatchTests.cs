using System.Runtime.InteropServices;
using Debris.Simulation.CoupledContacts;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace Debris.Simulation.Tests
{
    public sealed class CoupledContactManifoldBatchTests
    {
        [TestCase(1u,true),TestCase(2u,false)]
        public void FinalPoseUsesClassSpecificHardBound(uint contactClass,bool pass)
        {
            Assert.That(Marshal.SizeOf<CoupledContactPrimitive>(),Is.EqualTo(56));
            Assume.That(SystemInfo.supportsComputeShaders,Is.True);
            var initial=new[]{
                new CoupledContactPrimitive{Center=Vector2.zero,
                    Half=Vector2.one*.5f,Endpoint=0,ContactClass=contactClass,
                    FeatureId=11},
                new CoupledContactPrimitive{Center=Vector2.right,
                    Half=Vector2.one*.5f,Endpoint=1,ContactClass=contactClass,
                    FeatureId=22}};
            var corrected=(CoupledContactPrimitive[])initial.Clone();
            corrected[1].Center=new Vector2(.995f,0);
            var pose=new GraphicsBuffer(GraphicsBuffer.Target.Structured,2,56);
            var commands=new CommandBuffer();
            try
            {
                pose.SetData(corrected);
                using(var batch=new CoupledContactManifoldBatch(initial,2,
                    new Vector2(-8,-8),1,16,16))
                using(var gate=new CoupledFinalGeometryGate(batch))
                {
                    gate.RecordRequery(commands,pose);
                    Graphics.ExecuteCommandBuffer(commands);
                    var decision=new uint[8];gate.Decision.GetData(decision);
                    Assert.That(decision[0],Is.EqualTo(pass?1u:0u));
                    Assert.That((decision[1]&16u)!=0,Is.EqualTo(!pass));
                    var facts=batch.ReadFinalGapFacts();
                    Assert.That(facts.GeometryComplete,Is.True);
                    Assert.That(facts.HardBoundsPass,Is.EqualTo(pass));
                    Assert.That(facts.NegativePoints,Is.EqualTo(2));
                    Assert.That(facts.InvalidRecords,Is.Zero);
                    Assert.That(facts.FirstManifold,Is.Zero);
                    Assert.That(facts.APrimitive,Is.Zero);
                    Assert.That(facts.BPrimitive,Is.EqualTo(1));
                    Assert.That(facts.AEndpoint,Is.Zero);
                    Assert.That(facts.BEndpoint,Is.EqualTo(1));
                    Assert.That(facts.AFeature,Is.EqualTo(11));
                    Assert.That(facts.BFeature,Is.EqualTo(22));
                    Assert.That(contactClass==1?facts.MaximumGrainPenetration:
                        facts.MaximumSolidPenetration,Is.EqualTo(.005f).Within(1e-4f));
                }
            }
            finally{commands.Release();pose.Release();}
        }

        [Test] public void FinalPoseRejectsUnclassifiedPrimitiveWithoutContacts()
        {
            Assume.That(SystemInfo.supportsComputeShaders,Is.True);
            var primitive=new CoupledContactPrimitive{Center=Vector2.zero,
                Half=Vector2.one*.5f,Endpoint=0};
            var pose=new GraphicsBuffer(GraphicsBuffer.Target.Structured,1,56);
            var commands=new CommandBuffer();
            try
            {
                pose.SetData(new[]{primitive});
                using(var batch=new CoupledContactManifoldBatch(new[]{primitive},1,
                    new Vector2(-8,-8),1,16,16))
                using(var gate=new CoupledFinalGeometryGate(batch))
                {
                    gate.RecordRequery(commands,pose);
                    Graphics.ExecuteCommandBuffer(commands);
                    var decision=new uint[8];gate.Decision.GetData(decision);
                    Assert.That(decision[0],Is.Zero);
                    Assert.That((decision[1]&8u)!=0,Is.True);
                    var facts=batch.ReadFinalGapFacts();
                    Assert.That(facts.InvalidRecords,Is.EqualTo(1));
                    Assert.That(facts.GeometryComplete,Is.False);
                    Assert.That(facts.HardBoundsPass,Is.False);
                }
            }
            finally{commands.Release();pose.Release();}
        }

        [Test] public void IndexedCandidatesBecomeTwoPointActiveManifold()
        {
            Assert.That(Marshal.SizeOf<CoupledContactManifold>(),Is.EqualTo(52));
            Assume.That(SystemInfo.supportsComputeShaders,Is.True);
            var primitives=new[]{
                new CoupledContactPrimitive{Center=Vector2.zero,Half=Vector2.one*.5f,Endpoint=0},
                new CoupledContactPrimitive{Center=Vector2.right,Half=Vector2.one*.5f,Endpoint=1},
                new CoupledContactPrimitive{Center=new Vector2(6,6),Half=Vector2.one*.5f,Endpoint=2}};
            using(var batch=new CoupledContactManifoldBatch(primitives,3,
                new Vector2(-16,-16),1,32,32))
            {
                var result=batch.Build();
                Assert.That(result.Success,Is.True);
                Assert.That(result.CandidateCount,Is.EqualTo(1));
                Assert.That(result.ActiveCount,Is.EqualTo(1));
                Assert.That(result.Pairs[0].AEndpoint,Is.EqualTo(0));
                Assert.That(result.Pairs[0].BEndpoint,Is.EqualTo(1));
                Assert.That(result.Manifolds[0].Count,Is.EqualTo(2));
                Assert.That(result.Manifolds[0].Normal.x,Is.EqualTo(1).Within(1e-5));
            }
        }
    }
}
