using System.Collections;
using System.Linq;
using Debris.Materials;
using Debris.Ships;
using Debris.Sites;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Debris.Simulation.Tests
{
    public sealed class CandidateStarterLayoutTests
    {
        [UnityTest, Timeout(120000)] public IEnumerator CandidateLayoutIsDeterministicLooseAndUnique()
        {
            var catalog=Resources.Load<MaterialCatalog>("Materials"); var profile=Resources.Load<AsteroidProfile>("Asteroid"); var ship=new ShipRuntime(Resources.Load<ShipBlueprint>("StarterShip"));
            using(var session=new MatterSession(catalog,profile,4,128,8192))
            {
                session.ConfigureShip(ship.CollisionMask(),ship.Position);session.ConfigureShipBody(ship.MassProperties(catalog));var read=session.SnapshotAsync();while(!read.IsCompleted)yield return null;
                var first=CandidateStarterLayout.Add(read.Result,ship,catalog);var repeat=session.SnapshotAsync();while(!repeat.IsCompleted)yield return null;var second=CandidateStarterLayout.Add(repeat.Result,ship,catalog);
                Assert.That(first.Cells,Has.Length.EqualTo(CandidateStarterLayout.GrainCount));Assert.That(first.Cells.Select(c=>c.Identity).Distinct().Count(),Is.EqualTo(CandidateStarterLayout.GrainCount));
                Assert.That(first.Cells.All(c=>c.Velocity==Vector2.zero&&(c.Flags&1)!=0&&c.Material!=0),Is.True);
                CollectionAssert.AreEqual(first.Cells.Select(c=>c.Position),second.Cells.Select(c=>c.Position));
            }
        }
    }
}
