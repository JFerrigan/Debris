using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Debris.Simulation.CoupledContacts
{
    // A full persistent identity. The hash is only a dictionary accelerator;
    // equality always compares every word, including the entity namespace.
    [StructLayout(LayoutKind.Sequential,Pack=4,Size=20)]
    public struct CoupledPersistentIdentity : IEquatable<CoupledPersistentIdentity>
    {
        public uint Kind;
        public ulong Low,High;

        public static CoupledPersistentIdentity Grain(uint grainIdentity)
        {
            if(grainIdentity==0)throw new ArgumentOutOfRangeException(nameof(grainIdentity));
            return new CoupledPersistentIdentity{Kind=1,Low=grainIdentity};
        }

        public bool Equals(CoupledPersistentIdentity other)=>
            Kind==other.Kind&&Low==other.Low&&High==other.High;
        public override bool Equals(object other)=>
            other is CoupledPersistentIdentity identity&&Equals(identity);
        public override int GetHashCode()
        {
            unchecked
            {
                uint hash=Kind*16777619u;
                hash=(hash^(uint)Low)*16777619u;
                hash=(hash^(uint)(Low>>32))*16777619u;
                hash=(hash^(uint)High)*16777619u;
                return (int)((hash^(uint)(High>>32))*16777619u);
            }
        }
    }

    // Shared C#/GPU layout. Identity generation follows release/recreation;
    // slot generation changes on every release or replacement at this index.
    // A cache may refer to slot+slot generation, never to slot alone.
    [StructLayout(LayoutKind.Sequential,Pack=4,Size=36)]
    public struct CoupledEndpointBinding
    {
        public CoupledPersistentIdentity Identity;
        public uint Generation,SlotGeneration,Revision,Bound;
    }

    // The solver session owns this table. It never derives persistence from a
    // compact slot, primitive index, or truncated contact-key hash.
    public sealed class CoupledEndpointIdentityTable
    {
        readonly CoupledEndpointBinding[] slots;
        readonly Dictionary<CoupledPersistentIdentity,int> occupied=
            new Dictionary<CoupledPersistentIdentity,int>();
        readonly Dictionary<CoupledPersistentIdentity,uint> lifetimes=
            new Dictionary<CoupledPersistentIdentity,uint>();
        public int Capacity=>slots.Length;
        public uint TableRevision { get; private set; }

        public CoupledEndpointIdentityTable(int capacity)
        {
            if(capacity<=0||capacity>8210)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            if(Marshal.SizeOf<CoupledPersistentIdentity>()!=20||
               Marshal.SizeOf<CoupledEndpointBinding>()!=36||
               Marshal.OffsetOf<CoupledPersistentIdentity>(
                   nameof(CoupledPersistentIdentity.Low)).ToInt32()!=4||
               Marshal.OffsetOf<CoupledPersistentIdentity>(
                   nameof(CoupledPersistentIdentity.High)).ToInt32()!=12||
               Marshal.OffsetOf<CoupledEndpointBinding>(
                   nameof(CoupledEndpointBinding.Generation)).ToInt32()!=20||
               Marshal.OffsetOf<CoupledEndpointBinding>(
                   nameof(CoupledEndpointBinding.SlotGeneration)).ToInt32()!=24||
               Marshal.OffsetOf<CoupledEndpointBinding>(
                   nameof(CoupledEndpointBinding.Revision)).ToInt32()!=28||
               Marshal.OffsetOf<CoupledEndpointBinding>(
                   nameof(CoupledEndpointBinding.Bound)).ToInt32()!=32)
                throw new InvalidOperationException("V2 endpoint identity layout mismatch");
            slots=new CoupledEndpointBinding[capacity];
            for(int i=0;i<capacity;i++)slots[i].SlotGeneration=1;
            TableRevision=1;
        }

        public CoupledEndpointBinding Bind(int slot,CoupledPersistentIdentity identity,
            uint physicalRevision)
        {
            CheckSlot(slot);
            if(identity.Kind<1||identity.Kind>3||
               (identity.Low==0&&identity.High==0)||physicalRevision==0)
                throw new ArgumentException("Invalid persistent endpoint identity or revision");
            if(occupied.TryGetValue(identity,out int other)&&other!=slot)
                throw new InvalidOperationException("Persistent endpoint identity is already live");
            var current=slots[slot];
            if(current.Bound!=0&&current.Identity.Equals(identity))
            {
                if(physicalRevision<current.Revision)
                    throw new InvalidOperationException("Physical revision moved backward");
                if(physicalRevision==current.Revision)return current;
                uint nextTableRevision=Increment(TableRevision);
                current.Revision=physicalRevision;
                slots[slot]=current;
                TableRevision=nextTableRevision;
                return current;
            }
            uint nextRevision=Increment(TableRevision);
            uint identityGeneration=lifetimes.TryGetValue(identity,out uint prior)?prior:1;
            if(current.Bound!=0)
            {
                uint oldNextGeneration=Increment(current.Generation);
                uint nextSlotGeneration=Increment(current.SlotGeneration);
                occupied.Remove(current.Identity);
                lifetimes[current.Identity]=oldNextGeneration;
                current.SlotGeneration=nextSlotGeneration;
            }
            current.Identity=identity;
            current.Generation=identityGeneration;
            current.Revision=physicalRevision;
            current.Bound=1;
            occupied.Add(identity,slot);
            lifetimes[identity]=identityGeneration;
            slots[slot]=current;
            TableRevision=nextRevision;
            return current;
        }

        public void Release(int slot)
        {
            CheckSlot(slot);
            var current=slots[slot];
            if(current.Bound==0)return;
            var identity=current.Identity;
            uint nextGeneration=Increment(current.Generation);
            uint nextSlotGeneration=Increment(current.SlotGeneration);
            uint nextRevision=Increment(TableRevision);
            occupied.Remove(identity);
            current.Identity=default;
            current.Revision=0;
            current.Bound=0;
            current.Generation=nextGeneration;
            current.SlotGeneration=nextSlotGeneration;
            lifetimes[identity]=nextGeneration;
            slots[slot]=current;
            TableRevision=nextRevision;
        }

        public CoupledEndpointBinding BumpPhysicalRevision(int slot)
        {
            var current=Resolve(slot);
            uint nextRevision=Increment(current.Revision);
            uint nextTableRevision=Increment(TableRevision);
            current.Revision=nextRevision;
            slots[slot]=current;
            TableRevision=nextTableRevision;
            return current;
        }

        public CoupledEndpointBinding Resolve(int slot)
        {
            CheckSlot(slot);
            var current=slots[slot];
            if(current.Bound==0)throw new InvalidOperationException("Endpoint slot is unbound");
            return current;
        }

        public CoupledEndpointBinding[] Snapshot()
        {
            var copy=new CoupledEndpointBinding[slots.Length];
            Array.Copy(slots,copy,slots.Length);
            return copy;
        }

        static uint Increment(uint value)
        {
            if(value==uint.MaxValue)
                throw new InvalidOperationException("Endpoint generation/revision exhausted");
            return value+1;
        }

        void CheckSlot(int slot)
        {
            if(slot<0||slot>=slots.Length)
                throw new ArgumentOutOfRangeException(nameof(slot));
        }
    }
}
