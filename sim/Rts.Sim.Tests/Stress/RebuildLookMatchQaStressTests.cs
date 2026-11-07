using System.Numerics;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA, M5-4d slice 2 (session 2026-11-07-0236), criterion 4 at match scale: four Normal AI seats on Pale Hills and on
/// Vathar Crossing, QA's <see cref="PlayerViewOracle"/> on <b>every</b> think of every seat (not only hunting ones), and
/// each look point and hall rebuild checked as it is made: a look point is set only on a building the seat knows (seen or
/// a ghost), on that building's own plateau, at a ramp top (a ramp cell one level below beside it), with the building's
/// cell not visible to the seat; a hall rebuild's Build goes to the anchor the seat's own census remembered
/// (<see cref="AiPlayer.LostHallCell"/>) or to a cell the seat has explored. Bounded at 36,000 ticks (the 30-minute mark).
/// </summary>
public class RebuildLookMatchQaStressTests
{
    private readonly ITestOutputHelper _out;

    public RebuildLookMatchQaStressTests(ITestOutputHelper output) => _out = output;

    private sealed class SeatProbe
    {
        public Vector2 LastPoint;
        public bool WasLooking;
        public bool[] Done = Array.Empty<bool>();
        public int Looks, Handovers, Reopens, HallBuilds, HallBuildsAtAnchor;
    }

    [Theory]
    [Trait("Category", "Stress")]
    [InlineData("pale_hills", 1UL)]
    [InlineData("vathar_crossing", 1UL)]
    public void FourAiSeats_EveryThinkOracleClean_EveryLookAndHallRebuildFromTheSeatsOwnKnowledge(string map, ulong seed)
    {
        var seats = new PlayerSeat[4];
        for (int p = 0; p < 4; p++) seats[p] = new PlayerSeat(p % 2, SeatController.Ai, AiDifficulty.Normal);
        Simulation sim = HuntStressTests.Match(map, seats, seed, units: 1024);
        World w = sim.World;
        NavGrid g = w.NavGrid;
        var tally = new PlayerViewOracle.Tally();
        var probes = new SeatProbe[4];
        string? bad = null;
        for (int p = 0; p < 4; p++)
        {
            int player = p;
            AiPlayer seat = sim.AiOf(p)!;
            var probe = probes[p] = new SeatProbe { Done = new bool[seat.Opening.BuildOrder.Length] };
            seat.SentForTests = c =>
            {
                if (bad != null || c.Kind != CommandKind.Build || c.TypeId != seat.TownHallType || seat.TownHallCell >= 0) return;
                // A hall Build while the seat has no hall: the rebuild (HallFirst). Its anchor is the census's memory or explored ground.
                probe.HallBuilds++;
                int lost = seat.LostHallCell;
                if (lost >= 0 && c.Position == seat.CellCentre(lost)) { probe.HallBuildsAtAnchor++; return; }
                if (!g.WorldToCell(c.Position, out int x, out int y) || !seat.View.IsExplored(y * g.Width + x))
                    bad = $"tick {seat.Now} player {player}: hall rebuild at {c.Position}, neither the remembered anchor {lost} nor explored ground";
            };
            seat.ThoughtForTests = a =>
            {
                if (bad != null) return;
                PlayerViewOracle.Check(w, a.View, player, tally, $"{map} seed {seed} tick {a.Now} player {player}");
                if (tally.First.Length > 0) { bad = tally.First; return; }
                for (int i = 0; i < probe.Done.Length; i++)
                {
                    if (probe.Done[i] && !a.Board.StepDone[i]) probe.Reopens++;
                    probe.Done[i] = a.Board.StepDone[i];
                }
                bool looking = MilitaryManager.Looking(a);
                if (probe.WasLooking && !looking && a.Board.WaveActive && !a.Board.Hunting) probe.Handovers++;
                if (looking && (!probe.WasLooking || a.Board.WaveTargetPoint != probe.LastPoint))
                {
                    probe.Looks++;
                    int target = a.Board.WaveTargetCell;
                    if (!a.EnemyBuildingKnown(a.Board.WaveTarget, out _)) bad = $"tick {a.Now} player {player}: a look point on a building it doesn't know";
                    else if (!g.WorldToCell(a.Board.WaveTargetPoint, out int lx, out int ly)) bad = $"tick {a.Now} player {player}: look point off the map";
                    else
                    {
                        int lc = ly * g.Width + lx, level = a.View.LevelAt(lc);
                        bool rampBelow = false;
                        foreach (int n in new[] { lc - 1, lc + 1, lc - g.Width, lc + g.Width })
                            rampBelow |= a.View.IsRamp(n) && a.View.LevelAt(n) == level - 1;
                        if (a.View.PlateauAt(lc) != a.View.PlateauAt(target) || a.View.LevelAt(target) != level || !rampBelow)
                            bad = $"tick {a.Now} player {player}: look ({lx}, {ly}) level {level} is not a ramp top of the target's plateau (target cell {target} level {a.View.LevelAt(target)})";
                        else if (!probe.WasLooking && a.View.IsVisible(target))
                            bad = $"tick {a.Now} player {player}: a look point though the target's cell is visible";
                    }
                }
                probe.WasLooking = looking;
                probe.LastPoint = a.Board.WaveTargetPoint;
            };
        }
        while (sim.TickNumber < 36_000 && !sim.MatchOver && bad == null)
        {
            sim.Tick();
            for (int p = 0; p < 4; p++) PlayerViewOracle.NodeOracle.For(w).Observe(w, p, tally, $"{map} seed {seed} tick {sim.TickNumber} player {p}");
            if (tally.First.Length > 0) bad ??= tally.First;
        }
        for (int p = 0; p < 4; p++)
        {
            SeatProbe s = probes[p];
            _out.WriteLine($"{map} seed {seed} p{p}: looks {s.Looks}, hand-overs {s.Handovers}, steps reopened {s.Reopens}, hall rebuild Builds {s.HallBuilds} ({s.HallBuildsAtAnchor} at the old anchor); defeated {w.IsDefeated(p)}");
        }
        _out.WriteLine($"ended at {sim.TickNumber}, over {sim.MatchOver}; {tally}");
        Assert.True(bad == null, bad);
        Assert.Equal(0, tally.Leaks);
        Assert.True(probes.Sum(s => s.Looks + s.Reopens + s.HallBuilds) > 0, "nothing looked or rebuilt: the row checks nothing");
    }

