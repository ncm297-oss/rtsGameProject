using Rts.Sim.Ai;
using Rts.Sim.Combat;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M6-S14 item C (BUG-1555, docs/03 "Implementation (M6-S14)"): <see cref="PlayerView.IsKnownClosed"/>, the rally search's map
/// read, answers from the seat's memory as <see cref="PlayerView.CanPlace"/> does. The building half is QA's row
/// (<c>M6S12AiRulesQaTests.IsKnownClosed_FollowsTheFog_AndARazeOutOfSight_StaysClosedUntilSeen_Bug1555</c>); this file holds
/// the node half and the block read's equality with the cell read.
/// </summary>
public class KnownClosedMemoryTests
{
    private readonly ITestOutputHelper _out;

    public KnownClosedMemoryTests(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// Criterion 5, the node (failed on the base: the felled cell read open while player 0 still remembered the tree): a tree
    /// player 0 never saw reads open; seen, closed; with the eye gone, still closed; felled out of sight, still closed (player 0
    /// remembers it standing); a unit of player 0 sees the ground again, open.
    /// </summary>
    [Fact]
    public void ANodeFelledOutOfSight_StaysClosed_UntilTheSeatSeesTheGroundAgain()
    {
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human), explored: false);
        World w = sim.World;
        int size = AiScenes.FlatSize, tree = w.Data.FindResource("tree"), x = 40, y = 56;
        Assert.True(w.Resources.Spawn(tree, y * size + x, w.Data.Rules.TreeWood, out EntityHandle node));
        w.Fog.Update();
        PlayerView v = sim.AiOf(0)!.View;
        bool never = v.IsKnownClosed(x, y);
        EntityHandle eye = CombatScenes.Place(sim, 0, w.Data.FindUnit("malazan_laborer"), w.NavGrid.CellCenter(x - 4, y));
        w.Fog.Update();
        bool seen = v.IsKnownClosed(x, y);
        CombatSystem.HitUnit(w, new PendingHit(default, 1, eye, false, w.Units.Hp[eye.Index]));
        sim.Tick();
        w.Fog.Update();
        bool remembered = v.IsKnownClosed(x, y);
        Assert.False(w.Fog.IsVisible(0, y * size + x), "precondition: the tree's cell is out of sight");
        w.Resources.Take(node, int.MaxValue);
        sim.Tick();
        w.Fog.Update();
        Assert.False(w.Resources.IsAlive(node));
        Assert.True((w.NavGrid.FlagsAt(x, y) & NavFlags.Resource) == 0, "precondition: the felled cell's truth is open");
        bool felledUnseen = v.IsKnownClosed(x, y);
        CombatScenes.Place(sim, 0, w.Data.FindUnit("malazan_laborer"), w.NavGrid.CellCenter(x - 4, y));
        w.Fog.Update();
        bool seenAgain = v.IsKnownClosed(x, y);
        _out.WriteLine($"never seen {never}; seen {seen}; eye gone {remembered}; felled out of sight {felledUnseen}; seen again {seenAgain}");
        Assert.False(never);
        Assert.True(seen);
        Assert.True(remembered);
        Assert.True(felledUnseen); // BUG-1555: the memory, as CanPlace (before M6-S14: open)
        Assert.False(seenAgain);
    }

    /// <summary>
    /// The block read the rally search uses (<see cref="PlayerView.KnownClosedWindow"/>) gives <see cref="PlayerView.IsKnownClosed"/>'s
    /// answer cell for cell: every 250 ticks of a two-seat AI match with early waves and raiders (buildings razed and trees
    /// felled in and out of each seat's sight, ghosts and node memories left behind), for each seat, blocks of 11 x 11 and
    /// 21 x 21 cells laid over the whole map, the map's edges and corners included (cells off the map closed).
    /// </summary>
    [Fact]
    public void TheWindow_AnswersAsIsKnownClosed_CellForCell_ThroughAnAiMatch()
    {
        PlayerSeat[] seats = AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Ai);
        Simulation sim = AiScenes.Generated(seats, 3, record: false, out _, data: AiScenes.EarlyWaves(), raiders: 2);
        NavGrid g = sim.World.NavGrid;
        int checks = 0, remembered = 0;
        var buffer = new bool[21 * 21];
        for (int t = 0; t <= 8000; t++)
        {
            if (t % 250 == 0)
                for (int p = 0; p < 2; p++)
                {
                    PlayerView v = sim.AiOf(p)!.View;
                    foreach (int side in new[] { 11, 21 })
                        for (int y0 = -side / 2; y0 < g.Height; y0 += side - 3)
                            for (int x0 = -side / 2; x0 < g.Width; x0 += side - 3)
                            {
                                v.KnownClosedWindow(x0, y0, side, side, buffer);
                                for (int dy = 0; dy < side; dy++)
                                    for (int dx = 0; dx < side; dx++)
                                    {
                                        int x = x0 + dx, y = y0 + dy;
                                        bool cell = v.IsKnownClosed(x, y);
                                        if (buffer[dy * side + dx] != cell)
                                            Assert.Fail($"tick {sim.TickNumber}, player {p}, block ({x0}, {y0}) x {side}: cell ({x}, {y}) window {buffer[dy * side + dx]}, IsKnownClosed {cell}");
                                        checks++;
                                        if (cell && g.InBounds(x, y) && (g.FlagsAt(x, y) & NavFlags.Blocked) == 0) remembered++;
                                    }
                            }
                }
            sim.Tick();
        }
        _out.WriteLine($"{checks} cell answers compared; {remembered} closed by memory alone (open ground)");
        Assert.True(remembered > 0, "no cell was closed by memory alone: the match never exercised the memory");
    }
}
