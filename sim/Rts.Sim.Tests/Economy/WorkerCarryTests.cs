using System.Numerics;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Save;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;
using static Rts.Sim.Tests.ResourceMaps;

namespace Rts.Sim.Tests;

/// <summary>
/// M7-7 (Request 48): a worker type's own <c>carry</c> replaces <c>rules.json</c> <c>workerCarry</c> for that type in the
/// gather loop (fill, return, deposit) and in the AI's full-load test; absent, every worker plays exactly as before (an
/// explicit carry equal to the rules' value plays hash-identical tick by tick to none); nothing new is saved; no allocation.
/// Rules 58 / 59: every copy strips the field first (<see cref="UnitCarryLoaderTests.StripCarry"/>) and sets its own.
/// </summary>
[Collection(SerialCollection.Name)]
public class WorkerCarryTests(ITestOutputHelper output)
{
    // 32 x 20 flat map: a Garrison Keep on cells 8-11 x 7-10 and a 2 x 2 mine on cells 16-17 x 8-9 (as GatherLoopTests).
    private const int KeepX = 8, KeepY = 7, MineX = 16, MineY = 8;

    /// <summary>A copy with no unit's carry, then every shipped faction's worker at <paramref name="carry"/> (null: left out).</summary>
    private static GameData Workers(int? carry) => AiScenes.DataWith(dir =>
    {
        UnitCarryLoaderTests.StripCarry(dir);
        if (carry == null) return;
        foreach (string f in TestSim.ShippedFactions)
            dir.SetUnitField(f, AiScenes.UnitKey(f, UnitSlot.Worker), "carry", carry.Value.ToString());
    });

    private static readonly Lazy<GameData> s_fifteen = new(() => Workers(15));
    private static readonly Lazy<GameData> s_absent = new(() => Workers(null));
    private static readonly Lazy<GameData> s_rules = new(() => Workers(TestSim.Data.Rules.WorkerCarry));

    private static (Simulation Sim, EntityHandle Worker, EntityHandle Mine) KeepAndMine(GameData data)
    {
        Simulation sim = new(new SimConfig(5, 1, 32, 144) { Data = data }, Flat(32, 20));
        EntityHandle mine = Spawn(sim.World, data.FindResource("gold_mine"), MineX, MineY, 2500);
        GatherMaps.Building(sim, KeepX, KeepY, type: data.FindBuilding("malazan_garrison_keep"));
        EntityHandle worker = GatherMaps.Unit(sim, GatherMaps.At(sim, 13, 6), type: data.FindUnit("malazan_laborer"));
        sim.Enqueue(Command.Gather(0, worker, GatherMaps.At(sim, MineX + 1, MineY)));
        return (sim, worker, mine);
    }

    /// <summary>The worker's first load: the most it carried before its first deposit, the gold that deposit paid, and the mine after it.</summary>
    private static (int Peak, int Deposit, int MineLeft) FirstTrip(GameData data)
    {
        (Simulation sim, EntityHandle worker, EntityHandle mine) = KeepAndMine(data);
        World w = sim.World;
        int gold = w.Gold[0], peak = 0;
        for (int t = 0; t < 3000 && w.Gold[0] == gold; t++)
        {
            sim.Tick();
            peak = Math.Max(peak, w.Units.Cargo[worker.Index]);
        }
        Assert.True(w.Gold[0] > gold, "never deposited");
        return (peak, w.Gold[0] - gold, w.Resources.Remaining[mine.Index]);
    }

    [Fact]
    public void AWorkerWithItsOwnCarry_FillsToIt_AndDepositsIt()
    {
        (int peak, int deposit, int left) = FirstTrip(s_fifteen.Value);
        output.WriteLine($"carry 15: peak {peak}, deposit {deposit}, mine left {left}");
        Assert.Equal((15, 15, 2500 - 15), (peak, deposit, left));
    }

    [Fact]
    public void AWorkerWithoutOne_CarriesTheRulesValue()
    {
        int rules = s_absent.Value.Rules.WorkerCarry;
        Assert.Equal((rules, rules, 2500 - rules), FirstTrip(s_absent.Value));
        Assert.Equal((rules, rules, 2500 - rules), FirstTrip(s_rules.Value));
    }

