namespace Rts.Sim.Tests.Content;

/// <summary>
/// The factions the content pins run over (D-R36): the shipped data's factions, keys in data order
/// (<see cref="Rts.Sim.Data.GameData.Factions"/>), not a typed list, so a third faction's files are pinned to its page the
/// session they land. A pin that types a per-faction table (a roster, an upgrade) reads it through <see cref="Typed{T}"/>,
/// which fails naming the table and the faction's page when the row is missing rather than with a bare key error.
/// </summary>
internal static class ContentFactions
{
    /// <summary>
    /// Pages written ahead of their faction's data, checked for the sections the pins read
    /// (<see cref="PagePinReadinessTests"/>). Empty since D28 shipped the Teblor roster (teblor.md is a data faction's page
    /// now); the next faction page written ahead (M9's) goes here until its files land.
    /// </summary>
    public static readonly string[] PagesAhead = Array.Empty<string>();

    /// <summary>Every faction key of the shipped data, in data order.</summary>
    public static string[] Keys => TestSim.Data.Factions.Select(f => f.Key).ToArray();

    /// <summary>Every data faction, then each page written ahead of its data, once each.</summary>
    public static string[] Pages => Keys.Concat(PagesAhead).Distinct(StringComparer.Ordinal).ToArray();

    /// <summary>One theory row per data faction.</summary>
    public static TheoryData<string> Theory()
    {
        var rows = new TheoryData<string>();
        foreach (string key in Keys) rows.Add(key);
        return rows;
    }

    /// <summary>One theory row per page the readiness test checks (<see cref="Pages"/>).</summary>
    public static TheoryData<string> PageTheory()
    {
        var rows = new TheoryData<string>();
        foreach (string key in Pages) rows.Add(key);
        return rows;
    }

    /// <summary>
    /// <paramref name="faction"/>'s row of a typed pin table, failing "&lt;table&gt; has no '&lt;faction&gt;' row: type it
    /// from docs/factions/&lt;faction&gt;.md" when the data has a faction the table doesn't.
    /// </summary>
    public static T Typed<T>(IReadOnlyDictionary<string, T> table, string faction, string tableName)
    {
        Assert.True(table.TryGetValue(faction, out T? row),
            $"{tableName} has no '{faction}' row: the data ships faction '{faction}'; type the row from docs/factions/{faction}.md");
        return row!;
    }

    /// <summary>The faction after <paramref name="faction"/> in data order (wrapping), for pins that need another faction's id.</summary>
    public static string Other(string faction)
    {
        string[] keys = Keys;
        int i = Array.IndexOf(keys, faction);
        Assert.True(i >= 0, $"'{faction}' is not a faction of the shipped data");
        return keys[(i + 1) % keys.Length];
    }
}
