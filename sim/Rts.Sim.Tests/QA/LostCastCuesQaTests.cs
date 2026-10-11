using System.Numerics;
using Rts.Sim.Abilities;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;
using static Rts.Sim.Tests.AbilityScenes;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M5-V1: the lost-cast cue pool (<see cref="LostCastCues"/>) against a brute-force oracle under seeded random cast /
/// cancel / move / stop streams (both players, ringed and free mages), with frames lagging the ticks; many drops in one
/// tick; a cancel never cued; and the hash twin across 6 seeds (collect, draw, age and expire every tick vs a bare run).
/// </summary>
[Collection(SerialCollection.Name)]
public class LostCastCuesQaTests
{
    private readonly ITestOutputHelper _out;

    public LostCastCuesQaTests(ITestOutputHelper output) => _out = output;

    /// <summary>A mage of <paramref name="owner"/> at <paramref name="at"/>, ringed by eight holding enemy Raiders (bodies touching), so any walk-in is stuck.</summary>
    private static EntityHandle RingedMage(Simulation sim, int owner, Vector2 at)
    {
        EntityHandle mage = Place(sim, owner, Mage, at);
        for (int k = 0; k < 8; k++)
        {
            float a = k * MathF.Tau / 8f;
            EntityHandle wall = Place(sim, 1 - owner, Raider, at + new Vector2(MathF.Cos(a), MathF.Sin(a)));
            sim.World.Units.Hold[wall.Index] = true;
        }
        return mage;
    }

    private static int OracleDrops(Simulation sim, int player)
    {
        int n = 0;
        foreach (AbilityEvent e in sim.World.AbilityEvents) if (e.Dropped && !e.Resolved && e.Owner == player) n++;
        return n;
    }

    /// <summary>Stages 3 ringed mages per player plus 3 free ones per player; returns every mage.</summary>
    private static EntityHandle[] Stage(Simulation sim)
    {
        var mages = new List<EntityHandle>();
        for (int k = 0; k < 3; k++)
        {
            mages.Add(RingedMage(sim, 0, At(sim, 6 + 6 * k, 8)));
            mages.Add(RingedMage(sim, 1, At(sim, 6 + 6 * k, 40)));
            mages.Add(Place(sim, 0, Mage, At(sim, 6 + 6 * k, 20)));
            mages.Add(Place(sim, 1, Mage, At(sim, 6 + 6 * k, 28)));
        }
        return mages.ToArray();
    }

    // One random order for a random mage: a far cast (walks: drops if ringed), a near cast (resolves), a queued cast, a Stop,
    // a Move, or the same far cast spammed.
    private static void RandomOrder(Simulation sim, Random rng, EntityHandle[] mages)
    {
        int pick = rng.Next(mages.Length / 4);
        EntityHandle m = mages[4 * pick + (rng.Next(10) < 7 ? rng.Next(2) : 2 + rng.Next(2))]; // 70 %: a ringed mage (Stage order: ringed, ringed, free, free)
        UnitStore u = sim.World.Units;
        if (!u.Alive[m.Index] || u.Generation[m.Index] != m.Generation) return;
        int owner = u.Owner[m.Index];
        Vector2 p = u.Position[m.Index];
        Vector2 far = At(sim, 2 + rng.Next(44), 2 + rng.Next(44));
        switch (rng.Next(10))
        {
            case 0: case 1: case 2: case 3: sim.Enqueue(Command.UseAbility(owner, m, 0, far)); break;
            case 4: sim.Enqueue(Command.UseAbility(owner, m, 0, p + new Vector2(3f, 0f))); break;
            case 5: sim.Enqueue(Command.UseAbility(owner, m, 0, far, queued: true)); break;
            case 6: sim.Enqueue(Command.Stop(owner, m)); break;
            case 7: sim.Enqueue(Command.Move(owner, m, far)); break;
            default:
                for (int k = 0; k < 4; k++) sim.Enqueue(Command.UseAbility(owner, m, 0, far));
                break;
        }
    }

