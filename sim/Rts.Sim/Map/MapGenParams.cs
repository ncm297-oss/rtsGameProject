using System;

namespace Rts.Sim.Map;

/// <summary>Tunables of the terraced map generator. All sizes are in cells; defaults give a docs/02 128 x 128 map.</summary>
/// <remarks>
/// Plateaus are rectangles: level-1 ones on open ground, level-2 ones inside a level-1 one.
/// Overlapping rectangles merge into one plateau.
/// </remarks>
public sealed record MapGenParams
{
    /// <summary>The default 128 x 128 map.</summary>
    public static MapGenParams Default { get; } = new();

    /// <summary>Map width in cells (docs/02: 128).</summary>
    public int Width { get; init; } = 128;

    /// <summary>Map height in cells (docs/02: 128).</summary>
    public int Height { get; init; } = 128;

    /// <summary>Cells of level-0 ground kept free of plateaus inside the blocked border ring.</summary>
    public int EdgeMargin { get; init; } = 6;

    /// <summary>Number of level-1 plateau rectangles.</summary>
    public int Level1Plateaus { get; init; } = 5;

    /// <summary>Smallest side of a level-1 plateau rectangle.</summary>
    public int Level1MinSize { get; init; } = 16;

    /// <summary>Largest side of a level-1 plateau rectangle.</summary>
    public int Level1MaxSize { get; init; } = 34;

    /// <summary>Number of level-2 plateau rectangles, each placed inside a random level-1 one.</summary>
    public int Level2Plateaus { get; init; } = 2;

    /// <summary>Smallest side of a level-2 plateau rectangle.</summary>
    public int Level2MinSize { get; init; } = 7;

    /// <summary>Largest side of a level-2 plateau rectangle.</summary>
    public int Level2MaxSize { get; init; } = 14;

    /// <summary>Minimum cells between a level-2 rectangle and the edge of its level-1 parent.</summary>
    public int Level2Inset { get; init; } = 2;

    /// <summary>Ramp width across the slope; ramps are the chokepoints (docs/02).</summary>
    public int RampWidth { get; init; } = 3;

    /// <summary>Ramp cells along the slope; long enough that the slope stays at or under 30 degrees.</summary>
    public int RampLength { get; init; } = 4;

    /// <summary>Ramps the generator tries to give each plateau rectangle.</summary>
    public int RampsPerPlateau { get; init; } = 2;

    /// <summary>Random placements tried per ramp before giving up on it (bounds generation time).</summary>
    public int RampTries { get; init; } = 32;

    /// <summary>A layout with a smaller passable share of the map is rejected and regenerated.</summary>
    public float MinPassableFraction { get; init; } = 0.5f;

    /// <summary>Layouts tried before falling back (bounds generation time; it never loops forever).</summary>
    public int MaxAttempts { get; init; } = 8;

    /// <summary>Largest <see cref="RampTries"/>; with the other caps it bounds the worst valid generation (BUG-0013, BUG-0015).</summary>
    public const int MaxRampTries = 128;

    /// <summary>Largest <see cref="MaxAttempts"/>: the default. Every attempt builds a full nav grid, so this cap is most of the worst-case time bound (BUG-0013, BUG-0015).</summary>
    public const int MaxMaxAttempts = 8;

    /// <summary>Largest <see cref="Level1Plateaus"/> and <see cref="Level2Plateaus"/> (BUG-0013).</summary>
    public const int MaxPlateaus = 32;

    /// <summary>Throws if any value is out of range or the pieces can't fit on the map.</summary>
    /// <remarks>Every size gets an upper bound before it is used in arithmetic, so nothing can overflow (BUG-0012).</remarks>
    public void Validate()
    {
        Check(Width >= 16 && Width <= 1024, nameof(Width));
        Check(Height >= 16 && Height <= 1024, nameof(Height));
        int minDim = Math.Min(Width, Height);
        Check(EdgeMargin >= 1 && EdgeMargin <= minDim, nameof(EdgeMargin));
        Check(RampWidth >= 1 && RampWidth <= minDim, nameof(RampWidth));
        Check(RampLength >= 1 && RampLength <= minDim, nameof(RampLength));
        Check(Level2Inset >= 1 && Level2Inset <= minDim, nameof(Level2Inset));
        // Rise of one level over the ramp plus the flat cell at each end must stay within 30 degrees.
        Check(MapConstants.LevelHeight / ((RampLength + 1) * MapConstants.CellSize) <= MapConstants.MaxRampSlope, nameof(RampLength));
        Check(RampsPerPlateau >= 1 && RampsPerPlateau <= 16, nameof(RampsPerPlateau));
        Check(RampTries >= 1 && RampTries <= MaxRampTries, nameof(RampTries));

        // A side needs room for the ramp mouth plus a plateau cell on each side of it.
        int minSide = Math.Max(3, RampWidth + 2);
        int interior = minDim - 2 * (1 + EdgeMargin);
        Check(Level1Plateaus >= 0 && Level1Plateaus <= MaxPlateaus, nameof(Level1Plateaus));
        Check(Level1MinSize >= minSide, nameof(Level1MinSize));
        Check(Level1MaxSize >= Level1MinSize && Level1MaxSize <= interior, nameof(Level1MaxSize));
        Check(Level2Plateaus >= 0 && Level2Plateaus <= MaxPlateaus, nameof(Level2Plateaus));
        Check(Level2MinSize >= minSide, nameof(Level2MinSize));
        Check(Level2MaxSize >= Level2MinSize, nameof(Level2MaxSize));

        Check(MinPassableFraction >= 0f && MinPassableFraction <= 1f, nameof(MinPassableFraction));
        Check(MaxAttempts >= 1 && MaxAttempts <= MaxMaxAttempts, nameof(MaxAttempts));
    }

    private static void Check(bool ok, string name)
    {
        if (!ok) throw new ArgumentOutOfRangeException(name);
    }
}
