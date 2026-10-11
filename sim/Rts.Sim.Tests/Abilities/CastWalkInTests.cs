using System.Numerics;
using Rts.Sim.Abilities;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests;

/// <summary>
/// M4-H3, BUG-0461 (Producer decision 2026-10-10, docs/03 "Implementation (M4-4a)" walk-in rule): a caster walking into range
/// that is held up by its own side's fight (an own unit attacking or casting within <see cref="SimConfig.CastWalkInBlockerRange"/>
/// = 1.5 m ahead) counts no stuck ticks and casts once the way clears; any other stuck walk-in waits
/// <see cref="SimConfig.CastWalkInPatienceSeconds"/> = 3 s (not a Move's 1 s) before the cast is dropped; a dropped cast raises an
/// <see cref="AbilityEvent"/> with <see cref="AbilityEvent.Dropped"/>. The scenes are the three sandbox replays' layouts
/// (the bug file): a caster behind its own crossbowmen in the only way down (replay 1: a 1-cell pass), one ringed by its own
/// fighters that never takes a step (replay 2), and a walk-in stuck behind a unit that will never move.
/// </summary>
public class CastWalkInTests
{
    private static int Sapper => TestSim.Data.FindUnit("malazan_sapper");

    /// <summary>
    /// 30 x 9: an enemy strip (cell rows 1-2), a cliff band (row 3, open at x 28 only), a 1-cell (2 m) pass along row 4
    /// (x 1-28), and a cliff band below it. The pass's west end is reached only along the pass (the open end is in the east,
    /// so the pocket rule keeps both as one region); a crossbowman in the pass shoots a worker on the strip 4 m away.
    /// </summary>
    private static Heightmap PassMap() => LocalMovementTests.Rows(
        "000000000000000000000000000000",
        "000000000000000000000000000000",
        "000000000000000000000000000000",
        "111111111111111111111111111100",
        "000000000000000000000000000000",
        "111111111111111111111111111111",
        "111111111111111111111111111111",
        "111111111111111111111111111111",
        "111111111111111111111111111111");

    private static Simulation Sim(Heightmap map, bool combat = true, SimConfig? config = null) =>
        TestSim.Explored(new Simulation(config ?? (TestSim.Config(Seed: 1, PlayerCount: 2, UnitCapacity: 64, CommandCapacity: 512) with { Combat = combat }), map));

    /// <summary>An enemy worker that holds where it stands and outlasts any fight a test runs (until <see cref="EndFight"/>).</summary>
    private static EntityHandle Dummy(Simulation sim, Vector2 at)
    {
        EntityHandle h = Place(sim, 1, Laborer, at);
        sim.World.Units.Hold[h.Index] = true;
        sim.World.Units.Hp[h.Index] = 1_000_000;
        return h;
    }

    /// <summary>Own crossbowmen at <paramref name="at"/>, each ordered to attack <paramref name="target"/> (spotted for player 0).</summary>
    private static EntityHandle[] Fighters(Simulation sim, EntityHandle target, params Vector2[] at)
    {
        Spot(sim, 0, target);
        var list = new EntityHandle[at.Length];
        for (int k = 0; k < at.Length; k++)
        {
            list[k] = Place(sim, 0, Crossbowman, at[k]);
            sim.Enqueue(Command.Attack(0, list[k], target, false));
        }
        return list;
    }

    /// <summary>The fight ends: the dummy drops to 1 hp, so the next bolt kills it and its attackers stand Idle.</summary>
    private static void EndFight(Simulation sim, EntityHandle dummy) => sim.World.Units.Hp[dummy.Index] = 1;

    /// <summary>The dropped-cast events of <paramref name="caster"/> in the last tick.</summary>
    private static int Drops(Simulation sim, EntityHandle caster)
    {
        int n = 0;
        foreach (AbilityEvent e in sim.World.AbilityEvents)
            if (e.Caster == caster && e.Dropped) n++;
        return n;
    }

    /// <summary>Runs <paramref name="ticks"/> ticks, asserting each one that the caster still walks to its cast (Moving, cast pending, stuck count at most 1, no drop).</summary>
    private static void AssertWaits(Simulation sim, EntityHandle caster, int ticks)
    {
        UnitStore u = sim.World.Units;
        for (int t = 0; t < ticks; t++)
        {
            sim.Tick();
            Assert.True(Drops(sim, caster) == 0, $"cast dropped on tick {sim.TickNumber - 1}, {t + 1} ticks into the wait");
            Assert.True(u.CastAbility[caster.Index] >= 0 && u.State[caster.Index] == UnitState.Moving,
                $"tick {sim.TickNumber - 1}: cast {u.CastAbility[caster.Index]}, state {u.State[caster.Index]}");
            Assert.True(u.StuckTicks[caster.Index] <= 1, $"tick {sim.TickNumber - 1}: {u.StuckTicks[caster.Index]} stuck ticks behind its own fighters");
        }
    }

