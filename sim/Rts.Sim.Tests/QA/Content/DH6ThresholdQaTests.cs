using Rts.Sim.Ai;
using Rts.Sim.Data;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA for D-H6 (session 2026-10-18-1236): BUG-0982's "keep 18" decision re-measured on two seeds the developer did not
/// pick (the 4th and 5th smallest in 1-500 making <c>cadre_rush</c> v <c>raider_rush</c>; the dev used 7, 8, 14), the
/// same thresholds (18 / 16 / 14, cost the floor of the Age I mix at that pop) and the same Producer rule extended to all
/// five seeds; Easy's opening read from the built simulation, not from the test-side pick helper; and the KnownBeatenAlive
/// pattern (<c>crossbow_push</c> v <c>raider_rush</c>) looked for on that pairing's 4th and 5th seeds (since M5-4g seed
/// 25 shows it too: a named allowance, BUG-1010).
/// </summary>
public class DH6ThresholdQaTests
{
    private const int Tps = SimConstants.TicksPerSecond;
    private const int Ticks = 24_000;
    private readonly ITestOutputHelper _out;

    public DH6ThresholdQaTests(ITestOutputHelper output) => _out = output;

    private static List<ulong> PairSeeds(int malazan, int whirlwind, int count)
    {
        var seeds = new List<ulong>();
        for (ulong s = 1; s <= 500 && seeds.Count < count; s++)
            if (Rts.Sim.Tests.Content.AiOpeningSeeds.Pick(s, 0, AiDifficulty.Normal) == malazan
                && Rts.Sim.Tests.Content.AiOpeningSeeds.Pick(s, 1, AiDifficulty.Normal) == whirlwind) seeds.Add(s);
        Assert.Equal(count, seeds.Count);
        return seeds;
    }

