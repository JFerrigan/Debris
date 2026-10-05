using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Debris.Simulation.Tests
{
    // Offline Float64 block projected Gauss-Seidel. No player assembly references it.
    internal static class CoupledPackedReference
    {
        internal sealed class Result
        {
            public bool Converged;
            public string FirstFailure,FirstFeature;
            public int Substeps,Sweeps,Refreshes,Points,LocalRank,RankUpper;
            public double VelocityResidual=double.NaN,PositionResidual=double.NaN,GrainGap=double.NaN,SolidGap=double.NaN;
            public DBody[] FreeBodies,VelocityBodies,PositionFreeBodies,PositionBodies;
            public List<Manifold> VelocityContacts,PositionContacts;
        }
        internal sealed class Point
        {
            public int A,B; public DVec P,N,BaseA,BaseB; public double AngleA,AngleB,Gap,Normal,Tangent; public bool Solid;
            public string Key;
        }
        internal sealed class Manifold
        {
            public Point[] Points;
        }
        sealed class PatchGroup
        {
            public int Body;
            public CoupledReferenceGeometry.Patch[] Patches;
            public List<CoupledReferenceGeometry.Exposed> Faces;
        }
        static DVec Perp(DVec n)=>new DVec(-n.Y,n.X);
        static DVec Rotate(DVec p,double angle)
        {double c=Math.Cos(angle),s=Math.Sin(angle);return new DVec(c*p.X-s*p.Y,s*p.X+c*p.Y);}
        static double Cross(DVec a,DVec b)=>a.Cross(b);
        static double Row(DBody[] b,Point p,DVec d)
        {
            var a=b[p.A];var z=b[p.B];return d.Dot(z.Velocity-a.Velocity)+Cross(p.P-z.Center,d)*z.Spin-Cross(p.P-a.Center,d)*a.Spin;
        }
        static double PositionRow(DBody[] b,Point p,DVec d)
        {
            DVec da=b[p.A].Center-p.BaseA,db=b[p.B].Center-p.BaseB;
            return d.Dot(db-da)+Cross(p.P-p.BaseB,d)*(b[p.B].Angle-p.AngleB)-Cross(p.P-p.BaseA,d)*(b[p.A].Angle-p.AngleA);
        }
        static double Coupling(DBody[] b,Point p,DVec d,Point q,DVec e)
        {
            double sum=0;
            for(int side=0;side<2;side++)
            {
                int id=side==0?p.A:p.B;double sign=side==0?-1:1;
                for(int other=0;other<2;other++)
                {
                    if(id!=(other==0?q.A:q.B))continue;
                    double otherSign=other==0?-1:1;
                    DVec r=p.P-(side==0?p.BaseA:p.BaseB),t=q.P-(other==0?q.BaseA:q.BaseB);
                    sum+=sign*otherSign*(b[id].InverseMass*d.Dot(e)+b[id].InverseInertia*Cross(r,d)*Cross(t,e));
                }
            }
            return sum;
        }
        static void Apply(DBody[] b,Point p,DVec direction,double impulse,bool position)
        {
            if(impulse==0)return;
            for(int side=0;side<2;side++)
            {
                int id=side==0?p.A:p.B;double sign=side==0?-1:1;
                var value=b[id];DVec linear=direction*(sign*impulse*value.InverseMass);
                double angular=sign*impulse*Cross(p.P-(side==0?p.BaseA:p.BaseB),direction)*value.InverseInertia;
                if(position){value.Center+=linear;value.Angle+=angular;}
                else{value.Velocity+=linear;value.Spin+=angular;}
                b[id]=value;
            }
        }
        static List<PatchGroup> Groups(DArchive input)
        {
            var byBody=new SortedDictionary<int,List<CoupledReferenceGeometry.Patch>>();
            for(int i=0;i<input.Patches.Length;i++)
            {
                int body=input.PatchBodies[i];if(!byBody.TryGetValue(body,out var list)){list=new List<CoupledReferenceGeometry.Patch>();byBody.Add(body,list);}
                var p=input.Patches[i];p.Center=p.Center-input.LocalCOM[body];list.Add(p);
            }
            var result=new List<PatchGroup>();
            foreach(var entry in byBody)
            {var patches=entry.Value.ToArray();result.Add(new PatchGroup{Body=entry.Key,Patches=patches,Faces=CoupledReferenceGeometry.Exterior(patches)});}
            return result;
        }
        static int Bin(DVec center)
        {int x=(int)Math.Floor(center.X/2),y=(int)Math.Floor(center.Y/2);return ((x+4096)<<14)^(y+4096);}
        static DBox Grain(DBody b,int id,ulong identity)=>new DBox(b.Center,new DVec(.5,.5),b.Angle,identity);
        static bool OnExterior(PatchGroup group,DVec local,DVec outward)
        {
            foreach(var face in group.Faces)
            {
                if(face.Normal.Dot(outward)<=0)continue;
                var edge=face.B-face.A;double length=edge.Length;
                if(length<1e-12 || Math.Abs((local-face.A).Cross(edge))/length>1e-6)continue;
                double along=(local-face.A).Dot(edge)/length;
                if(along>=-1e-6 && along<=length+1e-6)return true;
            }
            return false;
        }
        static List<DContact[]> SolidPair(DBody a,PatchGroup ga,DBody b,PatchGroup gb,double margin)
        {
            var frameA=new DBox(a.Center,new DVec(),a.Angle);
            var frameB=new DBox(b.Center,new DVec(),b.Angle);
            var manifolds=new List<DContact[]>();var seen=new List<DContact>();
            foreach(var pa in ga.Patches)foreach(var pb in gb.Patches)
            {
                var boxA=new DBox(frameA.World(pa.Center),pa.Half,a.Angle,pa.Feature);
                var boxB=new DBox(frameB.World(pb.Center),pb.Half,b.Angle,pb.Feature);
                var pair=new List<DContact>(2);
                foreach(var contact in CoupledReferenceGeometry.BoxBox(boxA,boxB,margin))
                {
                    DVec localA=frameA.Local(contact.AnchorA),localB=frameB.Local(contact.AnchorB);
                    DVec normalA=Rotate(contact.Normal,-a.Angle),normalB=Rotate(contact.Normal*-1,-b.Angle);
                    if(!OnExterior(ga,localA,normalA)||!OnExterior(gb,localB,normalB))continue;
                    bool duplicate=false;
                    foreach(var old in seen)if((old.Midpoint-contact.Midpoint).Length<CoupledReferenceGeometry.Dedup &&
                        (old.Normal-contact.Normal).Length<CoupledReferenceGeometry.Dedup){duplicate=true;break;}
                    if(!duplicate){pair.Add(contact);seen.Add(contact);}
                }
                if(pair.Count>0)manifolds.Add(pair.ToArray());
            }
            return manifolds;
        }
        static void Add(List<Manifold> result,DBody[] bodies,int active,int a,int b,DContact[] contacts)
        {
            if(contacts.Length==0)return;
            var points=new Point[contacts.Length];
            for(int i=0;i<points.Length;i++)points[i]=new Point{A=a,B=b,P=contacts[i].Midpoint,N=contacts[i].Normal,Gap=contacts[i].Gap,Key=contacts[i].Key,BaseA=bodies[a].Center,BaseB=bodies[b].Center,AngleA=bodies[a].Angle,AngleB=bodies[b].Angle,Solid=b>=active};
            result.Add(new Manifold{Points=points});
        }
        static List<Manifold> Gather(DArchive input,DBody[] bodies,List<PatchGroup> groups,double margin)
        {
            int n=input.ActiveGrains;var result=new List<Manifold>(n*3);
            var bins=new Dictionary<int,List<int>>();
            for(int i=0;i<n;i++){int key=Bin(bodies[i].Center);if(!bins.TryGetValue(key,out var list)){list=new List<int>();bins.Add(key,list);}list.Add(i);}
            for(int i=0;i<n;i++)
            {
                var a=Grain(bodies[i],i,input.Grains[i].Identity);int bx=(int)Math.Floor(a.Center.X/2),by=(int)Math.Floor(a.Center.Y/2);
                for(int x=bx-1;x<=bx+1;x++)for(int y=by-1;y<=by+1;y++)
                {
                    int key=((x+4096)<<14)^(y+4096);if(!bins.TryGetValue(key,out var list))continue;
                    foreach(int j in list)if(j>i)
                    {
                        if(Math.Abs(a.Center.X-bodies[j].Center.X)>1.5+margin||Math.Abs(a.Center.Y-bodies[j].Center.Y)>1.5+margin)continue;
                        Add(result,bodies,n,i,j,CoupledReferenceGeometry.BoxBox(a,Grain(bodies[j],j,input.Grains[j].Identity),margin));
                    }
                }
                foreach(var group in groups)
                {
                    var body=bodies[group.Body];var frame=new DBox(body.Center,new DVec(),body.Angle,(ulong)group.Body);
                    Add(result,bodies,n,i,group.Body,CoupledReferenceGeometry.SquareUnionPrepared(a,group.Patches,group.Faces,frame,margin));
                }
            }
            for(int i=0;i<groups.Count;i++)for(int j=i+1;j<groups.Count;j++)
            {
                var a=groups[i];var b=groups[j];
                foreach(var pair in SolidPair(bodies[a.Body],a,bodies[b.Body],b,margin))
                    Add(result,bodies,n,a.Body,b.Body,pair);
            }
            return result;
        }
        // Exact two-normal local block; tangents are projected in point order.
        static void Sweep(DBody[] bodies,List<Manifold> manifolds,double mu,double h,bool position)
        {
            foreach(var manifold in manifolds)
            {
                var points=manifold.Points;int count=points.Length;
                if(count==1)
                {
                    var p=points[0];double d=Coupling(bodies,p,p.N,p,p.N);
                    if(!(d>0))continue;
                    double w=position?p.Gap+(p.Solid?.0001:.002)+PositionRow(bodies,p,p.N):Row(bodies,p,p.N)+Math.Max(p.Gap,0)/h;
                    double old=p.Normal;p.Normal=Math.Max(0,old-w/d);Apply(bodies,p,p.N,p.Normal-old,position);
                }
                else
                {
                    // Enumerate the four two-point normal active sets using the
                    // current endpoint motion with this block's impulse removed.
                    double old0=points[0].Normal,old1=points[1].Normal;
                    Apply(bodies,points[0],points[0].N,-old0,position);
                    Apply(bodies,points[1],points[1].N,-old1,position);
                    double a=Coupling(bodies,points[0],points[0].N,points[0],points[0].N);
                    double d=Coupling(bodies,points[1],points[1].N,points[1],points[1].N);
                    double c=Coupling(bodies,points[0],points[0].N,points[1],points[1].N);
                    double w0=position?points[0].Gap+(points[0].Solid?.0001:.002)+PositionRow(bodies,points[0],points[0].N):Row(bodies,points[0],points[0].N)+Math.Max(points[0].Gap,0)/h;
                    double w1=position?points[1].Gap+(points[1].Solid?.0001:.002)+PositionRow(bodies,points[1],points[1].N):Row(bodies,points[1],points[1].N)+Math.Max(points[1].Gap,0)/h;
                    bool found=false;double chosen0=old0,chosen1=old1;
                    for(int mask=0;mask<4 && !found;mask++)
                    {
                        double x=0,y=0;
                        if(mask==1 && a>0)x=-w0/a;
                        if(mask==2 && d>0)y=-w1/d;
                        if(mask==3){double det=a*d-c*c;if(det>1e-14*a*d){x=(-w0*d+c*w1)/det;y=(-w1*a+c*w0)/det;}else continue;}
                        if(x>=-1e-12&&y>=-1e-12&&w0+a*x+c*y>=-1e-9&&w1+c*x+d*y>=-1e-9){chosen0=Math.Max(0,x);chosen1=Math.Max(0,y);found=true;}
                    }
                    points[0].Normal=chosen0;Apply(bodies,points[0],points[0].N,chosen0,position);
                    points[1].Normal=chosen1;Apply(bodies,points[1],points[1].N,chosen1,position);
                }
                if(position)continue;
                foreach(var p in points)
                {
                    var t=Perp(p.N);double d=Coupling(bodies,p,t,p,t);
                    double bound=p.Gap<=.0001?mu*p.Normal:0;
                    if(!(d>0))continue;
                    double old=p.Tangent;double next=Math.Max(-bound,Math.Min(bound,old-Row(bodies,p,t)/d));
                    p.Tangent=next;Apply(bodies,p,t,next-old,false);
                }
            }
        }
        static double Residual(DBody[] bodies,List<Manifold> manifolds,double mu,double h,bool position,out double maxGap)
        {
            double worst=0;maxGap=0;
            foreach(var m in manifolds)foreach(var p in m.Points)
            {
                worst=Math.Max(worst,Violation(bodies,p,mu,h,position));
                maxGap=Math.Max(maxGap,Math.Max(0,-p.Gap));
            }
            return worst;
        }
        static double Violation(DBody[] bodies,Point p,double mu,double h,bool position)
        {
            double w=position?p.Gap+(p.Solid?.0001:.002)+PositionRow(bodies,p,p.N):Row(bodies,p,p.N)+Math.Max(p.Gap,0)/h;
            double worst=Math.Max(0,-p.Normal);
            worst=Math.Max(worst,p.Normal>1e-12?Math.Abs(w):Math.Max(0,-w));
            if(position)return worst;
            double bound=p.Gap<=.0001?mu*p.Normal:0;
            worst=Math.Max(worst,Math.Max(0,Math.Abs(p.Tangent)-bound));
            if(bound<=1e-12)return Math.Max(worst,Math.Abs(p.Tangent));
            double speed=Row(bodies,p,Perp(p.N));
            if(p.Tangent>=bound-1e-12)worst=Math.Max(worst,Math.Max(0,speed));
            else if(p.Tangent<=-bound+1e-12)worst=Math.Max(worst,Math.Max(0,-speed));
            else worst=Math.Max(worst,Math.Abs(speed));
            return worst;
        }
        static int LocalRank(DBody[] bodies,List<Manifold> manifolds)
        {
            int rank=0;foreach(var m in manifolds)
            {
                if(m.Points.Length==1){if(Coupling(bodies,m.Points[0],m.Points[0].N,m.Points[0],m.Points[0].N)>1e-12)rank++;}
                else{var a=m.Points[0];var b=m.Points[1];double x=Coupling(bodies,a,a.N,a,a.N),y=Coupling(bodies,b,b.N,b,b.N),z=Coupling(bodies,a,a.N,b,b.N);if(x>1e-12)rank++;if(x*y-z*z>1e-12*x*y)rank++;}
            }
            return rank;
        }
        static bool Solve(DBody[] bodies,List<Manifold> manifolds,double mu,double h,bool position,Stopwatch clock,Result result)
        {
            double tolerance=1e-8;int sweep=0;
            for(;sweep<100000 && clock.Elapsed.TotalSeconds<600;sweep++)
            {
                Sweep(bodies,manifolds,mu,h,position);
                if((sweep&15)!=15)continue;
                double residual=Residual(bodies,manifolds,mu,h,position,out _);
                if(position)result.PositionResidual=residual;else result.VelocityResidual=residual;
                if(residual<=tolerance){result.Sweeps+=sweep+1;return true;}
            }
            result.Sweeps+=sweep;
            double final=Residual(bodies,manifolds,mu,h,position,out _);
            if(position)result.PositionResidual=final;else result.VelocityResidual=final;
            result.FirstFailure=clock.Elapsed.TotalSeconds>=600?"ReferenceTimeout":position?"PositionNonconvergence":"VelocityNonconvergence";
            foreach(var m in manifolds)foreach(var p in m.Points)
            {
                if(Violation(bodies,p,mu,h,position)>tolerance){result.FirstFeature=p.Key;return false;}
            }
            return false;
        }
        public static Result Run(DArchive input)
        {
            var result=new Result();var clock=Stopwatch.StartNew();
            if(input.MountedSuction&&input.SuctionForce>0){result.FirstFailure="ArchiveMissingSuctionGeometry";result.FirstFeature="suction-mouth";return result;}
            var bodies=(DBody[])input.Bodies.Clone();var groups=Groups(input);
            double maxSpeed=0;
            for(int i=0;i<input.ActiveGrains;i++)maxSpeed=Math.Max(maxSpeed,bodies[i].Velocity.Length+Math.Abs(bodies[i].Spin)*.707107);
            double shipRadius=0;
            for(int i=input.BodyEndpointStart;i<bodies.Length;i++)
            {
                double radius=0;for(int p=0;p<input.Patches.Length;p++)if(input.PatchBodies[p]==i)
                    radius=Math.Max(radius,(input.Patches[p].Center-input.LocalCOM[i]).Length+input.Patches[p].Half.Length);
                maxSpeed=Math.Max(maxSpeed,bodies[i].Velocity.Length+Math.Abs(bodies[i].Spin)*radius);
                if(i==input.BodyEndpointStart)shipRadius=radius;
            }
            maxSpeed+=input.Dt*(input.Force.Length*bodies[input.BodyEndpointStart].InverseMass+Math.Abs(input.Torque)*bodies[input.BodyEndpointStart].InverseInertia*shipRadius);
            int substeps=Math.Max(4,(int)Math.Ceiling(Math.Max(0,2*maxSpeed*input.Dt/.25-1e-5)));
            result.Substeps=substeps;if(substeps>16){result.FirstFailure="SubstepEnvelope";result.FirstFeature="global-speed-bound";return result;}
            double h=input.Dt/substeps;
            for(int sub=0;sub<substeps;sub++)
            {
                var stepStart=(DBody[])bodies.Clone();
                int ship=input.BodyEndpointStart;var s=bodies[ship];s.Velocity+=Rotate(input.Force,s.Angle)*(s.InverseMass*h);s.Spin+=input.Torque*s.InverseInertia*h;bodies[ship]=s;
                var contacts=Gather(input,bodies,groups,.25);
                if(sub==0){result.FreeBodies=(DBody[])bodies.Clone();result.VelocityContacts=contacts;}
                int points=0;foreach(var m in contacts)points+=m.Points.Length;
                result.Points=Math.Max(result.Points,points);
                result.LocalRank=Math.Max(result.LocalRank,LocalRank(bodies,contacts));
                result.RankUpper=Math.Max(result.RankUpper,Math.Min(points,3*(input.ActiveGrains+bodies.Length-input.BodyEndpointStart)));
                if(!Solve(bodies,contacts,input.Friction,h,false,clock,result))return result;
                if(sub==0)result.VelocityBodies=(DBody[])bodies.Clone();
                for(int i=0;i<input.ActiveGrains;i++){var b=bodies[i];b.Center+=b.Velocity*h;b.Angle+=b.Spin*h;bodies[i]=b;}
                for(int i=input.BodyEndpointStart;i<bodies.Length;i++){var b=bodies[i];b.Center+=b.Velocity*h;b.Angle+=b.Spin*h;bodies[i]=b;}
                bool positionSettled=false;
                for(int refresh=0;refresh<12;refresh++)
                {
                    contacts=Gather(input,bodies,groups,.001);
                    double current=Residual(bodies,contacts,0,h,true,out _);
                    result.PositionResidual=current;
                    if(current<=1e-8){positionSettled=true;break;}
                    if(sub==0&&refresh==0){result.PositionFreeBodies=(DBody[])bodies.Clone();result.PositionContacts=contacts;}
                    if(!Solve(bodies,contacts,0,h,true,clock,result))return result;
                    if(sub==0&&refresh==0)result.PositionBodies=(DBody[])bodies.Clone();
                    result.Refreshes++;
                }
                if(!positionSettled)
                {
                    result.FirstFailure="PositionRefreshLimit";
                    foreach(var m in contacts)foreach(var p in m.Points)
                        if(Violation(bodies,p,0,h,true)>1e-8){result.FirstFeature=p.Key;break;}
                    if(result.FirstFeature==null)result.FirstFeature="geometry-refresh";
                    return result;
                }
                contacts=Gather(input,bodies,groups,0);
                if(double.IsNaN(result.GrainGap))result.GrainGap=0;
                if(double.IsNaN(result.SolidGap))result.SolidGap=0;
                foreach(var m in contacts)foreach(var p in m.Points)
                {if(p.B<input.ActiveGrains)result.GrainGap=Math.Max(result.GrainGap,-p.Gap);else result.SolidGap=Math.Max(result.SolidGap,-p.Gap);}
                for(int grain=0;grain<input.ActiveGrains;grain++)foreach(var group in groups)
                {
                    var body=bodies[group.Body];var frame=new DBox(body.Center,new DVec(),body.Angle);
                    var square=Grain(bodies[grain],grain,input.Grains[grain].Identity);
                    var local=new DBox(frame.Local(square.Center),square.Half,square.Angle-frame.Angle);
                    if(CoupledReferenceGeometry.SquareContained(group.Patches,local))
                    {result.FirstFailure="ContainedGrain";result.FirstFeature=input.Grains[grain].Identity.ToString();return result;}
                }
                if(result.GrainGap>.01||result.SolidGap>.001)
                {
                    result.FirstFailure="FinalGeometry";
                    foreach(var m in contacts)foreach(var p in m.Points)if(-p.Gap>(p.Solid?.001:.01)){result.FirstFeature=p.Key;return result;}
                    return result;
                }
                for(int i=0;i<input.ActiveGrains;i++)
                {
                    double travel=(bodies[i].Center-stepStart[i].Center).Length+
                        Math.Abs(bodies[i].Angle-stepStart[i].Angle)*.7071067811865476;
                    if(travel>.375+1e-9){result.FirstFailure="SubstepBound";result.FirstFeature=input.Grains[i].Identity.ToString();return result;}
                }
                foreach(var group in groups)
                {
                    int i=group.Body;double radius=0;
                    foreach(var patch in group.Patches)radius=Math.Max(radius,patch.Center.Length+patch.Half.Length);
                    double travel=(bodies[i].Center-stepStart[i].Center).Length+
                        Math.Abs(bodies[i].Angle-stepStart[i].Angle)*radius;
                    if(travel>.375+1e-9){result.FirstFailure="SubstepBound";result.FirstFeature=i.ToString();return result;}
                }
            }
            result.Converged=true;return result;
        }
    }
}
