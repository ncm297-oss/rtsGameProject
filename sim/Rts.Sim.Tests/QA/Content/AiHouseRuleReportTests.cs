using System.Diagnostics;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Rts.Sim.Tests.Content;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// D15 report (D14's flag): the shipped Malazan order places 12 Billets by <c>pop</c> steps, and the economy manager's house
/// rule also places one whenever the cap is within <c>houseWhenPopWithin</c> of the population. On the flat AI scene
/// (<see cref="AiScenes.Flat"/>: a Garrison Keep, five Laborers, two mines and a forest in sight; player 1 a human that does
/// nothing), Normal, 12,000 ticks (10 min), this prints who placed each house, when it finished, the cap over time, and
/// whether the order's later house steps still complete once the cap is reached (and what they cost). Only that the run
/// finishes is asserted (plus two documented preconditions: the seat's faction and the AI's house type): the numbers are a
/// report for the owner (the proposal is in the D15 session report).
/// <para>
/// Measured 2026-10-12 on M5-2's AI (nothing checks these; the live numbers are in each session report that re-runs this):
/// every house both orders placed came from a step and the rule sent none; on this scene wood runs out (12 trees of 100 a
/// base), so the Malazan cap stalled at 98 after 30 min and the 12th Billet (step 18) stayed due, at no cost (a due house
/// step that can't pay is refused at the budget before the site search).
/// </para>
/// <para>
/// Re-run 2026-10-17 (D20, after M5-4f's D15 C and gather rates 0.85 / 0.75): on the flat scene the cap still stalls below
/// 100 (Malazan 98 at 30 min, wood gone), so D15 C never applies there; the menu-map rows added then reach the cap, and
/// <c>D20PinQaTests.HouseNote_SaysWhatTheMenuMapRunsShow</c> pins the pages' "Houses at the cap" notes to them (and asserts
/// the D15 C behaviour itself: no house Build sent at the hard cap).
/// </para>
/// <para>
/// D21 (2026-10-18): the menu-map rows run per opening (the seat's pick, read from the AI: <c>AiPlayer.Opening</c>), on the
/// smallest seed in 1-50 whose Normal pick is that opening; the 12th-house row measures the standard opening without its
/// 12th house step on the same seed.
/// </para>
/// <para>
/// Reads through the AI's internals (all <c>internal</c>, reached through <c>InternalsVisibleTo</c>; no public-to-tests
/// surface carries a step's state or a sent house): <c>Simulation.AiOf</c>; <c>AiPlayer.Board</c>, <c>AiPlayer.ThinkTicks</c>
/// and <c>AiPlayer.HouseType</c> (read from the AI, not re-derived beside it, BUG-0641); <c>Blackboard.StepDueTick</c>,
/// <c>Blackboard.StepDone</c>, <c>Blackboard.StepPending</c> and <c>Blackboard.HousePending</c>; <c>PendingOrder.Kind</c>,
/// <c>PendingOrder.AppliesAt</c> and <c>PendingOrder.Cell</c>; <c>PendingKind.Build</c>. Everything else comes from
/// <see cref="GameData"/> or the world. A sim rename of any of those breaks this file at compile time: fix the reads.
/// </para>
/// <para>
/// The "workers" column is a live count of the player's worker units at the sample tick (the world's units store), not
/// <c>AiPlayer.WorkerCount</c>, which is the AI's census as of its last think; the two can differ by a tick's births and deaths.
/// </para>
/// </summary>
public class AiHouseRuleReportTests
{
    private const int Tps = SimConstants.TicksPerSecond;
    private const int SampleEvery = 60 * Tps;

    private readonly ITestOutputHelper _out;

    public AiHouseRuleReportTests(ITestOutputHelper output) => _out = output;

    /// <summary>A house Build the AI sent: who sent it (a step's 1-based number, or 0 for the economy manager), where, when.</summary>
    private sealed record Send(int Step, int Cell, int Tick);

