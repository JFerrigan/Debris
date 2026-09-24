using System.Collections.Generic;
using System.Collections;
using Debris.Simulation.ParallelProof;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Debris.Simulation.Tests
{
    public sealed class ProofViabilityFixtureTests
    {
        [Test]
        public void CombinedFixtureHasLockedPopulationAndPhysicalEndpoints()
        {
            var fixture = ProofViabilityFixture.Create();
            Assert.AreEqual(8192, fixture.Grains.Length);
            Assert.AreEqual(18, fixture.Bodies.Length);
            Assert.AreEqual(21, fixture.Boundaries.Length);
            Assert.AreEqual(10000, 1 / fixture.Parameters[0].InverseMass, .01);
            var ids = new HashSet<uint>();
            var centers = new HashSet<Vector2>();
            foreach (var grain in fixture.Grains)
            {
                Assert.IsTrue(ids.Add(grain.Identity));
                Assert.IsTrue(centers.Add(grain.Center), "initial square centers must be distinct");
                Assert.AreEqual(1, grain.Material);
                Assert.Greater(grain.Center.x, -511);
                Assert.Less(grain.Center.x, 511);
                Assert.Greater(grain.Center.y, -511);
                Assert.Less(grain.Center.y, 511);
            }
            for (int i = 1; i <= 16; i++)
            {
                Assert.AreEqual(4, 1 / fixture.Parameters[i].InverseMass, .0001);
                Assert.AreEqual(1, fixture.Parameters[i].BoundaryCount);
                Assert.AreEqual((uint)(8192 + i), fixture.Boundaries[i + 3].Body);
            }
            Assert.AreEqual(0, fixture.Parameters[17].Mobility);
            Assert.AreEqual(Vector2.zero, fixture.Bodies[17].Velocity);
            Assert.AreEqual(new Vector2(-140, 67), fixture.Boundaries[20].Center);
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator DeliberateSpeedFaultRollsBackEntireCommittedState()
        {
            var fixture = ProofFixtures.IsolatedHeavy();
            fixture.Grains[0].Velocity = new Vector2(121, 0);
            using (var solver = fixture.Create(4, 2, 0))
            {
                var beforeTask = solver.SnapshotAsync();
                while (!beforeTask.IsCompleted) yield return null;
                var before = beforeTask.Result;
                solver.Step();
                var afterTask = solver.SnapshotAsync();
                while (!afterTask.IsCompleted) yield return null;
                var after = afterTask.Result;
                Assert.That(after.Fault.HasFlag(SolverFault.Speed), Is.True);
                Assert.That(after.CompletedTick, Is.EqualTo(before.CompletedTick));
                Assert.That(after.Grains[0].Center, Is.EqualTo(before.Grains[0].Center));
                Assert.That(after.Grains[0].Velocity, Is.EqualTo(before.Grains[0].Velocity));
                Assert.That(after.Grains[0].Angle, Is.EqualTo(before.Grains[0].Angle));
                Assert.That(after.Grains[0].AngularVelocity, Is.EqualTo(before.Grains[0].AngularVelocity));
                Assert.That(after.Grains[0].Identity, Is.EqualTo(before.Grains[0].Identity));
                Assert.That(after.Grains[0].Material, Is.EqualTo(before.Grains[0].Material));
                Assert.That(after.Endpoints[1].Center, Is.EqualTo(before.Endpoints[1].Center));
                Assert.That(after.Endpoints[1].Velocity, Is.EqualTo(before.Endpoints[1].Velocity));
                Assert.That(after.Endpoints[1].Angle, Is.EqualTo(before.Endpoints[1].Angle));
                Assert.That(after.Endpoints[1].AngularVelocity, Is.EqualTo(before.Endpoints[1].AngularVelocity));
            }
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator FastGrainTransfersMomentumToFiniteBodyWithoutTunnelling()
        {
            var fixture = ProofFixtures.IsolatedHeavy();
            fixture.Grains[0].Center = new Vector2(-1.25f, 0);
            fixture.Grains[0].Velocity = new Vector2(120, 0);
            fixture.Bodies[0] = new BodyState();
            fixture.Parameters[0].InverseMass = .25f;
            fixture.Parameters[0].InverseInertia = 1.5f;
            fixture.Boundaries[0].HalfSize = Vector2.one * .5f;
            using (var solver = fixture.Create(8, 4, 0))
            {
                solver.Step();
                var task = solver.SnapshotAsync();
                while (!task.IsCompleted) yield return null;
                var state = task.Result;
                Assert.That(state.Fault, Is.EqualTo(SolverFault.None));
                Assert.That(state.Diagnostics[2], Is.LessThanOrEqualTo(16));
                Assert.That(state.CompletedTick, Is.EqualTo(1));
                Assert.That(state.Grains[0].Center.x, Is.LessThan(state.Endpoints[1].Center.x));
                Assert.That(state.Endpoints[1].Velocity.x, Is.GreaterThan(0));
                Assert.That(state.Grains[0].Velocity.x + 4 * state.Endpoints[1].Velocity.x,
                    Is.EqualTo(120).Within(.012));
            }
        }
    }
}
