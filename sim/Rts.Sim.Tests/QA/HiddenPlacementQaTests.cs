using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Determinism;
using Rts.Sim.Economy;
using Rts.Sim.Entities;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA (BUG-0280 sweep, M4-4a): <see cref="World.CanPlace"/> never depends on an enemy unit the player can't see. Two worlds
/// that differ only by a crowd of hidden enemy units must answer every (building type, anchor cell) query of the player
/// identically, reason included; seen enemy units, which both worlds hold, still refuse as <c>UnitInTheWay</c>.
/// </summary>
[Collection(SerialCollection.Name)]
public class HiddenPlacementQaTests
{
    private static Simulation World(ulong seed, bool withHidden, out List<EntityHandle> hidden, out List<EntityHandle> seen)
    {
        var sim = TestSim.Explored(new Simulation(
            TestSim.ConfigWithoutBuildingRequires(Seed: seed, PlayerCount: 2, UnitCapacity: 128, CommandCapacity: 64) with { Combat = false },
            LocalMovementTests.Flat(48)));
        BuildMaps.Give(sim, 0, 100_000, 100_000);
        hidden = new List<EntityHandle>();
        seen = new List<EntityHandle>();
        int laborer = CombatScenes.Laborer;
        // Player 0's eyes: two laborers in the south-west quarter (sight 14 m).
        CombatScenes.Place(sim, 0, laborer, CombatScenes.At(sim, 6, 6));
        CombatScenes.Place(sim, 0, laborer, CombatScenes.At(sim, 12, 8));
        // Seen enemies, in both worlds: near the laborers.
        var rng = new SimRng(seed, 280);
        for (int k = 0; k < 6; k++)
            seen.Add(CombatScenes.Place(sim, 1, laborer, CombatScenes.At(sim, 4 + rng.NextInt(0, 10), 4 + rng.NextInt(0, 6), rng.NextFloat() - 0.5f, rng.NextFloat() - 0.5f)));
        // Hidden enemies, only in one world: anywhere at least 30 cells (60 m) from the laborers' corner along an axis.
        var hrng = new SimRng(seed, 281);
        for (int k = 0; k < 60; k++)
        {
            int x = hrng.NextInt(0, 48), y = hrng.NextInt(26, 48);
            if (k % 2 == 0) (x, y) = (hrng.NextInt(26, 48), hrng.NextInt(0, 48));
            float dx = hrng.NextFloat() - 0.5f, dy = hrng.NextFloat() - 0.5f;
            if (withHidden) hidden.Add(CombatScenes.Place(sim, 1, CombatScenes.Crossbowman, CombatScenes.At(sim, x, y, dx, dy)));
        }
        sim.Tick();
        sim.Tick();
        return sim;
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void CanPlace_IsTheSame_WithOrWithoutHiddenEnemyUnits_ForEveryTypeAndCell(ulong seed)
    {
        Simulation a = World(seed, withHidden: true, out List<EntityHandle> hidden, out List<EntityHandle> seenA);
        Simulation b = World(seed, withHidden: false, out _, out _);
        foreach (EntityHandle h in hidden) Assert.False(a.World.Fog.CanSeeUnit(0, h.Index), $"seed {seed}: hidden unit {h.Index} is seen");
        int seenCount = 0;
        foreach (EntityHandle h in seenA) if (a.World.Fog.CanSeeUnit(0, h.Index)) seenCount++;
        Assert.True(seenCount >= 4, $"only {seenCount} seen enemies");
        int cells = a.World.NavGrid.Width * a.World.NavGrid.Height;
        int queries = 0, inTheWay = 0, hiddenOverlaps = 0, okOverHidden = 0;
        for (int type = 0; type < TestSim.Data.Buildings.Length; type++)
            for (int c = 0; c < cells; c++)
            {
                bool okA = a.World.CanPlace(0, type, c, out PlacementError ra);
                bool okB = b.World.CanPlace(0, type, c, out PlacementError rb);
                queries++;
                Assert.True(okA == okB && ra == rb, $"seed {seed}: type {type} cell {c}: with hidden {okA}/{ra}, without {okB}/{rb}");
                if (ra == PlacementError.UnitInTheWay) inTheWay++;
                if (ra == PlacementError.None && HiddenIn(a, hidden, type, c))
                {
                    hiddenOverlaps++;
                    okOverHidden++;
                }
            }
        // The sweep had teeth: seen enemies refused some spots, and hidden ones sat under spots the query allowed.
        Assert.True(inTheWay > 0, "no seen enemy was ever in the way");
        Assert.True(okOverHidden > 20, $"only {okOverHidden} allowed spots over a hidden enemy");
        // And the Build onto one of those spots is still dropped when it applies (nothing paid, no site).
        int placedOver = 0;
        for (int type = 0; type < TestSim.Data.Buildings.Length && placedOver == 0; type++)
            for (int c = 0; c < cells && placedOver == 0; c++)
                if (a.World.CanPlace(0, type, c, out _) && HiddenIn(a, hidden, type, c))
                {
                    placedOver++;
                    int count = a.World.Buildings.Count, gold = a.World.Gold[0], wood = a.World.Wood[0];
                    EntityHandle worker = FirstWorker(a);
                    var g = a.World.NavGrid;
                    a.Enqueue(Command.Build(0, worker, type, g.CellCenter(c % g.Width, c / g.Width)));
                    a.Tick();
                    a.Tick();
                    Assert.Equal((count, gold, wood), (a.World.Buildings.Count, a.World.Gold[0], a.World.Wood[0]));
                }
        Assert.Equal(1, placedOver);
        _ = queries + hiddenOverlaps;
    }

    private static EntityHandle FirstWorker(Simulation sim)
    {
        UnitStore u = sim.World.Units;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.Owner[i] == 0) return new EntityHandle(i, u.Generation[i]);
        throw new Xunit.Sdk.XunitException("no worker");
    }

    private static bool HiddenIn(Simulation sim, List<EntityHandle> hidden, int type, int anchor)
    {
        var def = TestSim.Data.Buildings[type];
        int w = sim.World.NavGrid.Width, x0 = anchor % w, y0 = anchor / w;
        foreach (EntityHandle h in hidden)
        {
            if (!sim.World.Units.IsAlive(h)) continue;
            Vector2 q = sim.World.Units.Position[h.Index] / Rts.Sim.Map.MapConstants.CellSize;
            if (q.X >= x0 && q.X < x0 + def.FootprintWidth && q.Y >= y0 && q.Y < y0 + def.FootprintHeight) return true;
        }
        return false;
    }
}
