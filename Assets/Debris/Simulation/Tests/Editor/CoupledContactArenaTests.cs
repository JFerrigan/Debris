using System;
using Debris.Simulation.CoupledContacts;
using NUnit.Framework;
using UnityEngine;

namespace Debris.Simulation.Tests
{
    public sealed class CoupledContactArenaTests
    {
        [Test] public void NamedSlicesRejectOverBudgetAndReleaseExactlyOnce()
        {
            Assume.That(SystemInfo.supportsComputeShaders,Is.True);
            var arena=new CoupledContactArena(64);
            var first=arena.Allocate("graph/rows",4,8);
            var second=arena.Allocate("graph/count",1,4);
            try
            {
                Assert.That(arena.UsedBytes,Is.EqualTo(36));
                Assert.That(arena.SliceCount,Is.EqualTo(2));
                Assert.Throws<InvalidOperationException>(()=>
                    arena.Allocate("graph/overflow",4,8));
                Assert.That(arena.UsedBytes,Is.EqualTo(36));
                Assert.Throws<InvalidOperationException>(()=>
                    arena.Allocate("graph/rows",1,4));
                var slices=arena.Snapshot();
                Assert.That(slices[0].Name,Is.EqualTo("graph/count"));
                Assert.That(slices[1].Bytes,Is.EqualTo(32));
            }
            finally
            {
                arena.Release(second);arena.Release(first);
            }
            arena.AssertEmpty();
            Assert.Throws<InvalidOperationException>(()=>arena.Release(first));
        }

        [Test] public void FailedGraphConstructionRollsBackOnlyItsOwnSlices()
        {
            Assume.That(SystemInfo.supportsComputeShaders,Is.True);
            var arena=new CoupledContactArena(128);
            var existing=arena.Allocate("existing",4,4);
            try
            {
                var primitive=new CoupledContactPrimitive{Center=Vector2.zero,
                    Half=Vector2.one*.5f,Endpoint=0};
                var error=Assert.Throws<InvalidOperationException>(()=>
                    new CoupledContactBroadPhase(new[]{primitive},1,
                        new Vector2(-4,-4),1,8,8,.25f,null,arena));
                Assert.That(error.Message,Does.Contain("V2 GPU arena overflow"));
                Assert.That(arena.UsedBytes,Is.EqualTo(16));
                Assert.That(arena.SliceCount,Is.EqualTo(1));
            }
            finally {arena.Release(existing);}
            arena.AssertEmpty();
        }
    }
}
