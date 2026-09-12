using System;
using System.Collections;
using System.Runtime.InteropServices;
using Debris.Simulation.ParallelProof;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Grain = Debris.Simulation.ParallelProof.LooseCell;

namespace Debris.Simulation.Tests
{
    public sealed class ParallelMetricsTests
    {
        [Test]
        public void MetricsReadbackMatchesGpuResultLayout()
        {
            Assert.That(Marshal.SizeOf<ProofMetricsReadback>(), Is.EqualTo(80));
            Assert.That(Marshal.OffsetOf<ProofMetricsReadback>(nameof(ProofMetricsReadback.MaximumMomentumError)).ToInt32(), Is.EqualTo(16));
            Assert.That(Marshal.OffsetOf<ProofMetricsReadback>(nameof(ProofMetricsReadback.GrainCount)).ToInt32(), Is.EqualTo(32));
            Assert.That(Marshal.OffsetOf<ProofMetricsReadback>(nameof(ProofMetricsReadback.InitialMomentumScale)).ToInt32(), Is.EqualTo(72));
        }

        [Test]
        public void DiagnosticWordsHaveNamedMeaning()
        {
            var words = new uint[ProofDiagnostics.WordCount];
            words[0] = (uint)SolverFault.Envelope;
            words[1] = 12;
            words[2] = 8;
            words[3] = unchecked((uint)BitConverter.SingleToInt32Bits(3.5f));
            words[6] = 4;
            words[7] = 9;
            words[8] = unchecked((uint)BitConverter.SingleToInt32Bits(.01f));
            words[9] = unchecked((uint)BitConverter.SingleToInt32Bits(.001f));
            words[10] = 2;
            words[11] = 1;
            words[13] = 7;
            words[14] = 1;
            words[15] = unchecked((uint)BitConverter.SingleToInt32Bits(.0005f));

            var facts = ProofDiagnostics.From(words);
            Assert.That(facts.Fault, Is.EqualTo(SolverFault.Envelope));
            Assert.That(facts.CompletedTick, Is.EqualTo(12));
            Assert.That(facts.Substeps, Is.EqualTo(8));
            Assert.That(facts.MaximumSpeed, Is.EqualTo(3.5f));
            Assert.That(facts.MaximumContactsPerGrain, Is.EqualTo(4));
            Assert.That(facts.MaximumIncidentContactsPerGrain, Is.EqualTo(9));
            Assert.That(facts.MaximumGrainPenetration, Is.EqualTo(.01f));
            Assert.That(facts.MaximumSolidPenetration, Is.EqualTo(.001f));
            Assert.That(facts.EnvelopeViolations, Is.EqualTo(2));
            Assert.That(facts.RejectedTicks, Is.EqualTo(1));
            Assert.That(facts.RigidManifoldCount, Is.EqualTo(7));
            Assert.That(facts.MaximumRigidPointsPerPair, Is.EqualTo(1));
            Assert.That(facts.MaximumRigidPenetration, Is.EqualTo(.0005f));
        }

