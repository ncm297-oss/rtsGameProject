using System.Collections.Immutable;
using System.Numerics;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.Pathfinding;
using Rts.Sim.Tests.Content;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// M6-S6 criterion 5: does the AI spend a <c>high</c> start instead of banking it? Every Malazan x Whirlwind opening
/// pairing (9, so each of the six openings plays three times) on its smallest seed (the pairs report's,
/// <see cref="AiOpeningSeeds.SmallestPair"/>), Normal v Normal on the menu's map (12 forests, 8 mines) for 10 minutes, once
/// at <c>standard</c> and once at <c>high</c>. Asserted per side: the brief's measure, the bank (gold + wood) at 10:00 at
/// <c>high</c> no more than twice the <c>standard</c> run's; and (BUG-1050, the measure that reads the start rather than one
/// sample of a fight's ebb) the mean bank from 2:00 to 10:00 (every 5 s) at <c>high</c> no more than
/// <see cref="MeanBankRatio"/> times <c>standard</c>'s. Before the spend-down rule (fix round 1) five sides failed the first
/// and eleven the second (Malazan <c>cadre_rush</c> held 1,325-1,515 at 4:00). Printed for the report: both runs' 2-minute
/// timeline (banks, army and worker pop, waves, producers busy, pop and cap, build order steps done and due), and per side the
/// 10:00 banks, the mean bank and the bank at 4:00. Lines starting "ROW" are machine-read by the QA report.
/// </summary>
public class StartingResourcesAiStressTests
{
    private const int Tps = SimConstants.TicksPerSecond;
    private const int Ticks = 10 * 60 * Tps;
    private readonly ITestOutputHelper _out;

    public StartingResourcesAiStressTests(ITestOutputHelper output) => _out = output;

    public static TheoryData<int, int> Openings()
    {
        var rows = new TheoryData<int, int>();
        for (int m = 0; m < 3; m++) for (int w = 0; w < 3; w++) rows.Add(m, w);
        return rows;
    }

    [Theory]
    [Trait("Category", "Stress")]
    [MemberData(nameof(Openings))]
    public void EachOpening_AtHigh_SpendsItsStart_BankAtTenMinutesAtMostTwiceStandards(int opening, int other)
    {
        ulong seed = AiOpeningSeeds.SmallestPair(opening, other);
        Run standard = Play(seed, "standard", opening, other);
        Run high = Play(seed, "high", opening, other);
        _out.WriteLine($"malazan {standard.Names[0]} v whirlwind {standard.Names[1]}, menu map seed {seed}, Normal v Normal, {Ticks} ticks");
        foreach (Run r in new[] { standard, high })
        {
            _out.WriteLine($"  {r.Preset}: time | malazan | whirlwind");
            foreach (string row in r.Timeline) _out.WriteLine(row);
        }
        var over = new List<string>();
        for (int p = 0; p < 2; p++)
        {
            string side = p == 0 ? "malazan" : "whirlwind";
            int s = standard.Bank[p], h = high.Bank[p];
            _out.WriteLine($"ROW {side} {standard.Names[p]} seed {seed}: bank at 10:00 standard {s} high {h} (x{(s == 0 ? 0 : (double)h / s):0.00}); "
                + $"waves {standard.Waves[p]} / {high.Waves[p]}; Age II {Sec(standard.AgeTwo[p])} / {Sec(high.AgeTwo[p])}; "
                + $"army pop at 10:00 {standard.Army[p] / 2.0} / {high.Army[p] / 2.0}; mean bank 2:00-10:00 {standard.Mean[p]} / {high.Mean[p]}; bank at 4:00 {standard.At4[p]} / {high.At4[p]}");
            if (h > 2 * s) over.Add($"{side} {standard.Names[p]} seed {seed}: high banks {h} at 10:00, over twice standard's {s}");
            if (high.Mean[p] > MeanBankRatio * standard.Mean[p])
                over.Add($"{side} {standard.Names[p]} seed {seed}: high's mean bank 2:00-10:00 {high.Mean[p]}, over {MeanBankRatio} x standard's {standard.Mean[p]}");
        }
        Assert.True(over.Count == 0, string.Join("\n", over));
    }

    /// <summary>
    /// BUG-1050: the most the mean bank 2:00-10:00 at <c>high</c> may be, as a multiple of <c>standard</c>'s. With the
    /// spend-down rule the 18 sides are at 0.44-1.26 (before it 0.42-3.17, eleven over 1.5: the start held for minutes).
    /// </summary>
    internal const double MeanBankRatio = 1.5;