    /// <summary>
    /// The developer's three seeds plus the 4th and 5th: per threshold and seed, the Malazan first wave, Age II and defeat.
    /// Asserts the rule (lowest of 16 / 14 at which seed 8 waves and Age II on every seed is at most 60 s later than at 18,
    /// else 18) still picks the shipped number, and prints whether the new seeds alone would argue otherwise.
    /// </summary>
    [Fact]
    [Trait("Category", "Stress")]
    public void Bug0982_KeepEighteen_HoldsOnTwoMoreSeeds()
    {
        List<ulong> all = PairSeeds(2, 1, 5);
        Assert.Equal(new ulong[] { 7, 8, 14 }, all.Take(3));
        ulong[] seeds = all.ToArray();
        int[] pops = { 18, 16, 14 };
        int malazan = TestSim.Data.FindFaction("malazan");
        AiOpening shipped = TestSim.Data.Ai[malazan].Openings[2];
        Assert.Equal("cadre_rush", shipped.Id);
        // The description's number is the shipped wave pop (the brief's "description == attack.minArmyPop").
        Assert.Contains($"The first wave waits for {shipped.AttackMinHalfPop / 2} army pop.", shipped.Description);
        Assert.Equal(36, shipped.AttackMinHalfPop);
        Assert.Equal(1332, shipped.AttackMinCost);
        double perPop = 0, weight = 0;
        AiComposition one = shipped.Compositions[0];
        for (int k = 0; k < one.Units.Length; k++)
        {
            UnitDef u = TestSim.Data.Units[one.Units[k]];
            weight += one.Weights[k];
            perPop += one.Weights[k] * (u.CostGold + u.CostWood) / (u.HalfPop / 2.0);
        }
        perPop /= weight;
        Assert.Equal(1332, (int)Math.Floor(perPop * 18));
        var waved = new int[pops.Length, seeds.Length];
        var ageTwo = new int[pops.Length, seeds.Length];
        var fell = new int[pops.Length, seeds.Length];
        for (int i = 0; i < pops.Length; i++)
        {
            int pop = pops[i], cost = (int)Math.Floor(perPop * pop);
            GameData data = AiScenes.DataWithAi("malazan", root =>
                AiScenes.Opening(root, 2)["attack"] = new System.Text.Json.Nodes.JsonObject { ["minArmyPop"] = pop, ["minArmyCost"] = cost });
            for (int j = 0; j < seeds.Length; j++)
            {
                Simulation sim = Rts.Sim.Tests.Content.AiSpendingReportTests.MenuMap(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Ai), seeds[j], data);
                Assert.Equal((2, 1), (sim.AiOpening(0), sim.AiOpening(1)));
                (waved[i, j], ageTwo[i, j], fell[i, j]) = (-1, -1, -1);
                int peak = 0;
                for (int t = 0; t < Ticks; t++)
                {
                    sim.Tick();
                    if (t % 20 == 0) peak = Math.Max(peak, ArmyHalfPop(sim, 0));
                    if (waved[i, j] < 0 && sim.AiOf(0)!.Board.WavesStarted > 0) waved[i, j] = sim.TickNumber;
                    if (ageTwo[i, j] < 0 && sim.World.Age(0) >= 2) ageTwo[i, j] = sim.TickNumber;
                    if (fell[i, j] < 0 && sim.World.IsDefeated(0)) fell[i, j] = sim.TickNumber;
                }
                _out.WriteLine($"| {pop} / {cost} | {seeds[j]} | {Sec(waved[i, j])} ({sim.AiOf(0)!.Board.WavesStarted}) | {Sec(ageTwo[i, j])} | {Sec(fell[i, j])} | {peak / 2.0} | whirlwind waves {sim.AiOf(1)!.Board.WavesStarted}, defeated {Sec(sim.World.IsDefeated(1) ? 0 : -1)}");
            }
        }
        int pick = 18;
        for (int i = pops.Length - 1; i >= 1; i--)
        {
            bool seedEight = waved[i, 1] >= 0;
            bool inTime = true;
            for (int j = 0; j < seeds.Length; j++)
                if (ageTwo[0, j] >= 0 && (ageTwo[i, j] < 0 || ageTwo[i, j] > ageTwo[0, j] + 60 * Tps)) inTime = false;
            _out.WriteLine($"  {pops[i]} pop over 5 seeds: seed 8 waves {seedEight}; Age II within 60 s on every seed {inTime}");
            if (seedEight && inTime) { pick = pops[i]; break; }
        }
        // Would the new seeds alone change anything? (a lower threshold makes a seed wave that never waved at 18)
        for (int i = 1; i < pops.Length; i++)
            for (int j = 3; j < seeds.Length; j++)
                if (waved[0, j] < 0 && waved[i, j] >= 0)
                    _out.WriteLine($"  NOTE: seed {seeds[j]} sends no wave at 18 but does at {pops[i]}");
        _out.WriteLine($"  rule over 5 seeds picks {pick}; shipped {shipped.AttackMinHalfPop / 2}");
        Assert.Equal(pick * 2, shipped.AttackMinHalfPop);
    }

    /// <summary>Easy picks opening 0 on both seats for seeds 1-50, read from the constructed simulation (the AI's own pick), not the test-side mirror.</summary>
    [Fact]
    public void Easy_PicksOpeningZero_InTheBuiltSimulation_Seeds1To50()
    {
        for (ulong seed = 1; seed <= 50; seed++)
        {
            Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Easy, SeatController.Ai, SeatController.Ai), seed: seed);
            Assert.True(sim.AiOpening(0) == 0 && sim.AiOpening(1) == 0, $"seed {seed}: Easy picked {sim.AiOpening(0)} / {sim.AiOpening(1)}");
            for (int p = 0; p < 2; p++) Assert.Equal(sim.AiOpening(p), Rts.Sim.Tests.Content.AiOpeningSeeds.Pick(seed, p, AiDifficulty.Easy));
        }
    }

    /// <summary>
    /// The KnownBeatenAlive pattern (<c>crossbow_push</c> v <c>raider_rush</c>, seed 19 = the 3rd seed): the pairing's 4th
    /// and 5th seeds, printed (first wave, defeat, peak army, standing at 20:00). Fails only on the existing rule (no wave
    /// and not defeated), except for the seeds named in <see cref="KnownBeatenAlive"/>, so a further unlisted case shows here.
    /// </summary>
    [Fact]
    [Trait("Category", "Stress")]
    public void CrossbowPushVersusRaiderRush_FourthAndFifthSeeds()
    {
        List<ulong> seeds = PairSeeds(1, 1, 5);
        Assert.Equal(19UL, seeds[2]);
        Assert.Equal(25UL, seeds[3]);
        var bad = new List<string>();
        foreach (ulong seed in seeds.Skip(3))
        {
            Simulation sim = Rts.Sim.Tests.Content.AiSpendingReportTests.MenuMap(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Ai), seed);
            Assert.Equal((1, 1), (sim.AiOpening(0), sim.AiOpening(1)));
            int first = -1, fell = -1, peak = 0;
            for (int t = 0; t < Ticks; t++)
            {
                sim.Tick();
                if (first < 0 && sim.AiOf(0)!.Board.WavesStarted > 0) first = sim.TickNumber;
                if (fell < 0 && sim.World.IsDefeated(0)) fell = sim.TickNumber;
                if (t % 20 == 0) peak = Math.Max(peak, ArmyHalfPop(sim, 0));
            }
            _out.WriteLine($"seed {seed}: crossbow_push first wave {Sec(first)} ({sim.AiOf(0)!.Board.WavesStarted}), defeated {Sec(fell)}, peak {peak / 2.0}, at 20:00 pop {sim.World.HalfPop[0] / 2.0}; raider_rush waves {sim.AiOf(1)!.Board.WavesStarted}");
            bool known = Array.IndexOf(KnownBeatenAlive, seed) >= 0;
            if (first < 0 && fell < 0)
            {
                if (known) _out.WriteLine($"  KNOWN (BUG-1010): seed {seed} crossbow_push sent no wave and was beaten but not defeated; see KnownBeatenAlive");
                else bad.Add($"seed {seed}: crossbow_push no wave and not defeated");
            }
            else if (known) _out.WriteLine($"  the known case seed {seed} now passes the rule: take it out of KnownBeatenAlive (BUG-1010)");
        }
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }

    /// <summary>
    /// The pairing's seeds (here the 4th and 5th) where <c>crossbow_push</c> may send no wave and still stand at 20:00, named
    /// instead of loosening the rule (D-R32, 2026-10-26, BUG-1010; the 3rd seed, 19, is <c>D21OpeningsQaTests.KnownBeatenAlive</c>).
    /// Seed 25: after M5-4g's reserve the Malazan side sends no wave, its army peaks at 12 pop (under its 14-pop wave, so the
    /// reserve never engages), and at 20:00 it is down to 1 pop but not defeated (D-H6 read it defeated at 11:26).
    /// Take the entry out when the row passes the rule (the run prints so).
    /// </summary>
    private static readonly ulong[] KnownBeatenAlive = { 25 };

    private static int ArmyHalfPop(Simulation sim, int player)
    {
        var u = sim.World.Units;
        int army = 0;
        for (int s = 0; s < u.Capacity; s++)
            if (u.Alive[s] && u.Owner[s] == player && sim.World.Data.Units[u.TypeId[s]].Slot != UnitSlot.Worker) army += sim.World.Data.Units[u.TypeId[s]].HalfPop;
        return army;
    }

    private static string Sec(int tick) => tick < 0 ? "never" : $"{tick / Tps / 60}:{tick / Tps % 60:00}";
}
