using System;
using Debris.Simulation.CoupledContacts;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace Debris.Simulation.Tests
{
    public sealed class CoupledPositionSolveTests
    {
        static CoupledContactPrimitive Primitive(Vector2 center,uint endpoint,
            uint contactClass,bool anchored=false)=>new CoupledContactPrimitive
            {
                Center=center,Half=Vector2.one*.5f,Endpoint=endpoint,
                ContactClass=contactClass,Anchored=anchored?1u:0u
            };

        [TestCase(1u,false,CoupledSolveProfile.C1)]
        [TestCase(2u,false,CoupledSolveProfile.C2)]
        [TestCase(2u,true,CoupledSolveProfile.C3)]
        public void OverlapCorrectionUsesMassAndClassSlop(uint secondClass,
            bool anchored,CoupledSolveProfile profile)
        {
            Assume.That(SystemInfo.supportsComputeShaders,Is.True);
            float inverseB=anchored?0:.25f;
            var bodies=new[]{
                new CoupledContactBody{Center=Vector2.zero,InverseMass=1},
                new CoupledContactBody{Center=Vector2.right*.95f,InverseMass=inverseB}
            };
            var primitives=new[]{Primitive(bodies[0].Center,0,1),
                Primitive(bodies[1].Center,1,secondClass,anchored)};
            using(var op=new CoupledContactOperator(bodies,primitives,
                Vector2.one*-4,1,8,8,normalOnly:true))
            using(var position=new CoupledPositionSolve(op))
            using(var commands=new CommandBuffer())
            {
                // Poison both scratch inputs. A fresh geometry must ignore
                // old impulses and free velocities on every call.
                position.Multipliers.SetData(new float[]{100,200});
                op.FreeMotionInput.SetData(new[]{new Vector4(9,8,7,0),Vector4.one});
                op.RecordGraph(commands);
                position.Record(commands,profile);
                Graphics.ExecuteCommandBuffer(commands);
                var control=new uint[8];position.Control.GetData(control);
                Assert.That(control[1],Is.EqualTo(1u));
                Assert.That(control[3],Is.Zero);
                Assert.That(control[4],Is.EqualTo(4u),
                    "Position always allows four Newton updates, including C2/C3");
                var motion=new Vector4[2];position.Displacement.GetData(motion);
                float correction=.05f-(secondClass==1?.002f:.0001f);
                Assert.That(motion[0].x,Is.EqualTo(-correction/(1+inverseB)).Within(2e-5));
                Assert.That(motion[1].x,Is.EqualTo(correction*inverseB/(1+inverseB)).Within(2e-5));
                Assert.That(motion[0].y,Is.Zero.Within(1e-6));
                Assert.That(motion[0].z,Is.Zero.Within(1e-6));
                if(!anchored)
                    Assert.That(motion[0].x+4*motion[1].x,Is.Zero.Within(1e-6),
                        "Finite endpoints preserve the center of mass");
                else Assert.That(motion[1],Is.EqualTo(Vector4.zero));

                commands.Clear();
                position.Record(commands,profile);
                Graphics.ExecuteCommandBuffer(commands);
                var repeated=new Vector4[2];position.Displacement.GetData(repeated);
                Assert.That(repeated[0].x,Is.EqualTo(motion[0].x).Within(1e-6),
                    "Reusing the same geometry starts p at zero rather than accumulating correction");
            }
        }

        [Test] public void SingleCornerRowCanAcceptZeroDisplacement()
        {
            Assume.That(SystemInfo.supportsComputeShaders,Is.True);
            var bodies=new[]{
                new CoupledContactBody{Center=Vector2.zero,InverseMass=1},
                new CoupledContactBody{Center=Vector2.one,InverseMass=1}
            };
            using(var op=new CoupledContactOperator(bodies,
                new[]{Primitive(Vector2.zero,0,1),Primitive(Vector2.one,1,1)},
                Vector2.one*-4,1,8,8,normalOnly:true))
            using(var position=new CoupledPositionSolve(op))
            using(var commands=new CommandBuffer())
            {
                op.RecordGraph(commands);
                position.Record(commands,CoupledSolveProfile.C1);
                Graphics.ExecuteCommandBuffer(commands);
                var count=new uint[1];op.ActiveRowCounter.GetData(count);
                Assert.That(count[0],Is.EqualTo(1u),"A normal-only corner has an odd row count");
                var control=new uint[8];position.Control.GetData(control);
                Assert.That(control[1],Is.EqualTo(1u));
                Assert.That(control[5],Is.Zero,"An already satisfied system needs no Krylov products");
                var motion=new Vector4[2];position.Displacement.GetData(motion);
                Assert.That(motion,Is.EqualTo(new[]{Vector4.zero,Vector4.zero}));
            }
        }

        [Test] public void UnclassifiedContactCannotProduceAcceptedCorrection()
        {
            Assume.That(SystemInfo.supportsComputeShaders,Is.True);
            var bodies=new[]{
                new CoupledContactBody{Center=Vector2.zero,InverseMass=1},
                new CoupledContactBody{Center=Vector2.right*.95f,InverseMass=1}
            };
            using(var op=new CoupledContactOperator(bodies,
                new[]{Primitive(bodies[0].Center,0,0),Primitive(bodies[1].Center,1,1)},
                Vector2.one*-4,1,8,8,normalOnly:true))
            using(var position=new CoupledPositionSolve(op))
            using(var commands=new CommandBuffer())
            {
                op.RecordGraph(commands);
                position.Record(commands,CoupledSolveProfile.C1);
                Graphics.ExecuteCommandBuffer(commands);
                var control=new uint[8];position.Control.GetData(control);
                Assert.That(control[1],Is.Zero);
                Assert.That(control[3],Is.EqualTo(4u));
            }
        }

        [Test] public void VelocityGraphIsRejectedBeforePositionAllocation()
        {
            Assume.That(SystemInfo.supportsComputeShaders,Is.True);
            using(var op=new CoupledContactOperator(
                new[]{new CoupledContactBody{InverseMass=1}},
                Array.Empty<CoupledContactRow>()))
            {
                long prior=op.Arena.UsedBytes;
                Assert.Throws<ArgumentException>(()=>new CoupledPositionSolve(op));
                Assert.That(op.Arena.UsedBytes,Is.EqualTo(prior));
            }
        }
    }
}