    /// <summary>A Gather order given to a worker already holding a load at its own carry (above the rules') sends it home at once, as a full one did.</summary>
    [Fact]
    public void AGatherOrder_ToAWorkerHoldingItsOwnFullLoad_ReturnsIt()
    {
        GameData data = s_fifteen.Value;
        (Simulation sim, EntityHandle worker, _) = KeepAndMine(data);
        UnitStore u = sim.World.Units;
        // M7-H2 (BUG-1341 item 2): the load is picked around the loaded rules' value (was a literal 12 that assumed
        // workerCarry below it): one above the rules' full load, below the type's own carry, so not full.
        int rules = data.Rules.WorkerCarry, own = data.CarryOf(u.TypeId[worker.Index]), held = rules + 1;
        Assert.True(held < own, $"precondition: the rules' workerCarry {rules} leaves no load above it below the copy's carry {own}");
        GatherMaps.Run(sim, 2);
        u.Cargo[worker.Index] = held;
        u.CargoKind[worker.Index] = ResourceKind.Gold;
        sim.Enqueue(Command.Gather(0, worker, GatherMaps.At(sim, MineX + 1, MineY)));
        GatherMaps.Run(sim, 2);
        Assert.NotEqual(UnitState.Returning, u.State[worker.Index]);
        Assert.True(u.Cargo[worker.Index] >= held && u.Cargo[worker.Index] <= own);
        u.Cargo[worker.Index] = own;
        sim.Enqueue(Command.Gather(0, worker, GatherMaps.At(sim, MineX + 1, MineY)));
        int gold = sim.World.Gold[0];
        for (int t = 0; t < 600 && sim.World.Gold[0] == gold; t++) sim.Tick();
        Assert.Equal(gold + own, sim.World.Gold[0]);
    }

    /// <summary>
    /// The Teblor's worker at its own carry (set, not added: a copy whose data already carries the Gatherer's 15 is the same
    /// copy) and every Teblor opening quiet for an hour, so a flat scene's commands are the economy's.
    /// </summary>
    private static readonly Lazy<GameData> s_teblorQuiet = new(() => AiScenes.DataWith(dir =>
    {
        string gatherer = AiScenes.UnitKey("teblor", UnitSlot.Worker);
        UnitCarryLoaderTests.StripCarry(dir);
        dir.SetUnitField("teblor", gatherer, "carry", "15");
        dir.EditJson("factions/teblor/ai.json", root =>
        {
            foreach (System.Text.Json.Nodes.JsonNode? o in root["openings"]!.AsArray())
                AiScenes.SetBuildOrder(o, AiScenes.BuildOrder(("time", 3600, "train", gatherer, 1)));
        });
    }));

    /// <summary>
    /// M7-H2 (BUG-1341 item 3): the AI's full-load test (<c>EconomyManager.FullLoad</c>, reached only through
    /// <c>NoteDroppedGather</c>) reads the worker's own carry. An idle Teblor worker (carry 15) whose last AI Gather went to a
    /// gold mine out of its seat's sight: holding the rules' full load (10, below its own 15) it is not full, so the Gather is
    /// judged dropped and the mine marked; holding 15 it is full, so the Gather is read as one that went home to deposit
    /// first, and the mine is not marked. On the rules' value alone both loads would be full.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheAisFullLoadTest_ReadsTheWorkersOwnCarry(bool ownFullLoad)
    {
        GameData d = s_teblorQuiet.Value;
        PlayerSeat[] seats = { new(d.FindFaction("teblor"), SeatController.Ai, AiDifficulty.Hard), new(d.FindFaction("malazan"), SeatController.Human, AiDifficulty.Hard) };
        Simulation sim = AiScenes.Flat(seats, d);
        World w = sim.World;
        AiPlayer ai = sim.AiOf(0)!;
        sim.Tick(); // tick 0: the first think
        int gold = -1, s = -1;
        while (ai.View.NextResource(ref s, out SeenResource n))
            if (n.Remaining < 0 && d.Resources[n.TypeId].Resource == ResourceKind.Gold) { gold = s; break; }
        Assert.True(gold >= 0, "precondition: a gold mine out of the seat's sight");
        UnitStore u = w.Units;
        int worker = -1;
        for (int i = 0; i < u.Capacity && worker < 0; i++)
            if (u.Alive[i] && u.Owner[i] == 0 && d.Units[u.TypeId[i]].Slot == UnitSlot.Worker && !ai.IsScout(i, u.Generation[i])) worker = i;
        Assert.True(worker >= 0, "precondition: a worker that is not the scout");
        int rules = d.Rules.WorkerCarry, own = d.CarryOf(u.TypeId[worker]);
        Assert.True(rules < own, $"precondition: the rules' workerCarry {rules} is below the Gatherer's own {own}");
        GatherMaps.Run(sim, 1); // the first think's orders apply
        sim.Enqueue(Command.Stop(0, new EntityHandle(worker, u.Generation[worker])));
        while (u.State[worker] != UnitState.Idle && sim.TickNumber < ai.ThinkTicks - 1) sim.Tick();
        output.WriteLine($"stopped by tick {sim.TickNumber}; thinks every {ai.ThinkTicks}");
        Assert.Equal(UnitState.Idle, u.State[worker]);
        Assert.True(sim.TickNumber < ai.ThinkTicks, "precondition: the second think is still to come");
        // As if the AI's last order to it were a Gather of that mine (MarkOrdered), now applied; the load is set.
        ai.Board.UnitGeneration[worker] = u.Generation[worker];
        ai.Board.GatherKind[worker] = EconomyManager.Gold;
        ai.Board.GatherNode[worker] = new EntityHandle(gold, w.Resources.Generation[gold]);
        u.Cargo[worker] = ownFullLoad ? own : rules;
        u.CargoKind[worker] = ResourceKind.Gold;
        while (sim.TickNumber <= ai.ThinkTicks) sim.Tick(); // the second think
        output.WriteLine($"holding {u.Cargo[worker]} of {own} (rules {rules}): mine marked {ai.Board.NodeDropped[gold] != 0}");
        Assert.Equal(ownFullLoad ? 0 : w.Resources.Generation[gold], ai.Board.NodeDropped[gold]);
    }

