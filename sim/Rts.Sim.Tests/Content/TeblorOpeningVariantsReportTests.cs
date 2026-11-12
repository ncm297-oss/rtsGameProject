using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// D29 (BUG-1241: a Normal Teblor AI still in Age I at 10:00 in 7 of 8 of QA's D28 pairings, no wave in 4): how far
/// <c>ai.json</c> alone moves it. QA's eight pairings (<c>QA/Content/TeblorAiMatchQaStressTests</c>: the generated map,
/// Raraku, Pale Hills and Vathar Crossing; the Teblor on seat 0 against a Malazan, on seat 1 against a Whirlwind; Normal
/// against Normal; 12,000 ticks) on seeds 1, 2 and 3, on a copy of the shipped data whose Teblor <c>standard</c> opening is
/// replaced by a variant (<see cref="Variants"/>). Each row prints the tick Age II completes, the waves started, the
/// Gatherers alive and the gold and wood in hand at 7:00 and 10:00; each variant and seed the count of rows with Age II by
/// 10:00 and with a wave, against the target of 6 of 8. A measurement (rule 48): only its preconditions are asserted (the
/// seat plays the Teblor, every row ran 12,000 ticks). The best variant (<c>Fixtures/teblor_standard_d29.json</c>, V4) is
/// also played with the seats swapped beside the shipped one, and one failing row is printed minute by minute (BUG-1241's
/// table). The AI plays as in a match: the rows change data only, never the AI's state.
/// </summary>
[Trait("Category", "Stress")]
public class TeblorOpeningVariantsReportTests(ITestOutputHelper output)
{
    private const int Tps = SimConstants.TicksPerSecond;
    private const int Bound = 12_000;
    private const int Seven = 7 * 60 * Tps;
    private const int Target = 6;

    private static readonly string[] Maps = { "generated", "raraku", "pale_hills", "vathar_crossing" };
    private static readonly string[] Opponents = { "malazan", "whirlwind" };
    private static readonly ulong[] Seeds = { 1, 2, 3 };

    /// <summary>The best variant's opening, the file D29 hands over (the shipped <c>standard</c> replaced by it whole).</summary>
    internal static string FixturePath => Path.Combine(TestDataDir.RepoRoot(), "sim", "Rts.Sim.Tests", "Content", "Fixtures", "teblor_standard_d29.json");

    /// <summary>The shipped Teblor <c>standard</c> opening, parsed afresh (each variant edits its own copy).</summary>
    internal static JsonObject Shipped()
    {
        JsonNode root = JsonNode.Parse(File.ReadAllText(Path.Combine(TestDataDir.Shipped, "factions", "teblor", "ai.json")))!;
        JsonObject opening = root["openings"]![0]!.AsObject();
        Assert.Equal("standard", (string)opening["id"]!);
        return JsonNode.Parse(opening.ToJsonString())!.AsObject();
    }

    /// <summary>The fixture's opening.</summary>
    internal static JsonObject Fixture() => JsonNode.Parse(File.ReadAllText(FixturePath))!.AsObject();

    /// <summary>A copy of the shipped data whose Teblor opening 0 is <paramref name="opening"/> (the loader's errors fail it).</summary>
    internal static GameData DataWith(JsonObject opening) =>
        AiScenes.DataWithAi("teblor", root => root["openings"]![0] = JsonNode.Parse(opening.ToJsonString()));

    /// <summary>The step of <paramref name="o"/> that does <paramref name="what"/> to <paramref name="id"/> (the first, or the <paramref name="nth"/>).</summary>
    private static JsonObject Step(JsonObject o, string what, string id, int nth = 0)
    {
        foreach (JsonNode? s in o["buildOrder"]!.AsArray())
            if ((string)s!["do"]! == what && (string?)s["id"] == id && nth-- == 0) return s.AsObject();
        throw new InvalidOperationException($"no {what} {id} step");
    }

    /// <summary>Moves <paramref name="step"/> to the end of the build order with a time trigger <paramref name="time"/>.</summary>
    private static void ToEnd(JsonObject o, JsonObject step, int time)
    {
        JsonArray order = o["buildOrder"]!.AsArray();
        order.Remove(step);
        step["at"] = new JsonObject { ["time"] = time };
        order.Add(step);
    }

