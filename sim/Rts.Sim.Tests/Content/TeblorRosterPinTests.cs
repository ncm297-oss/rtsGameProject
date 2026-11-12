using System.Text.Json.Nodes;
using Rts.Sim.Data;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// D28: the Teblor roster as shipped, pinned to teblor.md where no shared pin reads it. (1) The page's "Owner review" table
/// is every player-facing name and description the Teblor files carry, character for character, both ways: each row is the
/// file's entry, and each Teblor faction, bonus, unit, building, faction tech, ability and opening has exactly one row.
/// (2) The shared pins read the Teblor both ways: a page cell changed by hand reads red, and a file value changed by hand
/// (in a temp copy of the data) reads red, for the Units table, the AI section and the Owner review table.
/// </summary>
public class TeblorRosterPinTests
{
    private const string Faction = "teblor";

    private static GameData Data => TestSim.Data;

    /// <summary>One Owner review row: the file cell as written ("`units.json`"), the id (null for "—"), the name, the description.</summary>
    private sealed record ReviewRow(string File, string? Id, string Name, string Description);

    /// <summary>The "## Owner review ..." table of <paramref name="lines"/> (its heading carries a dated suffix).</summary>
    private static List<ReviewRow> ReviewTable(string[] lines)
    {
        int h = PageTables.HeadingStartingWith(lines, "## Owner review");
        Assert.True(h >= 0, "teblor.md has no '## Owner review' section");
        var rows = new List<ReviewRow>();
        foreach (string[] c in PageTables.FirstTableRows(lines, h, stripBackticks: false))
        {
            Assert.True(c.Length == 4, $"teblor.md Owner review row '{string.Join(" | ", c)}': {c.Length} cells, expected 4");
            string id = c[1].Trim('`');
            rows.Add(new ReviewRow(c[0], id == "—" ? null : id, c[2], c[3]));
        }
        return rows;
    }

    /// <summary>Every player-facing (file cell, id, name, description) the Teblor files of <paramref name="d"/> carry, in file order.</summary>
    private static List<ReviewRow> FileEntries(GameData d)
    {
        int f = d.FindFaction(Faction);
        FactionDef fd = d.Factions[f];
        var rows = new List<ReviewRow>
        {
            new("`faction.json`", Faction, fd.DisplayName, fd.Description),
            new("`faction.json` bonus", null, fd.BonusDisplayName, fd.BonusDescription),
        };
        rows.AddRange(d.Units.Where(u => u.Faction == f).Select(u => new ReviewRow("`units.json`", u.Key, u.DisplayName, u.Description)));
        rows.AddRange(d.Buildings.Where(b => b.Faction == f).Select(b => new ReviewRow("`buildings.json`", b.Key, b.DisplayName, b.Description)));
        rows.AddRange(d.Techs.Where(t => t.Faction == f).Select(t => new ReviewRow("`techs.json`", t.Key, t.DisplayName, t.Description)));
        rows.AddRange(d.Abilities.Where(a => a.Faction == f).Select(a => new ReviewRow("`abilities.json`", a.Key, a.DisplayName, a.Description)));
        rows.AddRange(d.Ai[f].Openings.Select(o => new ReviewRow("`ai.json` opening", o.Id, o.DisplayName, o.Description)));
        return rows;
    }

