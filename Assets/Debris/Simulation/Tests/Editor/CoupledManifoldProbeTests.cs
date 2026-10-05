using System.Runtime.InteropServices;
using NUnit.Framework;
using UnityEngine;

namespace Debris.Simulation.Tests
{
    public sealed class CoupledManifoldProbeTests
    {
        [StructLayout(LayoutKind.Sequential,Pack=4,Size=48)]
        struct Pair
        {
            public Vector2 ACenter,AHalf;
            public float AAngle;
            public uint AIdentity;
            public Vector2 BCenter,BHalf;
            public float BAngle;
            public uint BIdentity;
        }
        [StructLayout(LayoutKind.Sequential,Pack=4,Size=40)]
        struct Manifold
        {
            public uint Count,ReferenceFace;
            public Vector2 Normal,Point0,Point1;
            public float Gap0,Gap1;
        }
        [Test] public void GpuManifoldRetainsTwoFlatPointsAndOneDiagonalCorner()
        {
            Assert.That(Marshal.SizeOf<Pair>(),Is.EqualTo(48));
            Assert.That(Marshal.SizeOf<Manifold>(),Is.EqualTo(40));
            Assume.That(SystemInfo.supportsComputeShaders,Is.True);
            var asset=Resources.Load<ComputeShader>("CoupledManifoldProbe");
            Assert.That(asset,Is.Not.Null);
            var shader=Object.Instantiate(asset);
            var pairs=new[]
            {
                new Pair{ACenter=Vector2.zero,AHalf=Vector2.one*.5f,
                    BCenter=Vector2.right,BHalf=Vector2.one*.5f},
                new Pair{ACenter=Vector2.zero,AHalf=Vector2.one*.5f,
                    BCenter=Vector2.one,BHalf=Vector2.one*.5f}
            };
            var input=new GraphicsBuffer(GraphicsBuffer.Target.Structured,pairs.Length,48);
            var output=new GraphicsBuffer(GraphicsBuffer.Target.Structured,pairs.Length,40);
            try
            {
                input.SetData(pairs);
                int kernel=shader.FindKernel("BuildManifolds");
                shader.SetBuffer(kernel,"_Pairs",input);shader.SetBuffer(kernel,"_Manifolds",output);
                shader.SetInt("_PairCount",pairs.Length);shader.SetFloat("_Margin",0);
                shader.Dispatch(kernel,1,1,1);
                var found=new Manifold[pairs.Length];output.GetData(found);
                Assert.That(found[0].Count,Is.EqualTo(2));
                Assert.That(found[0].Normal.x,Is.EqualTo(1).Within(1e-5));
                Assert.That(found[0].Gap0,Is.EqualTo(0).Within(1e-5));
                Assert.That(found[0].Gap1,Is.EqualTo(0).Within(1e-5));
                Assert.That(found[0].Point0.x,Is.EqualTo(.5).Within(1e-5));
                Assert.That(found[0].Point1.x,Is.EqualTo(.5).Within(1e-5));
                Assert.That(Mathf.Abs(found[0].Point0.y-found[0].Point1.y),Is.EqualTo(1).Within(1e-5));
                Assert.That(found[1].Count,Is.EqualTo(1));
                Assert.That(found[1].Point0.x,Is.EqualTo(.5).Within(1e-5));
                Assert.That(found[1].Point0.y,Is.EqualTo(.5).Within(1e-5));
            }
            finally
            {
                input.Release();output.Release();Object.DestroyImmediate(shader);
            }
        }
    }
}
