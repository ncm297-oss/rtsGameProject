using Rts.Sim.Ai;
using Rts.Sim.Data;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// D21 criterion 2: every Malazan opening against every Whirlwind opening (3 x 3 = 9 pairs), two Normal AI seats on the
/// menu's map (<see cref="AiSpendingReportTests.MenuMap"/>: 12 forests, 8 mines, combat on), 24,000 ticks (20 min). The seed
/// of each pair is the smallest that makes the two seats pick it (<see cref="AiOpeningSeeds.SmallestPair"/>), asserted on
/// the match itself. Asserted: both sides started a wave, and no step is a dead end: at 20:00 every step of each side is
/// done or legitimately waiting, which is one of (a) its <c>pop</c> trigger was never reached, (b) the side was defeated
/// first, (c) a house step at the hard cap, or one of the reasons <see cref="WhyWaiting"/> lists (a requirement not had
/// yet, its gold or wood not in hand, no known free mine, its building razed). Everything else
/// is printed for the owner: the first wave, Age II, the defeat, the steps done. D-H6: a step waiting on gold or wood also
/// prints since when it has been due, how long it waited and the banks at the end; a wait over 5:00 is marked as
/// BUG-0981's pattern (printed, not failed: the AI keeps no reserve for a due step, the sim's M5-4g). The BUG-0982 row
/// measures <c>cadre_rush</c>'s wave threshold against <c>raider_rush</c>.
/// <para>
/// Reads the AI through its internals (<c>InternalsVisibleTo</c>): <c>Simulation.AiOf</c>, <c>AiPlayer.Opening</c>,
/// <c>AiPlayer.HouseType</c>, <c>Blackboard.WavesStarted</c>, <c>StepDueTick</c>, <c>StepDone</c>. Everything else is world state.
/// </para>
/// </summary>
public class AiOpeningPairsReportTests
{
    private const int Tps = SimConstants.TicksPerSecond;
    private const int Ticks = 24_000;

    /// <summary>D-H6: a step due this long and still short of gold or wood is printed as BUG-0981's pattern (5 minutes).</summary>
    internal const int LongWaitTicks = 5 * 60 * Tps;

    /// <summary>The mark a long gold / wood wait carries in the output (a report, not a failure: the fix is the sim's M5-4g).</summary>
    internal const string LongWaitFlag = "BUG-0981 pattern: waited over 5:00 for gold or wood";

    private readonly ITestOutputHelper _out;

    public AiOpeningPairsReportTests(ITestOutputHelper output) => _out = output;

    public static TheoryData<int, int> Pairs()
    {
        var rows = new TheoryData<int, int>();
        int m = TestSim.Data.Ai[TestSim.Data.FindFaction("malazan")].Openings.Length;
        int w = TestSim.Data.Ai[TestSim.Data.FindFaction("whirlwind")].Openings.Length;
        for (int i = 0; i < m; i++)
            for (int j = 0; j < w; j++) rows.Add(i, j);
        return rows;
    }

