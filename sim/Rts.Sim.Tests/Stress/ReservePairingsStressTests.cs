using Rts.Sim.Ai;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Tests.Content;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA (M5-4g, session 2026-10-18-1814): BUG-0981's reserve measured independently on shipped content. Normal v Normal on
/// the menu's map for 20 minutes, the 9 Malazan x Whirlwind pairings on their 1st, 2nd and 3rd smallest seeds in 1-500
/// (27 runs, the seeds the pairs report and <c>D21OpeningsQaTests</c> use), each run ticked as twins side by side (state
/// hash compared every 50 ticks, the AI command counts at the end). Per side it prints: Age II, the first wave and the
/// count, the defeat, BUG-0981's flag (a due step not done at 20:00, its requirements had, the bank short of its cost,
/// due over 5:00: the pairs report's rule) with, for an <c>expand</c>, whether a free gold mine and an ungated gold node
/// are known, the gatherers' gold / wood switches per minute (the shift's oscillation), and the reserve's statistics
/// (thinks with a reserve, the longest hold on one step). Asserted: the twins agree, no bank goes negative, and every side
/// alive at 20:00 reached Age II but the named <see cref="KnownNoAgeTwo"/> (criterion 3 as restated after its waiver,
/// BUG-1027), with the 27 runs' totals (no-wave sides, Malazan waves, defeats). The wave and flag counts are printed for the report: the
/// data-owned rows (<c>Content/AiOpeningPairsReportTests</c>, <c>QA/Content/D21OpeningsQaTests</c>) hold their own gates.
/// Also BUG-1010's six seeds and Easy / Hard on three seeds. Lines starting "ROW" are machine-read by the QA report.
/// </summary>
public class ReservePairingsStressTests
{
    private const int Tps = SimConstants.TicksPerSecond;
    private const int Ticks = 24_000;
    private const int LongWait = 5 * 60 * Tps;
    private readonly ITestOutputHelper _out;

    public ReservePairingsStressTests(ITestOutputHelper output) => _out = output;

    public static TheoryData<int, int, int> PairingSeeds()
    {
        var rows = new TheoryData<int, int, int>();
        for (int nth = 1; nth <= 3; nth++)
            for (int m = 0; m < 3; m++)
                for (int w = 0; w < 3; w++) rows.Add(m, w, nth);
        return rows;
    }

    /// <summary>The pairing's <paramref name="nth"/>-smallest seed in 1-500 (both seats Normal).</summary>
    internal static ulong NthSeed(int malazan, int whirlwind, int nth)
    {
        int found = 0;
        for (ulong s = 1; s <= 500; s++)
            if (AiOpeningSeeds.Pick(s, 0, AiDifficulty.Normal) == malazan && AiOpeningSeeds.Pick(s, 1, AiDifficulty.Normal) == whirlwind && ++found == nth) return s;
        Assert.Fail($"no {nth}th seed for {malazan} x {whirlwind}");
        return 0;
    }

    /// <summary>
    /// BUG-1027 / BUG-1024 (the restated criterion 3, waived at the 2026-10-18-1814 ACCEPT: no gate form keeps both every
    /// Age II and every wave, docs/03 "Implementation (M5-4g)"): the sides alive at 20:00 without Age II, by (Malazan
    /// opening, Whirlwind opening, nth seed, player). Each is the fix round 2 tree's measured state (c76bc5f, re-run at
    /// a840d45). A listed side that reaches Age II or is defeated prints "take it out".
    /// </summary>
    internal static readonly (int Malazan, int Whirlwind, int Nth, int Player)[] KnownNoAgeTwo =
    {
        // crossbow_push v raider_rush seed 19, Malazan: no wave (its army stays under the wave's size, so the reserve never
        // engages: base behaviour); peak army 7, 910 gold / 15 wood at 20:00, Age II due 7:00 (400 / 200) never paid.
        (1, 1, 3, 0),
        // crossbow_push v standard seed 32, Malazan: wood-starved, one wave at 7:29, 794 gold / 5 wood at 20:00; the Crossbow
        // Range (0 / 150) due since 2:43 and Age II (400 / 200) since 7:00 (round 1 had it defeated at 12:35).
        (1, 0, 2, 0),
        // cadre_rush v standard seed 13, Whirlwind: no gold node known by 20:00 (11 gold / 277 wood; the expand's
        // freeMine=False ungatedGold=False), so Age II (400 / 200, due 7:30) is not Fillable; 3 waves from 6:51.
        (2, 0, 1, 1),
        // crossbow_push v standard seed 9, Whirlwind: the same, no gold node known (9 gold / 235 wood), Age II due 7:30;
        // 5 waves from 6:20.
        (1, 0, 1, 1),
    };

