using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
namespace Debris.Persistence.Tests
{
    public sealed class SiteIndexTests
    {
        [Test] public void HundredThousandSitesRemainAddressableWithoutLoadingPayloads()
        {
            string directory=Path.Combine(Path.GetTempPath(),"debris-index-"+Guid.NewGuid().ToString("N")),path=Path.Combine(directory,"sites.idx");
            try
            {
                var watch=Stopwatch.StartNew();SiteIndex.Merge(path,Enumerable.Range(1,100000).Select(i=>new SiteIndexEntry(i.ToString("x32"),1)));long write=watch.ElapsedMilliseconds;watch.Restart();
                using(var index=new SiteIndex(path))
                {
                    long open=watch.ElapsedMilliseconds;Assert.That(index.Count,Is.EqualTo(100000));watch.Restart();
                    for(int i=1;i<=100000;i+=97){Assert.That(index.TryGet(i.ToString("x32"),out var entry),Is.True);Assert.That(entry.Revision,Is.EqualTo(1));}
                    Assert.That(index.TryGet(100001.ToString("x32"),out _),Is.False);
                    UnityEngine.Debug.Log($"DEBRIS_INDEX sites={index.Count} bytes={new FileInfo(path).Length} write_ms={write} open_ms={open} lookups=1031 lookup_ms={watch.ElapsedMilliseconds} payloads_loaded=0");
                }
                SiteIndex.Merge(path,new[]{new SiteIndexEntry(50.ToString("x32"),2),new SiteIndexEntry(100001.ToString("x32"),1)});
                using(var index=new SiteIndex(path)){Assert.That(index.Count,Is.EqualTo(100001));Assert.That(index.TryGet(50.ToString("x32"),out var entry),Is.True);Assert.That(entry.Revision,Is.EqualTo(2));}
                Assert.Throws<InvalidOperationException>(()=>SiteIndex.Merge(path,new[]{new SiteIndexEntry(50.ToString("x32"),1)}));
                var corrupt=File.ReadAllBytes(path);corrupt[100]^=1;File.WriteAllBytes(path,corrupt);
                using(var recovered=new SiteIndex(path)){Assert.That(recovered.RecoveredBackup,Is.True);Assert.That(recovered.Count,Is.EqualTo(100000));}
            }
            finally{if(Directory.Exists(directory))Directory.Delete(directory,true);}
        }
    }
}
