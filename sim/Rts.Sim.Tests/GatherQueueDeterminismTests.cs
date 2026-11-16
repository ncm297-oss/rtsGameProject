using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Replays;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M6-S11 criterion 6: queued orders landing on gathering workers (<see cref="GatherQueueScene"/>) are deterministic: twins
/// hash alike every tick for 6,000 ticks, a save taken while a worker is finishing its trip with orders queued loads and
/// follows the run hash for hash, and the recording replays with every checkpoint equal. No new saved state.
/// </summary>
public class GatherQueueDeterminismTests
{
    private readonly ITestOutputHelper _out;

    public GatherQueueDeterminismTests(ITestOutputHelper output) => _out = output;

    /// <summary>Workers of <paramref name="sim"/> finishing a trip with orders queued: on a loop, carrying, queue not empty.</summary>
    private static int FinishingWithQueue(Simulation sim)
    {
        UnitStore u = sim.World.Units;
        int n = 0;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && EconomySystem.OnLoop(u, i) && u.Cargo[i] > 0 && u.QueueCount[i] > 0) n++;
        return n;
    }

    [Fact]
    public void QueuedOrdersOnGatherers_TwinsHashEqualEveryTick_For6000Ticks_AndTheReplayPlaysBack()
    {
        GatherQueueScene a = GatherQueueScene.Create(31, record: true);
        GatherQueueScene b = GatherQueueScene.Create(31);
        Assert.Equal(a.Sim.StateHash(), b.Sim.StateHash());
        int finishing = 0, delivered = 0;
        int gathered = a.Sim.World.GatheredGold[0] + a.Sim.World.GatheredWood[0];
        for (int t = 0; t < 6000; t++)
        {
            a.Issue(t, a.Sim);
            b.Issue(t, b.Sim);
            a.Sim.Tick();
            b.Sim.Tick();
            Assert.True(a.Sim.StateHash() == b.Sim.StateHash(), $"twins differ after tick {a.Sim.TickNumber}");
            finishing += FinishingWithQueue(a.Sim);
        }
        delivered = a.Sim.World.GatheredGold[0] + a.Sim.World.GatheredWood[0] - gathered;
        _out.WriteLine($"worker-ticks finishing a trip with orders queued: {finishing}; player 0 delivered {delivered}");
        Assert.True(finishing > 0, "no worker ever finished a trip with orders queued: the row proves nothing");

        Replay recorded = a.Recorder!.ToReplay();
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(ReplayFormat.Write(recorded), out Replay? parsed));
        Assert.Equal(recorded.FormatVersion, parsed!.FormatVersion);
        ReplayResult played = ReplayPlayer.Run(parsed, TestSim.Data);
        Assert.True(played.Ok, played.ToString());
        Assert.Equal(recorded.TickCount, played.TicksRun);
    }

    [Fact]
    public void ASaveTakenWhileAWorkerFinishesItsTripWithOrdersQueued_LoadsAndFollowsTheRun()
    {
        GatherQueueScene a = GatherQueueScene.Create(37);
        int t = 0;
        // Run until some worker is finishing a trip with orders queued and the stream is between commands.
        for (; t < 6000 && (FinishingWithQueue(a.Sim) == 0 || a.Sim.PendingCommandCount > 0 || t < 300); t++)
        {
            a.Issue(t, a.Sim);
            a.Sim.Tick();
        }
        Assert.True(FinishingWithQueue(a.Sim) > 0, "no worker finishing a trip with orders queued");
        byte[] bytes = SaveTestRun.Save(a.Sim);
        Simulation load = SaveTestRun.Load(TestSim.Data, bytes);
        Assert.Equal(bytes, SaveTestRun.Save(load));
        SaveTestRun.AssertSameState(a.Sim, load, "at the save");
        _out.WriteLine($"saved at tick {a.Sim.TickNumber} with {FinishingWithQueue(a.Sim)} worker(s) finishing a trip with orders queued");
        for (int k = 0; k < 2000; k++, t++)
        {
            a.Issue(t, a.Sim, load);
            a.Sim.Tick();
            load.Tick();
            Assert.True(a.Sim.StateHash() == load.StateHash(), $"diverged at tick {a.Sim.TickNumber}");
        }
        SaveTestRun.AssertSameState(a.Sim, load, "save + 2000");
    }
}
