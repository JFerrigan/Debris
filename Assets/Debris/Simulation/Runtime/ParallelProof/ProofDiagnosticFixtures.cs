using UnityEngine;

namespace Debris.Simulation.ParallelProof
{
    // Motion channels used to isolate the packed contact reproduction. The fixture
    // geometry, mass, density, and solver profile are always inherited from Packed.
    public enum PackedDiagnosticMotion
    {
        Stationary,
        TranslationOnly,
        RotationOnly,
        Combined
    }

    public static class ProofDiagnosticFixtures
    {
        public static ProofFixture Packed(bool sharedMotion, PackedDiagnosticMotion motion)
        {
            if ((uint)motion > (uint)PackedDiagnosticMotion.Combined)
                throw new System.ArgumentOutOfRangeException(nameof(motion));

            var source = ProofFixtures.Packed(sharedMotion);
            var fixture = Clone(source);

            // Combined is deliberately an untouched clone. This protects the
            // canonical initial values from algebraic recombination differences.
            if (motion == PackedDiagnosticMotion.Combined)
                return fixture;

            bool translation = motion == PackedDiagnosticMotion.TranslationOnly;
            bool rotation = motion == PackedDiagnosticMotion.RotationOnly;

            if (sharedMotion)
            {
                var body = source.Bodies[0];
                var linear = body.Velocity;
                var spin = body.AngularVelocity;

                fixture.Bodies[0].Velocity = translation ? linear : Vector2.zero;
                fixture.Bodies[0].AngularVelocity = rotation ? spin : 0;
                for (int i = 0; i < fixture.Grains.Length; i++)
                {
                    var grain = source.Grains[i];
                    var offset = grain.Center - body.Center;
                    var rotationalVelocity = new Vector2(-spin * offset.y, spin * offset.x);
                    fixture.Grains[i].Velocity = (translation ? linear : Vector2.zero) +
                        (rotation ? rotationalVelocity : Vector2.zero);
                    fixture.Grains[i].AngularVelocity = rotation ? grain.AngularVelocity : 0;
                }
            }
            else
            {
                // The resting canonical case carries its isolated channels as
                // applied force and torque rather than initial velocity.
                fixture.Force = translation ? source.Force : Vector2.zero;
                fixture.Torque = rotation ? source.Torque : 0;
            }

            return fixture;
        }

        static ProofFixture Clone(ProofFixture source)
        {
            return new ProofFixture
            {
                Grains = (LooseCell[])source.Grains.Clone(),
                Bodies = (BodyState[])source.Bodies.Clone(),
                Parameters = (BodyParameters[])source.Parameters.Clone(),
                Boundaries = (Boundary[])source.Boundaries.Clone(),
                Force = source.Force,
                Torque = source.Torque
            };
        }
    }
}
