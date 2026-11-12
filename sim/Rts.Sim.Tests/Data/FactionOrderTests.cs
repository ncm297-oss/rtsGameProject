using System.Reflection;
using System.Text.Json.Nodes;
using Rts.Sim.Data;
using Rts.Sim.Tests.QA;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M7-3: <c>common/factions.json</c>, the append-only faction order (docs/03 "Implementation (M7-3)"). Faction id = index in
/// <c>order</c>, and unit and building type ids go faction by faction in that order (ordinal within a faction), so a third
/// faction's folder appended to the list moves no Malazan or Whirlwind id, state hash or digest; listed first, it renumbers
/// everything (the order is the rule, not the folder name). The load errors are one each, at <c>order[i]</c> or naming the folder.
/// M7-3b (BUG-1171): tech ids are <c>common/techs.json</c>'s first, then each faction's in the order, and ability ids faction
/// by faction, each ordinal within its file, so a third faction's naturally named tech and ability move nothing either.
/// </summary>
[Collection(SerialCollection.Name)]
public class FactionOrderTests
{
    private const string OrderFile = "common/factions.json";

    private readonly ITestOutputHelper _out;

    public FactionOrderTests(ITestOutputHelper output) => _out = output;

    // ------------------------------------------------------------------ criterion 2: the shipped ids

    /// <summary>
    /// Every shipped unit and building key's id, generated once on 0416dcc (the folders' ordinal order and the global key
    /// sort, before the order file). The order file must reproduce it bit for bit.
    /// </summary>
    private static readonly (string Key, int Id)[] Units0416 =
    {
        ("malazan_cadre_mage", 0), ("malazan_catapult", 1), ("malazan_crossbowman", 2), ("malazan_heavy_infantry", 3),
        ("malazan_laborer", 4), ("malazan_sapper", 5), ("malazan_wickan_lancer", 6), ("whirlwind_battering_ram", 7),
        ("whirlwind_camp_follower", 8), ("whirlwind_desert_archer", 9), ("whirlwind_horse_raider", 10), ("whirlwind_priest", 11),
        ("whirlwind_raider", 12), ("whirlwind_zealot", 13),
    };

    private static readonly (string Key, int Id)[] Buildings0416 =
    {
        ("malazan_armory", 0), ("malazan_barracks", 1), ("malazan_billet", 2), ("malazan_cadre_tower", 3),
        ("malazan_crossbow_range", 4), ("malazan_depot", 5), ("malazan_engineers_yard", 6), ("malazan_garrison_keep", 7),
        ("malazan_watchtower", 8), ("malazan_wickan_corral", 9), ("whirlwind_archer_camp", 10), ("whirlwind_holy_camp", 11),
        ("whirlwind_horse_lines", 12), ("whirlwind_lookout_tower", 13), ("whirlwind_raider_camp", 14), ("whirlwind_ram_yard", 15),
        ("whirlwind_shrine", 16), ("whirlwind_smithy", 17), ("whirlwind_supply_cache", 18), ("whirlwind_tent", 19),
    };

    /// <summary>
    /// M7-3b: every shipped tech key's id under the rule (the common file's ordinally, then Malazan's, then Whirlwind's). Before
    /// M7-3b (0416dcc to de591f0) one ordinal table across the files: age_ii 0, armor_1 1, armor_2 2, dryjhnas_prophecy 3,
    /// melee_weapons_1 4, melee_weapons_2 5, moranth_supply 6, ranged_weapons_1 7, ranged_weapons_2 8.
    /// </summary>
    private static readonly (string Key, int Id, int Faction)[] TechsM73b =
    {
        ("age_ii", 0, -1), ("armor_1", 1, -1), ("armor_2", 2, -1), ("melee_weapons_1", 3, -1), ("melee_weapons_2", 4, -1),
        ("ranged_weapons_1", 5, -1), ("ranged_weapons_2", 6, -1), ("moranth_supply", 7, 0), ("dryjhnas_prophecy", 8, 1),
    };

