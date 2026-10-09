using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Rts.Sim.Data;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// Content (D3): each faction's shipped <c>techs.json</c> holds exactly its faction upgrade, with the cost, time, place,
/// requirement and effects of its page's "Faction upgrade" line and docs/02 "Forge upgrades" ("Faction upgrade" row:
/// Age II, 200 G / 150 W, 45 s). The table below is typed from the docs; B and C read the pages themselves, and D
/// checks the player-facing description states every effect with its number.
/// </summary>
public class TechContentTests
{
    private static readonly string[] Factions = { "malazan", "whirlwind" };

    // docs/factions/<id>.md "Faction upgrade": effects as the file writes them (stat, amount in data units: seconds for
    // abilityCooldown, meters for range, whole points otherwise, the one unit it applies to).
    private static readonly Dictionary<string, (string Id, string Name, int Gold, int Wood, int Seconds, string[] Requires, (string Stat, double Amount, string Unit)[] Effects)> Upgrade = new()
    {
        ["malazan"] = ("moranth_supply", "Moranth Supply", 200, 150, 45, new[] { "age_ii" },
            new[] { ("abilityCooldown", -15.0, "malazan_sapper"), ("range", 4.0, "malazan_catapult") }),
        ["whirlwind"] = ("dryjhnas_prophecy", "Dryjhna's Prophecy", 200, 150, 45, new[] { "age_ii" },
            new[] { ("hp", 20.0, "whirlwind_zealot"), ("abilityCooldown", -15.0, "whirlwind_priest") }),
    };

    private static GameData Data => TestSim.Data;

