using System;
using System.Collections.Generic;
using System.IO;
using Debris.Simulation.ParallelProof;

namespace Debris.Simulation.Tests
{
    internal struct DBody
    {
        public DVec Center,Velocity;
        public double Angle,Spin,InverseMass,InverseInertia;
    }
    internal struct DRow
    {
        public int A,B; public DVec Point,Direction; public double Gap;
    }
    internal sealed class DSystem
    {
        public DBody[] Bodies; public DRow[] Rows; public double[,] J,A;
        public DSystem(DBody[] bodies,DRow[] rows)
        {
            Bodies=bodies;Rows=rows; J=new double[rows.Length,bodies.Length*3];A=new double[rows.Length,rows.Length];
            for(int i=0;i<rows.Length;i++)
            {var r=rows[i]; DVec ra=r.Point-bodies[r.A].Center,rb=r.Point-bodies[r.B].Center;
             J[i,r.A*3]=-r.Direction.X;J[i,r.A*3+1]=-r.Direction.Y;J[i,r.A*3+2]=-ra.Cross(r.Direction);
             J[i,r.B*3]=r.Direction.X;J[i,r.B*3+1]=r.Direction.Y;J[i,r.B*3+2]=rb.Cross(r.Direction); }
            for(int i=0;i<rows.Length;i++)for(int j=0;j<rows.Length;j++)for(int k=0;k<bodies.Length*3;k++)A[i,j]+=J[i,k]*Weight(k)*J[j,k];
        }
        double Weight(int k)=>k%3==2?Bodies[k/3].InverseInertia:Bodies[k/3].InverseMass;
        public double[] Free()
        {var u=new double[Bodies.Length*3];for(int i=0;i<Bodies.Length;i++){u[3*i]=Bodies[i].Velocity.X;u[3*i+1]=Bodies[i].Velocity.Y;u[3*i+2]=Bodies[i].Spin;}return MultiplyJ(u);}
        public double[] MultiplyJ(double[] u)
        {var r=new double[Rows.Length];for(int i=0;i<r.Length;i++)for(int k=0;k<u.Length;k++)r[i]+=J[i,k]*u[k];return r;}
        public double[] Apply(double[] impulse)
        {var u=new double[Bodies.Length*3];for(int k=0;k<u.Length;k++)for(int i=0;i<Rows.Length;i++)u[k]+=Weight(k)*J[i,k]*impulse[i];return u;}
        // Independently gather equal/opposite forces at each endpoint, then evaluate rows.
        public double[] GatherApply(double[] impulse)
        {var u=new double[Bodies.Length*3];for(int i=0;i<Rows.Length;i++)
            {var r=Rows[i];DVec ra=r.Point-Bodies[r.A].Center,rb=r.Point-Bodies[r.B].Center;
             u[3*r.A]-=r.Direction.X*impulse[i]*Bodies[r.A].InverseMass;
             u[3*r.A+1]-=r.Direction.Y*impulse[i]*Bodies[r.A].InverseMass;
             u[3*r.A+2]-=ra.Cross(r.Direction)*impulse[i]*Bodies[r.A].InverseInertia;
             u[3*r.B]+=r.Direction.X*impulse[i]*Bodies[r.B].InverseMass;
             u[3*r.B+1]+=r.Direction.Y*impulse[i]*Bodies[r.B].InverseMass;
             u[3*r.B+2]+=rb.Cross(r.Direction)*impulse[i]*Bodies[r.B].InverseInertia; }
            var result=new double[Rows.Length];for(int i=0;i<Rows.Length;i++)
            {var r=Rows[i];DVec ra=r.Point-Bodies[r.A].Center,rb=r.Point-Bodies[r.B].Center;
             result[i]=r.Direction.X*(u[3*r.B]-u[3*r.A])+r.Direction.Y*(u[3*r.B+1]-u[3*r.A+1])+rb.Cross(r.Direction)*u[3*r.B+2]-ra.Cross(r.Direction)*u[3*r.A+2];}
            return result;
        }
    }
    internal sealed class DAnswer
    {
        public bool Conclusive; public string Diagnostic; public double[] Impulses,Motion; public int Rank; public double Residual; public int Candidates;
    }
    internal static class CoupledContactReference
    {
        public const double PhysicalTolerance=1e-8, RankRelative=1e-12;
        public const int MaxJacobiSweeps=100;
        // One-sided Jacobi SVD: orthogonalize columns, retain right singular vectors.
        // Systems here contain at most 16 rows; the threshold is relative to sigma_max.
        static bool Svd(double[,] matrix,double[] rhs,out double[] x,out int rank,out double residual)
        {
            int m=matrix.GetLength(0),n=matrix.GetLength(1);x=new double[n];rank=0;residual=double.PositiveInfinity;
            var b=(double[,])matrix.Clone();var v=new double[n,n];for(int i=0;i<n;i++)v[i,i]=1;
            bool converged=false;
            for(int sweep=0;sweep<MaxJacobiSweeps;sweep++)
            {bool changed=false;for(int p=0;p<n;p++)for(int q=p+1;q<n;q++)
             {double pp=0,qq=0,pq=0;for(int i=0;i<m;i++){pp+=b[i,p]*b[i,p];qq+=b[i,q]*b[i,q];pq+=b[i,p]*b[i,q];}
              if(pp==0||qq==0||Math.Abs(pq)<=1e-15*Math.Sqrt(pp*qq))continue;
              changed=true;double tau=(qq-pp)/(2*pq),t=(tau>=0?1:-1)/(Math.Abs(tau)+Math.Sqrt(1+tau*tau));
              double c=1/Math.Sqrt(1+t*t),s=c*t;
              for(int i=0;i<m;i++){double a=b[i,p],d=b[i,q];b[i,p]=c*a-s*d;b[i,q]=s*a+c*d;}
              for(int i=0;i<n;i++){double a=v[i,p],d=v[i,q];v[i,p]=c*a-s*d;v[i,q]=s*a+c*d;}
             }
             if(!changed){converged=true;break;}
            }
            if(!converged)return false;
            var sigma=new double[n];double max=0;for(int j=0;j<n;j++){for(int i=0;i<m;i++)sigma[j]+=b[i,j]*b[i,j];sigma[j]=Math.Sqrt(sigma[j]);max=Math.Max(max,sigma[j]);}
            for(int j=0;j<n;j++)if(sigma[j]>RankRelative*max)
            {rank++;double coefficient=0;for(int i=0;i<m;i++)coefficient+=b[i,j]*rhs[i];coefficient/=sigma[j]*sigma[j];for(int k=0;k<n;k++)x[k]+=v[k,j]*coefficient;}
            residual=0;for(int i=0;i<m;i++){double e=-rhs[i];for(int j=0;j<n;j++)e+=matrix[i,j]*x[j];residual=Math.Max(residual,Math.Abs(e));}
            return true;
        }
        static double[] Motion(DSystem system,double[] impulses)
        {var f=system.Free();var delta=system.GatherApply(impulses);for(int i=0;i<f.Length;i++)f[i]+=delta[i];return f;}
        static bool Better(double[] candidate,double[] best)
        {if(best==null)return true;double a=0,b=0;for(int i=0;i<candidate.Length;i++){a+=candidate[i]*candidate[i];b+=best[i]*best[i];}return a<b-1e-12;}
        public static DAnswer SolveManifold(DSystem system,int points,double mu,double h)
        {
            if(points<1||points>2||system.Rows.Length!=2*points||h<=0||mu<0)throw new ArgumentException("Expected one or two normal/tangent points");
            int n=2*points;var free=system.Free();var answer=new DAnswer{Diagnostic="No certified active state",Residual=double.PositiveInfinity};
            for(int states=0;states<(1<<(2*points));states++)
            {var matrix=new double[n,n];var rhs=new double[n];
             for(int p=0;p<points;p++)
             {int normal=2*p,tangent=normal+1,state=(states>>(2*p))&3;
              double bound=-Math.Max(system.Rows[normal].Gap,0)/h;
              if(state==0){matrix[normal,normal]=1;matrix[tangent,tangent]=1;}
              else
              {for(int j=0;j<n;j++)matrix[normal,j]=system.A[normal,j];rhs[normal]=bound-free[normal];
               if(system.Rows[normal].Gap>.0001 || mu==0)matrix[tangent,tangent]=1;
               else if(state==1){for(int j=0;j<n;j++)matrix[tangent,j]=system.A[tangent,j];rhs[tangent]=-free[tangent];}
               else {matrix[tangent,tangent]=1;matrix[tangent,normal]=state==2?mu:-mu;}
              }
             }
             answer.Candidates++;
             if(!Svd(matrix,rhs,out var impulse,out int rank,out double equation)){answer.Diagnostic="SVD failed to converge within 100 sweeps";continue;}
             var motion=Motion(system,impulse);double residual=equation;bool valid=equation<=PhysicalTolerance;
             for(int p=0;p<points;p++)
             {int ni=2*p,ti=ni+1,state=(states>>(2*p))&3;double normal=impulse[ni],tangent=impulse[ti];
              double wn=motion[ni]+Math.Max(system.Rows[ni].Gap,0)/h,vt=motion[ti];
              bool friction=system.Rows[ni].Gap<=.0001 && mu>0;
              residual=Math.Max(residual,Math.Max(Math.Max(0,-normal),Math.Max(0,-wn)));
              residual=Math.Max(residual,Math.Abs(Math.Min(normal,wn)));
              residual=Math.Max(residual,Math.Max(0,Math.Abs(tangent)-(friction?mu*normal:0)));
              if(!friction)residual=Math.Max(residual,Math.Abs(tangent));
              else if(normal>PhysicalTolerance)
              { if(state==1)residual=Math.Max(residual,Math.Abs(vt));
                if(state==2)residual=Math.Max(residual,Math.Max(0,-vt));
                if(state==3)residual=Math.Max(residual,Math.Max(0,vt)); }
              if(state==0)residual=Math.Max(residual,Math.Abs(normal)+Math.Abs(tangent));
             }
             valid &= residual<=PhysicalTolerance;
             if(valid && Better(impulse,answer.Impulses))
             {answer.Conclusive=true;answer.Impulses=impulse;answer.Motion=motion;answer.Rank=rank;answer.Residual=residual;answer.Diagnostic="Certified active state "+states;}
            }
            return answer;
        }
        public static DAnswer SolveFrictionless(DSystem system,double[] offsets)
        {
            int n=offsets.Length;if(n<1||n>8||system.Rows.Length!=n)throw new ArgumentException("One to eight normal rows required");
            var free=system.Free();var answer=new DAnswer{Diagnostic="No certified active set",Residual=double.PositiveInfinity};
            for(int mask=0;mask<(1<<n);mask++)
            {var matrix=new double[n,n];var rhs=new double[n];
             for(int i=0;i<n;i++)if((mask&(1<<i))==0)matrix[i,i]=1;
             else {for(int j=0;j<n;j++)matrix[i,j]=system.A[i,j];rhs[i]=-free[i]-offsets[i];}
             answer.Candidates++;if(!Svd(matrix,rhs,out var impulse,out int rank,out double equation)){answer.Diagnostic="SVD failed to converge within 100 sweeps";continue;}
             var motion=Motion(system,impulse);double residual=equation;
             for(int i=0;i<n;i++){double y=motion[i]+offsets[i];residual=Math.Max(residual,Math.Max(0,-impulse[i]));residual=Math.Max(residual,Math.Max(0,-y));residual=Math.Max(residual,Math.Abs(Math.Min(impulse[i],y)));}
             if(residual<=PhysicalTolerance && Better(impulse,answer.Impulses))
             {answer.Conclusive=true;answer.Impulses=impulse;answer.Motion=motion;answer.Rank=rank;answer.Residual=residual;answer.Diagnostic="Certified set "+mask;}
            }
            return answer;
        }
        public static DAnswer CorrectPosition(DSystem system,double[] gaps,double slop)
        { // Fresh p=0: no velocity enters the KKT right hand side.
            var stationary=new DBody[system.Bodies.Length];Array.Copy(system.Bodies,stationary,stationary.Length);
            for(int i=0;i<stationary.Length;i++){stationary[i].Velocity=new DVec();stationary[i].Spin=0;}
            var frozen=new DSystem(stationary,system.Rows);var offsets=new double[gaps.Length];for(int i=0;i<gaps.Length;i++)offsets[i]=gaps[i]+slop;
            return SolveFrictionless(frozen,offsets);
        }
    }
    internal sealed class DArchive
    {
        public string Hash; public int ActiveGrains,GrainCapacity,BodyEndpointStart; public DBody[] Bodies; public DBox[] Grains; public CoupledReferenceGeometry.Patch[] Patches; public int[] PatchBodies; public DVec[] LocalCOM; public DVec Force; public double Dt,Friction,Torque,SuctionForce; public bool MountedSuction;
        public static DArchive Read(string directory,string expectedHash)
        {
            var input=CoupledReplayArchive.Read(directory,out var manifest);
            if(manifest.sha256!=expectedHash)throw new InvalidDataException("Unexpected immutable archive hash");
            if(manifest.bodyEndpointStart!=input.GrainCapacity)throw new InvalidDataException("Body endpoint index mismatch");
            var result=new DArchive {Hash=manifest.sha256,ActiveGrains=input.Grains.Length,GrainCapacity=input.GrainCapacity,BodyEndpointStart=manifest.bodyEndpointStart,
                Bodies=new DBody[input.Endpoints.Length],Grains=new DBox[input.Grains.Length],Patches=new CoupledReferenceGeometry.Patch[input.Boundaries.Length],PatchBodies=new int[input.Boundaries.Length],LocalCOM=new DVec[input.Endpoints.Length],Dt=input.Dt,Friction=input.Friction,Force=new DVec(input.Force.x,input.Force.y),Torque=input.Torque,SuctionForce=input.SuctionForce,MountedSuction=input.MountedSuction};
            for(int i=0;i<input.Grains.Length;i++)
            {var g=input.Grains[i];double mass=input.Masses==null?1:input.Masses[i];result.Bodies[i]=new DBody{Center=new DVec(g.Center.x,g.Center.y),Velocity=new DVec(g.Velocity.x,g.Velocity.y),Angle=g.Angle,Spin=g.AngularVelocity,InverseMass=1/mass,InverseInertia=6/mass};
             result.Grains[i]=new DBox(result.Bodies[i].Center,new DVec(.5,.5),g.Angle,g.Identity);}
            for(int i=0;i<input.Parameters.Length;i++)
            {int index=input.GrainCapacity+i;var b=input.Endpoints[index];var p=input.Parameters[i];result.Bodies[index]=new DBody{Center=new DVec(b.Center.x,b.Center.y),Velocity=new DVec(b.Velocity.x,b.Velocity.y),Angle=b.Angle,Spin=b.AngularVelocity,InverseMass=p.InverseMass,InverseInertia=p.InverseInertia};result.LocalCOM[index]=new DVec(p.LocalCOM.x,p.LocalCOM.y);}
            for(int i=0;i<input.Boundaries.Length;i++)
            {var patch=input.Boundaries[i];if(patch.Body>=(uint)result.Bodies.Length)throw new InvalidDataException("Boundary endpoint exceeds allocated capacity");
             result.Patches[i]=new CoupledReferenceGeometry.Patch(new DVec(patch.Center.x,patch.Center.y),new DVec(patch.HalfSize.x,patch.HalfSize.y),patch.Feature);result.PatchBodies[i]=(int)patch.Body;}
            return result;
        }
    }
}
