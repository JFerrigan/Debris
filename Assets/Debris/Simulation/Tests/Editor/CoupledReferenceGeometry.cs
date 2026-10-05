using System;
using System.Collections.Generic;

namespace Debris.Simulation.Tests
{
    internal struct DVec
    {
        public double X, Y;
        public DVec(double x, double y) { X=x; Y=y; }
        public static DVec operator +(DVec a,DVec b)=>new DVec(a.X+b.X,a.Y+b.Y);
        public static DVec operator -(DVec a,DVec b)=>new DVec(a.X-b.X,a.Y-b.Y);
        public static DVec operator *(DVec a,double k)=>new DVec(a.X*k,a.Y*k);
        public static DVec operator /(DVec a,double k)=>new DVec(a.X/k,a.Y/k);
        public double Dot(DVec b)=>X*b.X+Y*b.Y;
        public double Cross(DVec b)=>X*b.Y-Y*b.X;
        public double Length=>Math.Sqrt(Dot(this));
    }
    internal struct DBox
    {
        public DVec Center, Half;
        public double Angle;
        public ulong Identity;
        public uint Revision;
        public DBox(DVec center,DVec half,double angle=0,ulong identity=0,uint revision=0)
        { Center=center; Half=half; Angle=angle; Identity=identity; Revision=revision; }
        public DVec Local(DVec world)
        { double c=Math.Cos(Angle),s=Math.Sin(Angle); DVec p=world-Center; return new DVec(c*p.X+s*p.Y,-s*p.X+c*p.Y); }
        public DVec World(DVec local)
        { double c=Math.Cos(Angle),s=Math.Sin(Angle); return Center+new DVec(c*local.X-s*local.Y,s*local.X+c*local.Y); }
        public DVec[] Vertices() => new[]{World(new DVec(-Half.X,-Half.Y)),World(new DVec(Half.X,-Half.Y)),World(new DVec(Half.X,Half.Y)),World(new DVec(-Half.X,Half.Y))};
    }
    internal struct DContact
    {
        public DVec Normal, AnchorA, AnchorB, Midpoint, LocalA, LocalB;
        public double Gap;
        public string Key;
    }
    internal static class CoupledReferenceGeometry
    {
        public const double Dedup=1e-5, AxisTie=1e-6;
        static DVec Normal(DVec a,DVec b) { DVec d=b-a; return new DVec(d.Y,-d.X)/d.Length; }
        static double Separation(DVec[] from,DVec[] to,DVec n)
        { double hi=double.NegativeInfinity,lo=double.PositiveInfinity; foreach(var p in from)hi=Math.Max(hi,p.Dot(n)); foreach(var p in to)lo=Math.Min(lo,p.Dot(n)); return lo-hi; }
        static double MaximumSat(DVec[] a,DVec[] b)
        {
            double maximum=double.NegativeInfinity;
            for(int i=0;i<8;i++)
            {DVec[] from=i<4?a:b,to=i<4?b:a;int side=i%4;
             maximum=Math.Max(maximum,Separation(from,to,Normal(from[side],from[(side+1)%4])));}
            return maximum;
        }
        // The previous reference is a face ID: 0..3 on A, 4..7 on B.
        public static DContact[] BoxBox(DBox a,DBox b,double margin=0,int previousFace=-1)
        {
            DVec[] av=a.Vertices(),bv=b.Vertices(); double best=double.NegativeInfinity; int face=-1;
            for(int i=0;i<8;i++)
            { var from=i<4?av:bv; var to=i<4?bv:av; int edge=i%4;
              double sep=Separation(from,to,Normal(from[edge],from[(edge+1)%4]));
              if(sep>best+AxisTie || (Math.Abs(sep-best)<=AxisTie && (face<0 || i<face))) { best=sep; face=i; }
            }
            if(previousFace>=0 && previousFace<8)
            { var from=previousFace<4?av:bv; var to=previousFace<4?bv:av; int edge=previousFace%4;
              if(best-Separation(from,to,Normal(from[edge],from[(edge+1)%4]))<=AxisTie) face=previousFace;
            }
            if(best>margin+1e-12) return Array.Empty<DContact>();
            bool swap=face>=4; DVec[] reference=swap?bv:av, incident=swap?av:bv;
            int f=face%4; DVec r0=reference[f],r1=reference[(f+1)%4];
            DVec outward=Normal(r0,r1), tangent=(r1-r0)/(r1-r0).Length;
            int inc=0; double antiparallel=double.PositiveInfinity;
            for(int i=0;i<4;i++) { double d=Normal(incident[i],incident[(i+1)%4]).Dot(outward); if(d<antiparallel) {antiparallel=d;inc=i;} }
            DVec q0=incident[inc],q1=incident[(inc+1)%4];
            double u0=(q0-r0).Dot(tangent),u1=(q1-r0).Dot(tangent),length=(r1-r0).Length;
            double low=Math.Max(0,Math.Min(u0,u1)), high=Math.Min(length,Math.Max(u0,u1));
            if(high<low-1e-12) return Array.Empty<DContact>();
            var result=new List<DContact>(2);
            foreach(double u in new[]{low,high})
            { double fraction=Math.Abs(u1-u0)<1e-14?0:(u-u0)/(u1-u0);
              DVec qi=q0+(q1-q0)*fraction,qr=r0+tangent*u;
              double gap=(qi-qr).Dot(outward);
              if(gap>margin+1e-12) continue;
              DVec aa=swap?qi:qr,bb=swap?qr:qi;
              DVec mid=(aa+bb)*.5;
              if(result.Count>0 && (mid-result[0].Midpoint).Length<Dedup) continue;
              string provenance=fraction<=1e-12?"v0":fraction>=1-1e-12?"v1":u<=1e-12?"side0":"side1";
              result.Add(new DContact {Normal=swap?outward*-1:outward,AnchorA=aa,AnchorB=bb,
                  Midpoint=mid,LocalA=a.Local(aa),LocalB=b.Local(bb),Gap=gap,
                  Key=a.Identity+":"+a.Revision+"/"+b.Identity+":"+b.Revision+"/"+face+"/"+inc+"/"+provenance});
            }
            return result.ToArray();
        }
        internal struct Patch { public DVec Center,Half; public ulong Feature; public Patch(DVec c,DVec h,ulong f){Center=c;Half=h;Feature=f;} }
        internal struct Exposed { public DVec A,B,Normal; public ulong Feature; public int Side; }
        // Axis-aligned patches are in one body's local frame. Split at every collinear
        // endpoint, then test a point immediately across each interval against the union.
        public static List<Exposed> Exterior(IReadOnlyList<Patch> patches)
        {
            var result=new List<Exposed>();
            for(int i=0;i<patches.Count;i++) for(int side=0;side<4;side++)
            { var p=patches[i]; double fixedAxis=(side==0?p.Center.Y-p.Half.Y:side==2?p.Center.Y+p.Half.Y:side==1?p.Center.X+p.Half.X:p.Center.X-p.Half.X);
              bool horizontal=side%2==0; double lo=horizontal?p.Center.X-p.Half.X:p.Center.Y-p.Half.Y, hi=horizontal?p.Center.X+p.Half.X:p.Center.Y+p.Half.Y;
              var cuts=new List<double>{lo,hi};
              foreach(var q in patches) {double l=horizontal?q.Center.X-q.Half.X:q.Center.Y-q.Half.Y,h=horizontal?q.Center.X+q.Half.X:q.Center.Y+q.Half.Y;
                  if(l>lo && l<hi)cuts.Add(l); if(h>lo && h<hi)cuts.Add(h); }
              cuts.Sort();
              for(int j=0;j<cuts.Count-1;j++)
              { double l=cuts[j],h=cuts[j+1]; if(h-l<1e-12)continue;
                double mid=(l+h)*.5; DVec normal=side==0?new DVec(0,-1):side==1?new DVec(1,0):side==2?new DVec(0,1):new DVec(-1,0);
                DVec point=horizontal?new DVec(mid,fixedAxis):new DVec(fixedAxis,mid);
                if(Inside(patches,point+normal*1e-8))continue;
                bool owned=false;
                for(int k=0;k<i;k++) if(OnFace(patches[k],side,point)) {owned=true;break;}
                if(owned)continue;
                result.Add(new Exposed{A=horizontal?new DVec(l,fixedAxis):new DVec(fixedAxis,l),B=horizontal?new DVec(h,fixedAxis):new DVec(fixedAxis,h),Normal=normal,Feature=p.Feature,Side=side});
              }
            }
            // Adjacent coplanar exposed spans describe one support face. Merge
            // them before a grain can see two constraints at their shared end.
            bool changed;
            do
            { changed=false;
              for(int i=0;i<result.Count&&!changed;i++)for(int j=i+1;j<result.Count;j++)
              {var a=result[i];var b=result[j];
               if((a.Normal-b.Normal).Length>1e-12 || Math.Abs((a.B-a.A).Cross(b.A-a.A))>1e-12)continue;
               if((a.B-b.A).Length<1e-12){a.B=b.B;result[i]=a;result.RemoveAt(j);changed=true;break;}
               if((b.B-a.A).Length<1e-12){a.A=b.A;result[i]=a;result.RemoveAt(j);changed=true;break;}
              }
            }while(changed);
            return result;
        }
        // A is a square, B is the occupied rectangle union. Only exterior
        // intervals can supply a physical contact; containment is separate.
        public static DContact[] SquareUnion(DBox square,IReadOnlyList<Patch> patches,double margin=0,ulong unionIdentity=0,uint unionRevision=0)
            => SquareUnionPrepared(square,patches,Exterior(patches),margin,unionIdentity,unionRevision);
        public static DContact[] SquareUnionPrepared(DBox square,IReadOnlyList<Patch> patches,IReadOnlyList<Exposed> faces,double margin=0,ulong unionIdentity=0,uint unionRevision=0)
        {
            var vertices=square.Vertices();var contacts=new List<DContact>();
            foreach(var face in faces)
            {double radius=0;foreach(var v in vertices)radius=Math.Max(radius,Math.Abs((v-square.Center).Dot(face.Normal)));
             if((square.Center-face.A).Dot(face.Normal)<-radius-1e-12)continue;
             DVec tangent=(face.B-face.A)/(face.B-face.A).Length;int incident=0;double smallest=double.PositiveInfinity;
             for(int i=0;i<4;i++){double projection=Normal(vertices[i],vertices[(i+1)%4]).Dot(face.Normal);if(projection<smallest){smallest=projection;incident=i;}}
             DVec q0=vertices[incident],q1=vertices[(incident+1)%4];double u0=(q0-face.A).Dot(tangent),u1=(q1-face.A).Dot(tangent);
             double low=Math.Max(0,Math.Min(u0,u1)),high=Math.Min((face.B-face.A).Length,Math.Max(u0,u1));if(high<low-1e-12)continue;
             // Exterior membership alone is insufficient. A square beside a
             // rectangle also clips its top/bottom edges with a deep negative
             // gap. Retain this exterior face only when its axis is a winning
             // SAT axis for an owning patch under the clipped interval.
             DVec intervalMid=face.A+tangent*((low+high)*.5);bool winning=false;
             foreach(var patch in patches)
             {
                 if(!OnFace(patch,face.Side,intervalMid))continue;
                 var box=new DBox(patch.Center,patch.Half);DVec[] pv=box.Vertices();
                 double faceSeparation=Separation(pv,vertices,face.Normal);
                 if(faceSeparation>=MaximumSat(pv,vertices)-AxisTie){winning=true;break;}
             }
             if(!winning)continue;
             foreach(double u in new[]{low,high})
             {double fraction=Math.Abs(u1-u0)<1e-14?0:(u-u0)/(u1-u0);DVec qa=q0+(q1-q0)*fraction,qb=face.A+tangent*u;
              double gap=(qa-qb).Dot(face.Normal);if(gap>margin+1e-12)continue;
              string provenance=fraction<=1e-12?"v0":fraction>=1-1e-12?"v1":u<=1e-12?"side0":"side1";
              string key=square.Identity+":"+square.Revision+"/"+unionIdentity+":"+unionRevision+"/"+face.Feature+":"+face.Side+"/"+incident+"/"+provenance;
              DVec mid=(qa+qb)*.5;bool duplicate=false;foreach(var old in contacts)if(old.Key==key || ((old.Midpoint-mid).Length<Dedup && (old.Normal+face.Normal).Length<Dedup)){duplicate=true;break;}
              if(duplicate)continue;
              contacts.Add(new DContact{Normal=face.Normal*-1,AnchorA=qa,AnchorB=qb,Midpoint=mid,LocalA=square.Local(qa),LocalB=qb,Gap=gap,
                  Key=key});
             }
            }
            // A square that straddles a thin occupied strip sees both exterior
            // faces. Those are alternative exit directions, not simultaneous
            // nonpenetration constraints. Pick the shorter translation; on a
            // tie use the stable normal order (+X exit before -X exit).
            if(Inside(patches,square.Center))
            {
                for(int i=0;i<contacts.Count;i++)for(int j=i+1;j<contacts.Count;j++)
                {
                    if((contacts[i].Normal+contacts[j].Normal).Length>1e-8)continue;
                    double gi=contacts[i].Gap,gj=contacts[j].Gap;
                    bool removeI=gi<gj-1e-10 || (Math.Abs(gi-gj)<=1e-10 &&
                        (contacts[i].Normal.X>contacts[j].Normal.X+1e-10 ||
                         (Math.Abs(contacts[i].Normal.X-contacts[j].Normal.X)<=1e-10 && contacts[i].Normal.Y>contacts[j].Normal.Y)));
                    DVec discarded=removeI?contacts[i].Normal:contacts[j].Normal;
                    contacts.RemoveAll(c=>(c.Normal-discarded).Length<1e-8);
                    i=-1;break;
                }
            }
            return contacts.ToArray();
        }
        public static DContact[] SquareUnion(DBox square,IReadOnlyList<Patch> localPatches,DBox unionFrame,double margin=0)
            => SquareUnionPrepared(square,localPatches,Exterior(localPatches),unionFrame,margin);
        public static DContact[] SquareUnionPrepared(DBox square,IReadOnlyList<Patch> localPatches,IReadOnlyList<Exposed> faces,DBox unionFrame,double margin=0)
        {
            var localSquare=new DBox(unionFrame.Local(square.Center),square.Half,square.Angle-unionFrame.Angle,square.Identity,square.Revision);
            var contacts=SquareUnionPrepared(localSquare,localPatches,faces,margin,unionFrame.Identity,unionFrame.Revision);
            for(int i=0;i<contacts.Length;i++)
            {var c=contacts[i];c.AnchorA=unionFrame.World(c.AnchorA);c.AnchorB=unionFrame.World(c.AnchorB);c.Midpoint=unionFrame.World(c.Midpoint);
             c.Normal=unionFrame.World(c.Normal)-unionFrame.Center;c.LocalA=square.Local(c.AnchorA);c.LocalB=unionFrame.Local(c.AnchorB);contacts[i]=c;}
            return contacts;
        }
        static bool OnFace(Patch p,int side,DVec point)
        { double fixedAxis=side==0?p.Center.Y-p.Half.Y:side==2?p.Center.Y+p.Half.Y:side==1?p.Center.X+p.Half.X:p.Center.X-p.Half.X;
          return side%2==0?Math.Abs(point.Y-fixedAxis)<1e-12 && point.X>=p.Center.X-p.Half.X && point.X<=p.Center.X+p.Half.X:Math.Abs(point.X-fixedAxis)<1e-12 && point.Y>=p.Center.Y-p.Half.Y && point.Y<=p.Center.Y+p.Half.Y; }
        public static bool Inside(IReadOnlyList<Patch> patches,DVec point)
        { foreach(var p in patches) if(Math.Abs(point.X-p.Center.X)<=p.Half.X && Math.Abs(point.Y-p.Center.Y)<=p.Half.Y)return true; return false; }
        // Exact rectangle-union coverage: partition the square polygon by every patch
        // x/y edge and test the center of each positive-area partition cell.
        public static bool SquareContained(IReadOnlyList<Patch> patches,DBox square)
        {
            var vertices=square.Vertices(); var xs=new List<double>();var ys=new List<double>();
            foreach(var v in vertices){xs.Add(v.X);ys.Add(v.Y);}
            foreach(var p in patches){xs.Add(p.Center.X-p.Half.X);xs.Add(p.Center.X+p.Half.X);ys.Add(p.Center.Y-p.Half.Y);ys.Add(p.Center.Y+p.Half.Y);}
            xs.Sort();ys.Sort();
            for(int i=0;i<xs.Count-1;i++)for(int j=0;j<ys.Count-1;j++)
            { if(xs[i+1]-xs[i]<1e-12||ys[j+1]-ys[j]<1e-12)continue;
              DVec sample=new DVec((xs[i]+xs[i+1])*.5,(ys[j]+ys[j+1])*.5);
              var polygon=new List<DVec>(vertices);
              polygon=ClipHalfPlane(polygon,0,xs[i],true);polygon=ClipHalfPlane(polygon,0,xs[i+1],false);
              polygon=ClipHalfPlane(polygon,1,ys[j],true);polygon=ClipHalfPlane(polygon,1,ys[j+1],false);
              double twiceArea=0;for(int k=0;k<polygon.Count;k++)twiceArea+=polygon[k].Cross(polygon[(k+1)%polygon.Count]);
              if(Math.Abs(twiceArea)>1e-12 && !Inside(patches,sample))return false;
            }
            return true;
        }
        static List<DVec> ClipHalfPlane(List<DVec> polygon,int axis,double bound,bool greater)
        {
            var result=new List<DVec>();if(polygon.Count==0)return result;
            DVec previous=polygon[polygon.Count-1];double old=axis==0?previous.X:previous.Y;
            foreach(var current in polygon)
            {double value=axis==0?current.X:current.Y;bool was=greater?old>=bound:old<=bound,inside=greater?value>=bound:value<=bound;
             if(was!=inside){double t=(bound-old)/(value-old);result.Add(previous+(current-previous)*t);}
             if(inside)result.Add(current);previous=current;old=value;
            }
            return result;
        }
    }
}
