using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Rts.Sim.Tests.Content;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA D29 (session 2026-11-12-0546), measurements (rule 48: only preconditions asserted). The D29 hand-over (V4, the fixture)
/// beside the shipped opening (V0) on QA's eight Teblor pairings:
/// <list type="bullet">
/// <item>on seed 1 (the developer's) and seeds 4 and 5 (not used to pick V4), played to 15:00: Age II by 10:00, a wave by 10:00,
/// any pairing V0 met and V4 loses, and by 15:00 the expansion, every building kind of the opening ever finished, the
/// Anointing researched;</item>
/// <item>the developer's missing-rule claim checked by measurement: the shipped <c>rules.json</c> <c>ai.reserveArmyShare</c>
/// (1) set to 0.5 on a data copy (both seats, so the opponent moves too), on V4 with the shipped 8 / 600 wave put back.</item>
/// </list>
/// Since fix round 1, "the fixture" in the first three reports is V4 (<c>teblor_standard_d29_v4.json</c>, kept as BUG-1300's
/// evidence); the hand-over is V24 (<see cref="TeblorOpeningHorizonReportTests.HandOver"/>), cross-checked by
/// <see cref="HandOverShippedAndV14_Seeds1To5_To15Minutes_Report"/>.
/// </summary>
[Trait("Category", "Stress")]
public class D29OpeningQaStressTests(ITestOutputHelper output)
{
    private const int Tps = SimConstants.TicksPerSecond;
    private const int Ten = 12_000;
    private const int Fifteen = 18_000;
    private static readonly string[] Maps = { "generated", "raraku", "pale_hills", "vathar_crossing" };
    private static readonly string[] Opponents = { "malazan", "whirlwind" };

    private sealed record Row(string Variant, ulong Seed, string Map, string Opponent, int Seat, int AgeTwoAt, int WavesAt10, int WavesAtEnd,
        int ExpandedAt, string NeverBuilt, int AnointedAt, int DefeatedAt)
    {
        public bool Age10 => AgeTwoAt >= 0 && AgeTwoAt <= Ten;
        public bool Wave10 => WavesAt10 > 0;
    }

    private static Row Play(string variant, GameData data, string map, string opponent, ulong seed, int bound)
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
        Assert.Equal("standard", ai.Opening.Id);

        var kinds = new SortedSet<int>();
        foreach (AiStep s in ai.Opening.BuildOrder) if (s.Action == AiStepAction.Build) kinds.Add(s.Target);
        var built = new HashSet<int>();
        int hall = -1;
        for (int b = 0; b < data.Buildings.Length; b++) if (data.Buildings[b].Faction == teblor && data.Buildings[b].Slot == BuildingSlot.TownHall) hall = b;
        int anoint = data.FindTech("teblor_blood_oil_anointing"), age = data.FindTech("age_ii");
        int ageAt = -1, waves10 = 0, expanded = -1, fell = -1;
        while (sim.TickNumber < bound)
        {
            sim.Tick();
            int now = sim.TickNumber;
            if (ageAt < 0 && w.Age(p) >= 2) ageAt = now;
            if (now == Ten) waves10 = ai.Board.WavesStarted;
            if (fell < 0 && w.IsDefeated(p)) fell = now;
            if (now % Tps == 0)
            {
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
        }
        Assert.Equal(bound, sim.TickNumber);
        string never = string.Join(",", kinds.Where(k => !built.Contains(k)).Select(k => data.Buildings[k].Key.Replace("teblor_", "")));
        return new Row(variant, seed, map, opponent, p, ageAt, waves10, ai.Board.WavesStarted, expanded, never == "" ? "-" : never,
            w.TechResearchedTick(p, anoint), fell);
    }

