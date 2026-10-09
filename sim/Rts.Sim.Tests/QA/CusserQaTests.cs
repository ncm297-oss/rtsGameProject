using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Xunit.Abstractions;
using static Rts.Sim.Tests.AbilityScenes;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M4-4b-1: the Sapper's Cusser on its friendly-fire and building paths (self-kill, footprint corner vs centre, a site
/// under construction, two Cussers on one building in a tick, a tech landing on the resolve tick) and a 0 B row for the
/// building scan.
/// </summary>
[Collection(SerialCollection.Name)]
public class CusserQaTests
{
    private readonly ITestOutputHelper _out;

    public CusserQaTests(ITestOutputHelper output) => _out = output;

    private static int Sapper => TestSim.Data.FindUnit("malazan_sapper");
    private static int Slot(EntityHandle h) => h.Index * DataLimits.MaxUnitAbilities;

    /// <summary>Casts the Cusser and ticks until it resolves, collecting every death event on the way.</summary>
    private static List<DeathEvent> CastAndResolve(Simulation sim, int player, EntityHandle sapper, Vector2 point)
    {
        sim.Enqueue(Command.UseAbility(player, sapper, 0, point));
        var deaths = new List<DeathEvent>();
        for (int t = 0; t < 60; t++)
        {
            sim.Tick();
            deaths.AddRange(sim.World.Deaths.ToArray());
            if (Had(sim, sapper, resolved: true)) return deaths;
        }
        throw new Xunit.Sdk.XunitException("the Cusser never resolved");
    }

    private static Simulation SelfKillScene(out EntityHandle sapper, out EntityHandle hall)
    {
        Simulation sim = NoFights();
        hall = GatherMaps.Building(sim, 14, 19, player: 1); // x 28-36, y 38-46
        sapper = Place(sim, 0, Sapper, new Vector2(26f, 42f)); // 2 m from the Keep's west edge
        sim.World.Units.Hp[sapper.Index] = 30; // its own half hit (60 - 1 = 59, x 0.5 = 29.5 -> 30) kills it
        return sim;
    }

    /// <summary>
    /// A Cusser thrown on the Sapper's own feet: the Sapper dies to its own half hit (killer and victim its owner, one kill
    /// and one loss), the enemy Keep beside it still takes the full 355 after the caster is gone, and twins hash-equal.
    /// </summary>
    [Fact]
    public void Cusser_OnItsOwnPosition_KillsTheSapper_CreditsItsOwner_StillHitsTheKeep_TwinsEqual()
    {
        Simulation a = SelfKillScene(out EntityHandle sapper, out EntityHandle hall);
        Simulation b = SelfKillScene(out _, out _);
        int hallHp = a.World.Buildings.Hp[hall.Index];
        Vector2 p = a.World.Units.Position[sapper.Index];
        a.Enqueue(Command.UseAbility(0, sapper, 0, p));
        b.Enqueue(Command.UseAbility(0, sapper, 0, p));
        var deaths = new List<DeathEvent>();
        for (int t = 0; t < 40; t++)
        {
            a.Tick();
            b.Tick();
            deaths.AddRange(a.World.Deaths.ToArray());
            Assert.Equal(a.StateHash(), b.StateHash());
        }
        DeathEvent d = Assert.Single(deaths);
        Assert.Equal((sapper, false, 0, 0), (d.Victim, d.IsBuilding, d.KillerOwner, d.VictimOwner));
        Assert.False(a.World.Units.IsAlive(sapper));
        Assert.Equal((1, 1, 0, 0), (a.World.Kills[0], a.World.Losses[0], a.World.Kills[1], a.World.Losses[1]));
        Assert.Equal(hallHp - 355, a.World.Buildings.Hp[hall.Index]);
        // The freed slot's cooldown doesn't leak into the next unit there.
        EntityHandle next = Place(a, 0, Sapper, new Vector2(10f, 10f));
        if (next.Index == sapper.Index) Assert.Equal(0, a.World.Units.AbilityReadyTick[Slot(next)]);
    }

    /// <summary>
    /// The building test is the footprint rectangle's nearest point (<see cref="CombatSystem.BuildingDistanceSquared"/>,
    /// the same Clamp rule splash and <c>VisionSystem.UnitSeesFootprint</c> use): 3.49 m diagonally off a corner hits,
    /// 3.51 m doesn't; a point inside the footprint (its centre) hits with no falloff.
    /// </summary>
    [Theory]
    [InlineData(3.49f, true)]
    [InlineData(3.51f, false)]
    [InlineData(-1f, true)] // the centre
    public void Cusser_FootprintCornerVsCentre(float cornerDistance, bool hit)
    {
        Simulation sim = NoFights();
        EntityHandle hall = GatherMaps.Building(sim, 14, 19, player: 1); // x 28-36, y 38-46
        int hp = sim.World.Buildings.Hp[hall.Index];
        Vector2 p = cornerDistance < 0 ? new Vector2(32f, 42f) : new Vector2(28f, 38f) - new Vector2(1f, 1f) * (cornerDistance / MathF.Sqrt(2f));
        Assert.Equal(cornerDistance < 0 ? 0f : cornerDistance * cornerDistance, CombatSystem.BuildingDistanceSquared(sim.World, hall.Index, p), 3);
        EntityHandle sapper = Place(sim, 0, Sapper, cornerDistance < 0 ? new Vector2(26.5f, 42f) : p - new Vector2(4.5f, 0f));
        CastAndResolve(sim, 0, sapper, p);
        Assert.Equal(hit ? hp - 355 : hp, sim.World.Buildings.Hp[hall.Index]);
    }

