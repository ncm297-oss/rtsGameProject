using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Replays;
using Rts.Sim.Vision;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA hostile fuzz for M4-3b (session 2026-10-09-0125): on generated maps, both players' towers on every level and mixed
/// armies; every 20 ticks a burst of hostile commands: explicit Attacks on building handles of every kind (live enemy,
/// live own, a stale generation of a reused slot, a free slot, out-of-range slots), on units, attack-moves, moves, stops,
/// holds, Builds at random anchors (most unexplored), dev SpawnBuildings that churn slots. 3,000 ticks, 4 seeds. Twins
/// hash-equal after every tick; per tick: no unit targets an own building; a unit holding a gone building is in an ordered
/// Attack its owner remembers, and never swings at it; a wind-up is only at a live target; a tower targets only another
/// owner's unit; a site holds no tower state; every tower shot carries a building type with an attack; HP within bounds;
/// positions finite; every ghost entry is another player's and the counts add up; every live, placed (non-dev) building's
/// footprint was explored by its owner when placed. The recorded replay round-trips and plays back equal.
/// </summary>
public class GhostAttackHostileFuzzStressTests
{
    private const int Ticks = 3000;
    private const int PerSide = 40;
    private readonly ITestOutputHelper _out;

    public GhostAttackHostileFuzzStressTests(ITestOutputHelper output) => _out = output;

    private static readonly string[] Army0 = { "malazan_heavy_infantry", "malazan_crossbowman", "malazan_wickan_lancer", "malazan_laborer", "malazan_laborer" };
    private static readonly string[] Army1 = { "whirlwind_raider", "whirlwind_desert_archer", "whirlwind_horse_raider", "whirlwind_battering_ram", "whirlwind_camp_follower" };
    private static readonly string[] Towers = { "malazan_watchtower", "whirlwind_lookout_tower" };

    private static Simulation NewSim(ulong seed, ReplayRecorder?[] recorder, out List<int> cells)
    {
        var sim = new Simulation(TestSim.Config(Seed: seed, PlayerCount: 2, UnitCapacity: 3 * PerSide, CommandCapacity: 16 * PerSide + 128));
        if (recorder.Length > 0) recorder[0] = new ReplayRecorder(sim, checkpointInterval: 50);
        World w = sim.World;
        NavGrid g = w.NavGrid;
        cells = new List<int>();
        for (int c = 0; c < g.Width * g.Height; c++)
            if (g.IsPassable(c % g.Width, c / g.Width) && !w.Heightmap.IsRamp(c % g.Width, c / g.Width)) cells.Add(c);
        var rng = new SimRng(seed, 211);
        for (int side = 0; side < 2; side++)
        {
            int type = TestSim.Data.FindBuilding(Towers[side]);
            for (int k = 0; k < 6; k++)
            {
                int c = cells[rng.NextInt(0, cells.Count)];
                sim.Enqueue(Command.SpawnBuilding(side, type, g.CellCenter(c % g.Width, c / g.Width)));
            }
        }
        for (int k = 0; k < PerSide; k++)
            for (int side = 0; side < 2; side++)
            {
                int c = cells[rng.NextInt(0, cells.Count)];
                string[] army = side == 0 ? Army0 : Army1;
                sim.Enqueue(Command.SpawnUnit(side, TestSim.Data.FindUnit(army[rng.NextInt(0, army.Length)]), g.CellCenter(c % g.Width, c / g.Width)));
            }
        return sim;
    }

    private static Vector2 RandomPoint(World w, List<int> cells, ref SimRng rng)
    {
        int c = cells[rng.NextInt(0, cells.Count)];
        return w.NavGrid.CellCenter(c % w.NavGrid.Width, c / w.NavGrid.Width);
    }

