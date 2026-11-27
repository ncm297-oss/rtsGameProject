using Rts.Sim.Ai;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Tests.Draw;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// M6-S13 criterion 10 (measured and reported, not pass / fail: no AI cure is in scope, rule 48) and the AI half of criteria 6
/// and 8: the stalled AI matches on the shipped data (the draw rule on, 300 s): for each, the first spent tick, how often the
/// clock restarted, the ticks spent in all, and the end (winner or draw, tick) or none by the bound. Every row prints one line
/// beginning <c>DRAWROW</c>. The rows that draw also run their twin with the wait out of reach (hash-equal at every tick up to
/// the draw) and run a second time (the same draw tick and hash).
/// </summary>
public class DrawStalledRowsReportTests
{
    private readonly ITestOutputHelper _out;

    public DrawStalledRowsReportTests(ITestOutputHelper output) => _out = output;

    private static string Clock(int ticks) => $"{ticks / 1200}:{ticks / 20 % 60:D2}";

    internal readonly record struct Outcome(int End, bool Over, bool Drawn, string Winner, int FirstSpent, int Restarts, int SpentTicks, ulong Hash, ulong Fold);

    internal static Simulation Build(string map, ulong seed, int players, int[]? teams, GameData? data = null)
    {
        var seats = new PlayerSeat[players];
        for (int p = 0; p < players; p++) seats[p] = new PlayerSeat(p % 2, SeatController.Ai, AiDifficulty.Normal, teams == null ? 0 : teams[p]);
        if (data == null) return HuntStressTests.Match(map, seats, seed, units: players > 2 ? 1024 : 400);
        int size = TestSim.Data.Maps[TestSim.Data.FindMap(map)].Size;
        return AiScenes.Generated(seats, seed, false, out _, units: players > 2 ? 1024 : 400, data: data,
            configure: c => c with { MapId = map, Map = c.Map with { Width = size, Height = size } });
    }

    /// <summary>Plays <paramref name="sim"/> to its end or <paramref name="bound"/>, watching the draw clock; <paramref name="hashes"/> collects every tick's hash when given.</summary>
    internal static Outcome Play(Simulation sim, int bound, List<ulong>? hashes = null)
    {
        World w = sim.World;
        int firstSpent = -1, restarts = 0, spentTicks = 0, prev = 0;
        var fold = new StateHasher();
        while (!sim.MatchOver && sim.TickNumber < bound)
        {
            sim.Tick();
            ulong h = sim.StateHash();
            fold.Add(h);
            hashes?.Add(h);
            if (w.DrawClock > 0)
            {
                spentTicks++;
                if (firstSpent < 0) firstSpent = sim.TickNumber;
            }
            if (w.DrawClock == 0 && prev > 0 && !sim.MatchOver) restarts++;
            prev = w.DrawClock;
        }
        string winner = "-";
        if (sim.MatchOver && !w.MatchDrawn)
        {
            var standing = new List<string>();
            for (int p = 0; p < w.Config.PlayerCount; p++)
                if (w.EverBuilt(p) && !w.IsDefeated(p)) standing.Add($"p{p}");
            winner = string.Join("+", standing);
        }
        return new Outcome(sim.TickNumber, sim.MatchOver, w.MatchDrawn, winner, firstSpent, restarts, spentTicks, sim.StateHash(), fold.Value);
    }

    private string Line(string name, Outcome o, Simulation sim)
    {
        World w = sim.World;
        string end = !o.Over ? $"no end by {o.End} ({Clock(o.End)})" : o.Drawn ? $"DRAW at {o.End} ({Clock(o.End)})" : $"won by {o.Winner} at {o.End} ({Clock(o.End)})";
        var seats = new List<string>();
        for (int p = 0; p < w.Config.PlayerCount; p++)
            seats.Add($"p{p} {(w.IsDefeated(p) ? "out" : "in")} gold {w.Gold[p]} units {w.LiveUnits(p)} workers {w.Ledger.LiveWorkers[p]} prod {w.ProductionBuildings(p)} train {(w.CanStillTrain(p) ? "yes" : "no")}");
        string line = $"DRAWROW {name}: first spent {(o.FirstSpent < 0 ? "never" : $"{o.FirstSpent} ({Clock(o.FirstSpent)})")}, restarts {o.Restarts}, spent ticks {o.SpentTicks}, {end}; " +
            $"gold left on the map {w.Resources.GoldLeft}; {string.Join("; ", seats)}; hash {o.Hash:X16}";
        _out.WriteLine(line);
        return line;
    }

    public static TheoryData<string, ulong, int, string> Rows() => new()
    {
        { "pale_hills", 1UL, 4, "" },
        { "pale_hills", 2UL, 4, "" },
        { "pale_hills", 3UL, 4, "" },
        { "pale_hills", 4UL, 4, "" },
        { "pale_hills", 6UL, 4, "" },
        { "pale_hills", 7UL, 4, "" },
        { "vathar_crossing", 2UL, 4, "" },
        { "pale_hills", 2UL, 4, "1,2,1,2" },
    };