    /// <summary>M7-3b: every shipped ability key's id (Malazan's ordinally, then Whirlwind's). Before: cusser 0, sandstorm 1, telas_fire 2.</summary>
    private static readonly (string Key, int Id, int Faction)[] AbilitiesM73b = { ("cusser", 0, 0), ("telas_fire", 1, 0), ("sandstorm", 2, 1) };

    [Fact]
    public void TheShippedIds_AreBitIdenticalTo0416dcc_AndTheTechsAndAbilitiesToM73b()
    {
        GameData d = TestSim.Data;
        // M7-H1 (BUG-1191): the pin is about the first two factions, so it pins the tables' prefix (a faction appended to the
        // order adds its ids after these and moves none of them; the whole tables are pinned on the two-faction cut).
        Assert.Equal(new[] { "malazan", "whirlwind" }, TestSim.ShippedFactions.Take(2));
        Assert.Equal(Units0416, d.Units.Take(Units0416.Length).Select(u => (u.Key, u.Id)));
        Assert.Equal(Buildings0416, d.Buildings.Take(Buildings0416.Length).Select(b => (b.Key, b.Id)));
        Assert.Equal(TechsM73b, d.Techs.Take(TechsM73b.Length).Select(t => (t.Key, t.Id, t.Faction)));
        Assert.Equal(AbilitiesM73b, d.Abilities.Take(AbilitiesM73b.Length).Select(a => (a.Key, a.Id, a.Faction)));
        GameData two = TestSim.TwoFactionData;
        Assert.Equal(Units0416, two.Units.Select(u => (u.Key, u.Id)));
        Assert.Equal(Buildings0416, two.Buildings.Select(b => (b.Key, b.Id)));
        Assert.Equal(TechsM73b, two.Techs.Select(t => (t.Key, t.Id, t.Faction)));
        Assert.Equal(AbilitiesM73b, two.Abilities.Select(a => (a.Key, a.Id, a.Faction)));
        Assert.All(d.Units.Skip(Units0416.Length), u => Assert.True(u.Faction >= 2));
        Assert.All(d.Buildings.Skip(Buildings0416.Length), b => Assert.True(b.Faction >= 2));
        Assert.All(d.Techs.Skip(TechsM73b.Length), t => Assert.True(t.Faction >= 2));
        Assert.All(d.Abilities.Skip(AbilitiesM73b.Length), a => Assert.True(a.Faction >= 2));
        foreach ((string key, int id) in Units0416) Assert.Equal(id, d.FindUnit(key));
        foreach ((string key, int id) in Buildings0416) Assert.Equal(id, d.FindBuilding(key));
        foreach ((string key, int id, _) in TechsM73b) Assert.Equal(id, d.FindTech(key));
        foreach ((string key, int id, _) in AbilitiesM73b) Assert.Equal(id, d.FindAbility(key));
        Assert.Equal((0, 1), (d.FindFaction("malazan"), d.FindFaction("whirlwind")));
        // The age tech and the units' ability lists follow the renumbering (they resolve by key at load).
        Assert.Equal(new[] { d.FindTech("age_ii") }, d.AgeTechs);
        Assert.Equal(new[] { d.FindAbility("telas_fire") }, d.Units[d.FindUnit("malazan_cadre_mage")].Abilities);
        Assert.Equal(new[] { d.FindAbility("sandstorm") }, d.Units[d.FindUnit("whirlwind_priest")].Abilities);
    }

    /// <summary>The order file is not hashed (<see cref="GameData.ContentHash"/> hashes the keys in id order): the shipped hash is the golden's header (rule 49, compared dynamically).</summary>
    [Fact]
    public void TheShippedContentHash_IsTheGoldenHeaders()
    {
        string golden = File.ReadAllText(Path.Combine(TestDataDir.RepoRoot(), "sim", "Rts.Sim.Tests", "Replays", "cross_map_seed1.replay"));
        Assert.Contains("data-hash " + TestSim.Data.ContentHash().ToString("X16"), golden);
    }

    // ------------------------------------------------------------------ criterion 1: the file's rules

    private static void WriteOrder(TestDataDir dir, string json) => File.WriteAllText(dir.FullPath(OrderFile), json);

    private static DataError OneError(TestDataDir dir)
    {
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.False(r.Ok);
        return Assert.Single(r.Errors);
    }

