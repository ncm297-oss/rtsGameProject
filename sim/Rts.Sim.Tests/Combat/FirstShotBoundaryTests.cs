using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using static Rts.Sim.Tests.HitBonusScenes;

namespace Rts.Sim.Tests;

/// <summary>
/// M8-3 criterion 5, the boundary of docs/01 2026-11-18 (b) (a): the bonus follows the shooter's Stealthed status at the
/// tick its shot lands, not when it was thrown. A Teblor Javelin Thrower (stand-still stealth 3 s, <c>fromStealthBonus</c>
/// 0.5) hidden after 61 still ticks throws at a Malazan Laborer 10 m off (12 pierce x 1.25 = 15 plain, x 1.5 = 22.5 -> 23).
/// The javelin is thrown in tick <see cref="Thrown"/> and lands in tick <see cref="Lands"/> (the reference run). Each case
/// does one thing to the shooter between two ticks and pins the landing's damage on both sides of the boundary.
/// </summary>
public class FirstShotBoundaryTests
{
    /// <summary>The tick the javelin appears in the store (the wind-up's end) and the tick it lands, in the reference run.</summary>
    private const int Thrown = 70, Lands = 78;

    private const int Plain = 15, Bonus = 23;

    private sealed record Shot(Simulation Sim, EntityHandle Thrower, EntityHandle Target)
    {
        internal World W => Sim.World;

        /// <summary>Runs ticks until <paramref name="tick"/> is the next to run.</summary>
        internal void RunTo(int tick)
        {
            while (Sim.TickNumber < tick) Sim.Tick();
        }

        /// <summary>The damage the target takes in the next tick.</summary>
        internal int NextTick()
        {
            int before = W.Units.Hp[Target.Index];
            Sim.Tick();
            return before - W.Units.Hp[Target.Index];
        }
    }

    /// <summary>The hidden thrower with its attack ordered (applied in tick 61).</summary>
    private static Shot Hidden()
    {
        Simulation sim = Scene();
        World w = sim.World;
        EntityHandle j = Place(sim, 2, "teblor_javelin_thrower", At(sim, 10, 20));
        EntityHandle t = Dummy(sim, 0, At(sim, 40, 40)); // out of sight while it waits
        StillScenes.Ticks(sim, 61);
        Assert.True(StillScenes.Held(sim, j) && w.IsStealthed(j.Index));
        w.Units.Position[t.Index] = At(sim, 15, 20);
        sim.Enqueue(Command.Attack(2, j, t, false));
        return new Shot(sim, j, t);
    }

    [Fact]
    public void TheReferenceRun_ThrowsAt70_LandsAt78_WithTheBonus()
    {
        Shot s = Hidden();
        Assert.True(CombatScenes.RunUntil(s.Sim, () => s.W.Projectiles.Count > 0, 100) < 100);
        Assert.Equal(Thrown, s.Sim.TickNumber - 1);
        Assert.True(s.W.IsStealthed(s.Thrower.Index)); // throwing is not landing: still hidden in flight
        s.RunTo(Lands);
        Assert.True(s.W.IsStealthed(s.Thrower.Index));
        Assert.Equal(Bonus, s.NextTick());
        Assert.False(s.W.IsStealthed(s.Thrower.Index)); // revealed by its landing
        // And the next javelin is plain.
        Assert.Equal(Plain, FirstHits(s.Sim, s.Target, 1)[0].Damage);
    }

    /// <summary>(a) It loses hit points with the javelin in the air: the stand-still hold is let go at once, the landing is plain; hurt after the landing, it was not.</summary>
    [Theory]
    [InlineData(Lands, Plain)]
    [InlineData(Lands + 1, Bonus)]
    public void HurtInFlight_TheLandingIsPlain(int hurtBefore, int expected)
    {
        Shot s = Hidden();
        int landed = Hurt(s, hurtBefore);
        Assert.Equal(expected, landed);
    }

