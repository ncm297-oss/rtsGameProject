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
/// Reads the AI through its own members (BUG-0641: read from the AI, not re-derived beside it; all <c>internal</c>, reached
/// through <c>InternalsVisibleTo</c>): <c>Simulation.AiOf</c>, <c>AiPlayer.Faction</c>, <c>AiPlayer.Def.BuildOrder</c>,
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
        (_, _, _, int defeatedAt, _, _) = Report(sim, player, -1, _out.WriteLine);
        Assert.Equal(-1, defeatedAt); // combat off: nobody falls, so every mark counts
    }

    /// <summary>D18 item 2: seeds 1 and 7 of the menu's generated map, both factions, and the dated conclusion (marks before the idle seat's defeat).</summary>
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
    }

    /// <summary>
    /// D20 (the report-conformance row): both faction pages' "Spending on the menu's map" paragraphs and docs/03's
    /// "Implementation (M5-4f)" spending bullet quote what <see cref="GeneratedNormal_Spending_Report"/> prints on the shipped
    /// data (the defeat times, the 5-minute gold / wood, the wood-bound count and the verdict) and carry the re-print's date
    /// and reason. A number the data or the sim moves fails here, by page, until the paragraphs are re-printed.
    /// </summary>
    [Fact]
    public void GeneratedNormal_PagesAndDocs03_QuoteTheReport()
    {
        GeneratedRun run = Generated.Value;
        string verdictLine = $"Across both factions {run.WoodBound} of those {run.Marks} marks had wood under {WoodShort} with gold at {GoldPiled} or more";
        string verdictQuote = $"the report now reads \"{run.Verdict}\"";
        foreach (string faction in new[] { "malazan", "whirlwind" })
        {
            SeatRun s1 = run.Seat(faction, 1), s7 = run.Seat(faction, 7);
            string page = BoldParagraph(PageTables.FactionLines(faction), "**Spending on the menu's map", faction + ".md");
            Assert.True(s1.DefeatedAt >= 0 && s7.DefeatedAt >= 0, $"{faction}: the idle seat stood; the page's 'razes the idle base' sentence no longer holds: re-word it");
            Assert.True(s1.Marks == 1 && s7.Marks == 1, $"{faction}: {s1.Marks} / {s7.Marks} marks before the defeat; the page's 'only the 5-minute mark' clause no longer holds: re-word it");
            string subject = faction == "malazan" ? "the Malazans hold" : "the Whirlwind holds";
            foreach (string want in new[]
            {
                "(D20, 2026-10-17, M5-4f",
                $"the AI razes the idle base at {s1.DefeatedAt / Tps} s (seed 1) and {s7.DefeatedAt / Tps} s (seed 7), so only the 5-minute mark is still a match",
                $"At 5 minutes {subject} {s1.Gold5} gold / {s1.Wood5} wood (seed 1) and {s7.Gold5} / {s7.Wood5} (seed 7)",
                verdictLine,
                verdictQuote,
            })
                Assert.True(page.Contains(want, StringComparison.Ordinal), $"{faction}.md 'Spending on the menu's map' lacks the report's \"{want}\"; it reads: {page}");
        }

        string docs03 = BoldParagraph(PageTables.Lines(Path.Combine("docs", "03-technical-design.md")), "- **Spending re-measured (D20, 2026-10-17", "docs/03");
        var wants = new List<string> { "M5-4f", verdictLine, verdictQuote };
        foreach (SeatRun s in run.Seats) wants.Add($"{s.Faction} seed {s.Seed} {s.DefeatedAt / Tps} s ({s.Gold5} gold / {s.Wood5} wood at 5 min)");
        foreach (string want in wants)
            Assert.True(docs03.Contains(want, StringComparison.Ordinal), $"docs/03's spending bullet lacks the report's \"{want}\"; it reads: {docs03}");
    }

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

    /// <summary>One AI seat's run on the menu map: when the idle seat fell, the 5-minute gold / wood, and its mark counts.</summary>
    private sealed record SeatRun(string Faction, ulong Seed, int DefeatedAt, int Gold5, int Wood5, int WoodBound, int Marks, int AllMarks);

    /// <summary>The four menu-map runs, the printed lines, and the conclusion over the marks before each defeat.</summary>
    private sealed record GeneratedRun(SeatRun[] Seats, List<string> Lines, int WoodBound, int Marks, string Verdict)
    {
        public SeatRun Seat(string faction, ulong seed) => Seats.Single(s => s.Faction == faction && s.Seed == seed);
    }

    /// <summary>Run once per test process: the report and its conformance row read the same four deterministic runs.</summary>
    private static readonly Lazy<GeneratedRun> Generated = new(RunGenerated);

    private static GeneratedRun RunGenerated()
    {
        var lines = new List<string>();
        var seats = new List<SeatRun>();
        int bound = 0, marks = 0, cut = 0;
        foreach (ulong seed in new ulong[] { 1, 7 })
            foreach (string faction in new[] { "malazan", "whirlwind" })
            {
                int player = TestSim.Data.FindFaction(faction);
                Simulation sim = MenuMap(AiScenes.Seats(AiDifficulty.Normal, Controllers(player)), seed);
                Assert.Equal(MenuMines, Count(sim, "gold_mine"));
                lines.Add("");
                lines.Add($"== {faction}, generated 128 map seed {seed} ({MenuForests} forests, {MenuMines} mines), Normal, {Ticks} ticks, combat on, the other seat idle");
                (int b, int m, int all, int defeatedAt, int gold5, int wood5) = Report(sim, player, sim.World.StartLocations[player].Cell, lines.Add);
                seats.Add(new SeatRun(faction, seed, defeatedAt, gold5, wood5, b, m, all));
                bound += b;
                marks += m;
                cut += all - m;
            }
        lines.Add("");
        string verdict = 2 * bound >= marks ? "wood is still the limit" : bound > 0 ? "wood binds at some marks only, not the limit overall" : "wood is not the limit";
        lines.Add($"Conclusion (2026-10-17, shipped data: M5-4f's gather rates and sized defense): {verdict} on the menu's generated map: {bound} of {marks} marks before the idle seat's defeat had wood in hand < {WoodShort} with gold >= {GoldPiled} ({cut} marks after a defeat not counted).");
        return new GeneratedRun(seats.ToArray(), lines, bound, marks, verdict);
    }

    /// <summary>
    /// <see cref="AiScenes.Generated"/>'s match (the hall and five workers per player from <c>StartBase.Plan</c>, by command)
    /// on the menu's map: <see cref="MenuForests"/> forests and <see cref="MenuMines"/> mines.
    /// </summary>
    internal static Simulation MenuMap(PlayerSeat[] seats, ulong seed)
    {
        var map = MapGenParams.Default with { Forests = MenuForests, GoldMines = MenuMines };
        var sim = new Simulation(new SimConfig(seed, seats.Length, 400, 1024) { Data = TestSim.Data, Map = map, Seats = seats.ToImmutableArray() });
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
    private static (int WoodBound, int Marks, int AllMarks, int IdleDefeatedAt, int Gold5, int Wood5) Report(Simulation sim, int player, int startCell, Action<string> write)
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
        int woodBound = 0, counted = 0, idleDefeatedAt = -1, gold5 = -1, wood5 = -1;

        for (int t = 0; t < Ticks; t++)
        {
            sim.Tick();
            if (idleDefeatedAt < 0 && w.IsDefeated(1 - player)) idleDefeatedAt = sim.TickNumber;
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
                if (gold5 < 0) (gold5, wood5) = (w.Gold[player], w.Wood[player]);
                bool live = idleDefeatedAt < 0;
                if (live) counted++;
                if (live && w.Wood[player] < WoodShort && w.Gold[player] >= GoldPiled) woodBound++;
                marks.Add($"{sim.TickNumber / Tps / 60} min{(live ? "" : " (after the defeat: not counted)")} | {w.Gold[player]} | {w.Wood[player]} | {army / 2.0} | {workers / 2.0} | {w.HalfPop[player] / 2.0} / {w.HalfPopCap[player] / 2.0} | {ai.Board.WavesStarted} | {w.Age(player)} | {StepsDone(ai)} / {ai.Def.BuildOrder.Length} | {Left(sim, "gold_mine", -1)} | {Left(sim, "tree", startCell)}");
            }
        }
        Assert.Equal(Ticks, sim.TickNumber);

        write($"waves started {ai.Board.WavesStarted}; the idle seat defeated: {(idleDefeatedAt < 0 ? "no" : $"at {idleDefeatedAt / Tps} s")}");
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
        for (int i = 0; i < ai.Def.BuildOrder.Length; i++)
            if (!ai.Board.StepDone[i]) open.Add($"{i + 1} {AiContentTests.StepText(ai.Def.BuildOrder[i]).Split(" | ")[0]}");
        write("order steps not done at the end: " + (open.Count == 0 ? "none" : string.Join("; ", open)));
        return (woodBound, counted, marks.Count, idleDefeatedAt, gold5, wood5);
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
