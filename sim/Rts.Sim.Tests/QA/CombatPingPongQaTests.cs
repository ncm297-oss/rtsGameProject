using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.Pathfinding;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M4-2a (session 2026-10-08-0313), BUG-0150: the combat-on versions of the two-owner movement rows that went combat
/// off in M4-2a (CrowdRowSweep seeds 81-140, GridChangeQaTests' four groups). The re-check (round 1) showed they are not
/// the ping-pong loop (that was GridChangeFuzz seed 3, now back on combat) but a slow, one-sided brawl: player 1's units
/// that can't fight back in this slice (ranged, casters, the ram) are cut down by a crowd of player 0's melee units, and
/// the scene ends on its own (seed 110: tick 3,759; four groups: 5,057-6,618). So the bound is the combat one of
/// docs/03 "Combat switch" (CombatTerminationTests: at rest within 3x the walking limit, 9,000 ticks here), plus a
/// livelock guard: while anybody moves, hp is lost in every 1,000-tick window.
/// </summary>
[Collection(SerialCollection.Name)]
public class CombatPingPongQaTests
{
    private const int WalkingLimit = 3000;
    private const int CombatLimit = 3 * WalkingLimit;

    private readonly ITestOutputHelper _out;

    public CombatPingPongQaTests(ITestOutputHelper output) => _out = output;

    /// <summary>Ticks <paramref name="sim"/> until nobody moves; returns that tick, or -1 at <see cref="CombatLimit"/>; <paramref name="stall"/> is the first 1,000-tick window with units moving and no hp lost, or -1.</summary>
    private static int RunToRest(Simulation sim, Action<int>? beforeTick, out int stall)
    {
        UnitStore u = sim.World.Units;
        stall = -1;
        long windowStartHp = long.MaxValue;
        sim.Tick(); // the Moves are stamped for the next tick
        for (int t = 1; t <= CombatLimit; t++)
        {
            beforeTick?.Invoke(t);
            sim.Tick();
            long hp = 0;
            int moving = 0;
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i]) continue;
                hp += u.Hp[i];
                if (u.State[i] == UnitState.Moving) moving++;
            }
            if (moving == 0) return t;
            if (t % 1000 == 1) windowStartHp = hp;
            else if (t % 1000 == 0 && hp == windowStartHp && stall < 0) stall = t;
        }
        return -1;
    }

    private static Simulation CrowdSweepScene(ulong seed)
    {
        // CrowdRows.MoreGoalsThanCacheSlots' scene (one player per goal), combat on, without its 3,000-tick walking bound.
        Simulation sim = MoveScenario.Spawn(seed, units: 128, maxCost: 15f, out int center);
        var g = sim.World.NavGrid;
        FlowField near = FlowField.Build(g, center);
        var goals = new List<int>();
        for (int c = 0; c < g.Width * g.Height && goals.Count < 64; c++)
            if (near.CostAt(c) <= 15f) goals.Add(c);
        Assert.Equal(64, goals.Count);
        UnitStore u = sim.World.Units;
        int firstOfPlayer1 = 0;
        while (firstOfPlayer1 < u.Capacity && u.Owner[firstOfPlayer1] == 0) firstOfPlayer1++;
        for (int i = 0; i < u.Capacity; i++)
        {
            int k = u.Owner[i] == 0 ? 2 * i % 64 : (2 * (i - firstOfPlayer1) + 1) % 64;
            sim.Enqueue(Command.Move(u.Owner[i], MoveScenario.Handle(sim, i), MoveScenario.Center(g, goals[k])));
        }
        return sim;
    }

    /// <summary>GridChangeQaTests' four-groups row with combat on (closings every 1 / 2 / 3 ticks for 300 ticks): at rest within 9,000 ticks, no stalled window.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void FourGroups_CombatOn_ComesToRest_WithinTheCombatBound(int period)
    {
        Simulation sim = MoveScenario.Spawn(1, 300, 40f, out int goalCell);
        World w = sim.World;
        Vector2[] goals = CrowdRows.FourPoints(MoveScenario.Center(w.NavGrid, goalCell));
        UnitStore u = w.Units;
        for (int i = 0; i < u.Capacity; i++)
            sim.Enqueue(Command.Move(u.Owner[i], MoveScenario.Handle(sim, i), goals[CrowdRows.PointOf(u, i, onePlayerPerPoint: true)]));
        int end = RunToRest(sim, t => { if (t < 300 && t % period == 0) w.NavGrid.BumpVersionForTests(); }, out int stall);
        _out.WriteLine($"closing every {period}: at rest at tick {end}; stalled window {stall}");
        Assert.True(end > 0, $"still moving after {CombatLimit} ticks");
        Assert.Equal(-1, stall);
    }

    /// <summary>CrowdRowSweepStressTests' seeds 81-140 with combat on: every map at rest within 9,000 ticks, no stalled window.</summary>
    [Fact]
    [Trait("Category", "Soak")]
    public void MoreGoalsThanCacheSlots_Seeds81To140_CombatOn_ComeToRest_WithinTheCombatBound()
    {
        var bad = new List<string>();
        int worst = 0;
        ulong worstSeed = 0;
        for (ulong seed = 81; seed <= 140; seed++)
        {
            int end = RunToRest(CrowdSweepScene(seed), null, out int stall);
            if (end < 0 || stall >= 0) bad.Add($"seed {seed}: end {end}, stalled window {stall}");
            if (end > worst) (worst, worstSeed) = (end, seed);
        }
        _out.WriteLine($"slowest: seed {worstSeed} at tick {worst}; {bad.Count} bad. {string.Join("; ", bad)}");
        Assert.Empty(bad);
    }
}
