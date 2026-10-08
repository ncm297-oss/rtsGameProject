using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Entities;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>
/// M4-V1 test scene, all through commands (so twins and recorders see everything): on a flat 48 x 48 map, 20 Malazan Heavy
/// Infantry (player 0) and 20 Whirlwind Raiders (player 1) facing each other across the centre, a Billet behind player 0's
/// line and a Tent behind player 1's, then every unit attack-moved to the far building. Whoever wins the brawl walks on and
/// knocks the other side's building down, so the run has unit deaths and a building death.
/// </summary>
internal static class CombatViewScene
{
    public const int PerSide = 20;

    /// <summary>A flat map sim sized for the scene.</summary>
    public static Simulation Create(ulong seed = 1) =>
        new(TestSim.Config(Seed: seed, PlayerCount: 2, UnitCapacity: 64, CommandCapacity: 512), LocalMovementTests.Flat(48));

    /// <summary>The commands that spawn the armies and buildings (tick 1).</summary>
    public static Command[] Spawns(Simulation sim)
    {
        var data = sim.World.Data;
        int hi = data.FindUnit("malazan_heavy_infantry"), raider = data.FindUnit("whirlwind_raider");
        var list = new List<Command>
        {
            Command.SpawnBuilding(0, data.FindBuilding("malazan_billet"), BuildingAt(0)),
            Command.SpawnBuilding(1, data.FindBuilding("whirlwind_tent"), BuildingAt(1)),
        };
        for (int k = 0; k < PerSide; k++)
        {
            float y = 48f + (k % 10 - 4.5f) * 1.4f;
            float depth = k / 10 * 1.4f;
            list.Add(Command.SpawnUnit(0, hi, new Vector2(40f - depth, y)));
            list.Add(Command.SpawnUnit(1, raider, new Vector2(56f + depth, y)));
        }
        return list.ToArray();
    }

    /// <summary>Where each side's building stands (its footprint centre near here).</summary>
    public static Vector2 BuildingAt(int player) => new(player == 0 ? 20f : 76f, 48f);

    /// <summary>One attack-move per live unit, to the other side's building.</summary>
    public static Command[] Orders(Simulation sim)
    {
        UnitStore u = sim.World.Units;
        var list = new List<Command>();
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            int p = u.Owner[i];
            Vector2 goal = BuildingAt(1 - p) + new Vector2(p == 0 ? -4f : 4f, 0f);
            list.Add(Command.AttackMove(p, new EntityHandle(i, u.Generation[i]), goal));
        }
        return list.ToArray();
    }

    /// <summary>Enqueues every command into each sim, spawns, runs the two spawn ticks, then orders.</summary>
    public static void Start(params Simulation[] sims)
    {
        Command[] spawns = Spawns(sims[0]);
        foreach (Simulation s in sims)
        {
            foreach (Command c in spawns) s.Enqueue(c);
            s.Tick();
            s.Tick();
        }
        Command[] orders = Orders(sims[0]);
        foreach (Simulation s in sims)
            foreach (Command c in orders) s.Enqueue(c);
    }
}
