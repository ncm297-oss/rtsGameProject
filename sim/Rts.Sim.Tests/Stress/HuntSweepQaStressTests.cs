using System.Numerics;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Tests.Save;
using Rts.Sim.Vision;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA, M5-4d re-check round 1 (session 2026-11-06-0926), BUG-1107's fix: the border ring and the seen-points rule. A human
/// seat that never plays hides its five workers (1) at the sweep's worst border points (an edge point level with a grid line,
/// half a scout cell from the two nearest ring points, and the interior's worst point): BUG-1107, 11 of 16 rows red at 6a3c83c
/// (edge points level with a grid line are never seen). (Corner hides on the 2nd / 3rd seed of each opening were measured once
/// and dropped: on the hand-made maps every seed of an opening ends on the same tick as the shipped row.) The seen-points rule is checked against
/// the fog oracle on every hunting think (a point is marked only when the seat's own fog, own plus allies' byte maps, shows its
/// cell visible, and every such point is marked), and a save taken mid-sweep after marks were set by sight follows the
/// uninterrupted run hash for hash.
/// </summary>
internal static class HuntSweepQa
{
    internal static string Clock(int ticks) => $"{ticks / 1200}:{ticks / 20 % 60:D2}";

    /// <summary>The <paramref name="nth"/> (0-based) seed in 1-2000 whose Normal opening for <paramref name="player"/> is <paramref name="opening"/>.</summary>
    internal static ulong SeedFor(int player, int opening, int nth)
    {
        for (ulong s = 1; s <= 2000; s++)
            if (Rts.Sim.Tests.Content.AiOpeningSeeds.Pick(s, player, AiDifficulty.Normal) == opening && nth-- == 0) return s;
        throw new InvalidOperationException("no seed");
    }

    /// <summary>
    /// The sweep's worst border points: on each edge a point level with an interior grid line (half a scout cell from the two
    /// ring points beside it, a scout cell from the interior corner), and one interior point half a cell from a centre and a corner.
    /// </summary>
    internal static Vector2[] WorstPoints(float size, int cols, int rows)
    {
        float cw = size / cols, ch = size / rows, e = 1.5f;
        return new[]
        {
            new Vector2(cols / 2 * cw, e),                 // north edge, mid grid line
            new Vector2(e, Math.Max(1, rows / 3) * ch),     // west edge
            new Vector2(1 * cw, size - e),                 // south edge, first grid line (near the corner)
            new Vector2(size - e, (rows - 1) * ch),         // east edge, last grid line
            new Vector2(Math.Max(1, cols / 3) * cw, (Math.Max(1, rows / 3) + 0.5f) * ch), // interior worst point
        };
    }

    internal sealed record Outcome(Simulation Sim, AiPlayer Ai, int Human, bool Over, int Left, string Line);

    internal static Simulation Setup(string map, int aiSeat, ulong seed, bool worst, out AiPlayer ai, out int human)
    {
        human = 1 - aiSeat;
        var seats = new PlayerSeat[2];
        seats[aiSeat] = new PlayerSeat(aiSeat, SeatController.Ai, AiDifficulty.Normal);
        seats[human] = new PlayerSeat(human, SeatController.Human, AiDifficulty.Normal);
        Simulation sim = HuntStressTests.Match(map, seats, seed);
        World w = sim.World;
        while (w.LiveUnits(human) == 0 && sim.TickNumber < 10) sim.Tick();
        ai = sim.AiOf(aiSeat)!;
        float size = w.NavGrid.Width * MapConstants.CellSize;
        int cols = ScoutManager.GridLines(w.NavGrid.Width, ai.Rules.ScoutGrid), rows = ScoutManager.GridLines(w.NavGrid.Height, ai.Rules.ScoutGrid);
        Vector2[] hides = worst
            ? WorstPoints(size, cols, rows)
            : new Vector2[] { new(4, 4), new(size - 4, 4), new(4, size - 4), new(size - 4, size - 4), new(size / 2, size / 2) };
        int k = 0;
        for (int s = 0; s < w.Units.Capacity; s++)
            if (w.Units.Alive[s] && w.Units.Owner[s] == human)
                sim.Enqueue(Command.Move(human, new EntityHandle(s, w.Units.Generation[s]), hides[k++ % hides.Length]));
        Assert.Equal(5, k);
        return sim;
    }

    internal static string Where(Simulation sim, AiPlayer ai, int human)
    {
        World w = sim.World;
        int cols = ScoutManager.GridLines(w.NavGrid.Width, ai.Rules.ScoutGrid), rows = ScoutManager.GridLines(w.NavGrid.Height, ai.Rules.ScoutGrid);
        var where = new List<string>();
        for (int s = 0; s < w.Units.Capacity; s++)
        {
            if (!w.Units.Alive[s] || w.Units.Owner[s] != human) continue;
            float best = float.MaxValue;
            for (int i = 0; i < MilitaryManager.HuntPoints(cols, rows); i++)
                best = Math.Min(best, Vector2.Distance(w.Units.Position[s], MilitaryManager.HuntPoint(ai, cols, rows, i)));
            where.Add($"{w.Units.Position[s]} ({best:F1} m)");
        }
        return string.Join(", ", where);
    }