    /// <summary>
    /// The variants (the brief's): V0 shipped; V1 the eight Gatherers (step 19) after the Age II step in time (480: Age II's
    /// research is 60 s); V2 Armor I and the eight Gatherers after every other step (time 600 and last in the file, the
    /// Producer's probe); V3 V2 with Age II due at 5:00 instead of 7:00; V4 the fixture: V3, plus the Javelin Ground (wood
    /// only) instead of the Bloodwood Carver as Age II's second kind (the Carver at 8:00, after Age II), the expansion and the
    /// Stables last at 10:00, 9 Gatherers at the start instead of 11, and the wave at 4 army pop / 300 instead of 8 / 600.
    /// Why the wave: the M5-4g reserve is held only while the army is at least one wave (<c>AiPlayer.MayReserve</c>), so the
    /// threshold is the one file field that decides when Age II's cost is saved (the D29 session report has the sweep).
    /// </summary>
    internal static (string Name, JsonObject Opening)[] Variants()
    {
        JsonObject v1 = Shipped();
        Step(v1, "train", "teblor_gatherer", 1)["at"] = new JsonObject { ["time"] = 480 };

        JsonObject v2 = Shipped();
        ToEnd(v2, Step(v2, "research", "armor_1"), 600);
        ToEnd(v2, Step(v2, "train", "teblor_gatherer", 1), 600);

        JsonObject v3 = JsonNode.Parse(v2.ToJsonString())!.AsObject();
        Step(v3, "research", "age_ii")["at"] = new JsonObject { ["time"] = 300 };

        return new[] { ("V0", Shipped()), ("V1", v1), ("V2", v2), ("V3", v3), ("V4", Fixture()) };
    }

    /// <summary>One match's figures for the Teblor seat (ticks; -1: never).</summary>
    internal sealed record Row(string Variant, ulong Seed, string Map, string Opponent, int Seat, int AgeTwoAt, int Waves, int FirstWaveAt,
        int Gatherers7, int Gatherers10, int Gold7, int Wood7, int Gold10, int Wood10, int DefeatedAt, int OtherDefeatedAt, List<string> Minutes)
    {
        public bool AgeTwo => AgeTwoAt >= 0;
        public bool Waved => Waves > 0;
    }

    /// <summary>The seats: the Teblor on seat 0 against the Malazan and on seat 1 against the Whirlwind, or the reverse when <paramref name="swapped"/>.</summary>
    private static (PlayerSeat[] Seats, int Teblor) Seats(GameData data, string opponent, bool swapped)
    {
        PlayerSeat t = new(data.FindFaction("teblor"), SeatController.Ai, AiDifficulty.Normal);
        PlayerSeat o = new(data.FindFaction(opponent), SeatController.Ai, AiDifficulty.Normal);
        bool first = (opponent == "malazan") != swapped;
        return first ? (new[] { t, o }, 0) : (new[] { o, t }, 1);
    }

    /// <summary>
    /// <c>HuntStressTests.Match</c>'s set-up (the map, 400 units, each base by command) on <paramref name="data"/>: the same
    /// call to <see cref="AiScenes.Generated"/> with the data copy passed.
    /// </summary>
    private static Simulation Match(GameData data, string map, PlayerSeat[] seats, ulong seed)
    {
        if (map == "generated") return AiScenes.Generated(seats, seed, false, out _, data: data);
        int size = data.Maps[data.FindMap(map)].Size;
        return AiScenes.Generated(seats, seed, false, out _, data: data,
            configure: c => c with { MapId = map, Map = c.Map with { Width = size, Height = size } });
    }

