using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using static Rts.Sim.Tests.AbilityScenes;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests;

/// <summary>M4-4a criterion 3: a Cadre Mage casting Telas Fire (docs/factions/malazan.md "Abilities") through <c>Command.UseAbility</c>.</summary>
[Collection(SerialCollection.Name)]
public class AbilitySystemTests
{
    private static int Slot(EntityHandle h) => h.Index * DataLimits.MaxUnitAbilities;

    /// <summary>In range: the mage stands 0.8 s (16 ticks), then every enemy unit within 3 m Burns for 4 x 10 magic damage; its own units and every building are untouched; the cooldown is 25 s from the resolve.</summary>
    [Fact]
    public void InRange_Stands16Ticks_ThenEnemiesWithin3mBurn40_OwnAndBuildingsUntouched()
    {
        Simulation sim = NoFights();
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle mage = Place(sim, 0, Mage, At(sim, 10, 20));
        Vector2 point = At(sim, 17, 20); // 14 m away
        EntityHandle hit = Place(sim, 1, Crossbowman, point);
        EntityHandle edge = Place(sim, 1, Crossbowman, Off(point, 0f, 2.9f));
        EntityHandle outside = Place(sim, 1, Crossbowman, Off(point, 0f, -3.2f));
        EntityHandle heavy = Place(sim, 1, HeavyInfantry, Off(point, -2f)); // Heavy armor 3: magic x1.25, armor ignored
        EntityHandle own = Place(sim, 0, Crossbowman, Off(point, 1f));
        EntityHandle hall = GatherMaps.Building(sim, 18, 21, player: 1); // footprint within 3 m of the point
        int hallHp = w.Buildings.Hp[hall.Index];
        sim.Enqueue(Command.UseAbility(0, mage, 0, point));
        int start = TickOf(sim, mage, resolved: false);
        Assert.Equal(UnitState.Casting, u.State[mage.Index]);
        Assert.Equal(At(sim, 10, 20), u.Position[mage.Index]);
        int resolve = TickOf(sim, mage, resolved: true);
        Assert.Equal(16, resolve - start + 1); // 0.8 s, counting the tick it started
        Assert.Equal(At(sim, 10, 20), u.Position[mage.Index]); // it stood still
        Assert.Equal(UnitState.Idle, u.State[mage.Index]);
        Assert.Equal(-1, u.CastAbility[mage.Index]);
        Assert.Equal(resolve + 500, u.AbilityReadyTick[Slot(mage)]); // 25 s from the resolve
        foreach (EntityHandle h in new[] { hit, edge, heavy })
            Assert.Equal((10f, 80), StatusOf(sim, h, Burning));
        Assert.Equal((0f, 0), StatusOf(sim, outside, Burning));
        Assert.Equal((0f, 0), StatusOf(sim, own, Burning));
        Assert.Equal(0, u.Statuses.Count[mage.Index]);
        GatherMaps.Run(sim, 80);
        Assert.Equal(55 - 40, u.Hp[hit.Index]);
        Assert.Equal(55 - 40, u.Hp[edge.Index]);
        Assert.Equal(130 - 4 * 13, u.Hp[heavy.Index]); // 10 x 1.25 = 12.5 -> 13 a second
        Assert.Equal(55, u.Hp[outside.Index]);
        Assert.Equal(55, u.Hp[own.Index]);
        Assert.Equal(hallHp, w.Buildings.Hp[hall.Index]);
        Assert.Equal(0, u.Statuses.Count[hit.Index]); // expired
        GatherMaps.Run(sim, 40);
        Assert.Equal(55 - 40, u.Hp[hit.Index]);
    }

