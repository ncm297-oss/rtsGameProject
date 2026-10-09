using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.Scenario;

/// <summary>
/// The counter-triangle scene (M4-2b; BUG-0230 item 2): the one fight harness <see cref="CounterTriangleTests"/> asserts
/// winners on and the data track's balance report prints margins from, so a page's "on the scene of
/// <c>Scenario/CounterTriangleTests</c>" stays true when the scene changes. Every scene parameter is a named constant here.
/// </summary>
public static class CounterTriangleScene
{
    /// <summary>Gold + wood per side; each side fields the whole number of units nearest to it (at least one).</summary>
    public const int Budget = 1200;

    /// <summary>Ticks a group fight may last (2.5 min).</summary>
    public const int MaxTicks = 3000;

    /// <summary>Ticks a siege row's building may stand before the row counts it as not destroyed (5 min).</summary>
    public const int SiegeMaxTicks = 6000;

    /// <summary>The group fight's flat map side in cells (128 m).</summary>
    public const int MapSize = 64;

    /// <summary>The group fight's sim seed.</summary>
    public const ulong Seed = 7;

    /// <summary>Units a rank of a block.</summary>
    public const int BlockWidth = 5;

    /// <summary>Meters between neighbours in a block, along a rank and between ranks.</summary>
    public const float Spacing = 1.8f;

    /// <summary>Meters from the map's centre to each block's front rank (so the fronts start 24 m apart).</summary>
    public const float FrontOffset = 12f;

    /// <summary>The siege rows' flat map side in cells.</summary>
    public const int SiegeMapSize = 48;

    /// <summary>A unit's gold + wood cost.</summary>
    public static int Cost(UnitDef d) => d.CostGold + d.CostWood;

    /// <summary>How many units of <paramref name="type"/> <see cref="Budget"/> fields: the nearest whole number, at least one.</summary>
    public static int CountFor(GameData data, int type) =>
        Math.Max(1, (int)Math.Round((double)Budget / Cost(data.Units[type]), MidpointRounding.AwayFromZero));

    /// <summary>How many <paramref name="lineKey"/> units cost the same as one <paramref name="siegeKey"/> (nearest, at least one).</summary>
    public static int SameCostCount(GameData data, string siegeKey, string lineKey) =>
        Math.Max(1, (int)Math.Round((double)Cost(data.Units[data.FindUnit(siegeKey)]) / Cost(data.Units[data.FindUnit(lineKey)]), MidpointRounding.AwayFromZero));

    /// <summary>One side's result: units fielded and left, their hit points and cost.</summary>
    public readonly record struct Side(string Key, int Fielded, int FieldedCost, int Left, int Hp, int CostLeft);

    /// <summary>
    /// <paramref name="keyA"/> (player <paramref name="seatA"/>) against <paramref name="keyB"/>, <see cref="Budget"/> each,
    /// two blocks <see cref="BlockWidth"/> wide whose fronts start 2 x <see cref="FrontOffset"/> apart on a flat
    /// <see cref="MapSize"/> map (seed <see cref="Seed"/>), every unit attack-moved to the far block's center. Seat 1
    /// mirrors the scene: A's block on the east and spawned second (higher slots). Runs until one side is wiped out or
    /// <see cref="MaxTicks"/>; returns both sides and the ticks run. Shipped data (<see cref="TestSim.Data"/>).
    /// </summary>
    public static (Side A, Side B, int Ticks) Fight(string keyA, string keyB, int seatA)
    {
        GameData data = TestSim.Data;
        int typeA = data.FindUnit(keyA), typeB = data.FindUnit(keyB);
        int nA = CountFor(data, typeA), nB = CountFor(data, typeB);
        Simulation sim = Flat(size: MapSize, units: nA + nB + 4, seed: Seed);
        UnitStore u = sim.World.Units;
        float half = MapSize * Map.MapConstants.CellSize / 2f;
        var center = new Vector2(half, half);
        var mid = new Vector2[2];
        var handles = new List<EntityHandle>[] { new(), new() };
        int[] seats = { seatA, 1 - seatA };
        int[] types = { typeA, typeB };
        int[] counts = { nA, nB };
        foreach (int side in seatA == 0 ? new[] { 0, 1 } : new[] { 1, 0 })
        {
            float dir = (side == 0) == (seatA == 0) ? -1f : 1f;
            int ranks = (counts[side] + BlockWidth - 1) / BlockWidth;
            mid[side] = center + new Vector2(dir * (FrontOffset + (ranks - 1) * Spacing / 2f), 0f);
            for (int k = 0; k < counts[side]; k++)
            {
                int rank = k / BlockWidth, file = k % BlockWidth;
                var at = center + new Vector2(dir * (FrontOffset + rank * Spacing), (file - (BlockWidth - 1) / 2f) * Spacing);
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

    /// <summary>
    /// Ticks until <paramref name="n"/> units of <paramref name="attackerKey"/> (player 0), ordered to attack it, destroy a
    /// player-1 <paramref name="buildingKey"/> anchored at cell (24, 24) of a flat <see cref="SiegeMapSize"/> map, the
    /// attackers in a column from (30, 44) m 2 m apart and a player-0 spotter 3 cells past the building's east side (the
    /// attackers start out of sight of it, and the fog would drop their Attack); <see cref="int.MaxValue"/> if not within
    /// <paramref name="max"/> ticks.
    /// </summary>
    public static int TimeToKill(string attackerKey, int n, string buildingKey, int max = SiegeMaxTicks)
    {
        GameData data = TestSim.Data;
        Simulation sim = Flat(size: SiegeMapSize, units: n + 4);
        World w = sim.World;
        Assert.True(w.Buildings.Spawn(1, data.FindBuilding(buildingKey), 24 * w.NavGrid.Width + 24, out EntityHandle b));
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
}