    /// <summary>Plays one row to <see cref="Bound"/>; with <paramref name="minutes"/> it also keeps BUG-1241's per-minute line.</summary>
    internal static Row Play(string variant, GameData data, string map, string opponent, bool swapped, ulong seed, bool minutes = false)
    {
        (PlayerSeat[] seats, int p) = Seats(data, opponent, swapped);
        Simulation sim = Match(data, map, seats, seed);
        World w = sim.World;
        Assert.Equal(data.FindFaction("teblor"), w.FactionOf(p));
        AiPlayer ai = sim.AiOf(p)!;
        Assert.Equal("standard", ai.Opening.Id);
        int gatherer = data.FindUnit("teblor_gatherer");
        int ageTwo = -1, firstWave = -1, fell = -1, otherFell = -1, g7 = 0, gold7 = 0, wood7 = 0;
        var lines = new List<string>();
        int thinks = 0, gateOpen = 0, defending = 0;
        while (sim.TickNumber < Bound)
        {
            sim.Tick();
            int t = sim.TickNumber;
            if (minutes && (t - 1) % ai.ThinkTicks == 0)
            {
                // Read after the think of tick t - 1: whether the M5-4g gate (AiPlayer.MayReserve) was open, and the defense flag.
                thinks++;
                if (ai.MayReserve()) gateOpen++;
                if (ai.Board.Defending) defending++;
            }
            if (ageTwo < 0 && w.Age(p) >= 2) ageTwo = t;
            if (firstWave < 0 && ai.Board.WavesStarted > 0) firstWave = t;
            if (fell < 0 && w.IsDefeated(p)) fell = t;
            if (otherFell < 0 && w.IsDefeated(1 - p)) otherFell = t;
            if (t == Seven) (g7, gold7, wood7) = (Count(w, p, gatherer), w.Gold[p], w.Wood[p]);
            if (minutes && t % (60 * Tps) == 0)
                lines.Add($"| {t / Tps / 60}:00 | {w.Gold[p]} / {w.Wood[p]} | {w.GatheredGold[p]} / {w.GatheredWood[p]} | {w.GatheredGold[1 - p]} / {w.GatheredWood[1 - p]} | "
                    + $"{Count(w, p, gatherer)} | {Army(w, p) / 2.0} | {w.Age(p)}{(w.Age(p) < 2 && ai.TechResearchedOrQueued(data.FindTech("age_ii")) ? " (Age II queued)" : "")} | {ai.Board.WavesStarted} | "
                    + $"{gateOpen} / {thinks} | {defending} / {thinks} |");
            if (minutes && t % (60 * Tps) == 0) thinks = gateOpen = defending = 0;
        }
        Assert.Equal(Bound, sim.TickNumber);
        return new Row(variant, seed, map, opponent, p, ageTwo, ai.Board.WavesStarted, firstWave, g7, Count(w, p, gatherer),
            gold7, wood7, w.Gold[p], w.Wood[p], fell, otherFell, lines);
    }

    private static int Count(World w, int player, int type)
    {
        var u = w.Units;
        int n = 0;
        for (int s = 0; s < u.Capacity; s++) if (u.Alive[s] && u.Owner[s] == player && u.TypeId[s] == type) n++;
        return n;
    }

    private static int Army(World w, int player)
    {
        var u = w.Units;
        int half = 0;
        for (int s = 0; s < u.Capacity; s++)
            if (u.Alive[s] && u.Owner[s] == player && w.Data.Units[u.TypeId[s]].Slot != UnitSlot.Worker) half += w.Data.Units[u.TypeId[s]].HalfPop;
        return half;
    }

