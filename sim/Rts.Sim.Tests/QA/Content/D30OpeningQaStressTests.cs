using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Rts.Sim.Tests.Content;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA D30 (session 2026-11-13-1118), a measurement (rule 48: only preconditions asserted). The developer's re-measurement
/// cross-checked through QA's own harness on one configuration: V24 (the hand-over, read from its fixture file) with the
/// Frenzy rows written here from docs/03 "For the data track (M7-4)" (not through <see cref="TeblorLanding"/>) and the
/// global <c>ai.reserveArmyShare</c> 0.5, seeds 1-8 on QA's eight pairings, Normal against Normal, to 15:00. Printed in
/// D29's columns plus the Frenzy: the Teblor units newly carrying <c>teblor_frenzied</c> (counted every tick) and the
/// matches with any. Run on the merged tree (the sim track's M7-6 merged in) to check rule 41.
/// </summary>
[Trait("Category", "Stress")]
public class D30OpeningQaStressTests(ITestOutputHelper output)
{
    private const int Tps = SimConstants.TicksPerSecond;
    private const int Ten = 12_000;
    private const int Fifteen = 18_000;
    private static readonly string[] Maps = { "generated", "raraku", "pale_hills", "vathar_crossing" };
    private static readonly string[] Opponents = { "malazan", "whirlwind" };

    private sealed record Row(ulong Seed, string Map, string Opponent, int AgeTwoAt, int Waves10, int ExpandedAt, bool EveryKind, int AnointedAt,
        int DefeatedAt, int Army15Half, int FrenzyStarts, int FirstFrenzy);

    /// <summary>docs/03's four rows, typed from the doc (QA's own copy).</summary>
    private static void Rows(TestDataDir dir)
    {
        dir.EditJson("common/statuses.json", root => root["statuses"]!.AsArray().Add(JsonNode.Parse(
            """{ "id": "teblor_frenzied", "displayName": "Blood-oil Frenzy", "description": "QA", "kind": "frenzied", "damageBonus": 0.2, "onEnd": { "status": "slowed", "magnitude": 0.2, "duration": 4 } }""")));
        dir.EditJson("factions/teblor/abilities.json", root =>
            root["abilities"]!.AsArray().Single(a => (string)a!["id"]! == "blood_oil_frenzy")!["effects"] =
                JsonNode.Parse("""[{ "kind": "applyStatus", "status": "teblor_frenzied", "magnitude": 0.4, "duration": 10 }]"""));
        dir.EditJson("factions/teblor/techs.json", root =>
            root["techs"]!.AsArray().Single(t => (string)t!["id"]! == "teblor_blood_oil_anointing")!["effects"]!.AsArray()
                .Add(JsonNode.Parse("""{ "stat": "statusDuration", "amount": 4, "statuses": ["teblor_frenzied"] }""")));
        dir.EditJson("factions/teblor/buildings.json", root => { foreach (JsonNode? b in root["buildings"]!.AsArray()) b!["scale"] = 1.25; });
    }

    private static Row Play(GameData data, string map, string opponent, ulong seed)
    {
        int teblor = data.FindFaction("teblor");
        PlayerSeat t = new(teblor, SeatController.Ai, AiDifficulty.Normal);
        PlayerSeat o = new(data.FindFaction(opponent), SeatController.Ai, AiDifficulty.Normal);
        bool first = opponent == "malazan";
        PlayerSeat[] seats = first ? new[] { t, o } : new[] { o, t };
        int p = first ? 0 : 1;
        Simulation sim = map == "generated"
            ? AiScenes.Generated(seats, seed, false, out _, data: data)
            : AiScenes.Generated(seats, seed, false, out _, data: data,
                configure: c => c with { MapId = map, Map = c.Map with { Width = data.Maps[data.FindMap(map)].Size, Height = data.Maps[data.FindMap(map)].Size } });
        World w = sim.World;
        Assert.Equal(teblor, w.FactionOf(p));
        AiPlayer ai = sim.AiOf(p)!;
        var kinds = new SortedSet<int>();
        foreach (AiStep s in ai.Opening.BuildOrder) if (s.Action == AiStepAction.Build) kinds.Add(s.Target);
        var built = new HashSet<int>();
        int hall = data.FindBuilding("teblor_longhouse"), anoint = data.FindTech("teblor_blood_oil_anointing"), frenzy = data.FindStatus("teblor_frenzied");
        Assert.True(frenzy >= 0);
        int ageAt = -1, waves10 = 0, expanded = -1, fell = -1, starts = 0, firstFrenzy = -1;
        var had = new bool[w.Units.Capacity];
        var gen = new int[w.Units.Capacity];
        while (sim.TickNumber < Fifteen)
        {
            sim.Tick();
            int now = sim.TickNumber;
            if (ageAt < 0 && w.Age(p) >= 2) ageAt = now;
            if (now == Ten) waves10 = ai.Board.WavesStarted;
            if (fell < 0 && w.IsDefeated(p)) fell = now;
            var u = w.Units;
            for (int s = 0; s < u.Capacity; s++)
            {
                bool has = false;
                if (u.Alive[s] && u.Owner[s] == p)
                    for (int k = 0; k < u.Statuses.Count[s]; k++)
                        if (u.Statuses.StatusId[s * Rts.Sim.Abilities.StatusStore.PerUnit + k] == frenzy) has = true;
                if (u.Generation[s] != gen[s]) (gen[s], had[s]) = (u.Generation[s], false);
                if (has && !had[s])
                {
                    starts++;
                    if (firstFrenzy < 0) firstFrenzy = now;
                }
                had[s] = has;
            }
            if (now % Tps != 0) continue;
            var bs = w.Buildings;
            int halls = 0;
            for (int s = 0; s < bs.Capacity; s++)
            {
                if (!bs.Alive[s] || bs.Owner[s] != p || bs.UnderConstruction[s]) continue;
                built.Add(bs.TypeId[s]);
                if (bs.TypeId[s] == hall) halls++;
            }
            if (expanded < 0 && halls >= 2) expanded = now;
        }
        int army = 0;
        for (int s = 0; s < w.Units.Capacity; s++)
            if (w.Units.Alive[s] && w.Units.Owner[s] == p && data.Units[w.Units.TypeId[s]].Slot != UnitSlot.Worker) army += data.Units[w.Units.TypeId[s]].HalfPop;
        return new Row(seed, map, opponent, ageAt, waves10, expanded, kinds.All(built.Contains), w.TechResearchedTick(p, anoint), fell, army, starts, firstFrenzy);
    }

