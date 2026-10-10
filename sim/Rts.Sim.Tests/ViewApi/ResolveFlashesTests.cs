using System.Numerics;
using Rts.Sim.Abilities;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;
using static Rts.Sim.Tests.AbilityScenes;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>M4-V6b: the resolve flash pool (<see cref="ResolveFlashes"/>): one flash per resolved cast, none for a start, a capped ring.</summary>
[Collection(SerialCollection.Name)]
public class ResolveFlashesTests
{
    private readonly ITestOutputHelper _out;

    public ResolveFlashesTests(ITestOutputHelper output) => _out = output;

    private static int TelasFire => TestSim.Data.FindAbility("telas_fire");

    [Fact]
    public void ARealCast_OneFlashAtItsPointOnTheResolve_NoneForTheStart_CollectedOncePerTick()
    {
        Simulation sim = NoFights();
        EntityHandle mage = Place(sim, 0, Mage, At(sim, 10, 20));
        Place(sim, 1, HeavyInfantry, At(sim, 16, 20));
        Vector2 point = At(sim, 16, 20);
        var flashes = new ResolveFlashes();
        sim.Enqueue(Command.UseAbility(0, mage, 0, point));
        int starts = 0, resolves = 0;
        for (int t = 0; t < 40; t++)
        {
            sim.Tick();
            int added = flashes.Collect(sim.World.AbilityEvents, sim.World.TickNumber);
            Assert.Equal(0, flashes.Collect(sim.World.AbilityEvents, sim.World.TickNumber)); // a second frame of the same tick
            if (Had(sim, mage, resolved: false)) { starts++; Assert.Equal(0, added); }
            if (Had(sim, mage, resolved: true))
            {
                resolves++;
                Assert.Equal(1, added);
            }
        }
        Assert.Equal(1, starts);
        Assert.Equal(1, resolves);
        Assert.Equal(1, flashes.Count);
        int slot = Array.IndexOf(flashes.Active.ToArray(), true);
        Assert.Equal(point, flashes.Point[slot]);
        Assert.Equal(TelasFire, flashes.Ability[slot]);
        Assert.Equal(0, flashes.Owner[slot]);
    }

    [Fact]
    public void AFlash_AgesOverHalfASecond_ExpiresOnlyOnceDrawn()
    {
        var flashes = new ResolveFlashes(4);
        var removed = new int[4];
        int slot = flashes.Add(0, 0, new Vector2(5f, 5f), 100);
        Assert.Equal(0, flashes.Expire(200, removed)); // never drawn: kept, and first drawn early in its life
        Assert.InRange(flashes.Age(slot, 200, 0f), 0.09f, 0.11f);
        flashes.MarkDrawn(slot);
        Assert.Equal(1f, flashes.Age(slot, 200, 0f));
        Assert.Equal(1, flashes.Expire(200, removed));
        Assert.Equal(slot, removed[0]);
        Assert.Equal(0, flashes.Count);
        slot = flashes.Add(0, 0, Vector2.Zero, 300);
        flashes.MarkDrawn(slot);
        Assert.Equal(0f, flashes.Age(slot, 300, 0f));
        Assert.Equal(0.55f, flashes.Age(slot, 305, 0.5f), 5);
        Assert.Equal(0, flashes.Expire(300 + ResolveFlashes.LifetimeTicks - 1, removed));
        Assert.Equal(1, flashes.Expire(300 + ResolveFlashes.LifetimeTicks, removed));
        Assert.Equal(10, ResolveFlashes.LifetimeTicks); // 0.5 s at 20 Hz
    }

    [Fact]
    public void TwoHundredResolvesInOneTick_FitThePool_ASecondStormReplacesTheOldest_NoGrowth_ZeroBytes()
    {
        var flashes = new ResolveFlashes();
        var events = new AbilityEvent[200];
        for (int i = 0; i < events.Length; i++) events[i] = new AbilityEvent(new EntityHandle(i, 1), 0, 0, new Vector2(i, 0f), true);
        Assert.Equal(200, flashes.Collect(events, 1));
        Assert.Equal(200, flashes.Count);
        Assert.Equal(200, flashes.Collect(events, 2));
        Assert.Equal(ResolveFlashes.DefaultCapacity, flashes.Count);
        Assert.Equal(400 - ResolveFlashes.DefaultCapacity, flashes.Replaced);
        var removed = new int[ResolveFlashes.DefaultCapacity];
        long tick = 3;
        float sum = 0f;
        int runs = AllocationProbe.AssertZero(() =>
        {
            tick++;
            flashes.Collect(events, tick);
            for (int i = 0; i < flashes.Capacity; i++)
                if (flashes.Active[i]) { sum += flashes.Age(i, tick, 0.5f); flashes.MarkDrawn(i); }
            flashes.Expire(tick, removed);
        }, _out);
        Assert.True(flashes.Count <= ResolveFlashes.DefaultCapacity);
        _out.WriteLine($"storms: {flashes.Added} added, {flashes.Replaced} replaced, {flashes.Expired} expired; 0 bytes (runs {runs}, sum {sum})");
    }
}
