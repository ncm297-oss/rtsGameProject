using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Abilities;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using static Rts.Sim.Tests.AbilityScenes;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests;

/// <summary>
/// M4-4b-3b criterion 4: the <c>summon</c> kind and the <c>spawn</c> effect (docs/02 "Ability system": Summon "Spawns
/// temporary units"; docs/factions/shadow.md's Summon Wraiths is the reference). Cast like a self / aura ability; at the
/// resolve the units appear on free cells round the caster, free and at full hit points, and are taken away
/// <c>lifetime</c> ticks later through the death path as an expiry (no kill, no loss, no <c>onDeath</c>). Test-only abilities.
/// </summary>
public class SummonTests
{
    /// <summary>
    /// Test data: the Malazan Cadre Mage has <c>zz_test_summon</c> as ability 1 (3 Heavy Infantry for 20 s; 1.0 s cast, 50 s
    /// cooldown: Summon Wraiths' numbers) and <c>zz_test_summon_brief</c> as ability 2 (3 Heavy Infantry for 2 s; 0.5 s cast,
    /// 3 s cooldown: for the cost rows), and the Whirlwind Priest <c>zz_test_summon_zealots</c> as ability 1 (3 Zealots for
    /// 5 s; 0.5 s cast, 10 s cooldown).
    /// </summary>
    internal static GameData Data => s_data.Value;