    private const string TwinVariable = "RTS_QA_REBUILD_MATCH_TWIN_CHILD";
    private const string TwinLine = "QA-REBUILD-MATCH-TWIN ";

    /// <summary>
    /// Criterion 6 on a real match: four Normal AI seats on Pale Hills seed 2 (free-for-all) to tick 40,000, a match whose
    /// losing seats reopen dozens of build steps and where a seat loses its hall (tick 38,240 at 8155ee4). This process
    /// and a fresh child <c>dotnet test</c> of this row fold the same per-tick hashes.
    /// </summary>
    [Fact]
    [Trait("Category", "Stress")]
    public void PaleHillsSeed2_ARebuildingMatch_TwoProcesses_SamePerTickHashes()
    {
        var seats = new PlayerSeat[4];
        for (int p = 0; p < 4; p++) seats[p] = new PlayerSeat(p % 2, SeatController.Ai, AiDifficulty.Normal);
        Simulation sim = HuntStressTests.Match("pale_hills", seats, 2, units: 1024);
        int reopens = 0;
        bool hallLost = false;
        for (int p = 0; p < 4; p++)
        {
            AiPlayer seat = sim.AiOf(p)!;
            var done = new bool[seat.Opening.BuildOrder.Length];
            seat.ThoughtForTests = a =>
            {
                for (int i = 0; i < done.Length; i++) { if (done[i] && !a.Board.StepDone[i]) reopens++; done[i] = a.Board.StepDone[i]; }
                hallLost |= a.LostHallCell >= 0;
            };
        }
        var h = new Rts.Sim.Determinism.StateHasher();
        while (sim.TickNumber < 40_000)
        {
            sim.Tick();
            h.Add(sim.StateHash());
        }
        Assert.True(reopens > 0 && hallLost, $"reopens {reopens}, hall lost {hallLost}: not a rebuilding match");
        string mine = $"{h.Value:X16} {sim.StateHash():X16} {reopens}";
        if (Environment.GetEnvironmentVariable(TwinVariable) == "1")
        {
            _out.WriteLine(TwinLine + mine);
            return;
        }
        Assert.Equal(mine, MapFileTests.RunChild(typeof(RebuildLookMatchQaStressTests), nameof(PaleHillsSeed2_ARebuildingMatch_TwoProcesses_SamePerTickHashes), TwinVariable, TwinLine, seconds: 600));
    }

    /// <summary>
    /// Criterion 5 on a real match: Pale Hills seed 2, saved at the first think past tick 30,000 at which a seat holds a
    /// reopened (due-again) step not yet done again; the load follows the run hash for hash for 1,000 ticks and resaves
    /// byte-identical.
    /// </summary>
    [Fact]
    [Trait("Category", "Stress")]
    public void PaleHillsSeed2_SavedWithADueAgainStep_FollowsTheRun()
    {
        var seats = new PlayerSeat[4];
        for (int p = 0; p < 4; p++) seats[p] = new PlayerSeat(p % 2, SeatController.Ai, AiDifficulty.Normal);
        Simulation sim = HuntStressTests.Match("pale_hills", seats, 2, units: 1024);
        bool pending = false;
        var everDone = new bool[4][];
        for (int p = 0; p < 4; p++)
        {
            int player = p;
            AiPlayer seat = sim.AiOf(p)!;
            everDone[p] = new bool[seat.Opening.BuildOrder.Length];
            seat.ThoughtForTests = a =>
            {
                for (int i = 0; i < everDone[player].Length; i++)
                {
                    if (everDone[player][i] && !a.Board.StepDone[i] && a.Now > 30_000) pending = true;
                    everDone[player][i] |= a.Board.StepDone[i];
                }
            };
        }
        while (!pending && sim.TickNumber < 60_000) sim.Tick();
        Assert.True(pending, "no seat held a due-again step past tick 30,000");
        for (int p = 0; p < 4; p++) sim.AiOf(p)!.ThoughtForTests = null;
        _out.WriteLine($"saved at tick {sim.TickNumber}");
        byte[] bytes = Rts.Sim.Tests.Save.SaveTestRun.Save(sim);
        Simulation load = Rts.Sim.Tests.Save.SaveTestRun.Load(TestSim.Data, bytes);
        Assert.Equal(bytes, Rts.Sim.Tests.Save.SaveTestRun.Save(load));
        for (int t = 0; t < 1000; t++)
        {
            sim.Tick();
            load.Tick();
            Assert.True(sim.StateHash() == load.StateHash(), $"diverged at tick {sim.TickNumber}");
        }
        Rts.Sim.Tests.Save.SaveTestRun.AssertSameState(sim, load, "due-again + 1,000");
    }
}
