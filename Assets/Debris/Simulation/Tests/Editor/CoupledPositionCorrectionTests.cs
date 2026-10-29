using Debris.Simulation.CoupledContacts;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace Debris.Simulation.Tests
{
    public sealed class CoupledPositionCorrectionTests
    {
        static CoupledContactOperator Pair(float center)
        {
            return new CoupledContactOperator(new[]{
                new CoupledContactBody{InverseMass=1,InverseInertia=1},
                new CoupledContactBody{Center=Vector2.right*center}
            },new[]{
                new CoupledContactPrimitive{Half=Vector2.one*.5f,ContactClass=1},
                new CoupledContactPrimitive{Center=Vector2.right*center,Half=Vector2.one*.5f,
                    Endpoint=1,Anchored=1,ContactClass=2}
            },Vector2.one*-4,1,8,8,normalOnly:true);
        }

        [TestCase(CoupledSolveProfile.C1,2)]
        [TestCase(CoupledSolveProfile.C2,3)]
        [TestCase(CoupledSolveProfile.C3,4)]
        public void ProfileStopsAfterFirstTargetAndIndependentlyAccepts(CoupledSolveProfile profile,int cap)
        {
            Assume.That(SystemInfo.supportsComputeShaders,Is.True);
            using(var op=Pair(.95f))
            using(var correction=new CoupledPositionCorrection(op))
            using(var commands=new CommandBuffer())
            {
                correction.Record(commands,profile);Graphics.ExecuteCommandBuffer(commands);
                var control=new uint[12];var decision=new uint[8];
                correction.Control.GetData(control);correction.Decision.GetData(decision);
                Assert.That(control[0],Is.Zero);
                Assert.That(control[2],Is.EqualTo(1u),"Later geometry slots are masked after cleanup");
                Assert.That(control[4],Is.EqualTo((uint)cap));
                Assert.That(decision[0],Is.EqualTo(1u));
                Assert.That(decision[1],Is.Zero);
                Assert.That(decision[2],Is.EqualTo(1u));
                Assert.That(decision[7],Is.Zero);
                var bodies=new CoupledContactBody[2];correction.WorkingBodies.GetData(bodies);
                Assert.That(bodies[0].Center.x,Is.EqualTo(-.0499f).Within(2e-5));
                Assert.That(bodies[1].Center.x,Is.EqualTo(.95f).Within(1e-6));
            }
        }

        [Test] public void RejectedStageRestoresPredictedBodiesPrimitivesAndGraph()
        {
            Assume.That(SystemInfo.supportsComputeShaders,Is.True);
            using(var op=Pair(.5f))
            using(var correction=new CoupledPositionCorrection(op))
            using(var commands=new CommandBuffer())
            {
                correction.Record(commands,CoupledSolveProfile.C1);Graphics.ExecuteCommandBuffer(commands);
                var control=new uint[12];var decision=new uint[8];
                correction.Control.GetData(control);correction.Decision.GetData(decision);
                Assert.That(decision[0],Is.Zero);
                Assert.That(decision[1],Is.EqualTo(3u));
                Assert.That(control[8],Is.EqualTo(3u),"Correction exceeds the .25-cell envelope");
                var working=new CoupledContactBody[2];var graph=new CoupledContactBody[2];
                correction.WorkingBodies.GetData(working);op.BodiesBuffer.GetData(graph);
                Assert.That(working[0].Center,Is.EqualTo(Vector2.zero));
                Assert.That(graph[0].Center,Is.EqualTo(Vector2.zero));
                var primitives=new CoupledContactPrimitive[2];correction.WorkingPrimitives.GetData(primitives);
                Assert.That(primitives[0].Center,Is.EqualTo(Vector2.zero));
                Assert.That(primitives[0].ContactClass,Is.EqualTo(1u));
            }
        }

        [Test] public void IsolatedUnclassifiedPrimitiveCannotBeAccepted()
        {
            Assume.That(SystemInfo.supportsComputeShaders,Is.True);
            using(var op=new CoupledContactOperator(new[]{new CoupledContactBody{InverseMass=1}},
                new[]{new CoupledContactPrimitive{Half=Vector2.one*.5f}},
                Vector2.one*-4,1,8,8,normalOnly:true))
            using(var correction=new CoupledPositionCorrection(op))
            using(var commands=new CommandBuffer())
            {
                correction.Record(commands,CoupledSolveProfile.C1);Graphics.ExecuteCommandBuffer(commands);
                var decision=new uint[8];correction.Decision.GetData(decision);
                Assert.That(decision[0],Is.Zero);
                Assert.That(decision[7]&8u,Is.EqualTo(8u),"Final independent classification must include isolated matter");
            }
        }

        [Test] public void InitiallyMaskedSolvePreservesFreeMotionMultipliersAndOutput()
        {
            Assume.That(SystemInfo.supportsComputeShaders,Is.True);
            using(var op=Pair(.95f))
            using(var solve=new CoupledPositionSolve(op))
            using(var gate=new GraphicsBuffer(GraphicsBuffer.Target.Structured,1,4))
            using(var commands=new CommandBuffer())
            {
                gate.SetData(new uint[]{0});
                var free=new[]{new Vector4(9,8,7,0),new Vector4(6,5,4,0)};
                var output=new[]{new Vector4(3,2,1,0),Vector4.one};
                op.FreeMotionInput.SetData(free);op.EndpointOutput.SetData(output);
                solve.Multipliers.SetData(new float[]{100,200});
                op.RecordGraph(commands);solve.Record(commands,CoupledSolveProfile.C1,gate);
                Graphics.ExecuteCommandBuffer(commands);
                var control=new uint[8];solve.Control.GetData(control);
                Assert.That(control[0],Is.Zero);Assert.That(control[1],Is.Zero);
                Assert.That(control[2],Is.Zero);Assert.That(control[5],Is.Zero);
                var actual=new Vector4[2];op.FreeMotionInput.GetData(actual);
                Assert.That(actual,Is.EqualTo(free));
                solve.Displacement.GetData(actual);Assert.That(actual,Is.EqualTo(output));
                var multipliers=new float[2];solve.Multipliers.GetData(multipliers,0,0,2);
                Assert.That(multipliers,Is.EqualTo(new float[]{100,200}));
            }
        }

        [Test] public void FinalHardPassKeepsTargetNotReachedDistinct()
        {
            Assume.That(SystemInfo.supportsComputeShaders,Is.True);
            var shader=Object.Instantiate(Resources.Load<ComputeShader>("CoupledPositionCorrection"));
            try
            {
                using(var control=new GraphicsBuffer(GraphicsBuffer.Target.Structured,12,4))
                using(var decision=new GraphicsBuffer(GraphicsBuffer.Target.Structured,8,4))
                using(var final=new GraphicsBuffer(GraphicsBuffer.Target.Structured,8,4))
                using(var commands=new CommandBuffer())
                {
                    control.SetData(new uint[]{0,0,2,0,2,0,1,8,0,0,0,1});
                    final.SetData(new uint[]{1,0,uint.MaxValue,uint.MaxValue,0,0,0,0});
                    int kernel=shader.FindKernel("FinishCorrection");
                    shader.SetBuffer(kernel,"_Control",control);shader.SetBuffer(kernel,"_Decision",decision);
                    shader.SetBuffer(kernel,"_FinalDecision",final);
                    commands.DispatchCompute(shader,kernel,1,1,1);Graphics.ExecuteCommandBuffer(commands);
                    var result=new uint[8];decision.GetData(result);
                    Assert.That(result[0],Is.EqualTo(1u));
                    Assert.That(result[2],Is.Zero,"A hard-bound pass at the cap is not target cleanup");
                    Assert.That(result[3],Is.EqualTo(2u));
                }
            }
            finally { Object.DestroyImmediate(shader); }
        }
    }
}
