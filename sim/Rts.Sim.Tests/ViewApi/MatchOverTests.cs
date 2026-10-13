using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;
using static Rts.Sim.Tests.BuildMaps;
using static Rts.Sim.Tests.GatherMaps;
using static Rts.Sim.Tests.ResourceMaps;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>M6-V2: the view's dev "match over" watch (<see cref="MatchOver"/>): a player's building count going from above 0 to 0 is reported once.</summary>
[Collection(SerialCollection.Name)]
public class MatchOverTests
{
    [Fact]
    public void Spans_OneToZero_ReportsOnce_AndStaysQuietAtZero()
    {
        var watch = new MatchOver(2);
        bool[] alive = { true, true, false };
        int[] owner = { 0, 1, 0 };
        Assert.Equal(0, watch.Observe(alive, owner));
        Assert.Equal((1, 1), (watch.BuildingCount(0), watch.BuildingCount(1)));

        alive[1] = false; // player 1's only building razed
        Assert.Equal(1, watch.Observe(alive, owner));
        Assert.True(watch.JustEmptied(1));
        Assert.False(watch.JustEmptied(0));
        for (int k = 0; k < 5; k++)
        {
            Assert.Equal(0, watch.Observe(alive, owner));
            Assert.False(watch.JustEmptied(1));
        }
    }

    [Fact]
    public void Spans_ZeroFromTheStart_IsNeverReported()
    {
        var watch = new MatchOver(3);
        bool[] alive = { true, false, false, false };
        int[] owner = { 0, 1, 2, 2 };
        for (int k = 0; k < 10; k++) Assert.Equal(0, watch.Observe(alive, owner));
        Assert.False(watch.JustEmptied(1) || watch.JustEmptied(2));
    }

    [Fact]
    public void Spans_RebuiltThenRazed_IsReportedAgain()
    {
        var watch = new MatchOver(2);
        bool[] alive = { true, false };
        int[] owner = { 0, 0 };
        watch.Observe(alive, owner);
        alive[0] = false;
        Assert.Equal(1, watch.Observe(alive, owner));
        alive[1] = true; // a new building in another slot
        Assert.Equal(0, watch.Observe(alive, owner));
        alive[1] = false;
        Assert.Equal(1, watch.Observe(alive, owner));
        Assert.True(watch.JustEmptied(0));
    }

    [Fact]
    public void Spans_TwoPlayersEmptiedTogether_AndAStrayOwner_AreCountedRight()
    {
        var watch = new MatchOver(2);
        bool[] alive = { true, true, true, true };
        int[] owner = { 0, 1, 7, -1 };
        watch.Observe(alive, owner);
        Assert.Equal((1, 1, 0), (watch.BuildingCount(0), watch.BuildingCount(1), watch.BuildingCount(7)));
        alive[0] = alive[1] = false;
        Assert.Equal(2, watch.Observe(alive, owner));
        Assert.True(watch.JustEmptied(0) && watch.JustEmptied(1));
        Assert.False(watch.JustEmptied(-1) || watch.JustEmptied(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MatchOver(0));
    }

    /// <summary>
    /// A hand-built world: player 0's only building is a site it cancels; player 1 never has one. M5-4b: the cancel defeats
    /// player 0 (the sim's defeat rule), so its rebuild is dropped at the command door and nothing more is reported (the
    /// watch's own rebuild case: <see cref="Spans_RebuiltThenRazed_IsReportedAgain"/>).
    /// </summary>
    [Fact]
    public void World_ACancelledLastSite_IsReportedOnTheTickItGoes_AndARebuildIsDropped()
    {
        Simulation sim = BuildMaps.NewSim(Flat(30, 20), players: 2);
        EntityHandle w = Unit(sim, At(sim, 5, 5));
        var watch = new MatchOver(2);
        var reports = new List<(int Tick, int Player)>();
        for (int round = 0; round < 2; round++)
        {
            SetTotals(sim, 0, 2000, 2000);
            sim.Enqueue(Command.Build(0, w, Keep, At(sim, 10, 10)));
            for (int t = 0; t < 4; t++) Step(sim, watch, reports);
            Assert.Equal(1 - round, watch.BuildingCount(0));
            if (round == 1) break;
            sim.Enqueue(Command.Cancel(0, At(sim, 11, 11)));
            int applies = sim.TickNumber + 2; // queued between ticks, it applies in the second Tick() from here (ConstructionSystemTests.Cancel_*)
            for (int t = 0; t < 6; t++) Step(sim, watch, reports);
            Assert.Equal(0, watch.BuildingCount(0));
            Assert.Equal((applies, 0), reports[round]);
            Assert.True(sim.World.IsDefeated(0));
        }
        Assert.Single(reports);
    }

    /// <summary>The default match (100 units and a Town Hall with five workers a side) for 400 ticks: nobody's last building falls.</summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(7UL)]
    public void ANormalMatch_400Ticks_ReportsNothing(ulong seed)
    {
        (Simulation sim, Vector2[][] blocks, StartBasePlan plan) = StartBaseTests.MatchSetup(seed, 100, 5);
        var watch = new MatchOver(sim.World.Config.PlayerCount);
        StartBaseTests.Apply(sim, blocks, plan);
        var reports = new List<(int Tick, int Player)>();
        while (sim.TickNumber < 400) Step(sim, watch, reports);
        Assert.Empty(reports);
        Assert.True(watch.BuildingCount(0) >= 1 && watch.BuildingCount(1) >= 1);
    }

    [Fact]
    public void Observe_AllocatesNothing()
    {
        (Simulation sim, Vector2[][] blocks, StartBasePlan plan) = StartBaseTests.MatchSetup(3, 10, 5);
        StartBaseTests.Apply(sim, blocks, plan);
        var watch = new MatchOver(2);
        World world = sim.World;
        int sum = 0;
        Action block = () =>
        {
            for (int k = 0; k < 100; k++) sum += watch.Observe(world.Buildings.Alive, world.Buildings.Owner);
        };
        AllocationProbe.AssertZero(block);
        Assert.Equal(0, sum);
    }

    private static void Step(Simulation sim, MatchOver watch, List<(int Tick, int Player)> reports)
    {
        sim.Tick();
        if (watch.Observe(sim.World.Buildings.Alive, sim.World.Buildings.Owner) == 0) return;
        for (int p = 0; p < watch.PlayerCount; p++)
            if (watch.JustEmptied(p)) reports.Add((sim.TickNumber, p));
    }
}