    /// <summary>
    /// Replay 1's layout (seed 6, tick 969): the Cadre Mage casts Telas Fire 44 m away along the only way there, a 1-cell
    /// pass two of its own crossbowmen stand in, attacking. It walks up behind them and waits 5 s (well past the 3 s
    /// patience) counting no stuck tick; when their fight ends they stand Idle, it shoves them along and the cast resolves.
    /// Before M4-H3 it gave up after 1 s and the cast was silently lost.
    /// </summary>
    [Fact]
    public void ACasterBehindItsOwnFightersInAPass_WaitsWithoutCountingStuck_AndCastsWhenTheWayClears()
    {
        Simulation sim = Sim(PassMap());
        UnitStore u = sim.World.Units;
        EntityHandle dummy = Dummy(sim, At(sim, 17, 2));
        // Two abreast fill the 2 m pass (a single 0.4 m unit leaves room to slip by), one more behind them.
        Fighters(sim, dummy, At(sim, 17, 4, 0f, -0.5f), At(sim, 17, 4, 0f, 0.5f), At(sim, 16, 4));
        EntityHandle mage = Place(sim, 0, AbilityScenes.Mage, At(sim, 24, 4));
        sim.Tick();
        sim.Tick();
        sim.Enqueue(Command.UseAbility(0, mage, 0, At(sim, 2, 4)));
        sim.Tick(); // the command applies in the next tick
        // Walk up (about 9 m at 4.2 m/s), then the long wait.
        AssertWaits(sim, mage, 140);
        Assert.True(u.Position[mage.Index].X < At(sim, 20, 4).X, $"never walked up: x {u.Position[mage.Index].X}");
        EndFight(sim, dummy);
        int resolved = AbilityScenes.TickOf(sim, mage, resolved: true, max: 400);
        Assert.True(resolved > 0);
        Assert.True(u.State[mage.Index] == UnitState.Idle);
    }

    /// <summary>
    /// Replay 2's layout (seed 6, tick 2164): the Sapper stands ringed by seven of its own crossbowmen 1 m away, all
    /// attacking a worker 8 m north; its Cusser point is 20 m south (range 6). It never takes a step, and it waits; once
    /// the fight ends it shoves out of the ring and the cast resolves.
    /// </summary>
    [Fact]
    public void ACasterRingedByItsOwnFighters_NeverMoving_WaitsAndCastsWhenTheFightEnds()
    {
        Simulation sim = Sim(LocalMovementTests.Flat(48));
        UnitStore u = sim.World.Units;
        Vector2 c = At(sim, 24, 24);
        EntityHandle dummy = Dummy(sim, c + new Vector2(0f, -8f));
        var ring = new Vector2[7];
        for (int k = 0; k < ring.Length; k++)
        {
            float a = k * MathF.Tau / ring.Length + 0.3f;
            ring[k] = c + new Vector2(MathF.Cos(a), MathF.Sin(a));
        }
        Fighters(sim, dummy, ring);
        EntityHandle sapper = Place(sim, 0, Sapper, c);
        sim.Tick();
        sim.Tick();
        sim.Enqueue(Command.UseAbility(0, sapper, 0, c + new Vector2(0f, 20f)));
        sim.Tick();
        AssertWaits(sim, sapper, 100);
        Assert.True(Vector2.Distance(u.Position[sapper.Index], c) < 0.5f, $"walked {Vector2.Distance(u.Position[sapper.Index], c)} m out of the ring");
        EndFight(sim, dummy);
        int resolved = AbilityScenes.TickOf(sim, sapper, resolved: true, max: 400);
        Assert.True(resolved > 0);
    }

