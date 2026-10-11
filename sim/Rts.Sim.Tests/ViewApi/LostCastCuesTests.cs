using System.Numerics;
using Rts.Sim.Abilities;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;
using static Rts.Sim.Tests.AbilityScenes;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>
/// M5-V1: the lost-cast cue pool (<see cref="LostCastCues"/>): one cue per own <see cref="AbilityEvent.Dropped"/> event, none
/// for a start, a resolve, an enemy's drop or a cast the player cancelled with a new order; collected once per tick; a capped
/// ring that keeps a cue until a frame drew it; 0 bytes; reading it leaves the state hash alone.
/// </summary>
[Collection(SerialCollection.Name)]
public class LostCastCuesTests
{
    private readonly ITestOutputHelper _out;

    public LostCastCuesTests(ITestOutputHelper output) => _out = output;

    private static int TelasFire => TestSim.Data.FindAbility("telas_fire");

    /// <summary>A Cadre Mage of player 0 ringed by eight holding enemy Raiders (bodies touching), so any walk-in is stuck where it stands.</summary>
    private static EntityHandle RingedMage(Simulation sim, Vector2 at)
    {
        EntityHandle mage = Place(sim, 0, Mage, at);
        for (int k = 0; k < 8; k++)
        {
            float a = k * MathF.Tau / 8f;
            EntityHandle wall = Place(sim, 1, Raider, at + new Vector2(MathF.Cos(a), MathF.Sin(a)));
            sim.World.Units.Hold[wall.Index] = true;
        }
        return mage;
    }

    private static int Drops(Simulation sim, EntityHandle caster)
    {
        int n = 0;
        foreach (AbilityEvent e in sim.World.AbilityEvents) if (e.Caster == caster && e.Dropped) n++;
        return n;
    }

    [Fact]
    public void AWalkInRingedByEnemies_DropsAfterThreeSeconds_OneCueForItsOwner_NoneForTheOtherPlayer()
    {
        Simulation sim = NoFights();
        EntityHandle mage = RingedMage(sim, At(sim, 10, 20));
        Vector2 point = At(sim, 40, 20); // 60 m off: out of Telas Fire's 16 m range, so it walks
        var own = new LostCastCues();
        var enemy = new LostCastCues();
        sim.Enqueue(Command.UseAbility(0, mage, 0, point));
        int dropTick = -1, drops = 0;
        for (int t = 0; t < 200; t++)
        {
            sim.Tick();
            int added = own.Collect(sim.World.AbilityEvents, sim.World.TickNumber, 0);
            Assert.Equal(0, own.Collect(sim.World.AbilityEvents, sim.World.TickNumber, 0)); // a second frame of the same tick
            Assert.Equal(0, enemy.Collect(sim.World.AbilityEvents, sim.World.TickNumber, 1));
            int d = Drops(sim, mage);
            Assert.Equal(d, added);
            if (d > 0) dropTick = t;
            drops += d;
        }
        _out.WriteLine($"dropped on tick {dropTick} of the run");
        Assert.Equal(1, drops);
        Assert.InRange(dropTick, 59, 80); // the walk-in's 3 s patience (60 stuck ticks), not a Move's 1 s
        Assert.Equal(1, own.Added);
        Assert.Equal(1, own.Count);
        Assert.Equal(0, enemy.Added);
        int slot = Array.IndexOf(own.Active.ToArray(), true);
        Assert.Equal(mage, own.Caster[slot]);
        Assert.Equal(point, own.Point[slot]);
        Assert.Equal(TelasFire, own.Ability[slot]);
        Assert.True(sim.World.Units.CastAbility[mage.Index] < 0);
    }

    [Fact]
    public void ACastCancelledByANewOrder_OrResolved_AddsNoCue()
    {
        Simulation sim = NoFights();
        EntityHandle ringed = RingedMage(sim, At(sim, 10, 20));
        EntityHandle free = Place(sim, 0, Mage, At(sim, 10, 34));
        var cues = new LostCastCues();
        sim.Enqueue(Command.UseAbility(0, ringed, 0, At(sim, 40, 20)));
        sim.Enqueue(Command.UseAbility(0, free, 0, At(sim, 14, 34))); // in range: casts and resolves
        int resolves = 0;
        for (int t = 0; t < 200; t++)
        {
            // 1 s into the stuck walk-in (before its 3 s patience runs out), the player gives it a new order.
            if (t == 20) sim.Enqueue(Command.Stop(0, ringed));
            sim.Tick();
            cues.Collect(sim.World.AbilityEvents, sim.World.TickNumber, 0);
            Assert.Equal(0, Drops(sim, ringed));
            if (Had(sim, free, resolved: true)) resolves++;
        }
        Assert.Equal(1, resolves);
        Assert.True(sim.World.Units.CastAbility[ringed.Index] < 0);
        Assert.Equal(0, cues.Added);
    }

