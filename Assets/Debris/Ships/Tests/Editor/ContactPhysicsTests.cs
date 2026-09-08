using Debris.Materials;
using NUnit.Framework;
using UnityEngine;
namespace Debris.Ships.Tests
{
    public sealed class ContactPhysicsTests
    {
        [Test] public void InelasticImpactConservesMomentumAndMatchesMassRatio()
        {
            var ship=new BodyMass{Mass=10000,Inertia=10000};var cell=new BodyMass{Mass=1,Inertia=1};
            float j=ContactPhysics.Impulse(ship,cell,Vector2.right*10,0,Vector2.zero,0,Vector2.zero,Vector2.zero,Vector2.right);
            float a=10-j/ship.Mass,b=j;
            Assert.That(a,Is.EqualTo(100000f/10001).Within(.000002));Assert.That(b,Is.EqualTo(a).Within(.000002));
            Assert.That(a*ship.Mass+b,Is.EqualTo(100000).Within(.01));
            Assert.That(.5f*ship.Mass*a*a+.5f*b*b,Is.LessThanOrEqualTo(500000));
        }
        [Test] public void AnchoredGlancingContactPreservesTangentAndOffCenterImpactTurnsBody()
        {
            var body=new BodyMass{Mass=10,Inertia=20};var anchor=new BodyMass{Mass=1,Inertia=1,Mobility=BodyMobility.Anchored};
            float j=ContactPhysics.Impulse(body,anchor,new Vector2(3,4),0,Vector2.zero,0,Vector2.zero,Vector2.zero,Vector2.right);
            var velocity=new Vector2(3,4)-Vector2.right*j/body.Mass;
            Assert.That(velocity,Is.EqualTo(new Vector2(0,4)));Assert.That(anchor.InverseMass,Is.Zero);
            j=ContactPhysics.Impulse(body,anchor,new Vector2(3,4),0,Vector2.zero,0,Vector2.up,Vector2.zero,Vector2.right);
            Assert.That(j,Is.EqualTo(20).Within(.00001));Assert.That(-ContactPhysics.Cross(Vector2.up,Vector2.right*j)/body.Inertia,Is.EqualTo(1));
            Assert.That(ContactPhysics.Impulse(body,anchor,Vector2.left,0,Vector2.zero,0,Vector2.zero,Vector2.zero,Vector2.right),Is.Zero);
        }
        [Test] public void AttachedMassUsesDensityWholeUnitsAndFuelButExcludesFreeCargo()
        {
            var blueprint=ShipBlueprint.Starter(2);var catalog=Resources.Load<MaterialCatalog>("Materials");
            try
            {
                var ship=new ShipRuntime(blueprint);var body=ship.MassProperties(catalog);
                Assert.That(body.Mass,Is.EqualTo(ship.Structure.Count*catalog.DefinitionAt(2).Density+7*20+ship.Fuel.Count*catalog.DefinitionAt(catalog.IndexOf("fuel-standard")).Density).Within(.01));
                ship.CargoMass=5000;Assert.That(ship.MassProperties(catalog).Mass,Is.EqualTo(body.Mass));
                Assert.That(ship.MassProperties(catalog).Inertia,Is.EqualTo(body.Inertia));
                var velocity=ContactPhysics.Accelerate(body,Vector3.zero,new Vector3(body.Mass,0,body.Inertia),.1f);
                Assert.That(velocity.x,Is.EqualTo(.1f).Within(.000001));Assert.That(velocity.z,Is.EqualTo(.1f).Within(.000001));
                var fuelMass=ship.MassProperties(catalog).Mass;
                ship.Fuel.Contents.RemoveAt(0);ship.Fuel.Add("dense",1);
                Assert.That(ship.MassProperties(catalog).Mass-fuelMass,Is.EqualTo(catalog.DefinitionAt(catalog.IndexOf("fuel-dense")).Density-catalog.DefinitionAt(catalog.IndexOf("fuel-standard")).Density).Within(.001));
                var force=ship.FlightForce(Vector2.right,0,1f/60,body);Assert.That(force.x,Is.GreaterThan(0));Assert.That(ship.Velocity,Is.EqualTo(Vector2.zero));
            }
            finally{Object.DestroyImmediate(blueprint);}
        }
    }
}
