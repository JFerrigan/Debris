using NUnit.Framework;

namespace Debris.Ships.Tests;

public sealed class CargoGridTests
{
    [Test]
    public void CargoStoresOnlyOneMaterialPerVisibleCavityCell()
    {
        var cell = new CargoCell(2, 3);
        var cargo = new CargoGrid(new[] { cell, new CargoCell(3, 3) });

        Assert.That(cargo.TryRecordOccupancy(cell, "iron"), Is.True);
        Assert.That(cargo.TryRecordOccupancy(cell, "copper"), Is.False);
        Assert.That(cargo.OccupiedCount, Is.EqualTo(1));
        Assert.That(cargo.FreeCount, Is.EqualTo(1));
        Assert.That(cargo.TryGetMaterial(cell, out var material), Is.True);
        Assert.That(material, Is.EqualTo("iron"));
    }
}
