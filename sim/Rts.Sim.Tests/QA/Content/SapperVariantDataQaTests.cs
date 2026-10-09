using Rts.Sim.Data;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA D6: the Sapper report's scratch data copies (<c>SapperSplashReportTests</c>) change only the named field. A copy
/// that writes the shipped value back hashes like the shipped data (so the round trip through the temp folder changes
/// nothing else), a copy with the new value differs in the hash and in that one field of that one unit, and a wrong
/// value or a misspelled field fails validation instead of loading silently.
/// </summary>
public class SapperVariantDataQaTests
{
    private const string Sapper = "malazan_sapper";

    private static DataLoadResult Load(string field, string? rawJson)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.SetUnitField("malazan", Sapper, field, rawJson);
        return DataLoader.LoadAll(dir.Path);
    }

    private static GameData LoadOk(string field, string? rawJson)
    {
        DataLoadResult r = Load(field, rawJson);
        Assert.True(r.Ok, string.Join(Environment.NewLine, r.Errors));
        return r.Data!;
    }

    [Theory]
    [InlineData("attack.minRange", "0")]
    [InlineData("attack.splash", "2.0")]
    public void CopyWithTheShippedValue_HashesLikeTheShippedData(string field, string shippedValue)
    {
        Assert.Equal(TestSim.Data.ContentHash(), LoadOk(field, shippedValue).ContentHash());
    }

    [Fact]
    public void MinRange2Copy_ChangesOnlyTheSappersMinRange()
    {
        GameData shipped = TestSim.Data, copy = LoadOk("attack.minRange", "2");
        Assert.NotEqual(shipped.ContentHash(), copy.ContentHash());
        AssertOnlySapperAttackDiffers(shipped, copy, a => a.MinRange, 0f, 2f);
    }

    [Fact]
    public void Splash1Copy_ChangesOnlyTheSappersSplash()
    {
        GameData shipped = TestSim.Data, copy = LoadOk("attack.splash", "1.0");
        Assert.NotEqual(shipped.ContentHash(), copy.ContentHash());
        AssertOnlySapperAttackDiffers(shipped, copy, a => a.Splash, 2f, 1f);
    }

    private static void AssertOnlySapperAttackDiffers(GameData shipped, GameData copy, Func<AttackDef, float> field, float was, float now)
    {
        Assert.Equal(shipped.Units.Length, copy.Units.Length);
        int sapper = shipped.FindUnit(Sapper);
        Assert.Equal(sapper, copy.FindUnit(Sapper));
        Assert.Equal(was, field(shipped.Units[sapper].Attack));
        Assert.Equal(now, field(copy.Units[sapper].Attack));
        for (int i = 0; i < shipped.Units.Length; i++)
        {
            AttackDef a = shipped.Units[i].Attack, b = copy.Units[i].Attack;
            Assert.Equal(shipped.Units[i].Id, copy.Units[i].Id);
            Assert.Equal((a.Value, a.DamageType, a.CooldownTicks, a.WindupTicks, a.Range, a.FriendlyFire, a.ProjectileTypeId, a.Targets),
                (b.Value, b.DamageType, b.CooldownTicks, b.WindupTicks, b.Range, b.FriendlyFire, b.ProjectileTypeId, b.Targets));
            Assert.Equal(a.BonusVs.ToArray(), b.BonusVs.ToArray());
            if (i == sapper) continue;
            Assert.Equal((a.MinRange, a.Splash), (b.MinRange, b.Splash));
        }
    }

    [Theory]
    [InlineData("attack.minRange", "-1", "minRange")]
    [InlineData("attack.minRange", "\"2\"", "minRange")]
    [InlineData("attack.minRang", "2", "minRang")]
    [InlineData("attack.splash", "-1.0", "splash")]
    [InlineData("attack.splash", "\"1\"", "splash")]
    public void WrongCopy_FailsValidationNamingTheField(string field, string rawJson, string named)
    {
        DataLoadResult r = Load(field, rawJson);
        Assert.False(r.Ok, $"{field} = {rawJson} loaded");
        string all = string.Join(Environment.NewLine, r.Errors);
        Assert.Contains(named, all);
    }
}
