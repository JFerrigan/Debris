using System.Collections;
using System.Runtime.InteropServices;
using Debris.Materials;
using Debris.Sites;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
namespace Debris.Simulation.Tests
{
    public sealed class MatterTests
    {
        [Test] public void ShaderLayoutIsExactly32Bytes(){Assert.That(Marshal.SizeOf<LooseCell>(),Is.EqualTo(32));Assert.That((int)Marshal.OffsetOf<LooseCell>(nameof(LooseCell.Material)),Is.EqualTo(16));}
        [UnityTest] public IEnumerator CuttingMatchesCpuReferenceAndSaturationRetainsMatter()
        {
            var catalog=Resources.Load<MaterialCatalog>("Materials");
            using(var session=new MatterSession(catalog,Resources.Load<AsteroidProfile>("Asteroid"),2,128,31))
            {
                var before=session.SnapshotAsync();while(!before.IsCompleted)yield return null;
                Assert.That(before.IsFaulted,Is.False,before.Exception?.ToString());
                var reference=before.Result;
                var command=new SiteCommand(SiteCommandType.CutterStroke,Vector2.zero,Vector2.zero,8,600,1);
                CpuCutReference.Apply(reference,catalog,command);session.Step(command);
                for(int i=0;i<10;i++)session.Step(command);
                for(int i=0;i<10;i++)CpuCutReference.Apply(reference,catalog,command);
                var after=session.SnapshotAsync();while(!after.IsCompleted)yield return null;
                Assert.That(after.IsFaulted,Is.False,after.Exception?.ToString());var actual=after.Result;Debug.Log("REFERENCE "+string.Join(",",reference.Counters)+" ACTUAL "+string.Join(",",actual.Counters));
                Assert.That(actual.Cells.Length,Is.EqualTo(31));Assert.That(actual.Counters[2],Is.GreaterThan(0));
                CollectionAssert.AreEqual(reference.Counters,actual.Counters);
                for(int i=0;i<actual.Fields.Length;i++)CollectionAssert.AreEqual(reference.Fields[i],actual.Fields[i]);
                for(int i=0;i<actual.Cells.Length;i++){Assert.That(actual.Cells[i].Material,Is.EqualTo(reference.Cells[i].Material));Assert.That(actual.Cells[i].Position,Is.EqualTo(reference.Cells[i].Position));}
                Assert.DoesNotThrow(()=>CpuCutReference.Validate(actual));
            }
        }
        [UnityTest] public IEnumerator MovingCellsDoNotOverlapTerrainOrEachOther()
        {
            using(var session=new MatterSession(Resources.Load<MaterialCatalog>("Materials"),Resources.Load<AsteroidProfile>("Asteroid"),2,128,2048))
            {
                for(int i=0;i<100;i++)
                {
                    session.Step(new SiteCommand(SiteCommandType.CutterStroke,new Vector2(45,i%30-15),new Vector2(9,4),8,600,1));
                    if(i%10==0)yield return null;
                }
                var task=session.SnapshotAsync();while(!task.IsCompleted)yield return null;
                Assert.That(task.IsFaulted,Is.False,task.Exception?.ToString());
                Assert.DoesNotThrow(()=>CpuCutReference.Validate(task.Result));
            }
        }
        [UnityTest] public IEnumerator PageEvictionAndResumePreservesDamageAndEveryCell()
        {
            var catalog=Resources.Load<MaterialCatalog>("Materials");var profile=Resources.Load<AsteroidProfile>("Asteroid");
            MatterSnapshot sleeping;
            using(var session=new MatterSession(catalog,profile))
            {
                session.Step(new SiteCommand(SiteCommandType.CutterStroke,Vector2.zero,new Vector2(2,0),7,600,1));
                session.Step(new SiteCommand(SiteCommandType.CutterStroke,new Vector2(20,0),Vector2.zero,4,1,1));
                var task=session.SnapshotAsync();Assert.Throws<System.InvalidOperationException>(()=>session.Step());
                while(!task.IsCompleted)yield return null;Assert.That(task.IsFaulted,Is.False,task.Exception?.ToString());sleeping=task.Result;
            }
            using(var resumed=new MatterSession(catalog,profile))
            {
                resumed.Restore(sleeping);var task=resumed.SnapshotAsync();while(!task.IsCompleted)yield return null;
                Assert.That(task.IsFaulted,Is.False,task.Exception?.ToString());var restored=task.Result;
                CollectionAssert.AreEqual(sleeping.Cells,restored.Cells);CollectionAssert.AreEqual(sleeping.Counters,restored.Counters);
                for(int i=0;i<sleeping.Fields.Length;i++){CollectionAssert.AreEqual(sleeping.Fields[i],restored.Fields[i]);CollectionAssert.AreEqual(sleeping.Damage[i],restored.Damage[i]);}
                Assert.DoesNotThrow(()=>CpuCutReference.Validate(restored));
            }
        }
        [UnityTest] public IEnumerator InspectionIsAsynchronousAndReturnsMaterial()
        {
            using(var session=new MatterSession(Resources.Load<MaterialCatalog>("Materials"),Resources.Load<AsteroidProfile>("Asteroid")))
            {
                bool done=false;ushort result=0;
                session.Inspect(Vector2Int.zero,m=>{result=m;done=true;});
                Assert.That(session.ReadbackQueue,Is.EqualTo(1));
                while(!done)yield return null;
                Assert.That(result,Is.GreaterThan(0));Assert.That(session.ReadbackQueue,Is.Zero);
            }
        }
    }
}
