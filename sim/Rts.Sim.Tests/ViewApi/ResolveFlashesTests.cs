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
        int slot = flashes.Add(0, 0, Vector2.Zero, 300);
        Assert.Equal(0, flashes.Expire(400, removed)); // never drawn: kept
        flashes.MarkDrawn(slot, 300);
        Assert.Equal(0f, flashes.Age(slot, 300, 0f));
        Assert.Equal(0.55f, flashes.Age(slot, 305, 0.5f), 5);
        Assert.Equal(0, flashes.Expire(300 + ResolveFlashes.LifetimeTicks - 1, removed));
        Assert.Equal(1, flashes.Expire(300 + ResolveFlashes.LifetimeTicks, removed));
        Assert.Equal(slot, removed[0]);
        Assert.Equal(0, flashes.Count);
        Assert.Equal(10, ResolveFlashes.LifetimeTicks); // 0.5 s at 20 Hz
    }

    /// <summary>BUG-0371 (c): a flash first drawn several ticks after its resolve (a frame covering several ticks) starts its age one tick before that frame and runs on from there: no jump to its real age on the next frame, and its full fade before it expires.</summary>
    [Fact]
    public void ALateFirstDraw_StartsItsAgeAtThatFrame_NoJumpOnTheNext()
    {
        var flashes = new ResolveFlashes(4);
        var removed = new int[4];
        int slot = flashes.Add(0, 0, new Vector2(5f, 5f), 100);
        // Collected at tick 100, first frame at tick 103 (alpha 0.5).
        float first = flashes.Age(slot, 103, 0.5f);
        flashes.MarkDrawn(slot, 103);
        Assert.Equal(first, flashes.Age(slot, 103, 0.5f));
        Assert.Equal(0.15f, first, 5);
        float next = flashes.Age(slot, 104, 0.5f);
        Assert.Equal(0.25f, next, 5); // one tick on, not (104 - 100 + 0.5) / 10 = 0.45
        Assert.Equal(1f, flashes.Age(slot, 112, 0f));
        Assert.Equal(0, flashes.Expire(111, removed));
        Assert.Equal(1, flashes.Expire(112, removed));
        // A flash long overdue (never drawn for 100 ticks) still shows its fade from the start when a frame reaches it.
        slot = flashes.Add(0, 0, Vector2.Zero, 100);
        flashes.MarkDrawn(slot, 200);
        Assert.InRange(flashes.Age(slot, 200, 0f), 0.09f, 0.11f);
        Assert.Equal(0, flashes.Expire(200, removed));
        Assert.Equal(1, flashes.Expire(209, removed));
        Assert.Equal(100, flashes.StartTick[slot]); // the resolve's tick is kept for readers
    }

    /// <summary>BUG-0371 (b): the first frame's fog answer is kept for the flash's life: hidden then, never drawn later when its cell comes into sight mid-fade; shown then, still drawn (the fade finishes) if the fog closes. Either way it expires on time.</summary>
    [Fact]
    public void TheFirstFramesFogAnswer_IsKeptForTheFlashsLife()
    {
        var flashes = new ResolveFlashes(4);
        var removed = new int[4];
        int hidden = flashes.Add(0, 1, new Vector2(60f, 60f), 50);
        int seen = flashes.Add(0, 0, new Vector2(10f, 10f), 50);
        Assert.False(flashes.Drawn[hidden]);
        flashes.MarkDrawn(hidden, 50, shown: false);
        flashes.MarkDrawn(seen, 50, shown: true);
        for (long t = 51; t < 60; t++)
        {
            flashes.MarkDrawn(hidden, t, shown: true); // the cell came into sight: no change
            flashes.MarkDrawn(seen, t, shown: false); // the fog closed: no change
            Assert.False(flashes.Shown[hidden]);
            Assert.True(flashes.Shown[seen]);
        }
        Assert.True(flashes.Drawn[hidden]);
        Assert.Equal(2, flashes.Expire(60, removed));
        // A slot reused by a new flash is undecided again.
        int again = flashes.Add(0, 0, Vector2.One, 70);
        Assert.False(flashes.Drawn[again] || flashes.Shown[again]);
        flashes.MarkDrawn(again, 70);
        Assert.True(flashes.Shown[again]);
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
                if (flashes.Active[i]) { sum += flashes.Age(i, tick, 0.5f); flashes.MarkDrawn(i, tick, (i & 1) == 0); }
            flashes.Expire(tick, removed);
        }, _out);
        Assert.True(flashes.Count <= ResolveFlashes.DefaultCapacity);
        _out.WriteLine($"storms: {flashes.Added} added, {flashes.Replaced} replaced, {flashes.Expired} expired; 0 bytes (runs {runs}, sum {sum})");
    }
}
