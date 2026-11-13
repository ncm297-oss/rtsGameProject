using System.Text.Json.Nodes;
using Rts.Sim.Data;
using Xunit.Abstractions;
using Row = Rts.Sim.Tests.Content.TeblorOpeningHorizonReportTests.Row;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// D30: the Teblor openings re-measured with the Frenzy rows in play, so D31's pick rests on the game as it will ship. The
/// D29 harness (<see cref="TeblorOpeningHorizonReportTests.Play"/>: QA's eight pairings, Normal against Normal, to 15:00)
/// on V0 (shipped), V24 (the hand-over), V25 (V24's family at a 7 pop / 525 wave), V14 (two Warriors, 4 pop / 300) and V24
/// with the Stables at the shipped 8:15, each without and with <see cref="TeblorLanding"/>'s four rows, at the global
/// <c>ai.reserveArmyShare</c> 1 (shipped) and 0.5 (BUG-1301's stand-in for Request 52: it moves the opponent's gate too).
/// Seeds 1-8 (D29 ran 1-5; seeds 2 / 5 and 3 / 4 often repeat on the named maps). A measurement (rule 48): only
/// preconditions are asserted. Frenzy casts per match are the Teblor AI's sent <c>UseAbility</c> commands and the casts that
/// landed.
/// </summary>
[Trait("Category", "Stress")]
public class TeblorLandingOpeningReportTests(ITestOutputHelper output)
{
    private const int Tps = SimConstants.TicksPerSecond;
    private const int Ten = 12_000;

    private static readonly string[] Maps = { "generated", "raraku", "pale_hills", "vathar_crossing" };
    private static readonly string[] Opponents = { "malazan", "whirlwind" };
    private static readonly ulong[] Seeds = { 1, 2, 3, 4, 5, 6, 7, 8 };

    /// <summary>The openings measured, by D29's names, and V24 with the Stables at 8:15 ("V24s").</summary>
    private static (string Name, JsonObject Opening)[] Openings()
    {
        var all = TeblorOpeningHorizonReportTests.Variants();
        JsonObject Get(string name)
        {
            var v = all.Where(x => x.Name == name).ToArray();
            Assert.True(v.Length == 1, $"no variant {name} (is D29_VARIANTS set?)");
            return JsonNode.Parse(v[0].Opening.ToJsonString())!.AsObject();
        }
        JsonObject v24s = Get("V24");
        JsonObject stables = v24s["buildOrder"]!.AsArray().Single(s => (string)s!["do"]! == "build" && (string?)s["id"] == "teblor_stables")!.AsObject();
        Assert.Equal(495, (int)TimeOf(TeblorOpeningVariantsReportTests.Shipped(), "teblor_stables"));
        Assert.NotEqual(495, (int)stables["at"]!["time"]!);
        stables["at"] = new JsonObject { ["time"] = 495 };
        return new[] { ("V0", Get("V0")), ("V24", Get("V24")), ("V25", Get("V25")), ("V14", Get("V14")), ("V24s", v24s) };
    }

    private static double TimeOf(JsonObject opening, string building) =>
        (double)opening["buildOrder"]!.AsArray().Single(s => (string)s!["do"]! == "build" && (string?)s["id"] == building)!["at"]!["time"]!;

    /// <summary>The shipped data with <paramref name="opening"/> as the Teblor's, the landing's rows when <paramref name="rows"/>, and the share.</summary>
    private static GameData Make(JsonObject opening, bool rows, double share) => AiScenes.DataWith(dir =>
    {
        if (rows) TeblorLanding.Edit(dir);
        dir.EditJson("factions/teblor/ai.json", root => root["openings"]![0] = JsonNode.Parse(opening.ToJsonString()));
        dir.EditJson("common/rules.json", root => root["ai"]!["reserveArmyShare"] = share);
    });

    private static string Name(string opening, bool rows) => $"{opening} {(rows ? "+rows" : "-rows")}";

    private List<Row> Run(double share)
    {
        var configs = new List<(string Name, GameData Data)>();
        foreach ((string name, JsonObject opening) in Openings())
            foreach (bool withRows in new[] { false, true })
                configs.Add((Name(name, withRows), Make(opening, withRows, share)));
        foreach (var c in configs.Where(c => c.Name.EndsWith("+rows", StringComparison.Ordinal)))
            Assert.True(c.Data.FindStatus(TeblorLanding.Status) >= 0, c.Name);
        Assert.All(configs, c => Assert.Equal((float)share, c.Data.AiRules.ReserveArmyShare));

        var jobs = new List<(string, GameData, string, string, ulong, bool)>();
        foreach (var c in configs)
            foreach (ulong seed in Seeds)
                foreach (string m in Maps)
                    foreach (string o in Opponents) jobs.Add((c.Name, c.Data, m, o, seed, false));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        List<Row> rows = TeblorOpeningHorizonReportTests.PlayAll(jobs);
        output.WriteLine($"D30, reserveArmyShare {share}: {jobs.Count} matches to 15:00 in {clock.Elapsed.TotalSeconds:0} s (seeds 1-8; teblor seat 0 v malazan, seat 1 v whirlwind; Normal v Normal)");
        output.WriteLine("");
        string[] order = configs.Select(c => c.Name).ToArray();
        Print(rows, order, Seeds, "seeds 1-8");
        Print(rows, order, new ulong[] { 1, 2, 3, 4, 5 }, "seeds 1-5 (D29's)");
        PrintRowsEffect(rows, order);
        PrintPerSeed(rows, order);
        Assert.Equal(configs.Count * Seeds.Length * 8, rows.Count);
        return rows;
    }

