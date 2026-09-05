using System;
using System.Collections.Generic;
using Debris.Core;
using Debris.World;

namespace Debris.Persistence
{

[Serializable]
public sealed class SiteRecord
{
    public string SiteId;
    public string GeneratorKey;
    public int GeneratorRevision;
    public ulong GeneratorSeed;
    public List<ChangedChunkRecord> ChangedChunks = new();

    public SiteRecord(StableId siteId, string generatorKey, int generatorRevision, ulong generatorSeed)
    {
        SiteId = siteId.Value;
        GeneratorKey = generatorKey;
        GeneratorRevision = generatorRevision;
        GeneratorSeed = generatorSeed;
    }
}

[Serializable]
public sealed class ChangedChunkRecord
{
    public int X;
    public int Y;
    public int FormatVersion;
    public byte[] CompressedPayload;
    public string IntegrityHash;

    public ChangedChunkRecord(ChunkCoord coordinate, int formatVersion, byte[] compressedPayload, string integrityHash)
    {
        X = coordinate.X;
        Y = coordinate.Y;
        FormatVersion = formatVersion;
        CompressedPayload = compressedPayload;
        IntegrityHash = integrityHash;
    }
}

}