    /// <summary>
    /// 8 seeds x 1,500 ticks of random orders: each tick, both players' pools add exactly the oracle's own Dropped count (and
    /// a second collect of the tick adds none); frames lag (drawn every 1-4 ticks): no cue expires undrawn, Count equals the
    /// active slots, cues added equal the oracle's total, and a pool never exceeds its capacity.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void RandomOrders_CuesMatchTheOracle_EveryTick_WithLaggingFrames(int seed)
    {
        Simulation sim = NoFights(units: 128, seed: (ulong)seed);
        EntityHandle[] mages = Stage(sim);
        var rng = new Random(seed * 7919);
        var pools = new[] { new LostCastCues(), new LostCastCues() };
        var removed = new int[LostCastCues.DefaultCapacity];
        long[] oracle = new long[2];
        int nextFrame = 1, frames = 0;
        long drawnSeen = 0;
        for (int t = 0; t < 1500; t++)
        {
            int orders = rng.Next(12) == 0 ? 1 : 0;
            for (int k = 0; k < orders; k++) RandomOrder(sim, rng, mages);
            sim.Tick();
            for (int p = 0; p < 2; p++)
            {
                int want = OracleDrops(sim, p);
                oracle[p] += want;
                int added = pools[p].Collect(sim.World.AbilityEvents, sim.World.TickNumber, p);
                Assert.Equal(want, added);
                Assert.Equal(0, pools[p].Collect(sim.World.AbilityEvents, sim.World.TickNumber, p));
            }
            if (t + 1 < nextFrame) continue;
            nextFrame = t + 1 + 1 + rng.Next(4);
            frames++;
            for (int p = 0; p < 2; p++)
            {
                LostCastCues c = pools[p];
                // A frame: expire first (as the view does), then draw every live cue.
                int gone = c.Expire(sim.World.TickNumber, removed);
                for (int g = 0; g < gone; g++) Assert.True(!c.Active[removed[g]]);
                int active = 0;
                for (int i = 0; i < c.Capacity; i++)
                {
                    if (!c.Active[i]) continue;
                    active++;
                    if (!c.Drawn[i]) drawnSeen++;
                    c.MarkDrawn(i, sim.World.TickNumber);
                    float age = c.Age(i, sim.World.TickNumber, 0.5f);
                    Assert.InRange(age, 0f, 1f);
                    Assert.False(float.IsNaN(age));
                }
                Assert.Equal(active, c.Count);
                Assert.InRange(c.Count, 0, c.Capacity);
            }
        }
        for (int p = 0; p < 2; p++) Assert.Equal(oracle[p], pools[p].Added);
        // Every cue added was reached by a frame, unless the ring replaced it first.
        long addedAll = pools[0].Added + pools[1].Added;
        Assert.True(drawnSeen <= addedAll);
        Assert.True(addedAll <= drawnSeen + pools[0].Replaced + pools[1].Replaced + Undrawn(pools[0]) + Undrawn(pools[1]),
            $"{addedAll} cues added but only {drawnSeen} reached a frame");
        _out.WriteLine($"seed {seed}: drops p0 {oracle[0]} p1 {oracle[1]}, {frames} frames, replaced {pools[0].Replaced}/{pools[1].Replaced}");
        Assert.True(oracle[0] + oracle[1] > 0, "the fuzz raised no drop: it tests nothing");
    }

    private static int Undrawn(LostCastCues c)
    {
        int n = 0;
        for (int i = 0; i < c.Capacity; i++) if (c.Active[i] && !c.Drawn[i]) n++;
        return n;
    }

    /// <summary>
    /// Twelve ringed mages of player 0 given far casts on one tick all drop on one tick: twelve cues in that collect (one per
    /// caster, each its own), none for player 1; then a Stop to all twelve before the 3 s raises no drop and no cue.
    /// </summary>
    [Fact]
    public void TwelveRingedMages_DropTogether_TwelveCues_AndStoppedOnesNone()
    {
        Simulation sim = NoFights(units: 160);
        var mages = new EntityHandle[12];
        for (int k = 0; k < 12; k++) mages[k] = RingedMage(sim, 0, At(sim, 5 + 6 * (k % 6), 8 + 8 * (k / 6)));
        var own = new LostCastCues();
        var other = new LostCastCues();
        foreach (EntityHandle m in mages) sim.Enqueue(Command.UseAbility(0, m, 0, At(sim, 44, 44)));
        int maxInOneTick = 0, total = 0;
        for (int t = 0; t < 150; t++)
        {
            sim.Tick();
            int n = own.Collect(sim.World.AbilityEvents, sim.World.TickNumber, 0);
            Assert.Equal(0, other.Collect(sim.World.AbilityEvents, sim.World.TickNumber, 1));
            maxInOneTick = Math.Max(maxInOneTick, n);
            total += n;
        }
        Assert.Equal(12, total);
        Assert.InRange(maxInOneTick, 2, 12); // the rings differ slightly, so they drop over a few ticks
        var casters = new HashSet<EntityHandle>();
        for (int i = 0; i < own.Capacity; i++) if (own.Active[i]) casters.Add(own.Caster[i]);
        Assert.Equal(12, casters.Count);
        foreach (EntityHandle m in mages) Assert.Contains(m, casters);

        // The same walk-ins cancelled by the player's Stop 1 s in.
        long before = own.Added;
        foreach (EntityHandle m in mages) sim.Enqueue(Command.UseAbility(0, m, 0, At(sim, 44, 44)));
        for (int t = 0; t < 200; t++)
        {
            if (t == 20) foreach (EntityHandle m in mages) sim.Enqueue(Command.Stop(0, m));
            sim.Tick();
            own.Collect(sim.World.AbilityEvents, sim.World.TickNumber, 0);
        }
        Assert.Equal(before, own.Added);
        foreach (EntityHandle m in mages) Assert.True(sim.World.Units.CastAbility[m.Index] < 0);
    }

