using ProofGrain = Debris.Simulation.ParallelProof.LooseCell;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Debris.Simulation.ParallelProof;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Debris.Simulation.Tests
{
    public sealed class ParallelTraceTests
    {
        [Test]
        public void DiagnosticRecordsMatchGpuStrides()
        {
            Assert.That(Marshal.SizeOf<ProofTraceCheckpoint>(), Is.EqualTo(40));
            Assert.That(Marshal.SizeOf<ProofTraceState>(), Is.EqualTo(48));
            Assert.That(Marshal.SizeOf<ProofTraceContact>(), Is.EqualTo(96));
            Assert.That(Marshal.SizeOf<ProofTraceSummary>(), Is.EqualTo(32));
            Assert.That(Marshal.OffsetOf<ProofTraceContact>(nameof(ProofTraceContact.Normal)).ToInt32(), Is.EqualTo(32));
            Assert.That(Marshal.OffsetOf<ProofTraceContact>(nameof(ProofTraceContact.IncrementalImpulse)).ToInt32(), Is.EqualTo(80));
        }

        [TestCase(0, 1)]
        [TestCase(17, 1)]
        [TestCase(4, 0)]
        [TestCase(4, 16385)]
        public void InvalidTraceCapacityRejectsBeforeGpuAllocation(int substeps, int contacts)
        {
            var f = ProofFixtures.IsolatedHeavy();
            Assert.Throws<ArgumentOutOfRangeException>(() => new ParallelGrainSolver(f.Grains, f.Bodies, f.Parameters, f.Boundaries,
                traceConfiguration: new ProofTraceConfiguration { SubstepCapacity = substeps, ContactCapacity = contacts }));
        }

        [Test]
        public void TracedSolverRejectsPopulationGrowth()
        {
            var initial = new[] { new ProofGrain { Center = Vector2.zero, Material = 1, Identity = 1 } };
            using (var solver = new ParallelGrainSolver(initial, Array.Empty<BodyState>(), Array.Empty<BodyParameters>(), Array.Empty<Boundary>(),
                4, 2, traceConfiguration: new ProofTraceConfiguration(), allocatedGrainCapacity: 2))
            {
                Assert.That(solver.TryAppend(new ProofGrain { Center = Vector2.right * 4, Material = 1, Identity = 2 }, 1), Is.False);
                Assert.That(solver.GrainCount, Is.EqualTo(1));
            }
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator CapacityTruncationIsSeparateFromPhysicsAndKeepsFullSummaries()
        {
            var grains = new[]
            {
                new ProofGrain { Center = Vector2.left, Material = 1, Identity = 1 },
                new ProofGrain { Center = Vector2.zero, Material = 1, Identity = 2 },
                new ProofGrain { Center = Vector2.right, Material = 1, Identity = 3 }
            };
            foreach (bool limitContacts in new[] { true, false })
            using (var solver = new ParallelGrainSolver(grains, Array.Empty<BodyState>(), Array.Empty<BodyParameters>(), Array.Empty<Boundary>(),
                4, 2, traceConfiguration: new ProofTraceConfiguration { ContactCapacity = limitContacts ? 1 : 64, SubstepCapacity = limitContacts ? 4 : 1 }))
            {
                solver.Step();
                var read = solver.TraceAsync(); while (!read.IsCompleted) yield return null;
                var trace = read.Result;
                Assert.That(trace.Truncated, Is.True);
                Assert.That(trace.Checkpoints.Length, Is.EqualTo((limitContacts ? 4 : 1) * 16));
                var before = trace.Checkpoints[1];
                Assert.That(before.Stage, Is.EqualTo(ProofTraceStage.BeforeVelocity));
                Assert.That(before.ContactCount, Is.EqualTo(2));
                Assert.That(before.CapturedContactCount, Is.EqualTo(limitContacts ? 1 : 2));
                Assert.That(trace.Summaries[1].GrainContactCount, Is.EqualTo(2), "summary must scan beyond diagnostic contact capacity");
                Assert.That(trace.Summaries[1].MaximumDegree, Is.EqualTo(2));
                var snapshot = solver.SnapshotAsync(); while (!snapshot.IsCompleted) yield return null;
                Assert.That(snapshot.Result.Fault, Is.EqualTo(SolverFault.None));
                Assert.That(snapshot.Result.CompletedTick, Is.EqualTo(1));
                Assert.That(snapshot.Result.Grains, Is.EqualTo(grains));
            }
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator IsolatedTraceCapturesEveryPassAndLatestTick()
        {
            var fixture = ProofFixtures.IsolatedHeavy();
            using (var solver = new ParallelGrainSolver(fixture.Grains, fixture.Bodies, fixture.Parameters, fixture.Boundaries,
                4, 2, 0, traceConfiguration: new ProofTraceConfiguration()))
            {
                for (int tick = 1; tick <= 2; tick++)
                {
                    solver.Step();
                    var task = solver.TraceAsync();
                    while (!task.IsCompleted) yield return null;
                    var trace = task.Result;
                    Assert.That(trace.Tick, Is.EqualTo(tick));
                    Assert.That(trace.Truncated, Is.False);
                    var replay = ParallelContactReference.ReplayTrace(trace, tolerance: 1e-5);
                    Assert.That(replay.Valid, Is.True, replay.ToString());
                    Assert.That(trace.Checkpoints.Length, Is.EqualTo(4 * (4 + 2 * 4 + 2 * 2)));
                    if (tick == 2)
                    {
                        string prefix = Path.Combine(Path.GetTempPath(), "b3r-trace-roundtrip-" + Guid.NewGuid().ToString("N"));
                        string archive = trace.Export(prefix);
                        var decoded = ProofTraceArchive.Read(archive);
                        Assert.That(decoded.Parameters, Is.EqualTo(trace.Parameters));
                        Assert.That(decoded.Boundaries, Is.EqualTo(trace.Boundaries));
                        Assert.That(decoded.Checkpoints, Is.EqualTo(trace.Checkpoints));
                        Assert.That(decoded.States, Is.EqualTo(trace.States));
                        Assert.That(decoded.Contacts, Is.EqualTo(trace.Contacts));
                        Assert.That(decoded.Summaries, Is.EqualTo(trace.Summaries));
                        Assert.That(decoded.Diagnostics, Is.EqualTo(trace.Diagnostics));
                        Assert.That(decoded.Friction, Is.Zero);
                        Assert.That(decoded.VelocityIterations, Is.EqualTo(4));
                        File.Delete(archive); File.Delete(prefix + ".summary.csv");
                    }
                    var keys = new HashSet<string>();
                    foreach (var checkpoint in trace.Checkpoints)
                    {
                        Assert.That(checkpoint.Tick, Is.EqualTo(tick));
                        Assert.That(checkpoint.Substep, Is.LessThan(4));
                        Assert.That(keys.Add($"{checkpoint.Substep}:{checkpoint.Stage}:{checkpoint.Iteration}"), Is.True, "Every pass needs its own storage");
                        Assert.That(checkpoint.StateOffset + 2, Is.LessThanOrEqualTo(trace.States.Length));
                        if (checkpoint.Stage == ProofTraceStage.VelocityEvaluated || checkpoint.Stage == ProofTraceStage.PositionEvaluated)
                        {
                            Assert.That(checkpoint.CapturedContactCount, Is.EqualTo(1));
                            var contact = trace.Contacts[checkpoint.ContactOffset];
                            Assert.That(contact.IdentityA, Is.EqualTo(1));
                            Assert.That(contact.A, Is.Zero);
                            Assert.That(contact.B, Is.EqualTo(1));
                            Assert.That(contact.Feature, Is.Zero);
                        }
                    }
                }
                var snapshotTask = solver.SnapshotAsync();
                while (!snapshotTask.IsCompleted) yield return null;
                Assert.That(snapshotTask.Result.Fault, Is.EqualTo(SolverFault.None));
                Assert.That(snapshotTask.Result.CompletedTick, Is.EqualTo(2));
            }
        }
    }
}
