using Rts.Sim.Ai;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M6-S12 (BUG-1460, docs/01 2026-11-16 item 3; docs/03 "Implementation (M6-S12)"): a seat whose scout's sweep has ended
/// (phase 2) and that is short of a resource its army mix needs and knows no live node of it sends one worker on the scout's
/// sweep again, even past <c>ai.scoutReplacements</c>; at most one at a time. The flat AI scene, unexplored but for what the
/// seat sees, the Malazan seat's home forest felled and its home mines mined out (it knows no tree: the other base's nodes lie unseen; its five workers stand idle, nothing to gather), 1,000 gold and
/// no wood (every unit of its army mix costs wood), three of its units lost (past the cap of 2) and the sweep ended. Before
/// the rule it sent nobody: its wood never came (Raraku seed 1's winner, 24 workers idle from 36,000).
/// </summary>
[Collection(SerialCollection.Name)] // measures allocation
public class BlindRescoutTests(ITestOutputHelper output)
{
    /// <summary>The quiet order (one Laborer an hour in), so the scout rule alone moves a worker.</summary>
    private static readonly Lazy<GameData> s_quiet = new(() => AiScenes.DataWithAi("malazan", root =>
        AiScenes.Opening(root)["buildOrder"] = AiScenes.BuildOrder(("time", 3600, "train", "malazan_laborer", 1))));

    /// <summary>The quiet data the scene plays (a save of it loads with this).</summary>
    internal static GameData Quiet => s_quiet.Value;

    /// <summary>The blind scene (criterion 9's re-scout twin, <c>Stress/M6S12DeterminismStressTests</c>).</summary>
    internal static Simulation BlindScene() => Scene(keepATree: false);

    /// <summary>The scene; with <paramref name="keepATree"/> one home tree stands (in sight: the seat knows a live tree and its workers go to it).</summary>
    private static Simulation Scene(bool keepATree)
    {
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human), s_quiet.Value, explored: false);
        World w = sim.World;
        int tree = w.Data.FindResource("tree");
        bool kept = false;
        for (int n = 0; n < w.Resources.Capacity; n++)
        {
            if (!w.Resources.Alive[n] || w.Resources.Cell[n] % AiScenes.FlatSize >= AiScenes.FlatSize / 2) continue;
            if (keepATree && !kept && w.Resources.TypeId[n] == tree) { kept = true; continue; }
            w.Resources.Take(w.Resources.HandleOf(n), int.MaxValue);
        }
        w.Ledger.Gold[0] = 1000;
        w.Ledger.Wood[0] = 0;
        int infantry = w.Data.FindUnit("malazan_heavy_infantry");
        (int hx, int hy) = AiScenes.HallAt(0);
        for (int k = 0; k < 3; k++)
        {
            EntityHandle h = CombatScenes.Place(sim, 0, infantry, w.NavGrid.CellCenter(hx + 6, hy + k));
            CombatSystem.HitUnit(w, new PendingHit(default, 1, h, false, w.Units.Hp[h.Index]));
        }
        sim.AiOf(0)!.Board.ScoutPhase = 2; // the sweep ended (as after a scout lost past the cap)
        return sim;
    }

    /// <summary>What a run of <paramref name="ticks"/> came to: the units sent a Move, and every (slot, generation) that was the scout.</summary>
    private static (HashSet<int> Moved, List<(int Slot, int Gen, int From)> Scouts) Run(Simulation sim, int ticks)
    {
        List<Command> log = AiScenes.Log(sim, 0);
        Blackboard b = sim.AiOf(0)!.Board;
        var scouts = new List<(int, int, int)>();
        for (int t = 0; t < ticks; t++)
        {
            sim.Tick();
            if (b.ScoutPhase != 1) continue;
            if (scouts.Count == 0 || scouts[^1].Item1 != b.ScoutSlot || scouts[^1].Item2 != b.ScoutGeneration) scouts.Add((b.ScoutSlot, b.ScoutGeneration, sim.TickNumber));
        }
        var moved = new HashSet<int>();
        foreach (Command c in log)
            if (c.Kind == CommandKind.Move) moved.Add(c.Unit.Index);
        return (moved, scouts);
    }

    /// <summary>Criterion 5 (red on the base: no worker was sent): exactly one worker goes on the sweep, and no second while it lives.</summary>
    [Fact]
    public void ABlindSeatPastTheCap_SendsOneWorkerOnTheSweep()
    {
        Simulation sim = Scene(keepATree: false);
        AiPlayer ai = sim.AiOf(0)!;
        Assert.True(sim.World.UnitLosses[0] > ai.Rules.ScoutReplacements, "precondition: past ai.scoutReplacements");
        (HashSet<int> moved, List<(int Slot, int Gen, int From)> scouts) = Run(sim, 3000);
        foreach (var s in scouts) output.WriteLine($"scout: unit {s.Slot} (gen {s.Gen}) from tick {s.From}");
        output.WriteLine($"units sent a Move: {string.Join(", ", moved)}; phase at the end {ai.Board.ScoutPhase}; losses {sim.World.UnitLosses[0]}");
        Assert.True(scouts.Count == 1, $"{scouts.Count} scouts (want exactly one)");
        Assert.True(sim.World.Data.Units[sim.World.Units.TypeId[scouts[0].Slot]].IsWorker, "the scout is not a worker");
        Assert.True(sim.World.Units.IsAlive(new EntityHandle(scouts[0].Slot, scouts[0].Gen)), "the scout died: the row tests nothing about a second one");
        Assert.Single(moved); // one unit walked the sweep
        Assert.Contains(scouts[0].Slot, moved);
    }

    /// <summary>Criterion 5's control (green on the base and after): a seat that knows a live tree sends none.</summary>
    [Fact]
    public void ASeatThatKnowsALiveTree_SendsNone()
    {
        Simulation sim = Scene(keepATree: true);
        AiPlayer ai = sim.AiOf(0)!;
        sim.Tick();
        Assert.False(ScoutManager.BlindTo(ai, ResourceKind.Wood));
        (HashSet<int> moved, List<(int Slot, int Gen, int From)> scouts) = Run(sim, 3000);
        Assert.Empty(scouts);
        Assert.Empty(moved);
        Assert.Equal(2, ai.Board.ScoutPhase);
    }

    /// <summary>The check a phase-2 seat runs on every think's window allocates nothing (blind or not).</summary>
    [Fact]
    public void TheBlindCheck_AllocatesNothing()
    {
        Simulation sim = Scene(keepATree: true);
        AiPlayer ai = sim.AiOf(0)!;
        sim.Tick();
        AllocationProbe.AssertZero(() => ScoutManager.BlindTo(ai, ResourceKind.Wood));
        AllocationProbe.AssertZero(() => ScoutManager.BlindTo(ai, ResourceKind.Gold));
    }
}
