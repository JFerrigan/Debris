using System;

namespace Debris.Core
{

/// <summary>Stable serialized identity. Generate once, never derive from a Unity instance ID.</summary>
[Serializable]
public readonly struct StableId : IEquatable<StableId>
{
    public readonly string Value;

    public StableId(string value)
    {
        if (!Guid.TryParseExact(value, "N", out var id)) throw new ArgumentException("A 128-bit ID in N format is required.", nameof(value));
        Value = id.ToString("N");
    }

    public static StableId New() => new(Guid.NewGuid().ToString("N"));
    public bool Equals(StableId other) => StringComparer.Ordinal.Equals(Value, other.Value);
    public override bool Equals(object obj) => obj is StableId other && Equals(other);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);
    public override string ToString() => Value;
}

}
