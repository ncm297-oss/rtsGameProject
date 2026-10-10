using System.Numerics;
using Rts.Sim.Abilities;
using Rts.Sim.Entities;
using Rts.Sim.Vision;
using static Rts.Sim.Tests.AbilityScenes;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests;

/// <summary>
/// M4-4b-2 criterion 4: a Sandstorm (a zone that blocks vision) hides its cells from the other players' units outside it;
/// an enemy unit inside sees only its Blinded 2 m; the owner sees the whole zone; the hidden cells stay explored; and the
/// blocker is gone with the zone (docs/02 "Vision and fog of war": "hide their contents from enemies outside them").
/// </summary>
[Collection(SerialCollection.Name)]
public class ZoneVisionTests
{
    private static Rts.Sim.Data.AbilityDef Sandstorm => TestSim.Data.Abilities[TestSim.Data.FindAbility("sandstorm")];

    /// <summary>A 48 x 48 flat two-player map; <paramref name="combat"/> off unless asked.</summary>
    private static Simulation Scene(bool combat)
    {
        SimConfig c = combat
            ? TestSim.Config(Seed: 1, PlayerCount: 2, UnitCapacity: 64, CommandCapacity: 512)
            : TestSim.ConfigNoCombat(Seed: 1, PlayerCount: 2, UnitCapacity: 64, CommandCapacity: 512);
        return new Simulation(c, LocalMovementTests.Flat(48));
    }

    /// <summary>Runs ticks until a fog update has run after the current one (at most 4 ticks plus one).</summary>
    private static void NextUpdate(Simulation sim)
    {
        do sim.Tick();
        while (!VisionSystem.IsUpdateTick(sim.TickNumber - 1));
    }

    private static int CellOf(Simulation sim, Vector2 p) => sim.World.Fog.CellOf(p);

    [Fact]
    public void AnEnemyOutside_CannotSeeIntoTheStorm_TheOwnerSeesItAll_AndCellsStayExplored()
    {
        Simulation sim = Scene(combat: false);
        World w = sim.World;
        FogStore fog = w.Fog;
        Vector2 centre = At(sim, 24, 24);
        EntityHandle crossbow = Place(sim, 0, Crossbowman, Off(centre, -10f)); // sight 18
        EntityHandle raider = Place(sim, 1, Raider, centre);                    // sight 14: the whole zone
        NextUpdate(sim);
        Assert.True(fog.CanSeeUnit(0, raider.Index)); // before the storm
        ZoneSystem.Create(w, 1, Sandstorm, centre);
        NextUpdate(sim);
        Assert.False(fog.CanSeeUnit(0, raider.Index));
        int hidden = 0;
        for (int y = 20; y <= 28; y++)
            for (int x = 20; x <= 28; x++)
            {
                int c = y * fog.Width + x;
                Vector2 cc = At(sim, x, y);
                bool inZone = Vector2.DistanceSquared(cc, centre) <= 36f;
                Assert.True(fog.IsVisible(1, c), $"the owner doesn't see ({x}, {y})");
                if (inZone)
                {
                    hidden++;
                    Assert.False(fog.IsVisible(0, c), $"({x}, {y}) inside the storm is visible from outside");
                    Assert.True(fog.IsExplored(0, c), $"({x}, {y}) lost its explored state");
                    Assert.Equal(VisionConstants.Explored, fog.Visibility(0)[c]);
                }
                else if (Vector2.Distance(cc, w.Units.Position[crossbow.Index]) <= 18f)
                    Assert.True(fog.IsVisible(0, c), $"({x}, {y}) outside the storm is hidden");
            }
        Assert.Equal(29, hidden); // the cells whose centre is within 6 m: 3 cells each way, the edge cells included
        Assert.False(fog.IsVisible(0, 24 * fog.Width + 21)); // its centre exactly 6 m west: on the edge, inside, hidden
        Assert.True(fog.IsVisible(0, 24 * fog.Width + 20));  // 8 m west: outside
        Assert.False(fog.IsVisible(0, CellOf(sim, centre)));
    }

