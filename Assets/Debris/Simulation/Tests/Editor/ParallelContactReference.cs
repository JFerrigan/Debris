using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Debris.Simulation.ParallelProof;
using UnityEngine;

namespace Debris.Simulation.Tests
{
    // Test-only double-precision contact calculations and GPU trace replay.
    // Geometry projects vertices and clips edges independently of the HLSL.
    // No part of this reference advances authoritative runtime state.
    public static class ParallelContactReference
    {
        public const double Epsilon = 1e-20;
        public const double GrainPositionTarget = .002;
        public const double SolidPositionTarget = .0001;

        public struct Double2
        {
            public double x, y;

            public Double2(double x, double y) { this.x = x; this.y = y; }
            public static Double2 Zero => new Double2(0, 0);
            public static Double2 Right => new Double2(1, 0);
            public static Double2 Up => new Double2(0, 1);
            public static Double2 operator +(Double2 a, Double2 b) => new Double2(a.x + b.x, a.y + b.y);
            public static Double2 operator -(Double2 a, Double2 b) => new Double2(a.x - b.x, a.y - b.y);
            public static Double2 operator -(Double2 a) => new Double2(-a.x, -a.y);
            public static Double2 operator *(Double2 a, double b) => new Double2(a.x * b, a.y * b);
            public static Double2 operator *(double b, Double2 a) => a * b;
            public static Double2 operator /(Double2 a, double b) => new Double2(a.x / b, a.y / b);
            public double LengthSquared => x * x + y * y;
            public double Length => Math.Sqrt(LengthSquared);
            public bool IsFinite => IsFiniteNumber(x) && IsFiniteNumber(y);
            public static double Dot(Double2 a, Double2 b) => a.x * b.x + a.y * b.y;
            public static double Cross(Double2 a, Double2 b) => a.x * b.y - a.y * b.x;
            public static Double2 Perpendicular(Double2 a) => new Double2(-a.y, a.x);
            public static Double2 Normalize(Double2 a)
            {
                double length = a.Length;
                return length > Epsilon ? a / length : Zero;
            }
            public static Double2 Rotate(Double2 value, double angle)
            {
                double s = Math.Sin(angle), c = Math.Cos(angle);
                return new Double2(c * value.x - s * value.y, s * value.x + c * value.y);
            }
            public override string ToString() => $"({x:R},{y:R})";
        }

        public struct ReferenceObb
        {
            public Double2 Center, HalfSize;
            public double Angle;
            public ReferenceObb(Double2 center, Double2 halfSize, double angle = 0)
            {
                Center = center; HalfSize = halfSize; Angle = angle;
            }
        }

        public struct ContactPoint
        {
            public Double2 Normal, Point;
            public double Separation;
            public ContactPoint(Double2 normal, double separation, Double2 point)
            {
                Normal = normal; Separation = separation; Point = point;
            }
        }

        public struct ContactManifold
        {
            public bool Valid;
            public int Count;
            public ContactPoint Contact0, Contact1;
            public ContactPoint this[int index]
            {
                get
                {
                    if (index == 0) return Contact0;
                    if (index == 1 && Count > 1) return Contact1;
                    throw new ArgumentOutOfRangeException(nameof(index));
                }
            }
        }

        public struct BodyState
        {
            public Double2 Center, Velocity;
            public double Angle, AngularVelocity;
            public BodyState(Double2 center, Double2 velocity, double angle = 0, double angularVelocity = 0)
            {
                Center = center; Velocity = velocity; Angle = angle; AngularVelocity = angularVelocity;
            }
        }

        public struct BodyParameters
        {
            public double InverseMass, InverseInertia;
            public BodyParameters(double inverseMass, double inverseInertia)
            {
                InverseMass = inverseMass; InverseInertia = inverseInertia;
            }
            public bool IsAnchored => InverseMass <= 0 && InverseInertia <= 0;
        }

        // The rows are intentionally stateful.  A trace can load its lambdas,
        // run one pass, and write the updated values back without reconstructing
        // a solver or a Unity object graph.
        public struct ContactConstraint
        {
            public int BodyA, BodyB;
            public Double2 Normal, ArmA, ArmB;
            public double Separation, NormalLambda, TangentLambda, PositionLambda;
            public uint DegreeA, DegreeB;
            public ContactConstraint(int bodyA, int bodyB, Double2 normal, Double2 armA, Double2 armB,
                double separation = 0, uint degreeA = 1, uint degreeB = 1)
            {
                BodyA = bodyA; BodyB = bodyB; Normal = Double2.Normalize(normal); ArmA = armA; ArmB = armB;
                Separation = separation; NormalLambda = 0; TangentLambda = 0; PositionLambda = 0;
                DegreeA = degreeA; DegreeB = degreeB;
            }
        }

        public struct ImpulseResult
        {
            public Double2 Impulse;
            public double NormalLambda, TangentLambda;
            public double EffectiveNormal, EffectiveTangent;
            public Double2 RelativeVelocity;
        }

        public struct PositionResult
        {
            public Double2 Correction;
            public double PositionLambda, EffectiveMass;
        }

        public struct VelocityPassResult
        {
            public BodyState[] States;
            public ImpulseResult[] Impulses;
            public BodyDelta[] Reduced;
        }

        // A row in the compact adjacency reduction.  Each row contributes -J
        // to A and +J to B, exactly once, regardless of endpoint degree.
        public struct AdjacencyRow
        {
            public int BodyA, BodyB;
            public Double2 Impulse;
            public double AngularImpulseA, AngularImpulseB;
            public AdjacencyRow(int bodyA, int bodyB, Double2 impulse, double angularImpulseA, double angularImpulseB)
            {
                BodyA = bodyA; BodyB = bodyB; Impulse = impulse;
                AngularImpulseA = angularImpulseA; AngularImpulseB = angularImpulseB;
            }
            public static AdjacencyRow FromContact(ContactConstraint c, Double2 impulse)
            {
                return new AdjacencyRow(c.BodyA, c.BodyB, impulse,
                    Double2.Cross(c.ArmA, impulse), Double2.Cross(c.ArmB, impulse));
            }
        }