    private static readonly Lazy<GameData> s_data = new(() =>
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        AddTo(dir);
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join("; ", r.Errors));
        return r.Data!;
    });

    /// <summary>Adds the three test-only summons to a data copy and lists them on the Cadre Mage (abilities 1 and 2) and the Priest (ability 1).</summary>
    internal static void AddTo(TestDataDir dir)
    {
        dir.EditJson("factions/malazan/abilities.json", root => root["abilities"]!.AsArray().Add(new JsonObject
        {
            ["id"] = "zz_test_summon", ["displayName"] = "Test Summon", ["description"] = "Test only.", ["kind"] = "summon",
            ["castTime"] = 1.0, ["cooldown"] = 50, ["affects"] = "own_units", ["autocast"] = false,
            ["effects"] = new JsonArray(new JsonObject { ["kind"] = "spawn", ["unit"] = "malazan_heavy_infantry", ["count"] = 3, ["lifetime"] = 20 }),
        }));
        dir.EditJson("factions/malazan/abilities.json", root => root["abilities"]!.AsArray().Add(new JsonObject
        {
            ["id"] = "zz_test_summon_brief", ["displayName"] = "Test Brief Summon", ["description"] = "Test only.", ["kind"] = "summon",
            ["castTime"] = 0.5, ["cooldown"] = 3, ["affects"] = "own_units", ["autocast"] = false,
            ["effects"] = new JsonArray(new JsonObject { ["kind"] = "spawn", ["unit"] = "malazan_heavy_infantry", ["count"] = 3, ["lifetime"] = 2 }),
        }));
        dir.EditJson("factions/whirlwind/abilities.json", root => root["abilities"]!.AsArray().Add(new JsonObject
        {
            ["id"] = "zz_test_summon_zealots", ["displayName"] = "Test Zealots", ["description"] = "Test only.", ["kind"] = "summon",
            ["castTime"] = 0.5, ["cooldown"] = 10, ["affects"] = "own_units", ["autocast"] = false,
            ["effects"] = new JsonArray(new JsonObject { ["kind"] = "spawn", ["unit"] = "whirlwind_zealot", ["count"] = 3, ["lifetime"] = 5 }),
        }));
        dir.SetUnitField("malazan", "malazan_cadre_mage", "abilities", "[\"telas_fire\", \"zz_test_summon\", \"zz_test_summon_brief\"]");
        dir.SetUnitField("whirlwind", "whirlwind_priest", "abilities", "[\"sandstorm\", \"zz_test_summon_zealots\"]");
    }

    internal static Simulation Sim(int units = 64, bool combat = false) =>
        TestSim.Explored(new Simulation(new SimConfig(1, 2, units, 512) { Data = Data, Combat = combat }, LocalMovementTests.Flat(48)));

    private static int MageType => Data.FindUnit("malazan_cadre_mage");

    private static int PriestType => Data.FindUnit("whirlwind_priest");

    private static int Infantry => Data.FindUnit("malazan_heavy_infantry");

    private static int ZealotType => Data.FindUnit("whirlwind_zealot");

    /// <summary>The live summoned units, slot order.</summary>
    internal static List<EntityHandle> Summoned(World w)
    {
        UnitStore u = w.Units;
        var list = new List<EntityHandle>();
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.IsSummoned(i)) list.Add(new EntityHandle(i, u.Generation[i]));
        return list;
    }

    private static (int X, int Y) CellOf(Simulation sim, EntityHandle h)
    {
        Assert.True(sim.World.NavGrid.WorldToCell(sim.World.Units.Position[h.Index], out int x, out int y));
        return (x, y);
    }

    [Fact]
    public void TheTestSummons_LoadAsSummon()
    {
        AbilityDef s = Data.Abilities[Data.FindAbility("zz_test_summon")];
        Assert.Equal((AbilityKind.Summon, 0f, 0f, 20, 1000), (s.Kind, s.Range, s.Radius, s.CastTicks, s.CooldownTicks));
        AbilityEffect e = Assert.Single(s.Effects);
        Assert.Equal((AbilityEffectKind.Spawn, Infantry, 3, 400), (e.Kind, e.Unit, e.Count, e.LifetimeTicks));
        Assert.Equal(s.Id, Data.Units[MageType].Abilities[1]);
        foreach (AbilityDef a in TestSim.Data.Abilities) Assert.NotEqual(AbilityKind.Summon, a.Kind); // none shipped
    }

    /// <summary>
    /// Nothing appears when the cast starts; at the resolve three Heavy Infantry of the caster's owner appear on the three
    /// nearest free cells next to the caster's cell (the occupied one skipped; ties to the lower cell), at full hit points,
    /// taking no population and no gold or wood, each ending 400 ticks after the resolve. The command's point is ignored and
    /// the caster never walks.
    /// </summary>
    [Fact]
    public void ThreeUnitsAppearAtTheResolve_OnTheNearestFreeCells_FreeAndAtFullHp()
    {
        Simulation sim = Sim();
        World w = sim.World;
        UnitStore u = w.Units;
        Vector2 p = At(sim, 20, 20);
        EntityHandle mage = Place(sim, 0, MageType, p);
        Place(sim, 0, Infantry, At(sim, 20, 19)); // the first cell in ring order: taken
        sim.Tick();
        int count = u.Count, halfPop = w.HalfPop[0], gold = w.Gold[0], wood = w.Wood[0];
        sim.Enqueue(Command.UseAbility(0, mage, 1, At(sim, 40, 5)));
        int start = TickOf(sim, mage, resolved: false);
        Assert.Equal(count, u.Count);
        Assert.Equal(p, u.CastPoint[mage.Index]);
        Assert.Equal(0, u.SummonedUnits);
        int resolve = TickOf(sim, mage, resolved: true);
        Assert.Equal(start + 19, resolve); // 1.0 s = 20 ticks counting the start
        List<EntityHandle> summoned = Summoned(w);
        Assert.Equal(3, summoned.Count);
        Assert.Equal(count + 3, u.Count);
        Assert.Equal(3, u.SummonedUnits);
        Assert.Equal(new[] { (19, 20), (21, 20), (20, 21) }, summoned.Select(h => CellOf(sim, h)).ToArray());
        foreach (EntityHandle h in summoned)
        {
            Assert.Equal((0, Infantry), (u.Owner[h.Index], u.TypeId[h.Index]));
            Assert.Equal(w.Data.Units[Infantry].Hp, u.Hp[h.Index]);
            Assert.Equal(resolve + 400, u.ExpiresAtTick[h.Index]);
            Assert.Equal(UnitState.Idle, u.State[h.Index]);
        }
        Assert.Equal((halfPop, gold, wood), (w.HalfPop[0], w.Gold[0], w.Wood[0]));
        Assert.Equal(p, u.Position[mage.Index]);
        foreach (AbilityEvent e in w.AbilityEvents) Assert.Equal(p, e.Point);
        Assert.Equal(resolve + 1000, u.AbilityReadyTick[mage.Index * DataLimits.MaxUnitAbilities + 1]);
        Assert.False(u.IsSummoned(mage.Index));
    }

    /// <summary>
    /// They are taken away exactly 400 ticks after the resolve, in phase 5 of tick resolve + 400: one death event each with
    /// Expired, killer -1 and the owner; no kill and no loss counted; the population and the summoned count back where they were.
    /// </summary>
    [Fact]
    public void TheyExpireExactlyLifetimeTicksAfterTheResolve_AsExpiries_NotKills()
    {
        Simulation sim = Sim();
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle mage = Place(sim, 0, MageType, At(sim, 20, 20));
        sim.Enqueue(Command.UseAbility(0, mage, 1, At(sim, 20, 20)));
        int resolve = TickOf(sim, mage, resolved: true);
        List<EntityHandle> summoned = Summoned(w);
        int halfPop = w.HalfPop[0];
        while (sim.TickNumber < resolve + 400)
        {
            sim.Tick();
            Assert.Equal(0, w.Deaths.Length);
            foreach (EntityHandle h in summoned) Assert.True(u.IsAlive(h));
        }
        sim.Tick(); // tick resolve + 400
        Assert.Equal(3, w.Deaths.Length);
        for (int k = 0; k < 3; k++)
        {
            DeathEvent d = w.Deaths[k];
            Assert.Equal(summoned[k], d.Victim);
            Assert.True(d.Expired);
            Assert.Equal((false, Infantry, 0, -1), (d.IsBuilding, d.VictimType, d.VictimOwner, d.KillerOwner));
            Assert.False(u.IsAlive(summoned[k]));
            Assert.Equal(0, u.ExpiresAtTick[summoned[k].Index]);
        }
        Assert.Equal((0, 0, 0, 0), (w.Kills[0], w.Kills[1], w.Losses[0], w.Losses[1]));
        Assert.Equal(0, u.SummonedUnits);
        Assert.Equal(halfPop, w.HalfPop[0]);
        Assert.True(u.IsAlive(mage));
    }

    /// <summary>
    /// A Priest with Dryjhna's Prophecy summons Zealots at 83 hp (the hp bonus); their expiry gives no Martyrdom to a
    /// permanent Zealot standing among them.
    /// </summary>
    [Fact]
    public void SummonedZealots_HaveTheHpBonus_AndTheirExpiryGivesNoMartyrdom()
    {
        Simulation sim = Sim();
        World w = sim.World;
        UnitStore u = w.Units;
        ProductionSystem.Researched(w, 1, Data.FindTech("dryjhnas_prophecy"));
        EntityHandle priest = Place(sim, 1, PriestType, At(sim, 20, 20));
        EntityHandle guard = Place(sim, 1, ZealotType, At(sim, 21, 21)); // within 6 m of every ring-1 cell
        sim.Enqueue(Command.UseAbility(1, priest, 1, At(sim, 20, 20)));
        int resolve = TickOf(sim, priest, resolved: true);
        List<EntityHandle> zealots = Summoned(w);
        Assert.Equal(3, zealots.Count);
        foreach (EntityHandle h in zealots) Assert.Equal((83, 83), (u.Hp[h.Index], w.MaxHp(h.Index)));
        Assert.Equal(4, u.BelowHpUnits); // ordinary otherwise: their passives count
        while (sim.TickNumber <= resolve + 100) sim.Tick();
        foreach (EntityHandle h in zealots) Assert.False(u.IsAlive(h));
        Assert.Equal(0, u.Statuses.Count[guard.Index]);
        Assert.Equal(1, u.BelowHpUnits);
        Assert.Equal(0, w.Losses[1]);
    }

    /// <summary>
    /// A summoned unit killed before its time is an ordinary death: a loss and a kill, not Expired, Martyrdom for the Zealot
    /// beside it. It is not taken away again, and a unit that later takes its slot is permanent.
    /// </summary>
    [Fact]
    public void OneKilledEarly_IsAnOrdinaryDeath_AndNeverExpiresAgain()
    {
        Simulation sim = Sim();
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle priest = Place(sim, 1, PriestType, At(sim, 20, 20));
        EntityHandle guard = Place(sim, 1, ZealotType, At(sim, 21, 21)); // within 6 m of every ring-1 cell
        sim.Enqueue(Command.UseAbility(1, priest, 1, At(sim, 20, 20)));
        int resolve = TickOf(sim, priest, resolved: true);
        List<EntityHandle> zealots = Summoned(w);
        sim.Tick();
        CombatSystem.HitUnit(w, new PendingHit(default, 0, zealots[0], false, 1000));
        Assert.False(u.IsAlive(zealots[0]));
        Assert.False(w.Deaths[^1].Expired);
        Assert.Equal((1, 1), (w.Losses[1], w.Kills[0]));
        Assert.Equal(2, u.SummonedUnits);
        Assert.Equal(0.5f, StatusOf(sim, guard, Data.FindStatus("frenzied")).Magnitude); // Martyrdom
        EntityHandle later = Place(sim, 1, ZealotType, At(sim, 30, 30));
        Assert.Equal(zealots[0].Index, later.Index); // the freed slot, reused
        Assert.False(u.IsSummoned(later.Index));
        while (sim.TickNumber <= resolve + 100) sim.Tick();
        Assert.True(u.IsAlive(later));
        Assert.False(u.IsAlive(zealots[1]) || u.IsAlive(zealots[2]));
        Assert.Equal((1, 1), (w.Losses[1], w.Kills[0]));
        Assert.Equal(0, u.SummonedUnits);
    }

    /// <summary>A full unit store spawns what fits: with two free slots, two of the three appear; the cast resolves and its cooldown starts.</summary>
    [Fact]
    public void AFullStore_SpawnsWhatFits()
    {
        Simulation sim = Sim(units: 4);
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle mage = Place(sim, 0, MageType, At(sim, 20, 20));
        Place(sim, 1, Infantry, At(sim, 40, 40));
        sim.Enqueue(Command.UseAbility(0, mage, 1, At(sim, 20, 20)));
        int resolve = TickOf(sim, mage, resolved: true);
        Assert.Equal(2, Summoned(w).Count);
        Assert.Equal(0, u.FreeCount);
        Assert.Equal(resolve + 1000, u.AbilityReadyTick[mage.Index * DataLimits.MaxUnitAbilities + 1]);
    }

    /// <summary>
    /// With every cell within 3 rings of the caster's cell taken but one (a ring-3 corner), one unit appears there and the
    /// other two are dropped: the free cells beyond ring 3 are not "beside the caster".
    /// </summary>
    [Fact]
    public void NoFreeCellWithinTheSearch_DropsTheRest()
    {
        Simulation sim = Sim(units: 64);
        World w = sim.World;
        EntityHandle mage = Place(sim, 0, MageType, At(sim, 20, 20));
        for (int y = 17; y <= 23; y++)
            for (int x = 17; x <= 23; x++)
                if ((x, y) != (20, 20) && (x, y) != (23, 23)) Place(sim, 1, Infantry, At(sim, x, y));
        sim.Enqueue(Command.UseAbility(0, mage, 1, At(sim, 20, 20)));
        TickOf(sim, mage, resolved: true);
        EntityHandle only = Assert.Single(Summoned(w));
        Assert.Equal((23, 23), CellOf(sim, only));
        Assert.Equal(SummonSystem.SearchRings, 3);
    }

    /// <summary>The caster killed mid-cast summons nothing, and nothing appears later.</summary>
    [Fact]
    public void TheCasterDyingMidCast_SummonsNothing()
    {
        Simulation sim = Sim();
        World w = sim.World;
        EntityHandle mage = Place(sim, 0, MageType, At(sim, 20, 20));
        sim.Enqueue(Command.UseAbility(0, mage, 1, At(sim, 20, 20)));
        TickOf(sim, mage, resolved: false);
        sim.Tick();
        CombatSystem.HitUnit(w, new PendingHit(default, 1, mage, false, 10_000));
        for (int t = 0; t < 40; t++)
        {
            sim.Tick();
            Assert.Empty(Summoned(w));
        }
        Assert.Equal(0, w.Units.SummonedUnits);
        Assert.Equal(0, w.Units.CasterCount);
    }

    /// <summary>
    /// A shift-queued summon pops like a self / aura one: after the Move, the caster casts where it arrived (never toward the
    /// command's point) and the units appear round it there.
    /// </summary>
    [Fact]
    public void AQueuedSummon_PopsAfterTheMove_AndResolvesWhereTheCasterStands()
    {
        Simulation sim = Sim();
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle mage = Place(sim, 0, MageType, At(sim, 10, 10));
        Vector2 there = At(sim, 20, 10);
        sim.Enqueue(Command.Move(0, mage, there));
        sim.Enqueue(Command.UseAbility(0, mage, 1, At(sim, 40, 40), queued: true));
        sim.Tick();
        sim.Tick(); // both apply in the second tick
        Assert.Equal(1, u.QueueCount[mage.Index]);
        int start = TickOf(sim, mage, resolved: false, max: 400);
        Vector2 at = u.Position[mage.Index];
        Assert.True(Vector2.Distance(at, there) < 1f, $"cast at {at}, not where it walked");
        Assert.Equal(at, u.CastPoint[mage.Index]);
        TickOf(sim, mage, resolved: true);
        Assert.Equal(at, u.Position[mage.Index]);
        List<EntityHandle> summoned = Summoned(w);
        Assert.Equal(3, summoned.Count);
        foreach (EntityHandle h in summoned) Assert.True(Vector2.Distance(u.Position[h.Index], at) < 3f);
        Assert.True(start > 1);
    }

    /// <summary>Summoned units are ordinary units: ordered, they walk; they take statuses.</summary>
    [Fact]
    public void SummonedUnits_TakeOrdersAndStatuses()
    {
        Simulation sim = Sim();
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle mage = Place(sim, 0, MageType, At(sim, 20, 20));
        sim.Enqueue(Command.UseAbility(0, mage, 1, At(sim, 20, 20)));
        TickOf(sim, mage, resolved: true);
        EntityHandle h = Summoned(w)[0];
        Vector2 from = u.Position[h.Index];
        sim.Enqueue(Command.Move(0, h, At(sim, 30, 20)));
        for (int t = 0; t < 30; t++) sim.Tick();
        Assert.True(Vector2.Distance(u.Position[h.Index], from) > 3f);
        StatusSystem.Apply(w, h.Index, Data.FindStatus("slowed"), 0.3f, 40, 1);
        Assert.Equal(1, u.Statuses.Count[h.Index]);
        Assert.True(u.IsSummoned(h.Index));
    }
}
