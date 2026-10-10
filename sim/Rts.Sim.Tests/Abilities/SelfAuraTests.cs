using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Abilities;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using static Rts.Sim.Tests.AbilityScenes;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests;

/// <summary>
/// M4-4b-3a criterion 4: the <c>selfAura</c> ability kind (docs/02 "Ability system": Self / aura). No target: the command's
/// point is ignored, the cast runs as a target-ground one (no walk, the cast timer, <see cref="UnitState.Casting"/>), and the
/// effects resolve around the caster where it stands at the resolve, with the ability's radius (the edge counts). Two
/// test-only abilities on the Heavy Infantry (no shipped ability uses the kind yet).
/// </summary>
public class SelfAuraTests
{
    /// <summary>Test data: the Malazan Heavy Infantry has <c>zz_test_rally</c> (ability 0) and <c>zz_test_aura_zone</c> (ability 1).</summary>
    internal static GameData Data => s_data.Value;

    private static readonly Lazy<GameData> s_data = new(() =>
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        AddTo(dir);
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        return r.Data!;
    });

    /// <summary>
    /// Adds the two test-only selfAura abilities to a data copy and lists them on the Heavy Infantry: a rally (own units
    /// within 4 m: Frenzied +50 % for 5 s; 0.5 s cast, 10 s cooldown) and an aura zone (a 3 m zone of Slowed 0.3 that lasts
    /// 4 s, on enemy units).
    /// </summary>
    internal static void AddTo(TestDataDir dir)
    {
        dir.EditJson("factions/malazan/abilities.json", root =>
        {
            JsonArray list = root["abilities"]!.AsArray();
            list.Add(new JsonObject
            {
                ["id"] = "zz_test_rally", ["displayName"] = "Test Rally", ["description"] = "Test only.", ["kind"] = "selfAura",
                ["radius"] = 4, ["castTime"] = 0.5, ["cooldown"] = 10, ["affects"] = "own_units", ["autocast"] = false,
                ["effects"] = new JsonArray(new JsonObject { ["kind"] = "applyStatus", ["status"] = "frenzied", ["magnitude"] = 0.5, ["duration"] = 5 }),
            });
            list.Add(new JsonObject
            {
                ["id"] = "zz_test_aura_zone", ["displayName"] = "Test Aura Zone", ["description"] = "Test only.", ["kind"] = "selfAura",
                ["radius"] = 3, ["castTime"] = 0, ["cooldown"] = 10, ["duration"] = 4, ["affects"] = "enemy_units", ["autocast"] = false,
                ["effects"] = new JsonArray(new JsonObject
                {
                    ["kind"] = "createZone",
                    ["statuses"] = new JsonArray(new JsonObject { ["status"] = "slowed", ["magnitude"] = 0.3, ["duration"] = 1 }),
                }),
            });
        });
        dir.SetUnitField("malazan", "malazan_heavy_infantry", "abilities", "[\"zz_test_rally\", \"zz_test_aura_zone\"]");
    }

    private static Simulation Sim(int units = 64) =>
        TestSim.Explored(new Simulation(new SimConfig(1, 2, units, 512) { Data = Data, Combat = false }, LocalMovementTests.Flat(48)));

    private static int Infantry => Data.FindUnit("malazan_heavy_infantry");

    private static int Frenzied => Data.FindStatus("frenzied");

    [Fact]
    public void TheTestAbilities_LoadAsSelfAura_WithNoRange()
    {
        AbilityDef rally = Data.Abilities[Data.FindAbility("zz_test_rally")];
        Assert.Equal((AbilityKind.SelfAura, 0f, 4f, 10, 200), (rally.Kind, rally.Range, rally.Radius, rally.CastTicks, rally.CooldownTicks));
        Assert.Equal(new[] { rally.Id, Data.FindAbility("zz_test_aura_zone") }, Data.Units[Infantry].Abilities.ToArray());
        foreach (AbilityDef a in TestSim.Data.Abilities) Assert.Equal(AbilityKind.TargetGround, a.Kind); // none shipped
    }

    [Fact]
    public void Rally_ReachesTheCasterAndOwnUnitsAt4m_Not401_NorEnemies()
    {
        Simulation sim = Sim();
        UnitStore u = sim.World.Units;
        Vector2 p = At(sim, 20, 20);
        EntityHandle caster = Place(sim, 0, Infantry, p);
        EntityHandle edge = Place(sim, 0, Infantry, Off(p, 4f));
        EntityHandle edgeY = Place(sim, 0, Infantry, Off(p, 0f, -4f));
        EntityHandle outside = Place(sim, 0, Infantry, Off(p, -4.01f));
        EntityHandle enemyNear = Place(sim, 1, Infantry, Off(p, 0f, 1.5f));
        EntityHandle enemyEdge = Place(sim, 1, Infantry, Off(p, 0f, 4f));
        sim.Enqueue(Command.UseAbility(0, caster, 0, Off(p, 15f, 15f)));
        int start = TickOf(sim, caster, resolved: false);
        Assert.Equal(UnitState.Casting, u.State[caster.Index]);
        Assert.Equal(p, u.CastPoint[caster.Index]); // the caster's place, not the command's point
        int resolve = TickOf(sim, caster, resolved: true);
        Assert.Equal(start + 9, resolve); // 0.5 s = 10 ticks counting the start
        foreach (EntityHandle h in new[] { caster, edge, edgeY })
            Assert.Equal((0.5f, 100), StatusOf(sim, h, Frenzied));
        foreach (EntityHandle h in new[] { outside, enemyNear, enemyEdge })
            Assert.Equal(0, u.Statuses.Count[h.Index]);
        Assert.Equal(p, u.Position[caster.Index]); // it never walked
        Assert.Equal(UnitState.Idle, u.State[caster.Index]);
        foreach (AbilityEvent e in sim.World.AbilityEvents) Assert.Equal(p, e.Point); // the resolve's point is the caster's
    }

    [Fact]
    public void ThePointIsIgnored_TwoCastsWithDifferentPointsResolveIdentically()
    {
        ulong[] hashes = new ulong[3];
        Vector2[] points = { new(5f, 5f), new(80f, 60f), default };
        for (int k = 0; k < 3; k++)
        {
            Simulation sim = Sim();
            Vector2 p = At(sim, 20, 20);
            EntityHandle caster = Place(sim, 0, Infantry, p);
            Place(sim, 0, Infantry, Off(p, 2f));
            Place(sim, 1, Infantry, Off(p, -2f));
            sim.Enqueue(Command.UseAbility(0, caster, 0, k == 2 ? p : points[k]));
            TickOf(sim, caster, resolved: true);
            for (int t = 0; t < 30; t++) sim.Tick();
            hashes[k] = sim.StateHash();
        }
        Assert.Equal(hashes[0], hashes[1]);
        Assert.Equal(hashes[0], hashes[2]);
    }

    [Fact]
    public void TheEffectsLandWhereTheCasterStandsAtTheResolve()
    {
        Simulation sim = Sim();
        UnitStore u = sim.World.Units;
        Vector2 p = At(sim, 20, 20);
        EntityHandle caster = Place(sim, 0, Infantry, p);
        EntityHandle there = Place(sim, 0, Infantry, Off(p, 8f));
        EntityHandle here = Place(sim, 0, Infantry, Off(p, -3f));
        sim.Enqueue(Command.UseAbility(0, caster, 0, p));
        TickOf(sim, caster, resolved: false);
        u.Position[caster.Index] = Off(p, 6f); // a test seam: nothing moves a casting unit
        TickOf(sim, caster, resolved: true);
        Assert.Equal((0.5f, 100), StatusOf(sim, there, Frenzied)); // 2 m from where it stands now
        Assert.Equal(0, u.Statuses.Count[here.Index]);           // 9 m from it now
    }

    [Fact]
    public void ACreateZone_FromASelfAura_IsMadeAtTheCaster()
    {
        Simulation sim = Sim();
        Vector2 p = At(sim, 20, 20);
        EntityHandle caster = Place(sim, 0, Infantry, p);
        EntityHandle enemy = Place(sim, 1, Infantry, Off(p, 0f, 3f));
        sim.Enqueue(Command.UseAbility(0, caster, 1, Off(p, 20f, -10f)));
        TickOf(sim, caster, resolved: true);
        ZoneStore z = sim.World.Zones;
        Assert.Equal(1, z.Count);
        Assert.Equal(p, z.Center[0]);
        Assert.Equal((3f, 80), (z.Radius(0), z.TicksRemaining[0]));
        Assert.Equal(0.3f, StatusOf(sim, enemy, Data.FindStatus("slowed")).Magnitude);
    }

    [Fact]
    public void TheCooldownStartsAtTheResolve_AndAMoveCancelsTheCastWithNone()
    {
        Simulation sim = Sim();
        UnitStore u = sim.World.Units;
        Vector2 p = At(sim, 20, 20);
        EntityHandle caster = Place(sim, 0, Infantry, p);
        sim.Enqueue(Command.UseAbility(0, caster, 0, p));
        int resolve = TickOf(sim, caster, resolved: true);
        Assert.Equal(resolve + 200, u.AbilityReadyTick[caster.Index * DataLimits.MaxUnitAbilities]);
        // On cooldown: a second cast is dropped.
        sim.Enqueue(Command.UseAbility(0, caster, 0, p));
        sim.Tick();
        sim.Tick(); // a command applies in the tick after the next
        Assert.Equal(-1, u.CastAbility[caster.Index]);
        Assert.False(Had(sim, caster, resolved: false));
        // A cast cancelled by a Move starts no cooldown.
        EntityHandle other = Place(sim, 0, Infantry, Off(p, 10f));
        sim.Enqueue(Command.UseAbility(0, other, 0, p));
        TickOf(sim, other, resolved: false);
        sim.Enqueue(Command.Move(0, other, Off(p, 10f, 5f)));
        sim.Tick();
        sim.Tick();
        Assert.Equal(-1, u.CastAbility[other.Index]);
        Assert.Equal(UnitState.Moving, u.State[other.Index]);
        Assert.Equal(0, u.AbilityReadyTick[other.Index * DataLimits.MaxUnitAbilities]);
    }
}
