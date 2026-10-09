using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Xunit.Abstractions;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// D6 balance report (M4-2b): how wide each counter-triangle win is at equal cost. The six group pairs of
/// <c>Scenario/CounterTriangleTests</c> run in both seats on the same scene, and its two siege rows run as there; the test
/// prints one markdown table (the "Balance baseline" section of docs/factions/malazan.md and whirlwind.md). It pins only
/// what docs/02 "Faction template" pins, the winner: the margins are the owner's call, so no margin is asserted.
/// <para>
/// The scene is a copy of <c>CounterTriangleTests</c>' private <c>Fight</c> / <c>TimeToKill</c> on the public
/// <see cref="CombatScenes"/> helpers (the data track does not edit the sim's scenario file): 1,200 gold + wood a side,
/// two blocks 5 wide, fronts 24 m apart, seed 7, attack-move to the far block's center, 3,000 ticks at most.
/// </para>
/// </summary>
public class CounterTriangleMarginsTests
{
    private const int Budget = 1200;
    private const int MaxTicks = 3000;
    private const int SiegeMaxTicks = 6000;

    private readonly ITestOutputHelper _out;

    public CounterTriangleMarginsTests(ITestOutputHelper output) => _out = output;

    private static int Cost(UnitDef d) => d.CostGold + d.CostWood;

    /// <summary>One side after the fight: units fielded and left, their cost.</summary>
    private readonly record struct Side(string Key, int Fielded, int FieldedCost, int Left, int CostLeft);

    /// <summary>The docs/02 counter pairs: rule, the doc's winner, its loser (as in <c>CounterTriangleTests</c>).</summary>
    private static readonly (string Rule, string Winner, string Loser)[] Pairs =
    {
        ("Line beats Shock", "malazan_heavy_infantry", "whirlwind_horse_raider"),
        ("Line beats Shock", "whirlwind_raider", "malazan_wickan_lancer"),
        ("Shock beats Ranged", "malazan_wickan_lancer", "whirlwind_desert_archer"),
        ("Shock beats Ranged", "whirlwind_horse_raider", "malazan_crossbowman"),
        ("Ranged beats casters", "malazan_crossbowman", "whirlwind_priest"),
        ("Ranged beats casters", "whirlwind_desert_archer", "malazan_cadre_mage"),
    };

    /// <summary>docs/02 "Siege beats buildings": siege unit, the same cost of its faction's line infantry, the enemy building.</summary>
    private static readonly (string Siege, string Line, string Building)[] SiegeRows =
    {
        ("malazan_catapult", "malazan_heavy_infantry", "whirlwind_tent"),
        ("whirlwind_battering_ram", "whirlwind_raider", "malazan_billet"),
    };