    private sealed record Run(string Preset, string[] Names, int[] Bank, int[] Army, int[] Waves, int[] AgeTwo, List<string> Timeline, int[] Mean, int[] At4);

    private static Run Play(ulong seed, string preset, int opening, int other)
    {
        Simulation sim = MenuMap(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Ai), seed, preset);
        Assert.Equal((opening, other), (sim.AiOpening(0), sim.AiOpening(1)));
        World w = sim.World;
        var ageTwo = new[] { -1, -1 };
        var sum = new long[2];
        var at4 = new int[2];
        int samples = 0;
        var timeline = new List<string>();
        for (int t = 0; t < Ticks; t++)
        {
            sim.Tick();
            for (int p = 0; p < 2; p++)
                if (ageTwo[p] < 0 && w.Age(p) >= 2) ageTwo[p] = sim.TickNumber;
            if (sim.TickNumber > 2 * 60 * Tps && sim.TickNumber % (5 * Tps) == 0)
            {
                samples++;
                for (int p = 0; p < 2; p++) sum[p] += w.Gold[p] + w.Wood[p];
            }
            if (sim.TickNumber == 4 * 60 * Tps)
                for (int p = 0; p < 2; p++) at4[p] = w.Gold[p] + w.Wood[p];
            if (sim.TickNumber % (2 * 60 * Tps) == 0)
            {
                var row = new List<string>();
                for (int p = 0; p < 2; p++)
                {
                    (int army, int workers) = Pop(sim, p);
                    (int busy, int halls) = Producers(sim, p);
                    AiPlayer ai = sim.AiOf(p)!;
                    int due = 0, done = 0;
                    for (int i = 0; i < ai.Opening.BuildOrder.Length; i++)
                    {
                        if (ai.Board.StepDone[i]) done++;
                        else if (ai.Board.StepDueTick[i] >= 0) due++;
                    }
                    row.Add($"gold {w.Gold[p]} wood {w.Wood[p]} army {army / 2.0} workers {workers / 2.0} waves {ai.Board.WavesStarted} producers busy {busy}/{halls} pop {w.HalfPop[p] / 2.0}/{w.HalfPopCap[p] / 2.0} steps done {done} due {due}/{ai.Opening.BuildOrder.Length}");
                }
                timeline.Add($"    {Sec(sim.TickNumber)} | {string.Join(" | ", row)}");
            }
        }
        var names = new string[2];
        var bank = new int[2];
        var armyPop = new int[2];
        var waves = new int[2];
        for (int p = 0; p < 2; p++)
        {
            names[p] = AiOpeningSeeds.Name(sim.AiOf(p)!.Opening);
            bank[p] = w.Gold[p] + w.Wood[p];
            armyPop[p] = Pop(sim, p).Army;
            waves[p] = sim.AiOf(p)!.Board.WavesStarted;
        }
        return new Run(preset, names, bank, armyPop, waves, ageTwo, timeline, new[] { (int)(sum[0] / samples), (int)(sum[1] / samples) }, at4);
    }

    /// <summary>
    /// The pairs report's menu map (<see cref="AiSpendingReportTests.MenuMap"/>: 12 forests, 8 mines, each seat's hall and
    /// workers on the start-base plan) at a starting-resources preset.
    /// </summary>
    private static Simulation MenuMap(PlayerSeat[] seats, ulong seed, string preset)
    {
        // The pairs report's own map settings (its scene builds the same way at the default preset).
        MapGenParams map = AiSpendingReportTests.MenuMap(seats, seed).World.Config.Map;
        var sim = new Simulation(new SimConfig(seed, seats.Length, 400, 1024)
            { Data = TestSim.Data, Map = map, Seats = seats.ToImmutableArray(), StartingResources = preset });
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

    /// <summary>The player's finished buildings that train units, and how many of them have a queue.</summary>
    private static (int Busy, int Total) Producers(Simulation sim, int player)
    {
        var b = sim.World.Buildings;
        int busy = 0, total = 0;
        for (int s = 0; s < b.Capacity; s++)
        {
            if (!b.Alive[s] || b.Owner[s] != player || b.UnderConstruction[s] || sim.World.Data.UnitsTrainedAt(b.TypeId[s]).IsEmpty) continue;
            total++;
            if (b.QueueCount[s] > 0) busy++;
        }
        return (busy, total);
    }

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
