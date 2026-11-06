using System.Numerics;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA for M6-S4 criterion 5, the long run: four AI seats as 2v2 (teams 1, 2, 1, 2, so allies sit across the map) on the
/// shipped data, two raiders of each seat in the next (an enemy's) base, 12,000 ticks a seed. Every AI command is checked as
/// it is sent: no Attack on an ally's unit or building, no AttackMove or cast onto an ally's building. Every tick: no wave
/// targets an ally's building, and a seat that starts defending has an enemy (not an ally) army unit near its hall or a
/// damaged own building. At the end (allies not at mirror starts) both sides have waved, and at least three of the four seats.
/// M6-S5 (BUG-1042): the wave check is strict on every row, the mirror-start rows (<c>adjacent: true</c>, teams 1, 1, 2, 2)
/// included: every seat knows an enemy building by the end (with shared vision, what its ally sees counts), and every seat
/// that is not starved starts a wave at an enemy. Starved: its army (every unit the census counts as army) never reached its
/// opening's wave size (<c>attackMinHalfPop</c> and <c>attackMinCost</c> x the difficulty's <c>waveSizeMultiplier</c>, what
/// <c>MilitaryManager.TryStart</c> asks) on a tick it was not defending, up to tick 11,000 (a wave needs that army; a seat
/// whose raiders wrecked its economy, or an Easy seat that fell behind, never gets one, with or without teams). M6-S5 (BUG-1045):
/// a starved seat must have been raided (an enemy army within <c>ai.defendRadius</c> of its hall on at least 2,000 ticks), so a
/// seat starved by its own gather choice fails; each seat's waves, peak army, raided ticks, gathered gold / wood are printed.
/// </summary>
[Collection(SerialCollection.Name)]
[Trait("Category", "Soak")]
public class TeamAiSoakStressTests
{
    /// <summary>M6-S5 (BUG-1045): the least ticks with an enemy army near its hall a seat that never reached wave size must have had (100 s).</summary>
    // BUG-1049 item 3: the line sits in a narrow band. Measured 2026-11-05: the legitimately starved (raided) seats at
    // 2,570-7,168 raided ticks, the reach-rule-off failures (gather-starved) at 1,687 and 1,957. A change in raid behaviour
    // can move a row across 2,000 either way; move the line on purpose, with both ends re-measured.
    private const int RaidedTicksIfStarved = 2000;

    private readonly ITestOutputHelper _out;

    public TeamAiSoakStressTests(ITestOutputHelper output) => _out = output;

    private static bool NearAlliedBuilding(World w, int p, Vector2 point, float slack)
    {
        BuildingStore b = w.Buildings;
        for (int k = 0; k < b.Capacity; k++)
        {
            if (!b.Alive[k] || b.Owner[k] == p || w.IsEnemy(p, b.Owner[k])) continue;
            if (Combat.CombatSystem.BuildingDistanceSquared(w, k, point) <= slack * slack) return true;
        }
        return false;
    }

