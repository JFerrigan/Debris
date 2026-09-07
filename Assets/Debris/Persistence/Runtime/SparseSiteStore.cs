using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using Debris.Core;
using Debris.Materials;
using Debris.Simulation;
using Debris.Sites;
namespace Debris.Persistence
{
    public sealed class SparseSiteData
    {
        public SalvageSave Save;
        public string ShipJson,BaselineHash;
        public readonly Dictionary<int,byte[]> Chunks=new Dictionary<int,byte[]>();
    }
    // Immutable, content-addressed blobs are published only by a world manifest transaction.
    public static class SparseSiteStore
    {
        const int Magic=0x44535350;
        public static uint[][] Baseline(SalvageSave save,MaterialCatalog catalog,AsteroidProfile profile)
        {
            if(save.GeneratorKey!="asteroid"||save.GeneratorRevision!=1)throw new NotSupportedException("The site's generator revision is unavailable.");
            var s=save.Matter;var result=new uint[s.Side*s.Side][];
            for(int y=0;y<s.Side;y++)for(int x=0;x<s.Side;x++)
                result[y*s.Side+x]=AsteroidGenerator.GenerateChunk(save.GeneratorSeed,new StableId(save.SiteId),s.OriginX/s.ChunkSize+x,s.OriginY/s.ChunkSize+y,s.ChunkSize,profile,catalog).Select(v=>(uint)v).ToArray();
            return result;
        }
        public static SparseSiteData Prepare(SalvageSave source,string shipJson,uint[][] baseline)
        {
            CpuCutReference.Validate(source.Matter);var s=FuelTransfers.Copy(source.Matter);
            if(baseline.Length!=s.Fields.Length||baseline.Any(c=>c.Length!=s.ChunkSize*s.ChunkSize))throw new ArgumentException("Baseline geometry mismatch.");
            var result=new SparseSiteData{ShipJson=shipJson,BaselineHash=HashFields(baseline)};
            for(int i=0;i<s.Fields.Length;i++)
                if(!s.Fields[i].SequenceEqual(baseline[i])||s.Damage[i].Any(v=>v!=0))result.Chunks.Add(i,PackChunk(s.Fields[i],s.Damage[i]));
            s.Fields=baseline.Select(c=>new uint[c.Length]).ToArray();s.Damage=baseline.Select(c=>new float[c.Length]).ToArray();s.Counters[1]=(uint)s.Cells.Length;
            result.Save=new SalvageSave{SiteId=source.SiteId,GeneratorKey=source.GeneratorKey,GeneratorRevision=source.GeneratorRevision,GeneratorSeed=source.GeneratorSeed,MaterialKeys=(string[])source.MaterialKeys.Clone(),Matter=s};
            return result;
        }
        public static SalvageSave Reconstruct(SparseSiteData data,MaterialCatalog catalog,AsteroidProfile profile)
        {
            var save=data.Save;var s=save.Matter;
            // Verify generation in the saved catalog order before remapping all records together.
            var oldKeys=save.MaterialKeys.Select((key,i)=>(key,index:(uint)i+1)).ToDictionary(p=>p.key,p=>p.index);
            var current=SalvageSave.Keys(catalog);var map=new uint[current.Length+1];
            for(int i=0;i<current.Length;i++)if(oldKeys.TryGetValue(current[i],out uint saved))map[i+1]=saved;
            var baseline=Baseline(save,catalog,profile);
            foreach(var chunk in baseline)for(int i=0;i<chunk.Length;i++)
            {uint value=chunk[i];if(value!=0&&map[value]==0)throw new NotSupportedException("Generated material is absent from this save's content table.");chunk[i]=map[value];}
            if(HashFields(baseline)!=data.BaselineHash)throw new NotSupportedException("Generator baseline changed without a compatible revision; original site retained.");
            s.Fields=baseline;s.Damage=baseline.Select(c=>new float[c.Length]).ToArray();
            foreach(var patch in data.Chunks)
            {
                using(var raw=new MemoryStream(patch.Value))using(var zip=new DeflateStream(raw,CompressionMode.Decompress))using(var r=new BinaryReader(zip))
                {
                    for(int i=0;i<s.Fields[patch.Key].Length;i++)s.Fields[patch.Key][i]=r.ReadUInt32();
                    for(int i=0;i<s.Damage[patch.Key].Length;i++)s.Damage[patch.Key][i]=r.ReadSingle();
                    if(zip.ReadByte()!=-1)throw new InvalidDataException("Trailing chunk data.");
                }
            }
            s.Counters[1]=(uint)s.Cells.Length;foreach(var field in s.Fields)foreach(uint material in field)if(material!=0)s.Counters[1]++;
            SalvageSaveCodec.ResolveContent(save,data.ShipJson,catalog);CpuCutReference.Validate(s);return save;
        }
        static byte[] PackChunk(uint[] field,float[] damage)
        {
            using(var stream=new MemoryStream())
            {using(var zip=new DeflateStream(stream,CompressionLevel.Optimal,true))using(var w=new BinaryWriter(zip)){foreach(uint v in field)w.Write(v);foreach(float v in damage)w.Write(v);}return stream.ToArray();}
        }
        static string HashFields(uint[][] fields)
        {
            using(var hash=SHA256.Create())
            {
                foreach(var field in fields){var bytes=new byte[field.Length*4];Buffer.BlockCopy(field,0,bytes,0,bytes.Length);hash.TransformBlock(bytes,0,bytes.Length,bytes,0);}
                hash.TransformFinalBlock(Array.Empty<byte>(),0,0);return Hex(hash.Hash);
            }
        }
        internal static string Hex(byte[] bytes)=>BitConverter.ToString(bytes).Replace("-","").ToLowerInvariant();
        internal static string Digest(byte[] bytes){using(var hash=SHA256.Create())return Hex(hash.ComputeHash(bytes));}
        internal static void Flush(string path,byte[] bytes)
        {Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));using(var f=new FileStream(path,FileMode.Create,FileAccess.Write,FileShare.None)){f.Write(bytes,0,bytes.Length);f.Flush(true);}}
        internal static string BlobPath(string root,string hash)
        {
            if(hash==null||hash.Length!=64||hash.Any(c=>!(c>='0'&&c<='9'||c>='a'&&c<='f')))throw new InvalidDataException("Invalid blob address.");
            return Path.Combine(root,"blobs",hash.Substring(0,2),hash+".blob");
        }
        static string PutBlob(string root,byte[] bytes)
        {
            string hash=Digest(bytes),path=BlobPath(root,hash);
            if(File.Exists(path)){if(Digest(File.ReadAllBytes(path))!=hash)throw new InvalidDataException("An existing site blob is damaged; committed history retained.");return hash;}
            string pending=path+".pending";Flush(pending,bytes);if(Digest(File.ReadAllBytes(pending))!=hash)throw new IOException("Blob verification failed.");File.Move(pending,path);return hash;
        }
        static byte[] ReadBlob(string root,string hash)
        {
            string path=BlobPath(root,hash);if(new FileInfo(path).Length>256*1024*1024)throw new InvalidDataException("Oversized site blob.");
            var bytes=File.ReadAllBytes(path);if(Digest(bytes)!=hash)throw new InvalidDataException("Site blob checksum failed.");return bytes;
        }
        public static string RecordPath(string root,string id,long revision)
        {new StableId(id);if(revision<1)throw new ArgumentOutOfRangeException(nameof(revision));return Path.Combine(root,"sites",id,revision+".site");}
        public static void Write(string root,long revision,SparseSiteData data)
        {
            var source=data.Save;var core=FuelTransfers.Copy(source.Matter);core.Cells=Array.Empty<LooseCell>();core.FuelCells=Array.Empty<FuelCellState>();core.Counters[0]=core.Counters[1]=0;
            var metadata=new SalvageSave{SiteId=source.SiteId,GeneratorKey=source.GeneratorKey,GeneratorRevision=source.GeneratorRevision,GeneratorSeed=source.GeneratorSeed,MaterialKeys=source.MaterialKeys,Matter=core};
            string state=PutBlob(root,SalvageSaveCodec.Encode(metadata,data.ShipJson));var buckets=SpatialCellCodec.Encode(source.Matter);byte[] bytes;
            using(var stream=new MemoryStream())using(var w=new BinaryWriter(stream))
            {
                w.Write(Magic);w.Write(2);w.Write(data.BaselineHash);w.Write(state);w.Write(data.Chunks.Count);
                foreach(var chunk in data.Chunks.OrderBy(c=>c.Key)){w.Write(chunk.Key);w.Write(PutBlob(root,chunk.Value));}
                w.Write(buckets.Count);foreach(var bucket in buckets.OrderBy(b=>b.Key,StringComparer.Ordinal)){w.Write(bucket.Key);w.Write(PutBlob(root,bucket.Value));}
                bytes=stream.ToArray();
            }
            string path=RecordPath(root,data.Save.SiteId,revision);var sealedBytes=Seal(bytes);
            if(File.Exists(path))
            {if(!File.ReadAllBytes(path).SequenceEqual(sealedBytes))throw new InvalidOperationException("Committed site revisions are immutable.");}
            else{Flush(path+".pending",sealedBytes);File.Move(path+".pending",path);}
            Read(root,data.Save.SiteId,revision);
        }
        public static SparseSiteData Read(string root,string id,long revision)
        {
            var bytes=Unseal(File.ReadAllBytes(RecordPath(root,id,revision)));
            using(var stream=new MemoryStream(bytes))using(var r=new BinaryReader(stream))
            {
                if(r.ReadInt32()!=Magic)throw new InvalidDataException("Invalid sparse site record.");int schema=r.ReadInt32();if(schema<1||schema>2)throw new NotSupportedException("Unsupported sparse site schema.");
                string baseline=r.ReadString(),state=r.ReadString();var save=SalvageSaveCodec.Decode(ReadBlob(root,state),out var json);
                if(save.SiteId!=id)throw new InvalidDataException("Site identity mismatch.");
                var result=new SparseSiteData{Save=save,ShipJson=json,BaselineHash=baseline};int count=r.ReadInt32();
                if(count<0||count>save.Matter.Fields.Length)throw new InvalidDataException("Invalid changed chunk count.");
                for(int n=0;n<count;n++){int i=r.ReadInt32();if(i<0||i>=save.Matter.Fields.Length||result.Chunks.ContainsKey(i))throw new InvalidDataException("Invalid changed chunk index.");result.Chunks.Add(i,ReadBlob(root,r.ReadString()));}
                if(schema>=2)
                {
                    int bucketCount=r.ReadInt32();if(bucketCount<0||bucketCount>save.Matter.Capacity)throw new InvalidDataException("Invalid spatial bucket count.");
                    var buckets=new Dictionary<string,byte[]>();for(int i=0;i<bucketCount;i++){string key=r.ReadString();if(buckets.ContainsKey(key))throw new InvalidDataException("Duplicate spatial bucket.");buckets.Add(key,ReadBlob(root,r.ReadString()));}
                    SpatialCellCodec.Decode(save.Matter,buckets);
                }
                if(stream.Position!=stream.Length)throw new InvalidDataException("Trailing site metadata.");return result;
            }
        }
        internal static string[] ReferencedBlobs(string root,string id,long revision)
        {
            using(var stream=new MemoryStream(Unseal(File.ReadAllBytes(RecordPath(root,id,revision)))))using(var r=new BinaryReader(stream))
            {
                if(r.ReadInt32()!=Magic)throw new InvalidDataException("Invalid sparse site record.");int schema=r.ReadInt32();if(schema<1||schema>2)throw new NotSupportedException("Unsupported sparse site schema.");
                r.ReadString();var paths=new List<string>{BlobPath(root,r.ReadString())};int chunks=r.ReadInt32();
                if(chunks<0||chunks>256)throw new InvalidDataException("Invalid changed chunk count.");
                var indices=new HashSet<int>();for(int i=0;i<chunks;i++){int index=r.ReadInt32();if(index<0||index>=256||!indices.Add(index))throw new InvalidDataException("Invalid changed chunk index.");paths.Add(BlobPath(root,r.ReadString()));}
                if(schema>=2)
                {
                    int buckets=r.ReadInt32();if(buckets<0||buckets>1000000)throw new InvalidDataException("Invalid spatial bucket count.");
                    var keys=new HashSet<string>();for(int i=0;i<buckets;i++){if(!keys.Add(r.ReadString()))throw new InvalidDataException("Duplicate spatial bucket.");paths.Add(BlobPath(root,r.ReadString()));}
                }
                if(stream.Position!=stream.Length)throw new InvalidDataException("Trailing site metadata.");return paths.ToArray();
            }
        }
        internal static byte[] Seal(byte[] bytes){using(var hash=SHA256.Create())return bytes.Concat(hash.ComputeHash(bytes)).ToArray();}
        internal static byte[] Unseal(byte[] bytes)
        {
            if(bytes.Length<40)throw new InvalidDataException("Truncated record.");var body=bytes.Take(bytes.Length-32).ToArray();
            using(var hash=SHA256.Create())if(!hash.ComputeHash(body).SequenceEqual(bytes.Skip(body.Length)))throw new InvalidDataException("Record checksum failed.");return body;
        }
    }
}
