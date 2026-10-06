using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.Map;

namespace Rts.Sim.Tests;

/// <summary>M3-2 test helpers: workers, Town Halls and gather orders on hand-made maps.</summary>
public static class GatherMaps
{
    /// <summary>The shipped Malazan worker type id.</summary>
    public static int Laborer => TestSim.Data.FindUnit("malazan_laborer");

    /// <summary>A shipped non-worker type id.</summary>
    public static int Infantry => TestSim.Data.FindUnit("malazan_heavy_infantry");

    /// <summary>The shipped Malazan Town Hall type id.</summary>
    public static int Keep => TestSim.Data.FindBuilding("malazan_garrison_keep");

    /// <summary>Gold per tick in reach (rules.json, converted).</summary>
    public static float GoldPerTick => TestSim.Data.Rules.GoldPerTick;

    /// <summary>A sim on a hand-made map with room for <paramref name="units"/> units.</summary>
    public static Simulation NewSim(Heightmap map, int units = 32, int players = 1, ulong seed = 5) =>
        new(TestSim.Config(Seed: seed, PlayerCount: players, UnitCapacity: units, CommandCapacity: 4 * units + 16), map);

    /// <summary>World point of cell (x, y)'s center.</summary>
    public static Vector2 At(Simulation sim, int x, int y) => sim.World.NavGrid.CellCenter(x, y);

    /// <summary>Places a building with its anchor at cell (x, y) through the command, runs two ticks, and asserts it stands.</summary>
    public static EntityHandle Building(Simulation sim, int x, int y, int player = 0, int type = -1)
    {
        BuildingStore b = sim.World.Buildings;
        int before = b.Count;
        sim.Enqueue(Command.SpawnBuilding(player, type < 0 ? Keep : type, At(sim, x, y)));
        Run(sim, 2); // a command applies in the tick after the one running when it was queued
        Assert.Equal(before + 1, b.Count);
        int anchor = y * sim.World.NavGrid.Width + x;
        for (int k = 0; k < b.Capacity; k++)
            if (b.Alive[k] && b.Cell[k] == anchor) return b.HandleOf(k);
        throw new InvalidOperationException("building not found");
    }

    /// <summary>Spawns one unit at <paramref name="pos"/>, runs two ticks, and returns its handle.</summary>
    public static EntityHandle Unit(Simulation sim, Vector2 pos, int player = 0, int type = -1)
    {
        UnitStore u = sim.World.Units;
        var alive = new bool[u.Capacity];
        Array.Copy(u.Alive, alive, u.Capacity);
        sim.Enqueue(Command.SpawnUnit(player, type < 0 ? Laborer : type, pos));
        Run(sim, 2);
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && !alive[i]) return new EntityHandle(i, u.Generation[i]);
        throw new InvalidOperationException("spawn refused");
    }

    /// <summary>Runs <paramref name="ticks"/> ticks.</summary>
    public static void Run(Simulation sim, int ticks)
    {
        for (int t = 0; t < ticks; t++) sim.Tick();
    }

    /// <summary>Distance (m) from a point to a footprint rectangle at <paramref name="anchor"/>.</summary>
    public static float DistanceToFootprint(NavGrid g, Vector2 p, int anchor, int fw, int fh)
    {
        float cs = MapConstants.CellSize, x0 = anchor % g.Width * cs, y0 = anchor / g.Width * cs;
        float dx = MathF.Max(MathF.Max(x0 - p.X, p.X - (x0 + fw * cs)), 0f);
        float dy = MathF.Max(MathF.Max(y0 - p.Y, p.Y - (y0 + fh * cs)), 0f);
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>Sum per kind (gold 0, wood 1) of every player's total, every unit's cargo and every node's remaining amount: constant while only gathering moves resources.</summary>
    public static (long Gold, long Wood) Conserved(World w)
    {
        long gold = 0, wood = 0;
        for (int p = 0; p < w.Gold.Length; p++)
        {
            gold += w.Gold[p];
            wood += w.Wood[p];
        }
        UnitStore u = w.Units;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            if (u.CargoKind[i] == Data.ResourceKind.Gold) gold += u.Cargo[i];
            else wood += u.Cargo[i];
        }
        ResourceStore r = w.Resources;
        for (int n = 0; n < r.Capacity; n++)
        {
            if (!r.Alive[n]) continue;
            if (w.Data.Resources[r.TypeId[n]].Resource == Data.ResourceKind.Gold) gold += r.Remaining[n];
            else wood += r.Remaining[n];
        }
        return (gold, wood);
    }
}
