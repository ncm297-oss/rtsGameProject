using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Entities;
using Rts.Sim.Map;

namespace Rts.Sim.Tests;

/// <summary>M4-1 test helpers: shipped fighter types and small fights on flat hand-made maps.</summary>
public static class CombatScenes
{
    /// <summary>Malazan Heavy Infantry: 130 hp, armor 3, Heavy, 10 melee, cooldown 1.5 s, wind-up 0.3 s, sight 14.</summary>
    public static int HeavyInfantry => TestSim.Data.FindUnit("malazan_heavy_infantry");

    /// <summary>The Malazan worker (melee 4, Light).</summary>
    public static int Laborer => TestSim.Data.FindUnit("malazan_laborer");

    /// <summary>A unit that "can attack" in the priority (ranged) but does not scan or swing until M4-2 (it fires projectiles).</summary>
    public static int Crossbowman => TestSim.Data.FindUnit("malazan_crossbowman");

    /// <summary>A sim on a flat <paramref name="size"/> x <paramref name="size"/> cell map (only the ring blocked).</summary>
    public static Simulation Flat(int size = 48, int units = 64, int players = 2, ulong seed = 1) =>
        new(TestSim.Config(Seed: seed, PlayerCount: players, UnitCapacity: units, CommandCapacity: 8 * units + 32), LocalMovementTests.Flat(size));