    /// <summary>Burning lands one pulse a second, the first a second after the resolve.</summary>
    [Fact]
    public void Burning_LandsAPulseEverySecond_TheFirstASecondAfterTheResolve()
    {
        Simulation sim = NoFights();
        UnitStore u = sim.World.Units;
        EntityHandle mage = Place(sim, 0, Mage, At(sim, 10, 20));
        EntityHandle target = Place(sim, 1, Crossbowman, At(sim, 14, 20));
        sim.Enqueue(Command.UseAbility(0, mage, 0, At(sim, 14, 20)));
        TickOf(sim, mage, resolved: true);
        var hp = new List<int>();
        for (int t = 0; t < 90; t++)
        {
            sim.Tick();
            hp.Add(u.Hp[target.Index]);
        }
        Assert.Equal(55, hp[18]);
        Assert.Equal(45, hp[19]); // 20 ticks after the resolve
        Assert.Equal(45, hp[38]);
        Assert.Equal(35, hp[39]);
        Assert.Equal(15, hp[79]);
        Assert.Equal(15, hp[89]);
    }

    /// <summary>Out of range: the mage walks until in range (16 m), then casts.</summary>
    [Fact]
    public void OutOfRange_WalksFirst_ThenCastsFromWithinRange()
    {
        Simulation sim = NoFights();
        UnitStore u = sim.World.Units;
        EntityHandle mage = Place(sim, 0, Mage, At(sim, 4, 20));
        Vector2 point = At(sim, 30, 20); // 52 m away
        EntityHandle target = Place(sim, 1, Crossbowman, point);
        sim.Enqueue(Command.UseAbility(0, mage, 0, point));
        sim.Tick();
        sim.Tick();
        Assert.Equal(UnitState.Moving, u.State[mage.Index]);
        Assert.Equal(0, u.CastAbility[mage.Index]);
        TickOf(sim, mage, resolved: false);
        float d = Vector2.Distance(u.Position[mage.Index], point);
        Assert.True(d <= 16f && d > 14f, $"cast started {d} m from the point");
        TickOf(sim, mage, resolved: true);
        Assert.Equal((10f, 80), StatusOf(sim, target, Burning));
    }

    /// <summary>On cooldown the command is dropped; 25 s after the resolve it is taken again.</summary>
    [Fact]
    public void OnCooldown_IsDropped_UntilTheReadyTick()
    {
        Simulation sim = NoFights();
        UnitStore u = sim.World.Units;
        EntityHandle mage = Place(sim, 0, Mage, At(sim, 10, 20));
        Vector2 point = At(sim, 14, 20);
        sim.Enqueue(Command.UseAbility(0, mage, 0, point));
        int resolve = TickOf(sim, mage, resolved: true);
        // Applies on tick resolve + 2: dropped.
        sim.Enqueue(Command.UseAbility(0, mage, 0, point));
        sim.Tick();
        sim.Tick();
        Assert.Equal(-1, u.CastAbility[mage.Index]);
        Assert.Equal(UnitState.Idle, u.State[mage.Index]);
        // A queued one is dropped too.
        sim.Enqueue(Command.UseAbility(0, mage, 0, point, queued: true));
        sim.Tick();
        sim.Tick();
        Assert.Equal(0, u.QueueCount[mage.Index]);
        // The rule a command applying on tick t meets: refused on resolve + 499, taken from resolve + 500.
        while (sim.TickNumber < resolve + 499) sim.Tick();
        Assert.False(Rts.Sim.Abilities.AbilitySystem.CanUse(sim.World, mage.Index, 0, point));
        sim.Tick();
        Assert.True(Rts.Sim.Abilities.AbilitySystem.CanUse(sim.World, mage.Index, 0, point));
        sim.Enqueue(Command.UseAbility(0, mage, 0, point));
        sim.Tick();
        sim.Tick();
        Assert.Equal(0, u.CastAbility[mage.Index]);
        Assert.Equal(UnitState.Casting, u.State[mage.Index]);
    }

