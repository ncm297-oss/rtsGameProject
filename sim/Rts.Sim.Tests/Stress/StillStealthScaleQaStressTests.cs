using System.Diagnostics;
using System.Numerics;
using Rts.Sim.Abilities;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;
using static Rts.Sim.Tests.StillScenes;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA (M8-2, session 2026-11-17-0816): the stand-still stealth at scale. 2,000 units, every one of a type with the field
/// (<see cref="StillScenes.Still"/>: Malazan Heavy Infantry / Crossbowman / Laborer, Whirlwind Desert Archer, Teblor Javelin
/// Thrower), three players on a flat 320 m map, combat on, seeded random move / attack-move / attack / stop / hold orders
/// (also the same order spammed and orders to stale handles). Per seed, 2,000 ticks, twins hash-equal every tick and a
/// save at tick 1,000 continuing hash-equal. After every tick, re-derived independently of the store: a unit is held
/// exactly when the delay has passed since its own last position change (tracked here from positions), its last landed attack
/// and its last hp loss (an hp drop seen here must be the store's last hurt tick); the developer's invariant
/// (<see cref="StillScenes.AssertInvariant"/>); the stealth count; and no player sees an undetected stealthed enemy.
/// </summary>
[Trait("Category", "Stress")]
public class StillStealthScaleQaStressTests
{
    private readonly ITestOutputHelper _out;

    public StillStealthScaleQaStressTests(ITestOutputHelper output) => _out = output;

    private static GameData D => Still;

    private const int Units = 2000, Ticks = 2000, SaveAt = 1000, Delay = 60;

    private static readonly string[][] Types =
    {
        new[] { "malazan_heavy_infantry", "malazan_crossbowman", "malazan_laborer" },
        new[] { "whirlwind_desert_archer" },
        new[] { "teblor_javelin_thrower" },
    };

    private static readonly Vector2[] Bases = { new(80f, 80f), new(240f, 80f), new(160f, 240f) };

    private static Simulation NewSim(ulong seed)
    {
        var sim = new Simulation(new SimConfig(seed, 3, Units + 64, 8 * Units) { Data = D }, LocalMovementTests.Flat(160));
        var rng = new SimRng(seed, 1590);
        for (int k = 0; k < Units; k++)
        {
            int p = k % 3;
            string[] list = Types[p];
            Vector2 at = Bases[p] + new Vector2(rng.NextInt(-50, 51), rng.NextInt(-50, 51));
            CombatScenes.Place(sim, p, D.FindUnit(list[rng.NextInt(0, list.Length)]), at);
        }
        return sim;
    }

