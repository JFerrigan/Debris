using System;
using System.Collections.Generic;

namespace Debris.Simulation.Tests
{
    // Separate matrix-free double implementation of the V2 scaled residual and
    // generalized derivative. It never calls the block sequential oracle.
    internal sealed class CoupledV2Double
    {
        internal sealed class Answer
        {
            public bool Converged; public string Failure,FirstFeature; public int Newton,Krylov,Restarts;
            public double SearchShift;
            public double Residual,PhysicalResidual,NormalResidual,FrictionResidual,EndpointError;
        }
        struct Row
        {
            public int A,B,NormalIndex;
            public DVec Point,Direction;
            public double Gap,Diagonal,Root,Mu;
            public bool Tangent,Solid;public string Key;
        }
        readonly DBody[] free;
        readonly Row[] rows;
        readonly int[] starts,sizes;
        readonly bool position;
        readonly double h;
        public int Rows=>rows.Length;
        public CoupledV2Double(DBody[] freeBodies,List<CoupledPackedReference.Manifold> manifolds,double friction,double substep,bool positionSolve=false)
        {
            free=(DBody[])freeBodies.Clone();h=substep;position=positionSolve;
            var list=new List<Row>();var blockStarts=new List<int>();var blockSizes=new List<int>();
            foreach(var manifold in manifolds)
            {
                blockStarts.Add(list.Count);
                foreach(var point in manifold.Points)
                {
                    int ni=list.Count;
                    list.Add(new Row{A=point.A,B=point.B,Point=point.P,Direction=point.N,Gap=point.Gap,Mu=friction,NormalIndex=ni,Solid=point.Solid,Key=point.Key});
                    if(position)continue;
                    list.Add(new Row{A=point.A,B=point.B,Point=point.P,Direction=new DVec(-point.N.Y,point.N.X),Gap=point.Gap,Mu=point.Gap<=.0001?friction:0,NormalIndex=ni,Tangent=true,Solid=point.Solid,Key=point.Key});
                }
                blockSizes.Add(list.Count-blockStarts[blockStarts.Count-1]);
            }
            rows=list.ToArray();starts=blockStarts.ToArray();sizes=blockSizes.ToArray();
            for(int i=0;i<rows.Length;i++)
            {
                var r=rows[i];double aa=free[r.A].InverseMass+free[r.B].InverseMass;
                double ca=(r.Point-free[r.A].Center).Cross(r.Direction),cb=(r.Point-free[r.B].Center).Cross(r.Direction);
                aa+=free[r.A].InverseInertia*ca*ca+free[r.B].InverseInertia*cb*cb;
                if(!(aa>0)||double.IsNaN(aa))throw new InvalidOperationException("Nonpositive V2 contact diagonal");
                r.Diagonal=aa;r.Root=Math.Sqrt(aa);rows[i]=r;
            }
        }
        double[] J(double[] u)
        {
            var output=new double[rows.Length];
            for(int i=0;i<rows.Length;i++)
            {
                var r=rows[i];DVec a=r.Point-free[r.A].Center,b=r.Point-free[r.B].Center;
                output[i]=r.Direction.X*(u[3*r.B]-u[3*r.A])+r.Direction.Y*(u[3*r.B+1]-u[3*r.A+1])
                    +b.Cross(r.Direction)*u[3*r.B+2]-a.Cross(r.Direction)*u[3*r.A+2];
            }
            return output;
        }
        double[] JT(double[] impulse)
        {
            var result=new double[free.Length*3];
            for(int i=0;i<rows.Length;i++)
            {
                var r=rows[i];double value=impulse[i];
                DVec a=r.Point-free[r.A].Center,b=r.Point-free[r.B].Center;
                result[3*r.A]-=r.Direction.X*value;result[3*r.A+1]-=r.Direction.Y*value;result[3*r.A+2]-=a.Cross(r.Direction)*value;
                result[3*r.B]+=r.Direction.X*value;result[3*r.B+1]+=r.Direction.Y*value;result[3*r.B+2]+=b.Cross(r.Direction)*value;
            }
            for(int i=0;i<free.Length;i++)
            {result[3*i]*=free[i].InverseMass;result[3*i+1]*=free[i].InverseMass;result[3*i+2]*=free[i].InverseInertia;}
            return result;
        }
        double[] FreeMotion()
        {
            var result=new double[free.Length*3];if(position)return result;
            for(int i=0;i<free.Length;i++){result[3*i]=free[i].Velocity.X;result[3*i+1]=free[i].Velocity.Y;result[3*i+2]=free[i].Spin;}return result;
        }
        double[] EndpointMotion(double[] x)
        {
            var impulse=new double[rows.Length];for(int i=0;i<rows.Length;i++)impulse[i]=x[i]/rows[i].Root;
            var u=JT(impulse);var baseMotion=FreeMotion();for(int i=0;i<u.Length;i++)u[i]+=baseMotion[i];return u;
        }
        double[] Operator(double[] z)
        {
            var impulse=new double[rows.Length];for(int i=0;i<rows.Length;i++)impulse[i]=z[i]/rows[i].Root;
            var y=J(JT(impulse));for(int i=0;i<y.Length;i++)y[i]/=rows[i].Root;return y;
        }
        internal double[] Residual(double[] x,out double[] w,out double physical)
        {
            var motion=EndpointMotion(x);var speed=J(motion);w=new double[rows.Length];var f=new double[rows.Length];
            for(int i=0;i<rows.Length;i++)
            {
                var r=rows[i];double offset=position?r.Gap+(r.Solid?.0001:.002):r.Tangent?0:Math.Max(r.Gap,0)/h;
                w[i]=(speed[i]+offset)/r.Root;
                if(!r.Tangent)
                {
                    f[i]=x[i]-Math.Max(0,x[i]-w[i]);
                }
                else
                {
                    double gamma=r.Mu*rows[r.NormalIndex].Root/r.Root;
                    double bound=gamma*Math.Max(0,x[r.NormalIndex]);
                    f[i]=x[i]-Math.Max(-bound,Math.Min(bound,x[i]-w[i]));
                }
            }
            physical=Physical(x,w,out _,out _,out _);
            return f;
        }
        double Physical(double[] x,double[] w,out double normal,out double friction,out string first)
        {
            normal=0;friction=0;first=null;
            for(int i=0;i<rows.Length;i++)
            {
                var r=rows[i];double impulse=x[i]/r.Root,speed=w[i]*r.Root,error;
                if(!r.Tangent)
                    error=Math.Max(Math.Max(0,-impulse),impulse>1e-12?Math.Abs(speed):Math.Max(0,-speed));
                else
                {
                    double bound=r.Mu*Math.Max(0,x[r.NormalIndex]/rows[r.NormalIndex].Root);
                    error=Math.Max(0,Math.Abs(impulse)-bound);
                    if(bound>1e-12)
                    {
                        if(impulse>=bound-1e-12)error=Math.Max(error,Math.Max(0,speed));
                        else if(impulse<=-bound+1e-12)error=Math.Max(error,Math.Max(0,-speed));
                        else error=Math.Max(error,Math.Abs(speed));
                    }
                }
                if(error>1e-5 && first==null)first=r.Key;
                if(r.Tangent)friction=Math.Max(friction,error);else normal=Math.Max(normal,error);
            }
            return Math.Max(normal,friction);
        }
        double[] Derivative(double[] x,double[] w,double[] z)
        {
            var bz=Operator(z);var result=new double[rows.Length];
            for(int i=0;i<rows.Length;i++)
            {
                var r=rows[i];if(!r.Tangent){result[i]=NormalActive(x[i],w[i])?bz[i]:z[i];continue;}
                double gamma=r.Mu*rows[r.NormalIndex].Root/r.Root,bound=gamma*Math.Max(0,x[r.NormalIndex]);
                double trial=x[i]-w[i];
                if(bound==0){result[i]=z[i];continue;}
                if(trial>=bound)result[i]=z[i]-(x[r.NormalIndex]>0?gamma*z[r.NormalIndex]:0);
                else if(trial<=-bound)result[i]=z[i]+(x[r.NormalIndex]>0?gamma*z[r.NormalIndex]:0);
                else result[i]=bz[i];
            }
            return result;
        }
        internal double[] DirectionalDerivative(double[] x,double[] z)
        {
            Residual(x,out var w,out _);return Derivative(x,w,z);
        }
        double LocalB(int i,int j)
        {
            var a=rows[i];var b=rows[j];double value=0;
            for(int side=0;side<2;side++)for(int other=0;other<2;other++)
            {
                int id=side==0?a.A:a.B;if(id!=(other==0?b.A:b.B))continue;
                double sign=(side==other?1:-1);
                double armA=(a.Point-free[id].Center).Cross(a.Direction),armB=(b.Point-free[id].Center).Cross(b.Direction);
                value+=sign*(free[id].InverseMass*a.Direction.Dot(b.Direction)+free[id].InverseInertia*armA*armB);
            }
            return value/(a.Root*b.Root);
        }
        double HEntry(int i,int j,double[] x,double[] w)
        {
            var r=rows[i];if(!r.Tangent)return NormalActive(x[i],w[i])?LocalB(i,j):(i==j?1:0);
            double gamma=r.Mu*rows[r.NormalIndex].Root/r.Root,bound=gamma*Math.Max(0,x[r.NormalIndex]);
            if(bound==0)return i==j?1:0;
            double trial=x[i]-w[i];
            if(trial>=bound)return (i==j?1:0)-(x[r.NormalIndex]>0&&j==r.NormalIndex?gamma:0);
            if(trial<=-bound)return (i==j?1:0)+(x[r.NormalIndex]>0&&j==r.NormalIndex?gamma:0);
            return LocalB(i,j);
        }
        static bool NormalActive(double impulse,double scaledSpeed)
        {
            double trial=impulse-scaledSpeed;
            return trial>0 || (trial==0 && scaledSpeed<0);
        }
        sealed class Block
        {
            public int Start,Size;public double[,] Inverse;
        }
        Block[] Preconditioner(double[] x,double[] w)
        {
            var blocks=new Block[starts.Length];
            for(int group=0;group<starts.Length;group++)
            {
                int start=starts[group],size=sizes[group];var a=new double[size,size*2];
                for(int i=0;i<size;i++)for(int j=0;j<size;j++)a[i,j]=HEntry(start+i,start+j,x,w)+(i==j?.001:0);
                for(int i=0;i<size;i++)a[i,size+i]=1;
                bool singular=false;
                for(int col=0;col<size;col++)
                {
                    int pivot=col;for(int row=col+1;row<size;row++)if(Math.Abs(a[row,col])>Math.Abs(a[pivot,col]))pivot=row;
                    if(Math.Abs(a[pivot,col])<1e-6){singular=true;break;}
                    if(pivot!=col)for(int j=0;j<size*2;j++){double t=a[col,j];a[col,j]=a[pivot,j];a[pivot,j]=t;}
                    double scale=a[col,col];for(int j=0;j<size*2;j++)a[col,j]/=scale;
                    for(int row=0;row<size;row++)if(row!=col)
                    {double factor=a[row,col];for(int j=0;j<size*2;j++)a[row,j]-=factor*a[col,j];}
                }
                var inverse=new double[size,size];
                for(int i=0;i<size;i++)for(int j=0;j<size;j++)
                {
                    double diagonal=HEntry(start+i,start+i,x,w)+.001;
                    if(Math.Abs(diagonal)<1e-6)diagonal=diagonal<0?-1e-6:1e-6;
                    inverse[i,j]=singular?(i==j?1/diagonal:0):a[i,size+j];
                }
                blocks[group]=new Block{Start=start,Size=size,Inverse=inverse};
            }
            return blocks;
        }
        double[] Precondition(Block[] blocks,double[] vector)
        {
            var output=new double[vector.Length];
            foreach(var block in blocks)for(int i=0;i<block.Size;i++)for(int j=0;j<block.Size;j++)output[block.Start+i]+=block.Inverse[i,j]*vector[block.Start+j];
            return output;
        }
        static double Dot(double[] a,double[] b){double r=0;for(int i=0;i<a.Length;i++)r+=a[i]*b[i];return r;}
        static double Norm(double[] x)=>Math.Sqrt(Dot(x,x));
        static double[] Scale(double[] x,double s){var y=new double[x.Length];for(int i=0;i<x.Length;i++)y[i]=x[i]*s;return y;}
        double[] SearchProduct(double[] x,double[] w,double[] vector,double shift)
        {
            var product=Derivative(x,w,vector);
            if(shift!=0)for(int i=0;i<product.Length;i++)product[i]+=shift*vector[i];
            return product;
        }
        // The fixed manifold preconditioner is right-applied in every GMRES(16)
        // cycle. A restart uses the true linear residual, not its Hessenberg estimate.
        double[] Direction(double[] x,double[] w,double[] f,Answer answer)
        {
            int n=f.Length,m=Math.Min(16,n);double initial=Norm(f);
            if(initial==0)return new double[n];
            var blocks=Preconditioner(x,w);
            double[] shifts={0,1e-5,1e-3};int[] budgets={256,128,128};
            for(int attempt=0;attempt<shifts.Length;attempt++)
            {
                double shift=shifts[attempt];var direction=new double[n];int usedBudget=0;
                while(usedBudget<budgets[attempt])
                {
                    var residual=SearchProduct(x,w,direction,shift);
                    for(int i=0;i<n;i++)residual[i]=-f[i]-residual[i];
                    double beta=Norm(residual);
                    if(beta<=.1*initial){answer.SearchShift=shift;return direction;}
                    if(double.IsNaN(beta)||double.IsInfinity(beta))break;
                    var v=new double[m+1][];v[0]=Scale(residual,1/beta);
                    var hess=new double[m+1,m];var cs=new double[m];var sn=new double[m];var g=new double[m+1];g[0]=beta;
                    int used=0;
                    for(int j=0;j<m && usedBudget<budgets[attempt];j++)
                    {
                        var q=SearchProduct(x,w,Precondition(blocks,v[j]),shift);
                        for(int pass=0;pass<2;pass++)for(int i=0;i<=j;i++)
                        {double projection=Dot(q,v[i]);hess[i,j]+=projection;for(int k=0;k<n;k++)q[k]-=projection*v[i][k];}
                        hess[j+1,j]=Norm(q);
                        v[j+1]=hess[j+1,j]>1e-14?Scale(q,1/hess[j+1,j]):new double[n];
                        for(int i=0;i<j;i++)
                        {double t=cs[i]*hess[i,j]+sn[i]*hess[i+1,j];hess[i+1,j]=-sn[i]*hess[i,j]+cs[i]*hess[i+1,j];hess[i,j]=t;}
                        double hyp=Math.Sqrt(hess[j,j]*hess[j,j]+hess[j+1,j]*hess[j+1,j]);
                        if(!(hyp>1e-14))break;
                        cs[j]=hess[j,j]/hyp;sn[j]=hess[j+1,j]/hyp;hess[j,j]=hyp;hess[j+1,j]=0;
                        g[j+1]=-sn[j]*g[j];g[j]=cs[j]*g[j];used=j+1;usedBudget++;answer.Krylov++;
                        if(Math.Abs(g[j+1])<=.1*initial)break;
                    }
                    if(used==0)break;
                    var coefficient=new double[used];
                    for(int i=used-1;i>=0;i--)
                    {double value=g[i];for(int j=i+1;j<used;j++)value-=hess[i,j]*coefficient[j];coefficient[i]=value/hess[i,i];}
                    for(int j=0;j<used;j++)
                    {var z=Precondition(blocks,v[j]);for(int k=0;k<n;k++)direction[k]+=coefficient[j]*z[k];}
                    answer.Restarts++;
                }
            }
            return null;
        }
        public Answer Solve(DBody[] expected=null)
        {
            var answer=new Answer();var x=new double[rows.Length];if(rows.Length==0){answer.Converged=true;return answer;}
            for(int iteration=0;iteration<64;iteration++)
            {
                var f=Residual(x,out var w,out double physical);double norm=Norm(f);
                answer.Residual=norm;answer.PhysicalResidual=physical;answer.Newton=iteration;
                Physical(x,w,out answer.NormalResidual,out answer.FrictionResidual,out answer.FirstFeature);
                if(physical<=1e-5)
                {
                    var projected=(double[])x.Clone();
                    for(int i=0;i<rows.Length;i++)if(!rows[i].Tangent)projected[i]=Math.Max(0,projected[i]);
                    for(int i=0;i<rows.Length;i++)if(rows[i].Tangent)
                    {var r=rows[i];double gamma=r.Mu*rows[r.NormalIndex].Root/r.Root,bound=gamma*projected[r.NormalIndex];projected[i]=Math.Max(-bound,Math.Min(bound,projected[i]));}
                    var checkedResidual=Residual(projected,out var checkedW,out double checkedPhysical);
                    if(checkedPhysical>1e-5)
                    {
                        x=projected;answer.Residual=Norm(checkedResidual);answer.PhysicalResidual=checkedPhysical;
                        Physical(x,checkedW,out answer.NormalResidual,out answer.FrictionResidual,out answer.FirstFeature);
                        continue;
                    }
                    x=projected;answer.Residual=Norm(checkedResidual);answer.PhysicalResidual=checkedPhysical;
                    Physical(x,checkedW,out answer.NormalResidual,out answer.FrictionResidual,out answer.FirstFeature);
                    answer.Converged=true;var u=EndpointMotion(x);
                    if(expected!=null)for(int i=0;i<expected.Length;i++)
                    {
                        answer.EndpointError=Math.Max(answer.EndpointError,Math.Abs(u[3*i]-(position?expected[i].Center.X-free[i].Center.X:expected[i].Velocity.X)));
                        answer.EndpointError=Math.Max(answer.EndpointError,Math.Abs(u[3*i+1]-(position?expected[i].Center.Y-free[i].Center.Y:expected[i].Velocity.Y)));
                        answer.EndpointError=Math.Max(answer.EndpointError,Math.Abs(u[3*i+2]-(position?expected[i].Angle-free[i].Angle:expected[i].Spin)));
                    }
                    return answer;
                }
                var direction=Direction(x,w,f,answer);
                if(direction==null){answer.Failure="LinearSolve";return answer;}
                bool accepted=false;
                for(int step=0;step<8;step++)
                {
                    double alpha=1.0/(1<<step);var trial=new double[x.Length];for(int i=0;i<x.Length;i++)trial[i]=x[i]+alpha*direction[i];
                    var next=Residual(trial,out _,out double candidatePhysical);double length=Norm(next);
                    if(double.IsNaN(length))continue;
                    if(length*length<=(1-1e-4*alpha)*norm*norm||candidatePhysical<=1e-5){x=trial;accepted=true;break;}
                }
                if(!accepted){answer.Failure="LineSearch";return answer;}
            }
            answer.Failure="NewtonBudget";return answer;
        }
    }
}