    [Fact]
    public void TheShippedList_Loads_InItsOrder()
    {
        JsonArray order = JsonNode.Parse(File.ReadAllText(Path.Combine(TestDataDir.Shipped, "common", "factions.json")))!["order"]!.AsArray();
        Assert.Equal(order.Select(n => (string)n!), TestSim.Data.Factions.Select(f => f.Key));
        for (int f = 0; f < TestSim.Data.Factions.Length; f++) Assert.Equal(f, TestSim.Data.Factions[f].Id);
    }

    [Theory]
    [InlineData("{\"order\": [\"malazan\"]}", "order", "folder factions/whirlwind/ is not listed")]
    [InlineData("{\"order\": [\"malazan\", \"whirlwind\", \"teblor\"]}", "order[2]", "faction 'teblor' has no folder factions/teblor/")]
    [InlineData("{\"order\": [\"malazan\", \"whirlwind\", \"malazan\"]}", "order[2]", "faction 'malazan' is listed twice")]
    [InlineData("{\"order\": [\"malazan\", 3, \"whirlwind\"]}", "$.order[1]", "expected a string")]
    [InlineData("{\"order\": [\"Malazan_X\", \"whirlwind\", \"malazan\"]}", "order[0]", "not snake_case")]
    [InlineData("{}", "order", "missing required field")]
    [InlineData("{\"order\": [\"malazan\", \"whirlwind\"], \"extra\": 1}", "$", "")]
    public void ABrokenOrder_IsOneErrorAtItsEntry(string json, string path, string message)
    {
        using TestDataDir dir = TestDataDir.CopyOfShippedTwoFactions(); // M7-H1 (BUG-1191): the rows are written for exactly two folders
        WriteOrder(dir, json);
        DataError e = OneError(dir);
        _out.WriteLine(e.ToString());
        Assert.Equal(OrderFile, e.File);
        Assert.StartsWith(path, e.Path);
        Assert.Contains(message, e.Message);
    }

    /// <summary>
    /// M7-H1 (BUG-1173 item 2): a folder whose name differs from its listed id only in case (<c>factions/Whirlwind/</c>
    /// listed as <c>whirlwind</c>) is one order error naming both spellings, not "has no folder" (false on Windows, where
    /// the folder opens case-insensitively) plus "is not listed"; the folder's own id check still reports the mismatch.
    /// </summary>
    [Fact]
    public void AFolderDifferingOnlyInCase_IsOneOrderErrorNamingBothSpellings()
    {
        using TestDataDir dir = TestDataDir.CopyOfShippedTwoFactions();
        string from = dir.FullPath("factions/whirlwind"), temp = dir.FullPath("factions/ww_tmp"), to = dir.FullPath("factions/Whirlwind");
        Directory.Move(from, temp); // a case-only rename needs a step through another name on Windows
        Directory.Move(temp, to);
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        foreach (DataError e in r.Errors) _out.WriteLine(e.ToString());
        Assert.False(r.Ok);
        DataError order = Assert.Single(r.Errors, e => e.File == OrderFile);
        Assert.Equal("order[1]", order.Path);
        Assert.Contains("factions/Whirlwind/, not factions/whirlwind/", order.Message);
        Assert.DoesNotContain(r.Errors, e => e.Message.Contains("has no folder") || e.Message.Contains("is not listed"));
        Assert.Contains(r.Errors, e => e.File == "factions/Whirlwind/faction.json" && e.Message.Contains("does not match its folder name 'Whirlwind'"));
    }

