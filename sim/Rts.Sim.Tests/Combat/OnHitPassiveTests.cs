using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Abilities;
using Rts.Sim.Combat;
using Rts.Sim.Data;
using Rts.Sim.Entities;

namespace Rts.Sim.Tests;

/// <summary>
/// M7-2 criterion 4: the <c>onHit</c> passive (docs/factions/teblor.md "Unit notes", Hamstring: "each bite Slows the target
/// 20% for 2 s") on a test copy cut to Malazan and Whirlwind where every unit of both carries <c>{onHit, slowed, 0.2, 2 s}</c>
/// (neither faction ships one). A direct melee hit and a unit's projectile landing on its aimed target apply it to a surviving victim; a splash
/// neighbour, a damage-over-time pulse, a tower's shot and a killing blow do not (docs/03 "Implementation (M7-2)").
/// </summary>
[Collection(SerialCollection.Name)]
public class OnHitPassiveTests
{
    /// <summary>The copy: every unit bites (the Zealot keeps its two passives and gains the third).</summary>
    internal static readonly Lazy<GameData> s_bite = new(() => AiScenes.DataWith(BiteAll));

    /// <summary>
    /// Cuts <paramref name="dir"/> to the first two factions (<see cref="TestDataDir.CutToFirstFactions"/>) and gives every
    /// unit of both a Hamstring. M7-5 (BUG-1222): the cut keeps "every unit" to units with no onHit of their own, since a
    /// unit holds at most one passive per trigger and an appended faction may ship one (the Teblor War-dog).
    /// </summary>
    internal static void BiteAll(TestDataDir dir)
    {
        dir.CutToFirstFactions(2);
        foreach (string f in TestSim.FirstTwoFactions)
            dir.EditJson($"factions/{f}/units.json", root =>
            {
                foreach (JsonNode? unit in root["units"]!.AsArray())
                {
                    if (unit!["passives"] is not JsonArray list)
                    {
                        list = new JsonArray();
                        unit["passives"] = list;
                    }
                    list.Add(Hamstring());
                }
            });
    }

    internal static JsonObject Hamstring() => new() { ["trigger"] = "onHit", ["status"] = "slowed", ["magnitude"] = 0.2, ["duration"] = 2 };

    private static GameData D => s_bite.Value;

    private static int Slowed => D.FindStatus("slowed");

    /// <summary>A two-player flat sim (player 0 Malazan, player 1 Whirlwind) on the copy.</summary>
    private static Simulation Scene(int units = 64) =>
        new(new SimConfig(3, 2, units, 8 * units + 32) { Data = D }, LocalMovementTests.Flat(48));

    private static EntityHandle Put(Simulation sim, int owner, string type, int x, int y, float dx = 0f) =>
        CombatScenes.Place(sim, owner, D.FindUnit(type), CombatScenes.At(sim, x, y, dx));

    /// <summary>Unit <paramref name="h"/>'s slowed entry's ticks left, or -1 without one.</summary>
    private static int SlowLeft(Simulation sim, EntityHandle h)
    {
        StatusStore s = sim.World.Units.Statuses;
        int at = s.IndexOf(h.Index, Slowed);
        return at < 0 ? -1 : s.TicksRemaining[at];
    }

    private static float SlowMagnitude(Simulation sim, EntityHandle h)
    {
        StatusStore s = sim.World.Units.Statuses;
        int at = s.IndexOf(h.Index, Slowed);
        return at < 0 ? 0f : s.Magnitude[at];
    }

    /// <summary>Ticks until <paramref name="victim"/> has lost hit points (or is dead); fails after <paramref name="max"/>.</summary>
    private static void UntilHit(Simulation sim, EntityHandle victim, int max = 400)
    {
        UnitStore u = sim.World.Units;
        int hp = u.Hp[victim.Index];
        int ran = CombatScenes.RunUntil(sim, () => !u.IsAlive(victim) || u.Hp[victim.Index] < hp, max);
        Assert.True(ran < max, "no hit landed");
    }

    [Fact]
    public void TheCopy_GivesEveryUnitHamstring_AndNoShippedUnitHasIt()
    {
        foreach (UnitDef u in D.Units)
        {
            Assert.True(u.HitPassive >= 0, u.Key);
            UnitPassive p = u.Passives[u.HitPassive];
            Assert.Equal((PassiveTrigger.OnHit, Slowed, 0.2f, 40), (p.Trigger, p.Status, p.Magnitude, p.DurationTicks));
        }
        Assert.All(TestSim.TwoFactionData.Units, u => Assert.Equal(-1, u.HitPassive)); // M7-5: the two factions the copy is cut to
    }