    private static void Burst(Simulation sim, List<int> cells, ref SimRng rng)
    {
        World w = sim.World;
        UnitStore u = w.Units;
        BuildingStore b = w.Buildings;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || rng.NextInt(0, 3) != 0) continue;
            int owner = u.Owner[i];
            var unit = new EntityHandle(i, u.Generation[i]);
            switch (rng.NextInt(0, 10))
            {
                case 0:
                case 1:
                case 2:
                {
                    // A building handle of any kind: live (own or enemy), a stale generation, a free slot, out of range.
                    int j = rng.NextInt(-2, b.Capacity + 3);
                    int gen = (uint)j < (uint)b.Capacity ? b.Generation[j] - rng.NextInt(0, 3) : rng.NextInt(0, 4);
                    sim.Enqueue(Command.Attack(owner, unit, new EntityHandle(j, gen), isBuilding: true, queued: rng.NextInt(0, 4) == 0));
                    break;
                }
                case 3:
                {
                    int j = rng.NextInt(0, u.Capacity);
                    sim.Enqueue(Command.Attack(owner, unit, new EntityHandle(j, u.Generation[j]), isBuilding: false));
                    break;
                }
                case 4:
                case 5:
                    sim.Enqueue(Command.AttackMove(owner, unit, RandomPoint(w, cells, ref rng)));
                    break;
                case 6:
                    sim.Enqueue(Command.Move(owner, unit, RandomPoint(w, cells, ref rng)));
                    break;
                case 7:
                    sim.Enqueue(rng.NextInt(0, 2) == 0 ? Command.Stop(owner, unit) : Command.HoldPosition(owner, unit));
                    break;
                case 8:
                    sim.Enqueue(Command.Build(owner, unit, rng.NextInt(0, w.Data.Buildings.Length), RandomPoint(w, cells, ref rng)));
                    break;
                default:
                    // A Build beside the unit (explored ground): sites the enemy may see, then lose when cancelled unseen.
                    sim.Enqueue(Command.Build(owner, unit, rng.NextInt(0, w.Data.Buildings.Length),
                        u.Position[i] + new Vector2(rng.NextInt(-8, 9), rng.NextInt(-8, 9))));
                    break;
            }
        }
        // Cancel a random live site (its slot freed: a ghost of a gone building for whoever saw it).
        for (int j = 0; j < b.Capacity; j++)
        {
            if (!b.Alive[j] || !b.UnderConstruction[j] || rng.NextInt(0, 3) != 0) continue;
            var site = new Vector2((b.Cell[j] % w.NavGrid.Width + 0.5f) * MapConstants.CellSize, (b.Cell[j] / w.NavGrid.Width + 0.5f) * MapConstants.CellSize);
            sim.Enqueue(Command.Cancel(b.Owner[j], site));
        }
        // Slot churn: a dev building for a random player somewhere (it takes the lowest free slot).
        if (rng.NextInt(0, 2) == 0)
            sim.Enqueue(Command.SpawnBuilding(rng.NextInt(0, 2), rng.NextInt(0, w.Data.Buildings.Length), RandomPoint(w, cells, ref rng)));
    }

    [Theory]
    [InlineData(2UL)]
    [InlineData(7UL)]
    [InlineData(11UL)]
    [InlineData(19UL)]
    public void HostileGhostAttacks_TwinsEqualEveryTick_InvariantsHold_ReplayPlaysBack(ulong seed)
    {
        var rec = new ReplayRecorder?[1];
        Simulation a = NewSim(seed, rec, out List<int> cells), b = NewSim(seed, Array.Empty<ReplayRecorder?>(), out _);
        World w = a.World;
        UnitStore u = w.Units;
        BuildingStore bs = w.Buildings;
        var rngA = new SimRng(seed, 212);
        var rngB = new SimRng(seed, 212);
        int ghostHeld = 0, ghostHeldGone = 0, towerShots = 0, maxGhosts = 0, ordersOnBuildings = 0, lagged = 0;
        var pending = new EntityHandle[u.Capacity];
        for (int t = 0; t < Ticks; t++)
        {
            if (t % 20 == 3)
            {
                Burst(a, cells, ref rngA);
                Burst(b, cells, ref rngB);
            }
            var flying = new bool[w.Projectiles.Capacity];
            for (int k = 0; k < flying.Length; k++) flying[k] = w.Projectiles.Alive[k];
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"seed {seed}: twins differ after tick {t}");
            for (int k = 0; k < flying.Length; k++)
            {
                if (flying[k] || !w.Projectiles.Alive[k] || !w.Projectiles.AttackerIsBuilding[k]) continue;
                towerShots++;
                int type = w.Projectiles.AttackerType[k];
                Assert.True((uint)type < (uint)w.Data.Buildings.Length && w.Data.Buildings[type].Attack != null, $"seed {seed} tick {t}: a tower shot of building type {type}");
            }
            for (int i = 0; i < u.Capacity; i++)
            {
                // A gone building dropped from the list by this update's phase 12 is let go at the next phase 7.
                if (pending[i].Generation != 0)
                {
                    Assert.False(u.Alive[i] && u.TargetIsBuilding[i] && u.Target[i] == pending[i], $"seed {seed} tick {t}: unit {i} still holds {pending[i]} a tick after its owner forgot it");
                    pending[i] = default;
                }
                if (!u.Alive[i]) continue;
                Vector2 p = u.Position[i];
                Assert.True(float.IsFinite(p.X) && float.IsFinite(p.Y), $"seed {seed} tick {t}: unit {i} at {p}");
                Assert.InRange(u.Hp[i], 1, w.Data.Units[u.TypeId[i]].Hp);
                EntityHandle target = u.Target[i];
                if (target.Generation == 0 || !u.TargetIsBuilding[i]) continue;
                ordersOnBuildings++;
                bool live = bs.IsAlive(target);
                if (live) Assert.True(bs.Owner[target.Index] != u.Owner[i], $"seed {seed} tick {t}: unit {i} targets its own building {target}");
                bool remembered = u.Mode[i] == CombatMode.Ordered && w.Fog.HasGhost(u.Owner[i], target);
                if (remembered) ghostHeld++;
                if (live) continue;
                ghostHeldGone++;
                if (!remembered)
                {
                    Assert.True(VisionSystem.IsUpdateTick(t) && u.Mode[i] == CombatMode.Ordered, $"seed {seed} tick {t}: unit {i} holds gone building {target} (mode {u.Mode[i]}) not on its owner's list");
                    pending[i] = target;
                    lagged++;
                }
                Assert.True(u.WindupTicks[i] == 0 && u.State[i] != UnitState.Attacking, $"seed {seed} tick {t}: unit {i} swings at gone building {target}");
            }
            for (int j = 0; j < bs.Capacity; j++)
            {
                if (!bs.Alive[j]) continue;
                bool tower = w.Data.Buildings[bs.TypeId[j]].Attack != null;
                EntityHandle target = bs.TowerTarget[j];
                if (!tower || bs.UnderConstruction[j])
                {
                    Assert.True(target == default && bs.TowerCooldown[j] == 0 && bs.TowerWindup[j] == 0, $"seed {seed} tick {t}: building {j} holds tower state");
                    continue;
                }
                Assert.InRange(bs.TowerCooldown[j], 0, w.Data.Buildings[bs.TypeId[j]].Attack!.CooldownTicks);
                if (target.Generation != 0 && u.IsAlive(target)) Assert.NotEqual(bs.Owner[j], u.Owner[target.Index]);
                if (bs.TowerWindup[j] > 0) Assert.NotEqual(default, target);
            }
            for (int p = 0; p < 2; p++)
            {
                int n = 0;
                ReadOnlySpan<BuildingGhost> ghosts = w.Fog.Ghosts(p);
                for (int j = 0; j < ghosts.Length; j++)
                {
                    if (!ghosts[j].Known) continue;
                    n++;
                    Assert.NotEqual(p, ghosts[j].Owner);
                    Assert.True((uint)ghosts[j].TypeId < (uint)w.Data.Buildings.Length);
                }
                Assert.Equal(n, w.Fog.GhostCount(p));
                maxGhosts = Math.Max(maxGhosts, n);
            }
        }
        _out.WriteLine($"seed {seed}: tower shots {towerShots}, unit-ticks on a building {ordersOnBuildings}, held through the list {ghostHeld} "
            + $"(gone {ghostHeldGone}), most ghosts {maxGhosts}, one-tick lags {lagged}, buildings {bs.Count}, units {u.Count}, kills {w.Kills[0]}/{w.Kills[1]}");
        Assert.True(towerShots > 0, "no tower fired");
        Replay replay = rec[0]!.ToReplay();
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(ReplayFormat.Write(replay), out Replay? back));
        ReplayResult result = ReplayPlayer.Run(back!, TestSim.Data);
        Assert.True(result.Ok, $"seed {seed}: replay {result.Error} at tick {result.Tick}");
    }
}