        public struct BodyDelta
        {
            public Double2 Linear;
            public double Angular;
        }

        public struct TraceContactObservation
        {
            public string Key;
            public double Separation, Penetration;
            public int BodyA, BodyB;
            public TraceContactObservation(string key, int bodyA, int bodyB, double separation)
            {
                Key = key; BodyA = bodyA; BodyB = bodyB; Separation = separation;
                Penetration = Math.Max(0, -separation);
            }
        }

        public struct TraceMaximum
        {
            public bool HasValue;
            public string Key;
            public double Penetration;
            public int BodyA, BodyB;
        }

        public struct TraceReplayReport
        {
            public bool Valid;
            public bool Truncated;
            public int Checkpoints, Contacts, GeometryCompared, GeometryMismatches;
            public int MassCompared, MassMismatches, DegreeCompared, DegreeMismatches;
            public int LambdaCompared, LambdaMismatches, ReductionCompared, ReductionMismatches;
            public double MaximumPenetration, MaximumSeparationError, MaximumNormalError, MaximumArmError;
            public double MaximumMassError, MaximumLambdaError, MaximumReductionError;
            public string MaximumKey, FirstError;
            public double MaximumValidatedSolidPenetration;
            public string MaximumValidatedSolidKey;
            public int FirstFailingSubstep;
            public int AxisChoiceDifferences, AmbiguousReferenceFaces;
            public double MaximumAxisSeparationLoss, MaximumWinnerNormalDifference;

            public override string ToString()
            {
                return $"valid={Valid} truncated={Truncated} checkpoints={Checkpoints} contacts={Contacts} "
                    + $"geometry={GeometryCompared}/{GeometryMismatches} mass={MassCompared}/{MassMismatches} "
                    + $"degree={DegreeCompared}/{DegreeMismatches} lambda={LambdaCompared}/{LambdaMismatches} "
                    + $"reduction={ReductionCompared}/{ReductionMismatches} penetration={MaximumPenetration:R} "
                    + $"separationError={MaximumSeparationError:R} normalError={MaximumNormalError:R} armError={MaximumArmError:R} "
                    + $"massError={MaximumMassError:R} lambdaError={MaximumLambdaError:R} reductionError={MaximumReductionError:R} "
                    + $"key={MaximumKey ?? ""} first={FirstError ?? ""} "
                    + $"axisChoices={AxisChoiceDifferences} ambiguousReferenceFaces={AmbiguousReferenceFaces} axisSeparationLoss={MaximumAxisSeparationLoss:R} winnerNormalDifference={MaximumWinnerNormalDifference:R} "
                    + $"firstFailingSubstep={FirstFailingSubstep} validatedSolid={MaximumValidatedSolidPenetration:R} solidKey={MaximumValidatedSolidKey ?? ""}";
            }
        }

        public static ContactManifold OBBContacts(ReferenceObb a, ReferenceObb b)
        {
            ContactManifold result = new ContactManifold();
            if (!FiniteBox(a) || !FiniteBox(b)) return result;

            Double2[] axes = { Axis(a, 0), Axis(a, 1), Axis(b, 0), Axis(b, 1) };
            double[] separations = new double[4];
            double best = double.NegativeInfinity;
            int bestAxis = 0;
            double scale = 1;
            for (int i = 0; i < axes.Length; i++)
            {
                separations[i] = AxisSeparation(a, b, axes[i]);
                if (separations[i] > best) { best = separations[i]; bestAxis = i; }
                scale = Math.Max(scale, Math.Abs(separations[i]) + Radius(a, axes[i]) + Radius(b, axes[i]));
            }

            double tie = 2e-5 * scale;
            int secondAxis = -1;
            for (int candidate = 0; candidate < axes.Length; candidate++)
            {
                if (Math.Abs(separations[candidate] - best) <= tie &&
                    Math.Abs(Double2.Dot(axes[candidate], axes[bestAxis])) <= .9999)
                { secondAxis = candidate; break; }
            }

            result.Valid = true;
            result.Count = secondAxis < 0 ? 1 : 2;
            result.Contact0 = ContactOnAxis(a, b, bestAxis, axes[bestAxis], separations[bestAxis]);
            if (secondAxis >= 0)
                result.Contact1 = ContactOnAxis(a, b, secondAxis, axes[secondAxis], separations[secondAxis]);
            return result;
        }

        public static double EffectiveMass(BodyParameters parameters, Double2 arm, Double2 direction)
        {
            double lever = Double2.Cross(arm, direction);
            return parameters.InverseMass + parameters.InverseInertia * lever * lever;
        }

        // Evaluates one velocity row against the supplied pre-pass body states.
        // The returned impulse is reduced separately so this method never hides
        // endpoint ordering or accidentally applies a body twice.
        public static ImpulseResult EvaluateVelocity(ref ContactConstraint contact,
            BodyState stateA, BodyState stateB, BodyParameters parametersA, BodyParameters parametersB,
            double substepDt, double friction)
        {
            if (substepDt <= 0 || !IsFiniteNumber(substepDt)) throw new ArgumentOutOfRangeException(nameof(substepDt));
            Double2 tangent = Double2.Perpendicular(contact.Normal);
            Double2 relative = stateB.Velocity + stateB.AngularVelocity * Double2.Perpendicular(contact.ArmB)
                - stateA.Velocity - stateA.AngularVelocity * Double2.Perpendicular(contact.ArmA);
            double kNormal = Split(contact.DegreeA, parametersA, contact.ArmA, contact.Normal)
                + Split(contact.DegreeB, parametersB, contact.ArmB, contact.Normal);
            double target = -Math.Max(0, contact.Separation) / substepDt;
            double nextNormal = Math.Max(0, contact.NormalLambda + (target - Double2.Dot(relative, contact.Normal)) / Math.Max(kNormal, Epsilon));
            double deltaNormal = nextNormal - contact.NormalLambda;
            contact.NormalLambda = nextNormal;

            double kTangent = Split(contact.DegreeA, parametersA, contact.ArmA, tangent)
                + Split(contact.DegreeB, parametersB, contact.ArmB, tangent);
            double nextTangent = contact.Separation <= .0001
                ? Clamp(contact.TangentLambda - Double2.Dot(relative, tangent) / Math.Max(kTangent, Epsilon), -friction * nextNormal, friction * nextNormal)
                : 0;
            double deltaTangent = nextTangent - contact.TangentLambda;
            contact.TangentLambda = nextTangent;
            return new ImpulseResult
            {
                Impulse = contact.Normal * deltaNormal + tangent * deltaTangent,
                NormalLambda = nextNormal, TangentLambda = nextTangent,
                EffectiveNormal = kNormal, EffectiveTangent = kTangent,
                RelativeVelocity = relative
            };
        }

