using System.Text.Json.Nodes;
using Rts.Sim.Data;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA (D2, session 2026-10-06-2114): the fourteen-unit rosters checked against QA's own table (typed from the
/// faction pages' "Units" tables and unit notes, keyed by id, times already in ticks so the loader's conversion is
/// checked independently), plus where each unit can be trained, and mutation probes showing the loader's validation
/// and the content hash reach every entry, not just index 0.
/// </summary>
public class UnitRosterQaTests
{
    // Page "melee" range, docs/02 "Units" note and docs/03 "Data format": 0.5 m edge to edge.
    private const float M = 0.5f;

    // id, faction, slot, name, hp, armor, armor class, attack, damage type, cooldown ticks, range, min range,
    // speed m/s, sight, gold, wood, pop, train ticks, trained at, splash, friendly fire, bonus vs (from unit notes)
    private static readonly (string Id, string Faction, UnitSlot Slot, string Name, int Hp, int Armor, string Class, int Atk, string Type, int CdTicks, float Range, float Min, double Speed, int Sight, int Gold, int Wood, int Pop, int TrainTicks, string At, float Splash, bool Ff, string Bonus)[] Expected =
    {
        ("malazan_laborer", "malazan", UnitSlot.Worker, "Laborer", 40, 0, "light", 4, "melee", 30, M, 0, 4.0, 14, 50, 0, 1, 240, "malazan_garrison_keep", 0, false, ""),
        ("malazan_heavy_infantry", "malazan", UnitSlot.Line, "Heavy Infantry", 130, 3, "heavy", 10, "melee", 30, M, 0, 3.0, 14, 54, 20, 1, 280, "malazan_barracks", 0, false, "mounted=1.5"),
        ("malazan_crossbowman", "malazan", UnitSlot.Ranged, "Crossbowman", 55, 0, "light", 9, "pierce", 44, 15, 0, 3.2, 18, 36, 45, 1, 320, "malazan_crossbow_range", 0, false, "heavy=1.3"),
        ("malazan_wickan_lancer", "malazan", UnitSlot.Shock, "Wickan Lancer", 150, 1, "mounted", 12, "melee", 36, M, 0, 6.2, 16, 90, 30, 2, 520, "malazan_wickan_corral", 0, false, "light=1.5"),
        ("malazan_cadre_mage", "malazan", UnitSlot.Caster, "Cadre Mage", 60, 0, "light", 9, "magic", 44, 12, 0, 3.2, 16, 100, 50, 2, 600, "malazan_cadre_tower", 1.5f, false, ""),
        ("malazan_catapult", "malazan", UnitSlot.Siege, "Catapult", 220, 4, "heavy", 50, "siege", 100, 24, 6, 2.2, 18, 200, 150, 3, 800, "malazan_engineers_yard", 2.5f, true, ""),
        ("malazan_sapper", "malazan", UnitSlot.Unique, "Sapper", 70, 1, "light", 20, "siege", 60, 8, 0, 3.4, 16, 72, 40, 1, 400, "malazan_engineers_yard", 2.0f, true, ""),
        ("whirlwind_camp_follower", "whirlwind", UnitSlot.Worker, "Camp Follower", 36, 0, "light", 4, "melee", 30, M, 0, 4.0, 14, 40, 0, 1, 240, "whirlwind_holy_camp", 0, false, ""),
        ("whirlwind_raider", "whirlwind", UnitSlot.Line, "Raider", 108, 1, "heavy", 11, "melee", 30, M, 0, 3.4, 14, 48, 16, 1, 360, "whirlwind_raider_camp", 0, false, "heavy=1.2,mounted=1.5"),
        ("whirlwind_desert_archer", "whirlwind", UnitSlot.Ranged, "Desert Archer", 50, 0, "light", 7, "pierce", 36, 14, 0, 3.6, 18, 32, 36, 1, 400, "whirlwind_archer_camp", 0, false, ""),
        ("whirlwind_horse_raider", "whirlwind", UnitSlot.Shock, "Horse Raider", 135, 0, "mounted", 11, "melee", 32, M, 0, 6.6, 18, 72, 24, 2, 520, "whirlwind_horse_lines", 0, false, "light=1.5"),
        ("whirlwind_priest", "whirlwind", UnitSlot.Caster, "Priest of the Whirlwind", 54, 0, "light", 9, "magic", 44, 12, 0, 3.2, 16, 80, 40, 2, 600, "whirlwind_shrine", 1.5f, false, ""),
        ("whirlwind_battering_ram", "whirlwind", UnitSlot.Siege, "Battering Ram", 240, 6, "heavy", 60, "siege", 60, M, 0, 2.4, 10, 160, 120, 3, 720, "whirlwind_ram_yard", 0, false, ""),
        ("whirlwind_zealot", "whirlwind", UnitSlot.Unique, "Zealot", 63, 0, "light", 9, "melee", 20, M, 0, 4.4, 14, 30, 10, 1, 200, "whirlwind_raider_camp", 0, false, ""),
    };

