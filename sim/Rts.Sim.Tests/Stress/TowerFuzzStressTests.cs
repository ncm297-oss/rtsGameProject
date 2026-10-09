using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Replays;
using Rts.Sim.Vision;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// M4-3b criterion 2: towers on all three levels of generated maps (both players, placed by command), mixed armies
/// attack-moved across the levels every 150 ticks, 2,000 ticks, 3 seeds. Twins hash-equal after every tick; every tower's
/// target is a live enemy unit (never a building) and a site holds no tower state; every last-known entry is another
/// player's building and the counts match the entries; towers fired; the recorded replay round-trips and plays back equal.
/// </summary>
public class TowerFuzzStressTests
{
    private const int Ticks = 2000;
    private const int PerSide = 50;
    private const int TowersPerLevel = 2;
    private readonly ITestOutputHelper _out;

    public TowerFuzzStressTests(ITestOutputHelper output) => _out = output;

    private static readonly string[] Army0 = { "malazan_heavy_infantry", "malazan_crossbowman", "malazan_wickan_lancer", "malazan_laborer" };
    private static readonly string[] Army1 = { "whirlwind_raider", "whirlwind_desert_archer", "whirlwind_horse_raider", "whirlwind_battering_ram" };
    private static readonly string[] Towers = { "malazan_watchtower", "whirlwind_lookout_tower" };

    /// <summary>Passable non-ramp cells of the map by level.</summary>
    private static List<int>[] Cells(World w)
    {
        NavGrid g = w.NavGrid;
        var byLevel = new List<int>[MapConstants.LevelCount];
        for (int k = 0; k < byLevel.Length; k++) byLevel[k] = new List<int>();
        for (int c = 0; c < g.Width * g.Height; c++)
        {
            int x = c % g.Width, y = c / g.Width;
            if (g.IsPassable(x, y) && !w.Heightmap.IsRamp(x, y)) byLevel[g.LevelAt(x, y)].Add(c);
        }
        return byLevel;
    }

    private static Simulation NewSim(ulong seed, ReplayRecorder?[] recorder)
    {
        var sim = new Simulation(TestSim.Config(Seed: seed, PlayerCount: 2, UnitCapacity: 2 * PerSide, CommandCapacity: 4 * PerSide + 64));
        if (recorder.Length > 0) recorder[0] = new ReplayRecorder(sim, checkpointInterval: 50);
        World w = sim.World;
        NavGrid g = w.NavGrid;
        List<int>[] cells = Cells(w);
        var rng = new SimRng(seed, 131);
        // Towers first (they apply before the units in tick 1): each player, each level, on its own half where it fits.
        for (int side = 0; side < 2; side++)
        {
            int type = TestSim.Data.FindBuilding(Towers[side]);
            for (int level = 0; level < MapConstants.LevelCount; level++)
            {
                int placed = 0;
                for (int tries = 0; tries < 400 && placed < TowersPerLevel && cells[level].Count > 0; tries++)
                {
                    int c = cells[level][rng.NextInt(0, cells[level].Count)];
                    if ((c % g.Width < g.Width / 2) != (side == 0) || !w.Buildings.Fits(type, c)) continue;
                    sim.Enqueue(Command.SpawnBuilding(side, type, g.CellCenter(c % g.Width, c / g.Width)));
                    placed++;
                }
            }
        }
        for (int k = 0; k < PerSide; k++)
            for (int side = 0; side < 2; side++)
            {
                List<int> list = cells[k % cells.Length];
                if (list.Count == 0) list = cells[0];
                int c = list[rng.NextInt(0, list.Count)];
                Vector2 corner = g.CellCenter(c % g.Width, c / g.Width) - new Vector2(MapConstants.CellSize / 2);
                int type = TestSim.Data.FindUnit((side == 0 ? Army0 : Army1)[rng.NextInt(0, 4)]);
                sim.Enqueue(Command.SpawnUnit(side, type, corner + new Vector2(0.1f + rng.NextFloat() * 1.8f, 0.1f + rng.NextFloat() * 1.8f)));
            }
        return sim;
    }

