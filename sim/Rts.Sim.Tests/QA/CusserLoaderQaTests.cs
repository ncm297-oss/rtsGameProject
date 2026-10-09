using System.Text.Json.Nodes;
using Rts.Sim.Data;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M4-4b-1: loader fuzz on the damage effect's new <c>buildings</c> / <c>friendlyFire</c> fields and the damage-over-time
/// whole-seconds rule. Every bad value is an error naming the file and the effect field; nothing throws; nothing defaults.
/// </summary>
public class CusserLoaderQaTests
{
    private const string Abilities = "factions/malazan/abilities.json";

    /// <summary>Loads the shipped data with the Cusser (abilities[1]) edited.</summary>
    private static DataLoadResult LoadWithCusser(Action<JsonObject> edit)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson(Abilities, root =>
        {
            JsonObject a = root["abilities"]![1]!.AsObject();
            Assert.Equal("cusser", (string)a["id"]!);
            edit(a);
        });
        return DataLoader.LoadAll(dir.Path);
    }

    private static DataLoadResult LoadWithTelas(Action<JsonObject> edit)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson(Abilities, root => edit(root["abilities"]![0]!.AsObject()));
        return DataLoader.LoadAll(dir.Path);
    }

    private static void AssertFieldError(DataLoadResult r, string path)
    {
        Assert.False(r.Ok, "loaded");
        Assert.True(r.Errors.Any(e => e.File == Abilities && (e.Path == path || e.Path == "$." + path)), $"no error at {path}: " + string.Join("; ", r.Errors));
    }

    public static IEnumerable<object[]> BadFriendlyFire() => new[]
    {
        new object[] { JsonValue.Create("0.5")! },
        new object[] { JsonValue.Create(true)! },
        new object[] { JsonValue.Create(1.0000001)! },
        new object[] { JsonValue.Create(-0.0000001)! },
        new object[] { JsonValue.Create(1e300)! },
        new object[] { new JsonArray(0.5) },
        new object[] { new JsonObject() },
    };

    [Theory]
    [MemberData(nameof(BadFriendlyFire))]
    public void BadFriendlyFire_IsAnErrorAtTheField(JsonNode value) =>
        AssertFieldError(LoadWithCusser(a => a["effects"]![0]!["friendlyFire"] = value.DeepClone()), "abilities[1].effects[0].friendlyFire");

    public static IEnumerable<object[]> BadBuildings() => new[]
    {
        new object[] { JsonValue.Create("yes")! },
        new object[] { JsonValue.Create(1)! },
        new object[] { new JsonArray(true) },
    };

    [Theory]
    [MemberData(nameof(BadBuildings))]
    public void BadBuildings_IsAnErrorAtTheField(JsonNode value) =>
        AssertFieldError(LoadWithCusser(a => a["effects"]![0]!["buildings"] = value.DeepClone()), "abilities[1].effects[0].buildings");

    /// <summary>Explicit false on a status effect is still a field the kind doesn't use.</summary>
    [Fact]
    public void BuildingsFalse_OnApplyStatus_IsStillAnError() =>
        AssertFieldError(LoadWithTelas(a => a["effects"]![0]!["buildings"] = false), "abilities[0].effects[0].buildings");

    /// <summary>Null is absent: the Cusser without friendly fire and without buildings loads and no longer hits buildings.</summary>
    [Fact]
    public void NullOrFalseFields_LoadAsOff()
    {
        DataLoadResult r = LoadWithCusser(a => { a["effects"]![0]!["friendlyFire"] = null; a["effects"]![0]!["buildings"] = false; });
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        AbilityDef c = r.Data!.Abilities[r.Data.FindAbility("cusser")];
        Assert.False(c.HitsBuildings);
        Assert.Equal((false, 0f), (c.Effects[0].Buildings, c.Effects[0].FriendlyFire));
    }

    /// <summary><c>buildings</c> on an all_units ability is allowed; a second damage effect with it sets HitsBuildings.</summary>
    [Fact]
    public void Buildings_OnAllUnits_Loads_AndASecondEffectSetsHitsBuildings()
    {
        DataLoadResult r = LoadWithCusser(a =>
        {
            a["affects"] = "all_units";
            a["effects"]![0]!["friendlyFire"] = 0;
            a["effects"]![0]!["buildings"] = false;
            a["effects"]!.AsArray().Add(new JsonObject { ["kind"] = "damage", ["type"] = "siege", ["amount"] = 5, ["buildings"] = true });
        });
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        AbilityDef c = r.Data!.Abilities[r.Data.FindAbility("cusser")];
        Assert.True(c.HitsBuildings);
        Assert.Equal(AbilityAffects.AllUnits, c.Affects);
    }

    /// <summary>A DoT duration just off a whole second, or a string, is refused at the field; 1 s and a long 30 s load.</summary>
    [Theory]
    [InlineData(1.0000001, false)]
    [InlineData(0.999, false)]
    [InlineData(-1.0, false)]
    [InlineData(1.0, true)]
    [InlineData(30.0, true)]
    public void DotDuration_Boundaries(double seconds, bool ok)
    {
        DataLoadResult r = LoadWithTelas(a => a["effects"]![0]!["duration"] = seconds);
        if (ok) Assert.True(r.Ok, string.Join("\n", r.Errors));
        else AssertFieldError(r, "abilities[0].effects[0].duration");
    }

    [Fact]
    public void DotDuration_AsAString_IsAnError() =>
        AssertFieldError(LoadWithTelas(a => a["effects"]![0]!["duration"] = "4"), "abilities[0].effects[0].duration");
}
