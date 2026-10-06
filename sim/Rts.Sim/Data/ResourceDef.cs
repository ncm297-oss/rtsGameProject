namespace Rts.Sim.Data;

/// <summary>One resource node type from <c>common/resources.json</c> (a tree, a gold mine). The amount a placed node holds comes from <see cref="RulesDef"/>.</summary>
public sealed class ResourceDef
{
    internal ResourceDef()
    {
    }

    /// <summary>Dense resource type id (index into <see cref="GameData.Resources"/>).</summary>
    public int Id { get; init; }
    /// <summary>String id, e.g. <c>gold_mine</c>.</summary>
    public required string Key { get; init; }
    /// <summary>Player-facing name.</summary>
    public required string DisplayName { get; init; }
    /// <summary>Player-facing tooltip text.</summary>
    public required string Description { get; init; }
    /// <summary>What gathering this node yields.</summary>
    public ResourceKind Resource { get; init; }
    /// <summary>Footprint width in nav cells (x), 1 to <see cref="DataLimits.MaxFootprint"/>.</summary>
    public int FootprintWidth { get; init; }
    /// <summary>Footprint height in nav cells (y), 1 to <see cref="DataLimits.MaxFootprint"/>.</summary>
    public int FootprintHeight { get; init; }
}
