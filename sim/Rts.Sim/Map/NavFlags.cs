using System;

namespace Rts.Sim.Map;

/// <summary>Per-cell passability flags of the <see cref="NavGrid"/>.</summary>
[Flags]
public enum NavFlags : byte
{
    /// <summary>Open ground.</summary>
    None = 0,

    /// <summary>Ground units can't enter (cliff, map border, unreachable pocket; later buildings, trees, water).</summary>
    Blocked = 1,

    /// <summary>A plateau edge cell above a drop with no ramp; always also <see cref="Blocked"/>.</summary>
    Cliff = 2,

    /// <summary>A sloped cell joining its level to the level above.</summary>
    Ramp = 4,
}
