namespace Rts.Sim.Vision;

/// <summary>Fog-of-war rules (M4-3a, docs/02 "Vision and fog of war" and "High ground", docs/03 "Vision, detection, fog").</summary>
public static class VisionConstants
{
    /// <summary>Ticks between two fog updates (docs/03 tick phase 12: every 4 ticks, 5 Hz). Every player updates on the same tick.</summary>
    public const int UpdateInterval = 4;

    /// <summary>
    /// The fog updates on the ticks where <c>tick % UpdateInterval == UpdatePhase</c>: 1, so the first update follows tick 1,
    /// the tick the commands queued before the match's first tick (the start bases) apply in.
    /// </summary>
    public const int UpdatePhase = 1;

    /// <summary>Meters around a viewer inside which every cell is visible whatever its level (docs/02 "High ground": 4 m).</summary>
    public const float LipRadius = 4f;

    /// <summary>Ticks an attacker hitting from a higher level stays visible to its victim's owner (docs/02 "High ground": 2 s).</summary>
    public const int HighGroundRevealTicks = 40;

    /// <summary>A cell the player has never seen (docs/02: black).</summary>
    public const byte Unexplored = 0;

    /// <summary>A cell the player has seen before but not at the last update (docs/02: darkened).</summary>
    public const byte Explored = 1;

    /// <summary>A cell inside one of the player's sight circles at the last update.</summary>
    public const byte Visible = 2;
}