    private static bool Met(IEnumerable<Row> seed) => seed.Count(r => r.Age10) >= 6 && seed.Count(r => r.Wave10) >= 6;

    /// <summary>D29's columns per configuration, then the Frenzy casts, the 10-15 minute figures and the Shamans fielded.</summary>
    private void Print(List<Row> all, string[] order, ulong[] seeds, string label)
    {
        output.WriteLine($"Per configuration, {label}. 'Met' = Age II and a wave by 10:00 in at least 6 of 8 on that seed. Army pop sums over the matches.");
        output.WriteLine("| configuration | matches | seeds met | Age II by 10:00 | wave by 10:00 | expanded | every building kind | Anointing | defeated by 15:00 | of them after 10:00 | army pop 10:00 | army pop 15:00 | Shamans fielded | Frenzy casts sent / match | landed / match | matches with a landed cast |");
        output.WriteLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (string v in order)
        {
            List<Row> g = all.Where(r => r.Variant == v && seeds.Contains(r.Seed)).ToList();
            int met = g.GroupBy(r => r.Seed).Count(Met);
            output.WriteLine($"| {v} | {g.Count} | {met} of {seeds.Length} | {g.Count(r => r.Age10)} | {g.Count(r => r.Wave10)} | {g.Count(r => r.ExpandedAt >= 0)} | "
                + $"{g.Count(r => r.NeverBuilt == "-")} | {g.Count(r => r.AnointedAt >= 0)} | {g.Count(r => r.Lost)} | {g.Count(r => r.DefeatedAt > Ten)} | "
                + $"{g.Sum(r => r.Army10) / 2.0} | {g.Sum(r => r.Army15) / 2.0} | {g.Count(r => r.Fielded[1])} | "
                + $"{g.Average(r => r.CastsSent):0.00} | {g.Average(r => r.CastsLanded):0.00} | {g.Count(r => r.CastsLanded > 0)} |");
        }
        output.WriteLine("");
    }

    /// <summary>Pairing by pairing, what the rows change for each opening: defeats gained and lost, and casts where Shamans were fielded.</summary>
    private void PrintRowsEffect(List<Row> all, string[] order)
    {
        output.WriteLine("The rows' effect, pairing by pairing (same opening, seed, map, opponent):");
        foreach (string v in order.Where(o => o.EndsWith("-rows", StringComparison.Ordinal)).Select(o => o[..^6]))
        {
            var lostOnlyWith = new List<string>();
            var lostOnlyWithout = new List<string>();
            int armyWith = 0, armyWithout = 0;
            foreach (Row a in all.Where(r => r.Variant == Name(v, false)))
            {
                Row b = all.Single(r => r.Variant == Name(v, true) && r.Seed == a.Seed && r.Map == a.Map && r.Opponent == a.Opponent);
                string at = $"seed {a.Seed} {a.Map} v {a.Opponent}";
                if (b.Lost && !a.Lost) lostOnlyWith.Add($"{at} ({TeblorOpeningHorizonReportTests.T(b.DefeatedAt)})");
                if (a.Lost && !b.Lost) lostOnlyWithout.Add($"{at} ({TeblorOpeningHorizonReportTests.T(a.DefeatedAt)})");
                armyWith += b.Army15;
                armyWithout += a.Army15;
            }
            List<Row> shamans = all.Where(r => r.Variant == Name(v, true) && r.Fielded[1]).ToList();
            output.WriteLine($"- {v}: defeated with the rows only {lostOnlyWith.Count} [{string.Join("; ", lostOnlyWith)}]; without them only {lostOnlyWithout.Count} "
                + $"[{string.Join("; ", lostOnlyWithout)}]; army pop at 15:00 {armyWithout / 2.0} -> {armyWith / 2.0}; with the rows, in the {shamans.Count} matches that fielded a Shaman: "
                + $"{(shamans.Count == 0 ? "-" : $"{shamans.Average(r => r.CastsSent):0.00} casts sent, {shamans.Average(r => r.CastsLanded):0.00} landed a match")}");
        }
        output.WriteLine("");
    }