    /// <summary>Every way the Owner review table of <paramref name="lines"/> and the Teblor files of <paramref name="d"/> differ; empty when equal.</summary>
    internal static List<string> OwnerReviewProblems(GameData d, string[] lines)
    {
        var problems = new List<string>();
        List<ReviewRow> page = ReviewTable(lines);
        List<ReviewRow> files = FileEntries(d);
        foreach (ReviewRow e in files)
        {
            ReviewRow[] own = page.Where(r => r.File == e.File && r.Id == e.Id).ToArray();
            string what = $"{e.File} {e.Id ?? "—"}";
            if (own.Length != 1) { problems.Add($"teblor.md Owner review: {own.Length} rows for {what}, expected 1"); continue; }
            if (own[0].Name != e.Name) problems.Add($"teblor.md Owner review {what} name: page '{own[0].Name}' vs data '{e.Name}'");
            if (own[0].Description != e.Description) problems.Add($"teblor.md Owner review {what} description: page '{own[0].Description}' vs data '{e.Description}'");
        }
        foreach (ReviewRow r in page)
            if (!files.Any(e => e.File == r.File && e.Id == r.Id))
                problems.Add($"teblor.md Owner review: row {r.File} {r.Id ?? "—"} is no entry of the Teblor files");
        return problems;
    }

    /// <summary>
    /// M7-3b's promise for an appended faction (docs/03 "Implementation (M7-3)"): every Teblor id comes after every shipped
    /// one, so the Teblor tech is 9 and its ability 3, its units 14-20 and its buildings 20-29, each ordinal within the faction.
    /// </summary>
    [Fact]
    public void TheTeblorIds_AreAppended_AfterEveryShippedId()
    {
        Assert.Equal(new[] { "malazan", "whirlwind", "teblor" }, ContentFactions.Keys);
        Assert.Equal(9, Data.FindTech("teblor_blood_oil_anointing"));
        Assert.Equal(3, Data.FindAbility("blood_oil_frenzy"));
        Assert.Equal(10, Data.Techs.Length);
        Assert.Equal(4, Data.Abilities.Length);
        int f = Data.FindFaction(Faction);
        string[] units = Data.Units.Where(u => u.Faction == f).Select(u => u.Key).ToArray();
        Assert.Equal(units.OrderBy(k => k, StringComparer.Ordinal), units);
        Assert.Equal(Enumerable.Range(14, 7), units.Select(Data.FindUnit));
        string[] buildings = Data.Buildings.Where(b => b.Faction == f).Select(b => b.Key).ToArray();
        Assert.Equal(buildings.OrderBy(k => k, StringComparer.Ordinal), buildings);
        Assert.Equal(Enumerable.Range(20, 10), buildings.Select(Data.FindBuilding));
    }

    [Fact]
    public void TheOwnerReviewTable_IsTheFilesWords_CharacterForCharacter_BothWays()
    {
        List<string> problems = OwnerReviewProblems(Data, PageTables.FactionLines(Faction));
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        // Not vacuous: 1 faction + 1 bonus + 7 units + 10 buildings + 1 tech + 1 ability + 1 opening.
        Assert.Equal(22, ReviewTable(PageTables.FactionLines(Faction)).Count);
    }

    private static string[] PageEdited(string find, string replace)
    {
        string[] lines = PageTables.FactionLines(Faction);
        int i = Array.FindIndex(lines, l => l.Contains(find, StringComparison.Ordinal));
        Assert.True(i >= 0, $"teblor.md has no line containing '{find}'");
        lines[i] = lines[i].Replace(find, replace, StringComparison.Ordinal);
        return lines;
    }