    /// <summary>A melee hit slows its victim 0.2 for 40 ticks; speed x 0.8 while it lasts, back to the type's after the 40th tick.</summary>
    [Fact]
    public void AMeleeHit_Slows_ItsVictim20PercentFor40Ticks_ThenSpeedIsRestored()
    {
        Simulation sim = Scene();
        UnitStore u = sim.World.Units;
        EntityHandle heavy = Put(sim, 0, "malazan_heavy_infantry", 20, 20);
        EntityHandle raider = Put(sim, 1, "whirlwind_raider", 21, 20);
        float speed = D.Units[u.TypeId[raider.Index]].SpeedPerTick;
        UntilHit(sim, raider);
        Assert.True(u.IsAlive(raider));
        Assert.Equal((40, 0.2f), (SlowLeft(sim, raider), SlowMagnitude(sim, raider)));
        Assert.Equal(speed * (1f - 0.2f), u.Speed[raider.Index]);
        // The biter leaves (no more hits): 39 ticks later the slow is still on with 1 tick left; the 40th ends it.
        u.Free(heavy);
        for (int k = 0; k < 39; k++) sim.Tick();
        Assert.Equal(1, SlowLeft(sim, raider));
        Assert.Equal(speed * 0.8f, u.Speed[raider.Index]);
        sim.Tick();
        Assert.Equal(-1, SlowLeft(sim, raider));
        Assert.Equal(speed, u.Speed[raider.Index]);
    }

    /// <summary>A second bite inside the 2 s refreshes the entry to 40 ticks left (the stacking rule: the longer duration wins; the magnitude is equal).</summary>
    [Fact]
    public void ASecondHit_InsideTheTwoSeconds_RefreshesTo40Left()
    {
        Simulation sim = Scene();
        UnitStore u = sim.World.Units;
        Put(sim, 0, "malazan_heavy_infantry", 20, 20);
        EntityHandle raider = Put(sim, 1, "whirlwind_raider", 21, 20);
        UntilHit(sim, raider);
        Assert.Equal(40, SlowLeft(sim, raider));
        int hp = u.Hp[raider.Index], left = 40;
        for (int k = 0; k < 40 && u.Hp[raider.Index] == hp; k++)
        {
            left = SlowLeft(sim, raider);
            sim.Tick();
        }
        Assert.True(u.Hp[raider.Index] < hp, "no second hit inside 40 ticks (a 30-tick cooldown)");
        Assert.InRange(left, 1, 39); // counting down before the second bite
        Assert.Equal(40, SlowLeft(sim, raider));
        Assert.Equal(1, u.Statuses.Count[raider.Index]); // one entry per status id, refreshed
    }

    /// <summary>A crossbow bolt landing on its target applies the shooter's onHit.</summary>
    [Fact]
    public void AProjectileLanding_OnItsTarget_Slows_It()
    {
        Simulation sim = Scene();
        UnitStore u = sim.World.Units;
        EntityHandle bow = Put(sim, 0, "malazan_crossbowman", 10, 20);
        EntityHandle target = Put(sim, 1, "whirlwind_camp_follower", 16, 20);
        UntilHit(sim, target);
        Assert.True(u.IsAlive(target) && u.IsAlive(bow));
        Assert.True(sim.World.Impacts.Length > 0, "the hit was no projectile's");
        Assert.Equal((40, 0.2f), (SlowLeft(sim, target), SlowMagnitude(sim, target)));
    }

    /// <summary>A Cadre Mage's magic bolt slows its aimed target; the neighbour its splash hurts is not slowed.</summary>
    [Fact]
    public void ASplashNeighbour_IsHurt_ButNotSlowed()
    {
        Simulation sim = Scene();
        UnitStore u = sim.World.Units;
        Put(sim, 0, "malazan_cadre_mage", 10, 20);
        EntityHandle target = Put(sim, 1, "whirlwind_camp_follower", 15, 20);
        EntityHandle neighbour = Put(sim, 1, "whirlwind_camp_follower", 15, 20, dx: 0.9f);
        int neighbourHp = u.Hp[neighbour.Index];
        UntilHit(sim, target);
        Assert.True(u.IsAlive(target) && u.IsAlive(neighbour));
        Assert.True(u.Hp[neighbour.Index] < neighbourHp, "precondition: the splash reached the neighbour");
        Assert.Equal(40, SlowLeft(sim, target));
        Assert.Equal(-1, SlowLeft(sim, neighbour));
    }

