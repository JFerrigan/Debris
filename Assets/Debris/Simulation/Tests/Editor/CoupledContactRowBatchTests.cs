using Debris.Simulation.CoupledContacts;
using System.Runtime.InteropServices;
using NUnit.Framework;
using UnityEngine;

namespace Debris.Simulation.Tests
{
    public sealed class CoupledContactRowBatchTests
    {
        static CoupledContactPrimitive Box(Vector2 center,uint endpoint)
            =>new CoupledContactPrimitive{Center=center,Half=Vector2.one*.5f,Endpoint=endpoint};

        [Test] public void IndexedPairCompactsToFourPhysicalRows()
        {
            Assert.That(Marshal.SizeOf<CoupledContactFeature>(),Is.EqualTo(24));
            Assume.That(SystemInfo.supportsComputeShaders,Is.True);
            var bodies=new[]{
                new CoupledContactBody{Center=Vector2.zero,InverseMass=1,InverseInertia=1},
                new CoupledContactBody{Center=Vector2.right,InverseMass=1,InverseInertia=1}};
            using(var batch=new CoupledContactRowBatch(bodies,
                new[]{Box(Vector2.zero,0),Box(Vector2.right,1)},
                new Vector2(-8,-8),1,16,16))
            {
                var result=batch.Build();
                Assert.That(result.Success,Is.True);
                Assert.That(result.CandidateCount,Is.EqualTo(1));
                Assert.That(result.ManifoldCount,Is.EqualTo(1));
                Assert.That(result.RowCount,Is.EqualTo(4));
                for(int i=0;i<4;i++)
                {
                    Assert.That(result.Rows[i].A,Is.EqualTo(0));
                    Assert.That(result.Rows[i].B,Is.EqualTo(1));
                    Assert.That(result.Rows[i].InverseRoot,
                        Is.EqualTo(1/Mathf.Sqrt(2.5f)).Within(1e-5));
                    Assert.That(result.Features[i].APrimitive,Is.EqualTo(0));
                    Assert.That(result.Features[i].BPrimitive,Is.EqualTo(1));
                    Assert.That(result.Features[i].ReferenceFace,Is.EqualTo(1));
                    Assert.That(result.Features[i].IncidentFace,Is.EqualTo(3));
                    Assert.That(result.Features[i].Provenance,Is.EqualTo(i<2?1u:0u));
                    Assert.That(result.Features[i].PointAxis,Is.EqualTo((uint)i));
                }
            }
        }

        [Test] public void SeparatedBoxesKeepRowsEmpty()
        {
            Assume.That(SystemInfo.supportsComputeShaders,Is.True);
            var bodies=new[]{new CoupledContactBody{Center=Vector2.zero,InverseMass=1},
                new CoupledContactBody{Center=new Vector2(3,0),InverseMass=1}};
            using(var batch=new CoupledContactRowBatch(bodies,
                new[]{Box(Vector2.zero,0),Box(new Vector2(3,0),1)},
                new Vector2(-8,-8),1,16,16))
            {
                var result=batch.Build();
                Assert.That(result.Success,Is.True);
                Assert.That(result.RowCount,Is.Zero);
                Assert.That(result.Rows,Is.Empty);
            }
        }
    }
}
