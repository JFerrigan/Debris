using System;
using System.Collections;
using System.Linq;
using Debris.Materials;
using Debris.Ships;
using Debris.Simulation;
using Debris.Sites;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
namespace Debris.Persistence.Tests
{
    public sealed class FragmentTests
    {
        [UnityTest] public IEnumerator DestroyedAnchorRetainsWholeMachineAsIndependentFragment()
        {
            var b=ShipBlueprint.Starter(2);
            try
            {
                var ship=new ShipRuntime(b);var unit=ship.Units.Single(u=>u.Placement.Definition.Key=="upper-thruster");
                using(var session=new MatterSession(Resources.Load<MaterialCatalog>("Materials"),Resources.Load<AsteroidProfile>("Asteroid"),4,128,32))
                {
                    session.ConfigureShip(ship.CollisionMask(),ship.Position);var task=session.SnapshotAsync();while(!task.IsCompleted)yield return null;
                    using(var damage=ShipDamage.CutHull(task.Result,ship,new[]{unit.Placement.Anchor}))
                    {
                        Assert.That(damage.Released,Is.EqualTo(1));Assert.That(damage.Ship.Fragments.Count,Is.EqualTo(1));
                        var fragment=damage.Ship.Fragments.Single();Assert.That(fragment.Cells.Count,Is.Zero);Assert.That(fragment.Units.Single().Placement.Id,Is.EqualTo(unit.Placement.Id));
                        Assert.That(damage.Matter.Fragments[0].Hull.Count(m=>m!=0),Is.EqualTo(15*8));
                        session.Restore(damage.Matter);session.Step();task=session.SnapshotAsync();while(!task.IsCompleted)yield return null;
                        Assert.DoesNotThrow(()=>CpuCutReference.Validate(task.Result));
                    }
                }
            }finally{UnityEngine.Object.DestroyImmediate(b);}
        }
        [UnityTest] public IEnumerator HullCollisionQueuesWholeUnitDamageAcrossSaveBoundary()
        {
            var b=ShipBlueprint.Starter(2);
            try
            {
                var ship=new ShipRuntime(b){Position=new Vector2(-156,0)};
                using(var session=new MatterSession(Resources.Load<MaterialCatalog>("Materials"),Resources.Load<AsteroidProfile>("Asteroid"),4,128,32))
                {
                    session.ConfigureShip(ship.CollisionMask(),ship.Position);var task=session.SnapshotAsync();while(!task.IsCompleted)yield return null;var state=task.Result;
                    foreach(var field in state.Fields)Array.Clear(field,0,field.Length);
                    state.Fields[9][28]=2;state.Counters[1]=1;session.Restore(state);
                    for(int i=0;i<20;i++)session.Step(shipMotion:new Vector3(.2f,0,0));
                    task=session.SnapshotAsync();while(!task.IsCompleted)yield return null;
                    Assert.That(task.Result.Impact[3],Is.EqualTo(1));
                    float speed=BitConverter.ToSingle(BitConverter.GetBytes(task.Result.Impact[2]),0);Assert.That(speed,Is.EqualTo(12).Within(.001));
                    var save=new SalvageSave{Matter=task.Result,MaterialKeys=SalvageSave.Keys(Resources.Load<MaterialCatalog>("Materials"))};
                    var loaded=SalvageSaveCodec.Decode(SalvageSaveCodec.Encode(save,""),out _);CollectionAssert.AreEqual(task.Result.Impact,loaded.Matter.Impact);
                    var p=new Vector2Int((int)loaded.Matter.Impact[0]-64,(int)loaded.Matter.Impact[1]-64);
                    using(var damage=ShipDamage.CutHull(loaded.Matter,ship,new[]{p},(speed-6)*10))
                    {
                        Assert.That(damage.Released,Is.Zero);Assert.That(damage.Ship.Units.Single(u=>u.Placement.Definition.Kind==UnitKind.Drill).Health,Is.EqualTo(40).Within(.01));
                        Assert.That(damage.Matter.Impact[3],Is.Zero);Assert.DoesNotThrow(()=>CpuCutReference.Validate(damage.Matter));
                    }
                }
            }finally{UnityEngine.Object.DestroyImmediate(b);}
        }
        [UnityTest] public IEnumerator CutThrusterProducesMovingSolidFragmentAndExactDiskState()
        {
            var catalog=Resources.Load<MaterialCatalog>("Materials");var profile=Resources.Load<AsteroidProfile>("Asteroid");var b=ShipBlueprint.Starter(2);
            try
            {
                var ship=new ShipRuntime(b){Velocity=new Vector2(0,3)};int initial=ship.Structure.Count;
                using(var session=new MatterSession(catalog,profile,4,128,256))
                {
                    session.ConfigureShip(ship.CollisionMask(),ship.Position);
                    var task=session.SnapshotAsync();while(!task.IsCompleted)yield return null;
                    task.Result.ShipPose[1]=new Vector4(0,3,0,0); // Authoritative GPU motion drives detached inheritance.
                    using(var cut=ShipDamage.CutHull(task.Result,ship,new[]{new Vector2Int(24,25),new Vector2Int(24,26),new Vector2Int(24,27)}))
                    {
                        Assert.That(cut.Released,Is.EqualTo(3));Assert.That(cut.Ship.Fragments.Count,Is.EqualTo(1));Assert.That(ship.Structure.Count,Is.EqualTo(initial));
                        Assert.That(cut.Ship.Structure.Count+cut.Ship.Fragments.Sum(f=>f.Cells.Count)+cut.Released,Is.EqualTo(initial));
                        var thruster=cut.Ship.Units.Single(u=>u.Placement.Definition.Key=="upper-thruster");Assert.That(thruster.Operational,Is.False);Assert.That(thruster.OwnerId,Is.EqualTo(cut.Ship.Fragments[0].Id));
                        session.Restore(cut.Matter);var start=cut.Matter.Fragments[0].Pose;
                        for(int i=0;i<90;i++){session.Step();if(i%15==0)yield return null;}
                        task=session.SnapshotAsync();while(!task.IsCompleted)yield return null;
                        Assert.DoesNotThrow(()=>CpuCutReference.Validate(task.Result));Assert.That(task.Result.Fragments[0].Pose.y,Is.GreaterThan(start.y+1));
                        ShipDamage.SynchronizeFragments(task.Result,cut.Ship);
                        var save=new SalvageSave{Matter=task.Result,MaterialKeys=SalvageSave.Keys(catalog),Ship=ShipSnapshot.Capture(cut.Ship)};
                        var loaded=SalvageSaveCodec.Decode(SalvageSaveCodec.Encode(save,JsonUtility.ToJson(save.Ship)),out var json);SalvageSaveCodec.ResolveContent(loaded,json,catalog);
                        Assert.That(loaded.Matter.Fragments[0].Pose,Is.EqualTo(task.Result.Fragments[0].Pose));CollectionAssert.AreEqual(loaded.Matter.Fragments[0].Hull,task.Result.Fragments[0].Hull);
                        var restoredShip=loaded.Ship.Restore(out var loadedBlueprint);
                        try{Assert.That(restoredShip.Fragments[0].Units[0],Is.SameAs(restoredShip.Units.Single(u=>u.Placement.Id==restoredShip.Fragments[0].Units[0].Placement.Id)));}
                        finally{UnityEngine.Object.DestroyImmediate(loadedBlueprint);}
                        session.Restore(loaded.Matter);session.Step();task=session.SnapshotAsync();while(!task.IsCompleted)yield return null;Assert.DoesNotThrow(()=>CpuCutReference.Validate(task.Result));
                    }
                }
            }finally{UnityEngine.Object.DestroyImmediate(b);}
        }
        [UnityTest] public IEnumerator CargoEscapesCutWallAndFullPoolRetainsHull()
        {
            var catalog=Resources.Load<MaterialCatalog>("Materials");var b=ShipBlueprint.Starter(2);var ship=new ShipRuntime(b);
            try
            {
                using(var session=new MatterSession(catalog,Resources.Load<AsteroidProfile>("Asteroid"),4,128,64))
                {
                    session.ConfigureShip(ship.CollisionMask(),ship.Position);var task=session.SnapshotAsync();while(!task.IsCompleted)yield return null;var state=task.Result;
                    state.Cells=new[]{new LooseCell{Position=new Vector2(0,23),Velocity=Vector2.up*1.5f,Material=2,Identity=1,Flags=4}};state.NextIdentity=2;state.Counters[0]=1;state.Counters[1]++;
                    var points=Enumerable.Range(-2,5).SelectMany(x=>Enumerable.Range(25,3).Select(y=>new Vector2Int(x,y))).ToArray();
                    using(var cut=ShipDamage.CutHull(state,ship,points))
                    {
                        Assert.That(cut.Released,Is.EqualTo(15));session.Restore(cut.Matter);
                        for(int i=0;i<320;i++)
                        {
                            session.Step();
                            if(i<10||i%20==0){var check=session.SnapshotAsync();while(!check.IsCompleted)yield return null;Assert.DoesNotThrow(()=>CpuCutReference.Validate(check.Result),"breach step "+i);}
                        }
                        task=session.SnapshotAsync();while(!task.IsCompleted)yield return null;
                        Assert.DoesNotThrow(()=>CpuCutReference.Validate(task.Result));Assert.That(task.Result.Cells.Single(c=>c.Identity==1).Flags&4,Is.Zero);Assert.That(task.Result.Cells.Length,Is.EqualTo(16));Assert.DoesNotThrow(()=>CpuCutReference.Validate(task.Result));
                    }
                    state.Capacity=1;
                    using(var blocked=ShipDamage.CutHull(state,ship,points)){Assert.That(blocked.Released,Is.Zero);Assert.That(blocked.Ship.Structure.Count,Is.EqualTo(ship.Structure.Count));}
                }
            }finally{UnityEngine.Object.DestroyImmediate(b);}
        }
        [UnityTest] public IEnumerator FullRotatingCavityCannotAdmitOverlappingExtraCargo()
        {
            var catalog=Resources.Load<MaterialCatalog>("Materials");var b=ShipBlueprint.Starter(2);
            try
            {
                using(var session=new MatterSession(catalog,Resources.Load<AsteroidProfile>("Asteroid"),4,128,8192))
                {
                    session.ConfigureShip(new ShipRuntime(b).CollisionMask(),new Vector2(-175,0));
                    var task=session.SnapshotAsync();while(!task.IsCompleted)yield return null;var state=task.Result;
                    state.Cells=Enumerable.Range(0,2500).Select(i=>new LooseCell{Identity=(uint)i+1,Material=2,Flags=4,Position=new Vector2(i%50-25,i/50-25)}).ToArray();
                    state.Counters[0]=2500;state.Counters[1]+=2500;state.NextIdentity=2501;session.Restore(state);
                    for(int i=0;i<60;i++)
                    {
                        session.Step(shipMotion:new Vector3(0,0,.003f));
                        if(i%15==0){var check=session.SnapshotAsync();while(!check.IsCompleted)yield return null;Assert.DoesNotThrow(()=>CpuCutReference.Validate(check.Result),"full cavity step "+i);}
                    }
                    task=session.SnapshotAsync();while(!task.IsCompleted)yield return null;state=task.Result;
                    Assert.That(state.ShipPose[2].x,Is.EqualTo(2500));
                    var extra=new LooseCell{Identity=state.NextIdentity++,Material=2,Position=FuelTransfers.World(state,new Vector2(-30.5f,.5f))-Vector2.one*.5f};
                    state.Cells=state.Cells.Concat(new[]{extra}).ToArray();state.Counters[0]++;state.Counters[1]++;session.Restore(state);
                    for(int i=0;i<45;i++){session.Step(force:40,doorOpen:true,mountedSuction:true);if(i%15==0)yield return null;}
                    task=session.SnapshotAsync();while(!task.IsCompleted)yield return null;
                    Assert.That(task.Result.Cells.Length,Is.EqualTo(2501));Assert.That(task.Result.ShipPose[2].x,Is.LessThanOrEqualTo(2500));Assert.DoesNotThrow(()=>CpuCutReference.Validate(task.Result));
                }
            }finally{UnityEngine.Object.DestroyImmediate(b);}
        }
        [UnityTest] public IEnumerator UnitImpactFailsAsAWholeAndRetainsTankFuelForSpill()
        {
            var catalog=Resources.Load<MaterialCatalog>("Materials");var b=ShipBlueprint.Starter(2);
            try
            {
                var ship=new ShipRuntime(b);
                using(var session=new MatterSession(catalog,Resources.Load<AsteroidProfile>("Asteroid"),4,128,32))
                {
                    session.ConfigureShip(ship.CollisionMask(),ship.Position);var task=session.SnapshotAsync();while(!task.IsCompleted)yield return null;
                    using(var damage=ShipDamage.CutHull(task.Result,ship,new[]{new Vector2Int(30,10)},100))
                    {
                        Assert.That(damage.Ship.Has(UnitKind.Tank),Is.False);Assert.That(damage.Ship.Fuel.Energy,Is.EqualTo(ship.Fuel.Energy));
                        Assert.That(damage.Released,Is.Zero);Assert.That(damage.Matter.Hull[(10+64)*128+30+64],Is.Not.Zero);
                        session.Restore(damage.Matter);var transfer=FuelTransfers.Spill(damage.Matter,damage.Ship.Fuel,catalog,new[]{ship.Position+new Vector2(35,32)},Vector2.zero);
                        Assert.That(transfer.Count,Is.EqualTo(1));Assert.That(transfer.Tank.Count+transfer.Matter.Cells.Length,Is.EqualTo(ship.Fuel.Count));
                    }
                }
            }finally{UnityEngine.Object.DestroyImmediate(b);}
        }
        [UnityTest] public IEnumerator RigidFragmentStopsAtTerrainWithoutOverlappingIt()
        {
            using(var session=new MatterSession(Resources.Load<MaterialCatalog>("Materials"),Resources.Load<AsteroidProfile>("Asteroid"),4,128,32))
            {
                var task=session.SnapshotAsync();while(!task.IsCompleted)yield return null;var s=task.Result;
                foreach(var field in s.Fields)Array.Clear(field,0,field.Length);s.Counters[1]=1;s.Fields[10][0]=1;
                var mask=new uint[16384];mask[64*128+64]=2;
                s.Fragments=new[]{new RigidFragmentSnapshot{Id="00000000000000000000000000000008",Hull=mask,Pose=new Vector4(-3,0,0,1),Motion=new Vector4(12,0,0,0)}};session.Restore(s);
                for(int i=0;i<30;i++)session.Step();task=session.SnapshotAsync();while(!task.IsCompleted)yield return null;
                Assert.That(task.Result.Fragments[0].Pose.x,Is.InRange(-1.21f,-.99f));Assert.That(task.Result.Fragments[0].Motion.w,Is.EqualTo(1));Assert.DoesNotThrow(()=>CpuCutReference.Validate(task.Result));
            }
        }
    }
}