    // Where each slot trains (docs/02 "Buildings" Provides); the unique's hall is stated on its faction page.
    private static readonly Dictionary<string, BuildingSlot> UniqueHall = new()
    {
        ["malazan_sapper"] = BuildingSlot.SiegeWorks,
        ["whirlwind_zealot"] = BuildingSlot.InfantryHall,
    };

    private static readonly Dictionary<UnitSlot, BuildingSlot> SlotHall = new()
    {
        [UnitSlot.Worker] = BuildingSlot.TownHall,
        [UnitSlot.Line] = BuildingSlot.InfantryHall,
        [UnitSlot.Ranged] = BuildingSlot.RangedHall,
        [UnitSlot.Shock] = BuildingSlot.ShockHall,
        [UnitSlot.Caster] = BuildingSlot.CasterHall,
        [UnitSlot.Siege] = BuildingSlot.SiegeWorks,
    };

    [Fact]
    public void EveryShippedUnit_MatchesQaTable_AndNothingElseShips()
    {
        GameData d = TestSim.Data;
        DamageTable t = d.DamageTable;
        Assert.Equal(Expected.Select(e => e.Id).OrderBy(s => s, StringComparer.Ordinal), d.Units.Select(u => u.Key).OrderBy(s => s, StringComparer.Ordinal));
        foreach (var e in Expected)
        {
            UnitDef u = d.Units[d.FindUnit(e.Id)];
            AttackDef a = u.Attack;
            Assert.Equal((e.Id, e.Faction, e.Slot, e.Name), (e.Id, d.Factions[u.Faction].Key, u.Slot, u.DisplayName));
            Assert.Equal((e.Id, e.Hp, e.Armor, e.Class), (e.Id, u.Hp, u.Armor, t.ArmorClassKeys[u.ArmorClass]));
            Assert.Equal((e.Id, e.Atk, e.Type, e.CdTicks), (e.Id, a.Value, t.DamageTypeKeys[a.DamageType], a.CooldownTicks));
            Assert.Equal((e.Id, e.Range, e.Min, e.Splash, e.Ff), (e.Id, a.Range, a.MinRange, a.Splash, a.FriendlyFire));
            Assert.Equal((e.Id, (float)(e.Speed / 20.0), (float)e.Sight), (e.Id, u.SpeedPerTick, u.Sight));
            Assert.Equal((e.Id, e.Gold, e.Wood, e.Pop * 2, e.TrainTicks, e.At), (e.Id, u.CostGold, u.CostWood, u.HalfPop, u.TrainTicks, u.TrainedAt));

            // Bonus multipliers: the notes' list, 1.0 against every other armor class.
            var bonus = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (string kv in e.Bonus.Split(',', StringSplitOptions.RemoveEmptyEntries))
                bonus[kv.Split('=')[0]] = float.Parse(kv.Split('=')[1], System.Globalization.CultureInfo.InvariantCulture);
            for (int ac = 0; ac < t.ArmorClassCount; ac++)
            {
                float want = bonus.TryGetValue(t.ArmorClassKeys[ac], out float b) ? b : 1f;
                Assert.True(want == a.BonusVs[ac], $"{e.Id} bonusVs {t.ArmorClassKeys[ac]}: want {want}, got {a.BonusVs[ac]}");
            }
        }
    }

