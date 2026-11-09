using System.Reflection;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA, M5-9a rounds 1 + 2 (full tier): whole matches watched on every think of every AI seat.
/// <list type="bullet">
/// <item>The counter memory's fog oracle (round 1's focus): for each enemy unit slot, the non-worker enemy units the seat can
/// see this think (<see cref="Vision.FogStore.CanSeeUnit"/>, the world's truth) must equal the memory's count when the memory
/// took this think's sighting, and be under the faded value when it did not; so no unseen unit is ever counted.</item>
/// <item>The starved wave (round 2): the first stamp, the first think it came due, the waves / hunts started on a due think,
/// how often the stamp was cleared; QA focus (d): no starved wave before tick 12,000 on any pinned match.</item>
/// <item>BUG-1154's measure: thinks where every composition entry is short of a kind the seat can never get (no node known,
/// no worker on it) but the shipped rule says not starved, because another short kind is gatherable.</item>
/// </list>
/// </summary>
[Trait("Category", "Stress")]
public class StarvedWaveProbeQaStressTests
{
    private readonly ITestOutputHelper _out;

    public StarvedWaveProbeQaStressTests(ITestOutputHelper output) => _out = output;

    private sealed class Seat
    {
        public int FirstStamp = -1, FirstDue = -1, FirstStarvedWave = -1, Stamps, Clears, WavesDue, HuntsDue, Thinks;
        public int MissedByRule, MissedRun, LongestMissedRun, FixedFirstDue = -1, FixedRunStart = -1;
        public int PrevWaves, PrevHunts, PrevStamp = -1;
        public int OracleChecks;
        public string? OracleError;
    }

