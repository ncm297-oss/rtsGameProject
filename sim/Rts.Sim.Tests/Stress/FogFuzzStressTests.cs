using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Replays;
using Rts.Sim.Vision;
using Xunit.Abstractions;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// M4-3a criterion 5: two players' mixed armies (line, ranged, shock, siege) on generated 3-level maps, attack-moved
/// across the levels to random points every 150 ticks, 2,500 ticks, 4 seeds. Twins hash-equal after every tick; after
/// every fog update the explored bits agree with the visibility bytes and nothing visible is unexplored; every 250 ticks
/// the fog matches the brute-force oracle for both players; the recorded replay round-trips and plays back equal.
/// </summary>
public class FogFuzzStressTests
{
    private const int Ticks = 2500;
    private const int PerSide = 60;
    private readonly ITestOutputHelper _out;

    public FogFuzzStressTests(ITestOutputHelper output) => _out = output;

    private static readonly string[] Army0 = { "malazan_heavy_infantry", "malazan_crossbowman", "malazan_wickan_lancer", "malazan_catapult" };
    private static readonly string[] Army1 = { "whirlwind_raider", "whirlwind_desert_archer", "whirlwind_horse_raider", "whirlwind_zealot" };

    /// <summary>Passable cells of the map by level, ramps in the last list.</summary>
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

    private static Simulation NewSim(ulong seed, ReplayRecorder?[] recorder)
    {
        var sim = new Simulation(TestSim.Config(Seed: seed, PlayerCount: 2, UnitCapacity: 2 * PerSide, CommandCapacity: 4 * PerSide + 16)
);
        if (recorder.Length > 0) recorder[0] = new ReplayRecorder(sim, checkpointInterval: 50);
        List<int>[] cells = Cells(sim.World);
        NavGrid g = sim.World.NavGrid;
        var rng = new SimRng(seed, 97);
        for (int k = 0; k < PerSide; k++)
            for (int side = 0; side < 2; side++)
            {
                // Each side spawns on its own half, on every level and on ramps.
                List<int> list = cells[k % cells.Length];
                int c;
                int tries = 0;
                do c = list[rng.NextInt(0, list.Count)];
                while ((c % g.Width < g.Width / 2) != (side == 0) && ++tries < 50);
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
            int c = list[rng.NextInt(0, list.Count)];
            sim.Enqueue(Command.AttackMove(u.Owner[i], new EntityHandle(i, u.Generation[i]), g.CellCenter(c % g.Width, c / g.Width)));
        }
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    [InlineData(4UL)]
    public void ArmiesAcrossThreeLevels_TwinsHashEqualEveryTick_FogMatchesTheOracle_AndTheReplayPlaysBack(ulong seed)
    {
        var rec = new ReplayRecorder?[1];
        Simulation a = NewSim(seed, rec), b = NewSim(seed, Array.Empty<ReplayRecorder?>());
        World w = a.World;
        List<int>[] cells = Cells(w);
        var rngA = new SimRng(seed, 98);
        var rngB = new SimRng(seed, 98);
        int oracleChecks = 0, revealedTicks = 0, maxExplored = 0;
        for (int t = 0; t < Ticks; t++)
        {
            if (t % 150 == 2)
            {
                Orders(a, cells, ref rngA);
                Orders(b, cells, ref rngB);
            }
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"seed {seed}: twins differ after tick {t}");
            for (int p = 0; p < 2; p++)
                for (int i = 0; i < w.Units.Capacity; i++)
                    if (w.Units.Alive[i] && w.Units.Owner[i] != p && w.Fog.RevealEnd(p, i) > a.TickNumber) revealedTicks++;
            if (!VisionSystem.IsUpdateTick(t)) continue;
            for (int p = 0; p < 2; p++)
            {
                ReadOnlySpan<byte> vis = w.Fog.Visibility(p);
                int explored = 0;
                for (int c = 0; c < vis.Length; c++)
                {
                    Assert.True(vis[c] <= VisionConstants.Visible);
                    if (vis[c] != VisionConstants.Unexplored) explored++;
                }
                maxExplored = Math.Max(maxExplored, explored);
            }
            if (t % 248 == 1) // every 62nd update
            {
                FogMaps.AssertMatchesOracle(w, 0, $"seed {seed} tick {t}");
                FogMaps.AssertMatchesOracle(w, 1, $"seed {seed} tick {t}");
                oracleChecks++;
            }
        }
        _out.WriteLine($"seed {seed}: kills {w.Kills[0]}/{w.Kills[1]}, alive {w.Units.Count}, oracle checks {oracleChecks}, "
            + $"unit-ticks revealed by high ground {revealedTicks}, most cells explored {maxExplored} of {w.Fog.Width * w.Fog.Height}");
        Assert.True(w.Kills[0] + w.Kills[1] > 0, "nobody died: not a fight");
        Replay replay = rec[0]!.ToReplay();
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(ReplayFormat.Write(replay), out Replay? back));
        ReplayResult result = ReplayPlayer.Run(back!, TestSim.Data);
        Assert.True(result.Ok, $"seed {seed}: replay {result.Error} at tick {result.Tick}");
    }
}
