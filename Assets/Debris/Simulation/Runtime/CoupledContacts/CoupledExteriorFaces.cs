using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Debris.Simulation.CoupledContacts
{
    [StructLayout(LayoutKind.Sequential, Pack=4, Size=20)]
    public struct CoupledUnionPatch
    {
        public Vector2 Center,Half;
        public uint Feature;
    }

    [StructLayout(LayoutKind.Sequential, Pack=4, Size=32)]
    public struct CoupledExteriorFace
    {
        public Vector2 A,B,Normal;
        public uint Feature,Side;
    }

    // Topology-time builder, never a per-frame scan. Patches are axis-aligned
    // in one rigid body's local frame. Output intervals are the union boundary.
    public static class CoupledExteriorFaces
    {
        public const int MaxFaces=4096;
        const double Tolerance=1e-8;

        public static CoupledExteriorFace[] Build(IReadOnlyList<CoupledUnionPatch> patches)
        {
            if(patches==null)throw new ArgumentNullException(nameof(patches));
            var features=new HashSet<uint>();
            foreach(var p in patches)
            {
                if(!Finite(p.Center.x)||!Finite(p.Center.y)||!Finite(p.Half.x)||!Finite(p.Half.y)||
                   p.Half.x<=0||p.Half.y<=0||!features.Add(p.Feature))
                    throw new ArgumentException("Invalid or duplicate union patch feature",nameof(patches));
            }
            var faces=new List<CoupledExteriorFace>();
            for(int i=0;i<patches.Count;i++)for(uint side=0;side<4;side++)
            {
                var p=patches[i];bool horizontal=(side&1)==0;
                double fixedAxis=side==0?p.Center.y-p.Half.y:
                    side==2?p.Center.y+p.Half.y:
                    side==1?p.Center.x+p.Half.x:p.Center.x-p.Half.x;
                double lo=horizontal?p.Center.x-p.Half.x:p.Center.y-p.Half.y;
                double hi=horizontal?p.Center.x+p.Half.x:p.Center.y+p.Half.y;
                var cuts=new List<double>{lo,hi};
                foreach(var q in patches)
                {
                    double start=horizontal?q.Center.x-q.Half.x:q.Center.y-q.Half.y;
                    double end=horizontal?q.Center.x+q.Half.x:q.Center.y+q.Half.y;
                    if(start>lo+Tolerance&&start<hi-Tolerance)cuts.Add(start);
                    if(end>lo+Tolerance&&end<hi-Tolerance)cuts.Add(end);
                }
                cuts.Sort();
                Vector2 normal=side==0?Vector2.down:side==1?Vector2.right:
                    side==2?Vector2.up:Vector2.left;
                for(int cut=0;cut+1<cuts.Count;cut++)
                {
                    double start=cuts[cut],end=cuts[cut+1];
                    if(end-start<=Tolerance)continue;
                    double mid=(start+end)*.5;
                    double px=horizontal?mid:fixedAxis,py=horizontal?fixedAxis:mid;
                    if(Inside(patches,px+normal.x*Tolerance,py+normal.y*Tolerance))continue;
                    bool duplicate=false;
                    for(int earlier=0;earlier<i;earlier++)
                        if(OnFace(patches[earlier],side,px,py)){duplicate=true;break;}
                    if(duplicate)continue;
                    var a=horizontal?new Vector2((float)start,(float)fixedAxis):
                        new Vector2((float)fixedAxis,(float)start);
                    var b=horizontal?new Vector2((float)end,(float)fixedAxis):
                        new Vector2((float)fixedAxis,(float)end);
                    faces.Add(new CoupledExteriorFace{A=a,B=b,Normal=normal,
                        Feature=p.Feature,Side=side});
                }
            }
            // Merge coplanar contiguous pieces before contact generation. One
            // primitive interval then owns an adjacent patch seam endpoint.
            bool changed;
            do
            {
                changed=false;
                for(int i=0;i<faces.Count&&!changed;i++)for(int j=i+1;j<faces.Count;j++)
                {
                    var a=faces[i];var b=faces[j];
                    if(a.Side!=b.Side||Vector2.Distance(a.Normal,b.Normal)>Tolerance)continue;
                    Vector2 axis=a.B-a.A;
                    if(Math.Abs(axis.x*(b.A.y-a.A.y)-axis.y*(b.A.x-a.A.x))>Tolerance)continue;
                    if(Vector2.Distance(a.B,b.A)<=Tolerance)
                    {a.B=b.B;faces[i]=a;faces.RemoveAt(j);changed=true;break;}
                    if(Vector2.Distance(b.B,a.A)<=Tolerance)
                    {a.A=b.A;faces[i]=a;faces.RemoveAt(j);changed=true;break;}
                }
            }while(changed);
            if(faces.Count>MaxFaces)throw new InvalidOperationException("V2 exterior face capacity exceeded");
            return faces.ToArray();
        }

        public static bool ContainsPoint(IReadOnlyList<CoupledUnionPatch> patches,Vector2 point)
        {
            if(patches==null)throw new ArgumentNullException(nameof(patches));
            return Inside(patches,point.x,point.y);
        }

        static bool Inside(IReadOnlyList<CoupledUnionPatch> patches,double x,double y)
        {
            foreach(var p in patches)
                if(x>=p.Center.x-p.Half.x&&x<=p.Center.x+p.Half.x&&
                   y>=p.Center.y-p.Half.y&&y<=p.Center.y+p.Half.y)return true;
            return false;
        }
        static bool OnFace(CoupledUnionPatch p,uint side,double x,double y)
        {
            double fixedAxis=side==0?p.Center.y-p.Half.y:
                side==2?p.Center.y+p.Half.y:
                side==1?p.Center.x+p.Half.x:p.Center.x-p.Half.x;
            return (side&1)==0?
                Math.Abs(y-fixedAxis)<=Tolerance&&x>=p.Center.x-p.Half.x-Tolerance&&x<=p.Center.x+p.Half.x+Tolerance:
                Math.Abs(x-fixedAxis)<=Tolerance&&y>=p.Center.y-p.Half.y-Tolerance&&y<=p.Center.y+p.Half.y+Tolerance;
        }
        static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
    }
}
