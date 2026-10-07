using System.Collections.Immutable;

namespace Rts.Sim.Data;

/// <summary>One tech from <c>common/techs.json</c> or <c>factions/&lt;id&gt;/techs.json</c> (docs/02 "Tech", M3-5), converted to sim units.</summary>
public sealed class TechDef
{
    internal TechDef()
    {
    }

    /// <summary>Dense tech id (index into <see cref="GameData.Techs"/>), ordinal order of the string ids across every techs file.</summary>
    public int Id { get; init; }
    /// <summary>String id, e.g. <c>age_ii</c>.</summary>
    public required string Key { get; init; }
    /// <summary>Owning faction id, or -1 for a common tech every faction researches.</summary>
    public int Faction { get; init; }
    /// <summary>Player-facing name.</summary>
    public required string DisplayName { get; init; }
    /// <summary>Player-facing tooltip text.</summary>
    public required string Description { get; init; }
    /// <summary>The building slot that researches it; a common tech resolves to each faction's building of that slot.</summary>
    public BuildingSlot ResearchedAtSlot { get; init; }
    /// <summary>Gold cost.</summary>
    public int CostGold { get; init; }
    /// <summary>Wood cost.</summary>
    public int CostWood { get; init; }
    /// <summary>Ticks to research.</summary>
    public int ResearchTicks { get; init; }
    /// <summary>Tech / building ids it requires; every id exists (checked at load), gating is M3-6.</summary>
    public required ImmutableArray<string> Requires { get; init; }
    /// <summary>What it changes once researched; empty for a tech that only unlocks (Age II).</summary>
    public required ImmutableArray<TechEffect> Effects { get; init; }
}
