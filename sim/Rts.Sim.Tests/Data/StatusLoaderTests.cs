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
        Assert.Equal(6, d.Statuses.Length); // + blinded (M4-4b-2), frenzied (M4-4b-3a), stealthed and revealed (M4-5)
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

    /// <summary>
    /// M4-4b-3a criterion 1: Frenzied is a frenzied status with no damage type, sight or reach, and text without a number (the
    /// data track pins it); the kind is appended, so the existing kinds keep their values.
    /// </summary>
    [Fact]
    public void Shipped_Frenzied_IsAFrenziedStatus_AndTheOldKindsKeepTheirValues()
    {
        GameData d = TestSim.Data;
        StatusDef frenzied = d.Statuses[d.FindStatus("frenzied")];
        Assert.Equal(StatusKind.Frenzied, frenzied.Kind);
        Assert.Equal("Frenzied", frenzied.DisplayName);
        Assert.False(string.IsNullOrWhiteSpace(frenzied.Description));
        Assert.DoesNotContain(frenzied.Description, char.IsDigit);
        Assert.Equal((-1, 0f, 0f), (frenzied.DamageType, frenzied.Sight, frenzied.Reach));
        Assert.Equal((0, 1, 2, 3), ((int)StatusKind.DamageOverTime, (int)StatusKind.Slow, (int)StatusKind.Blind, (int)StatusKind.Frenzied));
        Assert.Equal(new[] { "damageOverTime", "slow", "blind", "frenzied", "stealthed", "revealed" }, DataLimits.StatusKindIds.ToArray()); // M4-5 appended two
        // The entry follows blinded (appended after it; M4-5's stealthed and revealed come after it).
        JsonArray list = JsonNode.Parse(File.ReadAllText(Path.Combine(TestDataDir.Shipped, "common", "statuses.json")))!["statuses"]!.AsArray();
        Assert.Equal("frenzied", (string)list[3]!["id"]!);
    }

    /// <summary>M4-4b-3a criterion 1: a frenzied status refuses <c>sight</c>, <c>reach</c> and <c>damageType</c>, each at its path.</summary>
    [Fact]
    public void AFrenzied_RefusesSightReachAndDamageType()
    {
        AssertErrorAt(LoadWith(r => Entry(r, "frenzied")["sight"] = 2), StatusFile, "statuses[3].sight", "only a blind");
        AssertErrorAt(LoadWith(r => Entry(r, "frenzied")["reach"] = 3), StatusFile, "statuses[3].reach", "only a blind");
        AssertErrorAt(LoadWith(r => Entry(r, "frenzied")["damageType"] = "magic"), StatusFile, "statuses[3].damageType", "only a damageOverTime");
        Assert.False(LoadWith(r => Entry(r, "frenzied")["magnitude"] = 0.5).Ok); // the magnitude is the applying effect's
    }

    [Fact]
    public void MissingFile_IsAnError()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        File.Delete(dir.FullPath(StatusFile));
        AssertErrorAt(DataLoader.LoadAll(dir.Path), StatusFile, "", "required file is missing");
    }
}
