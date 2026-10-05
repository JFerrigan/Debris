using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace Debris.Simulation.Tests
{
    public sealed class CoupledPackedReferenceTests
    {
        static DBody Body(double x,double y,double vx=0,double vy=0,double mass=1)
            =>new DBody{Center=new DVec(x,y),Velocity=new DVec(vx,vy),InverseMass=mass==0?0:1/mass,InverseInertia=mass==0?0:6/mass};
        [Test] public void SeparateV2DoubleSolveMatchesDirectEndpointMotion()
        {
            var free=new[]{Body(0,0,2,0),Body(1,0,0,0)};
            var p=new CoupledPackedReference.Point{A=0,B=1,P=new DVec(.5,0),N=new DVec(1,0),BaseA=free[0].Center,BaseB=free[1].Center};
            var manifolds=new List<CoupledPackedReference.Manifold>{new CoupledPackedReference.Manifold{Points=new[]{p}}};
            var v2=new CoupledV2Double(free,manifolds,0,1.0/60);
            var x=new[]{1.0,0.0};var z=new[]{.3,.2};double eps=1e-6;
            var plus=new[]{x[0]+eps*z[0],x[1]+eps*z[1]};var minus=new[]{x[0]-eps*z[0],x[1]-eps*z[1]};
            var fp=v2.Residual(plus,out _,out _);var fm=v2.Residual(minus,out _,out _);var derivative=v2.DirectionalDerivative(x,z);
            for(int i=0;i<2;i++)Assert.That(derivative[i],Is.EqualTo((fp[i]-fm[i])/(2*eps)).Within(1e-8));
            var direct=CoupledContactReference.SolveManifold(new DSystem(free,new[]{new DRow{A=0,B=1,Point=p.P,Direction=p.N},new DRow{A=0,B=1,Point=p.P,Direction=new DVec(0,1)}}),1,0,1.0/60);
            Assert.That(direct.Conclusive,Is.True,direct.Diagnostic);
            var expected=(DBody[])free.Clone();expected[0].Velocity=new DVec(1,0);expected[1].Velocity=new DVec(1,0);
            var answer=v2.Solve(expected);
            TestContext.WriteLine("V2 tiny rank={0} residual={1:R} physical={2:R} endpoint={3:R} newton={4} krylov={5}",direct.Rank,answer.Residual,answer.PhysicalResidual,answer.EndpointError,answer.Newton,answer.Krylov);
            Assert.That(answer.Converged,Is.True,answer.Failure);Assert.That(answer.EndpointError,Is.LessThanOrEqualTo(1e-8));
        }
        static readonly string Root=Path.Combine("Logs","b3r-v2-0a","20260925T005106Z-f0c73f7-3c17c6d83c514f76b0db7db4c9f06ce1","inputs");
        static readonly string[] Names={"shared-4-2","shared-8-4","shared-12-6","resting-4-2","resting-8-4","resting-12-6","combined-4-2","combined-8-4","combined-12-6"};
        static readonly string[] Hashes={"ffb1527d47c1ae9ee9857fcb86941899c8bf66ff5264ab9bbf2da9820f6ab92e","02c901cba69570be66875a355b1e490f5b396e95786d2df6f4ea838f6811085f","977cfd2b0695cec9b648101d284eef7733468dcd5c2ea232f69184c4298d6ef5","0f65aadc2beaa4810f4674d4ca88014e9310b442393cfe07875fb28f31539e77","b668fc5a7d39b561ae4175aed0a2dd4ff25823c7ccdf84743010fca8ebc52816","806eee6ce1035212cd975e37b7978c06066a4f92608a8865fe8e465e03e58c66","2bd4e6a806c146baab3b7513459d83b5cf53175ae38f18c054bd978345293b01","13677b70b524007eee2252df9caab4c7d6caaed691e665719b24449d2f9e6ca4","2e5f40eff028a6a89763de8546e8916e5487083ec9f71aad477e24cfe1665549"};
        [Test, Explicit("Full nine-archive V2-0C workload"), Timeout(6000000)] public void NineExactPackedInputsProduceClassifiedReferenceAndV2Comparison()
        {
            for(int i=0;i<Names.Length;i++)
            {
                var archive=DArchive.Read(Path.Combine(Root,Names[i]),Hashes[i]);
                var result=CoupledPackedReference.Run(archive);
                CoupledV2Double.Answer v2=null,v2Position=null;
                if(result.FreeBodies!=null&&result.VelocityContacts!=null)
                {
                    var comparison=new CoupledV2Double(result.FreeBodies,result.VelocityContacts,archive.Friction,archive.Dt/result.Substeps);
                    v2=comparison.Solve(result.VelocityBodies);
                }
                if(result.PositionFreeBodies!=null&&result.PositionContacts!=null)
                    v2Position=new CoupledV2Double(result.PositionFreeBodies,result.PositionContacts,0,archive.Dt/result.Substeps,true).Solve(result.PositionBodies);
                TestContext.WriteLine("{0} hash={1} substeps={2} points-max={3} local-rank-max={4} normal-rank-upper-max={5} sweeps={6} refresh={7} velocity={8:R} position={9:R} grain={10:R} solid={11:R} accepted={12} first={13} feature={14} v2={15} v2-residual={16:R} v2-physical={17:R} endpoint={18:R} v2-position={19} position-residual={20:R} position-endpoint={21:R} v2-feature={22} v2-position-feature={23} v2-normal={24:R} v2-friction={25:R}",
                    Names[i],archive.Hash,result.Substeps,result.Points,result.LocalRank,result.RankUpper,result.Sweeps,result.Refreshes,result.VelocityResidual,result.PositionResidual,result.GrainGap,result.SolidGap,result.Converged,result.FirstFailure,result.FirstFeature,v2==null?"unavailable":v2.Converged?"converged":v2.Failure,v2==null?double.NaN:v2.Residual,v2==null?double.NaN:v2.PhysicalResidual,v2==null?double.NaN:v2.EndpointError,v2Position==null?"unavailable":v2Position.Converged?"converged":v2Position.Failure,v2Position==null?double.NaN:v2Position.PhysicalResidual,v2Position==null?double.NaN:v2Position.EndpointError,v2?.FirstFeature,v2Position?.FirstFeature,v2==null?double.NaN:v2.NormalResidual,v2==null?double.NaN:v2.FrictionResidual);
                if(result.Converged)
                {
                    Assert.That(result.VelocityResidual,Is.LessThanOrEqualTo(1e-8));
                    Assert.That(result.PositionResidual,Is.LessThanOrEqualTo(1e-8));
                    Assert.That(result.GrainGap,Is.LessThanOrEqualTo(.01));
                    Assert.That(result.SolidGap,Is.LessThanOrEqualTo(.001));
                    Assert.That(v2,Is.Not.Null);
                    Assert.That(v2.Converged,Is.True,v2.Failure);
                    Assert.That(v2.EndpointError,Is.LessThanOrEqualTo(1e-5));
                    if(result.PositionFreeBodies!=null)
                    {Assert.That(v2Position,Is.Not.Null);Assert.That(v2Position.Converged,Is.True,v2Position.Failure);Assert.That(v2Position.EndpointError,Is.LessThanOrEqualTo(1e-5));}
                }
                else Assert.That(result.FirstFailure,Is.Not.Empty,"Inconclusive reference must report its first failing feature");
            }
        }
        [TestCase(8),TestCase(32),TestCase(50),Explicit("Offline finite-boundary chain"),Timeout(650000)]
        public void FiniteBoundaryChainReportsCoupledMotion(int count)
        {
            var result=CoupledPackedReference.Run(FiniteChain(count));
            TestContext.WriteLine("chain={0} accepted={1} first={2} feature={3} sweeps={4} velocity={5:R} position={6:R} grain={7:R} solid={8:R}",
                count,result.Converged,result.FirstFailure,result.FirstFeature,result.Sweeps,result.VelocityResidual,result.PositionResidual,result.GrainGap,result.SolidGap);
            Assert.That(result.Converged,Is.True,result.FirstFailure+" "+result.FirstFeature);
            Assert.That(result.VelocityBodies[count].Velocity.X,Is.GreaterThan(0));
            Assert.That(result.VelocityBodies[count+1].Velocity.X,Is.GreaterThan(0));
        }
        [Test] public void TwoFinitePatchBodiesExchangeMomentum()
        {
            var archive=new DArchive{ActiveGrains=0,GrainCapacity=0,BodyEndpointStart=0,
                Bodies=new[]{Body(0,0,2,0,10),Body(1,0,0,0,10)},Grains=Array.Empty<DBox>(),
                Patches=new[]{new CoupledReferenceGeometry.Patch(new DVec(),new DVec(.5,.5),1),
                    new CoupledReferenceGeometry.Patch(new DVec(),new DVec(.5,.5),2)},
                PatchBodies=new[]{0,1},LocalCOM=new DVec[2],Dt=1.0/60,Friction=0};
            var result=CoupledPackedReference.Run(archive);
            Assert.That(result.Converged,Is.True,result.FirstFailure+" "+result.FirstFeature);
            Assert.That(result.Points,Is.GreaterThanOrEqualTo(2));
            Assert.That(result.VelocityBodies[0].Velocity.X,Is.EqualTo(1).Within(1e-7));
            Assert.That(result.VelocityBodies[1].Velocity.X,Is.EqualTo(1).Within(1e-7));
        }
        internal static DArchive FiniteChain(int count)
        {
            var archive=new DArchive{ActiveGrains=count,GrainCapacity=count,BodyEndpointStart=count,
                Bodies=new DBody[count+2],Grains=new DBox[count],Patches=new CoupledReferenceGeometry.Patch[2],
                PatchBodies=new[]{count,count+1},LocalCOM=new DVec[count+2],Dt=1.0/60,Friction=0,Force=new DVec(6000,0)};
            for(int i=0;i<count;i++)
            {archive.Bodies[i]=Body(i,0);archive.Grains[i]=new DBox(new DVec(i,0),new DVec(.5,.5),0,(ulong)(i+1));}
            archive.Bodies[count]=Body(-1,0,0,0,10);archive.Bodies[count+1]=Body(count,0,0,0,10);
            archive.Patches[0]=new CoupledReferenceGeometry.Patch(new DVec(),new DVec(.5,.5),1);
            archive.Patches[1]=new CoupledReferenceGeometry.Patch(new DVec(),new DVec(.5,.5),2);
            return archive;
        }
    }
}
