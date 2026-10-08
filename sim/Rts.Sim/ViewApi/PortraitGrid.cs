using System;

namespace Rts.Sim.ViewApi;

/// <summary>The selection panel's multi-select grid (M3-V3): how many portraits show and the "+N" overflow. Pure.</summary>
public static class PortraitGrid
{
    /// <summary>Most portraits the grid shows (3 rows of 8).</summary>
    public const int MaxPortraits = 24;

    /// <summary>Portraits columns per row.</summary>
    public const int Columns = 8;

    /// <summary>Portraits shown for a selection of <paramref name="count"/> units.</summary>
    public static int Shown(int count) => Math.Clamp(count, 0, MaxPortraits);

    /// <summary>Units not shown (the "+N" label), 0 when all fit.</summary>
    public static int Overflow(int count) => Math.Max(0, count - MaxPortraits);
}
