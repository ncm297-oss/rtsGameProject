using System.Text.RegularExpressions;
using Rts.Sim.Data;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA (D3): roster-level rules for the shipped techs that hold for any faction, not just the two in the slice:
/// one faction upgrade per faction at its Forge, snake_case ids, <c>requires</c> lists that stay inside the faction
/// (own buildings or common techs), and faction upgrades that only touch and only name their own faction.
/// </summary>
public class TechRosterQaTests
{
    private static readonly Regex SnakeCase = new("^[a-z][a-z0-9]*(_[a-z0-9]+)*$");

    private static GameData Data => TestSim.Data;

    [Fact]
    public void EveryFaction_HasExactlyOneFactionUpgrade_AtTheForge_NeedingAgeII()
    {
        Assert.NotEmpty(Data.Factions);
        foreach (FactionDef f in Data.Factions)
        {
            TechDef[] own = Data.Techs.Where(t => t.Faction == f.Id).ToArray();
            TechDef t = Assert.Single(own);
            Assert.Equal(BuildingSlot.Forge, t.ResearchedAtSlot);
            Assert.Contains("age_ii", t.Requires);
            Assert.NotEmpty(t.Effects);
        }
    }

    [Fact]
    public void EveryTechId_IsSnakeCase_AndUnique()
    {
        foreach (TechDef t in Data.Techs)
            Assert.True(SnakeCase.IsMatch(t.Key), $"tech id '{t.Key}' is not snake_case");
        Assert.Equal(Data.Techs.Length, Data.Techs.Select(t => t.Key).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void EveryRequiresEntry_IsACommonTech_OrAnOwnFactionBuildingOrTech()
    {
        foreach (TechDef t in Data.Techs)
            foreach (string r in t.Requires) AssertOwnOrCommon(t.Key, t.Faction, r);
        foreach (BuildingDef b in Data.Buildings)
            foreach (string r in b.Requires) AssertOwnOrCommon(b.Key, b.Faction, r);
        foreach (UnitDef u in Data.Units)
            foreach (string r in u.Requires) AssertOwnOrCommon(u.Key, u.Faction, r);
    }

    private static void AssertOwnOrCommon(string who, int faction, string id)
    {
        int tech = Data.FindTech(id);
        int building = Data.FindBuilding(id);
        Assert.True(tech >= 0 || building >= 0, $"{who} requires unknown '{id}'");
        if (tech >= 0)
            Assert.True(Data.Techs[tech].Faction == -1 || Data.Techs[tech].Faction == faction, $"{who} requires another faction's tech {id}");
        else if (faction == -1)
            Assert.Fail($"common {who} requires the faction building {id}"); // every faction must be able to research it
        else
            Assert.True(Data.Buildings[building].Faction == faction, $"{who} requires another faction's building {id}");
    }

    [Fact]
    public void ABuildingNeverRequiresItself_NorABuildingThatItselfNeedsAgeII()
    {
        foreach (BuildingDef b in Data.Buildings)
        {
            Assert.DoesNotContain(b.Key, b.Requires);
            foreach (string r in b.Requires)
            {
                int other = Data.FindBuilding(r);
                // A building that needs Age II isn't itself something an Age I building may need.
                if (other >= 0) Assert.DoesNotContain("age_ii", Data.Buildings[other].Requires);
            }
        }
    }

    [Fact]
    public void FactionUpgrades_OnlyAffectAndOnlyNameTheirOwnFaction()
    {
        foreach (TechDef t in Data.Techs.Where(t => t.Faction >= 0))
        {
            foreach (TechEffect e in t.Effects)
            {
                Assert.NotEmpty(e.Units);
                foreach (int u in e.Units)
                    Assert.True(Data.Units[u].Faction == t.Faction, $"{t.Key} affects another faction's {Data.Units[u].Key}");
            }
            foreach (string text in new[] { t.DisplayName, t.Description })
            {
                foreach (UnitDef u in Data.Units)
                    if (u.Faction != t.Faction) Assert.False(text.Contains(u.DisplayName, StringComparison.Ordinal), $"{t.Key} names {u.DisplayName}");
                foreach (BuildingDef b in Data.Buildings)
                    if (b.Faction != t.Faction) Assert.False(text.Contains(b.DisplayName, StringComparison.Ordinal), $"{t.Key} names {b.DisplayName}");
                foreach (FactionDef f in Data.Factions)
                    if (f.Id != t.Faction) Assert.False(text.Contains(f.DisplayName, StringComparison.Ordinal), $"{t.Key} names {f.DisplayName}");
            }
        }
    }

    [Fact]
    public void FactionUpgradeNames_AreDistinctFromEveryOtherPlayerFacingName()
    {
        string[] others = Data.Units.Select(u => u.DisplayName).Concat(Data.Buildings.Select(b => b.DisplayName))
            .Concat(Data.Techs.Where(t => t.Faction == -1).Select(t => t.DisplayName)).ToArray();
        foreach (TechDef t in Data.Techs.Where(t => t.Faction >= 0))
            Assert.DoesNotContain(t.DisplayName, others);
    }
}