    /// <summary>Every live unit of both sides attack-moves to a random passable cell of a random level.</summary>
    private static void Orders(Simulation sim, List<int>[] cells, ref SimRng rng)
    {
        UnitStore u = sim.World.Units;
        NavGrid g = sim.World.NavGrid;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            List<int> list = cells[rng.NextInt(0, cells.Length)];
            if (list.Count == 0) list = cells[0];
            int c = list[rng.NextInt(0, list.Count)];
            sim.Enqueue(Command.AttackMove(u.Owner[i], new EntityHandle(i, u.Generation[i]), g.CellCenter(c % g.Width, c / g.Width)));
        }
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(3UL)]
    [InlineData(5UL)]
    public void TowersOnThreeLevels_TwinsHashEqualEveryTick_InvariantsHold_AndTheReplayPlaysBack(ulong seed)
    {
        var rec = new ReplayRecorder?[1];
        Simulation a = NewSim(seed, rec), b = NewSim(seed, Array.Empty<ReplayRecorder?>());
        World w = a.World;
        BuildingStore bs = w.Buildings;
        UnitStore u = w.Units;
        List<int>[] cells = Cells(w);
        var rngA = new SimRng(seed, 132);
        var rngB = new SimRng(seed, 132);
        int towerShots = 0, towerTargetTicks = 0, maxGhosts = 0;
        var levels = new HashSet<int>();
        for (int t = 0; t < Ticks; t++)
        {
            if (t % 150 == 2)
            {
                Orders(a, cells, ref rngA);
                Orders(b, cells, ref rngB);
            }
            var flying = new bool[w.Projectiles.Capacity];
            for (int k = 0; k < flying.Length; k++) flying[k] = w.Projectiles.Alive[k];
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"seed {seed}: twins differ after tick {t}");
            for (int k = 0; k < flying.Length; k++)
                if (!flying[k] && w.Projectiles.Alive[k] && w.Projectiles.AttackerIsBuilding[k]) towerShots++;
            for (int j = 0; j < bs.Capacity; j++)
            {
                if (!bs.Alive[j]) continue;
                bool tower = w.Data.Buildings[bs.TypeId[j]].Attack != null;
                if (tower && t == 1) levels.Add(w.Fog.BuildingLevel(j));
                EntityHandle target = bs.TowerTarget[j];
                if (!tower || bs.UnderConstruction[j])
                {
                    Assert.True(target == default && bs.TowerCooldown[j] == 0 && bs.TowerWindup[j] == 0, $"seed {seed} tick {t}: building {j} holds tower state");
                    continue;
                }
                if (target == default) continue;
                towerTargetTicks++;
                // A unit handle of another owner (one killed in this tick's phase 11 is dropped in the next tick's phase 7).
                if (u.IsAlive(target)) Assert.NotEqual(bs.Owner[j], u.Owner[target.Index]);
            }
            if (!VisionSystem.IsUpdateTick(t)) continue;
            for (int p = 0; p < 2; p++)
            {
                int n = 0;
                ReadOnlySpan<BuildingGhost> ghosts = w.Fog.Ghosts(p);
                for (int j = 0; j < ghosts.Length; j++)
                {
                    if (!ghosts[j].Known) continue;
                    n++;
                    Assert.NotEqual(p, ghosts[j].Owner);
                    // Seen now: the entry is that building as it stands.
                    if (bs.Alive[j] && bs.Owner[j] != p && w.Fog.CanSeeBuilding(p, j))
                        Assert.Equal(new BuildingGhost(bs.Generation[j], bs.TypeId[j], bs.Cell[j], bs.Owner[j]), ghosts[j]);
                }
                Assert.Equal(n, w.Fog.GhostCount(p));
                maxGhosts = Math.Max(maxGhosts, n);
            }
        }
        _out.WriteLine($"seed {seed}: tower levels {string.Join(",", levels.OrderBy(x => x))}, tower shots {towerShots}, tower-ticks with a target "
            + $"{towerTargetTicks}, kills {w.Kills[0]}/{w.Kills[1]}, buildings left {bs.Count}, most ghosts {maxGhosts}");
        Assert.Equal(3, levels.Count);
        Assert.True(towerShots > 0, "no tower fired");
        Replay replay = rec[0]!.ToReplay();
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(ReplayFormat.Write(replay), out Replay? back));
        ReplayResult result = ReplayPlayer.Run(back!, TestSim.Data);
        Assert.True(result.Ok, $"seed {seed}: replay {result.Error} at tick {result.Tick}");
    }
}
