using System.Collections.Immutable;
using System.Numerics;
using Rts.Sim.Abilities;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Xunit.Abstractions;
using static Rts.Sim.Tests.ProductionMaps;

namespace Rts.Sim.Tests.QA.Content;

/// <summary>
/// QA D28 (session 2026-11-11-1317): the shipped Teblor roster played end to end on the shipped data, not on a test copy.
/// A Teblor seat (player 0) on a flat map: the War-dog pack (one Train, three dogs, 120 / 0 once, Age II gate), Hamstring
/// (a bite slows the victim 20% for 2 s), the faction's regeneration (1 HP/s after 6 s), the Giant class in real combat
/// (pierce and magic x1.25), and pop 2 / 3 against the cap. docs/factions/teblor.md "Faction bonus", "Units", "Unit notes".
/// </summary>
[Collection(SerialCollection.Name)]
public class TeblorRosterQaTests(ITestOutputHelper output)
{
    private static GameData D => TestSim.Data;

    private static int Teblor => D.FindFaction("teblor");

    /// <summary>Player 0 Teblor, player 1 Malazan, both human, on a flat 48-cell map with the shipped data.</summary>
    private static Simulation Scene(bool combat = true, int units = 64) =>
        new(new SimConfig(11, 2, units, 8 * units + 32)
        {
            Data = D,
            Combat = combat,
            Seats = ImmutableArray.Create(
                new PlayerSeat(Teblor, SeatController.Human, AiDifficulty.Normal),
                new PlayerSeat(D.FindFaction("malazan"), SeatController.Human, AiDifficulty.Normal)),
        }, LocalMovementTests.Flat(48));

    private static EntityHandle Put(Simulation sim, int owner, string type, int x, int y) =>
        CombatScenes.Place(sim, owner, D.FindUnit(type), CombatScenes.At(sim, x, y));

