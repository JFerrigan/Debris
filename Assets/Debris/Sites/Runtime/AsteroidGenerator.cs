using System;
using Debris.Core;
using Debris.Materials;

namespace Debris.Sites
{

/// <summary>Deterministic CPU generation used to populate GPU chunk fields. It has no Unity random dependency.</summary>
public static class AsteroidGenerator
{
    public static ushort[] GenerateChunk(ulong worldSeed, StableId siteId, int chunkX, int chunkY, int chunkSize, AsteroidProfile profile, MaterialCatalog catalog)
    {
        if (chunkSize <= 0 || chunkSize > 1024) throw new ArgumentOutOfRangeException(nameof(chunkSize));
        profile.Validate(catalog);
        var result = new ushort[chunkSize * chunkSize];
        var rng = new DeterministicRandom(DeterministicRandom.Seed(worldSeed, siteId, "asteroid-shape"));
        var radius = profile.MinimumRadiusCells + rng.NextInt(profile.MaximumRadiusCells - profile.MinimumRadiusCells + 1);
        var cx = (rng.NextFloat() - .5f) * 16f;
        var cy = (rng.NextFloat() - .5f) * 16f;

        for (var y = 0; y < chunkSize; y++)
        for (var x = 0; x < chunkSize; x++)
        {
            var worldX = chunkX * chunkSize + x;
            var worldY = chunkY * chunkSize + y;
            var dx = worldX - cx;
            var dy = worldY - cy;
            var radialNoise = Sample01(worldSeed, siteId, worldX / 6, worldY / 6, "asteroid-noise") * 14f - 7f;
            if (dx * dx + dy * dy > (radius + radialNoise) * (radius + radialNoise)) continue;
            result[y * chunkSize + x] = ChooseMaterial(worldSeed, siteId, worldX, worldY, profile, catalog);
        }
        return result;
    }

    private static ushort ChooseMaterial(ulong worldSeed, StableId siteId, int x, int y, AsteroidProfile profile, MaterialCatalog catalog)
    {
        if (profile.Materials == null || profile.Materials.Length == 0)
            throw new InvalidOperationException("Asteroid profiles require at least one material band.");
        var roll = Sample01(worldSeed, siteId, x, y, "asteroid-material");
        var total = 0f;
        foreach (var band in profile.Materials) total += band.Weight;
        if (total <= 0f)
            throw new InvalidOperationException("Asteroid profile material weights must have a positive total.");
        var cursor = 0f;
        foreach (var band in profile.Materials)
        {
            cursor += band.Weight / total;
            if (band.Weight > 0 && roll < cursor) return catalog.IndexOf(band.MaterialKey);
        }
        return catalog.IndexOf(profile.Materials[profile.Materials.Length - 1].MaterialKey);
    }

    private static float Sample01(ulong worldSeed, StableId siteId, int x, int y, string purpose)
    {
        var seed = DeterministicRandom.Seed(worldSeed, siteId, purpose) ^ ((ulong)(uint)x << 32) ^ (uint)y;
        return new DeterministicRandom(seed).NextFloat();
    }
}

}
