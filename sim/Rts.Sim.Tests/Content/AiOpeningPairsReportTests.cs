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
/// yet, its gold or wood not in hand, no known free mine, its building razed; M5-9a: a build step with no worker alive;
/// D-R39: "beaten: hall lost", no finished town hall of the side's stands). Everything else
/// is printed for the owner: the first wave, Age II, the defeat, the steps done. D-H6: a step waiting on gold or wood also
/// prints since when it has been due, how long it waited and the banks at the end; a wait over 5:00 is marked as
/// BUG-0981's pattern (printed, not failed: since the sim's M5-4g the AI reserves for a due step, but only while its army
/// is wave-sized and the base is not defended, so long waits remain, docs/03 "Implementation (M5-4g)"). The BUG-0982 row
/// measures <c>cadre_rush</c>'s wave threshold against <c>raider_rush</c>. D-R32 (2026-10-26): each pairing run also pins
/// its cells of both pages' "Age II against ..." table (<see cref="AssertAgeTwoCells"/>; the 2nd and 3rd seeds are
/// <c>QA/Content/D21OpeningsQaTests</c>' runs).
/// <para>
/// D-R39 (2026-11-15, after the sim's M6-S10 gather change) re-states "both sides started a wave" for one named case
/// (<see cref="DefeatedBeforeDue"/>): a side defeated before its first wave was ever due (<see cref="NoWave"/>) is
/// reported, not failed, when it is named there. The list is empty: its one entry, (0, 2) seed 3's <c>holy_war</c> side
/// (defeated at 12:29 under the sim's M6-S10), was taken out by M6-SH2, when that side survived and waved. M6-S12 named
/// the same side again in <see cref="AliveWithoutAWave"/> (alive at 20:00 with no wave, its army never at its 20-pop
/// threshold; BUG-1553), and D33 (2026-11-17) took it out: <c>holy_war</c> waves at 14 army pop since then and the side
/// waves on seed 3 (<c>Content/HolyWarFirstWaveReportTests</c>, whirlwind.md "The holy war's first wave"). Any side
/// defeated without a wave, a side alive at 20:00 without one, or a named side if its wave was due before it fell, fails.
/// </para>
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

    /// <summary>The mark a long gold / wood wait carries in the output (a report, not a failure: M5-4g's reserve is gated, docs/03 "Implementation (M5-4g)").</summary>
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
        var firstDue = new[] { -1, -1 };
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
                if (firstDue[p] < 0 && defeated[p] < 0 && sim.TickNumber % ai.ThinkTicks == 0 && WaveDue(sim, ai)) firstDue[p] = sim.TickNumber;
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
            List<string> waiting = Waiting(sim, ai, defeated[p], deadEnds, out int done);
            _out.WriteLine($"  {side}: steps done {done} / {ai.Steps.Length}; waiting: {(waiting.Count == 0 ? "none" : string.Join("; ", waiting))}");
            int longWaits = waiting.Count(x => x.Contains(LongWaitFlag, StringComparison.Ordinal));
            if (longWaits > 0) _out.WriteLine($"  {side}: {longWaits} step(s) due over 5:00 and still short of gold or wood at 20:00 (BUG-0981's pattern, left by M5-4g's gated reserve; not a failure)");
            if (Array.IndexOf(AliveWithoutAWave, (malazan, whirlwind, seed, p)) >= 0)
            {
                // M6-S12 (BUG-1553): the named side stands at 20:00 without a wave: reported, not failed; any other outcome fails,
                // so the entry cannot go stale.
                if (ai.Board.WavesStarted == 0 && defeated[p] < 0) _out.WriteLine($"  {side} ({ai.Opening.Id}) sent no wave on seed {seed} and stands at 20:00 (BUG-1553: reported, not failed)");
                else deadEnds.Add($"{side} ({ai.Opening.Id}) on seed {seed} now waves or falls: take it out of AliveWithoutAWave");
                continue;
            }
            bool named = Array.IndexOf(DefeatedBeforeDue, (malazan, whirlwind, seed, p)) >= 0;
            string? noWave = NoWave(side, ai.Opening.Id, seed, ai.Board.WavesStarted, defeated[p], firstDue[p], named, out bool fails);
            if (noWave != null && fails) deadEnds.Add(noWave);
            else if (noWave != null) _out.WriteLine($"  {noWave}");
        }
        Assert.True(deadEnds.Count == 0, string.Join("\n", deadEnds));
        AssertAgeTwoCells(malazan, whirlwind, 1, seed, ageTwo, defeated);
    }

    /// <summary>
    /// Every step of <paramref name="ai"/>'s opening not done, as the report prints it ("step 3 (...): reason"); a step
    /// with no <see cref="WhyWaiting"/> reason reads "DEAD END" and is added to <paramref name="deadEnds"/> (the row's
    /// failures). <paramref name="done"/> counts the steps done.
    /// </summary>
    internal static List<string> Waiting(Simulation sim, AiPlayer ai, int defeatedTick, List<string> deadEnds, out int done)
    {
        string side = sim.World.Data.Factions[ai.Faction].Key;
        var waiting = new List<string>();
        done = 0;
        for (int i = 0; i < ai.Steps.Length; i++)
        {
            if (ai.Board.StepDone[i]) { done++; continue; }
            AiStep s = ai.Steps[i];
            string? why = WhyWaiting(sim, ai, s, i, defeatedTick);
            string text = $"step {i + 1} ({AiContentTests.StepText(s)}): {why ?? "DEAD END (due at " + Sec(ai.Board.StepDueTick[i]) + ", not done)"}";
            waiting.Add(text);
            if (why == null) deadEnds.Add($"{side} {text}");
        }
        return waiting;
    }

    /// <summary>
    /// D-R39: the "both sides started a wave" verdict for one side; null when it sent a wave. A <paramref name="named"/>
    /// side (<see cref="DefeatedBeforeDue"/>) defeated before its first wave was due (<paramref name="firstDueTick"/> never,
    /// or not before <paramref name="defeatedTick"/>) is reported with <paramref name="fails"/> false; any other side
    /// without a wave fails as before, and so does the named one alive at the end or defeated after its wave was due. A
    /// named side that now waves is printed, to be taken out of the list (a failure, so the list cannot go stale).
    /// </summary>
    internal static string? NoWave(string side, string opening, ulong seed, int wavesStarted, int defeatedTick, int firstDueTick, bool named, out bool fails)
    {
        fails = false;
        if (wavesStarted > 0 && !named) return null;
        if (named && wavesStarted == 0 && defeatedTick >= 0 && (firstDueTick < 0 || firstDueTick >= defeatedTick))
            return $"{side} ({opening}) sent no wave on seed {seed}: defeated at {Sec(defeatedTick)} before its first wave was due (reported, not failed)";
        fails = true;
        if (wavesStarted > 0) return $"{side} ({opening}) on seed {seed} now sends a wave: take it out of DefeatedBeforeDue";
        return defeatedTick >= 0
            ? $"{side} ({opening}) sent no wave in 20 minutes on seed {seed}: defeated at {Sec(defeatedTick)}, its first wave due at {Sec(firstDueTick)}"
            : $"{side} ({opening}) sent no wave in 20 minutes on seed {seed}";
    }

    /// <summary>
    /// D-R39: the one named side the no-wave rule reports instead of failing (Malazan opening, Whirlwind opening, seed,
    /// player): measured one seed of nine for <c>holy_war</c> (<c>Content/HolyWarFirstWaveReportTests</c>), a balance
    /// finding for D33, not a pattern.
    /// </summary>
    // M6-SH2 (the sim's drop-off rule and retrain, handed this list): (0, 2, 3, 1) taken out at the row's own request
    // ("now sends a wave: take it out"): on seed 3 holy_war survives the Malazan first wave and waves at 14:45.
    internal static readonly (int Malazan, int Whirlwind, ulong Seed, int Player)[] DefeatedBeforeDue = Array.Empty<(int, int, ulong, int)>();

    /// <summary>
    /// M6-S12 (the sim's AI rules, handed this class while the data track is stopped; BUG-1553, a finding for the balance
    /// pass, D33): the one named side the no-wave rule reports instead of failing because it stands at 20:00 without a wave.
    /// (0, 2) seed 3: the Malazan <c>standard</c> sends five waves from 5:50 and the <c>holy_war</c> army never reaches its
    /// 20-pop threshold (peak 16; 600 thinks defending); it is not defeated (M6-SH2: it waved once, at 14:45).
    /// <c>Content/HolyWarFirstWaveReportTests</c> and whirlwind.md "The holy war's first wave" print it. Any other side alive
    /// at 20:00 without a wave still fails, and so does this one if it waves or falls.
    /// </summary>
    // D33 (2026-11-17, BUG-1553): (0, 2, 3, 1) taken out; holy_war's wave is 14 army pop (was 20) and the side waves on
    // seed 3 (Content/D33BalanceReportTests' 14-pop row: first wave 6:21).
    internal static readonly (int Malazan, int Whirlwind, ulong Seed, int Player)[] AliveWithoutAWave = Array.Empty<(int, int, ulong, int)>();

    /// <summary>
    /// D-R39: whether <paramref name="ai"/>'s first wave is due now, from world state: its live army (every unit, so at
    /// least the free ones the military counts) meets the opening's <c>attack</c> pop and cost scaled by the difficulty's
    /// wave size, as <c>MilitaryManager.TryStart</c> asks, or the starved charge is due. The earliest tick a wave could start.
    /// </summary>
    internal static bool WaveDue(Simulation sim, AiPlayer ai)
    {
        if (ai.StarvedWaveDue) return true;
        var u = sim.World.Units;
        GameData d = sim.World.Data;
        int half = 0, cost = 0, count = 0;
        for (int s = 0; s < u.Capacity; s++)
        {
            if (!u.Alive[s] || u.Owner[s] != ai.Player) continue;
            UnitDef def = d.Units[u.TypeId[s]];
            if (def.Slot == UnitSlot.Worker) continue;
            half += def.HalfPop;
            cost += def.UnitWorth;
            count++;
        }
        float scale = ai.Knobs.WaveSizeMultiplier;
        return count > 0 && half >= ai.Opening.AttackMinHalfPop * scale && cost >= ai.Opening.AttackMinCost * scale;
    }

    /// <summary>
    /// D-R32 (2026-10-26, M5-4g's reserve): the section, one per page, whose table gives that faction's Age II per pairing
    /// and seed: one row per (own opening, other opening), one column per seed (the pairing's 1st, 2nd and 3rd smallest in
    /// 1-500: this report's run and <c>QA/Content/D21OpeningsQaTests</c>' two).
    /// </summary>
    internal static string AgeTwoSection(string faction) =>
        faction == "malazan" ? "Age II against the Whirlwind (M5-4g, 2026-10-26)" : "Age II against the Malazans (M5-4g, 2026-10-26)";

    /// <summary>The Age II table's header on <paramref name="faction"/>'s page.</summary>
    internal static string[] AgeTwoHeader(string faction) => faction == "malazan"
        ? new[] { "Malazan opening", "Whirlwind opening", "1st seed", "2nd seed", "3rd seed" }
        : new[] { "Whirlwind opening", "Malazan opening", "1st seed", "2nd seed", "3rd seed" };

    /// <summary>A side's cell: "seed 4: 15:19 (defeated 19:53)", "seed 19: never".</summary>
    internal static string AgeTwoCell(ulong seed, int ageTwoTick, int defeatedTick) =>
        $"seed {seed}: {Sec(ageTwoTick)}{(defeatedTick >= 0 ? $" (defeated {Sec(defeatedTick)})" : "")}";

    /// <summary>
    /// Both pages' cell for this pairing's <paramref name="nth"/> seed equals the run (player 0 Malazan, player 1 Whirlwind):
    /// a sim or data change that moves an Age II or a defeat fails here, naming the page, row and seed, until it is re-printed.
    /// </summary>
    internal static void AssertAgeTwoCells(int malazan, int whirlwind, int nth, ulong seed, int[] ageTwo, int[] defeated)
    {
        // Two-faction by design (D-R37, the Producer's ruling on BUG-1186): a report measures what exists; the pins cover every data faction.
        string[] factions = { "malazan", "whirlwind" };
        int[] openings = { malazan, whirlwind };
        for (int p = 0; p < 2; p++)
        {
            string faction = factions[p];
            string own = TestSim.Data.Ai[TestSim.Data.FindFaction(faction)].Openings[openings[p]].Id;
            string other = TestSim.Data.Ai[TestSim.Data.FindFaction(factions[1 - p])].Openings[openings[1 - p]].Id;
            (string[] header, List<string[]> rows) = PageTables.NamedTable(PageTables.FactionLines(faction), AgeTwoSection(faction), faction + ".md");
            Assert.Equal(AgeTwoHeader(faction), header);
            string[]? row = rows.FirstOrDefault(r => r.Length == 5 && r[0] == own && r[1] == other);
            Assert.True(row != null, $"{faction}.md '{AgeTwoSection(faction)}' has no row {own} v {other}");
            string want = AgeTwoCell(seed, ageTwo[p], defeated[p]);
            Assert.True(row![1 + nth] == want,
                $"{faction}.md '{AgeTwoSection(faction)}' row {own} v {other}, seed #{nth}, reads \"{row[1 + nth]}\"; the run gives \"{want}\" (re-print it from the run)");
        }
    }

    /// <summary>Each page's Age II table has one row per pairing, own opening outer, so no row goes unpinned.</summary>
    [Theory]
    [InlineData("malazan")]
    [InlineData("whirlwind")]
    public void AgeTwoTable_OneRowPerPairing(string faction)
    {
        string otherFaction = faction == "malazan" ? "whirlwind" : "malazan";
        var own = TestSim.Data.Ai[TestSim.Data.FindFaction(faction)].Openings;
        var other = TestSim.Data.Ai[TestSim.Data.FindFaction(otherFaction)].Openings;
        (string[] header, List<string[]> rows) = PageTables.NamedTable(PageTables.FactionLines(faction), AgeTwoSection(faction), faction + ".md");
        Assert.Equal(AgeTwoHeader(faction), header);
        var want = new List<string>();
        foreach (AiOpening a in own)
            foreach (AiOpening b in other) want.Add($"{a.Id} v {b.Id}");
        Assert.Equal(want, rows.Select(r => $"{r[0]} v {r[1]}"));
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
    /// (the Producer's, re-stated by D33 under BUG-1524): take the lowest threshold of at most 16 at which seed 8 (the BUG's
    /// seed) waves, no seed that stands at 20:00 at 18 is defeated, and Age II comes on every measured seed no more than 60 s
    /// later than at 18; else keep 18 (D-H6's rule had no "no seed lost" test, and since the sim's M6-S10 it picked 14, where
    /// seed 8 falls). Prints the table the malazan page quotes and the rule's pick; asserts the shipped number is the pick (a
    /// sim change that moves the outcome fails here, and the page and this rule are re-read then). Every run asserts its own
    /// pick of openings.
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
            bool ageTwoInTime = true, noneLost = true;
            for (int j = 0; j < seeds.Length; j++)
            {
                if (ageTwo[0, j] >= 0 && (ageTwo[i, j] < 0 || ageTwo[i, j] > ageTwo[0, j] + 60 * Tps)) ageTwoInTime = false;
                if (fell[0, j] < 0 && fell[i, j] >= 0) noneLost = false;
            }
            _out.WriteLine($"  {pops[i]} pop: seed 8 waves {(seedEightWaves ? "yes" : "no")}; no seed standing at 18 defeated {(noneLost ? "yes" : "no")}; Age II no more than 60 s later than at 18 on every seed {(ageTwoInTime ? "yes" : "no")}");
            if (seedEightWaves && noneLost && ageTwoInTime) { pick = pops[i]; break; }
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
    /// now (since M5-4g the AI reserves for a due step only while its army is wave-sized and the base is not defended, so
    /// production can still spend first, docs/03 "Implementation (M5-4g)"; the wait's length is printed and one over <see cref="LongWaitTicks"/> marked, BUG-0981); an <c>expand</c> with no known free mine (the AI's own view, as the executor asks it); a step whose
    /// building stands no more (a <c>build</c> step completes once; a razed Forge leaves its research waiting); D-R39: the
    /// side has no finished town hall left ("beaten: hall lost": its hall razed, it is beaten down, not held; a side whose
    /// hall stands and that cannot place a step is still a dead end).
    /// </summary>
    internal static string? WhyWaiting(Simulation sim, AiPlayer ai, AiStep s, int i, int defeatedAt)
    {
        World w = sim.World;
        GameData d = w.Data;
        int p = ai.Player;
        if (defeatedAt >= 0) return $"the side was defeated at {Sec(defeatedAt)}";
        if (ai.Board.StepDueTick[i] < 0)
            return s.Trigger == AiStepTrigger.Pop ? $"pop {s.At / 2.0} never reached (pop {w.HalfPop[p] / 2.0} at the end)" : null;
        if (s.Action == AiStepAction.Build && s.Target == ai.HouseType && w.HalfPopCap[p] >= d.Rules.HalfPopCap)
            return "a house step at the hard cap";
        // M5-9a: a side beaten to its last building (no worker alive, not yet defeated) can't build: not a dead end.
        if (s.Action is AiStepAction.Build or AiStepAction.Expand && Pop(sim, p).Workers == 0)
            return "no worker alive to build it (the side is beaten, its buildings still stand)";
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
        if (!Stands(sim, p, x => d.Buildings[x].Slot == BuildingSlot.TownHall)) return HallLost;
        return null;
    }

    /// <summary>D-R39: the reason a step waits on a side with no finished town hall left (see <see cref="WhyWaiting"/>).</summary>
    internal const string HallLost = "beaten: hall lost (no finished town hall of the side's stands)";

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
