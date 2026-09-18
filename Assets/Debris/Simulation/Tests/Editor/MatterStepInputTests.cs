using System;
using Debris.Simulation;
using NUnit.Framework;
using UnityEngine;

namespace Debris.Simulation.Tests
{
    public sealed class MatterStepInputTests
    {
        [Test]
        public void AcceptsForceAndToolStateWithoutPoseOrDisplacement()
        {
            var input = new MatterStepInput(null, 40, Vector2.zero, true, true, true, new Vector3(3, 4, 5));
            Assert.That(input.SuctionForce, Is.EqualTo(40));
            Assert.That(input.DoorRequestedOpen, Is.True);
            Assert.That(input.LocalForce, Is.EqualTo(new Vector3(3, 4, 5)));
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(-.01f)]
        public void RejectsInvalidSuctionForce(float force)
        {
            Assert.Throws<ArgumentException>(() => new MatterStepInput(null, force, Vector2.zero, false, false, false, Vector3.zero));
        }
    }
}
