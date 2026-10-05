using System;
using System.Runtime.InteropServices;
using Debris.Simulation.CoupledContacts;
using NUnit.Framework;
using UnityEngine;

namespace Debris.Simulation.Tests
{
    public sealed class CoupledContactOperatorTests
    {
        [Test] public void EmptyFrozenGraphKeepsFreeMotionAcrossRepeatedProducts()
        {
            Assume.That(SystemInfo.supportsComputeShaders,Is.True);
            var bodies=new[]{new CoupledContactBody{Center=Vector2.zero,InverseMass=1,InverseInertia=1}};
            using(var op=new CoupledContactOperator(bodies,Array.Empty<CoupledContactRow>()))
            {
                var free=new[]{new Vector4(2,-3,.25f,0)};
                for(int i=0;i<2;i++)
                {
                    var result=op.Apply(free,Array.Empty<float>());
                    Assert.That(op.SegmentCount,Is.Zero);
                    Assert.That(result.RowVelocity,Is.Empty);
                    Assert.That(result.EndpointMotion[0],Is.EqualTo(free[0]));
                }
            }
        }
        [Test] public void FrozenGpuProductMatchesOffCenterTwoBodyMassAndTorque()
        {
            Assert.That(Marshal.SizeOf<CoupledContactBody>(),Is.EqualTo(16));
            Assert.That(Marshal.SizeOf<CoupledContactRow>(),Is.EqualTo(32));
            Assume.That(SystemInfo.supportsComputeShaders,Is.True);
            var bodies=new[]
            {
                new CoupledContactBody{Center=Vector2.zero,InverseMass=1,InverseInertia=1},
                new CoupledContactBody{Center=Vector2.right,InverseMass=1,InverseInertia=1}
            };
            float root=Mathf.Sqrt(2.125f);
            var row=new CoupledContactRow{A=0,B=1,Point=new Vector2(.5f,.25f),
                Direction=Vector2.right,InverseRoot=1/root};
            using(var op=new CoupledContactOperator(bodies,new[]{row}))
            {
                var result=op.Apply(new Vector4[2],new[]{root});
                Assert.That(result.EndpointMotion[0].x,Is.EqualTo(-1).Within(1e-5));
                Assert.That(result.EndpointMotion[1].x,Is.EqualTo(1).Within(1e-5));
                Assert.That(result.EndpointMotion[0].z,Is.EqualTo(.25).Within(1e-5));
                Assert.That(result.EndpointMotion[1].z,Is.EqualTo(-.25).Within(1e-5));
                Assert.That(result.RowVelocity[0],Is.EqualTo(root).Within(1e-5));
                var repeated=op.Apply(new Vector4[2],new[]{root});
                Assert.That(repeated.RowVelocity[0],Is.EqualTo(result.RowVelocity[0]).Within(1e-6));
            }
        }

        [TestCase(200),TestCase(4096),Explicit("V2-1 high-degree GPU operator workload")]
        public void SegmentedFiniteHullReactionIncludesEveryIncidentPoint(int count)
        {
            Assume.That(SystemInfo.supportsComputeShaders,Is.True);
            var bodies=new CoupledContactBody[count+1];
            var rows=new CoupledContactRow[count];var impulses=new float[count];
            bodies[0]=new CoupledContactBody{Center=Vector2.zero,InverseMass=.01f,InverseInertia=.01f};
            float root=Mathf.Sqrt(1.01f);
            for(int i=0;i<count;i++)
            {
                bodies[i+1]=new CoupledContactBody{Center=Vector2.right,InverseMass=1,InverseInertia=1};
                rows[i]=new CoupledContactRow{A=0,B=(uint)(i+1),Point=new Vector2(.5f,0),
                    Direction=Vector2.right,InverseRoot=1/root};
                impulses[i]=root;
            }
            using(var op=new CoupledContactOperator(bodies,rows))
            {
                var result=op.Apply(new Vector4[count+1],impulses);
                TestContext.WriteLine("points={0} segments={1} hullSegments={2} bytes={3}",
                    count,op.SegmentCount,op.MaxSegmentsPerBody,op.BufferBytes);
                Assert.That(op.MaxSegmentsPerBody,Is.EqualTo((count+63)/64));
                Assert.That(result.EndpointMotion[0].x,Is.EqualTo(-count*.01f).Within(2e-3));
                for(int i=0;i<count;i++)
                {
                    Assert.That(result.EndpointMotion[i+1].x,Is.EqualTo(1).Within(1e-5));
                    Assert.That(result.RowVelocity[i],Is.EqualTo((1+count*.01f)/root).Within(2e-3));
                }
            }
        }
    }
}
