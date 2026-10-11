using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
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

    // docs/factions/<id>.md "Units" Targets column (D5; docs/02 "Combat: Stats" attack.targets): every unit "all" but
    // the Battering Ram, which "attacks buildings only" (whirlwind.md unit notes).
    private static readonly Dictionary<string, string> Targets = new()
    {
        ["malazan_laborer"] = "all",
        ["malazan_heavy_infantry"] = "all",
        ["malazan_crossbowman"] = "all",
        ["malazan_wickan_lancer"] = "all",
        ["malazan_cadre_mage"] = "all",
        ["malazan_catapult"] = "all",
        ["malazan_sapper"] = "all",
        ["whirlwind_camp_follower"] = "all",
        ["whirlwind_raider"] = "all",
        ["whirlwind_desert_archer"] = "all",
        ["whirlwind_horse_raider"] = "all",
        ["whirlwind_priest"] = "all",
        ["whirlwind_battering_ram"] = "buildings",
        ["whirlwind_zealot"] = "all",
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

    /// <summary>The data's <c>attack.targets</c> as the file spells it ("all" when the field is absent).</summary>
    private static string TargetsOf(UnitDef u) => DataLimits.AttackTargetIds[(int)u.Attack.Targets];

    /// <summary>One field of a pin, failing "page X vs data Y" with the unit and the field named (BUG-0132's style).</summary>
    private static void Pin(string where, string field, string page, string data) =>
        Assert.True(page == data, $"{where} {field}: page {page} vs data {data}");

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
                Pin(r.Id, "attack.targets", Targets[r.Id], TargetsOf(u));

                // The file holds seconds and m/s, not ticks.
                JsonNode f = FileUnit(faction, r.Id);
                Assert.Equal((r.Cd, r.Speed, (double)r.Train), ((double)f["attack"]!["cooldown"]!, (double)f["speed"]!, (double)f["trainTime"]!));
                // Style rule, not a page value (BUG-0200): "all" is the default, so the file writes the field only when it
                // narrows the attack. The page's value itself is pinned just above.
                string? written = (string?)f["attack"]!["targets"];
                string? expected = Targets[r.Id] == "all" ? null : Targets[r.Id];
                Assert.True(written == expected, written == "all"
                    ? $"{r.Id} file attack.targets: \"all\" written out although it is the default (style rule: write targets only when it is not \"all\")"
                    : $"{r.Id} file attack.targets: file {written ?? "(absent)"}, expected {expected ?? "(absent)"} (style rule: write targets only when it is not \"all\")");
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
                // Slot | Unit | HP | Armor | Class | Attack | Type | CD | Range | Targets | Speed | Sight | Cost (G/W) | Pop | Train | Trained at
                string[] c = rows[i];
                var r = roster[i];
                Assert.Equal(16, c.Length);
                string where = $"{faction}.md Units row {r.Name}";
                Assert.True((r.Slot, r.Name) == (FactionPage.Slot<UnitSlot>(c[0]), c[1]), where + " slot/name");
                Assert.True((r.Hp, r.Armor, r.Class) == (int.Parse(c[2]), int.Parse(c[3]), c[4].ToLowerInvariant()), where + " hp/armor/class");
                Assert.True((r.Atk, r.Type, r.Cd) == (int.Parse(c[5]), c[6].ToLowerInvariant(), FactionPage.Num(c[7])), where + " attack");
                // BUG-0111: invariant culture, so a page range like 7.5 is not expected as "7,5" on a de-DE machine.
                string range = r.Range == Melee ? "melee"
                    : r.MinRange > 0 ? $"{FactionPage.Text(r.Range)} (min {FactionPage.Text(r.MinRange)})"
                    : FactionPage.Text(r.Range);
                Assert.True(range == c[8], where + $" range '{c[8]}'");
                Pin(where, "targets", c[9], Targets[r.Id]);
                Assert.True((r.Speed, (double)r.Sight) == (FactionPage.Num(c[10]), FactionPage.Num(c[11])), where + " speed/sight");
                Assert.True((r.Gold, r.Wood) == FactionPage.Cost(c[12]), where + " cost");
                Assert.True((r.Pop, r.Train) == (int.Parse(c[13]), int.Parse(c[14])), where + " pop/train");
                Assert.True(Data.Buildings[Data.FindBuilding(r.TrainedAt)].DisplayName == c[15], where + " trained at");
            }
        }
    }

    /// <summary>"attacks buildings only" / "attacks units only" in a unit note: the sentence form of a narrowed <c>attack.targets</c>.</summary>
    private static readonly Regex AttacksOnly = new(@"\battacks (?<t>buildings|units) only\b", RegexOptions.IgnoreCase);

    [Fact]
    public void H_TheUnitNotes_SayAttacksXOnly_ExactlyWhenTheDataNarrowsTargets()
    {
        // The whirlwind page's Battering Ram note says "attacks buildings only"; the sentence and the field must agree
        // both ways: a note that says it binds the data, and a unit the data narrows must have a note that says so.
        foreach (string faction in Factions)
        {
            string[] notes = FactionPage.Bullets(faction, "Unit notes", 3);
            foreach (var r in Roster[faction])
            {
                string where = $"{faction}.md Unit notes {r.Name}";
                string[] own = notes.Where(n => n.StartsWith(r.Name + ":", StringComparison.Ordinal)
                    || n.StartsWith(r.Name + " (", StringComparison.Ordinal)).ToArray();
                Assert.True(own.Length <= 1, $"{where}: {own.Length} notes");
                Match m = own.Length == 1 ? AttacksOnly.Match(own[0]) : Match.Empty;
                string page = m.Success ? m.Groups["t"].Value.ToLowerInvariant() : "all";
                Pin(where, "attack.targets", page, TargetsOf(Unit(r.Id)));
            }
            // No note may say it of a unit outside the roster (a renamed unit would otherwise drop its check silently).
            foreach (string n in notes.Where(n => AttacksOnly.IsMatch(n)))
                Assert.True(Roster[faction].Any(r => n.StartsWith(r.Name + ":", StringComparison.Ordinal) || n.StartsWith(r.Name + " (", StringComparison.Ordinal)),
                    $"{faction}.md Unit notes: '{n}' says 'attacks ... only' but names no unit of the roster");
        }
    }

    // D12: docs/02 "Stealth and detection" ("Detectors: ... all casters (10 m)") and the "Caster" class row ("detector"),
    // typed here: the two shipped casters detect at 10 m and no other unit has a detector.
    private static readonly Dictionary<string, double> Detectors = new()
    {
        ["malazan_cadre_mage"] = 10,
        ["whirlwind_priest"] = 10,
    };

    /// <summary>"detector 10 m" / "detector (14 m)" in a unit note; the radius is absent when the note says only "detector".</summary>
    private static readonly Regex NoteDetector = new(@"\bdetector\b(?:\s+\(?(?<n>\d+(?:\.\d+)?) m\)?)?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>A description says it detects when it "detects" / "spots" / "reveals" hidden units or enemies (the towers' wording).</summary>
    private static readonly Regex SaysDetects = new(@"\b(spots|reveals|detects) hidden (enemies|units)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>docs/02 "Stealth and detection": "all casters (10 m)".</summary>
    private static readonly Regex CastersDetect = new(@"\ball casters \((?<n>\d+(?:\.\d+)?) m\)", RegexOptions.CultureInvariant);

    private static bool IsNoteOf(string note, string name) =>
        note.StartsWith(name + ":", StringComparison.Ordinal) || note.StartsWith(name + " (", StringComparison.Ordinal);

    /// <summary>A detector radius as the pages write it, "none" for 0.</summary>
    private static string DetectorText(double meters) => meters > 0 ? FactionPage.Text(Math.Round(meters, 4)) : "none";

    /// <summary>
    /// D12: each roster unit's "Unit notes" detector ("detector 10 m", or none) against <see cref="UnitDef.Detector"/>, both
    /// ways; a note stating a detector for a unit outside the roster fails too. Problems name the page, the unit and the field.
    /// </summary>
    internal static List<string> PageDetectorProblems(GameData data, string faction, string[] notes)
    {
        var problems = new List<string>();
        foreach (var r in Roster[faction])
        {
            string where = $"{faction}.md Unit notes {r.Name} detector";
            string[] own = notes.Where(n => IsNoteOf(n, r.Name)).ToArray();
            if (own.Length > 1) { problems.Add($"{where}: {own.Length} notes"); continue; }
            Match m = own.Length == 1 ? NoteDetector.Match(own[0]) : Match.Empty;
            string page = !m.Success ? "none"
                : m.Groups["n"].Success ? DetectorText(double.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture))
                : "'detector' without a radius";
            string have = DetectorText(data.Units[data.FindUnit(r.Id)].Detector);
            if (page != have) problems.Add($"{where}: page {page} vs data {have}");
        }
        foreach (string n in notes.Where(n => NoteDetector.IsMatch(n)))
            if (!Roster[faction].Any(r => IsNoteOf(n, r.Name)))
                problems.Add($"{faction}.md Unit notes: '{n}' states a detector but names no unit of the roster");
        return problems;
    }

    /// <summary>D12: every unit's description says it detects hidden units exactly when the data gives it a detector.</summary>
    internal static List<string> DescriptionDetectorProblems(GameData data)
    {
        var problems = new List<string>();
        foreach (UnitDef u in data.Units)
        {
            bool says = SaysDetects.IsMatch(u.Description), detects = u.Detector > 0;
            if (says && !detects) problems.Add($"{u.Key} detector: description says it detects hidden units, data has no detector");
            if (detects && !says) problems.Add($"{u.Key} detector: data has detector {DetectorText(u.Detector)}, description does not say it detects hidden units");
        }
        return problems;
    }

    /// <summary>D12: docs/02 "Stealth and detection"'s "all casters (&lt;n&gt; m)" against every Caster-slot unit's detector.</summary>
    internal static List<string> Docs02CasterDetectorProblems(GameData data, string text)
    {
        Match m = CastersDetect.Match(text);
        if (!m.Success) return new List<string> { "docs/02 Stealth and detection all casters: states no casters' detector radius" };
        string page = DetectorText(double.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture));
        var problems = new List<string>();
        foreach (UnitDef u in data.Units)
            if (u.Slot == UnitSlot.Caster && DetectorText(u.Detector) != page)
                problems.Add($"docs/02 Stealth and detection all casters: page {page} vs data {u.Key} {DetectorText(u.Detector)}");
        return problems;
    }

    private static string Docs02Stealth() => FactionPage.DocText(Path.Combine("docs", "02-game-design.md"), "Stealth and detection", 2);

    private static string[] ShippedNotes(string faction) => FactionPage.Bullets(faction, "Unit notes", 3);

    /// <summary>Every detector problem for <paramref name="data"/> against the given pages' notes and docs/02 text.</summary>
    private static List<string> AllDetectorProblems(GameData data, Func<string, string[]> notes, string docs02)
    {
        var problems = new List<string>();
        foreach (string faction in Factions) problems.AddRange(PageDetectorProblems(data, faction, notes(faction)));
        problems.AddRange(DescriptionDetectorProblems(data));
        problems.AddRange(Docs02CasterDetectorProblems(data, docs02));
        return problems;
    }

    [Fact]
    public void I_OnlyTheTwoCasters_Detect_At10m()
    {
        foreach (UnitDef u in Data.Units)
            Pin(u.Key, "detector", DetectorText(Detectors.GetValueOrDefault(u.Key)), DetectorText(u.Detector));
        foreach (string id in Detectors.Keys)
        {
            Assert.Equal(UnitSlot.Caster, Unit(id).Slot);
            Assert.Equal(10f, Unit(id).Detector);
        }
        // Every caster of the roster detects (docs/02 "Caster" row: "detector").
        Assert.All(Data.Units.Where(u => u.Slot == UnitSlot.Caster), u => Assert.True(u.Detector > 0, $"{u.Key}: a caster without a detector"));
    }

    [Fact]
    public void J_TheDetectors_MatchTheUnitNotes_TheDescriptions_AndDocs02_BothWays()
    {
        List<string> problems = AllDetectorProblems(Data, ShippedNotes, Docs02Stealth());
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        // Not vacuous: both pages state the casters' detector, and docs/02 states the casters' radius.
        foreach ((string faction, string name) in new[] { ("malazan", "Cadre Mage"), ("whirlwind", "Priest of the Whirlwind") })
            Assert.Contains(ShippedNotes(faction), n => IsNoteOf(n, name) && n.Contains("detector 10 m", StringComparison.Ordinal));
        Assert.Matches(CastersDetect, Docs02Stealth());
    }

    /// <summary>D12 criterion 2, the page side: a unit note changed in memory fails naming the page, the unit and the field.</summary>
    [Theory]
    [InlineData("malazan", "Cadre Mage:", "detector 10 m", "detector 12 m", "malazan.md Unit notes Cadre Mage detector: page 12 vs data 10")]
    [InlineData("whirlwind", "Priest of the Whirlwind:", ", detector 10 m", "", "whirlwind.md Unit notes Priest of the Whirlwind detector: page none vs data 10")]
    [InlineData("whirlwind", "Priest of the Whirlwind:", "detector 10 m", "detector", "whirlwind.md Unit notes Priest of the Whirlwind detector: page 'detector' without a radius vs data 10")]
    [InlineData("malazan", "Crossbowman:", "than a bow.", "than a bow. Detector 8 m.", "malazan.md Unit notes Crossbowman detector: page 8 vs data none")]
    [InlineData("whirlwind", "Desert Archer:", "Desert Archer: fires faster but hits softer than the template. Fast on foot.",
        "Desert Scout: fires faster. Detector 8 m.", "whirlwind.md Unit notes: 'Desert Scout: fires faster. Detector 8 m.' states a detector but names no unit of the roster")]
    public void K_AMutatedUnitNote_FailsNamingTheUnit(string faction, string note, string text, string mutant, string want)
    {
        string[] notes = ShippedNotes(faction);
        int i = Array.FindIndex(notes, n => n.StartsWith(note, StringComparison.Ordinal));
        Assert.True(i >= 0 && notes[i].Contains(text, StringComparison.Ordinal), $"{faction}.md has no '{note}' note with '{text}'");
        notes[i] = notes[i].Replace(text, mutant, StringComparison.Ordinal);

        List<string> problems = AllDetectorProblems(Data, f => f == faction ? notes : ShippedNotes(f), Docs02Stealth());

        Assert.Contains(want, problems);
    }

    /// <summary>D12 criterion 2, docs/02's side: "all casters (10 m)" changed in memory fails naming each caster.</summary>
    [Theory]
    [InlineData("all casters (10 m)", "all casters (12 m)", "docs/02 Stealth and detection all casters: page 12 vs data malazan_cadre_mage 10")]
    [InlineData("all casters (10 m)", "all casters (12 m)", "docs/02 Stealth and detection all casters: page 12 vs data whirlwind_priest 10")]
    [InlineData("all casters (10 m)", "all casters", "docs/02 Stealth and detection all casters: states no casters' detector radius")]
    public void K2_AMutatedDocs02Detectors_FailsNamingTheCaster(string text, string mutant, string want)
    {
        string docs02 = Docs02Stealth();
        Assert.Contains(text, docs02);

        List<string> problems = AllDetectorProblems(Data, ShippedNotes, docs02.Replace(text, mutant, StringComparison.Ordinal));

        Assert.Contains(want, problems);
    }

    private static GameData LoadEdited(Action<TestDataDir> edit)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        edit(dir);
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        return r.Data!;
    }

    /// <summary>
    /// D12 criterion 2, the data side: a detector (or a caster's description) changed in a copy of the data fails naming the
    /// unit and the field on the page, the description and docs/02 alike; each row lists every message it must produce.
    /// </summary>
    [Theory]
    [InlineData("malazan", "malazan_cadre_mage", "detector", "12",
        "malazan.md Unit notes Cadre Mage detector: page 10 vs data 12|docs/02 Stealth and detection all casters: page 10 vs data malazan_cadre_mage 12")]
    [InlineData("whirlwind", "whirlwind_priest", "detector", null,
        "whirlwind.md Unit notes Priest of the Whirlwind detector: page 10 vs data none|whirlwind_priest detector: description says it detects hidden units, data has no detector|docs/02 Stealth and detection all casters: page 10 vs data whirlwind_priest none")]
    [InlineData("malazan", "malazan_crossbowman", "detector", "10",
        "malazan.md Unit notes Crossbowman detector: page none vs data 10|malazan_crossbowman detector: data has detector 10, description does not say it detects hidden units")]
    [InlineData("whirlwind", "whirlwind_priest", "description", "\"Calls down the goddess's storm to blind and slow the enemy.\"",
        "whirlwind_priest detector: data has detector 10, description does not say it detects hidden units")]
    public void K3_AMutatedDetectorInTheData_FailsNamingTheUnit(string faction, string unit, string field, string? rawJson, string wants)
    {
        GameData data = LoadEdited(dir => dir.SetUnitField(faction, unit, field, rawJson));

        List<string> problems = AllDetectorProblems(data, ShippedNotes, Docs02Stealth());

        Assert.Equal(wants.Split('|'), problems);
    }
}
