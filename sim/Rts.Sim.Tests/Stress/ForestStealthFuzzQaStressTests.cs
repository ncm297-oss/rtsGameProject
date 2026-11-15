using System.Numerics;
using Rts.Sim.Abilities;
using Rts.Sim.Ai;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Tests.Save;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA (M8-1, session 2026-11-14-1246): forest stealth under hostile command streams and AI seats, on a copy where two
/// factions carry the block (Malazan and Whirlwind, 3 s, siege excepted) and the Teblor do not.
/// (1) Three players on generated forest maps, seeded random orders (attack-moves, walks into and out of the trees, gathers
/// that fell trees, stealth casts, Sandstorms, stops, the same order spammed, orders to dead units): after every tick the
/// twins hash equal, the hold is exactly the rule (re-derived from phase 5's positions and the two timers), the bookkeeping
/// holds, no enemy sees an undetected unit, the target rule holds; a save in the middle continues hash-equal.
/// (2) Four AI seats (two forest factions) for 9,000 ticks twice, hash-equal every tick, the oracle on every seat's view,
/// the seats' own hidden units still moving under their orders, and a save at 4,500 continuing hash-equal to the end.
/// </summary>
[Trait("Category", "Stress")]
public class ForestStealthFuzzQaStressTests
{
    private readonly ITestOutputHelper _out;

    public ForestStealthFuzzQaStressTests(ITestOutputHelper output) => _out = output;

    internal static GameData Data => s_data.Value;

    private static readonly Lazy<GameData> s_data = new(() => AiScenes.DataWith(dir =>
    {
        dir.EditJson("factions/malazan/faction.json", r => r["forestStealth"] = ForestScenes.Block(3));
        dir.EditJson("factions/whirlwind/faction.json", r => r["forestStealth"] = ForestScenes.Block(3));
        StealthTests.AddTo(dir);
    }));

    private static readonly string[][] Rosters =
    {
        new[] { "malazan_heavy_infantry", "malazan_crossbowman", "malazan_laborer", "malazan_catapult", "malazan_cadre_mage", "malazan_heavy_infantry" },
        new[] { "whirlwind_raider", "whirlwind_desert_archer", "whirlwind_priest", "whirlwind_camp_follower", "whirlwind_raider" },
        new[] { "teblor_warrior", "teblor_javelin_thrower", "teblor_gatherer", "teblor_warrior" },
    };

    private static List<int> Edge(World w)
    {
        NavGrid g = w.NavGrid;
        var edge = new List<int>();
        for (int c = 0; c < g.Width * g.Height; c++)
            if (g.IsPassable(c % g.Width, c / g.Width) && ForestStealthSystem.BesideTree(w, g.CellCenter(c % g.Width, c / g.Width))) edge.Add(c);
        return edge;
    }

    private static Simulation NewFuzz(ulong seed)
    {
        var sim = new Simulation(new SimConfig(seed, 3, 160, 2048) { Data = Data, Map = MapGenParams.Default with { Forests = 14, GoldMines = 4 } });
        World w = sim.World;
        NavGrid g = w.NavGrid;
        List<int> edge = Edge(w);
        Assert.True(edge.Count > 100, $"seed {seed}: {edge.Count} edge cells");
        var rng = new SimRng(seed, 8101);
        int anchor = edge[rng.NextInt(0, edge.Count)];
        List<int> near = edge.OrderBy(c => Dist2(g, anchor, c)).ThenBy(c => c).Take(90).ToList();
        for (int p = 0; p < 3; p++)
        {
            int hall = StartBase.BuildingOfSlot(Data, w.FactionOf(p), BuildingSlot.TownHall);
            int hc = near[rng.NextInt(0, near.Count)];
            sim.Enqueue(Command.SpawnBuilding(p, hall, g.CellCenter(hc % g.Width, hc / g.Width) + new Vector2(8f * (p - 1), 10f)));
            for (int k = 0; k < 14; k++)
            {
                int c = near[rng.NextInt(0, near.Count)];
                sim.Enqueue(Command.SpawnUnit(p, Data.FindUnit(Rosters[p][k % Rosters[p].Length]), g.CellCenter(c % g.Width, c / g.Width)));
            }
        }
        return sim;
    }

