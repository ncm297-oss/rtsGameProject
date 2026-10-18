using System.Collections.Immutable;
using Rts.Sim.ViewApi;
using System.Numerics;
using Rts.Sim.Ai;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>M5-3 criterion 1: <see cref="ScoutManager"/>, the AI's early scout (docs/03 "Implementation (M5-3)").</summary>
public class ScoutManagerTests
{
    private readonly ITestOutputHelper _out;

    public ScoutManagerTests(ITestOutputHelper output) => _out = output;

    /// <summary>A malazan ai.json whose only step can't fire for an hour, so the scene's commands are the managers'.</summary>
    private static readonly Lazy<GameData> s_quiet = new(() => AiScenes.DataWithAi("malazan", root =>
        AiScenes.Opening(root)["buildOrder"] = AiScenes.BuildOrder(("time", 3600, "train", "malazan_laborer", 1))));

    private static List<Command> Moves(List<Command> log) => log.Where(c => c.Kind == CommandKind.Move).ToList();

    /// <summary>
    /// On the flat scene (every node known; the enemy hall not) the scout leaves at the first think from <c>scoutAfter</c>,
    /// gets at most one Move a think, each to a waypoint of the documented list, and the sweep ends once it sees the enemy
    /// hall; then it gathers. The list: the compass points 40 m round the hall (clamped inside the map; the first toward the
    /// map centre), the hall's mirror, then the map grid (BUG-0593: the 128 m map in 3 x 3 cells of at most 56 m).
    /// </summary>
    [Fact]
    public void TheScout_LeavesAtScoutAfter_MovesOnlyToItsWaypoints_ThenGathers()
    {
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Hard, SeatController.Ai, SeatController.Human), s_quiet.Value);
        AiPlayer ai = sim.AiOf(0)!;
        List<Command> log = AiScenes.Log(sim, 0);
        int think = ai.ThinkTicks, after = ai.Rules.ScoutAfterTicks;
        while (sim.TickNumber < 8000 && ai.Board.ScoutPhase != 2) sim.Tick();
        Assert.Equal(2, ai.Board.ScoutPhase);
        Assert.True(ai.Board.KnownEnemyBuildings > 0, "the sweep ended without finding the enemy");
        int released = sim.TickNumber;

        List<Command> moves = Moves(log);
        Assert.NotEmpty(moves);
        Assert.Equal((after + think - 1) / think * think + 1, moves[0].Tick);
        EntityHandle scout = moves[0].Unit;
        Assert.All(moves, m => Assert.Equal(scout, m.Unit));
        Assert.All(moves.GroupBy(m => m.Tick), g => Assert.Single(g));
        Assert.All(moves, m => Assert.Contains(m.Position, ai.Board.ScoutPoints));
        _out.WriteLine($"{moves.Count} Moves, released at tick {released}: {string.Join(" ", moves.Select(m => m.Position))}");

        // The list, from the hall's centre and the 128 m map.
        Assert.True(ai.TryHallPoint(out Vector2 hall));
        Vector2 size = new(AiScenes.FlatSize * 2f);
        Vector2[] p = ai.Board.ScoutPoints;
        Assert.All(p, w => Assert.True(w.X >= 1f && w.Y >= 1f && w.X <= size.X - 1f && w.Y <= size.Y - 1f, $"{w} off the map"));
        Assert.Equal(hall + new Vector2(40, 0), p[0]); // east, toward the map centre, first
        for (int k = 0; k < 8; k++)
        {
            Vector2 raw = hall + 40f * Vector2.Normalize(p[k] - hall);
            bool clamped = raw.X < 1f || raw.Y < 1f || raw.X > size.X - 1f || raw.Y > size.Y - 1f;
            if (!clamped) Assert.InRange(Vector2.Distance(hall, p[k]), 39.99f, 40.01f);
        }
        Assert.Equal(1f, p[4].X); // west, 40 m off a hall 20 m from the edge: clamped half a cell inside
        Assert.Equal(size - hall, p[8]);
        Assert.Equal(9 + 3 * 3, p.Length);
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 3; x++)
                Assert.Equal(new Vector2((x + 0.5f) * size.X / 3, (y + 0.5f) * size.Y / 3), p[9 + y * 3 + x]);

        // Released: the economy sends it to gather.
        for (int t = 0; t < 10 * think; t++) sim.Tick();
        Assert.Contains(log, c => c.Kind == CommandKind.Gather && c.Unit == scout && c.Tick > released);
        Assert.Equal(moves.Count, Moves(log).Count);
    }

    /// <summary>
    /// The flat map unexplored, with player 0's hall, a mine, a few trees and workers as on <see cref="AiScenes.Flat"/>, and
    /// player 1's hall at <paramref name="enemyHall"/> (none when null).
    /// </summary>
    private static Simulation Unexplored((int X, int Y)? enemyHall, int workers = 5)
    {
        PlayerSeat[] seats = AiScenes.Seats(AiDifficulty.Hard, SeatController.Ai, SeatController.Human);
        GameData data = s_quiet.Value;
        var config = new SimConfig(3, seats.Length, 256, 512) { Data = data, Seats = seats.ToImmutableArray(), Combat = false };
        var sim = new Simulation(config, LocalMovementTests.Flat(AiScenes.FlatSize));
        World w = sim.World;
        (int hx, int hy) = AiScenes.HallAt(0);
        Assert.True(w.Buildings.Spawn(0, StartBase.BuildingOfSlot(data, 0, BuildingSlot.TownHall), hy * AiScenes.FlatSize + hx, out _));
        int mine = data.FindResource("gold_mine"), tree = data.FindResource("tree");
        Assert.True(w.Resources.Spawn(mine, (hy - 6) * AiScenes.FlatSize + hx + 2, data.Rules.StartMineGold, out _));
        for (int t = 0; t < 4; t++) Assert.True(w.Resources.Spawn(tree, (hy + 9) * AiScenes.FlatSize + hx + t, data.Rules.TreeWood, out _));
        int worker = StartBase.UnitOfSlot(data, 0, UnitSlot.Worker);
        for (int k = 0; k < workers; k++) CombatScenes.Place(sim, 0, worker, w.NavGrid.CellCenter(hx + k % 4, hy + 5 + k / 4));
        if (enemyHall is (int ex, int ey))
            Assert.True(w.Buildings.Spawn(1, StartBase.BuildingOfSlot(data, 1, BuildingSlot.TownHall), ey * AiScenes.FlatSize + ex, out _));
        return sim;
    }

    /// <summary>
    /// BUG-0593: the flat map unexplored, the enemy's only building a hall in the far corner (anchor (56, 56)), neither at the
    /// mirror of the AI's hall nor seen from the ring or the old quadrant centres. The sweep goes on past its head through the
    /// map grid, nearest point first, until it sees the hall; each Move after the head is to a grid point, one a think.
    /// Before the fix the sweep ended at the last quadrant centre knowing no enemy building.
    /// </summary>
    [Fact]
    public void AnEnemyBaseOffTheMirror_IsFound_ByTheMapGrid()
    {
        Simulation sim = Unexplored((56, 56));
        AiPlayer ai = sim.AiOf(0)!;
        List<Command> log = AiScenes.Log(sim, 0);
        while (sim.TickNumber < 12000 && ai.Board.ScoutPhase != 2) sim.Tick();
        List<Command> moves = Moves(log);
        _out.WriteLine($"phase {ai.Board.ScoutPhase} at tick {sim.TickNumber}, enemy buildings known {ai.Board.KnownEnemyBuildings}; moves: " +
            string.Join(" ", moves.Select(m => $"{m.Tick}:{Array.IndexOf(ai.Board.ScoutPoints, m.Position)}")));
        Assert.Equal(2, ai.Board.ScoutPhase);
        Assert.True(ai.Board.KnownEnemyBuildings > 0, "the sweep ended without finding the enemy hall");
        Assert.All(moves.GroupBy(m => m.Tick), g => Assert.Single(g));
        Assert.All(moves, m => Assert.Contains(m.Position, ai.Board.ScoutPoints));
        Assert.Contains(moves, m => Array.IndexOf(ai.Board.ScoutPoints, m.Position) >= 9);
    }

    /// <summary>
    /// BUG-0593: past the head the scout walks to the unvisited grid point nearest it, never to one whose cell the player has
    /// explored, and never twice in a row to the same point; with no enemy to find the sweep ends with every point visited.
    /// </summary>
    [Fact]
    public void PastTheHead_TheScoutTakesTheNearestUnexploredGridPoint()
    {
        Simulation sim = Unexplored(null, workers: 3);
        World w = sim.World;
        AiPlayer ai = sim.AiOf(0)!;
        var view = new PlayerView(w, 0);
        var moves = new List<Command>();
        int gridMoves = 0;
        ai.SentForTests = c =>
        {
            if (c.Kind != CommandKind.Move) return;
            moves.Add(c);
            int k = Array.IndexOf(ai.Board.ScoutPoints, c.Position);
            Assert.True(k >= 0, $"Move to {c.Position}, not a waypoint");
            if (k < 9) return;
            Vector2 p = ai.Board.ScoutPoints[k];
            Assert.False(view.IsExplored((int)(p.Y / 2f) * AiScenes.FlatSize + (int)(p.X / 2f)), $"sent to explored {p}");
            Vector2 at = w.Units.Position[c.Unit.Index];
            float d = Vector2.DistanceSquared(at, p);
            for (int j = 9; j < ai.Board.ScoutPoints.Length; j++)
                if (j != k && !ai.Board.ScoutVisited[j])
                    Assert.True(Vector2.DistanceSquared(at, ai.Board.ScoutPoints[j]) >= d, $"{ai.Board.ScoutPoints[j]} is nearer than {p}");
            gridMoves++;
        };
        while (sim.TickNumber < 12000 && ai.Board.ScoutPhase != 2) sim.Tick();
        _out.WriteLine($"{gridMoves} grid Moves checked; phase {ai.Board.ScoutPhase} at tick {sim.TickNumber}");
        Assert.Equal(2, ai.Board.ScoutPhase);
        Assert.True(gridMoves >= 2, $"only {gridMoves} Moves past the head");
        for (int i = 1; i < moves.Count; i++) Assert.NotEqual(moves[i - 1].Position, moves[i].Position);
        Assert.All(ai.Board.ScoutVisited, v => Assert.True(v));
    }

    /// <summary>A scout killed mid-sweep is not replaced: no Move after its death, no crash.</summary>
    [Fact]
    public void ADeadScout_IsNotReplaced()
    {
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Hard, SeatController.Ai, SeatController.Human), s_quiet.Value);
        AiPlayer ai = sim.AiOf(0)!;
        List<Command> log = AiScenes.Log(sim, 0);
        while (Moves(log).Count < 2) sim.Tick();
        EntityHandle scout = Moves(log)[0].Unit;
        CombatSystem.HitUnit(sim.World, new PendingHit(default, 1, scout, false, sim.World.Units.Hp[scout.Index]));
        Assert.False(sim.World.Units.IsAlive(scout));
        int killed = sim.TickNumber, before = Moves(log).Count;
        for (int t = 0; t < 100 * ai.ThinkTicks; t++) sim.Tick();
        Assert.Equal(2, ai.Board.ScoutPhase);
        Assert.Equal(before, Moves(log).Count);
        Assert.DoesNotContain(log, c => c.Kind == CommandKind.Move && c.Tick > killed + 1);
    }

    /// <summary>
    /// Criterion 1 on a generated 128 map with the shipped Malazan order, seeds 1-3: the AI knows a wood node by tick 1,200
    /// and has wood coming in by tick 2,000; the scout's Moves are one a think, each to a waypoint. Before M5-4a the map had no
    /// start locations and on seeds 1 and 2 the nearest forest was 71-75 m from the hall (known at 2,374 / 2,406, in at 4,394 /
    /// 3,816, BUG-0592); since M5-4a the base stands on its start location, with a start forest within 24 m.
    /// </summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void GeneratedMap_TheAiFindsWood_AndGathersIt(ulong seed)
    {
        PlayerSeat[] seats = AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human);
        Simulation sim = AiScenes.Generated(seats, seed, record: false, out _);
        AiPlayer ai = sim.AiOf(0)!;
        List<Command> log = AiScenes.Log(sim, 0);
        int knewWood = -1, firstWood = -1, lastWood = sim.World.Wood[0];
        while (sim.TickNumber < 6000)
        {
            sim.Tick();
            if (knewWood < 0 && KnowsWood(ai)) knewWood = sim.TickNumber;
            if (firstWood < 0 && sim.World.Wood[0] > lastWood) firstWood = sim.TickNumber;
            lastWood = sim.World.Wood[0];
        }
        _out.WriteLine($"seed {seed}: knew wood at tick {knewWood}, first wood in at {firstWood}, scout phase {ai.Board.ScoutPhase}");
        // M5-4a (BUG-0592's measured target): the bases stand on start locations with a forest within 24 m, so known wood
        // by 1,200 and first wood in by 2,000 (measured: known at 386 / 2 / 2, in at 902 / 720 / 472 on seeds 1 / 2 / 3).
        Assert.InRange(knewWood, 0, 1200);
        Assert.InRange(firstWood, 0, 2000);
        List<Command> moves = log.Where(c => c.Kind == CommandKind.Move && IsWorker(sim, c.Unit)).ToList();
        Assert.NotEmpty(moves);
        Assert.All(moves.GroupBy(m => m.Tick), g => Assert.Single(g));
        Assert.All(moves, m => Assert.Contains(m.Position, ai.Board.ScoutPoints));
    }

    private static bool IsWorker(Simulation sim, EntityHandle unit) =>
        TestSim.Data.Units[sim.World.Units.TypeId[unit.Index]].Slot == UnitSlot.Worker;

    private static bool KnowsWood(AiPlayer ai)
    {
        int s = -1;
        while (ai.View.NextResource(ref s, out SeenResource n))
            if (ai.Data.Resources[n.TypeId].Resource == ResourceKind.Wood) return true;
        return false;
    }
}
