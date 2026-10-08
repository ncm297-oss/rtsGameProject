using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Vision;
using Xunit.Abstractions;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests;

/// <summary>
/// M4-3a criterion 3: the high-ground rule in the stamp (docs/02 "High ground"): low ground can't see up, high ground sees
/// down, the 4 m lip is seen from any level, a ramp counts as its lower level; and the brute-force oracle agrees on every
/// cell of generated 3-level maps.
/// </summary>
public class HighGroundVisionTests
{
    private readonly ITestOutputHelper _out;

    public HighGroundVisionTests(ITestOutputHelper output) => _out = output;

    private static int C(int x, int y) => y * FogMaps.Size + x;

    /// <summary>One player-0 Crossbowman (sight 18) at cell (x, y) of the two-level map, after the first update.</summary>
    private static World Viewer(int x, int y)
    {
        Simulation sim = FogMaps.Sim(FogMaps.TwoLevel(), combat: false);
        Place(sim, 0, Crossbowman, FogMaps.Cell(x, y));
        sim.Tick();
        return sim.World;
    }

    [Fact]
    public void TheHandMap_HasTwoLevels_AndARampThatReportsItsLowerLevel()
    {
        Heightmap hm = FogMaps.TwoLevel();
        Assert.Equal(0, hm.LevelAt(FogMaps.PlateauX - 1, 30));
        Assert.Equal(1, hm.LevelAt(FogMaps.PlateauX, 30));
        for (int x = FogMaps.RampX0; x <= FogMaps.RampX1; x++)
        {
            Assert.True(hm.IsRamp(x, 19));
            Assert.Equal(0, hm.LevelAt(x, 19));
        }
    }

    [Fact]
    public void ALevel0Unit_DoesNotSeeALevel1Cell10mAway()
    {
        World w = Viewer(15, 30); // level 0, 5 cells (10 m) west of the plateau's first column
        Assert.False(w.Fog.IsVisible(0, C(FogMaps.PlateauX, 30)));
        Assert.False(w.Fog.IsExplored(0, C(FogMaps.PlateauX, 30)));
        Assert.True(w.Fog.IsVisible(0, C(FogMaps.PlateauX - 1, 30))); // level 0, 8 m
        Assert.True(w.Fog.IsVisible(0, C(6, 30)));                      // level 0, 18 m west
        Assert.False(w.Fog.IsVisible(0, C(5, 30)));                     // 20 m
        FogMaps.AssertMatchesOracle(w, 0);
    }

    [Fact]
    public void ALevel1Unit_SeesTheLevel0CellsInItsRadius()
    {
        World w = Viewer(24, 30);
        for (int x = 16; x < FogMaps.PlateauX; x++) Assert.True(w.Fog.IsVisible(0, C(x, 30)), $"x {x}");
        Assert.True(w.Fog.IsVisible(0, C(15, 30))); // 18 m
        Assert.False(w.Fog.IsVisible(0, C(14, 30)));
        FogMaps.AssertMatchesOracle(w, 0);
    }

    [Fact]
    public void CellsWithin4m_AreSeenFromAnyLevel()
    {
        World w = Viewer(FogMaps.PlateauX - 1, 30); // level 0, under the cliff
        Assert.True(w.Fog.IsVisible(0, C(FogMaps.PlateauX, 30)));     // 2 m up the cliff
        Assert.True(w.Fog.IsVisible(0, C(FogMaps.PlateauX + 1, 30))); // 4 m: the lip's edge
        Assert.True(w.Fog.IsVisible(0, C(FogMaps.PlateauX, 31)));     // 2.8 m
        Assert.False(w.Fog.IsVisible(0, C(FogMaps.PlateauX + 2, 30))); // 6 m
        Assert.False(w.Fog.IsVisible(0, C(FogMaps.PlateauX + 1, 31))); // 4.5 m
        FogMaps.AssertMatchesOracle(w, 0);
    }

    [Fact]
    public void AUnitOnTheRamp_SeesAsLevel0()
    {
        World w = Viewer(FogMaps.RampX1, 19); // the ramp's top cell: level 0, right under the plateau's edge
        Assert.Equal(0, w.Heightmap.LevelAt(FogMaps.RampX1, 19));
        Assert.True(w.Fog.IsVisible(0, C(FogMaps.PlateauX + 1, 19)));  // the lip
        Assert.False(w.Fog.IsVisible(0, C(FogMaps.PlateauX + 2, 19))); // 6 m onto the plateau: not seen
        Assert.False(w.Fog.IsVisible(0, C(FogMaps.PlateauX + 5, 19)));
        Assert.True(w.Fog.IsVisible(0, C(FogMaps.RampX0, 19)));        // down the ramp
        Assert.True(w.Fog.IsVisible(0, C(FogMaps.RampX1 - 8, 19)));
        FogMaps.AssertMatchesOracle(w, 0);
    }