    private static int Count(World w, int type, int owner = 0)
    {
        int n = 0;
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.TypeId[i] == type && w.Units.Owner[i] == owner) n++;
        return n;
    }

    /// <summary>Player 0's Longhouse (cap 10) at (25, 10) and a <paramref name="producer"/> at (10, 10); player 1's Keep far off.</summary>
    private static (Simulation Sim, int Producer) Base(string producer, int gatherers, bool combat = false)
    {
        Simulation sim = Scene(combat);
        World w = sim.World;
        Assert.Equal(Teblor, w.FactionOf(0));
        Assert.True(w.Buildings.Spawn(0, D.FindBuilding("teblor_longhouse"), 10 * 48 + 25, out _));
        Assert.True(w.Buildings.Spawn(0, D.FindBuilding(producer), 10 * 48 + 10, out EntityHandle bar));
        Assert.True(w.Buildings.Spawn(1, D.FindBuilding("malazan_garrison_keep"), 40 * 48 + 40, out _));
        BuildMaps.SetTotals(sim, 0, 5000, 5000);
        for (int k = 0; k < gatherers; k++) Put(sim, 0, "teblor_gatherer", 4 + k % 8, 30 + k / 8);
        Put(sim, 1, "malazan_laborer", 44, 44);
        return (sim, bar.Index);
    }

    /// <summary>
    /// Criterion 1 (M7-3b's promise), checked apart from <c>FactionOrderTests</c> (whose first line pins two factions, BUG-1191):
    /// every unit, building, tech and ability id shipped before D28 keeps its number, factions 0 / 1 keep theirs, and every
    /// Teblor id comes after them (tech 9, ability 3).
    /// </summary>
    [Fact]
    public void EveryIdShippedBeforeD28_KeepsItsNumber_AndTheTeblorIdsAreAppended()
    {
        string[] units =
        {
            "malazan_cadre_mage", "malazan_catapult", "malazan_crossbowman", "malazan_heavy_infantry", "malazan_laborer", "malazan_sapper",
            "malazan_wickan_lancer", "whirlwind_battering_ram", "whirlwind_camp_follower", "whirlwind_desert_archer", "whirlwind_horse_raider",
            "whirlwind_priest", "whirlwind_raider", "whirlwind_zealot",
        };
        string[] buildings =
        {
            "malazan_armory", "malazan_barracks", "malazan_billet", "malazan_cadre_tower", "malazan_crossbow_range", "malazan_depot",
            "malazan_engineers_yard", "malazan_garrison_keep", "malazan_watchtower", "malazan_wickan_corral", "whirlwind_archer_camp",
            "whirlwind_holy_camp", "whirlwind_horse_lines", "whirlwind_lookout_tower", "whirlwind_raider_camp", "whirlwind_ram_yard",
            "whirlwind_shrine", "whirlwind_smithy", "whirlwind_supply_cache", "whirlwind_tent",
        };
        string[] techs = { "age_ii", "armor_1", "armor_2", "melee_weapons_1", "melee_weapons_2", "ranged_weapons_1", "ranged_weapons_2", "moranth_supply", "dryjhnas_prophecy" };
        string[] abilities = { "cusser", "telas_fire", "sandstorm" };
        for (int i = 0; i < units.Length; i++) Assert.Equal((units[i], i), (units[i], D.FindUnit(units[i])));
        for (int i = 0; i < buildings.Length; i++) Assert.Equal((buildings[i], i), (buildings[i], D.FindBuilding(buildings[i])));
        for (int i = 0; i < techs.Length; i++) Assert.Equal((techs[i], i), (techs[i], D.FindTech(techs[i])));
        for (int i = 0; i < abilities.Length; i++) Assert.Equal((abilities[i], i), (abilities[i], D.FindAbility(abilities[i])));
        Assert.Equal((0, 1, 2), (D.FindFaction("malazan"), D.FindFaction("whirlwind"), Teblor));
        Assert.All(D.Units.Skip(units.Length), u => Assert.Equal(Teblor, u.Faction));
        Assert.All(D.Buildings.Skip(buildings.Length), b => Assert.Equal(Teblor, b.Faction));
        Assert.Equal((9, 3), (D.FindTech("teblor_blood_oil_anointing"), D.FindAbility("blood_oil_frenzy")));
        Assert.Equal((21, 30, 10, 4), (D.Units.Length, D.Buildings.Length, D.Techs.Length, D.Abilities.Length));
    }

    /// <summary>
    /// "one queue item spawns a pack of 3 ... 1 pop each", Age II: before Age II the Train is refused LockedByRequirement;
    /// after it, one Train pays 120 / 0 once, reserves 6 halves at its start and spawns exactly three War-dogs after 24 s
    /// (480 ticks), each 2 halves, each at the type's size (radius 0.4).
    /// </summary>
    [Fact]
    public void AWarDogOrder_IsAgeIIGated_PaysOnce_AndSpawnsThreeDogs_After24Seconds()
    {
        (Simulation sim, int stables) = Base("teblor_stables", gatherers: 2);
        World w = sim.World;
        int dog = D.FindUnit("teblor_war_dog");
        Assert.Equal((3, 2, 6, 480, 120, 0), (D.Units[dog].SpawnCount, D.Units[dog].HalfPop, D.Units[dog].ItemHalfPop, D.Units[dog].TrainTicks, D.Units[dog].CostGold, D.Units[dog].CostWood));
        var refusals = new List<string>();
        sim.RefusedForTests = (c, why, detail) => refusals.Add($"{c.Kind}:{why}:{(why == CommandRefusal.TrainRefused ? ((TrainError)detail).ToString() : detail.ToString())}");
        sim.Enqueue(Command.Train(0, In(sim, stables), dog));
        sim.Tick();
        sim.Tick();
        Assert.Equal(new[] { "Train:TrainRefused:LockedByRequirement" }, refusals);
        Assert.Equal((0, 5000, 5000), (w.Buildings.QueueCount[stables], w.Gold[0], w.Wood[0]));

        ProductionSystem.Researched(w, 0, D.FindTech("age_ii"));
        refusals.Clear();
        int half = w.HalfPop[0];
        sim.Enqueue(Command.Train(0, In(sim, stables), dog));
        sim.Tick();
        sim.Tick();
        Assert.Empty(refusals);
        Assert.Equal((5000 - 120, 5000), (w.Gold[0], w.Wood[0]));
        Assert.Equal((1, 1), (w.Buildings.QueueCount[stables], w.Buildings.Progress[stables]));
        Assert.Equal(half + 6, w.HalfPop[0]);
        int ran = CombatScenes.RunUntil(sim, () => Count(w, dog) > 0, 600);
        output.WriteLine($"the pack spawned {ran} ticks after its start tick; dogs {Count(w, dog)}");
        Assert.Equal(3, Count(w, dog));
        Assert.Equal(479, ran); // started at progress 1: 479 more ticks to the 480th
        Assert.Equal((half + 6, RecountHalfPop(w, 0)), (w.HalfPop[0], RecountHalfPop(w, 0)));
        foreach (int i in LiveUnits(w))
            if (w.Units.TypeId[i] == dog) Assert.Equal(0.4f, D.Units[dog].Radius);
        Assert.Equal((5000 - 120, 5000), (w.Gold[0], w.Wood[0])); // paid once for three
    }

    /// <summary>Hamstring on the shipped War-dog: a bite on a surviving Heavy Infantry leaves it Slowed 0.2 for 40 ticks (speed x 0.8), and the slow ends after them.</summary>
    [Fact]
    public void AWarDogBite_SlowsItsVictim20Percent_For2Seconds()
    {
        Simulation sim = Scene();
        World w = sim.World;
        UnitStore u = w.Units;
        int slowed = D.FindStatus("slowed");
        EntityHandle dog = Put(sim, 0, "teblor_war_dog", 20, 20);
        EntityHandle heavy = Put(sim, 1, "malazan_heavy_infantry", 21, 20);
        u.Hold[heavy.Index] = true; // the victim doesn't fight back: the dog lives to be removed
        float speed = D.Units[u.TypeId[heavy.Index]].SpeedPerTick;
        int hp = u.Hp[heavy.Index];
        int ran = CombatScenes.RunUntil(sim, () => u.Hp[heavy.Index] < hp, 400);
        Assert.True(ran < 400, "the dog never bit");
        Assert.True(u.IsAlive(heavy));
        StatusStore s = u.Statuses;
        int at = s.IndexOf(heavy.Index, slowed);
        Assert.True(at >= 0, "the bitten Heavy Infantry carries no slowed status");
        Assert.Equal((40, 0.2f), (s.TicksRemaining[at], s.Magnitude[at]));
        Assert.Equal(speed * 0.8f, u.Speed[heavy.Index]);
        u.Free(dog);
        for (int k = 0; k < 39; k++) sim.Tick();
        Assert.Equal(speed * 0.8f, u.Speed[heavy.Index]);
        sim.Tick();
        Assert.True(s.IndexOf(heavy.Index, slowed) < 0, "the slow outlived 2 s");
        Assert.Equal(speed, u.Speed[heavy.Index]);
    }

    /// <summary>"All units regenerate 1 HP/s after 6 s without taking damage": a Warrior hit to 270 stays there 120 ticks, then gains 1 HP every 20 ticks to 320 and stops; a second hit restarts the wait. A Malazan unit never heals.</summary>
    [Fact]
    public void ATeblorWarrior_Regenerates1HpPerSecond_From6SecondsAfterTheLastHit()
    {
        Simulation sim = Scene(combat: false);
        World w = sim.World;
        UnitStore u = w.Units;
        Assert.Equal((20, 120), (D.Factions[Teblor].RegenPerTick, D.Factions[Teblor].RegenDelayTicks));
        Assert.Equal((0, 0), (D.Factions[D.FindFaction("malazan")].RegenPerTick, D.Factions[D.FindFaction("whirlwind")].RegenPerTick));
        EntityHandle war = Put(sim, 0, "teblor_warrior", 20, 20);
        EntityHandle heavy = Put(sim, 1, "malazan_heavy_infantry", 30, 30);
        sim.Tick();
        int t = sim.TickNumber;
        CombatSystem.HitUnit(w, new PendingHit(default, 1, war, false, 50));
        CombatSystem.HitUnit(w, new PendingHit(default, 0, heavy, false, 30));
        int heavyHp = u.Hp[heavy.Index];
        Assert.Equal(270, u.Hp[war.Index]);
        while (sim.TickNumber <= t + 118) sim.Tick();
        Assert.Equal(270, u.Hp[war.Index]); // through tick t + 118: still waiting
        while (sim.TickNumber <= t + 139) sim.Tick();
        Assert.Equal(271, u.Hp[war.Index]); // the first HP after 6 s + 1 s of accrual
        while (sim.TickNumber <= t + 339) sim.Tick();
        Assert.Equal(281, u.Hp[war.Index]); // (339 - 119) / 20 = 11 HP
        CombatSystem.HitUnit(w, new PendingHit(default, 1, war, false, 1)); // restart the wait
        int t2 = sim.TickNumber;
        while (sim.TickNumber <= t2 + 118) sim.Tick();
        Assert.Equal(280, u.Hp[war.Index]);
        while (sim.TickNumber <= t2 + 2000) sim.Tick();
        Assert.Equal(320, u.Hp[war.Index]); // capped at max
        Assert.Equal(heavyHp, u.Hp[heavy.Index]); // the Malazan never heals
    }

    /// <summary>
    /// The Giant class in real combat: a Malazan Crossbowman's bolt (9 pierce, x1.3 vs heavy only) takes 9 x 1.25 = 11.25 -> 11,
    /// minus armor 2 = 9 off a Warrior and 9 - 2 = 7 off a Destrier Rider (Mounted, x1); a Cadre Mage's bolt (9 magic,
    /// ignores armor) 11 off a Shaman (Giant) and 9 off a Gatherer (Light).
    /// </summary>
    [Theory]
    [InlineData("malazan_crossbowman", "teblor_warrior", 9)]
    [InlineData("malazan_crossbowman", "teblor_destrier_rider", 7)]
    [InlineData("malazan_cadre_mage", "teblor_shaman", 11)]
    [InlineData("malazan_cadre_mage", "teblor_gatherer", 9)]
    [InlineData("malazan_heavy_infantry", "teblor_breaker", 7)] // melee x1 on a giant: 10 - 3
    public void AGiant_TakesTimesOneAndAQuarter_FromPierceAndMagic_InACombatScene(string attacker, string target, int expected)
    {
        Simulation sim = Scene();
        UnitStore u = sim.World.Units;
        EntityHandle victim = Put(sim, 0, target, 20, 20);
        u.Hold[victim.Index] = true;
        int range = D.Units[D.FindUnit(attacker)].Attack.Range > 2f ? 8 : 1;
        Put(sim, 1, attacker, 20 + range, 20);
        int hp = u.Hp[victim.Index];
        int ran = CombatScenes.RunUntil(sim, () => u.Hp[victim.Index] < hp, 600);
        Assert.True(ran < 600, $"{attacker} never hit {target}");
        output.WriteLine($"{attacker} -> {target}: {hp - u.Hp[victim.Index]} (want {expected})");
        Assert.Equal(expected, hp - u.Hp[victim.Index]);
    }

    /// <summary>
    /// Pop 2 and 3 count against the cap: Longhouse cap 10 with 7 Gatherers (7 pop), a Breaker (3) starts and lands at 10;
    /// a Warrior (2) queued then never starts while the cap is 10, and starts the tick a Hut (+8) stands.
    /// </summary>
    [Fact]
    public void PopTwoAndThree_CountAgainstTheCap()
    {
        (Simulation sim, int hall) = Base("teblor_breakers_hall", gatherers: 7);
        World w = sim.World;
        Assert.True(w.Buildings.Spawn(0, D.FindBuilding("teblor_war_lodge"), 20 * 48 + 10, out EntityHandle lodge));
        Assert.Equal((14, 20), (w.HalfPop[0], w.HalfPopCap[0]));
        int breaker = D.FindUnit("teblor_breaker"), warrior = D.FindUnit("teblor_warrior");
        Assert.Equal((6, 4, 4, 6, 2), (D.Units[breaker].HalfPop, D.Units[warrior].HalfPop, D.Units[D.FindUnit("teblor_shaman")].HalfPop,
            D.Units[D.FindUnit("teblor_destrier_rider")].HalfPop, D.Units[D.FindUnit("teblor_gatherer")].HalfPop));
        sim.Enqueue(Command.Train(0, In(sim, hall), breaker));
        sim.Tick();
        sim.Tick();
        Assert.Equal(20, w.HalfPop[0]);
        sim.Enqueue(Command.Train(0, In(sim, lodge.Index), warrior));
        for (int k = 0; k < 1000; k++) sim.Tick();
        Assert.Equal(1, Count(w, breaker));
        Assert.Equal((0, 1, 20), (Count(w, warrior), w.Buildings.QueueCount[lodge.Index], w.HalfPop[0]));
        Assert.Equal(0, w.Buildings.Progress[lodge.Index]); // waiting at the cap
        Assert.True(w.Buildings.Spawn(0, D.FindBuilding("teblor_hut"), 30 * 48 + 30, out _));
        Assert.Equal(36, w.HalfPopCap[0]);
        int ran = CombatScenes.RunUntil(sim, () => Count(w, warrior) > 0, 700);
        Assert.True(ran < 700, "the Warrior never trained after the Hut");
        Assert.Equal((24, 24), (w.HalfPop[0], RecountHalfPop(w, 0)));
    }
}
