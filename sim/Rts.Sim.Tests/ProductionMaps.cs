using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.Map;

namespace Rts.Sim.Tests;

/// <summary>M3-4 test helpers: shipped production types, and queue / population shortcuts.</summary>
public static class ProductionMaps
{
    /// <summary>Malazan Legion Barracks (3 x 3, trains Heavy Infantry, provides no pop).</summary>
    public static int Barracks => TestSim.Data.FindBuilding("malazan_barracks");

    /// <summary>Malazan Wickan Corral (trains the Lancer, pop 2).</summary>
    public static int Corral => TestSim.Data.FindBuilding("malazan_wickan_corral");

    /// <summary>Malazan Engineers' Yard (trains the Catapult and the locked Sapper).</summary>
    public static int EngineersYard => TestSim.Data.FindBuilding("malazan_engineers_yard");

    /// <summary>Whirlwind Raider Camp (trains the Raider and the locked Zealot).</summary>
    public static int RaiderCamp => TestSim.Data.FindBuilding("whirlwind_raider_camp");

    /// <summary>Whirlwind Town Hall.</summary>
    public static int HolyCamp => TestSim.Data.FindBuilding("whirlwind_holy_camp");

    /// <summary>The Wickan Lancer (pop 2: 4 half-pop).</summary>
    public static int Lancer => TestSim.Data.FindUnit("malazan_wickan_lancer");

    /// <summary>The Sapper, Malazan unique with <c>requires: ["age_ii"]</c>.</summary>
    public static int Sapper => TestSim.Data.FindUnit("malazan_sapper");

    /// <summary>The Zealot, Whirlwind unique with <c>requires: ["age_ii"]</c>.</summary>
    public static int Zealot => TestSim.Data.FindUnit("whirlwind_zealot");

    /// <summary>The Whirlwind Raider.</summary>
    public static int Raider => TestSim.Data.FindUnit("whirlwind_raider");

    /// <summary>The Whirlwind worker.</summary>
    public static int CampFollower => TestSim.Data.FindUnit("whirlwind_camp_follower");

    /// <summary>A point inside building slot <paramref name="k"/>'s footprint (its anchor cell's center).</summary>
    public static Vector2 In(Simulation sim, int k)
    {
        NavGrid g = sim.World.NavGrid;
        int c = sim.World.Buildings.Cell[k];
        return g.CellCenter(c % g.Width, c / g.Width);
    }

    /// <summary>Live unit slots, in slot order.</summary>
    public static int[] LiveUnits(World w)
    {
        var list = new List<int>();
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i]) list.Add(i);
        return list.ToArray();
    }

    /// <summary>The recount of <paramref name="player"/>'s population: live units' half-pop plus started production items' reservations.</summary>
    public static int RecountHalfPop(World w, int player)
    {
        int n = 0;
        UnitStore u = w.Units;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.Owner[i] == player) n += w.Data.Units[u.TypeId[i]].HalfPop;
        BuildingStore b = w.Buildings;
        for (int k = 0; k < b.Capacity; k++)
            if (b.Alive[k] && b.Owner[k] == player && b.Progress[k] > 0 && !b.QueueIsTechAt(k, 0)) n += w.Data.Units[b.QueueTypeAt(k, 0)].HalfPop; // research reserves none (M3-5)
        return n;
    }

    /// <summary>Chebyshev ring of cell (x, y) round the footprint of building slot <paramref name="k"/> (0 inside it).</summary>
    public static int RingOf(Simulation sim, int k, int x, int y)
    {
        BuildingStore b = sim.World.Buildings;
        var def = sim.World.Data.Buildings[b.TypeId[k]];
        int w = sim.World.NavGrid.Width, x0 = b.Cell[k] % w, y0 = b.Cell[k] / w;
        int dx = x < x0 ? x0 - x : x >= x0 + def.FootprintWidth ? x - (x0 + def.FootprintWidth - 1) : 0;
        int dy = y < y0 ? y0 - y : y >= y0 + def.FootprintHeight ? y - (y0 + def.FootprintHeight - 1) : 0;
        return Math.Max(dx, dy);
    }

    /// <summary>Enqueues a command and runs the two ticks after which it has applied.</summary>
    public static void Apply(Simulation sim, Command c)
    {
        sim.Enqueue(c);
        sim.Tick();
        sim.Tick();
    }
}
