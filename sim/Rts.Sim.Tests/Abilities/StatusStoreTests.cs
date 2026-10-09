using System.Numerics;
using Rts.Sim.Abilities;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using static Rts.Sim.Tests.AbilityScenes;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests;

/// <summary>M4-4a criterion 4: the status store's stacking rule, Slowed's speed, expiry on time, and death clearing a unit's statuses.</summary>
[Collection(SerialCollection.Name)]
public class StatusStoreTests
{
    [Fact]
    public void Reapplying_KeepsTheLongerDuration_AndTheStrongerMagnitude_DifferentStatusesStack()
    {
        Simulation sim = NoFights();
        World w = sim.World;
        EntityHandle a = Place(sim, 1, Crossbowman, At(sim, 20, 20));
        StatusSystem.Apply(w, a.Index, Burning, 10f, 80, 0);
        GatherMaps.Run(sim, 30);
        Assert.Equal((10f, 50), StatusOf(sim, a, Burning));
        // The same again: the 4 s come back.
        StatusSystem.Apply(w, a.Index, Burning, 10f, 80, 0);
        Assert.Equal((10f, 80), StatusOf(sim, a, Burning));
        // Weaker and shorter: neither the magnitude nor the duration drops.
        StatusSystem.Apply(w, a.Index, Burning, 4f, 20, 0);
        Assert.Equal((10f, 80), StatusOf(sim, a, Burning));
        // Stronger and shorter: the magnitude rises, the longer duration stays, the source follows the stronger.
        StatusSystem.Apply(w, a.Index, Burning, 15f, 20, 1);
        Assert.Equal((15f, 80), StatusOf(sim, a, Burning));
        StatusStore s = w.Units.Statuses;
        Assert.Equal(1, s.SourcePlayer[s.IndexOf(a.Index, Burning)]);
        // A different status stacks beside it.
        StatusSystem.Apply(w, a.Index, Slowed, 0.3f, 40, 0);
        Assert.Equal(2, s.Count[a.Index]);
        Assert.Equal((0.3f, 40), StatusOf(sim, a, Slowed));
    }

    [Fact]
    public void AUnitCarriesAtMost8_TheNinthIsDropped()
    {
        var s = new StatusStore(2);
        for (int k = 0; k < StatusStore.PerUnit; k++) Assert.True(s.Apply(1, k, 1f, 10, 0));
        Assert.False(s.Apply(1, 8, 1f, 10, 0));
        Assert.Equal(StatusStore.PerUnit, s.Count[1]);
        Assert.Equal(-1, s.IndexOf(1, 8));
        Assert.Equal(0, s.Count[0]);
        // Removing keeps the order of the rest.
        s.RemoveAt(1, 1 * StatusStore.PerUnit + 2);
        Assert.Equal(new[] { 0, 1, 3, 4, 5, 6, 7 }, s.StatusId.AsSpan(StatusStore.PerUnit, 7).ToArray());
    }

    /// <summary>Slowed 0.3: over 100 ticks of walking the unit covers 70 % of an unslowed twin's distance; once it expires the speed is back.</summary>
    [Fact]
    public void Slowed03_Covers70PercentOfTheDistance_Over100Ticks_ThenExpires()
    {
        Simulation sim = NoFights();
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle slow = Place(sim, 0, Crossbowman, At(sim, 4, 10));
        EntityHandle fast = Place(sim, 0, Crossbowman, At(sim, 4, 30));
        StatusSystem.Apply(w, slow.Index, Slowed, 0.3f, 120, 1);
        Assert.Equal(u.Speed[fast.Index] * 0.7f, u.Speed[slow.Index], 5);
        sim.Enqueue(Command.Move(0, slow, At(sim, 44, 10)));
        sim.Enqueue(Command.Move(0, fast, At(sim, 44, 30)));
        sim.Tick();
        sim.Tick(); // the Moves apply in this tick; walking starts here
        Vector2 s0 = u.Position[slow.Index], f0 = u.Position[fast.Index];
        GatherMaps.Run(sim, 100);
        float ds = Vector2.Distance(s0, u.Position[slow.Index]), df = Vector2.Distance(f0, u.Position[fast.Index]);
        Assert.True(df > 10f);
        Assert.InRange(ds / df, 0.69f, 0.71f);
        // 120 ticks from the apply: expired, full speed again.
        GatherMaps.Run(sim, 20);
        Assert.Equal(0, u.Statuses.Count[slow.Index]);
        Assert.Equal(u.Speed[fast.Index], u.Speed[slow.Index]);
    }

    [Fact]
    public void BurningAndSlowed_ExpireOnTheirLastTick()
    {
        Simulation sim = NoFights();
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle a = Place(sim, 1, Crossbowman, At(sim, 20, 20));
        StatusSystem.Apply(w, a.Index, Burning, 10f, 80, 0);
        StatusSystem.Apply(w, a.Index, Slowed, 0.5f, 30, 0);
        GatherMaps.Run(sim, 29);
        Assert.Equal((0.5f, 1), StatusOf(sim, a, Slowed));
        sim.Tick();
        Assert.Equal((0f, 0), StatusOf(sim, a, Slowed));
        Assert.Equal(1, u.Statuses.Count[a.Index]);
        GatherMaps.Run(sim, 49);
        Assert.Equal((10f, 1), StatusOf(sim, a, Burning));
        sim.Tick();
        Assert.Equal(0, u.Statuses.Count[a.Index]);
        Assert.Equal(55 - 40, u.Hp[a.Index]);
    }

    [Fact]
    public void Death_ClearsTheStatuses_AndTheSlotsNextUnitStartsClean()
    {
        Simulation sim = NoFights(units: 4);
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle a = Place(sim, 1, Crossbowman, At(sim, 20, 20));
        StatusSystem.Apply(w, a.Index, Burning, 10f, 80, 0);
        StatusSystem.Apply(w, a.Index, Slowed, 0.5f, 80, 0);
        u.Hp[a.Index] = 10;
        GatherMaps.Run(sim, 20); // the first pulse kills it
        Assert.False(u.IsAlive(a));
        Assert.Equal(0, u.Statuses.Count[a.Index]);
        EntityHandle b = Place(sim, 1, Crossbowman, At(sim, 20, 20));
        Assert.Equal(a.Index, b.Index);
        Assert.Equal(0, u.Statuses.Count[b.Index]);
        Assert.Equal(TestSim.Data.Units[Crossbowman].SpeedPerTick, u.Speed[b.Index]);
    }
}