        // Evaluate all rows against one pre-pass snapshot and perform one
        // complete adjacency reduction.  This mirrors the GPU phase boundary:
        // no row receives an order-dependent partially updated endpoint state.
        public static VelocityPassResult EvaluateVelocityPass(BodyState[] states,
            BodyParameters[] parameters, ContactConstraint[] contacts, double substepDt,
            double friction)
        {
            if (states == null || parameters == null || contacts == null || states.Length != parameters.Length)
                throw new ArgumentException("States and parameters must have equal non-null lengths.");
            BodyState[] snapshot = (BodyState[])states.Clone();
            ImpulseResult[] impulses = new ImpulseResult[contacts.Length];
            var rows = new AdjacencyRow[contacts.Length];
            for (int i = 0; i < contacts.Length; i++)
            {
                ContactConstraint contact = contacts[i];
                if (contact.BodyA < 0 || contact.BodyA >= snapshot.Length || contact.BodyB < 0 || contact.BodyB >= snapshot.Length)
                    throw new ArgumentOutOfRangeException(nameof(contacts));
                impulses[i] = EvaluateVelocity(ref contact, snapshot[contact.BodyA], snapshot[contact.BodyB],
                    parameters[contact.BodyA], parameters[contact.BodyB], substepDt, friction);
                contacts[i] = contact;
                rows[i] = AdjacencyRow.FromContact(contact, impulses[i].Impulse);
            }
            BodyDelta[] reduced = ReduceAdjacency(snapshot.Length, rows);
            BodyState[] resultStates = (BodyState[])snapshot.Clone();
            ApplyVelocityDeltas(resultStates, parameters, reduced);
            return new VelocityPassResult { States = resultStates, Impulses = impulses, Reduced = reduced };
        }

        // Evaluate one nonlinear positional row.  Unlike velocity impulses,
        // this correction is never written into linear or angular velocity.
        public static PositionResult EvaluatePosition(ref ContactConstraint contact,
            BodyParameters parametersA, BodyParameters parametersB, double separation,
            double solidTarget = SolidPositionTarget)
        {
            double k = Split(contact.DegreeA, parametersA, contact.ArmA, contact.Normal)
                + Split(contact.DegreeB, parametersB, contact.ArmB, contact.Normal);
            double next = Math.Max(0, contact.PositionLambda + (-separation - solidTarget) / Math.Max(k, Epsilon));
            double delta = next - contact.PositionLambda;
            contact.PositionLambda = next;
            return new PositionResult { Correction = contact.Normal * delta, PositionLambda = next, EffectiveMass = k };
        }

        public static BodyDelta[] ReduceAdjacency(int bodyCount, IList<AdjacencyRow> rows)
        {
            if (bodyCount < 0) throw new ArgumentOutOfRangeException(nameof(bodyCount));
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            BodyDelta[] result = new BodyDelta[bodyCount];
            for (int i = 0; i < rows.Count; i++)
            {
                AdjacencyRow row = rows[i];
                if (row.BodyA < 0 || row.BodyA >= bodyCount || row.BodyB < 0 || row.BodyB >= bodyCount)
                    throw new ArgumentOutOfRangeException(nameof(rows));
                result[row.BodyA].Linear -= row.Impulse;
                result[row.BodyA].Angular -= row.AngularImpulseA;
                result[row.BodyB].Linear += row.Impulse;
                result[row.BodyB].Angular += row.AngularImpulseB;
            }
            return result;
        }

        public static void ApplyVelocityDeltas(BodyState[] states, BodyParameters[] parameters, BodyDelta[] reduced)
        {
            ValidateBodies(states, parameters, reduced);
            for (int i = 0; i < states.Length; i++)
            {
                states[i].Velocity += parameters[i].InverseMass * reduced[i].Linear;
                states[i].AngularVelocity += parameters[i].InverseInertia * reduced[i].Angular;
            }
        }

        public static void ApplyPositionDeltas(BodyState[] states, BodyParameters[] parameters, BodyDelta[] reduced)
        {
            ValidateBodies(states, parameters, reduced);
            for (int i = 0; i < states.Length; i++)
            {
                states[i].Center += parameters[i].InverseMass * reduced[i].Linear;
                states[i].Angle += parameters[i].InverseInertia * reduced[i].Angular;
            }
        }

        // A tiny trace utility is kept schema-agnostic: callers can feed the
        // exported rows as they decode them and get the same maximum report the
        // GPU proof records.  This is useful while the transport format evolves.
        public static TraceMaximum MaximumPenetration(IList<TraceContactObservation> observations)
        {
            if (observations == null) throw new ArgumentNullException(nameof(observations));
            TraceMaximum maximum = new TraceMaximum { HasValue = false, Penetration = 0 };
            for (int i = 0; i < observations.Count; i++)
            {
                TraceContactObservation current = observations[i];
                bool greater = current.Penetration > maximum.Penetration;
                bool stableTie = maximum.HasValue && current.Penetration == maximum.Penetration
                    && string.CompareOrdinal(current.Key, maximum.Key) < 0;
                if (!maximum.HasValue || greater || stableTie)
                {
                    maximum.HasValue = true; maximum.Key = current.Key; maximum.Penetration = current.Penetration;
                    maximum.BodyA = current.BodyA; maximum.BodyB = current.BodyB;
                }
            }
            return maximum;
        }

