using System.Collections.Immutable;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;
using static Rts.Sim.Tests.ProductionMaps;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M8-4 (Request 56 G4, docs/03 "Implementation (M8-4)"): the edges of a unit type's <c>maxAlive</c> count the developer's
/// <see cref="UnitLimitTests"/> leave out. A pack that straddles the limit (room for part of it), eight players at their limit
/// in the same tick, a cancel of a started head and a re-queue at the same building in one tick, a death in the tick a
/// Train applies (the summons' expiry: commands apply first, so the Train of that tick is refused and the next one taken),
/// a player that lost every building (defeated), the kept count after a load with summons alive and packs queued, and
/// loader values the developer's rows do not try. Test data only (the developer's copies, which strip every shipped
/// <c>maxAlive</c> first: rule 58).
/// </summary>
public class UnitLimitQaTests(ITestOutputHelper output)
{
    private static int Heavy(GameData d) => UnitLimitTests.HeavyOf(d);

    private static List<(int Player, TrainError Why)> Refusals(Simulation sim)
    {
        var list = new List<(int, TrainError)>();
        sim.RefusedForTests = (c, why, detail) => { if (why == CommandRefusal.TrainRefused) list.Add((c.Player, (TrainError)detail)); };
        return list;
    }

    /// <summary>A pack of 3, limit 7: with 5 counted there is room for 2 but not the pack: refused, nothing charged; with 4 the pack fits exactly (7).</summary>
    [Fact]
    public void APackStraddlingTheLimit_IsRefused_ExactFitIsTaken()
    {
        GameData d = UnitLimitTests.s_pack.Value;
        (Simulation sim, int[,] bar) = UnitLimitTests.Base(d);
        World w = sim.World;
        int heavy = Heavy(d);
        var refused = Refusals(sim);
        UnitLimitTests.PlaceLive(sim, 0, heavy, 5);
        Assert.False(w.CanTrain(0, bar[0, 0], heavy, out TrainError why));
        Assert.Equal(TrainError.LimitReached, why);
        int gold = w.Gold[0];
        sim.Enqueue(Command.Train(0, In(sim, bar[0, 0]), heavy));
        UnitLimitTests.Apply(sim);
        Assert.Equal(new[] { (0, TrainError.LimitReached) }, refused);
        Assert.Equal((gold, 0, 5), (w.Gold[0], w.Buildings.QueueCount[bar[0, 0]], UnitLimitTests.Count(w, 0, heavy)));

        (Simulation fit, int[,] b2) = UnitLimitTests.Base(d);
        var refusedFit = Refusals(fit);
        UnitLimitTests.PlaceLive(fit, 0, heavy, 4);
        fit.Enqueue(Command.Train(0, In(fit, b2[0, 0]), heavy));
        UnitLimitTests.Apply(fit);
        Assert.Empty(refusedFit);
        Assert.Equal(7, UnitLimitTests.Count(fit.World, 0, heavy));
    }

    /// <summary>
    /// Eight players, each with 6 Heavy Infantry alive and two Barracks, all send two Trains in the same tick: each player's
    /// first is taken and its second refused for the limit (eight refusals, one per player); every count is 7 and equal to
    /// the walk; one death frees exactly that player's place.
    /// </summary>
    [Fact]
    public void EightPlayers_AtTheLimitInTheSameTick_EachHasItsOwnCount()
    {
        GameData d = UnitLimitTests.s_capped.Value;
        const int players = 8, size = 112;
        var seats = new PlayerSeat[players];
        for (int p = 0; p < players; p++) seats[p] = new PlayerSeat(d.FindFaction("malazan"), SeatController.Human, AiDifficulty.Normal);
        var sim = new Simulation(new SimConfig(5, players, 256, 1024) { Data = d, Seats = seats.ToImmutableArray() }, LocalMovementTests.Flat(size));
        World w = sim.World;
        int heavy = Heavy(d);
        var bar = new int[players, 2];
        for (int p = 0; p < players; p++)
        {
            int row = 4 + 13 * p;
            Assert.True(w.Buildings.Spawn(p, d.FindBuilding("malazan_garrison_keep"), row * size + 30, out _));
            Assert.True(w.Buildings.Spawn(p, d.FindBuilding("malazan_barracks"), row * size + 4, out EntityHandle a));
            Assert.True(w.Buildings.Spawn(p, d.FindBuilding("malazan_barracks"), row * size + 14, out EntityHandle b));
            (bar[p, 0], bar[p, 1]) = (a.Index, b.Index);
            BuildMaps.SetTotals(sim, p, 5000, 5000);
            for (int k = 0; k < 6; k++) CombatScenes.Place(sim, p, heavy, CombatScenes.At(sim, 60 + k * 2, row + 1));
        }
        var refused = Refusals(sim);
        for (int p = 0; p < players; p++)
        {
            sim.Enqueue(Command.Train(p, In(sim, bar[p, 0]), heavy));
            sim.Enqueue(Command.Train(p, In(sim, bar[p, 1]), heavy));
        }
        UnitLimitTests.Apply(sim);
        Assert.Equal(Enumerable.Range(0, players).Select(p => (p, TrainError.LimitReached)), refused);
        for (int p = 0; p < players; p++)
        {
            Assert.Equal(7, UnitLimitTests.Count(w, p, heavy));
            Assert.Equal((1, 0), (w.Buildings.QueueCount[bar[p, 0]], w.Buildings.QueueCount[bar[p, 1]]));
        }
        // One of player 3's dies: only player 3 may train again.
        int v = -1;
        for (int i = 0; i < w.Units.Capacity && v < 0; i++) if (w.Units.Alive[i] && w.Units.Owner[i] == 3 && w.Units.TypeId[i] == heavy) v = i;
        CombatSystem.HitUnit(w, new PendingHit(default, 1, new EntityHandle(v, w.Units.Generation[v]), false, w.Units.Hp[v] + 1));
        for (int p = 0; p < players; p++)
        {
            bool can = w.CanTrain(p, bar[p, 1], heavy, out TrainError why);
            Assert.Equal(p == 3, can);
            if (p != 3) Assert.Equal(TrainError.LimitReached, why);
            Assert.Equal(p == 3 ? 6 : 7, UnitLimitTests.Count(w, p, heavy));
        }
    }

