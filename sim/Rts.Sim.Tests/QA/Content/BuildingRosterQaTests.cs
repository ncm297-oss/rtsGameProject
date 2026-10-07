using System.Text.Json.Nodes;
using Rts.Sim.Data;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA (D1, session 2026-10-06-1744): the ten-building rosters checked against QA's own table (typed from docs/02
/// "Buildings" and the faction pages, keyed by id rather than by slot), plus mutation probes showing the loader's
/// validation and the content hash reach every new entry, not just index 0.
/// </summary>
public class BuildingRosterQaTests
{
    // id, faction, slot, displayName, hp, armor, gold, wood, buildSeconds, footprint, popProvided, dropOff
    private static readonly (string Id, string Faction, BuildingSlot Slot, string Name, int Hp, int Armor, int Gold, int Wood, int Build, int Size, int Pop, bool Drop)[] Expected =
    {
        ("malazan_garrison_keep", "malazan", BuildingSlot.TownHall, "Garrison Keep", 2400, 5, 275, 275, 90, 4, 10, true),
        ("malazan_billet", "malazan", BuildingSlot.House, "Billet", 500, 3, 0, 50, 20, 2, 8, false),
        ("malazan_depot", "malazan", BuildingSlot.Camp, "Quartermaster's Depot", 600, 3, 0, 75, 25, 2, 0, true),
        ("malazan_barracks", "malazan", BuildingSlot.InfantryHall, "Legion Barracks", 1200, 4, 0, 150, 40, 3, 0, false),
        ("malazan_crossbow_range", "malazan", BuildingSlot.RangedHall, "Crossbow Range", 1200, 4, 0, 150, 40, 3, 0, false),
        ("malazan_wickan_corral", "malazan", BuildingSlot.ShockHall, "Wickan Corral", 1200, 4, 75, 150, 45, 3, 0, false),
        ("malazan_armory", "malazan", BuildingSlot.Forge, "Armory", 1000, 4, 100, 100, 40, 3, 0, false),
        ("malazan_cadre_tower", "malazan", BuildingSlot.CasterHall, "Cadre Tower", 1200, 4, 150, 150, 50, 3, 0, false),
        ("malazan_engineers_yard", "malazan", BuildingSlot.SiegeWorks, "Engineers' Yard", 1400, 4, 150, 200, 55, 3, 0, false),
        ("malazan_watchtower", "malazan", BuildingSlot.WatchTower, "Watchtower", 800, 5, 50, 125, 35, 2, 0, false),
        ("whirlwind_holy_camp", "whirlwind", BuildingSlot.TownHall, "Holy Camp", 2400, 5, 275, 275, 90, 4, 10, true),
        ("whirlwind_tent", "whirlwind", BuildingSlot.House, "Tent", 500, 3, 0, 50, 20, 2, 8, false),
        ("whirlwind_supply_cache", "whirlwind", BuildingSlot.Camp, "Supply Cache", 600, 3, 0, 75, 25, 2, 0, true),
        ("whirlwind_raider_camp", "whirlwind", BuildingSlot.InfantryHall, "Raider Camp", 1200, 4, 0, 150, 40, 3, 0, false),
        ("whirlwind_archer_camp", "whirlwind", BuildingSlot.RangedHall, "Archer Camp", 1200, 4, 0, 150, 40, 3, 0, false),
        ("whirlwind_horse_lines", "whirlwind", BuildingSlot.ShockHall, "Horse Lines", 1200, 4, 75, 150, 45, 3, 0, false),
        ("whirlwind_smithy", "whirlwind", BuildingSlot.Forge, "Smithy", 1000, 4, 100, 100, 40, 3, 0, false),
        ("whirlwind_shrine", "whirlwind", BuildingSlot.CasterHall, "Shrine of the Whirlwind", 1200, 4, 150, 150, 50, 3, 0, false),
        ("whirlwind_ram_yard", "whirlwind", BuildingSlot.SiegeWorks, "Ram Yard", 1400, 4, 150, 200, 55, 3, 0, false),
        ("whirlwind_lookout_tower", "whirlwind", BuildingSlot.WatchTower, "Lookout Tower", 800, 5, 50, 125, 35, 2, 0, false),
    };

