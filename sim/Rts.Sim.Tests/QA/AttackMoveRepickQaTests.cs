using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Xunit.Abstractions;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M4-2a re-check (session 2026-10-08-0313, round 2): the BUG-0154 fix makes an unqueued attack-move to any point but
/// the leg's own re-pick by priority at once (<see cref="UnitStore.Repick"/>). These rows attack what that must not break:
/// a player's A-click spam never lands on exactly the same point, so jittered spam must still not throw swings away
/// (BUG-0152's bound); the flag is never left set between ticks (it rides in the hash); twins stay equal; and a later
/// order in the same tick wins over the re-pick.
/// </summary>
[Collection(SerialCollection.Name)]
public class AttackMoveRepickQaTests
{
    private readonly ITestOutputHelper _out;

    public AttackMoveRepickQaTests(ITestOutputHelper output) => _out = output;

    private const int PerSide = 40;

    /// <summary>
    /// A 40 v 40 brawl (Heavy Infantry v Raiders, 5 ranks): player 0's whole army is re-attack-moved every
    /// <paramref name="every"/> ticks toward the enemy block, either to the same point or to a point jittered by 2-3 m
    /// (a human's click spam). Over 400 ticks player 0 must deal at least 90 % of the damage one order deals with spam to the
    /// same point (BUG-0152's bound: a re-pick that keeps the same target keeps the swing). Jittered spam is bounded as the
    /// Producer decided for BUG-0157 (2026-10-08-2144): no single interval under 80 % (one 400-tick brawl swings by about
    /// 10 points either way), and the mean over the intervals at least 90 % (<see cref="BrawlSpam_JitteredAttackMove_MeanOverIntervals1To20_AtLeast90Percent"/>).
    /// </summary>
    [Theory]
    [InlineData(1, false)]
    [InlineData(3, false)]
    [InlineData(3, true)]
    [InlineData(5, true)]
    [InlineData(10, true)]
    public void BrawlSpam_JitteredAttackMove_StillDealsTheDamage(int every, bool jitter)
    {
        int once = BrawlDamage(0, false, out int switchesOnce);
        int spam = BrawlDamage(every, jitter, out int switches);
        _out.WriteLine($"damage dealt in 400 ticks: one order {once} (target switches {switchesOnce}); re-issued every {every} ticks, jitter {jitter}: {spam} (target switches {switches})");
        Assert.True(once > 0, "setup: no damage");
        int percent = jitter ? 8 : 9;
        Assert.True(spam * 10 >= once * percent, $"spam every {every} (jitter {jitter}): {spam} damage vs {once} with one order (bound {percent}0 %)");
    }

    /// <summary>
    /// BUG-0157's bound (Producer decision 2026-10-08-2144): with the kept-chase rule (an attack-move to a new point keeps a
    /// chase whose target its owner still sees, and re-picks), jittered spam over intervals 1-20 ticks at 40 v 40 deals on
    /// average at least 90 % of one order's damage in 400 ticks, and no interval under 80 %.
    /// </summary>
    [Fact]
    public void BrawlSpam_JitteredAttackMove_MeanOverIntervals1To20_AtLeast90Percent()
    {
        int[] intervals = { 1, 2, 3, 4, 5, 6, 7, 8, 10, 12, 15, 20 };
        int once = BrawlDamage(0, false, out _);
        Assert.True(once > 0, "setup: no damage");
        double sum = 0, worst = double.MaxValue;
        var row = new List<string>();
        foreach (int every in intervals)
        {
            double share = 100.0 * BrawlDamage(every, true, out _) / once;
            sum += share;
            worst = Math.Min(worst, share);
            row.Add($"{every}: {share:F0} %");
        }
        double mean = sum / intervals.Length;
        _out.WriteLine($"one order {once}; jittered spam {string.Join(", ", row)}; mean {mean:F1} %, worst {worst:F0} %");
        Assert.True(mean >= 90.0, $"mean {mean:F1} % of one order's damage ({string.Join(", ", row)})");
        Assert.True(worst >= 80.0, $"an interval under 80 %: {string.Join(", ", row)}");
    }

