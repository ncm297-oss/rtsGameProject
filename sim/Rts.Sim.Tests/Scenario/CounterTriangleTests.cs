using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Xunit.Abstractions;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.Scenario;

/// <summary>
/// M4 criterion "Scenario tests for the counter triangle" (M4-2b; docs/02 "Faction template", the counter table): two
/// equal-cost groups on a flat map, attack-moved into each other, for both factions' pairs, each seat order; and siege
/// against a building next to the same cost of line infantry. Every row prints the survivors and the time. A row whose
/// doc winner loses fails naming the pair and the margin: that is a data issue for the data track's balance pass, not a
/// sim tune.
/// </summary>
public class CounterTriangleTests
{
    /// <summary>Gold + wood per side; each side fields the whole number of units nearest to it.</summary>
    private const int Budget = 1200;

    /// <summary>Ticks a fight may last (2.5 min).</summary>
    private const int MaxTicks = 3000;

    private readonly ITestOutputHelper _out;

    public CounterTriangleTests(ITestOutputHelper output) => _out = output;

    private static int Cost(UnitDef d) => d.CostGold + d.CostWood;

    /// <summary>One side's result: units left, their hit points and cost.</summary>
    private readonly record struct Side(string Key, int Fielded, int FieldedCost, int Left, int Hp, int CostLeft);

    /// <summary>
    /// <paramref name="keyA"/> (player <paramref name="seatA"/>) against <paramref name="keyB"/>, <see cref="Budget"/> each,
    /// two blocks 5 wide whose fronts start 24 m apart, every unit attack-moved to the far block's center. Seat 1 mirrors
    /// the scene: A's block on the east and spawned second (higher slots). Runs until one side is wiped out or
    /// <see cref="MaxTicks"/>.
    /// </summary>
    private static (Side A, Side B, int Ticks) Fight(string keyA, string keyB, int seatA)
    {
        GameData data = TestSim.Data;
        int typeA = data.FindUnit(keyA), typeB = data.FindUnit(keyB);
        int nA = Math.Max(1, (int)Math.Round((double)Budget / Cost(data.Units[typeA]), MidpointRounding.AwayFromZero));
        int nB = Math.Max(1, (int)Math.Round((double)Budget / Cost(data.Units[typeB]), MidpointRounding.AwayFromZero));
        Simulation sim = Flat(size: 64, units: nA + nB + 4, seed: 7);
        UnitStore u = sim.World.Units;
        var center = new Vector2(64f, 64f);
        var mid = new Vector2[2];
        var handles = new List<EntityHandle>[] { new(), new() };
        int[] seats = { seatA, 1 - seatA };
        int[] types = { typeA, typeB };
        int[] counts = { nA, nB };
        foreach (int side in seatA == 0 ? new[] { 0, 1 } : new[] { 1, 0 })
        {
            float dir = (side == 0) == (seatA == 0) ? -1f : 1f;
            const int width = 5;
            const float spacing = 1.8f;
            int ranks = (counts[side] + width - 1) / width;
            mid[side] = center + new Vector2(dir * (12f + (ranks - 1) * spacing / 2f), 0f);
            for (int k = 0; k < counts[side]; k++)
            {
                int rank = k / width, file = k % width;
                var at = center + new Vector2(dir * (12f + rank * spacing), (file - (width - 1) / 2f) * spacing);
                handles[side].Add(Place(sim, seats[side], types[side], at));
            }
        }
        foreach (int side in seatA == 0 ? new[] { 0, 1 } : new[] { 1, 0 })
            foreach (EntityHandle h in handles[side])
                sim.Enqueue(Command.AttackMove(seats[side], h, mid[1 - side]));
        int ticks = RunUntil(sim, () => !handles[0].Any(u.IsAlive) || !handles[1].Any(u.IsAlive), MaxTicks);
        Side Result(int side) => new(
            new[] { keyA, keyB }[side], counts[side], counts[side] * Cost(data.Units[types[side]]),
            handles[side].Count(u.IsAlive),
            handles[side].Where(u.IsAlive).Sum(h => u.Hp[h.Index]),
            handles[side].Count(u.IsAlive) * Cost(data.Units[types[side]]));
        return (Result(0), Result(1), ticks);
    }

