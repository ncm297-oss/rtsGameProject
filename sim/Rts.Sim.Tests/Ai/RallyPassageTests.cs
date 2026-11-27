using System.Numerics;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Xunit.Abstractions;
using static Rts.Sim.Tests.ResourceMaps;

namespace Rts.Sim.Tests;

/// <summary>
/// M6-S12 (BUG-1461, docs/01 2026-11-16 item 3; docs/03 "Implementation (M6-S12)"): the AI's rally point keeps off one-cell
/// passages and their mouths. The flat AI scene (player 0's Keep on cells 8-11 x 30-33, its centre (20, 64); the enemy and the
/// map centre both due east, so the nominal point is (32, 64), cell (16, 32)), with a wall of level-1 ground on columns 17-19
/// open only at row 32: a one-cell passage whose west mouth is the nominal point. The seat takes the nearest acceptable point
/// on the same line, back toward the hall first: (28, 64), on the hall side and more than <c>ai.rallyReach</c> from every
/// passage cell. On open ground the point is today's to the bit.
/// </summary>
[Collection(SerialCollection.Name)] // measures allocation (criterion 10)
public class RallyPassageTests(ITestOutputHelper output)
{
    /// <summary>The quiet order (one Laborer an hour in) so nothing is built and the base stays as it is.</summary>
    private static readonly Lazy<GameData> s_quiet = new(() => AiScenes.DataWithAi("malazan", root =>
        AiScenes.SetBuildOrder(AiScenes.Opening(root), AiScenes.BuildOrder(("time", 3600, "train", "malazan_laborer", 1)))));

    private const int WallX0 = 17, WallX1 = 19, PassageRow = 32;

    /// <summary>The flat 64 x 64 AI map with the wall (columns 17-19, all rows but 32) or without it.</summary>
    private static Heightmap Map(bool wall)
    {
        var rows = new string[AiScenes.FlatSize];
        for (int y = 0; y < rows.Length; y++)
        {
            var row = new char[AiScenes.FlatSize];
            for (int x = 0; x < row.Length; x++) row[x] = wall && x >= WallX0 && x <= WallX1 && y != PassageRow ? '1' : '0';
            rows[y] = new string(row);
        }
        return FromRows(rows);
    }

    private static Simulation Scene(bool wall) =>
        AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human), s_quiet.Value, map: Map(wall));

    /// <summary>Runs <paramref name="thinks"/> thinks and returns every (tick, point, version) the seat's rally took.</summary>
    private static List<(int Tick, Vector2 Point, int Version)> Rallies(Simulation sim, int thinks)
    {
        AiPlayer ai = sim.AiOf(0)!;
        var seen = new List<(int, Vector2, int)>();
        int version = 0;
        for (int t = 0; t < thinks * ai.ThinkTicks; t++)
        {
            sim.Tick();
            if (ai.Board.RallyVersion == version) continue;
            version = ai.Board.RallyVersion;
            seen.Add((sim.TickNumber, ai.Board.Rally, version));
        }
        return seen;
    }

    /// <summary>The nominal point (docs/02's rule before M6-S12): <c>ai.rallyDistance</c> from the hall's centre, due east here.</summary>
    private static Vector2 Nominal(AiPlayer ai) => ai.HallPoint + new Vector2(ai.Rules.RallyDistance, 0f);

    /// <summary>
    /// Criterion 2 (red on the base: the point stood at the passage's west mouth): the chosen point is outside the mouth (no
    /// passage cell within <c>ai.rallyReach</c>, its own cell open), on the hall side (nearer the hall than the nominal point,
    /// on the line), the same on two runs, and unchanged (one version) for 200 thinks with nothing else changing.
    /// </summary>
    [Fact]
    public void ANominalPointInAPassageMouth_MovesBackTowardTheHall_AndStays()
    {
        Simulation a = Scene(wall: true), b = Scene(wall: true);
        AiPlayer ai = a.AiOf(0)!;
        NavGrid g = a.World.NavGrid;
        Assert.False(g.IsPassable(WallX0, PassageRow - 1)); // the wall
        Assert.True(g.IsPassable(WallX0, PassageRow) && g.IsPassable(WallX1, PassageRow)); // the passage
        List<(int Tick, Vector2 Point, int Version)> ra = Rallies(a, 201), rb = Rallies(b, 201);
        foreach (var r in ra) output.WriteLine($"tick {r.Tick}: rally v{r.Version} at {r.Point}");
        Vector2 nominal = Nominal(ai);
        output.WriteLine($"hall {ai.HallPoint}, nominal {nominal}");
        Assert.True(ra.Count >= 1, "the seat set no rally point");
        Vector2 p = ra[^1].Point;
        Assert.Equal(ra, rb); // two runs alike
        Assert.Single(ra); // one point for 200 thinks: no flapping
        float reach = ai.Rules.RallyReach;
        for (int x = WallX0; x <= WallX1; x++)
            Assert.True(Vector2.Distance(p, g.CellCenter(x, PassageRow)) > reach, $"the point {p} is within {reach} m of passage cell ({x}, {PassageRow})");
        Assert.True(g.IsPassable((int)(p.X / MapConstants.CellSize), (int)(p.Y / MapConstants.CellSize)), $"the point {p} is on closed ground");
        Assert.True(Vector2.Distance(p, ai.HallPoint) < Vector2.Distance(nominal, ai.HallPoint), $"the point {p} is not on the hall side of {nominal}");
        Assert.Equal(nominal.Y, p.Y); // on the line from the hall toward the target
        Assert.Equal(new Vector2(28f, 64f), p);
    }

    /// <summary>Criterion 2's control (green on the base and after): on open ground the point is today's exact nominal point.</summary>
    [Fact]
    public void OnOpenGround_ThePointIsTheNominalOne()
    {
        Simulation sim = Scene(wall: false);
        AiPlayer ai = sim.AiOf(0)!;
        List<(int Tick, Vector2 Point, int Version)> r = Rallies(sim, 50);
        Assert.Single(r);
        Assert.Equal(new Vector2(32f, 64f), r[0].Point);
        Assert.Equal(Nominal(ai), r[0].Point);
        Assert.False(MilitaryManager.NarrowWithin(ai, r[0].Point));
    }

    /// <summary>Criterion 10: the thinks that run the rally search (the nominal point in the mouth every think) allocate nothing.</summary>
    [Fact]
    public void TheRallySearch_AllocatesNothing()
    {
        Simulation sim = Scene(wall: true);
        AiPlayer ai = sim.AiOf(0)!;
        GatherMaps.Run(sim, 10 * ai.ThinkTicks); // warm
        Assert.True(MilitaryManager.NarrowWithin(ai, Nominal(ai)), "the nominal point is not in the mouth: the search does not run");
        while (sim.TickNumber % ai.ThinkTicks != 0) sim.Tick();
        AllocationProbe.AssertZero(() => GatherMaps.Run(sim, 20 * ai.ThinkTicks));
        AllocationProbe.AssertZero(() => MilitaryManager.NarrowWithin(ai, Nominal(ai)));
    }
}