        enum ErrorKind { Geometry, Mass, Degree, Lambda, Reduction }

        struct LambdaState
        {
            public double Normal, Tangent, Position;
        }

        // Replay a ProofTraceReadback without invoking the GPU solver. The
        // readback carries the exact parameters and boundary metadata used to
        // reconstruct both grain and wall contacts.
        public static TraceReplayReport ReplayTrace(ProofTraceReadback trace,
            BodyParameters[] parameters = null, double tolerance = 1e-5)
        {
            if (trace == null) throw new ArgumentNullException(nameof(trace));
            if (tolerance < 0 || !IsFiniteNumber(tolerance)) throw new ArgumentOutOfRangeException(nameof(tolerance));
            bool truncated = false;
            if (trace.Checkpoints != null)
                for (int i = 0; i < trace.Checkpoints.Length; i++) truncated |= trace.Checkpoints[i].Truncated;
            TraceReplayReport report = new TraceReplayReport { Valid = !truncated, Truncated = truncated,
                Checkpoints = trace.Checkpoints == null ? 0 : trace.Checkpoints.Length, FirstFailingSubstep = -1 };
            if (trace.Checkpoints == null) return report;
            BodyParameters[] sourceParameters = parameters != null && parameters.Length == trace.EndpointCount
                ? parameters : ConvertParameters(trace.Parameters);
            bool haveParameters = sourceParameters != null && sourceParameters.Length == trace.EndpointCount;
            var previous = new Dictionary<string, LambdaState>();
            for (int c = 0; c < trace.Checkpoints.Length; c++)
            {
                ProofTraceCheckpoint checkpoint = trace.Checkpoints[c];
                if (report.FirstFailingSubstep < 0 && trace.Diagnostics.Length > c * 16 && trace.Diagnostics[c * 16] != 0)
                    report.FirstFailingSubstep = (int)checkpoint.Substep;
                int contactStart = checked((int)checkpoint.ContactOffset);
                int stateStart = checked((int)checkpoint.StateOffset);
                int captured = Math.Min((int)checkpoint.CapturedContactCount, Math.Max(0, trace.Contacts.Length - contactStart));
                report.Contacts += captured;
                var rows = new List<AdjacencyRow>(captured);
                var incidence = new int[Math.Max(0, trace.EndpointCount)];
                for (int i = 0; i < captured; i++)
                {
                    ProofTraceContact gpu = trace.Contacts[contactStart + i];
                    string contactKey = StableKey(gpu);
                    string key = $"{checkpoint.Tick}:{checkpoint.Substep}:{checkpoint.Stage}:{checkpoint.Iteration}:{contactKey}";
                    TraceContactObservation observation = new TraceContactObservation(key, (int)gpu.A, (int)gpu.B, gpu.Separation);
                    if (report.MaximumKey == null || observation.Penetration > report.MaximumPenetration ||
                        (observation.Penetration == report.MaximumPenetration && string.CompareOrdinal(key, report.MaximumKey) < 0))
                    {
                        report.MaximumPenetration = observation.Penetration; report.MaximumKey = key;
                    }

                    if (checkpoint.Stage == ProofTraceStage.Validation && gpu.Feature != uint.MaxValue && observation.Penetration > report.MaximumValidatedSolidPenetration)
                    {
                        report.MaximumValidatedSolidPenetration = observation.Penetration;
                        report.MaximumValidatedSolidKey = key;
                    }
                    Double2 impulse = new Double2(gpu.IncrementalImpulse.x, gpu.IncrementalImpulse.y);
                    double torqueA = Double2.Cross(new Double2(gpu.ArmA.x, gpu.ArmA.y), impulse);
                    double torqueB = Double2.Cross(new Double2(gpu.ArmB.x, gpu.ArmB.y), impulse);
                    if (checkpoint.Stage == ProofTraceStage.VelocityEvaluated || checkpoint.Stage == ProofTraceStage.PositionEvaluated)
                    {
                        double torqueError = Math.Max(Math.Abs(torqueA - gpu.IncrementalTorqueA), Math.Abs(torqueB - gpu.IncrementalTorqueB));
                        report.MaximumReductionError = Math.Max(report.MaximumReductionError, torqueError);
                        if (torqueError > tolerance) AddError(ref report, "torque " + key, ErrorKind.Reduction);
                    }

                    int localA = checked((int)gpu.A), localB = checked((int)gpu.B);
                    if (localA >= trace.EndpointCount || localB >= trace.EndpointCount || stateStart + localA >= trace.States.Length || stateStart + localB >= trace.States.Length)
                        continue;
                    if (localA >= 0 && localB >= 0 && gpu.Feature == UInt32.MaxValue)
                    {
                        ProofTraceState stateA = trace.States[stateStart + localA], stateB = trace.States[stateStart + localB];
                        report.GeometryCompared++;
                        CompareGeometry(new ReferenceObb(new Double2(stateA.Center.x, stateA.Center.y), new Double2(.5, .5), stateA.Angle),
                            new ReferenceObb(new Double2(stateB.Center.x, stateB.Center.y), new Double2(.5, .5), stateB.Angle),
                            gpu, stateA, stateB, tolerance, key, ref report);
                    }
                    else if (localA >= 0 && localB >= 0 && gpu.Feature < (trace.Boundaries == null ? 0u : (uint)trace.Boundaries.Length)
                        && haveParameters && trace.Boundaries[(int)gpu.Feature].Body == gpu.B)
                    {
                        ProofTraceState stateA = trace.States[stateStart + localA], stateB = trace.States[stateStart + localB];
                        Boundary patch = trace.Boundaries[(int)gpu.Feature];
                        Double2 localCom = trace.Parameters != null && localB < trace.Parameters.Length
                            ? new Double2(trace.Parameters[localB].LocalCOM.x, trace.Parameters[localB].LocalCOM.y) : Double2.Zero;
                        Double2 patchCenter = new Double2(stateB.Center.x, stateB.Center.y)
                            + Double2.Rotate(new Double2(patch.Center.x, patch.Center.y) - localCom, stateB.Angle);
                        report.GeometryCompared++;
                        CompareGeometry(new ReferenceObb(new Double2(stateA.Center.x, stateA.Center.y), new Double2(.5, .5), stateA.Angle),
                            new ReferenceObb(patchCenter, new Double2(patch.HalfSize.x, patch.HalfSize.y), stateB.Angle),
                            gpu, stateA, stateB, tolerance, key, ref report);
                    }
                    if (haveParameters)
                    {
                        ProofTraceState stateA = trace.States[stateStart + localA], stateB = trace.States[stateStart + localB];
                        Double2 normal = new Double2(gpu.Normal.x, gpu.Normal.y);
                        double expectedA = Math.Max(1u, stateA.Degree) * EffectiveMass(sourceParameters[localA], new Double2(gpu.ArmA.x, gpu.ArmA.y), normal);
                        double expectedB = Math.Max(1u, stateB.Degree) * EffectiveMass(sourceParameters[localB], new Double2(gpu.ArmB.x, gpu.ArmB.y), normal);
                        double massError = Math.Max(Math.Abs(expectedA - gpu.EffectiveMassA), Math.Abs(expectedB - gpu.EffectiveMassB));
                        report.MassCompared++; report.MaximumMassError = Math.Max(report.MaximumMassError, massError);
                        if (massError > tolerance) AddError(ref report, "mass " + key, ErrorKind.Mass);
                    }
                    if (localA >= 0 && localA < incidence.Length) incidence[localA]++;
                    if (localB >= 0 && localB < incidence.Length) incidence[localB]++;
                    BodyState replayA = new BodyState(new Double2(trace.States[stateStart + localA].Center.x, trace.States[stateStart + localA].Center.y),
                        new Double2(trace.States[stateStart + localA].Velocity.x, trace.States[stateStart + localA].Velocity.y),
                        trace.States[stateStart + localA].Angle, trace.States[stateStart + localA].AngularVelocity);
                    BodyState replayB = new BodyState(new Double2(trace.States[stateStart + localB].Center.x, trace.States[stateStart + localB].Center.y),
                        new Double2(trace.States[stateStart + localB].Velocity.x, trace.States[stateStart + localB].Velocity.y),
                        trace.States[stateStart + localB].Angle, trace.States[stateStart + localB].AngularVelocity);
                    CheckLambdas(checkpoint, gpu, contactKey, replayA, replayB, haveParameters ? sourceParameters[localA] : new BodyParameters(),
                        haveParameters ? sourceParameters[localB] : new BodyParameters(), trace.States[stateStart + localA].Degree,
                        trace.States[stateStart + localB].Degree, trace.Dt / Math.Max(1u, trace.Diagnostics.Length > c * 16 + 2 ? trace.Diagnostics[c * 16 + 2] : 1u), trace.Friction,
                        tolerance, ref previous, ref report);
                    rows.Add(new AdjacencyRow(localA, localB, impulse, gpu.IncrementalTorqueA, gpu.IncrementalTorqueB));
                }

                if (captured == (int)checkpoint.ContactCount && captured > 0)
                    CompareDegrees(trace, checkpoint, incidence, tolerance, ref report);
                if (rows.Count > 0 && haveParameters)
                    CompareReduction(trace, checkpoint, rows, sourceParameters, tolerance, ref report);
            }
            return report;
        }

