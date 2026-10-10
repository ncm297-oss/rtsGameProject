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
        Assert.Equal(3, d.Statuses.Length); // + blinded (M4-4b-2)
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

    /// <summary>M4-4b-2: Blinded is a blind status with docs/02's sight 2 m and reach 3 m, in the data, not in C#.</summary>
    [Fact]
    public void Shipped_Blinded_IsABlind_Sight2_Reach3()
    {
        GameData d = TestSim.Data;
        StatusDef blinded = d.Statuses[d.FindStatus("blinded")];
        Assert.Equal(StatusKind.Blind, blinded.Kind);
        Assert.Equal((2f, 3f), (blinded.Sight, blinded.Reach));
        Assert.Equal("Blinded", blinded.DisplayName);
        Assert.False(string.IsNullOrWhiteSpace(blinded.Description));
        Assert.Equal(-1, blinded.DamageType);
        Assert.Equal((0f, 0f), (d.Statuses[d.FindStatus("slowed")].Sight, d.Statuses[d.FindStatus("burning")].Reach));
    }

    /// <summary>M4-4b-2 criterion 1: a blind status needs a sight and a reach (m, above 0, at most 64); no other kind takes them.</summary>
    [Fact]
    public void ABlind_NeedsSightAndReach_AndNoOtherKindHasThem()
    {
        AssertErrorAt(LoadWith(r => Entry(r, "blinded").Remove("sight")), StatusFile, "statuses[2].sight", "missing");
        AssertErrorAt(LoadWith(r => Entry(r, "blinded").Remove("reach")), StatusFile, "statuses[2].reach", "missing");
        AssertErrorAt(LoadWith(r => Entry(r, "blinded")["sight"] = 0), StatusFile, "statuses[2].sight", "must be positive");
        AssertErrorAt(LoadWith(r => Entry(r, "blinded")["reach"] = -1), StatusFile, "statuses[2].reach", "must be positive");
        AssertErrorAt(LoadWith(r => Entry(r, "blinded")["reach"] = DataLimits.MaxSight + 1), StatusFile, "statuses[2].reach", "above the maximum");
        AssertErrorAt(LoadWith(r => Entry(r, "slowed")["sight"] = 2), StatusFile, "statuses[1].sight", "only a blind");
        AssertErrorAt(LoadWith(r => Entry(r, "burning")["reach"] = 3), StatusFile, "statuses[0].reach", "only a blind");
        AssertErrorAt(LoadWith(r => Entry(r, "blinded")["damageType"] = "magic"), StatusFile, "statuses[2].damageType", "only a damageOverTime");
    }

    [Fact]
    public void MissingFile_IsAnError()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        File.Delete(dir.FullPath(StatusFile));
        AssertErrorAt(DataLoader.LoadAll(dir.Path), StatusFile, "", "required file is missing");
    }
}