    /// <summary>
    /// The cancel paths a player has: Stop, Move, Hold, a new non-queued cast that resolves: none raises a drop or a cue for
    /// a stuck walk-in (a fresh ringed mage per path).
    /// </summary>
    [Theory]
    [InlineData("stop")]
    [InlineData("move")]
    [InlineData("hold")]
    [InlineData("recast-near")]
    public void EveryPlayerCancel_OfAStuckWalkIn_AddsNoCue(string how)
    {
        Simulation sim = NoFights();
        EntityHandle mage = RingedMage(sim, 0, At(sim, 10, 20));
        var cues = new LostCastCues();
        sim.Enqueue(Command.UseAbility(0, mage, 0, At(sim, 40, 20)));
        for (int t = 0; t < 200; t++)
        {
            if (t == 30)
            {
                Vector2 p = sim.World.Units.Position[mage.Index];
                sim.Enqueue(how switch
                {
                    "stop" => Command.Stop(0, mage),
                    "move" => Command.Move(0, mage, At(sim, 40, 40)),
                    "hold" => Command.HoldPosition(0, mage),
                    _ => Command.UseAbility(0, mage, 0, p + new Vector2(3f, 0f)),
                });
            }
            sim.Tick();
            cues.Collect(sim.World.AbilityEvents, sim.World.TickNumber, 0);
        }
        Assert.Equal(0, cues.Added);
    }

    /// <summary>The view's whole read (collect, draw, age, expire every tick, for both players) leaves every tick's state hash equal to a bare run's under the random order stream, 6 seeds x 600 ticks.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(11)]
    [InlineData(23)]
    [InlineData(42)]
    [InlineData(99)]
    public void TheViewsRead_UnderRandomOrders_LeavesTheHashAlone(int seed)
    {
        (ulong[] Hashes, long Drops) Run(bool read)
        {
            Simulation sim = NoFights(units: 128, seed: (ulong)seed);
            EntityHandle[] mages = Stage(sim);
            var rng = new Random(seed);
            var pools = new[] { new LostCastCues(), new LostCastCues() };
            var removed = new int[LostCastCues.DefaultCapacity];
            var hashes = new ulong[600];
            for (int t = 0; t < 600; t++)
            {
                int orders = rng.Next(12) == 0 ? 1 : 0;
                for (int k = 0; k < orders; k++) RandomOrder(sim, rng, mages);
                sim.Tick();
                if (read)
                    for (int p = 0; p < 2; p++)
                    {
                        LostCastCues c = pools[p];
                        c.Collect(sim.World.AbilityEvents, sim.World.TickNumber, p);
                        c.Expire(sim.World.TickNumber, removed);
                        for (int i = 0; i < c.Capacity; i++)
                            if (c.Active[i])
                            {
                                c.MarkDrawn(i, sim.World.TickNumber);
                                c.Age(i, sim.World.TickNumber, 0.5f);
                            }
                    }
                hashes[t] = sim.StateHash();
            }
            return (hashes, pools[0].Added + pools[1].Added);
        }
        (ulong[] bare, _) = Run(false);
        (ulong[] read, long drops) = Run(true);
        Assert.Equal(bare, read);
        Assert.True(drops > 0, "no drops in the run");
    }
}