    /// <summary>A Move during the cast cancels it: no resolve, no cooldown, and the ability is usable at once.</summary>
    [Fact]
    public void AMoveMidCast_Cancels_WithNoCooldown()
    {
        Simulation sim = NoFights();
        UnitStore u = sim.World.Units;
        EntityHandle mage = Place(sim, 0, Mage, At(sim, 10, 20));
        EntityHandle target = Place(sim, 1, Crossbowman, At(sim, 14, 20));
        sim.Enqueue(Command.UseAbility(0, mage, 0, At(sim, 14, 20)));
        TickOf(sim, mage, resolved: false);
        GatherMaps.Run(sim, 5);
        sim.Enqueue(Command.Move(0, mage, At(sim, 10, 30)));
        sim.Tick();
        sim.Tick();
        Assert.Equal(-1, u.CastAbility[mage.Index]);
        Assert.Equal(UnitState.Moving, u.State[mage.Index]);
        Assert.Equal(0, u.AbilityReadyTick[Slot(mage)]);
        GatherMaps.Run(sim, 30);
        Assert.Equal(0, u.Statuses.Count[target.Index]);
        sim.Enqueue(Command.UseAbility(0, mage, 0, u.Position[mage.Index]));
        sim.Tick();
        sim.Tick();
        Assert.Equal(UnitState.Casting, u.State[mage.Index]);
        // Stop and HoldPosition cancel too.
        sim.Enqueue(Command.Stop(0, mage));
        sim.Tick();
        sim.Tick();
        Assert.Equal((-1, UnitState.Idle, 0), (u.CastAbility[mage.Index], u.State[mage.Index], u.AbilityReadyTick[Slot(mage)]));
    }

    /// <summary>A queued UseAbility waits for the orders before it; orders queued after a cast wait for its resolve.</summary>
    [Fact]
    public void Queued_CastsAfterTheMove_AndTheNextQueuedOrderWaitsForTheResolve()
    {
        Simulation sim = NoFights();
        UnitStore u = sim.World.Units;
        EntityHandle mage = Place(sim, 0, Mage, At(sim, 10, 20));
        EntityHandle target = Place(sim, 1, Crossbowman, At(sim, 14, 22));
        sim.Enqueue(Command.Move(0, mage, At(sim, 10, 26)));
        sim.Enqueue(Command.UseAbility(0, mage, 0, At(sim, 14, 22), queued: true));
        sim.Enqueue(Command.Move(0, mage, At(sim, 10, 14), queued: true));
        int start = TickOf(sim, mage, resolved: false);
        Assert.True(Vector2.Distance(u.Position[mage.Index], At(sim, 10, 26)) < 1.5f, "cast before arriving");
        Assert.Equal(1, u.QueueCount[mage.Index]);
        int resolve = TickOf(sim, mage, resolved: true);
        Assert.True(resolve - start >= 15);
        Assert.Equal((10f, 80), StatusOf(sim, target, Burning));
        Assert.Equal(0, u.QueueCount[mage.Index]);
        Assert.Equal(UnitState.Moving, u.State[mage.Index]);
    }

    /// <summary>Dropped like a bad Gather: a dead caster, another player's, an ability its type lacks, a point off the map.</summary>
    [Fact]
    public void Drops_DeadCaster_NotOwned_NoSuchAbility_OffMap()
    {
        Simulation sim = NoFights();
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle mage = Place(sim, 0, Mage, At(sim, 10, 20));
        EntityHandle bowman = Place(sim, 0, Crossbowman, At(sim, 10, 24));
        EntityHandle dead = Place(sim, 0, Mage, At(sim, 10, 16));
        u.Free(dead);
        Vector2 point = At(sim, 14, 20);
        sim.Enqueue(Command.UseAbility(1, mage, 0, point));            // not the player's
        sim.Enqueue(Command.UseAbility(0, mage, 1, point));            // no ability 1
        sim.Enqueue(Command.UseAbility(0, mage, -1, point));           // no ability -1
        sim.Enqueue(Command.UseAbility(0, bowman, 0, point));          // a type with none
        sim.Enqueue(Command.UseAbility(0, dead, 0, point));            // dead
        sim.Enqueue(Command.UseAbility(0, mage, 0, new Vector2(-5f, 20f))); // off the map
        sim.Enqueue(Command.UseAbility(0, mage, 0, new Vector2(float.NaN, 20f)));
        sim.Tick();
        sim.Tick();
        Assert.Equal(-1, u.CastAbility[mage.Index]);
        Assert.Equal(-1, u.CastAbility[bowman.Index]);
        Assert.Equal(UnitState.Idle, u.State[mage.Index]);
        Assert.Equal(0, w.AbilityEvents.Length);
        Assert.Equal(0, u.QueueCount[mage.Index]);
        Assert.Equal(At(sim, 10, 20), u.Position[mage.Index]);
    }

