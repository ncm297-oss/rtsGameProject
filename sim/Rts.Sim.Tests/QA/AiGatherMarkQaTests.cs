using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// QA (M5-2 re-check round 1, session 2026-10-11-0634): attacks on BUG-0570's fix, the economy manager's dropped-Gather marks
/// (<see cref="Blackboard.NodeDropped"/>). Every remembered node of both kinds gone: Gathers stay bounded and stop; a live mine
/// out of sight is never marked while workers walk to it; marks are deterministic; a live mine a loaded worker can't start
/// on (no drop-off) must not be blacklisted for good (BUG-0574).
/// </summary>
public class AiGatherMarkQaTests
{
    private readonly ITestOutputHelper _out;

    public AiGatherMarkQaTests(ITestOutputHelper output) => _out = output;

    /// <summary>An ai.json for malazan whose only step can't fire for an hour, so a scene's commands are the economy's.</summary>
    private static readonly Lazy<GameData> s_quietOrder = new(() => AiScenes.DataWithAi("malazan", root =>
        AiScenes.Opening(root)["buildOrder"] = AiScenes.BuildOrder(("time", 3600, "train", "malazan_laborer", 1))));

    private static bool FarHalf(int cell) => cell % AiScenes.FlatSize >= AiScenes.FlatSize / 2;

    /// <summary>Takes every node of <paramref name="typeKey"/> the filter picks (alive ones), returns their slots.</summary>
    private static List<int> TakeAll(World w, string typeKey, Func<int, bool> pickCell)
    {
        ResourceStore r = w.Resources;
        int type = w.Data.FindResource(typeKey);
        var taken = new List<int>();
        for (int k = 0; k < r.Capacity; k++)
        {
            if (!r.Alive[k] || r.TypeId[k] != type || !pickCell(r.Cell[k])) continue;
            taken.Add(k);
            r.Take(new EntityHandle(k, r.Generation[k]), int.MaxValue);
        }
        return taken;
    }