    /// <summary>Spawns units (owner, type, position) in one tick, runs the tick that applies them, and returns their handles in argument order.</summary>
    public static EntityHandle[] Spawn(Simulation sim, params (int Owner, int Type, Vector2 At)[] units)
    {
        UnitStore u = sim.World.Units;
        var alive = (bool[])u.Alive.Clone();
        foreach ((int owner, int type, Vector2 at) in units) sim.Enqueue(Command.SpawnUnit(owner, type, at));
        sim.Tick();
        sim.Tick();
        var handles = new List<EntityHandle>();
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && !alive[i]) handles.Add(new EntityHandle(i, u.Generation[i]));
        Assert.Equal(units.Length, handles.Count);
        // Spawns take ascending free slots in command order, so slot order is argument order.
        return handles.ToArray();
    }

    /// <summary>Allocates a unit straight into the store between ticks (no tick runs), the way production spawns it: for scenes that need exact slots and no scan before the first order.</summary>
    public static EntityHandle Place(Simulation sim, int owner, int type, Vector2 at)
    {
        Assert.True(sim.World.Units.TrySpawn(owner, type, sim.World.Data.Units[type], at, out EntityHandle h));
        return h;
    }

    /// <summary>A point of cell (x, y), offset from its center.</summary>
    public static Vector2 At(Simulation sim, int x, int y, float dx = 0f, float dy = 0f) =>
        sim.World.NavGrid.CellCenter(x, y) + new Vector2(dx, dy);

    /// <summary>Runs ticks until <paramref name="done"/> or <paramref name="max"/> ticks; returns the ticks run.</summary>
    public static int RunUntil(Simulation sim, Func<bool> done, int max)
    {
        int t = 0;
        while (t < max && !done())
        {
            sim.Tick();
            t++;
        }
        return t;
    }

    /// <summary>The Whirlwind Raider (Heavy line infantry, 11 melee).</summary>
    public static int Raider => TestSim.Data.FindUnit("whirlwind_raider");

    /// <summary>
    /// Places <paramref name="perSide"/> Heavy Infantry for player 0 and as many Raiders for player 1 in two blocks
    /// <paramref name="ranks"/> deep (along x) and as wide as that takes (along y), units 1 m apart, the front ranks
    /// <paramref name="gap"/> m apart across <paramref name="center"/> (straight into the store, no tick), then attack-moves
    /// every unit to the far block's center.
    /// </summary>
    public static void FlatBrawl(Simulation sim, int perSide, Vector2 center, float gap, int ranks = 20)
    {
        int columns = ranks;
        var mid = new Vector2[2];
        for (int side = 0; side < 2; side++)
        {
            float dir = side == 0 ? -1f : 1f;
            int rows = (perSide + columns - 1) / columns;
            mid[side] = center + new Vector2(dir * (gap / 2 + (columns - 1) * 0.5f), 0f);
            for (int k = 0; k < perSide; k++)
            {
                int col = k % columns, row = k / columns;
                var at = center + new Vector2(dir * (gap / 2 + col * 1f), (row - (rows - 1) / 2f) * 1f);
                Place(sim, side, side == 0 ? HeavyInfantry : Raider, at);
            }
        }
        UnitStore u = sim.World.Units;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i]) sim.Enqueue(Command.AttackMove(u.Owner[i], new EntityHandle(i, u.Generation[i]), mid[1 - u.Owner[i]]));
    }

    /// <summary>
    /// A brawl on seed <paramref name="seed"/>'s generated default map, all through commands (so a recorder attached in
    /// <paramref name="onCreated"/> sees everything): <paramref name="perSide"/> Heavy Infantry for player 0 on the passable
    /// cells within 14 path cells of the central cell left of it, as many Raiders for player 1 right of it, then (two
    /// ticks later) every unit attack-moved to the other side's half.
    /// </summary>
    public static Simulation MapBrawl(ulong seed, int perSide, Action<Simulation>? onCreated = null)
    {
        var sim = new Simulation(TestSim.Config(Seed: seed, PlayerCount: 2, UnitCapacity: 2 * perSide, CommandCapacity: 4 * perSide + 16));
        onCreated?.Invoke(sim);
        NavGrid g = sim.World.NavGrid;
        int center = MoveScenario.CentralCell(g);
        int cx = center % g.Width;
        Pathfinding.FlowField field = Pathfinding.FlowField.Build(g, center);
        var sides = new[] { new List<int>(), new List<int>() };
        for (int c = 0; c < g.Width * g.Height; c++)
        {
            if (!(field.CostAt(c) <= 14f)) continue;
            if (c % g.Width < cx - 1) sides[0].Add(c);
            else if (c % g.Width > cx + 1) sides[1].Add(c);
        }
        var rng = new Determinism.SimRng(seed, 71);
        for (int k = 0; k < perSide; k++)
        {
            for (int side = 0; side < 2; side++)
            {
                int cell = sides[side][rng.NextInt(0, sides[side].Count)];
                Vector2 corner = MoveScenario.Center(g, cell) - new Vector2(MapConstants.CellSize / 2);
                sim.Enqueue(Command.SpawnUnit(side, side == 0 ? HeavyInfantry : Raider, corner + new Vector2(0.05f + rng.NextFloat() * 1.9f, 0.05f + rng.NextFloat() * 1.9f)));
            }
        }
        sim.Tick();
        sim.Tick();
        UnitStore u = sim.World.Units;
        Vector2 c0 = MoveScenario.Center(g, center);
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i]) sim.Enqueue(Command.AttackMove(u.Owner[i], new EntityHandle(i, u.Generation[i]), c0 + new Vector2(u.Owner[i] == 0 ? 12f : -12f, 0f)));
        return sim;
    }

    /// <summary>
    /// Damage player 1's Heavy Infantry takes from player 0's first hit, with tech <paramref name="techP0"/> researched by player 0
    /// and <paramref name="techP1"/> by player 1 (-1 for none): the two stand in reach of each other on a flat map.
    /// </summary>
    public static int FirstHitDamage(int techP0, int techP1)
    {
        Simulation sim = Flat();
        if (techP0 >= 0) sim.World.Techs.Set(0, techP0, true);
        if (techP1 >= 0) sim.World.Techs.Set(1, techP1, true);
        EntityHandle a = Place(sim, 0, HeavyInfantry, At(sim, 20, 20));
        EntityHandle b = Place(sim, 1, HeavyInfantry, At(sim, 20, 20, dx: 1f));
        UnitStore u = sim.World.Units;
        int full = u.Hp[b.Index];
        RunUntil(sim, () => u.Hp[b.Index] < full, 200);
        Assert.True(u.IsAlive(a) && u.IsAlive(b));
        return full - u.Hp[b.Index];
    }
}
