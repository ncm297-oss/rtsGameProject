using System.Text.Json.Nodes;
using Rts.Sim.Data;
using Rts.Sim.Tests.Content;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA D30 (session 2026-11-13-1118): the Teblor landing's copy (<see cref="TeblorLanding"/>) and the pins taught M7-4's
/// fields, attacked independently of the developer's own wrong copies:
/// <list type="bullet">
/// <item>the copy differs from the shipped data in exactly docs/03's four rows (and the two descriptions that state them),
/// every other file byte for byte;</item>
/// <item>the M4 statuses' state-hash numbers are pinned on the shipped data itself (not only on the copy), so a landing
/// whose Frenzy id sorts before <c>stealthed</c> (<c>blood_oil_frenzied</c> sorts before <c>burning</c>) is caught by
/// name the session it ships, before any golden moves;</item>
/// <item>QA's own mutants of each new arm (other numbers and other fields than the developer's): each must be named.</item>
/// </list>
/// </summary>
public class D30LandingQaTests
{
    private const string Teblor = "teblor";

    private static void AssertNone(IEnumerable<string> problems) => Assert.True(!problems.Any(), string.Join("\n", problems));

    private static void AssertNamed(IEnumerable<string> problems, string want) =>
        Assert.True(problems.Any(p => p.Contains(want, StringComparison.Ordinal)), $"no problem containing '{want}':\n" + string.Join("\n", problems));

    private static JsonObject Parse(TestDataDir d, string rel) => JsonNode.Parse(File.ReadAllText(d.FullPath(rel)))!.AsObject();

    private static JsonObject Row(JsonObject root, string list, string id) =>
        root[list]!.AsArray().Single(n => (string)n!["id"]! == id)!.AsObject();

    // ---------- the copy is the shipped data plus exactly the four rows ----------