    /// <summary>The shipped files hold 3 openings each, so the theory below is the 9 pairs (a file with fewer would shrink it silently).</summary>
    [Fact]
    public void ThePairs_AreNine()
    {
        Assert.Equal(9, Pairs().Count());
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void NormalAiVersusAi_EveryOpeningPair_BothWave_NoDeadEndStep(int malazan, int whirlwind)
    {
        ulong seed = AiOpeningSeeds.SmallestPair(malazan, whirlwind);
        SeatController[] ais = { SeatController.Ai, SeatController.Ai };
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Simulation sim = AiSpendingReportTests.MenuMap(AiScenes.Seats(AiDifficulty.Normal, ais), seed);
        Assert.Equal((malazan, whirlwind), (sim.AiOpening(0), sim.AiOpening(1)));
        World w = sim.World;
        GameData d = w.Data;
        var firstWave = new[] { -1, -1 };
        var ageTwo = new[] { -1, -1 };
        var defeated = new[] { -1, -1 };
        var defendingThinks = new int[2];
        var timeline = new List<string>();
        for (int t = 0; t < Ticks; t++)
        {
            sim.Tick();
            if (sim.TickNumber % (2 * 60 * Tps) == 0)
            {
                var row = new List<string>();
                for (int p = 0; p < 2; p++)
                {
                    (int army, int workers) = Pop(sim, p);
                    row.Add($"army {army / 2.0} workers {workers / 2.0} gold {w.Gold[p]} wood {w.Wood[p]} waves {sim.AiOf(p)!.Board.WavesStarted}");
                }
                timeline.Add($"    {Sec(sim.TickNumber)} | {string.Join(" | ", row)}");
            }
            for (int p = 0; p < 2; p++)
            {
                AiPlayer ai = sim.AiOf(p)!;
                if (firstWave[p] < 0 && ai.Board.WavesStarted > 0) firstWave[p] = sim.TickNumber;
                if (ageTwo[p] < 0 && w.Age(p) >= 2) ageTwo[p] = sim.TickNumber;
                if (defeated[p] < 0 && w.IsDefeated(p)) defeated[p] = sim.TickNumber;
                if (sim.TickNumber % ai.ThinkTicks == 0 && ai.Board.Defending) defendingThinks[p]++;
            }
        }
        clock.Stop();
        _out.WriteLine($"malazan {AiOpeningSeeds.Name(sim.AiOf(0)!.Opening)} v whirlwind {AiOpeningSeeds.Name(sim.AiOf(1)!.Opening)}, menu map seed {seed}, Normal v Normal, {Ticks} ticks ({clock.Elapsed.TotalSeconds:0.0} s wall clock)");
        _out.WriteLine("    time | malazan | whirlwind");
        foreach (string row in timeline) _out.WriteLine(row);
        var deadEnds = new List<string>();
        for (int p = 0; p < 2; p++)
        {
            AiPlayer ai = sim.AiOf(p)!;
            string side = d.Factions[ai.Faction].Key;
            _out.WriteLine($"  {side}: waves started {ai.Board.WavesStarted} (first at {Sec(firstWave[p])}); Age II at {Sec(ageTwo[p])}; defeated {(defeated[p] < 0 ? "no" : "at " + Sec(defeated[p]))}; thinks defending {defendingThinks[p]}; pop {w.HalfPop[p] / 2.0} / cap {w.HalfPopCap[p] / 2.0}; gold {w.Gold[p]}, wood {w.Wood[p]}");
            var waiting = new List<string>();
            int done = 0;
            for (int i = 0; i < ai.Opening.BuildOrder.Length; i++)
            {
                if (ai.Board.StepDone[i]) { done++; continue; }
                AiStep s = ai.Opening.BuildOrder[i];
                string? why = WhyWaiting(sim, ai, s, i, defeated[p]);
                string text = $"step {i + 1} ({AiContentTests.StepText(s)}): {why ?? "DEAD END (due at " + Sec(ai.Board.StepDueTick[i]) + ", not done)"}";
                waiting.Add(text);
                if (why == null) deadEnds.Add($"{side} {text}");
            }
            _out.WriteLine($"  {side}: steps done {done} / {ai.Opening.BuildOrder.Length}; waiting: {(waiting.Count == 0 ? "none" : string.Join("; ", waiting))}");
            int longWaits = waiting.Count(x => x.Contains(LongWaitFlag, StringComparison.Ordinal));
            if (longWaits > 0) _out.WriteLine($"  {side}: {longWaits} step(s) due over 5:00 and still short of gold or wood at 20:00 (BUG-0981's pattern, the sim's M5-4g; not a failure)");
            if (ai.Board.WavesStarted == 0) deadEnds.Add($"{side} ({ai.Opening.Id}) sent no wave in 20 minutes on seed {seed}");
        }
        Assert.True(deadEnds.Count == 0, string.Join("\n", deadEnds));
    }

    /// <summary>The malazan page's section holding the BUG-0982 measurement (its first table is pinned to the row below).</summary>
    internal const string Bug0982Section = "Fast Age II against the raider rush (BUG-0982, D-H6, 2026-10-18)";

    /// <summary>The seeds the BUG-0982 measurement runs: the three smallest in 1-500 making <c>cadre_rush</c> v <c>raider_rush</c> (7, 8 and the third).</summary>
    internal static ulong[] CadreRushVersusRaiderRushSeeds()
    {
        var seeds = new List<ulong>();
        for (ulong s = 1; s <= 500 && seeds.Count < 3; s++)
            if (AiOpeningSeeds.Pick(s, 0, AiDifficulty.Normal) == 2 && AiOpeningSeeds.Pick(s, 1, AiDifficulty.Normal) == 1) seeds.Add(s);
        Assert.Equal(3, seeds.Count);
        return seeds.ToArray();
    }

    /// <summary>
    /// BUG-0982, decided by measurement (D-H6): Malazan <c>cadre_rush</c> v Whirlwind <c>raider_rush</c>, Normal v Normal on
    /// the menu's map, 20 minutes, on the pairing's three smallest seeds, with <c>cadre_rush</c>'s <c>attack.minArmyPop</c>
    /// at 18 (D21's number), 16 and 14 and <c>minArmyCost</c> the floor of the Age I mix at that pop (D21's rule). The rule
    /// (the Producer's): take the lowest threshold of at most 16 at which seed 8 (the BUG's seed) waves and Age II comes on
    /// every measured seed no more than 60 s later than at 18; else keep 18. Prints the table the malazan page quotes and
    /// the rule's pick; asserts the shipped number is the pick (a sim change that moves the outcome fails here, and the page
    /// and this rule are re-read then). Every run asserts its own pick of openings.
    /// </summary>
    [Fact]
    [Trait("Category", "Stress")]
    public void Bug0982_CadreRushVersusRaiderRush_WaveThresholdTable()
    {
        ulong[] seeds = CadreRushVersusRaiderRushSeeds();
        Assert.Equal(new ulong[] { 7, 8 }, seeds.Take(2));
        int[] pops = { 18, 16, 14 };
        int malazan = TestSim.Data.FindFaction("malazan");
        AiOpening shipped = TestSim.Data.Ai[malazan].Openings[2];
        Assert.Equal("cadre_rush", shipped.Id);
        double perPop = 0, weight = 0;
        AiComposition one = shipped.Compositions[0];
        for (int k = 0; k < one.Units.Length; k++)
        {
            UnitDef u = TestSim.Data.Units[one.Units[k]];
            weight += one.Weights[k];
            perPop += one.Weights[k] * (u.CostGold + u.CostWood) / (u.HalfPop / 2.0);
        }
        perPop /= weight;
        var waved = new int[pops.Length, seeds.Length];
        var ageTwo = new int[pops.Length, seeds.Length];
        var fell = new int[pops.Length, seeds.Length];
        var printed = new List<string>();
        _out.WriteLine($"BUG-0982: cadre_rush v raider_rush, Normal v Normal, menu map, {Ticks} ticks; the Age I mix costs {perPop:0.##} a pop");
        _out.WriteLine("| minArmyPop / minArmyCost | seed | waved at (waves) | Age II at | defeated at | peak army pop |");
        _out.WriteLine("| --- | --- | --- | --- | --- | --- |");
        for (int i = 0; i < pops.Length; i++)
        {
            int pop = pops[i], cost = (int)Math.Floor(perPop * pop);
            GameData data = AiScenes.DataWithAi("malazan", root =>
                AiScenes.Opening(root, 2)["attack"] = new System.Text.Json.Nodes.JsonObject { ["minArmyPop"] = pop, ["minArmyCost"] = cost });
            for (int j = 0; j < seeds.Length; j++)
            {
                Simulation sim = AiSpendingReportTests.MenuMap(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Ai), seeds[j], data);
                Assert.Equal((2, 1), (sim.AiOpening(0), sim.AiOpening(1)));
                (waved[i, j], ageTwo[i, j], fell[i, j]) = (-1, -1, -1);
                int peak = 0;
                for (int t = 0; t < Ticks; t++)
                {
                    sim.Tick();
                    if (t % 20 == 0) peak = Math.Max(peak, Pop(sim, 0).Army);
                    if (waved[i, j] < 0 && sim.AiOf(0)!.Board.WavesStarted > 0) waved[i, j] = sim.TickNumber;
                    if (ageTwo[i, j] < 0 && sim.World.Age(0) >= 2) ageTwo[i, j] = sim.TickNumber;
                    if (fell[i, j] < 0 && sim.World.IsDefeated(0)) fell[i, j] = sim.TickNumber;
                }
                printed.Add($"| {pop} / {cost:#,0} | {seeds[j]} | {Sec(waved[i, j])} ({sim.AiOf(0)!.Board.WavesStarted}) | {Sec(ageTwo[i, j])} | {Sec(fell[i, j])} | {peak / 2.0} |");
                _out.WriteLine(printed[^1]);
            }
        }
        int pick = pops[0];
        for (int i = pops.Length - 1; i >= 1; i--)
        {
            bool seedEightWaves = waved[i, 1] >= 0;
            bool ageTwoInTime = true;
            for (int j = 0; j < seeds.Length; j++)
                if (ageTwo[0, j] >= 0 && (ageTwo[i, j] < 0 || ageTwo[i, j] > ageTwo[0, j] + 60 * Tps)) ageTwoInTime = false;
            _out.WriteLine($"  {pops[i]} pop: seed 8 waves {(seedEightWaves ? "yes" : "no")}; Age II no more than 60 s later than at 18 on every seed {(ageTwoInTime ? "yes" : "no")}");
            if (seedEightWaves && ageTwoInTime) { pick = pops[i]; break; }
        }
        _out.WriteLine($"  the rule picks {pick} pop; cadre_rush ships {shipped.AttackMinHalfPop / 2.0} pop / {shipped.AttackMinCost}");
        Assert.True(pick * 2 == shipped.AttackMinHalfPop,
            $"BUG-0982's rule now picks {pick} pop but cadre_rush ships {shipped.AttackMinHalfPop / 2.0}: re-read the table, the malazan page's measurement and the description");
        // The malazan page's table is this output, cell for cell (re-print it from the lines above when a number moves).
        (string[] header, List<string[]> rows) = PageTables.NamedTable(PageTables.FactionLines("malazan"), Bug0982Section, "malazan.md");
        Assert.Equal(PageTables.Cells("| minArmyPop / minArmyCost | seed | waved at (waves) | Age II at | defeated at | peak army pop |"), header);
        Assert.Equal(printed.Select(r => string.Join(" | ", PageTables.Cells(r))), rows.Select(r => string.Join(" | ", r)));
    }

