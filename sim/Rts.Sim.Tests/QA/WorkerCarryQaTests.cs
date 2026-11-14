using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Save;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;
using static Rts.Sim.Tests.ResourceMaps;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA for M7-7 (Request 48, <c>units.json</c> <c>carry</c>): the boundaries the developer's <c>WorkerCarryTests</c> leave
/// out. A carry below the rules' value, a mine with less than a load left, a worker holding more than its own carry, a
/// save taken while a worker holds a load between the rules' value and its own carry, the save refused under data without
/// the field, and one faction's carry never leaking to another faction's (or an ally's) workers in an AI match.
/// Rules 58 / 59: every copy strips <c>carry</c> first (<see cref="UnitCarryLoaderTests.StripCarry"/>) and sets its own,
/// and no literal of <c>rules.json</c> <c>workerCarry</c> is assumed: the values are chosen around the loaded one.
/// </summary>
[Collection(SerialCollection.Name)]
public class WorkerCarryQaTests(ITestOutputHelper output)
{
    private const int KeepX = 8, KeepY = 7, MineX = 16, MineY = 8;

    private static int Rules => TestSim.Data.Rules.WorkerCarry;

    /// <summary>A copy with no unit's carry, then <paramref name="faction"/>'s worker (null: every shipped faction's) at <paramref name="carry"/>.</summary>
    internal static GameData Workers(int carry, string? faction = null) => AiScenes.DataWith(dir =>
    {
        UnitCarryLoaderTests.StripCarry(dir);
        foreach (string f in TestSim.ShippedFactions)
            if (faction == null || f == faction)
                dir.SetUnitField(f, AiScenes.UnitKey(f, UnitSlot.Worker), "carry", carry.ToString());
    });

    private static readonly Lazy<GameData> s_absent = new(() => AiScenes.DataWith(UnitCarryLoaderTests.StripCarry));
    private static readonly Lazy<GameData> s_one = new(() => Workers(1));
    private static readonly Lazy<GameData> s_above = new(() => Workers(Rules + 5));

    private static (Simulation Sim, EntityHandle Worker, EntityHandle Mine) KeepAndMine(GameData data, int mineGold = 2500)
    {
        Simulation sim = new(new SimConfig(5, 1, 32, 144) { Data = data }, Flat(32, 20));
        EntityHandle mine = Spawn(sim.World, data.FindResource("gold_mine"), MineX, MineY, mineGold);
        GatherMaps.Building(sim, KeepX, KeepY, type: data.FindBuilding("malazan_garrison_keep"));
        EntityHandle worker = GatherMaps.Unit(sim, GatherMaps.At(sim, 13, 6), type: data.FindUnit("malazan_laborer"));
        sim.Enqueue(Command.Gather(0, worker, GatherMaps.At(sim, MineX + 1, MineY)));
        return (sim, worker, mine);
    }

    /// <summary>The first deposit: the most carried before it and the gold it paid.</summary>
    private static (int Peak, int Deposit) FirstTrip(Simulation sim, EntityHandle worker)
    {
        World w = sim.World;
        int gold = w.Gold[0], peak = 0;
        for (int t = 0; t < 4000 && w.Gold[0] == gold; t++)
        {
            sim.Tick();
            peak = Math.Max(peak, w.Units.Cargo[worker.Index]);
        }
        Assert.True(w.Gold[0] > gold, "never deposited");
        return (peak, w.Gold[0] - gold);
    }

    /// <summary>A carry of 1 (below the rules' value): every trip is one gold, and a second trip is one more.</summary>
    [Fact]
    public void ACarryOfOne_EveryTripIsOne()
    {
        Assert.True(Rules > 1, "the rules' value is 1: the row does not test 'below'");
        (Simulation sim, EntityHandle worker, _) = KeepAndMine(s_one.Value);
        Assert.Equal((1, 1), FirstTrip(sim, worker));
        Assert.Equal((1, 1), FirstTrip(sim, worker));
    }