    /// <summary>
    /// At the limit with a started head: a CancelTrain of that head and a Train at the same building in one tick (in that
    /// order) is taken: the count is back at 7, the gold the same (full refund, then the cost again), and the new item starts
    /// from 0. Then the other order at the other building: the Train is refused and the cancel still frees the place.
    /// </summary>
    [Fact]
    public void CancelAStartedHead_AndRequeue_AtTheSameBuilding_InOneTick()
    {
        GameData d = UnitLimitTests.s_capped.Value;
        (Simulation sim, int[,] bar) = UnitLimitTests.Base(d);
        World w = sim.World;
        int heavy = Heavy(d);
        var refused = Refusals(sim);
        UnitLimitTests.PlaceLive(sim, 0, heavy, 6);
        sim.Enqueue(Command.Train(0, In(sim, bar[0, 0]), heavy));
        UnitLimitTests.Apply(sim);
        for (int t = 0; t < 40; t++) sim.Tick();
        Assert.True(w.Buildings.Progress[bar[0, 0]] > 0, "the head has started");
        Assert.Equal(7, UnitLimitTests.Count(w, 0, heavy));
        int gold = w.Gold[0];
        sim.Enqueue(Command.CancelTrain(0, In(sim, bar[0, 0]), 0));
        sim.Enqueue(Command.Train(0, In(sim, bar[0, 0]), heavy));
        UnitLimitTests.Apply(sim);
        Assert.Empty(refused);
        Assert.Equal(7, UnitLimitTests.Count(w, 0, heavy));
        Assert.Equal(1, w.Buildings.QueueCount[bar[0, 0]]);
        Assert.Equal(gold, w.Gold[0]);
        Assert.True(w.Buildings.Progress[bar[0, 0]] <= 2, $"the re-queued item starts again (progress {w.Buildings.Progress[bar[0, 0]]})");
        // Train first, then the cancel: refused, and the cancel frees the place.
        sim.Enqueue(Command.Train(0, In(sim, bar[0, 1]), heavy));
        sim.Enqueue(Command.CancelTrain(0, In(sim, bar[0, 0]), 0));
        UnitLimitTests.Apply(sim);
        Assert.Equal(new[] { (0, TrainError.LimitReached) }, refused);
        Assert.Equal(6, UnitLimitTests.Count(w, 0, heavy));
    }

