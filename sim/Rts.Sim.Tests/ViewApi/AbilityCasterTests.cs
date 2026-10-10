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
        Pick(sim, sel, point, queued, ReadOnlySpan<EntityHandle>.Empty, out index);

    private static int Pick(Simulation sim, EntityHandle[] sel, Vector2 point, bool queued, ReadOnlySpan<EntityHandle> sent, out int index) =>
        AbilityCaster.PickCaster(sel, U(sim).Alive, U(sim).Generation, U(sim).TypeId, U(sim).Position, U(sim).CastAbility, U(sim).AbilityReadyTick,
            U(sim).QueueCount, U(sim).QueueKind, U(sim).QueueTypeId, sent, sim.World.Data.Units, TelasFire, sim.World.TickNumber, point, queued, out index);

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
    public void ACastWaitingInAWalkingMagesQueue_IsBusy_TheNextQueuedClickGoesToTheOther_BothResolve()
    {
        // BUG-0370: two mages walk under a Move; a Shift-queued cast sits behind it (CastAbility still -1).
        Simulation sim = NoFights();
        EntityHandle a = Place(sim, 0, Mage, At(sim, 10, 20)), b = Place(sim, 0, Mage, At(sim, 10, 26));
        var sel = new[] { a, b };
        sim.Enqueue(Command.Move(0, a, At(sim, 40, 20)));
        sim.Enqueue(Command.Move(0, b, At(sim, 40, 26)));
        sim.Tick();
        sim.Tick(); // the Moves apply in the second Tick(): walking
        Vector2 p1 = At(sim, 30, 21), p2 = At(sim, 30, 22);
        Assert.Equal(a.Index, Pick(sim, sel, p1, true, out _));
        sim.Enqueue(Command.UseAbility(0, a, 0, p1, queued: true));
        sim.Tick();
        sim.Tick(); // stamped for the next tick number: in the queue after the second Tick()
        UnitStore u = U(sim);
        Assert.Equal((-1, 1, UnitState.Moving), (u.CastAbility[a.Index], u.QueueCount[a.Index], u.State[a.Index]));
        Assert.True(AbilityCaster.HasQueuedCast(u.QueueCount, u.QueueKind, u.QueueTypeId, a.Index, 0));
        Assert.False(AbilityCaster.HasQueuedCast(u.QueueCount, u.QueueKind, u.QueueTypeId, b.Index, 0));
        Assert.False(AbilityCaster.HasQueuedCast(u.QueueCount, u.QueueKind, u.QueueTypeId, a.Index, 1)); // another list index
        // a is nearer p2, but its cast is queued: the queued click and a plain one both take b.
        Assert.Equal(b.Index, Pick(sim, sel, p2, true, out int k));
        Assert.Equal(0, k);
        Assert.Equal(b.Index, Pick(sim, sel, p2, false, out _));
        // Alone, a queued click has no free caster; a plain click recasts (it replaces the queue).
        Assert.Equal(-1, Pick(sim, new[] { a }, p2, true, out _));
        Assert.Equal(a.Index, Pick(sim, new[] { a }, p2, false, out _));
        sim.Enqueue(Command.UseAbility(0, b, 0, p2, queued: true));
        int resolves = 0;
        for (int t = 0; t < 600; t++)
        {
            sim.Tick();
            resolves += (Had(sim, a, resolved: true) ? 1 : 0) + (Had(sim, b, resolved: true) ? 1 : 0);
        }
        Assert.Equal(2, resolves);
    }

    [Fact]
    public void TwoClicksInOneTick_TheSentMemoryPicksTwoMages_UntilTheCommandsApply()
    {
        Simulation sim = NoFights();
        EntityHandle a = Place(sim, 0, Mage, At(sim, 10, 20)), b = Place(sim, 0, Mage, At(sim, 30, 20));
        var sel = new[] { a, b };
        var sent = new SentCasts(4);
        Vector2 p = At(sim, 12, 20);
        int Click(bool queued)
        {
            int tick = sim.World.TickNumber;
            int c = Pick(sim, sel, p, queued, sent.For(tick, TelasFire), out int k);
            if (c < 0) return c;
            var h = c == a.Index ? a : b;
            sim.Enqueue(Command.UseAbility(0, h, k, p, queued));
            sent.Note(tick + 1, TelasFire, h);
            return c;
        }
        Assert.Equal(a.Index, Click(true));
        // Before the command applies nothing in the store shows it: without the memory the same mage is picked again.
        Assert.Equal(a.Index, Pick(sim, sel, p, true, out _));
        Assert.Equal(b.Index, Click(true));
        Assert.Equal(-1, Click(true)); // a third queued click with two mages: refused, nothing sent
        Assert.Equal(a.Index, Pick(sim, sel, p, false, sent.For(sim.World.TickNumber, TelasFire), out _)); // a plain one: the nearer recasts
        sim.Tick(); // the commands are stamped for the tick after this one: still pending, still remembered
        Assert.Equal(2, sent.For(sim.World.TickNumber, TelasFire).Length);
        Assert.Equal(0, sent.For(sim.World.TickNumber, TelasFire + 1).Length); // per ability
        Assert.Equal(-1, Pick(sim, sel, p, true, sent.For(sim.World.TickNumber, TelasFire), out _));
        sim.Tick(); // applied: the store says busy (a casts at once, b walks in), the memory lets them go
        Assert.Equal(0, sent.For(sim.World.TickNumber, TelasFire).Length);
        Assert.Equal(0, sent.Count);
        Assert.Equal((0, 0), (U(sim).CastAbility[a.Index], U(sim).CastAbility[b.Index]));
        Assert.Equal(-1, Pick(sim, sel, p, true, out _));
        int resolves = 0;
        for (int t = 0; t < 600; t++)
        {
            sim.Tick();
            resolves += (Had(sim, a, resolved: true) ? 1 : 0) + (Had(sim, b, resolved: true) ? 1 : 0);
        }
        Assert.Equal(2, resolves);
    }

    [Fact]
    public void SentCasts_FullDropsTheOldest_ClearForgets_ZeroCapacityThrows()
    {
        var sent = new SentCasts(3);
        for (int i = 0; i < 5; i++) sent.Note(10, 7, new EntityHandle(i, 1));
        ReadOnlySpan<EntityHandle> kept = sent.For(10, 7);
        Assert.Equal(3, kept.Length);
        Assert.Equal(new EntityHandle(2, 1), kept[0]);
        Assert.Equal(new EntityHandle(4, 1), kept[2]);
        Assert.Equal(3, sent.For(9, 7).Length); // an earlier tick (never asked in play): nothing applied yet
        sent.Clear();
        Assert.Equal(0, sent.For(10, 7).Length);
        sent.Note(10, 7, new EntityHandle(1, 1));
        sent.Note(12, 7, new EntityHandle(2, 1));
        Assert.Equal(1, sent.For(11, 7).Length); // the first applied in tick 10
        Assert.Equal(new EntityHandle(2, 1), sent.For(11, 7)[0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => new SentCasts(0));
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
        // A queued cast behind a Move, so the queue scan runs too.
        sim.Enqueue(Command.Move(0, a, At(sim, 40, 20)));
        sim.Enqueue(Command.UseAbility(0, a, 0, At(sim, 30, 20), queued: true));
        sim.Tick();
        var sent = new SentCasts();
        Pick(sim, sel, At(sim, 12, 20), false, out _);
        Soonest(sim, sel, 0);
        long before = GC.GetAllocatedBytesForCurrentThread();
        int sum = 0;
        for (int i = 0; i < 1000; i++)
        {
            sent.Note(i / 3 + 1, TelasFire, (i & 1) == 0 ? a : b);
            sum += Pick(sim, sel, At(sim, 12 + i % 5, 20), (i & 1) == 0, sent.For(i / 3, TelasFire), out _);
            sum += Soonest(sim, sel, i);
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(sum >= -4000);
    }
}
