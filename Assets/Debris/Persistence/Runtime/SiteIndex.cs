using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Debris.Core;
namespace Debris.Persistence
{
    public readonly struct SiteIndexEntry
    {
        public readonly string Id;
        public readonly long Revision;
        public SiteIndexEntry(string id,long revision){Id=new StableId(id).Value;if(revision<0)throw new ArgumentOutOfRangeException(nameof(revision));Revision=revision;}
    }
    // Sorted fixed-size records are addressable directly. Opening never loads site payloads.
    public sealed class SiteIndex : IDisposable
    {
        const int Magic=0x44534958,Header=16,Record=40;
        static readonly object Gate=new object();
        readonly FileStream file;
        readonly BinaryReader reader;
        public long Count {get;}
        public bool RecoveredBackup {get;}
        public SiteIndex(string path)
        {
            try{file=OpenVerified(path);}
            catch(Exception e)when(e is IOException||e is InvalidDataException){if(!File.Exists(path+".backup"))throw;file=OpenVerified(path+".backup");RecoveredBackup=true;}
            reader=new BinaryReader(file,Encoding.ASCII,true);file.Position=8;Count=reader.ReadInt64();
        }
        static FileStream OpenVerified(string path)
        {
            var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete);
            try
            {
                using(var r=new BinaryReader(f,Encoding.ASCII,true))
                {
                    if(r.ReadInt32()!=Magic)throw new InvalidDataException("Invalid site index.");
                    if(r.ReadInt32()!=1)throw new NotSupportedException("Unsupported site index schema.");
                    long count=r.ReadInt64();if(count<0||count>(long.MaxValue-Header-32)/Record||f.Length!=Header+count*Record+32)throw new InvalidDataException("Invalid index size.");
                    f.Position=0;var actual=HashPrefix(f,f.Length-32);var expected=r.ReadBytes(32);
                    if(!actual.SequenceEqual(expected))throw new InvalidDataException("Site index checksum failed.");
                }
                return f;
            }
            catch{f.Dispose();throw;}
        }
        static byte[] HashPrefix(Stream stream,long length)
        {
            using(var hash=SHA256.Create())
            {
                var buffer=new byte[65536];long remaining=length;
                while(remaining>0){int read=stream.Read(buffer,0,(int)Math.Min(buffer.Length,remaining));if(read==0)throw new EndOfStreamException();hash.TransformBlock(buffer,0,read,buffer,0);remaining-=read;}
                hash.TransformFinalBlock(Array.Empty<byte>(),0,0);return hash.Hash;
            }
        }
        SiteIndexEntry At(long index)
        {
            file.Position=Header+index*Record;return new SiteIndexEntry(Encoding.ASCII.GetString(reader.ReadBytes(32)),reader.ReadInt64());
        }
        public bool TryGet(string id,out SiteIndexEntry entry)
        {
            id=new StableId(id).Value;long low=0,high=Count-1;
            lock(file)
            {
                while(low<=high){long middle=low+(high-low)/2;var candidate=At(middle);int comparison=string.CompareOrdinal(candidate.Id,id);if(comparison==0){entry=candidate;return true;}if(comparison<0)low=middle+1;else high=middle-1;}
            }
            entry=default;return false;
        }
        public IEnumerable<SiteIndexEntry> Entries()
        {
            for(long i=0;i<Count;i++){SiteIndexEntry entry;lock(file)entry=At(i);yield return entry;}
        }
        public static void Merge(string path,IEnumerable<SiteIndexEntry> changes)
        {
            var updates=changes.OrderBy(e=>e.Id,StringComparer.Ordinal).ToArray();
            for(int i=0;i<updates.Length;i++){new StableId(updates[i].Id);if(updates[i].Revision<0||i>0&&updates[i].Id==updates[i-1].Id)throw new ArgumentException("Invalid/duplicate site update.");}
            lock(Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                using(var prior=File.Exists(path)||File.Exists(path+".backup")?new SiteIndex(path):null)
                {
                    string pending=path+".pending";long old=0,count=0;int next=0;
                    using(var output=new FileStream(pending,FileMode.Create,FileAccess.ReadWrite,FileShare.None))using(var w=new BinaryWriter(output,Encoding.ASCII,true))
                    {
                        w.Write(Magic);w.Write(1);w.Write(0L);
                        while(old<(prior?.Count??0)||next<updates.Length)
                        {
                            var existing=old<(prior?.Count??0)?prior.At(old):default;
                            bool useUpdate=next<updates.Length&&(existing.Id==null||string.CompareOrdinal(updates[next].Id,existing.Id)<=0);
                            SiteIndexEntry chosen;
                            if(useUpdate){chosen=updates[next++];if(chosen.Id==existing.Id){if(chosen.Revision<existing.Revision)throw new InvalidOperationException("Site revision cannot move backwards.");old++;}}
                            else{chosen=existing;old++;}
                            w.Write(Encoding.ASCII.GetBytes(chosen.Id));w.Write(chosen.Revision);count++;
                        }
                        output.Position=8;w.Write(count);w.Flush();output.Position=0;var hash=HashPrefix(output,output.Length);output.Position=output.Length;w.Write(hash);w.Flush();output.Flush(true);
                    }
                    using(var verified=new SiteIndex(pending)){if(verified.Count!=count)throw new InvalidDataException("Index verification failed.");}
                    if(File.Exists(path))File.Replace(pending,path,prior!=null&&!prior.RecoveredBackup?path+".backup":null);else File.Move(pending,path);
                }
            }
        }
        public void Dispose(){reader.Dispose();file.Dispose();}
    }
}