    private static GameData LoadEdited(Action<TestDataDir> edit)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        edit(dir);
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        return r.Data!;
    }

    private static void AssertNamed(IEnumerable<string> problems, string want) =>
        Assert.True(problems.Any(p => p.Contains(want, StringComparison.Ordinal)), $"expected a problem containing '{want}', got:\n{string.Join("\n", problems)}");

    /// <summary>Criterion 3, the page side: one Owner review cell changed by hand fails naming the entry and the field.</summary>
    [Theory]
    [InlineData("| Stone Cairn | A lookout", "| Stone Carin | A lookout", "teblor.md Owner review `buildings.json` teblor_stone_cairn name: page 'Stone Carin' vs data 'Stone Cairn'")]
    [InlineData("they heal between fights.", "they heal between fights", "teblor.md Owner review `faction.json` teblor description")]
    [InlineData("| `ai.json` opening | `standard` | War band |", "| `ai.json` opening | `standard` | Warband |", "teblor.md Owner review `ai.json` opening standard name")]
    [InlineData("| `units.json` | `teblor_war_dog` |", "| `units.json` | `teblor_wardog` |", "teblor.md Owner review: 0 rows for `units.json` teblor_war_dog")]
    public void AnOwnerReviewCellChanged_IsCaught(string find, string replace, string want) =>
        AssertNamed(OwnerReviewProblems(Data, PageEdited(find, replace)), want);

    /// <summary>Criterion 3, the file side: one name or description changed in a copy of the files fails naming the entry.</summary>
    [Fact]
    public void AFileWordChanged_IsCaught()
    {
        GameData d = LoadEdited(dir => dir.SetUnitField(Faction, "teblor_shaman", "description",
            "\"Rubs blood-oil into nearby warriors to drive them into frenzy. Detects hidden units.\""));
        AssertNamed(OwnerReviewProblems(d, PageTables.FactionLines(Faction)), "teblor.md Owner review `units.json` teblor_shaman description");
        GameData bonus = LoadEdited(dir => dir.EditJson("factions/teblor/faction.json", root => root["bonus"]!["displayName"] = "Children of the Mountain"));
        AssertNamed(OwnerReviewProblems(bonus, PageTables.FactionLines(Faction)), "teblor.md Owner review `faction.json` bonus — name");
    }

    /// <summary>Criterion 2, the Units table: a teblor.md cell changed by hand fails naming the row and the field.</summary>
    [Theory]
    [InlineData("| Unique | War-dog | `teblor_war_dog` | 70 |", "| Unique | War-dog | `teblor_war_dog` | 75 |", "teblor.md Units row War-dog hp/armor/class")]
    [InlineData("| 120 / 0 | 1 | 24 | 3 | 1 |", "| 120 / 0 | 1 | 24 | 2 | 1 |", "teblor.md Units row War-dog spawnCount: page 2 vs data 3")]
    [InlineData("| 180 / 120 | 3 | 45 | 1 | 1.5 |", "| 180 / 120 | 3 | 45 | 1 | 1.25 |", "teblor.md Units row Breaker scale: page 1.25 vs data 1.5")]
    [InlineData("| 3.4 | 16 | 120 / 30 | 2 | 28 |", "| 3.4 | 16 | 120 / 30 | 2 | 30 |", "teblor.md Units row Warrior pop/train")]
    public void AUnitsCellChanged_IsCaught(string find, string replace, string want)
    {
        Exception? e = Record.Exception(() => UnitContentTests.PinUnitsTable(Faction, PageEdited(find, replace)));
        Assert.True(e != null && e.Message.Contains(want, StringComparison.Ordinal), $"expected '{want}', got: {e?.Message ?? "no failure"}");
        UnitContentTests.PinUnitsTable(Faction, PageTables.FactionLines(Faction)); // the shipped page passes
    }

    /// <summary>Criterion 2, the Units table: a value changed in a copy of <c>units.json</c> fails naming the unit and the field.</summary>
    [Theory]
    [InlineData("teblor_warrior", "hp", "321", "teblor_warrior hp/armor/class")]
    [InlineData("teblor_destrier_rider", "trainTime", "35", "teblor_destrier_rider trainTime")]
    [InlineData("teblor_breaker", "armorClass", "\"heavy\"", "teblor_breaker hp/armor/class")]
    public void AUnitsFileValueChanged_IsCaught(string unit, string field, string rawJson, string want)
    {
        Assert.Empty(UnitContentTests.LoadedRosterProblems(Data, Faction));
        GameData d = LoadEdited(dir => dir.SetUnitField(Faction, unit, field, rawJson));
        AssertNamed(UnitContentTests.LoadedRosterProblems(d, Faction), want);
    }

    /// <summary>Criterion 2, the AI section (openings, steps, composition, wave): a page cell changed fails naming the row.</summary>
    [Theory]
    [InlineData("| 3 | pop 7 | build | `teblor_war_lodge` | 1 |", "| 3 | pop 8 | build | `teblor_war_lodge` | 1 |", "teblor standard step 3")]
    [InlineData("| 25 | time 570 | research |", "| 25 | time 600 | research |", "teblor standard step 25")]
    [InlineData("| `teblor_breaker` | — | 10 |", "| `teblor_breaker` | — | 15 |", "teblor standard")]
    [InlineData("first at 8 army pop and 600 gold + wood", "first at 8 army pop and 640 gold + wood", "teblor standard attack")]
    [InlineData("a unit under 40% hp", "a unit under 30% hp", "teblor")]
    public void AnAiCellChanged_IsCaught(string find, string replace, string want)
    {
        Assert.Empty(AiContentTests.Problems(Data, Faction, PageTables.FactionLines(Faction)));
        AssertNamed(AiContentTests.Problems(Data, Faction, PageEdited(find, replace)), want);
    }

    /// <summary>Criterion 2, the AI section: a value changed in a copy of <c>ai.json</c> fails naming the row.</summary>
    [Fact]
    public void AnAiFileValueChanged_IsCaught()
    {
        GameData step = LoadEdited(dir => dir.EditJson("factions/teblor/ai.json", root =>
            root["openings"]![0]!["buildOrder"]![1]!["at"] = JsonNode.Parse("{ \"pop\": 5 }")));
        AssertNamed(AiContentTests.Problems(step, Faction, PageTables.FactionLines(Faction)), "teblor standard step 2: page 'pop 6 | build | teblor_hut | 1' vs data 'pop 5 | build | teblor_hut | 1'");
        GameData wave = LoadEdited(dir => dir.EditJson("factions/teblor/ai.json", root =>
            root["openings"]![0]!["attack"]!["minArmyCost"] = 640));
        AssertNamed(AiContentTests.Problems(wave, Faction, PageTables.FactionLines(Faction)), "teblor standard attack");
    }

    /// <summary>Criterion 2, the Difficulty table: a page cell and a file value changed each fail.</summary>
    [Fact]
    public void ADifficultyCellOrFileValueChanged_IsCaught()
    {
        Assert.Empty(AiContentTests.DifficultyProblems(Data, Faction, PageTables.FactionLines(Faction)));
        Assert.NotEmpty(AiContentTests.DifficultyProblems(Data, Faction, PageEdited("| Hard | 0.5 | 0 | 1.25 |", "| Hard | 0.5 | 0 | 1.5 |")));
        GameData d = LoadEdited(dir => dir.EditJson("factions/teblor/ai.json", root =>
            root["difficulties"]!["easy"]!["expandAfter"] = 600));
        Assert.NotEmpty(AiContentTests.DifficultyProblems(d, Faction, PageTables.FactionLines(Faction)));
    }

    /// <summary>Criterion 2, "How the AI adapts": a page number and a file value changed each fail naming the Teblor.</summary>
    [Fact]
    public void AnAdaptsCellOrFileValueChanged_IsCaught()
    {
        Assert.Empty(AiAdaptsContentTests.Problems(Data, Faction, PageTables.FactionLines(Faction)));
        AssertNamed(AiAdaptsContentTests.Problems(Data, Faction,
            PageEdited("(`teblor_woodcutters_lodge`, 0 gold / 75 wood)", "(`teblor_woodcutters_lodge`, 0 gold / 70 wood)")),
            "teblor the far drop-off: page '70' vs data '75'");
        GameData d = LoadEdited(dir => dir.EditJson("factions/teblor/ai.json", root =>
            root["difficulties"]!["normal"]!["counterStrength"] = 0.75));
        AssertNamed(AiAdaptsContentTests.Problems(d, Faction, PageTables.FactionLines(Faction)), "teblor counterStrength: page '0.5' vs data '0.75'");
    }
}
