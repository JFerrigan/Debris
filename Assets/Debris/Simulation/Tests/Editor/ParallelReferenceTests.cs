using System;
using System.Collections.Generic;
using Debris.Simulation.ParallelProof;
using NUnit.Framework;
using Ref = Debris.Simulation.Tests.ParallelContactReference;
using UnityEngine;

namespace Debris.Simulation.Tests
{
    public sealed class ParallelReferenceTests
    {
        static void Close(Ref.Double2 actual, Ref.Double2 expected, double tolerance = 1e-12)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(tolerance));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(tolerance));
        }

        [Test]
        public void DoubleObbReferenceMatchesRotatedFaceAndCornerProfiles()
        {
            double angle = .37;
            Ref.Double2 x = Ref.Double2.Rotate(Ref.Double2.Right, angle);
            Ref.Double2 y = Ref.Double2.Perpendicular(x);
            Ref.ContactManifold face = Ref.OBBContacts(
                new Ref.ReferenceObb(Ref.Double2.Zero, new Ref.Double2(.5, .5), angle),
                new Ref.ReferenceObb(x * .75 + y * .1, new Ref.Double2(.5, .5), angle));
            Assert.That(face.Valid, Is.True);
            Assert.That(face.Count, Is.EqualTo(1));
            Close(face.Contact0.Normal, x);
            Assert.That(face.Contact0.Separation, Is.EqualTo(-.25).Within(1e-12));
            Close(face.Contact0.Point, x * .375 + y * .05, 1e-12);

            Ref.ContactManifold corner = Ref.OBBContacts(
                new Ref.ReferenceObb(Ref.Double2.Zero, new Ref.Double2(.5, .5)),
                new Ref.ReferenceObb(new Ref.Double2(.99, .99), new Ref.Double2(.5, .5)));
            Assert.That(corner.Valid, Is.True);
            Assert.That(corner.Count, Is.EqualTo(2));
            Close(corner.Contact0.Normal, Ref.Double2.Right);
            Close(corner.Contact1.Normal, Ref.Double2.Up);
            Assert.That(corner.Contact0.Separation, Is.EqualTo(-.01).Within(1e-12));
            Assert.That(corner.Contact1.Separation, Is.EqualTo(-.01).Within(1e-12));
            Close(corner.Contact0.Point, new Ref.Double2(.495, .495), 1e-12);
            Close(corner.Contact1.Point, new Ref.Double2(.495, .495), 1e-12);
        }

        [Test]
        public void SeparatedSupportPatchUsesGapAndMidFaceAnchor()
        {
            Ref.ContactManifold separated = Ref.OBBContacts(
                new Ref.ReferenceObb(Ref.Double2.Zero, new Ref.Double2(.5, .5), .2),
                new Ref.ReferenceObb(Ref.Double2.Rotate(new Ref.Double2(2, 0), .2), new Ref.Double2(.5, .5), .2));
            Assert.That(separated.Valid, Is.True);
            Assert.That(separated.Count, Is.EqualTo(1));
            Ref.Double2 normal = Ref.Double2.Rotate(Ref.Double2.Right, .2);
            Close(separated.Contact0.Normal, normal);
            Assert.That(separated.Contact0.Separation, Is.EqualTo(1).Within(1e-12));
            Close(separated.Contact0.Point, normal, 1e-12);
        }

        [Test]
        public void OffCentreNormalImpulseUsesBothMassAndInertia()
        {
            var a = new Ref.BodyState(new Ref.Double2(-1, 1.5), new Ref.Double2(1, 0));
            var b = new Ref.BodyState(Ref.Double2.Zero, Ref.Double2.Zero);
            var pa = new Ref.BodyParameters(1, 6);
            var pb = new Ref.BodyParameters(.25, 3.0 / 17.0);
            var contact = new Ref.ContactConstraint(0, 1, Ref.Double2.Right,
                new Ref.Double2(.5, 0), new Ref.Double2(-.5, 1.5));
            Ref.ImpulseResult result = Ref.EvaluateVelocity(ref contact, a, b, pa, pb, 1.0 / 60.0, 0);
            double expectedEffective = 1 + .25 + (3.0 / 17.0) * 1.5 * 1.5;
            double expectedImpulse = 1 / expectedEffective;
            Assert.That(result.EffectiveNormal, Is.EqualTo(expectedEffective).Within(1e-12));
            Assert.That(result.NormalLambda, Is.EqualTo(expectedImpulse).Within(1e-12));
            Close(result.Impulse, new Ref.Double2(expectedImpulse, 0));

            Ref.BodyDelta[] reduced = Ref.ReduceAdjacency(2, new[] { Ref.AdjacencyRow.FromContact(contact, result.Impulse) });
            Ref.BodyState[] states = { a, b };
            Ref.BodyParameters[] parameters = { pa, pb };
            Ref.ApplyVelocityDeltas(states, parameters, reduced);
            Assert.That(states[0].Velocity.x, Is.EqualTo(1 - expectedImpulse).Within(1e-12));
            Assert.That(states[1].Velocity.x, Is.EqualTo(.25 * expectedImpulse).Within(1e-12));
            Assert.That(states[1].AngularVelocity, Is.EqualTo(-1.5 * 3.0 / 17.0 * expectedImpulse).Within(1e-12));
        }

        [Test]
        public void AnchoredEndpointReceivesNoVelocityOrPositionCorrection()
        {
            var moving = new Ref.BodyState(new Ref.Double2(-1.25, 0), new Ref.Double2(120, 3));
            var wall = new Ref.BodyState(Ref.Double2.Zero, Ref.Double2.Zero);
            var movingParameters = new Ref.BodyParameters(1, 0);
            var anchorParameters = new Ref.BodyParameters(0, 0);
            var contact = new Ref.ContactConstraint(0, 1, Ref.Double2.Right,
                new Ref.Double2(.5, 0), new Ref.Double2(-.5, 0), -.01);
            Ref.ImpulseResult impulse = Ref.EvaluateVelocity(ref contact, moving, wall, movingParameters, anchorParameters, 1.0 / 60.0, 0);
            Ref.BodyDelta[] velocity = Ref.ReduceAdjacency(2, new[] { Ref.AdjacencyRow.FromContact(contact, impulse.Impulse) });
            Ref.BodyState[] states = { moving, wall };
            Ref.BodyParameters[] parameters = { movingParameters, anchorParameters };
            Ref.ApplyVelocityDeltas(states, parameters, velocity);
            Assert.That(states[0].Velocity.x, Is.EqualTo(0).Within(1e-12));
            Assert.That(states[0].Velocity.y, Is.EqualTo(3).Within(1e-12));
            Close(states[1].Velocity, Ref.Double2.Zero);

            Ref.PositionResult position = Ref.EvaluatePosition(ref contact, movingParameters, anchorParameters, -.01);
            Ref.BodyDelta[] correction = Ref.ReduceAdjacency(2, new[] { Ref.AdjacencyRow.FromContact(contact, position.Correction) });
            Ref.ApplyPositionDeltas(states, parameters, correction);
            Assert.That(states[0].Center.x, Is.EqualTo(-1.25 - position.PositionLambda).Within(1e-12));
            Assert.That(states[1].Center.x, Is.EqualTo(0).Within(1e-12));
            Assert.That(states[0].Velocity.y, Is.EqualTo(3).Within(1e-12));
        }

        [Test]
        public void SharedBodyAdjacencySumsEveryEqualAndOppositeRowOnce()
        {
            var rows = new List<Ref.AdjacencyRow>
            {
                new Ref.AdjacencyRow(0, 1, new Ref.Double2(2, 0), 0.5, -1),
                new Ref.AdjacencyRow(2, 0, new Ref.Double2(3, 0), -0.25, 0.75),
                new Ref.AdjacencyRow(1, 2, new Ref.Double2(-1, 4), 2, -3)
            };
            Ref.BodyDelta[] reduced = Ref.ReduceAdjacency(3, rows);
            Close(reduced[0].Linear, new Ref.Double2(1, 0));
            Close(reduced[1].Linear, new Ref.Double2(3, -4));
            Close(reduced[2].Linear, new Ref.Double2(-4, 4));
            Assert.That(reduced[0].Angular, Is.EqualTo(-.5 + .75).Within(1e-12));
            Assert.That(reduced[1].Angular, Is.EqualTo(-3).Within(1e-12));
            Assert.That(reduced[2].Angular, Is.EqualTo(-2.75).Within(1e-12));
            Assert.That(reduced[0].Linear.x + reduced[1].Linear.x + reduced[2].Linear.x, Is.EqualTo(0).Within(1e-12));
            Assert.That(reduced[0].Linear.y + reduced[1].Linear.y + reduced[2].Linear.y, Is.EqualTo(0).Within(1e-12));
        }

        [Test]
        public void DegreeWeightedEffectiveMassAndPositionLambdaAreIndependent()
        {
            var p = new Ref.BodyParameters(.25, 2);
            var contact = new Ref.ContactConstraint(0, 1, Ref.Double2.Up,
                new Ref.Double2(3, 0), new Ref.Double2(-2, 0), -.01, 4, 3);
            double oneSide = .25 + 2 * 9;
            double otherSide = .25 + 2 * 4;
            Assert.That(Ref.EffectiveMass(p, contact.ArmA, contact.Normal), Is.EqualTo(oneSide).Within(1e-12));
            Ref.PositionResult result = Ref.EvaluatePosition(ref contact, p, p, -.01);
            Assert.That(result.EffectiveMass, Is.EqualTo(4 * oneSide + 3 * otherSide).Within(1e-12));
            Assert.That(result.PositionLambda, Is.EqualTo(.0099 / (4 * oneSide + 3 * otherSide)).Within(1e-12));
        }

        [Test]
        public void TouchingFrictionClampsToAccumulatedNormalLambda()
        {
            var a = new Ref.BodyState(Ref.Double2.Zero, new Ref.Double2(10, 8));
            var b = new Ref.BodyState(Ref.Double2.Zero, Ref.Double2.Zero);
            var p = new Ref.BodyParameters(1, 0);
            var contact = new Ref.ContactConstraint(0, 1, Ref.Double2.Right,
                Ref.Double2.Zero, Ref.Double2.Zero, -.0001);
            Ref.ImpulseResult result = Ref.EvaluateVelocity(ref contact, a, b, p, p, 1.0 / 60.0, .5);
            Assert.That(result.NormalLambda, Is.EqualTo(5).Within(1e-12));
            Assert.That(result.TangentLambda, Is.EqualTo(2.5).Within(1e-12));
            Close(result.Impulse, new Ref.Double2(5, 2.5));
        }

        [Test]
        public void TraceMaximumReportsStableOffendingKey()
        {
            var maximum = Ref.MaximumPenetration(new[]
            {
                new Ref.TraceContactObservation("grain:7|wall:2", 7, 2, -.0004),
                new Ref.TraceContactObservation("grain:3|grain:9", 3, 9, -.0021),
                new Ref.TraceContactObservation("grain:1|wall:0", 1, 0, .01)
            });
            Assert.That(maximum.HasValue, Is.True);
            Assert.That(maximum.Key, Is.EqualTo("grain:3|grain:9"));
            Assert.That(maximum.Penetration, Is.EqualTo(.0021).Within(1e-12));
            Assert.That(maximum.BodyA, Is.EqualTo(3));
            Assert.That(maximum.BodyB, Is.EqualTo(9));
        }

        [Test]
        public void TraceReplayMatchesGeometryMassAndReducedStatePass()
        {
            var trace = new ProofTraceReadback
            {
                Tick = 1, GrainCount = 2, EndpointCount = 2,
                Checkpoints = new[]
                {
                    new ProofTraceCheckpoint { Tick = 1, Substep = 0, Stage = ProofTraceStage.VelocityEvaluated,
                        Iteration = 0, ContactCount = 1, CapturedContactCount = 1, StateOffset = 0, ContactOffset = 0 },
                    new ProofTraceCheckpoint { Tick = 1, Substep = 0, Stage = ProofTraceStage.VelocityApplied,
                        Iteration = 0, CapturedContactCount = 0, StateOffset = 2, ContactOffset = 1 }
                },
                States = new[]
                {
                    new ProofTraceState { Center = Vector2.zero, Velocity = Vector2.right, Endpoint = 0, Degree = 1 },
                    new ProofTraceState { Center = Vector2.right, Velocity = Vector2.zero, Endpoint = 1, Degree = 1 },
                    new ProofTraceState { Center = Vector2.zero, Velocity = Vector2.zero, Endpoint = 0, Degree = 1 },
                    new ProofTraceState { Center = Vector2.right, Velocity = Vector2.zero, Endpoint = 1, Degree = 1 }
                },
                Contacts = new[]
                {
                    new ProofTraceContact { A = 0, B = 1, Feature = UInt32.MaxValue, Normal = Vector2.right,
                        Separation = 0, NormalLambda = 1, ArmA = new Vector2(.5f, 0), ArmB = new Vector2(-.5f, 0),
                        IncrementalImpulse = Vector2.right, EffectiveMassA = 1, EffectiveMassB = 0 }
                }
            };
            var parameters = new[] { new Ref.BodyParameters(1, 0), new Ref.BodyParameters(0, 0) };
            Ref.TraceReplayReport report = Ref.ReplayTrace(trace, parameters, 1e-6);
            Assert.That(report.Valid, Is.True, report.ToString());
            Assert.That(report.GeometryCompared, Is.EqualTo(1));
            Assert.That(report.MassCompared, Is.EqualTo(1));
            Assert.That(report.ReductionCompared, Is.EqualTo(2));
            Assert.That(report.MaximumPenetration, Is.Zero);
            trace.Contacts[0].NormalLambda = .8f;
            Assert.That(Ref.ReplayTrace(trace, parameters, 1e-6).LambdaMismatches, Is.GreaterThan(0));
            trace.Contacts[0].NormalLambda = 1;
            trace.States[0].Degree = 2;
            Assert.That(Ref.ReplayTrace(trace, parameters, 1e-6).DegreeMismatches, Is.GreaterThan(0));
            trace.States[0].Degree = 1;
            trace.Contacts[0].Normal = Vector2.up;
            Assert.That(Ref.ReplayTrace(trace, parameters, 1e-6).GeometryMismatches, Is.GreaterThan(0), "A nonwinning SAT axis is not accepted just because it was recorded");
        }
    }
}
