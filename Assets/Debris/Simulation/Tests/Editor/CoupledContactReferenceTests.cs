using System;
using System.IO;
using NUnit.Framework;

namespace Debris.Simulation.Tests
{
    public sealed class CoupledContactReferenceTests
    {
        static DBody Body(double x,double y,double vx,double vy,double mass=1,double inertia=1,double spin=0)
            =>new DBody{Center=new DVec(x,y),Velocity=new DVec(vx,vy),Spin=spin,InverseMass=mass==0?0:1/mass,InverseInertia=inertia==0?0:1/inertia};
        static DSystem One(DBody a,DBody b,DVec point,double gap=0)
            =>new DSystem(new[]{a,b},new[]{new DRow{A=0,B=1,Point=point,Direction=new DVec(1,0),Gap=gap},new DRow{A=0,B=1,Point=point,Direction=new DVec(0,1),Gap=gap}});
        static DAnswer Solve(DSystem s,double mu=0)=>CoupledContactReference.SolveManifold(s,1,mu,1.0/60);
        static void Near(double a,double b,double tol=1e-8)=>Assert.That(a,Is.EqualTo(b).Within(tol));
        static void Good(DAnswer a){Assert.That(a.Conclusive,Is.True,a.Diagnostic);Assert.That(a.Residual,Is.LessThanOrEqualTo(1e-8));}
        [Test] public void MatrixIsSymmetricPositiveSemidefiniteAndMatchesEndpointGather()
        {
            var s=One(Body(0,0,2,1,2,3),Body(1,0,-1,-2,5,7),new DVec(.5,.25));
            Near(s.A[0,0],1.0/2+1.0/5+.25*.25*(1.0/3+1.0/7));
            Near(s.A[0,1],s.A[1,0]);
            for(int i=-2;i<=2;i++)for(int j=-2;j<=2;j++){double q=i*i*s.A[0,0]+2*i*j*s.A[0,1]+j*j*s.A[1,1];Assert.That(q,Is.GreaterThanOrEqualTo(-1e-12));}
            var impulse=new[]{2.0,-.4};var gathered=s.GatherApply(impulse);var applied=s.MultiplyJ(s.Apply(impulse));
            for(int i=0;i<2;i++){Near(gathered[i],applied[i]);Near(gathered[i],s.A[i,0]*impulse[0]+s.A[i,1]*impulse[1]);}
            var delta=s.Apply(impulse);Near(2*delta[0]+5*delta[3],0);Near(2*delta[1]+5*delta[4],0);
            // About the world origin, orbital plus spin angular impulse cancels.
            double torque=2*(0*delta[1]-0*delta[0])+3*delta[2]+5*(1*delta[4])+7*delta[5];Near(torque,0);
        }
        [Test] public void InelasticCentralMassRatiosAndAnchoredFiniteCounterparts()
        {
            var equal=Solve(One(Body(0,0,10,0),Body(1,0,0,0),new DVec(.5,0)));Good(equal);Near(equal.Impulses[0],5);Near(equal.Motion[0],0);
            var heavy=Solve(One(Body(0,0,10,0,10000,10000),Body(1,0,0,0),new DVec(.5,0)));Good(heavy);
            TestContext.WriteLine("heavy rank={0} residual={1:R} candidates={2}",heavy.Rank,heavy.Residual,heavy.Candidates);
            Near(heavy.Impulses[0],100000.0/10001);Near(10-heavy.Impulses[0]/10000,100000.0/10001);Near(heavy.Impulses[0],100000.0/10001);
            var anchored=Solve(One(Body(0,0,10,0),Body(1,0,0,0,0,0),new DVec(.5,0)));Good(anchored);Near(anchored.Impulses[0],10);
        }
        [Test] public void OffCenterImpactMakesAnalyticalSpin()
        {
            var s=One(Body(0,0,10,0,1,1),Body(1,0,0,0,1,1),new DVec(.5,.5));var result=Solve(s);Good(result);
            Near(s.A[0,0],2.5);Near(result.Impulses[0],4);var delta=s.Apply(result.Impulses);Near(delta[2],2);Near(delta[5],-2);
        }
        [Test] public void StickingBothSlidingDirectionsFrictionlessAndNoLift()
        {
            var stick=Solve(One(Body(0,0,1,0),Body(1,0,0,.1),new DVec(.5,0)),.3);Good(stick);Near(stick.Impulses[0],.5);Near(stick.Impulses[1],-.04);
            var positive=Solve(One(Body(0,0,1,0),Body(1,0,0,2),new DVec(.5,0)),.3);Good(positive);Near(positive.Impulses[1],-.15);
            var negative=Solve(One(Body(0,0,1,0),Body(1,0,0,-2),new DVec(.5,0)),.3);Good(negative);Near(negative.Impulses[1],.15);
            var none=Solve(One(Body(0,0,1,0),Body(1,0,0,2),new DVec(.5,0)),0);Good(none);Near(none.Impulses[1],0);
            var noLoad=Solve(One(Body(0,0,0,0),Body(1,0,0,2),new DVec(.5,0)),.3);Good(noLoad);Near(noLoad.Impulses[0],0);Near(noLoad.Impulses[1],0);
            var speculative=CoupledContactReference.SolveManifold(One(Body(0,0,2,0),Body(1.01,0,0,2),new DVec(.505,0),.01),1,.3,1.0/60);
            Good(speculative);Near(speculative.Impulses[0],.7);Near(speculative.Impulses[1],0);Near(speculative.Motion[0],-.6);
        }
        [Test] public void TwoPointSupportCenteredAndOffCenterLoad()
        {
            var anchor=Body(0,0,0,0,0,0);var moving=Body(0,1,0,-2,1,1);
            var rows=new[]{new DRow{A=0,B=1,Point=new DVec(-.5,.5),Direction=new DVec(0,1)},new DRow{A=0,B=1,Point=new DVec(1.0,.5),Direction=new DVec(1,0)},
                new DRow{A=0,B=1,Point=new DVec(.5,.5),Direction=new DVec(0,1)},new DRow{A=0,B=1,Point=new DVec(1.0,.5),Direction=new DVec(1,0)}};
            var centered=CoupledContactReference.SolveManifold(new DSystem(new[]{anchor,moving},rows),2,.3,1.0/60);Good(centered);Near(centered.Impulses[0],1);Near(centered.Impulses[2],1);Near(centered.Motion[0],0);Near(centered.Motion[2],0);
            TestContext.WriteLine("flat rank={0} residual={1:R} candidates={2}",centered.Rank,centered.Residual,centered.Candidates);
            moving.Spin=-1;var shifted=CoupledContactReference.SolveManifold(new DSystem(new[]{anchor,moving},rows),2,.3,1.0/60);Good(shifted);
            Assert.That(shifted.Impulses[2],Is.GreaterThan(shifted.Impulses[0]));
        }
        [Test] public void RedundantSupportUsesRankAndComparesMotion()
        {
            var bodies=new[]{Body(0,0,0,0,0,0),Body(0,1,0,-2)};
            var rows=new DRow[2];for(int i=0;i<2;i++)rows[i]=new DRow{A=0,B=1,Point=new DVec(0,.5),Direction=new DVec(0,1)};
            var s=new DSystem(bodies,rows);var a=CoupledContactReference.SolveFrictionless(s,new double[2]);Good(a);
            TestContext.WriteLine("duplicate rank={0} residual={1:R} candidates={2}",a.Rank,a.Residual,a.Candidates);
            Assert.That(a.Rank,Is.LessThan(2));Near(a.Impulses[0]+a.Impulses[1],2);Near(a.Motion[0],0);Near(a.Motion[1],0);
            // Duplicate feature is rejected by geometry before it can add another row.
            var features=new System.Collections.Generic.HashSet<string>();var points=CoupledReferenceGeometry.BoxBox(new DBox(new DVec(),new DVec(.5,.5),0,1),new DBox(new DVec(1,0),new DVec(.5,.5),0,2));
            foreach(var p in points)Assert.That(features.Add(p.Key),Is.True);
        }
        [Test] public void EightIndependentFrictionlessContactsEnumerateAllActiveSets()
        {
            var bodies=new DBody[9];bodies[0]=Body(0,0,0,0,0,0);var rows=new DRow[8];
            for(int i=0;i<8;i++){bodies[i+1]=Body(i+1,0,-1,0);rows[i]=new DRow{A=0,B=i+1,Point=new DVec(i+1,0),Direction=new DVec(1,0)};}
            var answer=CoupledContactReference.SolveFrictionless(new DSystem(bodies,rows),new double[8]);Good(answer);
            TestContext.WriteLine("eight rank={0} residual={1:R} candidates={2}",answer.Rank,answer.Residual,answer.Candidates);
            Assert.That(answer.Candidates,Is.EqualTo(256));Assert.That(answer.Rank,Is.EqualTo(8));
            for(int i=0;i<8;i++){Near(answer.Impulses[i],1);Near(answer.Motion[i],0);}
        }
        [Test] public void FrictionlessGlobalChainAndFreshPositionKkt()
        {
            var bodies=new[]{Body(0,0,0,0,0,0),Body(1,0,-1,0),Body(2,0,-1,0)};
            var rows=new[]{new DRow{A=0,B=1,Point=new DVec(.5,0),Direction=new DVec(1,0)},new DRow{A=1,B=2,Point=new DVec(1.5,0),Direction=new DVec(1,0)}};
            var s=new DSystem(bodies,rows);var v=CoupledContactReference.SolveFrictionless(s,new double[2]);Good(v);Near(v.Impulses[0],2);Near(v.Impulses[1],1);Near(v.Motion[0],0);Near(v.Motion[1],0);
            var p=CoupledContactReference.CorrectPosition(s,new[]{-.2,-.2},0);Good(p);Near(p.Impulses[0],.6);Near(p.Impulses[1],.4);Near(p.Motion[0]-.2,0);Near(p.Motion[1]-.2,0);
            TestContext.WriteLine("chain velocity rank={0} residual={1:R}; position rank={2} residual={3:R}",v.Rank,v.Residual,p.Rank,p.Residual);
        }
        [Test] public void AllNineImmutableArchivesConvertWithCapacityAndBodyIndices()
        {
            string root=Path.Combine("Logs","b3r-v2-0a","20260925T005106Z-f0c73f7-3c17c6d83c514f76b0db7db4c9f06ce1","inputs");
            string[] names={"shared-4-2","shared-8-4","shared-12-6","resting-4-2","resting-8-4","resting-12-6","combined-4-2","combined-8-4","combined-12-6"};
            string[] hashes={"ffb1527d47c1ae9ee9857fcb86941899c8bf66ff5264ab9bbf2da9820f6ab92e","02c901cba69570be66875a355b1e490f5b396e95786d2df6f4ea838f6811085f","977cfd2b0695cec9b648101d284eef7733468dcd5c2ea232f69184c4298d6ef5","0f65aadc2beaa4810f4674d4ca88014e9310b442393cfe07875fb28f31539e77","b668fc5a7d39b561ae4175aed0a2dd4ff25823c7ccdf84743010fca8ebc52816","806eee6ce1035212cd975e37b7978c06066a4f92608a8865fe8e465e03e58c66","2bd4e6a806c146baab3b7513459d83b5cf53175ae38f18c054bd978345293b01","13677b70b524007eee2252df9caab4c7d6caaed691e665719b24449d2f9e6ca4","2e5f40eff028a6a89763de8546e8916e5487083ec9f71aad477e24cfe1665549"};
            for(int i=0;i<names.Length;i++)
            {var archive=DArchive.Read(Path.Combine(root,names[i]),hashes[i]);Assert.That(archive.Hash,Is.EqualTo(hashes[i]));Assert.That(archive.ActiveGrains,Is.EqualTo(i<6?2500:8192));Assert.That(archive.GrainCapacity,Is.EqualTo(i<6?2500:8192));Assert.That(archive.BodyEndpointStart,Is.EqualTo(archive.GrainCapacity));Assert.That(archive.Bodies.Length,Is.GreaterThan(archive.GrainCapacity));Near(archive.Dt,1f/60f,0);Assert.That(archive.Grains[0].Center.X,Is.EqualTo(archive.Bodies[0].Center.X));Assert.That(archive.Patches.Length,Is.EqualTo(archive.PatchBodies.Length));foreach(int endpoint in archive.PatchBodies)Assert.That(endpoint,Is.LessThan(archive.Bodies.Length));}
        }
    }
}
