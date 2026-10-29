using System;
using System.Runtime.InteropServices;
using Debris.Simulation.CoupledContacts;
using NUnit.Framework;
using UnityEngine;

namespace Debris.Simulation.Tests
{
    public sealed class CoupledBroadPhaseTests
    {
        static CoupledContactPrimitive Box(float x,float y,uint endpoint,
            Vector2? half=null,uint anchored=0)
        {
            return new CoupledContactPrimitive{Center=new Vector2(x,y),
                Half=half??Vector2.one*.5f,Endpoint=endpoint,Anchored=anchored};
        }

        [Test] public void IndexedBinsEmitOnePairAcrossSharedCells()
        {
            Assert.That(Marshal.SizeOf<CoupledContactPrimitive>(),Is.EqualTo(56));
            Assume.That(SystemInfo.supportsComputeShaders,Is.True);
            var input=new[]{Box(0,0,0,Vector2.one*1.5f),
                Box(1,0,1,Vector2.one*1.5f),Box(8,8,2)};
            using(var broad=new CoupledContactBroadPhase(input,3,new Vector2(-16,-16),1,32,32))
            {
                var result=broad.Build();
                Assert.That(result.FaultFlags,Is.Zero);
                Assert.That(result.PairCount,Is.EqualTo(1));
                Assert.That(result.Pairs[0].AEndpoint,Is.EqualTo(0));
                Assert.That(result.Pairs[0].BEndpoint,Is.EqualTo(1));
            }
        }

        [Test] public void PrimitiveFeatureIdentityIsUniqueWithinAnEndpoint()
        {
            Assert.Throws<ArgumentException>(()=>new CoupledContactBroadPhase(
                new[]{Box(0,0,0),Box(1,0,0)},1,new Vector2(-4,-4),1,8,8));
        }

        [Test] public void CandidatePairsUseEndpointFeatureOrderAcrossBuilds()
        {
            Assume.That(SystemInfo.supportsComputeShaders,Is.True);
            // Primitive input order deliberately differs from endpoint order.
            var input=new[]{Box(0,0,2),Box(0,0,0),Box(0,0,1)};
            using(var broad=new CoupledContactBroadPhase(input,3,
                new Vector2(-4,-4),1,8,8))
            {
                ulong[] Keys(CoupledContactBroadPhase.Result result)
                {
                    Assert.That(result.Success,Is.True);
                    Assert.That(result.PairCount,Is.EqualTo(3));
                    var keys=new ulong[3];
                    for(int i=0;i<3;i++)
                    {
                        uint a=result.Pairs[i].AEndpoint,b=result.Pairs[i].BEndpoint;
                        keys[i]=((ulong)Math.Min(a,b)<<32)|Math.Max(a,b);
                    }
                    return keys;
                }
                var first=Keys(broad.Build());
                var second=Keys(broad.Build());
                ulong[] expected={1ul,2ul,(1ul<<32)|2ul};
                Assert.That(first,Is.EqualTo(expected));
                Assert.That(second,Is.EqualTo(expected));
            }
        }

        [Test] public void PersistentIdentityOrderOverridesEndpointSlotOrder()
        {
            Assume.That(SystemInfo.supportsComputeShaders,Is.True);
            var identities=new CoupledEndpointIdentityTable(3);
            identities.Bind(0,CoupledPersistentIdentity.Grain(30),1);
            identities.Bind(1,CoupledPersistentIdentity.Grain(20),1);
            identities.Bind(2,CoupledPersistentIdentity.Grain(10),1);
            var input=new[]{Box(0,0,2),Box(0,0,0),Box(0,0,1)};
            using(var broad=new CoupledContactBroadPhase(input,3,
                new Vector2(-4,-4),1,8,8,.25f,identities.Snapshot()))
            {
                var result=broad.Build();
                Assert.That(result.Success,Is.True);
                Assert.That(result.PairCount,Is.EqualTo(3));
                ulong[] expected={(10ul<<32)|20ul,(10ul<<32)|30ul,
                    (20ul<<32)|30ul};
                var actual=new ulong[3];
                for(int i=0;i<3;i++)
                {
                    uint slotA=result.Pairs[i].AEndpoint;
                    uint slotB=result.Pairs[i].BEndpoint;
                    ulong idA=identities.Resolve((int)slotA).Identity.Low;
                    ulong idB=identities.Resolve((int)slotB).Identity.Low;
                    actual[i]=(Math.Min(idA,idB)<<32)|Math.Max(idA,idB);
                }
                Assert.That(actual,Is.EqualTo(expected));
            }
        }

        [Test] public void LongProxyStillPairsWithShortAndOtherLongProxy()
        {
            Assume.That(SystemInfo.supportsComputeShaders,Is.True);
            var input=new[]{Box(0,0,0,new Vector2(33,.5f)),
                Box(0,0,1),Box(0,0,2,new Vector2(33,.5f))};
            using(var broad=new CoupledContactBroadPhase(input,3,new Vector2(-64,-64),1))
            {
                var result=broad.Build();
                Assert.That(result.FaultFlags,Is.Zero);
                Assert.That(result.LongProxyCount,Is.EqualTo(2));
                Assert.That(result.PairCount,Is.EqualTo(3));
            }
        }

        [Test] public void AnchoredPairsAreExcludedAndGridOverflowFaults()
        {
            Assume.That(SystemInfo.supportsComputeShaders,Is.True);
            using(var broad=new CoupledContactBroadPhase(new[]{
                Box(0,0,0,anchored:1),Box(0,0,1,anchored:1)},
                2,new Vector2(-4,-4),1,8,8))
            {
                var result=broad.Build();
                Assert.That(result.FaultFlags,Is.Zero);
                Assert.That(result.PairCount,Is.Zero);
            }
            using(var broad=new CoupledContactBroadPhase(new[]{Box(10,0,0)},
                1,new Vector2(-4,-4),1,8,8))
            {
                var result=broad.Build();
                Assert.That(result.FaultFlags&2u,Is.EqualTo(2u));
                Assert.That(result.Pairs,Is.Empty);
            }
        }
    }
}
