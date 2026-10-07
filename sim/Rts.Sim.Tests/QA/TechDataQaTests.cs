using System.Text.Json.Nodes;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using static Rts.Sim.Tests.GatherMaps;
using static Rts.Sim.Tests.ProductionMaps;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M3-5 (2026-10-07-1131): hostile <c>techs.json</c> content: stat spelling variants, out-of-range amounts, costs and
/// times, filters that match nothing or everything, <c>requires</c> cycles, cross-faction references, and more than 64
/// techs (the flags' second word). The loader must reject bad values with one clear error at the field, never crash or
/// silently default.
/// </summary>
public class TechDataQaTests
{
    private const string Common = "common/techs.json";
    private const string MalazanTechs = "factions/malazan/techs.json";

    private static void EditTech(TestDataDir dir, string file, int index, Action<JsonObject> edit) =>
        dir.EditJson(file, root => edit(root["techs"]![index]!.AsObject()));

    private static DataError OneError(TestDataDir dir)
    {
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.Null(r.Data);
        return Assert.Single(r.Errors);
    }

    private static GameData Loads(TestDataDir dir)
    {
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        return r.Data!;
    }

    [Theory]
    [InlineData("\"ATTACK\"")]
    [InlineData("\"Armor\"")]
    [InlineData("\"abilitycooldown\"")]
    [InlineData("\"ability_cooldown\"")]
    [InlineData("\"AbilityCooldown\"")]
    [InlineData("\" attack\"")]
    [InlineData("\"attack \"")]
    [InlineData("\"HP\"")]
    [InlineData("\"\"")]
    [InlineData("3")]
    [InlineData("null")]
    public void AStatSpellingVariant_IsOneErrorAtTheStat(string raw)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        EditTech(dir, Common, 1, t => t["effects"]![0]!["stat"] = JsonNode.Parse(raw));
        DataError e = OneError(dir);
        Assert.Equal(Common, e.File);
        if (raw.StartsWith('"')) Assert.Equal("techs[1].effects[0].stat", e.Path);
    }

    [Theory]
    [InlineData("attack", "1000000", true)]
    [InlineData("attack", "-1000000", true)]
    [InlineData("attack", "1000001", false)]
    [InlineData("attack", "1e300", false)]
    [InlineData("attack", "-1e300", false)]
    [InlineData("hp", "2147483648", false)]
    [InlineData("range", "1000000", true)]
    [InlineData("range", "1000000.5", false)]
    [InlineData("range", "0.001", true)]
    [InlineData("abilityCooldown", "3600", true)]
    [InlineData("abilityCooldown", "-3600", true)]
    [InlineData("abilityCooldown", "3600.5", false)]
    [InlineData("abilityCooldown", "0.01", false)] // rounds to 0 ticks
    [InlineData("abilityCooldown", "\"15\"", false)] // a string
    public void AnEffectAmountAtAndPastItsBounds_LoadsOrIsOneError(string stat, string amount, bool ok)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        EditTech(dir, Common, 1, t =>
        {
            JsonObject e = t["effects"]![0]!.AsObject();
            e["stat"] = stat;
            e["amount"] = JsonNode.Parse(amount);
        });
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        if (ok)
        {
            Assert.True(r.Ok, string.Join("\n", r.Errors));
            return;
        }
        Assert.Null(r.Data);
        DataError err = Assert.Single(r.Errors);
        Assert.Equal(Common, err.File);
    }

    [Fact]
    public void HugeAmountsOnEveryTech_SumWithoutDrift_InTheBonusQuery()
    {
        // Every common tech +1,000,000 attack on every unit (appliesTo {}): the sum of seven is exactly 7,000,000.
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson(Common, root =>
        {
            foreach (JsonNode? t in root["techs"]!.AsArray())
                t!["effects"] = JsonNode.Parse("[{\"stat\": \"attack\", \"amount\": 1000000, \"appliesTo\": {}}]");
        });
        GameData d = Loads(dir);
        var sim = new Simulation(new SimConfig(5, 2, 16, 64) { Data = d }, ResourceMaps.Flat(16, 16));
        for (int t = 0; t < d.Techs.Length; t++)
            if (d.Techs[t].Faction < 0) sim.World.Techs.Set(0, t, true);
        Assert.Equal(7_000_000f, sim.World.TechBonus(0, d.FindUnit("malazan_catapult"), TechStat.Attack));
        Assert.Equal(0f, sim.World.TechBonus(1, d.FindUnit("malazan_catapult"), TechStat.Attack));
    }

    [Theory]
    [InlineData("researchTime", "0")]
    [InlineData("researchTime", "0.01")]
    [InlineData("researchTime", "-30")]
    [InlineData("researchTime", "3600.5")]
    [InlineData("researchTime", "\"30\"")]
    [InlineData("cost", "{\"gold\": -1, \"wood\": 50}")]
    [InlineData("cost", "{\"gold\": 1000001, \"wood\": 50}")]
    [InlineData("cost", "{\"gold\": 2147483648, \"wood\": 50}")]
    [InlineData("cost", "{\"gold\": 1.5, \"wood\": 50}")]
    [InlineData("cost", "{\"gold\": 100}")]
    [InlineData("cost", "null")]
    [InlineData("researchedAt", "\"\"")]
    [InlineData("researchedAt", "\"Town_Hall\"")]
    [InlineData("researchedAt", "\"malazan_armory\"")] // a building id, not a slot id
    [InlineData("displayName", "\"\"")]
    [InlineData("displayName", "null")]
    [InlineData("effects", "null")]
    [InlineData("effects", "{}")]
    [InlineData("id", "\"Melee_Weapons_1\"")]
    public void ABadTechField_IsOneError_NeverACrash(string field, string raw)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        // Tech 1 is melee_weapons_1: nothing requires it by id except melee_weapons_2, so an id edit is checked separately.
        int index = field == "id" ? 0 : 1;
        string file = field == "id" ? MalazanTechs : Common;
        EditTech(dir, file, index, t => t[field] = JsonNode.Parse(raw));
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.Null(r.Data);
        Assert.NotEmpty(r.Errors);
        Assert.All(r.Errors, e => Assert.Equal(file, e.File));
        Assert.True(r.Errors.Count == 1, string.Join("\n", r.Errors));
    }

    [Fact]
    public void TheMaximumResearchTime_LoadsAs72000Ticks()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        EditTech(dir, Common, 1, t => t["researchTime"] = 3600);
        GameData d = Loads(dir);
        Assert.Equal(72_000, d.Techs[d.FindTech("melee_weapons_1")].ResearchTicks);
    }

    [Fact]
    public void AFactionUpgradeFilteringByAnotherFactionsOnlyTag_OrByAttackType_TouchesOnlyWhatItMatches()
    {
        // tags / attackType filters in a faction file aren't faction-checked (only units are); the bonus is per player, so
        // a Malazan player's upgrade can't reach a Whirlwind player's units. Pin the per-player isolation.
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        EditTech(dir, MalazanTechs, 0, t => t["effects"] = JsonNode.Parse("[{\"stat\": \"hp\", \"amount\": 5, \"appliesTo\": {\"tags\": [\"cavalry\"]}}]"));
        GameData d = Loads(dir);
        var sim = new Simulation(new SimConfig(5, 2, 16, 64) { Data = d }, ResourceMaps.Flat(16, 16));
        int moranth = d.FindTech("moranth_supply");
        sim.World.Techs.Set(0, moranth, true);
        Assert.Equal(5f, sim.World.TechBonus(0, d.FindUnit("malazan_wickan_lancer"), TechStat.Hp));
        Assert.Equal(0f, sim.World.TechBonus(1, d.FindUnit("whirlwind_horse_raider"), TechStat.Hp));
    }

    [Fact]
    public void AFilterThatMatchesNoUnit_Loads_AndTheTechChangesNothing()
    {
        // Documented: "each filter it sets must match". Magic and the siege slot never meet in the shipped units.
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        EditTech(dir, Common, 1, t => t["effects"]![0]!["appliesTo"] = JsonNode.Parse("{\"attackType\": \"magic\", \"siege\": true}"));
        GameData d = Loads(dir);
        var sim = new Simulation(new SimConfig(5, 1, 16, 64) { Data = d }, ResourceMaps.Flat(16, 16));
        sim.World.Techs.Set(0, d.FindTech("melee_weapons_1"), true);
        for (int u = 0; u < d.Units.Length; u++)
            for (int s = 0; s < DataLimits.TechStatIds.Length; s++)
                Assert.Equal(0f, sim.World.TechBonus(0, u, (TechStat)s));
    }

    /// <summary>
    /// BUG-0098 (fixed M3-6): an empty <c>units</c> (or <c>tags</c>) list was read as "no filter", so the effect applied to
    /// every unit of every faction. It is one error at the list.
    /// </summary>
    [Theory]
    [InlineData("units")]
    [InlineData("tags")]
    public void AnEmptyUnitsOrTagsFilter_IsRejected_NotReadAsEveryUnit(string filter)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        EditTech(dir, MalazanTechs, 0, t => t["effects"]![0]!["appliesTo"] = JsonNode.Parse($"{{\"{filter}\": []}}"));
        DataError e = OneError(dir);
        Assert.Equal((MalazanTechs, $"techs[0].effects[0].appliesTo.{filter}"), (e.File, e.Path));
    }

    /// <summary>BUG-0099 (fixed M3-6): <c>requires</c> cycles (a tech requiring itself, two techs requiring each other) would be unresearchable forever; each is an error at a requires entry.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ARequiresCycle_IsOneError(bool self)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        // melee_weapons_1 is common techs[1]; melee_weapons_2 (techs[2]) already requires it.
        EditTech(dir, Common, 1, t => t["requires"] = JsonNode.Parse(self ? "[\"melee_weapons_1\"]" : "[\"melee_weapons_2\"]"));
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.Null(r.Data);
        Assert.Contains(r.Errors, e => e.Path.Contains("requires"));
    }

    /// <summary>BUG-0099 (fixed M3-6): a tech may not take a building's id (a <c>requires</c> entry naming it would be ambiguous): one error at the tech's id.</summary>
    [Fact]
    public void ATechIdEqualToABuildingId_IsOneError()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        EditTech(dir, MalazanTechs, 0, t => t["id"] = "malazan_armory");
        DataError e = OneError(dir);
        Assert.Equal((MalazanTechs, "techs[0].id"), (e.File, e.Path));
    }

    [Fact]
    public void AnEmptyFactionTechsList_Loads_TheFactionSimplyHasNoUpgrade()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson(MalazanTechs, root => root["techs"] = new JsonArray());
        GameData d = Loads(dir);
        Assert.Equal(8, d.Techs.Length);
        Assert.Equal(-1, d.FindTech("moranth_supply"));
    }

    [Fact]
    public void SeventyTechs_UseASecondFlagWord_AndEveryFlagIsHashed_AndResearchable()
    {
        // 61 extra common techs at the Forge (+1 hp each on every unit): 70 in all, so tech ids 64-69 live in word 1.
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson(Common, root =>
        {
            JsonArray list = root["techs"]!.AsArray();
            for (int n = 0; n < 61; n++)
                list.Add(JsonNode.Parse($"{{\"id\": \"zz_extra_{n:D2}\", \"displayName\": \"X\", \"description\": \"X\", \"researchedAt\": \"forge\", " +
                    "\"cost\": {\"gold\": 1, \"wood\": 1}, \"researchTime\": 0.05, \"requires\": [], " +
                    "\"effects\": [{\"stat\": \"hp\", \"amount\": 1, \"appliesTo\": {}}]}"));
        });
        GameData d = Loads(dir);
        Assert.Equal(70, d.Techs.Length);
        var sim = new Simulation(new SimConfig(5, 2, 16, 512) { Data = d }, ResourceMaps.Flat(32, 32));
        Assert.Equal(2, sim.World.Techs.WordsPerPlayer);
        var seen = new HashSet<ulong> { sim.StateHash() };
        for (int t = 60; t < 70; t++)
        {
            ulong h0 = sim.StateHash();
            sim.World.Techs.Set(1, t, true);
            Assert.True(seen.Add(sim.StateHash()), $"tech {t}: flag not hashed or collides");
            sim.World.Techs.Set(1, t, false);
            Assert.Equal(h0, sim.StateHash());
        }
        // Research ids 64-69 (word 1) through the queue in one tick: five fit the queue, the sixth is dropped (full);
        // each takes one tick, the next starting in the tick the one before completes.
        int forge = d.FindBuilding("malazan_armory");
        sim.Enqueue(Command.SpawnBuilding(0, forge, At(sim, 10, 10)));
        Run(sim, 2);
        int k = BuildMaps.SiteAt(sim, 10, 10);
        BuildMaps.Give(sim, 0, 1000, 1000);
        Assert.Equal("zz_extra_60", d.Techs[69].Key);
        for (int t = 64; t < 70; t++) sim.Enqueue(Command.Research(0, In(sim, k), t));
        Run(sim, 2 + 6);
        for (int t = 64; t < 69; t++) Assert.True(sim.World.HasTech(0, t), d.Techs[t].Key);
        Assert.False(sim.World.HasTech(0, 69));
        Assert.Equal(0, sim.World.Buildings.QueueCount[k]);
        Assert.Equal(5f, sim.World.TechBonus(0, d.FindUnit("malazan_laborer"), TechStat.Hp));
        Assert.Equal(0f, sim.World.TechBonus(1, d.FindUnit("malazan_laborer"), TechStat.Hp));
    }
}
