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
    public sealed class ShipMatterTests
    {
        static MatterSession Session()=>new MatterSession(Resources.Load<MaterialCatalog>("Materials"),Resources.Load<AsteroidProfile>("Asteroid"),4,128,512);
        static void Empty(MatterSnapshot s,params LooseCell[] cells)
        {
            foreach(var f in s.Fields)Array.Clear(f,0,f.Length);
            foreach(var f in s.Damage)Array.Clear(f,0,f.Length);
            s.Cells=cells;s.Counters=new[]{(uint)cells.Length,(uint)cells.Length,0u,0u};Array.Clear(s.Dirty,0,s.Dirty.Length);
        }
        [UnityTest] public IEnumerator RotatingCargoConservesVolumeAndResumesExactly()
        {
            var blueprint=ShipBlueprint.Starter(2);
            try
            {
                using(var session=Session())
                {
                    var ship=new ShipRuntime(blueprint);session.ConfigureShip(ship.CollisionMask(),new Vector2(-100,0));
                    var task=session.SnapshotAsync();while(!task.IsCompleted)yield return null;Assert.That(task.IsFaulted,Is.False);
                    var cells=new List<LooseCell>();
                    for(int y=-20;y<20;y+=4)for(int x=-20;x<20;x+=4)cells.Add(new LooseCell{Position=new Vector2(x,y),Velocity=new Vector2(2,-1),Material=2,Identity=(uint)cells.Count+1,Flags=4});
                    var state=task.Result;Empty(state,cells.ToArray());session.Restore(state);
                    for(int t=0;t<240;t++)
                    {
                        session.Step(shipMotion:new Vector3(.01f,0,.002f));
                        if(t%30==0)
                        {
                            var check=session.SnapshotAsync();while(!check.IsCompleted)yield return null;
                            Assert.That(check.IsFaulted,Is.False,check.Exception?.ToString());Assert.DoesNotThrow(()=>CpuCutReference.Validate(check.Result),"step "+t);
                        }
                    }
                    var saved=session.SnapshotAsync();while(!saved.IsCompleted)yield return null;
                    Assert.That(saved.Result.Cells.Length,Is.EqualTo(100));Assert.That(saved.Result.ShipPose[0].z,Is.GreaterThan(.4f));
                    using(var resumed=Session())
                    {
                        resumed.Restore(saved.Result);var restored=resumed.SnapshotAsync();while(!restored.IsCompleted)yield return null;
                        CollectionAssert.AreEqual(saved.Result.Cells,restored.Result.Cells);CollectionAssert.AreEqual(saved.Result.ShipPose,restored.Result.ShipPose);
                        resumed.Step();var check=resumed.SnapshotAsync();while(!check.IsCompleted)yield return null;Assert.DoesNotThrow(()=>CpuCutReference.Validate(check.Result));
                    }
                }
            }finally{UnityEngine.Object.DestroyImmediate(blueprint);}
        }
        [UnityTest] public IEnumerator IntakeSpillAndObstructedDoorNeverDuplicateOrOverlap()
        {
            var blueprint=ShipBlueprint.Starter(2);
            try
            {
                using(var session=Session())
                {
                    session.ConfigureShip(new ShipRuntime(blueprint).CollisionMask(),new Vector2(-100,0));
                    var task=session.SnapshotAsync();while(!task.IsCompleted)yield return null;
                    var state=task.Result;Empty(state,new LooseCell{Position=new Vector2(-125,0),Velocity=Vector2.right*4,Material=2,Identity=1});
                    session.Restore(state);session.Step(doorOpen:true);
                    task=session.SnapshotAsync();while(!task.IsCompleted)yield return null;
                    Assert.That(task.Result.Cells[0].Flags&4,Is.EqualTo(4));Assert.That(task.Result.ShipPose[2].x,Is.EqualTo(1));
                    state=task.Result;state.Cells[0].Position=new Vector2(-28,0);state.Cells[0].Velocity=Vector2.left*8;session.Restore(state);
                    session.Step(doorOpen:false);task=session.SnapshotAsync();while(!task.IsCompleted)yield return null;
                    Assert.That(task.Result.ShipPose[2].z,Is.EqualTo(1),"Door must stay open when obstructed");Assert.DoesNotThrow(()=>CpuCutReference.Validate(task.Result));
                    for(int t=0;t<40;t++)session.Step(doorOpen:true);
                    task=session.SnapshotAsync();while(!task.IsCompleted)yield return null;
                    Assert.That(task.Result.Cells.Length,Is.EqualTo(1));Assert.That(task.Result.Cells[0].Flags&4,Is.Zero);Assert.That(task.Result.ShipPose[2].x,Is.Zero);
                    Assert.DoesNotThrow(()=>CpuCutReference.Validate(task.Result));
                }
            }finally{UnityEngine.Object.DestroyImmediate(blueprint);}
        }
        [UnityTest] public IEnumerator MountedDrillCutsOnlyInFrontOfShip()
        {
            var blueprint=ShipBlueprint.Starter(2);
            try
            {
                using(var session=Session())
                {
                    session.ConfigureShip(new ShipRuntime(blueprint).CollisionMask(),new Vector2(-105,0));
                    session.Step(new SiteCommand(SiteCommandType.CutterStroke,Vector2.zero,Vector2.zero,6,600,1),mountedCut:true);
                    var task=session.SnapshotAsync();while(!task.IsCompleted)yield return null;
                    Assert.That(task.Result.Cells.Length,Is.GreaterThan(0));
                    foreach(var cell in task.Result.Cells)Assert.That(cell.Position.x,Is.InRange(-54,-40));
                    Assert.DoesNotThrow(()=>CpuCutReference.Validate(task.Result));
                }
            }finally{UnityEngine.Object.DestroyImmediate(blueprint);}
        }
    }
}