        // Editor execute-method entry point used after a player exports an archive:
        // -debrisTraceDirectory <dir> -debrisTraceOutput <path>. Every export
        // is replayed and retained in the output for discrepancy auditing.
        public static void RunReplay()
        {
            string directory = CommandLineValue("-debrisTraceDirectory");
            string output = CommandLineValue("-debrisTraceOutput");
            if (String.IsNullOrEmpty(directory)) { Debug.LogError("-debrisTraceDirectory is required"); return; }
            string[] files = Directory.Exists(directory) ? Directory.GetFiles(directory, "*.b3rt.gz") : new string[0];
            if (files.Length == 0) { Debug.LogError("No proof trace archive found in " + directory); return; }
            Array.Sort(files, StringComparer.Ordinal);
            var outputText = new StringBuilder();
            bool valid = true;
            for (int i = 0; i < files.Length; i++)
            {
                ProofTraceReadback trace = ProofTraceArchive.Read(files[i]);
                TraceReplayReport report = ReplayTrace(trace);
                ParallelTraceReport.WriteSelectedWall(files[i] + ".wall.csv", trace);
                valid &= report.Valid && !report.Truncated;
                string line = Path.GetFileName(files[i]) + " " + report;
                outputText.AppendLine(line); Debug.Log("B3R_TRACE_REPLAY " + line);
            }
            if (!String.IsNullOrEmpty(output)) File.WriteAllText(output, outputText.ToString());
            if (!valid) Debug.LogError("B3R_TRACE_REPLAY discrepancies found");
        }

