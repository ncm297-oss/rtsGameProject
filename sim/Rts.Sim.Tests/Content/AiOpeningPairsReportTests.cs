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
/// is printed for the owner: the first wave, Age II, the defeat, the steps done.
/// <para>
/// Reads the AI through its internals (<c>InternalsVisibleTo</c>): <c>Simulation.AiOf</c>, <c>AiPlayer.Opening</c>,
/// <c>AiPlayer.HouseType</c>, <c>Blackboard.WavesStarted</c>, <c>StepDueTick</c>, <c>StepDone</c>. Everything else is world state.
/// </para>
/// </summary>
public class AiOpeningPairsReportTests
{
    private const int Tps = SimConstants.TicksPerSecond;
    private const int Ticks = 24_000;

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
            if (ai.Board.WavesStarted == 0) deadEnds.Add($"{side} ({ai.Opening.Id}) sent no wave in 20 minutes on seed {seed}");
        }
        Assert.True(deadEnds.Count == 0, string.Join("\n", deadEnds));
    }

    /// <summary>
    /// Why a step that is not done is legitimately waiting at the end, or null for a dead end (see the type's summary). The
    /// reasons, in the order tried: the side is out; a <c>pop</c> trigger never reached; a house step at the hard cap; a
    /// tech or building it requires not had yet (the step that gives it is itself waiting); its gold or wood not in hand
    /// now (the AI keeps no reserve for a due step: production spends first, a known sim-side limit, docs/03 "Implementation
    /// (M5-2)"); an <c>expand</c> with no known free mine (the AI's own view, as the executor asks it); a step whose
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
        if (w.Gold[p] < gold || w.Wood[p] < wood) return $"can't pay {gold} gold / {wood} wood (holds {w.Gold[p]} / {w.Wood[p]})";
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
