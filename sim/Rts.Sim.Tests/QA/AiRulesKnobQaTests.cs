using System.Text.Json.Nodes;
using Rts.Sim.Data;

namespace Rts.Sim.Tests;

/// <summary>
/// QA (M5-3, session 2026-10-12-1930): the seven new <c>rules.json</c> <c>ai</c> knobs against hostile values the dev's rows
/// don't try: wrong JSON types, null, huge and tiny literals. Each is refused with errors naming the knob, never a crash and
/// never a silent default; the edges (0 s, 3,600 s, 1 m, the maximum) load with the exact value.
/// </summary>
public class AiRulesKnobQaTests
{
    public static IEnumerable<object[]> Hostile()
    {
        foreach (string knob in new[] { "scoutAfter", "scoutRadius", "scoutReach", "scoutGrid", "rallyDistance", "rallyReach", "waveCooldown" }) // scoutGrid: re-check round 1 (BUG-0593)
            foreach (string raw in new[] { "\"40\"", "true", "null", "[]", "{}", "1e300", "-1e300", "1e-300" })
                yield return new object[] { knob, raw };
        // scoutGrid's own floor (16 m, not 1 m): just under it, and the values the other meter knobs accept.
        foreach (string raw in new[] { "15.999", "1", "0", "-16", "256.001" })
            yield return new object[] { "scoutGrid", raw };
    }

    [Theory]
    [MemberData(nameof(Hostile))]
    public void AHostileKnobValue_IsRefusedAtTheKnob(string knob, string raw)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("common/rules.json", r => r["ai"]![knob] = JsonNode.Parse(raw));
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        bool tinyTime = raw == "1e-300" && knob is "scoutAfter" or "waveCooldown"; // 1e-300 s is a valid time (0 ticks)
        if (tinyTime)
        {
            Assert.True(r.Ok, string.Join("; ", r.Errors));
            return;
        }
        Assert.False(r.Ok, $"{knob} = {raw} loaded");
        Assert.All(r.Errors, e => Assert.Contains(knob, e.Path + " " + e.Message));
    }

    [Theory]
    [InlineData("scoutAfter", 0)]
    [InlineData("scoutAfter", 3600)]
    [InlineData("waveCooldown", 0)]
    [InlineData("waveCooldown", 3600)]
    [InlineData("scoutRadius", 1)]
    [InlineData("scoutRadius", 64)]
    [InlineData("rallyDistance", 64)]
    [InlineData("scoutReach", 16)]
    [InlineData("rallyReach", 1)]
    [InlineData("scoutGrid", 16)]
    [InlineData("scoutGrid", 256)]
    [InlineData("scoutGrid", 16.5)]
    public void AKnobAtItsEdge_Loads_WithThatValue(string knob, double value)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("common/rules.json", r => r["ai"]![knob] = value);
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join("; ", r.Errors));
        AiRulesDef a = r.Data!.AiRules;
        double got = knob switch
        {
            "scoutAfter" => a.ScoutAfterTicks / 20.0,
            "waveCooldown" => a.WaveCooldownTicks / 20.0,
            "scoutRadius" => a.ScoutRadius,
            "rallyDistance" => a.RallyDistance,
            "scoutReach" => a.ScoutReach,
            "scoutGrid" => a.ScoutGrid,
            _ => a.RallyReach,
        };
        Assert.Equal(value, got, 6);
    }
}
