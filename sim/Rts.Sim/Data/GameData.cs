using System;
using System.Collections.Immutable;

namespace Rts.Sim.Data;

/// <summary>All loaded definitions, immutable, indexed by dense int ids. Build it with <see cref="DataLoader.LoadAll"/>.</summary>
/// <remarks>
/// Tick code indexes the arrays by id. The string lookups (<see cref="FindUnit"/>, <see cref="FindFaction"/>)
/// are for load time, commands from tests/UI, and tooling: they binary-search the sorted key arrays.
/// </remarks>
public sealed class GameData
{
    internal GameData()
    {
    }

    /// <summary>Damage type × armor class table.</summary>
    public required DamageTable DamageTable { get; init; }
    /// <summary>Global economy rules.</summary>
    public required RulesDef Rules { get; init; }
    /// <summary>Factions, indexed by faction id (ordinal order of their string ids).</summary>
    public required ImmutableArray<FactionDef> Factions { get; init; }
    /// <summary>Unit types of every faction, indexed by unit id (ordinal order of their string ids).</summary>
    public required ImmutableArray<UnitDef> Units { get; init; }

    /// <summary>Unit id for a string id, or -1.</summary>
    public int FindUnit(string key) => Find(Units, static u => u.Key, key);

    /// <summary>Faction id for a string id, or -1.</summary>
    public int FindFaction(string key) => Find(Factions, static f => f.Key, key);

    // Binary search: the arrays are in ordinal order of their keys, which is how ids were assigned.
    private static int Find<T>(ImmutableArray<T> items, Func<T, string> keyOf, string key)
    {
        int lo = 0, hi = items.Length - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            int c = string.CompareOrdinal(keyOf(items[mid]), key);
            if (c == 0) return mid;
            if (c < 0) lo = mid + 1; else hi = mid - 1;
        }
        return -1;
    }
}
