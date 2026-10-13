using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Replays;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M5-3 criterion 9: the headless AI-vs-AI proof. Malazan against Whirlwind, both Normal, a generated 128 map (the match's
/// 12 forests), 6,000 ticks (5 minutes), seeds 1-3: no exception; each seat's scout finishes its sweep and each gathers wood;
/// with the test's own attack threshold (<see cref="AiScenes.EarlyWaves"/>: one unit, rule 12) every seat that fields an army
/// and knows an enemy building sends AttackMoves, and in every seed one does; every recording plays back, every seat human, to
/// equal checkpoints. Measured, not gated (see the output): with the shipped thresholds (20 / 14 pop) neither seat reaches a
/// wave in 5 minutes; the first wood comes in late where the nearest forest is out of the scout's 40 m ring.
/// </summary>
public class AiVersusAiTests
{
    private readonly ITestOutputHelper _out;

    public AiVersusAiTests(ITestOutputHelper output) => _out = output;

    private static readonly Lazy<GameData> s_early = new(() => AiScenes.EarlyWaves(1));

    /// <summary>Runs the match 6,000 ticks recorded; per player the first tick wood came in (-1: never) and the largest army.</summary>
    private (Simulation Sim, Replay Replay, int[] FirstWood, int[] Army) Run(ulong seed, GameData data)
    {
        PlayerSeat[] seats = AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Ai);
        Simulation sim = AiScenes.Generated(seats, seed, record: true, out ReplayRecorder? rec, data: data, forests: 12);
        int[] firstWood = { -1, -1 }, army = new int[2], last = { sim.World.Wood[0], sim.World.Wood[1] };
        while (sim.TickNumber < 6000)
        {
            sim.Tick();
            for (int p = 0; p < 2; p++)
            {
                if (firstWood[p] < 0 && sim.World.Wood[p] > last[p]) firstWood[p] = sim.TickNumber;
                last[p] = sim.World.Wood[p];
                army[p] = Math.Max(army[p], sim.AiOf(p)!.ArmyCount);
            }
        }
        Replay r = rec!.ToReplay();
        for (int p = 0; p < 2; p++)
        {
            AiPlayer ai = sim.AiOf(p)!;
            _out.WriteLine($"seed {seed} player {p}: first wood at {firstWood[p]}, most army {army[p]}, waves {ai.Board.WavesStarted}, " +
                $"AttackMoves {r.Commands.Count(c => c.Player == p && c.Kind == CommandKind.AttackMove)}, pop {sim.World.HalfPop[p] / 2}, workers {ai.WorkerCount}, " +
                $"buildings {ai.OwnBuildingCount}, known enemy buildings {ai.Board.KnownEnemyBuildings}, scout phase {ai.Board.ScoutPhase}");
        }
        ReplayResult played = ReplayPlayer.Run(r, data);
        Assert.True(played.Ok, played.ToString());
        Assert.Equal(6000, played.TicksRun);
        return (sim, r, firstWood, army);
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void MalazanVersusWhirlwind_Normal_6000Ticks_ScoutAndGatherWood(ulong seed)
    {
        (Simulation sim, _, int[] firstWood, _) = Run(seed, TestSim.Data);
        for (int p = 0; p < 2; p++)
        {
            Assert.True(firstWood[p] > 0, $"seed {seed}: player {p} gathered no wood");
            Assert.Equal(2, sim.AiOf(p)!.Board.ScoutPhase);
        }
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void MalazanVersusWhirlwind_Normal_6000Ticks_TestThresholds_BothAttack(ulong seed)
    {
        (Simulation sim, Replay r, _, int[] army) = Run(seed, s_early.Value);
        int attackers = 0;
        for (int p = 0; p < 2; p++)
        {
            bool sent = r.Commands.Any(c => c.Player == p && c.Kind == CommandKind.AttackMove);
            if (army[p] > 0 && sim.AiOf(p)!.Board.KnownEnemyBuildings > 0) Assert.True(sent, $"seed {seed}: player {p} had an army and a target but sent no AttackMove");
            if (sent) attackers++;
        }
        Assert.True(attackers >= 1, $"seed {seed}: nobody attacked");
    }
}
