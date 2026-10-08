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
/// M4-2b criterion 7: both factions' whole rosters (ranged, casters, Catapults, Sappers, the ram and melee) on a generated
/// map, 6 seeds x 3,000 ticks, all through commands: reinforcements spawned every 150 ticks and a random order stream
/// (AttackMove, Move, Stop, Hold, Attack on live and stale handles). Twins hash-equal after every tick; every tick hp in
/// (0, max], every projectile in flight finite with flight left, kills and losses balanced and grown by this tick's
/// death events; then the recording plays back checkpoint for checkpoint.
/// </summary>
public class RangedCombatFuzzTests
{
    private const int Ticks = 3000;
    private readonly ITestOutputHelper _out;

    public RangedCombatFuzzTests(ITestOutputHelper output) => _out = output;

    public static IEnumerable<object[]> Seeds() => Enumerable.Range(1, 6).Select(s => new object[] { (ulong)s });

    /// <summary>Unit type ids of faction <paramref name="faction"/>, id order.</summary>
    private static int[] Roster(int faction) => TestSim.Data.Units.Where(d => d.Faction == faction).Select(d => d.Id).ToArray();

    [Theory]
    [MemberData(nameof(Seeds))]
    public void WholeRosters_RandomOrders_TwinsEqualEveryTick_InvariantsHold_AndTheReplayPlaysBack(ulong seed)
    {
        ReplayRecorder? recorder = null;
        SimConfig config = TestSim.Config(Seed: seed, PlayerCount: 2, UnitCapacity: 200, CommandCapacity: 512);
        var a = new Simulation(config);
        recorder = new ReplayRecorder(a, checkpointInterval: 100);
        var b = new Simulation(config);
        World w = a.World;
        UnitStore u = w.Units;
        NavGrid g = w.NavGrid;
        int center = MoveScenario.CentralCell(g);
        FlowField field = FlowField.Build(g, center);
        var near = new List<int>();
        for (int c = 0; c < g.Width * g.Height; c++)
            if (field.CostAt(c) <= 18f) near.Add(c);
        int[][] rosters = { Roster(0), Roster(1) };
        var rng = new SimRng(seed, 808);
        long deaths = 0;
        int hits = 0, misses = 0, maxInFlight = 0, friendlyKills = 0;
        for (int t = 0; t < Ticks; t++)
        {
            if (t % 150 == 0 && u.Count < 160)
            {
                for (int n = 0; n < 16; n++)
                {
                    int p = n % 2;
                    int cell = near[rng.NextInt(0, near.Count)];
                    Vector2 corner = MoveScenario.Center(g, cell) - new Vector2(MapConstants.CellSize / 2);
                    Vector2 at = corner + new Vector2(0.1f + rng.NextFloat() * 1.8f, 0.1f + rng.NextFloat() * 1.8f);
                    Both(a, b, Command.SpawnUnit(p, rosters[p][rng.NextInt(0, rosters[p].Length)], at));
                }
            }
            if (t % 2 == 0)
            {
                for (int n = 0; n < 4; n++)
                {
                    int i = rng.NextInt(0, u.Capacity);
                    var h = new EntityHandle(i, u.Generation[i] + (rng.NextInt(0, 8) == 0 ? 1 : 0));
                    int p = u.Alive[i] ? u.Owner[i] : rng.NextInt(0, 2);
                    Vector2 at = MoveScenario.Center(g, near[rng.NextInt(0, near.Count)]);
                    int j = rng.NextInt(0, u.Capacity);
                    int roll = rng.NextInt(0, 100);
                    Command c = roll < 35 ? Command.AttackMove(p, h, at, rng.NextInt(0, 4) == 0)
                        : roll < 50 ? Command.Move(p, h, at, rng.NextInt(0, 4) == 0)
                        : roll < 58 ? Command.Stop(p, h)
                        : roll < 66 ? Command.HoldPosition(p, h)
                        : Command.Attack(p, h, new EntityHandle(j, u.Generation[j]), isBuilding: false, queued: rng.NextInt(0, 4) == 0);
                    Both(a, b, c);
                }
            }
            int killsBefore = w.Kills[0] + w.Kills[1];
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"seed {seed}: twins differ after tick {t}");

            foreach (DeathEvent e in w.Deaths)
            {
                deaths++;
                if (e.KillerOwner == e.VictimOwner) friendlyKills++;
                Assert.False(e.IsBuilding ? w.Buildings.IsAlive(e.Victim) : u.IsAlive(e.Victim));
            }
            Assert.Equal(killsBefore + w.Deaths.Length, w.Kills[0] + w.Kills[1]);
            Assert.Equal(deaths, (long)w.Kills[0] + w.Kills[1]);
            Assert.Equal(deaths, (long)w.Losses[0] + w.Losses[1]);
            foreach (ProjectileImpact e in w.Impacts)
            {
                if (e.Hit) hits++;
                else misses++;
                Assert.True(float.IsFinite(e.Position.X) && float.IsFinite(e.Position.Y));
            }
            ProjectileStore ps = w.Projectiles;
            int live = 0;
            for (int k = 0; k < ps.Capacity; k++)
            {
                if (!ps.Alive[k]) continue;
                live++;
                Assert.True(float.IsFinite(ps.Position[k].X) && float.IsFinite(ps.Position[k].Y), $"seed {seed} tick {t}: projectile {k} at {ps.Position[k]}");
                Assert.True(ps.TicksLeft[k] > 0, $"seed {seed} tick {t}: projectile {k} landed but still in flight");
            }
            Assert.Equal(ps.Count, live);
            maxInFlight = Math.Max(maxInFlight, live);
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i]) continue;
                UnitDef def = w.Data.Units[u.TypeId[i]];
                Assert.True(u.Hp[i] > 0 && u.Hp[i] <= def.Hp, $"seed {seed} tick {t}: unit {i} hp {u.Hp[i]} of {def.Hp}");
                Assert.True(float.IsFinite(u.Position[i].X) && float.IsFinite(u.Position[i].Y));
                if (u.Target[i] != default)
                    Assert.True(u.TargetIsBuilding[i] ? w.Buildings.IsAlive(u.Target[i]) : u.IsAlive(u.Target[i]), $"seed {seed} tick {t}: unit {i} targets a dead one");
                // Planted between swings only on a target outside its minimum range (an Ordered unit with a too-near target waits Idle).
                if (u.WindupTicks[i] == 0 && u.State[i] == UnitState.Attacking && def.Attack.MinRange > 0f && !u.TargetIsBuilding[i] && u.Target[i] != default)
                {
                    int j = u.Target[i].Index;
                    float gap = Vector2.Distance(u.Position[i], u.Position[j]) - u.Radius[i] - u.Radius[j];
                    Assert.True(gap >= def.Attack.MinRange, $"seed {seed} tick {t}: unit {i} ({def.Key}) planted on a target {gap:F2} m off, inside its minimum range");
                }
            }
        }
        _out.WriteLine($"seed {seed}: {deaths} deaths ({friendlyKills} by friendly fire), {hits} hits / {misses} misses landed, up to {maxInFlight} projectiles in flight, {u.Count} units left");
        Assert.True(deaths >= 20, $"seed {seed}: only {deaths} deaths");
        Assert.True(hits > 20 && misses > 0, $"seed {seed}: {hits} hits, {misses} misses: the ranged units are not fighting");

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
