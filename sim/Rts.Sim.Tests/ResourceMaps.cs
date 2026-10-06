using Rts.Sim.Entities;
using Rts.Sim.Map;

namespace Rts.Sim.Tests;

/// <summary>M3-1 test helpers: hand-made maps and resource node shortcuts.</summary>
public static class ResourceMaps
{
    /// <summary>The shipped <c>tree</c> type id.</summary>
    public static int Tree => TestSim.Data.FindResource("tree");

    /// <summary>The shipped <c>gold_mine</c> type id.</summary>
    public static int Mine => TestSim.Data.FindResource("gold_mine");

    /// <summary>Wood in a placed tree (rules.json <c>treeWood</c>).</summary>
    public static int TreeWood => TestSim.Data.Rules.TreeWood;

    /// <summary>A heightmap from rows of chars: digit = plateau level, 'r' = ramp from level 0 halfway up to 1.</summary>
    public static Heightmap FromRows(params string[] rows)
    {
        int w = rows[0].Length, h = rows.Length;
        var levels = new byte[w * h];
        var elevations = new float[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                char c = rows[y][x];
                int i = y * w + x;
                levels[i] = c == 'r' ? (byte)0 : (byte)(c - '0');
                elevations[i] = c == 'r' ? MapConstants.LevelHeight / 2 : levels[i] * MapConstants.LevelHeight;
            }
        }
        return new Heightmap(w, h, levels, elevations);
    }

    /// <summary>A flat level-0 map (the border ring is blocked by the nav grid).</summary>
    public static Heightmap Flat(int width, int height) =>
        new(width, height, new byte[width * height], new float[width * height]);

    /// <summary>A one-player sim on a hand-made map.</summary>
    public static Simulation NewSim(Heightmap map, int units = 8, int resourceCapacity = ResourceStore.DefaultCapacity, ulong seed = 5) =>
        new(TestSim.Config(Seed: seed, PlayerCount: 1, UnitCapacity: units, CommandCapacity: 64) with { ResourceCapacity = resourceCapacity }, map);

    /// <summary>Spawns a node and asserts it was accepted.</summary>
    public static EntityHandle Spawn(World w, int type, int x, int y, int amount)
    {
        Assert.True(w.Resources.Spawn(type, y * w.NavGrid.Width + x, amount, out EntityHandle h), $"spawn of type {type} at ({x}, {y}) refused");
        return h;
    }
}