    /// <summary>
    /// Runs a hide match to 72,000 ticks or its end. With <paramref name="oracle"/>, every think of the AI seat that continues a
    /// sweep is checked: each point newly marked (not the last target) has its cell visible in the oracle's union byte map, and
    /// each point left unmarked (not the last target, not the new one) has not. Returns the counts it checked.
    /// </summary>
    internal static Outcome Run(string map, int aiSeat, ulong seed, bool worst, bool oracle, out int checkedThinks, out int seenMarks, Action<Simulation, AiPlayer>? perTick = null)
    {
        Simulation sim = Setup(map, aiSeat, seed, worst, out AiPlayer ai, out int human);
        World w = sim.World;
        int thinks = 0, marks = 0;
        string? bad = null;
        if (oracle)
        {
            int cols = ScoutManager.GridLines(w.NavGrid.Width, ai.Rules.ScoutGrid), rows = ScoutManager.GridLines(w.NavGrid.Height, ai.Rules.ScoutGrid);
            bool[] prev = new bool[ai.Board.HuntVisited.Length];
            bool prevSweeping = false;
            int prevSweep = -1, prevHunts = 0;
            ai.ThoughtForTests = a =>
            {
                Blackboard b = a.Board;
                bool[] now = b.HuntVisited;
                if (bad == null && prevSweeping && b.Hunting && b.HuntsStarted == prevHunts && now.Count(v => v) >= prev.Count(v => v))
                {
                    byte[] vis = PlayerViewOracle.UnionVisibility(w, aiSeat);
                    thinks++;
                    for (int i = 0; i < now.Length && bad == null; i++)
                    {
                        if (i == prevSweep) continue;
                        Vector2 p = MilitaryManager.HuntPoint(a, cols, rows, i);
                        int x = Math.Clamp((int)(p.X / MapConstants.CellSize), 0, w.Fog.Width - 1), y = Math.Clamp((int)(p.Y / MapConstants.CellSize), 0, w.Fog.Height - 1);
                        bool visible = vis[y * w.Fog.Width + x] == VisionConstants.Visible;
                        if (!prev[i] && now[i])
                        {
                            marks++;
                            if (!visible) bad = $"tick {a.Now}: point {i} {p} marked visited, its cell ({x}, {y}) not visible to player {aiSeat}";
                        }
                        if (!now[i] && i != b.HuntSweep && visible) bad = $"tick {a.Now}: point {i} {p} visible to player {aiSeat} but left unvisited";
                    }
                }
                Array.Copy(now, prev, now.Length);
                prevSweeping = b.Hunting && b.HuntSweep >= 0;
                prevSweep = b.HuntSweep;
                prevHunts = b.HuntsStarted;
            };
        }
        int hallLost = -1, firstHunt = -1;
        while (sim.TickNumber < 72_000 && !sim.MatchOver && bad == null)
        {
            sim.Tick();
            if (hallLost < 0 && w.ProductionBuildings(human) == 0) hallLost = sim.TickNumber;
            if (firstHunt < 0 && ai.Board.HuntsStarted > 0) firstHunt = sim.TickNumber;
            perTick?.Invoke(sim, ai);
        }
        Assert.True(bad == null, bad);
        checkedThinks = thinks;
        seenMarks = marks;
        int left = w.LiveUnits(human);
        string line = $"{map} ai {aiSeat} opening {ai.Opening.Id} seed {seed} {(worst ? "worst" : "corners")}: over {sim.MatchOver} at {Clock(sim.TickNumber)} ({sim.TickNumber}, margin {Clock(72_000 - sim.TickNumber)}); " +
            $"hall lost {(hallLost < 0 ? "never" : Clock(hallLost))}, first hunt {(firstHunt < 0 ? "never" : Clock(firstHunt))}, hunts {ai.Board.HuntsStarted}, visited {ai.Board.HuntVisited.Count(v => v)} / {ai.Board.HuntVisited.Length}; left {left}" +
            (left > 0 ? $" at {Where(sim, ai, human)}" : "");
        return new Outcome(sim, ai, human, sim.MatchOver && w.IsDefeated(human), left, line);
    }
}

/// <summary>Hidden workers at the sweep's worst border and interior points, all four maps, either AI seat, two openings.</summary>
public class HuntSweepWorstPointQaTests
{
    private readonly ITestOutputHelper _out;
    public HuntSweepWorstPointQaTests(ITestOutputHelper o) => _out = o;

    public static TheoryData<string, int, int> Rows()
    {
        var rows = new TheoryData<string, int, int>();
        foreach (string map in new[] { "generated", "raraku", "pale_hills", "vathar_crossing" })
            for (int ai = 0; ai < 2; ai++)
                foreach (int o in new[] { 0, 2 }) rows.Add(map, ai, o);
        return rows;
    }

