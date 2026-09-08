using System;
using System.Collections;
using System.Collections.Generic;
using Debris.Materials;
using Debris.Ships;
using Debris.Sites;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
namespace Debris.Simulation.Tests
{
    public sealed class ContactIslandTests
    {
        static MatterSession Session(int capacity=1024)=>new MatterSession(Resources.Load<MaterialCatalog>("Materials"),Resources.Load<AsteroidProfile>("Asteroid"),2,128,capacity);
        static void Empty(MatterSnapshot s,LooseCell[] cells)
        {
            foreach(var f in s.Fields)Array.Clear(f,0,f.Length);foreach(var f in s.Damage)Array.Clear(f,0,f.Length);
            s.Cells=cells;s.NextIdentity=(uint)cells.Length+1;s.Counters=new[]{(uint)cells.Length,(uint)cells.Length,0u,0u};Array.Clear(s.Dirty,0,s.Dirty.Length);
        }
        [UnityTest] public IEnumerator CellPairExchangesEqualOppositeMomentum()
        {
            using(var session=Session(16))
            {
                var hull=new uint[16384];hull[64*128+64]=2;session.ConfigureShip(hull,new Vector2(-50,0));
                var task=session.SnapshotAsync();while(!task.IsCompleted)yield return null;var state=task.Result;
                Empty(state,new[]{new LooseCell{Material=1,Identity=1,Position=Vector2.zero,Velocity=Vector2.right*10},new LooseCell{Material=1,Identity=2,Position=Vector2.right,Flags=1}});
                session.Restore(state);session.ConfigureShipBody(new BodyMass{Mass=10000,Inertia=10000,Center=Vector2.one*.5f});
                session.Step();task=session.SnapshotAsync();while(!task.IsCompleted)yield return null;state=task.Result;
                Assert.That(state.Cells[0].Velocity.x,Is.EqualTo(5).Within(.0001));Assert.That(state.Cells[1].Velocity.x,Is.EqualTo(5).Within(.0001));
                Assert.That(state.Cells[0].Position.x,Is.GreaterThan(.08));Assert.That(state.Cells[1].Position.x,Is.GreaterThan(1.08));
                Assert.DoesNotThrow(()=>CpuCutReference.Validate(state));
            }
        }
        [UnityTest] public IEnumerator GlancingAnchorRemovesNormalVelocityAndReleasedChipIsDynamic()
        {
            using(var session=Session(16))
            {
                var hull=new uint[16384];hull[64*128+64]=2;session.ConfigureShip(hull,Vector2.zero);
                var task=session.SnapshotAsync();while(!task.IsCompleted)yield return null;var state=task.Result;Empty(state,Array.Empty<LooseCell>());
                // Anchored wall at x=1. The body can slide upward beside it.
                for(int y=-10;y<10;y++){int lx=1-state.OriginX,ly=y-state.OriginY;state.Fields[(ly/128)*2+lx/128][(ly%128)*128+lx%128]=1;state.Counters[1]++;}
                state.ShipPose[1]=new Vector4(10,3,0,0);session.Restore(state);session.ConfigureShipBody(new BodyMass{Mass=10000,Inertia=100000000,Center=Vector2.one*.5f});
                for(int step=0;step<10;step++)session.Step();task=session.SnapshotAsync();while(!task.IsCompleted)yield return null;state=task.Result;
                Assert.That(state.ShipPose[1].x,Is.EqualTo(0).Within(.001));Assert.That(state.ShipPose[1].y,Is.EqualTo(3).Within(.001));
                Assert.That(state.ShipPose[0].y,Is.GreaterThan(.45));Assert.That(state.Counters[1],Is.EqualTo(20));
                session.Step(new SiteCommand(SiteCommandType.CutterStroke,new Vector2(1.5f,5.5f),Vector2.right*5,.6f,600,1));
                task=session.SnapshotAsync();while(!task.IsCompleted)yield return null;state=task.Result;
                Assert.That(state.Cells.Length,Is.EqualTo(1));Assert.That(state.Cells[0].Velocity.x,Is.GreaterThan(4.9));Assert.That(state.Cells[0].Flags&1,Is.Zero);
                Assert.DoesNotThrow(()=>CpuCutReference.ValidateShipPlacement(state,"00000000000000000000000000000001"));
            }
        }
        [UnityTest] public IEnumerator FreeCargoFrameDoesNotInventMomentumOrTranslation()
        {
            var ship=new ShipRuntime(Resources.Load<ShipBlueprint>("StarterShip"));var catalog=Resources.Load<MaterialCatalog>("Materials");
            using(var session=Session(16))
            {
                session.ConfigureShip(ship.CollisionMask(),Vector2.zero);var task=session.SnapshotAsync();while(!task.IsCompleted)yield return null;var state=task.Result;
                Empty(state,new[]{new LooseCell{Position=Vector2.zero,Material=1,Identity=1,Flags=4}});
                state.ShipPose[1]=new Vector4(3,0,.1f,0);session.Restore(state);session.ConfigureShipBody(ship.MassProperties(catalog));
                for(int step=0;step<30;step++)session.Step();task=session.SnapshotAsync();while(!task.IsCompleted)yield return null;state=task.Result;
                Assert.That(state.Cells[0].Velocity.sqrMagnitude,Is.LessThan(.000001));
                Assert.That(Vector2.Distance(FuelTransfers.World(state,state.Cells[0].Position+Vector2.one*.5f),Vector2.one*.5f),Is.LessThan(.001));
                Assert.That(state.ShipPose[1].x,Is.EqualTo(3).Within(.00001));Assert.That(state.ShipPose[1].z,Is.EqualTo(.1).Within(.00001));
                Assert.DoesNotThrow(()=>CpuCutReference.ValidateShipPlacement(state,ship.Id));
            }
        }
        [UnityTest] public IEnumerator OffCenterCellImpactTurnsDynamicFragmentAndConservesMomentum()
        {
            using(var session=Session(16))
            {
                var hull=new uint[16384];hull[64*128+64]=2;session.ConfigureShip(hull,new Vector2(-50,0));
                var task=session.SnapshotAsync();while(!task.IsCompleted)yield return null;var state=task.Result;
                Empty(state,new[]{new LooseCell{Position=new Vector2(-1,3),Velocity=Vector2.right,Material=1,Identity=1}});
                var mask=new uint[16384];for(int y=0;y<4;y++)mask[(64+y)*128+64]=1;
                state.Fragments=new[]{new RigidFragmentSnapshot{Id="00000000000000000000000000000002",Hull=mask,Pose=new Vector4(0,0,0,1)}};
                session.Restore(state);session.ConfigureShipBody(new BodyMass{Mass=10000,Inertia=10000,Center=Vector2.one*.5f});session.Step();
                task=session.SnapshotAsync();while(!task.IsCompleted)yield return null;state=task.Result;var fragment=state.Fragments[0];
                Assert.That(fragment.Motion.x,Is.GreaterThan(.1));Assert.That(fragment.Motion.z,Is.LessThan(-.1));
                Assert.That(fragment.Mass.Mass*fragment.Motion.x+state.Cells[0].Velocity.x,Is.EqualTo(1).Within(.0001));
                Assert.That(.5f*fragment.Mass.Mass*(fragment.Motion.x*fragment.Motion.x+fragment.Motion.y*fragment.Motion.y)+.5f*fragment.Mass.Inertia*fragment.Motion.z*fragment.Motion.z+.5f*state.Cells[0].Velocity.sqrMagnitude,Is.LessThanOrEqualTo(.50001));
                Assert.DoesNotThrow(()=>CpuCutReference.Validate(state));
            }
        }
        [UnityTest,Timeout(120000)] public IEnumerator LoadedRotatingCavityCouplesCargoOnceWithoutCreatingEnergy()
        {
            var ship=new ShipRuntime(Resources.Load<ShipBlueprint>("StarterShip"));var catalog=Resources.Load<MaterialCatalog>("Materials");var mass=ship.MassProperties(catalog);
            using(var session=Session(128))
            {
                session.ConfigureShip(ship.CollisionMask(),Vector2.zero);var task=session.SnapshotAsync();while(!task.IsCompleted)yield return null;var state=task.Result;
                var cells=new List<LooseCell>();for(int y=-15;y<15;y+=3)for(int x=-15;x<15;x+=3)cells.Add(new LooseCell{Position=new Vector2(x,y),Material=1,Identity=(uint)cells.Count+1,Flags=4});
                Empty(state,cells.ToArray());state.ShipPose[1]=new Vector4(10,0,.06f,0);session.Restore(state);session.ConfigureShipBody(mass);
                for(int step=0;step<90;step++)session.Step();task=session.SnapshotAsync();while(!task.IsCompleted)yield return null;state=task.Result;
                double momentum=mass.Mass*state.ShipPose[1].x,energy=.5*mass.Mass*(state.ShipPose[1].x*state.ShipPose[1].x+state.ShipPose[1].y*state.ShipPose[1].y)+.5*mass.Inertia*state.ShipPose[1].z*state.ShipPose[1].z;
                foreach(var cell in state.Cells){momentum+=cell.Velocity.x;energy+=.5*cell.Velocity.sqrMagnitude;}
                Assert.That(momentum,Is.EqualTo(mass.Mass*10).Within(1));Assert.That(energy,Is.LessThanOrEqualTo(.5*mass.Mass*100+.5*mass.Inertia*.06*.06+1));
                Assert.That(state.Cells.Length,Is.EqualTo(100));Assert.That(state.ShipPose[1].x,Is.LessThan(9.99));
                Assert.DoesNotThrow(()=>CpuCutReference.ValidateShipPlacement(state,ship.Id));
            }
        }
        [UnityTest,Timeout(120000)] public IEnumerator HundredCellPileConservesMomentumAndCouplesMass()
        {
            using(var session=Session())
            {
                var hull=new uint[16384];for(int y=0;y<10;y++)hull[(64+y)*128+64]=2;session.ConfigureShip(hull,Vector2.zero);
                var task=session.SnapshotAsync();while(!task.IsCompleted)yield return null;var state=task.Result;
                var cells=new List<LooseCell>();for(int y=0;y<10;y++)for(int x=1;x<=10;x++)cells.Add(new LooseCell{Position=new Vector2(x,y),Material=1,Identity=(uint)cells.Count+1,Flags=1});
                Empty(state,cells.ToArray());state.ShipPose[1]=new Vector4(10,0,0,0);session.Restore(state);
                session.ConfigureShipBody(new BodyMass{Mass=10000,Inertia=100000,Center=new Vector2(.5f,5)});
                for(int step=0;step<30;step++)session.Step();
                task=session.SnapshotAsync();while(!task.IsCompleted)yield return null;state=task.Result;
                double momentum=10000*state.ShipPose[1].x,energy=.5*10000*state.ShipPose[1].x*state.ShipPose[1].x+.5*100000*state.ShipPose[1].z*state.ShipPose[1].z;
                foreach(var cell in state.Cells){momentum+=cell.Velocity.x;energy+=.5*cell.Velocity.sqrMagnitude;}
                Assert.That(momentum,Is.EqualTo(100000).Within(3));Assert.That(energy,Is.LessThanOrEqualTo(500001));
                Assert.That(state.ShipPose[1].x,Is.LessThan(9.95));Assert.That(state.ShipPose[1].x,Is.GreaterThan(9.8));
                Assert.That(state.ShipPose[0].x,Is.GreaterThan(3),"Free pile must move with the ship");
                Assert.DoesNotThrow(()=>CpuCutReference.ValidateShipPlacement(state,"00000000000000000000000000000001"));
                Debug.Log($"PILE100 speed={state.ShipPose[1].x:F6} position={state.ShipPose[0].x:F6} momentum={momentum:F6} energy={energy:F6} fallback={state.ContactStats[1]}");
            }
        }
    }
}