    [Fact]
    public void ACue_AgesOverHalfASecond_ExpiresOnlyOnceDrawn_AndTheRingReplacesTheOldest()
    {
        var cues = new LostCastCues(2);
        var removed = new int[2];
        int slot = cues.Add(new EntityHandle(3, 1), 0, Vector2.Zero, 300);
        Assert.Equal(0, cues.Expire(400, removed)); // never drawn: kept, so every drop shows once
        cues.MarkDrawn(slot, 400);
        Assert.Equal(0f, cues.Age(slot, 399, 0f));
        Assert.Equal(0.5f, cues.Age(slot, 404, 0f));
        Assert.Equal(0, cues.Expire(408, removed));
        Assert.Equal(1, cues.Expire(409, removed));
        Assert.Equal(slot, removed[0]);
        Assert.Equal(0, cues.Count);
        cues.Add(new EntityHandle(1, 1), 0, Vector2.Zero, 500);
        cues.Add(new EntityHandle(2, 1), 0, Vector2.Zero, 500);
        int third = cues.Add(new EntityHandle(4, 1), 0, Vector2.Zero, 501);
        Assert.Equal(2, cues.Count);
        Assert.Equal(1, cues.Replaced);
        Assert.Equal(new EntityHandle(4, 1), cues.Caster[third]);
        Assert.Throws<ArgumentOutOfRangeException>(() => new LostCastCues(0));
    }

    [Fact]
    public void CollectDrawAndExpire_AllocateNothing()
    {
        var cues = new LostCastCues();
        var removed = new int[cues.Capacity];
        var events = new AbilityEvent[8];
        for (int k = 0; k < events.Length; k++)
            events[k] = new AbilityEvent(new EntityHandle(k, 1), k % 2, 0, new Vector2(k, k), Resolved: false, Dropped: k % 3 != 0);
        for (long tick = 1; tick < 50; tick++) Round(cues, events, removed, tick); // warm-up (the JIT)
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (long tick = 50; tick < 1050; tick++) Round(cues, events, removed, tick);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, bytes);
        Assert.True(cues.Added > 1000);
    }

    private static void Round(LostCastCues cues, AbilityEvent[] events, int[] removed, long tick)
    {
        cues.Collect(events, tick, 0);
        for (int i = 0; i < cues.Capacity; i++)
            if (cues.Active[i])
            {
                cues.MarkDrawn(i, tick);
                cues.Age(i, tick, 0.5f);
            }
        cues.Expire(tick, removed);
    }

    /// <summary>Collecting every tick (the view's read) leaves the state hash equal to a bare twin's, 2 seeds x 300 ticks with drops in them.</summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(6UL)]
    public void CollectingEveryTick_LeavesTheStateHashAlone(ulong seed)
    {
        ulong[] Run(bool read)
        {
            Simulation sim = NoFights(seed: seed);
            EntityHandle a = RingedMage(sim, At(sim, 10, 20));
            EntityHandle b = RingedMage(sim, At(sim, 30, 20));
            var cues = new LostCastCues();
            var hashes = new ulong[300];
            for (int t = 0; t < 300; t++)
            {
                if (t % 100 == 0)
                {
                    sim.Enqueue(Command.UseAbility(0, a, 0, At(sim, 40, 40)));
                    sim.Enqueue(Command.UseAbility(0, b, 0, At(sim, 2, 40)));
                }
                sim.Tick();
                if (read) cues.Collect(sim.World.AbilityEvents, sim.World.TickNumber, 0);
                hashes[t] = sim.StateHash();
            }
            if (read) Assert.True(cues.Added >= 4, $"only {cues.Added} drops collected");
            return hashes;
        }
        Assert.Equal(Run(false), Run(true));
    }
}