    /// <summary>The restated criterion's totals over the 27 runs (BUG-1027): the base's no-wave sides, Malazan's waves, the defeats.</summary>
    internal const int NoWaveSides = 3, MinMalazanWaves = 79, MaxMalazanDefeats = 8, MaxWhirlwindDefeats = 5;

    /// <summary>The 27 rows' results, by (Malazan, Whirlwind, nth); the row that completes the set asserts the totals (the rows of one class run one at a time).</summary>
    private static readonly SortedDictionary<int, Result> s_pairings = new();

    /// <summary>
    /// The twins agree (state hash every 50 ticks, the AI command counts at the end), no bank goes negative, and every side
    /// alive at 20:00 reached Age II except <see cref="KnownNoAgeTwo"/> (BUG-1027's restatement). When all 27 rows have run
    /// in this process, the totals are printed and asserted: no-wave sides = <see cref="NoWaveSides"/>, Malazan waves at
    /// least <see cref="MinMalazanWaves"/> (the base's), Malazan / Whirlwind defeats at most 8 / 5 (the base's). Until then
    /// (a filtered or partial run) each row prints that the totals were not asserted (BUG-1041).
    /// </summary>
    [Theory]
    [Trait("Category", "Stress")]
    [MemberData(nameof(PairingSeeds))]
    public void Pairing_Twins_AgeTwoForEverySideAlive(int malazan, int whirlwind, int nth)
    {
        ulong seed = NthSeed(malazan, whirlwind, nth);
        Result r = Run(seed, AiDifficulty.Normal, twins: true, label: $"pair {malazan}x{whirlwind} #{nth}");
        Assert.Equal((malazan, whirlwind), r.Openings);
        Totals? totals = null;
        int ran;
        lock (s_pairings)
        {
            s_pairings[(malazan * 3 + whirlwind) * 4 + nth] = r;
            ran = s_pairings.Count;
            if (ran == 27) totals = Totals.Of(s_pairings.Values);
        }
        // BUG-1041: a filtered or partial run asserts no total; say so, so a green partial run doesn't read as "totals held".
        if (ran < 27) _out.WriteLine($"TOTALS not asserted (yet): {ran} of 27 rows have run in this process; only the row that completes the 27 asserts them");
        string missing = AgeTwoMisses(malazan, whirlwind, nth, seed, r, KnownNoAgeTwo, _out.WriteLine);
        Assert.True(missing.Length == 0, missing);
        if (totals is not { } t) return;
        _out.WriteLine($"TOTALS over 27 runs: no-wave sides {t.NoWave} (= {NoWaveSides}), Malazan waves {t.MalazanWaves} (>= {MinMalazanWaves}), " +
            $"Whirlwind waves {t.WhirlwindWaves}, Malazan / Whirlwind defeats {t.MalazanDefeats} / {t.WhirlwindDefeats} (<= {MaxMalazanDefeats} / {MaxWhirlwindDefeats}), " +
            $"alive without Age II {t.NoAgeTwo} of {t.Alive}");
        Assert.Equal(NoWaveSides, t.NoWave);
        Assert.True(t.MalazanWaves >= MinMalazanWaves, $"Malazan waves {t.MalazanWaves} < {MinMalazanWaves}");
        Assert.True(t.MalazanDefeats <= MaxMalazanDefeats && t.WhirlwindDefeats <= MaxWhirlwindDefeats, $"defeats {t.MalazanDefeats} / {t.WhirlwindDefeats}");
    }