    private static Simulation Match(GameData data, ulong seed)
    {
        PlayerSeat t = new(data.FindFaction("teblor"), SeatController.Ai, AiDifficulty.Normal);
        PlayerSeat m = new(data.FindFaction("malazan"), SeatController.Ai, AiDifficulty.Normal);
        return AiScenes.Generated(new[] { t, m }, seed, false, out _, data: data);
    }

    /// <summary>
    /// The field absent and every worker carrying the rules' own value play the same Teblor v Malazan AI match (seed 1,
    /// 6,000 ticks) to the same state hash every tick; the content hashes differ (the files do).
    /// </summary>
    [Fact]
    public void TheRulesValue_OnEveryWorker_PlaysHashIdenticalToNoField()
    {
        Assert.NotEqual(s_absent.Value.ContentHash(), s_rules.Value.ContentHash());
        Simulation a = Match(s_absent.Value, 1), b = Match(s_rules.Value, 1);
        for (int t = 0; t < 6000; t++)
        {
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"diverged at tick {a.TickNumber}");
        }
        Assert.True(a.World.GatheredGold[0] > 0 && a.World.GatheredGold[1] > 0);
    }

    /// <summary>With carry 15 the same match's workers carry loads of 15 (never more), and the match plays differently.</summary>
    [Fact]
    public void ACarryOf15_InAnAiMatch_IsCarried_AndChangesTheMatch()
    {
        Simulation a = Match(s_absent.Value, 1), b = Match(s_fifteen.Value, 1);
        int peak = 0, diverged = -1;
        for (int t = 0; t < 3000; t++)
        {
            a.Tick();
            b.Tick();
            if (diverged < 0 && a.StateHash() != b.StateHash()) diverged = b.TickNumber;
            UnitStore u = b.World.Units;
            for (int s = 0; s < u.Capacity; s++) if (u.Alive[s]) peak = Math.Max(peak, u.Cargo[s]);
        }
        output.WriteLine($"peak load {peak}; diverged at tick {diverged}; gold gathered {a.World.GatheredGold[0]} / {b.World.GatheredGold[0]}");
        Assert.Equal(15, peak);
        Assert.True(diverged > 0);
    }

    /// <summary>No new saved state: AiSaveScenes' two-seat AI match on the carry-15 copy saved at ticks 137 and 1,000 loads and runs 2,000 ticks hash-identical.</summary>
    [Fact]
    public void AMidMatchSave_WithACarry_LoadsAndContinuesHashIdentical()
    {
        Assert.Equal(10, SaveFormat.CurrentVersion);
        SaveScene scene = AiSaveScenes.AiMatch(5, 2, AiDifficulty.Normal, data: s_fifteen.Value);
        int peak = 0;
        SaveTestRun.Result r = SaveTestRun.RoundTrip(scene, new[] { 137, 1000 }, after: 2000, observe: sim =>
        {
            UnitStore u = sim.World.Units;
            for (int s = 0; s < u.Capacity; s++) if (u.Alive[s]) peak = Math.Max(peak, u.Cargo[s]);
        });
        output.WriteLine($"compared {r.TicksCompared} ticks; peak load {peak}");
        Assert.True(r.TicksCompared >= 4000);
        Assert.True(peak > TestSim.Data.Rules.WorkerCarry, "no load above the rules' value: the scene does not exercise the carry");
    }

    /// <summary>Rule 5: the gather loop with a worker carry allocates nothing a tick (the keep-and-mine scene, a whole trip).</summary>
    [Fact]
    public void GatherTicks_WithAWorkerCarry_AllocateNothing()
    {
        (Simulation sim, _, _) = KeepAndMine(s_fifteen.Value);
        GatherMaps.Run(sim, 20);
        Action block = () => { for (int t = 0; t < 600; t++) sim.Tick(); };
        AllocationProbe.AssertZero(block, output);
    }
}