    [Fact]
    public void UnitIds_AndNames_AreUnique_AndDisjointFromBuildingAndFactionIds()
    {
        GameData d = TestSim.Data;
        var other = new HashSet<string>(d.Buildings.Select(b => b.Key).Concat(d.Factions.Select(f => f.Key)), StringComparer.OrdinalIgnoreCase);
        Assert.All(d.Units, u => Assert.DoesNotContain(u.Key, other));
        Assert.Equal(d.Units.Length, d.Units.Select(u => u.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(d.Units.Length, d.Units.Select(u => u.DisplayName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        // A unit and a building sharing a player-facing name would be ambiguous in the HUD.
        var buildingNames = new HashSet<string>(d.Buildings.Select(b => b.DisplayName), StringComparer.OrdinalIgnoreCase);
        Assert.All(d.Units, u => Assert.DoesNotContain(u.DisplayName, buildingNames));
    }

    /// <summary>Every unit is buildable: it trains at an own-faction building of the slot that trains it.</summary>
    [Fact]
    public void EveryUnit_TrainsAtItsOwnFactionsHallForItsSlot()
    {
        GameData d = TestSim.Data;
        foreach (UnitDef u in d.Units)
        {
            int bi = d.FindBuilding(u.TrainedAt);
            Assert.True(bi >= 0, $"{u.Key}: trainedAt {u.TrainedAt} is not a building");
            BuildingDef b = d.Buildings[bi];
            BuildingSlot want = u.Slot == UnitSlot.Unique ? UniqueHall[u.Key] : SlotHall[u.Slot];
            Assert.Equal((u.Key, u.Faction, want), (u.Key, b.Faction, b.Slot));
        }
    }

    /// <summary>Within a faction a higher-tier unit never costs less than its line infantry (cost out of line with tier).</summary>
    [Fact]
    public void ShockCasterSiege_CostMoreThanLine_PerFaction()
    {
        GameData d = TestSim.Data;
        foreach (FactionDef f in d.Factions)
        {
            UnitDef[] us = d.Units.Where(u => u.Faction == f.Id).ToArray();
            int Total(UnitSlot s) { UnitDef u = us.Single(x => x.Slot == s); return u.CostGold + u.CostWood; }
            int line = Total(UnitSlot.Line);
            foreach (UnitSlot s in new[] { UnitSlot.Shock, UnitSlot.Caster, UnitSlot.Siege })
                Assert.True(Total(s) > line, $"{f.Key} {s} total {Total(s)} <= line {line}");
            Assert.True(Total(UnitSlot.Worker) < line, $"{f.Key} worker costs at least the line");
        }
    }

    public static IEnumerable<object[]> AllEntries()
    {
        foreach (string f in new[] { "malazan", "whirlwind" })
            for (int i = 0; i < 7; i++) yield return new object[] { f, i };
    }

    /// <summary>Mutation: one stat of any entry changed by 1 changes the content hash (the golden data-hash catches drift).</summary>
    [Theory]
    [MemberData(nameof(AllEntries))]
    public void OneStatOfAnyEntry_ChangesTheContentHash(string faction, int index)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson($"factions/{faction}/units.json", root =>
        {
            JsonObject u = root["units"]![index]!.AsObject();
            u["hp"] = (int)u["hp"]! + 1;
        });
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        Assert.NotEqual(TestSim.Data.ContentHash(), r.Data!.ContentHash());
    }

    /// <summary>Mutation: a description edit alone (a dropped period) changes the content hash too.</summary>
    [Fact]
    public void DescriptionEdit_ChangesTheContentHash()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("factions/whirlwind/units.json", root =>
        {
            JsonObject u = root["units"]![6]!.AsObject();
            u["description"] = ((string)u["description"]!).TrimEnd('.');
        });
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        Assert.NotEqual(TestSim.Data.ContentHash(), r.Data!.ContentHash());
    }

    /// <summary>Mutation: a bad value in the last entry is one error at that entry's path.</summary>
    [Theory]
    [InlineData("hp", "-1")]
    [InlineData("speed", "0")]
    [InlineData("slot", "\"hero\"")]
    [InlineData("armorClass", "\"structure_typo\"")]
    public void BadFieldInLastEntry_IsOneErrorAtItsIndex(string field, string raw)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("factions/whirlwind/units.json", root => root["units"]![6]![field] = JsonNode.Parse(raw));
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.Null(r.Data);
        DataError e = Assert.Single(r.Errors);
        Assert.Equal("factions/whirlwind/units.json", e.File);
        Assert.StartsWith($"units[6].{field}", e.Path);
    }

    /// <summary>A Malazan unit id pasted into the Whirlwind file is a load error, not a silent override.</summary>
    [Fact]
    public void SameIdInBothFactionFiles_IsAnError()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("factions/whirlwind/units.json", root => root["units"]![3]!["id"] = "malazan_wickan_lancer");
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.Null(r.Data);
        Assert.NotEmpty(r.Errors);
    }

    /// <summary>
    /// Was the pinned gap "an unresolved <c>trainedAt</c> still loads"; flipped by M3-4 (sim track) as this pin asked:
    /// the loader resolves <c>trainedAt</c> now, so a cross-faction or misspelled one is one error at the unit's field.
    /// </summary>
    [Theory]
    [InlineData("malazan_barracks")]
    [InlineData("whirlwind_raider_camp_typo")]
    public void UnresolvedTrainedAt_IsOneErrorAtTheField(string trainedAt)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("factions/whirlwind/units.json", root => root["units"]![1]!["trainedAt"] = trainedAt);
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.Null(r.Data);
        DataError e = Assert.Single(r.Errors);
        Assert.Equal(("factions/whirlwind/units.json", "units[1].trainedAt"), (e.File, e.Path));
    }
}