    /// <summary>A Burning pulse that kills credits the caster's owner with the kill.</summary>
    [Fact]
    public void ABurningKill_IsCreditedToTheCastersOwner()
    {
        Simulation sim = NoFights();
        World w = sim.World;
        EntityHandle mage = Place(sim, 0, Mage, At(sim, 10, 20));
        EntityHandle target = Place(sim, 1, Crossbowman, At(sim, 14, 20));
        w.Units.Hp[target.Index] = 15; // two pulses
        sim.Enqueue(Command.UseAbility(0, mage, 0, At(sim, 14, 20)));
        TickOf(sim, mage, resolved: true);
        bool died = false;
        for (int t = 0; t < 60 && !died; t++)
        {
            sim.Tick();
            foreach (Combat.DeathEvent d in w.Deaths)
                if (d.Victim == target) { died = true; Assert.Equal((0, 1), (d.KillerOwner, d.VictimOwner)); }
        }
        Assert.True(died);
        Assert.Equal(1, w.Kills[0]);
        Assert.Equal(1, w.Losses[1]);
        Assert.False(w.Units.IsAlive(target));
        Assert.Equal(0, w.Units.Statuses.Count[target.Index]);
    }

    /// <summary>The caster dying mid-cast ends the cast: nothing resolves.</summary>
    [Fact]
    public void ACasterKilledMidCast_ResolvesNothing()
    {
        Simulation sim = NoFights();
        UnitStore u = sim.World.Units;
        EntityHandle mage = Place(sim, 0, Mage, At(sim, 10, 20));
        EntityHandle target = Place(sim, 1, Crossbowman, At(sim, 14, 20));
        sim.Enqueue(Command.UseAbility(0, mage, 0, At(sim, 14, 20)));
        TickOf(sim, mage, resolved: false);
        u.Free(mage);
        GatherMaps.Run(sim, 40);
        Assert.Equal(0, u.Statuses.Count[target.Index]);
    }

    // M4-4b-1 criterion 3: the Sapper's Cusser (docs/factions/malazan.md "Abilities": 6 / 3.5 m / 1.0 s / 45 s, 120 siege,
    // full damage to buildings, friendly fire at 50 %).

    private static int Sapper => TestSim.Data.FindUnit("malazan_sapper");

    /// <summary>The Cusser's point, 3.4 m west of an enemy Keep's footprint (anchor 14, 19: x 28-36 m, y 38-46 m).</summary>
    private static readonly Vector2 CusserPoint = new(24.6f, 42f);

