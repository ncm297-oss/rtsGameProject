using System.Text.Json.Nodes;
using Rts.Sim.Data;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// Content (D2): each faction's shipped <c>units.json</c> holds the seven-slot roster with the ids, names and
/// numbers of its faction page's "Units" table. The table below is typed from the pages, not read from the loader,
/// and G reads the pages themselves, so a number changed in the data or on a page alone fails here.
/// </summary>
public class UnitContentTests
{
    private static readonly string[] Factions = { "malazan", "whirlwind" };

    // Melee range on the pages is the word "melee"; the data writes it as 0.5 m edge to edge (docs/03 "Data format").
    private const double Melee = 0.5;

    // docs/factions/<id>.md "Units", in slot order. Times in seconds, speed in m/s, as the page and the file write them.
    private static readonly Dictionary<string, (UnitSlot Slot, string Id, string Name, int Hp, int Armor, string Class, int Atk, string Type, double Cd, double Range, double MinRange, double Speed, int Sight, int Gold, int Wood, int Pop, int Train, string TrainedAt)[]> Roster = new()
    {
        ["malazan"] = new[]
        {
            (UnitSlot.Worker, "malazan_laborer", "Laborer", 40, 0, "light", 4, "melee", 1.5, Melee, 0.0, 4.0, 14, 50, 0, 1, 12, "malazan_garrison_keep"),
            (UnitSlot.Line, "malazan_heavy_infantry", "Heavy Infantry", 130, 3, "heavy", 10, "melee", 1.5, Melee, 0.0, 3.0, 14, 54, 20, 1, 14, "malazan_barracks"),
            (UnitSlot.Ranged, "malazan_crossbowman", "Crossbowman", 55, 0, "light", 9, "pierce", 2.2, 15, 0.0, 3.2, 18, 36, 45, 1, 16, "malazan_crossbow_range"),
            (UnitSlot.Shock, "malazan_wickan_lancer", "Wickan Lancer", 150, 1, "mounted", 12, "melee", 1.8, Melee, 0.0, 6.2, 16, 90, 30, 2, 26, "malazan_wickan_corral"),
            (UnitSlot.Caster, "malazan_cadre_mage", "Cadre Mage", 60, 0, "light", 9, "magic", 2.2, 12, 0.0, 3.2, 16, 100, 50, 2, 30, "malazan_cadre_tower"),
            (UnitSlot.Siege, "malazan_catapult", "Catapult", 220, 4, "heavy", 50, "siege", 5.0, 24, 6, 2.2, 18, 200, 150, 3, 40, "malazan_engineers_yard"),
            (UnitSlot.Unique, "malazan_sapper", "Sapper", 70, 1, "light", 20, "siege", 3.0, 8, 0.0, 3.4, 16, 72, 40, 1, 20, "malazan_engineers_yard"),
        },
        ["whirlwind"] = new[]
        {
            (UnitSlot.Worker, "whirlwind_camp_follower", "Camp Follower", 36, 0, "light", 4, "melee", 1.5, Melee, 0.0, 4.0, 14, 40, 0, 1, 12, "whirlwind_holy_camp"),
            (UnitSlot.Line, "whirlwind_raider", "Raider", 108, 1, "heavy", 11, "melee", 1.5, Melee, 0.0, 3.4, 14, 48, 16, 1, 18, "whirlwind_raider_camp"),
            (UnitSlot.Ranged, "whirlwind_desert_archer", "Desert Archer", 50, 0, "light", 7, "pierce", 1.8, 14, 0.0, 3.6, 18, 32, 36, 1, 20, "whirlwind_archer_camp"),
            (UnitSlot.Shock, "whirlwind_horse_raider", "Horse Raider", 135, 0, "mounted", 11, "melee", 1.6, Melee, 0.0, 6.6, 18, 72, 24, 2, 26, "whirlwind_horse_lines"),
            (UnitSlot.Caster, "whirlwind_priest", "Priest of the Whirlwind", 54, 0, "light", 9, "magic", 2.2, 12, 0.0, 3.2, 16, 80, 40, 2, 30, "whirlwind_shrine"),
            (UnitSlot.Siege, "whirlwind_battering_ram", "Battering Ram", 240, 6, "heavy", 60, "siege", 3.0, Melee, 0.0, 2.4, 10, 160, 120, 3, 36, "whirlwind_ram_yard"),
            (UnitSlot.Unique, "whirlwind_zealot", "Zealot", 63, 0, "light", 9, "melee", 1.0, Melee, 0.0, 4.4, 14, 30, 10, 1, 10, "whirlwind_raider_camp"),
        },
    };

