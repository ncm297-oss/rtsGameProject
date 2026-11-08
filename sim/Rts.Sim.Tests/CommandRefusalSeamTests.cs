using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Orders;
using Rts.Sim.ViewApi;

namespace Rts.Sim.Tests;

/// <summary>
/// M5-5: the refusal seam (<see cref="Simulation.RefusedForTests"/>, docs/03 "Implementation (M5-5)"): every command phase 1
/// drops is reported once with its kind and detail, an applied one never; the hook changes no hash; and a tick that drops
/// commands allocates nothing with the hook unset. Two human seats on the flat scene (halls and five workers each, no AI).
/// </summary>
[Collection(SerialCollection.Name)]
public class CommandRefusalSeamTests
{
    private static Simulation Scene() =>
        AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, SeatController.Human, SeatController.Human), units: 64, commands: 256);

    private static EntityHandle UnitOf(World w, int owner, int nth = 0)
    {
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == owner && nth-- == 0) return new EntityHandle(i, w.Units.Generation[i]);
        throw new InvalidOperationException($"no unit {nth} of player {owner}");
    }

    private static Vector2 Cell(Simulation sim, int x, int y) => sim.World.NavGrid.CellCenter(x, y);

    /// <summary>One bad command of every kind the seam names, each with the refusal (and detail) it must be reported as; plus a good Move.</summary>
    private static List<(Command Command, CommandRefusal Why, int Detail)> BadCommands(Simulation sim)
    {
        World w = sim.World;
        GameData d = w.Data;
        EntityHandle mine = UnitOf(w, 0), other = UnitOf(w, 1), worker2 = UnitOf(w, 0, 1);
        (int hx, int hy) = AiScenes.HallAt(0);
        Vector2 hall = Cell(sim, hx + 1, hy + 1), open = Cell(sim, 30, 40);
        int workerType = w.Units.TypeId[mine.Index];
        int enemyWorker = StartBase.UnitOfSlot(d, w.FactionOf(1), UnitSlot.Worker);
        int house = StartBase.BuildingOfSlot(d, w.FactionOf(0), BuildingSlot.House);
        int hallSlot = w.Buildings.SlotAt(hx, hy);
        var dead = new EntityHandle(mine.Index, mine.Generation + 1);
        var list = new List<(Command, CommandRefusal, int)>
        {
            (Command.Move(0, mine, new Vector2(float.NaN, 3f)), CommandRefusal.Invalid, 0),
            (Command.Move(0, dead, open), CommandRefusal.UnitGone, 0),
            (Command.Move(0, other, open), CommandRefusal.NotOwner, 0),
            (Command.AttackMove(0, mine, new Vector2(-5f, -5f)), CommandRefusal.OffMap, 0),
            (Command.Attack(0, mine, w.Buildings.HandleOf(hallSlot), isBuilding: true), CommandRefusal.AttackTargetInvalid, 0),
            (Command.UseAbility(0, mine, 0, open), CommandRefusal.AbilityUnavailable, 0),
            (Command.Build(0, worker2, house, Cell(sim, hx, hy)), CommandRefusal.BuildRefused, (int)PlacementError.Blocked),
            (Command.Repair(0, worker2, hall), CommandRefusal.NothingToRepair, 0),
            (Command.Gather(0, worker2, open), CommandRefusal.NoResourceNode, 0),
            (Command.Train(0, open, workerType), CommandRefusal.TrainRefused, (int)TrainError.NoBuilding),
            (Command.Train(0, hall, enemyWorker), CommandRefusal.TrainRefused, (int)TrainError.WrongFaction),
            (Command.Research(0, open, 0), CommandRefusal.ResearchRefused, (int)ResearchError.NoBuilding),
            (Command.Cancel(0, open), CommandRefusal.NoSiteToCancel, 0),
            (Command.CancelTrain(0, hall, 0), CommandRefusal.NoItemToCancel, 0),
            (Command.ClearRally(0, open), CommandRefusal.NoRallyBuilding, 0),
            (Command.SpawnUnit(0, d.Units.Length, open), CommandRefusal.SpawnRefused, 0),
            (Command.SpawnBuilding(0, house, hall), CommandRefusal.SpawnRefused, 0),
            (Command.Move(0, mine, open), CommandRefusal.None, 0), // applied: never reported
        };
        // A full order queue: the queue's capacity in shift-Moves, then one more.
        EntityHandle third = UnitOf(w, 0, 2);
        for (int k = 0; k < OrderConstants.QueueCapacity; k++) list.Add((Command.Move(0, third, Cell(sim, 20 + k, 40), queued: true), CommandRefusal.None, 0));
        list.Add((Command.Move(0, third, Cell(sim, 30, 41), queued: true), CommandRefusal.UnitQueueFull, 0));
        return list;
    }

    /// <summary>
    /// Every bad command is reported exactly once, at the tick it applies, with its kind and detail; the good ones never; a
    /// non-worker's Gather is NotAWorker; a defeated player's command is PlayerDefeated. A sim without the hook, fed the same
    /// commands, has the same hash on every tick (the seam is no state).
    /// </summary>
    [Fact]
    public void EveryDroppedCommand_IsReportedOnce_WithItsKind_AndTheHookChangesNoHash()
    {
        Simulation hooked = Scene(), plain = Scene();
        var seen = new List<(Command Command, CommandRefusal Why, int Detail, int Tick)>();
        hooked.RefusedForTests = (c, why, detail) => seen.Add((c, why, detail, hooked.TickNumber));
        // Both sims one tick, the hashes compared after each.
        void Step()
        {
            hooked.Tick();
            plain.Tick();
            Assert.Equal(plain.StateHash(), hooked.StateHash());
        }
        Step();
        List<(Command Command, CommandRefusal Why, int Detail)> bad = BadCommands(hooked);
        foreach ((Command c, _, _) in bad)
        {
            hooked.Enqueue(c);
            plain.Enqueue(c);
        }
        int applyTick = hooked.TickNumber + 1; // stamped for the next tick number, applied in the tick that starts there
        Step();
        Assert.Empty(seen);
        Step();
        var expected = bad.Where(b => b.Why != CommandRefusal.None).ToList();
        Assert.True(expected.Count == seen.Count, $"expected {string.Join(", ", expected.Select(e => $"{e.Command.Kind}:{e.Why}"))}; seen {string.Join(", ", seen.Select(e => $"{e.Command.Kind}:{e.Why}:{e.Detail}"))}");
        for (int k = 0; k < expected.Count; k++)
        {
            // Phase 1 applies in (player, sequence) order: the enqueue order here.
            Assert.Equal(expected[k].Command.Kind, seen[k].Command.Kind);
            Assert.Equal((expected[k].Why, expected[k].Detail), (seen[k].Why, seen[k].Detail));
            Assert.Equal(applyTick, seen[k].Command.Tick);
            Assert.Equal(applyTick, seen[k].Tick);
        }

        // A soldier asked to gather; then player 1 defeated (everything of its freed) and its Stop dropped.
        World w = hooked.World;
        int line = StartBase.UnitOfSlot(w.Data, w.FactionOf(0), UnitSlot.Line);
        foreach (Simulation s in new[] { hooked, plain }) s.Enqueue(Command.SpawnUnit(0, line, Cell(s, 20, 20)));
        Step();
        Step();
        EntityHandle soldier = default;
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.TypeId[i] == line) soldier = new EntityHandle(i, w.Units.Generation[i]);
        Assert.NotEqual(default, soldier);
        EntityHandle p1 = UnitOf(w, 1);
        foreach (Simulation s in new[] { hooked, plain })
        {
            s.Enqueue(Command.Gather(0, soldier, Cell(s, 10, 24)));
            World sw = s.World;
            for (int i = 0; i < sw.Units.Capacity; i++)
                if (sw.Units.Alive[i] && sw.Units.Owner[i] == 1) sw.Units.Free(new EntityHandle(i, sw.Units.Generation[i]));
            for (int k = 0; k < sw.Buildings.Capacity; k++)
                if (sw.Buildings.Alive[k] && sw.Buildings.Owner[k] == 1) Assert.True(sw.Buildings.Free(sw.Buildings.HandleOf(k)));
        }
        seen.Clear();
        Step();
        Step();
        Assert.True(w.IsDefeated(1));
        Assert.Equal((CommandKind.Gather, CommandRefusal.NotAWorker), (seen.Single().Command.Kind, seen.Single().Why));
        foreach (Simulation s in new[] { hooked, plain }) s.Enqueue(Command.Stop(1, p1));
        seen.Clear();
        Step();
        Step();
        Assert.Equal((CommandKind.Stop, CommandRefusal.PlayerDefeated), (seen.Single().Command.Kind, seen.Single().Why));
        for (int t = 0; t < 40; t++) Step();
    }

    /// <summary>The seam costs nothing in a match: a tick that drops every kind of bad command allocates nothing with the hook unset.</summary>
    [Fact]
    public void ATickThatDropsCommands_WithTheHookUnset_AllocatesNothing()
    {
        Simulation sim = Scene();
        sim.Tick();
        List<(Command Command, CommandRefusal Why, int Detail)> batch = BadCommands(sim);
        Command[] bad = batch.Select(b => b.Command).ToArray();
        int dropped = 0;
        sim.RefusedForTests = (_, _, _) => dropped++;
        // Warm-up: JIT every drop path once (and check this batch is dropped, every command of it).
        foreach (Command c in bad) sim.Enqueue(c);
        sim.Tick();
        sim.Tick();
        Assert.Equal(batch.Count(b => b.Why != CommandRefusal.None), dropped);
        sim.RefusedForTests = null;
        // The setup queues the batch and runs the tick it is stamped from; the measured tick applies (drops) it.
        Action queue = () =>
        {
            foreach (Command c in bad) sim.Enqueue(c);
            sim.Tick();
        };
        Action tick = sim.Tick;
        AllocationProbe.AssertZero(tick, setup: queue);
    }
}
