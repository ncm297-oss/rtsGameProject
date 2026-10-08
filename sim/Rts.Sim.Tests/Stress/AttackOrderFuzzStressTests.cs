using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Replays;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA M4-2a (session 2026-10-08-0313): an Attack-heavy fuzz on generated maps. A 60 v 60 map brawl (recorded from tick 0)
/// plus Tents for both players, then 2,500 ticks of random Attack orders (live, stale, own, bogus index, unit or building,
/// queued or not, spam on the same target) mixed with Move / AttackMove / Stop / Hold. Twins hash equal every tick; after
/// every tick: positions finite, hit points in (0, max], an <see cref="CombatMode.Ordered"/> unit has no anchor, never
/// targets its own side or a kind its <c>attack.targets</c> forbids, and an Ordered unit with no target settles on the
/// next tick. The recording round-trips through format 4 and plays to every checkpoint.
/// </summary>
[Collection(SerialCollection.Name)]
public class AttackOrderFuzzStressTests
{
    private readonly ITestOutputHelper _out;

    public AttackOrderFuzzStressTests(ITestOutputHelper output) => _out = output;

    private const int PerSide = 60, Ticks = 2500;

    private static Simulation NewSim(ulong seed, out ReplayRecorder? recorder, bool record)
    {
        ReplayRecorder? rec = null;
        Simulation sim = CombatScenes.MapBrawl(seed, PerSide, s => { if (record) rec = new ReplayRecorder(s, 50); });
        recorder = rec;
        return sim;
    }

    private static void Both(Simulation a, Simulation b, Command c)
    {
        a.Enqueue(c);
        b.Enqueue(c);
    }