        [Test]
        public void DiagnosticWordsRequireFullFixedRecord()
        {
            Assert.Throws<ArgumentException>(() => ProofDiagnostics.From(new uint[15]));
            Assert.Throws<ArgumentNullException>(() => ProofDiagnostics.From(null));
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator MixedGrainAndBodyReducesMomentumSpinAndEnergy()
        {
            RequireGpu();
            var grains = new[]
            {
                new Grain
                {
                    Center = new Vector2(1, 0), Velocity = new Vector2(2, 0),
                    AngularVelocity = 1, Material = 1, Identity = 42
                }
            };
            var states = new[]
            {
                new BodyState { Center = grains[0].Center, Velocity = grains[0].Velocity, AngularVelocity = grains[0].AngularVelocity },
                new BodyState { Center = new Vector2(0, 2), Velocity = new Vector2(-1, 0), AngularVelocity = -2 }
            };
            var parameters = new[]
            {
                new BodyParameters { InverseMass = 1, InverseInertia = 6, Mobility = 1 },
                new BodyParameters { InverseMass = .5f, InverseInertia = 1f / 3f, Mobility = 1 }
            };
            using (var state = Buffer(states.Length, 32))
            using (var parameter = Buffer(parameters.Length, 32))
            using (var grain = Buffer(grains.Length, 48))
            using (var commands = new CommandBuffer { name = "ParallelMetrics test" })
            using (var metrics = new ProofMetrics(1, 1))
            {
                state.SetData(states); parameter.SetData(parameters); grain.SetData(grains);
                metrics.Record(commands, state, parameter, grain);
                Graphics.ExecuteCommandBuffer(commands);
                var task = metrics.ReadAsync();
                while (!task.IsCompleted) yield return null;
                Assert.That(task.IsFaulted, Is.False, task.Exception == null ? "" : task.Exception.ToString());
                var result = task.Result;

                Assert.That(result.LinearMomentum.x, Is.EqualTo(0).Within(.0001));
                Assert.That(result.LinearMomentum.y, Is.EqualTo(0).Within(.0001));
                Assert.That(result.AngularMomentum, Is.EqualTo(-11f / 6f).Within(.0001));
                Assert.That(result.KineticEnergy, Is.EqualTo(109f / 12f).Within(.0001));
                Assert.That(result.GrainCount, Is.EqualTo(1));
                Assert.That(result.IdentitySum, Is.EqualTo(42));
                Assert.That(result.IdentityXor, Is.EqualTo(42));
                Assert.That(result.NonfiniteCount, Is.Zero);
                Assert.That(result.SampleCount, Is.EqualTo(1));
            }
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator BaselineNormalizesMomentumAndEnergyGainIgnoresDissipation()
        {
            RequireGpu();
            var grains = new[]
            {
                new Grain { Center = new Vector2(-1, 0), Velocity = new Vector2(100, 0), Material = 1, Identity = 1 },
                new Grain { Center = new Vector2(1, 0), Velocity = new Vector2(-100, 0), Material = 1, Identity = 2 }
            };
            var states = new[]
            {
                new BodyState { Center = grains[0].Center, Velocity = grains[0].Velocity },
                new BodyState { Center = grains[1].Center, Velocity = grains[1].Velocity }
            };
            var parameters = new[]
            {
                new BodyParameters { InverseMass = 1, InverseInertia = 6, Mobility = 1 },
                new BodyParameters { InverseMass = 1, InverseInertia = 6, Mobility = 1 }
            };
            using (var state = Buffer(states.Length, 32))
            using (var parameter = Buffer(parameters.Length, 32))
            using (var grain = Buffer(grains.Length, 48))
            using (var commands = new CommandBuffer { name = "ParallelMetrics test" })
            using (var metrics = new ProofMetrics(2, 0))
            {
                parameter.SetData(parameters); grain.SetData(grains);
                state.SetData(states);
                metrics.Record(commands, state, parameter, grain);
                Graphics.ExecuteCommandBuffer(commands);
                var first = metrics.ReadAsync();
                while (!first.IsCompleted) yield return null;
                Assert.That(first.IsFaulted, Is.False, first.Exception == null ? "" : first.Exception.ToString());
                Assert.That(first.Result.InitialMomentumScale, Is.EqualTo(200).Within(.01));

                states[0].Velocity = new Vector2(101, 0);
                states[1].Velocity = new Vector2(-99, 0);
                state.SetData(states);
                commands.Clear();
                metrics.Record(commands, state, parameter, grain);
                Graphics.ExecuteCommandBuffer(commands);
                var second = metrics.ReadAsync();
                while (!second.IsCompleted) yield return null;
                Assert.That(second.IsFaulted, Is.False, second.Exception == null ? "" : second.Exception.ToString());
                Assert.That(second.Result.MaximumMomentumErrorMagnitude, Is.EqualTo(.01f).Within(.0001));
                Assert.That(second.Result.MaximumEnergyGain, Is.EqualTo(1).Within(.001));

                states[0].Velocity = new Vector2(1, 0);
                states[1].Velocity = new Vector2(-1, 0);
                state.SetData(states);
                commands.Clear();
                metrics.Record(commands, state, parameter, grain);
                Graphics.ExecuteCommandBuffer(commands);
                var third = metrics.ReadAsync();
                while (!third.IsCompleted) yield return null;
                Assert.That(third.IsFaulted, Is.False, third.Exception == null ? "" : third.Exception.ToString());
                Assert.That(third.Result.MaximumEnergyGain, Is.EqualTo(1).Within(.001));
                Assert.That(third.Result.KineticEnergy, Is.EqualTo(1).Within(.0001));
                Assert.That(third.Result.SampleCount, Is.EqualTo(3));
            }
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator NonfiniteStateIsReportedByCompactReadback()
        {
            RequireGpu();
            var grains = new[]
            {
                new Grain { Center = Vector2.zero, Velocity = new Vector2(float.NaN, 0), Material = 1, Identity = 9 }
            };
            var states = new[] { new BodyState { Center = Vector2.zero, Velocity = new Vector2(float.NaN, 0) } };
            var parameters = new[] { new BodyParameters { InverseMass = 1, InverseInertia = 6, Mobility = 1 } };
            using (var state = Buffer(1, 32))
            using (var parameter = Buffer(1, 32))
            using (var grain = Buffer(1, 48))
            using (var commands = new CommandBuffer { name = "ParallelMetrics test" })
            using (var metrics = new ProofMetrics(1, 0))
            {
                state.SetData(states); parameter.SetData(parameters); grain.SetData(grains);
                metrics.Record(commands, state, parameter, grain);
                Graphics.ExecuteCommandBuffer(commands);
                var task = metrics.ReadAsync();
                while (!task.IsCompleted) yield return null;
                Assert.That(task.IsFaulted, Is.False, task.Exception == null ? "" : task.Exception.ToString());
                Assert.That(task.Result.NonfiniteCount, Is.EqualTo(1));
                Assert.That(task.Result.MaximumNonfiniteCount, Is.EqualTo(1));
                Assert.That(task.Result.GrainCount, Is.EqualTo(1));
            }
        }

        static GraphicsBuffer Buffer(int count, int stride) => new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, stride);

        static void RequireGpu()
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback)
                Assert.Ignore("GPU metrics tests require compute shaders and async GPU readback");
        }
    }
}
