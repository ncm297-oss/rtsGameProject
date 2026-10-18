using Rts.Sim.Save;

namespace Rts.Sim.Map;

// M6-S2: the nav grid in a save: every cell's flags and cost (nodes, buildings and pocket cells as they are now), both
// versions and the passable count. The levels come from the saved heightmap the grid is built on.
public sealed partial class NavGrid
{
    /// <summary>Writes every cell's flags and cost, <see cref="Version"/>, <see cref="BlockVersion"/> and <see cref="PassableCount"/> (M6-S2).</summary>
    internal void WriteTo(SaveWriter w)
    {
        w.Tag(SaveFormat.TagGrid);
        w.WriteArray(_flags);
        w.WriteArray(_cost);
        w.Write(Version);
        w.Write(BlockVersion);
        w.Write(PassableCount);
    }

    /// <summary>Reads what <see cref="WriteTo"/> wrote over this grid, built from the same heightmap.</summary>
    internal void ReadFrom(SaveReader r)
    {
        if (!r.Expect(SaveFormat.TagGrid)) return;
        r.ReadArray(_flags);
        r.ReadArray(_cost);
        Version = r.ReadInt32();
        BlockVersion = r.ReadInt32();
        PassableCount = r.ReadInt32In(0, _flags.Length);
    }
}
