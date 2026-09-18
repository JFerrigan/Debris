using ProofGrain = Debris.Simulation.ParallelProof.LooseCell;
using System;
using System.Collections;
using System.IO;
using Debris.Simulation.ParallelProof;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Debris.Simulation.Tests
{
    public sealed class ParallelDiagnosticTests
    {
        static readonly PackedDiagnosticMotion[] Motions =
        {
            PackedDiagnosticMotion.Stationary,
            PackedDiagnosticMotion.TranslationOnly,
            PackedDiagnosticMotion.RotationOnly,
            PackedDiagnosticMotion.Combined
        };

        [TestCase(true)]
        [TestCase(false)]
        public void PackedDiagnosticVariantsPreserveCanonicalFixture(bool sharedMotion)
        {
            var canonical = ProofFixtures.Packed(sharedMotion);
            var original = Copy(canonical);

            foreach (var motion in Motions)
            {
                var diagnostic = ProofDiagnosticFixtures.Packed(sharedMotion, motion);
                AssertFixtureEqual(original, canonical, "canonical after " + motion);
                Assert.That(diagnostic.Grains.Length, Is.EqualTo(canonical.Grains.Length));
                Assert.That(diagnostic.Bodies.Length, Is.EqualTo(canonical.Bodies.Length));
                Assert.That(diagnostic.Parameters.Length, Is.EqualTo(canonical.Parameters.Length));
                Assert.That(diagnostic.Boundaries.Length, Is.EqualTo(canonical.Boundaries.Length));

                for (int i = 0; i < diagnostic.Parameters.Length; i++)
                    Assert.That(diagnostic.Parameters[i], Is.EqualTo(canonical.Parameters[i]), "parameters[" + i + "]");
                for (int i = 0; i < diagnostic.Boundaries.Length; i++)
                    Assert.That(diagnostic.Boundaries[i], Is.EqualTo(canonical.Boundaries[i]), "boundaries[" + i + "]");
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void PackedDiagnosticVariantsIsolateRequestedMotion(bool sharedMotion)
        {
            var canonical = ProofFixtures.Packed(sharedMotion);
            var translation = ProofDiagnosticFixtures.Packed(sharedMotion, PackedDiagnosticMotion.TranslationOnly);
            var rotation = ProofDiagnosticFixtures.Packed(sharedMotion, PackedDiagnosticMotion.RotationOnly);
            var stationary = ProofDiagnosticFixtures.Packed(sharedMotion, PackedDiagnosticMotion.Stationary);
            var combined = ProofDiagnosticFixtures.Packed(sharedMotion, PackedDiagnosticMotion.Combined);

            Assert.That(combined.Force, Is.EqualTo(canonical.Force));
            Assert.That(combined.Torque, Is.EqualTo(canonical.Torque));
            for (int i = 0; i < canonical.Grains.Length; i++)
            {
                var source = canonical.Grains[i];
                var linear = sharedMotion ? canonical.Bodies[0].Velocity : Vector2.zero;
                var angular = sharedMotion
                    ? new Vector2(-canonical.Bodies[0].AngularVelocity * (source.Center - canonical.Bodies[0].Center).y,
                        canonical.Bodies[0].AngularVelocity * (source.Center - canonical.Bodies[0].Center).x)
                    : Vector2.zero;
                Assert.That(stationary.Grains[i].Velocity, Is.EqualTo(Vector2.zero), "stationary velocity[" + i + "]");
                Assert.That(stationary.Grains[i].AngularVelocity, Is.EqualTo(0), "stationary spin[" + i + "]");
                Assert.That(translation.Grains[i].Velocity, Is.EqualTo(linear), "translation velocity[" + i + "]");
                Assert.That(translation.Grains[i].AngularVelocity, Is.EqualTo(0), "translation spin[" + i + "]");
                Assert.That(rotation.Grains[i].Velocity, Is.EqualTo(angular), "rotation velocity[" + i + "]");
                Assert.That(rotation.Grains[i].AngularVelocity, Is.EqualTo(sharedMotion ? source.AngularVelocity : 0), "rotation spin[" + i + "]");
            }

            if (sharedMotion)
            {
                Assert.That(stationary.Bodies[0].Velocity, Is.EqualTo(Vector2.zero));
                Assert.That(stationary.Bodies[0].AngularVelocity, Is.Zero);
                Assert.That(translation.Bodies[0].Velocity, Is.EqualTo(canonical.Bodies[0].Velocity));
                Assert.That(translation.Bodies[0].AngularVelocity, Is.Zero);
                Assert.That(rotation.Bodies[0].Velocity, Is.EqualTo(Vector2.zero));
                Assert.That(rotation.Bodies[0].AngularVelocity, Is.EqualTo(canonical.Bodies[0].AngularVelocity));
            }
            else
            {
                Assert.That(stationary.Force, Is.EqualTo(Vector2.zero));
                Assert.That(stationary.Torque, Is.Zero);
                Assert.That(translation.Force, Is.EqualTo(canonical.Force));
                Assert.That(translation.Torque, Is.Zero);
                Assert.That(rotation.Force, Is.EqualTo(Vector2.zero));
                Assert.That(rotation.Torque, Is.EqualTo(canonical.Torque));
            }
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator TraceDisabledAndEnabledMatchSuccessfulHeavyCase()
        {
            RequireGpu();
            var fixture = ProofFixtures.IsolatedHeavy();
            ProofSnapshot disabled = null;
            ProofSnapshot enabled = null;
            using (var solver = CreateSolver(fixture, 4, 2, false))
            {
                solver.Step();
                yield return ReadSnapshot(solver, value => disabled = value);
            }
            using (var solver = CreateSolver(fixture, 4, 2, true))
            {
                solver.Step();
                yield return ReadSnapshot(solver, value => enabled = value);
                var traceTask = solver.TraceAsync();
                while (!traceTask.IsCompleted) yield return null;
                if (traceTask.IsFaulted)
                    Assert.Fail(traceTask.Exception == null ? "proof trace readback failed" : traceTask.Exception.ToString());
                Assert.That(traceTask.Result.Tick, Is.EqualTo(1u));
                Assert.That(traceTask.Result.Checkpoints.Length, Is.GreaterThan(0));
            }
            AssertSnapshotsEquivalent(disabled, enabled, "successful heavy");
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator TraceDisabledAndEnabledMatchRejectedPackedCases()
        {
            RequireGpu();
            foreach (bool sharedMotion in new[] { true, false })
            foreach (var profile in Profiles())
            {
                var fixture = ProofDiagnosticFixtures.Packed(sharedMotion, PackedDiagnosticMotion.Combined);
                DiagnosticRun canonical = null;
                yield return RunUntilDecision(fixture, profile[0], profile[1], false, value => canonical = value);
                Assert.That(canonical.Previous, Is.Not.Null);
                // Use identical committed input for the attempted tick. Long
                // trajectories amplify nondeterministic adjacency reduction order.
                fixture.Grains = canonical.Previous.Grains;
                for (int body = 0; body < fixture.Bodies.Length; body++)
                    fixture.Bodies[body] = canonical.Previous.Endpoints[fixture.Grains.Length + body];
                DiagnosticRun disabled = null, enabled = null, repeated = null;
                yield return RunSingleAttempt(fixture, profile[0], profile[1], false, value => disabled = value);
                yield return RunSingleAttempt(fixture, profile[0], profile[1], true, value => enabled = value);
                yield return RunSingleAttempt(fixture, profile[0], profile[1], false, value => repeated = value);
                Assert.That(disabled, Is.Not.Null);
                Assert.That(enabled, Is.Not.Null);
                Assert.That(disabled.Snapshot.Fault, Is.Not.EqualTo(SolverFault.None), "the packed reproduction must reject within the bounded run");
                Assert.That(enabled.Snapshot.Fault, Is.EqualTo(disabled.Snapshot.Fault));
                Assert.That(enabled.Snapshot.CompletedTick, Is.EqualTo(disabled.Snapshot.CompletedTick));
                Assert.That(enabled.Trace, Is.Not.Null, "the traced rejection must include the attempted working state");
                Assert.That(enabled.Trace.Tick, Is.EqualTo((uint)enabled.AttemptedTicks));
                Assert.That(enabled.Trace.Checkpoints.Length, Is.GreaterThan(0));
                AssertRejectedTrace(enabled);
                string family = sharedMotion ? "shared" : "resting";
                string tracePath = Path.Combine("/private/tmp/b3r-diagnostic-tests",
                    string.Format("packed-{0}-{1}-{2}.json", family, profile[0], profile[1]));
                string archivePath = enabled.Trace.Export(tracePath);
                ParallelTraceReport.WriteSelectedWall(archivePath + ".wall.csv", enabled.Trace);
                var replay = ParallelContactReference.ReplayTrace(enabled.Trace);
                Debug.Log($"B3R_REPLAY family={family} profile={profile[0]}/{profile[1]} {replay}");
                Assert.That(replay.Valid, Is.True, replay.ToString());
                Assert.That(repeated.Snapshot.Fault, Is.EqualTo(disabled.Snapshot.Fault));
                Assert.That(repeated.Snapshot.CompletedTick, Is.EqualTo(disabled.Snapshot.CompletedTick));
                Debug.Log($"B3R_PARITY family={family} profile={profile[0]}/{profile[1]} canonicalAttempt={canonical.AttemptedTicks} untraced_repeat={StateDifference(disabled.Snapshot, repeated.Snapshot)} traced={StateDifference(disabled.Snapshot, enabled.Snapshot)}");
                AssertAttemptParity(disabled.Snapshot, enabled.Snapshot);
                Debug.Log(string.Format("B3R_DIAGNOSTIC_REJECT profile={0}/{1} attempted={2} fault={3} completed={4} grainPenetration={5:R} solidPenetration={6:R}",
                    profile[0], profile[1], disabled.AttemptedTicks, disabled.Snapshot.Fault,
                    disabled.Snapshot.CompletedTick, disabled.Snapshot.GrainPenetration, disabled.Snapshot.SolidPenetration));
            }
        }

        static int[][] Profiles()
        {
            return new[] { new[] { 4, 2 }, new[] { 8, 4 }, new[] { 12, 6 } };
        }

        static IEnumerator RunUntilDecision(ProofFixture fixture, int velocityIterations, int positionIterations, bool trace, Action<DiagnosticRun> receive)
        {
            var result = new DiagnosticRun();
            using (var solver = CreateSolver(fixture, velocityIterations, positionIterations, trace))
            {
                ProofSnapshot prior = null;
                for (int tick = 0; tick < 120; tick++)
                {
                    solver.Step(fixture.Force, fixture.Torque);
                    ProofSnapshot current = null;
                    yield return ReadSnapshot(solver, value => current = value);
                    result.AttemptedTicks++;
                    if (current.Fault != SolverFault.None)
                    {
                        // A rejection must expose the last committed state. Keeping
                        // this assertion here also checks the working-state trace
                        // cannot alter the authoritative rollback path.
                        if (prior != null)
                            AssertCommittedStateEquivalent(prior, current, "rollback after rejection");
                        result.Snapshot = current;
                        result.Previous = prior;
                        break;
                    }
                    prior = current;
                }
                result.Snapshot = result.Snapshot ?? prior;
                if (trace)
                {
                    var traceTask = solver.TraceAsync();
                    while (!traceTask.IsCompleted) yield return null;
                    if (traceTask.IsFaulted)
                        Assert.Fail(traceTask.Exception == null ? "proof trace readback failed" : traceTask.Exception.ToString());
                    result.Trace = traceTask.Result;
                }
            }
            receive(result);
        }

        static IEnumerator RunSingleAttempt(ProofFixture fixture, int velocity, int position, bool trace, Action<DiagnosticRun> receive)
        {
            using (var solver = CreateSolver(fixture, velocity, position, trace))
            {
                var result = new DiagnosticRun { AttemptedTicks = 1 };
                var initial = solver.SnapshotAsync(); while (!initial.IsCompleted) yield return null;
                result.Previous = initial.Result;
                solver.Step(fixture.Force, fixture.Torque);
                yield return ReadSnapshot(solver, value => result.Snapshot = value);
                AssertCommittedStateEquivalent(result.Previous, result.Snapshot, "single rejected attempt rollback");
                if (trace)
                {
                    var task = solver.TraceAsync(); while (!task.IsCompleted) yield return null;
                    result.Trace = task.Result;
                }
                receive(result);
            }
        }

        static void AssertAttemptParity(ProofSnapshot expected, ProofSnapshot actual)
        {
            // A rejected attempt must return the identical authoritative input,
            // even if its uncommitted reductions differ by floating-point roundoff.
            Assert.That(actual.Fault, Is.EqualTo(expected.Fault));
            Assert.That(actual.CompletedTick, Is.EqualTo(expected.CompletedTick));
            Assert.That(actual.Grains, Is.EqualTo(expected.Grains));
            Assert.That(actual.Endpoints, Is.EqualTo(expected.Endpoints));
        }

        static ParallelGrainSolver CreateSolver(ProofFixture fixture, int velocityIterations, int positionIterations, bool trace)
        {
            return new ParallelGrainSolver(fixture.Grains, fixture.Bodies, fixture.Parameters, fixture.Boundaries,
                velocityIterations, positionIterations, .3f, 64, 4096,
                trace ? new ProofTraceConfiguration() : null);
        }

        static IEnumerator ReadSnapshot(ParallelGrainSolver solver, Action<ProofSnapshot> receive)
        {
            var task = solver.SnapshotAsync();
            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted)
                Assert.Fail(task.Exception == null ? "proof snapshot readback failed" : task.Exception.ToString());
            receive(task.Result);
        }

        static ProofFixture Copy(ProofFixture source)
        {
            return new ProofFixture
            {
                Grains = (ProofGrain[])source.Grains.Clone(),
                Bodies = (BodyState[])source.Bodies.Clone(),
                Parameters = (BodyParameters[])source.Parameters.Clone(),
                Boundaries = (Boundary[])source.Boundaries.Clone(),
                Force = source.Force,
                Torque = source.Torque
            };
        }

        static void AssertFixtureEqual(ProofFixture expected, ProofFixture actual, string label)
        {
            Assert.That(actual.Force, Is.EqualTo(expected.Force), label + " force");
            Assert.That(actual.Torque, Is.EqualTo(expected.Torque), label + " torque");
            Assert.That(actual.Grains, Is.EqualTo(expected.Grains), label + " grains");
            Assert.That(actual.Bodies, Is.EqualTo(expected.Bodies), label + " bodies");
            Assert.That(actual.Parameters, Is.EqualTo(expected.Parameters), label + " parameters");
            Assert.That(actual.Boundaries, Is.EqualTo(expected.Boundaries), label + " boundaries");
        }

        static string StateDifference(ProofSnapshot expected, ProofSnapshot actual)
        {
            double position = 0, velocity = 0, angle = 0, spin = 0;
            for (int i = 0; i < expected.Endpoints.Length; i++)
            {
                var a = expected.Endpoints[i]; var b = actual.Endpoints[i];
                position = Math.Max(position, Math.Max(Math.Abs((double)a.Center.x - b.Center.x), Math.Abs((double)a.Center.y - b.Center.y)));
                velocity = Math.Max(velocity, Math.Max(Math.Abs((double)a.Velocity.x - b.Velocity.x), Math.Abs((double)a.Velocity.y - b.Velocity.y)));
                angle = Math.Max(angle, Math.Abs((double)a.Angle - b.Angle));
                spin = Math.Max(spin, Math.Abs((double)a.AngularVelocity - b.AngularVelocity));
            }
            return $"position:{position:R}|velocity:{velocity:R}|angle:{angle:R}|spin:{spin:R}";
        }

        static void AssertSnapshotsEquivalent(ProofSnapshot expected, ProofSnapshot actual, string label)
        {
            Assert.That(actual.Fault, Is.EqualTo(expected.Fault), label + " fault");
            Assert.That(actual.CompletedTick, Is.EqualTo(expected.CompletedTick), label + " completed tick");
            Assert.That(actual.Diagnostics, Is.EqualTo(expected.Diagnostics), label + " diagnostics");
            Assert.That(actual.Grains, Is.EqualTo(expected.Grains), label + " grains");
            Assert.That(actual.Endpoints, Is.EqualTo(expected.Endpoints), label + " endpoints");
        }

        static void AssertRejectedTrace(DiagnosticRun result)
        {
            Assert.That(result.Trace.Truncated, Is.False, "canonical replay must include complete adjacency");
            int validation = -1;
            uint lastSubstep = 0;
            for (int i = 0; i < result.Trace.Checkpoints.Length; i++)
            {
                var checkpoint = result.Trace.Checkpoints[i];
                if (checkpoint.Substep > lastSubstep) lastSubstep = checkpoint.Substep;
                if (checkpoint.Stage == ProofTraceStage.Validation) validation = i;
            }
            Assert.That(validation, Is.GreaterThanOrEqualTo(0), "rejected trace must retain a validation checkpoint");
            var lastValidation = result.Trace.Checkpoints[validation];
            Assert.That(result.Trace.Diagnostics.Length, Is.GreaterThan(validation * 16));
            Assert.That(result.Trace.Diagnostics[validation * 16], Is.EqualTo((uint)result.Snapshot.Fault), "trace validation D0");
            Assert.That(lastSubstep, Is.EqualTo(lastValidation.Substep), "rejected trace must stop after the faulting substep");

            bool grainMoved = false;
            bool bodyMoved = false;
            for (int i = 0; i < result.Trace.EndpointCount; i++)
            {
                var state = result.Trace.States[(int)lastValidation.StateOffset + i];
                Vector2 committed = result.Snapshot.Endpoints[i].Center;
                bool moved = Vector2.Distance(state.Center, committed) > .000001f;
                if (i < result.Snapshot.Grains.Length) grainMoved |= moved;
                else bodyMoved |= moved;
            }
            Assert.That(grainMoved, Is.True, "rejected trace must retain a working grain position");
            Assert.That(bodyMoved, Is.True, "rejected trace must retain a working body position");
        }

        static void RequireGpu()
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback)
                Assert.Ignore("parallel diagnostic tests require compute shaders and async GPU readback");
        }

        static void AssertCommittedStateEquivalent(ProofSnapshot expected, ProofSnapshot actual, string label)
        {
            Assert.That(actual.CompletedTick, Is.EqualTo(expected.CompletedTick), label + " completed tick");
            Assert.That(actual.Grains, Is.EqualTo(expected.Grains), label + " grains");
            Assert.That(actual.Endpoints, Is.EqualTo(expected.Endpoints), label + " endpoints");
        }

        sealed class DiagnosticRun
        {
            public int AttemptedTicks;
            public ProofSnapshot Snapshot;
            public ProofSnapshot Previous;
            public ProofTraceReadback Trace;
        }
    }
}
