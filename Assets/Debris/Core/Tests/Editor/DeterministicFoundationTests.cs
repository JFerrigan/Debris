using Debris.Core;
using Debris.World;
using NUnit.Framework;

namespace Debris.Core.Tests;

public sealed class DeterministicFoundationTests
{
    [Test]
    public void PurposeSeed_IsStableAndPurposeScoped()
    {
        var id = new StableId("a1b2c3");
        var first = DeterministicRandom.Seed(42, id, "asteroid");
        var second = DeterministicRandom.Seed(42, id, "asteroid");
        var otherPurpose = DeterministicRandom.Seed(42, id, "contact");

        Assert.That(first, Is.EqualTo(second));
        Assert.That(otherPurpose, Is.Not.EqualTo(first));
    }

    [TestCase(-1, -1)]
    [TestCase(-128, -1)]
    [TestCase(-129, -2)]
    [TestCase(128, 1)]
    public void ChunkCoordinates_FloorDivideNegativeCells(int cell, int expectedChunk)
    {
        Assert.That(ChunkCoord.FromCell(cell, 0, 128).X, Is.EqualTo(expectedChunk));
    }
}
