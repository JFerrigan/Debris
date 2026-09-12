using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Debris.Simulation.Tests
{
    // GPU probe cases are checked against analytic OBB fixtures. The probe
    // includes the production-intended pure helper without touching the
    // unfinished parallel solver.
    public sealed class ParallelGeometryTests
    {
        [StructLayout(LayoutKind.Sequential)]
        struct Pair
        {
            public Vector2 ACenter, AHalfSize;
            public float AAngle;
            public Vector2 BCenter, BHalfSize;
            public float BAngle;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct Output
        {
            public uint Valid, Count;
            public Vector2 Normal0;
            public float Separation0;
            public Vector2 Point0;
            public Vector2 Normal1;
            public float Separation1;
            public Vector2 Point1;
        }

        [Test]
        public void ProbeLayoutsMatchHlsl()
        {
            Assert.That(Marshal.SizeOf<Pair>(), Is.EqualTo(40));
            Assert.That(Marshal.SizeOf<Output>(), Is.EqualTo(48));
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator RotatedFaceContactMatchesAnalyticSupportPatch()
        {
            const float angle = .37f;
            var x = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            var y = new Vector2(-x.y, x.x);
            var pair = new Pair
            {
                ACenter = Vector2.zero,
                AHalfSize = Vector2.one * .5f,
                AAngle = angle,
                BCenter = x * .75f + y * .1f,
                BHalfSize = Vector2.one * .5f,
                BAngle = angle
            };
            var output = Probe(pair);
            Assert.That(output.Valid, Is.EqualTo(1u));
            Assert.That(output.Count, Is.EqualTo(1u));
            Assert.That(output.Normal0, Is.EqualTo(x).Using(Vector2Comparer(.0001f)));
            Assert.That(output.Separation0, Is.EqualTo(-.25f).Within(.0001f));
            Assert.That(output.Point0, Is.EqualTo(x * .375f + y * .05f).Using(Vector2Comparer(.0002f)));
            yield return null;
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator ShallowCornerKeepsBothDistinctNormalsAndAnchors()
        {
            var output = Probe(new Pair
            {
                ACenter = Vector2.zero,
                AHalfSize = Vector2.one * .5f,
                BCenter = new Vector2(.99f, .99f),
                BHalfSize = Vector2.one * .5f
            });
            Assert.That(output.Valid, Is.EqualTo(1u));
            Assert.That(output.Count, Is.EqualTo(2u));
            Assert.That(output.Normal0, Is.EqualTo(Vector2.right).Using(Vector2Comparer(.0001f)));
            Assert.That(output.Normal1, Is.EqualTo(Vector2.up).Using(Vector2Comparer(.0001f)));
            Assert.That(output.Separation0, Is.EqualTo(-.01f).Within(.0001f));
            Assert.That(output.Separation1, Is.EqualTo(-.01f).Within(.0001f));
            Assert.That(output.Point0, Is.EqualTo(new Vector2(.495f, .495f)).Using(Vector2Comparer(.0002f)));
            Assert.That(output.Point1, Is.EqualTo(new Vector2(.495f, .495f)).Using(Vector2Comparer(.0002f)));
            yield return null;
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator NotchedCornerPatchesKeepIndependentFaceNormals()
        {
            var pairs = new[]
            {
                new Pair
                {
                    ACenter = Vector2.zero, AHalfSize = Vector2.one * .5f,
                    BCenter = new Vector2(.57f, 0), BHalfSize = new Vector2(.1f, 2)
                },
                new Pair
                {
                    ACenter = Vector2.zero, AHalfSize = Vector2.one * .5f,
                    BCenter = new Vector2(0, .57f), BHalfSize = new Vector2(2, .1f)
                }
            };
            var outputs = Probe(pairs);
            Assert.That(outputs[0].Valid, Is.EqualTo(1u));
            Assert.That(outputs[0].Count, Is.EqualTo(1u));
            Assert.That(outputs[0].Normal0, Is.EqualTo(Vector2.right).Using(Vector2Comparer(.0001f)));
            Assert.That(outputs[0].Separation0, Is.EqualTo(-.03f).Within(.0001f));
            Assert.That(outputs[0].Point0, Is.EqualTo(new Vector2(.485f, 0)).Using(Vector2Comparer(.0002f)));
            Assert.That(outputs[1].Valid, Is.EqualTo(1u));
            Assert.That(outputs[1].Count, Is.EqualTo(1u));
            Assert.That(outputs[1].Normal0, Is.EqualTo(Vector2.up).Using(Vector2Comparer(.0001f)));
            Assert.That(outputs[1].Separation0, Is.EqualTo(-.03f).Within(.0001f));
            Assert.That(outputs[1].Point0, Is.EqualTo(new Vector2(0, .485f)).Using(Vector2Comparer(.0002f)));
            yield return null;
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator NearlyParallelWinningAxisUsesBodyAxisForSeparation()
        {
            // The body axis is only .003 radians from the grain axis. A
            // normal-alignment reference test can therefore select the grain
            // face even though SAT selected the body axis. The separation and
            // normal must follow the winning body axis.
            const float angle = .003f;
            var bodyAxis = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            var bodyTangent = new Vector2(-bodyAxis.y, bodyAxis.x);
            var output = Probe(new Pair
            {
                ACenter = Vector2.zero,
                AHalfSize = Vector2.one * .5f,
                BCenter = bodyAxis * .75f + bodyTangent * .49f,
                BHalfSize = Vector2.one * .5f,
                BAngle = angle
            });
            Assert.That(output.Valid, Is.EqualTo(1u));
            Assert.That(output.Count, Is.EqualTo(1u));
            Assert.That(output.Normal0, Is.EqualTo(bodyAxis).Using(Vector2Comparer(.0001f)));
            Assert.That(output.Separation0, Is.EqualTo(.75f - .5f - .5f * (Mathf.Abs(bodyAxis.x) + Mathf.Abs(bodyAxis.y))).Within(.0001f));
            yield return null;
        }

        static IEqualityComparer<Vector2> Vector2Comparer(float tolerance)
        {
            return new Vector2EqualityComparer(tolerance);
        }

        sealed class Vector2EqualityComparer : IEqualityComparer<Vector2>
        {
            readonly float tolerance;
            public Vector2EqualityComparer(float tolerance) { this.tolerance = tolerance; }
            public bool Equals(Vector2 x, Vector2 y) => Vector2.Distance(x, y) <= tolerance;
            public int GetHashCode(Vector2 obj) => 0;
        }

        static Output Probe(Pair pair)
        {
            var values = Probe(new[] { pair });
            return values[0];
        }

        static Output[] Probe(Pair[] pairs)
        {
            var shader = Resources.Load<ComputeShader>("ParallelGeometryProbe");
            Assert.That(shader, Is.Not.Null, "ParallelGeometryProbe compute shader was not imported");
            var kernel = shader.FindKernel("Probe");
            using (var input = new ComputeBuffer(pairs.Length, Marshal.SizeOf<Pair>(), ComputeBufferType.Structured))
            using (var output = new ComputeBuffer(pairs.Length, Marshal.SizeOf<Output>(), ComputeBufferType.Structured))
            {
                input.SetData(pairs);
                shader.SetInt("_Count", pairs.Length);
                shader.SetBuffer(kernel, "_Pairs", input);
                shader.SetBuffer(kernel, "_Outputs", output);
                shader.Dispatch(kernel, Mathf.Max(1, (pairs.Length + 63) / 64), 1, 1);
                var values = new Output[pairs.Length];
                output.GetData(values);
                return values;
            }
        }
    }
}