    /// <summary>
    /// A walk-in stuck behind a unit that will never move (an enemy standing in the pass, or an own unit holding position:
    /// a holder is not a fight that ends) is genuinely stuck: the cast is dropped 3 s (60 stuck ticks) after the caster's
    /// last progress tick, not a Move's 1 s, with an <see cref="AbilityEvent"/> that has <see cref="AbilityEvent.Dropped"/>
    /// (not resolved, the cast point, the ability) and no cooldown. An own fighter <i>behind</i> the caster doesn't hold it.
    /// </summary>
    [Theory]
    [InlineData("enemy")]
    [InlineData("own holder")]
    [InlineData("own fighter behind")]
    public void AGenuinelyStuckWalkIn_DropsTheCastAfterThreeSeconds_WithADroppedEvent(string blocker)
    {
        Simulation sim = Sim(PassMap());
        UnitStore u = sim.World.Units;
        EntityHandle mage;
        if (blocker == "own fighter behind")
        {
            // Already against a wall of two enemies when ordered; the fighter stands 0.9 m behind it, shooting a worker on the strip.
            Dummy(sim, At(sim, 14, 4, 0f, -0.5f));
            Dummy(sim, At(sim, 14, 4, 0f, 0.5f));
            EntityHandle dummy = Dummy(sim, At(sim, 16, 2));
            mage = Place(sim, 0, AbilityScenes.Mage, At(sim, 14, 4, 0.9f));
            Fighters(sim, dummy, At(sim, 14, 4, 1.8f));
        }
        else
        {
            // Two abreast fill the 2 m pass.
            foreach (float dy in new[] { -0.5f, 0.5f })
            {
                EntityHandle wall = Place(sim, blocker == "enemy" ? 1 : 0, Laborer, At(sim, 17, 4, 0f, dy));
                u.Hold[wall.Index] = true;
                u.Hp[wall.Index] = 1_000_000;
            }
            mage = Place(sim, 0, AbilityScenes.Mage, At(sim, 24, 4));
        }
        sim.Tick();
        sim.Tick();
        Vector2 point = At(sim, 2, 4);
        sim.Enqueue(Command.UseAbility(0, mage, 0, point));
        int lastProgress = -1, drop = -1;
        var trace = new System.Text.StringBuilder();
        for (int t = 0; t < 400 && drop < 0; t++)
        {
            sim.Tick();
            int ran = sim.TickNumber - 1;
            if (t % 10 == 0) trace.Append($" [{ran} {u.State[mage.Index]} c{u.CastAbility[mage.Index]} s{u.StuckTicks[mage.Index]} {u.Position[mage.Index]}]");
            if (u.CastAbility[mage.Index] >= 0 && u.State[mage.Index] == UnitState.Moving && u.StuckTicks[mage.Index] == 0) lastProgress = ran;
            foreach (AbilityEvent e in sim.World.AbilityEvents)
            {
                if (e.Caster != mage || !e.Dropped) continue;
                Assert.False(e.Resolved);
                Assert.Equal(point, e.Point);
                Assert.Equal(sim.World.Data.Units[AbilityScenes.Mage].Abilities[0], e.Ability);
                Assert.Equal(0, e.Owner);
                drop = ran;
            }
        }
        Assert.True(drop > 0, "never dropped:" + trace);
        Assert.True(lastProgress > 0, "never walked:" + trace);
        int waited = drop - lastProgress;
        // 60 stuck ticks after the last progress tick end the walk; the next tick's phase 6 drops the cast.
        Assert.InRange(waited, sim.World.CastWalkInPatienceTicks, sim.World.CastWalkInPatienceTicks + 2);
        Assert.Equal(60, sim.World.CastWalkInPatienceTicks);
        Assert.Equal(-1, u.CastAbility[mage.Index]);
        Assert.True(u.AbilityReadyTick[mage.Index * Data.DataLimits.MaxUnitAbilities] <= sim.TickNumber, "a dropped cast starts no cooldown");
    }

    /// <summary>The patience and the range are config, not literals: 1 s of patience drops after 20 stuck ticks; a range of 0 stops the own-fighter hold right behind a fighter whose body isn't touching.</summary>
    [Fact]
    public void ThePatienceAndTheBlockerRange_ComeFromTheConfig()
    {
        SimConfig config = TestSim.Config(Seed: 1, PlayerCount: 2, UnitCapacity: 64, CommandCapacity: 512) with { CastWalkInPatienceSeconds = 1f, CastWalkInBlockerRange = 0f };
        Simulation sim = Sim(PassMap(), config: config);
        Assert.Equal(20, sim.World.CastWalkInPatienceTicks);
        Assert.Equal(0f, sim.World.CastWalkInBlockerRange);
        Assert.Throws<ArgumentOutOfRangeException>(() => (config with { CastWalkInPatienceSeconds = 0f }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (config with { CastWalkInBlockerRange = -1f }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (config with { CastWalkInBlockerRange = float.NaN }).Validate());
        SimConfig defaults = TestSim.Config(Seed: 1, PlayerCount: 2, UnitCapacity: 8, CommandCapacity: 8);
        Assert.Equal((1.5f, 3f), (defaults.CastWalkInBlockerRange, defaults.CastWalkInPatienceSeconds));
    }
}
