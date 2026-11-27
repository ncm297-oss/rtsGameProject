using System.Collections.Immutable;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Xunit.Abstractions;
using static Rts.Sim.Tests.ProductionMaps;

namespace Rts.Sim.Tests;

/// <summary>
/// M8-4 criteria 8-9 (Request 56 G4, docs/03 "Implementation (M8-4)"): a unit type's <c>maxAlive</c> on test data. Counted per
/// player and type: live units of the type (made any way) plus the units its queued items will spawn (items x
/// <c>spawnCount</c>), over all its buildings. A Train is refused with <see cref="TrainError.LimitReached"/> when counted +
/// <c>spawnCount</c> &gt; <c>maxAlive</c>, judged when it applies, charging nothing. A cancel or a death frees a place at once;
/// an item already queued always completes; a unit made another way (an ability's <c>spawn</c>, the dev <c>SpawnUnit</c>)
/// counts but is never refused. The copies: Malazan Heavy Infantry (Age I, pop 1, 54 / 20, 280 ticks) with <c>maxAlive</c> 7
/// (and the test summons of <see cref="SummonTests"/>, which make Heavy Infantry), and a pack copy where it has
/// <c>spawnCount</c> 3 and <c>maxAlive</c> 7. Rule 58: the copies strip every shipped <c>maxAlive</c> first.
/// </summary>
public class UnitLimitTests
{
    internal static readonly Lazy<GameData> s_capped = new(() => AiScenes.DataWith(dir =>
    {
        MaxAliveLoaderTests.Strip(dir);
        SummonTests.AddTo(dir);
        dir.SetUnitField("malazan", MaxAliveLoaderTests.Heavy, "maxAlive", "7");
    }));

    internal static readonly Lazy<GameData> s_pack = new(() => AiScenes.DataWith(dir =>
    {
        MaxAliveLoaderTests.Strip(dir);
        dir.SetUnitField("malazan", MaxAliveLoaderTests.Heavy, "spawnCount", "3");
        dir.SetUnitField("malazan", MaxAliveLoaderTests.Heavy, "maxAlive", "7");
    }));

    private static readonly Lazy<GameData> s_packAtSpawnCount = new(() => AiScenes.DataWith(dir =>
    {
        MaxAliveLoaderTests.Strip(dir);
        dir.SetUnitField("malazan", MaxAliveLoaderTests.Heavy, "spawnCount", "3");
        dir.SetUnitField("malazan", MaxAliveLoaderTests.Heavy, "maxAlive", "3");
    }));

    /// <summary>
    /// A flat 48-cell sim on <paramref name="data"/> with <paramref name="players"/> Malazan players (faction 0), each with a
    /// Keep and two Barracks (row 10 + 14 x player: Keep at x 30, Barracks at x 4 and x 14) and 5,000 gold and wood.
    /// </summary>
    internal static (Simulation Sim, int[,] Barracks) Base(GameData data, int players = 1, int units = 128)
    {
        var seats = new PlayerSeat[players];
        for (int p = 0; p < players; p++) seats[p] = new PlayerSeat(data.FindFaction("malazan"), SeatController.Human, AiDifficulty.Normal);
        var sim = new Simulation(new SimConfig(5, players, units, 512) { Data = data, Seats = seats.ToImmutableArray() }, LocalMovementTests.Flat(48));
        World w = sim.World;
        var barracks = new int[players, 2];
        for (int p = 0; p < players; p++)
        {
            int row = 4 + 14 * p;
            Assert.True(w.Buildings.Spawn(p, data.FindBuilding("malazan_garrison_keep"), row * 48 + 30, out _));
            Assert.True(w.Buildings.Spawn(p, data.FindBuilding("malazan_barracks"), row * 48 + 4, out EntityHandle a));
            Assert.True(w.Buildings.Spawn(p, data.FindBuilding("malazan_barracks"), row * 48 + 14, out EntityHandle b));
            (barracks[p, 0], barracks[p, 1]) = (a.Index, b.Index);
            BuildMaps.SetTotals(sim, p, 5000, 5000);
        }
        return (sim, barracks);
    }

    /// <summary>Runs the two ticks after which a command enqueued now has applied (it is stamped for the next tick).</summary>
    internal static void Apply(Simulation sim)
    {
        sim.Tick();
        sim.Tick();
    }

