using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Combat;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA, M6-S14 (session 2026-11-26-1847, QA focus 2 and 5): what an enemy at the door costs a seat for 20 minutes (24,000
/// ticks) under the session's two worker rules. (1) The re-scout cap (item D) on every difficulty, in QA's blind scene, both
/// the seat blind to both kinds (idle workers only) and the seat that knows its gold mines and never saw a tree (the new
/// gatherer path, BUG-1554): workers lost, workers trained, gold spent; asserted: blind re-scouts lost equal
/// <c>ai.scoutReplacements</c>, workers alive at the end. (2) The worker floor (item B) with one worker killed every think:
/// workers trained and gold spent, printed (is the drain bounded by anything but gold?).
/// </summary>
public class M6S14DrainQaStressTests
{
    private readonly ITestOutputHelper _out;

    public M6S14DrainQaStressTests(ITestOutputHelper output) => _out = output;

    private static int Workers(World w, int player = 0)
    {
        int n = 0;
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == player && w.Data.Units[w.Units.TypeId[i]].IsWorker) n++;
        return n;
    }

    [Theory]
    [Trait("Category", "Stress")]
    [InlineData(AiDifficulty.Easy, false)]
    [InlineData(AiDifficulty.Normal, false)]
    [InlineData(AiDifficulty.Hard, false)]
    [InlineData(AiDifficulty.Easy, true)]
    [InlineData(AiDifficulty.Normal, true)]
    [InlineData(AiDifficulty.Hard, true)]
    public void CampedDoor_EveryScoutKilled_20Minutes_EveryDifficulty(AiDifficulty difficulty, bool knowsGold)
    {
        Simulation sim = M6S12AiRulesQaTests.BlindScene(keepMines: knowsGold, seats: AiScenes.Seats(difficulty, SeatController.Ai, SeatController.Human));
        World w = sim.World;
        AiPlayer ai = sim.AiOf(0)!;
        int gold0 = w.Gold[0], trained0 = w.UnitsTrained[0], losses0 = w.UnitLosses[0], workers0 = Workers(w);
        int rescouts = 0, killed = 0, fromGatherer = 0;
        byte phase = ai.Board.ScoutPhase;
        while (sim.TickNumber < 24_000 && !sim.MatchOver)
        {
            sim.Tick();
            Blackboard b = ai.Board;
            if (phase == 2 && b.ScoutPhase == 1)
            {
                rescouts++;
                if (b.ScoutSlot >= 0 && (b.GatherKind[b.ScoutSlot] & (EconomyManager.Gold | EconomyManager.Wood)) != 0) fromGatherer++;
            }
            phase = b.ScoutPhase;
            if (b.ScoutPhase == 1 && b.ScoutSlot >= 0 && w.Units.IsAlive(new EntityHandle(b.ScoutSlot, b.ScoutGeneration)))
            {
                CombatSystem.HitUnit(w, new PendingHit(default, 1, new EntityHandle(b.ScoutSlot, b.ScoutGeneration), false, w.Units.Hp[b.ScoutSlot]));
                killed++;
            }
        }
        _out.WriteLine($"QADRAIN door {difficulty} knowsGold {knowsGold}: blind re-scouts sent {rescouts} (from a gatherer {fromGatherer}), killed {killed}, counted {ai.Board.BlindScoutsLost} (cap {ai.Rules.ScoutReplacements}); "
            + $"own units lost {w.UnitLosses[0] - losses0}, trained {w.UnitsTrained[0] - trained0}; workers {workers0} -> {Workers(w)}; gold {gold0} -> {w.Gold[0]} (spent at least {gold0 - w.Gold[0]})");
        Assert.Equal(ai.Rules.ScoutReplacements, ai.Board.BlindScoutsLost);
        Assert.Equal(ai.Rules.ScoutReplacements, killed);
        Assert.True(Workers(w) > 0, "no worker left at 20 minutes");
    }

    /// <summary>A Malazan AI that only ever trains through the floor: one worker step at time 0.</summary>
    private static readonly Lazy<GameData> s_quiet = new(() => AiScenes.DataWith(dir =>
        dir.EditJson("factions/malazan/ai.json", root =>
        {
            foreach (JsonNode? o in root["openings"]!.AsArray())
                AiScenes.SetBuildOrder(o, AiScenes.BuildOrder(("time", 0, "train", "malazan_laborer", 1)));
        })));

    /// <summary>
    /// The floor's drain: a Malazan Normal seat with 5,000 gold, one worker of its killed every think for 20 minutes (the
    /// lowest slot's, an enemy that never stops). Printed: worker Trains, workers killed, gold spent, and the rate against the
    /// Keep's training time; asserted only that the floor kept trains going (it is a report).
    /// </summary>
    [Theory]
    [Trait("Category", "Stress")]
    [InlineData(false)]
    [InlineData(true)]
    public void Floor_AWorkerKilledEveryThink_20Minutes_Report(bool shipped)
    {
        PlayerSeat[] seats = AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human);
        Simulation sim = AiScenes.Flat(seats, shipped ? TestSim.Data : s_quiet.Value);
        World w = sim.World;
        AiPlayer ai = sim.AiOf(0)!;
        BuildMaps.SetTotals(sim, 0, 5000, 1000);
        int gold0 = w.Gold[0], trained0 = w.UnitsTrained[0], killed = 0, firstBroke = -1;
        int laborer = w.Data.FindUnit("malazan_laborer");
        while (sim.TickNumber < 24_000)
        {
            sim.Tick();
            if ((sim.TickNumber - 1) % ai.ThinkTicks != 0) continue;
            for (int i = 0; i < w.Units.Capacity; i++)
            {
                if (!w.Units.Alive[i] || w.Units.Owner[i] != 0 || !w.Data.Units[w.Units.TypeId[i]].IsWorker) continue;
                CombatSystem.HitUnit(w, new PendingHit(default, 1, new EntityHandle(i, w.Units.Generation[i]), false, w.Units.Hp[i]));
                killed++;
                break;
            }
            if (firstBroke < 0 && w.Gold[0] < w.Data.Units[laborer].CostGold) firstBroke = sim.TickNumber;
        }
        int trained = w.UnitsTrained[0] - trained0;
        int most = 24_000 / w.Data.Units[laborer].TrainTicks;
        _out.WriteLine($"QADRAIN floor {(shipped ? "shipped" : "floor-only")}: workers killed {killed}, units trained {trained} (one Keep's most in 20 min: {most}), "
            + $"gold {gold0} -> {w.Gold[0]} (below a Laborer's cost from {firstBroke}); workers at the end {Workers(w)}");
        Assert.True(trained > 0);
    }
}
