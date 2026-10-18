using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// D17 report (D15's note: the AI ends its first minutes with gold in hand): on the flat AI scene (<see cref="AiScenes.Flat"/>:
/// a Town Hall, five workers, two mines and 12 trees a base; the other seat a human that does nothing; combat off), Normal,
/// 24,000 ticks (20 min), this prints the AI's gold and wood in hand, its army and worker pop at 5 / 10 / 15 / 20 min, the
/// share of finished ticks each hall / production building had an empty queue, and the waves it started. Only the
/// preconditions are asserted (the seat is an AI of the faction; the match ran 24,000 ticks): the numbers are a report for the
/// owner, and the proposal is in the D17 session report.
/// <para>
/// D18 (QA's D17 note: the flat scene's 12 trees run dry, so re-measure on a real map): the same columns on
/// the generated map, seeds 1 and 7, both factions, combat on (the idle seat's base can fall, and the report says when), and a
/// dated conclusion line on whether wood is still the limit.
/// </para>
/// <para>
/// D-H5 (BUG-0721 items 2-3): the map is the menu's (12 forests, 8 mines: <c>LaunchOptions.DefaultForests</c> /
/// <c>DefaultMines</c>, which QA's <c>D18ReportPreconditionQaTests.D18Reports_MenuConstants_MatchTheGameScripts</c> pins),
/// set up as <see cref="AiScenes.Generated"/> does but with 8 mines (that helper fixes 6); and the conclusion counts only
/// the marks before the idle seat's defeat (a mark after it is a won match, not a spending question).
/// </para>
/// <para>
/// D20 (2026-10-17, M5-4f's gather rates gold 0.85 / wood 0.75, the sized defense and D15 C): re-run, and the menu-map
/// numbers the pages and docs/03 quote are now pinned to this run by <see cref="GeneratedNormal_PagesAndDocs03_QuoteTheReport"/>
/// (the four runs are computed once and shared by both rows).
/// </para>
/// <para>
/// D21 (2026-10-18, three openings per faction): a Normal seat picks its opening by seed, so seeds 1 and 7 no longer say
/// which build they measured. The menu-map runs are now one per opening on Normal, on the smallest seed in 1-50 whose
/// Normal pick is that opening, plus one Easy run (opening 0), each asserting the pick; the pages quote each run under the
/// opening's own heading, with the first wave and Age II added.
/// </para>
/// <para>
/// Reads the AI through its own members (BUG-0641: read from the AI, not re-derived beside it; all <c>internal</c>, reached
/// through <c>InternalsVisibleTo</c>): <c>Simulation.AiOf</c>, <c>AiPlayer.Faction</c>, <c>AiPlayer.Opening.BuildOrder</c>,
/// <c>AiPlayer.Board.WavesStarted</c> and <c>AiPlayer.Board.StepDone</c>.
/// Everything else is world state (<c>World.Gold</c> / <c>Wood</c>, the unit and building stores, the queue counts) or
/// <see cref="GameData"/>. A production building is any building type a unit of the faction is <c>trainedAt</c> (the hall too).
/// </para>
/// </summary>
public class AiSpendingReportTests
{
    private const int Tps = SimConstants.TicksPerSecond;
    private const int Ticks = 24_000;
    private const int MarkEvery = 5 * 60 * Tps;

    /// <summary>The game's default forest count (<c>game/scripts/LaunchOptions.DefaultForests</c>, read 2026-10-14).</summary>
    private const int MenuForests = 12;

    /// <summary>The game's default mine count (<c>game/scripts/LaunchOptions.DefaultMines</c>, read 2026-10-16).</summary>
    private const int MenuMines = 8;

    /// <summary>A mark counts as wood-bound when wood in hand is under this while gold in hand is at least <see cref="GoldPiled"/>.</summary>
    private const int WoodShort = 100;

    private const int GoldPiled = 300;

    private readonly ITestOutputHelper _out;

    public AiSpendingReportTests(ITestOutputHelper output) => _out = output;