    /// <summary>
    /// The brief's row is Malazan over 12,000 ticks; the 36,000-tick row (30 min) answers "do the later house steps complete
    /// past the cap", which 10 minutes on this scene doesn't reach; the Whirlwind row is the same question for the other order.
    /// </summary>
    [Theory]
    [InlineData("malazan", 12_000)]
    [InlineData("malazan", 36_000)]
    [InlineData("whirlwind", 12_000)]
    public void FlatNormal_HouseSources_Report(string faction, int ticks)
    {
        // Player p plays faction p mod 2 (AiScenes.Seats): the AI seat is the faction's, the other a human that does nothing.
        int player = TestSim.Data.FindFaction(faction);
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, AiSpendingReportTests.Controllers(player)));
        Run(sim, player, ticks, $"{faction}, flat scene", _out.WriteLine);
    }

    /// <summary>
    /// D20 (2026-10-17, M5-4f's D15 C), per opening since D21 (2026-10-18): the flat scene's 12 trees a base run dry before
    /// the cap is 100, so none of its rows reaches the rule "a house step due at the hard cap is done with no command sent".
    /// The menu's generated map (12 forests, 8 mines, combat on, the other seat idle: <see cref="AiSpendingReportTests.MenuMap"/>),
    /// 24,000 ticks, reaches it; this prints the same table plus, per house step, whether it was done at the hard cap without
    /// a Build. One row per opening, on Normal, on the smallest seed in 1-50 whose Normal pick is that opening
    /// (<see cref="AiOpeningSeeds.Smallest"/>; asserted on the run); plus <see cref="MenuMapNormal_TwelfthHouse_Report"/>.
    /// </summary>
    [Theory]
    [InlineData("malazan", 0)]
    [InlineData("malazan", 1)]
    [InlineData("malazan", 2)]
    [InlineData("whirlwind", 0)]
    [InlineData("whirlwind", 1)]
    [InlineData("whirlwind", 2)]
    public void MenuMapNormal_HouseSources_Report(string faction, int opening)
    {
        HouseRun r = MenuMapRun(faction, opening);
        foreach (string line in r.Lines) _out.WriteLine(line);
        _out.WriteLine("page: " + NoteSentence(r));
        Assert.True(r.Faction == faction && r.Opening == opening);
    }

    /// <summary>
    /// D21's measured decision on the parked D15 C finding (the standard opening's 12th house step, Malazan step 18
    /// <c>pop 81</c>, Whirlwind step 19 <c>pop 79</c>, is due at a cap of 90 or 98 and leaves 6 of its 8 pop unused): this
    /// row takes it out of a copy of the data (the same seed as the standard opening's row) and prints the same table, so
    /// the faction pages compare the two. Measured 2026-10-18: without it the cap reaches 100 later and the economy
    /// manager's house rule places the 12th house anyway, so the step stays (in every opening).
    /// </summary>
    [Theory]
    [InlineData("malazan")]
    [InlineData("whirlwind")]
    public void MenuMapNormal_TwelfthHouse_Report(string faction)
    {
        HouseRun r = TwelfthHouseRun(faction);
        foreach (string line in r.Lines) _out.WriteLine(line);
        _out.WriteLine("page: " + TwelfthSentence(r));
    }

    /// <summary>The standard opening's 12th house step: its index in the order and its pop trigger.</summary>
    internal static (int Index, double Pop) TwelfthHouse(string faction) => faction == "malazan" ? (17, 81) : (18, 79);

    /// <summary>The menu-map run for one faction's opening (deterministic; computed once per test process and shared with the pin row).</summary>
    internal static HouseRun MenuMapRun(string faction, int opening)
    {
        lock (MenuRuns)
        {
            if (MenuRuns.TryGetValue((faction, opening), out HouseRun? done)) return done;
            ulong seed = AiOpeningSeeds.Smallest(faction, opening, AiDifficulty.Normal);
            HouseRun r = MenuRun(faction, opening, seed, TestSim.Data, "");
            MenuRuns[(faction, opening)] = r;
            return r;
        }
    }

    /// <summary>The standard opening's run with its 12th house step taken out (a data copy; same seed).</summary>
    internal static HouseRun TwelfthHouseRun(string faction)
    {
        lock (MenuRuns)
        {
            if (MenuRuns.TryGetValue((faction, -1), out HouseRun? done)) return done;
            (int index, double pop) = TwelfthHouse(faction);
            string house = TestSim.Data.Buildings.First(b => b.Faction == TestSim.Data.FindFaction(faction) && b.Slot == BuildingSlot.House).Key;
            Assert.Equal($"pop {pop.ToString(System.Globalization.CultureInfo.InvariantCulture)} | build | {house} | 1", AiContentTests.StepText(TestSim.Data.Ai[TestSim.Data.FindFaction(faction)].Openings[0].BuildOrder[index]));
            GameData d = AiScenes.DataWithAi(faction, root => AiScenes.Opening(root)["buildOrder"]!.AsArray().RemoveAt(index));
            ulong seed = AiOpeningSeeds.Smallest(faction, 0, AiDifficulty.Normal);
            HouseRun r = MenuRun(faction, 0, seed, d, ", the 12th house step taken out");
            MenuRuns[(faction, -1)] = r;
            return r;
        }
    }

    private static HouseRun MenuRun(string faction, int opening, ulong seed, GameData data, string what)
    {
        int player = data.FindFaction(faction);
        PlayerSeat[] seats = AiScenes.Seats(AiDifficulty.Normal, AiSpendingReportTests.Controllers(player));
        Simulation sim = AiSpendingReportTests.MenuMap(seats, seed, data);
        Assert.Equal(opening, sim.AiOpening(player)); // the pick this run measures
        var lines = new List<string>();
        string name = AiOpeningSeeds.Name(data.Ai[player].Openings[opening]);
        HouseRun r = Run(sim, player, MenuTicks, $"{faction} opening {opening + 1} {name}{what}, menu map seed {seed} (12 forests, 8 mines, combat on)", lines.Add)
            with { Faction = faction, Opening = opening, Seed = seed, Lines = lines };
        return r;
    }

    private const int MenuTicks = 24_000;

    private static readonly Dictionary<(string, int), HouseRun> MenuRuns = new();

    /// <summary>
    /// One house step's life: its 1-based number, the Builds it sent, those sent while the cap was already the rules' cap
    /// (D15 C says none), when it was done (-1: never) and the cap then (half-pop).
    /// </summary>
    internal sealed record HouseStep(int Step, int Sends, int SendsAtCap, int DoneTick, int CapAtDone);

    /// <summary>
    /// What a run printed and measured: the house steps, the rules' cap (half-pop), the tick the cap reached it (-1: never),
    /// the cap at the end, the houses standing at the end and how many of them are past the cap (their pop unusable).
    /// </summary>
    internal sealed record HouseRun(string Faction, int Opening, ulong Seed, HouseStep[] Steps, int RulesCap, int CapReachedTick, int CapAtEnd,
        int HousesStanding, int HousesPastCap, string HouseName, List<string> Lines)
    {
        /// <summary>The steps done at the hard cap with no Build sent: the executor's D15 C skip.</summary>
        public HouseStep[] SkippedAtCap => Steps.Where(x => x.DoneTick >= 0 && x.Sends == 0 && x.CapAtDone >= RulesCap).ToArray();
    }

    /// <summary>
    /// A run as its opening's "Houses at the cap" note says it: "Normal, seed 2: the cap reaches 100 at 702 s; the last
    /// Billet step (step 17) is done at a cap of 90; skipped at the hard cap: none; 11 Billets stand at 20:00, 0 past the cap".
    /// </summary>
    internal static string NoteSentence(HouseRun r)
    {
        HouseStep last = r.Steps[^1];
        string cap = r.CapReachedTick < 0 ? $"the cap stays below {r.RulesCap / 2} ({FactionPage.Text(r.CapAtEnd / 2.0)} at 20:00)" : $"the cap reaches {r.RulesCap / 2} at {r.CapReachedTick / Tps} s";
        string lastText = last.DoneTick < 0 ? $"the last {r.HouseName} step (step {last.Step}) is never done" : $"the last {r.HouseName} step (step {last.Step}) is done at a cap of {FactionPage.Text(last.CapAtDone / 2.0)}";
        HouseStep[] skipped = r.SkippedAtCap;
        string skip = skipped.Length == 0 ? "none" : "step" + (skipped.Length > 1 ? "s " : " ") + string.Join(", ", skipped.Select(x => x.Step));
        return $"Normal, seed {r.Seed}: {cap}; {lastText}; skipped at the hard cap: {skip}; {r.HousesStanding} {r.HouseName}s stand at 20:00, {r.HousesPastCap} past the cap";
    }

    /// <summary>The 12th-house comparison as the standard opening's note says it (<paramref name="r"/> is <see cref="TwelfthHouseRun"/>).</summary>
    internal static string TwelfthSentence(HouseRun r)
    {
        string cap = r.CapReachedTick < 0 ? $"the cap stays below {r.RulesCap / 2}" : $"the cap reaches {r.RulesCap / 2} at {r.CapReachedTick / Tps} s";
        int step = TwelfthHouse(r.Faction).Index + 1;
        return $"without the 12th {r.HouseName} step (step {step}) on the same seed, {cap}, and {r.HousesStanding} {r.HouseName}s stand at 20:00, {r.HousesPastCap} past the cap";
    }
    private static HouseRun Run(Simulation sim, int player, int ticks, string label, Action<string> write)
    {
        GameData d = sim.World.Data;
        // Precondition, not a finding: the seat mapping above must give the AI seat this faction, or every row below
        // reports the other faction's order under this one's name.
        Assert.Equal(player, sim.World.FactionOf(player));
        AiPlayer ai = sim.AiOf(player)!;
        Blackboard board = ai.Board; // reads StepDueTick, StepDone, StepPending, HousePending
        // The AI's own think interval and house type (BUG-0641): a change to how AiPlayer derives either moves the samples
        // with it instead of silently sampling other ticks or another building.
        int thinkTicks = ai.ThinkTicks;
        int house = ai.HouseType;
        BuildingDef houseDef = d.Buildings[house];
        Assert.True(houseDef.Faction == player && houseDef.Slot == BuildingSlot.House, $"AiPlayer.HouseType {houseDef.Key} is not the House of {label}");
        int rulesCap = d.Rules.HalfPopCap;
        var steps = ai.Opening.BuildOrder; // D21: the opening the seat picked (read from the AI, BUG-0641)
        int tree = d.FindResource("tree"), mine = d.FindResource("gold_mine");
        var houseSteps = Enumerable.Range(0, steps.Length)
            .Where(i => steps[i].Action == AiStepAction.Build && steps[i].Target == house).ToArray();

        var sends = new List<Send>();
        var lastStepPending = new (int AppliesAt, int Cell)[steps.Length];
        (int AppliesAt, int Cell) lastHousePending = (-1, -1);
        var stepDoneTick = new int[steps.Length];
        Array.Fill(stepDoneTick, -1);
        var stepDoneCap = new int[steps.Length];
        var stepDueThinks = new int[steps.Length];       // thinks a house step was due and not done
        var stepDueAtCapThinks = new int[steps.Length];  // ... of which the cap was already the rules' cap
        var stepSends = new int[steps.Length];
        var stepSendsAtCap = new int[steps.Length];      // D15 C: a step's Build sent while the cap was the rules' cap
        int capReachedTick = -1;
        var samples = new List<string>();
        var thinkMsDue = new List<double>();
        var thinkMsNone = new List<double>();
        var clock = new Stopwatch();

        for (int t = 0; t < ticks; t++)
        {
            bool thinks = sim.TickNumber % thinkTicks == 0;
            bool anyDue = false;
            if (thinks)
                foreach (int i in houseSteps)
                    if (board.StepDueTick[i] >= 0 && !board.StepDone[i]) anyDue = true;
            clock.Restart();
            sim.Tick();
            clock.Stop();
            if (thinks) (anyDue ? thinkMsDue : thinkMsNone).Add(clock.Elapsed.TotalMilliseconds);

            int cap = sim.World.HalfPopCap[player];
            if (capReachedTick < 0 && cap >= rulesCap) capReachedTick = sim.TickNumber;
            foreach (int i in houseSteps)
            {
                PendingOrder p = board.StepPending[i];
                if (p.Kind == PendingKind.Build && (p.AppliesAt, p.Cell) != lastStepPending[i])
                {
                    lastStepPending[i] = (p.AppliesAt, p.Cell);
                    sends.Add(new Send(i + 1, p.Cell, p.AppliesAt));
                    stepSends[i]++;
                    if (cap >= rulesCap) stepSendsAtCap[i]++;
                }
                if (stepDoneTick[i] < 0 && board.StepDone[i]) (stepDoneTick[i], stepDoneCap[i]) = (sim.TickNumber, cap);
                if (thinks && board.StepDueTick[i] >= 0 && !board.StepDone[i])
                {
                    stepDueThinks[i]++;
                    if (cap >= rulesCap) stepDueAtCapThinks[i]++;
                }
            }
            PendingOrder h = board.HousePending;
            if (h.Kind == PendingKind.Build && (h.AppliesAt, h.Cell) != lastHousePending)
            {
                lastHousePending = (h.AppliesAt, h.Cell);
                sends.Add(new Send(0, h.Cell, h.AppliesAt));
            }
            if (sim.TickNumber % SampleEvery == 0)
            {
                (int finished, int sites) = Houses(sim, player, house);
                samples.Add($"{sim.TickNumber / Tps / 60}:{sim.TickNumber / Tps % 60:00} | {sim.World.HalfPop[player] / 2.0} | {cap / 2.0} | {finished} | {sites} | {sim.World.Gold[player]} | {sim.World.Wood[player]} | {Workers(sim, player)} | {Left(sim, tree)} | {Left(sim, mine)}");
            }
        }

        // Each standing house (site or finished) to the send that placed it, by anchor cell.
        var b = sim.World.Buildings;
        var placedBy = new List<(int Step, int Cell, bool Finished)>();
        for (int s = 0; s < b.Capacity; s++)
        {
            if (!b.Alive[s] || b.Owner[s] != player || b.TypeId[s] != house) continue;
            Send? who = sends.LastOrDefault(x => x.Cell == b.Cell[s]);
            placedBy.Add((who?.Step ?? -1, b.Cell[s], !b.UnderConstruction[s]));
        }
        int byOrder = placedBy.Count(x => x.Step > 0), byRule = placedBy.Count(x => x.Step == 0);
        int provided = 0;
        for (int s = 0; s < b.Capacity; s++)
            if (b.Alive[s] && b.Owner[s] == player && !b.UnderConstruction[s]) provided += d.Buildings[b.TypeId[s]].HalfPopProvided;

        write($"{label}, Normal, {ticks} ticks, opening {AiOpeningSeeds.Name(ai.Opening)}; {houseDef.Key} gives {houseDef.HalfPopProvided / 2.0} pop for {houseDef.CostGold} gold + {houseDef.CostWood} wood, rules cap {rulesCap / 2}");
        write($"house Builds sent: order steps {sends.Count(x => x.Step > 0)}, economy rule {sends.Count(x => x.Step == 0)}");
        write($"houses standing: {placedBy.Count} (placed by order steps {byOrder}, by the rule {byRule}, unknown {placedBy.Count(x => x.Step < 0)}); finished {placedBy.Count(x => x.Finished)}");
        write($"cap reached {rulesCap / 2} at tick {capReachedTick} ({(capReachedTick < 0 ? "never" : $"{capReachedTick / Tps} s")}); pop provided at the end {provided / 2.0} (cap {sim.World.HalfPopCap[player] / 2.0}), pop {sim.World.HalfPop[player] / 2.0}");
        int wasted = Math.Max(0, provided - rulesCap) / Math.Max(1, houseDef.HalfPopProvided);
        write($"houses past the cap (provided beyond {rulesCap / 2}): {wasted}, {wasted * houseDef.CostWood} wood + {wasted * houseDef.CostGold} gold");
        write("");
        write("step | at | due tick | done tick | cap at done | thinks due | thinks due at the cap | Builds sent | sent at the cap");
        var result = new List<HouseStep>();
        foreach (int i in houseSteps)
        {
            int done = stepDoneTick[i];
            string capAt = done < 0 ? "-" : (stepDoneCap[i] / 2.0).ToString(System.Globalization.CultureInfo.InvariantCulture);
            write($"{i + 1} | {AiContentTestsText(steps[i])} | {board.StepDueTick[i]} | {(done < 0 ? "never" : done.ToString())} | {capAt} | {stepDueThinks[i]} | {stepDueAtCapThinks[i]} | {stepSends[i]} | {stepSendsAtCap[i]}");
            result.Add(new HouseStep(i + 1, stepSends[i], stepSendsAtCap[i], done, done < 0 ? -1 : stepDoneCap[i]));
        }
        HouseRun run = new("", ai.OpeningIndex, 0, result.ToArray(), rulesCap, capReachedTick, sim.World.HalfPopCap[player], placedBy.Count, wasted, houseDef.DisplayName, new List<string>());
        HouseStep[] skipped = run.SkippedAtCap;
        write($"D15 C: house steps done at the hard cap {rulesCap / 2} with no Build sent: {(skipped.Length == 0 ? "none" : string.Join(", ", skipped.Select(x => x.Step)))}; house Builds sent at the hard cap: {result.Sum(x => x.SendsAtCap)}");
        write("");
        write("time | pop | cap | houses finished | house sites | gold | wood | workers | wood in trees | gold in mines");
        foreach (string row in samples) write(row);
        write("");
        write("house Builds in order (step 0 = economy rule): " + string.Join(", ", sends.Select(x => $"{x.Step}@{x.Tick}")));
        write($"think-tick cost (Debug, wall clock, noisy): with a house step due {Mean(thinkMsDue):0.000} ms over {thinkMsDue.Count}; none due {Mean(thinkMsNone):0.000} ms over {thinkMsNone.Count}");
        return run;
    }

    private static string AiContentTestsText(AiStep s) => Rts.Sim.Tests.Content.AiContentTests.StepText(s).Split(" | ")[0];

    /// <summary>What is left in every node of <paramref name="type"/> on the map (the scene has 12 trees and 2 mines a base).</summary>
    private static int Left(Simulation sim, int type)
    {
        var r = sim.World.Resources;
        int left = 0;
        for (int k = 0; k < r.Capacity; k++)
            if (r.Alive[k] && r.TypeId[k] == type) left += r.Remaining[k];
        return left;
    }

    /// <summary>The player's live worker units now (not the AI's last-think census, <c>AiPlayer.WorkerCount</c>).</summary>
    private static int Workers(Simulation sim, int player)
    {
        var u = sim.World.Units;
        GameData d = sim.World.Data;
        int n = 0;
        for (int s = 0; s < u.Capacity; s++)
            if (u.Alive[s] && u.Owner[s] == player && d.Units[u.TypeId[s]].Slot == UnitSlot.Worker) n++;
        return n;
    }

    private static double Mean(List<double> xs) => xs.Count == 0 ? 0 : xs.Average();

    private static (int Finished, int Sites) Houses(Simulation sim, int player, int house)
    {
        var b = sim.World.Buildings;
        int finished = 0, sites = 0;
        for (int s = 0; s < b.Capacity; s++)
        {
            if (!b.Alive[s] || b.Owner[s] != player || b.TypeId[s] != house) continue;
            if (b.UnderConstruction[s]) sites++;
            else finished++;
        }
        return (finished, sites);
    }
}
