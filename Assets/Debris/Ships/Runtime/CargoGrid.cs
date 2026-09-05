using System;
using System.Collections.Generic;

namespace Debris.Ships;

/// <summary>
/// Fixed-volume cavity occupancy index. The GPU loose-cell simulation owns continuous
/// position/velocity and settling; this class is only the discrete non-overlap/capacity mirror
/// used for save validation and deterministic tests. It must never snap cargo into slots.
/// </summary>
public sealed class CargoGrid
{
    private readonly HashSet<CargoCell> _cavityCells;
    private readonly Dictionary<CargoCell, string> _occupants = new();

    public CargoGrid(IEnumerable<CargoCell> cavityCells)
    {
        _cavityCells = new HashSet<CargoCell>(cavityCells ?? throw new ArgumentNullException(nameof(cavityCells)));
        if (_cavityCells.Count == 0) throw new ArgumentException("A cargo cavity needs at least one cell.", nameof(cavityCells));
    }

    public int Capacity => _cavityCells.Count;
    public int OccupiedCount => _occupants.Count;
    public int FreeCount => Capacity - OccupiedCount;
    public bool IsCavityCell(CargoCell cell) => _cavityCells.Contains(cell);
    public bool IsOccupied(CargoCell cell) => _occupants.ContainsKey(cell);
    public bool TryGetMaterial(CargoCell cell, out string materialKey) => _occupants.TryGetValue(cell, out materialKey);

    /// <summary>Records a cell after physics has resolved it to this unoccupied cavity location.</summary>
    public bool TryRecordOccupancy(CargoCell cell, string materialKey)
    {
        if (string.IsNullOrWhiteSpace(materialKey) || !_cavityCells.Contains(cell) || _occupants.ContainsKey(cell)) return false;
        _occupants.Add(cell, materialKey);
        return true;
    }

    public bool TryRemove(CargoCell cell, out string materialKey)
    {
        if (!_occupants.TryGetValue(cell, out materialKey)) return false;
        return _occupants.Remove(cell);
    }
}

public readonly struct CargoCell : IEquatable<CargoCell>
{
    public readonly int X;
    public readonly int Y;
    public CargoCell(int x, int y) { X = x; Y = y; }
    public bool Equals(CargoCell other) => X == other.X && Y == other.Y;
    public override bool Equals(object obj) => obj is CargoCell other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y);
}