    private static void Orders(Simulation sim, ref SimRng rng, List<Command> into)
    {
        into.Clear();
        if (sim.TickNumber % 20 != 3) return;
        UnitStore u = sim.World.Units;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || rng.NextInt(0, 6) != 0) continue;
            var h = new EntityHandle(i, u.Generation[i]);
            int p = u.Owner[i];
            int enemy = -1;
            for (int tries = 0; tries < 8 && enemy < 0; tries++)
            {
                int j = rng.NextInt(0, u.Capacity);
                if (u.Alive[j] && u.Owner[j] != p) enemy = j;
            }
            Vector2 near = (rng.NextInt(0, 4) == 0 ? new Vector2(160f, 140f) : Bases[p]) + new Vector2(rng.NextInt(-40, 41), rng.NextInt(-40, 41));
            switch (rng.NextInt(0, 9))
            {
                case 0: case 1: into.Add(Command.Move(p, h, near)); break;
                case 2: if (enemy >= 0) into.Add(Command.AttackMove(p, h, u.Position[enemy])); break;
                case 3: if (enemy >= 0) into.Add(Command.Attack(p, h, new EntityHandle(enemy, u.Generation[enemy]), isBuilding: false)); break;
                case 4: into.Add(Command.Stop(p, h)); break;
                case 5: into.Add(Command.HoldPosition(p, h)); break;
                case 6:
                    for (int k = 0; k < 3; k++) into.Add(Command.Move(p, h, near)); // the same order spammed
                    break;
                case 7: into.Add(Command.Move(p, new EntityHandle(i, u.Generation[i] + 1), near)); break; // a stale handle
                default: break; // left alone: it rests and hides
            }
        }
    }

    private sealed class Tally
    {
        public long Held, Hidden, FullStore, MoveLetGo, HurtLetGo, Ambiguous;
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    [InlineData(4UL)]
    public void TwoThousandFieldUnits_2000Ticks_TwinsAndASaveEqual_TheHoldIsExactlyTheRule(ulong seed)
    {
        Simulation a = NewSim(seed), b = NewSim(seed);
        Simulation? c = null;
        var rng = new SimRng(seed, 1591);
        var commands = new List<Command>();
        World w = a.World;
        UnitStore u = w.Units;
        StillStealthStore st = u.Still!;
        int cap = u.Capacity;
        var lastChange = new int[cap];
        var lastPos = new Vector2[cap];
        var lastHp = new int[cap];
        var heldBefore = new byte[cap];
        for (int i = 0; i < cap; i++)
        {
            lastPos[i] = u.Position[i];
            lastHp[i] = u.Hp[i];
        }
        var tally = new Tally();
        var clock = new Stopwatch();
        for (int t = 0; t < Ticks; t++)
        {
            if (t == SaveAt)
            {
                c = SaveTestRun.Load(D, SaveTestRun.Save(a));
                Assert.Equal(a.StateHash(), c.StateHash());
            }
            Orders(a, ref rng, commands);
            foreach (Command cmd in commands)
            {
                a.Enqueue(cmd);
                b.Enqueue(cmd);
                c?.Enqueue(cmd);
            }
            Array.Copy(st.Held, heldBefore, cap);
            clock.Start();
            a.Tick();
            clock.Stop();
            b.Tick();
            c?.Tick();
            ulong hash = a.StateHash();
            Assert.True(hash == b.StateHash(), $"seed {seed}: twins differ after tick {t}");
            if (c != null) Assert.True(hash == c.StateHash(), $"seed {seed}: the run loaded at {SaveAt} differs after tick {t}");
            AssertInvariant(a, timed: null);

            string at = $"seed {seed} tick {t}";
            int carriers = 0;
            for (int i = 0; i < cap; i++)
            {
                if (!u.Alive[i]) continue;
                bool held = st.Held[i] != 0;
                if (held) tally.Held++;
                if (heldBefore[i] != 0 && !held)
                {
                    if (st.LastHurtTick[i] == t) tally.HurtLetGo++;
                    else tally.MoveLetGo++;
                }
                if (u.Statuses.HasStealth(i)) carriers++;
                // An hp drop in this tick is the store's last hurt tick (regeneration only raises hp).
                if (u.Hp[i] < lastHp[i]) Assert.True(st.LastHurtTick[i] == t, $"{at}: unit {i} lost hp, last hurt {st.LastHurtTick[i]}");
                lastHp[i] = u.Hp[i];
                // The rule, re-derived: this tick's phase 5 saw the position as it was after the last tick.
                int attacked = st.LastAttackTick[i], hurt = st.LastHurtTick[i];
                bool? expect;
                if (hurt == t) expect = false; // let go at once
                else if (attacked == t) expect = null; // landed after the pass: held or not by what the pass saw
                else expect = t - lastChange[i] >= Delay && (attacked < 0 || t - attacked >= Delay) && (hurt < 0 || t - hurt >= Delay);
                if (expect == null) tally.Ambiguous++;
                else if (expect != held)
                {
                    if (expect == true && u.Statuses.Count[i] >= StatusStore.PerUnit && u.Statuses.IndexOf(i, Stealthed(D)) < 0) tally.FullStore++;
                    else Assert.Fail($"{at}: unit {i} ({D.Units[u.TypeId[i]].Key}) held {held}, the rule says {expect}: last change {lastChange[i]} (store {st.MovedTick[i]}), attack {attacked}, hurt {hurt}");
                }
                if (u.Position[i] != lastPos[i])
                {
                    lastPos[i] = u.Position[i];
                    lastChange[i] = t;
                }
                for (int p = 0; p < 3; p++)
                    if (w.HiddenFrom(p, i))
                    {
                        tally.Hidden++;
                        Assert.False(w.Fog.CanSeeUnit(p, i), $"{at}: player {p} sees undetected stealthed {i}");
                    }
            }
            Assert.True(carriers == u.Statuses.StealthedUnits, $"{at}: stealth count {u.Statuses.StealthedUnits}, recount {carriers}");
        }
        _out.WriteLine($"seed {seed}: {u.Count} units left; held unit-ticks {tally.Held}, hidden (unit, player) {tally.Hidden}, lets-go by a move or attack {tally.MoveLetGo}, by hp loss {tally.HurtLetGo}, full-store skips {tally.FullStore}, ambiguous {tally.Ambiguous}; sim A ticks avg {clock.Elapsed.TotalMilliseconds / Ticks:F2} ms (Debug, under the checks' twin load)");
        Assert.True(tally.Held > 100_000, $"only {tally.Held} held unit-ticks");
        Assert.True(tally.MoveLetGo > 0 && tally.HurtLetGo > 0, "no let-go by a move or by hp loss");
        Assert.True(tally.Hidden > 0);
    }

    /// <summary>
    /// Three AI seats (Malazan Hard, Whirlwind Normal, Teblor Hard) on the field copy, so the AIs train field units (the
    /// Crossbowman, Laborer, Heavy Infantry, Desert Archer, Javelin Thrower) and meet still-hidden enemies: 6,000 ticks twice
    /// hash-equal every tick, a save at 3,000 continuing hash-equal, <see cref="PlayerViewOracle"/> on every seat's view at
    /// every fog update (no still-hidden enemy leaks into an AI's view), every own held unit in its owner's view.
    /// </summary>
    [Fact]
    public void ThreeAiSeats_OnTheFieldCopy_6000Ticks_TwinsAndASaveEqual_NoStillHiddenEnemyInAnyAiView()
    {
        const int ticks = 6000, saveAt = 3000;
        const ulong seed = 7;
        GameData d = D;
        PlayerSeat[] seats =
        {
            new(d.FindFaction("malazan"), SeatController.Ai, AiDifficulty.Hard),
            new(d.FindFaction("whirlwind"), SeatController.Ai, AiDifficulty.Normal),
            new(d.FindFaction("teblor"), SeatController.Ai, AiDifficulty.Hard),
        };
        Simulation a = AiScenes.Generated(seats, seed, record: false, out _, units: 600, data: d);
        Simulation b = AiScenes.Generated(seats, seed, record: false, out _, units: 600, data: d);
        Simulation? c = null;
        World w = a.World;
        UnitStore u = w.Units;
        var oracle = new PlayerViewOracle.Tally();
        var held = new long[3];
        long ownHeldNotInView = 0, heldMoving = 0;
        for (int t = 0; t < ticks; t++)
        {
            if (t == saveAt)
            {
                c = SaveTestRun.Load(d, SaveTestRun.Save(a));
                Assert.Equal(a.StateHash(), c.StateHash());
            }
            a.Tick();
            b.Tick();
            c?.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"twins differ after tick {t}");
            if (c != null) Assert.True(a.StateHash() == c.StateHash(), $"the run loaded at {saveAt} differs after tick {t}");
            AssertInvariant(a, timed: null);
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i] || u.Still!.Held[i] == 0) continue;
                int p = u.Owner[i];
                held[p]++;
                if (u.Position[i] != u.PrevPosition[i]) heldMoving++;
                if (!w.IsDefeated(p) && !a.AiOf(p)!.View.TryGetUnit(i, out _)) ownHeldNotInView++;
            }
            if (t == 0 || t % 4 == 1)
                for (int p = 0; p < 3; p++)
                    if (!w.IsDefeated(p)) PlayerViewOracle.Check(w, a.AiOf(p)!.View, p, oracle, $"tick {t} player {p}");
            Assert.True(oracle.First.Length == 0, oracle.First + "\n" + oracle);
        }
        _out.WriteLine($"held unit-ticks by seat {string.Join(" / ", held)}; held while moving {heldMoving}; defeated {string.Join(" ", Enumerable.Range(0, 3).Select(w.IsDefeated))}; over {a.MatchOver}");
        _out.WriteLine($"oracle: {oracle}");
        Assert.Equal(0, ownHeldNotInView);
        Assert.Equal(0, oracle.Leaks);
        Assert.Equal(0, oracle.Misses);
        Assert.True(held[0] + held[1] + held[2] > 1000, "the seats' field units barely hid");
    }
}
