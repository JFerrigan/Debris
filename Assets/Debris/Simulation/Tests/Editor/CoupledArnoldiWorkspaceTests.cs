using Debris.Simulation.CoupledContacts;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace Debris.Simulation.Tests
{
    public sealed class CoupledArnoldiWorkspaceTests
    {
        [TestCase(1e-5f)]
        [TestCase(1e-3f)]
        public void RegularizedSearchKeepsUnshiftedLineSearch(float eta)
        {
            Assume.That(SystemInfo.supportsComputeShaders,Is.True);
            float inverseRoot=1/Mathf.Sqrt(2);
            var bodies=new[]{
                new CoupledContactBody{Center=Vector2.zero,InverseMass=1},
                new CoupledContactBody{Center=Vector2.right,InverseMass=1}
            };
            var rows=new[]{
                new CoupledContactRow{A=0,B=1,Point=Vector2.right*.5f,
                    Direction=Vector2.right,InverseRoot=inverseRoot},
                new CoupledContactRow{A=0,B=1,Point=Vector2.right*.5f,
                    Direction=Vector2.up,InverseRoot=inverseRoot}
            };
            using(var op=new CoupledContactOperator(bodies,rows))
            using(var velocity=new CoupledVelocityResidual(op,1))
            using(var preconditioner=new CoupledManifoldPreconditioner(op,velocity))
            using(var arnoldi=new CoupledArnoldiWorkspace(op,velocity,preconditioner))
            using(var commands=new CommandBuffer())
            {
                velocity.ImpulseInput.SetData(new float[]{0,0});
                velocity.FreeMotionInput.SetData(new[]{
                    new Vector4(1,0,0,0),Vector4.zero});
                velocity.RecordGraph(commands);
                velocity.RecordResidual(commands);
                arnoldi.RecordInitialize(commands,eta);
                arnoldi.RecordStep(commands,0);
                arnoldi.RecordDirectionAndVerify(commands,1);
                Graphics.ExecuteCommandBuffer(commands);
                var linear=new float[4];velocity.LinearResult.GetData(linear);
                Assert.That(linear[2],Is.EqualTo(1));
                Assert.That(linear[1],Is.LessThan(1e-5));
                var direction=new float[2];velocity.ImpulseInput.GetData(direction);
                Assert.That(direction[0],
                    Is.EqualTo(inverseRoot/(1+eta)).Within(1e-5));

                commands.Clear();
                velocity.RecordLineSearch(commands,arnoldi.VerifiedDirectionGate);
                Graphics.ExecuteCommandBuffer(commands);
                var selection=new uint[2];var merit=new float[1];
                velocity.TrialSelection.GetData(selection);
                velocity.Merit.GetData(merit);
                Assert.That(selection[0],Is.Zero);
                Assert.That(merit[0],Is.LessThan(.25f),
                    "The physical merit uses unshifted F");
            }
        }

        [Test] public void RestartUsesTrueResidualAndAccumulatesDirection()
        {
            Assume.That(SystemInfo.supportsComputeShaders,Is.True);
            float inverseRoot=1/Mathf.Sqrt(2);
            var bodies=new[]{
                new CoupledContactBody{Center=Vector2.zero,InverseMass=1},
                new CoupledContactBody{Center=Vector2.right,InverseMass=1},
                new CoupledContactBody{Center=Vector2.right*2,InverseMass=1}
            };
            var rows=new CoupledContactRow[4];
            for(int point=0;point<2;point++)
            {
                rows[2*point]=new CoupledContactRow{
                    A=(uint)point,B=(uint)(point+1),
                    Point=new Vector2(point+.5f,0),
                    Direction=Vector2.right,InverseRoot=inverseRoot};
                rows[2*point+1]=new CoupledContactRow{
                    A=(uint)point,B=(uint)(point+1),
                    Point=new Vector2(point+.5f,0),
                    Direction=Vector2.up,InverseRoot=inverseRoot};
            }
            using(var op=new CoupledContactOperator(bodies,rows))
            using(var velocity=new CoupledVelocityResidual(op,1))
            using(var preconditioner=new CoupledManifoldPreconditioner(op,velocity))
            using(var arnoldi=new CoupledArnoldiWorkspace(op,velocity,preconditioner))
            using(var commands=new CommandBuffer())
            {
                velocity.ImpulseInput.SetData(new float[4]);
                velocity.FreeMotionInput.SetData(new[]{
                    new Vector4(3,0,0,0),new Vector4(1,0,0,0),Vector4.zero});
                velocity.RecordGraph(commands);
                velocity.RecordResidual(commands);
                arnoldi.RecordInitialize(commands);
                arnoldi.RecordStep(commands,0);
                arnoldi.RecordDirectionAndVerify(commands,1);
                Graphics.ExecuteCommandBuffer(commands);
                var first=new float[4];velocity.LinearResult.GetData(first);
                Assert.That(first[2],Is.EqualTo(1));
                Assert.That(first[1],Is.GreaterThan(.1f));

                commands.Clear();
                arnoldi.RecordRestart(commands);
                arnoldi.RecordStep(commands,0);
                arnoldi.RecordDirectionAndVerify(commands,1);
                Graphics.ExecuteCommandBuffer(commands);
                var second=new float[4];velocity.LinearResult.GetData(second);
                Assert.That(second[2],Is.EqualTo(1));
                Assert.That(second[1],Is.LessThan(first[1]));
                var control=new uint[6];arnoldi.WorkControl.GetData(control);
                Assert.That(control[1],Is.EqualTo(4u),
                    "Two Arnoldi columns and two true checks share one cap across restarts");
                Assert.That(control[4],Is.EqualTo(16u));
            }
        }

        [Test] public void OneDimensionalContactFindsAZeroOrthogonalRemainder()
        {
            Assume.That(SystemInfo.supportsComputeShaders,Is.True);
            float inverseRoot=1/Mathf.Sqrt(2);
            var bodies=new[]{
                new CoupledContactBody{Center=Vector2.zero,InverseMass=1},
                new CoupledContactBody{Center=Vector2.right,InverseMass=1}
            };
            var rows=new[]{
                new CoupledContactRow{A=0,B=1,Point=Vector2.right*.5f,
                    Direction=Vector2.right,InverseRoot=inverseRoot},
                new CoupledContactRow{A=0,B=1,Point=Vector2.right*.5f,
                    Direction=Vector2.up,InverseRoot=inverseRoot}
            };
            using(var op=new CoupledContactOperator(bodies,rows))
            using(var velocity=new CoupledVelocityResidual(op,1))
            using(var preconditioner=new CoupledManifoldPreconditioner(op,velocity))
            using(var arnoldi=new CoupledArnoldiWorkspace(op,velocity,preconditioner))
            using(var commands=new CommandBuffer())
            {
                velocity.ImpulseInput.SetData(new float[]{0,0});
                velocity.FreeMotionInput.SetData(new[]{
                    new Vector4(1,0,0,0),Vector4.zero});
                velocity.RecordGraph(commands);
                velocity.RecordResidual(commands);
                arnoldi.RecordInitialize(commands);
                for(int column=0;column<16;column++)
                    arnoldi.RecordStep(commands,column);
                arnoldi.RecordDirectionAndVerify(commands);
                Graphics.ExecuteCommandBuffer(commands);
                var h=new float[17*16];var status=new uint[16];
                var solveStatus=new uint[1];var linear=new float[4];
                var estimated=new float[1];
                var control=new uint[6];
                arnoldi.Hessenberg.GetData(h);
                arnoldi.StepStatus.GetData(status);
                arnoldi.SolveStatus.GetData(solveStatus);
                arnoldi.EstimatedRelativeResidual.GetData(estimated);
                arnoldi.WorkControl.GetData(control);
                velocity.LinearResult.GetData(linear);
                Assert.That(h[0],Is.EqualTo(1/1.001f).Within(1e-5));
                Assert.That(h[16],Is.Zero.Within(1e-6));
                Assert.That(status[0],Is.EqualTo(1u),
                    "Exact one-dimensional Krylov closure is a breakdown fact");
                Assert.That(solveStatus[0],Is.Zero);
                Assert.That(estimated[0],Is.Zero.Within(1e-5));
                Assert.That(control[1],Is.EqualTo(2u),
                    "One Arnoldi product and one true verification must be counted");
                Assert.That(control[2],Is.EqualTo(1u),
                    "The later recorded columns must remain masked");
                Assert.That(control[3],Is.EqualTo(5u));
                Assert.That(linear[3],Is.EqualTo(1).Within(1e-5),
                    "The reconstructed direction needs a true linear check");
                Assert.That(linear[1],Is.Zero.Within(1e-5));

                commands.Clear();
                velocity.RecordLineSearch(commands,arnoldi.VerifiedDirectionGate);
                arnoldi.RecordRetry(commands,1e-5f,true);
                for(int column=0;column<16;column++)
                    arnoldi.RecordStep(commands,column);
                arnoldi.RecordDirectionAndVerify(commands);
                Graphics.ExecuteCommandBuffer(commands);
                arnoldi.WorkControl.GetData(control);
                var selection=new uint[2];velocity.TrialSelection.GetData(selection);
                var free=new Vector4[2];velocity.FreeMotionInput.GetData(free);
                Assert.That(selection[0],Is.Zero);
                Assert.That(control[1],Is.EqualTo(2u),
                    "A successful line search must mask the unused retry");
                Assert.That(control[5],Is.Zero);
                Assert.That(free[0].x,Is.EqualTo(1).Within(1e-6),
                    "A masked direction must restore immutable free motion");
            }
        }

        [Test] public void BoundedNewtonUpdatePreservesFirstAcceptedCandidate()
        {
            Assume.That(SystemInfo.supportsComputeShaders,Is.True);
            float inverseRoot=1/Mathf.Sqrt(2);
            var bodies=new[]{
                new CoupledContactBody{Center=Vector2.zero,InverseMass=1},
                new CoupledContactBody{Center=Vector2.right,InverseMass=1}
            };
            var rows=new[]{
                new CoupledContactRow{A=0,B=1,Point=Vector2.right*.5f,
                    Direction=Vector2.right,InverseRoot=inverseRoot},
                new CoupledContactRow{A=0,B=1,Point=Vector2.right*.5f,
                    Direction=Vector2.up,InverseRoot=inverseRoot}
            };
            using(var op=new CoupledContactOperator(bodies,rows))
            using(var velocity=new CoupledVelocityResidual(op,1))
            using(var preconditioner=new CoupledManifoldPreconditioner(op,velocity))
            using(var arnoldi=new CoupledArnoldiWorkspace(op,velocity,preconditioner))
            using(var commands=new CommandBuffer())
            {
                var update=new CoupledVelocityNewtonUpdate(op,velocity,arnoldi);
                velocity.ImpulseInput.SetData(new float[]{0,0});
                velocity.FreeMotionInput.SetData(new[]{
                    new Vector4(1,0,0,0),Vector4.zero});
                velocity.RecordGraph(commands);
                update.Record(commands,16);
                Graphics.ExecuteCommandBuffer(commands);
                var control=new uint[6];var selection=new uint[2];
                var projection=new uint[1];var input=new float[2];
                arnoldi.WorkControl.GetData(control);
                velocity.TrialSelection.GetData(selection);
                velocity.ProjectionStatus.GetData(projection);
                velocity.ImpulseInput.GetData(input);
                Assert.That(control[1],Is.EqualTo(2u));
                Assert.That(control[5],Is.Zero,
                    "Accepted first direction must skip both search-only retries");
                Assert.That(selection[0],Is.Zero,
                    "Masked retry slots must retain the accepted trial");
                Assert.That(projection[0],Is.EqualTo(2u));
                Assert.That(input[0],Is.EqualTo(inverseRoot).Within(1e-5));
            }
        }

        [Test] public void VelocitySolveMasksLaterNewtonUpdatesAfterAcceptance()
        {
            Assume.That(SystemInfo.supportsComputeShaders,Is.True);
            float inverseRoot=1/Mathf.Sqrt(2);
            var bodies=new[]{
                new CoupledContactBody{Center=Vector2.zero,InverseMass=1},
                new CoupledContactBody{Center=Vector2.right,InverseMass=1}
            };
            var rows=new[]{
                new CoupledContactRow{A=0,B=1,Point=Vector2.right*.5f,
                    Direction=Vector2.right,InverseRoot=inverseRoot},
                new CoupledContactRow{A=0,B=1,Point=Vector2.right*.5f,
                    Direction=Vector2.up,InverseRoot=inverseRoot}
            };
            using(var op=new CoupledContactOperator(bodies,rows))
            using(var velocity=new CoupledVelocityResidual(op,1))
            using(var preconditioner=new CoupledManifoldPreconditioner(op,velocity))
            using(var arnoldi=new CoupledArnoldiWorkspace(op,velocity,preconditioner))
            using(var solve=new CoupledVelocitySolve(op,velocity,arnoldi))
            using(var commands=new CommandBuffer())
            {
                velocity.ImpulseInput.SetData(new float[]{0,0});
                velocity.FreeMotionInput.SetData(new[]{
                    new Vector4(1,0,0,0),Vector4.zero});
                velocity.RecordGraph(commands);
                solve.Record(commands,CoupledSolveProfile.C1);
                Graphics.ExecuteCommandBuffer(commands);
                var result=new uint[8];var input=new float[2];
                solve.Control.GetData(result);
                velocity.ImpulseInput.GetData(input);
                Assert.That(result[0],Is.Zero);
                Assert.That(result[1],Is.EqualTo(1u));
                Assert.That(result[2],Is.EqualTo(1u),
                    "Three later Newton slots must remain masked");
                Assert.That(result[3],Is.Zero);
                Assert.That(result[5],Is.EqualTo(2u),
                    "Only one Arnoldi product and true check execute");
                Assert.That(input[0],Is.EqualTo(inverseRoot).Within(1e-5));
            }
        }
    }
}
