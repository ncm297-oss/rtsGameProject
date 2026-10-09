using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;
using static Rts.Sim.Tests.AbilityScenes;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>M4-V6a: the view's "only the nearest ready selected caster casts" rule and the ability button's cooldown read (<see cref="AbilityCaster"/>).</summary>
[Collection(SerialCollection.Name)]
public class AbilityCasterTests
{
    private static int TelasFire => TestSim.Data.FindAbility("telas_fire");

    private static int Pick(Simulation sim, EntityHandle[] sel, Vector2 point, bool queued, out int index) =>
        AbilityCaster.PickCaster(sel, U(sim).Alive, U(sim).Generation, U(sim).TypeId, U(sim).Position, U(sim).CastAbility, U(sim).AbilityReadyTick,
            sim.World.Data.Units, TelasFire, sim.World.TickNumber, point, queued, out index);

    private static UnitStore U(Simulation sim) => sim.World.Units;

    private static int Soonest(Simulation sim, EntityHandle[] sel, int tick) =>
        AbilityCaster.SoonestReady(sel, U(sim).Alive, U(sim).Generation, U(sim).TypeId, U(sim).AbilityReadyTick, sim.World.Data.Units, TelasFire, tick);

    private static void Cooldown(Simulation sim, EntityHandle unit, int ticksLeft) =>
        sim.World.Units.AbilityReadyTick[unit.Index * DataLimits.MaxUnitAbilities] = sim.World.TickNumber + ticksLeft;

    [Fact]
    public void TwoReadyMages_TheNearerToThePointCasts_AbilityIndexZero()
    {
        Simulation sim = NoFights();
        EntityHandle west = Place(sim, 0, Mage, At(sim, 10, 20)), east = Place(sim, 0, Mage, At(sim, 30, 20));
        EntityHandle soldier = Place(sim, 0, HeavyInfantry, At(sim, 21, 20));
        var sel = new[] { soldier, west, east };
        Assert.Equal(west.Index, Pick(sim, sel, At(sim, 18, 20), false, out int k));
        Assert.Equal(0, k);
        Assert.Equal(east.Index, Pick(sim, sel, At(sim, 22, 20), false, out _));
        // A tie keeps the earlier in the selection.
        Assert.Equal(west.Index, Pick(sim, sel, At(sim, 20, 20), false, out _));
        Assert.Equal(east.Index, Pick(sim, new[] { east, west }, At(sim, 20, 20), false, out _));
    }

    [Fact]
    public void TheNearerOnCooldown_TheReadyOneCasts_BothOnCooldown_NoneAndTheSoonestTicks()
    {
        Simulation sim = NoFights();
        EntityHandle near = Place(sim, 0, Mage, At(sim, 10, 20)), far = Place(sim, 0, Mage, At(sim, 30, 20));
        var sel = new[] { near, far };
        // A real cast puts the near one on cooldown (25 s from its resolve).
        sim.Enqueue(Command.UseAbility(0, near, 0, At(sim, 14, 20)));
        TickOf(sim, near, resolved: true);
        Assert.Equal(500, AbilityCaster.CooldownLeft(U(sim).AbilityReadyTick, near.Index, 0, sim.World.TickNumber - 1));
        Assert.Equal(far.Index, Pick(sim, sel, At(sim, 12, 20), false, out int k));
        Assert.Equal(0, k);
        Assert.Equal(0, Soonest(sim, sel, sim.World.TickNumber));
        Cooldown(sim, far, 120);
        Assert.Equal(-1, Pick(sim, sel, At(sim, 12, 20), false, out k));
        Assert.Equal(-1, k);
        Assert.Equal(-1, Pick(sim, sel, At(sim, 12, 20), true, out _));
        Assert.Equal(120, Soonest(sim, sel, sim.World.TickNumber));
        Cooldown(sim, far, 0); // ready again from this tick on
        Assert.Equal(far.Index, Pick(sim, sel, At(sim, 12, 20), false, out _));
    }

    [Fact]
    public void ABusyCaster_IsPassedOverForAFreeOne_RecastsOnlyWhenAlone_NeverQueued()
    {
        Simulation sim = NoFights();
        EntityHandle near = Place(sim, 0, Mage, At(sim, 10, 20)), far = Place(sim, 0, Mage, At(sim, 30, 20));
        sim.Enqueue(Command.UseAbility(0, near, 0, At(sim, 14, 20)));
        TickOf(sim, near, resolved: false);
        Assert.Equal(UnitState.Casting, sim.World.Units.State[near.Index]);
        var both = new[] { near, far };
        Assert.Equal(far.Index, Pick(sim, both, At(sim, 11, 20), false, out _));
        Assert.Equal(far.Index, Pick(sim, both, At(sim, 11, 20), true, out _));
        var alone = new[] { near };
        Assert.Equal(near.Index, Pick(sim, alone, At(sim, 11, 20), false, out _));
        Assert.Equal(-1, Pick(sim, alone, At(sim, 11, 20), true, out _));
        // Busy is not cooldown: the button stays live.
        Assert.Equal(0, Soonest(sim, alone, sim.World.TickNumber));
    }

    [Fact]
    public void NoCaster_DeadAndStaleHandles_AreNeverPicked()
    {
        Simulation sim = NoFights();
        EntityHandle soldier = Place(sim, 0, HeavyInfantry, At(sim, 10, 20));
        EntityHandle mage = Place(sim, 0, Mage, At(sim, 12, 20));
        var stale = new EntityHandle(mage.Index, mage.Generation + 1);
        Assert.Equal(-1, Pick(sim, new[] { soldier }, At(sim, 12, 20), false, out _));
        Assert.Equal(-1, Soonest(sim, new[] { soldier }, sim.World.TickNumber));
        Assert.Equal(-1, Pick(sim, new[] { soldier, stale }, At(sim, 12, 20), false, out _));
        Assert.Equal(-1, Pick(sim, new[] { new EntityHandle(9999, 1) }, At(sim, 12, 20), false, out _));
        Assert.Equal(mage.Index, Pick(sim, new[] { soldier, mage }, At(sim, 12, 20), false, out _));
        Assert.Equal(-1, AbilityCaster.IndexOf(sim.World.Data.Units[HeavyInfantry], TelasFire));
        Assert.Equal(0, AbilityCaster.IndexOf(sim.World.Data.Units[Mage], TelasFire));
    }

    [Fact]
    public void Progress_RunsFromZeroToOneOverTheCast()
    {
        Assert.Equal(0f, AbilityCaster.Progress(16, 16));
        Assert.Equal(0.5f, AbilityCaster.Progress(8, 16));
        Assert.Equal(1f, AbilityCaster.Progress(0, 16));
        Assert.Equal(1f, AbilityCaster.Progress(5, 0));
        Assert.Equal(0f, AbilityCaster.Progress(20, 16));
    }

    [Fact]
    public void Reads_AllocateNothing()
    {
        Simulation sim = NoFights();
        EntityHandle a = Place(sim, 0, Mage, At(sim, 10, 20)), b = Place(sim, 0, Mage, At(sim, 30, 20));
        var sel = new[] { a, b };
        Pick(sim, sel, At(sim, 12, 20), false, out _);
        Soonest(sim, sel, 0);
        long before = GC.GetAllocatedBytesForCurrentThread();
        int sum = 0;
        for (int i = 0; i < 1000; i++)
        {
            sum += Pick(sim, sel, At(sim, 12 + i % 5, 20), (i & 1) == 0, out _);
            sum += Soonest(sim, sel, i);
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(sum >= 0);
    }
}