    private static string T(int tick) => tick < 0 ? "never" : $"{tick / Tps / 60}:{tick / Tps % 60:00}";

    [Fact]
    public void V24_WithTheRows_Share05_Seeds1To8_Report()
    {
        JsonObject v24 = TeblorOpeningHorizonReportTests.HandOver();
        GameData data = AiScenes.DataWith(dir =>
        {
            Rows(dir);
            dir.EditJson("factions/teblor/ai.json", root => root["openings"]![0] = JsonNode.Parse(v24.ToJsonString()));
            dir.EditJson("common/rules.json", root => root["ai"]!["reserveArmyShare"] = 0.5);
        });
        Assert.Equal(0.5f, data.AiRules.ReserveArmyShare);
        var jobs = new List<(ulong S, string M, string O)>();
        foreach (ulong seed in new ulong[] { 1, 2, 3, 4, 5, 6, 7, 8 })
            foreach (string m in Maps)
                foreach (string o in Opponents) jobs.Add((seed, m, o));
        var rows = new Row[jobs.Count];
        var errors = new ConcurrentQueue<string>();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, i =>
        {
            try { rows[i] = Play(data, jobs[i].M, jobs[i].O, jobs[i].S); }
            catch (Exception e) { errors.Enqueue($"{jobs[i]}: {e}"); }
        });
        Assert.True(errors.IsEmpty, string.Join("\n", errors));
        output.WriteLine($"QA D30: V24 + docs/03 rows, reserveArmyShare 0.5, {rows.Length} matches to 15:00 in {clock.Elapsed.TotalSeconds:0} s");
        foreach (Row r in rows)
            output.WriteLine($"| {r.Seed} | {r.Map} | {r.Opponent} | Age II {T(r.AgeTwoAt)} | waves10 {r.Waves10} | expanded {T(r.ExpandedAt)} | every kind {r.EveryKind} | Anointing {T(r.AnointedAt)} | defeated {T(r.DefeatedAt)} | army15 {r.Army15Half / 2.0} | frenzy starts {r.FrenzyStarts} first {T(r.FirstFrenzy)} |");
        int met = rows.GroupBy(r => r.Seed).Count(g => g.Count(r => r.AgeTwoAt >= 0 && r.AgeTwoAt <= Ten) >= 6 && g.Count(r => r.Waves10 > 0) >= 6);
        output.WriteLine("| seeds met | Age II by 10:00 | wave by 10:00 | expanded | every building kind | Anointing | defeated by 15:00 | army pop 15:00 (sum) | matches with a Frenzy | Frenzy starts (unit-applications) |");
        output.WriteLine($"| {met} of 8 | {rows.Count(r => r.AgeTwoAt >= 0 && r.AgeTwoAt <= Ten)} | {rows.Count(r => r.Waves10 > 0)} | {rows.Count(r => r.ExpandedAt >= 0)} | {rows.Count(r => r.EveryKind)} | "
            + $"{rows.Count(r => r.AnointedAt >= 0)} | {rows.Count(r => r.DefeatedAt >= 0)} | {rows.Sum(r => r.Army15Half) / 2.0} | {rows.Count(r => r.FrenzyStarts > 0)} | {rows.Sum(r => r.FrenzyStarts)} |");
        Assert.Equal(64, rows.Length);
    }
}
