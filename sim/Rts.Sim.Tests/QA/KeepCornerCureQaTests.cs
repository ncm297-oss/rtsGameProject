using System.Numerics;
using Rts.Sim.Entities;
using Rts.Sim.Movement;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M6-S10 (session 2026-11-15-0506): the Keep-corner cure (<c>MovementSystem.QueuesBehind</c>, <c>EconomyConstants.QueueArrival</c>)
/// on the shipped data (<c>gatherOverlap</c> 0.5). (1) The cure's constant keeps its promise: a worker that stops within
/// <c>QueueArrival</c> of a corner cell's stand point is in reach of the footprint. (2) A crowded start mine (24 Laborers + the
/// AI-free generated map): twins hash-equal every tick, and a save taken on the first tick a gather pair stands overlapped
/// continues hash-equal to the uninterrupted run. (3) The same scene ticks without allocating.
/// </summary>
[Collection(SerialCollection.Name)]
public class KeepCornerCureQaTests(ITestOutputHelper output)
{
    [Fact]
    public void QueueArrival_PlusTheCornerPointsInset_IsTheReach()
    {
        // A corner cell's stand point is GoalInset off the footprint along both axes; a stop within QueueArrival of it must
        // be within Reach of the footprint (triangle inequality: QueueArrival + GoalInset * sqrt 2 <= Reach).
        float worst = Economy.EconomyConstants.QueueArrival + Economy.EconomyConstants.GoalInset * MathF.Sqrt(2f);
        Assert.True(worst <= Economy.EconomyConstants.Reach + 1e-5f, $"{worst} > reach {Economy.EconomyConstants.Reach}");
        Assert.True(Economy.EconomyConstants.QueueArrival > MovementConstants.ArrivalDistance, "the band would be empty: every crowded stop refused");
    }

    private static (Simulation Sim, EntityHandle[] Workers) CrowdedStartMine(ulong seed)
    {
        SimConfig config = TestSim.Config(Seed: seed, PlayerCount: 2, UnitCapacity: 64, CommandCapacity: 256)
            with { Map = Map.MapGenParams.Default with { Forests = 12, GoldMines = 8 }, Combat = false };
        var sim = new Simulation(config);
        EntityHandle[] hs = EconomyScenario.Setup(sim, 24, player: 0, mineIndex: 0);
        return (sim, hs);
    }

    private static bool AnyOverlappedPair(UnitStore u, EntityHandle[] hs)
    {
        for (int a = 0; a < hs.Length; a++)
            for (int b = a + 1; b < hs.Length; b++)
            {
                int i = hs[a].Index, j = hs[b].Index;
                if (MovementSystem.IsGatherPair(u, i, j) && Vector2.Distance(u.Position[i], u.Position[j]) < u.Radius[i] + u.Radius[j] - 0.01f) return true;
            }
        return false;
    }

    [Fact]
    public void CrowdedStartMine_OnTheShippedData_TwinsAndASaveWhileOverlapped_StayHashEqual()
    {
        Assert.True(TestSim.Data.Rules.GatherOverlap == 0.5f);
        var (a, hsA) = CrowdedStartMine(11);
        var (b, _) = CrowdedStartMine(11);
        Simulation? loaded = null;
        int savedAt = -1;
        for (int t = 0; t < 6000; t++)
        {
            a.Tick();
            b.Tick();
            loaded?.Tick();
            ulong h = a.StateHash();
            Assert.True(h == b.StateHash(), $"twins differ at tick {a.TickNumber}");
            if (loaded != null) Assert.True(h == loaded.StateHash(), $"the save taken at {savedAt} differs at tick {a.TickNumber}");
            if (loaded == null && a.TickNumber > 400 && AnyOverlappedPair(a.World.Units, hsA))
            {
                savedAt = a.TickNumber;
                loaded = SaveTestRun.Load(TestSim.Data, SaveTestRun.Save(a));
                Assert.Equal(a.StateHash(), loaded.StateHash());
            }
        }
        output.WriteLine($"twins equal 6,000 ticks; saved at {savedAt} with a gather pair overlapped, equal to the end; banked {a.World.Gold[0] + a.World.Wood[0]}");
        Assert.True(savedAt > 0, "no gather pair ever stood overlapped: the scene did not test the save");
    }

    [Fact]
    public void CrowdedStartMine_OnTheShippedData_TicksWithoutAllocating()
    {
        var (sim, _) = CrowdedStartMine(11);
        for (int t = 0; t < 1200; t++) sim.Tick(); // warm: the crowd is at the mine and the Keep, queues formed
        Action block = () => { for (int t = 0; t < 200; t++) sim.Tick(); };
        AllocationProbe.AssertZero(block, output);
    }
}