    /// <summary><c>CounterTriangleTests.Fight</c>: <paramref name="keyA"/> as player <paramref name="seatA"/> against <paramref name="keyB"/>; ticks until one side is gone.</summary>
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
        int[] order = seatA == 0 ? new[] { 0, 1 } : new[] { 1, 0 };
        foreach (int side in order)
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
        foreach (int side in order)
            foreach (EntityHandle h in handles[side])
                sim.Enqueue(Command.AttackMove(seats[side], h, mid[1 - side]));
        int ticks = RunUntil(sim, () => !handles[0].Any(u.IsAlive) || !handles[1].Any(u.IsAlive), MaxTicks);
        Side Result(int side)
        {
            int cost = Cost(data.Units[types[side]]);
            int left = handles[side].Count(u.IsAlive);
            return new Side(new[] { keyA, keyB }[side], counts[side], counts[side] * cost, left, left * cost);
        }
        return (Result(0), Result(1), ticks);
    }

    /// <summary><c>CounterTriangleTests.TimeToKill</c>: ticks for <paramref name="n"/> <paramref name="attackerKey"/> to destroy a <paramref name="buildingKey"/>; <see cref="int.MaxValue"/> if not within <see cref="SiegeMaxTicks"/>.</summary>
    private static int TimeToKill(string attackerKey, int n, string buildingKey)
    {
        GameData data = TestSim.Data;
        Simulation sim = Flat(size: 48, units: n + 4);
        World w = sim.World;
        Assert.True(w.Buildings.Spawn(1, data.FindBuilding(buildingKey), 24 * w.NavGrid.Width + 24, out EntityHandle b));
        // M4-3a, as in the scenario: the attackers start out of sight of the building (the fog would drop their Attack),
        // so a spotter stands on its far side.
        Spotter(sim, 0, At(sim, 24 + data.Buildings[data.FindBuilding(buildingKey)].FootprintWidth + 3, 24));
        int type = data.FindUnit(attackerKey);
        for (int k = 0; k < n; k++)
        {
            EntityHandle a = Place(sim, 0, type, new Vector2(30f, 44f + 2f * k));
            sim.Enqueue(Command.Attack(0, a, b, isBuilding: true));
        }
        int t = RunUntil(sim, () => !w.Buildings.IsAlive(b), SiegeMaxTicks);
        return w.Buildings.IsAlive(b) ? int.MaxValue : t;
    }

    private static string Name(string key) => TestSim.Data.Units[TestSim.Data.FindUnit(key)].DisplayName;

    private static string Seconds(int ticks) => ticks == int.MaxValue ? "not done" : FormattableString.Invariant($"{ticks / 20f:F1} s");

    [Fact]
    public void AllEightPairs_BothSeats_PrintTheMarginTable_AndTheDocWinnerWins()
    {
        var failures = new List<string>();
        _out.WriteLine("| Rule | Winner v loser | Winner seat | Fielded (winner v loser) | Winner left | Winner keeps (cost) | Time to last death |");
        _out.WriteLine("| --- | --- | --- | --- | --- | --- | --- |");
        foreach ((string rule, string winner, string loser) in Pairs)
            for (int seat = 0; seat < 2; seat++)
            {
                (Side w, Side l, int ticks) = Fight(winner, loser, seat);
                int keeps = (int)Math.Round(100.0 * w.CostLeft / w.FieldedCost, MidpointRounding.AwayFromZero);
                _out.WriteLine(FormattableString.Invariant(
                    $"| {rule} | {Name(winner)} v {Name(loser)} | {seat} | {w.Fielded} ({w.FieldedCost}) v {l.Fielded} ({l.FieldedCost}) | {w.Left} / {w.Fielded} | {w.CostLeft} / {w.FieldedCost} ({keeps} %) | {Seconds(ticks)} |"));
                // The only pin: docs/02's winner wins (the loser is wiped out, the winner is not).
                if (!(w.Left > 0 && l.Left == 0))
                    failures.Add($"{rule}: {winner} as player {seat} left {w.Left}, {loser} left {l.Left} after {ticks} ticks");
            }

        _out.WriteLine("");
        _out.WriteLine("| Rule | Siege unit | Same cost of line infantry | Building | Siege time | Line time | Siege / line |");
        _out.WriteLine("| --- | --- | --- | --- | --- | --- | --- |");
        GameData data = TestSim.Data;
        foreach ((string siege, string line, string building) in SiegeRows)
        {
            int n = Math.Max(1, (int)Math.Round((double)Cost(data.Units[data.FindUnit(siege)]) / Cost(data.Units[data.FindUnit(line)]), MidpointRounding.AwayFromZero));
            int s = TimeToKill(siege, 1, building), l = TimeToKill(line, n, building);
            string ratio = s == int.MaxValue || l == int.MaxValue ? "-" : FormattableString.Invariant($"{100.0 * s / l:F0} %");
            _out.WriteLine($"| Siege beats buildings | 1 {Name(siege)} | {n} {Name(line)} | {data.Buildings[data.FindBuilding(building)].DisplayName} | {Seconds(s)} | {Seconds(l)} | {ratio} |");
            if (!(s < l)) failures.Add($"Siege beats buildings: 1 {siege} {Seconds(s)} is not faster than {n} {line} {Seconds(l)} on a {building}");
        }
        Assert.True(failures.Count == 0, "The doc's winner does not win:\n" + string.Join("\n", failures));
    }
}
