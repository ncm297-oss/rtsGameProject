using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Vision;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA (2026-10-08-1435, M4-3a): a hostile command stream on generated 3-level maps with both players and buildings on
/// every level: attack-moves, moves, stops, holds and explicit Attacks on random enemy handles (seen, unseen, dead,
/// buildings), every 6 ticks. Twins hash-equal every tick; after every fog update both players' fog equals the integer
/// oracle (<see cref="QA.FogQaTests.Oracle"/>), explored cells never become unexplored and every visible cell is explored;
/// and no unit keeps a target its owner can't see (<see cref="VisionSystem.UnitSees"/>) for more than a scan interval
/// plus a tick.
/// </summary>
public class FogHostileFuzzQaTests
{
    private const int Ticks = 1200;
    private const int PerSide = 40;
    private readonly ITestOutputHelper _out;

    public FogHostileFuzzQaTests(ITestOutputHelper output) => _out = output;

    private static readonly string[] Army0 = { "malazan_heavy_infantry", "malazan_crossbowman", "malazan_catapult", "malazan_sapper", "malazan_laborer" };
    private static readonly string[] Army1 = { "whirlwind_raider", "whirlwind_desert_archer", "whirlwind_horse_raider", "whirlwind_priest", "whirlwind_battering_ram" };
    private static readonly string[] Towers = { "malazan_watchtower", "whirlwind_lookout_tower" };

    private static List<int>[] Cells(World w)
    {
        NavGrid g = w.NavGrid;
        var byLevel = new List<int>[MapConstants.LevelCount + 1];
        for (int k = 0; k < byLevel.Length; k++) byLevel[k] = new List<int>();
        for (int c = 0; c < g.Width * g.Height; c++)
        {
            int x = c % g.Width, y = c / g.Width;
            if (g.IsPassable(x, y)) byLevel[w.Heightmap.IsRamp(x, y) ? MapConstants.LevelCount : g.LevelAt(x, y)].Add(c);
        }
        return byLevel;
    }

    private static Simulation NewSim(ulong seed)
    {
        var sim = new Simulation(TestSim.Config(Seed: seed, PlayerCount: 2, UnitCapacity: 2 * PerSide + 8, CommandCapacity: 8 * PerSide + 64));
        World w = sim.World;
        List<int>[] cells = Cells(w);
        NavGrid g = w.NavGrid;
        var rng = new SimRng(seed, 501);
        for (int k = 0; k < PerSide; k++)
            for (int side = 0; side < 2; side++)
            {
                List<int> list = cells[k % cells.Length];
                int c = list[rng.NextInt(0, list.Count)];
                string[] army = side == 0 ? Army0 : Army1;
                sim.Enqueue(Command.SpawnUnit(side, TestSim.Data.FindUnit(army[rng.NextInt(0, army.Length)]), g.CellCenter(c % g.Width, c / g.Width)));
            }
        // A tower per side on each level (where one fits), straight into the store.
        for (int side = 0; side < 2; side++)
            for (int level = 0; level < MapConstants.LevelCount; level++)
                for (int tries = 0; tries < 20; tries++)
                {
                    List<int> list = cells[level];
                    if (w.Buildings.Spawn(side, w.Data.FindBuilding(Towers[side]), list[rng.NextInt(0, list.Count)], out _)) break;
                }
        return sim;
    }