        static void CompareReduction(ProofTraceReadback trace, ProofTraceCheckpoint checkpoint,
            List<AdjacencyRow> rows, BodyParameters[] parameters, double tolerance, ref TraceReplayReport report)
        {
            if (checkpoint.Stage != ProofTraceStage.VelocityEvaluated && checkpoint.Stage != ProofTraceStage.PositionEvaluated) return;
            ProofTraceStage appliedStage = checkpoint.Stage == ProofTraceStage.VelocityEvaluated ? ProofTraceStage.VelocityApplied : ProofTraceStage.PositionApplied;
            for (int i = 0; i < trace.Checkpoints.Length; i++)
            {
                ProofTraceCheckpoint applied = trace.Checkpoints[i];
                if (applied.Tick != checkpoint.Tick || applied.Substep != checkpoint.Substep || applied.Iteration != checkpoint.Iteration || applied.Stage != appliedStage) continue;
                if (applied.StateOffset + trace.EndpointCount > trace.States.Length) return;
                BodyDelta[] reduced = ReduceAdjacency(trace.EndpointCount, rows);
                for (int endpoint = 0; endpoint < trace.EndpointCount; endpoint++)
                {
                    ProofTraceState before = trace.States[(int)checkpoint.StateOffset + endpoint];
                    ProofTraceState after = trace.States[(int)applied.StateOffset + endpoint];
                    Double2 actualLinear = checkpoint.Stage == ProofTraceStage.VelocityEvaluated
                        ? new Double2(after.Velocity.x - before.Velocity.x, after.Velocity.y - before.Velocity.y)
                        : new Double2(after.Center.x - before.Center.x, after.Center.y - before.Center.y);
                    double actualAngular = checkpoint.Stage == ProofTraceStage.VelocityEvaluated
                        ? after.AngularVelocity - before.AngularVelocity : after.Angle - before.Angle;
                    Double2 expectedLinear = parameters[endpoint].InverseMass * reduced[endpoint].Linear;
                    double expectedAngular = parameters[endpoint].InverseInertia * reduced[endpoint].Angular;
                    double error = Math.Max(Math.Max(Math.Abs(actualLinear.x - expectedLinear.x), Math.Abs(actualLinear.y - expectedLinear.y)), Math.Abs(actualAngular - expectedAngular));
                    report.ReductionCompared++; report.MaximumReductionError = Math.Max(report.MaximumReductionError, error);
                    if (error > tolerance) AddError(ref report, "reduction " + checkpoint.Tick + ":" + checkpoint.Substep + ":" + checkpoint.Iteration + ":" + endpoint, ErrorKind.Reduction);
                }
                return;
            }
        }

        static void CompareDegrees(ProofTraceReadback trace, ProofTraceCheckpoint checkpoint,
            int[] incidence, double tolerance, ref TraceReplayReport report)
        {
            int stateStart = checked((int)checkpoint.StateOffset);
            if (trace.States == null || stateStart < 0 || stateStart + incidence.Length > trace.States.Length) return;
            for (int endpoint = 0; endpoint < incidence.Length; endpoint++)
            {
                uint actual = trace.States[stateStart + endpoint].Degree;
                report.DegreeCompared++;
                double error = Math.Abs(actual - incidence[endpoint]);
                if (error > tolerance) AddError(ref report, "degree " + checkpoint.Tick + ":" + checkpoint.Substep + ":" + endpoint, ErrorKind.Degree);
            }
        }

        static void CheckLambdas(ProofTraceCheckpoint checkpoint, ProofTraceContact contact, string key,
            BodyState stateA, BodyState stateB, BodyParameters parametersA, BodyParameters parametersB,
            uint degreeA, uint degreeB, double dt, double friction, double tolerance, ref Dictionary<string, LambdaState> previous,
            ref TraceReplayReport report)
        {
            LambdaState current = new LambdaState { Normal = contact.NormalLambda, Tangent = contact.TangentLambda, Position = contact.PositionLambda };
            LambdaState prior;
            if (checkpoint.Stage == ProofTraceStage.BeforeVelocity)
            {
                previous[key] = current;
                return;
            }
            if (!previous.TryGetValue(key, out prior)) prior = new LambdaState();
            if (checkpoint.Stage == ProofTraceStage.VelocityEvaluated || checkpoint.Stage == ProofTraceStage.PositionEvaluated)
            {
                Double2 normal = new Double2(contact.Normal.x, contact.Normal.y), impulse = new Double2(contact.IncrementalImpulse.x, contact.IncrementalImpulse.y);
                double expected = impulse.x * normal.x + impulse.y * normal.y;
                double observed = checkpoint.Stage == ProofTraceStage.PositionEvaluated
                    ? current.Position - prior.Position : current.Normal - prior.Normal;
                double error = Math.Abs(observed - expected);
                report.LambdaCompared++; report.MaximumLambdaError = Math.Max(report.MaximumLambdaError, error);
                if (error > tolerance) AddError(ref report, "lambda " + key, ErrorKind.Lambda);
                if (checkpoint.Stage == ProofTraceStage.VelocityEvaluated)
                {
                    Double2 tangent = Double2.Perpendicular(normal);
                    double tangentError = Math.Abs((current.Tangent - prior.Tangent) - (impulse.x * tangent.x + impulse.y * tangent.y));
                    report.LambdaCompared++; report.MaximumLambdaError = Math.Max(report.MaximumLambdaError, tangentError);
                    if (tangentError > tolerance) AddError(ref report, "lambda-tangent " + key, ErrorKind.Lambda);
                }

                if (dt > 0 && parametersA.InverseMass >= 0 && parametersB.InverseMass >= 0)
                {
                    ContactConstraint oracle = new ContactConstraint((int)contact.A, (int)contact.B,
                        new Double2(normal.x, normal.y), new Double2(contact.ArmA.x, contact.ArmA.y),
                        new Double2(contact.ArmB.x, contact.ArmB.y), contact.Separation, 1, 1);
                    oracle.DegreeA = degreeA; oracle.DegreeB = degreeB;
                    oracle.NormalLambda = prior.Normal; oracle.TangentLambda = prior.Tangent; oracle.PositionLambda = prior.Position;
                    if (checkpoint.Stage == ProofTraceStage.VelocityEvaluated)
                    {
                        ImpulseResult oracleResult = EvaluateVelocity(ref oracle, stateA, stateB, parametersA, parametersB, dt, friction);
                        double oracleError = Math.Max(Math.Max(Math.Abs(current.Normal - oracleResult.NormalLambda), Math.Abs(current.Tangent - oracleResult.TangentLambda)),
                            (impulse - oracleResult.Impulse).Length);
                        report.MaximumLambdaError = Math.Max(report.MaximumLambdaError, oracleError);
                        if (oracleError > tolerance) AddError(ref report, "lambda-equation " + key, ErrorKind.Lambda);
                    }
                    else
                    {
                        PositionResult oracleResult = EvaluatePosition(ref oracle, parametersA, parametersB, contact.Separation,
                            contact.Feature == UInt32.MaxValue ? GrainPositionTarget : SolidPositionTarget);
                        double oracleError = Math.Max(Math.Abs(current.Position - oracleResult.PositionLambda),
                            (impulse - oracleResult.Correction).Length);
                        report.MaximumLambdaError = Math.Max(report.MaximumLambdaError, oracleError);
                        if (oracleError > tolerance) AddError(ref report, "position-equation " + key, ErrorKind.Lambda);
                    }
                }
            }
            if (checkpoint.Stage == ProofTraceStage.VelocityEvaluated) { prior.Normal = current.Normal; prior.Tangent = current.Tangent; }
            if (checkpoint.Stage == ProofTraceStage.PositionEvaluated) prior.Position = current.Position;
            previous[key] = prior;
        }