    /// <summary>
    /// Every node player 0 remembers is gone (both far mines and all of player 1's trees, out of player 0's sight; its own mines
    /// and trees taken in its sight, so forgotten). Each Gather to a gone node is dropped at apply; with the fix every such node
    /// is marked once, so the Gathers stay within workers x remembered nodes, and once all are marked the AI sends none.
    /// </summary>
    [Fact]
    public void EveryRememberedNodeGone_BothKinds_GathersBounded_ThenNone()
    {
        Simulation sim = BothKindsGoneScene(out int remembered);
        World w = sim.World;
        List<Command> log = AiScenes.Log(sim, 0);
        AiPlayer ai = sim.AiOf(0)!;
        int think = ai.ThinkTicks;
        for (int t = 0; t < 40 * think; t++) sim.Tick();
        int early = log.Count(c => c.Kind == CommandKind.Gather);
        for (int t = 0; t < 40 * think; t++) sim.Tick();
        int total = log.Count(c => c.Kind == CommandKind.Gather);
        int workers = 0;
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == 0) workers++;
        int marked = 0;
        foreach (int m in ai.Board.NodeDropped) if (m != 0) marked++;
        _out.WriteLine($"remembered gone {remembered}; workers {workers}; Gathers after 40 thinks {early}, after 80 {total}; marked {marked}");
        Assert.True(total <= workers * remembered, $"{total} Gathers > {workers} workers x {remembered} remembered nodes");
        Assert.Equal(early, total); // every remembered node marked within 40 thinks: nothing sent after
        Assert.Equal(remembered, marked);
    }

    /// <summary>The same scene twice: the same hash at every think boundary (the marks feed commands, so they must be deterministic).</summary>
    [Fact]
    public void Marks_AreDeterministic_TwoRunsSameHashes()
    {
        Simulation a = BothKindsGoneScene(out _), b = BothKindsGoneScene(out _);
        int think = a.AiOf(0)!.ThinkTicks;
        for (int t = 0; t < 60 * think; t++)
        {
            a.Tick();
            b.Tick();
            if (t % think == 0) Assert.Equal(a.StateHash(), b.StateHash());
        }
        Assert.Equal(a.AiOf(0)!.Board.NodeDropped, b.AiOf(0)!.Board.NodeDropped);
    }

    private Simulation BothKindsGoneScene(out int remembered)
    {
        PlayerSeat[] seats = AiScenes.Seats(AiDifficulty.Hard, SeatController.Ai, SeatController.Human);
        Simulation sim = AiScenes.Flat(seats, s_quietOrder.Value);
        World w = sim.World;
        List<int> farMines = TakeAll(w, "gold_mine", FarHalf), farTrees = TakeAll(w, "tree", FarHalf);
        TakeAll(w, "gold_mine", c => true);
        TakeAll(w, "tree", c => true);
        sim.Tick(); // tick 0: fog update, first think
        var view = new PlayerView(w, 0);
        remembered = 0;
        int s = -1;
        while (view.NextResource(ref s, out SeenResource n))
        {
            Assert.False(w.Resources.Alive[s], $"precondition: node slot {s} is gone");
            remembered++;
        }
        // The far ones are out of sight (remembered); the near mines too (the hall's and workers' sight stop short of them).
        Assert.True(remembered >= farMines.Count + farTrees.Count, $"precondition: {remembered} remembered");
        return sim;
    }

    /// <summary>
    /// The plain scene: player 0's own two mines are live and out of its sight (11 cells above its workers). The first think's
    /// gold share sends workers to them: they walk (Moving at the next think, so their Gather is cleared) and mine. No live
    /// node is ever marked, and gold comes in.
    /// </summary>
    [Fact]
    public void ALiveMineOutOfSight_IsNeverMarked_WorkersWalkThereAndMineIt()
    {
        PlayerSeat[] seats = AiScenes.Seats(AiDifficulty.Hard, SeatController.Ai, SeatController.Human);
        Simulation sim = AiScenes.Flat(seats, s_quietOrder.Value);
        World w = sim.World;
        AiPlayer ai = sim.AiOf(0)!;
        int think = ai.ThinkTicks;
        int startGold = w.Gold[0];
        for (int t = 0; t < 200 * think; t++)
        {
            sim.Tick();
            for (int k = 0; k < ai.Board.NodeDropped.Length; k++)
                if (ai.Board.NodeDropped[k] != 0)
                    Assert.True(!w.Resources.Alive[k] || w.Resources.Generation[k] != ai.Board.NodeDropped[k],
                        $"tick {t}: live node slot {k} marked dropped");
        }
        _out.WriteLine($"gold {startGold} -> {w.Gold[0]}");
        Assert.True(w.Gold[0] > startGold, "no gold mined at the far live mines");
    }

    /// <summary>
    /// BUG-0574. Player 0 has lost its Town Hall (no drop-off) and its workers carry full gold loads; its own mines are live,
    /// out of its sight. A Gather of a gold mine with a full gold load goes to a drop-off first, there is
    /// none, so the loop ends at once: the worker is Idle at the next think, the node out of sight, and the fix marks the live
    /// mine as gone. The mark never clears (only a new generation in the slot escapes it), so once a drop-off stands again the
    /// AI never sends a worker to that mine. A live node must not be marked.
    /// </summary>
    [Fact] // BUG-0574 fixed in M5-3: no Gather is judged while the player has no drop-off or the worker carries a full load of its kind
    public void ALiveMine_IsNotMarked_WhenALoadedWorkerHasNoDropOff()
    {
        PlayerSeat[] seats = AiScenes.Seats(AiDifficulty.Hard, SeatController.Ai, SeatController.Human);
        Simulation sim = AiScenes.Flat(seats, s_quietOrder.Value);
        World w = sim.World;
        BuildingStore b = w.Buildings;
        for (int k = 0; k < b.Capacity; k++)
            if (b.IsAlive(new EntityHandle(k, b.Generation[k])) && b.Owner[k] == 0) Assert.True(b.Free(new EntityHandle(k, b.Generation[k])));
        UnitStore u = w.Units;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || u.Owner[i] != 0) continue;
            u.Cargo[i] = w.Data.Rules.WorkerCarry;
            u.CargoKind[i] = ResourceKind.Gold;
        }
        AiPlayer ai = sim.AiOf(0)!;
        int think = ai.ThinkTicks;
        for (int t = 0; t < 10 * think; t++) sim.Tick();
        var live = new List<string>();
        for (int k = 0; k < ai.Board.NodeDropped.Length; k++)
            if (ai.Board.NodeDropped[k] != 0 && w.Resources.Alive[k] && w.Resources.Generation[k] == ai.Board.NodeDropped[k])
                live.Add($"slot {k} (type {w.Resources.TypeId[k]})");
        _out.WriteLine($"live nodes marked dropped: {string.Join(", ", live)}");
        Assert.Empty(live);
    }
}
