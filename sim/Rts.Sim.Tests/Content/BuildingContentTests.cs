using System.Text.Json.Nodes;
using Rts.Sim.Data;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// Content (D1): each faction's shipped <c>buildings.json</c> holds the full ten-slot roster with the ids and names from
/// its faction page and the numbers from docs/02 "Buildings". The tables below are typed from the docs, not read
/// from the loader, so a data edit that drifts from the docs fails here.
/// </summary>
public class BuildingContentTests
{
    private static readonly string[] Factions = { "malazan", "whirlwind" };

    // docs/factions/<id>.md "Buildings": (slot, id, displayName), in DataLimits.BuildingSlotIds order.
    private static readonly Dictionary<string, (BuildingSlot Slot, string Id, string Name)[]> Roster = new()
    {
        ["malazan"] = new[]
        {
            (BuildingSlot.TownHall, "malazan_garrison_keep", "Garrison Keep"),
            (BuildingSlot.House, "malazan_billet", "Billet"),
            (BuildingSlot.Camp, "malazan_depot", "Quartermaster's Depot"),
            (BuildingSlot.InfantryHall, "malazan_barracks", "Legion Barracks"),
            (BuildingSlot.RangedHall, "malazan_crossbow_range", "Crossbow Range"),
            (BuildingSlot.ShockHall, "malazan_wickan_corral", "Wickan Corral"),
            (BuildingSlot.Forge, "malazan_armory", "Armory"),
            (BuildingSlot.CasterHall, "malazan_cadre_tower", "Cadre Tower"),
            (BuildingSlot.SiegeWorks, "malazan_engineers_yard", "Engineers' Yard"),
            (BuildingSlot.WatchTower, "malazan_watchtower", "Watchtower"),
        },
        ["whirlwind"] = new[]
        {
            (BuildingSlot.TownHall, "whirlwind_holy_camp", "Holy Camp"),
            (BuildingSlot.House, "whirlwind_tent", "Tent"),
            (BuildingSlot.Camp, "whirlwind_supply_cache", "Supply Cache"),
            (BuildingSlot.InfantryHall, "whirlwind_raider_camp", "Raider Camp"),
            (BuildingSlot.RangedHall, "whirlwind_archer_camp", "Archer Camp"),
            (BuildingSlot.ShockHall, "whirlwind_horse_lines", "Horse Lines"),
            (BuildingSlot.Forge, "whirlwind_smithy", "Smithy"),
            (BuildingSlot.CasterHall, "whirlwind_shrine", "Shrine of the Whirlwind"),
            (BuildingSlot.SiegeWorks, "whirlwind_ram_yard", "Ram Yard"),
            (BuildingSlot.WatchTower, "whirlwind_lookout_tower", "Lookout Tower"),
        },
    };

    // docs/02 "Buildings" table plus "Economy" (pop, drop-off). Same for every faction.
    // (hp, armor, gold, wood, build seconds, footprint w, footprint h, pop provided, drop-off)
    private static readonly Dictionary<BuildingSlot, (int Hp, int Armor, int Gold, int Wood, int BuildS, int W, int H, int Pop, bool DropOff)> Template = new()
    {
        [BuildingSlot.TownHall] = (2400, 5, 275, 275, 90, 4, 4, 10, true),
        [BuildingSlot.House] = (500, 3, 0, 50, 20, 2, 2, 8, false),
        [BuildingSlot.Camp] = (600, 3, 0, 75, 25, 2, 2, 0, true),
        [BuildingSlot.InfantryHall] = (1200, 4, 0, 150, 40, 3, 3, 0, false),
        [BuildingSlot.RangedHall] = (1200, 4, 0, 150, 40, 3, 3, 0, false),
        [BuildingSlot.ShockHall] = (1200, 4, 75, 150, 45, 3, 3, 0, false),
        [BuildingSlot.Forge] = (1000, 4, 100, 100, 40, 3, 3, 0, false),
        [BuildingSlot.CasterHall] = (1200, 4, 150, 150, 50, 3, 3, 0, false),
        [BuildingSlot.SiegeWorks] = (1400, 4, 150, 200, 55, 3, 3, 0, false),
        [BuildingSlot.WatchTower] = (800, 5, 50, 125, 35, 2, 2, 0, false),
    };

    // docs/02 "Faction template" + faction pages: where each unit slot trains. Unique units by faction page.
    private static readonly Dictionary<UnitSlot, BuildingSlot> TrainedAtSlot = new()
    {
        [UnitSlot.Worker] = BuildingSlot.TownHall,
        [UnitSlot.Line] = BuildingSlot.InfantryHall,
        [UnitSlot.Ranged] = BuildingSlot.RangedHall,
        [UnitSlot.Shock] = BuildingSlot.ShockHall,
        [UnitSlot.Caster] = BuildingSlot.CasterHall,
        [UnitSlot.Siege] = BuildingSlot.SiegeWorks,
    };

    private static readonly Dictionary<string, BuildingSlot> UniqueTrainedAt = new()
    {
        ["malazan_sapper"] = BuildingSlot.SiegeWorks,
        ["whirlwind_zealot"] = BuildingSlot.InfantryHall,
    };

    private static GameData Data => TestSim.Data;