    [Theory]
    [InlineData(11UL)]
    [InlineData(12UL)]
    [InlineData(13UL)]
    [InlineData(14UL)]
    public void AttackHeavyFuzz_TwinsMatch_InvariantsHold_ReplayPlays(ulong seed)
    {
        Simulation a = NewSim(seed, out ReplayRecorder? rec, record: true);
        Simulation b = NewSim(seed, out _, record: false);
        World w = a.World;
        UnitStore u = w.Units;
        NavGrid g = w.NavGrid;
        var rng = new SimRng(seed, 433);
        int tent = TestSim.Data.FindBuilding("whirlwind_tent");
        // A few Tents for each side (any free spot).
        for (int placed = 0, tries = 0; placed < 6 && tries < 2000; tries++)
        {
            int c = rng.NextInt(0, g.Width * g.Height);
            if (!w.Buildings.Fits(tent, c)) continue;
            Both(a, b, Command.SpawnBuilding(placed % 2, tent, g.CellCenter(c % g.Width, c / g.Width)));
            placed++;
        }
        var orderedNoTarget = new bool[u.Capacity];
        int attackOrders = 0, orderedTicks = 0, maxOrderedRun = 0;
        var orderedRun = new int[u.Capacity];
        for (int t = 0; t < Ticks; t++)
        {
            int n = rng.NextInt(0, 6);
            for (int k = 0; k < n; k++)
            {
                int i = rng.NextInt(0, u.Capacity);
                if (!u.Alive[i]) continue;
                var h = new EntityHandle(i, u.Generation[i]);
                int p = u.Owner[i];
                bool queued = rng.NextInt(0, 3) == 0;
                int roll = rng.NextInt(0, 100);
                Command c;
                if (roll < 60)
                {
                    c = AttackOn(ref rng, w, p, h, queued);
                    attackOrders++;
                }
                else
                {
                    Vector2 to = g.CellCenter(rng.NextInt(1, g.Width - 1), rng.NextInt(1, g.Height - 1));
                    c = roll < 75 ? Command.AttackMove(p, h, to, queued) : roll < 88 ? Command.Move(p, h, to, queued)
                        : roll < 94 ? Command.Stop(p, h, queued) : Command.HoldPosition(p, h, queued);
                }
                Both(a, b, c);
                // Spam: the same command again on the next ticks now and then.
                if (rng.NextInt(0, 10) == 0) Both(a, b, c);
            }
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"seed {seed} tick {a.TickNumber}: twins differ");
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i]) { orderedRun[i] = 0; orderedNoTarget[i] = false; continue; }
                UnitDef def = w.Data.Units[u.TypeId[i]];
                Vector2 pos = u.Position[i];
                Assert.True(float.IsFinite(pos.X) && float.IsFinite(pos.Y), $"seed {seed} tick {a.TickNumber}: unit {i} at {pos}");
                Assert.True(u.Hp[i] > 0, $"seed {seed} tick {a.TickNumber}: unit {i} alive with hp {u.Hp[i]}");
                if (u.Mode[i] != CombatMode.Ordered)
                {
                    orderedRun[i] = 0;
                    orderedNoTarget[i] = false;
                    continue;
                }
                Assert.True(u.AnchorPosition[i] == Vector2.Zero, $"seed {seed} tick {a.TickNumber}: Ordered unit {i} has an anchor");
                if (u.Target[i].Generation == 0)
                {
                    Assert.False(orderedNoTarget[i], $"seed {seed} tick {a.TickNumber}: Ordered unit {i} still Ordered with no target a tick later");
                    orderedNoTarget[i] = true;
                    continue;
                }
                orderedNoTarget[i] = false;
                orderedTicks++;
                maxOrderedRun = Math.Max(maxOrderedRun, ++orderedRun[i]);
                EntityHandle tg = u.Target[i];
                if (u.TargetIsBuilding[i])
                {
                    Assert.True(w.Buildings.IsAlive(tg), $"seed {seed} tick {a.TickNumber}: unit {i} targets a dead building");
                    Assert.NotEqual(u.Owner[i], w.Buildings.Owner[tg.Index]);
                    Assert.NotEqual(AttackTargets.Units, def.Attack.Targets);
                }
                else
                {
                    Assert.True(u.IsAlive(tg), $"seed {seed} tick {a.TickNumber}: unit {i} targets a dead unit");
                    Assert.NotEqual(u.Owner[i], u.Owner[tg.Index]);
                    Assert.NotEqual(AttackTargets.Buildings, def.Attack.Targets);
                }
                Assert.True(CombatSystem.CanFight(def), $"unit {i} ({def.Key}) Ordered but can't fight");
            }
        }
        _out.WriteLine($"seed {seed}: {attackOrders} Attack orders, {orderedTicks} unit-ticks Ordered with a target, longest Ordered engagement {maxOrderedRun} ticks; kills {w.Kills[0]}/{w.Kills[1]}, units {u.Count}");
        Assert.True(orderedTicks > 0, "no Attack order was ever taken");
        Replay replay = rec!.ToReplay();
        Assert.Equal(4, replay.FormatVersion);
        Assert.True(replay.Combat);
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(ReplayFormat.Write(replay), out Replay? back));
        Assert.Contains(back!.Commands, c => c.Kind == CommandKind.Attack && c.TargetIsBuilding);
        Assert.Contains(back.Commands, c => c.Kind == CommandKind.Attack && c.IsQueued);
        Assert.Contains(back.Commands, c => c.Kind == CommandKind.Attack && c.Target.Index < 0);
        ReplayResult result = ReplayPlayer.Run(back, TestSim.Data);
        Assert.True(result.Ok, $"seed {seed}: replay {result.Error} at tick {result.Tick}");
    }

    /// <summary>An Attack on a random unit or building of either side: live mostly, one in six stale, one in ten a bogus index (negative or past capacity).</summary>
    private static Command AttackOn(ref SimRng rng, World w, int p, EntityHandle h, bool queued)
    {
        bool building = rng.NextInt(0, 4) == 0;
        int capacity = building ? w.Buildings.Capacity : w.Units.Capacity;
        int start = rng.NextInt(0, capacity);
        EntityHandle target = default;
        for (int n = 0; n < capacity; n++)
        {
            int k = (start + n) % capacity;
            bool alive = building ? w.Buildings.Alive[k] : w.Units.Alive[k];
            if (!alive) continue;
            target = building ? w.Buildings.HandleOf(k) : new EntityHandle(k, w.Units.Generation[k]);
            if (rng.NextInt(0, 3) != 0) break; // usually the first live one, sometimes a later one
        }
        int roll = rng.NextInt(0, 30);
        if (roll < 5) target = new EntityHandle(target.Index, target.Generation + 1);
        else if (roll < 7) target = new EntityHandle(roll == 5 ? -1 : int.MaxValue, 1);
        else if (roll == 7) target = new EntityHandle(int.MinValue, int.MinValue);
        return Command.Attack(p, h, target, building, queued);
    }
}
