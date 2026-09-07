using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Debris.Core;
using Debris.Materials;
using Debris.Ships;
using Debris.Simulation;
using UnityEngine;
namespace Debris.Persistence
{
    public sealed class SalvageSave
    {
        public string SiteId="00000000000000000000000000000001",GeneratorKey="asteroid";
        public int GeneratorRevision=1;
        public ulong GeneratorSeed=42;
        public MatterSnapshot Matter;
        public ShipSnapshot Ship;
        public string[] MaterialKeys;
        public bool RecoveredBackup;
        public static string[] Keys(MaterialCatalog catalog)=>Enumerable.Range(1,catalog.Count).Select(i=>catalog.DefinitionAt((ushort)i).MaterialKey).ToArray();
    }

    // Versioned, compressed exact checkpoint. No Unity API is called by the disk worker.
    public static class SalvageSaveCodec
    {
        public const int Schema=3;
        const int Magic=0x44534252,MaxBytes=256*1024*1024;
        public static byte[] Encode(SalvageSave save,string shipJson)
        {
            CpuCutReference.Validate(save.Matter);new StableId(save.SiteId);
            byte[] raw;
            using(var stream=new MemoryStream())
            {
                using(var w=new BinaryWriter(stream,Encoding.UTF8,true))
                {
                    w.Write(save.SiteId);w.Write(save.GeneratorKey);w.Write(save.GeneratorRevision);w.Write(save.GeneratorSeed);
                    w.Write(save.MaterialKeys.Length);foreach(var key in save.MaterialKeys)w.Write(key);
                    w.Write(shipJson??"");var s=save.Matter;
                    w.Write(s.Side);w.Write(s.ChunkSize);w.Write(s.Capacity);w.Write(s.OriginX);w.Write(s.OriginY);w.Write(s.Tick);
                    foreach(var v in s.Counters)w.Write(v);foreach(var v in s.Dirty)w.Write(v);
                    foreach(var chunk in s.Fields)foreach(var v in chunk)w.Write(v);
                    foreach(var chunk in s.Damage)foreach(var v in chunk)w.Write(v);
                    w.Write(s.Cells.Length);foreach(var c in s.Cells){w.Write(c.Position.x);w.Write(c.Position.y);w.Write(c.Velocity.x);w.Write(c.Velocity.y);w.Write(c.Material);w.Write(c.Identity);w.Write(c.Step);w.Write(c.Flags);}
                    w.Write(s.NextIdentity);w.Write(s.FuelCells.Length);foreach(var fuel in s.FuelCells){w.Write(fuel.Identity);w.Write(fuel.Energy);}
                    foreach(uint value in s.Impact)w.Write(value);
                    w.Write(s.Fragments.Length);
                    foreach(var f in s.Fragments){w.Write(f.Id);foreach(uint m in f.Hull)w.Write(m);w.Write(f.Pose.x);w.Write(f.Pose.y);w.Write(f.Pose.z);w.Write(f.Pose.w);w.Write(f.Motion.x);w.Write(f.Motion.y);w.Write(f.Motion.z);w.Write(f.Motion.w);}
                    w.Write(s.ShipEnabled);
                    if(s.ShipEnabled){foreach(var v in s.Hull)w.Write(v);foreach(var p in s.ShipPose){w.Write(p.x);w.Write(p.y);w.Write(p.z);w.Write(p.w);}}
                }
                raw=stream.ToArray();
            }
            using(var output=new MemoryStream())
            {
                using(var w=new BinaryWriter(output,Encoding.UTF8,true))
                {w.Write(Magic);w.Write(Schema);w.Write(raw.Length);using(var hash=SHA256.Create())w.Write(hash.ComputeHash(raw));}
                using(var zip=new DeflateStream(output,System.IO.Compression.CompressionLevel.Optimal,true))zip.Write(raw,0,raw.Length);
                return output.ToArray();
            }
        }
        public static SalvageSave Decode(byte[] bytes,out string shipJson)
        {
            using(var input=new MemoryStream(bytes))using(var reader=new BinaryReader(input))
            {
                if(reader.ReadInt32()!=Magic)throw new InvalidDataException("Not a Debris save.");
                int version=reader.ReadInt32();if(version<1||version>Schema)throw new NotSupportedException("Save schema "+version+" is not supported; original file retained.");
                int length=reader.ReadInt32();if(length<0||length>MaxBytes)throw new InvalidDataException("Save exceeds safe decode size.");
                var expected=reader.ReadBytes(32);var raw=new byte[length];
                using(var zip=new DeflateStream(input,CompressionMode.Decompress,true))
                {
                    int offset=0;while(offset<length){int n=zip.Read(raw,offset,length-offset);if(n==0)throw new InvalidDataException("Truncated save.");offset+=n;}
                    if(zip.ReadByte()!=-1)throw new InvalidDataException("Unexpected save data.");
                }
                using(var hash=SHA256.Create())if(!hash.ComputeHash(raw).SequenceEqual(expected))throw new InvalidDataException("Save checksum failed.");
                using(var stream=new MemoryStream(raw))using(var r=new BinaryReader(stream))
                {
                    var save=new SalvageSave{SiteId=r.ReadString(),GeneratorKey=r.ReadString(),GeneratorRevision=r.ReadInt32(),GeneratorSeed=r.ReadUInt64()};new StableId(save.SiteId);
                    int keys=r.ReadInt32();if(keys<1||keys>65535)throw new InvalidDataException("Invalid catalog size.");
                    save.MaterialKeys=new string[keys];for(int i=0;i<keys;i++)save.MaterialKeys[i]=r.ReadString();
                    if(save.MaterialKeys.Distinct().Count()!=keys)throw new InvalidDataException("Duplicate content keys.");
                    shipJson=r.ReadString();var s=new MatterSnapshot{Side=r.ReadInt32(),ChunkSize=r.ReadInt32(),Capacity=r.ReadInt32(),OriginX=r.ReadInt32(),OriginY=r.ReadInt32(),Tick=r.ReadInt32()};save.Matter=s;
                    if(s.Side<2||s.Side>16||s.Side%2!=0||s.ChunkSize<8||s.ChunkSize>256||s.Capacity<1||s.Capacity>1000000)throw new InvalidDataException("Unsupported snapshot geometry.");
                    s.Counters=new uint[4];for(int i=0;i<4;i++)s.Counters[i]=r.ReadUInt32();
                    int chunks=s.Side*s.Side,area=s.ChunkSize*s.ChunkSize;
                    if((long)chunks*area*8>raw.Length)throw new InvalidDataException("Truncated fields.");
                    s.Dirty=new uint[chunks];for(int i=0;i<chunks;i++)s.Dirty[i]=r.ReadUInt32();
                    s.Fields=new uint[chunks][];s.Damage=new float[chunks][];
                    for(int i=0;i<chunks;i++){s.Fields[i]=new uint[area];for(int j=0;j<area;j++)s.Fields[i][j]=r.ReadUInt32();}
                    for(int i=0;i<chunks;i++){s.Damage[i]=new float[area];for(int j=0;j<area;j++)s.Damage[i][j]=r.ReadSingle();}
                    int count=r.ReadInt32();if(count<0||count>s.Capacity)throw new InvalidDataException("Invalid loose count.");
                    s.Cells=new LooseCell[count];for(int i=0;i<count;i++)s.Cells[i]=new LooseCell{Position=new Vector2(r.ReadSingle(),r.ReadSingle()),Velocity=new Vector2(r.ReadSingle(),r.ReadSingle()),Material=r.ReadUInt32(),Identity=r.ReadUInt32(),Step=r.ReadUInt32(),Flags=r.ReadUInt32()};
                    if(version>=2)
                    {
                        s.NextIdentity=r.ReadUInt32();int fuels=r.ReadInt32();if(fuels<0||fuels>count)throw new InvalidDataException("Invalid fuel state count.");
                        s.FuelCells=new FuelCellState[fuels];for(int i=0;i<fuels;i++)s.FuelCells[i]=new FuelCellState{Identity=r.ReadUInt32(),Energy=r.ReadDouble()};
                    }
                    else {uint maximum=0;foreach(var cell in s.Cells)maximum=Math.Max(maximum,cell.Identity);s.NextIdentity=checked(maximum+1);}
                    if(version>=3)
                    {
                        for(int i=0;i<4;i++)s.Impact[i]=r.ReadUInt32();
                        int fragments=r.ReadInt32();if(fragments<0||fragments>16)throw new InvalidDataException("Invalid active fragment count.");
                        s.Fragments=new RigidFragmentSnapshot[fragments];
                        for(int i=0;i<fragments;i++)
                        {
                            var f=new RigidFragmentSnapshot{Id=r.ReadString(),Hull=new uint[16384]};new StableId(f.Id);
                            for(int j=0;j<f.Hull.Length;j++)f.Hull[j]=r.ReadUInt32();
                            f.Pose=new Vector4(r.ReadSingle(),r.ReadSingle(),r.ReadSingle(),r.ReadSingle());f.Motion=new Vector4(r.ReadSingle(),r.ReadSingle(),r.ReadSingle(),r.ReadSingle());s.Fragments[i]=f;
                        }
                    }
                    s.ShipEnabled=r.ReadBoolean();s.Hull=new uint[16384];s.ShipPose=new Vector4[3];
                    if(s.ShipEnabled){for(int i=0;i<s.Hull.Length;i++)s.Hull[i]=r.ReadUInt32();for(int i=0;i<3;i++)s.ShipPose[i]=new Vector4(r.ReadSingle(),r.ReadSingle(),r.ReadSingle(),r.ReadSingle());}
                    if(stream.Position!=stream.Length)throw new InvalidDataException("Trailing save data.");
                    CpuCutReference.Validate(s);return save;
                }
            }
        }
        // Material keys survive catalog reordering; machinery and structural hull stay distinct.
        public static void ResolveContent(SalvageSave save,string shipJson,MaterialCatalog catalog)
        {
            var map=new uint[save.MaterialKeys.Length+1];for(int i=0;i<save.MaterialKeys.Length;i++)map[i+1]=catalog.IndexOf(save.MaterialKeys[i]);
            uint Map(uint value){if(value>=map.Length)throw new InvalidDataException("Unknown saved material index.");return map[value];}
            var s=save.Matter;foreach(var f in s.Fields)for(int i=0;i<f.Length;i++)f[i]=Map(f[i]);
            for(int i=0;i<s.Cells.Length;i++)s.Cells[i].Material=Map(s.Cells[i].Material);
            foreach(var f in s.Fragments)for(int i=0;i<f.Hull.Length;i++)f.Hull[i]=Map(f.Hull[i]);
            for(int i=0;i<s.Hull.Length;i++)if(s.Hull[i]!=uint.MaxValue)s.Hull[i]=Map(s.Hull[i]);
            if(!string.IsNullOrEmpty(shipJson))
            {
                save.Ship=JsonUtility.FromJson<ShipSnapshot>(shipJson);
                var b=ScriptableObject.CreateInstance<ShipBlueprint>();
                try
                {
                    JsonUtility.FromJsonOverwrite(save.Ship.BlueprintJson,b);
                    for(int i=0;i<b.Structure.Count;i++){var cell=b.Structure[i];cell.Material=(ushort)Map(cell.Material);b.Structure[i]=cell;}
                    b.Validate();save.Ship.BlueprintJson=JsonUtility.ToJson(b);
                }finally{UnityEngine.Object.DestroyImmediate(b);}
                for(int i=0;i<save.Ship.Structure.Length;i++)save.Ship.Structure[i].Material=(ushort)Map(save.Ship.Structure[i].Material);
                foreach(var f in save.Ship.Fragments)for(int i=0;i<f.Cells.Count;i++){var cell=f.Cells[i];cell.Material=(ushort)Map(cell.Material);f.Cells[i]=cell;}
            }
            save.MaterialKeys=SalvageSave.Keys(catalog);
        }
    }
    public static class AtomicSalvageStore
    {
        static readonly object Gate=new object();
        public static void Write(string path,byte[] bytes,Action beforeReplace=null)
        {
            // Reject invalid candidates before touching a committed generation.
            SalvageSaveCodec.Decode(bytes,out _);
            lock(Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                string temp=path+".pending",backup=path+".backup";
                using(var f=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None)){f.Write(bytes,0,bytes.Length);f.Flush(true);}
                SalvageSaveCodec.Decode(File.ReadAllBytes(temp),out _);beforeReplace?.Invoke();
                if(File.Exists(path))
                {
                    // Never replace a verified backup with a corrupt primary.
                    bool valid=true;try{SalvageSaveCodec.Decode(File.ReadAllBytes(path),out _);}catch(InvalidDataException){valid=false;}catch(EndOfStreamException){valid=false;}
                    File.Replace(temp,path,valid?backup:null);
                }
                else File.Move(temp,path);
            }
        }
        public static SalvageSave Read(string path,out string shipJson)
        {
            lock(Gate)
            {
                try{return SalvageSaveCodec.Decode(File.ReadAllBytes(path),out shipJson);}
                catch(Exception e)when(e is IOException||e is InvalidDataException)
                {
                    if(!File.Exists(path+".backup"))throw;
                    var save=SalvageSaveCodec.Decode(File.ReadAllBytes(path+".backup"),out shipJson);save.RecoveredBackup=true;return save;
                }
            }
        }
    }
}