    private static int Dist2(NavGrid g, int a, int c) => (c % g.Width - a % g.Width) * (c % g.Width - a % g.Width) + (c / g.Width - a / g.Width) * (c / g.Width - a / g.Width);

    /// <summary>The seeded orders: every 37 ticks a random third of the units get one order each.</summary>
    private static void Orders(Simulation sim, ref SimRng rng)
    {
        int t = sim.TickNumber;
        World w = sim.World;
        UnitStore u = w.Units;
        NavGrid g = w.NavGrid;
        if (t % 400 == 200)
        {
            // Reinforcements so the fight goes on.
            for (int p = 0; p < 3; p++)
                for (int k = 0; k < 4; k++)
                {
                    int x = rng.NextInt(0, g.Width), y = rng.NextInt(0, g.Height);
                    if (g.IsPassable(x, y)) sim.Enqueue(Command.SpawnUnit(p, Data.FindUnit(Rosters[p][rng.NextInt(0, Rosters[p].Length)]), g.CellCenter(x, y)));
                }
        }
        if (t % 37 != 5) return;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || rng.NextInt(0, 3) != 0) continue;
            var h = new EntityHandle(i, u.Generation[i]);
            int owner = u.Owner[i];
            Vector2 enemy = u.Position[i];
            EntityHandle enemyHandle = default;
            for (int tries = 0; tries < 8; tries++)
            {
                int j = rng.NextInt(0, u.Capacity);
                if (u.Alive[j] && u.Owner[j] != owner) { enemy = u.Position[j]; enemyHandle = new EntityHandle(j, u.Generation[j]); break; }
            }
            g.WorldToCell(u.Position[i], out int cx, out int cy);
            Vector2 edge = u.Position[i];
            for (int tries = 0; tries < 12; tries++)
            {
                int x = cx + rng.NextInt(-8, 9), y = cy + rng.NextInt(-8, 9);
                if (g.InBounds(x, y) && g.IsPassable(x, y) && ForestStealthSystem.BesideTree(w, g.CellCenter(x, y))) { edge = g.CellCenter(x, y); break; }
            }
            switch (rng.NextInt(0, 10))
            {
                case 0: case 1: sim.Enqueue(Command.AttackMove(owner, h, enemy)); break;
                case 2: case 3: sim.Enqueue(Command.Move(owner, h, edge)); break;
                case 4:
                    // A gather at a tree within 6 cells (only workers take it; others refuse): own workers fell the trees units hide by.
                    for (int r = 1, found = 0; r <= 6 && found == 0; r++)
                        for (int dx = -r; dx <= r && found == 0; dx++)
                            for (int dy = -r; dy <= r && found == 0; dy++)
                                if (g.InBounds(cx + dx, cy + dy) && w.Resources.IsWoodCell((cy + dy) * g.Width + cx + dx))
                                {
                                    sim.Enqueue(Command.Gather(owner, h, g.CellCenter(cx + dx, cy + dy)));
                                    found = 1;
                                }
                    break;
                case 5: sim.Enqueue(Command.UseAbility(owner, h, 0, enemy)); break; // the test stealth, Sandstorm, the Mage's, or a refusal
                case 6: sim.Enqueue(Command.Stop(owner, h)); break;
                case 7: if (enemyHandle.Generation != 0) sim.Enqueue(Command.Attack(owner, h, enemyHandle, isBuilding: false)); break;
                case 8:
                    for (int k = 0; k < 4; k++) sim.Enqueue(Command.Move(owner, h, edge)); // the same order spammed
                    break;
                default:
                    sim.Enqueue(Command.Move(owner, new EntityHandle(i, u.Generation[i] + 1), edge)); // a stale handle
                    sim.Enqueue(Command.HoldPosition(owner, h));
                    break;
            }
        }
    }

    /// <summary>What phase 5 decided for unit <paramref name="i"/>, re-derived: null when this tick's own attack or hp loss hides the timers phase 5 read.</summary>
    private static bool? ExpectedHeld(World w, int i)
    {
        UnitStore u = w.Units;
        FactionDef f = w.Data.Factions[w.FactionOf(u.Owner[i])];
        if (!f.HasForestStealth) return false;
        if ((f.ForestStealthExceptSlots & (1 << (int)w.Data.Units[u.TypeId[i]].Slot)) != 0 || u.IsSummoned(i)) return false;
        int now = w.TickNumber - 1, delay = f.ForestStealthDelayTicks;
        int attacked = u.Forest!.LastAttackTick[i], hurt = u.LastDamagedTick[i];
        if (hurt == now) return false; // an hp loss after phase 5 let go at once
        if (attacked == now) return null; // landed after phase 5: held or not, by what phase 5 saw before it
        if (attacked >= 0 && now - attacked < delay) return false;
        if (hurt >= 0 && now - hurt < delay) return false;
        return ForestStealthSystem.BesideTree(w, u.PrevPosition[i]);
    }

    private sealed class Tally
    {
        public long Held, Mismatch, FullStore, HiddenPairs, Reveals, Felled, NewHiddenTargets;
    }

    private static void CheckTick(Simulation sim, Tally tally, EntityHandle[] prevTarget, int[] hiddenTicks, string at)
    {
        World w = sim.World;
        UnitStore u = w.Units;
        StatusStore s = u.Statuses;
        int carriers = 0, revealed = w.Data.FindStatus("revealed");
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i])
            {
                Assert.True(u.Forest!.Held[i] == 0 && u.Forest.LastAttackTick[i] == -1, $"{at}: free slot {i} keeps forest state");
                continue;
            }
            Assert.True(float.IsFinite(u.Position[i].X) && float.IsFinite(u.Position[i].Y), $"{at}: unit {i} at {u.Position[i]}");
            Assert.True(u.Hp[i] > 0 && u.Hp[i] <= w.MaxHpOf(u.Owner[i], u.TypeId[i]), $"{at}: unit {i} hp {u.Hp[i]}");
            bool has = false;
            for (int k = 0; k < s.Count[i]; k++)
                has |= w.Data.Statuses[s.StatusId[i * StatusStore.PerUnit + k]].Kind == StatusKind.Stealthed;
            Assert.True(has == s.HasStealth(i), $"{at}: unit {i} stealth flag");
            if (has) carriers++;
            FactionDef f = w.Data.Factions[w.FactionOf(u.Owner[i])];
            if (!f.HasForestStealth) Assert.True(u.Forest!.LastAttackTick[i] == -1, $"{at}: unit {i} of a seat without the bonus has a last attack");
            bool held = u.Forest!.Held[i] != 0;
            if (held)
            {
                tally.Held++;
                int e = s.IndexOf(i, f.ForestStealthStatus);
                Assert.True(e >= 0 && s.TicksRemaining[e] >= 1, $"{at}: unit {i} held without its entry");
            }
            bool? expect = ExpectedHeld(w, i);
            if (expect == null || expect == held) continue;
            if (expect == true && s.Count[i] >= StatusStore.PerUnit && s.IndexOf(i, f.ForestStealthStatus) < 0)
            {
                tally.FullStore++;
                continue;
            }
            tally.Mismatch++;
            Assert.Fail($"{at}: unit {i} ({w.Data.Units[u.TypeId[i]].Key}, player {u.Owner[i]}) held {held}, the rule says {expect}; last attack {u.Forest.LastAttackTick[i]}, last hurt {u.LastDamagedTick[i]}, beside {ForestStealthSystem.BesideTree(w, u.PrevPosition[i])} / now {ForestStealthSystem.BesideTree(w, u.Position[i])}, statuses {s.Count[i]}");
        }
        Assert.True(carriers == s.StealthedUnits, $"{at}: stealth count {s.StealthedUnits}, recount {carriers}");
        for (int j = 0; j < u.Capacity; j++)
        {
            if (!u.Alive[j]) continue;
            int r = s.IndexOf(j, revealed);
            if (r >= 0 && s.TicksRemaining[r] == w.Data.Rules.RevealTicks) tally.Reveals++;
            for (int p = 0; p < w.Config.PlayerCount; p++)
                if (w.HiddenFrom(p, j))
                {
                    tally.HiddenPairs++;
                    Assert.False(w.Fog.CanSeeUnit(p, j), $"{at}: player {p} sees undetected stealthed {j}");
                }
        }
        for (int i = 0; i < u.Capacity; i++)
        {
            EntityHandle target = u.Alive[i] && !u.TargetIsBuilding[i] ? u.Target[i] : default;
            bool live = target.Generation != 0 && u.IsAlive(target);
            bool hidden = live && w.HiddenFrom(u.Owner[i], target.Index);
            // A new target hidden after the tick: legitimate only from an explicit Attack applied in phase 1 while the target was
            // seen, then hidden by phase 5 (the fuzz gives some); the scans (phase 7) never take one. Counted, and the keep rule
            // below bounds it either way.
            if (live && target != prevTarget[i] && hidden) tally.NewHiddenTargets++;
            hiddenTicks[i] = hidden ? hiddenTicks[i] + 1 : 0;
            Assert.True(hiddenTicks[i] <= 2 * CombatConstants.ScanInterval, $"{at}: unit {i} keeps hidden {target.Index} for {hiddenTicks[i]} ticks");
            prevTarget[i] = live ? target : default;
        }
        for (int p = 0; p < w.Config.PlayerCount; p++)
            Assert.True(w.Gold[p] >= 0 && w.Wood[p] >= 0, $"{at}: player {p} resources");
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    [InlineData(5UL)]
    [InlineData(8UL)]
    [InlineData(13UL)]
    public void ThreePlayers_TwoForestFactions_SeededOrders_TwinsEqual_TheHoldIsTheRule_ASaveContinues(ulong seed)
    {
        const int ticks = 2000, saveAt = 1100;
        Simulation a = NewFuzz(seed), b = NewFuzz(seed);
        Simulation? c = null;
        var rngA = new SimRng(seed, 8102);
        var rngB = new SimRng(seed, 8102);
        var rngC = rngA;
        UnitStore u = a.World.Units;
        var tally = new Tally();
        var prevTarget = new EntityHandle[u.Capacity];
        var hiddenTicks = new int[u.Capacity];
        int treesAtStart = Trees(a.World);
        for (int t = 0; t < ticks; t++)
        {
            if (t == saveAt)
            {
                c = SaveTestRun.Load(Data, SaveTestRun.Save(a));
                rngC = rngA;
                Assert.Equal(a.StateHash(), c.StateHash());
            }
            Orders(a, ref rngA);
            Orders(b, ref rngB);
            if (c != null) Orders(c, ref rngC);
            a.Tick();
            b.Tick();
            c?.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"seed {seed}: twins differ after tick {t}");
            if (c != null) Assert.True(a.StateHash() == c.StateHash(), $"seed {seed}: the run loaded at {saveAt} differs after tick {t}");
            CheckTick(a, tally, prevTarget, hiddenTicks, $"seed {seed} tick {t}");
        }
        tally.Felled = treesAtStart - Trees(a.World);
        _out.WriteLine($"seed {seed}: held unit-ticks {tally.Held}, full-store skips {tally.FullStore}, hidden (unit, player) ticks {tally.HiddenPairs}, reveals {tally.Reveals}, trees felled {tally.Felled}, new targets hidden after their tick {tally.NewHiddenTargets}, units left {u.Count}, kills {a.World.Kills[0]} / {a.World.Kills[1]} / {a.World.Kills[2]}");
        Assert.True(tally.Held > 2000, $"seed {seed}: only {tally.Held} held unit-ticks");
        Assert.True(tally.HiddenPairs > 0);
        Assert.Equal(0, tally.Mismatch);
    }

    private static int Trees(World w)
    {
        int n = 0;
        for (int i = 0; i < w.Resources.Capacity; i++)
            if (w.Resources.Alive[i] && w.Data.Resources[w.Resources.TypeId[i]].Resource == ResourceKind.Wood) n++;
        return n;
    }

    // ---------- four AI seats, 9,000 ticks ----------

    [Theory]
    [InlineData(4UL)]
    public void FourAiSeats_TwoForestFactions_9000Ticks_TwiceHashEqual_NoLeak_OwnHiddenUnitsObey_ASaveContinues(ulong seed)
    {
        const int ticks = 9000, saveAt = 4500;
        GameData d = Data;
        PlayerSeat[] seats =
        {
            new(d.FindFaction("malazan"), SeatController.Ai, AiDifficulty.Hard),
            new(d.FindFaction("whirlwind"), SeatController.Ai, AiDifficulty.Normal),
            new(d.FindFaction("teblor"), SeatController.Ai, AiDifficulty.Hard),
            new(d.FindFaction("whirlwind"), SeatController.Ai, AiDifficulty.Hard),
        };
        Simulation a = AiScenes.Generated(seats, seed, record: false, out _, units: 600, data: d, forests: 12);
        Simulation b = AiScenes.Generated(seats, seed, record: false, out _, units: 600, data: d, forests: 12);
        Simulation? c = null;
        World w = a.World;
        UnitStore u = w.Units;
        var oracle = new PlayerViewOracle.Tally();
        var heldTicks = new long[4];
        var heldMoving = new long[4];
        long ownHeldNotInView = 0;
        var tally = new Tally();
        var prevTarget = new EntityHandle[u.Capacity];
        var hiddenTicks = new int[u.Capacity];
        int treesAtStart = Trees(w);
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
            Assert.True(a.StateHash() == b.StateHash(), $"seed {seed}: twins differ after tick {t}");
            if (c != null) Assert.True(a.StateHash() == c.StateHash(), $"seed {seed}: the run loaded at {saveAt} differs after tick {t}");
            CheckTick(a, tally, prevTarget, hiddenTicks, $"seed {seed} tick {t}");
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i] || u.Forest!.Held[i] == 0) continue;
                int p = u.Owner[i];
                heldTicks[p]++;
                if (u.Position[i] != u.PrevPosition[i]) heldMoving[p]++;
                if (!a.AiOf(p)!.View.TryGetUnit(i, out _)) ownHeldNotInView++;
            }
            // The oracle at every fog update (ticks 1, 5, 9, ...; its node memory must see each one), for the seats still in.
            if (t == 0 || t % 4 == 1)
                for (int p = 0; p < 4; p++)
                    if (!w.IsDefeated(p)) PlayerViewOracle.Check(w, a.AiOf(p)!.View, p, oracle, $"seed {seed} tick {t} player {p}");
            Assert.True(oracle.First.Length == 0, oracle.First + "\n" + oracle);
        }
        _out.WriteLine($"seed {seed}: held unit-ticks by seat {string.Join(" / ", heldTicks)}, of them moving {string.Join(" / ", heldMoving)}; full-store skips {tally.FullStore}; hidden (unit, player) ticks {tally.HiddenPairs}; reveals {tally.Reveals}; trees felled {treesAtStart - Trees(w)}; defeated {string.Join(" ", Enumerable.Range(0, 4).Select(w.IsDefeated))}; over {a.MatchOver}");
        _out.WriteLine($"oracle: {oracle}");
        Assert.Equal(0, ownHeldNotInView);
        Assert.Equal(0, oracle.Leaks);
        Assert.Equal(0, oracle.Misses);
        Assert.Equal(0, heldTicks[2]);
        Assert.True(heldTicks[0] + heldTicks[1] + heldTicks[3] > 1000, "the forest seats barely hid");
        Assert.True(heldMoving[0] + heldMoving[1] + heldMoving[3] > 0, "no held unit ever moved under its orders");
        Assert.True(oracle.StealthHidden > 0, "no seat ever had an enemy hidden from it by stealth");
    }
}
