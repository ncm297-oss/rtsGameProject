using Rts.Sim.Data;

namespace Rts.Sim.Tests;

/// <summary>M4-2a: the <c>attack.targets</c> field (<c>units</c> / <c>buildings</c> / <c>all</c>, default <c>all</c>).</summary>
public class AttackTargetsLoaderTests
{
    private const string Faction = "malazan", Unit = "malazan_heavy_infantry";

    private static GameData LoadWith(string? rawTargets)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.SetUnitField(Faction, Unit, "attack.targets", rawTargets);
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        return r.Data!;
    }

    [Fact]
    public void Absent_IsAll()
    {
        GameData d = LoadWith(null);
        Assert.Equal(AttackTargets.All, d.Units[d.FindUnit(Unit)].Attack.Targets);
    }

    [Theory]
    [InlineData("\"all\"", AttackTargets.All)]
    [InlineData("\"units\"", AttackTargets.Units)]
    [InlineData("\"buildings\"", AttackTargets.Buildings)]
    public void EachValue_Loads(string raw, AttackTargets expected)
    {
        GameData d = LoadWith(raw);
        Assert.Equal(expected, d.Units[d.FindUnit(Unit)].Attack.Targets);
    }

    [Theory]
    [InlineData("\"Buildings\"")]
    [InlineData("\"structures\"")]
    [InlineData("\"\"")]
    [InlineData("3")]
    public void ABadValue_IsADataErrorAtTheField(string raw)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.SetUnitField(Faction, Unit, "attack.targets", raw);
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.False(r.Ok);
        Assert.Null(r.Data);
        Assert.Contains(r.Errors, e => e.ToString().Contains("attack.targets"));
    }

    [Fact]
    public void ShippedData_TheRamAttacksBuildingsOnly_EveryOtherUnitAll()
    {
        GameData d = TestSim.Data;
        foreach (UnitDef u in d.Units)
            Assert.Equal(u.Key == "whirlwind_battering_ram" ? AttackTargets.Buildings : AttackTargets.All, u.Attack.Targets);
    }

    [Fact]
    public void TheField_IsInTheContentHash()
    {
        GameData all = LoadWith("\"all\""), units = LoadWith("\"units\"");
        Assert.NotEqual(all.ContentHash(), units.ContentHash());
    }
}