    private void PrintPerSeed(List<Row> all, string[] order)
    {
        output.WriteLine("Per configuration and seed (8 pairings each): Age II 10:00 / wave 10:00 / defeated 15:00 / army pop 15:00 / casts landed.");
        output.WriteLine("| configuration | " + string.Join(" | ", Seeds.Select(s => $"seed {s}")) + " |");
        output.WriteLine("| --- " + string.Concat(Seeds.Select(_ => "| --- ")) + "|");
        foreach (string v in order)
        {
            var cells = Seeds.Select(s =>
            {
                List<Row> g = all.Where(r => r.Variant == v && r.Seed == s).ToList();
                return $"{g.Count(r => r.Age10)} / {g.Count(r => r.Wave10)} / {g.Count(r => r.Lost)} / {g.Sum(r => r.Army15) / 2.0} / {g.Sum(r => r.CastsLanded)}";
            });
            output.WriteLine($"| {v} | " + string.Join(" | ", cells) + " |");
        }
        output.WriteLine("");
    }

    /// <summary>
    /// The precondition behind "the rows change nothing": they act in this harness, from the first cast on and not before. The
    /// shipped opening (V0, the one that casts most) at share 0.5, seed 3's eight pairings, without and with the rows, in
    /// lockstep: each pair's state hashes are equal until the first tick a Teblor unit carries the Frenzy, and differ from
    /// that tick on. Printed: when each pair first casts and diverges.
    /// </summary>
    [Fact]
    public void TheRows_ChangeTheMatch_FromTheFirstFrenzyOn_Report()
    {
        JsonObject v0 = Openings().Single(o => o.Name == "V0").Opening;
        GameData off = Make(v0, false, 0.5), on = Make(v0, true, 0.5);
        int frenzyOn = on.FindStatus(TeblorLanding.Status), frenzyOff = off.FindStatus("frenzied");
        var jobs = Maps.SelectMany(m => Opponents.Select(o => (Map: m, Opponent: o))).ToArray();
        var lines = new string[jobs.Length];
        var firsts = new (int Cast, int Diverged)[jobs.Length];
        Parallel.For(0, jobs.Length, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, i =>
        {
            Simulation a = Start(off, jobs[i].Map, jobs[i].Opponent, 3, out int p);
            Simulation b = Start(on, jobs[i].Map, jobs[i].Opponent, 3, out _);
            int diverged = -1, castOff = -1, castOn = -1;
            while (a.TickNumber < 18_000)
            {
                a.Tick();
                b.Tick();
                if (diverged < 0 && a.StateHash() != b.StateHash()) diverged = a.TickNumber;
                if (castOff < 0 && Carries(a.World, p, frenzyOff)) castOff = a.TickNumber;
                if (castOn < 0 && Carries(b.World, p, frenzyOn)) castOn = b.TickNumber;
            }
            Assert.Equal(castOff, castOn); // the same match up to the first Frenzy, so the same first cast
            firsts[i] = (castOn, diverged);
            lines[i] = $"- seed 3 {jobs[i].Map} v {jobs[i].Opponent}: first Frenzy on a Teblor unit {TeblorOpeningHorizonReportTests.T(castOn)}, the two runs diverge {TeblorOpeningHorizonReportTests.T(diverged)}";
        });
        output.WriteLine("V0, share 0.5, seed 3, without and with the rows in lockstep:");
        foreach (string l in lines) output.WriteLine(l);
        Assert.All(firsts, f => Assert.Equal(f.Cast, f.Diverged));
        Assert.Contains(firsts, f => f.Cast >= 0); // not vacuous: some pair casts
    }

    private static Simulation Start(GameData data, string map, string opponent, ulong seed, out int p)
    {
        PlayerSeat t = new(data.FindFaction("teblor"), SeatController.Ai, AiDifficulty.Normal);
        PlayerSeat o = new(data.FindFaction(opponent), SeatController.Ai, AiDifficulty.Normal);
        bool first = opponent == "malazan";
        PlayerSeat[] seats = first ? new[] { t, o } : new[] { o, t };
        p = first ? 0 : 1;
        if (map == "generated") return AiScenes.Generated(seats, seed, false, out _, data: data);
        int size = data.Maps[data.FindMap(map)].Size;
        return AiScenes.Generated(seats, seed, false, out _, data: data,
            configure: c => c with { MapId = map, Map = c.Map with { Width = size, Height = size } });
    }

    /// <summary>Whether a live unit of <paramref name="player"/> carries status <paramref name="status"/>.</summary>
    private static bool Carries(World w, int player, int status)
    {
        var u = w.Units;
        for (int s = 0; s < u.Capacity; s++)
        {
            if (!u.Alive[s] || u.Owner[s] != player) continue;
            for (int k = 0; k < u.Statuses.Count[s]; k++)
                if (u.Statuses.StatusId[s * Abilities.StatusStore.PerUnit + k] == status) return true;
        }
        return false;
    }

    /// <summary>The five openings without and with the rows at the shipped share (1): 640 matches.</summary>
    [Fact]
    public void Openings_WithAndWithoutTheFrenzyRows_Share1_Seeds1To8_Report() => Run(1.0);

    /// <summary>The same at the global reserve share 0.5 (BUG-1301's stand-in for Request 52's per-opening share): 640 matches.</summary>
    [Fact]
    public void Openings_WithAndWithoutTheFrenzyRows_Share05_Seeds1To8_Report() => Run(0.5);
}
