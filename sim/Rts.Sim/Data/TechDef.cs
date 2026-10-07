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
    /// <summary>Tech / building ids it requires, as written; every id exists (checked at load). Kept for tools; the sim reads <see cref="RequiresTechs"/> / <see cref="RequiresBuildings"/>.</summary>
    public required ImmutableArray<string> Requires { get; init; }
    /// <summary><see cref="Requires"/>' tech ids, resolved at load (M3-6), ascending: each must be researched before this tech can be queued.</summary>
    /// <remarks>Set once by the loader; never written afterwards. Empty in hand-built data.</remarks>
    public ImmutableArray<int> RequiresTechs { get; internal set; } = ImmutableArray<int>.Empty;
    /// <summary><see cref="Requires"/>' building type ids, resolved at load (M3-6), ascending: the player needs an own finished building of each type.</summary>
    /// <remarks>Set once by the loader; never written afterwards. Empty in hand-built data.</remarks>
    public ImmutableArray<int> RequiresBuildings { get; internal set; } = ImmutableArray<int>.Empty;
    /// <summary>
    /// <c>requiresAnyOf.of</c> as written (M3-6): building slot ids (or, in a faction's own techs file, its building ids);
    /// empty when the tech has no any-of rule.
    /// </summary>
    public ImmutableArray<string> RequiresAnyOf { get; init; } = ImmutableArray<string>.Empty;
    /// <summary><c>requiresAnyOf.count</c> (M3-6): how many of <see cref="RequiresAnyOfSlots"/> need an own finished building; 0 when the tech has no any-of rule.</summary>
    public int RequiresAnyOfCount { get; init; }
    /// <summary>
    /// <see cref="RequiresAnyOf"/> resolved at load to <see cref="BuildingSlot"/> values (a building id becomes its slot), ascending
    /// and distinct (M3-6). A slot counts once however many own finished buildings fill it.
    /// </summary>
    /// <remarks>Set once by the loader; never written afterwards. Empty in hand-built data.</remarks>
    public ImmutableArray<int> RequiresAnyOfSlots { get; internal set; } = ImmutableArray<int>.Empty;
    /// <summary>What it changes once researched; empty for a tech that only unlocks (Age II).</summary>
    public required ImmutableArray<TechEffect> Effects { get; init; }
}