    /// <summary>
    /// A mine with fewer than a load left (carry above the rules'): the worker takes what was left and, with no other node
    /// in reach, ends its loop holding it (the depleted-node rule, M3-2), exactly as it does without the field.
    /// </summary>
    [Fact]
    public void AMineWithLessThanALoadLeft_GivesItsLast_AsWithoutTheField()
    {
        int left = Rules - 3;
        (int Cargo, UnitState State, bool MineAlive, int Gold) Run(GameData data)
        {
            (Simulation sim, EntityHandle w, EntityHandle m) = KeepAndMine(data, left);
            GatherMaps.Run(sim, 1500);
            return (sim.World.Units.Cargo[w.Index], sim.World.Units.State[w.Index], sim.World.Resources.IsAlive(m), sim.World.Gold[0]);
        }
        var withCarry = Run(s_above.Value);
        var without = Run(s_absent.Value);
        output.WriteLine($"mine {left}: carry {Rules + 5} -> {withCarry}; absent -> {without}");
        Assert.Equal(without, withCarry);
        Assert.Equal((left, false), (withCarry.Cargo, withCarry.MineAlive));
    }

    /// <summary>
    /// A worker made to hold more than its own carry (12 with carry 1; no data path does it today) and ordered to gather is
    /// full: it walks home with all 12 and the mine loses nothing (no negative take).
    /// </summary>
    [Fact]
    public void AWorkerHoldingMoreThanItsCarry_ReturnsAllOfIt_TheMineUntouched()
    {
        (Simulation sim, EntityHandle worker, EntityHandle mine) = KeepAndMine(s_one.Value);
        UnitStore u = sim.World.Units;
        GatherMaps.Run(sim, 2);
        int mineBefore = sim.World.Resources.Remaining[mine.Index] + u.Cargo[worker.Index];
        u.Cargo[worker.Index] = 12;
        u.CargoKind[worker.Index] = ResourceKind.Gold;
        sim.Enqueue(Command.Gather(0, worker, GatherMaps.At(sim, MineX + 1, MineY)));
        int gold = sim.World.Gold[0];
        for (int t = 0; t < 600 && sim.World.Gold[0] == gold; t++)
        {
            sim.Tick();
            Assert.True(u.Cargo[worker.Index] >= 0, $"negative cargo at tick {sim.TickNumber}");
        }
        Assert.Equal(gold + 12, sim.World.Gold[0]);
        Assert.True(sim.World.Resources.Remaining[mine.Index] <= mineBefore);
    }

    /// <summary>
    /// A save taken while a worker holds a load above the rules' value but below its own carry: the loaded sim keeps
    /// filling to its own carry (does not return at the rules' value), hash-identical to the uninterrupted run every tick
    /// for 2,000 ticks; the same bytes are refused under data without the field.
    /// </summary>
    [Fact]
    public void ASaveBetweenTheRulesValueAndItsOwnCarry_LoadsAndFillsToItsOwn_TheDataWithoutItRefuses()
    {
        GameData data = s_above.Value;
        (Simulation sim, EntityHandle worker, _) = KeepAndMine(data);
        UnitStore u = sim.World.Units;
        for (int t = 0; t < 4000 && u.Cargo[worker.Index] <= Rules; t++) sim.Tick();
        Assert.True(u.Cargo[worker.Index] > Rules && u.Cargo[worker.Index] < Rules + 5, $"cargo {u.Cargo[worker.Index]}");
        byte[] bytes = SaveTestRun.Save(sim);
        Simulation loaded = SaveTestRun.Load(data, bytes);
        Assert.Equal(sim.StateHash(), loaded.StateHash());
        int peak = 0;
        for (int t = 0; t < 2000; t++)
        {
            sim.Tick();
            loaded.Tick();
            Assert.True(sim.StateHash() == loaded.StateHash(), $"diverged at tick {sim.TickNumber}");
            peak = Math.Max(peak, loaded.World.Units.Cargo[worker.Index]);
        }
        Assert.Equal(Rules + 5, peak);
        SaveTestRun.AssertSameState(sim, loaded, "after 2,000 ticks");
        Assert.Equal(SaveError.DataMismatch, SaveTestRun.TryLoad(s_absent.Value, bytes));
    }

    private static readonly Lazy<GameData> s_teblorOnly = new(() => Workers(Rules + 5, "teblor"));