    /// <summary>
    /// The four-seat Pale Hills free-for-all seeds 1, 3, 4, 6, 7 (BUG-1068 / 1116 / 1559 / 1536) and seed 2 (the oracle row, a pin that moved), the Vathar four-seat seed 2
    /// (BUG-1621 / BUG-1610) and the Pale Hills 2v2 seed 2 (BUG-1149's match), to 144,000. A row that draws is also run with the
    /// wait out of reach (its twin: every tick's hash equal up to the draw tick) and once more (the same end).
    /// </summary>
    [Theory]
    [Trait("Category", "Stress")]
    [MemberData(nameof(Rows))]
    public void AStalledRow_Measured(string map, ulong seed, int players, string teams)
    {
        int[]? t = teams.Length == 0 ? null : teams.Split(',').Select(int.Parse).ToArray();
        string name = $"{map} seed {seed} {players} seats{(t == null ? " ffa" : " " + teams)}";
        var hashes = new List<ulong>();
        Simulation sim = Build(map, seed, players, t);
        Outcome o = Play(sim, 144_000, hashes);
        Line(name, o, sim);
        if (!o.Drawn) return;
        // The twin: the wait out of reach, every tick's hash equal before the draw tick.
        Simulation twin = Build(map, seed, players, t, DrawScenes.OutOfReach);
        for (int k = 0; k < hashes.Count - 1; k++)
        {
            twin.Tick();
            Assert.True(twin.StateHash() == hashes[k], $"{name}: the twin differs at tick {twin.TickNumber}, before the draw at {o.End}");
        }
        twin.Tick();
        Assert.NotEqual(hashes[^1], twin.StateHash());
        Assert.False(twin.MatchOver);
        // Run again: the same draw tick, hash and fold.
        Outcome again = Play(Build(map, seed, players, t), 144_000);
        Assert.Equal((o.End, o.Hash, o.Fold), (again.End, again.Hash, again.Fold));
        _out.WriteLine($"DRAWROW {name}: twin hash-equal for {hashes.Count - 1} ticks; run twice: the same draw");
    }

    /// <summary>
    /// Criterion 11 (rule 43): the Hard v Normal tally's matches that did not end with the Normal seat defeated
    /// (<c>HardVersusNormalTallyQaStressTests</c> counts a draw as "normal"): each to the tally's 72,000, the first spent tick and
    /// the end. A match spent before 72,000 would have to be re-tallied as a draw or unfinished.
    /// </summary>
    [Theory]
    [Trait("Category", "Stress")]
    [InlineData("pale_hills", 7UL, 1)]
    [InlineData("pale_hills", 8UL, 1)]
    [InlineData("pale_hills", 2UL, 0)]
    [InlineData("pale_hills", 3UL, 0)]
    [InlineData("pale_hills", 6UL, 0)]
    [InlineData("pale_hills", 7UL, 0)]
    [InlineData("pale_hills", 8UL, 0)]
    [InlineData("raraku", 1UL, 1)]
    public void ATallyMatchWithoutAHardWin_Measured(string map, ulong seed, int hardSeat)
    {
        AiDifficulty a = hardSeat == 0 ? AiDifficulty.Hard : AiDifficulty.Normal, b = hardSeat == 0 ? AiDifficulty.Normal : AiDifficulty.Hard;
        PlayerSeat[] seats = { new(0, SeatController.Ai, a), new(1, SeatController.Ai, b) };
        int size = TestSim.Data.Maps[TestSim.Data.FindMap(map)].Size;
        Simulation sim = map == "raraku"
            ? AiScenes.Generated(seats, seed, false, out _, configure: c => c with { MapId = map })
            : AiScenes.Generated(seats, seed, false, out _, configure: c => c with { MapId = map, Map = c.Map with { Width = size, Height = size } });
        Outcome o = Play(sim, 72_000);
        Line($"tally {map} seed {seed} hard seat {hardSeat}", o, sim);
    }

    /// <summary>
    /// BUG-1116's scene (<c>EndGameHardeningTests.ALoneIdleWorker_*</c>, skipped): an AI seat with its hall and gold against a
    /// lone worker of a seat whose buildings are gone. Measured: is it ever spent in the row's 20,000 ticks (expected no: the
    /// AI seat keeps its hall and its bank, so it can train).
    /// </summary>
    [Fact]
    [Trait("Category", "Stress")]
    public void TheLoneIdleWorkerScene_Measured()
    {
        GameData data = AiScenes.DataWith(dir => dir.EditJson("factions/malazan/ai.json", root =>
        {
            foreach (System.Text.Json.Nodes.JsonNode? o in root["openings"]!.AsArray())
            {
                AiScenes.SetBuildOrder(o, AiScenes.BuildOrder(("time", 3600, "train", "malazan_laborer", 1)));
                o["attack"] = new System.Text.Json.Nodes.JsonObject { ["minArmyPop"] = 2, ["minArmyCost"] = 0 };
            }
        }));
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human), data, map: LocalMovementTests.Flat(256));
        World w = sim.World;
        sim.Tick();
        for (int i = 0; i < w.Buildings.Capacity; i++)
            if (w.Buildings.Alive[i] && w.Buildings.Owner[i] == 1) w.Buildings.Free(new Entities.EntityHandle(i, w.Buildings.Generation[i]));
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == 1) w.Units.Free(new Entities.EntityHandle(i, w.Units.Generation[i]));
        CombatScenes.Place(sim, 1, TestSim.Data.FindUnit("whirlwind_camp_follower"), new System.Numerics.Vector2(110f, 100f));
        Outcome o = Play(sim, 20_000);
        Line("BUG-1116 lone idle worker scene", o, sim);
    }
}
