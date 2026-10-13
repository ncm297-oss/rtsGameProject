using System.Collections.Immutable;
using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.Tests.ViewApi;
using Rts.Sim.ViewApi;

namespace Rts.Sim.Tests.QA.ViewApi;

/// <summary>
/// QA (M6-V2, session 2026-10-12-1930): the view's dev "match over" watch (<see cref="MatchOver"/>) is read-only (a twin
/// observed every tick hashes equal to a bare twin, with AI seats thinking) and stays quiet when nobody ever had a building
/// (<c>--no-bases</c>, AI seats included).
/// </summary>
[Collection(SerialCollection.Name)]
public class MatchOverQaTests
{
    private static Simulation Seated(ulong seed, ImmutableArray<PlayerSeat> seats, int perPlayer, int workers, bool bases)
    {
        SimConfig config = TestSim.Config(seed, 2, 2010, 4096) with
        {
            Map = MapGenParams.Default with { Forests = 12, GoldMines = 8 },
            Seats = seats,
        };
        var sim = new Simulation(config);
        World w = sim.World;
        var blocks = new Vector2[2][];
        for (int p = 0; p < 2; p++)
        {
            FactionDef f = w.Data.Factions[w.FactionOf(p)];
            float r = 0f;
            foreach (int t in f.Units) r = Math.Max(r, w.Data.Units[t].Radius);
            blocks[p] = StartLayout.Block(w.NavGrid, perPlayer, west: p == 0, r);
        }
        StartBasePlan plan = ViewReads.Plan(w, blocks, bases ? workers : 0, StartBaseTests.MaxRadius(w.Data));
        for (int p = 0; p < 2; p++)
        {
            FactionDef f = w.Data.Factions[w.FactionOf(p)];
            for (int k = 0; k < blocks[p].Length; k++) sim.Enqueue(Commands.Command.SpawnUnit(p, f.Units[k % f.Units.Length], blocks[p][k]));
            if (!bases || plan.HallAnchor[p] < 0) continue;
            int a = plan.HallAnchor[p];
            sim.Enqueue(Commands.Command.SpawnBuilding(p, plan.HallType[p], w.NavGrid.CellCenter(a % w.NavGrid.Width, a / w.NavGrid.Width)));
            foreach (Vector2 v in plan.Workers[p]) sim.Enqueue(Commands.Command.SpawnUnit(p, plan.WorkerType[p], v));
        }
        return sim;
    }

    private static ImmutableArray<PlayerSeat> Seats(int f0, SeatController c0, int f1, SeatController c1) =>
        ImmutableArray.Create(new PlayerSeat(f0, c0, AiDifficulty.Hard), new PlayerSeat(f1, c1, AiDifficulty.Hard));

    /// <summary>Two AI seats (Hard), a base and 20 units a side, combat on: a twin observed after every tick equals a bare twin's hash every tick.</summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(9UL)]
    public void ObservedEveryTick_WithAiSeats_HashEqualsABareTwin_1200Ticks(ulong seed)
    {
        int mal = TestSim.Data.FindFaction("malazan"), whirl = TestSim.Data.FindFaction("whirlwind");
        ImmutableArray<PlayerSeat> seats = Seats(mal, SeatController.Ai, whirl, SeatController.Ai);
        Simulation a = Seated(seed, seats, 20, 5, bases: true), b = Seated(seed, seats, 20, 5, bases: true);
        var watch = new MatchOver(2);
        int reports = 0;
        for (int t = 0; t < 1200; t++)
        {
            a.Tick();
            b.Tick();
            ulong before = a.StateHash();
            reports += watch.Observe(a.World.Buildings.Alive, a.World.Buildings.Owner);
            Assert.Equal(before, a.StateHash());
            Assert.Equal(b.StateHash(), a.StateHash());
        }
        Assert.True(watch.BuildingCount(0) + watch.BuildingCount(1) > 0, "no building at all after 1,200 ticks");
        Assert.True(reports >= 0);
    }

    /// <summary><c>--no-bases</c> with the AI seat: nobody ever has a building, so 1,500 ticks report nothing (and the AI with no hall does not crash the sim).</summary>
    [Fact]
    public void NoBases_WithAnAiSeat_NeverReports()
    {
        int mal = TestSim.Data.FindFaction("malazan"), whirl = TestSim.Data.FindFaction("whirlwind");
        Simulation sim = Seated(4, Seats(mal, SeatController.Human, whirl, SeatController.Ai), 30, 0, bases: false);
        var watch = new MatchOver(2);
        for (int t = 0; t < 1500; t++)
        {
            sim.Tick();
            Assert.Equal(0, watch.Observe(sim.World.Buildings.Alive, sim.World.Buildings.Owner));
        }
        Assert.Equal(0, sim.World.Buildings.Count);
    }

    /// <summary>A watch sized for fewer players than the store's owners, and empty spans: never throws, never reports.</summary>
    [Fact]
    public void EmptyAndMismatchedSpans_AreQuiet()
    {
        var watch = new MatchOver(1);
        Assert.Equal(0, watch.Observe(ReadOnlySpan<bool>.Empty, ReadOnlySpan<int>.Empty));
        Assert.Equal(0, watch.Observe(new[] { true, true }, new[] { 3 })); // owner span shorter: only slot 0 is read
        Assert.Equal(0, watch.BuildingCount(0));
        Assert.Equal(0, watch.Observe(new[] { true }, new[] { 0, 0, 0 }));
        Assert.Equal(1, watch.BuildingCount(0));
        Assert.Equal(1, watch.Observe(new[] { false }, new[] { 0 }));
        Assert.Equal(0, watch.BuildingCount(int.MaxValue));
        Assert.False(watch.JustEmptied(int.MinValue));
    }
}
