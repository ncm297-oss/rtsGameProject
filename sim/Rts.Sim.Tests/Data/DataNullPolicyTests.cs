using System.Text.Json.Nodes;
using Rts.Sim.Data;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M7-H3 (docs/01 2026-11-25 (3), docs/03 "Data format", "A JSON <c>null</c>"): what a JSON <c>null</c> means in a data file,
/// pinned field by field as the loader reads it today (no loader change). One table a side: the optional fields whose
/// <c>null</c> reads as left out (a copy with the field <c>null</c> loads to the same content hash as the copy with the field
/// removed), and the optional numbers read raw so that <c>null</c> is one load error at the field's path. Each row edits a
/// row found by id (rule 58: a field the shipped row already carries is removed for the "left out" twin, so the pair is
/// the same edit whatever the data ships).
/// </summary>
public class DataNullPolicyTests
{
    private readonly ITestOutputHelper _out;

    public DataNullPolicyTests(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// The fields whose <c>null</c> reads as left out: (file, the object holding the field, the field). The object path is
    /// dotted; <c>list[key=value]</c> picks the first element of <c>list</c> whose <c>key</c> is <c>value</c>.
    /// </summary>
    public static IEnumerable<object[]> NullIsLeftOut() => new[]
    {
        // Optional blocks.
        new object[] { "factions/malazan/faction.json", "", "regen" },
        new object[] { "factions/malazan/faction.json", "", "forestStealth" },
        new object[] { "factions/malazan/units.json", "units[id=malazan_heavy_infantry]", "stillStealth" },
        new object[] { "common/statuses.json", "statuses[id=burning]", "onEnd" },
        new object[] { "factions/malazan/buildings.json", "buildings[id=malazan_barracks]", "attack" },
        new object[] { "common/rules.json", "", "draw" }, // M6-S13: the draw block written null is no draw rule
        // Optional numbers.
        new object[] { "factions/malazan/units.json", "units[id=malazan_heavy_infantry]", "scale" },
        new object[] { "factions/malazan/units.json", "units[id=malazan_heavy_infantry]", "spawnCount" },
        new object[] { "factions/malazan/units.json", "units[id=malazan_heavy_infantry]", "detector" },
        new object[] { "factions/malazan/units.json", "units[id=malazan_heavy_infantry].attack", "fromStealthBonus" },
        new object[] { "factions/malazan/units.json", "units[id=malazan_heavy_infantry].attack", "minRange" },
        new object[] { "factions/malazan/units.json", "units[id=malazan_heavy_infantry].attack", "splash" },
        new object[] { "factions/malazan/buildings.json", "buildings[id=malazan_barracks]", "scale" },
        new object[] { "factions/malazan/buildings.json", "buildings[id=malazan_barracks]", "detector" },
        new object[] { "common/statuses.json", "statuses[id=burning]", "damageBonus" },
        new object[] { "common/rules.json", "startingResources[id=standard]", "aiSpendAbove" },
        // Optional switches (docs/03 "Implementation (M7-8)": "left out or JSON null is off").
        new object[] { "factions/malazan/ai.json", "openings[id=standard].buildOrder[do=train]", "throughReserve" },
        new object[] { "factions/malazan/ai.json", "openings[id=standard].buildOrder[do=build]", "noReserve" },
    };

    /// <summary>The optional numbers read raw so that a <c>null</c> is an error: (file, object path, field, the error's path).</summary>
    public static IEnumerable<object[]> NullIsAnError() => new[]
    {
        new object[] { "common/rules.json", "", "gatherOverlap", "gatherOverlap" },
        new object[] { "factions/teblor/units.json", "units[id=teblor_gatherer]", "carry", "units[{0}].carry" },
        // M8-4: a unit type's limit written null is an error (leave the field out for no limit).
        new object[] { "factions/malazan/units.json", "units[id=malazan_heavy_infantry]", "maxAlive", "units[{0}].maxAlive" },
        new object[] { "factions/malazan/ai.json", "openings[id=standard].attack", "reserveArmyShare", "openings[{0}].attack.reserveArmyShare" },
        // M7-9: an opening's per-difficulty build orders written null is an error (leave the field out for "no entry").
        new object[] { "factions/teblor/ai.json", "openings[id=standard]", "buildOrders", "openings[{0}].buildOrders" },
        // M6-S13: inside the draw block the wait is required; null is an error, not "no rule" (leave the block out for that).
        new object[] { "common/rules.json", "draw", "afterSeconds", "draw.afterSeconds" },
    };

    [Theory]
    [MemberData(nameof(NullIsLeftOut))]
    public void ANull_ReadsAsLeftOut(string file, string at, string field)
    {
        using TestDataDir left = TestDataDir.CopyOfShipped(), nul = TestDataDir.CopyOfShipped();
        left.EditJson(file, root => Find(root, at, out _).Remove(field));
        nul.EditJson(file, root => Find(root, at, out _)[field] = null);
        DataLoadResult a = DataLoader.LoadAll(left.Path), b = DataLoader.LoadAll(nul.Path);
        Assert.True(a.Ok, $"left out: {string.Join("; ", a.Errors)}");
        Assert.True(b.Ok, $"{file} {at}.{field}: null: {string.Join("; ", b.Errors)}");
        Assert.Equal(a.Data!.ContentHash(), b.Data!.ContentHash());
        _out.WriteLine($"{file} {at}.{field}: null loads as left out ({b.Data.ContentHash():X16})");
    }

    [Theory]
    [MemberData(nameof(NullIsAnError))]
    public void ANull_IsOneLoadError(string file, string at, string field, string errorPath)
    {
        int index = -1;
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson(file, root => Find(root, at, out index)[field] = null);
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        DataError e = Assert.Single(r.Errors);
        _out.WriteLine(e.ToString());
        Assert.Equal(file, e.File);
        Assert.Equal(string.Format(errorPath, index), e.Path);
        Assert.Contains("null is not", e.Message);
    }

    /// <summary>The object at dotted <paramref name="path"/> under <paramref name="root"/> ("" = the root); <paramref name="firstIndex"/> is the index the first <c>[key=value]</c> picked.</summary>
    private static JsonObject Find(JsonObject root, string path, out int firstIndex)
    {
        firstIndex = -1;
        JsonObject at = root;
        if (path.Length == 0) return at;
        foreach (string part in path.Split('.'))
        {
            int open = part.IndexOf('[');
            if (open < 0)
            {
                at = at[part]!.AsObject();
                continue;
            }
            string list = part[..open];
            string[] kv = part[(open + 1)..^1].Split('=');
            JsonArray items = at[list]!.AsArray();
            int i = 0;
            while (i < items.Count && (string?)items[i]![kv[0]] != kv[1]) i++;
            Assert.True(i < items.Count, $"no {list} element with {kv[0]} = {kv[1]}");
            if (firstIndex < 0) firstIndex = i;
            at = items[i]!.AsObject();
        }
        return at;
    }
}