    private static string Describe(Side s) => $"{s.Key} {s.Left}/{s.Fielded} left ({s.Hp} hp, cost {s.CostLeft} of {s.FieldedCost})";

    [Theory]
    [InlineData("Line beats Shock", "malazan_heavy_infantry", "whirlwind_horse_raider")]
    [InlineData("Line beats Shock", "whirlwind_raider", "malazan_wickan_lancer")]
    [InlineData("Shock beats Ranged", "malazan_wickan_lancer", "whirlwind_desert_archer")]
    [InlineData("Shock beats Ranged", "whirlwind_horse_raider", "malazan_crossbowman")]
    [InlineData("Ranged beats Light (casters)", "malazan_crossbowman", "whirlwind_priest")]
    [InlineData("Ranged beats Light (casters)", "whirlwind_desert_archer", "malazan_cadre_mage")]
    public void EqualCostGroups_TheCounterWins(string rule, string winner, string loser)
    {
        var failures = new List<string>();
        for (int seat = 0; seat < 2; seat++)
        {
            (Side w, Side l, int ticks) = Fight(winner, loser, seat);
            string line = $"{rule}, {winner} as player {seat}: {Describe(w)} v {Describe(l)} after {ticks} ticks ({ticks / 20f:F1} s)";
            _out.WriteLine(line);
            if (!(w.Left > 0 && l.Left == 0))
                failures.Add($"{line}; margin {w.CostLeft - l.CostLeft} cost, {w.Hp - l.Hp} hp in {winner}'s favour");
        }
        Assert.True(failures.Count == 0, $"{winner} v {loser} ({rule}) does not give the doc's winner:\n" + string.Join("\n", failures));
    }

    /// <summary>Ticks until <paramref name="n"/> units of <paramref name="attackerKey"/> (player 0), ordered to attack it, destroy a player-1 <paramref name="buildingKey"/>; <see cref="int.MaxValue"/> if not within <paramref name="max"/> ticks.</summary>
    private static int TimeToKill(string attackerKey, int n, string buildingKey, int max)
    {
        GameData data = TestSim.Data;
        Simulation sim = Flat(size: 48, units: n + 4);
        World w = sim.World;
        Assert.True(w.Buildings.Spawn(1, data.FindBuilding(buildingKey), 24 * w.NavGrid.Width + 24, out EntityHandle b));
        // M4-3a: the attackers start out of sight of it (the fog would drop their Attack): a spotter on its far side.
        Spotter(sim, 0, At(sim, 24 + data.Buildings[data.FindBuilding(buildingKey)].FootprintWidth + 3, 24));
        int type = data.FindUnit(attackerKey);
        for (int k = 0; k < n; k++)
        {
            EntityHandle a = Place(sim, 0, type, new Vector2(30f, 44f + 2f * k));
            sim.Enqueue(Command.Attack(0, a, b, isBuilding: true));
        }
        int t = RunUntil(sim, () => !w.Buildings.IsAlive(b), max);
        return w.Buildings.IsAlive(b) ? int.MaxValue : t;
    }

    [Theory]
    [InlineData("malazan_catapult", "malazan_heavy_infantry", "whirlwind_tent")]
    [InlineData("whirlwind_battering_ram", "whirlwind_raider", "malazan_billet")]
    public void SiegeBeatsBuildings_OneSiegeUnitKillsABuildingFasterThanTheSameCostOfLineInfantry(string siege, string line, string building)
    {
        GameData data = TestSim.Data;
        int n = Math.Max(1, (int)Math.Round((double)Cost(data.Units[data.FindUnit(siege)]) / Cost(data.Units[data.FindUnit(line)]), MidpointRounding.AwayFromZero));
        const int max = 6000;
        int siegeTicks = TimeToKill(siege, 1, building, max);
        int lineTicks = TimeToKill(line, n, building, max);
        string Show(int t) => t == int.MaxValue ? $"not in {max} ticks" : $"{t} ticks ({t / 20f:F1} s)";
        _out.WriteLine($"{building}: 1 {siege} {Show(siegeTicks)}; {n} {line} {Show(lineTicks)}");
        Assert.True(siegeTicks < lineTicks, $"Siege beats buildings: 1 {siege} {Show(siegeTicks)} is not faster than {n} {line} {Show(lineTicks)} on a {building}");
    }
}
