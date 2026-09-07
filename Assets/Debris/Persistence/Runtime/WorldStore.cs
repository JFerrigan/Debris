using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Debris.Core;
namespace Debris.Persistence
{
    public sealed class WorldManifest
    {
        public long Revision;
        public string ActiveSiteId;
        public uint NextIdentity;
        public bool RecoveredBackup;
    }
    public sealed class WorldCollectionResult
    {
        public long KeptSites,DeletedFiles,DeletedBytes;
        public bool Deferred;
        public string Reason;
    }
    public static class WorldStore
    {
        const int Magic=0x44535752;
        static readonly object Gate=new object();
        public static string ManifestPath(string root)=>Path.Combine(root,"world.manifest");
        public static string IndexPath(string root,long revision)=>Path.Combine(root,"indices",revision+".index");
        public static bool Exists(string root)=>File.Exists(ManifestPath(root))||File.Exists(ManifestPath(root)+".backup");
        static byte[] Encode(WorldManifest manifest)
        {
            using(var stream=new MemoryStream())using(var w=new BinaryWriter(stream))
            {w.Write(Magic);w.Write(1);w.Write(manifest.Revision);w.Write(manifest.ActiveSiteId);w.Write(manifest.NextIdentity);return SparseSiteStore.Seal(stream.ToArray());}
        }
        static WorldManifest Decode(byte[] bytes)
        {
            using(var stream=new MemoryStream(SparseSiteStore.Unseal(bytes)))using(var r=new BinaryReader(stream))
            {
                if(r.ReadInt32()!=Magic)throw new InvalidDataException("Invalid world manifest.");if(r.ReadInt32()!=1)throw new NotSupportedException("Unsupported world schema; original retained.");
                var result=new WorldManifest{Revision=r.ReadInt64(),ActiveSiteId=r.ReadString(),NextIdentity=r.ReadUInt32()};new StableId(result.ActiveSiteId);
                if(result.Revision<1||result.NextIdentity<1||stream.Position!=stream.Length)throw new InvalidDataException("Invalid world manifest values.");return result;
            }
        }
        static WorldManifest ReadGeneration(string root,string path)
        {
            var manifest=Decode(File.ReadAllBytes(path));
            using(var index=new SiteIndex(IndexPath(root,manifest.Revision)))
            {
                if(index.RecoveredBackup||!index.TryGet(manifest.ActiveSiteId,out var entry))throw new InvalidDataException("Active site index is unavailable.");
                SparseSiteStore.Read(root,entry.Id,entry.Revision);
            }
            return manifest;
        }
        public static WorldManifest Read(string root)
        {
            lock(Gate)
            {
                try{return ReadGeneration(root,ManifestPath(root));}
                catch(Exception e)when(e is IOException||e is InvalidDataException)
                {
                    if(!File.Exists(ManifestPath(root)+".backup"))throw;
                    var prior=ReadGeneration(root,ManifestPath(root)+".backup");prior.RecoveredBackup=true;return prior;
                }
            }
        }
        public static SparseSiteData ReadSite(string root,WorldManifest manifest,string id)
        {
            using(var index=new SiteIndex(IndexPath(root,manifest.Revision)))
            {
                if(index.RecoveredBackup)throw new InvalidDataException("Committed index is damaged.");
                return index.TryGet(id,out var entry)?SparseSiteStore.Read(root,id,entry.Revision):null;
            }
        }
        // Mark both complete roots before deleting anything. A damaged record defers the entire collection.
        public static WorldCollectionResult CollectUnreferenced(string root)
        {
            lock(Gate)
            {
                var result=new WorldCollectionResult();if(!Exists(root))return result;
                using(var lease=new FileStream(Path.Combine(root,"writer.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
                {
                    var keep=new HashSet<string>(StringComparer.Ordinal);var blobs=new HashSet<string>(StringComparer.Ordinal);
                    try
                    {
                        foreach(string path in new[]{ManifestPath(root),ManifestPath(root)+".backup"})
                        {
                            if(!File.Exists(path)){if(path==ManifestPath(root))throw new InvalidDataException("Primary root missing.");continue;}
                            var manifest=ReadGeneration(root,path);string indexPath=IndexPath(root,manifest.Revision);keep.Add(indexPath);
                            using(var index=new SiteIndex(indexPath))foreach(var entry in index.Entries())
                            {
                                string record=SparseSiteStore.RecordPath(root,entry.Id,entry.Revision);
                                if(!keep.Add(record))continue;result.KeptSites++;
                                foreach(string blob in SparseSiteStore.ReferencedBlobs(root,entry.Id,entry.Revision))blobs.Add(blob);
                            }
                        }
                        // Missing/corrupt live dependencies must remain available for explicit recovery or repair.
                        foreach(string blob in blobs)
                        {if(SparseSiteStore.Digest(File.ReadAllBytes(blob))!=Path.GetFileNameWithoutExtension(blob))throw new InvalidDataException("Live world blob checksum failed.");keep.Add(blob);}
                    }
                    catch(Exception e)when(e is IOException||e is InvalidDataException||e is NotSupportedException)
                    {result.Deferred=true;result.Reason=e.Message;return result;}
                    foreach(string directory in new[]{"indices","sites","blobs"})
                    {
                        string path=Path.Combine(root,directory);if(!Directory.Exists(path))continue;
                        foreach(string file in Directory.EnumerateFiles(path,"*",SearchOption.AllDirectories))
                        {
                            // Restrict deletion to files owned by this store; retain any unrelated user files.
                            if(keep.Contains(file)||!(file.EndsWith(".index",StringComparison.Ordinal)||file.EndsWith(".index.backup",StringComparison.Ordinal)||file.EndsWith(".site",StringComparison.Ordinal)||file.EndsWith(".blob",StringComparison.Ordinal)||file.EndsWith(".pending",StringComparison.Ordinal)))continue;
                            long bytes=new FileInfo(file).Length;File.Delete(file);result.DeletedFiles++;result.DeletedBytes+=bytes;
                        }
                    }
                    string pending=ManifestPath(root)+".pending";if(File.Exists(pending)){result.DeletedBytes+=new FileInfo(pending).Length;File.Delete(pending);result.DeletedFiles++;}
                    return result;
                }
            }
        }
        // Write all immutable dependencies first; atomically publishing this root commits both sides of travel.
        // The expected revision prevents a stale asynchronous session from overwriting another commit.
        public static WorldManifest Commit(string root,long expectedRevision,string activeSite,uint nextIdentity,SparseSiteData[] changes,Action beforePublish=null)
        {
            new StableId(activeSite);if(nextIdentity<1||changes.Length<1||changes.Select(c=>c.Save.SiteId).Distinct().Count()!=changes.Length||!changes.Any(c=>c.Save.SiteId==activeSite&&c.Save.Matter.ShipEnabled)||changes.Any(c=>c.Save.SiteId!=activeSite&&c.Save.Matter.ShipEnabled))throw new ArgumentException("Invalid world transaction/player ownership.");
            lock(Gate)
            {
                Directory.CreateDirectory(root);
                using(var lease=new FileStream(Path.Combine(root,"writer.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
                {
                    var prior=Exists(root)?Read(root):null;
                    if((prior?.Revision??0)!=expectedRevision)throw new InvalidOperationException("World changed since this session loaded; reload before saving.");
                    if(prior!=null&&nextIdentity<prior.NextIdentity)throw new InvalidOperationException("World cell identity cannot move backwards.");
                    long revision=Math.Max(DateTime.UtcNow.Ticks,checked(expectedRevision+1));
                    foreach(var change in changes)
                    {
                        if(change.Save.Matter.NextIdentity>nextIdentity)throw new InvalidOperationException("World identity is behind a changed site.");
                        SparseSiteStore.Write(root,revision,change);
                    }
                    string indexPath=IndexPath(root,revision);Directory.CreateDirectory(Path.GetDirectoryName(indexPath));
                    if(prior!=null)File.Copy(IndexPath(root,prior.Revision),indexPath);
                    SiteIndex.Merge(indexPath,changes.Select(c=>new SiteIndexEntry(c.Save.SiteId,revision)));
                    var manifest=new WorldManifest{Revision=revision,ActiveSiteId=activeSite,NextIdentity=nextIdentity};
                    string path=ManifestPath(root),pending=path+".pending";
                    SparseSiteStore.Flush(pending,Encode(manifest));ReadGeneration(root,pending);beforePublish?.Invoke();
                    if(File.Exists(path))File.Replace(pending,path,prior!=null&&!prior.RecoveredBackup?path+".backup":null);else File.Move(pending,path);
                    return manifest;
                }
            }
        }
    }
}
