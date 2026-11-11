using Rts.Sim.Data;
using Rts.Sim.Tests.Content;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// D-R36 QA (2026-11-10): teblor.md is written ahead of its data, so the content pins can't compare it yet. These run the
/// pins' own parsers (not <see cref="PagePinReadinessTests"/>' re-implementation) over teblor.md with each Teblor name and
/// id swapped for the shipped Malazan one of the same slot, against the shipped Malazan data. The two factions share the
/// template's shared rules (counters by slot, the far-expansion knobs, the difficulty rows), so a Teblor sentence the pin
/// can't read, or a number in it the pin doesn't read, fails here now rather than in D28.
/// </summary>
public class TeblorPageParseQaTests
{
    private static readonly (UnitSlot Slot, string Name)[] TeblorUnits =
    {
        (UnitSlot.Worker, "Gatherer"), (UnitSlot.Line, "Warrior"), (UnitSlot.Ranged, "Javelin Thrower"),
        (UnitSlot.Shock, "Destrier Rider"), (UnitSlot.Caster, "Shaman"), (UnitSlot.Siege, "Breaker"), (UnitSlot.Unique, "War-dog"),
    };

    private static readonly (BuildingSlot Slot, string Name, string Id)[] TeblorBuildings =
    {
        (BuildingSlot.TownHall, "Longhouse", "teblor_longhouse"), (BuildingSlot.House, "Hut", "teblor_hut"),
        (BuildingSlot.Camp, "Woodcutters' Lodge", "teblor_woodcutters_lodge"), (BuildingSlot.InfantryHall, "War Lodge", "teblor_war_lodge"),
        (BuildingSlot.RangedHall, "Javelin Ground", "teblor_javelin_ground"), (BuildingSlot.ShockHall, "Stables & Kennels", "teblor_stables"),
        (BuildingSlot.Forge, "Bloodwood Carver", "teblor_bloodwood_carver"), (BuildingSlot.CasterHall, "Shaman's Circle", "teblor_shamans_circle"),
        (BuildingSlot.SiegeWorks, "Breaker's Hall", "teblor_breakers_hall"), (BuildingSlot.WatchTower, "Stone Cairn", "teblor_stone_cairn"),
    };

    /// <summary>teblor.md with every building name / id, then unit name, replaced by Malazan's of the same slot (longest first).</summary>
    private static string[] AsMalazan()
    {
        GameData d = TestSim.Data;
        int m = d.FindFaction("malazan");
        var swaps = new List<(string From, string To)>();
        foreach ((BuildingSlot slot, string name, string id) in TeblorBuildings)
        {
            BuildingDef b = d.Buildings.First(x => x.Faction == m && x.Slot == slot);
            swaps.Add((id, b.Key));
            swaps.Add((name, b.DisplayName));
        }
        foreach ((UnitSlot slot, string name) in TeblorUnits)
            swaps.Add((name, d.Units.First(u => u.Faction == m && u.Slot == slot).DisplayName));
        swaps = swaps.OrderByDescending(s => s.From.Length).ToList();
        return PageTables.FactionLines("teblor").Select(l =>
        {
            foreach ((string from, string to) in swaps) l = l.Replace(from, to, StringComparison.Ordinal);
            return l;
        }).ToArray();
    }

    [Fact]
    public void HowTheAiAdapts_ParsesWithThePinsOwnPatterns()
    {
        List<string> problems = AiAdaptsContentTests.Problems(TestSim.Data, "malazan", AsMalazan());
        Assert.True(problems.Count == 0, "teblor.md 'How the AI adapts' (names swapped to Malazan's):\n" + string.Join("\n", problems));
    }

    [Fact]
    public void Difficulty_ParsesWithThePinsOwnPatterns()
    {
        List<string> problems = AiContentTests.DifficultyProblems(TestSim.Data, "malazan", AsMalazan());
        Assert.True(problems.Count == 0, "teblor.md 'Difficulty':\n" + string.Join("\n", problems));
    }

    /// <summary>Not vacuous: a stray number in the Teblor sub-section is caught by the pin's page-to-data guard.</summary>
    [Fact]
    public void HowTheAiAdapts_AStrayNumber_IsCaught()
    {
        string[] lines = AsMalazan();
        int i = Array.FindIndex(lines, l => l.Contains("One at a time.", StringComparison.Ordinal));
        Assert.True(i >= 0);
        lines[i] = lines[i].Replace("One at a time.", "One at a time, 3 times.", StringComparison.Ordinal);
        Assert.Contains(AiAdaptsContentTests.Problems(TestSim.Data, "malazan", lines), p => p.Contains("the number 3", StringComparison.Ordinal));
    }
}
