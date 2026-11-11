using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Replays;

namespace Rts.Sim.Tests.Save;

/// <summary>
/// M7-3b: the two scenes behind the pre-M7-3b refusal fixtures (<c>Replays/pre_m7_3b/</c>). Both hold tech or ability ids
/// as table indexes, the thing M7-3b renumbered: the save a researched faction tech (<c>moranth_supply</c>, id 6 before
/// M7-3b, 7 after) and a Sapper's Cusser on cooldown; the recording a Research command naming <c>melee_weapons_1</c> by id
/// (4 before, 3 after). The fixtures were written with this code on the diff base (de591f0) and are never rewritten.
/// </summary>
internal static class TechIdScenes
{
    /// <summary>Ticks the save scene runs after the cast is ordered.</summary>
    internal const int CastTicks = 60;

    /// <summary>
    /// A small one-player sim on a flat 16 map: a Garrison Keep, a Sapper; <c>age_ii</c> and <c>moranth_supply</c> researched,
    /// then the Sapper casts Cusser and the scene runs <see cref="CastTicks"/> ticks, so the save holds the research flags and
    /// a running cooldown.
    /// </summary>
    internal static Simulation SaveScene()
    {
        GameData d = TestSim.Data;
        var config = new SimConfig(5, 1, 4, 8)
        {
            Data = d, ResourceCapacity = 1, BuildingCapacity = 2, ZoneCapacity = 2, ProjectileCapacity = 4, StatsSampleCapacity = 0,
        };
        var sim = new Simulation(config, LocalMovementTests.Flat(16));
        sim.Enqueue(Command.SpawnBuilding(0, d.FindBuilding("malazan_garrison_keep"), new Vector2(10f, 10f)));
        sim.Tick();
        EntityHandle sapper = CombatScenes.Place(sim, 0, d.FindUnit("malazan_sapper"), new Vector2(20f, 20f));
        ProductionSystem.Researched(sim.World, 0, d.FindTech("age_ii"));
        ProductionSystem.Researched(sim.World, 0, d.FindTech("moranth_supply"));
        sim.Tick();
        sim.Enqueue(Command.UseAbility(0, sapper, 0, new Vector2(24f, 20f)));
        for (int t = 0; t < CastTicks; t++) sim.Tick();
        return sim;
    }

    /// <summary>The Sapper's Cusser ready tick in <paramref name="sim"/> (the save scene's one unit with an ability).</summary>
    internal static int CusserReadyTick(Simulation sim)
    {
        UnitStore u = sim.World.Units;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.TypeId[i] == sim.World.Data.FindUnit("malazan_sapper"))
                return u.AbilityReadyTick[i * DataLimits.MaxUnitAbilities];
        return -1;
    }

    /// <summary>
    /// A two-player recording on the default test map, 200 ticks, checkpoints every 50: a Malazan Armory (the forge) spawned
    /// for player 0, then a Research of <c>melee_weapons_1</c> at it. The recording carries the command's tech id whether or
    /// not the research is accepted, which is what the refusal is about.
    /// </summary>
    internal static Replay RecordResearch()
    {
        GameData d = TestSim.Data;
        var sim = new Simulation(TestSim.Config(Seed: 3, PlayerCount: 2, UnitCapacity: 16, CommandCapacity: 32));
        var recorder = new ReplayRecorder(sim, checkpointInterval: 50);
        Vector2 at = MoveScenario.Center(sim.World.NavGrid, MoveScenario.CentralCell(sim.World.NavGrid)) + new Vector2(MapConstants.CellSize / 2);
        sim.Enqueue(Command.SpawnBuilding(0, d.FindBuilding("malazan_armory"), at));
        sim.Tick();
        sim.Enqueue(Command.Research(0, at, d.FindTech("melee_weapons_1")));
        while (sim.TickNumber < 200) sim.Tick();
        return recorder.ToReplay();
    }
}