    /// <summary>
    /// A site under construction takes the hit like a finished building (355) and keeps it when work goes on (BUG-0138:
    /// the work adds hit points, it doesn't recompute them); a fresh 1 hp site falls by the normal path (one death, the
    /// owner's loss, no population change since a site provides none, its slot freed).
    /// </summary>
    [Fact]
    public void Cusser_OnASiteUnderConstruction_DamageStaysThroughWork_AndAFreshSiteFalls()
    {
        Simulation sim = NoFights();
        World w = sim.World;
        BuildingStore b = w.Buildings;
        int keep = GatherMaps.Keep;
        int width = w.NavGrid.Width;
        Assert.True(b.Spawn(1, keep, 19 * width + 14, out EntityHandle site, site: true));
        int needed = b.WorkNeeded(keep), max = w.Data.Buildings[keep].Hp;
        b.SetWork(site.Index, needed / 2);
        Assert.True(b.UnderConstruction[site.Index]);
        int before = b.Hp[site.Index];
        Assert.Equal(max / 2, before);
        EntityHandle sapper = Place(sim, 0, Sapper, new Vector2(21f, 42f));
        CastAndResolve(sim, 0, sapper, new Vector2(25f, 42f)); // 3 m west of the site's edge
        Assert.Equal(before - 355, b.Hp[site.Index]);
        b.SetWork(site.Index, needed / 2 + needed / 10);
        Assert.Equal(before - 355 + max / 10, b.Hp[site.Index]); // damage stays taken

        // A fresh site (1 hp) on the far side.
        Assert.True(b.Spawn(1, keep, 5 * width + 30, out EntityHandle fresh, site: true)); // x 60-68, y 10-18
        Assert.Equal(1, b.Hp[fresh.Index]);
        int count = b.Count, cap = w.HalfPopCap[1];
        EntityHandle s2 = Place(sim, 0, Sapper, new Vector2(54f, 14f));
        List<DeathEvent> deaths = CastAndResolve(sim, 0, s2, new Vector2(58f, 14f));
        DeathEvent d = Assert.Single(deaths, x => x.IsBuilding);
        Assert.Equal((fresh, 0, 1), (d.Victim, d.KillerOwner, d.VictimOwner));
        Assert.False(b.IsAlive(fresh));
        Assert.Equal(count - 1, b.Count);
        Assert.Equal(cap, w.HalfPopCap[1]);
        Assert.Equal(1, w.Losses[1]);
    }

    /// <summary>
    /// Two Cussers resolving in the same tick on a Keep with 400 hp: the first brings it to 0 and frees it, the second
    /// skips the dead slot. One death event, one kill, one fewer building, nothing thrown.
    /// </summary>
    [Fact]
    public void TwoCussers_KillOneBuilding_InTheSameTick_OneDeath_NoDoubleFree()
    {
        Simulation sim = NoFights();
        World w = sim.World;
        BuildingStore b = w.Buildings;
        EntityHandle hall = GatherMaps.Building(sim, 14, 19, player: 1);
        b.Damage(hall, b.Hp[hall.Index] - 400);
        EntityHandle s1 = Place(sim, 0, Sapper, new Vector2(21f, 40f));
        EntityHandle s2 = Place(sim, 0, Sapper, new Vector2(21f, 44f));
        Vector2 p = new(25f, 42f);
        int count = b.Count;
        sim.Enqueue(Command.UseAbility(0, s1, 0, p));
        sim.Enqueue(Command.UseAbility(0, s2, 0, p));
        var deaths = new List<DeathEvent>();
        int resolves = 0;
        for (int t = 0; t < 40; t++)
        {
            sim.Tick();
            deaths.AddRange(w.Deaths.ToArray());
            foreach (Abilities.AbilityEvent e in w.AbilityEvents)
                if (e.Resolved) resolves++;
        }
        Assert.Equal(2, resolves);
        DeathEvent d = Assert.Single(deaths);
        Assert.Equal((hall, true, 0, 1), (d.Victim, d.IsBuilding, d.KillerOwner, d.VictimOwner));
        Assert.Equal(count - 1, b.Count);
        Assert.Equal((1, 1), (w.Kills[0], w.Losses[1]));
        Assert.Equal(70, w.Units.Hp[s1.Index]); // 4 m from the point: no friendly fire
    }

