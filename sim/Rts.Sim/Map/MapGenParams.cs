using System;

namespace Rts.Sim.Map;

/// <summary>Tunables of the terraced map generator and the resource placer. Sizes are in cells (<see cref="MineSpacing"/> and the start distances in meters); defaults give a docs/02 128 x 128 map with two start locations and no resources.</summary>
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

    /// <summary>Number of forests (connected blobs of trees) placed after the terrain; 0 (the default) places none and draws nothing (M3-1).</summary>
    public int Forests { get; init; }

    /// <summary>Fewest trees in one forest.</summary>
    public int ForestMinTrees { get; init; } = 12;

    /// <summary>Most trees in one forest.</summary>
    public int ForestMaxTrees { get; init; } = 40;

    /// <summary>Number of gold mines placed after the terrain; 0 (the default) places none and draws nothing (M3-1).</summary>
    public int GoldMines { get; init; }

    /// <summary>Least distance in meters between two gold mines' centers.</summary>
    public float MineSpacing { get; init; } = 24f;

    /// <summary>
    /// Start locations the generator picks before resources (M5-4a): 2 (the default; player 1's is player 0's point mirror
    /// through the map centre) to <see cref="MaxStartLocations"/> (quarter turns of player 0's; square maps only), or 0 for
    /// none (terrain-only maps, maps too small for bases, and replays older than format 6: the generator then draws and
    /// builds exactly as before M5-4a).
    /// </summary>
    public int StartLocations { get; init; } = 2;

    /// <summary>Radius in cells of each start location's clear disc: open, flat ground on one level that no resource covers.</summary>
    public int StartClearRadius { get; init; } = 8;

    /// <summary>Least cells between a start location's clear disc and the map edge.</summary>
    public int StartEdgeMargin { get; init; } = 10;

    /// <summary>Most meters from a start location's centre cell to the centre of each of its start mines (outside the clear disc).</summary>
    public float StartMineDistance { get; init; } = 18f;

    /// <summary>Most meters from a start location's centre cell to the first tree of its start forest (outside the clear disc).</summary>
    public float StartForestDistance { get; init; } = 24f;

    /// <summary>Largest <see cref="StartLocations"/> (docs/02: 2-4 players).</summary>
    public const int MaxStartLocations = 4;

    /// <summary>Largest <see cref="StartClearRadius"/>.</summary>
    public const int MaxStartClearRadius = 32;

    /// <summary>Most meters <see cref="StartMineDistance"/> may reach past the clear disc (bounds the site disc the generator checks).</summary>
    public const float MaxStartMineReach = 16f;

    /// <summary>Largest <see cref="StartForestDistance"/> in meters.</summary>
    public const float MaxStartForestDistance = 128f;

    /// <summary>
    /// Radius in cells of the disc the generator requires open round a start location: the clear disc, out to the start
    /// mines' reach, plus a cell, so a 2 x 2 mine always fits beside it.
    /// </summary>
    internal int StartSiteRadius => Math.Max(StartClearRadius, (int)MathF.Ceiling(StartMineDistance / MapConstants.CellSize)) + 1;

    /// <summary>Largest <see cref="Forests"/> and <see cref="GoldMines"/>: every placement runs a flood fill of the map, so this bounds setup time.</summary>
    public const int MaxResourceGroups = 64;

    /// <summary>Largest <see cref="ForestMaxTrees"/>.</summary>
    public const int MaxForestTrees = 256;

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

        Check(Forests >= 0 && Forests <= MaxResourceGroups, nameof(Forests));
        Check(ForestMinTrees >= 1 && ForestMinTrees <= MaxForestTrees, nameof(ForestMinTrees));
        Check(ForestMaxTrees >= ForestMinTrees && ForestMaxTrees <= MaxForestTrees, nameof(ForestMaxTrees));
        Check(GoldMines >= 0 && GoldMines <= MaxResourceGroups, nameof(GoldMines));
        // Written so NaN fails; no two cells of the map are further apart than this.
        // -0 is refused like the other odd float encodings (BUG-0076).
        Check(MineSpacing >= 0f && !float.IsNegative(MineSpacing) && MineSpacing <= 2f * MapConstants.CellSize * Math.Max(Width, Height), nameof(MineSpacing));

        Check(StartLocations == 0 || (StartLocations >= 2 && StartLocations <= MaxStartLocations), nameof(StartLocations));
        Check(StartClearRadius >= 2 && StartClearRadius <= MaxStartClearRadius, nameof(StartClearRadius));
        Check(StartEdgeMargin >= 1 && StartEdgeMargin <= minDim, nameof(StartEdgeMargin));
        float clear = StartClearRadius * MapConstants.CellSize;
        // Written so NaN fails: past the clear disc, and within reach of it.
        Check(StartMineDistance > clear && StartMineDistance <= clear + MaxStartMineReach, nameof(StartMineDistance));
        Check(StartForestDistance > clear && StartForestDistance <= MaxStartForestDistance, nameof(StartForestDistance));
        if (StartLocations == 0) return;
        // The pair must fit a flat map: player 0's clear disc StartEdgeMargin inside the map, its site disc in the west half
        // and on open ground, which starts two cells in from the border (BUG-0620: the site disc, not the clear disc, decides).
        int lo = StartEdgeMargin + StartClearRadius, rs = StartSiteRadius;
        int from = Math.Max(lo, rs + 2), hiY = Math.Min(Height - 1 - lo, Height - 3 - rs);
        Check(lo <= Height - 1 - lo, nameof(StartClearRadius));
        Check(from <= Math.Min(Width / 2 - 1 - rs, hiY), nameof(StartClearRadius)); // the clear radius sizes the site disc
        if (StartLocations > 2) Check(Width == Height, nameof(StartLocations)); // quarter turns need a square map
        // The westmost centres on a flat map: one whose positions keep the discs apart and (BUG-0621) the centres half the
        // smaller side apart must exist, so the flat fallback always has them.
        Span<int> xs = stackalloc int[MaxStartLocations];
        Span<int> ys = stackalloc int[MaxStartLocations];
        bool apart = false;
        for (int cy = from; cy <= hiY && !apart; cy++)
        {
            MapGenerator.Positions(Width, Height, from, cy, StartLocations, xs, ys);
            apart = MapGenerator.Apart(Width, Height, rs, StartLocations, xs, ys);
        }
        Check(apart, nameof(StartClearRadius));
    }

    private static void Check(bool ok, string name)
    {
        if (!ok) throw new ArgumentOutOfRangeException(name);
    }
}