    /// <summary>
    /// The restated rule for one run: a side alive at 20:00 without Age II fails unless it is in <paramref name="allowed"/>;
    /// a listed side that now passes prints "take it out". The failures, one line each (empty when none).
    /// </summary>
    internal static string AgeTwoMisses(int malazan, int whirlwind, int nth, ulong seed, Result r,
        (int Malazan, int Whirlwind, int Nth, int Player)[] allowed, Action<string> print)
    {
        string missing = "";
        for (int p = 0; p < 2; p++)
        {
            bool known = Array.IndexOf(allowed, (malazan, whirlwind, nth, p)) >= 0;
            bool passes = r.Sides[p].Defeated >= 0 || r.Sides[p].AgeTwo >= 0;
            if (known && !passes) print($"  KNOWN (BUG-1027): seed {seed} player {p} alive at 20:00 without Age II; see KnownNoAgeTwo");
            else if (known) print($"  the known case seed {seed} player {p} now reaches Age II or is defeated: take it out of KnownNoAgeTwo");
            else if (!passes) missing += $"seed {seed}: side {p} alive at 20:00 without Age II\n";
        }
        return missing;
    }

    /// <summary>The restated criterion's totals over a set of runs.</summary>
    internal readonly record struct Totals(int NoWave, int MalazanWaves, int WhirlwindWaves, int MalazanDefeats, int WhirlwindDefeats, int NoAgeTwo, int Alive)
    {
        /// <summary>Player 0 is Malazan, player 1 Whirlwind in every pairing run.</summary>
        internal static Totals Of(IEnumerable<Result> runs)
        {
            int noWave = 0, mWaves = 0, wWaves = 0, mDef = 0, wDef = 0, noAge = 0, alive = 0;
            foreach (Result r in runs)
                for (int p = 0; p < 2; p++)
                {
                    Side s = r.Sides[p];
                    if (s.Waves == 0) noWave++;
                    if (p == 0) mWaves += s.Waves; else wWaves += s.Waves;
                    if (s.Defeated >= 0) { if (p == 0) mDef++; else wDef++; continue; }
                    alive++;
                    if (s.AgeTwo < 0) noAge++;
                }
            return new Totals(noWave, mWaves, wWaves, mDef, wDef, noAge, alive);
        }
    }

    /// <summary>
    /// BUG-1027's mutation check: an allowance set to a side that passes (here one that reached Age II, and one defeated)
    /// prints "take it out" and fails nothing; an unlisted side alive without Age II fails; a listed one is printed KNOWN.
    /// </summary>
    [Fact]
    public void TheAllowance_SetToAPassingSide_PrintsTakeItOut()
    {
        var reached = new Side { AgeTwo = 9000 };
        var defeated = new Side { Defeated = 12000 };
        var missed = new Side();
        var printed = new List<string>();
        (int, int, int, int)[] mutated = { (1, 1, 3, 0), (1, 1, 3, 1) };
        string fails = AgeTwoMisses(1, 1, 3, 19, new Result((1, 1), new[] { reached, defeated }), mutated, printed.Add);
        Assert.Equal("", fails);
        Assert.Equal(2, printed.Count(l => l.Contains("take it out")));
        printed.Clear();
        fails = AgeTwoMisses(1, 1, 3, 19, new Result((1, 1), new[] { missed, missed }), new[] { (1, 1, 3, 0) }, printed.Add);
        Assert.Contains("side 1 alive at 20:00 without Age II", fails);
        Assert.DoesNotContain("side 0", fails);
        Assert.Contains(printed, l => l.Contains("KNOWN"));
        Assert.Equal(4, KnownNoAgeTwo.Length);
    }