    /// <summary>A damage-over-time pulse is no bite.</summary>
    [Fact]
    public void ADamageOverTimePulse_DoesNotSlow()
    {
        Simulation sim = Scene();
        UnitStore u = sim.World.Units;
        EntityHandle raider = Put(sim, 1, "whirlwind_raider", 30, 30);
        Put(sim, 0, "malazan_laborer", 4, 4); // player 0 stays in
        sim.Tick();
        StatusSystem.Apply(sim.World, raider.Index, D.FindStatus("burning"), 5f, 100, 0);
        int hp = u.Hp[raider.Index];
        for (int k = 0; k < 45; k++) sim.Tick();
        Assert.True(u.Hp[raider.Index] < hp, "precondition: a pulse landed");
        Assert.Equal(-1, SlowLeft(sim, raider));
    }

    /// <summary>
    /// A watchtower's bolt is no unit's hit: no slow. Its building type id is also a unit type id in this data, and that
    /// unit bites, so a hit path that read the shot's type as a unit's would slow here.
    /// </summary>
    [Fact]
    public void ATowersShot_DoesNotSlow()
    {
        Simulation sim = Scene();
        UnitStore u = sim.World.Units;
        int tower = D.FindBuilding("malazan_watchtower");
        Assert.True(tower < D.Units.Length && D.Units[tower].HitPassive >= 0, "precondition: the tower's type id names a biting unit");
        Put(sim, 0, "malazan_laborer", 2, 2); // player 0 stays in (a tower alone is no production building)
        Assert.True(sim.World.Buildings.Spawn(0, tower, 20 * 48 + 10, out _));
        EntityHandle target = Put(sim, 1, "whirlwind_camp_follower", 18, 20);
        u.Hold[target.Index] = true;
        UntilHit(sim, target);
        Assert.True(u.IsAlive(target));
        Assert.Equal(-1, SlowLeft(sim, target));
    }

    /// <summary>A killing blow applies nothing: the victim's slot is freed with no entry, and the store's count of units with statuses is exact.</summary>
    [Fact]
    public void AKillingBlow_AppliesNothing()
    {
        Simulation sim = Scene();
        UnitStore u = sim.World.Units;
        EntityHandle heavy = Put(sim, 0, "malazan_heavy_infantry", 20, 20);
        EntityHandle victim = Put(sim, 1, "whirlwind_camp_follower", 21, 20);
        Put(sim, 1, "whirlwind_camp_follower", 40, 40); // player 1 stays in
        u.Hp[victim.Index] = 1;
        UntilHit(sim, victim);
        Assert.False(u.IsAlive(victim));
        Assert.Equal(0, u.Statuses.Count[victim.Index]);
        int withStatuses = 0;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.Statuses.Count[i] > 0) withStatuses++;
        Assert.Equal(withStatuses, u.Statuses.UnitsWithStatuses);

        // The hit path itself: a hit of the victim's whole hp with the attacker's onHit type kills and applies nothing.
        EntityHandle other = Put(sim, 1, "whirlwind_raider", 30, 30);
        CombatSystem.HitUnit(sim.World, new PendingHit(heavy, 0, other, false, u.Hp[other.Index]), u.TypeId[heavy.Index]);
        Assert.False(u.IsAlive(other));
        Assert.Equal(0, u.Statuses.Count[other.Index]);
        // And one short of it survives with the slow.
        EntityHandle third = Put(sim, 1, "whirlwind_raider", 30, 34);
        CombatSystem.HitUnit(sim.World, new PendingHit(heavy, 0, third, false, u.Hp[third.Index] - 1), u.TypeId[heavy.Index]);
        Assert.Equal((1, 40), (u.Hp[third.Index], SlowLeft(sim, third)));
        // Without the type (a splash, a pulse, an ability) nothing is applied.
        EntityHandle fourth = Put(sim, 1, "whirlwind_raider", 30, 38);
        CombatSystem.HitUnit(sim.World, new PendingHit(heavy, 0, fourth, false, 5));
        Assert.Equal(-1, SlowLeft(sim, fourth));
    }

    /// <summary>A stronger slow already on the victim (Sandstorm's kind) is kept: the bite's 0.2 only lengthens it (the stacking rule).</summary>
    [Fact]
    public void AStrongerSlow_IsKept_TheBiteOnlyRefreshesItsDuration()
    {
        Simulation sim = Scene();
        UnitStore u = sim.World.Units;
        EntityHandle heavy = Put(sim, 0, "malazan_heavy_infantry", 20, 20);
        EntityHandle victim = Put(sim, 1, "whirlwind_raider", 30, 30);
        StatusSystem.Apply(sim.World, victim.Index, Slowed, 0.5f, 10, 0);
        CombatSystem.HitUnit(sim.World, new PendingHit(heavy, 0, victim, false, 5), u.TypeId[heavy.Index]);
        Assert.Equal((40, 0.5f), (SlowLeft(sim, victim), SlowMagnitude(sim, victim)));
        Assert.Equal(D.Units[u.TypeId[victim.Index]].SpeedPerTick * 0.5f, u.Speed[victim.Index]);
    }
}
