using System;

namespace Rts.Sim.Map;

/// <summary>Per-cell passability flags of the <see cref="NavGrid"/>.</summary>
[Flags]
public enum NavFlags : byte
{
    /// <summary>Open ground.</summary>
    None = 0,

    /// <summary>Ground units can't enter (cliff, map border, unreachable pocket, resource node, building; later water).</summary>
    Blocked = 1,

    /// <summary>A plateau edge cell above a drop with no ramp; always also <see cref="Blocked"/>.</summary>
    Cliff = 2,

    /// <summary>A sloped cell joining its level to the level above.</summary>
    Ramp = 4,

    /// <summary>Covered by a live resource node (a tree, a gold mine); always also <see cref="Blocked"/>. Cleared when the node is depleted (M3-1).</summary>
    Resource = 8,

    /// <summary>Covered by a building's footprint; always also <see cref="Blocked"/> (M3-2).</summary>
    Building = 16,
}
