using System;
using Debris.Simulation.CoupledContacts;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace Debris.Simulation.Tests
{
    public sealed class CoupledVelocityResidualTests
    {
        [Test] public void GatedDerivativeSkipsInactiveProductAndRunsWhenEnabled()
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
            using(var gate=new GraphicsBuffer(GraphicsBuffer.Target.Structured,1,4))
            using(var commands=new CommandBuffer())
            {
                velocity.ImpulseInput.SetData(new float[]{0,0});
                velocity.FreeMotionInput.SetData(new[]{
                    new Vector4(1,0,0,0),Vector4.zero});
                velocity.RecordGraph(commands);
                velocity.RecordResidual(commands);
                Graphics.ExecuteCommandBuffer(commands);
                velocity.ImpulseInput.SetData(new float[]{1,0});
                velocity.Derivative.SetData(new float[]{7,9});
                gate.SetData(new uint[]{0});
                commands.Clear();
                velocity.RecordDerivativeMasked(commands,gate);
                Graphics.ExecuteCommandBuffer(commands);
                var result=new float[2];velocity.Derivative.GetData(result);
                Assert.That(result,Is.EqualTo(new float[]{7,9}));

                gate.SetData(new uint[]{1});
                commands.Clear();
                velocity.RecordDerivativeMasked(commands,gate);
                Graphics.ExecuteCommandBuffer(commands);
                velocity.Derivative.GetData(result);
                Assert.That(result[0],Is.EqualTo(1).Within(1e-5));
                Assert.That(result[1],Is.Zero.Within(1e-5));

                var beforeFacts=new uint[8];velocity.Facts.GetData(beforeFacts);
                gate.SetData(new uint[]{0});
                commands.Clear();
                velocity.RecordLineSearch(commands,gate);
                Graphics.ExecuteCommandBuffer(commands);
                var selection=new uint[2];var input=new float[2];
                var afterFacts=new uint[8];var free=new Vector4[2];
                velocity.TrialSelection.GetData(selection);
                velocity.ImpulseInput.GetData(input);
                velocity.Facts.GetData(afterFacts);
                velocity.FreeMotionInput.GetData(free);
                Assert.That(selection[0],Is.EqualTo(uint.MaxValue));
                Assert.That(input,Is.EqualTo(new float[]{1,0}),
                    "A masked search must not apply a stale trial");
                Assert.That(afterFacts,Is.EqualTo(beforeFacts));
                Assert.That(free[0].x,Is.EqualTo(1).Within(1e-6));
            }
        }

        [Test] public void ClosingNormalProducesPhysicalResidualAndActiveDerivative()
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
            using(var velocity=new CoupledVelocityResidual(op,1,.3f))
            using(var commands=new CommandBuffer())
            {
                velocity.ImpulseInput.SetData(new float[]{0,0});
                velocity.FreeMotionInput.SetData(new[]{
                    new Vector4(1,0,0,0),Vector4.zero});
                velocity.RecordGraph(commands);
                velocity.RecordResidual(commands);
                Graphics.ExecuteCommandBuffer(commands);
                var residual=new float[2];var facts=new uint[8];
                velocity.Residual.GetData(residual);
                velocity.Facts.GetData(facts);
                var merit=new float[1];velocity.Merit.GetData(merit);
                Assert.That(residual[0],Is.EqualTo(-inverseRoot).Within(1e-5));
                Assert.That(residual[1],Is.Zero.Within(1e-5));
                Assert.That(BitConverter.ToSingle(BitConverter.GetBytes(facts[0]),0),
                    Is.EqualTo(1).Within(1e-5));
                Assert.That(facts[2],Is.EqualTo(uint.MaxValue));
                Assert.That(facts[3],Is.Zero);
                Assert.That(merit[0],Is.EqualTo(.25f).Within(1e-5));
                Assert.That(BitConverter.ToSingle(BitConverter.GetBytes(facts[5]),0),
                    Is.EqualTo(1).Within(1e-5));

                commands.Clear();
                velocity.RecordProjectionIfPhysical(commands);
                velocity.RecordProjectedResidualIfPhysical(commands);
                Graphics.ExecuteCommandBuffer(commands);
                var projectionGate=new uint[1];
                var projectionStatus=new uint[1];
                velocity.ProjectionGate.GetData(projectionGate);
                velocity.ProjectionStatus.GetData(projectionStatus);
                Assert.That(projectionGate[0],Is.Zero);
                Assert.That(projectionStatus[0],Is.Zero,
                    "An unconverged velocity state cannot enter projection");

                commands.Clear();
                velocity.ImpulseInput.SetData(new float[]{1,0});
                velocity.FreeMotionInput.SetData(new[]{Vector4.zero,Vector4.zero});
                velocity.RecordDerivative(commands);
                Graphics.ExecuteCommandBuffer(commands);
                var derivative=new float[2];
                velocity.Derivative.GetData(derivative);
                Assert.That(derivative[0],Is.EqualTo(1).Within(1e-5));
                Assert.That(derivative[1],Is.Zero.Within(1e-5));

                commands.Clear();
                velocity.RecordLinearVerification(commands);
                Graphics.ExecuteCommandBuffer(commands);
                var linear=new float[4];
                velocity.LinearResult.GetData(linear);
                Assert.That(linear[1],Is.EqualTo((1-inverseRoot)/inverseRoot)
                    .Within(1e-5));
                Assert.That(linear[2],Is.EqualTo(1));
                Assert.That(linear[3],Is.Zero,
                    "A direction above relative .1 must not qualify as solved");

                commands.Clear();
                velocity.RecordTrial(commands,1);
                Graphics.ExecuteCommandBuffer(commands);
                velocity.Residual.GetData(residual);
                velocity.Merit.GetData(merit);
                Assert.That(residual[0],Is.EqualTo(-inverseRoot+.5f).Within(1e-5));
                Assert.That(merit[0],
                    Is.EqualTo(.5f*Mathf.Pow(inverseRoot-.5f,2)).Within(1e-5));
                var directionInput=new float[2];
                velocity.ImpulseInput.GetData(directionInput);
                Assert.That(directionInput,Is.EqualTo(new float[]{1,0}),
                    "A rejected trial must not change the operator input");

                commands.Clear();
                velocity.RecordLineSearch(commands);
                Graphics.ExecuteCommandBuffer(commands);
                var selection=new uint[2];
                velocity.TrialSelection.GetData(selection);
                Assert.That(selection,Is.EqualTo(new uint[]{0,1}),
                    "The first finite Armijo step should win");
                velocity.ImpulseInput.GetData(directionInput);
                Assert.That(directionInput,Is.EqualTo(new float[]{1,0}));
                velocity.Residual.GetData(residual);
                Assert.That(residual[0],Is.EqualTo(1-inverseRoot).Within(1e-5),
                    "The selected impulse needs a true free-motion recheck");

                commands.Clear();
                velocity.ImpulseInput.SetData(new float[]{0,0});
                velocity.FreeMotionInput.SetData(new[]{
                    new Vector4(1,0,0,0),Vector4.zero});
                velocity.RecordResidual(commands);
                Graphics.ExecuteCommandBuffer(commands);
                commands.Clear();
                velocity.ImpulseInput.SetData(new float[]{-1,0});
                velocity.RecordDerivative(commands);
                Graphics.ExecuteCommandBuffer(commands);
                commands.Clear();
                velocity.RecordLineSearch(commands);
                Graphics.ExecuteCommandBuffer(commands);
                velocity.TrialSelection.GetData(selection);
                Assert.That(selection[0],Is.EqualTo(uint.MaxValue),
                    "A worsening direction must have no accepted step");
                velocity.ImpulseInput.GetData(directionInput);
                Assert.That(directionInput,Is.EqualTo(new float[]{0,0}),
                    "A failed search must restore the base impulse");

                commands.Clear();
                velocity.ImpulseInput.SetData(new float[]{-.1f,2});
                velocity.RecordResidual(commands);
                velocity.RecordProjection(commands);
                velocity.RecordProjectedResidual(commands);
                Graphics.ExecuteCommandBuffer(commands);
                var projected=new float[2];
                velocity.ProjectedImpulse.GetData(projected);
                velocity.Residual.GetData(residual);
                velocity.Merit.GetData(merit);
                Assert.That(projected,Is.EqualTo(new float[]{0,0}));
                Assert.That(residual[0],Is.EqualTo(-inverseRoot).Within(1e-5),
                    "Clamping a negative impulse to zero cannot cancel closing free motion");
                Assert.That(residual[1],Is.Zero.Within(1e-5));
                Assert.That(merit[0],Is.EqualTo(.25f).Within(1e-5));

                commands.Clear();
                velocity.RecordProjectionIfPhysical(commands);
                velocity.RecordProjectedResidualIfPhysical(commands);
                Graphics.ExecuteCommandBuffer(commands);
                velocity.ProjectionGate.GetData(projectionGate);
                velocity.ProjectionStatus.GetData(projectionStatus);
                Assert.That(projectionGate[0],Is.Zero);
                Assert.That(projectionStatus[0],Is.Zero,
                    "A projected impulse cannot publish a still-closing contact");

                commands.Clear();
                velocity.FreeMotionInput.SetData(new[]{Vector4.zero,Vector4.zero});
                velocity.ImpulseInput.SetData(new float[]{0,0});
                velocity.RecordResidual(commands);
                velocity.RecordProjectionIfPhysical(commands);
                velocity.RecordProjectedResidualIfPhysical(commands);
                Graphics.ExecuteCommandBuffer(commands);
                velocity.ProjectionGate.GetData(projectionGate);
                velocity.ProjectionStatus.GetData(projectionStatus);
                Assert.That(projectionGate[0],Is.EqualTo(1u));
                Assert.That(projectionStatus[0],Is.EqualTo(2u),
                    "Only the true projected residual may qualify publication");
            }
        }
    }
}
