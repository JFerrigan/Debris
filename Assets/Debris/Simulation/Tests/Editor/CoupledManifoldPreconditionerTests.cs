using Debris.Simulation.CoupledContacts;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace Debris.Simulation.Tests
{
    public sealed class CoupledManifoldPreconditionerTests
    {
        [Test] public void OnePointBlockAppliesShiftedLocalDerivative()
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
            using(var input=new GraphicsBuffer(GraphicsBuffer.Target.Structured,2,4))
            using(var output=new GraphicsBuffer(GraphicsBuffer.Target.Structured,2,4))
            using(var commands=new CommandBuffer())
            {
                velocity.ImpulseInput.SetData(new float[]{0,0});
                velocity.FreeMotionInput.SetData(new[]{Vector4.zero,Vector4.zero});
                input.SetData(new float[]{2,4});
                velocity.RecordGraph(commands);
                velocity.RecordResidual(commands);
                preconditioner.RecordBuild(commands);
                preconditioner.RecordApply(commands,input,output);
                Graphics.ExecuteCommandBuffer(commands);
                var result=new float[2];var facts=new uint[2];
                output.GetData(result);
                preconditioner.Fault.GetData(facts);
                Assert.That(facts,Is.EqualTo(new uint[]{0,0}));
                Assert.That(result[0],Is.EqualTo(2/1.001f).Within(1e-5));
                Assert.That(result[1],Is.EqualTo(4/1.001f).Within(1e-5));
            }
        }
    }
}