    private static JsonArray FileList(string faction) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(TestDataDir.Shipped, "factions", faction, "techs.json")))!["techs"]!.AsArray();

    private static TechDef[] FactionTechs(string faction)
    {
        int f = Data.FindFaction(faction);
        return Data.Techs.Where(t => t.Faction == f).ToArray();
    }

    private static string ForgeName(string faction)
    {
        int f = Data.FindFaction(faction);
        return Data.Buildings.Single(b => b.Faction == f && b.Slot == BuildingSlot.Forge).DisplayName;
    }

    private static string Signed(double v) => v.ToString("+0.##;-0.##", CultureInfo.InvariantCulture);

    private static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>The page's "Abilities" row of the unit: (ability name, base cooldown in seconds).</summary>
    private static (string Ability, double Cooldown) Ability(string faction, string unitName)
    {
        // Ability | Unit | Kind | Range | Radius | Cast | Cooldown | ...
        string[] row = FactionPage.Table(faction, "Abilities").Single(c => c[1] == unitName);
        Assert.EndsWith(" s", row[6]);
        return (row[0], FactionPage.Num(row[6][..^2]));
    }

    /// <summary>How the pages write one effect, without the unit: "Cusser cooldown 45 → 30 s", "range +4 m", "+20 HP".</summary>
    private static string PagePhrase(string faction, TechEffect e, string unitName, out string ability)
    {
        ability = "";
        switch (e.Stat)
        {
            case TechStat.AbilityCooldown:
                (ability, double cd) = Ability(faction, unitName);
                return $"{ability} cooldown {Num(cd)} → {Num(cd + e.Amount / SimConstants.TicksPerSecond)} s";
            case TechStat.Range: return $"range {Signed(e.Amount)} m";
            case TechStat.Hp: return $"{Signed(e.Amount)} HP";
            case TechStat.Attack: return $"{Signed(e.Amount)} attack";
            default: return $"{Signed(e.Amount)} armor";
        }
    }

    /// <summary>How a description states one effect's number: "from 45 to 30 s", "+4 m", "+20 HP".</summary>
    private static string DescriptionPhrase(string faction, TechEffect e, string unitName)
    {
        switch (e.Stat)
        {
            case TechStat.AbilityCooldown:
                double cd = Ability(faction, unitName).Cooldown;
                return $"from {Num(cd)} to {Num(cd + e.Amount / SimConstants.TicksPerSecond)} s";
            case TechStat.Range: return $"{Signed(e.Amount)} m";
            case TechStat.Hp: return $"{Signed(e.Amount)} HP";
            case TechStat.Attack: return $"{Signed(e.Amount)} attack";
            default: return $"{Signed(e.Amount)} armor";
        }
    }

    private static string UnitName(TechEffect e)
    {
        Assert.Single(e.Units);
        return Data.Units[e.Units[0]].DisplayName;
    }

    /// <summary>
    /// One field of a pin: fails naming the tech (<paramref name="where"/>), the field and both values, "page X vs data Y"
    /// (BUG-0132), so a one-sided edit says at a glance which side moved.
    /// </summary>
    private static void Pin<T>(string where, string field, T page, T data) =>
        Assert.True(EqualityComparer<T>.Default.Equals(page, data), $"{where} {field}: page {page} vs data {data}");

    /// <summary>Numbers in pinned text format invariantly (QA's culture rows run these pins under ar-SA, where -1 gets a mark).</summary>
    private static string Inv(FormattableString text) => FormattableString.Invariant(text);

    private static string Join(IEnumerable<string> ids) => string.Join(", ", ids.OrderBy(i => i, StringComparer.Ordinal));

    private static string Seconds(int ticks) => Num((double)ticks / SimConstants.TicksPerSecond) + " s";

    [Fact]
    public void A_EachFaction_ShipsExactlyItsFactionUpgrade_AsTheDocsSay()
    {
        // "page" here is the typed table above, copied from the pages.
        foreach (string faction in Factions)
        {
            var u = Upgrade[faction];
            string where = $"{faction} {u.Id}";
            Pin(where, "techs file ids", u.Id, string.Join(", ", FileList(faction).Select(n => (string)n!["id"]!)));
            TechDef[] own = FactionTechs(faction);
            Pin(where, "faction tech count", 1, own.Length);
            TechDef t = own[0];
            Pin(where, "id", u.Id, t.Key);
            Pin(where, "name", u.Name, t.DisplayName);
            Pin(where, "researched at", BuildingSlot.Forge, t.ResearchedAtSlot);
            Pin(where, "cost", Inv($"{u.Gold} / {u.Wood}"), Inv($"{t.CostGold} / {t.CostWood}"));
            Pin(where, "time", Inv($"{u.Seconds} s"), Seconds(t.ResearchTicks));
            Pin(where, "requires", Join(u.Requires), Join(t.Requires));
            Pin(where, "effect count", u.Effects.Length, t.Effects.Length);
            for (int i = 0; i < u.Effects.Length; i++)
            {
                (string stat, double amount, string unit) = u.Effects[i];
                TechEffect e = t.Effects[i];
                double sim = stat == "abilityCooldown" ? amount * SimConstants.TicksPerSecond : amount;
                Pin(where, $"effect {i}", $"{stat} {Num(sim)}", $"{DataLimits.TechStatIds[(int)e.Stat]} {Num(e.Amount)}");
                Pin(where, $"effect {i} units", unit, string.Join(", ", e.Units.Select(x => Data.Units[x].Key)));
                Pin(where, $"effect {i} filters", "attackType -1, siege -1, tags 0", Inv($"attackType {e.AttackType}, siege {e.Siege}, tags {e.Tags.Length}"));
            }

            // The file holds seconds and the slot id, not ticks.
            JsonNode f = FileList(faction)[0]!;
            Pin(where, "file researchedAt", "forge", (string)f["researchedAt"]!);
            Pin(where, "file researchTime", (double)u.Seconds, (double)f["researchTime"]!);
            Pin(where, "file effect amounts", string.Join(", ", u.Effects.Select(e => Num(e.Amount))),
                string.Join(", ", f["effects"]!.AsArray().Select(n => Num((double)n!["amount"]!))));
        }
    }

    /// <summary>The head of a page's faction upgrade line: "Age II, at the Armory, 200 G / 150 W, 45 s. ".</summary>
    private static readonly Regex UpgradeHead = new(@"^(?<req>.+?), at the (?<at>.+?), (?<g>\d+) G / (?<w>\d+) W, (?<s>[\d.]+) s\. ");

    [Fact]
    public void B_ThePagesFactionUpgradeLine_MatchesTheData()
    {
        foreach (string faction in Factions)
        {
            TechDef t = Assert.Single(FactionTechs(faction));
            string where = $"{faction}.md faction upgrade {t.Key}";
            string line = FactionPage.Paragraph(faction, "Faction upgrade: " + t.DisplayName);
            Match m = UpgradeHead.Match(line);
            Assert.True(m.Success, $"{where} line: page '{line}' does not start '<requires>, at the <building>, <g> G / <w> W, <s> s. '");
            Pin(where, "requires", m.Groups["req"].Value, RequiresText.Cell(Data, t.Requires));
            Pin(where, "researched at", m.Groups["at"].Value, ForgeName(faction));
            Pin(where, "cost", $"{m.Groups["g"].Value} / {m.Groups["w"].Value}", Inv($"{t.CostGold} / {t.CostWood}"));
            Pin(where, "time", m.Groups["s"].Value + " s", Seconds(t.ResearchTicks));
            for (int i = 0; i < t.Effects.Length; i++)
            {
                TechEffect e = t.Effects[i];
                string unit = UnitName(e);
                string phrase = PagePhrase(faction, e, unit, out string ability);
                Assert.True(line.Contains(phrase, StringComparison.Ordinal), $"{where} effect {i}: page '{line}' vs data '{phrase}'");
                // The unit is named, or (for a cooldown) its ability, which the Abilities table ties to the unit.
                Assert.True(line.Contains(unit, StringComparison.Ordinal) || (ability.Length > 0 && line.Contains(ability, StringComparison.Ordinal)),
                    $"{where} effect {i} unit: page '{line}' vs data '{unit}'{(ability.Length > 0 ? $" or '{ability}'" : "")}");
            }
        }
    }

    [Fact]
    public void C_ThePagesTechsTable_MatchesTheData()
    {
        foreach (string faction in Factions)
        {
            TechDef[] techs = FactionTechs(faction);
            string[][] rows = FactionPage.Table(faction, "Techs");
            Pin($"{faction}.md Techs", "row count", rows.Length, techs.Length);
            int f = Data.FindFaction(faction);
            foreach (string[] c in rows)
            {
                // Id | Name | Researched at | Cost (G/W) | Time (s) | Requires | Effects
                Assert.Equal(7, c.Length);
                string where = $"{faction}.md Techs row {c[0]}";
                TechDef? t = techs.SingleOrDefault(x => x.Key == c[0]);
                Assert.True(t != null, $"{where} id: page {c[0]} vs data [{string.Join(", ", techs.Select(x => x.Key))}]");
                Pin(where, "name", c[1], t!.DisplayName);
                Pin(where, "researched at", c[2], Data.Buildings.Single(b => b.Faction == f && b.Slot == t.ResearchedAtSlot).DisplayName);
                (int g, int w) = FactionPage.Cost(c[3]);
                Pin(where, "cost", Inv($"{g} / {w}"), Inv($"{t.CostGold} / {t.CostWood}"));
                Pin(where, "time", c[4] + " s", Seconds(t.ResearchTicks));
                Pin(where, "requires", c[5], RequiresText.Cell(Data, t.Requires));
                Pin(where, "effects", c[6], string.Join("; ", t.Effects.Select(e => UnitName(e) + " " + PagePhrase(faction, e, UnitName(e), out _))));
            }
        }
    }

    [Fact]
    public void D_Descriptions_StateEveryEffectWithItsNumber_AndTheirRequirements()
    {
        foreach (string faction in Factions)
            foreach (TechDef t in FactionTechs(faction))
            {
                string d = t.Description;
                Assert.True(d.Length <= 160, $"{t.Key}: {d.Length} chars");
                Assert.Equal(d.Trim(), d);
                Assert.EndsWith(".", d);
                int sentences = d.Split(". ").Length;
                Assert.True(sentences <= 2, $"{t.Key}: {sentences} sentences");
                foreach (TechEffect e in t.Effects)
                {
                    string unit = UnitName(e);
                    string phrase = DescriptionPhrase(faction, e, unit);
                    Assert.True(d.Contains(unit, StringComparison.Ordinal), $"{t.Key} should name {unit}");
                    Assert.True(d.Contains(phrase, StringComparison.Ordinal), $"{t.Key} should say '{phrase}'");
                    if (e.Stat == TechStat.AbilityCooldown)
                        Assert.True(d.Contains(Ability(faction, unit).Ability, StringComparison.Ordinal), $"{t.Key} should name the ability");
                }
                RequiresText.AssertMatches(Data, t.Key, d, t.Requires);
            }
    }

    // ---- D4: the seven shared techs in common/techs.json, pinned to docs/02 "Tech" ----

    private static readonly string Doc02 = Path.Combine("docs", "02-game-design.md");

    /// <summary>The shared techs' names (docs/02 "Ages" and "Forge upgrades"; level II adds " II").</summary>
    private static readonly (string Id, string Name)[] Shared =
    {
        ("age_ii", "Age II"),
        ("melee_weapons_1", "Melee Weapons"), ("melee_weapons_2", "Melee Weapons II"),
        ("ranged_weapons_1", "Ranged Weapons"), ("ranged_weapons_2", "Ranged Weapons II"),
        ("armor_1", "Armor"), ("armor_2", "Armor II"),
    };

    /// <summary>
    /// docs/02 "Forge upgrades" rows, typed: the two levels' ids, and how docs/03 "Implementation (M3-5)" maps the
    /// Effect column's target to <c>appliesTo</c> (attack type key or null; siege 1 / 0 / -1 any).
    /// </summary>
    private static readonly (string Name, string Level1, string Level2, string Target, string? AttackType, int Siege)[] Forge =
    {
        ("Melee Weapons", "melee_weapons_1", "melee_weapons_2", "melee units", "melee", -1),
        ("Ranged Weapons", "ranged_weapons_1", "ranged_weapons_2", "pierce units and towers", "pierce", -1),
        ("Armor", "armor_1", "armor_2", "all non-siege units", null, 0),
    };

    /// <summary>The faction-neutral words a shared description uses for a building slot (it may not name a building).</summary>
    private static readonly Dictionary<BuildingSlot, string> SlotWord = new()
    {
        [BuildingSlot.InfantryHall] = "infantry",
        [BuildingSlot.RangedHall] = "ranged",
        [BuildingSlot.ShockHall] = "mounted",
        [BuildingSlot.Forge] = "upgrade",
        [BuildingSlot.CasterHall] = "caster",
        [BuildingSlot.SiegeWorks] = "siege",
        [BuildingSlot.WatchTower] = "tower",
    };

    private static readonly string[] CountWords = { "zero", "one", "two", "three", "four" };

    /// <summary>"+1 / +2 attack for melee units": the level I amount, the level II total, the stat and the target.</summary>
    private static readonly Regex ForgeEffect = new(@"^\+(?<a1>\d+) / \+(?<a2>\d+) (?<stat>\w+) for (?<target>.+)$");

    /// <summary>"100 G / 50 W, 30 s".</summary>
    private static readonly Regex ForgeCost = new(@"^(?<g>\d+) G / (?<w>\d+) W, (?<s>\d+) s$");

    private static TechDef Tech(string id)
    {
        int t = Data.FindTech(id);
        Assert.True(t >= 0, $"common/techs.json has no tech '{id}'");
        return Data.Techs[t];
    }

    /// <summary>"Infantry Hall" (as docs/02 writes a slot) or null when the text names no slot.</summary>
    private static BuildingSlot? PageSlot(string text) =>
        Enum.TryParse(text.Replace(" ", ""), out BuildingSlot s) && Enum.IsDefined(s) && !char.IsDigit(text[0]) ? s : null;

    /// <summary>The slot as docs/02 writes it: <c>InfantryHall</c> -> "Infantry Hall".</summary>
    private static string SlotName(BuildingSlot s) => Regex.Replace(s.ToString(), "(?<=[a-z])(?=[A-Z])", " ");

    private static void PinCost(string where, Match page, TechDef t)
    {
        Pin(where, "cost", $"{page.Groups["g"].Value} / {page.Groups["w"].Value}", Inv($"{t.CostGold} / {t.CostWood}"));
        Pin(where, "time", page.Groups["s"].Value + " s", Seconds(t.ResearchTicks));
    }

    [Fact]
    public void E_CommonTechs_AreExactlyAgeII_AndTheSixForgeUpgrades_UnderTheirDocNames()
    {
        TechDef[] common = Data.Techs.Where(t => t.Faction == -1).ToArray();
        Pin("common/techs.json", "ids", Join(Shared.Select(s => s.Id)), Join(common.Select(t => t.Key)));
        foreach ((string id, string name) in Shared)
            Pin(id, "name", name, Tech(id).DisplayName);
    }

    [Fact]
    public void F_TheForgeUpgradesTable_MatchesTheSharedTechs()
    {
        string[][] rows = FactionPage.DocTable(Doc02, "Forge upgrades", 3);
        // Upgrade | Effect | Level 1 | Level 2 (Age II): the header puts level 2 behind Age II.
        Assert.Contains("| Upgrade | Effect | Level 1 | Level 2 (Age II) |", FactionPage.DocText(Doc02, "Forge upgrades", 3));
        foreach (var u in Forge)
        {
            string[]? row = rows.SingleOrDefault(r => r[0] == u.Name);
            Assert.True(row != null, $"02 Forge upgrades row: page [{string.Join(", ", rows.Select(r => r[0]))}] vs data {u.Name}");
            Match fx = ForgeEffect.Match(row![1]);
            Assert.True(fx.Success, $"02 Forge upgrades {u.Name} effect: page '{row[1]}' is not '+a / +b <stat> for <units>'");
            Pin($"02 Forge upgrades {u.Name}", "target", u.Target, fx.Groups["target"].Value);
            int a1 = int.Parse(fx.Groups["a1"].Value, CultureInfo.InvariantCulture);
            int a2 = int.Parse(fx.Groups["a2"].Value, CultureInfo.InvariantCulture);
            string stat = fx.Groups["stat"].Value;

            for (int level = 1; level <= 2; level++)
            {
                TechDef t = Tech(level == 1 ? u.Level1 : u.Level2);
                string where = t.Key;
                Match cost = ForgeCost.Match(row[level + 1]);
                Assert.True(cost.Success, $"{where} cost: page '{row[level + 1]}' is not '<g> G / <w> W, <s> s'");
                PinCost(where, cost, t);
                Pin(where, "researched at", BuildingSlot.Forge, t.ResearchedAtSlot);
                Pin(where, "requires any-of", "none", t.RequiresAnyOfCount == 0 ? "none" : Inv($"{t.RequiresAnyOfCount} of {Join(t.RequiresAnyOf)}"));
                string[] pageRequires = level == 1 ? Array.Empty<string>() : new[] { u.Level1, "age_ii" };
                Pin(where, "requires", Join(pageRequires), Join(t.Requires));

                // Each level adds its share of the total (docs/03 M3-5: "+1 / +2" are totals).
                int amount = level == 1 ? a1 : a2 - a1;
                Pin(where, "effect count", 1, t.Effects.Length);
                TechEffect e = t.Effects[0];
                Pin(where, "effect", $"{stat} {Signed(amount)}", $"{DataLimits.TechStatIds[(int)e.Stat]} {Signed(e.Amount)}");
                string attackType = e.AttackType < 0 ? "any" : Data.DamageTable.DamageTypeKeys[e.AttackType];
                Pin(where, "applies to", Inv($"{u.Target}: attackType {u.AttackType ?? "any"}, siege {u.Siege}, no tags or units"),
                    Inv($"{u.Target}: attackType {attackType}, siege {e.Siege}, {(e.Tags.Length + e.Units.Length == 0 ? "no tags or units" : "tags or units set")}"));

                // The description says the amount and the target as the page does, the total on level II, and what it needs.
                string d = t.Description;
                string phrase = $"{Signed(amount)} {stat} for {u.Target}";
                Assert.True(d.Contains(phrase, StringComparison.Ordinal), $"{where} description effect: page '{phrase}' vs data '{d}'");
                if (level == 2)
                    Assert.True(d.Contains($"(+{a2} in all)", StringComparison.Ordinal), $"{where} description total: page '(+{a2} in all)' vs data '{d}'");
                string[] pageNeeds = pageRequires.Select(r => RequiresText.Name(Data, r)).ToArray();
                Pin(where, "description needs", string.Join(", ", pageNeeds.OrderBy(n => n, StringComparer.Ordinal)),
                    string.Join(", ", RequiresText.Needs(d).OrderBy(n => n, StringComparer.Ordinal)));
            }
        }

        // The table's faction upgrade row: Age II only, the same cost and time for every faction's upgrade.
        string[] fu = rows.Single(r => r[0] == "Faction upgrade");
        Pin("02 Forge upgrades Faction upgrade", "level 1", "—", fu[2]);
        Match fc = ForgeCost.Match(fu[3]);
        Assert.True(fc.Success, $"02 Forge upgrades Faction upgrade level 2: page '{fu[3]}'");
        foreach (string faction in Factions)
            foreach (TechDef t in FactionTechs(faction))
            {
                PinCost($"{faction} {t.Key}", fc, t);
                Pin($"{faction} {t.Key}", "requires", "age_ii", Join(t.Requires));
            }
    }

    /// <summary>"Age II researched at the Town Hall: 400 Gold / 200 Wood, 60 s."</summary>
    private static readonly Regex AgeLine = new(@"Age II researched at the (?<at>[A-Za-z ]+): (?<g>\d+) Gold / (?<w>\d+) Wood, (?<s>\d+) s\.");

    /// <summary>
    /// "Requires finished buildings in two different slots (any two of Infantry Hall, Ranged Hall, Shock Hall, Forge):"
    /// (D5: the data's any-of rule, met by distinct slots, docs/03 "Data format" <c>requiresAnyOf</c>).
    /// </summary>
    private static readonly Regex AgeAnyOf = new(@"Requires finished buildings in (?<k>\w+) different slots \(any (?<n>\w+) of (?<of>[^)]+)\):");

    /// <summary>
    /// The any-of rule again in plain words, right after the slot list: "...Forge): two production halls, or one hall and
    /// the Forge." (BUG-0200, folded in from QA's <c>AgesRuleQaTests</c>). The clause must end its bullet: only the next
    /// bullet ("- ...") or the end of the section may follow, so a sentence appended after it that contradicts it ("Or
    /// the Forge alone.") fails (BUG-0230 item 1). It is matched against the clause's own bullet read from the file's lines
    /// (<see cref="AgesClauseBullet"/>), not the space-joined section, so " - Or the Forge alone." written on the same line
    /// is not mistaken for the next bullet (BUG-0260).
    /// </summary>
    private static readonly Regex AgeClause = new(@"\(any \w+ of [^)]+\): (?<n>\w+) production halls, or (?<m>\w+) halls? and the Forge\.$");

    /// <summary>
    /// The docs/02 "### Ages" bullet that states the any-of rule: a bullet starts at a line beginning "- " and runs over
    /// its continuation lines (joined by spaces, trimmed, <c>**</c> removed) to the next such line, blank line or heading.
    /// Exactly one bullet may mention "production halls".
    /// </summary>
    internal static string AgesClauseBullet(string[] lines)
    {
        int i = Array.IndexOf(lines, "### Ages");
        Assert.True(i >= 0, "docs/02 has no '### Ages' heading");
        var bullets = new List<string>();
        for (i++; i < lines.Length && !lines[i].StartsWith('#'); i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0) continue;
            if (lines[i].StartsWith("- ", StringComparison.Ordinal) || bullets.Count == 0) bullets.Add(line);
            else bullets[^1] += " " + line;
        }
        string[] withRule = bullets.Where(b => b.Contains("production halls", StringComparison.Ordinal)).ToArray();
        Assert.True(withRule.Length == 1, $"docs/02 Ages: {withRule.Length} bullets mention 'production halls', expected 1");
        return withRule[0].Replace("**", "");
    }

    /// <summary>
    /// The trailing clause (BUG-0200): with the three production halls and the Forge listed, "any n" means n halls, or
    /// n - 1 halls and the Forge, and the clause ends <paramref name="bullet"/>. A wrong rule sentence ("three production
    /// halls, or the Forge alone") or anything after it in the bullet fails.
    /// </summary>
    private static void CheckAgeClause(string bullet, TechDef t)
    {
        const string where = "age_ii";
        Match clause = AgeClause.Match(bullet);
        Assert.True(clause.Success, $"{where} Ages clause: bullet '{bullet}' does not end with '...): <n> production halls, or <n-1> hall(s) and the Forge.'");
        BuildingSlot[] clauseSlots = { BuildingSlot.InfantryHall, BuildingSlot.RangedHall, BuildingSlot.ShockHall, BuildingSlot.Forge };
        Pin(where, "Ages clause slots (three halls + Forge)", string.Join(", ", clauseSlots.OrderBy(s => s)),
            string.Join(", ", t.RequiresAnyOfSlots.Select(s => (BuildingSlot)s).OrderBy(s => s)));
        Pin(where, "Ages clause halls", clause.Groups["n"].Value, CountWords[t.RequiresAnyOfCount]);
        Pin(where, "Ages clause halls with the Forge", clause.Groups["m"].Value, CountWords[t.RequiresAnyOfCount - 1]);
    }

    private static string[] Doc02Lines() => File.ReadAllLines(Path.Combine(TestDataDir.RepoRoot(), Doc02));

    /// <summary>The rule sentence as docs/02 writes it; the mutants below replace it in memory.</summary>
    private const string AgesRuleSentence = "two production halls, or one hall and the Forge.";

    /// <summary>
    /// G's clause check rejects every wrong or contradicted rule sentence QA found: the eight BUG-0230 mutants and BUG-0260's
    /// " - Or the Forge alone." on the same line, each written into docs/02's own lines in memory.
    /// </summary>
    [Theory]
    [InlineData("two production halls, or one hall and the Forge. Or the Forge alone.")]
    [InlineData("three production halls, or two halls and the Forge.")]
    [InlineData("two production halls, or the Forge alone.")]
    [InlineData("two production halls, or two halls and the Forge.")]
    [InlineData("two Barracks, or one Barracks and the Forge.")]
    [InlineData("one production hall, or one hall and the Forge.")]
    [InlineData("two production halls, or one hall and the Forge, or the Forge alone.")]
    [InlineData("three production halls, or the Forge alone.")]
    [InlineData("two production halls, or one hall and the Forge. - Or the Forge alone.")]
    public void G_AgesClause_RejectsAWrongOrContradictedRuleSentence(string mutant)
    {
        string[] lines = Doc02Lines();
        int at = Array.FindIndex(lines, l => l.EndsWith(AgesRuleSentence, StringComparison.Ordinal));
        Assert.True(at >= 0, $"docs/02: no line ends with '{AgesRuleSentence}'");
        lines[at] = lines[at][..^AgesRuleSentence.Length] + mutant;
        TechDef t = Tech("age_ii");
        // A mutant whose rule sentence no longer mentions "production halls" leaves no clause bullet: also a failure.
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => CheckAgeClause(AgesClauseBullet(lines), t));
    }

    /// <summary>"Age II unlocks: Caster Hall, Siege Works, ..., and the faction upgrade."</summary>
    private static readonly Regex AgeUnlocks = new(@"Age II unlocks: (?<u>[^.]+)\.");

    [Fact]
    public void G_TheAgesSection_MatchesAgeII_AndItsDescription()
    {
        string page = FactionPage.DocText(Doc02, "Ages", 3);
        TechDef t = Tech("age_ii");
        string d = t.Description;
        const string where = "age_ii";

        Match m = AgeLine.Match(page);
        Assert.True(m.Success, $"02 Ages: page '{page}' has no 'Age II researched at the <slot>: <g> Gold / <w> Wood, <s> s.'");
        Pin(where, "researched at", PageSlot(m.Groups["at"].Value), (BuildingSlot?)t.ResearchedAtSlot);
        PinCost(where, m, t);
        Pin(where, "requires", "", Join(t.Requires));

        // Any two of the listed slots: the data's any-of rule, and the description's "Needs two kinds of building: ...".
        Match any = AgeAnyOf.Match(page);
        Assert.True(any.Success, $"02 Ages: page '{page}' has no 'Requires finished buildings in <n> different slots (any <n> of <slots>):'");
        Pin(where, "requires distinct slots", any.Groups["k"].Value, CountWords[t.RequiresAnyOfCount]);
        BuildingSlot[] pageSlots = any.Groups["of"].Value.Split(", ")
            .Select(x => PageSlot(x.Trim()) ?? throw new InvalidOperationException($"02 Ages: '{x}' is no slot")).ToArray();
        Pin(where, "requires any-of count", any.Groups["n"].Value, CountWords[t.RequiresAnyOfCount]);
        Pin(where, "requires any-of slots", string.Join(", ", pageSlots.OrderBy(s => s)),
            string.Join(", ", t.RequiresAnyOfSlots.Select(s => (BuildingSlot)s).OrderBy(s => s)));
        var said = RequiresText.AnyOf(d);
        Assert.True(said != null, $"{where} description any-of: page 'Needs {any.Groups["n"].Value} kinds of building: ...' vs data '{d}'");
        Pin(where, "description any-of count", any.Groups["n"].Value, said!.Value.Count);
        Pin(where, "description any-of kinds", string.Join(", ", pageSlots.Select(s => SlotWord[s]).OrderBy(w => w, StringComparer.Ordinal)),
            string.Join(", ", said.Value.Kinds.OrderBy(w => w, StringComparer.Ordinal)));
        RequiresText.AssertMatches(Data, where, d, t.Requires);

        // The trailing clause (BUG-0200): with the three production halls and the Forge listed, "any n" means n halls, or
        // n - 1 halls and the Forge. A wrong rule sentence ("three production halls, or the Forge alone") fails here, and
        // so does anything after it in its bullet (read from the file's lines, BUG-0260).
        CheckAgeClause(AgesClauseBullet(Doc02Lines()), t);

        // What Age II unlocks, each item checked against the data and said in the description in neutral words.
        Match un = AgeUnlocks.Match(page);
        Assert.True(un.Success, $"02 Ages: page '{page}' has no 'Age II unlocks: ...'");
        var unlockSlots = new List<BuildingSlot>();
        foreach (string raw in un.Groups["u"].Value.Split(", "))
        {
            string item = raw.StartsWith("and ", StringComparison.Ordinal) ? raw[4..] : raw;
            if (PageSlot(item) is BuildingSlot slot)
            {
                unlockSlots.Add(slot);
                Assert.True(d.Contains(SlotWord[slot], StringComparison.Ordinal), $"{where} description unlock: page '{item}' ('{SlotWord[slot]}') vs data '{d}'");
            }
            else if (item == "the unique unit")
            {
                foreach (FactionDef f in Data.Factions)
                    foreach (UnitDef u in Data.Units.Where(x => x.Faction == f.Id && x.Slot == UnitSlot.Unique))
                        Pin($"{f.Key} {u.Key}", "requires", "age_ii", Join(u.Requires));
                Assert.True(d.Contains("unique unit", StringComparison.Ordinal), $"{where} description unlock: page '{item}' vs data '{d}'");
            }
            else if (item == "level II Forge upgrades")
            {
                // Which shared techs need Age II: exactly the three level IIs (F pins each).
                Pin(where, "shared techs needing it", Join(Forge.Select(x => x.Level2)),
                    Join(Data.Techs.Where(x => x.Faction == -1 && x.Requires.Contains("age_ii")).Select(x => x.Key)));
                Assert.True(d.Contains("level II", StringComparison.Ordinal), $"{where} description unlock: page '{item}' vs data '{d}'");
            }
            else if (item == "the faction upgrade")
            {
                foreach (TechDef ft in Data.Techs.Where(x => x.Faction >= 0))
                    Pin(ft.Key, "requires", "age_ii", Join(ft.Requires));
                Assert.True(d.Contains("faction upgrade", StringComparison.Ordinal), $"{where} description unlock: page '{item}' vs data '{d}'");
            }
            else Assert.Fail($"02 Ages: unlock '{item}' has no check here; add one");
        }
        foreach (FactionDef f in Data.Factions)
            Pin($"{f.Key} buildings", "needing Age II", string.Join(", ", unlockSlots.OrderBy(s => s)),
                string.Join(", ", Data.Buildings.Where(b => b.Faction == f.Id && b.Requires.Contains("age_ii")).Select(b => b.Slot).OrderBy(s => s)));
    }

    [Fact]
    public void H_SharedTechText_NamesNoFactionsThing_AndFitsTheCard()
    {
        // A shared tech is read by every faction: the player sees "Cadre Tower", so neither a building's name nor docs/02's
        // slot name ("Caster Hall") may appear, nor any unit, faction or faction tech name ("Moranth Supply"), in any
        // case (BUG-0155: "caster hall" and a faction tech's name used to pass, caught only by the 160-char limit).
        string[] forbidden = Data.Buildings.Select(b => b.DisplayName)
            .Concat(Enum.GetValues<BuildingSlot>().Select(SlotName))
            .Concat(Data.Units.Select(u => u.DisplayName))
            .Concat(Data.Factions.Select(f => f.DisplayName))
            .Concat(Data.Techs.Where(t => t.Faction >= 0).Select(t => t.DisplayName))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach ((string id, _) in Shared)
        {
            TechDef t = Tech(id);
            foreach (string text in new[] { t.DisplayName, t.Description })
                foreach (string name in forbidden)
                    Assert.False(text.Contains(name, StringComparison.OrdinalIgnoreCase), $"{id}: shared text names '{name}' (any case): '{text}'");
            string d = t.Description;
            Assert.True(d.Length <= 160, $"{id}: {d.Length} chars");
            Assert.True(d.Trim() == d && d.EndsWith('.'), $"{id}: '{d}' should be trimmed and end with '.'");
            int sentences = d.Split(". ").Length;
            Assert.True(sentences <= 2, $"{id}: {sentences} sentences");
        }
    }

    [Fact]
    public void I_NoShippedDescription_SaysARequirementOtherThanWithNeeds()
    {
        // BUG-0132: RequiresText.Needs reads only "needs", so a "requires Age II" or "after Age II" would escape every
        // requires check. Shipped text keeps to the one word.
        var texts = Data.Units.Select(u => (u.Key, u.Description))
            .Concat(Data.Buildings.Select(b => (b.Key, b.Description)))
            .Concat(Data.Techs.Select(t => (t.Key, t.Description)));
        foreach ((string key, string d) in texts)
        {
            Match m = RequiresText.OtherWords.Match(d);
            Assert.False(m.Success, $"{key}: description says '{m.Value}'; write requirements as 'needs ...': '{d}'");
        }
    }
}
