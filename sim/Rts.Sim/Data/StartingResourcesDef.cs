namespace Rts.Sim.Data;

/// <summary>
/// M6-S6: one starting-resources preset from <c>common/rules.json</c> <c>startingResources</c> (docs/02 "Game flow" 2): the
/// gold and wood every seat holds at tick 0 when a match picks it (<see cref="SimConfig.StartingResources"/>).
/// </summary>
public sealed class StartingResourcesDef
{
    /// <summary>The preset every match plays unless it picks another, and the one a save or replay from before presets plays: <c>rules.json</c> must define it.</summary>
    public const string StandardId = "standard";

    internal StartingResourcesDef()
    {
    }

    /// <summary>The preset's snake_case id (what a save, a replay and <see cref="SimConfig.StartingResources"/> name it by).</summary>
    public string Key { get; init; } = "";

    /// <summary>Player-facing name (the setup screen's list).</summary>
    public string DisplayName { get; init; } = "";

    /// <summary>Player-facing description; empty when the file gives none.</summary>
    public string Description { get; init; } = "";

    /// <summary>Gold each seat starts with.</summary>
    public int Gold { get; init; }

    /// <summary>Wood each seat starts with.</summary>
    public int Wood { get; init; }

    /// <summary>
    /// BUG-1050: an AI seat spends down while its gold + wood is above this (<c>aiSpendAbove</c>, optional, 0 to
    /// <see cref="DataLimits.MaxInteger"/>; docs/03 "Implementation (M6-S6)"); 0, or the field left out, turns the rule off.
    /// </summary>
    public int AiSpendAbove { get; init; }
}