    private static int BrawlDamage(int every, bool jitter, out int switches)
    {
        Simulation sim = Flat(size: 64, units: 2 * PerSide);
        var center = new Vector2(64f, 64f);
        FlatBrawl(sim, PerSide, center, gap: 8f, ranks: 5);
        UnitStore u = sim.World.Units;
        Vector2 goal = center + new Vector2(4f + 2f, 0f); // FlatBrawl's goal for player 0: the far block's center
        int raiderHp = sim.World.Data.Units[Raider].Hp;
        var last = new EntityHandle[u.Capacity];
        switches = 0;
        for (int t = 1; t <= 400; t++)
        {
            if (every > 0 && t % every == 0)
            {
                Vector2 to = goal;
                if (jitter)
                {
                    int k = t / every % 4;
                    to += k switch { 0 => new Vector2(0f, 2.5f), 1 => new Vector2(2f, -2f), 2 => new Vector2(-2.5f, 0f), _ => new Vector2(0f, -3f) };
                }
                for (int i = 0; i < u.Capacity; i++)
                    if (u.Alive[i] && u.Owner[i] == 0) sim.Enqueue(Command.AttackMove(0, new EntityHandle(i, u.Generation[i]), to));
            }
            sim.Tick();
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i] || u.Owner[i] != 0) continue;
                // A switch from one live target to another (not the first pick, not a kill's re-pick).
                if (last[i].Generation != 0 && u.Target[i].Generation != 0 && u.Target[i] != last[i] && IsLiveUnit(u, last[i])) switches++;
                last[i] = u.Target[i];
            }
        }
        int left = 0;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.Owner[i] == 1) left += u.Hp[i];
        return PerSide * raiderHp - left;
    }

    private static bool IsLiveUnit(UnitStore u, EntityHandle h) => h.Generation != 0 && u.IsAlive(h);

    /// <summary>
    /// Attack-move fuzz in a 40 v 40 brawl with both sides re-ordered at random (new points, the same point, the leg's
    /// cell, queued legs, Moves, Stops) for 1,500 ticks: after every tick no unit holds the re-pick flag (it lives within
    /// one tick's phases 1-7), and a twin run is hash-equal every tick.
    /// </summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void RepickFuzz_FlagNeverOutlivesTheTick_TwinsMatch(ulong seed)
    {
        Simulation a = Fuzzed(seed, out var ra);
        Simulation b = Fuzzed(seed, out var rb);
        int orders = 0;
        for (int t = 0; t < 1500; t++)
        {
            orders += Orders(a, ref ra, t);
            Orders(b, ref rb, t);
            a.Tick();
            b.Tick();
            UnitStore u = a.World.Units;
            for (int i = 0; i < u.Capacity; i++)
                Assert.False(u.Repick[i], $"seed {seed} tick {a.World.TickNumber}: unit {i} kept the re-pick flag");
            Assert.Equal(a.StateHash(), b.StateHash());
        }
        _out.WriteLine($"seed {seed}: {orders} orders, {a.World.Kills[0] + a.World.Kills[1]} dead, {a.World.Units.Count} alive");
        Assert.True(a.World.Kills[0] + a.World.Kills[1] > 0, "setup: nobody died");
    }

    private static Simulation Fuzzed(ulong seed, out Determinism.SimRng rng)
    {
        Simulation sim = Flat(size: 64, units: 2 * PerSide, seed: seed);
        FlatBrawl(sim, PerSide, new Vector2(64f, 64f), gap: 8f, ranks: 5);
        rng = new Determinism.SimRng(seed, 911);
        return sim;
    }

    private static int Orders(Simulation sim, ref Determinism.SimRng rng, int t)
    {
        UnitStore u = sim.World.Units;
        int n = 0;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || rng.NextInt(0, 12) != 0) continue;
            var h = new EntityHandle(i, u.Generation[i]);
            int owner = u.Owner[i];
            Vector2 near = u.Position[i] + new Vector2(rng.NextFloat() * 16f - 8f, rng.NextFloat() * 16f - 8f);
            Vector2 p = Vector2.Clamp(near, new Vector2(1f), new Vector2(127f));
            switch (rng.NextInt(0, 8))
            {
                case 0: case 1: sim.Enqueue(Command.AttackMove(owner, h, p)); break;
                case 2: sim.Enqueue(Command.AttackMove(owner, h, u.AnchorPosition[i] == Vector2.Zero ? p : u.Position[i])); break;
                case 3: sim.Enqueue(Command.AttackMove(owner, h, p)); sim.Enqueue(Command.AttackMove(owner, h, p + new Vector2(0.3f, 0f))); break;
                case 4: sim.Enqueue(Command.AttackMove(owner, h, p, queued: true)); break;
                case 5: sim.Enqueue(Command.AttackMove(owner, h, p)); sim.Enqueue(Command.Move(owner, h, p)); break;
                case 6: sim.Enqueue(Command.Stop(owner, h)); break;
                default: sim.Enqueue(Command.HoldPosition(owner, h)); break;
            }
            n++;
        }
        return n;
    }

    /// <summary>
    /// The Tent-and-Raider scene of BUG-0154: in one tick an attack-move to the Raider and then, later in the same tick,
    /// a Move (or Stop): the later order wins, so the attack-move's re-pick doesn't happen (a Move leaves no target, a Stop no attack-move). An
    /// attack-move to the Raider given twice in the same tick (the second is the new leg's own point) still re-picks (the code's stated rule).
    /// </summary>
    [Theory]
    [InlineData(0)] // new point, then Move: the Move wins
    [InlineData(1)] // new point, then Stop: the Stop wins
    [InlineData(2)] // new point twice: the second is that leg's own point, the re-pick stands
    public void SameTick_LaterOrder_DecidesTheRepick(int then)
    {
        Simulation sim = Flat(units: 8);
        UnitStore u = sim.World.Units;
        int tentType = TestSim.Data.FindBuilding("whirlwind_tent");
        Assert.True(sim.World.Buildings.Spawn(1, tentType, 22 * sim.World.NavGrid.Width + 20, out EntityHandle tent));
        EntityHandle a = Place(sim, 0, HeavyInfantry, At(sim, 17, 23));
        Vector2 first = At(sim, 19, 23);
        sim.Enqueue(Command.AttackMove(0, a, first));
        int hit = RunUntil(sim, () => sim.World.Buildings.Hp[tent.Index] < sim.World.Data.Buildings[tentType].Hp, 600);
        Assert.True(hit < 600, "setup: the Tent was never hit");
        EntityHandle e = Place(sim, 1, Raider, At(sim, 17, 28));
        sim.Enqueue(Command.HoldPosition(1, e));
        sim.Enqueue(Command.AttackMove(0, a, u.Position[e.Index]));
        if (then == 0) sim.Enqueue(Command.Move(0, a, At(sim, 10, 10)));
        else if (then == 1) sim.Enqueue(Command.Stop(0, a));
        else sim.Enqueue(Command.AttackMove(0, a, u.Position[e.Index]));
        sim.Tick();
        sim.Tick(); // a command enqueued now applies on the second tick
        Assert.False(u.Repick[a.Index]);
        if (then == 2)
        {
            Assert.Equal(e, u.Target[a.Index]);
            Assert.Equal(CombatMode.AttackMove, u.Mode[a.Index]);
        }
        else if (then == 0)
        {
            Assert.Equal(default, u.Target[a.Index]); // walking away under a Move: no target
            Assert.Equal(CombatMode.None, u.Mode[a.Index]);
        }
        else
        {
            // A stopped unit scans as any idle unit does (it may take the Raider by Retaliate), but not as an attack-move.
            Assert.NotEqual(CombatMode.AttackMove, u.Mode[a.Index]);
        }
    }
}