    private static void Orders(Simulation sim, List<int>[] cells, ref SimRng rng)
    {
        World w = sim.World;
        UnitStore u = w.Units;
        BuildingStore b = w.Buildings;
        NavGrid g = w.NavGrid;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || rng.NextInt(0, 3) != 0) continue;
            var me = new EntityHandle(i, u.Generation[i]);
            int owner = u.Owner[i];
            List<int> list = cells[rng.NextInt(0, cells.Length)];
            int c = list[rng.NextInt(0, list.Count)];
            Vector2 to = g.CellCenter(c % g.Width, c / g.Width);
            switch (rng.NextInt(0, 6))
            {
                case 0: sim.Enqueue(Command.AttackMove(owner, me, to)); break;
                case 1: sim.Enqueue(Command.Move(owner, me, to)); break;
                case 2: sim.Enqueue(rng.NextInt(0, 2) == 0 ? Command.Stop(owner, me) : Command.HoldPosition(owner, me)); break;
                case 3:
                case 4:
                {
                    // Any enemy slot, live or not, the current or an older generation.
                    int j = rng.NextInt(0, u.Capacity);
                    var h = new EntityHandle(j, Math.Max(1, u.Generation[j] - rng.NextInt(0, 2)));
                    sim.Enqueue(Command.Attack(owner, me, h, isBuilding: false, queued: rng.NextInt(0, 4) == 0));
                    break;
                }
                default:
                {
                    int j = rng.NextInt(0, b.Capacity);
                    sim.Enqueue(Command.Attack(owner, me, b.HandleOf(j), isBuilding: true));
                    break;
                }
            }
        }
    }

    [Theory]
    [InlineData(11UL)]
    [InlineData(12UL)]
    [InlineData(13UL)]
    [InlineData(14UL)]
    [InlineData(15UL)]
    [InlineData(16UL)]
    public void HostileOrdersAcrossThreeLevels_TwinsEqual_FogEqualsTheOracleEveryUpdate_NoTargetKeptUnseen(ulong seed)
    {
        Simulation a = NewSim(seed), b = NewSim(seed);
        World w = a.World;
        UnitStore u = w.Units;
        List<int>[] cells = Cells(w);
        var rngA = new SimRng(seed, 502);
        var rngB = new SimRng(seed, 502);
        var unseenFor = new int[u.Capacity];
        var explored = new[] { new bool[w.Fog.Width * w.Fog.Height], new bool[w.Fog.Width * w.Fog.Height] };
        int updates = 0, worstUnseen = 0, attacksSeen = 0, revealTicks = 0;
        for (int t = 0; t < Ticks; t++)
        {
            if (t % 6 == 3)
            {
                Orders(a, cells, ref rngA);
                Orders(b, cells, ref rngB);
            }
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"seed {seed}: twins differ after tick {t}");
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i] || u.Target[i].Generation == 0)
                {
                    unseenFor[i] = 0;
                    continue;
                }
                attacksSeen++;
                bool targetAlive = u.TargetIsBuilding[i] ? w.Buildings.IsAlive(u.Target[i]) : u.IsAlive(u.Target[i]);
                if (!targetAlive) continue; // dropped at the next phase 7 / 10
                if (!u.TargetIsBuilding[i] && w.Fog.Revealed(u.Owner[i], u.Target[i].Index)) revealTicks++;
                bool seesIt = VisionSystem.UnitSees(w, i, u.Target[i], u.TargetIsBuilding[i]);
                // M4-3b: an explicit Attack on a building its owner remembers (the last-known list) is held unseen by design
                // (docs/03 "Implementation (M4-3b)"); the unit walks on and never swings at it while it doesn't see it.
                if (!seesIt && u.TargetIsBuilding[i] && u.Mode[i] == CombatMode.Ordered && w.Fog.HasGhost(u.Owner[i], u.Target[i]))
                {
                    Assert.True(u.State[i] != UnitState.Attacking && u.WindupTicks[i] == 0, $"seed {seed} tick {t}: unit {i} swings at an unseen remembered building");
                    unseenFor[i] = 0;
                    continue;
                }
                unseenFor[i] = seesIt ? 0 : unseenFor[i] + 1;
                worstUnseen = Math.Max(worstUnseen, unseenFor[i]);
                Assert.True(unseenFor[i] <= CombatConstants.ScanInterval + 1,
                    $"seed {seed} tick {t}: unit {i} kept target {u.Target[i]} (building {u.TargetIsBuilding[i]}) unseen for {unseenFor[i]} ticks");
            }
            if (!VisionSystem.IsUpdateTick(t)) continue;
            updates++;
            for (int p = 0; p < 2; p++)
            {
                bool[] now = QA.FogQaTests.Oracle(w, p);
                ReadOnlySpan<byte> vis = w.Fog.Visibility(p);
                for (int c = 0; c < now.Length; c++)
                {
                    Assert.True(now[c] == (vis[c] == VisionConstants.Visible), $"seed {seed} tick {t} player {p} cell {c}: fog {vis[c]}, oracle {now[c]}");
                    if (explored[p][c]) Assert.True(vis[c] != VisionConstants.Unexplored, $"seed {seed} tick {t}: cell {c} unexplored again");
                    explored[p][c] |= now[c];
                    Assert.True(explored[p][c] == (vis[c] != VisionConstants.Unexplored), $"seed {seed} tick {t} player {p} cell {c}: explored mismatch");
                }
            }
        }
        _out.WriteLine($"seed {seed}: {updates} updates checked, kills {w.Kills[0]}/{w.Kills[1]}, alive {u.Count}, buildings {w.Buildings.Count}, "
            + $"unit-ticks with a target {attacksSeen}, of which on a revealed unit {revealTicks}, longest unseen keep {worstUnseen} ticks");
        Assert.True(w.Kills[0] + w.Kills[1] > 0, "nobody died: not a fight");
    }
}
