using System;
using System.Collections.Generic;
using UnityEngine;

namespace Debris.Simulation.CoupledContacts
{
    // Construction-time owner of explicit V2 GPU slices. Every buffer counts
    // at reserved capacity, including inactive rows and sort scratch. Owners
    // retain their buffers but release them through this ledger exactly once.
    public sealed class CoupledContactArena
    {
        public const long DefaultLimitBytes=128L*1024*1024;
        public struct Slice
        {
            public string Name;
            public int Count,Stride;
            public long Bytes;
        }

        readonly Dictionary<GraphicsBuffer,Slice> slices=
            new Dictionary<GraphicsBuffer,Slice>();
        readonly HashSet<string> names=new HashSet<string>(StringComparer.Ordinal);
        uint nextOwner;
        public long LimitBytes { get; }
        public long UsedBytes { get; private set; }
        public int SliceCount=>slices.Count;

        public CoupledContactArena(long limitBytes=DefaultLimitBytes)
        {
            if(limitBytes<=0)throw new ArgumentOutOfRangeException(nameof(limitBytes));
            LimitBytes=limitBytes;
        }

        public string NewOwner(string label)
        {
            if(string.IsNullOrWhiteSpace(label)||nextOwner==uint.MaxValue)
                throw new ArgumentException("Invalid V2 arena owner");
            return label+"#"+(++nextOwner);
        }

        public void EnsureCapacity(long additionalBytes)
        {
            if(additionalBytes<0)throw new ArgumentOutOfRangeException(nameof(additionalBytes));
            if(additionalBytes>LimitBytes-UsedBytes)
                throw new InvalidOperationException(
                    $"V2 GPU arena overflow: {UsedBytes}+{additionalBytes}>{LimitBytes}");
        }

        public GraphicsBuffer Allocate(string name,int count,int stride,
            GraphicsBuffer.Target target=GraphicsBuffer.Target.Structured)
        {
            if(string.IsNullOrWhiteSpace(name)||count<=0||stride<=0)
                throw new ArgumentException("Invalid V2 arena slice");
            long bytes=checked((long)count*stride);
            if(names.Contains(name))
                throw new InvalidOperationException("Duplicate V2 arena slice: "+name);
            EnsureCapacity(bytes);
            var buffer=new GraphicsBuffer(target,count,stride);
            try
            {
                var slice=new Slice{Name=name,Count=count,Stride=stride,Bytes=bytes};
                slices.Add(buffer,slice);
                names.Add(name);
                UsedBytes+=bytes;
            }
            catch
            {
                slices.Remove(buffer);
                names.Remove(name);
                buffer.Release();
                throw;
            }
            return buffer;
        }

        public void Release(GraphicsBuffer buffer)
        {
            if(buffer==null||!slices.TryGetValue(buffer,out var slice))
                throw new InvalidOperationException("V2 arena buffer is not owned or was released");
            slices.Remove(buffer);
            names.Remove(slice.Name);
            UsedBytes-=slice.Bytes;
            buffer.Release();
        }

        public Slice[] Snapshot()
        {
            var result=new Slice[slices.Count];
            slices.Values.CopyTo(result,0);
            Array.Sort(result,(a,b)=>StringComparer.Ordinal.Compare(a.Name,b.Name));
            return result;
        }

        public void AssertEmpty()
        {
            if(slices.Count!=0||UsedBytes!=0)
                throw new InvalidOperationException("V2 arena still owns GPU buffers");
        }
    }
}
