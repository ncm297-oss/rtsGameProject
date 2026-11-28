using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA, M6-S15 (QA focus 3, "a seat that sits"; BUG-1890's family): whole AI matches watched for an AI seat that the draw rule
/// says can still train, with a building of its own free to train (finished, empty queue, trains a unit of its faction), that
/// has sent no Train for 6,000 ticks. Measured at the end tick (or the bound), and through the match every 20 ticks as the
/// longest such span; "plainly" adds that some unit of its faction is payable now from the bank, its producer is free, its
/// requirements are met, it is not at its limit and the population has room for it (a seat that could press Train now and
/// does not). Measure and report against the base (the class reads only what d26338d3 already had, so it runs there too);
/// no row asserts a count. Lines start "SIT".
/// </summary>
[Trait("Category", "Stress")]
public abstract class SittingSeatOracleQaStressBase
{
    private readonly ITestOutputHelper _out;

    protected SittingSeatOracleQaStressBase(ITestOutputHelper output) => _out = output;

    private const int Gap = 6_000;

    private sealed class Seat
    {
        public int LastTrain, LongestSit, LongestSitAt = -1, LongestPlain, LongestPlainAt = -1, SitFrom = -1, PlainFrom = -1, Trains;
        public string LastReserve = "";
        public int LastPick = -1;
        public bool LastStarved;
    }

    /// <summary>Whether <paramref name="p"/> owns a finished building with an empty queue that trains <paramref name="unit"/> (any of its faction's with -1).</summary>
    private static bool FreeProducer(World w, int p, int unit)
    {
        var b = w.Buildings;
        GameData d = w.Data;
        int f = w.FactionOf(p);
        for (int k = 0; k < b.Capacity; k++)
        {
            if (!b.Alive[k] || b.Owner[k] != p || b.UnderConstruction[k] || b.QueueCount[k] > 0) continue;
            int type = b.TypeId[k];
            if (unit >= 0)
            {
                if (d.Units[unit].TrainedAtTypeId == type) return true;
                continue;
            }
            if (f < 0) continue;
            foreach (int u in d.Factions[f].Units)
                if (d.Units[u].TrainedAtTypeId == type) return true;
        }
        return false;
    }

    private static bool OwnsFinished(World w, int p, int type)
    {
        var b = w.Buildings;
        for (int k = 0; k < b.Capacity; k++)
            if (b.Alive[k] && b.Owner[k] == p && !b.UnderConstruction[k] && b.TypeId[k] == type) return true;
        return false;
    }

    /// <summary>The faction's units <paramref name="p"/> could press Train on now (bank, free producer, requirements, limit, room).</summary>
    private static string PlainlyTrainable(World w, int p)
    {
        GameData d = w.Data;
        int f = w.FactionOf(p);
        if (f < 0) return "";
        var list = new List<string>();
        int room = w.HalfPopCap[p] - w.HalfPop[p];
        foreach (int u in d.Factions[f].Units)
        {
            UnitDef def = d.Units[u];
            if (w.Gold[p] < def.CostGold || w.Wood[p] < def.CostWood || def.ItemHalfPop > room) continue;
            if (def.MaxAlive > 0 && w.LimitedCount(p, u) + def.SpawnCount > def.MaxAlive) continue;
            bool ok = true;
            foreach (int t in def.RequiresTechs) ok &= w.HasTech(p, t);
            foreach (int bt in def.RequiresBuildings) ok &= OwnsFinished(w, p, bt);
            if (ok && FreeProducer(w, p, u)) list.Add(def.Key);
        }
        return string.Join(",", list);
    }

    private static string Buildings(World w, int p)
    {
        var b = w.Buildings;
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        for (int k = 0; k < b.Capacity; k++)
        {
            if (!b.Alive[k] || b.Owner[k] != p) continue;
            string key = w.Data.Buildings[b.TypeId[k]].Key + (b.UnderConstruction[k] ? "(site)" : b.QueueCount[k] > 0 ? $"(q{b.QueueCount[k]})" : "");
            counts[key] = counts.TryGetValue(key, out int n) ? n + 1 : 1;
        }
        return string.Join(" ", counts.Select(kv => kv.Value > 1 ? $"{kv.Key}x{kv.Value}" : kv.Key));
    }

    protected void Run(string name, Simulation sim, int bound)
    {
        World w = sim.World;
        int n = w.Config.PlayerCount;
        var seats = new Seat[n];
        for (int p = 0; p < n; p++)
        {
            Seat s = seats[p] = new Seat();
            AiPlayer? ai = sim.AiOf(p);
            if (ai == null) continue;
            ai.SentForTests = c =>
            {
                if (c.Kind != CommandKind.Train) return;
                s.LastTrain = sim.TickNumber;
                s.Trains++;
            };
            ai.ThoughtForTests = a =>
            {
                s.LastPick = ProductionManager.Pick(a);
                s.LastStarved = a.Board.StarvedSince >= 0;
                s.LastReserve = a.HallReserved ? $"hall {a.ReserveGold}/{a.ReserveWood}" : a.ReserveStep >= 0 ? $"step {a.ReserveStep} {a.ReserveGold}/{a.ReserveWood}" : "none";
            };
        }
        while (!sim.MatchOver && sim.TickNumber < bound)
        {
            sim.Tick();
            if (sim.TickNumber % 20 != 0) continue;
            for (int p = 0; p < n; p++)
            {
                if (sim.AiOf(p) == null || w.IsDefeated(p)) continue;
                Seat s = seats[p];
                int now = sim.TickNumber;
                bool sit = w.CanStillTrain(p) && FreeProducer(w, p, -1);
                bool plain = sit && PlainlyTrainable(w, p).Length > 0;
                Track(ref s.SitFrom, ref s.LongestSit, ref s.LongestSitAt, sit, Math.Max(s.LastTrain, 0), now);
                Track(ref s.PlainFrom, ref s.LongestPlain, ref s.LongestPlainAt, plain, Math.Max(s.LastTrain, 0), now);
            }
        }
        string end = !sim.MatchOver ? $"no end by {sim.TickNumber}" : w.MatchDrawn ? $"draw at {sim.TickNumber}" : $"over at {sim.TickNumber}";
        _out.WriteLine($"SIT {name}: {end}");
        int sitting = 0;
        for (int p = 0; p < n; p++)
        {
            AiPlayer? ai = sim.AiOf(p);
            if (ai == null) continue;
            Seat s = seats[p];
            bool can = !w.IsDefeated(p) && w.CanStillTrain(p);
            bool free = !w.IsDefeated(p) && FreeProducer(w, p, -1);
            int since = sim.TickNumber - s.LastTrain;
            bool sits = can && free && since >= Gap;
            string plain = w.IsDefeated(p) ? "" : PlainlyTrainable(w, p);
            if (sits) sitting++;
            string pick = s.LastPick >= 0 ? $"{w.Data.Units[s.LastPick].Key} {w.Data.Units[s.LastPick].CostGold}/{w.Data.Units[s.LastPick].CostWood}" : "-";
            _out.WriteLine($"SIT   p{p}{(w.IsDefeated(p) ? " out" : "")} {(sits ? "SITS" : "ok")}: can train {can}, free producer {free}, last Train {s.LastTrain} ({since} ago, {s.Trains} sent); "
                + $"bank {w.Gold[p]}/{w.Wood[p]}, pop {w.HalfPop[p] / 2.0}/{w.HalfPopCap[p] / 2.0}, workers {w.Ledger.LiveWorkers[p]}, drop-offs {w.Ledger.FinishedDropOffs[p]}, units {w.LiveUnits(p)}; "
                + $"pick {pick}, starved {s.LastStarved}, reserve {s.LastReserve}; plainly trainable now [{plain}]; buildings [{Buildings(w, p)}]; "
                + $"longest sit {s.LongestSit} (ending {s.LongestSitAt}), longest plain sit {s.LongestPlain} (ending {s.LongestPlainAt})");
        }
        _out.WriteLine($"SIT-SUM {name}: {sitting} seat(s) sit at the end; map gold {w.Resources.GoldLeft} wood {w.Resources.WoodLeft}");
    }

    private static void Track(ref int from, ref int longest, ref int at, bool on, int lastTrain, int now)
    {
        if (!on) { from = -1; return; }
        if (from < 0 || from < lastTrain) from = Math.Max(lastTrain, now - 20);
        int span = now - from;
        if (span > longest) { longest = span; at = now; }
    }

    protected static Simulation Tally(string map, ulong seed, int hardSeat)
    {
        GameData data = TestSim.Data;
        AiDifficulty a = hardSeat == 0 ? AiDifficulty.Hard : AiDifficulty.Normal, b = hardSeat == 0 ? AiDifficulty.Normal : AiDifficulty.Hard;
        int size = data.Maps[data.FindMap(map)].Size;
        PlayerSeat[] seats = { new(0, SeatController.Ai, a), new(1, SeatController.Ai, b) };
        return map == "raraku"
            ? AiScenes.Generated(seats, seed, false, out _, data: data, configure: c => c with { MapId = map })
            : AiScenes.Generated(seats, seed, false, out _, data: data, configure: c => c with { MapId = map, Map = c.Map with { Width = size, Height = size } });
    }
}

/// <summary>The four-seat tables (<c>M6S14FourSeatQaStressTests</c>' rows), one class per seed so they run side by side.</summary>
public abstract class SittingSeatFourSeatQaStressBase(ITestOutputHelper o) : SittingSeatOracleQaStressBase(o)
{
    protected abstract ulong Seed { get; }

    [Theory]
    [InlineData("pale_hills", false)]
    [InlineData("vathar_crossing", false)]
    [InlineData("pale_hills", true)]
    [InlineData("vathar_crossing", true)]
    public void FourSeats_SittingSeats(string map, bool oneVsOneVsTwo) =>
        Run($"{map} seed {Seed} four seats {(oneVsOneVsTwo ? "1v1v2" : "ffa")}", DrawStalledRowsReportTests.Build(map, Seed, 4, oneVsOneVsTwo ? new[] { 0, 1, 0, 1 } : null), 144_000);
}

public class SittingSeatFourSeatQaStressSeed1(ITestOutputHelper o) : SittingSeatFourSeatQaStressBase(o) { protected override ulong Seed => 1; }

public class SittingSeatFourSeatQaStressSeed2(ITestOutputHelper o) : SittingSeatFourSeatQaStressBase(o) { protected override ulong Seed => 2; }

public class SittingSeatFourSeatQaStressSeed3(ITestOutputHelper o) : SittingSeatFourSeatQaStressBase(o) { protected override ulong Seed => 3; }

public class SittingSeatFourSeatQaStressSeed4(ITestOutputHelper o) : SittingSeatFourSeatQaStressBase(o) { protected override ulong Seed => 4; }

public class SittingSeatFourSeatQaStressSeed5(ITestOutputHelper o) : SittingSeatFourSeatQaStressBase(o) { protected override ulong Seed => 5; }

public class SittingSeatFourSeatQaStressSeed6(ITestOutputHelper o) : SittingSeatFourSeatQaStressBase(o) { protected override ulong Seed => 6; }

public class SittingSeatFourSeatQaStressSeed7(ITestOutputHelper o) : SittingSeatFourSeatQaStressBase(o) { protected override ulong Seed => 7; }

public class SittingSeatFourSeatQaStressSeed8(ITestOutputHelper o) : SittingSeatFourSeatQaStressBase(o) { protected override ulong Seed => 8; }

/// <summary>Two seats (Normal v Normal, <c>CriterionMatch.Build</c>), seeds 1-8, one class per map, to 72,000.</summary>
public abstract class SittingSeatTwoSeatQaStressBase(ITestOutputHelper o) : SittingSeatOracleQaStressBase(o)
{
    protected abstract string Map { get; }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    [InlineData(4UL)]
    [InlineData(5UL)]
    [InlineData(6UL)]
    [InlineData(7UL)]
    [InlineData(8UL)]
    public void TwoSeats_SittingSeats(ulong seed) => Run($"{Map} seed {seed} two seats", CriterionMatch.Build(Map, seed), 72_000);
}

public class SittingSeatTwoSeatPaleHillsQaStress(ITestOutputHelper o) : SittingSeatTwoSeatQaStressBase(o) { protected override string Map => "pale_hills"; }

public class SittingSeatTwoSeatRarakuQaStress(ITestOutputHelper o) : SittingSeatTwoSeatQaStressBase(o) { protected override string Map => "raraku"; }

public class SittingSeatTwoSeatVatharQaStress(ITestOutputHelper o) : SittingSeatTwoSeatQaStressBase(o) { protected override string Map => "vathar_crossing"; }

/// <summary>The Hard v Normal tally (<c>HardVersusNormalTallyQaStressTests</c>' seven seeds), one class per map and Hard seat, to 72,000.</summary>
public abstract class SittingSeatTallyQaStressBase(ITestOutputHelper o) : SittingSeatOracleQaStressBase(o)
{
    protected abstract string Map { get; }

    [Theory]
    [InlineData(1UL, 0)]
    [InlineData(2UL, 0)]
    [InlineData(3UL, 0)]
    [InlineData(5UL, 0)]
    [InlineData(6UL, 0)]
    [InlineData(7UL, 0)]
    [InlineData(8UL, 0)]
    [InlineData(1UL, 1)]
    [InlineData(2UL, 1)]
    [InlineData(3UL, 1)]
    [InlineData(5UL, 1)]
    [InlineData(6UL, 1)]
    [InlineData(7UL, 1)]
    [InlineData(8UL, 1)]
    public void Tally_SittingSeats(ulong seed, int hardSeat) => Run($"tally {Map} seed {seed} hard seat {hardSeat}", Tally(Map, seed, hardSeat), 72_000);
}

public class SittingSeatTallyPaleHillsQaStress(ITestOutputHelper o) : SittingSeatTallyQaStressBase(o) { protected override string Map => "pale_hills"; }

public class SittingSeatTallyRarakuQaStress(ITestOutputHelper o) : SittingSeatTallyQaStressBase(o) { protected override string Map => "raraku"; }

public class SittingSeatTallyVatharQaStress(ITestOutputHelper o) : SittingSeatTallyQaStressBase(o) { protected override string Map => "vathar_crossing"; }
