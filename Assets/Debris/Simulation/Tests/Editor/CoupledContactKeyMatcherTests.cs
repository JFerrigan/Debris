using Debris.Simulation.CoupledContacts;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace Debris.Simulation.Tests
{
    public sealed class CoupledContactKeyMatcherTests
    {
        [Test] public void FullGpuKeysMatchAcrossRowOrderAndRejectGenerationReuse()
        {
            Assume.That(SystemInfo.supportsComputeShaders,Is.True);
            var a=new CoupledContactKeyWords{Endpoints=1u<<14,
                SlotGenerationA=1,SlotGenerationB=2,RevisionA=3,RevisionB=4,
                FeatureA=5,FeatureB=6,Point=7};
            var b=a;b.FeatureB=9;
            var changed=a;changed.SlotGenerationA=2;
            var previous=new[]{a,b};
            var current=new[]{b,changed,a};
            using(var matcher=new CoupledContactKeyMatcher())
            using(var oldKeys=new GraphicsBuffer(GraphicsBuffer.Target.Structured,3,32))
            using(var newKeys=new GraphicsBuffer(GraphicsBuffer.Target.Structured,3,32))
            using(var oldCount=new GraphicsBuffer(GraphicsBuffer.Target.Structured,1,4))
            using(var newCount=new GraphicsBuffer(GraphicsBuffer.Target.Structured,1,4))
            using(var commands=new CommandBuffer())
            {
                oldKeys.SetData(previous);newKeys.SetData(current);
                oldCount.SetData(new uint[]{2});newCount.SetData(new uint[]{3});
                matcher.Record(commands,oldKeys,oldCount,newKeys,newCount);
                Graphics.ExecuteCommandBuffer(commands);
                var matches=new uint[3];var fault=new uint[1];
                matcher.Matches.GetData(matches,0,0,3);
                matcher.Fault.GetData(fault);
                Assert.That(fault[0],Is.Zero);
                Assert.That(matches,Is.EqualTo(new uint[]{1,uint.MaxValue,0}));
            }
        }

        [Test] public void DuplicatePreviousKeyFaultsAndReportsLowestRow()
        {
            Assume.That(SystemInfo.supportsComputeShaders,Is.True);
            var key=new CoupledContactKeyWords{Endpoints=1u<<14,
                SlotGenerationA=1,SlotGenerationB=1,RevisionA=1,RevisionB=1,
                FeatureA=2,FeatureB=3,Point=4};
            using(var matcher=new CoupledContactKeyMatcher())
            using(var oldKeys=new GraphicsBuffer(GraphicsBuffer.Target.Structured,2,32))
            using(var newKeys=new GraphicsBuffer(GraphicsBuffer.Target.Structured,1,32))
            using(var oldCount=new GraphicsBuffer(GraphicsBuffer.Target.Structured,1,4))
            using(var newCount=new GraphicsBuffer(GraphicsBuffer.Target.Structured,1,4))
            using(var commands=new CommandBuffer())
            {
                oldKeys.SetData(new[]{key,key});newKeys.SetData(new[]{key});
                oldCount.SetData(new uint[]{2});newCount.SetData(new uint[]{1});
                matcher.Record(commands,oldKeys,oldCount,newKeys,newCount);
                Graphics.ExecuteCommandBuffer(commands);
                var matches=new uint[1];var fault=new uint[1];
                matcher.Matches.GetData(matches,0,0,1);
                matcher.Fault.GetData(fault);
                Assert.That(fault[0]&2u,Is.EqualTo(2u));
                Assert.That(matches[0],Is.Zero);
            }
        }

        [Test] public void DuplicatePreviousKeyFaultsWithoutAnyCurrentMatch()
        {
            Assume.That(SystemInfo.supportsComputeShaders,Is.True);
            var prior=new CoupledContactKeyWords{Endpoints=1u<<14,
                SlotGenerationA=1,SlotGenerationB=1,RevisionA=1,RevisionB=1,
                FeatureA=2,FeatureB=3,Point=4};
            var current=prior;current.RevisionA=2;
            using(var matcher=new CoupledContactKeyMatcher())
            using(var oldKeys=new GraphicsBuffer(GraphicsBuffer.Target.Structured,2,32))
            using(var newKeys=new GraphicsBuffer(GraphicsBuffer.Target.Structured,1,32))
            using(var oldCount=new GraphicsBuffer(GraphicsBuffer.Target.Structured,1,4))
            using(var newCount=new GraphicsBuffer(GraphicsBuffer.Target.Structured,1,4))
            using(var commands=new CommandBuffer())
            {
                oldKeys.SetData(new[]{prior,prior});newKeys.SetData(new[]{current});
                oldCount.SetData(new uint[]{2});newCount.SetData(new uint[]{1});
                matcher.Record(commands,oldKeys,oldCount,newKeys,newCount);
                Graphics.ExecuteCommandBuffer(commands);
                var matches=new uint[1];var fault=new uint[1];
                matcher.Matches.GetData(matches,0,0,1);
                matcher.Fault.GetData(fault);
                Assert.That(fault[0]&2u,Is.EqualTo(2u));
                Assert.That(matches[0],Is.EqualTo(uint.MaxValue));
            }
        }
    }
}
