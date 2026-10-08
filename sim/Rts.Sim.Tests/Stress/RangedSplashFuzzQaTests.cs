using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;
using Rts.Sim.Replays;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA M4-2b (session 2026-10-08-0913): a shooter-heavy brawl on a generated map with both players' buildings in the
/// fight, more shooters than the default projectile store holds (so shots are lost), Attack orders on units and
/// buildings, live and stale. 6 seeds x 3,000 ticks: twins equal after every tick, then the replay plays back. Every
/// tick: units and buildings within (0, max] hp; the store's count matches its live slots and its capacity; every
/// projectile finite and between its launch side and its impact point; landings never exceed the capacity; a unit's
/// last attacker is alive and an enemy, or none; kills and losses balance.
/// </summary>
public class RangedSplashFuzzQaTests
{
    private const int Ticks = 3000;
    private readonly ITestOutputHelper _out;

    public RangedSplashFuzzQaTests(ITestOutputHelper output) => _out = output;

    public static IEnumerable<object[]> Seeds() => Enumerable.Range(11, 6).Select(s => new object[] { (ulong)s });

    /// <summary>Shooter-heavy rosters: every unit of the faction with a projectile, twice, then the rest once.</summary>
    private static int[] Roster(int faction)
    {
        var list = new List<int>();
        foreach (UnitDef d in TestSim.Data.Units.Where(d => d.Faction == faction))
        {
            list.Add(d.Id);
            if (d.Attack.ProjectileTypeId >= 0) { list.Add(d.Id); list.Add(d.Id); }
        }
        return list.ToArray();
    }

    private static int[] BuildingsOf(int faction) =>
        TestSim.Data.Buildings.Where(b => b.Faction == faction && b.FootprintWidth <= 2).Select(b => b.Id).ToArray();

