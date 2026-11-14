using System.Text.Json.Nodes;
using Rts.Sim.Data;

namespace Rts.Sim.Tests;

/// <summary>
/// M7-4 criterion 1: the four new fields load (a frenzied status's <c>damageBonus</c>, any status's <c>onEnd</c>, the tech
/// stat <c>statusDuration</c>, a building's <c>scale</c>), and each bad value is exactly one error at its file and path.
/// </summary>
public class FrenzyRulesLoaderTests
{
    private const string Statuses = "common/statuses.json";
    private const string TeblorTechs = "factions/teblor/techs.json";
    private const string TeblorBuildings = "factions/teblor/buildings.json";
    private const string WhirlwindUnits = "factions/whirlwind/units.json";

    /// <summary>The Frenzy row's index in the test copy's statuses file (last; M7-6, BUG-1280: whether or not the data ships it).</summary>
    private static readonly int Row = BloodOilScenes.RowIndex;

    /// <summary>Blood-oil Anointing's statusDuration effect index in the copy (after its armor effects; BUG-1280 as <see cref="Row"/>).</summary>
    private static readonly int Effect = BloodOilScenes.EffectIndex;

    private static JsonArray ShippedArray(string rel, string list) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(TestDataDir.Shipped, rel)))![list]!.AsArray();

    private static int IndexOf(string rel, string list, string id)
    {
        JsonArray a = ShippedArray(rel, list);
        for (int i = 0; i < a.Count; i++)
            if ((string)a[i]!["id"]! == id) return i;
        throw new InvalidOperationException(id);
    }

    /// <summary>The Blood-oil copy (<see cref="BloodOilScenes.Edit"/>) with <paramref name="edit"/> on top.</summary>
    private static DataLoadResult LoadWith(Action<TestDataDir> edit)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        BloodOilScenes.Edit(dir);
        edit(dir);
        return DataLoader.LoadAll(dir.Path);
    }

    private static DataLoadResult WithFrenzy(Action<JsonObject> edit) =>
        LoadWith(dir => dir.EditJson(Statuses, r => edit(r["statuses"]![Row]!.AsObject())));

    private static DataLoadResult WithStatus(string id, Action<JsonObject> edit) =>
        LoadWith(dir => dir.EditJson(Statuses, r => edit(r["statuses"]!.AsArray().Single(n => (string)n!["id"]! == id)!.AsObject())));

    private static DataLoadResult WithTechEffect(Action<JsonObject> edit) =>
        LoadWith(dir => dir.EditJson(TeblorTechs, r => edit(r["techs"]![0]!["effects"]![Effect]!.AsObject())));

    /// <summary>Exactly one error, at <paramref name="file"/> <paramref name="path"/>, its message containing <paramref name="text"/>.</summary>
    private static void AssertOneErrorAt(DataLoadResult r, string file, string path, string text)
    {
        Assert.False(r.Ok);
        Assert.True(r.Errors.Count == 1 && r.Errors[0].File == file && r.Errors[0].Path == path && r.Errors[0].Message.Contains(text),
            $"want one error at {file} {path} ({text}), got: " + string.Join("; ", r.Errors));
    }

    [Fact]
    public void TheFourFields_Load()
    {
        GameData d = BloodOilScenes.Data;
        StatusDef frenzy = d.Statuses[d.FindStatus(BloodOilScenes.Status)];
        Assert.Equal(StatusKind.Frenzied, frenzy.Kind);
        Assert.Equal(0.2f, frenzy.DamageBonus);
        Assert.Equal((d.FindStatus("slowed"), 0.2f, 80), (frenzy.OnEndStatus, frenzy.OnEndMagnitude, frenzy.OnEndTicks));
        // Without the fields: no bonus, no onEnd (every shipped row but the data track's own Frenzy row, BUG-1280).
        foreach (StatusDef st in TestSim.Data.Statuses)
            if (st.Key != BloodOilScenes.Status)
                Assert.Equal((0f, -1, 0f, 0), (st.DamageBonus, st.OnEndStatus, st.OnEndMagnitude, st.OnEndTicks));
        TechEffect e = d.Techs[d.FindTech("teblor_blood_oil_anointing")].Effects[Effect];
        Assert.Equal((TechStat.StatusDuration, 80f), (e.Stat, e.Amount)); // +4 s
        Assert.Equal(new[] { frenzy.Id }, e.Statuses.ToArray());
        Assert.True(e.Units.IsEmpty && e.Tags.IsEmpty && e.AttackType == -1 && e.Siege == -1);
        Assert.Equal(1.25f, d.Buildings[d.FindBuilding("teblor_longhouse")].Scale);
        // Without a scale: 1 (every shipped building outside the Teblor's, whose 1.25 docs/03 gives the data track; BUG-1280).
        int teblor = TestSim.Data.FindFaction("teblor");
        Assert.All(TestSim.Data.Buildings.Where(b => b.Faction != teblor), b => Assert.Equal(1f, b.Scale));
        // The ends of the ranges load.
        Assert.True(WithFrenzy(x => x["damageBonus"] = 4).Ok);
        Assert.True(LoadWith(dir => dir.EditJson(TeblorBuildings, r => r["buildings"]![0]!["scale"] = 3.0)).Ok);
        Assert.True(LoadWith(dir => dir.EditJson(TeblorBuildings, r => r["buildings"]![0]!["scale"] = 0.5)).Ok);
    }

    [Theory]
    [InlineData(0.0, "outside 0-4")]
    [InlineData(-0.2, "outside 0-4")]
    [InlineData(4.01, "outside 0-4")]
    public void ADamageBonus_AtZero_OrAboveTheCap_IsOneError(double bonus, string text) =>
        AssertOneErrorAt(WithFrenzy(x => x["damageBonus"] = bonus), Statuses, $"statuses[{Row}].damageBonus", text);

    [Fact]
    public void ADamageBonus_OnAnotherKind_IsOneError()
    {
        int slowed = IndexOf(Statuses, "statuses", "slowed");
        AssertOneErrorAt(WithStatus("slowed", x => x["damageBonus"] = 0.2), Statuses, $"statuses[{slowed}].damageBonus", "only a frenzied status");
    }

    [Fact]
    public void AnOnEnd_NamingItself_IsOneError() =>
        AssertOneErrorAt(WithFrenzy(x => x["onEnd"]!["status"] = BloodOilScenes.Status), Statuses, $"statuses[{Row}].onEnd.status", "names the status itself");

    [Fact]
    public void AnOnEnd_NamingAnUnknownId_IsOneError() =>
        AssertOneErrorAt(WithFrenzy(x => x["onEnd"]!["status"] = "dazed"), Statuses, $"statuses[{Row}].onEnd.status", "unknown status 'dazed'");

    [Fact]
    public void AnOnEnd_NamingARowWithItsOwnOnEnd_IsOneError()
    {
        // Burning gets an onEnd of its own; the Frenzy then names Burning: a chain.
        DataLoadResult r = LoadWith(dir => dir.EditJson(Statuses, root =>
        {
            JsonArray list = root["statuses"]!.AsArray();
            list.Single(n => (string)n!["id"]! == "burning")!["onEnd"] = new JsonObject { ["status"] = "slowed", ["magnitude"] = 0.1, ["duration"] = 1 };
            list[Row]!["onEnd"] = new JsonObject { ["status"] = "burning", ["magnitude"] = 5, ["duration"] = 2 };
        }));
        AssertOneErrorAt(r, Statuses, $"statuses[{Row}].onEnd.status", "has an onEnd of its own");
    }

    [Theory]
    [InlineData("slowed", "magnitude", "1", "must be below 1")] // a slow's magnitude is a fraction below 1
    [InlineData("slowed", "magnitude", "0", "")]
    [InlineData("slowed", "duration", "0", "")] // at least a tick
    [InlineData("burning", "duration", "2.5", "whole number of seconds")] // damage over time: whole seconds
    [InlineData("burning", "magnitude", "1.5", "not a whole number")]
    [InlineData("blinded", "magnitude", "0.2", "takes no magnitude")]
    [InlineData("revealed", "magnitude", "0.2", "takes no magnitude")]
    public void AnOnEnd_ItsKindRefuses_IsOneError(string status, string field, string json, string text)
    {
        DataLoadResult r = WithFrenzy(x =>
        {
            var onEnd = new JsonObject { ["status"] = status, ["duration"] = 4 };
            if (status is "slowed" or "burning") onEnd["magnitude"] = status == "slowed" ? 0.2 : 5;
            onEnd[field] = JsonNode.Parse(json);
            x["onEnd"] = onEnd;
        });
        AssertOneErrorAt(r, Statuses, $"statuses[{Row}].onEnd.{field}", text);
    }

    [Fact]
    public void AnOnEnd_OfABlindOrAStealth_TakesNoMagnitude_AndLoads()
    {
        Assert.True(WithFrenzy(x => x["onEnd"] = new JsonObject { ["status"] = "blinded", ["duration"] = 1 }).Ok);
        DataLoadResult r = WithStatus("blinded", x => x["onEnd"] = new JsonObject { ["status"] = "revealed", ["duration"] = 0.05 });
        Assert.True(r.Ok, string.Join("; ", r.Errors)); // any row may carry one, of any kind
        Assert.Equal(1, r.Data!.Statuses[r.Data.FindStatus("blinded")].OnEndTicks);
        // The Frenzy's own Slowed may not carry one: the Frenzy would then name a row with an onEnd (a chain).
        Assert.False(WithStatus("slowed", x => x["onEnd"] = new JsonObject { ["status"] = "revealed", ["duration"] = 1 }).Ok);
    }

    [Fact]
    public void AWhenBelowHpPassive_OnAnOnEndRow_IsOneError()
    {
        int zealot = IndexOf(WhirlwindUnits, "units", "whirlwind_zealot");
        DataLoadResult r = LoadWith(dir => dir.EditJson(WhirlwindUnits, root => root["units"]![zealot]!["passives"]![0]!["status"] = BloodOilScenes.Status));
        AssertOneErrorAt(r, WhirlwindUnits, $"units[{zealot}].passives[0].status", "never ends while held");
        // The other passive triggers may name it (their entries run out).
        Assert.True(LoadWith(dir => dir.EditJson(WhirlwindUnits, root => root["units"]![zealot]!["passives"]![1]!["status"] = BloodOilScenes.Status)).Ok);
    }

    [Fact]
    public void AStatusDurationEffect_NamingAnUnknownStatus_IsOneError() =>
        AssertOneErrorAt(WithTechEffect(e => e["statuses"] = new JsonArray("dazed")), TeblorTechs, $"techs[0].effects[{Effect}].statuses[0]", "unknown status 'dazed'");

    [Theory]
    [InlineData("appliesTo", "{\"units\": [\"teblor_warrior\"]}", "appliesTo", "names statuses, not units")]
    [InlineData("statuses", "[]", "statuses", "empty list")]
    [InlineData("statuses", null, "statuses", "missing")]
    [InlineData("amount", "0", "amount", "non-zero")]
    [InlineData("amount", "0.01", "amount", "rounds to 0 ticks")]
    [InlineData("amount", "3601", "amount", "beyond")]
    public void AStatusDurationEffect_BadShapes_AreOneErrorEach(string field, string? json, string at, string text)
    {
        DataLoadResult r = WithTechEffect(e =>
        {
            if (json == null) e.Remove(field);
            else e[field] = JsonNode.Parse(json);
        });
        AssertOneErrorAt(r, TeblorTechs, $"techs[0].effects[{Effect}].{at}", text);
    }

    [Fact]
    public void AStatusDurationEffect_MayShorten_AndStatusesOnAnotherStat_IsOneError()
    {
        DataLoadResult r = WithTechEffect(e => e["amount"] = -2);
        Assert.True(r.Ok, string.Join("; ", r.Errors));
        Assert.Equal(-40f, r.Data!.Techs[r.Data.FindTech("teblor_blood_oil_anointing")].Effects[Effect].Amount);
        DataLoadResult armor = LoadWith(dir => dir.EditJson(TeblorTechs, root => root["techs"]![0]!["effects"]![0]!["statuses"] = new JsonArray("slowed")));
        AssertOneErrorAt(armor, TeblorTechs, "techs[0].effects[0].statuses", "only a statusDuration effect");
    }

    [Theory]
    [InlineData(0.4)]
    [InlineData(3.1)]
    [InlineData(0)]
    public void ABuildingScale_OutOfRange_IsOneError(double scale)
    {
        int hall = IndexOf(TeblorBuildings, "buildings", "teblor_war_lodge");
        DataLoadResult r = LoadWith(dir => dir.EditJson(TeblorBuildings, root => root["buildings"]![hall]!["scale"] = scale));
        AssertOneErrorAt(r, TeblorBuildings, $"buildings[{hall}].scale", "is outside 0.5-3");
    }
}
