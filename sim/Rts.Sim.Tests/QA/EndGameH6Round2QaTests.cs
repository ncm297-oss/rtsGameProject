using System.Numerics;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA, M5-H6 fix round 1 (session 2026-11-09-1336, full tier): BUG-1162's fix (<see cref="MilitaryManager.NextHuntStart"/>'s
/// second pass, a live enemy's own start once every free start is visited) attacked where the developer's row does not
/// reach: a team match (an ally's start must never be a hunt target, even after every free and enemy start is visited),
/// and the second pass's allocation and cost (the developer's and QA's round-1 rows only reach the first pass).
/// </summary>
[Collection(SerialCollection.Name)]
public class EndGameH6Round2QaTests
{
    private readonly ITestOutputHelper _out;

    public EndGameH6Round2QaTests(ITestOutputHelper output) => _out = output;

    /// <summary>The developer's 512 m flat map's four starts (cells).</summary>
    private static readonly StartLocation[] Starts512 = { new(7 * 256 + 136, 0), new(240 * 256 + 128, 0), new(200 * 256 + 200, 0), new(200 * 256 + 40, 0) };

    /// <summary>Seat 0 (AI) and seat 1 allies (team 1), seat 2 the enemy (team 2), start 3 free.</summary>
    private static (Simulation Sim, AiPlayer Ai, int Cols) TeamScene()
    {
        PlayerSeat[] seats =
        {
            new(0, SeatController.Ai, AiDifficulty.Normal, 1),
            new(1, SeatController.Human, AiDifficulty.Normal, 1),
            new(0, SeatController.Human, AiDifficulty.Normal, 2),
        };
        Simulation sim = AiScenes.Flat(seats, map: LocalMovementTests.Flat(256).WithStartLocations(Starts512));
        AiPlayer ai = sim.AiOf(0)!;
        sim.Tick();
        return (sim, ai, ScoutManager.GridLines(256, ai.Rules.ScoutGrid));
    }

    private static int Point(AiPlayer ai, int cols, int k) => MilitaryManager.StartProxy(ai, cols, cols, ai.CellCentre(Starts512[k].Cell));

    /// <summary>
    /// The order in a team match: the free start, then the live enemy's own start, then nothing: the ally's start (1) is
    /// never a target. Pins that the second pass reads <c>IsEnemy</c>, not merely "a live player".
    /// </summary>
    [Fact]
    public void InATeamMatch_TheHuntsStarts_AreTheFreeOneThenTheEnemys_NeverTheAllys()
    {
        (Simulation _, AiPlayer ai, int cols) = TeamScene();
        Assert.False(ai.View.IsEnemy(1));
        Assert.True(ai.View.IsEnemy(2));
        Vector2 from = ai.CellCentre(Starts512[0].Cell);
        Assert.Equal(Point(ai, cols, 3), MilitaryManager.NextHuntStart(ai, from, -1));
        ai.Board.HuntVisited[Point(ai, cols, 3)] = true;
        Assert.Equal(Point(ai, cols, 2), MilitaryManager.NextHuntStart(ai, from, -1));
        ai.Board.HuntVisited[Point(ai, cols, 2)] = true;
        Assert.Equal(-1, MilitaryManager.NextHuntStart(ai, from, -1));
        // skip: the enemy's start is passed over when it is the point being walked away from
        ai.Board.HuntVisited[Point(ai, cols, 2)] = false;
        Assert.Equal(-1, MilitaryManager.NextHuntStart(ai, from, Point(ai, cols, 2)));
    }

    /// <summary>The second pass (every free start visited) and the both-passes-empty case allocate nothing.</summary>
    [Fact]
    public void TheSecondPass_AllocatesNothing()
    {
        (Simulation _, AiPlayer ai, int cols) = TeamScene();
        ai.Board.HuntsStarted = 2;
        Vector2 from = new(40f, 60f);
        ai.Board.HuntVisited[Point(ai, cols, 3)] = true;
        AllocationProbe.AssertZero(() => MilitaryManager.NextHuntStart(ai, from, -1), _out);
        AllocationProbe.AssertZero(() => MilitaryManager.NextSweep(ai, from, -1), _out);
        ai.Board.HuntVisited[Point(ai, cols, 2)] = true;
        AllocationProbe.AssertZero(() => MilitaryManager.NextHuntStart(ai, from, -1), _out);
        AllocationProbe.AssertZero(() => MilitaryManager.NextSweep(ai, from, -1), _out);
    }

    /// <summary>
    /// The worst case the fix adds: every start visited, so both passes walk every start's proxy and return -1 before
    /// <see cref="MilitaryManager.NextSweep"/> falls back to nearest-first. Same 50 us bound as round 1's row. Rule 42: alone.
    /// </summary>
    [Fact]
    [Trait("Category", "Perf")]
    public void ASweepRetarget_WithEveryStartVisited_CostsUnder50Us()
    {
        (Simulation _, AiPlayer ai, int cols) = TeamScene();
        ai.Board.HuntsStarted = 2;
        for (int k = 0; k < Starts512.Length; k++) ai.Board.HuntVisited[Point(ai, cols, k)] = true;
        Vector2 from = new(40f, 60f);
        int sink = 0;
        for (int k = 0; k < 2000; k++) sink += MilitaryManager.NextSweep(ai, from, -1);
        const int calls = 20_000;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int k = 0; k < calls; k++) sink += MilitaryManager.NextSweep(ai, from, -1);
        sw.Stop();
        double us = sw.Elapsed.TotalMilliseconds * 1000.0 / calls;
        _out.WriteLine($"NextSweep, every start visited: {us:F2} us a call (sink {sink})");
        Assert.True(us < 50.0, $"a sweep re-target costs {us:F1} us");
    }
}
