using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M6-S15 C criterion 6 (BUG-1890; docs/01 2026-11-27-1857 (4), docs/03 "Implementation (M6-S15)" C): "spends what it has".
/// A Malazan AI seat (player 0, Normal) on the flat map, every gold mine emptied before tick 0 (it knows no gold node and no
/// worker can be at gold), a finished Barracks and Crossbow Range, an army mix of Heavy Infantry then Crossbowmen (so the
/// pick is the Heavy, 54 gold), a build order whose only step is far off. The pick can never be paid for: production now buys
/// what it can. The first three rows failed on the base d26338d3 (production waited for the Heavy for good); the guards held.
/// </summary>
public class SpendWhatItHasTests
{
    private readonly ITestOutputHelper _out;

    public SpendWhatItHasTests(ITestOutputHelper output) => _out = output;

    private static int Heavy => TestSim.Data.FindUnit("malazan_heavy_infantry");

    private static int Crossbow => TestSim.Data.FindUnit("malazan_crossbowman");

    private static int Laborer => TestSim.Data.FindUnit("malazan_laborer");

    private static JsonArray Mix() => new(
        new JsonObject { ["age"] = 1, ["units"] = new JsonObject { ["malazan_heavy_infantry"] = 35, ["malazan_crossbowman"] = 30 } },
        new JsonObject { ["age"] = 2, ["units"] = new JsonObject { ["malazan_heavy_infantry"] = 35, ["malazan_crossbowman"] = 30 } });

    /// <summary>The Heavy-then-Crossbow mix, a far step only (no reserve).</summary>
    private static readonly Lazy<GameData> s_mix = new(() => AiScenes.DataWithAi("malazan", root =>
    {
        foreach (JsonNode? o in root["openings"]!.AsArray())
        {
            AiScenes.SetBuildOrder(o, AiScenes.BuildOrder(("time", 3600, "build", "malazan_billet", 0)));
            o!["compositions"] = Mix();
        }
    }));

    /// <summary>The mix, a second Barracks step due at once (0 gold / 150 wood: a reserve of wood) and a wave from 1 half-pop at no cost, so a single Heavy lets the build order reserve.</summary>
    private static readonly Lazy<GameData> s_reserve = new(() => AiScenes.DataWithAi("malazan", root =>
    {
        foreach (JsonNode? o in root["openings"]!.AsArray())
        {
            AiScenes.SetBuildOrder(o, AiScenes.BuildOrder(("time", 0, "build", "malazan_barracks", 0)));
            o!["compositions"] = Mix();
            o["attack"] = new JsonObject { ["minArmyPop"] = 1, ["minArmyCost"] = 0 };
        }
        root["difficulties"]!["normal"]!["buildOrderSlack"] = 0; // the step acts (and reserves) from the first think
    }));

