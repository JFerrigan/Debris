using System.Collections;
using Debris.Materials;
using Debris.Ships;
using Debris.Sites;
using Debris.Simulation.ParallelProof;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Debris.Simulation.Tests
{
    public sealed class ParallelStartupTests
    {
        [UnityTest,Timeout(120000)]
        public IEnumerator GeneratedStartupWorldAcknowledgesEmptyCargoAndFlight()
        {
            var catalog=Resources.Load<MaterialCatalog>("Materials");
            var ship=new ShipRuntime(Resources.Load<ShipBlueprint>("StarterShip"));
            using(var source=new MatterSession(catalog,Resources.Load<AsteroidProfile>("Asteroid"),4,128,8192))
            {
                source.ConfigureShip(ship.CollisionMask(),ship.Position);
                source.ConfigureShipBody(ship.MassProperties(catalog));
                var read=source.SnapshotAsync();while(!read.IsCompleted)yield return null;
                Assert.That(read.Result.Cells,Is.Empty);
                using(var candidate=ParallelGameplaySession.Import(read.Result,ship,catalog))
                {
                    for(uint tick=1;tick<=3;tick++)
                    {
                        var input=new MatterStepInput(null,0,default,false,false,false,new Vector3(ship.MassProperties(catalog).Mass,0,0));
                        Assert.That(candidate.Submit(tick,input),Is.True);
                        ParallelGameplaySession.Completion completion;
                        while(!candidate.TryAcknowledge(out completion))yield return null;
                        Assert.That(candidate.Faulted,Is.False,candidate.Fault);
                        Assert.That(completion.Fault,Is.EqualTo(SolverFault.None));
                        Assert.That(completion.Tick,Is.EqualTo(tick));
                        Assert.That(completion.ShipVelocity.x,Is.GreaterThan(0));
                    }
                }
            }
        }
        [UnityTest,Timeout(120000)]
        public IEnumerator CompletionRingRejectsSaturationWithoutAddingFuelReservation()
        {
            var catalog=Resources.Load<MaterialCatalog>("Materials");var ship=new ShipRuntime(Resources.Load<ShipBlueprint>("StarterShip"));
            using(var source=new MatterSession(catalog,Resources.Load<AsteroidProfile>("Asteroid"),4,128,8192))
            {
                source.ConfigureShip(ship.CollisionMask(),ship.Position);source.ConfigureShipBody(ship.MassProperties(catalog));var read=source.SnapshotAsync();while(!read.IsCompleted)yield return null;
                using(var candidate=ParallelGameplaySession.Import(read.Result,ship,catalog))
                {
                    var input=new MatterStepInput(null,0,default,false,false,false,Vector3.zero);
                    for(uint tick=1;tick<=ParallelGameplaySession.MaximumPendingTicks;tick++)Assert.That(candidate.Submit(tick,input,.25),Is.True);
                    Assert.That(candidate.PendingTicks,Is.EqualTo(ParallelGameplaySession.MaximumPendingTicks));Assert.That(candidate.ProvisionalFuel,Is.EqualTo(4).Within(.000001));
                    Assert.That(candidate.Submit(17,input,.25),Is.False);Assert.That(candidate.PendingTicks,Is.EqualTo(ParallelGameplaySession.MaximumPendingTicks));Assert.That(candidate.ProvisionalFuel,Is.EqualTo(4).Within(.000001));
                }
            }
        }
    }
}