    /// <summary>
    /// A death in the tick a Train applies: 4 trained Heavy Infantry and 3 summoned (20 s) make 7. A Train is sent every tick
    /// around the summons' expiry. Commands apply at the start of a tick and the expiry is in its phase 5, so a Train that
    /// applies in the expiry tick is refused for the limit, and one after it is taken within two ticks; at every tick's end
    /// the count equals the walk and never passes 7.
    /// </summary>
    [Fact]
    public void ADeathInTheTickATrainApplies_TheTrainIsRefused_TheNextIsTaken()
    {
        GameData d = UnitLimitTests.s_capped.Value;
        (Simulation sim, int[,] bar) = UnitLimitTests.Base(d);
        World w = sim.World;
        int heavy = Heavy(d), mage = d.FindUnit("malazan_cadre_mage");
        UnitLimitTests.PlaceLive(sim, 0, heavy, 4);
        EntityHandle caster = CombatScenes.Place(sim, 0, mage, CombatScenes.At(sim, 30, 30));
        sim.Enqueue(Command.UseAbility(0, caster, 1, CombatScenes.At(sim, 30, 30)));
        AbilityScenes.TickOf(sim, caster, resolved: true);
        List<EntityHandle> summons = SummonTests.Summoned(w);
        Assert.Equal(3, summons.Count);
        Assert.Equal(7, UnitLimitTests.Count(w, 0, heavy));
        int expires = w.Units.ExpiresAtTick[summons[0].Index];
        foreach (EntityHandle s in summons) Assert.Equal(expires, w.Units.ExpiresAtTick[s.Index]);
        var applied = new List<(int Tick, bool Taken)>();
        sim.RefusedForTests = (c, why, detail) => { if (why == CommandRefusal.TrainRefused) applied.Add((sim.TickNumber, false)); };
        while (sim.TickNumber < expires - 3) sim.Tick();
        int queued = 0, firstTaken = -1, lastRefused = -1;
        while (sim.TickNumber < expires + 4)
        {
            int before = applied.Count;
            if (firstTaken < 0) sim.Enqueue(Command.Train(0, In(sim, bar[0, 1]), heavy));
            sim.Tick();
            int q = w.Buildings.QueueCount[bar[0, 1]];
            if (q > queued && firstTaken < 0) firstTaken = sim.TickNumber;
            queued = q;
            if (applied.Count > before) lastRefused = applied[^1].Tick;
            int count = UnitLimitTests.Count(w, 0, heavy);
            Assert.True(count <= 7, $"count {count} at tick {sim.TickNumber}");
        }
        output.WriteLine($"summons expire at tick {expires}; last Train refused in tick {lastRefused}; first taken in tick {firstTaken}");
        Assert.Empty(SummonTests.Summoned(w));
        Assert.True(lastRefused >= expires - 1, $"a Train applied before the expiry was taken (last refused {lastRefused}, expiry {expires})");
        Assert.True(firstTaken > expires, $"a Train was taken in or before the expiry tick ({firstTaken} <= {expires})");
        Assert.True(firstTaken <= expires + 2, $"the freed place was taken late ({firstTaken}, expiry {expires})");
        // A command takes two ticks to apply, so the Train sent in the tick before the first was seen taken lands too.
        Assert.InRange(UnitLimitTests.Count(w, 0, heavy), 5, 6);
    }

