using System.Text.Json.Nodes;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M7-3b (2026-11-10-2116): the tech / ability id rule (the common techs first, then each faction's in
/// <c>common/factions.json</c> order; abilities faction by faction; ordinal within each file) at its edges, and the claim
/// behind the one regeneration: renumbering the id tables changes no outcome. <see cref="OutcomeFingerprint"/> (id-free)
/// is compared tick for tick between the shipped data and copies whose every unit, building, tech and ability id moved.
/// </summary>
[Collection(SerialCollection.Name)]
public class TechAbilityIdRuleQaTests(ITestOutputHelper output)
{
    private static GameData Load(TestDataDir dir)
    {
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join(Environment.NewLine, r.Errors));
        return r.Data!;
    }

    private static DataLoadResult TryLoad(TestDataDir dir) => DataLoader.LoadAll(dir.Path);

    private static void Order(TestDataDir dir, params string[] order) =>
        File.WriteAllText(dir.FullPath("common/factions.json"), "{\"order\": [" + string.Join(", ", order.Select(o => $"\"{o}\"")) + "]}");

    /// <summary>Strips every <c>abilities</c> list from <paramref name="faction"/>'s units and deletes its abilities.json.</summary>
    private static void NoAbilities(TestDataDir dir, string faction)
    {
        dir.EditJson($"factions/{faction}/units.json", root =>
        {
            foreach (JsonNode? u in root["units"]!.AsArray()) u!.AsObject().Remove("abilities");
        });
        File.Delete(dir.FullPath($"factions/{faction}/abilities.json"));
    }

    private static (string, int, int)[] Techs(GameData d) => d.Techs.Select(t => (t.Key, t.Id, t.Faction)).ToArray();

    private static (string, int, int)[] Abilities(GameData d) => d.Abilities.Select(a => (a.Key, a.Id, a.Faction)).ToArray();

    // ------------------------------------------------------------------ the rule at its edges

    /// <summary>A faction with no abilities file in the middle of the order: the factions after it keep their grouping, no gap, no id of its.</summary>
    [Fact]
    public void AFactionWithoutAnAbilitiesFile_InTheMiddle_LeavesNoGap()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        FactionOrderTests.AddTeblor(dir);
        NoAbilities(dir, "teblor");
        Order(dir, "malazan", "teblor", "whirlwind");
        GameData d = Load(dir);
        Assert.Equal(new[] { ("cusser", 0, 0), ("telas_fire", 1, 0), ("sandstorm", 2, 2) }, Abilities(d));
        Assert.All(d.Units.Where(u => u.Faction == 1), u => Assert.Empty(u.Abilities));
        Assert.Equal(new[] { d.FindAbility("sandstorm") }, d.Units[d.FindUnit("whirlwind_priest")].Abilities);
        Assert.Equal(("moranth_supply", 7), (d.Techs[7].Key, d.Techs[7].Id));
        Assert.Equal(("teblor_blood_oil_anointing", 8, 1), (d.Techs[8].Key, d.Techs[8].Id, d.Techs[8].Faction));
        Assert.Equal(("dryjhnas_prophecy", 9, 2), (d.Techs[9].Key, d.Techs[9].Id, d.Techs[9].Faction));
    }

    /// <summary>The first faction with an empty techs list: the next faction's techs start right after the common ones.</summary>
    [Fact]
    public void AFirstFactionWithNoTechs_LeavesNoGap()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        FactionOrderTests.AddTeblor(dir, first: true);
        File.WriteAllText(dir.FullPath("factions/teblor/techs.json"), "{\"techs\": []}");
        dir.EditJson("factions/teblor/ai.json", root => StripResearch(root, "teblor_blood_oil_anointing"));
        GameData d = Load(dir);
        Assert.Equal(9, d.Techs.Length);
        Assert.Equal(("moranth_supply", 7, 1), (d.Techs[7].Key, d.Techs[7].Id, d.Techs[7].Faction));
        Assert.Equal(("dryjhnas_prophecy", 8, 2), (d.Techs[8].Key, d.Techs[8].Id, d.Techs[8].Faction));
    }

    private static void StripResearch(JsonNode node, string id)
    {
        if (node is JsonArray arr)
        {
            for (int i = arr.Count - 1; i >= 0; i--)
            {
                if (arr[i] is JsonObject o && (string?)o["do"] == "research" && (string?)o["id"] == id) arr.RemoveAt(i);
                else if (arr[i] != null) StripResearch(arr[i]!, id);
            }
        }
        else if (node is JsonObject obj)
            foreach (var kv in obj.ToList())
                if (kv.Value != null) StripResearch(kv.Value, id);
    }

    /// <summary>
    /// A faction's techs that sort before <c>age_ii</c> and require each other (and a common tech), and an AI step naming
    /// one: every reference resolves through the index to the right key, though the table is not sorted.
    /// </summary>
    [Fact]
    public void FactionTechsSortingBeforeAgeII_RequiringEachOther_ResolveByKey()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        FactionOrderTests.AddTeblor(dir, first: true);
        dir.EditJson("factions/teblor/techs.json", root =>
        {
            JsonArray list = root["techs"]!.AsArray();
            list[0]!["id"] = "aa_second";
            list[0]!["requires"] = new JsonArray("ab_first", "armor_1");
            JsonNode first = JsonNode.Parse(list[0]!.ToJsonString())!;
            first["id"] = "ab_first";
            first["requires"] = new JsonArray("age_ii");
            list.Add(first);
        });
        // The AI researches the renamed tech by its new key.
        string ai = File.ReadAllText(dir.FullPath("factions/teblor/ai.json")).Replace("\"teblor_blood_oil_anointing\"", "\"aa_second\"");
        File.WriteAllText(dir.FullPath("factions/teblor/ai.json"), ai);
        GameData d = Load(dir);
        Assert.Equal(new[] { "age_ii", "armor_1", "armor_2", "melee_weapons_1", "melee_weapons_2", "ranged_weapons_1", "ranged_weapons_2",
            "aa_second", "ab_first", "moranth_supply", "dryjhnas_prophecy" }, d.Techs.Select(t => t.Key));
        TechDef second = d.Techs[d.FindTech("aa_second")];
        Assert.Equal(new[] { "ab_first", "armor_1" }, second.RequiresTechs.Select(r => d.Techs[r].Key).Order(StringComparer.Ordinal));
        Assert.Equal(new[] { "age_ii" }, d.Techs[d.FindTech("ab_first")].RequiresTechs.Select(r => d.Techs[r].Key));
        Assert.Equal(0, d.FindTech("age_ii"));
        Assert.Equal(new[] { 0 }, d.AgeTechs);
        // The Forge researches the faction's techs by type id; each list ascending (ProductionSystem binary-searches it).
        foreach (BuildingDef b in d.Buildings)
        {
            var r = d.TechsResearchableAt(b.Id);
            for (int k = 1; k < r.Length; k++) Assert.True(r[k - 1] < r[k], $"{b.Key}: researchable list not ascending");
        }
        Assert.Contains(d.FindTech("aa_second"), d.TechsResearchableAt(d.FindBuilding("teblor_armory")));
    }

    /// <summary>A faction tech that repeats a common id is one error at the faction's file (the common file comes first); the same for a repeated ability.</summary>
    [Fact]
    public void ARepeatedTechOrAbilityId_IsOneErrorAtTheLaterFile()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        FactionOrderTests.AddTeblor(dir, first: true);
        string techs = File.ReadAllText(dir.FullPath("factions/teblor/techs.json")).Replace("\"teblor_blood_oil_anointing\"", "\"armor_1\"");
        File.WriteAllText(dir.FullPath("factions/teblor/techs.json"), techs);
        DataLoadResult r = TryLoad(dir);
        Assert.False(r.Ok);
        output.WriteLine(string.Join(Environment.NewLine, r.Errors));
        DataError e = Assert.Single(r.Errors, x => x.Message.Contains("duplicate tech id"));
        Assert.Equal(("factions/teblor/techs.json", "techs[0].id"), (e.File, e.Path));
        Assert.Contains("first defined in common/techs.json", e.Message);

        using TestDataDir dir2 = TestDataDir.CopyOfShipped();
        FactionOrderTests.AddTeblor(dir2, first: true);
        string abil = File.ReadAllText(dir2.FullPath("factions/teblor/abilities.json")).Replace("\"teblor_cusser\"", "\"sandstorm\"");
        File.WriteAllText(dir2.FullPath("factions/teblor/abilities.json"), abil);
        DataLoadResult r2 = TryLoad(dir2);
        Assert.False(r2.Ok);
        output.WriteLine(string.Join(Environment.NewLine, r2.Errors));
        // Teblor is first in the order, but file order of reading decides "first defined": the repeat is reported once.
        Assert.Single(r2.Errors, x => x.Message.Contains("duplicate ability id 'sandstorm'"));
    }

    /// <summary>An unknown tech in an AI step, then in a requires list (separate copies: a broken techs file skips the AI's id checks), after the renumbering: one error each, no throw.</summary>
    [Fact]
    public void AnUnknownTech_InAiOrRequires_IsOneError()
    {
        using (TestDataDir dir = TestDataDir.CopyOfShipped())
        {
            string ai = File.ReadAllText(dir.FullPath("factions/malazan/ai.json"));
            int at = ai.IndexOf("\"moranth_supply\"", StringComparison.Ordinal);
            File.WriteAllText(dir.FullPath("factions/malazan/ai.json"), ai[..at] + "\"aaa_nothing\"" + ai[(at + "\"moranth_supply\"".Length)..]);
            DataLoadResult r = TryLoad(dir);
            output.WriteLine(string.Join(Environment.NewLine, r.Errors));
            DataError e = Assert.Single(r.Errors);
            Assert.Equal(("factions/malazan/ai.json", "unknown tech 'aaa_nothing' (not in any techs.json)"), (e.File, e.Message));
        }
        using (TestDataDir dir = TestDataDir.CopyOfShipped())
        {
            dir.EditJson("factions/whirlwind/techs.json", root => root["techs"]![0]!["requires"] = new JsonArray("age_ii", "zzz_nothing"));
            DataLoadResult r = TryLoad(dir);
            output.WriteLine(string.Join(Environment.NewLine, r.Errors));
            DataError e = Assert.Single(r.Errors);
            Assert.Equal(("factions/whirlwind/techs.json", "techs[0].requires[1]"), (e.File, e.Path));
            Assert.Contains("unknown tech or building 'zzz_nothing'", e.Message);
        }
    }

    // ------------------------------------------------------------------ renumbering changes no outcome

    private static ulong[] Fingerprints(GameData data, int ticks, int casters) => Fingerprints(data, ticks, casters, out _);

    private static ulong[] Fingerprints(GameData data, int ticks, int casters, out ulong stateHash)
    {
        PlayerSeat[] seats =
        {
            new(data.FindFaction("malazan"), SeatController.Ai, AiDifficulty.Normal),
            new(data.FindFaction("whirlwind"), SeatController.Ai, AiDifficulty.Hard),
        };
        Simulation sim = AiScenes.Generated(seats, 7, false, out _, data: data, raiders: 2, casters: casters);
        var f = new ulong[ticks];
        for (int t = 0; t < ticks; t++)
        {
            sim.Tick();
            f[t] = OutcomeFingerprint.Of(sim);
        }
        stateHash = sim.StateHash();
        return f;
    }

    private void AssertSame(ulong[] a, ulong[] b, string what)
    {
        for (int t = 0; t < a.Length; t++)
            if (a[t] != b[t]) Assert.Fail($"{what}: tick {t + 1} plays differently ({a[t]:X16} v {b[t]:X16})");
        output.WriteLine($"{what}: {a.Length} ticks identical, last {a[^1]:X16}");
    }

    /// <summary>
    /// A Malazan v Whirlwind match with casters (Cusser, Telas Fire, Sandstorm cast early) plays the same outcome tick for
    /// tick on the shipped data, with the order swapped, and with a naturally named Teblor listed first (every unit,
    /// building, faction tech and ability id moved). The state hash moves (it hashes ids); the outcome must not.
    /// </summary>
    [Fact]
    [Trait("Category", "Stress")]
    public void ACastingMatch_PlaysTheSameOutcome_UnderEveryRenumbering()
    {
        const int ticks = 3000;
        ulong[] shipped = Fingerprints(TestSim.Data, ticks, 2, out ulong shippedHash);
        Assert.True(shipped.Distinct().Count() > ticks / 2, "the match stood still");
        using (TestDataDir dir = TestDataDir.CopyOfShipped())
        {
            Order(dir, "whirlwind", "malazan");
            AssertSame(shipped, Fingerprints(Load(dir), ticks, 2), "whirlwind,malazan");
        }
        using (TestDataDir dir = TestDataDir.CopyOfShipped())
        {
            FactionOrderTests.AddTeblor(dir, first: true);
            GameData d = Load(dir);
            Assert.NotEqual(TestSim.Data.FindAbility("cusser"), d.FindAbility("cusser"));
            Assert.NotEqual(TestSim.Data.FindTech("moranth_supply"), d.FindTech("moranth_supply"));
            AssertSame(shipped, Fingerprints(d, ticks, 2, out ulong firstHash), "teblor,malazan,whirlwind");
            Assert.NotEqual(shippedHash, firstHash); // the ids, and so the state hash, did move
        }
        using (TestDataDir dir = TestDataDir.CopyOfShipped())
        {
            FactionOrderTests.AddTeblor(dir);
            AssertSame(shipped, Fingerprints(Load(dir), ticks, 2), "malazan,whirlwind,teblor");
        }
    }

    // ------------------------------------------------------------------ a Teblor copy as an AI seat

    /// <summary>
    /// The natural-name Teblor copy in a three-seat Hard FFA with casters: its abilities (<c>blood_oil_frenzy</c>,
    /// <c>teblor_cusser</c>) are cast, every invariant holds, and two runs hash the same every 100 ticks. Prints the end
    /// hash as <c>XPROC</c> for the cross-process check.
    /// </summary>
    [Fact]
    [Trait("Category", "Stress")]
    public void ATeblorSeat_InAnFfa_CastsItsAbilities_AndIsDeterministic()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        FactionOrderTests.AddTeblor(dir);
        GameData d = Load(dir);
        (ulong[] a, bool castA, string infoA) = Ffa(d, 6000);
        (ulong[] b, _, _) = Ffa(d, 6000);
        Assert.Equal(a, b);
        output.WriteLine(infoA);
        output.WriteLine($"XPROC teblor-ffa {a[^1]:X16}");
        Assert.True(castA, "no Teblor caster ever cast");
    }

    private static (ulong[] Hashes, bool TeblorCast, string Info) Ffa(GameData d, int ticks)
    {
        PlayerSeat[] seats =
        {
            new(d.FindFaction("malazan"), SeatController.Ai, AiDifficulty.Hard),
            new(d.FindFaction("whirlwind"), SeatController.Ai, AiDifficulty.Hard),
            new(d.FindFaction("teblor"), SeatController.Ai, AiDifficulty.Hard),
        };
        Simulation sim = AiScenes.Generated(seats, 11, false, out _, data: d, raiders: 1, casters: 2);
        World w = sim.World;
        UnitStore u = w.Units;
        var hashes = new List<ulong>();
        bool cast = false;
        int frenzy = d.FindAbility("blood_oil_frenzy"), cusser = d.FindAbility("teblor_cusser");
        for (int t = 1; t <= ticks; t++)
        {
            sim.Tick();
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i]) continue;
                Assert.True(float.IsFinite(u.Position[i].X) && float.IsFinite(u.Position[i].Y), $"unit {i} position not finite at {t}");
                Assert.InRange(u.Hp[i], 1, w.MaxHp(i));
                UnitDef def = d.Units[u.TypeId[i]];
                foreach (int ab in def.Abilities) Assert.Equal(def.Faction, d.Abilities[ab].Faction);
                if (u.Owner[i] == 2 && def.Abilities.Length > 0)
                    for (int s = 0; s < def.Abilities.Length; s++)
                        if (u.AbilityReadyTick[i * DataLimits.MaxUnitAbilities + s] > 0
                            && (def.Abilities[s] == frenzy || def.Abilities[s] == cusser)) cast = true;
            }
            for (int p = 0; p < 3; p++) Assert.True(w.Gold[p] >= 0 && w.Wood[p] >= 0);
            if (t % 100 == 0) hashes.Add(sim.StateHash());
        }
        string techs = string.Join(" ", Enumerable.Range(0, 3).Select(p =>
            $"p{p}[{string.Join(",", d.Techs.Where(x => w.HasTech(p, x.Id)).Select(x => x.Key))}]"));
        return (hashes.ToArray(), cast, $"tick {sim.TickNumber}: {techs}");
    }
    /// <summary>
    /// The natural-name Teblor copy as a Hard seat against an Easy Whirlwind (seed 2, to tick 19,000): it reaches Age II and
    /// researches its own tech <c>teblor_blood_oil_anointing</c> (id 9, after every shipped tech) through its ai.json key, on
    /// the same tick a Malazan seat in its place researches <c>moranth_supply</c> (the copy is Malazan renamed), and the run
    /// hashes the same twice. Measured 2026-11-10-2116: age_ii at 15,821, the faction tech at 18,301.
    /// </summary>
    [Fact]
    [Trait("Category", "Stress")]
    public void ATeblorSeat_ResearchesItsOwnTech_ByKey_AsAMalazanSeatDoes()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        FactionOrderTests.AddTeblor(dir);
        GameData d = Load(dir);
        Assert.Equal(9, d.FindTech("teblor_blood_oil_anointing"));
        (ulong Hash, int At) Run(string faction, string ownTech)
        {
            int tech = d.FindTech(ownTech);
            PlayerSeat[] seats = { new(d.FindFaction(faction), SeatController.Ai, AiDifficulty.Hard), new(d.FindFaction("whirlwind"), SeatController.Ai, AiDifficulty.Easy) };
            Simulation sim = AiScenes.Generated(seats, 2, false, out _, data: d);
            int at = -1;
            while (sim.TickNumber < 19_000 && !sim.MatchOver)
            {
                sim.Tick();
                if (at < 0 && sim.World.HasTech(0, tech)) at = sim.TickNumber;
            }
            return (sim.StateHash(), at);
        }
        var a = Run("teblor", "teblor_blood_oil_anointing");
        var b = Run("teblor", "teblor_blood_oil_anointing");
        var m = Run("malazan", "moranth_supply");
        output.WriteLine($"teblor_blood_oil_anointing at {a.At}, moranth_supply (Malazan seat) at {m.At}; XPROC teblor-duel {a.Hash:X16}");
        Assert.Equal(a, b);
        Assert.True(a.At > 0, "the Teblor seat never researched its own tech");
        Assert.Equal(m.At, a.At);
    }
}
