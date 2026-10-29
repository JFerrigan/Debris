using System;
using System.Runtime.InteropServices;
using Debris.Simulation.CoupledContacts;
using NUnit.Framework;
using UnityEngine;

namespace Debris.Simulation.Tests
{
    public sealed class CoupledUnionManifoldProbeTests
    {
        static CoupledUnionPatch Patch(float x,float y,float hx,float hy,uint feature)
            =>new CoupledUnionPatch{Center=new Vector2(x,y),Half=new Vector2(hx,hy),Feature=feature};
        static CoupledUnionQuery Query(uint face)
            =>new CoupledUnionQuery{SquareCenter=Vector2.zero,SquareHalf=Vector2.one*.5f,
                SquareEndpoint=0,UnionEndpoint=1,FaceIndex=face,GroupIndex=0};

        [Test] public void OccupiedThinStripChoosesPositiveXExitOnly()
        {
            Assert.That(Marshal.SizeOf<CoupledUnionQuery>(),Is.EqualTo(40));
            Assert.That(Marshal.SizeOf<CoupledUnionQueryGroup>(),Is.EqualTo(32));
            Assume.That(SystemInfo.supportsComputeShaders,Is.True);
            var patches=new[]{Patch(0,0,.1f,2,1)};
            var faces=CoupledExteriorFaces.Build(patches);
            int left=Array.FindIndex(faces,f=>f.Normal==Vector2.left);
            int right=Array.FindIndex(faces,f=>f.Normal==Vector2.right);
            Assert.That(left,Is.GreaterThanOrEqualTo(0));
            Assert.That(right,Is.GreaterThanOrEqualTo(0));
            var group=new CoupledUnionQueryGroup{PatchCount=1,QueryCount=2};
            using(var probe=new CoupledUnionManifoldProbe(patches,faces,
                new[]{Query((uint)left),Query((uint)right)},new[]{group},2))
            {
                var result=probe.Build();
                Assert.That(result.Success,Is.True);
                Assert.That(result.Manifolds[0].Count,Is.EqualTo(2));
                Assert.That(result.Manifolds[0].Normal.x,Is.EqualTo(1).Within(1e-5));
                Assert.That(result.Manifolds[1].Count,Is.Zero);
            }
        }

        [Test] public void EmptyCavityKeepsOpposingDistinctWalls()
        {
            Assume.That(SystemInfo.supportsComputeShaders,Is.True);
            var patches=new[]{Patch(0,-1,1.5f,.5f,1),Patch(0,1,1.5f,.5f,2),
                Patch(-1,0,.5f,.5f,3),Patch(1,0,.5f,.5f,4)};
            var faces=CoupledExteriorFaces.Build(patches);
            int left=Array.FindIndex(faces,f=>f.Normal==Vector2.right&&
                Mathf.Abs(f.A.x+.5f)<1e-6f);
            int right=Array.FindIndex(faces,f=>f.Normal==Vector2.left&&
                Mathf.Abs(f.A.x-.5f)<1e-6f);
            Assert.That(left,Is.GreaterThanOrEqualTo(0));
            Assert.That(right,Is.GreaterThanOrEqualTo(0));
            var group=new CoupledUnionQueryGroup{PatchCount=(uint)patches.Length,QueryCount=2};
            using(var probe=new CoupledUnionManifoldProbe(patches,faces,
                new[]{Query((uint)left),Query((uint)right)},new[]{group},2))
            {
                var result=probe.Build();
                Assert.That(result.Success,Is.True);
                Assert.That(result.Manifolds[0].Count,Is.GreaterThan(0));
                Assert.That(result.Manifolds[1].Count,Is.GreaterThan(0));
            }
        }
    }
}
