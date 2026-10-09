using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using static Rts.Sim.Tests.GatherMaps;

namespace Rts.Sim.Tests;

/// <summary>M3-3 test helpers: shipped building types, resources, and the site a Build placed.</summary>
public static class BuildMaps
{
    /// <summary>Malazan House slot (2 x 2, 500 hp, 0 / 50, 20 s).</summary>
    public static int House => TestSim.Data.FindBuilding("malazan_billet");

    /// <summary>Whirlwind House slot: another faction's type for player 0.</summary>
    public static int WhirlwindHouse => TestSim.Data.FindBuilding("whirlwind_tent");

    /// <summary>The cell index of (x, y).</summary>
    public static int Cell(Simulation sim, int x, int y) => y * sim.World.NavGrid.Width + x;

    /// <summary>Gives <paramref name="player"/> extra gold and wood.</summary>
    public static void Give(Simulation sim, int player, int gold, int wood)
    {
        sim.World.AddToTotal(player, ResourceKind.Gold, gold);
        sim.World.AddToTotal(player, ResourceKind.Wood, wood);
    }

    /// <summary>Sets <paramref name="player"/>'s totals to exactly these amounts.</summary>
    public static void SetTotals(Simulation sim, int player, int gold, int wood) =>
        Give(sim, player, gold - sim.World.Gold[player], wood - sim.World.Wood[player]);

    /// <summary>Slot of the live building anchored at (x, y), or -1.</summary>
    public static int SiteAt(Simulation sim, int x, int y)
    {
        BuildingStore b = sim.World.Buildings;
        for (int k = 0; k < b.Capacity; k++)
            if (b.Alive[k] && b.Cell[k] == Cell(sim, x, y)) return k;
        return -1;
    }

    /// <summary>Workers on the cells 4-adjacent to a footprint at (x, y), all in reach of it, at most <paramref name="n"/>.</summary>
    public static EntityHandle[] WorkersRound(Simulation sim, int x, int y, int fw, int fh, int n, int player = 0)
    {
        var cells = new List<(int X, int Y)>();
        for (int k = 0; k < fw; k++) { cells.Add((x + k, y - 1)); cells.Add((x + k, y + fh)); }
        for (int k = 0; k < fh; k++) { cells.Add((x - 1, y + k)); cells.Add((x + fw, y + k)); }
        return cells.Take(n).Select(c => Unit(sim, At(sim, c.X, c.Y), player)).ToArray();
    }

    /// <summary>
    /// A sim on a hand-made map with <paramref name="players"/> players, plenty of command room; <paramref name="combat"/>
    /// false: no fights (BUG-0135). The map starts explored for every player (M4-3b, <see cref="TestSim.Explored"/>): these
    /// scenes build anywhere on it, and the explored rule has its own rows (<c>PlacementTests</c>).
    /// </summary>
    public static Simulation NewSim(Heightmap map, int units = 32, int players = 1, bool combat = true) =>
        TestSim.Explored(new(TestSim.Config(Seed: 5, PlayerCount: players, UnitCapacity: units, CommandCapacity: 512) with { Combat = combat }, map));
}