    /// <summary>
    /// Why a step that is not done is legitimately waiting at the end, or null for a dead end (see the type's summary). The
    /// reasons, in the order tried: the side is out; a <c>pop</c> trigger never reached; a house step at the hard cap; a
    /// tech or building it requires not had yet (the step that gives it is itself waiting); its gold or wood not in hand
    /// now (the AI keeps no reserve for a due step: production spends first, a known sim-side limit, docs/03 "Implementation
    /// (M5-2)"; the wait's length is printed and one over <see cref="LongWaitTicks"/> marked, BUG-0981); an <c>expand</c> with no known free mine (the AI's own view, as the executor asks it); a step whose
    /// building stands no more (a <c>build</c> step completes once; a razed Forge leaves its research waiting).
    /// </summary>
    private static string? WhyWaiting(Simulation sim, AiPlayer ai, AiStep s, int i, int defeatedAt)
    {
        World w = sim.World;
        GameData d = w.Data;
        int p = ai.Player;
        if (defeatedAt >= 0) return $"the side was defeated at {Sec(defeatedAt)}";
        if (ai.Board.StepDueTick[i] < 0)
            return s.Trigger == AiStepTrigger.Pop ? $"pop {s.At / 2.0} never reached (pop {w.HalfPop[p] / 2.0} at the end)" : null;
        if (s.Action == AiStepAction.Build && s.Target == ai.HouseType && w.HalfPopCap[p] >= d.Rules.HalfPopCap)
            return "a house step at the hard cap";
        (int gold, int wood) = s.Action switch
        {
            AiStepAction.Build => (d.Buildings[s.Target].CostGold, d.Buildings[s.Target].CostWood),
            AiStepAction.Expand => (d.Buildings[ai.TownHallType].CostGold, d.Buildings[ai.TownHallType].CostWood),
            AiStepAction.Research => (d.Techs[s.Target].CostGold, d.Techs[s.Target].CostWood),
            _ => (d.Units[s.Target].CostGold, d.Units[s.Target].CostWood),
        };
        var needs = s.Action switch
        {
            AiStepAction.Build => d.Buildings[s.Target].RequiresTechs,
            AiStepAction.Research => d.Techs[s.Target].RequiresTechs,
            AiStepAction.Train => d.Units[s.Target].RequiresTechs,
            _ => System.Collections.Immutable.ImmutableArray<int>.Empty,
        };
        foreach (int t in needs)
            if (!w.HasTech(p, t)) return $"waiting on {d.Techs[t].DisplayName}, not researched";
        if (s.Action == AiStepAction.Build)
            foreach (int b in d.Buildings[s.Target].RequiresBuildings)
                if (!Stands(sim, p, x => x == b)) return $"waiting on {d.Buildings[b].DisplayName}, none finished";
        if (w.Gold[p] < gold || w.Wood[p] < wood)
        {
            int waited = sim.TickNumber - ai.Board.StepDueTick[i];
            string flag = waited > LongWaitTicks ? $" [{LongWaitFlag}]" : "";
            return $"can't pay {gold} gold / {wood} wood (holds {w.Gold[p]} / {w.Wood[p]} at the end; due since {Sec(ai.Board.StepDueTick[i])}, waited {Sec(waited)}){flag}";
        }
        if (s.Action == AiStepAction.Expand
            && BuildOrderExecutor.NearestNode(ai, ResourceKind.Gold, BuildOrderExecutor.HallCentre(ai), ai.Rules.ExpandReachCells) < 0)
            return "no known free gold mine (the AI's view)";
        if (s.Action == AiStepAction.Research && !Stands(sim, p, x => d.Buildings[x].Slot == d.Techs[s.Target].ResearchedAtSlot))
            return $"no finished {d.Techs[s.Target].ResearchedAtSlot} stands (razed?)";
        if (s.Action == AiStepAction.Train && w.HalfPop[p] + d.Units[s.Target].HalfPop > w.HalfPopCap[p]) return "the population is at its cap";
        return null;
    }

    /// <summary>Whether a finished building of <paramref name="player"/>'s whose type passes <paramref name="type"/> stands.</summary>
    private static bool Stands(Simulation sim, int player, Func<int, bool> type)
    {
        var b = sim.World.Buildings;
        for (int s = 0; s < b.Capacity; s++)
            if (b.Alive[s] && b.Owner[s] == player && !b.UnderConstruction[s] && type(b.TypeId[s])) return true;
        return false;
    }

    /// <summary>The player's live army and worker half-pop now (the units store).</summary>
    private static (int Army, int Workers) Pop(Simulation sim, int player)
    {
        var u = sim.World.Units;
        GameData d = sim.World.Data;
        int army = 0, workers = 0;
        for (int s = 0; s < u.Capacity; s++)
        {
            if (!u.Alive[s] || u.Owner[s] != player) continue;
            UnitDef def = d.Units[u.TypeId[s]];
            if (def.Slot == UnitSlot.Worker) workers += def.HalfPop;
            else army += def.HalfPop;
        }
        return (army, workers);
    }

    private static string Sec(int tick) => tick < 0 ? "never" : $"{tick / Tps / 60}:{tick / Tps % 60:00}";
}
