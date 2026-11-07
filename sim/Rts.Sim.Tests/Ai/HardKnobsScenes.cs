using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;

namespace Rts.Sim.Tests;

/// <summary>
/// M5-4c slice 2: the scenes of the Hard knobs' tests (<c>focusFire</c>, <c>pullBackWounded</c>) on the flat scene. Player 0
/// (AI, Malazan) knows player 1's hall through a billet of its own placed in sight of it; its army is Heavy Infantry placed
/// by the test; a wave starts at four of them on every difficulty (the tests' own ai.json, rule 12: every opening quiet,
/// <c>waveSizeMultiplier</c> 1). Player 1 (human, Whirlwind) stands still.
/// </summary>
internal static class HardKnobsScenes
{
    internal static int Heavy => TestSim.Data.FindUnit("malazan_heavy_infantry");

    internal static int Raider => TestSim.Data.FindUnit("whirlwind_raider");

    /// <summary>The tests' data: every Malazan opening quiet, a wave at four Heavy Infantry, the wave size x1 on Normal and Hard, no scout, a 5 s cooldown.</summary>
    internal static readonly Lazy<GameData> Data = new(() => AiScenes.DataWith(dir =>
    {
        int half = TestSim.Data.Units[Heavy].HalfPop;
        dir.EditJson("factions/malazan/ai.json", root =>
        {
            foreach (JsonNode? o in root["openings"]!.AsArray())
            {
                o!["buildOrder"] = AiScenes.BuildOrder(("time", 3600, "train", "malazan_laborer", 1));
                o["attack"] = new JsonObject { ["minArmyPop"] = 4 * half / 2.0, ["minArmyCost"] = 0 };
            }
            root["difficulties"]!["normal"]!["waveSizeMultiplier"] = 1;
            root["difficulties"]!["hard"]!["waveSizeMultiplier"] = 1;
        });
        dir.EditJson("common/rules.json", r =>
        {
            r["ai"]!["scoutAfter"] = 3600;
            r["ai"]!["waveCooldown"] = 5;
        });
    }));

    /// <summary>The flat scene for <paramref name="difficulty"/>: player 0's billet in sight of player 1's hall, nothing to spend.</summary>
    internal static Simulation Scene(AiDifficulty difficulty, bool combat, GameData? data = null)
    {
        Simulation sim = AiScenes.Flat(AiScenes.Seats(difficulty, SeatController.Ai, SeatController.Human), data ?? Data.Value, combat: combat);
        World w = sim.World;
        Assert.True(w.Buildings.Spawn(0, w.Data.FindBuilding("malazan_billet"), 31 * AiScenes.FlatSize + 47, out _));
        BuildMaps.SetTotals(sim, 0, 0, 0); // nothing trained meanwhile
        return sim;
    }

    /// <summary><paramref name="n"/> units of <paramref name="type"/> (Heavy Infantry by default) of player <paramref name="owner"/> from cell (x, y), six a row.</summary>
    internal static List<EntityHandle> Army(Simulation sim, int n, int x, int y, int owner = 0, int type = -1)
    {
        var list = new List<EntityHandle>();
        for (int k = 0; k < n; k++)
            list.Add(CombatScenes.Place(sim, owner, type < 0 ? Heavy : type, sim.World.NavGrid.CellCenter(x + k % 6, y + k / 6)));
        return list;
    }

    /// <summary>
    /// The focus scene: four Heavy Infantry west of the middle, and three of player 1's Raiders in their way (cells (36, 27),
    /// (36, 30), (36, 33), outside player 0's defend radius) at 100, 40 and 70 hp.
    /// </summary>
    internal static (Simulation Sim, List<EntityHandle> Members, EntityHandle[] Raiders) Focus(AiDifficulty difficulty, bool combat = true)
    {
        Simulation sim = Scene(difficulty, combat);
        List<EntityHandle> members = Army(sim, 4, 18, 28);
        var raiders = new EntityHandle[3];
        int[] hp = { 100, 40, 70 };
        for (int k = 0; k < 3; k++)
        {
            raiders[k] = CombatScenes.Place(sim, 1, Raider, sim.World.NavGrid.CellCenter(36, 27 + 3 * k));
            sim.World.Units.Hp[raiders[k].Index] = hp[k];
        }
        return (sim, members, raiders);
    }

    /// <summary>Ticks to the next think of player 0 and through it (its commands are logged by then).</summary>
    internal static void NextThink(Simulation sim)
    {
        int think = sim.AiOf(0)!.ThinkTicks;
        do sim.Tick(); while ((sim.TickNumber - 1) % think != 0);
    }

    /// <summary>A digest of a command log: every field the AI sets, in order (a pin that the log is the one the code made before).</summary>
    internal static string Digest(IEnumerable<Command> log)
    {
        var h = new StateHasher();
        int n = 0;
        foreach (Command c in log)
        {
            h.Add((int)c.Kind);
            h.Add(c.Player);
            h.Add(c.Tick);
            h.Add(c.Unit.Index);
            h.Add(c.Unit.Generation);
            h.Add(c.Target.Index);
            h.Add(c.Target.Generation);
            h.Add(c.TargetIsBuilding);
            h.Add(c.Position);
            h.Add(c.TypeId);
            n++;
        }
        return $"{n} {h.Value:X16}";
    }
}