    /// <summary>
    /// The scene on <paramref name="data"/>: the flat map, every gold mine emptied (with <paramref name="keepGold"/> one mine
    /// left), a finished Barracks and Crossbow Range and a Crossbowman for seat 0, the bank set; with <paramref name="noHall"/> the Keep freed (no
    /// drop-off left), with <paramref name="heavy"/> one Heavy Infantry by the hall (army for the reserve gate).
    /// </summary>
    internal static Simulation Scene(GameData data, int gold, int wood, out List<Command> log, bool keepGold = false, bool noHall = false, bool heavy = false)
    {
        PlayerSeat[] seats = { new(0, SeatController.Ai, AiDifficulty.Normal), new(1, SeatController.Human, AiDifficulty.Normal) };
        Simulation sim = AiScenes.Flat(seats, data, explored: false); // unexplored: the emptied mines are not remembered
        World w = sim.World;
        bool kept = false;
        for (int i = 0; i < w.Resources.Capacity; i++)
        {
            if (!w.Resources.Alive[i] || data.Resources[w.Resources.TypeId[i]].Resource != ResourceKind.Gold) continue;
            if (keepGold && !kept && w.Resources.Cell[i] % AiScenes.FlatSize < 32)
            {
                kept = true;
                continue;
            }
            w.Resources.Take(w.Resources.HandleOf(i), int.MaxValue);
        }
        Assert.True(w.Buildings.Spawn(0, data.FindBuilding("malazan_barracks"), 40 * AiScenes.FlatSize + 4, out _));
        Assert.True(w.Buildings.Spawn(0, data.FindBuilding("malazan_crossbow_range"), 46 * AiScenes.FlatSize + 4, out _));
        if (noHall)
        {
            // Two Billets for the room the Keep gave, so the house rule does not spend the wood (a staging choice).
            Assert.True(w.Buildings.Spawn(0, data.FindBuilding("malazan_billet"), 52 * AiScenes.FlatSize + 4, out _));
            Assert.True(w.Buildings.Spawn(0, data.FindBuilding("malazan_billet"), 56 * AiScenes.FlatSize + 4, out _));
        }
        if (noHall)
            for (int k = 0; k < w.Buildings.Capacity; k++)
                if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == 0 && w.Buildings.TypeId[k] == data.FindBuilding("malazan_garrison_keep")) Assert.True(w.Buildings.Free(w.Buildings.HandleOf(k)));
        // A Crossbowman alive, so the Heavy is the most under-weighted entry (the pick) from the first think.
        w.Units.Hold[CombatScenes.Place(sim, 0, data.FindUnit("malazan_crossbowman"), w.NavGrid.CellCenter(16, 26)).Index] = true;
        if (heavy) w.Units.Hold[CombatScenes.Place(sim, 0, data.FindUnit("malazan_heavy_infantry"), w.NavGrid.CellCenter(14, 26)).Index] = true;
        w.Ledger.Gold[0] = gold;
        w.Ledger.Wood[0] = wood;
        log = AiScenes.Log(sim, 0);
        return sim;
    }

    private static void Thinks(Simulation sim, int n)
    {
        int think = sim.AiOf(0)!.ThinkTicks;
        for (int k = 0; k < n; k++)
            do sim.Tick(); while ((sim.TickNumber - 1) % think != 0);
    }

    private static string Sent(List<Command> log) => string.Join(", ", log.Where(c => c.Kind is CommandKind.Train or CommandKind.Build).Select(c =>
        $"{c.Kind}:{(c.Kind == CommandKind.Train ? TestSim.Data.Units[c.TypeId].Key : TestSim.Data.Buildings[c.TypeId].Key)}@{c.Tick}"));

    /// <summary>The pick (Heavy) can never be paid for, the bank pays for a Crossbowman: that Train within two thinks.</summary>
    [Fact]
    public void ThePickCanNeverBePaid_TheBankPaysAnotherMixUnit_TrainsItWithinTwoThinks()
    {
        Simulation sim = Scene(s_mix.Value, 40, 100, out List<Command> log);
        int pick = -2;
        sim.AiOf(0)!.ThoughtForTests = a => { if (pick == -2) pick = ProductionManager.Pick(a); };
        Thinks(sim, 2);
        Assert.Equal(Heavy, pick);
        _out.WriteLine(Sent(log));
        Assert.Contains(log, c => c.Kind == CommandKind.Train && c.TypeId == Crossbow);
        Assert.DoesNotContain(log, c => c.Kind == CommandKind.Train && c.TypeId == Heavy);
    }

    /// <summary>The bank (50 gold, no wood) pays for no mix unit but for a Laborer: it trains the Laborer.</summary>
    [Fact]
    public void ThePickCanNeverBePaid_NoMixUnitPayable_TrainsTheWorker()
    {
        Simulation sim = Scene(s_mix.Value, 50, 0, out List<Command> log);
        Thinks(sim, 2);
        _out.WriteLine(Sent(log));
        Assert.Contains(log, c => c.Kind == CommandKind.Train && c.TypeId == Laborer);
        Assert.Equal(1, log.Count(c => c.Kind == CommandKind.Train));
    }

    /// <summary>
    /// Held only by a reserve it can never fill: no hall and no drop-off left, 74 wood (under the Depot's 75, so A2's small
    /// drop-off is not bought), a Barracks step reserving 150 wood; the Crossbowman (36 / 45) fits the bank but not above the
    /// reserve. With no drop-off the reserve can never be filled, so it does not hold the buy.
    /// </summary>
    [Fact]
    public void HeldOnlyByAReserveItCanNeverFill_Trains()
    {
        Simulation sim = Scene(s_reserve.Value, 40, 74, out List<Command> log, noHall: true, heavy: true);
        AiPlayer ai = sim.AiOf(0)!;
        bool reserved = false;
        ai.ThoughtForTests = a => reserved |= a.ReserveStep >= 0;
        Thinks(sim, 2);
        _out.WriteLine(Sent(log));
        Assert.True(reserved, "the Barracks step was the reserved step");
        Assert.Contains(log, c => c.Kind == CommandKind.Train && c.TypeId == Crossbow);
    }

    /// <summary>Guard (green on the base): the same scene with its Keep (a drop-off: the reserve can be filled): the reserve holds the Crossbowman.</summary>
    [Fact]
    public void Guard_AReserveItCanFill_StillHolds()
    {
        Simulation sim = Scene(s_reserve.Value, 40, 74, out List<Command> log, heavy: true);
        AiPlayer ai = sim.AiOf(0)!;
        bool reserved = false;
        ai.ThoughtForTests = a => reserved |= a.ReserveStep >= 0;
        Thinks(sim, 4);
        _out.WriteLine(Sent(log));
        Assert.True(reserved, "the Barracks step was the reserved step");
        Assert.DoesNotContain(log, c => c.Kind == CommandKind.Train);
    }

    /// <summary>Guard (green on the base): a gold mine left (a node of the short kind it may gather): production waits for the Heavy as before.</summary>
    [Fact]
    public void Guard_AKnownNodeOfTheShortKind_WaitsAsToday()
    {
        Simulation sim = Scene(s_mix.Value, 40, 100, out List<Command> log, keepGold: true);
        Thinks(sim, 3);
        _out.WriteLine(Sent(log));
        Assert.DoesNotContain(log, c => c.Kind == CommandKind.Train && c.TypeId != Heavy);
    }

    /// <summary>The determinism twin: the first scene twice, hash-equal every tick for 1,000 ticks.</summary>
    [Fact]
    public void Twin_SameHashes()
    {
        Simulation a = Scene(s_mix.Value, 40, 100, out _), b = Scene(s_mix.Value, 40, 100, out _);
        for (int t = 0; t < 1000; t++)
        {
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"twins diverged at tick {a.TickNumber}");
        }
    }

    /// <summary>A save between two such thinks loads to the same state and plays the same 1,000 ticks.</summary>
    [Fact]
    public void ASaveBetweenTwoThinks_FollowsTheRun()
    {
        Simulation a = Scene(s_mix.Value, 40, 100, out _);
        Thinks(a, 1);
        a.Tick();
        Simulation load = SaveTestRun.Load(s_mix.Value, SaveTestRun.Save(a));
        SaveTestRun.AssertSameState(a, load, "between two thinks");
        for (int t = 0; t < 1000; t++)
        {
            a.Tick();
            load.Tick();
            Assert.True(a.StateHash() == load.StateHash(), $"the load diverged at tick {a.TickNumber}");
        }
    }
}