    [Fact]
    public void AUnitOnThePlateauTop_SeesTheRamp()
    {
        World w = Viewer(FogMaps.PlateauX + 4, 19);
        for (int x = FogMaps.RampX0 + 2; x <= FogMaps.RampX1; x++) // x 15 at row 18 is 18.1 m off
            for (int y = FogMaps.RampY0; y <= FogMaps.RampY1; y++)
                Assert.True(w.Fog.IsVisible(0, C(x, y)), $"ramp cell ({x}, {y})");
        FogMaps.AssertMatchesOracle(w, 0);
    }

    [Fact]
    public void TheHandMap_EveryCellMatchesTheOracle_ForViewersAllOverIt()
    {
        Simulation sim = FogMaps.Sim(FogMaps.TwoLevel(), combat: false, units: 64);
        int n = 0;
        for (int y = 3; y < FogMaps.Size - 3; y += 7)
            for (int x = 2; x < FogMaps.Size - 2; x += 5)
                Place(sim, n++ % 2, n % 3 == 0 ? HeavyInfantry : Crossbowman, FogMaps.Cell(x, y, 0.3f));
        sim.Tick();
        FogMaps.AssertMatchesOracle(sim.World, 0);
        FogMaps.AssertMatchesOracle(sim.World, 1);
    }

    /// <summary>
    /// Generated 3-level maps (the default 128 x 128 generator), 4 seeds: units of both players on every level and on
    /// ramps, plus a watchtower and a keep on the plateaus, match the oracle on every cell for both players; then the
    /// units move and the next update matches again, with every cell seen at either update explored.
    /// </summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    [InlineData(4UL)]
    public void GeneratedThreeLevelMaps_EveryCellMatchesTheOracle(ulong seed)
    {
        var sim = new Simulation(TestSim.ConfigNoCombat(Seed: seed, PlayerCount: 2, UnitCapacity: 200, CommandCapacity: 64));
        World w = sim.World;
        NavGrid g = w.NavGrid;
        var byLevel = new List<int>[MapConstants.LevelCount + 1]; // the last list: ramps
        for (int k = 0; k < byLevel.Length; k++) byLevel[k] = new List<int>();
        for (int c = 0; c < g.Width * g.Height; c++)
        {
            int x = c % g.Width, y = c / g.Width;
            if (!g.IsPassable(x, y)) continue;
            byLevel[w.Heightmap.IsRamp(x, y) ? MapConstants.LevelCount : g.LevelAt(x, y)].Add(c);
        }
        for (int k = 0; k < byLevel.Length; k++) Assert.True(byLevel[k].Count > 0, $"seed {seed}: no cell of class {k}");
        var rng = new Determinism.SimRng(seed, 5);
        int placed = 0;
        foreach (List<int> cells in byLevel)
            for (int k = 0; k < 20; k++)
            {
                int c = cells[rng.NextInt(0, cells.Count)];
                int type = k % 3 == 0 ? HeavyInfantry : k % 3 == 1 ? Crossbowman : Raider;
                Place(sim, k % 2, type, g.CellCenter(c % g.Width, c / g.Width));
                placed++;
            }
        int tower = w.Data.FindBuilding("malazan_watchtower");
        foreach (int level in new[] { 1, 2 })
        {
            int c = byLevel[level][byLevel[level].Count / 2];
            w.Buildings.Spawn(level - 1, tower, c, out _); // may not fit there; the oracle reads what stands
        }
        sim.Tick();
        _out.WriteLine($"seed {seed}: {placed} units, {w.Buildings.Count} buildings; cells per level {byLevel[0].Count} / {byLevel[1].Count} / {byLevel[2].Count}, ramps {byLevel[3].Count}");
        for (int p = 0; p < 2; p++) FogMaps.AssertMatchesOracle(w, p, $"seed {seed}");
        var before = new bool[2][];
        for (int p = 0; p < 2; p++) before[p] = FogMaps.Expected(w, p);
        // Every unit steps to another random cell (a test seam: positions written between ticks), then the next update.
        UnitStore u = w.Units;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            List<int> cells = byLevel[rng.NextInt(0, byLevel.Length)];
            int c = cells[rng.NextInt(0, cells.Count)];
            u.Position[i] = u.PrevPosition[i] = g.CellCenter(c % g.Width, c / g.Width);
        }
        FogMaps.RunThroughNextUpdate(sim);
        for (int p = 0; p < 2; p++)
        {
            FogMaps.AssertMatchesOracle(w, p, $"seed {seed} after the move");
            bool[] now = FogMaps.Expected(w, p);
            for (int c = 0; c < now.Length; c++)
                Assert.Equal(before[p][c] || now[c], w.Fog.IsExplored(p, c));
        }
    }
}