    /// <summary>BUG-1010's six seeds: <c>cadre_rush</c> v <c>raider_rush</c> 8, 14, 18, 23; <c>crossbow_push</c> v <c>raider_rush</c> 19, 25 (printed).</summary>
    [Theory]
    [Trait("Category", "Stress")]
    [InlineData(2, 1, 8)]
    [InlineData(2, 1, 14)]
    [InlineData(2, 1, 18)]
    [InlineData(2, 1, 23)]
    [InlineData(1, 1, 19)]
    [InlineData(1, 1, 25)]
    public void Bug1010Seeds_Printed(int malazan, int whirlwind, int seed)
    {
        Result r = Run((ulong)seed, AiDifficulty.Normal, twins: false, label: $"bug1010 {malazan}x{whirlwind}");
        Assert.Equal((malazan, whirlwind), r.Openings);
    }

    /// <summary>Easy v Easy and Hard v Hard on seeds 1-3 of the menu map: every side alive at 20:00 reached Age II (printed; Easy asserted printed only).</summary>
    [Theory]
    [Trait("Category", "Stress")]
    [InlineData(AiDifficulty.Easy, 1)]
    [InlineData(AiDifficulty.Easy, 2)]
    [InlineData(AiDifficulty.Easy, 3)]
    [InlineData(AiDifficulty.Hard, 1)]
    [InlineData(AiDifficulty.Hard, 2)]
    [InlineData(AiDifficulty.Hard, 3)]
    public void EasyAndHard_Printed(AiDifficulty difficulty, int seed)
    {
        Run((ulong)seed, difficulty, twins: false, label: $"{difficulty}");
    }

    internal sealed class Side
    {
        public int AgeTwo = -1, FirstWave = -1, Waves, Defeated = -1, Switches, GatherSends, ArmyTrained, PeakArmy;
        public int ReserveThinks, Thinks, LongestHold, ShareFlips, ReserveWaveOut, GatheredAtTen = -1;
        public string HoldText = "";
        public readonly List<string> Flags = new();
    }

    internal sealed record Result((int, int) Openings, Side[] Sides);