    [Theory]
    [MemberData(nameof(Seeds))]
    public void ShooterHeavyBrawl_WithBuildings_AndAnOverflowingStore_TwinsEqual_InvariantsHold_ReplayPlaysBack(ulong seed)
    {
        SimConfig config = TestSim.Config(Seed: seed, PlayerCount: 2, UnitCapacity: 400, CommandCapacity: 1024);
        var a = new Simulation(config);
        var recorder = new ReplayRecorder(a, checkpointInterval: 50);
        var b = new Simulation(config);
        World w = a.World;
        UnitStore u = w.Units;
        BuildingStore bs = w.Buildings;
        NavGrid g = w.NavGrid;
        int center = MoveScenario.CentralCell(g);
        FlowField field = FlowField.Build(g, center);
        var near = new List<int>();
        for (int c = 0; c < g.Width * g.Height; c++)
            if (field.CostAt(c) <= 16f) near.Add(c);
        int[][] rosters = { Roster(0), Roster(1) };
        int[][] buildings = { BuildingsOf(0), BuildingsOf(1) };
        int capacity = w.Projectiles.Capacity;
        var rng = new SimRng(seed, 4242);
        long deaths = 0;
        int hits = 0, misses = 0, maxInFlight = 0, friendly = 0, buildingDeaths = 0, peakUnits = 0;
        // Round 1 (BUG-0183): shots re-led in flight (same shot, impact point moved) and the longest step a led shot takes.
        var lastAim = new Vector2[capacity];
        var lastLeft = new int[capacity];
        int steers = 0;
        float longestStep = 0f;
        for (int t = 0; t < Ticks; t++)
        {
            if (t % 100 == 0 && u.Count < 340)
            {
                for (int n = 0; n < 40; n++)
                {
                    int p = n % 2;
                    int cell = near[rng.NextInt(0, near.Count)];
                    Vector2 corner = MoveScenario.Center(g, cell) - new Vector2(MapConstants.CellSize / 2);
                    Vector2 at = corner + new Vector2(0.1f + rng.NextFloat() * 1.8f, 0.1f + rng.NextFloat() * 1.8f);
                    Both(a, b, Command.SpawnUnit(p, rosters[p][rng.NextInt(0, rosters[p].Length)], at));
                }
            }
            if (t % 250 == 0)
            {
                for (int n = 0; n < 4; n++)
                {
                    int p = n % 2;
                    Vector2 at = MoveScenario.Center(g, near[rng.NextInt(0, near.Count)]);
                    Both(a, b, Command.SpawnBuilding(p, buildings[p][rng.NextInt(0, buildings[p].Length)], at));
                }
            }
            if (t % 3 == 0)
            {
                for (int n = 0; n < 6; n++)
                {
                    int i = rng.NextInt(0, u.Capacity);
                    var h = new EntityHandle(i, u.Generation[i] + (rng.NextInt(0, 10) == 0 ? 1 : 0));
                    int p = u.Alive[i] ? u.Owner[i] : rng.NextInt(0, 2);
                    Vector2 at = MoveScenario.Center(g, near[rng.NextInt(0, near.Count)]);
                    int roll = rng.NextInt(0, 100);
                    Command c;
                    if (roll < 40) c = Command.AttackMove(p, h, at, rng.NextInt(0, 4) == 0);
                    else if (roll < 50) c = Command.Move(p, h, at);
                    else if (roll < 55) c = Command.Stop(p, h);
                    else if (roll < 62) c = Command.HoldPosition(p, h);
                    else if (roll < 80)
                    {
                        int j = rng.NextInt(0, u.Capacity);
                        c = Command.Attack(p, h, new EntityHandle(j, u.Generation[j]), isBuilding: false, queued: rng.NextInt(0, 4) == 0);
                    }
                    else
                    {
                        int j = rng.NextInt(0, bs.Capacity);
                        c = Command.Attack(p, h, new EntityHandle(j, bs.Generation[j] + (rng.NextInt(0, 6) == 0 ? 1 : 0)), isBuilding: true);
                    }
                    Both(a, b, c);
                }
            }
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"seed {seed}: twins differ after tick {t}");

            foreach (DeathEvent e in w.Deaths)
            {
                deaths++;
                if (e.IsBuilding) buildingDeaths++;
                if (e.KillerOwner == e.VictimOwner) friendly++;
            }
            Assert.Equal(deaths, (long)w.Kills[0] + w.Kills[1]);
            Assert.Equal(deaths, (long)w.Losses[0] + w.Losses[1]);
            Assert.True(w.Impacts.Length <= capacity);
            foreach (ProjectileImpact e in w.Impacts)
            {
                if (e.Hit) hits++; else misses++;
                Assert.True(float.IsFinite(e.Position.X) && float.IsFinite(e.Position.Y));
                Assert.True(e.Owner is 0 or 1);
            }
            ProjectileStore ps = w.Projectiles;
            int live = 0;
            for (int k = 0; k < ps.Capacity; k++)
            {
                if (!ps.Alive[k]) continue;
                live++;
                Vector2 pos = ps.Position[k], target = ps.Target[k], prev = ps.PrevPosition[k];
                Assert.True(float.IsFinite(pos.X) && float.IsFinite(pos.Y), $"seed {seed} tick {t}: projectile {k} at {pos}");
                Assert.True(ps.TicksLeft[k] > 0);
                // Each step closes on the impact point (straight line, constant speed): never farther than the step before.
                Assert.True(Vector2.Distance(pos, target) <= Vector2.Distance(prev, target) + 1e-3f, $"seed {seed} tick {t}: projectile {k} moved away from its impact point");
                Assert.True(pos.X >= 0 && pos.Y >= 0 && pos.X <= g.Width * MapConstants.CellSize && pos.Y <= g.Height * MapConstants.CellSize);
                if (lastLeft[k] == ps.TicksLeft[k] + 1)
                {
                    if (target != lastAim[k]) steers++;
                    longestStep = MathF.Max(longestStep, Vector2.Distance(prev, pos));
                }
            }
            for (int k = 0; k < ps.Capacity; k++)
            {
                lastLeft[k] = ps.Alive[k] ? ps.TicksLeft[k] : -1;
                lastAim[k] = ps.Target[k];
            }
            Assert.Equal(ps.Count, live);
            Assert.True(live <= capacity);
            maxInFlight = Math.Max(maxInFlight, live);
            peakUnits = Math.Max(peakUnits, u.Count);
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i]) continue;
                UnitDef def = w.Data.Units[u.TypeId[i]];
                Assert.True(u.Hp[i] > 0 && u.Hp[i] <= def.Hp, $"seed {seed} tick {t}: unit {i} hp {u.Hp[i]} of {def.Hp}");
                Assert.True(float.IsFinite(u.Position[i].X) && float.IsFinite(u.Position[i].Y));
                EntityHandle la = u.LastAttacker[i];
                if (la != default)
                {
                    Assert.True(u.IsAlive(la), $"seed {seed} tick {t}: unit {i} remembers a dead attacker");
                    Assert.True(u.Owner[la.Index] != u.Owner[i], $"seed {seed} tick {t}: unit {i} remembers its own side as its attacker");
                }
                if (u.Target[i] != default)
                    Assert.True(u.TargetIsBuilding[i] ? bs.IsAlive(u.Target[i]) : u.IsAlive(u.Target[i]), $"seed {seed} tick {t}: unit {i} targets a dead one");
            }
            for (int j = 0; j < bs.Capacity; j++)
            {
                if (!bs.Alive[j]) continue;
                Assert.True(bs.Hp[j] > 0 && bs.Hp[j] <= w.Data.Buildings[bs.TypeId[j]].Hp, $"seed {seed} tick {t}: building {j} hp {bs.Hp[j]}");
            }
        }
        _out.WriteLine($"seed {seed}: {deaths} deaths ({buildingDeaths} buildings, {friendly} friendly fire), {hits} hits / {misses} misses, up to {maxInFlight} of {capacity} in flight, peak {peakUnits} units; {steers} re-leads, longest step {longestStep:F2} m");
        Assert.True(deaths >= 50, $"seed {seed}: only {deaths} deaths");
        Assert.True(hits > 50 && misses > 0, $"seed {seed}: {hits} hits, {misses} misses");
        Assert.True(steers > 0, $"seed {seed}: no shot was re-led in flight");

        Replay recorded = recorder.ToReplay();
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(ReplayFormat.Write(recorded), out Replay? back));
        ReplayResult played = ReplayPlayer.Run(back!, TestSim.Data);
        Assert.True(played.Ok, $"seed {seed}: {played}");
        Assert.Equal(Ticks, played.TicksRun);
    }

    private static void Both(Simulation a, Simulation b, Command c)
    {
        a.Enqueue(c);
        b.Enqueue(c);
    }
}