    private static int Hurt(Shot s, int before)
    {
        s.RunTo(Math.Min(before, Lands));
        int landed = -1;
        if (before > Lands) landed = s.NextTick(); // the landing first, then the hurt
        EntityHandle enemy = Place(s.Sim, 0, "malazan_laborer", At(s.Sim, 30, 40));
        CombatSystem.HitUnit(s.W, new PendingHit(enemy, 0, s.Thrower, false, 1, 0, false));
        Assert.False(s.W.IsStealthed(s.Thrower.Index));
        if (landed < 0) landed = s.NextTick();
        return landed;
    }

    /// <summary>
    /// (b) It is ordered to walk away with the javelin in the air. A Move sent before tick 76 runs first changes the unit's
    /// position in tick 77 (phase 9); tick 78's phase 5 sees the change and lets the hold go before the landing (plain). One
    /// sent before tick 77 first moves it in tick 78's phase 9, after that tick's phase 5: the hold is let go only in tick
    /// 79, after the landing (bonus). Pinned from the run, both sides of the boundary.
    /// </summary>
    [Theory]
    [InlineData(Lands - 2, Plain)]
    [InlineData(Lands - 1, Bonus)]
    public void MovedInFlight_TheLandingIsPlainOnlyOnceTheHoldIsLetGo(int orderedBefore, int expected)
    {
        Shot s = Hidden();
        Vector2 start = s.W.Units.Position[s.Thrower.Index];
        s.RunTo(orderedBefore);
        s.Sim.Enqueue(Command.Move(2, s.Thrower, At(s.Sim, 10, 10)));
        s.RunTo(Lands);
        Assert.Equal(expected == Plain, s.W.Units.Position[s.Thrower.Index] != start); // stepped in tick 77, or not yet
        Assert.True(s.W.IsStealthed(s.Thrower.Index)); // either way still held after tick 77: tick 78's phase 5 decides
        Assert.Equal(expected, s.NextTick());
    }

    /// <summary>(c) Another hit of its own lands first and reveals it: the javelin lands plain; that hit after the landing changes nothing.</summary>
    [Theory]
    [InlineData(Lands, Plain)]
    [InlineData(Lands + 1, Bonus)]
    public void RevealedByAnotherHitOfItsOwn_TheLandingIsPlain(int hitBefore, int expected)
    {
        Shot s = Hidden();
        EntityHandle other = Dummy(s.Sim, 0, At(s.Sim, 30, 40));
        s.RunTo(Math.Min(hitBefore, Lands));
        int landed = hitBefore > Lands ? s.NextTick() : -1;
        CombatSystem.HitUnit(s.W, new PendingHit(s.Thrower, 2, other, false, 1, 0, false));
        Assert.True(s.W.Units.Statuses.IndexOf(s.Thrower.Index, s.W.Data.FindStatus("revealed")) >= 0);
        Assert.False(s.W.IsStealthed(s.Thrower.Index));
        if (landed < 0) landed = s.NextTick();
        Assert.Equal(expected, landed);
    }

    /// <summary>(d) It dies with the javelin in the air: the javelin still lands, plain (a dead shooter's shot has no bonus).</summary>
    [Theory]
    [InlineData(Lands, Plain)]
    [InlineData(Lands + 1, Bonus)]
    public void DiedInFlight_TheLandingIsPlain(int diedBefore, int expected)
    {
        Shot s = Hidden();
        s.RunTo(Math.Min(diedBefore, Lands));
        int landed = diedBefore > Lands ? s.NextTick() : -1;
        s.W.Units.Free(s.Thrower);
        if (landed < 0) landed = s.NextTick();
        Assert.Equal(expected, landed);
    }

    /// <summary>(e) Still Stealthed at the landing but detected by an enemy (a Laborer 2 m off, inside the 3 m proximity): the bonus holds.</summary>
    [Fact]
    public void DetectedButStillStealthed_TheLandingCarriesTheBonus()
    {
        Shot s = Hidden();
        Place(s.Sim, 0, "malazan_laborer", s.W.Units.Position[s.Thrower.Index] + new Vector2(0f, 2f));
        s.RunTo(Lands);
        Assert.True(s.W.IsDetectedBy(0, s.Thrower.Index));
        Assert.True(s.W.IsStealthed(s.Thrower.Index));
        Assert.Equal(Bonus, s.NextTick());
    }
}
