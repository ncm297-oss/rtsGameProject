using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Draw;

/// <summary>
/// M6-S13 criterion 5 (the invariant fuzz): a staged spent match of two to four seats (an Infantry Hall and one to four soldiers
/// a seat, no gold, no worker, so nothing can make it un-spent) with seeded random move / attack-move / attack / stop / hold
/// orders. It is over within <c>wait x (non-summoned units alive at the first spent tick + seats + 1)</c> ticks, and the clock
/// never goes back to 0 without a death or a defeat on that tick. 200 seeds, a 300-tick wait (15 s), orders that send units at enemy units often enough that most matches see deaths and restarts before the draw.
/// </summary>
public class DrawInvariantFuzzTests
{
    private readonly ITestOutputHelper _out;

    public DrawInvariantFuzzTests(ITestOutputHelper output) => _out = output;

    private static readonly Lazy<GameData> s_data = new(() => DrawScenes.DataWith(15)); // 300 ticks

    public static TheoryData<int> Batches()
    {
        var rows = new TheoryData<int>();
        for (int b = 0; b < 10; b++) rows.Add(b);
        return rows;
    }

    [Theory]
    [MemberData(nameof(Batches))]
    public void ASpentMatch_AlwaysEnds_AndTheClockRestartsOnlyOnADeathOrADefeat(int batch)
    {
        int ended = 0, drawn = 0, won = 0, restarts = 0;
        for (int k = 0; k < 20; k++)
        {
            ulong seed = (ulong)(batch * 20 + k + 1);
            (bool draw, int r) = Play(seed);
            ended++;
            restarts += r;
            if (draw) drawn++;
            else won++;
        }
        _out.WriteLine($"batch {batch}: {ended} matches over ({drawn} drawn, {won} won), {restarts} clock restarts");
    }

    /// <summary>One fuzzed match; (drawn, clock restarts). Fails on a broken invariant.</summary>
    internal static (bool Drawn, int Restarts) Play(ulong seed, bool cancels = false)
    {
        var rng = new SimRng(seed, 913);
        int players = 2 + rng.NextInt(0, 3);
        int soldiers = 1 + rng.NextInt(0, 4);
        Simulation sim = DrawScenes.Spent(s_data.Value, players, soldiers, size: 40, seed: seed);
        World w = sim.World;
        int wait = w.DrawWaitTicks;
        UnitStore u = w.Units;
        // Spread every soldier at random and free them from Hold, so orders move them.
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            u.Hold[i] = false;
            u.Position[i] = u.PrevPosition[i] = new Vector2(6f + rng.NextFloat() * 68f, 6f + rng.NextFloat() * 68f);
        }
        sim.Tick();
        Assert.True(w.MatchSpent, $"seed {seed}: the staged match is not spent");
        int alive = 0;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && !u.IsSummoned(i)) alive++;
        long bound = (long)wait * (alive + players + 1);
        int start = sim.TickNumber, prevClock = w.DrawClock, restarts = 0;
        while (!sim.MatchOver)
        {
            Assert.True(sim.TickNumber - start <= bound, $"seed {seed}: not over within {bound} ticks ({players} seats, {alive} units)");
            if (rng.NextInt(0, 4) == 0) Orders(sim, ref rng);
            int unitsBefore = u.Count;
            sim.Tick();
            bool died = u.Count < unitsBefore, defeated = w.DefeatEvents.Length > 0;
            if (w.DrawClock == 0 && prevClock > 0 && !sim.MatchOver)
            {
                restarts++;
                Assert.True(died || defeated, $"seed {seed} tick {sim.TickNumber - 1}: the clock restarted from {prevClock} with no death and no defeat");
            }
            if (w.DrawClock > 0) Assert.Equal(prevClock + 1, w.DrawClock);
            prevClock = w.DrawClock;
        }
        Assert.True(sim.TickNumber - start <= bound, $"seed {seed}: over only after {sim.TickNumber - start} > {bound} ticks");
        return (w.MatchDrawn, restarts);
    }

    /// <summary>A random order for a random live unit of each seat: move, attack-move, attack an enemy, stop or hold.</summary>
    private static void Orders(Simulation sim, ref SimRng rng)
    {
        World w = sim.World;
        UnitStore u = w.Units;
        var live = new List<int>();
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i]) live.Add(i);
        if (live.Count == 0) return;
        int i0 = live[rng.NextInt(0, live.Count)];
        var h = new EntityHandle(i0, u.Generation[i0]);
        int owner = u.Owner[i0];
        var to = new Vector2(4f + rng.NextFloat() * 72f, 4f + rng.NextFloat() * 72f);
        int other = live[rng.NextInt(0, live.Count)];
        switch (rng.NextInt(0, 6))
        {
            case 0: sim.Enqueue(Command.Move(owner, h, to)); break;
            case 1: sim.Enqueue(Command.AttackMove(owner, h, to)); break;
            case 4: sim.Enqueue(Command.AttackMove(owner, h, u.Position[other])); break; // toward some unit, often an enemy
            case 2:
                int j = live[rng.NextInt(0, live.Count)];
                if (u.Owner[j] != owner && w.IsEnemy(owner, u.Owner[j]))
                {
                    var t = new EntityHandle(j, u.Generation[j]);
                    CombatScenes.Spot(sim, owner, t);
                    sim.Enqueue(Command.Attack(owner, h, t, isBuilding: false));
                }
                break;
            case 3: sim.Enqueue(Command.Stop(owner, h)); break;
            default: sim.Enqueue(Command.HoldPosition(owner, h)); break;
        }
    }
}
