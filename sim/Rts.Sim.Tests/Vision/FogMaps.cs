using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Vision;

namespace Rts.Sim.Tests;

/// <summary>M4-3a test helpers: a hand-built two-level map with a ramp, and the brute-force fog oracle.</summary>
public static class FogMaps
{
    /// <summary>Width and height of <see cref="TwoLevel"/> in cells.</summary>
    public const int Size = 40;

    /// <summary>First column of <see cref="TwoLevel"/>'s level-1 plateau (x &gt;= this).</summary>
    public const int PlateauX = 20;

    /// <summary>The ramp's columns (level 0, rising toward the plateau) and rows.</summary>
    public const int RampX0 = 14, RampX1 = 19, RampY0 = 18, RampY1 = 20;

    /// <summary>
    /// A 40 x 40 map: level 0 for x &lt; 20, level 1 for x &gt;= 20 (a cliff between), and a 3-wide ramp at x 14-19,
    /// y 18-20 that climbs from level 0 to just under level 1 (its cells report level 0, docs/02). The outer ring is blocked.
    /// </summary>
    public static Heightmap TwoLevel()
    {
        var levels = new byte[Size * Size];
        var elevations = new float[Size * Size];
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                int c = y * Size + x;
                levels[c] = (byte)(x >= PlateauX ? 1 : 0);
                elevations[c] = levels[c] * MapConstants.LevelHeight;
                if (x >= RampX0 && x <= RampX1 && y >= RampY0 && y <= RampY1)
                    elevations[c] = MapConstants.LevelHeight * (x - RampX0 + 1) / (RampX1 - RampX0 + 2);
            }
        return new Heightmap(Size, Size, levels, elevations);
    }

    /// <summary>A sim on <paramref name="map"/> with the shipped data; combat on or off.</summary>
    public static Simulation Sim(Heightmap map, bool combat = true, int units = 64, int players = 2) =>
        new(TestSim.Config(Seed: 1, PlayerCount: players, UnitCapacity: units, CommandCapacity: 8 * units + 32) with { Combat = combat }, map);

    /// <summary>The centre of cell (x, y) in meters.</summary>
    public static Vector2 Cell(int x, int y) => new((x + 0.5f) * MapConstants.CellSize, (y + 0.5f) * MapConstants.CellSize);

    /// <summary>The centre of cell (x, y) moved <paramref name="dx"/> m along x (inside the cell for |dx| &lt; 1).</summary>
    public static Vector2 Cell(int x, int y, float dx) => Cell(x, y) + new Vector2(dx, 0f);

    /// <summary>
    /// The brute-force oracle (docs/02 "Vision and fog of war", "High ground"): for <paramref name="player"/>, every cell
    /// whose centre is within a live own unit's sight (or building's) of that viewer's cell centre (a building: the cell
    /// holding its footprint's centre), and at or below the viewer's level or within 4 m. Float distances, every viewer
    /// against every cell: an independent implementation of the stamp.
    /// </summary>
    public static bool[] Expected(World w, int player)
    {
        Heightmap hm = w.Heightmap;
        int width = hm.Width, height = hm.Height;
        var seen = new bool[width * height];
        UnitStore u = w.Units;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || u.Owner[i] != player) continue;
            int sx = Math.Clamp((int)(u.Position[i].X / MapConstants.CellSize), 0, width - 1);
            int sy = Math.Clamp((int)(u.Position[i].Y / MapConstants.CellSize), 0, height - 1);
            Mark(hm, seen, sx, sy, w.Data.Units[u.TypeId[i]].Sight);
        }
        BuildingStore b = w.Buildings;
        for (int j = 0; j < b.Capacity; j++)
        {
            if (!b.Alive[j] || b.Owner[j] != player) continue;
            BuildingDef d = w.Data.Buildings[b.TypeId[j]];
            Mark(hm, seen, b.Cell[j] % width + d.FootprintWidth / 2, b.Cell[j] / width + d.FootprintHeight / 2, d.Sight);
        }
        return seen;
    }

    private static void Mark(Heightmap hm, bool[] seen, int sx, int sy, float sight)
    {
        int viewer = hm.LevelAt(sx, sy);
        Vector2 from = Cell(sx, sy);
        for (int y = 0; y < hm.Height; y++)
            for (int x = 0; x < hm.Width; x++)
            {
                float d = Vector2.Distance(from, Cell(x, y));
                if (d <= sight && (hm.LevelAt(x, y) <= viewer || d <= VisionConstants.LipRadius)) seen[y * hm.Width + x] = true;
            }
    }

    /// <summary>Asserts <paramref name="player"/>'s visible cells are exactly the oracle's; the message names the first few that differ.</summary>
    public static void AssertMatchesOracle(World w, int player, string context = "")
    {
        bool[] expected = Expected(w, player);
        ReadOnlySpan<byte> vis = w.Fog.Visibility(player);
        var wrong = new List<string>();
        int visible = 0;
        for (int c = 0; c < expected.Length; c++)
        {
            bool actual = vis[c] == VisionConstants.Visible;
            if (actual) visible++;
            if (actual != expected[c] && wrong.Count < 8)
                wrong.Add($"({c % w.Heightmap.Width}, {c / w.Heightmap.Width}) level {w.Heightmap.Levels[c]}: fog {vis[c]}, oracle {(expected[c] ? "visible" : "not visible")}");
        }
        Assert.True(wrong.Count == 0, $"{context} player {player}: {string.Join("; ", wrong)}");
    }

    /// <summary>Runs ticks (at least one) until the tick just run was a fog update tick (<see cref="VisionSystem.IsUpdateTick"/>).</summary>
    public static void RunThroughNextUpdate(Simulation sim)
    {
        do sim.Tick();
        while (!VisionSystem.IsUpdateTick(sim.TickNumber - 1));
    }

    /// <summary>Number of cells of <paramref name="player"/>'s fog in state <paramref name="state"/>.</summary>
    public static int Count(World w, int player, byte state)
    {
        int n = 0;
        foreach (byte v in w.Fog.Visibility(player)) if (v == state) n++;
        return n;
    }
}
