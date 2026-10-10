using System.Text.Json.Nodes;
using Rts.Sim.Data;

namespace Rts.Sim.Tests;

/// <summary>
/// M4-4b-3a criterion 1: a unit's <c>passives</c> in <c>units.json</c>: the shipped Zealot's Frenzy of the Apocalypse and
/// Martyrdom (docs/factions/whirlwind.md "Unit notes"), and every refused shape, each an error naming the file and
/// <c>units[k].passives[j].field</c>.
/// </summary>
public class PassiveLoaderTests
{
    private const string WhirlwindUnits = "factions/whirlwind/units.json";

    private static readonly int ZealotIndex = UnitIndex("whirlwind_zealot");

    private static int UnitIndex(string id)
    {
        JsonArray list = JsonNode.Parse(File.ReadAllText(Path.Combine(TestDataDir.Shipped, "factions", "whirlwind", "units.json")))!["units"]!.AsArray();
        for (int i = 0; i < list.Count; i++)
            if ((string)list[i]!["id"]! == id) return i;
        throw new InvalidOperationException(id);
    }

    /// <summary>Loads the shipped data with the Zealot's <c>passives</c> list edited.</summary>
    private static DataLoadResult LoadWith(Action<JsonArray> editPassives)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson(WhirlwindUnits, root => editPassives(root["units"]![ZealotIndex]!["passives"]!.AsArray()));
        return DataLoader.LoadAll(dir.Path);
    }

    private static JsonObject Below(JsonArray p) => p[0]!.AsObject();

    private static JsonObject Death(JsonArray p) => p[1]!.AsObject();

    private static string At(int passive, string field) => $"units[{ZealotIndex}].passives[{passive}]{(field.Length > 0 ? "." + field : "")}";

    private static void AssertErrorAt(DataLoadResult r, string path, string? text = null)
    {
        Assert.False(r.Ok);
        Assert.True(r.Errors.Any(e => e.File == WhirlwindUnits && e.Path == path && (text == null || e.Message.Contains(text))),
            $"no error at {WhirlwindUnits} {path}: " + string.Join("; ", r.Errors));
    }

    [Fact]
    public void Shipped_Zealot_HasFrenzyOfTheApocalypse_AndMartyrdom_AndNoOtherUnitHasPassives()
    {
        GameData d = TestSim.Data;
        UnitDef zealot = d.Units[d.FindUnit("whirlwind_zealot")];
        int frenzied = d.FindStatus("frenzied");
        Assert.Equal(2, zealot.Passives.Length);
        Assert.Equal((0, 1), (zealot.BelowHpPassive, zealot.DeathPassive));
        UnitPassive below = zealot.Passives[0];
        Assert.Equal((PassiveTrigger.WhenBelowHp, 0.5f, frenzied, 0.5f), (below.Trigger, below.HpFraction, below.Status, below.Magnitude));
        Assert.Equal((0, 0f), (below.DurationTicks, below.Radius));
        Assert.True(below.Units.IsEmpty);
        UnitPassive death = zealot.Passives[1];
        Assert.Equal((PassiveTrigger.OnDeath, frenzied, 0.5f, 100, 6f), (death.Trigger, death.Status, death.Magnitude, death.DurationTicks, death.Radius));
        Assert.Equal(0f, death.HpFraction);
        Assert.Equal(new[] { zealot.Id }, death.Units.ToArray());
        foreach (UnitDef u in d.Units)
            if (u.Id != zealot.Id) Assert.True(u.Passives.IsEmpty && u.BelowHpPassive < 0 && u.DeathPassive < 0, $"{u.Key} has passives");
        Assert.Equal(2, DataLimits.MaxUnitPassives);
        Assert.Equal(new[] { "whenBelowHp", "onDeath" }, DataLimits.PassiveTriggerIds.ToArray());
    }

    [Fact]
    public void OneTriggerAlone_Loads_AndOrderIsFileOrder()
    {
        DataLoadResult r = LoadWith(p => p.RemoveAt(0));
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        UnitDef z = r.Data!.Units[r.Data.FindUnit("whirlwind_zealot")];
        Assert.Equal((-1, 0), (z.BelowHpPassive, z.DeathPassive));
        r = LoadWith(p => { JsonNode first = p[0]!; p.RemoveAt(0); p.Add(first); });
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        z = r.Data!.Units[r.Data.FindUnit("whirlwind_zealot")];
        Assert.Equal((1, 0), (z.BelowHpPassive, z.DeathPassive));
    }

    [Fact]
    public void Trigger_UnknownMissing_ATriggerTwice_AndTooMany_AreErrors()
    {
        AssertErrorAt(LoadWith(p => Below(p)["trigger"] = "onHit"), At(0, "trigger"), "unknown value 'onHit'");
        AssertErrorAt(LoadWith(p => Below(p).Remove("trigger")), At(0, "trigger"), "missing");
        AssertErrorAt(LoadWith(p => p.Add(Below(p).DeepClone())), $"units[{ZealotIndex}].passives", "above the maximum 2");
        AssertErrorAt(LoadWith(p => p[1] = Below(p).DeepClone()), At(1, "trigger"), "at most one per trigger");
        AssertErrorAt(LoadWith(p => p[0] = null), At(0, ""), "missing");
    }

    [Fact]
    public void WhenBelowHp_NeedsAFractionInside0To1_AndRefusesTheOnDeathFields()
    {
        AssertErrorAt(LoadWith(p => Below(p).Remove("hpFraction")), At(0, "hpFraction"), "missing");
        foreach (double f in new[] { 0.0, 1.0, 1.5, -0.25 })
            AssertErrorAt(LoadWith(p => Below(p)["hpFraction"] = f), At(0, "hpFraction"), "outside 0-1");
        Assert.True(LoadWith(p => Below(p)["hpFraction"] = 0.99).Ok);
        AssertErrorAt(LoadWith(p => Below(p)["duration"] = 5), At(0, "duration"), "trigger 'whenBelowHp' has no such field");
        AssertErrorAt(LoadWith(p => Below(p)["radius"] = 6), At(0, "radius"), "trigger 'whenBelowHp' has no such field");
        AssertErrorAt(LoadWith(p => Below(p)["units"] = new JsonArray("whirlwind_zealot")), At(0, "units"), "trigger 'whenBelowHp' has no such field");
    }

    [Fact]
    public void OnDeath_NeedsDurationRadiusAndUnits_AndRefusesAFraction()
    {
        AssertErrorAt(LoadWith(p => Death(p)["hpFraction"] = 0.5), At(1, "hpFraction"), "trigger 'onDeath' has no such field");
        AssertErrorAt(LoadWith(p => Death(p).Remove("duration")), At(1, "duration"), "missing");
        AssertErrorAt(LoadWith(p => Death(p)["duration"] = 0), At(1, "duration"));
        AssertErrorAt(LoadWith(p => Death(p).Remove("radius")), At(1, "radius"), "missing");
        AssertErrorAt(LoadWith(p => Death(p)["radius"] = 0), At(1, "radius"), "must be positive");
        AssertErrorAt(LoadWith(p => Death(p)["radius"] = DataLimits.MaxPassiveRadius + 0.5), At(1, "radius"), "above the maximum");
        Assert.True(LoadWith(p => Death(p)["radius"] = DataLimits.MaxPassiveRadius).Ok);
        AssertErrorAt(LoadWith(p => Death(p).Remove("units")), At(1, "units"), "missing");
        AssertErrorAt(LoadWith(p => Death(p)["units"] = new JsonArray()), At(1, "units"), "at least one");
        AssertErrorAt(LoadWith(p => Death(p)["units"] = new JsonArray("whirlwind_zealot", "whirlwind_ghost")), At(1, "units[1]"), "unknown unit 'whirlwind_ghost'");
        AssertErrorAt(LoadWith(p => Death(p)["units"] = new JsonArray("malazan_heavy_infantry")), At(1, "units[0]"), "belongs to faction 'malazan'");
        AssertErrorAt(LoadWith(p => Death(p)["units"] = new JsonArray("whirlwind_zealot", "whirlwind_zealot")), At(1, "units[1]"), "listed twice");
        AssertErrorAt(LoadWith(p => Death(p)["units"] = new JsonArray("Whirlwind Zealot")), At(1, "units[0]"), "not snake_case");
        DataLoadResult two = LoadWith(p => Death(p)["units"] = new JsonArray("whirlwind_zealot", "whirlwind_raider"));
        Assert.True(two.Ok, string.Join("\n", two.Errors));
        Assert.Equal(new[] { two.Data!.FindUnit("whirlwind_zealot"), two.Data.FindUnit("whirlwind_raider") },
            two.Data.Units[two.Data.FindUnit("whirlwind_zealot")].Passives[1].Units.ToArray());
    }

    [Fact]
    public void StatusAndMagnitude_FollowTheApplyStatusRules_OnBothTriggers()
    {
        for (int k = 0; k < 2; k++)
        {
            int j = k;
            JsonObject Of(JsonArray p) => p[j]!.AsObject();
            AssertErrorAt(LoadWith(p => Of(p)["status"] = "enraged"), At(j, "status"), "unknown status 'enraged'");
            AssertErrorAt(LoadWith(p => Of(p).Remove("status")), At(j, "status"), "missing");
            AssertErrorAt(LoadWith(p => Of(p).Remove("magnitude")), At(j, "magnitude"), "missing");
            AssertErrorAt(LoadWith(p => Of(p)["magnitude"] = 0), At(j, "magnitude"), "must be positive");
            AssertErrorAt(LoadWith(p => { Of(p)["status"] = "slowed"; Of(p)["magnitude"] = 1; }), At(j, "magnitude"), "below 1");
            AssertErrorAt(LoadWith(p => { Of(p)["status"] = "burning"; Of(p)["magnitude"] = 2.5; }), At(j, "magnitude"), "not a whole number");
            AssertErrorAt(LoadWith(p => { Of(p)["status"] = "blinded"; Of(p)["magnitude"] = 1; }), At(j, "magnitude"), "takes no magnitude");
            Assert.True(LoadWith(p => { Of(p)["status"] = "blinded"; Of(p).Remove("magnitude"); }).Ok);
            Assert.True(LoadWith(p => Of(p)["magnitude"] = 2).Ok); // frenzied: any fraction above 0
            Assert.False(LoadWith(p => Of(p)["kind"] = "aura").Ok); // an unknown field (the JSON reader's rule)
        }
        // Damage over time on a death: whole seconds, as an applyStatus.
        AssertErrorAt(LoadWith(p => { Death(p)["status"] = "burning"; Death(p)["magnitude"] = 5; Death(p)["duration"] = 2.5; }), At(1, "duration"), "whole number of seconds");
    }
}