    /// <summary>
    /// A carry on the Teblor worker alone, in a Teblor + Malazan v Whirlwind + Malazan 2v2 of Normal AIs (teams 1 / 2):
    /// every tick no unit carries more than its own type's carry, non-workers carry nothing, the Teblor's loads reach their
    /// carry, and no other faction's worker (the Teblor's ally's included) ever holds more than the rules' value.
    /// </summary>
    [Fact]
    public void ACarryOnOneFaction_InA2v2_NeverLeaksToAnAllyOrAnEnemy()
    {
        GameData d = s_teblorOnly.Value;
        Assert.Equal(Rules + 5, d.CarryOf(d.FindUnit(AiScenes.UnitKey("teblor", UnitSlot.Worker))));
        PlayerSeat[] seats =
        {
            new(d.FindFaction("teblor"), SeatController.Ai, AiDifficulty.Normal, 1),
            new(d.FindFaction("whirlwind"), SeatController.Ai, AiDifficulty.Normal, 2),
            new(d.FindFaction("malazan"), SeatController.Ai, AiDifficulty.Normal, 1),
            new(d.FindFaction("malazan"), SeatController.Ai, AiDifficulty.Normal, 2),
        };
        Simulation sim = AiScenes.Generated(seats, 3, false, out _, data: d);
        World w = sim.World;
        int teblor = d.FindFaction("teblor");
        var peak = new int[seats.Length];
        for (int t = 0; t < 5000; t++)
        {
            sim.Tick();
            UnitStore u = w.Units;
            for (int s = 0; s < u.Capacity; s++)
            {
                if (!u.Alive[s]) continue;
                UnitDef def = d.Units[u.TypeId[s]];
                int c = u.Cargo[s];
                if (def.Slot != UnitSlot.Worker) Assert.True(c == 0, $"tick {sim.TickNumber}: a {def.Key} carries {c}");
                Assert.True(c >= 0 && c <= d.CarryOf(def.Id), $"tick {sim.TickNumber}: a {def.Key} carries {c} of {d.CarryOf(def.Id)}");
                if (def.Faction != teblor) Assert.True(c <= Rules, $"tick {sim.TickNumber}: a {def.Key} (player {u.Owner[s]}) carries {c} above the rules' {Rules}");
                peak[u.Owner[s]] = Math.Max(peak[u.Owner[s]], c);
            }
            for (int p = 0; p < seats.Length; p++) Assert.True(w.Gold[p] >= 0 && w.Wood[p] >= 0);
        }
        output.WriteLine($"peak load per player: {string.Join(" / ", peak)} (Teblor's carry {d.CarryOf(d.FindUnit(AiScenes.UnitKey("teblor", UnitSlot.Worker)))}, rules {Rules})");
        Assert.Equal(Rules + 5, peak[0]);
        Assert.Equal(Rules, peak[2]); // the ally fills to the rules' value, no more
    }

    /// <summary>
    /// Hash ambiguity probe: the carry on the Teblor worker and the same value on the Malazan worker are different files
    /// and hash differently; so do 1 and 2 (the marker is not the value).
    /// </summary>
    [Fact]
    public void TheSameCarry_OnTwoFactionsWorkers_HashesApart()
    {
        ulong teblor = Workers(Rules + 5, "teblor").ContentHash(), malazan = Workers(Rules + 5, "malazan").ContentHash();
        ulong one = Workers(1, "teblor").ContentHash(), two = Workers(2, "teblor").ContentHash();
        Assert.NotEqual(teblor, malazan);
        Assert.NotEqual(one, two);
        Assert.NotEqual(s_absent.Value.ContentHash(), one);
    }

    /// <summary>A carry written as 15.0 or 1.5e1 is the whole number 15 (JSON has one number type); 1e6 is the top of the range.</summary>
    [Theory]
    [InlineData("15.0", 15)]
    [InlineData("1.5e1", 15)]
    [InlineData("1e6", 1_000_000)]
    public void AWholeNumberWrittenAsAFraction_Loads(string json, int want)
    {
        DataLoadResult r = UnitCarryLoaderTests.LoadWith("teblor_gatherer", json);
        Assert.True(r.Ok, string.Join("; ", r.Errors));
        Assert.Equal(want, r.Data!.Units[r.Data.FindUnit("teblor_gatherer")].Carry);
    }

    /// <summary>Two workers with a broken carry are two errors, one at each row (the checker does not stop at the first).</summary>
    [Fact]
    public void TwoBrokenCarries_AreTwoErrors()
    {
        DataLoadResult r;
        using (TestDataDir dir = TestDataDir.CopyOfShipped())
        {
            UnitCarryLoaderTests.StripCarry(dir);
            dir.SetUnitField("teblor", "teblor_gatherer", "carry", "0");
            dir.SetUnitField("malazan", AiScenes.UnitKey("malazan", UnitSlot.Worker), "carry", "null");
            r = DataLoader.LoadAll(dir.Path);
        }
        Assert.False(r.Ok);
        Assert.Equal(2, r.Errors.Count);
        Assert.All(r.Errors, e => Assert.EndsWith(".carry", e.Path));
    }
}
