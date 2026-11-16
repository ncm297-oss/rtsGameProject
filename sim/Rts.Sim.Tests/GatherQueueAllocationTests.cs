using Rts.Sim.Commands;
using Rts.Sim.Economy;
using Rts.Sim.Entities;

namespace Rts.Sim.Tests;

/// <summary>
/// M6-S11 criterion 9 (CLAUDE.md rule 5): the ticks in which gathering workers with orders queued finish their trips, end
/// their loops and start the queue (the drop-off scan, the walk-out check, the delivery's early end, the pops) allocate nothing.
/// </summary>
[Collection(SerialCollection.Name)]
public class GatherQueueAllocationTests
{
    [Fact]
    public void Ticks_WhereGatherersFinishTheirTripsAndStartTheirQueues_AllocateNothing()
    {
        GatherQueueScene s = GatherQueueScene.Create(43);
        Simulation sim = s.Sim;
        UnitStore u = sim.World.Units;
        // Every live worker gathers again (unqueued), so each one is on a loop when the queued orders land.
        void Regather()
        {
            foreach (EntityHandle h in s.Workers)
                if (u.IsAlive(h)) sim.Enqueue(Command.Gather(u.Owner[h.Index], h, u.Position[h.Index]));
        }
        int started = 0;
        int[] queuedAt = new int[s.Workers.Length];
        Action queue = () =>
        {
            Regather();
            for (int t = 0; t < 120; t++) sim.Tick(); // workers out at their nodes, some loaded, some walking back
            for (int k = 0; k < s.Workers.Length; k++)
            {
                EntityHandle h = s.Workers[k];
                if (!u.IsAlive(h)) continue;
                int p = u.Owner[h.Index];
                sim.Enqueue(Command.Move(p, h, u.Position[h.Index] + new System.Numerics.Vector2(3f, 0f), queued: true));
                sim.Enqueue(Command.HoldPosition(p, h, queued: true));
            }
            sim.Tick();
            sim.Tick();
            for (int k = 0; k < s.Workers.Length; k++) queuedAt[k] = u.IsAlive(s.Workers[k]) ? u.QueueCount[s.Workers[k].Index] : 0;
        };
        Action ticks = () =>
        {
            for (int t = 0; t < 600; t++) sim.Tick();
        };
        queue();
        ticks(); // warm-up: JITs the trip's end and the pops
        AllocationProbe.AssertZero(ticks, setup: queue);
        for (int k = 0; k < s.Workers.Length; k++)
            if (queuedAt[k] > 0 && u.IsAlive(s.Workers[k]) && u.QueueCount[s.Workers[k].Index] < queuedAt[k] && !EconomySystem.OnLoop(u, s.Workers[k].Index)) started++;
        Assert.True(started >= s.Workers.Length / 2, $"only {started} of {s.Workers.Length} workers started their queue in the measured ticks");
    }
}
