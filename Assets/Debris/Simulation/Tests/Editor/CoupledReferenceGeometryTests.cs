using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Debris.Simulation.Tests
{
    public sealed class CoupledReferenceGeometryTests
    {
        static DBox Box(double x,double y,double angle=0,ulong id=1)=>new DBox(new DVec(x,y),new DVec(.5,.5),angle,id);
        static void Near(double actual,double expected,double tolerance=1e-10)=>Assert.That(actual,Is.EqualTo(expected).Within(tolerance));
        [Test] public void SeparatedTouchingPenetratingAndFlatTwoPointSupport()
        {
            Assert.That(CoupledReferenceGeometry.BoxBox(Box(0,0),Box(1.1,0,0,2)).Length,Is.Zero);
            var touch=CoupledReferenceGeometry.BoxBox(Box(0,0),Box(1,0,0,2));Assert.That(touch.Length,Is.EqualTo(2));
            foreach(var p in touch){Near(p.Gap,0);Near(p.Normal.X,1);Near(p.Normal.Y,0);Near(p.AnchorA.X,.5);Near(p.AnchorB.X,.5);Near(p.Midpoint.X,.5);}
            Near(Math.Abs(touch[0].Midpoint.Y-touch[1].Midpoint.Y),1);
            var pen=CoupledReferenceGeometry.BoxBox(Box(0,0),Box(.8,0,0,2));Assert.That(pen.Length,Is.EqualTo(2));
            foreach(var p in pen){Near(p.Gap,-.2);Near(p.AnchorA.X,.5);Near(p.AnchorB.X,.3);Near(p.LocalA.X,.5);Near(p.LocalB.X,-.5);}
        }
        [Test] public void CornerRotationReversalTranslationAndDiagonalTie()
        {
            var corner=CoupledReferenceGeometry.BoxBox(Box(0,0),Box(1,1,0,2));Assert.That(corner.Length,Is.EqualTo(1));Near(corner[0].Gap,0);
            var rotated=CoupledReferenceGeometry.BoxBox(Box(0,0,Math.PI/4),Box(Math.Sqrt(2),0,Math.PI/4,2));
            Assert.That(rotated.Length,Is.EqualTo(1));Near(rotated[0].Gap,0,1e-9);
            var original=CoupledReferenceGeometry.BoxBox(Box(0,0),Box(.8,0,0,2));
            var reverse=CoupledReferenceGeometry.BoxBox(Box(.8,0,0,2),Box(0,0));
            Assert.That(reverse.Length,Is.EqualTo(2));Near(reverse[0].Normal.X,-1);Near(reverse[0].Gap,-.2);
            var shifted=CoupledReferenceGeometry.BoxBox(Box(17,-23),Box(17.8,-23,0,2));
            Near(shifted[0].Gap,original[0].Gap);Near(shifted[0].LocalA.X,original[0].LocalA.X);Near(shifted[0].Midpoint.X-original[0].Midpoint.X,17);
            // A diagonal touch has two equal SAT axes but one physical constraint.
            var tie=CoupledReferenceGeometry.BoxBox(Box(0,0),Box(1,1,0,2),0,1);
            Assert.That(tie.Length,Is.EqualTo(1));Near(tie[0].Normal.X,1);
        }
        [Test] public void OverlapIntervalCollapsesToOneAndFeatureKeysAreStable()
        {
            var one=CoupledReferenceGeometry.BoxBox(Box(0,0),Box(1,1,0,2));
            Assert.That(one.Length,Is.EqualTo(1));
            var again=CoupledReferenceGeometry.BoxBox(Box(0,0),Box(1,1,0,2));Assert.That(again[0].Key,Is.EqualTo(one[0].Key));
            var two=CoupledReferenceGeometry.BoxBox(Box(0,0),Box(1,0,0,2));Assert.That(two.Length,Is.EqualTo(2));Assert.That(two[0].Key,Is.Not.EqualTo(two[1].Key));
        }
        [Test] public void UnionRejectsSeamsDeduplicatesSupportAndSplitsPartialExposure()
        {
            var adjacent=new[]{new CoupledReferenceGeometry.Patch(new DVec(0,0),new DVec(.5,.5),1),new CoupledReferenceGeometry.Patch(new DVec(1,0),new DVec(.5,.5),2)};
            var edges=CoupledReferenceGeometry.Exterior(adjacent);Assert.That(edges.Count,Is.EqualTo(4));
            foreach(var e in edges)Assert.That(!(Math.Abs(e.A.X-.5)<1e-12 && Math.Abs(e.B.X-.5)<1e-12),Is.True);
            var seamSupport=CoupledReferenceGeometry.SquareUnion(Box(.5,1),adjacent);
            Assert.That(seamSupport.Length,Is.EqualTo(2));foreach(var c in seamSupport){Near(c.Gap,0);Near(c.Normal.Y,-1);}
            var frame=new DBox(new DVec(10,-7),new DVec(1,1),Math.PI/2,77);
            var worldSquare=Box(9,-6.5,Math.PI/2);
            var transformed=CoupledReferenceGeometry.SquareUnion(worldSquare,adjacent,frame);
            Assert.That(transformed.Length,Is.EqualTo(2));foreach(var c in transformed){Near(c.Gap,0,1e-9);Near(c.Normal.X,1,1e-9);}
            var duplicate=new[]{adjacent[0],new CoupledReferenceGeometry.Patch(new DVec(0,0),new DVec(.5,.5),2)};
            Assert.That(CoupledReferenceGeometry.Exterior(duplicate).Count,Is.EqualTo(4));
            var partial=new[]{new CoupledReferenceGeometry.Patch(new DVec(0,0),new DVec(1,1),3),new CoupledReferenceGeometry.Patch(new DVec(1.5,.5),new DVec(.5,.5),4)};
            var visible=CoupledReferenceGeometry.Exterior(partial);double rightLength=0;
            foreach(var e in visible)if(e.Feature==3 && e.Side==1)rightLength+=(e.B-e.A).Length;
            Near(rightLength,1); // lower half of the old right face remains exposed
            var partialSupport=CoupledReferenceGeometry.SquareUnion(Box(1.5,-.5),partial);
            int exposedRight=0;foreach(var c in partialSupport)if(Math.Abs(c.Normal.X+1)<1e-12 && c.Key.Contains("/3:1/"))exposedRight++;
            Assert.That(exposedRight,Is.EqualTo(2));
        }
        [Test] public void ConcaveCornerCavityAndDeepContainment()
        {
            var l=new[]{new CoupledReferenceGeometry.Patch(new DVec(0,0),new DVec(2,.5),1),new CoupledReferenceGeometry.Patch(new DVec(-1.5,1),new DVec(.5,.5),2)};
            var edges=CoupledReferenceGeometry.Exterior(l);bool innerHorizontal=false,innerVertical=false;
            foreach(var e in edges){if(e.Feature==1&&e.Side==2&&e.A.X>= -1)innerHorizontal=true;if(e.Feature==2&&e.Side==1&&e.A.Y>=.5)innerVertical=true;}
            Assert.That(innerHorizontal&&innerVertical,Is.True);
            var cornerContacts=CoupledReferenceGeometry.SquareUnion(Box(-.75,.75),l);
            bool horizontalNormal=false,verticalNormal=false;
            foreach(var c in cornerContacts){if(c.Key.Contains("/1:2/"))verticalNormal=true;if(c.Key.Contains("/2:1/"))horizontalNormal=true;}
            Assert.That(horizontalNormal&&verticalNormal,Is.True);
            Assert.That(CoupledReferenceGeometry.SquareContained(l,Box(1,1)),Is.False);
            var solid=new[]{new CoupledReferenceGeometry.Patch(new DVec(0,0),new DVec(5,5),5)};
            Assert.That(CoupledReferenceGeometry.SquareContained(solid,Box(0,0)),Is.True);
            var ring=new[]{new CoupledReferenceGeometry.Patch(new DVec(-1.5,0),new DVec(.5,2),1),new CoupledReferenceGeometry.Patch(new DVec(1.5,0),new DVec(.5,2),2),new CoupledReferenceGeometry.Patch(new DVec(0,-1.5),new DVec(1,.5),3),new CoupledReferenceGeometry.Patch(new DVec(0,1.5),new DVec(1,.5),4)};
            Assert.That(CoupledReferenceGeometry.SquareContained(ring,Box(0,0)),Is.False);
            Assert.That(CoupledReferenceGeometry.SquareContained(ring,Box(0,0,.4)),Is.False);
            Assert.That(CoupledReferenceGeometry.SquareContained(solid,Box(0,0,.4)),Is.True);
        }
        [Test] public void ThinOccupiedStripChoosesOneExitFace()
        {
            var strip=new[]{new CoupledReferenceGeometry.Patch(new DVec(0,0),new DVec(.25,1),9)};
            var centered=CoupledReferenceGeometry.SquareUnion(Box(0,0),strip);
            Assert.That(centered.Length,Is.EqualTo(2));
            foreach(var c in centered){Near(c.Gap,-.75);Near(c.Normal.X,-1);}
            var left=CoupledReferenceGeometry.SquareUnion(Box(-.1,0),strip);
            Assert.That(left.Length,Is.EqualTo(2));
            foreach(var c in left)Near(c.Normal.X,1);
            var right=CoupledReferenceGeometry.SquareUnion(Box(.1,0),strip);
            Assert.That(right.Length,Is.EqualTo(2));
            foreach(var c in right)Near(c.Normal.X,-1);
        }
    }
}