    private static BuildingDef[] FactionBuildings(string faction) =>
        Data.Buildings.Where(b => Data.Factions[b.Faction].Key == faction).ToArray();

    private static JsonArray FileList(string faction) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(TestDataDir.Shipped, "factions", faction, "buildings.json")))!["buildings"]!.AsArray();

    [Fact]
    public void A_EachFaction_HasTenBuildings_OnePerSlot_InSlotOrder_TownHallFirst()
    {
        Assert.Equal(Factions, Data.Factions.Select(f => f.Key));
        foreach (string faction in Factions)
        {
            Assert.Equal(10, FactionBuildings(faction).Length);
            Assert.Equal(Enum.GetValues<BuildingSlot>(), FactionBuildings(faction).Select(b => b.Slot).OrderBy(s => s));
            // File order follows DataLimits.BuildingSlotIds, which starts with town_hall.
            Assert.Equal(DataLimits.BuildingSlotIds, FileList(faction).Select(n => (string)n!["slot"]!));
            Assert.Equal("town_hall", (string?)FileList(faction)[0]!["slot"]);
        }
        Assert.Equal(20, Data.Buildings.Length);
        Assert.Equal(20, Data.Buildings.Select(b => b.Key).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void B_IdsAndNames_MatchTheFactionPages()
    {
        foreach (string faction in Factions)
        {
            JsonArray file = FileList(faction);
            (BuildingSlot Slot, string Id, string Name)[] expected = Roster[faction];
            Assert.Equal(expected.Select(e => e.Id), file.Select(n => (string)n!["id"]!));
            foreach ((BuildingSlot slot, string id, string name) in expected)
            {
                int i = Data.FindBuilding(id);
                Assert.True(i >= 0, $"missing building {id}");
                BuildingDef b = Data.Buildings[i];
                Assert.Equal((slot, name, faction), (b.Slot, b.DisplayName, Data.Factions[b.Faction].Key));
            }
        }
    }

    [Fact]
    public void C_EveryNumber_MatchesDocs02()
    {
        foreach (BuildingDef b in Data.Buildings)
        {
            var t = Template[b.Slot];
            Assert.True((t.Hp, t.Armor) == (b.Hp, b.Armor), $"{b.Key} hp/armor");
            Assert.True((t.Gold, t.Wood) == (b.CostGold, b.CostWood), $"{b.Key} cost");
            Assert.True(t.BuildS * SimConstants.TicksPerSecond == b.BuildTicks, $"{b.Key} buildTime");
            Assert.True((t.W, t.H) == (b.FootprintWidth, b.FootprintHeight), $"{b.Key} footprint");
            Assert.True(t.Pop * 2 == b.HalfPopProvided, $"{b.Key} popProvided");
            Assert.True(t.DropOff == b.DropOff, $"{b.Key} dropOff");
        }
        // buildTime is seconds in the file, not ticks.
        foreach (string faction in Factions)
            foreach (JsonNode? n in FileList(faction))
                Assert.Equal(Template[Data.Buildings[Data.FindBuilding((string)n!["id"]!)].Slot].BuildS, (double)n["buildTime"]!);
    }

    [Fact]
    public void D_ExactlyTwoDropOffsPerFaction_TownHallAndCamp()
    {
        foreach (string faction in Factions)
        {
            BuildingSlot[] dropOffs = FactionBuildings(faction).Where(b => b.DropOff).Select(b => b.Slot).OrderBy(s => s).ToArray();
            Assert.Equal(new[] { BuildingSlot.TownHall, BuildingSlot.Camp }, dropOffs);
        }
    }

    [Fact]
    public void E_EveryUnitTrainsAtAnOwnFactionBuilding_OfTheMatchingSlot()
    {
        Assert.Equal(14, Data.Units.Length);
        foreach (UnitDef u in Data.Units)
        {
            int i = Data.FindBuilding(u.TrainedAt);
            Assert.True(i >= 0, $"{u.Key}.trainedAt '{u.TrainedAt}' is not a building id");
            BuildingDef b = Data.Buildings[i];
            Assert.True(b.Faction == u.Faction, $"{u.Key} trains at another faction's {b.Key}");
            BuildingSlot expected = u.Slot == UnitSlot.Unique ? UniqueTrainedAt[u.Key] : TrainedAtSlot[u.Slot];
            Assert.True(expected == b.Slot, $"{u.Key} trains at {b.Key} ({b.Slot}), expected {expected}");
        }
    }

    [Fact]
    public void F_Descriptions_AreShortSentences_NamingOnlyOwnFactionUnits()
    {
        foreach (BuildingDef b in Data.Buildings)
        {
            string d = b.Description;
            Assert.False(string.IsNullOrWhiteSpace(d), b.Key);
            Assert.True(d.Length <= 160, $"{b.Key}: {d.Length} chars");
            Assert.Equal(d.Trim(), d);
            Assert.EndsWith(".", d);
            foreach (UnitDef u in Data.Units)
            {
                bool named = d.Contains(u.DisplayName, StringComparison.Ordinal);
                // A trainer names what it trains; nobody names another faction's units.
                if (u.TrainedAt == b.Key) Assert.True(named, $"{b.Key} should name {u.DisplayName}");
                if (u.Faction != b.Faction) Assert.False(named, $"{b.Key} names {u.DisplayName}");
            }
        }
    }
}