    [Fact]
    public void AnEnemyInside_SeesARaider1_5mAway_ButNotOne4mAway()
    {
        Simulation sim = Scene(combat: false);
        World w = sim.World;
        FogStore fog = w.Fog;
        Vector2 centre = At(sim, 24, 24);
        EntityHandle inside = Place(sim, 0, Crossbowman, Off(centre, 1.5f));
        EntityHandle outside = Place(sim, 0, Crossbowman, Off(centre, -10f));
        EntityHandle near = Place(sim, 1, Raider, centre);                  // 1.5 m from the Malazan unit inside
        EntityHandle far = Place(sim, 1, Raider, Off(centre, 5.5f));        // 4 m from it, inside the storm too
        ZoneSystem.Create(w, 1, Sandstorm, centre);
        sim.Tick(); // phase 5 Blinds the unit inside
        Assert.Equal(w.Data.FindStatus("blinded"), w.Units.Statuses.BlindOf(inside.Index));
        NextUpdate(sim);
        Assert.True(fog.CanSeeUnit(0, near.Index));
        Assert.False(fog.CanSeeUnit(0, far.Index));
        Assert.True(VisionSystem.UnitSeesUnit(w, inside.Index, near.Index));
        // The unit outside sees neither through its own eyes (both stand in the storm).
        Assert.False(VisionSystem.ZoneHides(w, 0, w.Units.Position[inside.Index], true, w.Units.Position[far.Index]));
        Assert.True(VisionSystem.ZoneHides(w, 0, w.Units.Position[outside.Index], true, w.Units.Position[far.Index]));
        Assert.True(VisionSystem.ZoneHides(w, 0, w.Units.Position[outside.Index], true, w.Units.Position[near.Index]));
        // The owner is never hidden from.
        Assert.False(VisionSystem.ZoneHides(w, 1, Off(centre, -20f), true, centre));
    }

    [Fact]
    public void ACrossbowman10mFromTheCentre_CannotAcquireARaiderInside_UntilTheStormEnds()
    {
        Simulation sim = Scene(combat: true);
        World w = sim.World;
        UnitStore u = w.Units;
        Vector2 centre = At(sim, 24, 24);
        EntityHandle crossbow = Place(sim, 0, Crossbowman, Off(centre, -10f)); // range 15, sight 18
        EntityHandle raider = Place(sim, 1, Raider, centre);
        u.Hold[raider.Index] = true; // it stays in the storm (and, melee, never reaches the Crossbowman)
        ZoneSystem.Create(w, 1, Sandstorm, centre);
        int hp = u.Hp[raider.Index];
        GatherMaps.Run(sim, 60);
        Assert.Equal(0, u.Target[crossbow.Index].Generation);
        Assert.Equal(hp, u.Hp[raider.Index]);
        Assert.False(w.Fog.CanSeeUnit(0, raider.Index));
        Assert.False(VisionSystem.UnitSeesUnit(w, crossbow.Index, raider.Index));
        // The zone ends (240 ticks from its making): no blocker effect afterwards; the Crossbowman takes the Raider.
        RunUntil(sim, () => w.Zones.Count == 0, 300);
        Assert.Equal(0, w.Zones.Count);
        NextUpdate(sim);
        Assert.True(w.Fog.CanSeeUnit(0, raider.Index));
        Assert.True(w.Fog.IsVisible(0, w.Fog.CellOf(centre)));
        RunUntil(sim, () => u.Target[crossbow.Index].Generation != 0, 20);
        Assert.Equal(raider, u.Target[crossbow.Index]);
    }

    /// <summary>The blocker follows the vision cadence: a zone made between updates hides nothing until the next update, and one freed leaves its cells hidden until then.</summary>
    [Fact]
    public void TheMask_IsStampedOnTheVisionCadence()
    {
        Simulation sim = Scene(combat: false);
        World w = sim.World;
        FogStore fog = w.Fog;
        Vector2 centre = At(sim, 24, 24);
        Place(sim, 0, Crossbowman, Off(centre, -10f));
        EntityHandle raider = Place(sim, 1, Raider, centre);
        NextUpdate(sim); // tick 1 updated
        ZoneSystem.Create(w, 1, Sandstorm, centre);
        sim.Tick(); // tick 2: no update
        Assert.True(fog.CanSeeUnit(0, raider.Index));
        NextUpdate(sim);
        Assert.False(fog.CanSeeUnit(0, raider.Index));
        w.Zones.Free(0); // a test seam: the zone ends between updates
        sim.Tick();
        Assert.False(fog.CanSeeUnit(0, raider.Index));
        NextUpdate(sim);
        Assert.True(fog.CanSeeUnit(0, raider.Index));
    }

    /// <summary>A building of the zone owner's enemy never views from inside: a tower can't take a unit in an enemy storm by its own sight.</summary>
    [Fact]
    public void ABuilding_NeverViewsFromInside()
    {
        Simulation sim = Scene(combat: false);
        World w = sim.World;
        Vector2 centre = At(sim, 24, 24);
        ZoneSystem.Create(w, 1, Sandstorm, centre);
        Assert.True(VisionSystem.ZoneHides(w, 0, centre, false, Off(centre, 1f)));
        Assert.False(VisionSystem.ZoneHides(w, 0, centre, true, Off(centre, 1f)));
    }
}