        static BodyParameters[] ConvertParameters(Debris.Simulation.ParallelProof.BodyParameters[] source)
        {
            if (source == null) return null;
            var result = new BodyParameters[source.Length];
            for (int i = 0; i < source.Length; i++) result[i] = new BodyParameters(source[i].InverseMass, source[i].InverseInertia);
            return result;
        }

        static string StableKey(ProofTraceContact contact)
        {
            uint a = contact.IdentityA == 0 ? contact.A : contact.IdentityA;
            uint b = contact.IdentityB == 0 ? contact.B : contact.IdentityB;
            return $"{a}:{b}:feature={contact.Feature}";
        }

        static void CompareGeometry(ReferenceObb a, ReferenceObb b, ProofTraceContact actual, ProofTraceState stateA,
            ProofTraceState stateB, double tolerance, string key, ref TraceReplayReport report)
        {
            var best = OBBContacts(a, b);
            Double2 actualNormal = new Double2(actual.Normal.x, actual.Normal.y);
            Double2[] axes = { Axis(a, 0), Axis(a, 1), Axis(b, 0), Axis(b, 1) };
            int selected = 0; double closest = double.PositiveInfinity;
            for (int i = 0; i < axes.Length; i++)
            {
                var oriented = Double2.Dot(b.Center - a.Center, axes[i]) >= 0 ? axes[i] : -axes[i];
                double distance = (oriented - actualNormal).Length;
                if (distance < closest) { closest = distance; selected = i; }
            }
            // Near-identical endpoint angles can round to the same float normal.
            // That normal alone cannot recover which box owned the reference
            // face. Independently clip each admissible axis/owner, require both
            // SAT maximality and matching arms, and report the ambiguity.
            int closestAxis = selected;
            double bestError = double.PositiveInfinity, separationError = 0, normalError = 0, armError = 0, loss = 0;
            for (int i = 0; i < axes.Length; i++)
            {
                double separation = AxisSeparation(a, b, axes[i]);
                var candidate = ContactOnAxis(a, b, i, axes[i], separation);
                double candidateLoss = Math.Max(0, best.Contact0.Separation - separation);
                double candidateSeparation = Math.Abs(candidate.Separation - actual.Separation);
                double candidateNormal = (candidate.Normal - actualNormal).Length;
                double candidateArm = Math.Max((candidate.Point - new Double2(stateA.Center.x, stateA.Center.y) - new Double2(actual.ArmA.x, actual.ArmA.y)).Length,
                    (candidate.Point - new Double2(stateB.Center.x, stateB.Center.y) - new Double2(actual.ArmB.x, actual.ArmB.y)).Length);
                double error = Math.Max(Math.Max(candidateLoss, candidateSeparation), Math.Max(candidateNormal, candidateArm));
                if (error >= bestError) continue;
                bestError = error; selected = i; loss = candidateLoss;
                separationError = candidateSeparation; normalError = candidateNormal; armError = candidateArm;
            }
            if (selected != closestAxis) report.AmbiguousReferenceFaces++;
            double winnerDifference = (best.Contact0.Normal - actualNormal).Length;
            if (winnerDifference > tolerance) report.AxisChoiceDifferences++;
            report.MaximumWinnerNormalDifference = Math.Max(report.MaximumWinnerNormalDifference, winnerDifference);
            report.MaximumAxisSeparationLoss = Math.Max(report.MaximumAxisSeparationLoss, loss);
            report.MaximumSeparationError = Math.Max(report.MaximumSeparationError, separationError);
            report.MaximumNormalError = Math.Max(report.MaximumNormalError, normalError);
            report.MaximumArmError = Math.Max(report.MaximumArmError, armError);
            if (!best.Valid || loss > tolerance || separationError > tolerance || normalError > tolerance || armError > tolerance)
                AddError(ref report, "geometry " + key, ErrorKind.Geometry);
        }

        static void AddError(ref TraceReplayReport report, string error, ErrorKind kind)
        {
            report.Valid = false;
            switch (kind)
            {
                case ErrorKind.Geometry: report.GeometryMismatches++; break;
                case ErrorKind.Mass: report.MassMismatches++; break;
                case ErrorKind.Degree: report.DegreeMismatches++; break;
                case ErrorKind.Lambda: report.LambdaMismatches++; break;
                default: report.ReductionMismatches++; break;
            }
            if (String.IsNullOrEmpty(report.FirstError)) report.FirstError = error;
        }