    /// <summary>Plays every (variant, seed, map, opponent) job, several at once (each match is its own simulation), in job order.</summary>
    private static List<Row> PlayAll(List<(string Variant, GameData Data, string Map, string Opponent, bool Swapped, ulong Seed)> jobs)
    {
        var rows = new Row[jobs.Count];
        var errors = new ConcurrentQueue<string>();
        Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, i =>
        {
            var j = jobs[i];
            try { rows[i] = Play(j.Variant, j.Data, j.Map, j.Opponent, j.Swapped, j.Seed); }
            catch (Exception e) { errors.Enqueue($"{j.Variant} seed {j.Seed} {j.Map} v {j.Opponent}: {e}"); }
        });
        Assert.True(errors.IsEmpty, string.Join("\n", errors));
        return rows.ToList();
    }

    private static string Sec(int tick) => tick < 0 ? "never" : $"{tick / Tps / 60}:{tick / Tps % 60:00}";

    private static string Line(Row r) =>
        $"| {r.Variant} | {r.Seed} | {r.Map} | {r.Opponent} | {r.Seat} | {(r.AgeTwo ? Sec(r.AgeTwoAt) : "not by 12,000")} | {r.Waves} ({Sec(r.FirstWaveAt)}) | "
        + $"{r.Gatherers7} / {r.Gatherers10} | {r.Gold7} / {r.Wood7} | {r.Gold10} / {r.Wood10} | {(r.DefeatedAt < 0 ? "-" : Sec(r.DefeatedAt))} | {(r.OtherDefeatedAt < 0 ? "-" : Sec(r.OtherDefeatedAt))} |";

    private const string Header = "| variant | seed | map | opponent | teblor seat | Age II in | waves (first) | Gatherers 7:00 / 10:00 | gold / wood 7:00 | gold / wood 10:00 | teblor defeated | opponent defeated |";
    private const string Rule = "| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |";

    /// <summary>Prints the rows and the per (variant, seed) tally against the target.</summary>
    private void Print(string title, List<Row> rows)
    {
        output.WriteLine(title);
        output.WriteLine(Header);
        output.WriteLine(Rule);
        foreach (Row r in rows) output.WriteLine(Line(r));
        output.WriteLine("");
        output.WriteLine($"| variant | seed | Age II by 10:00 | a wave | target {Target} of 8 both |");
        output.WriteLine("| --- | --- | --- | --- | --- |");
        foreach (var g in rows.GroupBy(r => (r.Variant, r.Seed)))
        {
            int age = g.Count(r => r.AgeTwo), wave = g.Count(r => r.Waved);
            output.WriteLine($"| {g.Key.Variant} | {g.Key.Seed} | {age} of {g.Count()} | {wave} of {g.Count()} | {(age >= Target && wave >= Target ? "met" : "not met")} |");
        }
        output.WriteLine("");
    }

    /// <summary>
    /// Criteria 1 and 2: V0-V4 on seeds 1-3 (120 matches), the rows and the tally. Asserted: preconditions only (120 rows,
    /// each played to 12,000 ticks with the Teblor on the seat QA's pairing puts it).
    /// </summary>
    [Fact]
    public void VariantsV0ToV4_Seeds1To3_Report()
    {
        var jobs = new List<(string, GameData, string, string, bool, ulong)>();
        foreach ((string name, JsonObject opening) in Variants())
        {
            GameData data = DataWith(opening);
            foreach (ulong seed in Seeds)
                foreach (string map in Maps)
                    foreach (string opp in Opponents) jobs.Add((name, data, map, opp, false, seed));
        }
        var clock = System.Diagnostics.Stopwatch.StartNew();
        List<Row> rows = PlayAll(jobs);
        Print($"D29: Teblor standard variants, QA's eight pairings (teblor seat 0 v malazan, seat 1 v whirlwind), Normal v Normal, {Bound} ticks ({clock.Elapsed.TotalSeconds:0} s wall clock)", rows);
        Assert.Equal(120, rows.Count);
        Assert.All(rows, r => Assert.Equal(r.Opponent == "malazan" ? 0 : 1, r.Seat));
    }

    /// <summary>
    /// The conclusion's evidence, BUG-1241's format: minute by minute, the Teblor's gold / wood in hand, gold / wood gathered
    /// by both sides, Gatherers alive, army pop, age and waves, for BUG-1241's own row on the shipped opening (Raraku v
    /// Malazan, seed 1) and for the same row on the best variant (V4), which still fails it.
    /// </summary>
    [Fact]
    public void FailingRow_PerMinute_Report()
    {
        foreach ((string name, JsonObject opening) in Variants().Where(v => v.Name is "V0" or "V4"))
        {
            Row r = Play(name, DataWith(opening), FailingMap, FailingOpponent, false, FailingSeed, minutes: true);
            output.WriteLine($"{name}, {FailingMap} v {FailingOpponent}, seed {FailingSeed}, teblor seat {r.Seat}: Age II {(r.AgeTwo ? Sec(r.AgeTwoAt) : "not by 12,000")}, waves {r.Waves}");
            output.WriteLine($"| time | Teblor gold / wood in hand | gathered gold / wood (Teblor) | ({FailingOpponent}) | Gatherers | army pop | age | waves | thinks the reserve gate was open (that minute) | thinks defending |");
            output.WriteLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");
            foreach (string line in r.Minutes) output.WriteLine(line);
            output.WriteLine("");
            Assert.Equal(Bound / (60 * Tps), r.Minutes.Count);
        }
    }

    private const string FailingMap = "raraku";
    private const string FailingOpponent = "malazan";
    private const ulong FailingSeed = 1;

    /// <summary>The best variant (V4, the fixture) and the shipped one with the seats swapped (the Teblor on seat 1 against the Malazan, seat 0 against the Whirlwind), seeds 1-3.</summary>
    [Fact]
    public void BestVariantAndShipped_SeatsSwapped_Report()
    {
        var jobs = new List<(string, GameData, string, string, bool, ulong)>();
        foreach ((string name, JsonObject opening) in Variants().Where(v => v.Name is "V0" or "V4"))
        {
            GameData data = DataWith(opening);
            foreach (ulong seed in Seeds)
                foreach (string map in Maps)
                    foreach (string opp in Opponents) jobs.Add((name, data, map, opp, true, seed));
        }
        List<Row> rows = PlayAll(jobs);
        Print($"D29: seats swapped (teblor seat 1 v malazan, seat 0 v whirlwind), Normal v Normal, {Bound} ticks", rows);
        Assert.Equal(48, rows.Count);
        Assert.All(rows, r => Assert.Equal(r.Opponent == "malazan" ? 1 : 0, r.Seat));
    }
}
