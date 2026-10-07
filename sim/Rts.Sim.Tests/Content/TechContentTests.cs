using System.Globalization;
using System.Text.Json.Nodes;
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

    [Fact]
    public void A_EachFaction_ShipsExactlyItsFactionUpgrade_AsTheDocsSay()
    {
        foreach (string faction in Factions)
        {
            var u = Upgrade[faction];
            Assert.Equal(new[] { u.Id }, FileList(faction).Select(n => (string)n!["id"]!));
            TechDef t = Assert.Single(FactionTechs(faction));
            Assert.Equal((u.Id, u.Name), (t.Key, t.DisplayName));
            Assert.Equal((BuildingSlot.Forge, u.Gold, u.Wood, u.Seconds * SimConstants.TicksPerSecond), (t.ResearchedAtSlot, t.CostGold, t.CostWood, t.ResearchTicks));
            Assert.Equal(u.Requires, t.Requires.ToArray());
            Assert.Equal(u.Effects.Length, t.Effects.Length);
            for (int i = 0; i < u.Effects.Length; i++)
            {
                (string stat, double amount, string unit) = u.Effects[i];
                TechEffect e = t.Effects[i];
                double sim = stat == "abilityCooldown" ? amount * SimConstants.TicksPerSecond : amount;
                Assert.True((stat, (float)sim) == (DataLimits.TechStatIds[(int)e.Stat], e.Amount), $"{u.Id} effect {i}");
                Assert.Equal(new[] { Data.FindUnit(unit) }, e.Units.ToArray());
                Assert.True((e.AttackType, e.Siege, e.Tags.Length) == (-1, -1, 0), $"{u.Id} effect {i} filters");
            }

            // The file holds seconds and the slot id, not ticks.
            JsonNode f = FileList(faction)[0]!;
            Assert.Equal(("forge", (double)u.Seconds), ((string)f["researchedAt"]!, (double)f["researchTime"]!));
            Assert.Equal(u.Effects.Select(e => e.Amount), f["effects"]!.AsArray().Select(n => (double)n!["amount"]!));
        }
    }

    [Fact]
    public void B_ThePagesFactionUpgradeLine_MatchesTheData()
    {
        foreach (string faction in Factions)
        {
            TechDef t = Assert.Single(FactionTechs(faction));
            string line = FactionPage.Paragraph(faction, "Faction upgrade: " + t.DisplayName);
            string head = $"{RequiresText.Cell(Data, t.Requires)}, at the {ForgeName(faction)}, {t.CostGold} G / {t.CostWood} W, {Num((double)t.ResearchTicks / SimConstants.TicksPerSecond)} s. ";
            Assert.True(line.StartsWith(head, StringComparison.Ordinal), $"{faction}.md faction upgrade line '{line}' should start '{head}'");
            foreach (TechEffect e in t.Effects)
            {
                string unit = UnitName(e);
                string phrase = PagePhrase(faction, e, unit, out string ability);
                Assert.True(line.Contains(phrase, StringComparison.Ordinal), $"{faction}.md faction upgrade line lacks '{phrase}'");
                // The unit is named, or (for a cooldown) its ability, which the Abilities table ties to the unit.
                Assert.True(line.Contains(unit, StringComparison.Ordinal) || (ability.Length > 0 && line.Contains(ability, StringComparison.Ordinal)), $"{faction}.md faction upgrade line names neither {unit} nor its ability");
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
            Assert.Equal(techs.Length, rows.Length);
            int f = Data.FindFaction(faction);
            foreach (string[] c in rows)
            {
                // Id | Name | Researched at | Cost (G/W) | Time (s) | Requires | Effects
                Assert.Equal(7, c.Length);
                TechDef t = techs.Single(x => x.Key == c[0]);
                string where = $"{faction}.md Techs row {c[0]}";
                Assert.True(t.DisplayName == c[1], where + " name");
                Assert.True(Data.Buildings.Single(b => b.Faction == f && b.Slot == t.ResearchedAtSlot).DisplayName == c[2], where + " researched at");
                Assert.True((t.CostGold, t.CostWood) == FactionPage.Cost(c[3]), where + " cost");
                Assert.True(t.ResearchTicks == FactionPage.Num(c[4]) * SimConstants.TicksPerSecond, where + " time");
                Assert.True(RequiresText.Cell(Data, t.Requires) == c[5], where + $" requires '{c[5]}'");
                string effects = string.Join("; ", t.Effects.Select(e => UnitName(e) + " " + PagePhrase(faction, e, UnitName(e), out _)));
                Assert.True(effects == c[6], where + $" effects '{c[6]}', expected '{effects}'");
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
}