    /// <summary>
    /// 3.4 m from an enemy Keep's footprint: the Keep takes 120 x 3.0 - 5 = 355; an enemy Light unit 60 - 0; an own Heavy
    /// unit half of 90 - 3 (43.5 rounds to 44); an own Keep 2 m away nothing; units 3.51 m out nothing; the Sapper 4 m away
    /// nothing; a Light unit it kills is the caster owner's kill. Cast 1.0 s, cooldown 45 s.
    /// </summary>
    [Fact]
    public void Cusser_HitsEnemyBuildingsAndUnits_OwnUnitsAtHalf_NeverOwnBuildings_NothingPastTheRadius()
    {
        Simulation sim = NoFights();
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle hall = GatherMaps.Building(sim, 14, 19, player: 1);
        EntityHandle ownHall = GatherMaps.Building(sim, 8, 22, player: 0); // x 16-24, y 44-52: 2.1 m from the point
        int hallHp = w.Buildings.Hp[hall.Index], ownHallHp = w.Buildings.Hp[ownHall.Index];
        Vector2 p = CusserPoint;
        EntityHandle sapper = Place(sim, 0, Sapper, Off(p, -4f));
        EntityHandle light = Place(sim, 1, Crossbowman, Off(p, 0f, -2f));
        u.Hp[light.Index] = 200;
        EntityHandle doomed = Place(sim, 1, Crossbowman, Off(p, 1.5f, 1.5f)); // 55 hp: 60 kills it
        EntityHandle heavy = Place(sim, 0, HeavyInfantry, Off(p, -2f, -1f));
        EntityHandle outEnemy = Place(sim, 1, Crossbowman, Off(p, 0f, -3.51f));
        EntityHandle outOwn = Place(sim, 0, Crossbowman, Off(p, 0f, 3.51f));
        sim.Enqueue(Command.UseAbility(0, sapper, 0, p));
        int start = TickOf(sim, sapper, resolved: false);
        bool doomedDied = false;
        int resolve = -1;
        for (int t = 0; t < 40 && resolve < 0; t++)
        {
            sim.Tick();
            if (Had(sim, sapper, resolved: true)) resolve = sim.TickNumber - 1;
            foreach (Combat.DeathEvent d in w.Deaths)
                if (d.Victim == doomed) { doomedDied = true; Assert.Equal((0, 1, false), (d.KillerOwner, d.VictimOwner, d.IsBuilding)); }
        }
        Assert.Equal(20, resolve - start + 1); // 1.0 s
        Assert.Equal(resolve + 900, u.AbilityReadyTick[Slot(sapper)]); // 45 s
        Assert.Equal(hallHp - 355, w.Buildings.Hp[hall.Index]);
        Assert.Equal(200 - 60, u.Hp[light.Index]);
        Assert.True(doomedDied);
        Assert.False(u.IsAlive(doomed));
        Assert.Equal(1, w.Kills[0]);
        Assert.Equal(130 - 44, u.Hp[heavy.Index]);
        Assert.Equal(ownHallHp, w.Buildings.Hp[ownHall.Index]);
        Assert.Equal(55, u.Hp[outEnemy.Index]);
        Assert.Equal(55, u.Hp[outOwn.Index]);
        Assert.Equal(70, u.Hp[sapper.Index]);
    }

    /// <summary>
    /// Cast 2 m from itself the Sapper takes half its own hit (60 - 1 = 59, half 29.5 rounds to 30); an own unit it kills is
    /// the caster owner's kill and loss (the splash convention); an enemy Keep brought to 0 is destroyed the normal way.
    /// </summary>
    [Fact]
    public void Cusser_HurtsTheSapperItself_CreditsAnOwnKillToItsOwner_AndDestroysABuildingAt0()
    {
        Simulation sim = NoFights();
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle hall = GatherMaps.Building(sim, 14, 19, player: 1);
        w.Buildings.Damage(hall, w.Buildings.Hp[hall.Index] - 300); // 300 left: 355 destroys it
        Vector2 p = CusserPoint;
        EntityHandle sapper = Place(sim, 0, Sapper, Off(p, -2f));
        EntityHandle own = Place(sim, 0, Crossbowman, Off(p, 0f, -2f));
        u.Hp[own.Index] = 10;
        int navVersion = w.NavGrid.Version;
        sim.Enqueue(Command.UseAbility(0, sapper, 0, p));
        var deaths = new List<Combat.DeathEvent>();
        for (int t = 0; t < 40; t++)
        {
            sim.Tick();
            deaths.AddRange(w.Deaths.ToArray());
            if (Had(sim, sapper, resolved: true)) break;
        }
        Assert.Equal(70 - 30, u.Hp[sapper.Index]);
        Assert.Contains(deaths, d => d.Victim == own && !d.IsBuilding && d.KillerOwner == 0 && d.VictimOwner == 0);
        Assert.Contains(deaths, d => d.Victim == hall && d.IsBuilding && d.KillerOwner == 0 && d.VictimOwner == 1);
        Assert.False(w.Buildings.IsAlive(hall));
        Assert.NotEqual(navVersion, w.NavGrid.Version); // its cells reopened
        Assert.Equal((2, 1), (w.Kills[0], w.Losses[0])); // the Keep and its own Crossbowman; the Crossbowman
        Assert.Equal(1, w.Losses[1]);
    }
}
