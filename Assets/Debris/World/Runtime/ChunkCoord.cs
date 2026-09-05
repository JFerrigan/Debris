using System;

namespace Debris.World;

public readonly struct ChunkCoord : IEquatable<ChunkCoord>
{
    public readonly int X;
    public readonly int Y;
    public ChunkCoord(int x, int y) { X = x; Y = y; }
    public bool Equals(ChunkCoord other) => X == other.X && Y == other.Y;
    public override bool Equals(object obj) => obj is ChunkCoord other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y);
    public override string ToString() => $"({X}, {Y})";

    public static ChunkCoord FromCell(int cellX, int cellY, int size) =>
        new(FloorDivide(cellX, size), FloorDivide(cellY, size));

    private static int FloorDivide(int value, int divisor)
    {
        var quotient = value / divisor;
        var remainder = value % divisor;
        return remainder < 0 ? quotient - 1 : quotient;
    }
}
