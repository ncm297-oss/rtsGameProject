using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Replays;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA M4-1 determinism and invariants under real fighting: eight seeds of the map brawl run as twins with a hostile
/// order stream on top (orders to dead handles, Stop / Hold / Move / AttackMove spam), checked after every tick, then
/// recorded and played back.
/// </summary>
public class CombatDeterminismQaTests
{
    public static IEnumerable<object[]> Seeds() => Enumerable.Range(1, 8).Select(s => new object[] { (ulong)s });

    /// <summary>
    /// Every tick: twins hash-equal; every live unit finite, on the map, on passable ground, hp in (0, max]; no live
    /// target or last attacker is a dead handle; a unit Attacking at the end of two ticks running did not move; a
    /// holder that was holding at the end of both ticks did not move; pop equals its recount; kills and losses balance,
    /// and grow by exactly this tick's death events, whose victims are all stale.
    /// </summary>
    [Theory]
    [MemberData(nameof(Seeds))]
    public void Brawl8Seeds_HostileOrders_TwinsAndInvariantsEveryTick_ThenReplayPlaysBack(ulong seed)
    {
        ReplayRecorder? recorder = null;
        Simulation a = CombatScenes.MapBrawl(seed, 120, s => recorder = new ReplayRecorder(s, checkpointInterval: 25));
        Simulation b = CombatScenes.MapBrawl(seed, 120);
        UnitStore u = a.World.Units;
        NavGrid g = a.World.NavGrid;
        var rng = new SimRng(seed, 404);
        var prevPos = new Vector2[u.Capacity];
        var prevAttacking = new bool[u.Capacity];
        var prevHold = new bool[u.Capacity];
        var prevGen = new int[u.Capacity];
        var deadHandles = new List<EntityHandle>();
        var orderedNow = new bool[u.Capacity];
        var orderedBefore = new bool[u.Capacity];
        int lastKills = 0, totalDeaths = 0;
        for (int t = 0; t < 1200; t++)
        {
            // Hostile orders, same stream to both twins.
            // Orders enqueued now apply on the tick after next: a unit ordered in either of the last two batches is exempt
            // from the two-tick still-standing checks below (an order may walk it and combat plant it again).
            Array.Copy(orderedNow, orderedBefore, u.Capacity);
            Array.Clear(orderedNow);
            if (t % 3 == 0)
            {
                for (int n = 0; n < 6; n++)
                {
                    EntityHandle h;
                    if (deadHandles.Count > 0 && rng.NextInt(0, 4) == 0) h = deadHandles[rng.NextInt(0, deadHandles.Count)];
                    else
                    {
                        int i = rng.NextInt(0, u.Capacity);
                        h = new EntityHandle(i, u.Generation[i]);
                    }
                    int p = rng.NextInt(0, 2);
                    Vector2 at = new(rng.NextFloat() * g.Width * MapConstants.CellSize, rng.NextFloat() * g.Height * MapConstants.CellSize);
                    Command c = rng.NextInt(0, 6) switch
                    {
                        0 => Command.Stop(p, h),
                        1 => Command.HoldPosition(p, h),
                        2 => Command.Move(p, h, at, rng.NextInt(0, 3) == 0),
                        _ => Command.AttackMove(p, h, at, rng.NextInt(0, 3) == 0),
                    };
                    a.Enqueue(c);
                    b.Enqueue(c);
                    if ((uint)h.Index < (uint)u.Capacity) orderedNow[h.Index] = true;
                }
            }
            for (int i = 0; i < u.Capacity; i++)
            {
                prevPos[i] = u.Position[i];
                prevAttacking[i] = u.Alive[i] && u.State[i] == UnitState.Attacking;
                prevHold[i] = u.Alive[i] && u.Hold[i];
                prevGen[i] = u.Generation[i];
            }
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"seed {seed}: twins differ after tick {a.TickNumber - 1}");

            World w = a.World;
            foreach (DeathEvent d in w.Deaths)
            {
                if (d.IsBuilding) continue;
                Assert.False(u.IsAlive(d.Victim), $"seed {seed} tick {t}: death event for a live handle");
                deadHandles.Add(d.Victim);
            }
            totalDeaths += w.Deaths.Length;
            int kills = w.Kills[0] + w.Kills[1];
            Assert.Equal(lastKills + w.Deaths.Length, kills);
            Assert.Equal(kills, w.Losses[0] + w.Losses[1]);
            lastKills = kills;
            for (int p = 0; p < 2; p++) Assert.Equal(ProductionMaps.RecountHalfPop(w, p), w.HalfPop[p]);

            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i]) continue;
                Vector2 pos = u.Position[i];
                Assert.True(float.IsFinite(pos.X) && float.IsFinite(pos.Y), $"seed {seed} tick {t}: unit {i} at {pos}");
                Assert.True(g.WorldToCell(pos, out int cx, out int cy) && g.IsPassable(cx, cy), $"seed {seed} tick {t}: unit {i} at {pos} off the map or on blocked ground");
                int max = w.Data.Units[u.TypeId[i]].Hp;
                Assert.True(u.Hp[i] > 0 && u.Hp[i] <= max, $"seed {seed} tick {t}: unit {i} hp {u.Hp[i]} / {max}");
                if (u.Target[i].Generation != 0)
                {
                    bool alive = u.TargetIsBuilding[i] ? w.Buildings.IsAlive(u.Target[i]) : u.IsAlive(u.Target[i]);
                    Assert.True(alive, $"seed {seed} tick {t}: unit {i} targets dead {u.Target[i]}");
                    if (!u.TargetIsBuilding[i]) Assert.NotEqual(u.Owner[i], u.Owner[u.Target[i].Index]);
                }
                if (u.LastAttacker[i].Generation != 0)
                {
                    Assert.True(u.IsAlive(u.LastAttacker[i]), $"seed {seed} tick {t}: unit {i}'s last attacker {u.LastAttacker[i]} is dead");
                    Assert.NotEqual(u.Owner[i], u.Owner[u.LastAttacker[i].Index]);
                }
                if (prevGen[i] != u.Generation[i] || orderedBefore[i] || orderedNow[i]) continue; // a new unit in the slot, or one just ordered
                if (prevAttacking[i] && u.State[i] == UnitState.Attacking)
                    Assert.True(pos == prevPos[i], $"seed {seed} tick {t}: unit {i} moved while Attacking {prevPos[i]} -> {pos}");
                if (prevHold[i] && u.Hold[i])
                    Assert.True(pos == prevPos[i], $"seed {seed} tick {t}: holder {i} moved {prevPos[i]} -> {pos}");
            }
        }
        Assert.True(totalDeaths >= 40, $"seed {seed}: only {totalDeaths} deaths, not a brawl");

        Replay recorded = recorder!.ToReplay();
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(ReplayFormat.Write(recorded), out Replay? parsed));
        ReplayResult played = ReplayPlayer.Run(parsed!, TestSim.Data);
        Assert.True(played.Ok, $"seed {seed}: {played}");
        Assert.Equal(recorded.TickCount, played.TicksRun);
    }

    /// <summary>
    /// Stale target, slot reused between ticks: A fights B; B is freed and its slot (same index, next generation) is taken
    /// at once by a unit of A's own side, then (second run) by an enemy. A never hits the friendly, drops the stale
    /// handle on the next tick, and only takes the new enemy through a fresh scan (its target handle carries the new
    /// generation).
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TargetFreed_AndItsSlotReusedBetweenTicks_ByAFriendlyOrAnEnemy_NeverHitThroughTheOldHandle(bool reusedByEnemy)
    {
        Simulation sim = CombatScenes.Flat(units: 4);
        EntityHandle a = CombatScenes.Place(sim, 0, CombatScenes.HeavyInfantry, CombatScenes.At(sim, 20, 20));
        EntityHandle b = CombatScenes.Place(sim, 1, CombatScenes.HeavyInfantry, CombatScenes.At(sim, 20, 20, dx: 1f));
        UnitStore u = sim.World.Units;
        CombatScenes.RunUntil(sim, () => u.Target[a.Index] == b && u.WindupTicks[a.Index] > 0, 40);
        Assert.Equal(b, u.Target[a.Index]);
        Assert.True(u.WindupTicks[a.Index] > 0); // mid-swing
        Vector2 at = u.Position[b.Index];
        u.Free(b);
        EntityHandle c = CombatScenes.Place(sim, reusedByEnemy ? 1 : 0, CombatScenes.Laborer, at);
        Assert.Equal(b.Index, c.Index);
        Assert.NotEqual(b.Generation, c.Generation);
        int full = u.Hp[c.Index];
        sim.Tick();
        Assert.NotEqual(b, u.Target[a.Index]);
        Assert.Equal(full, u.Hp[c.Index]); // the swing at b did not land on c
        for (int t = 0; t < 200; t++)
        {
            sim.Tick();
            if (!reusedByEnemy)
            {
                Assert.Equal(full, u.Hp[c.Index]);
                Assert.Equal(default, u.Target[a.Index]);
            }
        }
        if (reusedByEnemy) Assert.True(!u.IsAlive(c) || u.Hp[c.Index] < full, "the new enemy in the slot was never fought");
        Assert.True(u.IsAlive(a));
        if (!reusedByEnemy) Assert.Equal(sim.World.Data.Units[CombatScenes.HeavyInfantry].Hp, u.Hp[a.Index]); // nothing ever hit a
    }
}
