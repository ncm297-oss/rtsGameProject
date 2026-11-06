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

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M6-S6 (2026-11-05-1206): the AI at the <c>high</c> start, per opening rather than per pairing. Each of the six
/// openings on the three smallest seeds (1-50) its seat picks it on, Normal v Normal on the menu map, 10 minutes at
/// <c>standard</c> and at <c>high</c>. Printed per side ("QAROW"): the bank at 5:00 and 10:00, the mean bank 2:00-10:00, the
/// build steps done by 5:00, Age II, waves, defeat, and the final state hash (for the cross-process twin check). Asserted:
/// no opening is idle at <c>high</c> (its build steps done by 5:00 at least <c>standard</c>'s), at most one wave fewer
/// than at <c>standard</c>, no side defeated at <c>high</c> that stands at <c>standard</c>. The brief's measure (the bank
/// at 10:00 at most twice <c>standard</c>'s) is printed ("OVER2X"), not asserted: BUG-1050 decides it.
/// </summary>
public class StartingResourcesAiQaTests
{
    private const int Tps = SimConstants.TicksPerSecond;
    private const int Ticks = 10 * 60 * Tps;
    private readonly ITestOutputHelper _out;

    public StartingResourcesAiQaTests(ITestOutputHelper output) => _out = output;

    public static TheoryData<int, int> Openings()
    {
        var rows = new TheoryData<int, int>();
        for (int player = 0; player < 2; player++) for (int o = 0; o < 3; o++) rows.Add(player, o);
        return rows;
    }

    private static List<ulong> SeedsFor(int player, int opening)
    {
        var seeds = new List<ulong>();
        for (ulong seed = 1; seed <= AiOpeningSeeds.MaxReportSeed && seeds.Count < 3; seed++)
            if (AiOpeningSeeds.Pick(seed, player, AiDifficulty.Normal) == opening) seeds.Add(seed);
        Assert.Equal(3, seeds.Count);
        return seeds;
    }

    [Theory]
    [Trait("Category", "Stress")]
    [MemberData(nameof(Openings))]
    public void EachOpening_ThreeSeeds_AtHigh_IsNotIdle_AndNoWorseThanStandard(int player, int opening)
    {
        var problems = new List<string>();
        foreach (ulong seed in SeedsFor(player, opening))
        {
            Run s = Play(seed, "standard");
            Run h = Play(seed, "high");
            Assert.Equal(opening, h.Opening[player]);
            int p = player;
            string side = p == 0 ? "malazan" : "whirlwind";
            bool over = h.Bank10[p] > 2 * s.Bank10[p];
            _out.WriteLine($"QAROW {side} {h.Names[p]} seed {seed}: bank 5:00 {s.Bank5[p]} / {h.Bank5[p]}; 10:00 {s.Bank10[p]} / {h.Bank10[p]}{(over ? " OVER2X" : "")}; "
                + $"mean 2-10 {s.Mean[p]} / {h.Mean[p]}; steps by 5:00 {s.Steps5[p]} / {h.Steps5[p]}; Age II {Sec(s.AgeTwo[p])} / {Sec(h.AgeTwo[p])}; "
                + $"waves {s.Waves[p]} / {h.Waves[p]}; defeated {s.Defeated[p]} / {h.Defeated[p]}; opponent waves {s.Waves[1 - p]} / {h.Waves[1 - p]}; hash high {h.Hash:X16}");
            if (h.Steps5[p] < s.Steps5[p]) problems.Add($"{side} {h.Names[p]} seed {seed}: {h.Steps5[p]} steps done by 5:00 at high, {s.Steps5[p]} at standard");
            // Age II is printed, not asserted: at high a Whirlwind side may spend on waves instead (seeds 1, 2: Age II at
            // 9:20 / 9:47 at standard, not by 10:00 at high, with as many or more waves).
            if (h.Waves[p] < s.Waves[p] - 1) problems.Add($"{side} {h.Names[p]} seed {seed}: {h.Waves[p]} waves at high, {s.Waves[p]} at standard");
            if (h.Defeated[p] && !s.Defeated[p]) problems.Add($"{side} {h.Names[p]} seed {seed}: defeated at high, standing at standard");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    internal sealed record Run(int[] Opening, string[] Names, int[] Bank5, int[] Bank10, int[] Mean, int[] Steps5, int[] AgeTwo, int[] Waves, bool[] Defeated, ulong Hash);

    internal static Run Play(ulong seed, string preset)
    {
        Simulation sim = MenuMap(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Ai), seed, preset);
        World w = sim.World;
        var ageTwo = new[] { -1, -1 };
        var bank5 = new int[2];
        var steps5 = new int[2];
        var sum = new long[2];
        int samples = 0;
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
            if (sim.TickNumber == 5 * 60 * Tps)
                for (int p = 0; p < 2; p++)
                {
                    bank5[p] = w.Gold[p] + w.Wood[p];
                    AiPlayer ai = sim.AiOf(p)!;
                    for (int i = 0; i < ai.Opening.BuildOrder.Length; i++) if (ai.Board.StepDone[i]) steps5[p]++;
                }
        }
        var names = new string[2];
        var opening = new int[2];
        var bank10 = new int[2];
        var waves = new int[2];
        var defeated = new bool[2];
        for (int p = 0; p < 2; p++)
        {
            names[p] = AiOpeningSeeds.Name(sim.AiOf(p)!.Opening);
            opening[p] = sim.AiOpening(p);
            bank10[p] = w.Gold[p] + w.Wood[p];
            waves[p] = sim.AiOf(p)!.Board.WavesStarted;
            defeated[p] = w.IsDefeated(p);
        }
        return new Run(opening, names, bank5, bank10, new[] { (int)(sum[0] / samples), (int)(sum[1] / samples) }, steps5, ageTwo, waves, defeated, sim.StateHash());
    }

    /// <summary>The pairs report's menu map at a preset (as <c>Stress/StartingResourcesAiStressTests</c> builds it).</summary>
    internal static Simulation MenuMap(PlayerSeat[] seats, ulong seed, string preset)
    {
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

    internal static string Sec(int tick) => tick < 0 ? "never" : $"{tick / Tps / 60}:{tick / Tps % 60:00}";
}
