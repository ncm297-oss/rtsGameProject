using System.Numerics;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Replays;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M5-3 criterion 9: the headless AI-vs-AI proof. Malazan against Whirlwind, both Normal, a generated 128 map (the match's
/// 12 forests), 6,000 ticks (5 minutes), seeds 1-3: no exception; each seat's scout finishes its sweep knowing an enemy
/// building and each gathers wood by tick 3,000 (M5-4a: the bases stand on the map's mirrored start locations, each with a
/// forest in reach); with the test's own attack threshold (<see cref="AiScenes.EarlyWaves"/>: one unit, rule 12) each seat waves
/// (M5-4b: or, when the other's wave reaches it first, defends); every recording plays back, every seat human, to equal checkpoints. Measured, not gated (see the output): with
/// the shipped thresholds (20 / 14 pop) neither seat reaches a wave in 5 minutes; since M5-4a the first wood comes in at
/// 468-902 (before, without start locations, the Malazan seat's came at 3,220 / 3,823 on seeds 1 / 2, BUG-0592).
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
            // M5-4a: wood by tick 3,000 on both seats (measured: 479-902 on seeds 1-3).
            Assert.True(firstWood[p] > 0 && firstWood[p] <= 3000, $"seed {seed}: player {p}'s first wood came in at {firstWood[p]}");
            Assert.Equal(2, sim.AiOf(p)!.Board.ScoutPhase);
            Assert.True(sim.AiOf(p)!.Board.KnownEnemyBuildings > 0, $"seed {seed}: player {p} knows no enemy building");
        }
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void MalazanVersusWhirlwind_Normal_6000Ticks_TestThresholds_BothAttack(ulong seed)
    {
        (Simulation sim, Replay r, _, int[] army) = Run(seed, s_early.Value);
        for (int p = 0; p < 2; p++)
        {
            AiPlayer ai = sim.AiOf(p)!;
            Assert.True(army[p] > 0, $"seed {seed}: player {p} fielded no army");
            Assert.True(ai.Board.KnownEnemyBuildings > 0, $"seed {seed}: player {p} knows no enemy building");
            // M5-4b: a seat whose base the other's wave reaches before its own first wave defends instead (docs/02: defense
            // first; no wave starts while the threat holds): measured on seed 2, player 0 under attack from tick 4,641.
            Assert.True(ai.Board.WavesStarted > 0 || ai.Board.Defenses > 0, $"seed {seed}: player {p} neither waved nor defended");
            if (ai.Board.WavesStarted > 0)
                Assert.True(r.Commands.Any(c => c.Player == p && c.Kind == CommandKind.AttackMove), $"seed {seed}: player {p} sent no AttackMove");
        }
        Assert.True(sim.AiOf(0)!.Board.WavesStarted + sim.AiOf(1)!.Board.WavesStarted > 0, $"seed {seed}: nobody attacked");
    }

    /// <summary>
    /// M5-4b criterion 3: with the early-wave data, four of player 0's Heavy Infantry spawned in player 1's base (so player 0
    /// knows the hall from the start) and player 1's hall pre-damaged to 1 hp with nothing to spend (so nothing else of it
    /// stands yet), player 0's first wave razes the hall: the match reaches <see cref="Simulation.MatchOver"/> and player 1's
    /// AI sends nothing after its defeat (none of its commands in the log after it). The hp and totals are test seams a replay
    /// can't carry, so this run isn't played back (MatchOverTests plays a defeat back past MatchOver). The 20-minute "one side
    /// wins" test is M5-5's. M5-4e: the razed hall alone no longer ends player 1 (its workers keep it in, and the AI doesn't
    /// hunt them before M5-4d), so once its hall is down and nothing of it trains, the scene takes its units away (a seam).
    /// </summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void AHallAt1Hp_IsRazedByTheOtherSeatsWave_MatchOver_AndTheLoserGoesSilent(ulong seed)
    {
        PlayerSeat[] seats = AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Ai);
        Simulation sim = AiScenes.Generated(seats, seed, record: true, out ReplayRecorder? rec, data: s_early.Value, forests: 12);
        World w = sim.World;
        sim.Tick();
        sim.Tick(); // the bases' commands apply
        int heavy = w.Data.FindUnit("malazan_heavy_infantry"), spawned = 0;
        for (int i = 0; i < w.Units.Capacity && spawned < 4; i++)
        {
            if (!w.Units.Alive[i] || w.Units.Owner[i] != 1) continue;
            sim.Enqueue(Command.SpawnUnit(0, heavy, w.Units.Position[i] + new Vector2(0.4f, 0.5f))); // beside one of its workers
            spawned++;
        }
        for (int k = 0; k < w.Buildings.Capacity; k++)
            if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == 1) w.Buildings.SetHp(k, 1);
        BuildMaps.SetTotals(sim, 1, 0, 0);
        int defeatedAt = -1, sentAtDefeat = 0;
        int hallDownAt = -1;
        while (sim.TickNumber < 6000 && (defeatedAt < 0 || sim.TickNumber < defeatedAt + 2000))
        {
            if (hallDownAt < 0 && w.ProductionBuildings(1) == 0)
            {
                hallDownAt = sim.TickNumber;
                for (int i = 0; i < w.Units.Capacity; i++)
                    if (w.Units.Alive[i] && w.Units.Owner[i] == 1) w.Units.Free(new EntityHandle(i, w.Units.Generation[i]));
            }
            sim.Tick();
            if (defeatedAt < 0 && w.IsDefeated(1))
            {
                defeatedAt = sim.TickNumber;
                sentAtDefeat = sim.AiOf(1)!.CommandsSent;
            }
        }
        _out.WriteLine($"seed {seed}: player 1's hall down at tick {hallDownAt}, defeated at tick {defeatedAt}; player 0 waves {sim.AiOf(0)!.Board.WavesStarted}; player 1 commands {sentAtDefeat}");
        Assert.True(defeatedAt > 0, $"seed {seed}: player 1 was never defeated");
        Assert.True(sim.AiOf(0)!.Board.WavesStarted > 0, $"seed {seed}: player 0 sent no wave");
        Assert.True(sim.MatchOver);
        Assert.False(w.IsDefeated(0));
        Assert.True(sentAtDefeat > 0);
        Assert.Equal(sentAtDefeat, sim.AiOf(1)!.CommandsSent);
        Assert.DoesNotContain(rec!.ToReplay().Commands, c => c.Player == 1 && c.Tick > defeatedAt);
    }
}
