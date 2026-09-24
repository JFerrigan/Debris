using System;
using UnityEngine;

namespace Debris.Simulation.ParallelProof
{
    // Revision 1: the locked 8,192-active-grain checkpoint layout.
    public static class ProofViabilityFixture
    {
        public const int GrainCount = 8192;
        public const int BayCount = 2500;
        public const int PileCount = 1000;
        public const int ExteriorCount = 4692;
        public const int FragmentCount = 16;
        public const int Revision = 1;

        public static ProofFixture Create()
        {
            var packed = ProofFixtures.Packed(true);
            var grains = new LooseCell[GrainCount];
            Array.Copy(packed.Grains, grains, BayCount);
            for (int y = 0; y < 25; y++)
            for (int x = 0; x < 40; x++)
            {
                int i = BayCount + y * 40 + x;
                grains[i] = new LooseCell { Center = new Vector2(-159.5f + x, 68 + y),
                    Velocity = Vector2.down, Material = 1, Identity = (uint)i + 1 };
            }
            for (int y = 0; y < 69; y++)
            for (int x = 0; x < 68; x++)
            {
                int i = BayCount + PileCount + y * 68 + x;
                grains[i] = new LooseCell { Center = new Vector2(100 + 2 * x, -140 + 2 * y),
                    Material = 1, Identity = (uint)i + 1 };
            }

            var bodies = new BodyState[18];
            var parameters = new BodyParameters[18];
            var boundaries = new Boundary[21];
            bodies[0] = packed.Bodies[0];
            parameters[0] = packed.Parameters[0];
            for (int i = 0; i < 4; i++)
            {
                boundaries[i] = packed.Boundaries[i];
                boundaries[i].Body = GrainCount;
            }
            for (int i = 0; i < FragmentCount; i++)
            {
                int body = i + 1;
                bodies[body] = new BodyState { Center = new Vector2(-158.5f + 2.5f * i, 95),
                    Velocity = new Vector2(0, -2) };
                parameters[body] = new BodyParameters { InverseMass = .25f, InverseInertia = 1.5f,
                    BoundaryStart = (uint)(4 + i), BoundaryCount = 1, Mobility = 1, ShapeRevision = 1 };
                boundaries[4 + i] = new Boundary { HalfSize = Vector2.one * .5f,
                    Body = (uint)(GrainCount + body), Feature = (uint)(4 + i) };
            }
            parameters[17] = new BodyParameters { BoundaryStart = 20, BoundaryCount = 1,
                Mobility = 0, ShapeRevision = 1 };
            boundaries[20] = new Boundary { Center = new Vector2(-140, 67),
                HalfSize = new Vector2(21, .5f), Body = GrainCount + 17, Feature = 20 };
            return new ProofFixture { Grains = grains, Bodies = bodies,
                Parameters = parameters, Boundaries = boundaries };
        }
    }
}
