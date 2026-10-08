using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Movement;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA attacks on the M1-5 queued-walker rule (a no-progress tick while a groupmate just ahead made
/// progress holds the stuck count): truly wedged units must still give up, and the hold must end
/// once the group stops making progress.
/// </summary>
public class QueuedGiveUpQaTests
{
    private readonly ITestOutputHelper _out;

    public QueuedGiveUpQaTests(ITestOutputHelper output) => _out = output;

    private static int CountMoving(UnitStore u, int owner = -1)
    {
        int n = 0;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.State[i] == UnitState.Moving && (owner < 0 || u.Owner[i] == owner)) n++;
        return n;
    }

    /// <summary>A 48 x 24 map split by a cliff column at x = 20, open only through rows <paramref name="lo"/>..<paramref name="hi"/>.</summary>
    private static Heightmap GapMap(int lo, int hi)
    {
        var rows = new string[24];
        for (int y = 0; y < 24; y++)
        {
            char[] r = new string('0', 48).ToCharArray();
            if (y < lo || y > hi) r[20] = '1';
            rows[y] = new string(r);
        }
        return LocalMovementTests.Rows(rows);
    }

    /// <summary>
    /// BUG-0035: a 3-cell gap (a ramp's width) plugged by three wide enemies standing still. Enemies are
    /// walls (docs/03), and the 0.2 m slits between them are far narrower than any unit, so nobody may
    /// get through. At M1-5 20-27 of 60 squeeze through per seed (1-21 under the pre-M1-5 give-up rule).
    /// </summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    [InlineData(4UL)]
    [InlineData(5UL)]
    public void CrowdAtAThreeCellGapPluggedByEnemies_NobodyPassesThrough(ulong seed)
    {
        (_, _, _, int past) = RunPluggedGap(seed, 10, 12);
        Assert.Equal(0, past);
    }

    /// <summary>
    /// The M1-5 hold must not keep a blocked crowd alive: a 1-cell gap plugged by one wide enemy (no
    /// slit to squeeze through), so nobody gets past the plug and every walker gives up within the
    /// walk to the plug plus 10 give-up periods.
    /// </summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    [InlineData(4UL)]
    [InlineData(5UL)]
    public void CrowdAtAOneCellGapPluggedByAnEnemy_AllGiveUp_InBoundedTime(ulong seed)
    {
        (int ticks, int bound, int gaveUp, int past) = RunPluggedGap(seed, 11, 11);
        Assert.Equal(0, past);
        Assert.Equal(60, gaveUp);
        Assert.True(ticks <= bound, $"{ticks} ticks > {bound}");
    }

    /// <summary>
    /// 60 units (every type) packed west of a gap in a cliff column (rows lo..hi at x = 20), every gap
    /// cell plugged by a wide (radius 0.9) enemy at its center, all ordered to a point east of it.
    /// Ticks until player 0 has no Moving unit; returns (ticks, bound, gave up, past the plug).
    /// </summary>
    private (int Ticks, int Bound, int GaveUp, int Past) RunPluggedGap(ulong seed, int lo, int hi)
    {
        const int crowd = 60;
        Simulation sim = new(TestSim.ConfigNoCombat(Seed: seed, PlayerCount: 2, UnitCapacity: crowd + hi - lo + 1, CommandCapacity: 4 * crowd + 16), GapMap(lo, hi));
        NavGrid g = sim.World.NavGrid;
        var rng = new SimRng(seed, 77);
        for (int i = 0; i < crowd; i++)
        {
            var at = new Vector2(24.1f + rng.NextFloat() * 13.8f, 12.1f + rng.NextFloat() * 21.8f);
            sim.Enqueue(Command.SpawnUnit(0, i % TestSim.UnitTypeCount, at));
        }
        int wide = LocalMovementTests.TypeWithRadius(0.9f);
        for (int y = lo; y <= hi; y++) sim.Enqueue(Command.SpawnUnit(1, wide, g.CellCenter(20, y)));
        sim.Tick();
        sim.Tick();
        UnitStore u = sim.World.Units;
        Vector2 goal = g.CellCenter(36, 11);
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.Owner[i] == 0) sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, i), goal));
        float slowest = float.MaxValue;
        foreach (var def in TestSim.Data.Units) slowest = MathF.Min(slowest, def.SpeedPerTick);
        // Farthest spawn to the plug: about 15 m; walk time for the slowest type, plus 10 give-up periods.
        int bound = (int)(16f / slowest) + 10 * MovementConstants.GiveUpTicks;
        int ticks = 0;
        do
        {
            sim.Tick();
            ticks++;
            Assert.Equal(-1, MoveScenario.FirstUnitOnBlockedGround(sim.World));
        } while ((ticks < 2 || CountMoving(u, 0) > 0) && ticks < 4 * bound);
        int gaveUp = 0, past = 0;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || u.Owner[i] != 0) continue;
            if (u.GoalCell[i] == -1) gaveUp++;
            if (u.Position[i].X > 21 * MapConstants.CellSize) past++;
        }
        _out.WriteLine($"seed {seed}, gap rows {lo}..{hi}: all stopped after {ticks} ticks (bound {bound}); gave up {gaveUp}/{crowd}, past the plug {past}");
        Assert.Equal(0, CountMoving(u, 0));
        return (ticks, bound, gaveUp, past);
    }

    /// <summary>
    /// The premise of the M1-5 termination argument: a Moving unit reads StuckTicks 0 (the "made
    /// progress last tick" signal neighbors queue on) only right after a tick that set a new best. A
    /// queued tick must never leave 0 behind, or waiters could hold each other up with nobody moving.
    /// Checked every tick of a 200-unit cross-map run: StuckTicks 0 without a lower BestRemaining
    /// than the tick before is a violation.
    /// </summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(7UL)]
    public void CrossMap_NoMovingUnitShowsZeroStuckWithoutANewBest(ulong seed)
    {
        CrossMapQaScenario s = CrossMapQaScenario.Create(seed, 200, 12f)!;
        UnitStore u = s.Sim.World.Units;
        MoveScenario.MoveAll(s.Sim, s.Goal);
        s.Sim.Tick();
        s.Sim.Tick();
        var prevBest = new float[u.Capacity];
        for (int i = 0; i < u.Capacity; i++) prevBest[i] = u.BestRemaining[i];
        int checkedTicks = 0;
        for (int t = 0; t < s.LimitTicks && CountMoving(u) > 0; t++)
        {
            s.Sim.Tick();
            checkedTicks++;
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i] || u.State[i] != UnitState.Moving) continue;
                if (u.StuckTicks[i] == 0)
                    Assert.True(u.BestRemaining[i] < prevBest[i], $"tick {t}: unit {i} reads StuckTicks 0 without a new best ({u.BestRemaining[i]} vs {prevBest[i]})");
                prevBest[i] = u.BestRemaining[i];
            }
        }
        _out.WriteLine($"seed {seed}: {checkedTicks} ticks checked");
    }
}
