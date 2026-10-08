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

    /// <summary>The armor class id (<c>common/damage_table.json</c>) buildings take damage as.</summary>
    public const string StructureClassKey = "structure";
}