    // Producer decision M1-2: the pages don't give these, so they are pinned as shipped. Radius by class
    // (0.4 foot / 0.7 mounted / 0.9 siege); windup in seconds.
    private static readonly Dictionary<string, (double Radius, double Windup)> Unspecified = new()
    {
        ["malazan_laborer"] = (0.4, 0.3),
        ["malazan_heavy_infantry"] = (0.4, 0.3),
        ["malazan_crossbowman"] = (0.4, 0.45),
        ["malazan_wickan_lancer"] = (0.7, 0.3),
        ["malazan_cadre_mage"] = (0.4, 0.4),
        ["malazan_catapult"] = (0.9, 0.4),
        ["malazan_sapper"] = (0.4, 0.4),
        ["whirlwind_camp_follower"] = (0.4, 0.3),
        ["whirlwind_raider"] = (0.4, 0.3),
        ["whirlwind_desert_archer"] = (0.4, 0.4),
        ["whirlwind_horse_raider"] = (0.7, 0.3),
        ["whirlwind_priest"] = (0.4, 0.4),
        ["whirlwind_battering_ram"] = (0.9, 0.3),
        ["whirlwind_zealot"] = (0.4, 0.3),
    };

    private static GameData Data => TestSim.Data;

    private static UnitDef Unit(string id)
    {
        int i = Data.FindUnit(id);
        Assert.True(i >= 0, $"missing unit {id}");
        return Data.Units[i];
    }