    [Theory]
    [Trait("Category", "Stress")]
    [MemberData(nameof(Rows))]
    public void WorkersAtTheWorstSweepPoints_AreHuntedDown(string map, int aiSeat, int opening)
    {
        HuntSweepQa.Outcome r = HuntSweepQa.Run(map, aiSeat, HuntSweepQa.SeedFor(aiSeat, opening, 0), true, false, out _, out _);
        _out.WriteLine("WORST " + r.Line);
        Assert.True(r.Over, r.Line);
    }
}

/// <summary>The seen-points rule against the fog oracle, a save mid-sweep, and determinism of a hide match.</summary>
public class HuntSweepInvariantQaTests
{
    private readonly ITestOutputHelper _out;
    public HuntSweepInvariantQaTests(ITestOutputHelper o) => _out = o;

    [Theory]
    [Trait("Category", "Stress")]
    [InlineData("pale_hills", 0, 0)]
    [InlineData("vathar_crossing", 1, 2)]
    [InlineData("raraku", 0, 1)]
    public void SeenPoints_MatchTheFogOracle_OnEverySweepThink(string map, int aiSeat, int opening)
    {
        HuntSweepQa.Outcome r = HuntSweepQa.Run(map, aiSeat, HuntSweepQa.SeedFor(aiSeat, opening, 0), true, true, out int thinks, out int marks);
        _out.WriteLine($"ORACLE {r.Line}; {thinks} sweep thinks checked, {marks} points marked by sight");
        Assert.True(thinks > 50, $"only {thinks} sweep thinks to check");
        Assert.True(marks > 0, "no point was ever marked by sight");
    }

    /// <summary>
    /// Pale Hills, worst-point hides: saved at the first think after 25 or more points are marked (most by sight) and again
    /// 6,000 ticks later; each load is hash-equal every tick to the end and field for field equal there.
    /// </summary>
    [Fact]
    [Trait("Category", "Stress")]
    public void ASaveMidSweep_AfterMarksBySight_FollowsTheRunToTheEnd()
    {
        Simulation sim = HuntSweepQa.Setup("pale_hills", 0, HuntSweepQa.SeedFor(0, 0, 0), true, out AiPlayer ai, out _);
        var loads = new List<(string What, Simulation Sim)>();
        int firstAt = -1;
        while (sim.TickNumber < 72_000 && !sim.MatchOver)
        {
            sim.Tick();
            Blackboard b = ai.Board;
            bool save = (firstAt < 0 && b.Hunting && b.HuntSweep >= 0 && b.HuntVisited.Count(v => v) >= 25)
                || (firstAt > 0 && loads.Count == 1 && sim.TickNumber == firstAt + 6000);
            if (save)
            {
                if (firstAt < 0) firstAt = sim.TickNumber;
                byte[] bytes = SaveTestRun.Save(sim);
                loads.Add(($"@{sim.TickNumber} sweep {b.HuntSweep} visited {b.HuntVisited.Count(v => v)} hunting {b.Hunting}", SaveTestRun.Load(TestSim.Data, bytes)));
            }
            foreach ((string what, Simulation l) in loads)
            {
                if (l.TickNumber < sim.TickNumber) l.Tick();
                Assert.True(l.StateHash() == sim.StateHash(), $"{what}: diverged at tick {sim.TickNumber}");
            }
        }
        _out.WriteLine($"ended {sim.MatchOver} at {HuntSweepQa.Clock(sim.TickNumber)}; loads: {string.Join(", ", loads.Select(l => l.What))}");
        Assert.True(loads.Count >= 1, "never reached 25 marks in a sweep");
        foreach ((string what, Simulation l) in loads) SaveTestRun.AssertSameState(sim, l, what + " at the end");
    }

    /// <summary>Two runs of the same worst-point hide match are hash-equal at every 600-tick checkpoint; another seed differs.</summary>
    [Fact]
    [Trait("Category", "Stress")]
    public void AHideMatch_IsDeterministic()
    {
        ulong seed = HuntSweepQa.SeedFor(1, 1, 0);
        var a = new List<ulong>();
        var b = new List<ulong>();
        var c = new List<ulong>();
        HuntSweepQa.Run("vathar_crossing", 1, seed, true, false, out _, out _, (s, _) => { if (s.TickNumber % 600 == 0) a.Add(s.StateHash()); });
        HuntSweepQa.Run("vathar_crossing", 1, seed, true, false, out _, out _, (s, _) => { if (s.TickNumber % 600 == 0) b.Add(s.StateHash()); });
        HuntSweepQa.Run("vathar_crossing", 1, HuntSweepQa.SeedFor(1, 1, 1), true, false, out _, out _, (s, _) => { if (s.TickNumber % 600 == 0) c.Add(s.StateHash()); });
        _out.WriteLine($"{a.Count} checkpoints, last {a[^1]:X16}");
        Assert.Equal(a, b);
        Assert.NotEqual(a[^1], c[^1]);
    }
}
