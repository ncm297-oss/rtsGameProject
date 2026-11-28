using System.Text.Json.Nodes;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA, M6-S15 C (QA focus 4, "C must not change a healthy seat"): a seat that is only briefly short (its pick, the Heavy
/// Infantry, short of gold, a gold mine it may gather known) on the flat scene, three seeds, 12,000 ticks; and three shipped
/// two-seat matches to 12,000. Prints the state hash every 500 ticks and the fold of every tick's hash ("HEALTHY" lines), for a
/// run on the branch and on the base d26338d3 (it reads only what the base had). Report only: no pinned hash.
/// </summary>
[Trait("Category", "Stress")]
public class M6S15HealthySeatReportQaStressTests
{
    private readonly ITestOutputHelper _out;

    public M6S15HealthySeatReportQaStressTests(ITestOutputHelper output) => _out = output;

    private static readonly Lazy<GameData> s_mix = new(() => AiScenes.DataWithAi("malazan", root =>
    {
        foreach (JsonNode? o in root["openings"]!.AsArray())
        {
            AiScenes.SetBuildOrder(o, AiScenes.BuildOrder(("time", 3600, "build", "malazan_billet", 0)));
            o!["compositions"] = new JsonArray(
                new JsonObject { ["age"] = 1, ["units"] = new JsonObject { ["malazan_heavy_infantry"] = 35, ["malazan_crossbowman"] = 30 } },
                new JsonObject { ["age"] = 2, ["units"] = new JsonObject { ["malazan_heavy_infantry"] = 35, ["malazan_crossbowman"] = 30 } });
        }
    }));

    private void Play(string name, Simulation sim, int ticks)
    {
        var fold = new StateHasher();
        var marks = new List<string>();
        for (int t = 0; t < ticks && !sim.MatchOver; t++)
        {
            sim.Tick();
            ulong h = sim.StateHash();
            fold.Add(h);
            if (sim.TickNumber % 500 == 0) marks.Add($"{sim.TickNumber}:{h:X16}");
        }
        _out.WriteLine($"HEALTHY {name}: end {sim.TickNumber} hash {sim.StateHash():X16} fold {fold.Value:X16}");
        foreach (string m in marks) _out.WriteLine($"HEALTHY {name} @ {m}");
    }

    /// <summary>The staged seat: one mine of the two kept (known, may be gathered), a Barracks and a Range, a Crossbowman alive (so the pick is the Heavy), 40 gold / 100 wood.</summary>
    [Theory]
    [InlineData(3UL)]
    [InlineData(4UL)]
    [InlineData(5UL)]
    public void ABrieflyShortSeat_12000Ticks(ulong seed)
    {
        GameData data = s_mix.Value;
        PlayerSeat[] seats = { new(0, SeatController.Ai, AiDifficulty.Normal), new(1, SeatController.Human, AiDifficulty.Normal) };
        Simulation sim = AiScenes.Flat(seats, data, seed: seed);
        World w = sim.World;
        bool kept = false;
        for (int i = 0; i < w.Resources.Capacity; i++)
        {
            if (!w.Resources.Alive[i] || data.Resources[w.Resources.TypeId[i]].Resource != ResourceKind.Gold) continue;
            if (!kept && w.Resources.Cell[i] % AiScenes.FlatSize < 32) { kept = true; continue; }
            w.Resources.Take(w.Resources.HandleOf(i), int.MaxValue);
        }
        Assert.True(kept);
        Assert.True(w.Buildings.Spawn(0, data.FindBuilding("malazan_barracks"), 40 * AiScenes.FlatSize + 4, out _));
        Assert.True(w.Buildings.Spawn(0, data.FindBuilding("malazan_crossbow_range"), 46 * AiScenes.FlatSize + 4, out _));
        w.Units.Hold[CombatScenes.Place(sim, 0, data.FindUnit("malazan_crossbowman"), w.NavGrid.CellCenter(16, 26)).Index] = true;
        w.Ledger.Gold[0] = 40;
        w.Ledger.Wood[0] = 100;
        Play($"staged seed {seed}", sim, 12_000);
    }

    [Theory]
    [InlineData("pale_hills", 1UL)]
    [InlineData("raraku", 1UL)]
    [InlineData("vathar_crossing", 1UL)]
    public void AShippedMatch_12000Ticks(string map, ulong seed) => Play($"{map} seed {seed}", CriterionMatch.Build(map, seed), 12_000);
}
