using System.Linq;
using System.Runtime.InteropServices;
using Debris.Simulation.CoupledContacts;
using NUnit.Framework;
using UnityEngine;

namespace Debris.Simulation.Tests
{
    public sealed class CoupledExteriorFacesTests
    {
        static CoupledUnionPatch Patch(float x,float y,float halfX,float halfY,uint feature)
            =>new CoupledUnionPatch{Center=new Vector2(x,y),Half=new Vector2(halfX,halfY),Feature=feature};

        [Test] public void AdjacentPatchesMergeWithoutInternalOrDuplicateSupport()
        {
            Assert.That(Marshal.SizeOf<CoupledUnionPatch>(),Is.EqualTo(20));
            Assert.That(Marshal.SizeOf<CoupledExteriorFace>(),Is.EqualTo(32));
            var faces=CoupledExteriorFaces.Build(new[]{
                Patch(0,0,.5f,.5f,1),Patch(1,0,.5f,.5f,2)});
            Assert.That(faces.Length,Is.EqualTo(4));
            Assert.That(faces.Count(f=>f.Side==0),Is.EqualTo(1));
            Assert.That(faces.Count(f=>f.Side==2),Is.EqualTo(1));
            Assert.That(faces.Any(f=>Mathf.Abs(f.A.x-.5f)<1e-6f&&
                Mathf.Abs(f.B.x-.5f)<1e-6f),Is.False);
        }

        [Test] public void ConcaveCavityKeepsDistinctInteriorWalls()
        {
            var patches=new[]{Patch(0,-1,1.5f,.5f,1),Patch(0,1,1.5f,.5f,2),
                Patch(-1,0,.5f,.5f,3),Patch(1,0,.5f,.5f,4)};
            var faces=CoupledExteriorFaces.Build(patches);
            Assert.That(CoupledExteriorFaces.ContainsPoint(patches,Vector2.zero),Is.False);
            Assert.That(faces.Any(f=>f.Normal==Vector2.right&&Mathf.Abs(f.A.x+.5f)<1e-6f),Is.True);
            Assert.That(faces.Any(f=>f.Normal==Vector2.left&&Mathf.Abs(f.A.x-.5f)<1e-6f),Is.True);
        }

        [Test] public void ThinOccupiedStripKeepsBothExitsForQueryPolicy()
        {
            var patches=new[]{Patch(0,0,.1f,2,1)};
            var faces=CoupledExteriorFaces.Build(patches);
            Assert.That(CoupledExteriorFaces.ContainsPoint(patches,Vector2.zero),Is.True);
            Assert.That(faces.Any(f=>f.Normal==Vector2.left),Is.True);
            Assert.That(faces.Any(f=>f.Normal==Vector2.right),Is.True);
        }
    }
}
