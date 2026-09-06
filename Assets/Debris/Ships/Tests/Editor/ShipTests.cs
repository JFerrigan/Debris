using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
namespace Debris.Ships.Tests
{
    public sealed class ShipTests
    {
        [Test] public void StarterBlueprintHasRealCavityAndWholeMachinery()
        {
            var b=ShipBlueprint.Starter(2);
            try{Assert.DoesNotThrow(b.Validate);Assert.That(b.CargoCavity.width*b.CargoCavity.height,Is.EqualTo(2500));Assert.That(b.Units.Count,Is.EqualTo(7));Assert.That(b.Units.Count(u=>u.Definition.Kind==UnitKind.Thruster),Is.EqualTo(2));Assert.Throws<InvalidOperationException>(()=>b.DrawCell(0,0,2));Assert.Throws<InvalidOperationException>(()=>b.DrawCell(30,0,2));}
            finally{UnityEngine.Object.DestroyImmediate(b);}
        }
        [Test] public void FailedPrefabPlacementIsAtomic()
        {
            var b=ShipBlueprint.Starter(2);int count=b.Structure.Count;
            try{Assert.Throws<InvalidOperationException>(()=>b.PlacePrefab(new[]{new StructuralCell(100,100,2),new StructuralCell(0,0,2)},Vector2Int.zero));Assert.That(b.Structure.Count,Is.EqualTo(count));}finally{UnityEngine.Object.DestroyImmediate(b);}
        }
        [Test] public void CargoMassReducesAccelerationAndFuelCannotUnderflow()
        {
            var b=ShipBlueprint.Starter(2);
            try
            {
                var light=new ShipRuntime(b);var heavy=new ShipRuntime(b){CargoMass=5000};
                light.Tick(Vector2.right,1,1f/60);heavy.Tick(Vector2.right,1,1f/60);
                Assert.That(light.Velocity.x,Is.GreaterThan(heavy.Velocity.x));Assert.That(light.AngularVelocity,Is.GreaterThan(heavy.AngularVelocity));
                var fuel=new TankInventory{Capacity=2};Assert.That(fuel.Add("dense",2),Is.True);Assert.That(fuel.Add("low",1),Is.False);Assert.That(fuel.Consume(8.1),Is.False);Assert.That(fuel.Energy,Is.EqualTo(8));Assert.That(fuel.Consume(7.5),Is.True);Assert.That(fuel.Energy,Is.EqualTo(.5));Assert.That(fuel.Consume(.5),Is.True);Assert.That(fuel.Count,Is.Zero);
            }finally{UnityEngine.Object.DestroyImmediate(b);}
        }
        [Test] public void DetachedStructurePersistsAndSeveredThrusterStops()
        {
            var b=ShipBlueprint.Starter(2);
            try
            {
                var ship=new ShipRuntime(b);int count=ship.Structure.Count;
                for(int x=-40;x<25;x++)ship.RemoveHull(new Vector2Int(x,25));
                // Cut remaining two cells of the upper rail at its attachment to the front spine.
                ship.RemoveHull(new Vector2Int(24,26));ship.RemoveHull(new Vector2Int(24,27));
                Assert.That(ship.Fragments.Count,Is.GreaterThan(0));Assert.That(ship.Units.Single(u=>u.Placement.Definition.Key=="upper-thruster").Operational,Is.False);
                Assert.That(ship.Structure.Count+ship.Fragments.Sum(f=>f.Cells.Count),Is.EqualTo(count-67));
            }finally{UnityEngine.Object.DestroyImmediate(b);}
        }
        [Test] public void LostCommandPreservesInertiaButDisablesControl()
        {
            var b=ShipBlueprint.Starter(2);
            try
            {
                var ship=new ShipRuntime(b){Velocity=new Vector2(3,4),AngularVelocity=.2f};
                ship.DamageUnit(ship.Units.Single(u=>u.Placement.Definition.Kind==UnitKind.Command).Placement.Id,100);
                var before=ship.Position;var fuel=ship.Fuel.Energy;
                ship.Tick(Vector2.right,1,.1f);
                Assert.That(Vector2.Distance(ship.Position,before+new Vector2(.3f,.4f)),Is.LessThan(.0001));
                Assert.That(ship.Angle,Is.EqualTo(.02f).Within(.00001));Assert.That(ship.Fuel.Energy,Is.EqualTo(fuel));
                Assert.That(ship.Has(UnitKind.Drill),Is.False);
            }finally{UnityEngine.Object.DestroyImmediate(b);}
        }
        [Test] public void DestroyedTankStopsFuelSupplyAndInvalidTimeCannotPoisonMotion()
        {
            var b=ShipBlueprint.Starter(2);
            try
            {
                var ship=new ShipRuntime(b);
                ship.DamageUnit(ship.Units.Single(u=>u.Placement.Definition.Kind==UnitKind.Tank).Placement.Id,100);
                ship.Tick(Vector2.right,1,.1f);Assert.That(ship.Velocity,Is.EqualTo(Vector2.zero));
                Assert.Throws<ArgumentOutOfRangeException>(()=>ship.Tick(Vector2.right,0,float.NaN));
            }finally{UnityEngine.Object.DestroyImmediate(b);}
        }
        [Test] public void RotatingTransformRoundTripsWithoutScalingCells()
        {
            var b=ShipBlueprint.Starter(2);try{var ship=new ShipRuntime(b);for(int i=0;i<36;i++){ship.Angle=i*Mathf.PI/18;var p=new Vector2(12.4f,-8.2f);Assert.That(Vector2.Distance(ship.ToLocal(ship.ToWorld(p)),p),Is.LessThan(.0001));Assert.That(Vector2.Distance(ship.ToWorld(p),ship.ToWorld(p+Vector2.right)),Is.EqualTo(1).Within(.0001));}}finally{UnityEngine.Object.DestroyImmediate(b);}
        }
    }
}
