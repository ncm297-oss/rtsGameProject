using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M6-S15 A2 criterion 4 (docs/01 2026-11-27-1857 (3); docs/03 "Implementation (M6-S15)" A2): the computer player's side of
/// the draw rule's drop-off clause. On the flat scene, seat 0 (an AI of each shipped faction) loses its hall with five workers
/// alive, the two mines and the forest known, and a bank that pays for its faction's small drop-off but not for the hall:
/// it now sends that Build first and its workers deliver there (on the base d26338d3 it banked for the hall for good, its
/// gatherers with nowhere to deliver: red). Guard: a bank that pays for the hall rebuilds the hall first, as before.
/// </summary>
public class SmallDropOffTests
{
    private readonly ITestOutputHelper _out;

    public SmallDropOffTests(ITestOutputHelper output) => _out = output;

    private static Simulation Scene(string faction, int gold, int wood, out List<Command> log)
    {
        int f = TestSim.Data.FindFaction(faction);
        var seats = new[] { new PlayerSeat(f, SeatController.Ai, AiDifficulty.Normal), new PlayerSeat(f == 0 ? 1 : 0, SeatController.Human, AiDifficulty.Normal) };
        Simulation sim = AiScenes.Flat(seats);
        World w = sim.World;
        for (int k = 0; k < w.Buildings.Capacity; k++)
            if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == 0) w.Buildings.Free(new EntityHandle(k, w.Buildings.Generation[k]));
        BuildMaps.SetTotals(sim, 0, gold, wood);
        log = AiScenes.Log(sim, 0);
        return sim;
    }

    /// <summary>The faction's cheapest drop-off by gold, then wood, then data order.</summary>
    private static BuildingDef Small(int faction)
    {
        BuildingDef? best = null;
        foreach (BuildingDef b in TestSim.Data.Buildings)
            if (b.Faction == faction && b.DropOff && (best == null || b.CostGold < best.CostGold || b.CostGold == best.CostGold && b.CostWood < best.CostWood)) best = b;
        return best!;
    }

    [Theory]
    [InlineData("malazan")]
    [InlineData("whirlwind")]
    [InlineData("teblor")]
    public void NoHallNoDropOff_ABankForTheSmallDropOffButNotTheHall_BuildsItAndItsWorkersDeliverThere(string faction)
    {
        int f = TestSim.Data.FindFaction(faction);
        BuildingDef small = Small(f), hall = TestSim.Data.Buildings[StartBase.BuildingOfSlot(TestSim.Data, f, BuildingSlot.TownHall)];
        Assert.NotEqual(hall.Id, small.Id);
        int gold = Math.Max(small.CostGold, 0), wood = small.CostWood + 25;
        Assert.True(gold < hall.CostGold || wood < hall.CostWood, "precondition: the bank does not pay the hall");
        Simulation sim = Scene(faction, gold, wood, out List<Command> log);
        World w = sim.World;
        int built = -1, bankAtFinish = -1;
        for (int t = 0; t < 6000 && bankAtFinish < 0; t++)
        {
            sim.Tick();
            if (built < 0 && log.Any(c => c.Kind == CommandKind.Build && c.TypeId == small.Id)) built = sim.TickNumber;
            for (int k = 0; k < w.Buildings.Capacity && built >= 0; k++)
                if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == 0 && w.Buildings.TypeId[k] == small.Id && !w.Buildings.UnderConstruction[k]) bankAtFinish = w.Gold[0];
        }
        Assert.True(built > 0, $"{faction}: no Build of {small.Key} in 6,000 ticks; sent {string.Join(", ", log.Select(c => $"{c.Kind}:{c.TypeId}@{c.Tick}"))}");
        Assert.True(built <= 2 * sim.AiOf(0)!.ThinkTicks + 1, $"{faction}: the Build came at {built}, not at the first thinks");
        Assert.True(bankAtFinish >= 0, $"{faction}: {small.Key} never finished");
        int finished = sim.TickNumber;
        for (int t = 0; t < 2000; t++) sim.Tick();
        _out.WriteLine($"{faction}: {small.Key} Build sent at {built}, finished at {finished} with {bankAtFinish} gold; 2,000 ticks on {w.Gold[0]} gold / {w.Wood[0]} wood");
        Assert.True(w.Gold[0] > bankAtFinish, $"{faction}: no gold delivered to {small.Key} (bank {bankAtFinish} -> {w.Gold[0]})");
        Assert.Equal(0, log.Count(c => c.Kind == CommandKind.Build && c.TypeId == hall.Id && c.Tick <= built));
    }

    /// <summary>Guard (green on the base): a bank that pays for the hall rebuilds the hall first, no small drop-off.</summary>
    [Theory]
    [InlineData("malazan")]
    [InlineData("whirlwind")]
    [InlineData("teblor")]
    public void Guard_ABankThatPaysTheHall_RebuildsTheHallAsToday(string faction)
    {
        int f = TestSim.Data.FindFaction(faction);
        BuildingDef small = Small(f), hall = TestSim.Data.Buildings[StartBase.BuildingOfSlot(TestSim.Data, f, BuildingSlot.TownHall)];
        Simulation sim = Scene(faction, hall.CostGold + 10, hall.CostWood + 100, out List<Command> log);
        for (int t = 0; t < 4 * sim.AiOf(0)!.ThinkTicks; t++) sim.Tick();
        Command first = log.First(c => c.Kind == CommandKind.Build);
        Assert.Equal(hall.Id, first.TypeId);
        Assert.DoesNotContain(log, c => c.Kind == CommandKind.Build && c.TypeId == small.Id);
    }
}
