using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
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

    // docs/02 "Buildings" Requires column, with "Infantry Hall" written as the faction's own hall name on its page.
    private static readonly Dictionary<BuildingSlot, string> PageRequires = new()
    {
        [BuildingSlot.ShockHall] = "InfantryHall",
        [BuildingSlot.CasterHall] = "Age II",
        [BuildingSlot.SiegeWorks] = "Age II",
        [BuildingSlot.WatchTower] = "Age II",
    };

    [Fact]
    public void G_ThePagesBuildingTables_MatchTheRosterAndTemplate()
    {
        foreach (string faction in Factions)
        {
            string[][] rows = FactionPage.Table(faction, "Buildings");
            Assert.Equal(Roster[faction].Length, rows.Length);
            for (int i = 0; i < rows.Length; i++)
            {
                // Slot | Name | Id | HP | Armor | Cost (G/W) | Build (s) | Footprint | Sight | Provides | Requires (Sight: J)
                string[] c = rows[i];
                Assert.Equal(11, c.Length);
                (BuildingSlot slot, string id, string name) = Roster[faction][i];
                var t = Template[slot];
                string where = $"{faction}.md Buildings row {name}";
                Assert.True((slot, name, id) == (FactionPage.Slot<BuildingSlot>(c[0]), c[1], c[2]), where + " slot/name/id");
                Assert.True((t.Hp, t.Armor) == (int.Parse(c[3]), int.Parse(c[4])), where + " hp/armor");
                Assert.True((t.Gold, t.Wood) == FactionPage.Cost(c[5]), where + " cost");
                Assert.True(t.BuildS == int.Parse(c[6]), where + " build");
                Assert.True($"{t.W}×{t.H}" == c[7], where + " footprint");

                // Provides: pop, drop-off and the units trained here must agree with the data.
                string provides = c[9];
                // BUG-0111: any "+N pop" claim must be the building's own pop (a 0-pop building claims none).
                int[] popClaims = Regex.Matches(provides, @"\+(\d+) pop").Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToArray();
                Assert.True(popClaims.SequenceEqual(t.Pop > 0 ? new[] { t.Pop } : Array.Empty<int>()), where + $" pop claim in '{provides}'");
                Assert.True(t.DropOff == provides.Contains("drop-off", StringComparison.OrdinalIgnoreCase), where + " drop-off");
                string[] trained = Data.Units.Where(u => u.TrainedAt == id).Select(u => u.DisplayName).ToArray();
                string trains = trained.Length == 0 ? "" : "rains " + string.Join(", ", trained);
                Assert.True(trained.Length == 0 ? !provides.Contains("rains", StringComparison.Ordinal) : provides.Contains(trains, StringComparison.Ordinal), where + $" provides '{provides}'");
                // BUG-0111: the whole cell is docs/02's with the faction's names substituted, so free text can't drift.
                string expectedProvides = Provides(faction, slot, id);
                Assert.True(expectedProvides == provides, where + $" provides '{provides}', expected '{expectedProvides}'");

                string requires = PageRequires.TryGetValue(slot, out string? r)
                    ? (r == "InfantryHall" ? Roster[faction].Single(e => e.Slot == BuildingSlot.InfantryHall).Name : r)
                    : "—";
                Assert.True(requires == c[10], where + $" requires '{c[10]}'");
                // ...and the data's requires, written as names, is that same cell.
                BuildingDef b = Data.Buildings[Data.FindBuilding(id)];
                Assert.True(RequiresText.Cell(Data, b.Requires) == c[10], where + $" requires '{c[10]}' vs data [{string.Join(", ", b.Requires)}]");
            }
        }
    }

    // docs/02 "Buildings" rows by slot: (Provides, Requires) as docs/02 writes them, generic names.
    private static Dictionary<BuildingSlot, (string Provides, string Requires)> Docs02Rows() =>
        FactionPage.DocTable(Path.Combine("docs", "02-game-design.md"), "Buildings")
            .ToDictionary(c => FactionPage.Slot<BuildingSlot>(c[0]), c => (c[6], c[7]));

    /// <summary>
    /// docs/02's Provides cell with the faction's names in: "trains Worker" / "Trains Line" become the units trained at
    /// this building, and the faction's own techs researched here (the faction upgrade at the Forge) are appended.
    /// </summary>
    private static string Provides(string faction, BuildingSlot slot, string id)
    {
        string generic = Docs02Rows()[slot].Provides;
        string[] trained = Data.Units.Where(u => u.TrainedAt == id).OrderBy(u => u.Slot).Select(u => u.DisplayName).ToArray();
        string s = trained.Length == 0 ? generic : Regex.Replace(generic, "([Tt]rains) [^,;]+", m => m.Groups[1].Value + " " + string.Join(", ", trained));
        int f = Data.FindFaction(faction);
        string[] ownTechs = Data.Techs.Where(x => x.Faction == f && x.ResearchedAtSlot == slot).Select(x => x.DisplayName).ToArray();
        return ownTechs.Length == 0 ? s : s + ", " + string.Join(", ", ownTechs);
    }

    [Fact]
    public void G2_Docs02Requires_IsWhatThePagesSubstitute()
    {
        Dictionary<BuildingSlot, (string Provides, string Requires)> rows = Docs02Rows();
        Assert.Equal(Enum.GetValues<BuildingSlot>().OrderBy(s => s), rows.Keys.OrderBy(s => s));
        foreach (BuildingSlot slot in Enum.GetValues<BuildingSlot>())
        {
            string expected = PageRequires.TryGetValue(slot, out string? r) ? (r == "InfantryHall" ? "Infantry Hall" : r) : "—";
            Assert.True(expected == rows[slot].Requires, $"docs/02 {slot} requires '{rows[slot].Requires}'");
        }
    }

    [Fact]
    public void H_Requires_ShockHallNeedsTheInfantryHall_AgeIIBuildingsNeedAgeII_OthersNothing()
    {
        foreach (string faction in Factions)
            foreach ((BuildingSlot slot, string id, string _) in Roster[faction])
            {
                string infantryHall = Roster[faction].Single(e => e.Slot == BuildingSlot.InfantryHall).Id;
                string[] expected = slot switch
                {
                    BuildingSlot.ShockHall => new[] { infantryHall },
                    BuildingSlot.CasterHall or BuildingSlot.SiegeWorks or BuildingSlot.WatchTower => new[] { "age_ii" },
                    _ => Array.Empty<string>(),
                };
                Assert.Equal(expected, Data.Buildings[Data.FindBuilding(id)].Requires.ToArray());
                // The file writes it the same way (an explicit list, empty when none).
                JsonNode file = FileList(faction).Single(n => (string)n!["id"]! == id)!;
                Assert.Equal(expected, file["requires"]!.AsArray().Select(n => (string)n!));
            }
    }

    [Fact]
    public void I_BUG0090_ADescriptionThatSaysNeedsX_HasExactlyThoseRequires()
    {
        foreach (BuildingDef b in Data.Buildings)
            RequiresText.AssertMatches(Data, b.Key, b.Description, b.Requires);
    }

    [Theory]
    [InlineData("Trains the Wickan Lancer; needs a Legion Barracks.", "Legion Barracks")]
    [InlineData("Needs Melee Weapons and Age II.", "Melee Weapons|Age II")]
    [InlineData("Trains the Cadre Mage; needs Age II.", "Age II")]
    [InlineData("Trains the Raider and, from Age II, the Zealot.", "")]
    public void I2_TheNeedsReader_FindsEveryNamedRequirement(string description, string names)
    {
        Assert.Equal(names.Length == 0 ? Array.Empty<string>() : names.Split('|'), RequiresText.Needs(description));
    }

    // D7: the pages' Sight column. docs/02 "Buildings" gives the Watch Tower sight 24; "Vision and fog of war" gives every
    // other building rules.json's buildingSight, 12 m (docs/03 "Vision, detection, fog").
    private const int TowerSight = 24;
    private const int DefaultBuildingSight = 12;
    private const int SightColumn = 8;

    /// <summary>
    /// Every page Buildings row's Sight cell against its building's <see cref="BuildingDef.Sight"/>, and every building in
    /// <paramref name="data"/> against a page row, so a change on either side is reported naming the building and field.
    /// </summary>
    private static List<string> SightMismatches(GameData data, Func<string, string[][]> table)
    {
        var failures = new List<string>();
        var onPages = new List<string>();
        foreach (string faction in Factions)
            foreach (string[] c in table(faction))
            {
                string id = c[2];
                onPages.Add(id);
                int i = data.FindBuilding(id);
                if (i < 0) { failures.Add($"{faction}.md Buildings {id} sight: on the page, not in the data"); continue; }
                string inData = FactionPage.Text(data.Buildings[i].Sight);
                if (c[SightColumn] != inData) failures.Add($"{faction}.md Buildings {id} sight: page {c[SightColumn]} vs data {inData}");
            }
        foreach (BuildingDef b in data.Buildings)
            if (!onPages.Contains(b.Key)) failures.Add($"{b.Key} sight: data {FactionPage.Text(b.Sight)}, on no page's Buildings table");
        return failures;
    }

    private static string[][] PageTable(string faction) => FactionPage.Table(faction, "Buildings");

    [Fact]
    public void J_PageSight_IsBuildingDefSight_ForAll20Buildings_BothWays()
    {
        Assert.Equal(20, Factions.Sum(f => PageTable(f).Length));
        List<string> failures = SightMismatches(Data, PageTable);
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void J2_TheSightPin_NamesBuildingAndField_WhenAPageCellChanges()
    {
        string[][] Mutated(string faction)
        {
            string[][] rows = PageTable(faction).Select(r => (string[])r.Clone()).ToArray();
            foreach (string[] r in rows)
            {
                if (r[2] == "malazan_watchtower") r[SightColumn] = "18";
                if (r[2] == "malazan_barracks") r[SightColumn] = "14";
            }
            return rows;
        }
        Assert.Equal(
            new[]
            {
                "malazan.md Buildings malazan_barracks sight: page 14 vs data 12",
                "malazan.md Buildings malazan_watchtower sight: page 18 vs data 24",
            },
            SightMismatches(Data, Mutated));
    }

    [Fact]
    public void J3_TheSightPin_NamesBuildingAndField_WhenTheDataChanges()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("factions/whirlwind/buildings.json", root =>
        {
            foreach (JsonObject b in root["buildings"]!.AsArray().Select(n => n!.AsObject()))
            {
                if ((string)b["id"]! == "whirlwind_lookout_tower") b["sight"] = 20;
                if ((string)b["id"]! == "whirlwind_tent") b["sight"] = 14;
            }
        });
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        Assert.Equal(
            new[]
            {
                "whirlwind.md Buildings whirlwind_tent sight: page 12 vs data 14",
                "whirlwind.md Buildings whirlwind_lookout_tower sight: page 24 vs data 20",
            },
            SightMismatches(r.Data!, PageTable));
    }

    [Fact]
    public void J4_TowersCarrySight24_EveryOtherBuildingInheritsBuildingSight12()
    {
        // rules.json's buildingSight is the 12 every non-tower row of the pages shows.
        JsonNode rules = JsonNode.Parse(File.ReadAllText(Path.Combine(TestDataDir.Shipped, "common", "rules.json")))!;
        Assert.Equal(DefaultBuildingSight, (double)rules["buildingSight"]!);
        Assert.Equal(DefaultBuildingSight, Data.Rules.BuildingSight);
        foreach (string faction in Factions)
        {
            foreach (JsonNode? n in FileList(faction))
            {
                string id = (string)n!["id"]!;
                BuildingSlot slot = Data.Buildings[Data.FindBuilding(id)].Slot;
                JsonNode? sight = n["sight"];
                if (slot == BuildingSlot.WatchTower)
                    Assert.True(sight != null && (double)sight == TowerSight, $"{id} sight: data {sight?.ToJsonString() ?? "none"}, expected {TowerSight}");
                else
                    Assert.True(sight == null, $"{id} sight: data carries {sight?.ToJsonString()}, expected none (rules.json buildingSight)");
            }
            foreach (string[] c in PageTable(faction))
            {
                BuildingSlot slot = FactionPage.Slot<BuildingSlot>(c[0]);
                string expected = FactionPage.Text(slot == BuildingSlot.WatchTower ? TowerSight : Data.Rules.BuildingSight);
                Assert.True(expected == c[SightColumn], $"{faction}.md Buildings {c[2]} sight: page {c[SightColumn]}, expected {expected}");
            }
        }
    }

    [Fact]
    public void J5_Docs02_StatesTheBuildingDefaultAndTheTowersSight()
    {
        float tower = Data.Buildings.Single(b => b.Key == "malazan_watchtower").Sight;
        Assert.Equal(TowerSight, tower);
        // The Buildings table: only the Watch Tower names a sight, the tower's.
        foreach ((BuildingSlot slot, (string provides, string _)) in Docs02Rows())
        {
            Match m = Regex.Match(provides, @"sight (\d+)");
            string said = m.Success ? m.Groups[1].Value : "none";
            string expected = slot == BuildingSlot.WatchTower ? FactionPage.Text(tower) : "none";
            Assert.True(expected == said, $"docs/02 Buildings {slot} sight: page {said}, expected {expected}");
        }
        // "Vision and fog of war": the default and the tower's, in meters.
        string vision = FactionPage.DocText(Path.Combine("docs", "02-game-design.md"), "Vision and fog of war", 2);
        Match d = Regex.Match(vision, @"Buildings see (\d+) m unless");
        Assert.True(d.Success && d.Groups[1].Value == FactionPage.Text(Data.Rules.BuildingSight),
            $"docs/02 Vision and fog buildingSight: page '{(d.Success ? d.Groups[1].Value : "none")}' vs data {FactionPage.Text(Data.Rules.BuildingSight)}");
        Match w = Regex.Match(vision, @"Watch Tower sees (\d+) m");
        Assert.True(w.Success && w.Groups[1].Value == FactionPage.Text(tower),
            $"docs/02 Vision and fog Watch Tower sight: page '{(w.Success ? w.Groups[1].Value : "none")}' vs data {FactionPage.Text(tower)}");
    }

    // BUG-0090's last item: docs/02 "Buildings" gives the Watch Tower an attack and a detector ("Stealth and detection":
    // Watch Towers detect at 16 m), and the towers' descriptions say so. A description says it shoots when it has the
    // word "shoots" / "fires"; it says it detects when it "spots" / "reveals" / "detects" hidden enemies or units.
    private static readonly Regex SaysShoots = new(@"\b(shoots|fires)\b", RegexOptions.IgnoreCase);
    private static readonly Regex SaysDetects = new(@"\b(spots|reveals|detects) hidden (enemies|units)\b", RegexOptions.IgnoreCase);

    /// <summary>
    /// Every building's description against its <see cref="BuildingDef.Attack"/> and <see cref="BuildingDef.Detector"/>,
    /// both ways: text that says it shoots / spots hidden enemies needs the field, and a building with the field says so.
    /// Each mismatch names the building and the field, in <see cref="GameData.Buildings"/> order (ids ascending).
    /// </summary>
    private static List<string> TowerTextMismatches(GameData data)
    {
        var failures = new List<string>();
        foreach (BuildingDef b in data.Buildings)
        {
            bool saysShoots = SaysShoots.IsMatch(b.Description), shoots = b.Attack != null;
            if (saysShoots && !shoots) failures.Add($"{b.Key} attack: description says it shoots, data has no attack");
            if (shoots && !saysShoots) failures.Add($"{b.Key} attack: data has an attack, description does not say it shoots");
            bool saysDetects = SaysDetects.IsMatch(b.Description), detects = b.Detector > 0;
            if (saysDetects && !detects) failures.Add($"{b.Key} detector: description says it spots hidden enemies, data has no detector");
            if (detects && !saysDetects) failures.Add($"{b.Key} detector: data has detector {FactionPage.Text(b.Detector)}, description does not say it spots hidden enemies");
        }
        return failures;
    }

    [Fact]
    public void K_BUG0090_ATowerThatSaysItShootsAndSpots_HasAttackAndDetector_BothWays()
    {
        List<string> failures = TowerTextMismatches(Data);
        Assert.True(failures.Count == 0, string.Join("\n", failures));
        // The shipped pair the pin is about: both towers say both, and carry both.
        foreach (string faction in Factions)
        {
            BuildingDef t = Data.Buildings[Data.FindBuilding(Roster[faction].Single(e => e.Slot == BuildingSlot.WatchTower).Id)];
            Assert.True(t.Attack != null && t.Detector > 0, $"{t.Key}: attack {(t.Attack != null)}, detector {FactionPage.Text(t.Detector)}");
            Assert.True(SaysShoots.IsMatch(t.Description) && SaysDetects.IsMatch(t.Description), $"{t.Key} description: '{t.Description}'");
        }
    }

    private static GameData LoadEdited(string faction, Action<JsonObject> edit)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson($"factions/{faction}/buildings.json", root =>
        {
            foreach (JsonObject b in root["buildings"]!.AsArray().Select(n => n!.AsObject())) edit(b);
        });
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        return r.Data!;
    }

    [Fact]
    public void K2_TheTowerPin_NamesBuildingAndField_WhenTheDataChanges()
    {
        GameData malazan = LoadEdited("malazan", b =>
        {
            if ((string)b["id"]! == "malazan_watchtower") b.Remove("detector");
        });
        GameData whirlwind = LoadEdited("whirlwind", b =>
        {
            if ((string)b["id"]! == "whirlwind_lookout_tower") b.Remove("attack");
            // A building that gains the fields without the words fails the other way.
            if ((string)b["id"]! == "whirlwind_tent")
            {
                b["attack"] = JsonNode.Parse("""{ "value": 10, "type": "pierce", "cooldown": 2, "range": 18, "windup": 0.4, "projectile": "arrow", "targets": "units" }""");
                b["detector"] = 16;
            }
        });
        Assert.Equal(new[] { "malazan_watchtower detector: description says it spots hidden enemies, data has no detector" }, TowerTextMismatches(malazan));
        Assert.Equal(
            new[]
            {
                "whirlwind_lookout_tower attack: description says it shoots, data has no attack",
                "whirlwind_tent attack: data has an attack, description does not say it shoots",
                "whirlwind_tent detector: data has detector 16, description does not say it spots hidden enemies",
            },
            TowerTextMismatches(whirlwind));
    }

    [Fact]
    public void K3_TheTowerPin_NamesBuildingAndField_WhenTheTextChanges()
    {
        GameData malazan = LoadEdited("malazan", b =>
        {
            if ((string)b["id"]! == "malazan_watchtower")
                b["description"] = "A manned tower that watches the approaches and shoots at intruders; needs Age II.";
            if ((string)b["id"]! == "malazan_billet")
                b["description"] = "Bunks for a squad that shoots at anyone near, raising your population cap by 8.";
        });
        GameData whirlwind = LoadEdited("whirlwind", b =>
        {
            if ((string)b["id"]! == "whirlwind_lookout_tower")
                b["description"] = "A tall lookout that watches the sands and spots hidden enemies; needs Age II.";
            if ((string)b["id"]! == "whirlwind_tent")
                b["description"] = "Shelter from the sun that spots hidden enemies, raising your population cap by 8.";
        });
        Assert.Equal(
            new[]
            {
                "malazan_billet attack: description says it shoots, data has no attack",
                "malazan_watchtower detector: data has detector 16, description does not say it spots hidden enemies",
            },
            TowerTextMismatches(malazan));
        Assert.Equal(
            new[]
            {
                "whirlwind_lookout_tower attack: data has an attack, description does not say it shoots",
                "whirlwind_tent detector: description says it spots hidden enemies, data has no detector",
            },
            TowerTextMismatches(whirlwind));
    }
}
