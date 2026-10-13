using System.Diagnostics;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// D15 report (D14's flag): the shipped Malazan order places 12 Billets by <c>pop</c> steps, and the economy manager's house
/// rule also places one whenever the cap is within <c>houseWhenPopWithin</c> of the population. On the flat AI scene
/// (<see cref="AiScenes.Flat"/>: a Garrison Keep, five Laborers, two mines and a forest in sight; player 1 a human that does
/// nothing), Normal, 12,000 ticks (10 min), this prints who placed each house, when it finished, the cap over time, and
/// whether the order's later house steps still complete once the cap is reached (and what they cost). Only that the run
/// finishes is asserted (plus one documented precondition on the seat's faction): the numbers are a report for the owner
/// (the proposal is in the D15 session report).
/// <para>
/// Measured 2026-10-12 on M5-2's AI (nothing checks these; the live numbers are in each session report that re-runs this):
/// every house both orders placed came from a step and the rule sent none; on this scene wood runs out (12 trees of 100 a
/// base), so the Malazan cap stalled at 98 after 30 min and the 12th Billet (step 18) stayed due, at no cost (a due house
/// step that can't pay is refused at the budget before the site search).
/// </para>
/// <para>
/// Reads through the AI's internals: <c>Blackboard.StepDueTick</c>, <c>Blackboard.StepDone</c>, <c>Blackboard.StepPending</c>
/// and <c>Blackboard.HousePending</c> (no public-to-tests surface carries a step's state or a sent house). Everything else
/// comes from <see cref="GameData"/> or the world. A sim rename of those four breaks this file at compile time: fix the reads.
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
        SeatController[] controllers = { SeatController.Human, SeatController.Human };
        controllers[player] = SeatController.Ai;
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, controllers));
        GameData d = sim.World.Data;
        // Precondition, not a finding: the seat mapping above must give the AI seat this faction, or every row below
        // reports the other faction's order under this one's name.
        Assert.Equal(player, sim.World.FactionOf(player));
        Blackboard board = sim.AiOf(player)!.Board; // reads StepDueTick, StepDone, StepPending, HousePending
        AiDef def = d.Ai[player];
        int thinkTicks = Math.Max(1, def.Difficulty(AiDifficulty.Normal).ThinkTicks); // as AiPlayer derives it
        int house = d.Buildings.ToList().FindIndex(b => b.Faction == player && b.Slot == BuildingSlot.House);
        BuildingDef houseDef = d.Buildings[house];
        int rulesCap = d.Rules.HalfPopCap;
        var steps = def.BuildOrder;
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

        _out.WriteLine($"{faction}, flat scene, Normal, {ticks} ticks; {houseDef.Key} gives {houseDef.HalfPopProvided / 2.0} pop for {houseDef.CostGold} gold + {houseDef.CostWood} wood, rules cap {rulesCap / 2}");
        _out.WriteLine($"house Builds sent: order steps {sends.Count(x => x.Step > 0)}, economy rule {sends.Count(x => x.Step == 0)}");
        _out.WriteLine($"houses standing: {placedBy.Count} (placed by order steps {byOrder}, by the rule {byRule}, unknown {placedBy.Count(x => x.Step < 0)}); finished {placedBy.Count(x => x.Finished)}");
        _out.WriteLine($"cap reached {rulesCap / 2} at tick {capReachedTick} ({(capReachedTick < 0 ? "never" : $"{capReachedTick / Tps} s")}); pop provided at the end {provided / 2.0} (cap {sim.World.HalfPopCap[player] / 2.0}), pop {sim.World.HalfPop[player] / 2.0}");
        int wasted = Math.Max(0, provided - rulesCap) / Math.Max(1, houseDef.HalfPopProvided);
        _out.WriteLine($"houses past the cap (provided beyond {rulesCap / 2}): {wasted}, {wasted * houseDef.CostWood} wood + {wasted * houseDef.CostGold} gold");
        _out.WriteLine("");
        _out.WriteLine("step | at | due tick | done tick | cap at done | thinks due | thinks due at the cap");
        foreach (int i in houseSteps)
        {
            int done = stepDoneTick[i];
            string capAt = done < 0 ? "-" : (stepDoneCap[i] / 2.0).ToString(System.Globalization.CultureInfo.InvariantCulture);
            _out.WriteLine($"{i + 1} | {AiContentTestsText(steps[i])} | {board.StepDueTick[i]} | {(done < 0 ? "never" : done.ToString())} | {capAt} | {stepDueThinks[i]} | {stepDueAtCapThinks[i]}");
        }
        _out.WriteLine("");
        _out.WriteLine("time | pop | cap | houses finished | house sites | gold | wood | workers | wood in trees | gold in mines");
        foreach (string row in samples) _out.WriteLine(row);
        _out.WriteLine("");
        _out.WriteLine("house Builds in order (step 0 = economy rule): " + string.Join(", ", sends.Select(x => $"{x.Step}@{x.Tick}")));
        _out.WriteLine($"think-tick cost (Debug, wall clock, noisy): with a house step due {Mean(thinkMsDue):0.000} ms over {thinkMsDue.Count}; none due {Mean(thinkMsNone):0.000} ms over {thinkMsNone.Count}");
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