    [Theory]
    [InlineData("malazan")]
    [InlineData("whirlwind")]
    public void FlatNormal_Spending_Report(string faction)
    {
        // Player p plays faction p mod 2 (AiScenes.Seats): the AI seat is the faction's, the other a human that does nothing.
        int player = TestSim.Data.FindFaction(faction);
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, Controllers(player)));
        _out.WriteLine($"{faction}, flat scene, Normal, {Ticks} ticks ({Ticks / Tps / 60} min), combat off, the other seat idle");
        Assert.Equal(-1, Report(sim, player, -1, _out.WriteLine).IdleDefeatedAt); // combat off: nobody falls, so every mark counts
    }

    /// <summary>
    /// D18 item 2, per opening since D21 (2026-10-18): the menu's generated map, both factions, combat on, the other seat
    /// idle, and the dated conclusion (marks before the idle seat's defeat). One run per opening on Normal, on the smallest
    /// seed in 1-50 whose Normal pick is that opening (<see cref="AiOpeningSeeds.Smallest"/>), plus one Easy run (Easy plays
    /// opening 0 on every seed: seed 1). Each run asserts the pick it measured (<see cref="Simulation.AiOpening"/>).
    /// </summary>
    [Fact]
    public void GeneratedNormal_Spending_Report()
    {
        GeneratedRun run = Generated.Value;
        foreach (string line in run.Lines) _out.WriteLine(line);
        foreach (SeatRun r in run.Seats)
        {
            // The cut-off: exactly the marks that fell before the defeat count (all of them if the seat stood).
            int before = 0;
            for (int t = MarkEvery; t <= Ticks; t += MarkEvery) if (r.DefeatedAt < 0 || t < r.DefeatedAt) before++;
            Assert.Equal(before, r.Marks);
            Assert.Equal(Ticks / MarkEvery, r.AllMarks);
        }
        // Every opening of both factions measured once on Normal, opening 0 once more on Easy.
        foreach (string faction in new[] { "malazan", "whirlwind" })
        {
            int n = TestSim.Data.Ai[TestSim.Data.FindFaction(faction)].Openings.Length;
            Assert.Equal(Enumerable.Range(0, n), run.Seats.Where(s => s.Faction == faction && s.Difficulty == AiDifficulty.Normal).Select(s => s.Opening));
            Assert.Equal(new[] { 0 }, run.Seats.Where(s => s.Faction == faction && s.Difficulty == AiDifficulty.Easy).Select(s => s.Opening));
        }
    }

    /// <summary>
    /// D20's report-conformance row, per opening since D21: each opening's "Spending on the menu's map" paragraph on its
    /// faction page (under the opening's own heading, <see cref="AiContentTests.OpeningHeading"/>) quotes its run's sentence
    /// (<see cref="Sentence"/>: the seed and why, the idle base's fall time, gold and wood in hand at every 5-minute mark
    /// before it, the first wave) and the re-print's date; opening 0's also quotes the Easy run. docs/03's D21 spending
    /// bullet quotes the verdict and every run's fall time. A number the data or the sim moves fails here, by page and
    /// opening, until the paragraph is re-printed.
    /// </summary>
    [Fact]
    public void GeneratedNormal_PagesAndDocs03_QuoteTheReport()
    {
        GeneratedRun run = Generated.Value;
        string verdictLine = $"Across both factions {run.WoodBound} of those {run.Marks} marks had wood under {WoodShort} with gold at {GoldPiled} or more";
        string verdictQuote = $"the report reads \"{run.Verdict}\"";
        foreach (string faction in new[] { "malazan", "whirlwind" })
        {
            AiDef ai = TestSim.Data.Ai[TestSim.Data.FindFaction(faction)];
            string[] lines = PageTables.FactionLines(faction);
            for (int k = 0; k < ai.Openings.Length; k++)
            {
                string where = $"{faction}.md opening {k + 1} ({ai.Openings[k].Id})";
                string page = BoldParagraph(AiContentTests.OpeningLines(lines, faction, k, ai.Openings[k]), "**Spending on the menu's map", where);
                var wants = new List<string> { "(D21, 2026-10-18", Sentence(run.Seat(faction, k, AiDifficulty.Normal)) };
                if (k == 0) wants.Add(Sentence(run.Seat(faction, 0, AiDifficulty.Easy)));
                foreach (string want in wants)
                    Assert.True(page.Contains(want, StringComparison.Ordinal), $"{where} 'Spending on the menu's map' lacks the report's \"{want}\"; it reads: {page}");
            }
        }

        string docs03 = BoldParagraph(PageTables.Lines(Path.Combine("docs", "03-technical-design.md")), "- **Spending per opening (D21, 2026-10-18", "docs/03");
        var all = new List<string> { verdictLine, verdictQuote };
        foreach (SeatRun s in run.Seats) all.Add(Short(s));
        foreach (string want in all)
            Assert.True(docs03.Contains(want, StringComparison.Ordinal), $"docs/03's D21 spending bullet lacks the report's \"{want}\"; it reads: {docs03}");
    }

    /// <summary>
    /// A run as its opening's page paragraph says it: "Normal, seed 2 (the smallest seed in 1-50 whose Normal pick is this
    /// opening): the idle base falls at 512 s; in hand at 5 min 156 gold / 25 wood; the first wave at 401 s; Age II in at
    /// 450 s" (Age II "in": researched, not queued).
    /// </summary>
    internal static string Sentence(SeatRun r)
    {
        string why = r.Difficulty == AiDifficulty.Easy
            ? "(Easy always plays the first opening)"
            : $"(the smallest seed in 1-{AiOpeningSeeds.MaxReportSeed} whose Normal pick is this opening)";
        string fall = r.DefeatedAt < 0 ? "the idle base still stands at 20:00" : $"the idle base falls at {r.DefeatedAt / Tps} s";
        var marks = new List<string>();
        for (int m = 0; m < r.Marks; m++)
            marks.Add(m == 0 ? $"in hand at 5 min {r.Gold[m]} gold / {r.Wood[m]} wood" : $"at {5 * (m + 1)} min {r.Gold[m]} / {r.Wood[m]}");
        string hand = marks.Count == 0 ? "no 5-minute mark before it" : string.Join(", ", marks);
        string wave = r.FirstWaveAt < 0 ? "no wave started" : $"the first wave at {r.FirstWaveAt / Tps} s";
        string age = r.AgeTwoAt < 0 ? "no Age II in 20 minutes" : $"Age II in at {r.AgeTwoAt / Tps} s";
        return $"{(r.Difficulty == AiDifficulty.Easy ? "Easy" : "Normal")}, seed {r.Seed} {why}: {fall}; {hand}; {wave}; {age}";
    }

    /// <summary>A run as docs/03's bullet lists it: "malazan standard Normal seed 2 512 s".</summary>
    private static string Short(SeatRun r) =>
        $"{r.Faction} {r.OpeningId} {(r.Difficulty == AiDifficulty.Easy ? "Easy" : "Normal")} seed {r.Seed} {(r.DefeatedAt < 0 ? "stands" : $"{r.DefeatedAt / Tps} s")}";

    /// <summary>
    /// The paragraph (or bullet) whose first line starts with <paramref name="start"/>, up to a blank line, the next bullet or
    /// a heading, its lines trimmed and joined with spaces, bold removed.
    /// </summary>
    private static string BoldParagraph(string[] lines, string start, string where)
    {
        int i = Array.FindIndex(lines, l => l.StartsWith(start, StringComparison.Ordinal));
        Assert.True(i >= 0, $"{where} has no line starting '{start}'");
        var text = new List<string> { lines[i].Trim() };
        for (i++; i < lines.Length && lines[i].Trim().Length > 0 && !lines[i].StartsWith("- ", StringComparison.Ordinal) && !lines[i].StartsWith('#'); i++)
            text.Add(lines[i].Trim());
        return string.Join(" ", text).Replace("**", "");
    }

    /// <summary>
    /// One AI seat's run on the menu map: the opening it played, when the idle seat fell, gold and wood at every mark, the
    /// first wave's tick and its mark counts.
    /// </summary>
    internal sealed record SeatRun(string Faction, int Opening, string OpeningId, AiDifficulty Difficulty, ulong Seed, int DefeatedAt,
        int[] Gold, int[] Wood, int FirstWaveAt, int AgeTwoAt, int WoodBound, int Marks, int AllMarks);

    /// <summary>The menu-map runs, the printed lines, and the conclusion over the marks before each defeat.</summary>
    internal sealed record GeneratedRun(SeatRun[] Seats, List<string> Lines, int WoodBound, int Marks, string Verdict)
    {
        public SeatRun Seat(string faction, int opening, AiDifficulty difficulty) =>
            Seats.Single(s => s.Faction == faction && s.Opening == opening && s.Difficulty == difficulty);
    }

    /// <summary>Run once per test process: the report and its conformance row read the same deterministic runs.</summary>
    private static readonly Lazy<GeneratedRun> Generated = new(RunGenerated);

    private static GeneratedRun RunGenerated()
    {
        var lines = new List<string>();
        var seats = new List<SeatRun>();
        int bound = 0, marks = 0, cut = 0;
        foreach (string faction in new[] { "malazan", "whirlwind" })
        {
            AiDef def = TestSim.Data.Ai[TestSim.Data.FindFaction(faction)];
            var runs = Enumerable.Range(0, def.Openings.Length).Select(k => (k, AiDifficulty.Normal)).Append((0, AiDifficulty.Easy));
            foreach ((int k, AiDifficulty difficulty) in runs)
            {
                ulong seed = AiOpeningSeeds.Smallest(faction, k, difficulty);
                int player = TestSim.Data.FindFaction(faction);
                Simulation sim = MenuMap(AiScenes.Seats(difficulty, Controllers(player)), seed);
                Assert.Equal(MenuMines, Count(sim, "gold_mine"));
                Assert.Equal(k, sim.AiOpening(player)); // the pick this run measures
                lines.Add("");
                lines.Add($"== {faction} opening {k + 1} {AiOpeningSeeds.Name(def.Openings[k])}, {difficulty}, generated 128 map seed {seed} ({MenuForests} forests, {MenuMines} mines), {Ticks} ticks, combat on, the other seat idle");
                ReportResult r = Report(sim, player, sim.World.StartLocations[player].Cell, lines.Add);
                var seat = new SeatRun(faction, k, def.Openings[k].Id, difficulty, seed, r.IdleDefeatedAt, r.Gold, r.Wood, r.FirstWaveAt, r.AgeTwoAt, r.WoodBound, r.Marks, r.AllMarks);
                lines.Add("page: " + Sentence(seat));
                seats.Add(seat);
                bound += r.WoodBound;
                marks += r.Marks;
                cut += r.AllMarks - r.Marks;
            }
        }
        lines.Add("");
        string verdict = 2 * bound >= marks ? "wood is still the limit" : bound > 0 ? "wood binds at some marks only, not the limit overall" : "wood is not the limit";
        lines.Add($"Conclusion (2026-10-18, shipped data: D21's openings): {verdict} on the menu's generated map: {bound} of {marks} marks before the idle seat's defeat had wood in hand < {WoodShort} with gold >= {GoldPiled} ({cut} marks after a defeat not counted).");
        lines.Add("docs/03: " + string.Join(", ", seats.Select(Short)));
        return new GeneratedRun(seats.ToArray(), lines, bound, marks, verdict);
    }

    /// <summary>
    /// <see cref="AiScenes.Generated"/>'s match (the hall and five workers per player from <c>StartBase.Plan</c>, by command)
    /// on the menu's map: <see cref="MenuForests"/> forests and <see cref="MenuMines"/> mines.
    /// </summary>
    internal static Simulation MenuMap(PlayerSeat[] seats, ulong seed, GameData? data = null)
    {
        var map = MapGenParams.Default with { Forests = MenuForests, GoldMines = MenuMines };
        var sim = new Simulation(new SimConfig(seed, seats.Length, 400, 1024) { Data = data ?? TestSim.Data, Map = map, Seats = seats.ToImmutableArray() });
        World w = sim.World;
        NavGrid g = w.NavGrid;
        float maxRadius = 0f;
        foreach (UnitDef d in w.Data.Units) maxRadius = Math.Max(maxRadius, d.Radius);
        var blocks = new Vector2[seats.Length][];
        var factions = new int[seats.Length];
        for (int p = 0; p < seats.Length; p++) (blocks[p], factions[p]) = (Array.Empty<Vector2>(), w.FactionOf(p));
        StartBasePlan plan = StartBase.Plan(g, w.Data, w.Buildings, w.Resources.Alive, w.Resources.TypeId, w.Resources.Cell, factions, blocks, 5, maxRadius);
        for (int p = 0; p < seats.Length; p++)
        {
            int anchor = plan.HallAnchor[p];
            Assert.True(anchor >= 0, $"seed {seed}: no hall spot for player {p}");
            sim.Enqueue(Command.SpawnBuilding(p, plan.HallType[p], g.CellCenter(anchor % g.Width, anchor / g.Width)));
            foreach (Vector2 spot in plan.Workers[p]) sim.Enqueue(Command.SpawnUnit(p, plan.WorkerType[p], spot));
        }
        return sim;
    }

    private static int Count(Simulation sim, string key)
    {
        int type = sim.World.Data.FindResource(key), n = 0;
        var r = sim.World.Resources;
        for (int k = 0; k < r.Capacity; k++) if (r.Alive[k] && r.TypeId[k] == type) n++;
        return n;
    }

    internal static SeatController[] Controllers(int aiPlayer)
    {
        SeatController[] controllers = { SeatController.Human, SeatController.Human };
        controllers[aiPlayer] = SeatController.Ai;
        return controllers;
    }

    /// <summary>
    /// Runs <see cref="Ticks"/> and prints the report for <paramref name="player"/>'s AI; with a start cell, the last column
    /// is the wood left in trees within 60 m of it (else on the whole map). Returns the wood-bound marks and the marks before
    /// the idle seat's defeat, all the marks, the defeat tick (-1: it stood), and the gold and wood in hand at the first mark.
    /// </summary>
    /// <summary>What <see cref="Report"/> measured: the wood-bound marks and the marks before the idle seat's defeat, all the marks, the defeat tick (-1: it stood), gold and wood at each mark, the first wave's tick and the tick Age II was in (-1: none).</summary>
    private sealed record ReportResult(int WoodBound, int Marks, int AllMarks, int IdleDefeatedAt, int[] Gold, int[] Wood, int FirstWaveAt, int AgeTwoAt);

    private static ReportResult Report(Simulation sim, int player, int startCell, Action<string> write)
    {
        World w = sim.World;
        GameData d = w.Data;
        // Preconditions, not findings: the AI seat plays this faction, or every row reports the other faction's AI.
        AiPlayer? ai = sim.AiOf(player);
        Assert.NotNull(ai);
        Assert.Equal(player, w.FactionOf(player));
        Assert.Equal(player, ai!.Faction);

        var producesAt = new bool[d.Buildings.Length];
        foreach (UnitDef u in d.Units)
            if (u.Faction == player && u.TrainedAtTypeId >= 0) producesAt[u.TrainedAtTypeId] = true;

        var b = w.Buildings;
        var finishedTicks = new int[b.Capacity];
        var emptyTicks = new int[b.Capacity];
        var firstFinished = new int[b.Capacity];
        var typeAt = new int[b.Capacity];
        var generationAt = new int[b.Capacity];
        Array.Fill(firstFinished, -1);
        Array.Fill(typeAt, -1);
        var marks = new List<string>();
        int woodBound = 0, counted = 0, idleDefeatedAt = -1, firstWaveAt = -1, ageTwoAt = -1;
        var golds = new List<int>();
        var woods = new List<int>();

        for (int t = 0; t < Ticks; t++)
        {
            sim.Tick();
            if (idleDefeatedAt < 0 && w.IsDefeated(1 - player)) idleDefeatedAt = sim.TickNumber;
            if (firstWaveAt < 0 && ai.Board.WavesStarted > 0) firstWaveAt = sim.TickNumber;
            if (ageTwoAt < 0 && w.Age(player) >= 2) ageTwoAt = sim.TickNumber;
            for (int s = 0; s < b.Capacity; s++)
            {
                if (!b.Alive[s] || b.Owner[s] != player || !producesAt[b.TypeId[s]] || b.UnderConstruction[s]) continue;
                // A reused slot is another building: the report keeps the first one per slot (the "slot reused" line below
                // says if one was).
                if (typeAt[s] < 0) (typeAt[s], generationAt[s], firstFinished[s]) = (b.TypeId[s], b.Generation[s], sim.TickNumber);
                if (b.Generation[s] != generationAt[s]) continue;
                finishedTicks[s]++;
                if (b.QueueCount[s] == 0) emptyTicks[s]++;
            }
            if (sim.TickNumber % MarkEvery == 0)
            {
                (int army, int workers) = Pop(sim, player);
                golds.Add(w.Gold[player]);
                woods.Add(w.Wood[player]);
                bool live = idleDefeatedAt < 0;
                if (live) counted++;
                if (live && w.Wood[player] < WoodShort && w.Gold[player] >= GoldPiled) woodBound++;
                marks.Add($"{sim.TickNumber / Tps / 60} min{(live ? "" : " (after the defeat: not counted)")} | {w.Gold[player]} | {w.Wood[player]} | {army / 2.0} | {workers / 2.0} | {w.HalfPop[player] / 2.0} / {w.HalfPopCap[player] / 2.0} | {ai.Board.WavesStarted} | {w.Age(player)} | {StepsDone(ai)} / {ai.Opening.BuildOrder.Length} | {Left(sim, "gold_mine", -1)} | {Left(sim, "tree", startCell)}");
            }
        }
        Assert.Equal(Ticks, sim.TickNumber);

        write($"opening {AiOpeningSeeds.Name(ai.Opening)}; waves started {ai.Board.WavesStarted} (the first at {(firstWaveAt < 0 ? "never" : $"{firstWaveAt / Tps} s")}); the idle seat defeated: {(idleDefeatedAt < 0 ? "no" : $"at {idleDefeatedAt / Tps} s")}");
        write("");
        write($"time | gold | wood | army pop | worker pop | pop / cap | waves started | age | order steps done | gold in mines | wood in trees{(startCell >= 0 ? " within 60 m of the start" : "")}");
        foreach (string row in marks) write(row);
        write("");
        write("building | slot | finished at (s) | finished ticks | empty-queue share");
        int reused = 0;
        for (int s = 0; s < b.Capacity; s++)
        {
            if (typeAt[s] < 0) continue;
            if (b.Alive[s] && b.Generation[s] != generationAt[s]) reused++;
            string share = (100.0 * emptyTicks[s] / Math.Max(1, finishedTicks[s])).ToString("0.0", CultureInfo.InvariantCulture);
            write($"{d.Buildings[typeAt[s]].Key} | {s} | {firstFinished[s] / Tps} | {finishedTicks[s]} | {share} %");
        }
        write($"slot reused by another building: {reused}");
        var open = new List<string>();
        for (int i = 0; i < ai.Opening.BuildOrder.Length; i++)
            if (!ai.Board.StepDone[i]) open.Add($"{i + 1} {AiContentTests.StepText(ai.Opening.BuildOrder[i]).Split(" | ")[0]}");
        write("order steps not done at the end: " + (open.Count == 0 ? "none" : string.Join("; ", open)));
        return new ReportResult(woodBound, counted, marks.Count, idleDefeatedAt, golds.ToArray(), woods.ToArray(), firstWaveAt, ageTwoAt);
    }

    private static int StepsDone(AiPlayer ai)
    {
        int n = 0;
        foreach (bool done in ai.Board.StepDone) if (done) n++;
        return n;
    }

    /// <summary>The player's live army and worker half-pop now (the units store, not the AI's last-think census).</summary>
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

    /// <summary>What is left in every node of the resource <paramref name="key"/> on the map, or within 60 m of <paramref name="aroundCell"/> when it is a cell.</summary>
    private static int Left(Simulation sim, string key, int aroundCell)
    {
        int type = sim.World.Data.FindResource(key);
        var r = sim.World.Resources;
        NavGrid g = sim.World.NavGrid;
        Vector2 at = aroundCell >= 0 ? g.CellCenter(aroundCell % g.Width, aroundCell / g.Width) : default;
        int left = 0;
        for (int k = 0; k < r.Capacity; k++)
        {
            if (!r.Alive[k] || r.TypeId[k] != type) continue;
            if (aroundCell >= 0 && Vector2.Distance(at, g.CellCenter(r.Cell[k] % g.Width, r.Cell[k] / g.Width)) > 60f) continue;
            left += r.Remaining[k];
        }
        return left;
    }
}