    /// <summary>
    /// Moranth Supply completing in the resolve tick (production, phase 3, runs before abilities, phase 6; set here between
    /// the ticks, which is the same state at phase 6) shortens that resolve's cooldown: 600, not 900.
    /// </summary>
    [Fact]
    public void ATechLandingOnTheResolveTick_CountsForThatCooldown()
    {
        GameData d = TestSim.Data;
        Simulation sim = NoFights();
        EntityHandle s = Place(sim, 0, Sapper, new Vector2(20f, 20f));
        sim.Enqueue(Command.UseAbility(0, s, 0, new Vector2(24f, 20f)));
        int start = TickOf(sim, s, resolved: false);
        while (sim.TickNumber < start + 19) sim.Tick(); // the resolve tick is start + 19 (20 ticks counting the start)
        sim.World.Techs.Set(0, d.FindTech("moranth_supply"), true);
        sim.Tick();
        Assert.True(Had(sim, s, resolved: true));
        Assert.Equal(sim.TickNumber - 1 + 600, sim.World.Units.AbilityReadyTick[Slot(s)]);
    }

    /// <summary>
    /// The Cusser's building scan allocates nothing: 10 Sappers throwing at 12 enemy Keeps (each in reach of a cast) and
    /// their own units, every resolve walking every building slot.
    /// </summary>
    [Fact]
    public void CusserResolves_WithTheBuildingScan_AllocateNothing()
    {
        Simulation sim = TestSim.Explored(new Simulation(TestSim.ConfigNoCombat(Seed: 1, PlayerCount: 2, UnitCapacity: 64, CommandCapacity: 512), LocalMovementTests.Flat(96)));
        World w = sim.World;
        var sappers = new EntityHandle[10];
        var halls = new List<EntityHandle>();
        for (int k = 0; k < 12; k++) halls.Add(GatherMaps.Building(sim, 6 + 7 * (k % 6), 10 + 20 * (k / 6), player: 1));
        for (int k = 0; k < sappers.Length; k++)
            sappers[k] = Place(sim, 0, Sapper, new Vector2(16f + 14f * (k % 5), 32f + 40f * (k / 5)));
        for (int k = 0; k < 10; k++) Place(sim, 0, Crossbowman, new Vector2(16f + 14f * (k % 5), 30f + 40f * (k / 5)));
        int resolves = 0;
        void Setup()
        {
            foreach (EntityHandle h in halls) w.Buildings.SetHp(h.Index, w.Data.Buildings[GatherMaps.Keep].Hp);
            for (int i = 0; i < w.Units.Capacity; i++)
                if (w.Units.Alive[i]) w.Units.Hp[i] = 1000;
            foreach (EntityHandle s in sappers)
            {
                w.Units.AbilityReadyTick[Slot(s)] = 0;
                sim.Enqueue(Command.UseAbility(0, s, 0, w.Units.Position[s.Index] + new Vector2(0f, -4f)));
            }
        }
        Setup();
        for (int t = 0; t < 25; t++) sim.Tick(); // warm-up round
        int hpBefore = w.Buildings.Hp[halls[0].Index];
        AllocationProbe.AssertZero(() =>
        {
            for (int t = 0; t < 25; t++)
            {
                sim.Tick();
                foreach (Abilities.AbilityEvent e in w.AbilityEvents) if (e.Resolved) resolves++;
            }
        }, _out, setup: Setup);
        int hit = 0;
        foreach (EntityHandle h in halls) if (w.Buildings.Hp[h.Index] < w.Data.Buildings[GatherMaps.Keep].Hp) hit++;
        _out.WriteLine($"resolves {resolves}, keeps hit {hit}, first keep before {hpBefore}");
        Assert.True(resolves >= sappers.Length, $"only {resolves} resolves");
        Assert.True(hit >= 5, $"only {hit} keeps hit");
    }

    /// <summary>
    /// Every unit slot dies in one tick and a building with them: the tick's death list holds units + buildings. A world
    /// with 2 unit slots: the Sapper kills itself, the enemy Crossbowman and the enemy Keep in one resolve (3 deaths).
    /// </summary>
    [Fact(Skip = "BUG-0330: the death list holds UnitCapacity events; units + buildings dying in one tick overflow it")]
    public void ACusserKillingEveryUnitSlotAndABuilding_InOneTick_DoesNotOverflowTheDeathList()
    {
        Simulation sim = NoFights(units: 2);
        World w = sim.World;
        EntityHandle hall = GatherMaps.Building(sim, 14, 19, player: 1);
        w.Buildings.Damage(hall, w.Buildings.Hp[hall.Index] - 100);
        EntityHandle sapper = Place(sim, 0, Sapper, new Vector2(26f, 42f));
        w.Units.Hp[sapper.Index] = 30;
        Place(sim, 1, Crossbowman, new Vector2(26f, 43f));
        List<DeathEvent> deaths = CastAndResolve(sim, 0, sapper, new Vector2(26f, 42f));
        Assert.Equal(3, deaths.Count);
    }
}
