using System.Text.Json.Nodes;
using Rts.Sim.Data;

namespace Rts.Sim.Tests;

/// <summary>
/// M6-S9 criterion 1: <c>rules.json</c> <c>gatherOverlap</c> loads (absent and 0 as 0, a value up to
/// <see cref="DataLimits.MaxGatherOverlap"/> as itself), every bad value is one error naming the file and the field, and the
/// value enters <see cref="GameData.ContentHash"/> only when non-zero (the shipped hash pins stay green unedited).
/// Every copy writes or removes the field itself, so these hold whatever the shipped file carries (rule 58).
/// </summary>
public class GatherOverlapLoaderTests
{
    /// <summary>The shipped data with <c>gatherOverlap</c> set to <paramref name="raw"/> (raw JSON), or removed for null.</summary>
    private static DataLoadResult Load(string? raw)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("common/rules.json", r =>
        {
            if (raw == null) r.Remove("gatherOverlap");
            else r["gatherOverlap"] = JsonNode.Parse(raw);
        });
        return DataLoader.LoadAll(dir.Path);
    }

    /// <summary>The shipped data with the rules file's text rewritten by <paramref name="edit"/> (for a token JSON can't hold, like NaN).</summary>
    private static DataLoadResult LoadText(Func<string, string> edit)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("common/rules.json", r => r.Remove("gatherOverlap"));
        string path = dir.FullPath("common/rules.json");
        File.WriteAllText(path, edit(File.ReadAllText(path)));
        return DataLoader.LoadAll(dir.Path);
    }

    private static GameData Ok(DataLoadResult r)
    {
        Assert.True(r.Data != null, string.Join("; ", r.Errors));
        return r.Data!;
    }

    [Theory]
    [InlineData(null, 0f)]
    [InlineData("0", 0f)]
    [InlineData("0.0", 0f)]
    [InlineData("0.25", 0.25f)]
    [InlineData("0.5", 0.5f)]
    [InlineData("0.75", 0.75f)] // the cap is allowed
    [InlineData("0.001", 0.001f)]
    public void AValidValue_Loads(string? raw, float expected)
    {
        GameData d = Ok(Load(raw));
        Assert.Equal(expected, d.Rules.GatherOverlap);
    }

    [Fact]
    public void TheCap_IsBelowOne_AndAtLeastTheLargestMeasuredValue()
    {
        Assert.True(DataLimits.MaxGatherOverlap < 1.0);
        Assert.True(DataLimits.MaxGatherOverlap >= 0.75);
    }

    [Theory]
    [InlineData("null", "null is not a number")]
    [InlineData("\"0.5\"", "expected a number")]
    [InlineData("\"NaN\"", "expected a number")]
    [InlineData("true", "expected a number")]
    [InlineData("{}", "expected a number")]
    [InlineData("[0.5]", "expected a number")]
    [InlineData("-0.1", "must not be negative")]
    [InlineData("-1", "must not be negative")]
    [InlineData("0.7500001", "above the maximum 0.75")]
    [InlineData("0.76", "above the maximum 0.75")]
    [InlineData("1", "above the maximum 0.75")]
    [InlineData("1e400", "above the maximum 0.75")]
    public void ABadValue_IsExactlyOneError_NamingTheFileAndTheField(string raw, string message)
    {
        DataLoadResult r = Load(raw);
        Assert.Null(r.Data);
        DataError e = Assert.Single(r.Errors);
        Assert.Equal("common/rules.json", e.File);
        Assert.Equal("gatherOverlap", e.Path);
        Assert.Contains(message, e.Message);
    }

    /// <summary>NaN is no JSON number: the file is malformed there, one error naming the file and the field's path.</summary>
    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    public void ANonFiniteToken_IsOneError_NamingTheFileAndTheField(string token)
    {
        DataLoadResult r = LoadText(t => t.Replace("\"startingWorkers\"", $"\"gatherOverlap\": {token}, \"startingWorkers\""));
        Assert.Null(r.Data);
        DataError e = Assert.Single(r.Errors);
        Assert.Equal("common/rules.json", e.File);
        Assert.Contains("gatherOverlap", e.Path + " " + e.Message);
    }

    [Fact]
    public void TheHash_IsTheSameAbsentAndAtZero_AndMovesWithEachNonZeroValue()
    {
        ulong absent = Ok(Load(null)).ContentHash();
        Assert.Equal(absent, Ok(Load("0")).ContentHash());
        ulong quarter = Ok(Load("0.25")).ContentHash(), half = Ok(Load("0.5")).ContentHash();
        Assert.NotEqual(absent, quarter);
        Assert.NotEqual(absent, half);
        Assert.NotEqual(quarter, half);
    }

    /// <summary>With the field absent from the shipped file, a copy without it hashes as the shipped data (the golden's data hash holds).</summary>
    [Fact]
    public void WhileTheShippedFileOmitsTheField_ACopyWithoutItHashesAsTheShippedData()
    {
        if (TestSim.Data.Rules.GatherOverlap != 0f) return; // the value has shipped: its own hash move, not this test's subject
        Assert.Equal(TestSim.Data.ContentHash(), Ok(Load(null)).ContentHash());
    }

    /// <summary>The world reads the knob: off when 0, the scale 1 - v when on.</summary>
    [Fact]
    public void TheWorld_DerivesOnAndTheScale_FromTheData()
    {
        World off = new(new SimConfig(1, 1, 4, 16) { Data = Ok(Load(null)) });
        Assert.False(off.GatherOverlapOn);
        Assert.Equal(1f, off.GatherPairScale);
        World on = new(new SimConfig(1, 1, 4, 16) { Data = Ok(Load("0.25")) });
        Assert.True(on.GatherOverlapOn);
        Assert.Equal(0.75f, on.GatherPairScale);
    }
}
