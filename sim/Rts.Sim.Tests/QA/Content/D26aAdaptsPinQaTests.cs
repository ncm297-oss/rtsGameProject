using System.Text.Json.Nodes;
using Rts.Sim.Data;
using Rts.Sim.Tests.Content;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA (D26a, 2026-11-09-1336): the "How the AI adapts" pin (<see cref="AiAdaptsContentTests.Problems"/>) against edits the
/// developer's own rows don't make: every hall, worker, drop-off id, Whirlwind multiplier, Age I name and the extra-slot row
/// on the page side, and on the data side a Line answer added to an Age I mix, a counters row the page doesn't list, and the
/// drop-off losing its gold-free cost. Each edit must read red with a message that names what moved.
/// </summary>
public class D26aAdaptsPinQaTests
{
    [Theory]
    [InlineData("malazan", "×1.5 on Normal", "×1.4 on Normal", "malazan Normal's multiplier: page ×1.4 vs data ×1.5")]
    [InlineData("whirlwind", "Raiders ×1.5", "Raiders ×1.6", "whirlwind Normal's multiplier: page ×1.6 vs data ×1.5")]
    [InlineData("whirlwind", "its Raiders", "its Horse Raiders", "whirlwind the cavalry answer: page 'Horse Raiders' vs data 'Raider'")]
    [InlineData("malazan", "(`malazan_depot`", "(`malazan_supply`", "malazan the far drop-off: page 'malazan_supply' vs data 'malazan_depot'")]
    [InlineData("malazan", "0 gold / 75", "10 gold / 75", "malazan the far drop-off: page '10' vs data '0'")]
    [InlineData("whirlwind", "of its Holy Camp", "of its Raider Camp", "whirlwind ai.farExpandReach: page 'Raider Camp' vs data 'Holy Camp'")]
    [InlineData("malazan", "a Garrison Keep only", "a Legion Barracks only", "malazan the hall fallback: page 'Legion Barracks' vs data 'Garrison Keep'")]
    [InlineData("whirlwind", "no Camp Follower on gold", "no Laborer on gold", "whirlwind the leg's worker: page 'Laborer' vs data 'Camp Follower'")]
    [InlineData("whirlwind", "lists a Priest of the Whirlwind or a Battering Ram", "lists a Priest of the Whirlwind or a Raider", "whirlwind Age I's Line answers: page 'Priest of the Whirlwind, Raider'")]
    [InlineData("whirlwind", "enemy Siege → Raider, Horse Raider;", "enemy Siege → Raider, Horse Raider; enemy Worker → Horse Raider;", "whirlwind What answers what: page slots")]
    [InlineData("malazan", "A raid on its base resets the clock.", "A raid on its base resets the clock (tick 2400).", "malazan: the number 2400 ")]
    public void APageEdit_TheDevRowsDontMake_IsCaught(string faction, string from, string to, string expected)
    {
        string page = string.Join("\n", PageTables.FactionLines(faction));
        Assert.Contains(from, page, StringComparison.Ordinal);
        string[] edited = page.Replace(from, to, StringComparison.Ordinal).Split('\n');
        List<string> problems = AiAdaptsContentTests.Problems(TestSim.Data, faction, edited);
        Assert.True(problems.Any(p => p.StartsWith(expected, StringComparison.Ordinal)), $"expected '{expected}', got:\n{string.Join("\n", problems)}");
    }

    /// <summary>A Line answer (Catapult) added to an Age I mix breaks the page's BUG-1156 Age I sentence.</summary>
    [Fact]
    public void ALineAnswer_InAnAgeOneMix_FailsTheShippedPage()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("factions/malazan/ai.json", root =>
        {
            JsonObject opening = root["openings"]!.AsArray().Select(n => n!.AsObject()).Single(o => (string)o["id"]! == "cadre_rush");
            opening["compositions"]![0]!["units"]!.AsObject()["malazan_catapult"] = 5;
        });
        AssertCaught(dir, "malazan", "malazan cadre_rush: the Age I mix lists Catapult, which answers an enemy Line");
    }

    /// <summary>A counters row for Unique that the page writes as "none".</summary>
    [Fact]
    public void AUniqueCounter_FailsTheShippedPage()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("common/rules.json", root => root["ai"]!["counters"]!.AsObject()["unique"] = JsonNode.Parse("""["line"]"""));
        // D-R35 (BUG-1181 nit 4): the page's "none" row prints as 'none' (it printed '' before).
        AssertCaught(dir, "whirlwind", "whirlwind enemy Unique: page 'none' vs data (ai.counters) 'Raider'");
    }

    /// <summary>The page's depot given a gold cost: the faction then has no gold-free drop-off at all.</summary>
    [Fact]
    public void TheDropOff_CostingGold_FailsTheShippedPage()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("factions/malazan/buildings.json", root =>
            root["buildings"]!.AsArray().Select(n => n!.AsObject()).Single(b => (string)b["id"]! == "malazan_depot")["cost"] =
                JsonNode.Parse("""{ "gold": 10, "wood": 75 }"""));
        AssertCaught(dir, "malazan", "malazan: the data has no gold-free drop-off");
    }

    private static void AssertCaught(TestDataDir dir, string faction, string expected)
    {
        DataLoadResult result = DataLoader.LoadAll(dir.Path);
        Assert.True(result.Ok, string.Join("\n", result.Errors));
        List<string> problems = AiAdaptsContentTests.Problems(result.Data!, faction, PageTables.FactionLines(faction));
        Assert.True(problems.Any(p => p.StartsWith(expected, StringComparison.Ordinal)), $"expected '{expected}', got:\n{string.Join("\n", problems)}");
    }
}
