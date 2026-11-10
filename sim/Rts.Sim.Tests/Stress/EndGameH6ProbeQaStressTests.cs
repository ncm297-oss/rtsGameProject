using System.Numerics;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Vision;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA, M5-H6 (session 2026-11-09-1336, full tier), QA focus "the end game": Pale Hills seeds 1 / 2 / 5 Normal v Normal and the
/// five Hard-seat-0 tally seeds (2 / 3 / 6 / 7 / 8, Hard v Normal) to 144,000 ticks, every think of both AI seats watched:
/// <list type="bullet">
/// <item>The hunt's fog oracle: on a think that continues a sweep, every sweep point newly marked visited (the start proxies
/// included: their mark is the start's) has its cell visible to the seat in the world's own fog (own plus allies). A mark
/// on an unseen cell would be the hunt "knowing" ground it never looked at.</item>
/// <item>Lever (i)'s targets: each time a hunt's sweep point is a free start's proxy, the start is free at that think in the
/// world's truth (no player starts there, or its player is defeated); counted per seat.</item>
/// <item>BUG-1158's cap in real play: each leg death, and each time the cap ends a leg (every start marked looked as a dead
/// leg worker is found), with the seat's losses then and the starts it had not looked at yet.</item>
/// </list>
/// Prints a row per match (end tick, winner, hunts, waves) for the report's comparison with docs/03 "Implementation (M5-H6)".
/// </summary>
[Trait("Category", "Stress")]
public class EndGameH6ProbeQaStressTests
{
    private readonly ITestOutputHelper _out;

    public EndGameH6ProbeQaStressTests(ITestOutputHelper output) => _out = output;

    private sealed class Seat
    {
        public int Thinks, Marks, StartTargets, LegDeaths, CapEnds, HouseBuilds;
        public string? Error;
        public readonly List<string> Notes = new();
    }

