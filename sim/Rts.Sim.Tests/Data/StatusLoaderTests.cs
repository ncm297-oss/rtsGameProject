using System.Text.Json.Nodes;
using Rts.Sim.Data;

namespace Rts.Sim.Tests;

/// <summary>M4-4a criterion 2: <c>common/statuses.json</c> loads the shipped Burning and Slowed, and refuses bad entries, each error at its file and field.</summary>
public class StatusLoaderTests
{
    private const string StatusFile = "common/statuses.json";

    private static DataLoadResult LoadWith(Action<JsonObject> edit)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson(StatusFile, edit);
        return DataLoader.LoadAll(dir.Path);
    }

    private static JsonObject Entry(JsonObject root, string id) =>
        root["statuses"]!.AsArray().Select(n => n!.AsObject()).Single(s => (string)s["id"]! == id);

    private static void AssertErrorAt(DataLoadResult r, string file, string path, string? text = null)
    {
        Assert.False(r.Ok);
        Assert.True(r.Errors.Any(e => e.File == file && e.Path == path && (text == null || e.Message.Contains(text))),
            $"no error at {file} {path}: " + string.Join("; ", r.Errors));
    }

    [Fact]
    public void Shipped_BurningIsMagicDamageOverTime_SlowedIsASlow()
    {
        GameData d = TestSim.Data;
        Assert.Equal(2, d.Statuses.Length);
        StatusDef burning = d.Statuses[d.FindStatus("burning")];
        Assert.Equal(StatusKind.DamageOverTime, burning.Kind);
        Assert.Equal(d.DamageTable.DamageTypeKeys.IndexOf("magic"), burning.DamageType);
        Assert.Equal("Burning", burning.DisplayName);
        StatusDef slowed = d.Statuses[d.FindStatus("slowed")];
        Assert.Equal(StatusKind.Slow, slowed.Kind);
        Assert.Equal(-1, slowed.DamageType);
        Assert.False(string.IsNullOrWhiteSpace(slowed.Description));
    }

    [Fact]
    public void UnknownKind_IsAnError()
    {
        AssertErrorAt(LoadWith(r => Entry(r, "slowed")["kind"] = "frenzy"), StatusFile, "statuses[1].kind", "unknown");
    }

    [Fact]
    public void DamageOverTime_NeedsAKnownDamageType_AndASlowHasNone()
    {
        AssertErrorAt(LoadWith(r => Entry(r, "burning").Remove("damageType")), StatusFile, "statuses[0].damageType", "missing");
        AssertErrorAt(LoadWith(r => Entry(r, "burning")["damageType"] = "fire"), StatusFile, "statuses[0].damageType", "unknown damage type");
        AssertErrorAt(LoadWith(r => Entry(r, "slowed")["damageType"] = "magic"), StatusFile, "statuses[1].damageType", "only a damageOverTime");
    }

    [Fact]
    public void DuplicateId_MissingText_AndUnknownField_AreErrors()
    {
        AssertErrorAt(LoadWith(r => Entry(r, "slowed")["id"] = "burning"), StatusFile, "statuses[1].id", "duplicate");
        AssertErrorAt(LoadWith(r => Entry(r, "burning").Remove("displayName")), StatusFile, "statuses[0].displayName");
        Assert.False(LoadWith(r => Entry(r, "burning")["magnitude"] = 3).Ok);
    }

    [Fact]
    public void MissingFile_IsAnError()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        File.Delete(dir.FullPath(StatusFile));
        AssertErrorAt(DataLoader.LoadAll(dir.Path), StatusFile, "", "required file is missing");
    }
}