        static string CommandLineValue(string key)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == key) return args[i + 1];
            return null;
        }

        static double Split(uint degree, BodyParameters parameters, Double2 arm, Double2 normal)
        {
            return Math.Max(1u, degree) * EffectiveMass(parameters, arm, normal);
        }

        static Double2 Axis(ReferenceObb box, int index)
        {
            Double2 x = Double2.Rotate(Double2.Right, box.Angle);
            return index == 0 || index == 2 ? x : Double2.Perpendicular(x);
        }

        static double AxisSeparation(ReferenceObb a, ReferenceObb b, Double2 axis)
        {
            // Project actual vertices rather than using the support-radius
            // identity in the shader.  This keeps the oracle algebraically
            // independent while producing the same interval separation.
            Double2[] verticesA = Vertices(a), verticesB = Vertices(b);
            double minA, maxA, minB, maxB;
            Project(verticesA, axis, out minA, out maxA);
            Project(verticesB, axis, out minB, out maxB);
            return Math.Max(minA - maxB, minB - maxA);
        }

        static double Radius(ReferenceObb box, Double2 axis)
        {
            Double2[] vertices = Vertices(box);
            double radius = 0;
            for (int i = 0; i < vertices.Length; i++)
                radius = Math.Max(radius, Math.Abs(Double2.Dot(vertices[i] - box.Center, axis)));
            return radius;
        }

        static ContactPoint ContactOnAxis(ReferenceObb a, ReferenceObb b, int axisIndex, Double2 axis, double separation)
        {
            Double2 delta = b.Center - a.Center;
            Double2 normal = Double2.Dot(delta, axis) >= 0 ? axis : -axis;
            bool referenceA = axisIndex < 2;
            ReferenceObb reference = referenceA ? a : b;
            ReferenceObb incident = referenceA ? b : a;
            Double2 referenceCenter = reference.Center;
            Double2 incidentCenter = incident.Center;
            Double2 referenceNormal = referenceA ? normal : -normal;
            Double2 referenceX = Axis(reference, 0), referenceY = Axis(reference, 1);
            Double2 incidentX = Axis(incident, 0), incidentY = Axis(incident, 1);
            bool faceX = Math.Abs(Double2.Dot(referenceNormal, referenceX)) >= Math.Abs(Double2.Dot(referenceNormal, referenceY));
            Double2 tangent = faceX ? referenceY : referenceX;
            double side = faceX ? reference.HalfSize.y : reference.HalfSize.x;
            Double2 faceCenter = referenceCenter + referenceNormal * (faceX ? reference.HalfSize.x : reference.HalfSize.y);
            bool incidentXFace = Math.Abs(Double2.Dot(referenceNormal, incidentX)) >= Math.Abs(Double2.Dot(referenceNormal, incidentY));
            Double2 incidentNormal = incidentXFace ? incidentX : incidentY;
            if (Double2.Dot(incidentNormal, referenceNormal) > 0) incidentNormal = -incidentNormal;
            Double2 incidentTangent = incidentXFace ? incidentY : incidentX;
            double incidentHalf = incidentXFace ? incident.HalfSize.y : incident.HalfSize.x;
            Double2 edgeCenter = incidentCenter + incidentNormal * (incidentXFace ? incident.HalfSize.x : incident.HalfSize.y);
            Double2 p = edgeCenter - incidentTangent * incidentHalf;
            Double2 q = edgeCenter + incidentTangent * incidentHalf;
            Double2 point;
            Double2 clippedP = p, clippedQ = q;
            bool clipped = ClipHalfPlane(ref clippedP, ref clippedQ, faceCenter + tangent * side, tangent)
                && ClipHalfPlane(ref clippedP, ref clippedQ, faceCenter - tangent * side, -tangent);
            if (clipped)
            {
                point = (clippedP + clippedQ) * .5;
            }
            else
            {
                double u = Double2.Dot(p - faceCenter, tangent), v = Double2.Dot(q - faceCenter, tangent);
                double cu = Clamp(u, -side, side), cv = Clamp(v, -side, side);
                point = Math.Abs(cu - u) <= Math.Abs(cv - v) ? p : q;
            }
            point -= referenceNormal * Double2.Dot(point - faceCenter, referenceNormal) * .5;
            return new ContactPoint(normal, separation, point);
        }

        static Double2 Lerp(Double2 a, Double2 b, double t) => a + (b - a) * t;
        static double Clamp(double value, double min, double max) => Math.Max(min, Math.Min(max, value));

        static Double2[] Vertices(ReferenceObb box)
        {
            Double2 x = Axis(box, 0), y = Axis(box, 1);
            return new[]
            {
                box.Center + x * box.HalfSize.x + y * box.HalfSize.y,
                box.Center + x * box.HalfSize.x - y * box.HalfSize.y,
                box.Center - x * box.HalfSize.x - y * box.HalfSize.y,
                box.Center - x * box.HalfSize.x + y * box.HalfSize.y
            };
        }

        static void Project(Double2[] vertices, Double2 axis, out double min, out double max)
        {
            min = max = Double2.Dot(vertices[0], axis);
            for (int i = 1; i < vertices.Length; i++)
            {
                double value = Double2.Dot(vertices[i], axis);
                min = Math.Min(min, value); max = Math.Max(max, value);
            }
        }

        // Clip a segment against dot(point - planePoint, planeNormal) <= 0.
        // Applying this twice gives the reference-face tangent slab without
        // copying the shader's scalar t0/t1 interval implementation.
        static bool ClipHalfPlane(ref Double2 p, ref Double2 q, Double2 planePoint, Double2 planeNormal)
        {
            double dp = Double2.Dot(p - planePoint, planeNormal);
            double dq = Double2.Dot(q - planePoint, planeNormal);
            bool keepP = dp <= 0, keepQ = dq <= 0;
            if (!keepP && !keepQ) return false;
            if (keepP && keepQ) return true;
            double denominator = dp - dq;
            Double2 hit = Math.Abs(denominator) > Epsilon ? p + (q - p) * (dp / denominator) : p;
            if (!keepP) p = hit; else q = hit;
            return true;
        }
        static bool IsFiniteNumber(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        static bool FiniteBox(ReferenceObb box) => box.Center.IsFinite && IsFiniteNumber(box.Angle)
            && box.HalfSize.IsFinite && box.HalfSize.x > 0 && box.HalfSize.y > 0;

        static void ValidateBodies(BodyState[] states, BodyParameters[] parameters, BodyDelta[] reduced)
        {
            if (states == null || parameters == null || reduced == null || states.Length != parameters.Length || states.Length != reduced.Length)
                throw new ArgumentException("States, parameters and reduced adjacency must have equal non-null lengths.");
        }
    }
}
