using System;
using System.Collections;
using Debris.Materials;
using Debris.Ships;
using Debris.Sites;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
namespace Debris.Simulation.Tests
{
    public sealed class ContactMatterTests
    {
        [UnityTest] public IEnumerator StarterOffCenterContactDoesNotRejectPose()
        {
            var catalog=Resources.Load<MaterialCatalog>("Materials");var ship=new ShipRuntime(Resources.Load<ShipBlueprint>("StarterShip"));
            using(var session=new MatterSession(catalog,Resources.Load<AsteroidProfile>("Asteroid"),4,128,32))
            {
                session.ConfigureShip(ship.CollisionMask(),ship.Position);var task=session.SnapshotAsync();while(!task.IsCompleted)yield return null;
                var state=task.Result;foreach(var f in state.Fields)Array.Clear(f,0,f.Length);foreach(var d in state.Damage)Array.Clear(d,0,d.Length);
                state.Cells=new[]{new LooseCell{Position=ship.Position+new Vector2(55,0),Material=1,Identity=1,Flags=1}};state.NextIdentity=2;state.Counters=new uint[]{1,1,0,0};
                state.ShipPose[1]=new Vector4(10,0,0,0);session.Restore(state);session.ConfigureShipBody(ship.MassProperties(catalog));
                session.Step();task=session.SnapshotAsync();while(!task.IsCompleted)yield return null;state=task.Result;
                Assert.That(state.ContactStats[1],Is.Zero,$"pose={state.ShipPose[0]:F8} motion={state.ShipPose[1]:F8} cell={state.Cells[0].Position:F8} velocity={state.Cells[0].Velocity:F8} hull_contact=({state.Impact[0]-64},{state.Impact[1]-64})");
                Assert.DoesNotThrow(()=>CpuCutReference.ValidateShipPlacement(state,ship.Id));
                for(int i=0;i<120;i++)session.Step(shipForce:new Vector3(ship.MassProperties(catalog).Mass*.2f,0,0));
                task=session.SnapshotAsync();while(!task.IsCompleted)yield return null;
                Assert.That(task.Result.ContactStats[1],Is.Zero,"Sustained off-centre contact");
                Assert.DoesNotThrow(()=>CpuCutReference.ValidateShipPlacement(task.Result,ship.Id));
            }
        }
        [UnityTest] public IEnumerator HeavyShipPushesSleepingCellAndContinuesUnderForce()
        {
            var catalog=Resources.Load<MaterialCatalog>("Materials");
            using(var session=new MatterSession(catalog,Resources.Load<AsteroidProfile>("Asteroid"),2,128,32))
            {
                var hull=new uint[16384];hull[64*128+64]=2;session.ConfigureShip(hull,Vector2.zero);
                var task=session.SnapshotAsync();while(!task.IsCompleted)yield return null;Assert.That(task.IsFaulted,Is.False);
                var state=task.Result;foreach(var f in state.Fields)Array.Clear(f,0,f.Length);foreach(var d in state.Damage)Array.Clear(d,0,d.Length);
                state.Cells=new[]{new LooseCell{Position=Vector2.right,Material=1,Identity=1,Flags=1}};state.NextIdentity=2;state.Counters=new uint[]{1,1,0,0};
                state.ShipPose[1]=new Vector4(10,0,0,0);session.Restore(state);
                session.ConfigureShipBody(new BodyMass{Mass=10000,Inertia=10000,Center=Vector2.one*.5f});
                session.Step();task=session.SnapshotAsync();while(!task.IsCompleted)yield return null;Assert.That(task.IsFaulted,Is.False,task.Exception?.ToString());
                var result=task.Result;float m=catalog.DefinitionAt(1).Density,expected=100000/(10000+m);
                Assert.That(result.ShipPose[1].x,Is.EqualTo(expected).Within(.0001));Assert.That(result.Cells[0].Velocity.x,Is.EqualTo(expected).Within(.0001));
                Assert.That(result.ShipPose[1].x*10000+result.Cells[0].Velocity.x*m,Is.EqualTo(100000).Within(.1));
                Assert.That(result.ContactStats[0],Is.GreaterThan(0));Assert.That(result.ContactStats[1],Is.Zero,"Ordinary pixel must not invoke pose fallback");
                Assert.That(result.Cells[0].Flags,Is.Zero);Assert.That(result.Cells[0].Position.x,Is.GreaterThan(1));Assert.DoesNotThrow(()=>CpuCutReference.ValidateShipPlacement(result,"00000000000000000000000000000001"));
                for(int i=0;i<30;i++)session.Step(shipForce:new Vector3(10000,0,0));
                task=session.SnapshotAsync();while(!task.IsCompleted)yield return null;result=task.Result;
                Assert.That(result.ShipPose[1].x,Is.GreaterThan(expected+.49f));Assert.That(result.ContactStats[1],Is.Zero);
                Assert.That(result.Cells.Length,Is.EqualTo(1));Assert.DoesNotThrow(()=>CpuCutReference.ValidateShipPlacement(result,"00000000000000000000000000000001"));
                // Restore at a completed step, rebuild mass from the owning ship, retain authoritative motion.
                session.Restore(result);session.ConfigureShipBody(new BodyMass{Mass=10000,Inertia=10000,Center=Vector2.one*.5f});session.Step();
                task=session.SnapshotAsync();while(!task.IsCompleted)yield return null;
                Assert.That(task.Result.ShipPose[1].x,Is.EqualTo(result.ShipPose[1].x).Within(.0001));Assert.DoesNotThrow(()=>CpuCutReference.Validate(task.Result));
            }
        }
    }
}