    [Fact]
    public void EveryShippedBuilding_MatchesQaTable_AndNothingElseShips()
    {
        GameData d = TestSim.Data;
        Assert.Equal(Expected.Select(e => e.Id).OrderBy(s => s, StringComparer.Ordinal), d.Buildings.Select(b => b.Key).OrderBy(s => s, StringComparer.Ordinal));
        foreach (var e in Expected)
        {
            BuildingDef b = d.Buildings[d.FindBuilding(e.Id)];
            Assert.Equal((e.Faction, e.Slot, e.Name), (d.Factions[b.Faction].Key, b.Slot, b.DisplayName));
            Assert.Equal((e.Hp, e.Armor, e.Gold, e.Wood), (b.Hp, b.Armor, b.CostGold, b.CostWood));
            Assert.Equal(e.Build * SimConstants.TicksPerSecond, b.BuildTicks);
            Assert.Equal((e.Size, e.Size), (b.FootprintWidth, b.FootprintHeight));
            Assert.Equal(e.Pop * 2, b.HalfPopProvided);
            Assert.Equal(e.Drop, b.DropOff);
        }
    }

    [Fact]
    public void BuildingIds_DoNotCollideWithUnitOrFactionIds()
    {
        GameData d = TestSim.Data;
        var other = new HashSet<string>(d.Units.Select(u => u.Key).Concat(d.Factions.Select(f => f.Key)), StringComparer.OrdinalIgnoreCase);
        Assert.All(d.Buildings, b => Assert.DoesNotContain(b.Key, other));
        // Case-insensitive uniqueness too: ids differing only in case would be near-duplicates.
        Assert.Equal(d.Buildings.Length, d.Buildings.Select(b => b.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(d.Buildings.Length, d.Buildings.Select(b => b.DisplayName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    /// <summary>Only a building some unit trains at may say it trains or builds anything (a Camp must not claim to train).</summary>
    [Fact]
    public void OnlyTrainers_ClaimToTrain()
    {
        GameData d = TestSim.Data;
        foreach (BuildingDef b in d.Buildings)
        {
            bool trains = d.Units.Any(u => u.TrainedAt == b.Key);
            bool claims = b.Description.Contains("Trains", StringComparison.OrdinalIgnoreCase) || b.Description.StartsWith("Builds", StringComparison.Ordinal);
            if (b.Slot == BuildingSlot.TownHall) continue; // Town Hall text predates D1 and is pinned elsewhere.
            Assert.True(trains == claims, $"{b.Key}: trains={trains}, description claims={claims}: {b.Description}");
        }
    }

    /// <summary>A building description never names another faction's building (copy-paste across factions).</summary>
    [Fact]
    public void Descriptions_NameNoOtherFactionsBuilding()
    {
        GameData d = TestSim.Data;
        foreach (BuildingDef b in d.Buildings)
            foreach (BuildingDef o in d.Buildings)
                if (o.Faction != b.Faction)
                    Assert.False(b.Description.Contains(o.DisplayName, StringComparison.Ordinal), $"{b.Key} names {o.DisplayName}");
    }

    public static IEnumerable<object[]> NewEntries()
    {
        foreach (string f in new[] { "malazan", "whirlwind" })
            for (int i = 1; i < 10; i++) yield return new object[] { f, i };
    }

    /// <summary>Mutation: one stat of any new entry changed by 1 changes the content hash (the golden data-hash catches drift).</summary>
    [Theory]
    [MemberData(nameof(NewEntries))]
    public void OneStatOfANewEntry_ChangesTheContentHash(string faction, int index)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson($"factions/{faction}/buildings.json", root =>
        {
            JsonObject b = root["buildings"]![index]!.AsObject();
            b["hp"] = (int)b["hp"]! + 1;
        });
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        Assert.NotEqual(TestSim.Data.ContentHash(), r.Data!.ContentHash());
    }

    /// <summary>Mutation: a bad value in the last entry is one error at that entry's path, like index 0 in BuildingDataQaTests.</summary>
    [Theory]
    [InlineData("hp", "-1")]
    [InlineData("buildTime", "1e9")]
    [InlineData("slot", "\"tower\"")]
    [InlineData("popProvided", "-0.5")]
    public void BadFieldInLastEntry_IsOneErrorAtItsIndex(string field, string raw)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("factions/whirlwind/buildings.json", root => root["buildings"]![9]![field] = JsonNode.Parse(raw));
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.Null(r.Data);
        DataError e = Assert.Single(r.Errors);
        Assert.Equal("factions/whirlwind/buildings.json", e.File);
        Assert.StartsWith($"buildings[9].{field}", e.Path);
    }

    /// <summary>A Malazan building id pasted into the Whirlwind file is a load error, not a silent override.</summary>
    [Fact]
    public void SameIdInBothFactionFiles_IsAnError()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("factions/whirlwind/buildings.json", root => root["buildings"]![3]!["id"] = "malazan_barracks");
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.Null(r.Data);
        Assert.NotEmpty(r.Errors);
    }
}
