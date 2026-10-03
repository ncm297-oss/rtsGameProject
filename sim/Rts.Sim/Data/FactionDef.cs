using System.Collections.Immutable;

namespace Rts.Sim.Data;

/// <summary>One faction from <c>factions/&lt;id&gt;/faction.json</c>.</summary>
public sealed class FactionDef
{
    internal FactionDef()
    {
    }

    /// <summary>Dense faction id (index into <see cref="GameData.Factions"/>).</summary>
    public int Id { get; init; }
    /// <summary>String id, equal to the folder name.</summary>
    public required string Key { get; init; }
    /// <summary>Player-facing name.</summary>
    public required string DisplayName { get; init; }
    /// <summary>Player-facing blurb.</summary>
    public required string Description { get; init; }
    /// <summary>Player-facing name of the faction bonus.</summary>
    public required string BonusDisplayName { get; init; }
    /// <summary>Player-facing description of the faction bonus.</summary>
    public required string BonusDescription { get; init; }
    /// <summary>This faction's display name for Gold.</summary>
    public required string GoldName { get; init; }
    /// <summary>This faction's display name for Wood.</summary>
    public required string WoodName { get; init; }
    /// <summary>Primary palette color as 0xRRGGBB.</summary>
    public uint PrimaryColor { get; init; }
    /// <summary>Secondary palette color as 0xRRGGBB.</summary>
    public uint SecondaryColor { get; init; }
    /// <summary>Accent palette color as 0xRRGGBB.</summary>
    public uint AccentColor { get; init; }
    /// <summary>Ids of this faction's units, ascending.</summary>
    public required ImmutableArray<int> Units { get; init; }
}