    [Fact]
    public void TheLandingCopy_DiffersFromShipped_InExactlyTheFourRows()
    {
        using TestDataDir shipped = TestDataDir.CopyOfShipped();
        using TestDataDir copy = TestDataDir.CopyOfShipped();
        // Once D31 ships the rows, "shipped" is the data before them (the reverse copy); until then the shipped files as they are.
        if (TestSim.Data.FindStatus(TeblorLanding.Status) >= 0)
            foreach (string rel in TeblorLanding.Files) shipped.EditJson(rel, root => TeblorLanding.BeforeRoot(rel, root));
        TeblorLanding.Edit(copy);
        string[] four = TeblorLanding.Files;
        Assert.Equal(new[] { "common/statuses.json", "factions/teblor/abilities.json", "factions/teblor/techs.json", "factions/teblor/buildings.json" }, four);

        // Every other file untouched, byte for byte (and no file added or removed).
        string[] a = Directory.GetFiles(shipped.Path, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(shipped.Path, f).Replace('\\', '/')).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        string[] b = Directory.GetFiles(copy.Path, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(copy.Path, f).Replace('\\', '/')).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Assert.Equal(a, b);
        foreach (string rel in a.Where(r => !four.Contains(r)))
            Assert.True(File.ReadAllBytes(shipped.FullPath(rel)).SequenceEqual(File.ReadAllBytes(copy.FullPath(rel))), $"the copy changed {rel}");

        // statuses.json: one row appended, docs/03's, every shipped row unchanged and in place.
        JsonArray s0 = Parse(shipped, four[0])["statuses"]!.AsArray(), s1 = Parse(copy, four[0])["statuses"]!.AsArray();
        Assert.Equal(s0.Count + 1, s1.Count);
        for (int i = 0; i < s0.Count; i++) Assert.True(JsonNode.DeepEquals(s0[i], s1[i]), $"statuses[{i}] changed");
        JsonObject added = s1[^1]!.AsObject();
        Assert.Equal("teblor_frenzied", (string)added["id"]!);
        Assert.Equal("frenzied", (string)added["kind"]!);
        Assert.Equal(0.2, (double)added["damageBonus"]!);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{ "status": "slowed", "magnitude": 0.2, "duration": 4 }"""), added["onEnd"]));
        Assert.Equal(new[] { "damageBonus", "description", "displayName", "id", "kind", "onEnd" }, added.Select(kv => kv.Key).OrderBy(k => k, StringComparer.Ordinal));

        // abilities.json: only blood_oil_frenzy's effect, and only its status id.
        JsonObject ab0 = Parse(shipped, four[1]), ab1 = Parse(copy, four[1]);
        JsonArray al0 = ab0["abilities"]!.AsArray(), al1 = ab1["abilities"]!.AsArray();
        Assert.Equal(al0.Count, al1.Count);
        for (int i = 0; i < al0.Count; i++)
        {
            JsonObject x = al0[i]!.DeepClone().AsObject(), y = al1[i]!.DeepClone().AsObject();
            if ((string)x["id"]! == "blood_oil_frenzy")
            {
                Assert.Equal("frenzied", (string)x["effects"]![0]!["status"]!);
                Assert.Equal("teblor_frenzied", (string)y["effects"]![0]!["status"]!);
                y["effects"]![0]!["status"] = "frenzied";
            }
            Assert.True(JsonNode.DeepEquals(x, y), $"abilities[{i}] changed beyond the Frenzy's status id");
        }

        // techs.json: only the Anointing's third effect and its description.
        JsonArray t0 = Parse(shipped, four[2])["techs"]!.AsArray(), t1 = Parse(copy, four[2])["techs"]!.AsArray();
        Assert.Equal(t0.Count, t1.Count);
        for (int i = 0; i < t0.Count; i++)
        {
            JsonObject x = t0[i]!.DeepClone().AsObject(), y = t1[i]!.DeepClone().AsObject();
            if ((string)x["id"]! == "teblor_blood_oil_anointing")
            {
                JsonArray ye = y["effects"]!.AsArray();
                Assert.Equal(x["effects"]!.AsArray().Count + 1, ye.Count);
                Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{ "stat": "statusDuration", "amount": 4, "statuses": ["teblor_frenzied"] }"""), ye[^1]));
                ye.RemoveAt(ye.Count - 1);
                Assert.NotEqual((string)x["description"]!, (string)y["description"]!);
                y["description"] = (string)x["description"]!;
            }
            Assert.True(JsonNode.DeepEquals(x, y), $"techs[{i}] changed beyond the Anointing's third effect");
        }

        // buildings.json: each row gains scale 1.25, nothing else.
        JsonArray b0 = Parse(shipped, four[3])["buildings"]!.AsArray(), b1 = Parse(copy, four[3])["buildings"]!.AsArray();
        Assert.Equal(10, b1.Count);
        for (int i = 0; i < b0.Count; i++)
        {
            Assert.Null(b0[i]!["scale"]);
            JsonObject y = b1[i]!.DeepClone().AsObject();
            Assert.Equal(1.25, (double)y["scale"]!);
            y.Remove("scale");
            Assert.True(JsonNode.DeepEquals(b0[i], y), $"buildings[{i}] changed beyond scale");
        }
    }

    // ---------- the state-hash numbering hazard (docs/03: an id sorting before `burning`) ----------

    /// <summary>The M4 statuses' state-hash numbers as M7-1 fixed them (their ids' ordinal order); every pinned digest with a status in it depends on them.</summary>
    private static readonly (string Key, int Number)[] M4Numbers =
    {
        ("blinded", 0), ("burning", 1), ("frenzied", 2), ("revealed", 3), ("slowed", 4), ("stealthed", 5),
    };

    private static List<string> M4NumberProblems(GameData data)
    {
        var problems = new List<string>();
        foreach ((string key, int number) in M4Numbers)
        {
            int i = data.FindStatus(key);
            if (i < 0) { problems.Add($"status '{key}' is gone"); continue; }
            if (data.Statuses[i].StateHashId != number)
                problems.Add($"status '{key}' state-hash number {data.Statuses[i].StateHashId}, pinned {number}: a new M4-kind status id sorts before it (docs/03 'For the data track (M7-4)': pick an id after 'stealthed')");
        }
        return problems;
    }

    /// <summary>
    /// The hazard pinned on what ships (it holds today and after D31 with <c>teblor_frenzied</c>): a landing with any id of
    /// an M4 kind sorting before <c>stealthed</c> fails here, by name, instead of as unexplained golden digest moves.
    /// </summary>
    [Fact]
    public void ShippedData_TheM4StatusesKeepTheirStateHashNumbers()
    {
        AssertNone(M4NumberProblems(TestSim.Data));
        AssertNone(M4NumberProblems(TeblorLanding.Data));
    }

    [Theory]
    [InlineData("blood_oil_frenzied", "status 'burning' state-hash number 2, pinned 1")] // before burning: docs/03's example
    [InlineData("frenzied_teblor", "status 'revealed' state-hash number 4, pinned 3")]   // between frenzied and revealed
    [InlineData("shrieking", "status 'slowed' state-hash number 5, pinned 4")]           // between revealed and slowed
    public void ALandingWithAnIdBeforeStealthed_IsNamed(string id, string want)
    {
        GameData d = TeblorLanding.DataWith(dir =>
        {
            foreach (string rel in TeblorLanding.Files.Take(3))
                File.WriteAllText(dir.FullPath(rel), File.ReadAllText(dir.FullPath(rel)).Replace("\"teblor_frenzied\"", $"\"{id}\""));
        });
        Assert.True(d.FindStatus(id) >= 0);
        Assert.Equal(-1, d.FindStatus(TeblorLanding.Status));
        AssertNamed(M4NumberProblems(d), want);
    }

    // ---------- QA's own mutants of each new arm ----------

    private static GameData Wrong(string rel, string list, string id, Action<JsonObject> edit) =>
        TeblorLanding.DataWith(dir => dir.EditJson(rel, root => edit(Row(root, list, id))));

    [Fact]
    public void Mutant_AnointingAt3s_IsNamed_ByTheTechPinAndTheClaims()
    {
        GameData wrong = Wrong("factions/teblor/techs.json", "techs", "teblor_blood_oil_anointing", t => t["effects"]![2]!["amount"] = 3);
        AssertNamed(RuleRowClaims.Problems(wrong, Teblor, TeblorLanding.PageLines()), "page 'duration +4 s' vs data [3]");
        using (ContentScope.Use(TeblorLanding.Scope(wrong)))
            Assert.NotNull(Record.Exception(new TechContentTests().C_ThePagesTechsTable_MatchesTheData));
    }

    /// <summary>The +4 s on the Zealot's plain <c>frenzied</c> instead of the Teblor status: the techs table names the status, so C fails.</summary>
    [Fact]
    public void Mutant_StatusDurationOnTheWrongStatus_IsNamed()
    {
        GameData wrong = Wrong("factions/teblor/techs.json", "techs", "teblor_blood_oil_anointing", t => t["effects"]![2]!["statuses"] = new JsonArray("frenzied"));
        using (ContentScope.Use(TeblorLanding.Scope(wrong)))
        {
            Exception? c = Record.Exception(new TechContentTests().C_ThePagesTechsTable_MatchesTheData);
            Assert.True(c != null && c.Message.Contains("Frenzied duration +4 s", StringComparison.Ordinal), c?.Message ?? "C passed");
        }
    }

    [Fact]
    public void Mutant_OnEndMagnitude03_IsNamed()
    {
        GameData wrong = Wrong("common/statuses.json", "statuses", TeblorLanding.Status, s => s["onEnd"]!["magnitude"] = 0.3);
        string[] page = TeblorLanding.PageLines();
        AssertNamed(RuleRowClaims.Problems(wrong, Teblor, page), "page 'Slowed 20% for 4 s' vs data [30% for 4 s]");
        AssertNamed(AbilityContentTests.Compare(wrong, wrong.Abilities, Teblor, page).Problems, "'Blood-oil Frenzy' Effect onEnd:");
    }

    /// <summary>The aftermath turned into a Blind: the page's "Slowed 20% for 4 s" has nothing behind it.</summary>
    [Fact]
    public void Mutant_OnEndBlinded_IsNamed()
    {
        GameData wrong = Wrong("common/statuses.json", "statuses", TeblorLanding.Status, s => s["onEnd"] = JsonNode.Parse("""{ "status": "blinded", "duration": 4 }"""));
        string[] page = TeblorLanding.PageLines();
        AssertNamed(RuleRowClaims.Problems(wrong, Teblor, page), "page 'Slowed 20% for 4 s' vs data []");
        AssertNamed(AbilityContentTests.Compare(wrong, wrong.Abilities, Teblor, page).Problems, "'Blood-oil Frenzy' Effect onEnd:");
    }

    /// <summary>The damage bonus dropped from the row (the rest landed): the page's "+20% damage" is a promise nothing keeps.</summary>
    [Fact]
    public void Mutant_NoDamageBonus_IsNamed()
    {
        GameData wrong = Wrong("common/statuses.json", "statuses", TeblorLanding.Status, s => s.Remove("damageBonus"));
        string[] page = TeblorLanding.PageLines();
        AssertNamed(RuleRowClaims.Problems(wrong, Teblor, page), "page '+20% damage' vs data []");
        AssertNamed(AbilityContentTests.Compare(wrong, wrong.Abilities, Teblor, page).Problems, "'Blood-oil Frenzy' Effect:");
    }

    /// <summary>
    /// BUG-1330: the status tooltip ("deals 20% more damage ... Slowed 20% for 4 seconds") against a row whose damage bonus
    /// or aftermath magnitude moved alone. The description-number rule pools both into one set of percentages, so the other
    /// field's 20% keeps the stale one green: the player-facing text can go wrong with every pin passing.
    /// </summary>
    [Theory(Skip = "BUG-1330: the description rule cannot tell the Frenzy row's two 20%s apart (un-skip with the fix)")]
    [InlineData("onEnd.magnitude 0.3")]
    [InlineData("damageBonus 0.3")]
    [InlineData("damageBonus removed")]
    public void Mutant_OneOfTheTwo20s_Moved_TheStatusTooltipIsNamed(string mutant)
    {
        GameData wrong = Wrong("common/statuses.json", "statuses", TeblorLanding.Status, s =>
        {
            if (mutant == "onEnd.magnitude 0.3") s["onEnd"]!["magnitude"] = 0.3;
            else if (mutant == "damageBonus 0.3") s["damageBonus"] = 0.3;
            else s.Remove("damageBonus");
        });
        AssertNamed(AbilityContentTests.DescriptionProblems(wrong, wrong.Abilities, wrong.Statuses), "teblor_frenzied description:");
    }

    /// <summary>One Teblor building left at the default 1 (the landing missed a row): the Scale column names it.</summary>
    [Fact]
    public void Mutant_OneBuildingLeftAt1_IsNamed()
    {
        GameData wrong = Wrong("factions/teblor/buildings.json", "buildings", "teblor_longhouse", b => b.Remove("scale"));
        Assert.Equal(new[] { "teblor.md Buildings row Longhouse scale: page 1.25 vs data 1" },
            BuildingContentTests.ScaleProblems(wrong, Teblor, TeblorLanding.PageLines()));
    }

    /// <summary>The 1.25 put on another faction's building: that page has no Scale column, so its pin names it.</summary>
    [Fact]
    public void Mutant_AnotherFactionsBuildingScaled_IsNamed()
    {
        GameData wrong = TeblorLanding.DataWith(dir => dir.EditJson("factions/malazan/buildings.json", root => root["buildings"]![0]!["scale"] = 1.25));
        List<string> problems = BuildingContentTests.ScaleProblems(wrong, "malazan", PageTables.FactionLines("malazan"));
        AssertNamed(problems, "scale: the page has no Scale column, data 1.25");
    }

    /// <summary>The status tooltip's numbers held the other way: data 4 s, text "5 seconds".</summary>
    [Fact]
    public void Mutant_StatusDescriptionSays5Seconds_IsNamed()
    {
        var (abilities, statuses) = AbilityContentTests.WithDescription(TeblorLanding.Status, "for 4 seconds", "for 5 seconds", TeblorLanding.Data);
        AssertNamed(AbilityContentTests.DescriptionProblems(TeblorLanding.Data, abilities, statuses), "teblor_frenzied description: '5 seconds'");
    }
}
