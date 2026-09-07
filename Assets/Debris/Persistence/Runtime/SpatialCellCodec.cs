using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Debris.Simulation;
using UnityEngine;
namespace Debris.Persistence
{
    internal static class SpatialCellCodec
    {
        static string Bucket(LooseCell cell,int size)=>((cell.Flags&4)!=0?1:0)+":"+(int)Math.Floor((double)cell.Position.x/size)+":"+(int)Math.Floor((double)cell.Position.y/size);
        public static Dictionary<string,byte[]> Encode(MatterSnapshot state)
        {
            var result=new Dictionary<string,byte[]>();var fuels=state.FuelCells.Select((f,i)=>(f,i)).ToDictionary(p=>p.f.Identity);
            foreach(var bucket in state.Cells.Select((c,i)=>(c,i)).GroupBy(p=>Bucket(p.c,state.ChunkSize)))
            {
                using(var stream=new MemoryStream())
                {
                    using(var zip=new DeflateStream(stream,System.IO.Compression.CompressionLevel.Optimal,true))using(var w=new BinaryWriter(zip))
                    {
                        w.Write(bucket.Count());
                        foreach(var entry in bucket)
                        {
                            var c=entry.c;w.Write(entry.i);w.Write(c.Position.x);w.Write(c.Position.y);w.Write(c.Velocity.x);w.Write(c.Velocity.y);w.Write(c.Material);w.Write(c.Identity);w.Write(c.Step);w.Write(c.Flags);
                            if(fuels.TryGetValue(c.Identity,out var fuel)){w.Write(fuel.i);w.Write(fuel.f.Energy);}else w.Write(-1);
                        }
                    }
                    result.Add(bucket.Key,stream.ToArray());
                }
            }
            return result;
        }
        public static void Decode(MatterSnapshot state,Dictionary<string,byte[]> buckets)
        {
            var cells=new SortedDictionary<int,LooseCell>();var fuels=new SortedDictionary<int,FuelCellState>();
            foreach(var bucket in buckets)
            {
                using(var stream=new MemoryStream(bucket.Value))using(var zip=new DeflateStream(stream,CompressionMode.Decompress))using(var r=new BinaryReader(zip))
                {
                    int count=r.ReadInt32();if(count<1||count>state.Capacity-cells.Count)throw new InvalidDataException("Spatial loose-cell budget exceeded.");
                    for(int n=0;n<count;n++)
                    {
                        int slot=r.ReadInt32();var c=new LooseCell{Position=new Vector2(r.ReadSingle(),r.ReadSingle()),Velocity=new Vector2(r.ReadSingle(),r.ReadSingle()),Material=r.ReadUInt32(),Identity=r.ReadUInt32(),Step=r.ReadUInt32(),Flags=r.ReadUInt32()};
                        if(slot<0||slot>=state.Capacity||cells.ContainsKey(slot)||Bucket(c,state.ChunkSize)!=bucket.Key)throw new InvalidDataException("Invalid spatial cell slot/address.");cells.Add(slot,c);
                        int fuelSlot=r.ReadInt32();if(fuelSlot< -1||fuelSlot>=state.Capacity||fuelSlot>=0&&fuels.ContainsKey(fuelSlot))throw new InvalidDataException("Invalid fuel-state slot.");
                        if(fuelSlot>=0)fuels.Add(fuelSlot,new FuelCellState{Identity=c.Identity,Energy=r.ReadDouble()});
                    }
                    if(zip.ReadByte()!=-1)throw new InvalidDataException("Trailing spatial cell data.");
                }
            }
            if(cells.Count>0&&cells.Last().Key!=cells.Count-1||fuels.Count>0&&fuels.Last().Key!=fuels.Count-1)throw new InvalidDataException("Missing spatial cell/fuel slot.");
            state.Cells=cells.Values.ToArray();state.FuelCells=fuels.Values.ToArray();state.Counters[0]=(uint)cells.Count;state.Counters[1]+=(uint)cells.Count;CpuCutReference.Validate(state);
        }
    }
}
