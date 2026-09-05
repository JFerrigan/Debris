using UnityEngine;

namespace Debris.Sites;

public enum SiteCommandType : byte { CutterStroke, SuctionVolume, CargoIntake, SetComponentState }

/// <summary>CPU-to-GPU command payload. Commands are batched once per fixed simulation step.</summary>
public readonly struct SiteCommand
{
    public readonly SiteCommandType Type;
    public readonly Vector2 PositionCells;
    public readonly Vector2 Direction;
    public readonly float RadiusCells;
    public readonly float Strength;
    public readonly uint SourceId;

    public SiteCommand(SiteCommandType type, Vector2 positionCells, Vector2 direction, float radiusCells, float strength, uint sourceId)
    {
        Type = type;
        PositionCells = positionCells;
        Direction = direction;
        RadiusCells = radiusCells;
        Strength = strength;
        SourceId = sourceId;
    }
}