    private static JsonArray FileList(string faction) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(TestDataDir.Shipped, "factions", faction, "units.json")))!["units"]!.AsArray();

    private static JsonNode FileUnit(string faction, string id) => FileList(faction).Single(n => (string)n!["id"]! == id)!;

    private static int Ticks(double seconds) => (int)Math.Round(seconds * SimConstants.TicksPerSecond, MidpointRounding.AwayFromZero);

    [Fact]
    public void A_EachFaction_HasSevenUnits_OnePerSlot_InSlotOrder()
    {
        foreach (string faction in Factions)
        {
            Assert.Equal(DataLimits.SlotIds, FileList(faction).Select(n => (string)n!["slot"]!));
            UnitDef[] units = Data.Units.Where(u => Data.Factions[u.Faction].Key == faction).ToArray();
            // The loader sorts units by id, so slot order is checked on the file above; here, one of each slot.
            Assert.Equal(Enum.GetValues<UnitSlot>(), units.Select(u => u.Slot).OrderBy(s => s));
        }
        Assert.Equal(14, Data.Units.Length);
        Assert.Equal(14, Data.Units.Select(u => u.Key).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void B_IdsAndNames_MatchTheFactionPages()
    {
        foreach (string faction in Factions)
        {
            Assert.Equal(Roster[faction].Select(r => r.Id), FileList(faction).Select(n => (string)n!["id"]!));
            foreach (var r in Roster[faction])
            {
                UnitDef u = Unit(r.Id);
                Assert.Equal((r.Slot, r.Name, faction), (u.Slot, u.DisplayName, Data.Factions[u.Faction].Key));
            }
        }
    }

    [Fact]
    public void C_EveryNumber_MatchesThePage()
    {
        DamageTable t = Data.DamageTable;
        foreach (string faction in Factions)
            foreach (var r in Roster[faction])
            {
                UnitDef u = Unit(r.Id);
                AttackDef a = u.Attack;
                Assert.True((r.Hp, r.Armor, r.Class) == (u.Hp, u.Armor, t.ArmorClassKeys[u.ArmorClass]), $"{r.Id} hp/armor/class");
                Assert.True((r.Atk, r.Type, Ticks(r.Cd)) == (a.Value, t.DamageTypeKeys[a.DamageType], a.CooldownTicks), $"{r.Id} attack");
                Assert.True(((float)r.Range, (float)r.MinRange) == (a.Range, a.MinRange), $"{r.Id} range");
                Assert.True((float)(r.Speed / SimConstants.TicksPerSecond) == u.SpeedPerTick, $"{r.Id} speed");
                Assert.True(r.Sight == u.Sight, $"{r.Id} sight");
                Assert.True((r.Gold, r.Wood, r.Pop * 2) == (u.CostGold, u.CostWood, u.HalfPop), $"{r.Id} cost/pop");
                Assert.True(Ticks(r.Train) == u.TrainTicks, $"{r.Id} trainTime");
                Assert.True(r.TrainedAt == u.TrainedAt, $"{r.Id} trainedAt");

                // The file holds seconds and m/s, not ticks.
                JsonNode f = FileUnit(faction, r.Id);
                Assert.Equal((r.Cd, r.Speed, (double)r.Train), ((double)f["attack"]!["cooldown"]!, (double)f["speed"]!, (double)f["trainTime"]!));
            }
    }

    [Fact]
    public void D_OnlyTheUniques_HaveRequires_AndThatIsTheAgeIIPlaceholder()
    {
        // "age_ii" is the current placeholder for "needs Age II" (docs/02 "Buildings"); its real form is M3-5's call.
        foreach (UnitDef u in Data.Units)
            Assert.Equal(u.Slot == UnitSlot.Unique ? new[] { "age_ii" } : Array.Empty<string>(), u.Requires.ToArray());
    }

    [Fact]
    public void E_Descriptions_AreOneOrTwoShortSentences_NamingOnlyOwnFactionThings()
    {
        foreach (UnitDef u in Data.Units)
        {
            string d = u.Description;
            Assert.False(string.IsNullOrWhiteSpace(d), u.Key);
            Assert.True(d.Length <= 160, $"{u.Key}: {d.Length} chars");
            Assert.Equal(d.Trim(), d);
            Assert.EndsWith(".", d);
            int sentences = d.Split(". ").Length;
            Assert.True(sentences <= 2, $"{u.Key}: {sentences} sentences");
            foreach (UnitDef o in Data.Units)
                if (o.Faction != u.Faction) Assert.False(d.Contains(o.DisplayName, StringComparison.Ordinal), $"{u.Key} names {o.DisplayName}");
            foreach (BuildingDef b in Data.Buildings)
                if (b.Faction != u.Faction) Assert.False(d.Contains(b.DisplayName, StringComparison.Ordinal), $"{u.Key} names {b.DisplayName}");
            foreach (FactionDef f in Data.Factions)
                if (f.Id != u.Faction) Assert.False(d.Contains(f.DisplayName, StringComparison.Ordinal), $"{u.Key} names {f.DisplayName}");
        }
    }

    [Fact]
    public void F_RadiusAndWindup_ArePinnedAsShipped()
    {
        Assert.Equal(Data.Units.Select(u => u.Key).OrderBy(k => k, StringComparer.Ordinal), Unspecified.Keys.OrderBy(k => k, StringComparer.Ordinal));
        foreach (UnitDef u in Data.Units)
        {
            (double radius, double windup) = Unspecified[u.Key];
            Assert.True((float)radius == u.Radius, $"{u.Key} radius");
            Assert.True(Ticks(windup) == u.Attack.WindupTicks, $"{u.Key} windup");
        }
    }

    [Fact]
    public void G_ThePagesUnitTables_MatchTheRosterAbove()
    {
        foreach (string faction in Factions)
        {
            string[][] rows = FactionPage.Table(faction, "Units");
            var roster = Roster[faction];
            Assert.Equal(roster.Length, rows.Length);
            for (int i = 0; i < rows.Length; i++)
            {
                // Slot | Unit | HP | Armor | Class | Attack | Type | CD | Range | Speed | Sight | Cost (G/W) | Pop | Train | Trained at
                string[] c = rows[i];
                var r = roster[i];
                Assert.Equal(15, c.Length);
                string where = $"{faction}.md Units row {r.Name}";
                Assert.True((r.Slot, r.Name) == (FactionPage.Slot<UnitSlot>(c[0]), c[1]), where + " slot/name");
                Assert.True((r.Hp, r.Armor, r.Class) == (int.Parse(c[2]), int.Parse(c[3]), c[4].ToLowerInvariant()), where + " hp/armor/class");
                Assert.True((r.Atk, r.Type, r.Cd) == (int.Parse(c[5]), c[6].ToLowerInvariant(), FactionPage.Num(c[7])), where + " attack");
                // BUG-0111: invariant culture, so a page range like 7.5 is not expected as "7,5" on a de-DE machine.
                string range = r.Range == Melee ? "melee"
                    : r.MinRange > 0 ? $"{FactionPage.Text(r.Range)} (min {FactionPage.Text(r.MinRange)})"
                    : FactionPage.Text(r.Range);
                Assert.True(range == c[8], where + $" range '{c[8]}'");
                Assert.True((r.Speed, (double)r.Sight) == (FactionPage.Num(c[9]), FactionPage.Num(c[10])), where + " speed/sight");
                Assert.True((r.Gold, r.Wood) == FactionPage.Cost(c[11]), where + " cost");
                Assert.True((r.Pop, r.Train) == (int.Parse(c[12]), int.Parse(c[13])), where + " pop/train");
                Assert.True(Data.Buildings[Data.FindBuilding(r.TrainedAt)].DisplayName == c[14], where + " trained at");
            }
        }
    }
}
