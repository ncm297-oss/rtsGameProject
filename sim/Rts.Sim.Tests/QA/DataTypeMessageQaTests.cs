using Rts.Sim.Data;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M4-H1 (session 2026-10-08-2144), BUG-0113 item 1's fix: a value of the wrong type in a well-formed file names what
/// the field wants. Hostile cases on unit fields of every JSON shape the schema uses: an object, a map of numbers
/// (<c>attack.bonusVs</c>), a list of strings, a string, a number, a bool. Each load fails with one error at the field
/// that names the expected kind and no CLR type.
/// </summary>
public class DataTypeMessageQaTests
{
    private readonly ITestOutputHelper _out;

    public DataTypeMessageQaTests(ITestOutputHelper output) => _out = output;

    private const string Unit = "malazan_heavy_infantry";

    private DataError LoadWith(string field, string raw)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.SetUnitField("malazan", Unit, field, raw);
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.Null(r.Data);
        foreach (DataError e in r.Errors) _out.WriteLine($"{e.Path}: {e.Message}");
        return Assert.Single(r.Errors);
    }

    [Theory]
    [InlineData("tags", "\"worker\"", "a list")]
    [InlineData("requires", "7", "a list")]
    [InlineData("attack", "12", "an object")]
    [InlineData("attack", "[1, 2]", "an object")]
    [InlineData("hp", "true", "a whole number")]
    [InlineData("displayName", "5", "a string")]
    [InlineData("attack.bonusVs.mounted", "\"high\"", "a number")]
    public void WrongTypedValue_NamesWhatTheFieldWants(string field, string raw, string expected)
    {
        DataError e = LoadWith(field, raw);
        Assert.Contains($"expected {expected}", e.Message);
        Assert.DoesNotContain("System.", e.Message);
    }

    /// <summary>A map of numbers given a number or a list: the field wants an object (a name-to-number map), not a number. BUG-0242.</summary>
    [Theory(Skip = "BUG-0242: a map-of-numbers field given the wrong type says 'expected a number', not 'an object'")]
    [InlineData("attack.bonusVs", "1.5")]
    [InlineData("attack.bonusVs", "[1.5]")]
    public void MapOfNumbersField_GivenANumberOrAList_SaysExpectedAnObject(string field, string raw)
    {
        DataError e = LoadWith(field, raw);
        Assert.Contains("expected an object", e.Message);
    }

    /// <summary>Pin for BUG-0242 (today's behaviour). Flip to the skipped theory above when fixed.</summary>
    [Theory]
    [InlineData("attack.bonusVs", "1.5")]
    [InlineData("attack.bonusVs", "[1.5]")]
    public void Bug0242Pin_MapOfNumbersField_SaysExpectedANumber(string field, string raw)
    {
        DataError e = LoadWith(field, raw);
        Assert.Contains("expected a number", e.Message);
    }
}
