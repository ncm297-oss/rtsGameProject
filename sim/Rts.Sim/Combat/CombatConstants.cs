namespace Rts.Sim.Combat;

/// <summary>Combat tunables that are engine rules, not unit stats (docs/03 "Orders and unit states", "Implementation (M4-1)").</summary>
public static class CombatConstants
{
    /// <summary>Ticks between two target scans of one unit; unit slot s scans on ticks where <c>(tick + s) % ScanInterval == 0</c>.</summary>
    public const int ScanInterval = 4;

    /// <summary>Meters past its reach a target may be at the wind-up point and still be hit (Producer default, M4-1); farther, the swing is lost.</summary>
    public const float WindupGrace = 0.5f;

    /// <summary>Meters past its own radius from a building's footprint where a chaser aims to stand: inside a melee reach of 0.5 m.</summary>
    public const float BuildingStandOff = 0.25f;

    /// <summary>
    /// Scans in a row (4 ticks each) a chaser may get no closer to its target before it gives the target up (BUG-0137):
    /// 10 scans = 2 s. Covers a target it can't reach (another plateau, a sealed pocket), one only reachable by a detour
    /// that leads away, and one that runs as fast as it does.
    /// </summary>
    public const int GiveUpScans = 10;

    /// <summary>Meters a chaser must gain on its best gap so far for a scan to count as progress (BUG-0137): crowd jitter is not.</summary>
    public const float ChaseProgress = 0.1f;

    /// <summary>
    /// Chases a unit may give up before its scans take only targets in reach (BUG-0137): it only remembers the last
    /// target it gave up, so two out of reach would otherwise take turns forever. Reset by an order or a landed hit.
    /// </summary>
    public const int MaxGiveUps = 3;

    /// <summary>Fraction of a splash radius inside which a victim takes full damage (docs/02 "Splash and friendly fire": 100 % within 40 %).</summary>
    public const float SplashFullFraction = 0.4f;

    /// <summary>Damage factor at the edge of a splash radius; it falls linearly from 1 at <see cref="SplashFullFraction"/> to this (docs/02: 50 %).</summary>
    public const float SplashEdgeFactor = 0.5f;

    /// <summary>Damage factor for own and allied units in a friendly-fire splash, after the falloff (docs/02: 50 %).</summary>
    public const float FriendlyFireFactor = 0.5f;

    /// <summary>The armor class id (<c>common/damage_table.json</c>) buildings take damage as.</summary>
    public const string StructureClassKey = "structure";
}
