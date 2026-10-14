using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;

namespace Rts.Sim.Tests;

/// <summary>
/// M5-4e criterion 3: the defeat rule's two counts (<see cref="World.LiveUnits"/>, <see cref="World.ProductionBuildings"/>)
/// are kept incrementally at spawn, finish and free; an oracle recounts them from the stores after every tick of a seeded
/// run with four players: units spawned (the door) and summoned (with a lifetime, the store seam), killed in fights and freed
/// by hand; buildings placed finished or as sites, finished, cancelled and razed; production at whatever trains. Every
/// player's counts equal the recount every tick, and a player defeated on a tick has 0 of both then.
/// </summary>
public class DefeatCountsFuzzTests
{
    private const int Players = 4, Size = 64;

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void TheCounts_EqualARecountFromTheStores_EveryTick(ulong seed)
    {
        Simulation sim = CombatScenes.Flat(Size, units: 160, players: Players, seed: seed);
        World w = sim.World;
        UnitStore u = w.Units;
        BuildingStore b = w.Buildings;
        GameData data = w.Data;
        var rng = new SimRng(seed, 5402);
        for (int p = 0; p < Players; p++) BuildMaps.SetTotals(sim, p, 100_000, 100_000);
        int summoned = 0, expired = 0, cancels = 0, razed = 0, finished = 0, freed = 0, defeats = 0, trainsAt = 0;
        var units = new int[Players];
        var production = new int[Players];
        for (int t = 0; t < 2000; t++)
        {
            int n = rng.NextInt(0, 4);
            for (int k = 0; k < n; k++)
            {
                int p = rng.NextInt(0, Players);
                Vector2 at = w.NavGrid.CellCenter(rng.NextInt(2, Size - 2), rng.NextInt(2, Size - 2));
                switch (rng.NextInt(0, 12))
                {
                    case 0 or 1:
                        sim.Enqueue(Command.SpawnUnit(p, rng.NextInt(0, data.Units.Length), at));
                        break;
                    case 2:
                    {
                        // A summon: free, gone when its lifetime ends (phase 5).
                        int type = rng.NextInt(0, data.Units.Length);
                        if (u.TrySpawn(p, type, data.Units[type], at, sim.TickNumber + rng.NextInt(1, 80), out _)) summoned++;
                        break;
                    }
                    case 3:
                        b.Spawn(p, rng.NextInt(0, data.Buildings.Length), rng.NextInt(2, Size - 6) * Size + rng.NextInt(2, Size - 6), out _, site: rng.NextInt(0, 2) == 0);
                        break;
                    case 4:
                    {
                        int s = RandomBuilding(ref rng, b);
                        if (s < 0) break;
                        if (b.UnderConstruction[s] && rng.NextInt(0, 2) == 0)
                        {
                            b.SetWork(s, b.WorkNeeded(b.TypeId[s]));
                            finished++;
                        }
                        else if (b.UnderConstruction[s])
                        {
                            sim.Enqueue(Command.Cancel(b.Owner[s], w.NavGrid.CellCenter(b.Cell[s] % Size, b.Cell[s] / Size)));
                            cancels++;
                        }
                        else if (data.UnitsTrainedAt(b.TypeId[s]).Length > 0)
                        {
                            var list = data.UnitsTrainedAt(b.TypeId[s]);
                            sim.Enqueue(Command.Train(b.Owner[s], w.NavGrid.CellCenter(b.Cell[s] % Size, b.Cell[s] / Size), list[rng.NextInt(0, list.Length)]));
                            trainsAt++;
                        }
                        break;
                    }
                    case 5:
                    {
                        int s = RandomBuilding(ref rng, b);
                        if (s < 0) break;
                        b.Damage(b.HandleOf(s), rng.NextInt(1, 2000));
                        if (!b.Alive[s]) razed++;
                        break;
                    }
                    case 6:
                    {
                        int i = rng.NextInt(0, u.Capacity);
                        if (!u.Alive[i]) break;
                        u.Free(new EntityHandle(i, u.Generation[i]));
                        freed++;
                        break;
                    }
                    default:
                    {
                        // A fight: a live unit attacks a live unit of another player.
                        int i = rng.NextInt(0, u.Capacity), j = rng.NextInt(0, u.Capacity);
                        if (!u.Alive[i] || !u.Alive[j] || u.Owner[i] == u.Owner[j]) break;
                        sim.Enqueue(Command.Attack(u.Owner[i], new EntityHandle(i, u.Generation[i]), new EntityHandle(j, u.Generation[j]), isBuilding: false));
                        break;
                    }
                }
                Check(w, units, production, $"seed {seed} tick {sim.TickNumber} after a seam");
            }
            int summonsBefore = u.SummonedUnits;
            sim.Tick();
            if (u.SummonedUnits < summonsBefore) expired += summonsBefore - u.SummonedUnits;
            Check(w, units, production, $"seed {seed} tick {sim.TickNumber - 1}");
            foreach (DefeatEvent e in w.DefeatEvents)
            {
                Assert.True(units[e.Player] == 0 && production[e.Player] == 0, $"seed {seed}: player {e.Player} defeated with {units[e.Player]} units, {production[e.Player]} production buildings");
                defeats++;
            }
        }
        Assert.True(summoned > 50 && expired > 20 && cancels > 10 && razed > 20 && finished > 10 && freed > 20 && trainsAt > 20,
            $"seed {seed}: precondition: summoned {summoned}, expired {expired}, cancels {cancels}, razed {razed}, finished {finished}, freed {freed}, trains {trainsAt}, defeats {defeats}");
    }

    private static int RandomBuilding(ref SimRng rng, BuildingStore b)
    {
        int start = rng.NextInt(0, b.Capacity);
        for (int n = 0; n < b.Capacity; n++)
        {
            int k = (start + n) % b.Capacity;
            if (b.Alive[k]) return k;
        }
        return -1;
    }

    /// <summary>The oracle: every live unit counts for its owner; every finished building whose type trains a unit counts for its owner.</summary>
    private static void Check(World w, int[] units, int[] production, string at)
    {
        Array.Clear(units);
        Array.Clear(production);
        UnitStore u = w.Units;
        BuildingStore b = w.Buildings;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i]) units[u.Owner[i]]++;
        for (int k = 0; k < b.Capacity; k++)
            if (b.Alive[k] && !b.UnderConstruction[k] && w.Data.UnitsTrainedAt(b.TypeId[k]).Length > 0) production[b.Owner[k]]++;
        for (int p = 0; p < Players; p++)
        {
            Assert.True(units[p] == w.LiveUnits(p), $"{at}: player {p} LiveUnits {w.LiveUnits(p)}, recount {units[p]}");
            Assert.True(production[p] == w.ProductionBuildings(p), $"{at}: player {p} ProductionBuildings {w.ProductionBuildings(p)}, recount {production[p]}");
        }
    }
}
