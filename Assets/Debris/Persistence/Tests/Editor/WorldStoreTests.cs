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
    public sealed class WorldStoreTests
    {
        [Test] public void LegacyCheckpointImportsIntoSparseWorldWithoutRefillingFuel()
        {
            string root=Path.Combine(Path.GetTempPath(),"debris-world-import-"+Guid.NewGuid().ToString("N"));ShipBlueprint blueprint=null;
            try
            {
                var catalog=Resources.Load<MaterialCatalog>("Materials");var profile=Resources.Load<AsteroidProfile>("Asteroid");
                var save=SalvageSaveCodec.Decode(File.ReadAllBytes("Assets/Debris/Persistence/Tests/Fixtures/schema1.debris.bytes"),out var json);SalvageSaveCodec.ResolveContent(save,json,catalog);
                var ship=save.Ship.Restore(out blueprint);save.Ship=ShipSnapshot.Capture(ship);double energy=save.Ship.Fuel.Energy;
                var manifest=WorldStore.Commit(root,0,save.SiteId,save.Matter.NextIdentity,new[]{Prepare(save,catalog,profile)});
                var loaded=SparseSiteStore.Reconstruct(WorldStore.ReadSite(root,manifest,manifest.ActiveSiteId),catalog,profile);
                Assert.That(loaded.Ship.Fuel.Energy,Is.EqualTo(energy));Assert.That(loaded.Matter.Cells.Length,Is.EqualTo(576));ExactSite(save.Matter,loaded.Matter);
                Assert.That(loaded.Ship.Units.All(u=>u.OwnerId==loaded.Ship.Id),Is.True);
            }
            finally{if(blueprint)UnityEngine.Object.DestroyImmediate(blueprint);if(Directory.Exists(root))Directory.Delete(root,true);}
        }
        static SparseSiteData Prepare(SalvageSave save,MaterialCatalog catalog,AsteroidProfile profile)=>SparseSiteStore.Prepare(save,save.Ship==null?"":JsonUtility.ToJson(save.Ship),SparseSiteStore.Baseline(save,catalog,profile));
        static SalvageSave NewSite(MatterSnapshot shape,string id,MaterialCatalog catalog,AsteroidProfile profile)
        {
            var s=FuelTransfers.Copy(shape);s.ShipEnabled=false;s.ShipPose=new Vector4[3];s.Hull=new uint[16384];s.Cells=Array.Empty<LooseCell>();s.FuelCells=Array.Empty<FuelCellState>();s.Fragments=Array.Empty<RigidFragmentSnapshot>();s.Impact=new uint[4];s.Counters=new uint[4];s.Dirty=new uint[s.Side*s.Side];s.Tick=0;
            var save=new SalvageSave{SiteId=id,Matter=s,MaterialKeys=SalvageSave.Keys(catalog)};s.Fields=SparseSiteStore.Baseline(save,catalog,profile);s.Damage=s.Fields.Select(c=>new float[c.Length]).ToArray();
            s.Counters[1]=(uint)s.Fields.Sum(c=>c.Count(m=>m!=0));return save;
        }
        static void ExactSite(MatterSnapshot expected,MatterSnapshot actual)
        {
            CollectionAssert.AreEqual(expected.Cells,actual.Cells);CollectionAssert.AreEqual(expected.Dirty,actual.Dirty);Assert.That(actual.Tick,Is.EqualTo(expected.Tick));
            for(int i=0;i<expected.Fields.Length;i++){CollectionAssert.AreEqual(expected.Fields[i],actual.Fields[i]);CollectionAssert.AreEqual(expected.Damage[i],actual.Damage[i]);}
            Assert.That(actual.Fragments.Length,Is.EqualTo(expected.Fragments.Length));
            for(int i=0;i<expected.Fragments.Length;i++){Assert.That(actual.Fragments[i].Id,Is.EqualTo(expected.Fragments[i].Id));Assert.That(actual.Fragments[i].Pose,Is.EqualTo(expected.Fragments[i].Pose));CollectionAssert.AreEqual(expected.Fragments[i].Hull,actual.Fragments[i].Hull);}
        }
        [UnityTest] public IEnumerator SparseWorldTravelNeverDuplicatesCargoOrDepositedFragments()
        {
            string root=Path.Combine(Path.GetTempPath(),"debris-world-"+Guid.NewGuid().ToString("N"));
            var catalog=Resources.Load<MaterialCatalog>("Materials");var profile=Resources.Load<AsteroidProfile>("Asteroid");var b=ShipBlueprint.Starter(2);
            try
            {
                var ship=new ShipRuntime(b);
                using(var session=new MatterSession(catalog,profile,4,128,256))
                {
                    session.ConfigureShip(ship.CollisionMask(),ship.Position);
                    session.Step(new SiteCommand(SiteCommandType.CutterStroke,Vector2.zero,Vector2.zero,3,600,1));
                    session.Step(new SiteCommand(SiteCommandType.CutterStroke,new Vector2(20,0),Vector2.zero,2,1,1));
                    var task=session.SnapshotAsync();while(!task.IsCompleted)yield return null;var matter=task.Result;
                    matter.Cells=matter.Cells.Concat(new[]{new LooseCell{Identity=matter.NextIdentity++,Material=2,Flags=4,Position=Vector2.zero},new LooseCell{Identity=matter.NextIdentity++,Material=2,Flags=4,Position=new Vector2(2,0)}}).ToArray();matter.Counters[0]+=2;matter.Counters[1]+=2;
                    using(var damaged=ShipDamage.CutHull(matter,ship,new[]{new Vector2Int(24,-28),new Vector2Int(24,-27),new Vector2Int(24,-26)}))
                    {
                        var current=new SalvageSave{Matter=damaged.Matter,Ship=ShipSnapshot.Capture(damaged.Ship),MaterialKeys=SalvageSave.Keys(catalog)};
                        current.Ship.Fuel.Consume(.75);
                        var spill=FuelTransfers.Spill(current.Matter,current.Ship.Fuel,catalog,new[]{new Vector2(-150,80)},Vector2.zero);
                        Assert.That(spill.Count,Is.EqualTo(1));current.Matter=spill.Matter;current.Ship.Fuel=spill.Tank;
                        // The rail's freshly released cells are still ship-local; all five travel, the rigid thruster stays.
                        var watch=System.Diagnostics.Stopwatch.StartNew();var prepared=Prepare(current,catalog,profile);
                        Assert.That(prepared.Chunks.Count,Is.InRange(1,4));
                        var first=WorldStore.Commit(root,0,current.SiteId,current.Matter.NextIdentity,new[]{prepared});long saveMs=watch.ElapsedMilliseconds;
                        int blobs=Directory.GetFiles(Path.Combine(root,"blobs"),"*.blob",SearchOption.AllDirectories).Length;
                        var checkpoint=WorldStore.Commit(root,first.Revision,current.SiteId,current.Matter.NextIdentity,new[]{prepared});
                        Assert.That(Directory.GetFiles(Path.Combine(root,"blobs"),"*.blob",SearchOption.AllDirectories).Length,Is.EqualTo(blobs));
                        var restored=SparseSiteStore.Reconstruct(WorldStore.ReadSite(root,checkpoint,current.SiteId),catalog,profile);ExactSite(current.Matter,restored.Matter);
                        CollectionAssert.AreEqual(current.Matter.FuelCells,restored.Matter.FuelCells);
                        var reordered=ScriptableObject.CreateInstance<MaterialCatalog>();
                        try
                        {
                            reordered.Configure(Enumerable.Range(1,catalog.Count).Select(i=>catalog.DefinitionAt((ushort)i)).Reverse().ToArray());
                            var remapped=SparseSiteStore.Reconstruct(WorldStore.ReadSite(root,checkpoint,current.SiteId),reordered,profile);
                            for(int i=0;i<current.Matter.Cells.Length;i++)Assert.That(reordered.DefinitionAt((ushort)remapped.Matter.Cells[i].Material).MaterialKey,Is.EqualTo(catalog.DefinitionAt((ushort)current.Matter.Cells[i].Material).MaterialKey));
                        }finally{UnityEngine.Object.DestroyImmediate(reordered);}
                        var mismatch=WorldStore.ReadSite(root,checkpoint,current.SiteId);mismatch.Save.GeneratorSeed++;Assert.Throws<NotSupportedException>(()=>SparseSiteStore.Reconstruct(mismatch,catalog,profile));
                        var departure=SiteTransit.Depart(restored);var other=NewSite(current.Matter,"00000000000000000000000000000002",catalog,profile);
                        var arrived=SiteTransit.Arrive(other,departure.Portable,current.Matter.NextIdentity,new Vector2(-175,0));
                        var changes=new[]{Prepare(departure.Site,catalog,profile),Prepare(arrived,catalog,profile)};
                        Assert.Throws<IOException>(()=>WorldStore.Commit(root,checkpoint.Revision,other.SiteId,arrived.Matter.NextIdentity,changes,()=>throw new IOException("interrupted before root publication")));
                        Assert.That(WorldStore.Read(root).Revision,Is.EqualTo(checkpoint.Revision));
                        var stillActive=SparseSiteStore.Reconstruct(WorldStore.ReadSite(root,WorldStore.Read(root),current.SiteId),catalog,profile);ExactSite(current.Matter,stillActive.Matter);
                        var travelled=WorldStore.Commit(root,checkpoint.Revision,other.SiteId,arrived.Matter.NextIdentity,changes);
                        Assert.Throws<InvalidOperationException>(()=>WorldStore.Commit(root,checkpoint.Revision,other.SiteId,arrived.Matter.NextIdentity,changes));
                        var sleeping=SparseSiteStore.Reconstruct(WorldStore.ReadSite(root,travelled,current.SiteId),catalog,profile);ExactSite(departure.Site.Matter,sleeping.Matter);
                        Assert.That(sleeping.Matter.ShipEnabled,Is.False);Assert.That(sleeping.Ship.Fuel.Count,Is.Zero);Assert.That(sleeping.Ship.Structure,Is.Empty);
                        Assert.That(arrived.Matter.Fragments,Is.Empty);Assert.That(arrived.Ship.Units.Any(u=>u.Placement.Definition.Key=="lower-thruster"),Is.False);
                        var reload=SparseSiteStore.Reconstruct(WorldStore.ReadSite(root,WorldStore.Read(root),other.SiteId),catalog,profile);
                        CpuCutReference.Apply(reload.Matter,catalog,new SiteCommand(SiteCommandType.CutterStroke,Vector2.zero,Vector2.zero,2,600,1));
                        var returning=SiteTransit.Depart(reload);
                        // Returning over the deposited fragment is rejected atomically; a clear arrival keeps it exact.
                        Assert.Throws<InvalidOperationException>(()=>SiteTransit.Arrive(sleeping,returning.Portable,reload.Matter.NextIdentity,Vector2.zero));
                        var back=SiteTransit.Arrive(sleeping,returning.Portable,reload.Matter.NextIdentity,new Vector2(-175,-95));
                        var final=WorldStore.Commit(root,travelled.Revision,back.SiteId,back.Matter.NextIdentity,new[]{Prepare(returning.Site,catalog,profile),Prepare(back,catalog,profile)});
                        var finalLoaded=SparseSiteStore.Reconstruct(WorldStore.ReadSite(root,WorldStore.Read(root),back.SiteId),catalog,profile);
                        CollectionAssert.AreEqual(departure.Site.Matter.Cells,finalLoaded.Matter.Cells.Where(c=>(c.Flags&4)==0).ToArray());
                        CollectionAssert.AreEqual(departure.Portable.Matter.Cells,finalLoaded.Matter.Cells.Where(c=>(c.Flags&4)!=0).ToArray());
                        Assert.That(finalLoaded.Ship.Fuel.Energy,Is.EqualTo(current.Ship.Fuel.Energy));Assert.That(finalLoaded.Ship.Fragments.Single().Id,Is.EqualTo(current.Ship.Fragments.Single().Id));
                        session.Restore(finalLoaded.Matter);task=session.SnapshotAsync();while(!task.IsCompleted)yield return null;ExactSite(finalLoaded.Matter,task.Result);
                        var bSleeping=SparseSiteStore.Reconstruct(WorldStore.ReadSite(root,final,other.SiteId),catalog,profile);ExactSite(returning.Site.Matter,bSleeping.Matter);
                        var allIds=finalLoaded.Matter.Cells.Concat(bSleeping.Matter.Cells).Select(c=>c.Identity).ToArray();Assert.That(allIds.Distinct().Count(),Is.EqualTo(allIds.Length));
                        string note=Path.Combine(root,"sites","notes.txt");File.WriteAllText(note,"retain unrelated user notes");
                        var collection=WorldStore.CollectUnreferenced(root);Assert.That(collection.Deferred,Is.False);Assert.That(collection.DeletedFiles,Is.GreaterThan(0));Assert.That(File.Exists(note),Is.True);
                        ExactSite(finalLoaded.Matter,SparseSiteStore.Reconstruct(WorldStore.ReadSite(root,WorldStore.Read(root),back.SiteId),catalog,profile).Matter);
                        Assert.That(WorldStore.ReadSite(root,travelled,other.SiteId),Is.Not.Null);
                        Debug.Log($"DEBRIS_WORLD_COLLECTION kept_site_records={collection.KeptSites} deleted_files={collection.DeletedFiles} deleted_bytes={collection.DeletedBytes} both_roots_preserved=true");
                        string activeRecord=SparseSiteStore.RecordPath(root,back.SiteId,final.Revision);var activeBytes=File.ReadAllBytes(activeRecord);
                        File.WriteAllBytes(activeRecord,new byte[]{1,2,3});Assert.That(WorldStore.Read(root).RecoveredBackup,Is.True);File.WriteAllBytes(activeRecord,activeBytes);
                        string inactiveRecord=SparseSiteStore.RecordPath(root,other.SiteId,final.Revision);var inactiveBytes=File.ReadAllBytes(inactiveRecord);
                        File.WriteAllBytes(inactiveRecord,new byte[]{1,2,3});Assert.That(WorldStore.Read(root).RecoveredBackup,Is.False);Assert.Throws<InvalidDataException>(()=>WorldStore.ReadSite(root,final,other.SiteId));
                        int filesBefore=Directory.GetFiles(root,"*",SearchOption.AllDirectories).Length;Assert.That(WorldStore.CollectUnreferenced(root).Deferred,Is.True);Assert.That(Directory.GetFiles(root,"*",SearchOption.AllDirectories).Length,Is.EqualTo(filesBefore));File.WriteAllBytes(inactiveRecord,inactiveBytes);
                        File.WriteAllBytes(WorldStore.ManifestPath(root),new byte[]{1,2,3});Assert.That(WorldStore.Read(root).RecoveredBackup,Is.True);Assert.That(WorldStore.Read(root).Revision,Is.EqualTo(travelled.Revision));
                        Debug.Log($"DEBRIS_WORLD sparse_chunks={prepared.Chunks.Count} initial_blobs={blobs} first_save_ms={saveMs} files={Directory.GetFiles(root,"*",SearchOption.AllDirectories).Length} bytes={Directory.GetFiles(root,"*",SearchOption.AllDirectories).Sum(p=>new FileInfo(p).Length)} leave_revisit=true no_duplicates=true interrupted=true recovered=true");
                    }
                }
            }
            finally{UnityEngine.Object.DestroyImmediate(b);if(Directory.Exists(root))Directory.Delete(root,true);}
        }
    }
}