    private Result Run(ulong seed, AiDifficulty difficulty, bool twins, string label)
    {
        Simulation a = AiSpendingReportTests.MenuMap(AiScenes.Seats(difficulty, SeatController.Ai, SeatController.Ai), seed);
        Simulation? b = twins ? AiSpendingReportTests.MenuMap(AiScenes.Seats(difficulty, SeatController.Ai, SeatController.Ai), seed) : null;
        World w = a.World;
        GameData d = w.Data;
        var sides = new[] { new Side(), new Side() };
        var kindBefore = new byte[2][];
        var genBefore = new int[2][];
        for (int p = 0; p < 2; p++)
        {
            kindBefore[p] = new byte[a.AiOf(p)!.Board.GatherKind.Length];
            genBefore[p] = new int[kindBefore[p].Length];
            AiPlayer ai = a.AiOf(p)!;
            Side s = sides[p];
            ai.SentForTests = c => { if (c.Kind == Commands.CommandKind.Gather) s.GatherSends++; else if (c.Kind == Commands.CommandKind.Train) s.ArmyTrained++; };
#if !QA_BASE
            int lastStep = -1, run = 0, runFrom = 0, runDefending = 0;
            float lastShare = -1f;
            ai.ThoughtForTests = x =>
            {
                s.Thinks++;
                if (x.ReserveStep >= 0) s.ReserveThinks++;
                if (x.ReserveStep >= 0 && x.Board.WaveActive) s.ReserveWaveOut++; // re-check 2: the gate counts an army away on a wave
                bool same = x.ReserveStep >= 0 && x.ReserveStep == lastStep;
                if (!same) { runFrom = x.Now; runDefending = 0; }
                run = same ? run + 1 : (x.ReserveStep >= 0 ? 1 : 0);
                if (x.ReserveStep >= 0 && x.Board.Defending) runDefending++;
                lastStep = x.ReserveStep;
                if (run > s.LongestHold)
                {
                    AiStep st = x.Opening.BuildOrder[x.ReserveStep];
                    s.HoldText = $"step {x.ReserveStep + 1} {st.Action} reserve {x.ReserveGold}/{x.ReserveWood} from {Sec(runFrom)} to {Sec(x.Now)}, bank {x.View.Gold}/{x.View.Wood}, short {(x.ReserveShortGold ? "gold" : "")}{(x.ReserveShortWood ? "wood" : "")}, defending {runDefending} thinks of it";
                }
                s.LongestHold = Math.Max(s.LongestHold, run);
                float share = x.WantedGoldShare();
                if (lastShare >= 0 && share != lastShare) s.ShareFlips++;
                lastShare = share;
            };
#endif
        }
        for (int t = 0; t < Ticks; t++)
        {
            bool think = a.TickNumber % a.AiOf(0)!.ThinkTicks == 0;
            if (think)
                for (int p = 0; p < 2; p++)
                {
                    Array.Copy(a.AiOf(p)!.Board.GatherKind, kindBefore[p], kindBefore[p].Length);
                    Array.Copy(a.AiOf(p)!.Board.UnitGeneration, genBefore[p], genBefore[p].Length);
                }
            a.Tick();
            b?.Tick();
            if (b != null && a.TickNumber % 50 == 0)
                Assert.True(a.StateHash() == b.StateHash(), $"{label} seed {seed}: twins differ at tick {a.TickNumber}");
            for (int p = 0; p < 2; p++)
            {
                Assert.True(w.Gold[p] >= 0 && w.Wood[p] >= 0, $"seed {seed}: player {p} bank negative at {a.TickNumber}");
                AiPlayer ai = a.AiOf(p)!;
                Side s = sides[p];
                if (think)
                {
                    byte[] k = ai.Board.GatherKind;
                    int[] g = ai.Board.UnitGeneration;
                    for (int i = 0; i < k.Length; i++)
                    {
                        int before = kindBefore[p][i] & 3, after = k[i] & 3;
                        if (genBefore[p][i] == g[i] && g[i] != 0 && before != 0 && after != 0 && before != after) s.Switches++;
                    }
                }
                if (s.FirstWave < 0 && ai.Board.WavesStarted > 0) s.FirstWave = a.TickNumber;
                if (s.AgeTwo < 0 && w.Age(p) >= 2) s.AgeTwo = a.TickNumber;
                if (s.Defeated < 0 && w.IsDefeated(p)) s.Defeated = a.TickNumber;
                if (a.TickNumber == Ticks / 2) s.GatheredAtTen = w.GatheredGold[p] + w.GatheredWood[p]; // BUG-1022: gold + wood by 10:00
                if (t % 20 == 0) s.PeakArmy = Math.Max(s.PeakArmy, Army(a, p));
            }
        }
        if (b != null)
            for (int p = 0; p < 2; p++) Assert.Equal(a.AiOf(p)!.CommandsSent, b.AiOf(p)!.CommandsSent);
        for (int p = 0; p < 2; p++)
        {
            AiPlayer ai = a.AiOf(p)!;
            Side s = sides[p];
            s.Waves = ai.Board.WavesStarted;
            if (s.Defeated < 0) Flags(a, ai, s);
            string side = $"{d.Factions[ai.Faction].Key}:{ai.Opening.Id}";
            double minutes = Ticks / (60.0 * Tps);
            _out.WriteLine($"ROW\t{label}\tseed {seed}\t{side}\tageII {Sec(s.AgeTwo)}\twave {Sec(s.FirstWave)} ({s.Waves})\tdefeated {Sec(s.Defeated)}\tpeakArmy {s.PeakArmy / 2.0}\t" +
                $"gathered10 {s.GatheredAtTen}\tgathered20 {w.GatheredGold[p] + w.GatheredWood[p]}\tswitches/min {s.Switches / minutes:0.00}\tgatherSends/min {s.GatherSends / minutes:0.0}\ttrains {s.ArmyTrained}\t" +
                $"reserveThinks {s.ReserveThinks}/{s.Thinks}\treserveWaveOut {s.ReserveWaveOut}\tlongestHold {s.LongestHold}\tshareFlips {s.ShareFlips}\tflags {s.Flags.Count}\t{string.Join(" ; ", s.Flags)}\tend gold {w.Gold[p]} wood {w.Wood[p]}\thold {s.HoldText}");
        }
        return new Result((a.AiOpening(0), a.AiOpening(1)), sides);
    }