    [Theory]
    [InlineData(1UL, false)]
    [InlineData(2UL, false)]
    [InlineData(5UL, false)]
    [InlineData(2UL, true)]
    [InlineData(3UL, true)]
    [InlineData(6UL, true)]
    [InlineData(7UL, true)]
    [InlineData(8UL, true)]
    public void PaleHills_To144000_HuntFogOracleAndLegCap(ulong seed, bool hardSeat0)
    {
        PlayerSeat[] seats = { new(0, SeatController.Ai, hardSeat0 ? AiDifficulty.Hard : AiDifficulty.Normal), new(1, SeatController.Ai, AiDifficulty.Normal) };
        Simulation sim = HuntStressTests.Match("pale_hills", seats, seed);
        World w = sim.World;
        var watch = new Seat[2];
        for (int p = 0; p < 2; p++) watch[p] = Watch(sim, p);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sim.TickNumber < 144_000 && !sim.MatchOver && watch[0].Error == null && watch[1].Error == null) sim.Tick();
        string winner = !sim.MatchOver ? "none" : w.IsDefeated(1) ? "p0" : "p1";
        int hunts = sim.AiOf(0)!.Board.HuntsStarted + sim.AiOf(1)!.Board.HuntsStarted, waves = sim.AiOf(0)!.Board.WavesStarted + sim.AiOf(1)!.Board.WavesStarted;
        _out.WriteLine($"H6 | pale_hills {seed} | {(hardSeat0 ? "Hard v Normal" : "Normal v Normal")} | {(sim.MatchOver ? "ends" : "NO END")} {sim.TickNumber} ({CriterionMatch.Clock(sim.TickNumber)}) | {winner} | waves {waves} hunts {hunts} | {sw.Elapsed.TotalSeconds:F0} s");
        for (int p = 0; p < 2; p++)
        {
            Seat s = watch[p];
            _out.WriteLine($"  seat {p}: thinks {s.Thinks}, sweep marks checked {s.Marks}, free-start targets {s.StartTargets}, leg deaths {s.LegDeaths}, cap ends {s.CapEnds}, house Builds {s.HouseBuilds}, oracle {s.Error ?? "clean"}");
            foreach (string n in s.Notes) _out.WriteLine("    " + n);
        }
        Assert.True(watch[0].Error == null, watch[0].Error);
        Assert.True(watch[1].Error == null, watch[1].Error);
    }

    private static Seat Watch(Simulation sim, int seat)
    {
        World w = sim.World;
        AiPlayer ai = sim.AiOf(seat)!;
        var s = new Seat();
        int cols = ScoutManager.GridLines(w.NavGrid.Width, ai.Rules.ScoutGrid), rows = ScoutManager.GridLines(w.NavGrid.Height, ai.Rules.ScoutGrid);
        bool[]? prev = null;
        bool prevSweeping = false;
        int prevSweep = -1, prevHunts = 0, legSlot = -1, legGen = 0, lastTargetSweep = -1, lastLooked = 0;
        int all = (1 << Math.Min(ai.View.StartCount, FarExpansion.MaxStarts)) - 1;
        ai.SentForTests = c =>
        {
            if (c.Kind == Commands.CommandKind.Build && c.TypeId == ai.HouseType) s.HouseBuilds++;
        };
        ai.ThoughtForTests = a =>
        {
            if (s.Error != null) return;
            s.Thinks++;
            Blackboard b = a.Board;
            bool[] now = b.HuntVisited;
            prev ??= new bool[now.Length];
            // The hunt's fog oracle (HuntSweepQaStressTests' rule): a newly marked point's cell is visible to the seat now.
            if (prevSweeping && b.Hunting && b.HuntsStarted == prevHunts)
            {
                byte[]? vis = null;
                for (int i = 0; i < now.Length && s.Error == null; i++)
                {
                    if (i == prevSweep || prev[i] || !now[i]) continue;
                    vis ??= PlayerViewOracle.UnionVisibility(w, seat);
                    s.Marks++;
                    Vector2 p = MilitaryManager.HuntPoint(a, cols, rows, i);
                    int x = Math.Clamp((int)(p.X / MapConstants.CellSize), 0, w.Fog.Width - 1), y = Math.Clamp((int)(p.Y / MapConstants.CellSize), 0, w.Fog.Height - 1);
                    if (vis[y * w.Fog.Width + x] != VisionConstants.Visible) s.Error = $"seat {seat} tick {a.Now}: point {i} {p} marked visited, its cell not visible";
                }
            }
            // Lever (i): a sweep target that is a start's proxy, from the second hunt on, is a free start's.
            if (b.Hunting && b.HuntSweep >= 0 && b.HuntSweep != lastTargetSweep && b.HuntsStarted > 1)
            {
                for (int k = 0; k < Math.Min(a.View.StartCount, FarExpansion.MaxStarts); k++)
                {
                    if (MilitaryManager.StartProxy(a, cols, rows, a.CellCentre(a.View.StartCell(k))) != b.HuntSweep) continue;
                    bool free = k >= a.View.PlayerCount || w.IsDefeated(k);
                    if (free) s.StartTargets++;
                    else if (k == seat) s.Notes.Add($"tick {a.Now}: the sweep's point {b.HuntSweep} is its own start {k}'s proxy (nearest-first)");
                }
            }
            lastTargetSweep = b.Hunting ? b.HuntSweep : -1;
            // BUG-1158 in real play.
            if (b.FarScoutSlot != legSlot || b.FarScoutGeneration != legGen)
            {
                if (legSlot >= 0 && !w.Units.IsAlive(new EntityHandle(legSlot, legGen)))
                {
                    s.LegDeaths++;
                    s.Notes.Add($"tick {a.Now}: leg worker died (losses {a.View.OwnUnitLosses}); looked {lastLooked} -> {b.FarLooked}, start {b.FarStart}, slot {b.FarScoutSlot}");
                    if (b.FarLooked == all && lastLooked != all)
                    {
                        s.CapEnds++;
                        s.Notes.Add($"tick {a.Now}: the cap ended the leg; starts not yet looked at {Convert.ToString(all & ~lastLooked, 2)}");
                    }
                }
                legSlot = b.FarScoutSlot;
                legGen = b.FarScoutGeneration;
            }
            lastLooked = b.FarLooked;
            Array.Copy(now, prev, now.Length);
            prevSweeping = b.Hunting && b.HuntSweep >= 0;
            prevSweep = b.HuntSweep;
            prevHunts = b.HuntsStarted;
        };
        return s;
    }
}
