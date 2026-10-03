using Rts.Sim.Data;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>QA follow-up on the BUG-0007 / BUG-0009 fixes: exact limits, error paths and messages.</summary>
public class DataLoaderBoundsQaTests
{
    private const string MalazanUnits = "factions/malazan/units.json";
    private const string Crossbow = "malazan_crossbowman"; // units[2] in the shipped Malazan file
    private readonly ITestOutputHelper _out;

    public DataLoaderBoundsQaTests(ITestOutputHelper output) => _out = output;

    private DataLoadResult Load(TestDataDir dir)
    {
        DataLoadResult? r = null;
        Exception? ex = Record.Exception(() => r = DataLoader.LoadAll(dir.Path));
        Assert.True(ex == null, $"LoadAll threw {ex}");
        foreach (DataError e in r!.Errors) _out.WriteLine(e.ToString());
        return r;
    }

    [Theory]
    [InlineData("hp", "1000000", true)]
    [InlineData("hp", "1000001", false)]
    [InlineData("armor", "1000001", false)]
    [InlineData("cost.gold", "1000000", true)]
    [InlineData("cost.gold", "1000001", false)]
    [InlineData("speed", "1000000", true)]
    [InlineData("speed", "1000000.5", false)]
    [InlineData("sight", "1e7", false)]
    [InlineData("attack.range", "1000000", true)]
    [InlineData("attack.range", "1000000.0001", false)]
    [InlineData("trainTime", "3600", true)]
    [InlineData("trainTime", "3600.05", false)]
    [InlineData("trainTime", "999999", false)]
    [InlineData("attack.cooldown", "3600", true)]
    [InlineData("attack.cooldown", "3601", false)]
    [InlineData("attack.windup", "0", true)]
    [InlineData("attack.windup", "3601", false)]
    [InlineData("pop", "1000000", true)]
    [InlineData("pop", "1000000.5", false)]
    public void UnitNumber_AtAndJustAboveTheLimit(string field, string raw, bool ok)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.SetUnitField("malazan", Crossbow, field, raw);
        DataLoadResult r = Load(dir);
        Assert.Equal(ok, r.Ok);
        if (ok)
        {
            Assert.NotNull(r.Data);
            return;
        }
        DataError e = Assert.Single(r.Errors);
        Assert.Equal(MalazanUnits, e.File);
        Assert.Equal("units[2]." + field, e.Path);
        Assert.DoesNotContain("Infinity", e.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("∞", e.Message);
    }

    [Theory]
    [InlineData(1_000_000, true)]
    [InlineData(1_000_001, false)]
    [InlineData(int.MaxValue, false)]
    public void PopCap_Limit_AndMessageShowsTheRealNumber(int popCap, bool ok)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("common/rules.json", root => root["popCap"] = popCap);
        DataLoadResult r = Load(dir);
        Assert.Equal(ok, r.Ok);
        if (ok)
        {
            Assert.Equal(2 * popCap, r.Data!.Rules.HalfPopCap);
            return;
        }
        DataError e = Assert.Single(r.Errors);
        Assert.Equal("popCap", e.Path);
        Assert.Contains(popCap.ToString(System.Globalization.CultureInfo.InvariantCulture), e.Message);
        Assert.DoesNotContain("-", e.Message);
    }

    [Fact]
    public void RequiresAndTagsEntries_ReportTheirOwnIndexedPath()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.SetUnitField("malazan", Crossbow, "requires", "[\"malazan_barracks\", null]");
        dir.SetUnitField("malazan", Crossbow, "tags", "[\"ranged\", \"Bad Tag\", \"\"]");
        DataLoadResult r = Load(dir);
        Assert.False(r.Ok);
        Assert.Contains(r.Errors, e => e.File == MalazanUnits && e.Path == "units[2].requires[1]");
        Assert.Contains(r.Errors, e => e.File == MalazanUnits && e.Path == "units[2].tags[1]");
        Assert.Contains(r.Errors, e => e.File == MalazanUnits && e.Path == "units[2].tags[2]");
        Assert.DoesNotContain(r.Errors, e => e.Path == "units[2].requires[0]" || e.Path == "units[2].tags[0]");
    }

    [Fact]
    public void ValidRequiresAndTags_AreKeptInOrder()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.SetUnitField("malazan", Crossbow, "requires", "[\"b_req\", \"a_req\"]");
        dir.SetUnitField("malazan", Crossbow, "tags", "[\"zz\", \"aa\"]");
        DataLoadResult r = Load(dir);
        Assert.True(r.Ok);
        UnitDef u = r.Data!.Units[r.Data.FindUnit(Crossbow)];
        Assert.Equal(new[] { "b_req", "a_req" }, u.Requires.ToArray());
        Assert.Equal(new[] { "zz", "aa" }, u.Tags.ToArray());
    }
}