    /// <summary>BUG-0981's flag as the pairs report reads it (its <c>WhyWaiting</c> order, the side alive), with the expand's mine facts.</summary>
    private static void Flags(Simulation sim, AiPlayer ai, Side s)
    {
        World w = sim.World;
        GameData d = w.Data;
        int p = ai.Player;
        for (int i = 0; i < ai.Opening.BuildOrder.Length; i++)
        {
            if (ai.Board.StepDone[i] || ai.Board.StepDueTick[i] < 0) continue;
            AiStep st = ai.Opening.BuildOrder[i];
            if (st.Action == AiStepAction.Build && st.Target == ai.HouseType && w.HalfPopCap[p] >= d.Rules.HalfPopCap) continue;
            (int gold, int wood) = st.Action switch
            {
                AiStepAction.Build => (d.Buildings[st.Target].CostGold, d.Buildings[st.Target].CostWood),
                AiStepAction.Expand => (d.Buildings[ai.TownHallType].CostGold, d.Buildings[ai.TownHallType].CostWood),
                AiStepAction.Research => (d.Techs[st.Target].CostGold, d.Techs[st.Target].CostWood),
                _ => (d.Units[st.Target].CostGold, d.Units[st.Target].CostWood),
            };
            var needs = st.Action switch
            {
                AiStepAction.Build => d.Buildings[st.Target].RequiresTechs,
                AiStepAction.Research => d.Techs[st.Target].RequiresTechs,
                AiStepAction.Train => d.Units[st.Target].RequiresTechs,
                _ => System.Collections.Immutable.ImmutableArray<int>.Empty,
            };
            bool blocked = false;
            foreach (int t in needs) if (!w.HasTech(p, t)) blocked = true;
            if (blocked) continue;
            if (w.Gold[p] >= gold && w.Wood[p] >= wood) continue;
            int waited = sim.TickNumber - ai.Board.StepDueTick[i];
            if (waited <= LongWait) continue;
            string extra = "";
            if (st.Action == AiStepAction.Expand)
            {
                bool freeMine = BuildOrderExecutor.ExpansionMine(ai) >= 0;
                extra = $" freeMine={freeMine} ungatedGold={KnowsGold(ai)}";
            }
            s.Flags.Add($"step {i + 1} {st.Action} {(st.Action == AiStepAction.Expand ? "hall" : st.Action == AiStepAction.Research ? d.Techs[st.Target].Key : st.Action == AiStepAction.Build ? d.Buildings[st.Target].Key : d.Units[st.Target].Key)} needs {gold}/{wood} has {w.Gold[p]}/{w.Wood[p]} due {Sec(ai.Board.StepDueTick[i])}{extra}");
        }
    }

    /// <summary>A seen gold node not marked dropped (the no-node guard's rule without the defense ring).</summary>
    private static bool KnowsGold(AiPlayer ai)
    {
        int s = -1;
        while (ai.View.NextResource(ref s, out SeenResource node))
            if (ai.Data.Resources[node.TypeId].Resource == ResourceKind.Gold && ai.Board.NodeDropped[s] != node.Handle.Generation) return true;
        return false;
    }

    private static int Army(Simulation sim, int player)
    {
        var u = sim.World.Units;
        int army = 0;
        for (int s = 0; s < u.Capacity; s++)
            if (u.Alive[s] && u.Owner[s] == player && !sim.World.Data.Units[u.TypeId[s]].IsWorker) army += sim.World.Data.Units[u.TypeId[s]].HalfPop;
        return army;
    }

    private static string Sec(int tick) => tick < 0 ? "never" : $"{tick / Tps / 60}:{tick / Tps % 60:00}";
}