    private static List<Row> PlayAll(List<(string V, GameData D, string M, string O, ulong S)> jobs, int bound)
    {
        var rows = new Row[jobs.Count];
        var errors = new ConcurrentQueue<string>();
        Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, i =>
        {
            var j = jobs[i];
            try { rows[i] = Play(j.V, j.D, j.M, j.O, j.S, bound); }
            catch (Exception e) { errors.Enqueue($"{j.V} {j.S} {j.M} {j.O}: {e}"); }
        });
        Assert.True(errors.IsEmpty, string.Join("\n", errors));
        return rows.ToList();
    }

    private static string T(int tick) => tick < 0 ? "never" : $"{tick / Tps / 60}:{tick / Tps % 60:00}";

    private void Tally(List<Row> rows)
    {
        output.WriteLine("| variant | seed | Age II by 10:00 | wave by 10:00 | target 6 of 8 both | expanded by 15:00 | every kind built by 15:00 | Anointing by 15:00 |");
        output.WriteLine("| --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var g in rows.GroupBy(r => (r.Variant, r.Seed)))
        {
            int a = g.Count(r => r.Age10), wv = g.Count(r => r.Wave10);
            output.WriteLine($"| {g.Key.Variant} | {g.Key.Seed} | {a} | {wv} | {(a >= 6 && wv >= 6 ? "met" : "not met")} | {g.Count(r => r.ExpandedAt >= 0)} | "
                + $"{g.Count(r => r.NeverBuilt == "-")} | {g.Count(r => r.AnointedAt >= 0)} |");
        }
    }

    [Fact]
    public void ShippedAndFixture_Seeds1_4_5_To15Minutes_Report()
    {
        GameData v0 = TeblorOpeningVariantsReportTests.DataWith(TeblorOpeningVariantsReportTests.Shipped());
        GameData v4 = TeblorOpeningVariantsReportTests.DataWith(TeblorOpeningVariantsReportTests.Fixture());
        var jobs = new List<(string, GameData, string, string, ulong)>();
        foreach (ulong seed in new ulong[] { 1, 4, 5 })
            foreach (string m in Maps)
                foreach (string o in Opponents)
                {
                    jobs.Add(("V0", v0, m, o, seed));
                    jobs.Add(("V4", v4, m, o, seed));
                }
        List<Row> rows = PlayAll(jobs, Fifteen);
        output.WriteLine("| variant | seed | map | opp | seat | Age II | waves 10:00 / 15:00 | expanded | never built | Anointing | teblor defeated |");
        output.WriteLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (Row r in rows.OrderBy(r => r.Variant).ThenBy(r => r.Seed))
            output.WriteLine($"| {r.Variant} | {r.Seed} | {r.Map} | {r.Opponent} | {r.Seat} | {T(r.AgeTwoAt)} | {r.WavesAt10} / {r.WavesAtEnd} | {T(r.ExpandedAt)} | {r.NeverBuilt} | {T(r.AnointedAt)} | {T(r.DefeatedAt)} |");
        output.WriteLine("");
        Tally(rows.OrderBy(r => r.Variant).ThenBy(r => r.Seed).ToList());
        output.WriteLine("");
        output.WriteLine("Pairings V0 met and V4 does not (Age II by 10:00 or a wave by 10:00):");
        foreach (Row a in rows.Where(r => r.Variant == "V0"))
        {
            Row b = rows.Single(r => r.Variant == "V4" && r.Seed == a.Seed && r.Map == a.Map && r.Opponent == a.Opponent);
            if (a.Age10 && !b.Age10) output.WriteLine($"- seed {a.Seed} {a.Map} v {a.Opponent}: Age II {T(a.AgeTwoAt)} on V0, {T(b.AgeTwoAt)} on V4");
            if (a.Wave10 && !b.Wave10) output.WriteLine($"- seed {a.Seed} {a.Map} v {a.Opponent}: wave by 10:00 on V0, none on V4");
            if (a.DefeatedAt < 0 && b.DefeatedAt >= 0) output.WriteLine($"- seed {a.Seed} {a.Map} v {a.Opponent}: V4 defeated at {T(b.DefeatedAt)}, V0 not");
        }
        Assert.Equal(48, rows.Count);
    }

    /// <summary>
    /// BUG-1300's evidence: the fixture on seed 1, Vathar Crossing v Malazan (Age II at 7:42, not defeated by 15:00, yet no
    /// Breakers Hall, Stables, expansion or Anointing), minute by minute from 7:00 to 15:00: purse, Gatherers, army pop, the
    /// steps done, the reserved step and how many thinks the reserve gate was open / the AI was defending.
    /// </summary>
    [Fact]
    public void Fixture_Seed1_VatharVMalazan_After7Minutes_PerMinute_Report()
    {
        GameData data = TeblorOpeningVariantsReportTests.DataWith(TeblorOpeningVariantsReportTests.Fixture());
        PlayerSeat[] seats = { new(data.FindFaction("teblor"), SeatController.Ai, AiDifficulty.Normal), new(data.FindFaction("malazan"), SeatController.Ai, AiDifficulty.Normal) };
        int size = data.Maps[data.FindMap("vathar_crossing")].Size;
        Simulation sim = AiScenes.Generated(seats, 1, false, out _, data: data,
            configure: c => c with { MapId = "vathar_crossing", Map = c.Map with { Width = size, Height = size } });
        World w = sim.World;
        AiPlayer ai = sim.AiOf(0)!;
        var steps = ai.Opening.BuildOrder;
        int gatherer = data.FindUnit("teblor_gatherer");
        int thinks = 0, open = 0, defending = 0;
        var reserved = new SortedDictionary<string, int>(StringComparer.Ordinal);
        output.WriteLine("| time | gold / wood | gathered gold / wood | Gatherers | army pop | age | waves | steps not done (due) | thinks gate open | thinks defending | reserved step (thinks) |");
        output.WriteLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");
        while (sim.TickNumber < Fifteen)
        {
            sim.Tick();
            int t = sim.TickNumber;
            if ((t - 1) % ai.ThinkTicks == 0)
            {
                thinks++;
                if (ai.MayReserve()) open++;
                if (ai.Board.Defending) defending++;
                string key = ai.ReserveStep >= 0 ? $"{steps[ai.ReserveStep].Action} {steps[ai.ReserveStep].TargetKey}".Trim() : "none";
                reserved[key] = reserved.GetValueOrDefault(key) + 1;
            }
            if (t % (60 * Tps) != 0 || t < 7 * 60 * Tps) continue;
            int g = 0, army = 0;
            var u = w.Units;
            for (int s = 0; s < u.Capacity; s++)
            {
                if (!u.Alive[s] || u.Owner[s] != 0) continue;
                if (u.TypeId[s] == gatherer) g++;
                else if (data.Units[u.TypeId[s]].Slot != UnitSlot.Worker) army += data.Units[u.TypeId[s]].HalfPop;
            }
            var pending = new List<string>();
            for (int i = 0; i < steps.Length; i++)
                if (!ai.Board.StepDone[i] && ai.Board.StepDueTick[i] >= 0 && steps[i].TargetKey != "teblor_hut") pending.Add($"{steps[i].Action} {steps[i].TargetKey}".Trim());
            output.WriteLine($"| {t / Tps / 60}:00 | {w.Gold[0]} / {w.Wood[0]} | {w.GatheredGold[0]} / {w.GatheredWood[0]} | {g} | {army / 2.0} | {w.Age(0)} | {ai.Board.WavesStarted} | "
                + $"{string.Join(", ", pending)} | {open} / {thinks} | {defending} / {thinks} | {string.Join(", ", reserved.Select(kv => $"{kv.Key} {kv.Value}"))} |");
            thinks = open = defending = 0;
            reserved.Clear();
        }
        Assert.Equal(Fifteen, sim.TickNumber);
    }

    /// <summary>
    /// Re-check (fix round 1): the new hand-over (V24, read from its fixture file), the shipped opening and the developer's
    /// alternative V14 (V4 with Age II due at 6:00, built here independently of the developer's variant list) on seeds 1-5 to
    /// 15:00 through QA's own harness, to cross-check the fix report's tallies. Also prints pairing differences against V0.
    /// </summary>
    [Fact]
    public void HandOverShippedAndV14_Seeds1To5_To15Minutes_Report()
    {
        JsonObject v14 = TeblorOpeningVariantsReportTests.Fixture(); // V4
        foreach (JsonNode? s in v14["buildOrder"]!.AsArray())
            if ((string)s!["do"]! == "research" && (string?)s["id"] == "age_ii") s["at"] = new JsonObject { ["time"] = 360 };
        var configs = new (string Name, GameData Data)[]
        {
            ("V0", TeblorOpeningVariantsReportTests.DataWith(TeblorOpeningVariantsReportTests.Shipped())),
            ("hand-over", TeblorOpeningVariantsReportTests.DataWith(TeblorOpeningHorizonReportTests.HandOver())),
            ("V14", TeblorOpeningVariantsReportTests.DataWith(v14)),
        };
        var jobs = new List<(string, GameData, string, string, ulong)>();
        foreach (var c in configs)
            foreach (ulong seed in new ulong[] { 1, 2, 3, 4, 5 })
                foreach (string m in Maps)
                    foreach (string o in Opponents) jobs.Add((c.Name, c.Data, m, o, seed));
        List<Row> rows = PlayAll(jobs, Fifteen);
        Tally(rows.OrderBy(r => r.Variant, StringComparer.Ordinal).ThenBy(r => r.Seed).ToList());
        output.WriteLine("");
        output.WriteLine("| variant | Age II 10:00 | wave 10:00 | seeds met | Age II 15:00 | expanded | every kind | Anointing | defeated |");
        output.WriteLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var c in configs)
        {
            List<Row> g = rows.Where(r => r.Variant == c.Name).ToList();
            int met = g.GroupBy(r => r.Seed).Count(s => s.Count(r => r.Age10) >= 6 && s.Count(r => r.Wave10) >= 6);
            output.WriteLine($"| {c.Name} | {g.Count(r => r.Age10)} | {g.Count(r => r.Wave10)} | {met} of 5 | {g.Count(r => r.AgeTwoAt >= 0)} | {g.Count(r => r.ExpandedAt >= 0)} | "
                + $"{g.Count(r => r.NeverBuilt == "-")} | {g.Count(r => r.AnointedAt >= 0)} | {g.Count(r => r.DefeatedAt >= 0)} |");
        }
        output.WriteLine("");
        foreach (string v in new[] { "hand-over", "V14" })
        {
            int lostAge = 0, lostWave = 0, worse = 0, better = 0;
            foreach (Row a in rows.Where(r => r.Variant == "V0"))
            {
                Row b = rows.Single(r => r.Variant == v && r.Seed == a.Seed && r.Map == a.Map && r.Opponent == a.Opponent);
                if (a.Age10 && !b.Age10) lostAge++;
                if (a.Wave10 && !b.Wave10) lostWave++;
                if (a.DefeatedAt < 0 && b.DefeatedAt >= 0) worse++;
                if (a.DefeatedAt >= 0 && b.DefeatedAt < 0) better++;
            }
            output.WriteLine($"- {v} vs V0: Age II by 10:00 lost {lostAge}, wave by 10:00 lost {lostWave}, defeated on {v} only {worse}, on V0 only {better}");
        }
        Assert.Equal(120, rows.Count);
    }

    /// <summary>The developer's request (a per-opening reserve share) checked with the existing global field on a data copy.</summary>
    [Fact]
    public void ReserveShareHalf_OnTheEightPopWave_Seeds1To3_Report()
    {
        JsonObject eight = TeblorOpeningVariantsReportTests.Fixture();
        eight["attack"] = new JsonObject { ["minArmyPop"] = 8, ["minArmyCost"] = 600 };
        GameData Make(JsonObject opening, double share) => AiScenes.DataWith(dir =>
        {
            dir.EditJson("factions/teblor/ai.json", root => root["openings"]![0] = JsonNode.Parse(opening.ToJsonString()));
            dir.EditJson("common/rules.json", root => root["ai"]!["reserveArmyShare"] = share);
        });
        var configs = new (string Name, GameData Data)[]
        {
            ("V4-8/600 share 1", Make(eight, 1)),
            ("V4-8/600 share 0.5", Make(eight, 0.5)),
            ("V0 share 0.5", Make(TeblorOpeningVariantsReportTests.Shipped(), 0.5)),
        };
        var jobs = new List<(string, GameData, string, string, ulong)>();
        foreach (var c in configs)
            foreach (ulong seed in new ulong[] { 1, 2, 3 })
                foreach (string m in Maps)
                    foreach (string o in Opponents) jobs.Add((c.Name, c.Data, m, o, seed));
        List<Row> rows = PlayAll(jobs, Ten);
        output.WriteLine("| config | seed | map | opp | Age II | waves by 10:00 |");
        output.WriteLine("| --- | --- | --- | --- | --- | --- |");
        foreach (Row r in rows) output.WriteLine($"| {r.Variant} | {r.Seed} | {r.Map} | {r.Opponent} | {T(r.AgeTwoAt)} | {r.WavesAt10} |");
        output.WriteLine("");
        output.WriteLine("| config | seed | Age II by 10:00 | wave by 10:00 |");
        output.WriteLine("| --- | --- | --- | --- |");
        foreach (var g in rows.GroupBy(r => (r.Variant, r.Seed)))
            output.WriteLine($"| {g.Key.Variant} | {g.Key.Seed} | {g.Count(r => r.Age10)} | {g.Count(r => r.Wave10)} |");
        Assert.Equal(72, rows.Count);
    }
}
