using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA, M5-9a round 2 fix round 1 (BUG-1154's fix: <see cref="AiPlayer.Starved"/> judges the seat on
/// <see cref="ProductionManager.Pick"/>, the unit production would train next): whole matches watched on every think of
/// every AI seat. The oracle: a think that ends with the seat stamped starved (the census found it starved and nothing later
/// in the think cleared it) sends no Train command, since a starved seat by definition cannot pay for the unit production
/// waits for and can never get what it lacks. A Train sent on a stamped think means the check and production disagree on the
/// pick or on what is payable. Also measured (reported, not asserted): thinks where a stamped seat's bank of a kind it
/// lacked for the pick rose since its last think (income of a kind the rule called unobtainable).
/// M6-SH2 fix round 2 (BUG-1534, the Producer's ruling: the retrain wins): a worker-type Train sent while the seat has no
/// live worker is exempt (the docs/02 retrain; Raraku seed 5's seat 0 at tick 27,960 is the case), and nothing else is.
/// M6-SH3 / M6-S15 C (BUG-1890, docs/01 2026-11-27-1857 (4)): the oracle is re-stated to what now holds. A starved seat's
/// pick can never be paid for, so production now buys instead what it can pay for (another unit of its mix, else any unit
/// of its faction, the worker included): a stamped think may send such a Train, counted and printed. What stays an error:
/// a stamped think that sends a Train of the pick itself (the unit the stamp says it cannot pay for and never will).
/// </summary>
[Trait("Category", "Stress")]
public class StarvedPickOracleQaStressTests
{
    private readonly ITestOutputHelper _out;

    public StarvedPickOracleQaStressTests(ITestOutputHelper output) => _out = output;

    private sealed class Seat
    {
        public int Thinks, StampedThinks, TrainsThisThink, TrainsOnStamped, FirstStamp = -1, Rose, PrevGold = -1, PrevWood = -1;
        public int RetrainsThisThink, RetrainsOnStamped, SpentInstead;
        public List<int> TrainedTypes = new();
        public bool PrevStamped, PrevShortGold, PrevShortWood;
        public string? Error;
    }

    private static Simulation Build(string map, ulong seed, AiDifficulty a, int players, int[]? teams)
    {
        GameData data = TestSim.Data;
        var seats = new PlayerSeat[players];
        for (int p = 0; p < players; p++)
            seats[p] = new PlayerSeat(p % 2, SeatController.Ai, p == 0 ? a : AiDifficulty.Normal, teams == null ? 0 : teams[p]);
        int size = data.Maps[data.FindMap(map)].Size;
        return AiScenes.Generated(seats, seed, false, out _, units: players > 2 ? 1024 : 400, data: data,
            configure: c => c with { MapId = map, Map = c.Map with { Width = size, Height = size } });
    }

    private static void Watch(Simulation sim, int p, Seat s)
    {
        AiPlayer ai = sim.AiOf(p)!;
        // BUG-1534 (Producer's ruling, M6-SH2 fix round 2: the retrain wins): a Train of a worker type sent while the seat has
        // no live worker is the docs/02 retrain ("trains one first thing ... whatever its build order says"), which runs on a
        // starved think too, since a worker is the only way a seat with none gets back the kind it lacks. That Train, and
        // nothing else, is exempt from the oracle; it is counted and printed.
        ai.SentForTests = c =>
        {
            if (c.Kind != CommandKind.Train) return;
            World w = sim.World;
            if (c.TypeId >= 0 && c.TypeId < w.Data.Units.Length && w.Data.Units[c.TypeId].Slot == UnitSlot.Worker && LiveWorkers(w, p) == 0)
                s.RetrainsThisThink++;
            else
            {
                s.TrainsThisThink++;
                s.TrainedTypes.Add(c.TypeId);
            }
        };
        ai.ThoughtForTests = a =>
        {
            s.Thinks++;
            bool stamped = a.Board.StarvedSince >= 0;
            if (stamped)
            {
                s.StampedThinks++;
                if (s.FirstStamp < 0) s.FirstStamp = a.Now;
                s.RetrainsOnStamped += s.RetrainsThisThink;
                if (s.TrainsThisThink > 0)
                {
                    s.TrainsOnStamped++;
                    // M6-S15 C: a buy instead of the pick is the rule (was: any Train here was an error).
                    int pickNow = ProductionManager.Pick(a);
                    if (pickNow >= 0 && s.TrainedTypes.Contains(pickNow))
                        s.Error ??= $"seat {p} tick {a.Now}: stamped starved (since {a.Board.StarvedSince}) but sent a Train of its pick {pickNow}; gold {a.Gold}, wood {a.Wood}";
                    else s.SpentInstead += s.TrainsThisThink;
                }
                if (s.PrevStamped && (s.PrevShortGold && a.Gold > s.PrevGold || s.PrevShortWood && a.Wood > s.PrevWood)) s.Rose++;
                int pick = ProductionManager.Pick(a);
                if (pick >= 0)
                {
                    UnitDef d = a.Data.Units[pick];
                    s.PrevShortGold = a.Gold < d.CostGold;
                    s.PrevShortWood = a.Wood < d.CostWood;
                }
                else s.PrevShortGold = s.PrevShortWood = false;
            }
            s.PrevStamped = stamped;
            s.PrevGold = a.Gold;
            s.PrevWood = a.Wood;
            s.TrainsThisThink = 0;
            s.RetrainsThisThink = 0;
            s.TrainedTypes.Clear();
        };
    }

    private static int LiveWorkers(World w, int player)
    {
        int n = 0;
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == player && w.Data.Units[w.Units.TypeId[i]].Slot == UnitSlot.Worker) n++;
        return n;
    }

    /// <summary>
    /// The matches the fix moves (Pale Hills 1 / 2, Raraku 5), the Easy-v-Normal rows that starve, and a four-seat
    /// free-for-all, to their end or the bound.
    /// </summary>
    [Theory]
    [InlineData("pale_hills", 1UL, AiDifficulty.Normal, 2, 72_000)]
    [InlineData("pale_hills", 2UL, AiDifficulty.Normal, 2, 72_000)]
    [InlineData("raraku", 5UL, AiDifficulty.Normal, 2, 72_000)]
    [InlineData("raraku", 1UL, AiDifficulty.Easy, 2, 36_000)]
    [InlineData("pale_hills", 3UL, AiDifficulty.Easy, 2, 36_000)]
    [InlineData("pale_hills", 2UL, AiDifficulty.Normal, 4, 144_000)]
    public void AStampedThink_SendsNoTrain(string map, ulong seed, AiDifficulty first, int players, int bound)
    {
        Simulation sim = Build(map, seed, first, players, null);
        var seats = new Seat[players];
        for (int p = 0; p < players; p++) Watch(sim, p, seats[p] = new Seat());
        while (sim.TickNumber < bound && !sim.MatchOver) sim.Tick();
        _out.WriteLine($"{map} seed {seed} {first} v Normal, {players} seats: over {sim.MatchOver} at {sim.TickNumber}, winner {CriterionMatch.Winner(sim.World)}");
        for (int p = 0; p < players; p++)
        {
            Seat s = seats[p];
            _out.WriteLine($"  seat {p}: {s.Thinks} thinks, {s.StampedThinks} stamped (first {s.FirstStamp}), Train on a stamped think {s.TrainsOnStamped} (M6-S15 C's buys instead of the pick: {s.SpentInstead} Trains), "
                + $"worker retrain on a stamped think {s.RetrainsOnStamped} (exempt, BUG-1534), lacked kind rose while stamped {s.Rose}; {(s.Error ?? "clean")}");
        }
        foreach (Seat s in seats) Assert.True(s.Error == null, s.Error);
    }
}