    /// <summary>
    /// A player that lost every building (defeated: nothing to train at) keeps a true count: its queued items leave the count
    /// with their buildings, its live units stay counted, and the count follows deaths after the defeat; the other player is
    /// untouched.
    /// </summary>
    [Fact]
    public void ADefeatedPlayer_CountStaysTrue()
    {
        GameData d = UnitLimitTests.s_capped.Value;
        (Simulation sim, int[,] bar) = UnitLimitTests.Base(d, players: 2);
        World w = sim.World;
        int heavy = Heavy(d);
        UnitLimitTests.PlaceLive(sim, 1, heavy, 4);
        UnitLimitTests.PlaceLive(sim, 0, heavy, 2);
        for (int k = 0; k < 3; k++) sim.Enqueue(Command.Train(1, In(sim, bar[1, k % 2]), heavy));
        sim.Enqueue(Command.Train(0, In(sim, bar[0, 0]), heavy));
        UnitLimitTests.Apply(sim);
        Assert.Equal((3, 7), (UnitLimitTests.Count(w, 0, heavy), UnitLimitTests.Count(w, 1, heavy)));
        for (int k = 0; k < w.Buildings.Capacity; k++)
            if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == 1) Assert.True(w.Buildings.Free(new EntityHandle(k, w.Buildings.Generation[k])));
        Assert.Equal(4, UnitLimitTests.Count(w, 1, heavy));
        for (int t = 0; t < 60; t++)
        {
            sim.Tick();
            UnitLimitTests.Count(w, 0, heavy);
            UnitLimitTests.Count(w, 1, heavy);
        }
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == 1)
                CombatSystem.HitUnit(w, new PendingHit(default, 0, new EntityHandle(i, w.Units.Generation[i]), false, w.Units.Hp[i] + 1));
        sim.Tick();
        Assert.Equal(0, UnitLimitTests.Count(w, 1, heavy));
        Assert.True(UnitLimitTests.Count(w, 0, heavy) >= 3);
    }

    /// <summary>
    /// After a load with summons alive (past the limit) and items queued, the kept count is rebuilt to the walk, a Train is
    /// still refused, and the loaded run equals the unsaved twin, hash and count, at every tick through the summons' expiry
    /// and the items' completion (400 ticks).
    /// </summary>
    [Fact]
    public void AfterALoad_WithSummonsAliveAndItemsQueued_TheCountIsRebuilt_AndRunsAsTheTwin()
    {
        GameData d = UnitLimitTests.s_capped.Value;
        (Simulation sim, int[,] bar) = UnitLimitTests.Base(d);
        World w = sim.World;
        int heavy = Heavy(d), mage = d.FindUnit("malazan_cadre_mage");
        UnitLimitTests.PlaceLive(sim, 0, heavy, 3);
        EntityHandle caster = CombatScenes.Place(sim, 0, mage, CombatScenes.At(sim, 30, 30));
        sim.Enqueue(Command.Train(0, In(sim, bar[0, 0]), heavy));
        sim.Enqueue(Command.Train(0, In(sim, bar[0, 1]), heavy));
        sim.Enqueue(Command.Train(0, In(sim, bar[0, 1]), heavy));
        UnitLimitTests.Apply(sim);
        sim.Enqueue(Command.UseAbility(0, caster, 1, CombatScenes.At(sim, 30, 30)));
        AbilityScenes.TickOf(sim, caster, resolved: true);
        Assert.Equal(9, UnitLimitTests.Count(w, 0, heavy));
        Simulation loaded = SaveTestRun.Load(d, SaveTestRun.Save(sim));
        Assert.Equal(9, UnitLimitTests.Count(loaded.World, 0, heavy));
        Assert.False(loaded.World.CanTrain(0, bar[0, 0], heavy, out TrainError why));
        Assert.Equal(TrainError.LimitReached, why);
        Assert.Equal(sim.StateHash(), loaded.StateHash());
        for (int t = 0; t < 600; t++)
        {
            if (t == 450)
            {
                sim.Enqueue(Command.Train(0, In(sim, bar[0, 0]), heavy));
                loaded.Enqueue(Command.Train(0, In(loaded, bar[0, 0]), heavy));
            }
            sim.Tick();
            loaded.Tick();
            Assert.True(sim.StateHash() == loaded.StateHash(), $"the loaded run differs at tick {sim.TickNumber}");
            Assert.Equal(UnitLimitTests.Count(w, 0, heavy), UnitLimitTests.Count(loaded.World, 0, heavy));
        }
        Assert.Empty(SummonTests.Summoned(w));
        Assert.Equal(7, UnitLimitTests.Count(loaded.World, 0, heavy)); // 3 + 3 trained, then one more once the summons went
    }

    /// <summary>Loader values the developer's rows do not try: an object, a number beyond a double, minus zero; each one error at the path naming the unit; 1000 with a pack of 8 loads.</summary>
    [Theory]
    [InlineData("{}", "wrong type of value for maxAlive")]
    [InlineData("1e400", "maxAlive")]
    [InlineData("-0", "maxAlive")]
    [InlineData("0.9999999", "maxAlive")]
    public void MoreBrokenValues_AreOneErrorEach_NamingTheUnit(string json, string text)
    {
        DataLoadResult r = MaxAliveLoaderTests.LoadWith(MaxAliveLoaderTests.Heavy, json);
        Assert.False(r.Ok);
        Assert.True(r.Errors.Count == 1 && r.Errors[0].Path.EndsWith(".maxAlive", StringComparison.Ordinal)
            && r.Errors[0].Message.Contains(text, StringComparison.Ordinal) && r.Errors[0].Message.Contains(MaxAliveLoaderTests.Heavy, StringComparison.Ordinal),
            string.Join("; ", r.Errors));
    }

    [Fact]
    public void TheBoundWithTheLargestPack_Loads()
    {
        DataLoadResult r = MaxAliveLoaderTests.LoadWith(MaxAliveLoaderTests.Heavy, DataLimits.MaxUnitMaxAlive.ToString(), DataLimits.MaxSpawnCount.ToString());
        Assert.True(r.Ok, string.Join("; ", r.Errors));
    }

    /// <summary>A refused spawnCount beside a valid maxAlive is the spawnCount's one error, not a second one about the limit.</summary>
    [Theory]
    [InlineData("9")]
    [InlineData("1.5")]
    [InlineData("0")]
    public void ARefusedSpawnCount_BesideAMaxAlive_IsOneError(string spawnCount)
    {
        DataLoadResult r = MaxAliveLoaderTests.LoadWith(MaxAliveLoaderTests.Heavy, "7", spawnCount);
        Assert.False(r.Ok);
        Assert.True(r.Errors.Count == 1 && r.Errors[0].Path.EndsWith(".spawnCount", StringComparison.Ordinal), string.Join("; ", r.Errors));
    }
}