    [Fact]
    public void AMissingOrderFile_IsOneError()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        File.Delete(dir.FullPath(OrderFile));
        DataError e = OneError(dir);
        Assert.Equal((OrderFile, "required file is missing"), (e.File, e.Message));
    }

    /// <summary>More than <see cref="DataLimits.MaxFactions"/> entries is an error at <c>order</c> (the entries' own errors come too: they have no folders).</summary>
    [Fact]
    public void MoreThanMaxFactions_IsAnError_AndEightIsNot()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        string Ids(int n) => string.Join(", ", new[] { "\"malazan\"", "\"whirlwind\"" }.Concat(Enumerable.Range(0, n - 2).Select(k => $"\"extra_{k}\"")));
        WriteOrder(dir, $"{{\"order\": [{Ids(DataLimits.MaxFactions + 1)}]}}");
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.Contains(r.Errors, e => e.File == OrderFile && e.Path == "order" && e.Message.Contains($"{DataLimits.MaxFactions + 1} factions is above the maximum {DataLimits.MaxFactions}"));
        Assert.Equal(DataLimits.MaxFactions - 1, r.Errors.Count(e => e.Message.Contains("has no folder")));
        WriteOrder(dir, $"{{\"order\": [{Ids(DataLimits.MaxFactions)}]}}");
        Assert.DoesNotContain(DataLoader.LoadAll(dir.Path).Errors, e => e.Message.Contains("above the maximum"));
    }

    /// <summary>The order is the rule: Whirlwind listed first takes faction 0 and the first unit and building ids.</summary>
    [Fact]
    public void ASwappedOrder_SwapsTheIds()
    {
        using TestDataDir dir = TestDataDir.CopyOfShippedTwoFactions(); // M7-H1 (BUG-1191): the two factions' swap
        WriteOrder(dir, "{\"order\": [\"whirlwind\", \"malazan\"]}");
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join(Environment.NewLine, r.Errors));
        GameData d = r.Data!;
        Assert.Equal((0, 1), (d.FindFaction("whirlwind"), d.FindFaction("malazan")));
        Assert.Equal(("whirlwind_battering_ram", 0), (d.Units[0].Key, d.Units[0].Faction));
        Assert.Equal(("malazan_cadre_mage", 7), (d.Units[7].Key, d.Units[7].Id));
        Assert.Equal("whirlwind_archer_camp", d.Buildings[0].Key);
        Assert.Equal("malazan_armory", d.Buildings[10].Key);
        Assert.NotEqual(TestSim.TwoFactionData.ContentHash(), d.ContentHash());
        // Every reference still resolves to the same key (trainedAt, the AI's ids): the swap is a renumbering only.
        foreach (UnitDef u in d.Units) Assert.Equal(TestSim.Data.Units[TestSim.Data.FindUnit(u.Key)].TrainedAt, u.TrainedAt);
        foreach (UnitDef u in d.Units) Assert.Equal(u.TrainedAt, d.Buildings[u.TrainedAtTypeId].Key);
    }

    // ------------------------------------------------------------------ criterion 3: a third faction on a copy

    /// <summary>
    /// Adds a third faction <c>teblor</c> to <paramref name="dir"/>, a full copy of Malazan's files (abilities too) with its ids
    /// renamed the way D28 will name them: <c>malazan_*</c> to <c>teblor_*</c>, its tech to <c>teblor_blood_oil_anointing</c>,
    /// its abilities to <c>blood_oil_frenzy</c> (it sorts before every shipped ability) and <c>teblor_cusser</c>; listed last
    /// or <paramref name="first"/>. Why a whole copy and not a minimal folder: the loader requires every faction's
    /// <c>ai.json</c> and <c>techs.json</c> and every template slot filled (BUG-0010). M7-3b: before it, natural names like
    /// these renumbered the shipped techs and abilities (BUG-1171), so the copy had no abilities and a tech sorting last.
    /// M7-H1 (BUG-1191): the copy is first cut to the first two factions of its order (<see cref="TestDataDir.CutToFirstFactions"/>),
    /// so the fixture is "Malazan, Whirlwind and a Malazan copy named teblor" whatever the shipped data appends (a real
    /// <c>factions/teblor/</c> is replaced, never written over), and every pin measured on it keeps its value.
    /// </summary>
    internal static void AddTeblor(TestDataDir dir, bool first = false)
    {
        dir.CutToFirstFactions(2);
        string from = dir.FullPath("factions/malazan"), to = dir.FullPath("factions/teblor");
        Directory.CreateDirectory(to);
        foreach (string file in Directory.GetFiles(from))
        {
            string text = File.ReadAllText(file)
                .Replace("malazan_", "teblor_")
                .Replace("moranth_supply", "teblor_blood_oil_anointing")
                .Replace("\"telas_fire\"", "\"blood_oil_frenzy\"")
                .Replace("\"cusser\"", "\"teblor_cusser\"");
            File.WriteAllText(Path.Combine(to, Path.GetFileName(file)), text);
        }
        dir.EditJson("factions/teblor/faction.json", root => root["id"] = "teblor");
        WriteOrder(dir, first ? "{\"order\": [\"teblor\", \"malazan\", \"whirlwind\"]}" : "{\"order\": [\"malazan\", \"whirlwind\", \"teblor\"]}");
    }

    private static GameData Load(TestDataDir dir)
    {
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join(Environment.NewLine, r.Errors));
        return r.Data!;
    }

    private static readonly Lazy<GameData> s_appended = new(() =>
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        AddTeblor(dir);
        return Load(dir);
    });

    private static readonly Lazy<GameData> s_teblorFirst = new(() =>
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        AddTeblor(dir, first: true);
        return Load(dir);
    });

    [Fact]
    public void AThirdFactionAppended_KeepsEveryShippedId_AndAddsItsOwnAfter()
    {
        GameData d = s_appended.Value;
        Assert.Equal(new[] { "malazan", "whirlwind", "teblor" }, d.Factions.Select(f => f.Key));
        Assert.Equal(Units0416, d.Units.Take(Units0416.Length).Select(u => (u.Key, u.Id)));
        Assert.Equal(Buildings0416, d.Buildings.Take(Buildings0416.Length).Select(b => (b.Key, b.Id)));
        Assert.Equal(TechsM73b, d.Techs.Take(TechsM73b.Length).Select(t => (t.Key, t.Id, t.Faction)));
        Assert.Equal(AbilitiesM73b, d.Abilities.Take(AbilitiesM73b.Length).Select(a => (a.Key, a.Id, a.Faction)));
        Assert.All(d.Units.Skip(Units0416.Length), u => Assert.Equal(2, u.Faction));
        Assert.Equal("teblor_cadre_mage", d.Units[Units0416.Length].Key); // ordinal within the faction
        Assert.All(d.Buildings.Skip(Buildings0416.Length), b => Assert.Equal(2, b.Faction));
        Assert.Equal(new[] { ("teblor_blood_oil_anointing", 9, 2) }, d.Techs.Skip(TechsM73b.Length).Select(t => (t.Key, t.Id, t.Faction)));
        // blood_oil_frenzy sorts before every shipped ability, yet its id comes after theirs (BUG-1171's symptom gone).
        Assert.Equal(new[] { ("blood_oil_frenzy", 3, 2), ("teblor_cusser", 4, 2) }, d.Abilities.Skip(AbilitiesM73b.Length).Select(a => (a.Key, a.Id, a.Faction)));
        Assert.Equal(new[] { d.FindAbility("blood_oil_frenzy") }, d.Units[d.FindUnit("teblor_cadre_mage")].Abilities);
        Assert.NotEqual(TestSim.TwoFactionData.ContentHash(), d.ContentHash()); // new keys (M7-H1: against the two factions it adds to)
    }

    [Fact]
    public void AThirdFactionFirst_RenumbersEveryShippedType()
    {
        GameData d = s_teblorFirst.Value;
        Assert.Equal(new[] { "teblor", "malazan", "whirlwind" }, d.Factions.Select(f => f.Key));
        Assert.Equal(7 + 0, d.FindUnit("malazan_cadre_mage"));
        Assert.Equal(10 + 0, d.FindBuilding("malazan_armory"));
        // M7-3b: the common techs keep their ids (they come first whatever the order); every faction tech and ability moves.
        Assert.Equal(TechsM73b.Take(7), d.Techs.Take(7).Select(t => (t.Key, t.Id, t.Faction)));
        Assert.Equal((7, 8, 9), (d.FindTech("teblor_blood_oil_anointing"), d.FindTech("moranth_supply"), d.FindTech("dryjhnas_prophecy")));
        Assert.Equal(new[] { "blood_oil_frenzy", "teblor_cusser", "cusser", "telas_fire", "sandstorm" }, d.Abilities.Select(a => a.Key));
        Assert.NotEqual(TestSim.TwoFactionData.ContentHash(), d.ContentHash()); // M7-H1: against the two factions it adds to
        Assert.NotEqual(s_appended.Value.ContentHash(), d.ContentHash());
    }

    /// <summary>A Malazan v Whirlwind AI match (seats by faction key, Normal v Hard, raiders so they fight early) on <paramref name="data"/>: the state hash after every tick.</summary>
    private static ulong[] MatchHashes(GameData data, int ticks)
    {
        PlayerSeat[] seats =
        {
            new(data.FindFaction("malazan"), SeatController.Ai, AiDifficulty.Normal),
            new(data.FindFaction("whirlwind"), SeatController.Ai, AiDifficulty.Hard),
        };
        Simulation sim = AiScenes.Generated(seats, 7, false, out _, data: data, raiders: 2);
        var hashes = new ulong[ticks];
        for (int t = 0; t < ticks; t++)
        {
            sim.Tick();
            hashes[t] = sim.StateHash();
        }
        return hashes;
    }

    [Fact]
    public void AMalazanVWhirlwindMatch_HashesTickForTick_WithATeblorFolderAppended_AndMovesWithItFirst()
    {
        const int ticks = 3000;
        ulong[] shipped = MatchHashes(TestSim.Data, ticks);
        ulong[] appended = MatchHashes(s_appended.Value, ticks);
        for (int t = 0; t < ticks; t++)
            if (shipped[t] != appended[t]) Assert.Fail($"tick {t + 1}: {shipped[t]:X16} on the shipped data, {appended[t]:X16} with teblor appended");
        Assert.True(shipped.Distinct().Count() > ticks / 2, "the match stood still");
        ulong[] first = MatchHashes(s_teblorFirst.Value, ticks);
        Assert.NotEqual(shipped[^1], first[^1]); // every type id moved, so the hashed state does
    }

    /// <summary>Two <see cref="StandardPresetPinQaTests"/> AI rows (seed 1 Easy v Normal; seed 2 Normal v Hard) give their pinned digests on the appended copy.</summary>
    [Theory]
    [Trait("Category", "Stress")]
    [InlineData(1, 2, 0)]
    [InlineData(2, 2, 1)]
    public void TwoStandardPresetPins_AreUnchanged_WithATeblorFolderAppended(int seed, int players, int difficulty)
    {
        object[] row = StandardPresetPinQaTests.AiRows().Single(r => (int)r[0] == seed && (int)r[1] == players && (int)r[2] == difficulty && !(bool)r[3]);
        SaveScene scene = AiSaveScenes.AiMatch((ulong)seed, players, (AiDifficulty)difficulty, data: s_appended.Value);
        ulong digest = StandardPresetPinQaTests.Digest(scene, AiSaveScenes.ProbeWindow, out _);
        _out.WriteLine(FormattableString.Invariant($"{scene.Name} on the appended copy: 0x{digest:X16}"));
        Assert.Equal((ulong)row[4], digest);
    }

    /// <summary>Two <see cref="NonHardMatchPinQaTests"/> rows (Raraku and Vathar Crossing seed 1, Normal v Normal) give their pinned digests on the appended copy.</summary>
    [Theory]
    [Trait("Category", "Stress")]
    [InlineData("raraku")]
    [InlineData("vathar_crossing")]
    public void TwoNonHardMatchPins_AreUnchanged_WithATeblorFolderAppended(string map)
    {
        MethodInfo m = typeof(NonHardMatchPinQaTests).GetMethod(nameof(NonHardMatchPinQaTests.AMatchWithoutHard_PlaysAsBeforeTheHardKnobs))!;
        object[] row = m.GetCustomAttributes<InlineDataAttribute>().SelectMany(a => a.GetData(m))
            .Single(r => (string)r[0] == map && (ulong)r[1] == 1UL && (AiDifficulty)r[2] == AiDifficulty.Normal);
        string got = NonHardMatchPinQaTests.Run(map, 1UL, AiDifficulty.Normal, s_appended.Value, out _);
        _out.WriteLine($"{map} seed 1 Normal v Normal on the appended copy: {got}");
        Assert.Equal((string)row[3], got);
    }
}
