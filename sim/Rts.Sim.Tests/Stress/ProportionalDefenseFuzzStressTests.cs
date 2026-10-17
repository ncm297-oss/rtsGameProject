using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Replays;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA M5-4f (session 2026-10-16-2036): the proportional defense in 3- and 4-seat AI-v-AI matches on generated maps (each
/// player's line units raiding the next player's base, early waves, combat on, the defense knobs at shipped and edge
/// values), with a per-think oracle on every seat and a hash twin. The oracle, checked right after each think from the
/// state the think saw (the seats think at the start of the tick, before any command applies):
/// <list type="bullet">
/// <item>a defender's generation is set only for an own army unit that was free at the think (not casting, no AI command
/// pending, not the scout, not a member owed its Move home), and only while the base is defended (none left after);</item>
/// <item>while picking (not every unit answering): the picked worth stays below the largest answer of this threat plus half
/// the dearest defender's cost when two or more are picked, and no free home candidate is left unpicked while the picked
/// worth plus half its cost is short of the answer;</item>
/// <item>a defender is never a live member of the wave in progress (a pulled member's <c>WaveGeneration</c> is 0);</item>
/// <item><c>WaveStartHalfPop</c> is never below the live members' half-pop.</item>
/// </list>
/// </summary>
[Collection(SerialCollection.Name)]
public class ProportionalDefenseFuzzStressTests
{
    private readonly ITestOutputHelper _out;

    public ProportionalDefenseFuzzStressTests(ITestOutputHelper output) => _out = output;

    private static GameData Data(double ratio, double min, double recall) => AiScenes.DataWith(dir =>
    {
        foreach (string f in new[] { "malazan", "whirlwind" })
            dir.EditJson($"factions/{f}/ai.json", root => root["attack"] = new JsonObject { ["minArmyPop"] = 3, ["minArmyCost"] = 0 });
        dir.EditJson("common/rules.json", r =>
        {
            JsonNode ai = r["ai"]!;
            ai["defendRatio"] = ratio;
            ai["defendMinValue"] = min;
            ai["recallHallHp"] = recall;
            ai["waveCooldown"] = 10;
            ai["scoutAfter"] = 5;
        });
    });

    private sealed class Stats
    {
        internal int Thinks, DefendingThinks, Picks, Pulls, Recalls, PickingChecks, MaxDefenders;
    }

    /// <summary>One match; the per-think oracle on every seat; returns the per-checkpoint hashes and the command log.</summary>
    private static (List<ulong> Hashes, List<Command> Log) Run(GameData data, int seats, ulong seed, int ticks, bool oracle, Stats stats)
    {
        var controllers = Enumerable.Repeat(SeatController.Ai, seats).ToArray();
        PlayerSeat[] seat = AiScenes.Seats(AiDifficulty.Normal, controllers);
        if (seats == 4) seat[3] = seat[3] with { Difficulty = AiDifficulty.Hard };
        Simulation sim = AiScenes.Generated(seat, seed, record: true, out ReplayRecorder? rec, units: 900, commands: 4096, data: data, forests: 10, raiders: 5, casters: 1);
        for (int p = 0; p < seats; p++) BuildMaps.SetTotals(sim, p, 1500, 1500);
        World w = sim.World;
        UnitStore u = w.Units;
        var hashes = new List<ulong>();
        int cap = u.Capacity;
        var free = new bool[seats][];
        var member = new bool[seats][];
        var genAt = new int[seats][];
        var typeAt = new int[seats][];
        var prevDef = new int[seats][];
        var maxAnswer = new float[seats];
        for (int p = 0; p < seats; p++)
        {
            free[p] = new bool[cap];
            member[p] = new bool[cap];
            genAt[p] = new int[cap];
            typeAt[p] = new int[cap];
            prevDef[p] = new int[cap];
        }
        for (int t = 0; t < ticks; t++)
        {
            bool[] thinks = new bool[seats];
            if (oracle)
                for (int p = 0; p < seats; p++)
                {
                    AiPlayer ai = sim.AiOf(p)!;
                    if (w.IsDefeated(p) || sim.TickNumber % ai.ThinkTicks != 0) continue;
                    thinks[p] = true;
                    Blackboard b = ai.Board;
                    for (int i = 0; i < cap; i++)
                    {
                        prevDef[p][i] = b.DefenderGeneration[i];
                        free[p][i] = false;
                        member[p][i] = false;
                        genAt[p][i] = u.Alive[i] ? u.Generation[i] : -1;
                        typeAt[p][i] = u.Alive[i] ? u.TypeId[i] : -1;
                        if (!u.Alive[i] || u.Owner[i] != p || w.Data.Units[u.TypeId[i]].Slot == UnitSlot.Worker) continue;
                        int g = u.Generation[i];
                        bool pending = b.UnitGeneration[i] == g && b.UnitOrderedUntil[i] >= sim.TickNumber;
                        bool owedHome = b.WaveGeneration[i] == g && b.WaveFlag[i] == MilitaryManager.ToRally;
                        free[p][i] = u.State[i] != UnitState.Casting && !pending && !ai.IsScout(i, g) && !owedHome;
                        member[p][i] = b.WaveActive && b.WaveGeneration[i] == g && b.WaveFlag[i] != MilitaryManager.ToRally;
                    }
                }
            sim.Tick();
            if (t % 100 == 0) hashes.Add(sim.StateHash());
            if (!oracle) continue;
            for (int p = 0; p < seats; p++)
            {
                if (!thinks[p]) continue;
                AiPlayer ai = sim.AiOf(p)!;
                Blackboard b = ai.Board;
                stats.Thinks++;
                string at = $"seed {seed} seats {seats} tick {sim.TickNumber - 1} player {p}";
                if (!b.Defending)
                {
                    maxAnswer[p] = 0f;
                    for (int i = 0; i < cap; i++) Assert.True(b.DefenderGeneration[i] == 0, $"{at}: slot {i} still a defender after the threat");
                    continue;
                }
                stats.DefendingThinks++;
                maxAnswer[p] = Math.Max(maxAnswer[p], b.DefendAnswer);
                float picked = 0f, dearest = 0f;
                int defenders = 0;
                for (int i = 0; i < cap; i++)
                {
                    int dg = b.DefenderGeneration[i];
                    if (dg == 0) continue;
                    bool live = u.Alive[i] && u.Generation[i] == dg;
                    if (dg != prevDef[p][i])
                    {
                        stats.Picks++;
                        // Picked this think: it was a live own army unit, free when the think ran (it may have died since, in the tick).
                        Assert.True(free[p][i] && genAt[p][i] == dg, $"{at}: slot {i} picked but not a free army unit at the think (alive {u.Alive[i]}, owner {u.Owner[i]}, state {u.State[i]})");
                    }
                    // Valued as the think saw it (a defender may die in the tick, after the think): alive at the think with this generation.
                    if (genAt[p][i] != dg) continue;
                    int cost = w.Data.Units[typeAt[p][i]].CostGold + w.Data.Units[typeAt[p][i]].CostWood;
                    picked += cost;
                    dearest = Math.Max(dearest, cost);
                    defenders++;
                    if (live) Assert.False(b.WaveActive && b.WaveGeneration[i] == dg && b.WaveFlag[i] != MilitaryManager.ToRally, $"{at}: slot {i} is a defender and a wave member");
                }
                stats.MaxDefenders = Math.Max(stats.MaxDefenders, defenders);
                if (!b.DefendAll)
                {
                    stats.PickingChecks++;
                    if (defenders >= 2)
                        Assert.True(picked < maxAnswer[p] + dearest / 2f + 0.01f, $"{at}: picked {picked} with {defenders} defenders, the largest answer {maxAnswer[p]}, the dearest {dearest}");
                    // No free home candidate (not a member at the think, not picked) left while the picked are short of it by more than half its cost.
                    for (int i = 0; i < cap; i++)
                    {
                        // Free at the think, not a member then (the threshold may keep a member), not picked.
                        if (!free[p][i] || member[p][i] || b.DefenderGeneration[i] == genAt[p][i]) continue;
                        int cost = w.Data.Units[typeAt[p][i]].CostGold + w.Data.Units[typeAt[p][i]].CostWood;
                        Assert.True(picked > 0f && picked + cost * 0.5f >= b.DefendAnswer,
                            $"{at}: home unit {i} (cost {cost}) free and unpicked while picked {picked} is short of the answer {b.DefendAnswer}");
                    }
                }
                if (b.WaveActive)
                {
                    int live = 0;
                    for (int i = 0; i < cap; i++)
                        if (u.Alive[i] && u.Owner[i] == p && b.WaveGeneration[i] == u.Generation[i] && b.WaveFlag[i] != MilitaryManager.ToRally)
                            live += w.Data.Units[u.TypeId[i]].HalfPop;
                    Assert.True(b.WaveStartHalfPop >= live, $"{at}: WaveStartHalfPop {b.WaveStartHalfPop} below the live members' {live}");
                }
            }
        }
        for (int p = 0; p < seats; p++)
        {
            stats.Pulls += sim.AiOf(p)!.Board.WavePulls;
            stats.Recalls += sim.AiOf(p)!.Board.WaveRecalls;
        }
        var log = new List<Command>();
        for (int i = 0; i < rec!.CommandCount; i++) log.Add(rec.CommandAt(i));
        return (hashes, log);
    }

    public static IEnumerable<object[]> Cases()
    {
        // (seats, seed, ratio, min, recall): shipped, the low and high edges, mixed.
        yield return new object[] { 3, 1UL, 1.5, 150.0, 0.5 };
        yield return new object[] { 4, 2UL, 1.5, 150.0, 0.5 };
        yield return new object[] { 3, 3UL, 0.5, 0.0, 0.0 };
        yield return new object[] { 4, 4UL, 10.0, 5000.0, 1.0 };
        yield return new object[] { 4, 5UL, 0.5, 5000.0, 0.0 };
        yield return new object[] { 3, 6UL, 10.0, 0.0, 1.0 };
        yield return new object[] { 3, 7UL, 1.5, 150.0, 0.5 };
        yield return new object[] { 4, 8UL, 1.5, 150.0, 0.5 };
        yield return new object[] { 3, 9UL, 3.0, 400.0, 0.25 };
        yield return new object[] { 4, 10UL, 0.75, 75.0, 0.75 };
        yield return new object[] { 4, 11UL, 2.0, 1000.0, 0.9 };
        yield return new object[] { 3, 12UL, 1.0, 0.0, 0.5 };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void AiVersusAi_PerThinkOracle_AndHashTwin(int seats, ulong seed, double ratio, double min, double recall)
    {
        GameData data = Data(ratio, min, recall);
        var stats = new Stats();
        (List<ulong> a, List<Command> logA) = Run(data, seats, seed, 5000, oracle: true, stats);
        (List<ulong> b, List<Command> logB) = Run(data, seats, seed, 5000, oracle: false, new Stats());
        _out.WriteLine($"seats {seats} seed {seed} ratio {ratio} min {min} recall {recall}: thinks {stats.Thinks}, defending {stats.DefendingThinks}, " +
            $"picks {stats.Picks}, picking checks {stats.PickingChecks}, most defenders {stats.MaxDefenders}, pulls {stats.Pulls}, recalls {stats.Recalls}, commands {logA.Count}");
        Assert.Equal(a, b);
        Assert.Equal(logA, logB);
        Assert.True(stats.DefendingThinks > 0 && stats.Picks > 0, "nobody defended: the run proves little");
    }
}
