using System;
using System.Collections;
using System.IO;
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
    public sealed class FuelTransferTests
    {
        [UnityTest] public IEnumerator PartialFuelSpillPumpSaveAndFurtherCutConserveEnergyAndIdentity()
        {
            var catalog=Resources.Load<MaterialCatalog>("Materials");var blueprint=ShipBlueprint.Starter(2);
            try
            {
                using(var session=new MatterSession(catalog,Resources.Load<AsteroidProfile>("Asteroid"),4,128,32))
                {
                    session.ConfigureShip(new ShipRuntime(blueprint).CollisionMask(),new Vector2(-175,0));
                    var task=session.SnapshotAsync();while(!task.IsCompleted)yield return null;var original=task.Result;
                    foreach(var f in original.Fields)Array.Clear(f,0,f.Length);original.Counters[1]=0;
                    var tank=new TankInventory{Capacity=3};tank.Add("low",1);tank.Add("standard",1);tank.Add("dense",1);tank.Consume(.5);
                    var outlets=new[]{new Vector2(-145,35),new Vector2(-143,35),new Vector2(-141,35)};
                    var obstructed=FuelTransfers.Spill(original,tank,catalog,new[]{new Vector2(-140,10)},Vector2.zero);
                    Assert.That(obstructed.Count,Is.Zero);Assert.That(obstructed.Tank.Energy,Is.EqualTo(6.5));Assert.That(obstructed.Matter.NextIdentity,Is.EqualTo(original.NextIdentity));
                    var crowded=FuelTransfers.Spill(original,tank,catalog,new[]{outlets[0],outlets[0]},Vector2.zero);
                    Assert.That(crowded.Count,Is.EqualTo(1));Assert.That(crowded.Tank.Count,Is.EqualTo(2));
                    var spill=FuelTransfers.Spill(original,tank,catalog,outlets,Vector2.zero);
                    Assert.That(spill.Count,Is.EqualTo(3));Assert.That(spill.Tank.Count,Is.Zero);Assert.That(tank.Count,Is.EqualTo(3),"Proposal must not mutate source tank");
                    Assert.That(original.Cells.Length,Is.Zero);Assert.That(spill.Matter.FuelCells.Sum(f=>f.Energy),Is.EqualTo(6.5));
                    var save=new SalvageSave{Matter=spill.Matter,MaterialKeys=SalvageSave.Keys(catalog)};
                    var loaded=SalvageSaveCodec.Decode(SalvageSaveCodec.Encode(save,null),out var json);SalvageSaveCodec.ResolveContent(loaded,json,catalog);
                    CollectionAssert.AreEqual(spill.Matter.FuelCells,loaded.Matter.FuelCells);
                    session.Restore(loaded.Matter);session.Step();task=session.SnapshotAsync();while(!task.IsCompleted)yield return null;
                    var pump=FuelTransfers.Pump(task.Result,spill.Tank,catalog,new Vector2(-144,35.5f),20,2);
                    Assert.That(pump.Count,Is.EqualTo(2));Assert.That(pump.Tank.Energy,Is.EqualTo(2.5));Assert.That(pump.Matter.Cells.Single().Identity,Is.EqualTo(3));
                    // Add a fixed fixture cell, then cut after compacting the pool. ID 3 must not be reused.
                    int slice=2*pump.Matter.Side+2;pump.Matter.Fields[slice][0]=1;pump.Matter.Counters[1]++;
                    session.Restore(pump.Matter);session.Step(new SiteCommand(SiteCommandType.CutterStroke,Vector2.zero,Vector2.zero,1,600,1));
                    task=session.SnapshotAsync();while(!task.IsCompleted)yield return null;
                    CollectionAssert.AreEquivalent(new uint[]{3,4},task.Result.Cells.Select(c=>c.Identity));
                    var finished=FuelTransfers.Pump(task.Result,pump.Tank,catalog,new Vector2(-141,35.5f),2);
                    Assert.That(finished.Count,Is.EqualTo(1));Assert.That(finished.Tank.Energy,Is.EqualTo(6.5));Assert.That(finished.Tank.Count,Is.EqualTo(3));
                    Assert.That(finished.Matter.Cells.Length,Is.EqualTo(1));Assert.That(finished.Matter.Cells[0].Material,Is.EqualTo(1));Assert.That(finished.Matter.FuelCells,Is.Empty);
                    Assert.DoesNotThrow(()=>CpuCutReference.Validate(finished.Matter));
                    var full=FuelTransfers.Pump(spill.Matter,finished.Tank,catalog,outlets[0],20);Assert.That(full.Count,Is.Zero);
                    spill.Matter.Capacity=3;var blocked=FuelTransfers.Spill(spill.Matter,finished.Tank,catalog,outlets,Vector2.zero);
                    Assert.That(blocked.Count,Is.Zero);Assert.That(blocked.Tank.Energy,Is.EqualTo(6.5));Assert.That(blocked.Matter.Cells.Length,Is.EqualTo(3));
                }
            }finally{UnityEngine.Object.DestroyImmediate(blueprint);}
        }
        [Test] public void SchemaOnePlayerCheckpointMigratesWithoutFuelRefill()
        {
            var bytes=File.ReadAllBytes(Path.Combine(Application.dataPath,"Debris/Persistence/Tests/Fixtures/schema1.debris.bytes"));
            Assert.That(BitConverter.ToInt32(bytes,4),Is.EqualTo(1));
            var loaded=SalvageSaveCodec.Decode(bytes,out var json);var catalog=Resources.Load<MaterialCatalog>("Materials");SalvageSaveCodec.ResolveContent(loaded,json,catalog);
            var ship=loaded.Ship.Restore(out var blueprint);
            try
            {
                Assert.That(ship.Fuel.Count,Is.EqualTo(250));Assert.That(ship.Fuel.Energy,Is.EqualTo(500));
                Assert.That(loaded.Matter.Cells.Length,Is.EqualTo(576));Assert.That(loaded.Matter.NextIdentity,Is.EqualTo(577));Assert.That(loaded.Matter.FuelCells,Is.Empty);
                var current=SalvageSaveCodec.Encode(loaded,JsonUtility.ToJson(ShipSnapshot.Capture(ship)));
                Assert.That(BitConverter.ToInt32(current,4),Is.EqualTo(SalvageSaveCodec.Schema));Assert.DoesNotThrow(()=>SalvageSaveCodec.Decode(current,out _));
                var partial=new TankInventory{Capacity=3,Low=1,Standard=1,Dense=1,BurnRemainder=.75};partial.MigrateLegacy();
                Assert.That(partial.Energy,Is.EqualTo(6.25));Assert.That(partial.Contents[0].Energy,Is.EqualTo(.25));
                partial.Add("dense",0);Assert.That(partial.Energy,Is.EqualTo(6.25));partial.Validate();
            }finally{UnityEngine.Object.DestroyImmediate(blueprint);}
        }
    }
}