    private static readonly MethodInfo GatheringM = typeof(AiPlayer).GetMethod("Gathering", BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly MethodInfo WalkM = typeof(AiPlayer).GetMethod("WalkForNode", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static bool Available(AiPlayer a, byte kind, ResourceKind rk) =>
        (bool)GatheringM.Invoke(a, new object[] { kind })! || (bool)WalkM.Invoke(a, new object[] { rk })!;

    /// <summary>BUG-1154's corrected reading of (i) + (ii): every entry short of some kind the seat cannot get.</summary>
    private static bool NothingEverBuyable(AiPlayer a)
    {
        if (a.Board.Defending || a.View.MatchOver || a.Opening.Compositions.Length == 0) return false;
        AiComposition mix = a.Opening.Compositions[Math.Clamp(a.View.Age, 1, a.Opening.Compositions.Length) - 1];
        if (mix.Units.Length == 0 || a.ArmyCount < a.Rules.StarvedMinArmy) return false;
        bool goldAvail = Available(a, EconomyManager.Gold, ResourceKind.Gold), woodAvail = Available(a, EconomyManager.Wood, ResourceKind.Wood);
        for (int k = 0; k < mix.Units.Length; k++)
        {
            UnitDef def = a.Data.Units[mix.Units[k]];
            bool g = a.Gold < def.CostGold, w = a.Wood < def.CostWood;
            if (!(g && !goldAvail || w && !woodAvail)) return false;
        }
        return true;
    }

    private static void Watch(Simulation sim, int p, Seat s)
    {
        World w = sim.World;
        var visible = new int[Blackboard.SlotCount];
        sim.AiOf(p)!.ThoughtForTests = a =>
        {
            s.Thinks++;
            // Fog oracle.
            Array.Clear(visible);
            for (int i = 0; i < w.Units.Capacity; i++)
            {
                if (!w.Units.Alive[i] || w.Units.Owner[i] == p || !w.IsEnemy(p, w.Units.Owner[i])) continue;
                if (a.IsWorkerType[w.Units.TypeId[i]] || !w.Fog.CanSeeUnit(p, i)) continue;
                visible[(int)w.Data.Units[w.Units.TypeId[i]].Slot]++;
            }
            for (int e = 0; e < Blackboard.SlotCount && s.OracleError == null; e++)
            {
                s.OracleChecks++;
                if (a.Board.SeenSlotTick[e] == a.Now && a.Board.SeenSlotCount[e] > 0)
                {
                    if (a.Board.SeenSlotCount[e] != visible[e])
                        s.OracleError = $"seat {p} tick {a.Now} slot {e}: memory took {a.Board.SeenSlotCount[e]}, {visible[e]} visible";
                }
                else if (visible[e] > 0 && visible[e] >= a.FadedSlot(e))
                    s.OracleError = $"seat {p} tick {a.Now} slot {e}: {visible[e]} visible >= faded {a.FadedSlot(e)} but the memory kept tick {a.Board.SeenSlotTick[e]}";
            }
            // Starved stamp.
            int st = a.Board.StarvedSince;
            if (st >= 0 && s.PrevStamp != st) { s.Stamps++; if (s.FirstStamp < 0) s.FirstStamp = st; }
            if (st < 0 && s.PrevStamp >= 0) s.Clears++;
            s.PrevStamp = st;
            bool due = a.StarvedWaveDue;
            if (due && s.FirstDue < 0) s.FirstDue = a.Now;
            if (due && a.Board.WavesStarted > s.PrevWaves) { s.WavesDue++; if (s.FirstStarvedWave < 0) s.FirstStarvedWave = a.Now; }
            if (due && a.Board.HuntsStarted > s.PrevHunts) { s.HuntsDue++; if (s.FirstStarvedWave < 0) s.FirstStarvedWave = a.Now; }
            s.PrevWaves = a.Board.WavesStarted;
            s.PrevHunts = a.Board.HuntsStarted;
            // BUG-1154's measure (at think end: compare like with like, both read the same inputs).
            bool fixedRule = NothingEverBuyable(a);
            if (fixedRule && !a.Starved())
            {
                s.MissedByRule++;
                s.LongestMissedRun = Math.Max(s.LongestMissedRun, ++s.MissedRun);
            }
            else s.MissedRun = 0;
            if (fixedRule) { if (s.FixedRunStart < 0) s.FixedRunStart = a.Now; }
            else s.FixedRunStart = -1;
            if (s.FixedRunStart >= 0 && s.FixedFirstDue < 0 && a.Now - s.FixedRunStart >= Math.Max(1, a.Rules.StarvedWaveAfterTicks)) s.FixedFirstDue = a.Now;
        };
    }

    private static Simulation Build(string map, ulong seed, AiDifficulty a, AiDifficulty b, int players, int[]? teams, GameData data)
    {
        var seats = new PlayerSeat[players];
        for (int p = 0; p < players; p++)
            seats[p] = new PlayerSeat(p % 2, SeatController.Ai, p == 0 ? a : b, teams == null ? 0 : teams[p]);
        int units = players > 2 ? 1024 : 400;
        if (map == "generated") return AiScenes.Generated(seats, seed, false, out _, units: units, data: data);
        int size = data.Maps[data.FindMap(map)].Size;
        return AiScenes.Generated(seats, seed, false, out _, units: units, data: data,
            configure: c => c with { MapId = map, Map = c.Map with { Width = size, Height = size } });
    }

    private (int End, bool Over, string Winner, Seat[] Seats) Play(string map, ulong seed, AiDifficulty a, AiDifficulty b, int players, int[]? teams, int bound, GameData data, string label)
    {
        Simulation sim = Build(map, seed, a, b, players, teams, data);
        var seats = new Seat[players];
        for (int p = 0; p < players; p++) Watch(sim, p, seats[p] = new Seat());
        while (sim.TickNumber < bound && !sim.MatchOver) sim.Tick();
        string winner = CriterionMatch.Winner(sim.World);
        _out.WriteLine($"{label}: {map} seed {seed} {a} v {b}{(players > 2 ? $" {players} seats" + (teams != null ? " 2v2" : " ffa") : "")}, knob {sim.AiOf(0)!.Rules.StarvedWaveAfterTicks} ticks: "
            + $"over {sim.MatchOver} at {sim.TickNumber} (bound {bound}), winner {winner}");
        for (int p = 0; p < players; p++)
        {
            Seat s = seats[p];
            AiPlayer ai = sim.AiOf(p)!;
            _out.WriteLine($"  seat {p}: {s.Thinks} thinks, oracle {s.OracleChecks} checks {(s.OracleError ?? "clean")}; stamp first {s.FirstStamp}, stamps {s.Stamps}, clears {s.Clears}, "
                + $"first due {s.FirstDue}, waves due {s.WavesDue}, hunts due {s.HuntsDue}, first starved wave {s.FirstStarvedWave}; "
                + $"BUG-1154: {s.MissedByRule} thinks nothing-buyable-but-not-starved (longest run {s.LongestMissedRun}), corrected rule first due {s.FixedFirstDue}; "
                + $"end: gold {ai.Gold}, wood {ai.Wood}, army {ai.ArmyCount}, workers {ai.WorkerCount}, waves {ai.Board.WavesStarted}, hunts {ai.Board.HuntsStarted}, defeated {sim.World.IsDefeated(p)}");
        }
        return (sim.TickNumber, sim.MatchOver, winner, seats);
    }

    /// <summary>
    /// QA focus (d) and round 1's fog oracle: the 8 <c>NonHardMatchPinQaTests</c> matches (Easy / Normal v Normal, to 36,000)
    /// and the six M5-5 criterion rows (to their bounds), shipped data: the fog oracle is clean on every think of every seat
    /// and no starved wave starts before tick 12,000 (early poverty with known mines is not starvation).
    /// </summary>
    [Theory]
    [InlineData("raraku", 1UL, AiDifficulty.Normal, 2, 36_000)]
    [InlineData("raraku", 1UL, AiDifficulty.Easy, 2, 36_000)]
    [InlineData("pale_hills", 1UL, AiDifficulty.Normal, 2, 36_000)]
    [InlineData("pale_hills", 1UL, AiDifficulty.Easy, 2, 36_000)]
    [InlineData("pale_hills", 3UL, AiDifficulty.Normal, 2, 36_000)]
    [InlineData("pale_hills", 3UL, AiDifficulty.Easy, 2, 36_000)]
    [InlineData("vathar_crossing", 1UL, AiDifficulty.Normal, 2, 36_000)]
    [InlineData("vathar_crossing", 1UL, AiDifficulty.Easy, 2, 36_000)]
    [InlineData("generated", 2UL, AiDifficulty.Normal, 2, 24_000)]
    [InlineData("vathar_crossing", 2UL, AiDifficulty.Normal, 4, 72_000)]
    [InlineData("vathar_crossing", 1UL, AiDifficulty.Normal, -4, 72_000)]
    public void APinnedMatch_TheFogOracleHolds_AndNoStarvedWaveFiresBefore12000(string map, ulong seed, AiDifficulty first, int players, int bound)
    {
        int[]? teams = players < 0 ? new[] { 1, 2, 1, 2 } : null;
        var r = Play(map, seed, first, AiDifficulty.Normal, Math.Abs(players), teams, bound, TestSim.Data, "shipped");
        foreach (Seat s in r.Seats)
        {
            Assert.True(s.OracleError == null, s.OracleError);
            Assert.True(s.FirstStarvedWave < 0 || s.FirstStarvedWave >= 12_000, $"a starved wave at {s.FirstStarvedWave}");
        }
    }

    private static readonly Lazy<GameData> KnobOff = new(() => AiScenes.DataWith(dir =>
        dir.EditJson("common/rules.json", r => r["ai"]!["starvedWaveAfter"] = 0)));

    /// <summary>
    /// Measurement (criterion 3 / BUG-1153): Pale Hills seed 1 and seed 2 Normal v Normal to 72,000 with the shipped knob and with
    /// it at 0 (Normal's counter shift on in both): whether the lever is what ends them, and how often BUG-1154's reading
    /// would have called a seat starved that the shipped rule did not. Asserts only the fog oracle; the numbers go to the report.
    /// </summary>
    [Theory]
    [InlineData("pale_hills", 1UL, false)]
    [InlineData("pale_hills", 1UL, true)]
    [InlineData("pale_hills", 2UL, false)]
    [InlineData("pale_hills", 2UL, true)]
    public void PaleHills_ToSeventyTwoThousand_ShippedAndKnobOff_Measured(string map, ulong seed, bool off)
    {
        var r = Play(map, seed, AiDifficulty.Normal, AiDifficulty.Normal, 2, null, 72_000, off ? KnobOff.Value : TestSim.Data, off ? "knob 0" : "shipped");
        foreach (Seat s in r.Seats) Assert.True(s.OracleError == null, s.OracleError);
    }
}
