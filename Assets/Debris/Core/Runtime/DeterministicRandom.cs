using System;

namespace Debris.Core;

/// <summary>Small deterministic PRNG for persistent generation. Do not replace with UnityEngine.Random.</summary>
public struct DeterministicRandom
{
    private ulong _state;

    public DeterministicRandom(ulong seed) => _state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;

    public uint NextUInt()
    {
        _state ^= _state >> 12;
        _state ^= _state << 25;
        _state ^= _state >> 27;
        return (uint)((_state * 2685821657736338717UL) >> 32);
    }

    public float NextFloat() => NextUInt() / ((float)uint.MaxValue + 1f);
    public int NextInt(int exclusiveMax) => (int)(NextFloat() * exclusiveMax);

    public static ulong Seed(ulong worldSeed, StableId id, string purpose)
    {
        ulong hash = 14695981039346656037UL ^ worldSeed;
        Hash(ref hash, id.Value);
        Hash(ref hash, purpose);
        return hash;
    }

    private static void Hash(ref ulong hash, string text)
    {
        foreach (var c in text ?? string.Empty)
        {
            hash ^= c;
            hash *= 1099511628211UL;
        }
    }
}