    private static bool EnemyArmyNear(World w, int p, Vector2 at, float radius)
    {
        UnitStore u = w.Units;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || !w.IsEnemy(p, u.Owner[i]) || w.Data.Units[u.TypeId[i]].IsWorker) continue;
            if (Vector2.DistanceSquared(u.Position[i], at) <= radius * radius) return true;
        }
        return false;
    }

    private static bool OwnBuildingDamaged(World w, int p)
    {
        BuildingStore b = w.Buildings;
        for (int k = 0; k < b.Capacity; k++)
            if (b.Alive[k] && b.Owner[k] == p && !b.UnderConstruction[k] && b.Hp[k] < w.Data.Buildings[b.TypeId[k]].Hp) return true;
        return false;
    }

    /// <summary>
    /// <paramref name="adjacent"/> false: teams 1, 2, 1, 2 (each seat's raider in an enemy's base); true: teams 1, 1, 2, 2, so seat 0's
    /// and seat 2's raider stand in an ally's base from tick 1 (an allied unit beside the hall, the defense's trap).
    /// </summary>
    [Theory]
    [InlineData(1UL, false)]
    [InlineData(2UL, false)]
    [InlineData(3UL, false)]
    [InlineData(4UL, false)]
    [InlineData(5UL, false)]
    [InlineData(1UL, true)]
    [InlineData(2UL, true)]
    [InlineData(3UL, true)]
    public void A2v2OfFourAiSeats_12000Ticks_NeverTargetsAnAlly_AndEverySeatStillWaves(ulong seed, bool adjacent)
    {
        PlayerSeat[] seats = Enumerable.Range(0, 4)
            .Select(p => new PlayerSeat(p % 2, SeatController.Ai, (AiDifficulty)((p + (int)seed) % 3), adjacent ? 1 + p / 2 : 1 + p % 2)).ToArray();
        // A 256 map with a start location, two mines and room for each seat (on the 128 helper map with six mines the seats past
        // the first two starve with or without teams: a scene limit, not a team one).
        Simulation sim = AiScenes.Generated(seats, seed, record: false, out _, units: 800, commands: 2048, raiders: 1,
            configure: c => c with { Map = c.Map with { Width = 256, Height = 256, Forests = 24, GoldMines = 16, StartLocations = 4 } });
        World w = sim.World;
        var bad = new List<string>();
        int attacks = 0, attackMoves = 0, casts = 0, defenses = 0;
        for (int p = 0; p < 4; p++)
        {
            int seat = p;
            sim.AiOf(p)!.SentForTests = c =>
            {
                switch (c.Kind)
                {
                    case CommandKind.Attack:
                        attacks++;
                        int owner = c.TargetIsBuilding ? (w.Buildings.IsAlive(c.Target) ? w.Buildings.Owner[c.Target.Index] : -1)
                            : (w.Units.IsAlive(c.Target) ? w.Units.Owner[c.Target.Index] : -1);
                        if (owner >= 0 && w.AreAllied(seat, owner)) bad.Add($"tick {sim.TickNumber}: seat {seat} Attack on player {owner}'s {(c.TargetIsBuilding ? "building" : "unit")}");
                        break;
                    case CommandKind.AttackMove:
                        attackMoves++;
                        if (NearAlliedBuilding(w, seat, c.Position, 0.5f)) bad.Add($"tick {sim.TickNumber}: seat {seat} AttackMove onto an ally's building at {c.Position}");
                        break;
                    case CommandKind.UseAbility:
                        casts++;
                        if (NearAlliedBuilding(w, seat, c.Position, 0.5f)) bad.Add($"tick {sim.TickNumber}: seat {seat} cast onto an ally's building at {c.Position}");
                        break;
                }
            };
        }
        var defending = new bool[4];
        var waveSized = new bool[4];
        var knew = new bool[4];
        var firstRaid = new int[4];
        var raidedTicks = new int[4];
        var peakArmy = new int[4];
        Array.Fill(firstRaid, -1);
        for (int t = 0; t < 12_000; t++)
        {
            sim.Tick();
            for (int p = 0; p < 4; p++)
            {
                AiPlayer ai = sim.AiOf(p)!;
                knew[p] |= ai.Board.KnownEnemyBuildings > 0;
                if (!w.IsDefeated(p) && EnemyArmyNear(w, p, ai.HallPoint, ai.Rules.DefendRadius))
                {
                    raidedTicks[p]++;
                    if (firstRaid[p] < 0) firstRaid[p] = sim.TickNumber;
                }
                if (sim.TickNumber % 20 == 0) peakArmy[p] = Math.Max(peakArmy[p], ArmyHalfPop(w, ai));
                if (!waveSized[p] && !ai.Board.Defending && sim.TickNumber <= 11_000) waveSized[p] = WaveSized(w, ai);
                EntityHandle target = ai.Board.WaveTarget;
                if (target.Generation != 0 && w.Buildings.IsAlive(target) && w.AreAllied(p, w.Buildings.Owner[target.Index]))
                    bad.Add($"tick {sim.TickNumber}: seat {p}'s wave targets player {w.Buildings.Owner[target.Index]}'s building");
                bool now = ai.Board.Defending;
                if (now && !defending[p])
                {
                    defenses++;
                    float radius = ai.Rules.DefendRadius + 12f;
                    if (!EnemyArmyNear(w, p, ai.HallPoint, radius) && !OwnBuildingDamaged(w, p))
                        bad.Add($"tick {sim.TickNumber}: seat {p} started defending with no enemy army within {radius} m of its hall and no damaged building");
                }
                defending[p] = now;
            }
            Assert.True(bad.Count == 0, string.Join("\n", bad.Take(10)));
            if (sim.MatchOver) break;
        }
        int[] waves = Enumerable.Range(0, 4).Select(p => sim.AiOf(p)!.Board.WavesStarted).ToArray();
        _out.WriteLine($"seed {seed} adjacent {adjacent}: {sim.TickNumber} ticks, {attacks} Attacks, {attackMoves} AttackMoves, {casts} casts, {defenses} defenses; " +
            $"waves {string.Join(", ", waves)}; defeated {string.Join(", ", Enumerable.Range(0, 4).Select(p => w.IsDefeated(p)))}; over {sim.MatchOver}");
        // Both sides wave at the enemy. Not every seat: on seed 3 the Easy seat 3 falls behind and never waves, the same with every
        // team 0 (QA checked the free-for-all twin: equal counts), so that is the economy, not the teams.
        Assert.True(w.Losses.ToArray().Sum() > 0, "nobody lost anything");
        _out.WriteLine($"knew an enemy building {string.Join(", ", knew)}; army reached wave size {string.Join(", ", waveSized)}");
        for (int p = 0; p < 4; p++)
            _out.WriteLine($"  seat {p} ({(AiDifficulty)((p + (int)seed) % 3)}): waves {waves[p]}, wave-sized {waveSized[p]}, peak army half-pop {peakArmy[p]}, " +
                $"enemy army within defendRadius of its hall from tick {firstRaid[p]} on {raidedTicks[p]} ticks, " +
                $"gathered gold {w.GatheredGold[p]} wood {w.GatheredWood[p]}, losses {w.Losses[p]}");
        // M6-S5 (BUG-1042, strict on every row): every seat learns of an enemy building (its own eyes or its ally's), and every
        // seat whose army reached wave size waves at the enemy.
        Assert.True(knew.All(k => k), $"a seat never knew an enemy building: {string.Join(", ", knew)}");
        for (int p = 0; p < 4; p++)
            Assert.True(!waveSized[p] || waves[p] > 0, $"seat {p}'s army reached wave size but it never waved (waves {string.Join(", ", waves)})");
        // M6-S5 (BUG-1045): "starved" is a raid's doing only. A seat whose army never reached wave size must have had an enemy
        // army within ai.defendRadius of its hall on at least RaidedTicksIfStarved ticks (BUG-1045's seat 3 starved unraided:
        // 0 ticks defending). Measured 2026-11-05: the six starved seats (adjacent seeds 1-3, seed 5) were raided from tick
        // 11-41 on 2,570-7,168 ticks.
        for (int p = 0; p < 4; p++)
            Assert.True(waveSized[p] || raidedTicks[p] >= RaidedTicksIfStarved,
                $"seat {p} never reached wave size but was raided on only {raidedTicks[p]} ticks (from tick {firstRaid[p]}): starved by its economy, not a raid");
        if (adjacent) return;
        Assert.True(waves[0] + waves[2] > 0 && waves[1] + waves[3] > 0, "a side never waved");
        Assert.True(waves.Count(n => n > 0) >= 3, "two seats never waved");
    }

    /// <summary>The live half-pop of <paramref name="ai"/>'s army (its census's army units).</summary>
    private static int ArmyHalfPop(World w, AiPlayer ai)
    {
        int half = 0;
        for (int k = 0; k < ai.ArmyCount; k++)
            if (w.Units.Alive[ai.ArmySlots[k]]) half += w.Data.Units[w.Units.TypeId[ai.ArmySlots[k]]].HalfPop;
        return half;
    }

    /// <summary>Whether <paramref name="ai"/>'s army (its census's army units) is wave-sized by its opening and difficulty, as <c>MilitaryManager.TryStart</c> sizes a wave.</summary>
    private static bool WaveSized(World w, AiPlayer ai)
    {
        int half = 0, cost = 0;
        for (int k = 0; k < ai.ArmyCount; k++)
        {
            int s = ai.ArmySlots[k];
            if (!w.Units.Alive[s]) continue;
            UnitDef d = w.Data.Units[w.Units.TypeId[s]];
            half += d.HalfPop;
            cost += d.CostGold + d.CostWood;
        }
        float scale = ai.Knobs.WaveSizeMultiplier;
        return half > 0 && half >= ai.Opening.AttackMinHalfPop * scale && cost >= ai.Opening.AttackMinCost * scale;
    }
}
