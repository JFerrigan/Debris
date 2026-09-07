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
    public sealed class SalvageSaveTests
    {
        [UnityTest] public IEnumerator DiskReloadPreservesCutsDamageFuelShipAndEveryCell()
        {
            string directory=Path.Combine(Path.GetTempPath(),"debris-save-"+Guid.NewGuid().ToString("N"));string path=Path.Combine(directory,"site.debris");
            var catalog=Resources.Load<MaterialCatalog>("Materials");var profile=Resources.Load<AsteroidProfile>("Asteroid");var blueprint=ShipBlueprint.Starter(catalog.IndexOf("iron"));ShipBlueprint restoredBlueprint=null;
            try
            {
                var ship=new ShipRuntime(blueprint);ship.Tick(Vector2.right,.4f,1f/60);ship.RemoveHull(new Vector2Int(24,26));
                MatterSnapshot before;
                using(var session=new MatterSession(catalog,profile,4))
                {
                    session.ConfigureShip(ship.CollisionMask(),ship.Position,ship.Angle);
                    session.Step(new SiteCommand(SiteCommandType.CutterStroke,Vector2.zero,Vector2.right*3,6,600,1));
                    session.Step(new SiteCommand(SiteCommandType.CutterStroke,new Vector2(20,0),Vector2.zero,4,1,1));
                    var task=session.SnapshotAsync();while(!task.IsCompleted)yield return null;before=task.Result;
                }
                // Preserve the same released identities while exercising both coordinate domains.
                for(int i=0;i<5;i++){before.Cells[i].Flags=4;before.Cells[i].Position=new Vector2(i*2,0);}
                var save=new SalvageSave{Matter=before,Ship=ShipSnapshot.Capture(ship),MaterialKeys=SalvageSave.Keys(catalog)};
                byte[] encoded=SalvageSaveCodec.Encode(save,JsonUtility.ToJson(save.Ship));AtomicSalvageStore.Write(path,encoded);
                var loaded=AtomicSalvageStore.Read(path,out var shipJson);SalvageSaveCodec.ResolveContent(loaded,shipJson,catalog);
                var restored=loaded.Ship.Restore(out restoredBlueprint);
                Assert.That(restored.Fuel.Energy,Is.EqualTo(ship.Fuel.Energy));Assert.That(restored.Velocity,Is.EqualTo(ship.Velocity));CollectionAssert.AreEquivalent(ship.Structure,restored.Structure);
                using(var resumed=new MatterSession(catalog,profile,4))
                {
                    resumed.Restore(loaded.Matter);var task=resumed.SnapshotAsync();while(!task.IsCompleted)yield return null;
                    CollectionAssert.AreEqual(before.Cells,task.Result.Cells);CollectionAssert.AreEqual(before.ShipPose,task.Result.ShipPose);
                    for(int i=0;i<before.Fields.Length;i++){CollectionAssert.AreEqual(before.Fields[i],task.Result.Fields[i]);CollectionAssert.AreEqual(before.Damage[i],task.Result.Damage[i]);}
                    Assert.DoesNotThrow(()=>CpuCutReference.Validate(task.Result));
                }
                var reordered=ScriptableObject.CreateInstance<MaterialCatalog>();
                try
                {
                    reordered.Configure(Enumerable.Range(1,catalog.Count).Select(i=>catalog.DefinitionAt((ushort)i)).Reverse().ToArray());
                    var remapped=SalvageSaveCodec.Decode(encoded,out var remapJson);SalvageSaveCodec.ResolveContent(remapped,remapJson,reordered);
                    for(int i=0;i<before.Cells.Length;i++)Assert.That(reordered.DefinitionAt((ushort)remapped.Matter.Cells[i].Material).MaterialKey,Is.EqualTo(catalog.DefinitionAt((ushort)before.Cells[i].Material).MaterialKey));
                    Assert.That(remapped.MaterialKeys,Is.EqualTo(SalvageSave.Keys(reordered)));
                }finally{UnityEngine.Object.DestroyImmediate(reordered);}
                // Failure before replacement leaves the committed generation readable.
                save.Matter.Tick++;byte[] next=SalvageSaveCodec.Encode(save,JsonUtility.ToJson(save.Ship));
                Assert.Throws<IOException>(()=>AtomicSalvageStore.Write(path,next,()=>throw new IOException("simulated interrupted write")));
                Assert.That(AtomicSalvageStore.Read(path,out _).Matter.Tick,Is.EqualTo(before.Tick-1));
                AtomicSalvageStore.Write(path,next);File.WriteAllBytes(path,new byte[]{1,2,3});
                var recovered=AtomicSalvageStore.Read(path,out _);Assert.That(recovered.RecoveredBackup,Is.True);Assert.That(recovered.Matter.Tick,Is.EqualTo(before.Tick-1));
                AtomicSalvageStore.Write(path,next);Assert.That(AtomicSalvageStore.Read(path,out _).RecoveredBackup,Is.False);
                var bad=(byte[])encoded.Clone();bad[12]^=1;Assert.Throws<InvalidDataException>(()=>SalvageSaveCodec.Decode(bad,out _));
                var future=(byte[])encoded.Clone();future[4]=99;Assert.Throws<NotSupportedException>(()=>SalvageSaveCodec.Decode(future,out _));
                Debug.Log($"DEBRIS_SAVE bytes={encoded.Length} loose={before.Cells.Length} schema={SalvageSaveCodec.Schema} exact=true interrupted=true recovered=true");
            }
            finally{UnityEngine.Object.DestroyImmediate(blueprint);if(restoredBlueprint)UnityEngine.Object.DestroyImmediate(restoredBlueprint);if(Directory.Exists(directory))Directory.Delete(directory,true);}
        }
    }
}
