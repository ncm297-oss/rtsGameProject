namespace Rts.Sim.Map;

/// <summary>Fixed terrain geometry from docs/02 "Map and terrain" and "High ground"; not tunable per map.</summary>
public static class MapConstants
{
    /// <summary>Edge length of one grid cell in meters (docs/02: 1 cell = 2 m).</summary>
    public const float CellSize = 2f;

    /// <summary>Height difference between two elevation levels in meters (docs/02: levels 4 m apart).</summary>
    public const float LevelHeight = 4f;

    /// <summary>Highest elevation level; maps use levels 0 to this (docs/02: levels 0-2).</summary>
    public const int MaxLevel = 2;

    /// <summary>Number of elevation levels.</summary>
    public const int LevelCount = MaxLevel + 1;

    /// <summary>Steepest passable slope as rise over run: tan(30 degrees) (docs/02: slopes over 30 degrees are impassable).</summary>
    public const float MaxRampSlope = 0.57735f;

    /// <summary>Nav cost of a normal passable cell (docs/03 "Navigation grid").</summary>
    public const byte CostPassable = 1;

    /// <summary>Nav cost of a blocked cell (docs/03 "Navigation grid").</summary>
    public const byte CostBlocked = 255;
}