    internal static int HeavyOf(GameData d) => d.FindUnit(MaxAliveLoaderTests.Heavy);

    /// <summary>Places <paramref name="n"/> live units of <paramref name="type"/> for <paramref name="player"/> straight into the store (counted as production's spawn counts them).</summary>
    internal static void PlaceLive(Simulation sim, int player, int type, int n)
    {
        for (int k = 0; k < n; k++) CombatScenes.Place(sim, player, type, CombatScenes.At(sim, 2 + k % 20, 40 - player * 3 - k / 20));
    }

    /// <summary>The oracle: <paramref name="player"/>'s live units of <paramref name="type"/> plus its queued items of the type x spawnCount, walking the stores.</summary>
    internal static int Recount(World w, int player, int type)
    {
        int n = 0;
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == player && w.Units.TypeId[i] == type) n++;
        BuildingStore b = w.Buildings;
        for (int k = 0; k < b.Capacity; k++)
        {
            if (!b.Alive[k] || b.Owner[k] != player) continue;
            for (int q = 0; q < b.QueueCount[k]; q++)
                if (!b.QueueIsTechAt(k, q) && b.QueueTypeAt(k, q) == type) n += w.Data.Units[type].SpawnCount;
        }
        return n;
    }

    /// <summary>The kept count, and (asserted) that it equals the oracle and the limit is the type's.</summary>
    internal static int Count(World w, int player, int type)
    {
        Assert.True(w.UnitLimit(player, type, out int count, out int limit));
        Assert.Equal(w.Data.Units[type].MaxAlive, limit);
        Assert.Equal(Recount(w, player, type), count);
        return count;
    }

    /// <summary>Records every Train refusal of the sim (its TrainError) in order.</summary>
    internal static List<TrainError> Refusals(Simulation sim)
    {
        var list = new List<TrainError>();
        sim.RefusedForTests = (c, why, detail) => { if (why == CommandRefusal.TrainRefused) list.Add((TrainError)detail); };
        return list;
    }

    [Fact]
    public void TheCopies_CarryTheLimit()
    {
        UnitDef capped = s_capped.Value.Units[HeavyOf(s_capped.Value)], pack = s_pack.Value.Units[HeavyOf(s_pack.Value)];
        Assert.Equal((1, 7), (capped.SpawnCount, capped.MaxAlive));
        Assert.Equal((3, 7), (pack.SpawnCount, pack.MaxAlive));
        Assert.True(capped.RequiresTechs.IsEmpty && capped.RequiresBuildings.IsEmpty, "an Age I unit");
    }

    /// <summary>Criterion 8, the boundary: with 5 alive and 1 queued, the seventh is accepted and the eighth refused with the new reason; nothing is charged for it.</summary>
    [Fact]
    public void TheSeventh_IsAccepted_TheEighth_IsRefused_WithLimitReached()
    {
        GameData d = s_capped.Value;
        (Simulation sim, int[,] bar) = Base(d);
        World w = sim.World;
        int heavy = HeavyOf(d);
        List<TrainError> refused = Refusals(sim);
        PlaceLive(sim, 0, heavy, 5);
        sim.Enqueue(Command.Train(0, In(sim, bar[0, 0]), heavy));
        Apply(sim);
        Assert.Equal(6, Count(w, 0, heavy));
        Assert.True(w.CanTrain(0, bar[0, 1], heavy, out _));
        sim.Enqueue(Command.Train(0, In(sim, bar[0, 1]), heavy)); // the seventh
        Apply(sim);
        Assert.Equal(7, Count(w, 0, heavy));
        Assert.Empty(refused);
        Assert.False(w.CanTrain(0, bar[0, 0], heavy, out TrainError why));
        Assert.Equal(TrainError.LimitReached, why);
        (int gold, int wood, int q0, int q1) = (w.Gold[0], w.Wood[0], w.Buildings.QueueCount[bar[0, 0]], w.Buildings.QueueCount[bar[0, 1]]);
        sim.Enqueue(Command.Train(0, In(sim, bar[0, 0]), heavy)); // the eighth
        Apply(sim);
        Assert.Equal(new[] { TrainError.LimitReached }, refused);
        Assert.Equal((gold, wood, q0, q1), (w.Gold[0], w.Wood[0], w.Buildings.QueueCount[bar[0, 0]], w.Buildings.QueueCount[bar[0, 1]]));
        Assert.Equal(7, Count(w, 0, heavy));
    }

    /// <summary>Criterion 8, a pack: spawnCount 3, limit 7: two items accepted (6), the third refused (9 &gt; 7); limit = spawnCount: one item, then refused.</summary>
    [Fact]
    public void APackOfThree_LimitSeven_TwoItemsAccepted_TheThirdRefused()
    {
        GameData d = s_pack.Value;
        (Simulation sim, int[,] bar) = Base(d);
        World w = sim.World;
        int heavy = HeavyOf(d);
        List<TrainError> refused = Refusals(sim);
        for (int k = 0; k < 3; k++) sim.Enqueue(Command.Train(0, In(sim, bar[0, k % 2]), heavy));
        Apply(sim);
        Assert.Equal(new[] { TrainError.LimitReached }, refused);
        Assert.Equal(6, Count(w, 0, heavy));
        Assert.Equal(2, w.Buildings.QueueCount[bar[0, 0]] + w.Buildings.QueueCount[bar[0, 1]]);
        Assert.Equal(5000 - 2 * 54, w.Gold[0]);

        GameData e = s_packAtSpawnCount.Value;
        (Simulation one, int[,] b2) = Base(e);
        List<TrainError> refusedOne = Refusals(one);
        one.Enqueue(Command.Train(0, In(one, b2[0, 0]), heavy));
        one.Enqueue(Command.Train(0, In(one, b2[0, 1]), heavy));
        Apply(one);
        Assert.Equal(new[] { TrainError.LimitReached }, refusedOne);
        Assert.Equal(3, Count(one.World, 0, heavy));
    }

    /// <summary>Criterion 8: the pack's three units spawn at once and the count does not move at the spawn (the queued item becomes three live units).</summary>
    [Fact]
    public void APacksSpawn_KeepsTheCount_ItemBecomesUnits()
    {
        GameData d = s_pack.Value;
        (Simulation sim, int[,] bar) = Base(d);
        World w = sim.World;
        int heavy = HeavyOf(d);
        sim.Enqueue(Command.Train(0, In(sim, bar[0, 0]), heavy));
        Apply(sim);
        for (int t = 0; t < 400; t++)
        {
            Assert.Equal(3, Count(w, 0, heavy));
            sim.Tick();
        }
        Assert.Equal(0, w.Buildings.QueueCount[bar[0, 0]]);
        Assert.Equal(3, Recount(w, 0, heavy));
    }

    /// <summary>Criterion 8, per player: two players each reach their own limit; one at its limit does not stop the other.</summary>
    [Fact]
    public void TwoPlayers_EachReachTheirOwnLimit()
    {
        GameData d = s_capped.Value;
        (Simulation sim, int[,] bar) = Base(d, players: 2);
        World w = sim.World;
        int heavy = HeavyOf(d);
        List<TrainError> refused = Refusals(sim);
        PlaceLive(sim, 0, heavy, 7);
        PlaceLive(sim, 1, heavy, 4);
        Assert.False(w.CanTrain(0, bar[0, 0], heavy, out TrainError why0));
        Assert.Equal(TrainError.LimitReached, why0);
        for (int k = 0; k < 4; k++) sim.Enqueue(Command.Train(1, In(sim, bar[1, k % 2]), heavy));
        Apply(sim);
        Assert.Equal(new[] { TrainError.LimitReached }, refused); // player 1's fourth: 4 + 3 = 7, the fourth would be 8
        Assert.Equal((7, 7), (Count(w, 0, heavy), Count(w, 1, heavy)));
        Assert.False(w.CanTrain(1, bar[1, 0], heavy, out TrainError why1));
        Assert.Equal(TrainError.LimitReached, why1);
    }

    /// <summary>Criterion 8: a cancel frees a place the same tick: a CancelTrain then a Train applied in one tick is accepted; the other order refuses the Train.</summary>
    [Fact]
    public void ACancel_FreesAPlace_TheSameTick_InCommandOrder()
    {
        GameData d = s_capped.Value;
        (Simulation sim, int[,] bar) = Base(d);
        World w = sim.World;
        int heavy = HeavyOf(d);
        List<TrainError> refused = Refusals(sim);
        PlaceLive(sim, 0, heavy, 6);
        sim.Enqueue(Command.Train(0, In(sim, bar[0, 0]), heavy));
        Apply(sim);
        Assert.Equal(7, Count(w, 0, heavy));
        // A Train before the cancel in the same tick is refused; the cancel still frees the place.
        sim.Enqueue(Command.Train(0, In(sim, bar[0, 1]), heavy));
        sim.Enqueue(Command.CancelTrain(0, In(sim, bar[0, 0]), 0));
        Apply(sim);
        Assert.Equal(new[] { TrainError.LimitReached }, refused);
        Assert.Equal(6, Count(w, 0, heavy));
        Assert.True(w.CanTrain(0, bar[0, 1], heavy, out _));
        // Re-queued and cancelled again, then cancel-and-train in one tick: the Train takes the freed place.
        sim.Enqueue(Command.Train(0, In(sim, bar[0, 0]), heavy));
        Apply(sim);
        sim.Enqueue(Command.CancelTrain(0, In(sim, bar[0, 0]), 0));
        sim.Enqueue(Command.Train(0, In(sim, bar[0, 1]), heavy));
        Apply(sim);
        Assert.Single(refused);
        Assert.Equal(7, Count(w, 0, heavy));
        Assert.Equal((0, 1), (w.Buildings.QueueCount[bar[0, 0]], w.Buildings.QueueCount[bar[0, 1]]));
    }

    /// <summary>Criterion 8: a death frees a place at once (between ticks the gate reads it; the next Train is accepted).</summary>
    [Fact]
    public void ADeath_FreesAPlace()
    {
        GameData d = s_capped.Value;
        (Simulation sim, int[,] bar) = Base(d);
        World w = sim.World;
        int heavy = HeavyOf(d);
        PlaceLive(sim, 0, heavy, 7);
        Assert.False(w.CanTrain(0, bar[0, 0], heavy, out _));
        int v = 0;
        while (!(w.Units.Alive[v] && w.Units.TypeId[v] == heavy)) v++;
        var victim = new EntityHandle(v, w.Units.Generation[v]);
        CombatSystem.HitUnit(w, new PendingHit(default, 1, victim, false, w.Units.Hp[victim.Index] + 1));
        Assert.False(w.Units.IsAlive(victim));
        Assert.Equal(6, Count(w, 0, heavy));
        Assert.True(w.CanTrain(0, bar[0, 0], heavy, out _));
        List<TrainError> refused = Refusals(sim);
        sim.Enqueue(Command.Train(0, In(sim, bar[0, 0]), heavy));
        Apply(sim);
        Assert.Empty(refused);
        Assert.Equal(7, Count(w, 0, heavy));
    }

    /// <summary>Criterion 8: four Trains in one tick at two buildings apply in order: the first two are accepted (5 + 2 = 7), the last two refused.</summary>
    [Fact]
    public void SeveralTrainsInOneTick_AtTwoBuildings_OnlyTheSurplusIsRefused()
    {
        GameData d = s_capped.Value;
        (Simulation sim, int[,] bar) = Base(d);
        World w = sim.World;
        int heavy = HeavyOf(d);
        PlaceLive(sim, 0, heavy, 5);
        List<TrainError> refused = Refusals(sim);
        for (int k = 0; k < 4; k++) sim.Enqueue(Command.Train(0, In(sim, bar[0, k % 2]), heavy));
        Apply(sim);
        Assert.Equal(new[] { TrainError.LimitReached, TrainError.LimitReached }, refused);
        Assert.Equal((1, 1), (w.Buildings.QueueCount[bar[0, 0]], w.Buildings.QueueCount[bar[0, 1]]));
        Assert.Equal(7, Count(w, 0, heavy));
        Assert.Equal(5000 - 2 * 54, w.Gold[0]);
    }

    /// <summary>
    /// Criterion 8: an item already queued always completes, even after an ability's spawn took the count past the limit; the
    /// summoned units count (and a dev spawn too) but are never refused; Trains are refused until the count is under again.
    /// </summary>
    [Fact]
    public void AQueuedItem_Completes_AfterASummonTookTheCountPastTheLimit()
    {
        GameData d = s_capped.Value;
        (Simulation sim, int[,] bar) = Base(d);
        World w = sim.World;
        int heavy = HeavyOf(d), mage = d.FindUnit("malazan_cadre_mage");
        List<TrainError> refused = Refusals(sim);
        PlaceLive(sim, 0, heavy, 5);
        EntityHandle caster = CombatScenes.Place(sim, 0, mage, CombatScenes.At(sim, 30, 30));
        sim.Enqueue(Command.Train(0, In(sim, bar[0, 0]), heavy));
        sim.Enqueue(Command.Train(0, In(sim, bar[0, 1]), heavy));
        Apply(sim);
        Assert.Equal(7, Count(w, 0, heavy));
        sim.Enqueue(Command.UseAbility(0, caster, 1, CombatScenes.At(sim, 30, 30)));
        AbilityScenes.TickOf(sim, caster, resolved: true);
        Assert.Equal(3, SummonTests.Summoned(w).Count);
        Assert.Equal(10, Count(w, 0, heavy)); // the summons count, past the limit
        sim.Enqueue(Command.SpawnUnit(0, heavy, CombatScenes.At(sim, 40, 40))); // a dev spawn: never refused
        sim.Enqueue(Command.Train(0, In(sim, bar[0, 0]), heavy));
        Apply(sim);
        Assert.Equal(11, Count(w, 0, heavy));
        Assert.Equal(new[] { TrainError.LimitReached }, refused);
        // The two queued items complete (280 ticks each, side by side) and the count holds.
        int live0 = 0;
        for (int i = 0; i < w.Units.Capacity; i++) if (w.Units.Alive[i] && w.Units.TypeId[i] == heavy && !w.Units.IsSummoned(i)) live0++;
        Assert.Equal(6, live0); // 5 placed + the dev spawn
        for (int t = 0; t < 300; t++)
        {
            sim.Tick();
            Count(w, 0, heavy);
        }
        Assert.Equal((0, 0), (w.Buildings.QueueCount[bar[0, 0]], w.Buildings.QueueCount[bar[0, 1]]));
        int trained = 0;
        for (int i = 0; i < w.Units.Capacity; i++) if (w.Units.Alive[i] && w.Units.TypeId[i] == heavy && !w.Units.IsSummoned(i)) trained++;
        Assert.Equal(8, trained); // both items came out
    }

    /// <summary>Criterion 8: a refused Train leaves gold, wood and every queue untouched, whatever else would refuse it (the limit is read first).</summary>
    [Fact]
    public void ARefusedTrain_ChargesNothing_AndTheLimitIsCheckedBeforeQueueAndCost()
    {
        GameData d = s_capped.Value;
        (Simulation sim, int[,] bar) = Base(d);
        World w = sim.World;
        int heavy = HeavyOf(d);
        for (int k = 0; k < EconomyConstants.ProductionQueueCapacity; k++) w.Buildings.Enqueue(bar[0, 0], heavy);
        PlaceLive(sim, 0, heavy, 2);
        Assert.Equal(7, Count(w, 0, heavy));
        BuildMaps.SetTotals(sim, 0, 0, 0);
        Assert.False(w.CanTrain(0, bar[0, 0], heavy, out TrainError why));
        Assert.Equal(TrainError.LimitReached, why); // not QueueFull, not CannotAfford
        Assert.False(w.CanTrain(0, bar[0, 1], heavy, out why));
        Assert.Equal(TrainError.LimitReached, why); // not CannotAfford
        // LockedByRequirement comes first: the Sapper (requires Age II) with a limit of its own.
        GameData sapperData = AiScenes.DataWith(dir =>
        {
            MaxAliveLoaderTests.Strip(dir);
            dir.SetUnitField("malazan", "malazan_sapper", "maxAlive", "1");
        });
        (Simulation s2, int[,] _) = Base(sapperData);
        int sapper = sapperData.FindUnit("malazan_sapper"), yard = sapperData.FindBuilding("malazan_engineers_yard");
        Assert.True(s2.World.Buildings.Spawn(0, yard, 30 * 48 + 30, out EntityHandle y));
        PlaceLive(s2, 0, sapper, 1);
        Assert.False(s2.World.CanTrain(0, y.Index, sapper, out why));
        Assert.Equal(TrainError.LockedByRequirement, why);
        // Refused for the limit: nothing moves.
        BuildMaps.SetTotals(sim, 0, 5000, 5000);
        List<TrainError> refused = Refusals(sim);
        sim.Enqueue(Command.Train(0, In(sim, bar[0, 1]), heavy));
        Apply(sim);
        Assert.Equal(new[] { TrainError.LimitReached }, refused);
        Assert.Equal((5000, 5000, 0), (w.Gold[0], w.Wood[0], w.Buildings.QueueCount[bar[0, 1]]));
    }

    /// <summary>Criterion 9: the accessor: a type without a limit (and a bad id or player) is false with (0, 0); the capped type gives (count, 7).</summary>
    [Fact]
    public void TheAccessor_GivesCountAndLimit_FalseWithoutOne()
    {
        GameData d = s_capped.Value;
        (Simulation sim, int[,] _) = Base(d);
        World w = sim.World;
        int heavy = HeavyOf(d), laborer = d.FindUnit("malazan_laborer");
        PlaceLive(sim, 0, heavy, 5);
        PlaceLive(sim, 0, laborer, 3);
        Assert.True(w.UnitLimit(0, heavy, out int count, out int limit));
        Assert.Equal((5, 7), (count, limit));
        Assert.False(w.UnitLimit(0, laborer, out count, out limit));
        Assert.Equal((0, 0), (count, limit));
        Assert.False(w.UnitLimit(0, -1, out count, out limit));
        Assert.False(w.UnitLimit(0, d.Units.Length, out _, out _));
        Assert.False(w.UnitLimit(7, heavy, out count, out limit));
        Assert.Equal((0, 0), (count, limit));
        // On shipped data nothing has a limit.
        (Simulation shipped, int[,] _) = Base(TestSim.Data);
        foreach (UnitDef u in TestSim.Data.Units) Assert.False(shipped.World.UnitLimit(0, u.Id, out _, out _));
    }

    /// <summary>Criterion 8: a building destroyed with items queued gives their places back (its queue goes with it).</summary>
    [Fact]
    public void ADestroyedBuilding_GivesItsQueuedPlacesBack()
    {
        GameData d = s_capped.Value;
        (Simulation sim, int[,] bar) = Base(d);
        World w = sim.World;
        int heavy = HeavyOf(d);
        for (int k = 0; k < 3; k++) sim.Enqueue(Command.Train(0, In(sim, bar[0, 0]), heavy));
        Apply(sim);
        Assert.Equal(3, Count(w, 0, heavy));
        Assert.True(w.Buildings.Free(new EntityHandle(bar[0, 0], w.Buildings.Generation[bar[0, 0]])));
        Assert.Equal(0, Count(w, 0, heavy));
    }
}

/// <summary>M8-4 criterion 14: the train check with a limit allocates nothing (the count is kept, never walked).</summary>
[Collection(SerialCollection.Name)]
public class UnitLimitAllocationTests
{
    private readonly ITestOutputHelper _out;

    public UnitLimitAllocationTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void TheCheck_AllocatesNothing()
    {
        GameData d = UnitLimitTests.s_capped.Value;
        (Simulation sim, int[,] bar) = UnitLimitTests.Base(d);
        World w = sim.World;
        int heavy = UnitLimitTests.HeavyOf(d), laborer = d.FindUnit("malazan_laborer");
        UnitLimitTests.PlaceLive(sim, 0, heavy, 7);
        int slot = bar[0, 0];
        int refusedForLimit = 0;
        void Block()
        {
            for (int k = 0; k < 10_000; k++)
            {
                if (!w.CanTrain(0, slot, heavy, out TrainError e) && e == TrainError.LimitReached) refusedForLimit++;
                w.CanTrain(0, slot, laborer, out _);
                w.UnitLimit(0, heavy, out _, out _);
            }
        }
        Action block = Block;
        AllocationProbe.AssertZero(block, _out);
        Assert.True(refusedForLimit >= 10_000);
    }
}
