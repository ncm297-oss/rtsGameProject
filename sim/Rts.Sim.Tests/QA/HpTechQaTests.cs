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

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M4-4b-3b (session 2026-10-10-1314): hp techs that raise hit points (BUG-0421) and the exact below-the-line compare
/// (BUG-0420), attacked past the developer's rows. The oracle for a landing: every live unit of the researching player
/// keeps its damage (max minus current) unless the 1-hp floor holds it up, every other unit is untouched, and every live
/// unit sits in 1..max. Test-only techs: two that overlap Dryjhna's Prophecy on the Zealot (by tag and by id) and lower
/// the Raider, and one that takes 100 hp from the Zealot.
/// </summary>
[Collection(SerialCollection.Name)]
public class HpTechQaTests
{
    private static GameData Data => s_data.Value;

    private static readonly Lazy<GameData> s_data = new(() =>
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("factions/whirlwind/techs.json", root =>
        {
            JsonArray techs = root["techs"]!.AsArray();
            techs.Add(new JsonObject
            {
                ["id"] = "zz_qa_vigor", ["displayName"] = "QA Vigor", ["description"] = "Test only.", ["researchedAt"] = "forge",
                ["cost"] = new JsonObject { ["gold"] = 1, ["wood"] = 1 }, ["researchTime"] = 1, ["requires"] = new JsonArray(),
                ["effects"] = new JsonArray(
                    new JsonObject { ["stat"] = "hp", ["amount"] = 10, ["appliesTo"] = new JsonObject { ["tags"] = new JsonArray("infantry") } },
                    new JsonObject { ["stat"] = "hp", ["amount"] = 5, ["appliesTo"] = new JsonObject { ["units"] = new JsonArray("whirlwind_zealot") } },
                    new JsonObject { ["stat"] = "hp", ["amount"] = -7, ["appliesTo"] = new JsonObject { ["units"] = new JsonArray("whirlwind_raider") } }),
            });
            techs.Add(new JsonObject
            {
                ["id"] = "zz_qa_frailty", ["displayName"] = "QA Frailty", ["description"] = "Test only.", ["researchedAt"] = "forge",
                ["cost"] = new JsonObject { ["gold"] = 1, ["wood"] = 1 }, ["researchTime"] = 1, ["requires"] = new JsonArray(),
                ["effects"] = new JsonArray(
                    new JsonObject { ["stat"] = "hp", ["amount"] = -100, ["appliesTo"] = new JsonObject { ["units"] = new JsonArray("whirlwind_zealot") } }),
            });
        });
        SummonTests.AddTo(dir); // the Priest's Zealot summon, for the summoned-unit rows
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join("; ", r.Errors));
        return r.Data!;
    });

    private static int ZealotT => Data.FindUnit("whirlwind_zealot");
    private static int RaiderT => Data.FindUnit("whirlwind_raider");
    private static int PriestT => Data.FindUnit("whirlwind_priest");
    private static int Prophecy => Data.FindTech("dryjhnas_prophecy");
    private static int Vigor => Data.FindTech("zz_qa_vigor");
    private static int Frailty => Data.FindTech("zz_qa_frailty");

    private static Simulation Sim(int units = 64, bool combat = false) =>
        TestSim.Explored(new Simulation(new SimConfig(1, 2, units, 512) { Data = Data, Combat = combat }, LocalMovementTests.Flat(48)));

    /// <summary>Researches <paramref name="tech"/> for <paramref name="player"/> as phase 3 does and checks the landing oracle.</summary>
    private static void LandAndCheck(World w, int player, int tech)
    {
        UnitStore u = w.Units;
        int n = u.Capacity;
        var alive = new bool[n];
        var hp = new int[n];
        var deficit = new int[n];
        for (int i = 0; i < n; i++)
        {
            alive[i] = u.Alive[i];
            hp[i] = u.Hp[i];
            if (alive[i]) deficit[i] = w.MaxHp(i) - u.Hp[i];
        }
        ProductionSystem.Researched(w, player, tech);
        for (int i = 0; i < n; i++)
        {
            Assert.Equal(alive[i], u.Alive[i]);
            if (!alive[i])
            {
                Assert.Equal(hp[i], u.Hp[i]);
                continue;
            }
            int max = w.MaxHp(i);
            Assert.InRange(u.Hp[i], 1, max);
            if (u.Owner[i] != player) Assert.Equal(hp[i], u.Hp[i]);
            else if (max - deficit[i] >= 1) Assert.Equal(max - deficit[i], u.Hp[i]); // the damage carried over
            else Assert.Equal(1, u.Hp[i]); // the floor
        }
    }

    /// <summary>
    /// Prophecy (+20 Zealot) then QA Vigor (+10 infantry, +5 Zealot, -7 Raider): a full Zealot reads 63, 83, 98; one at
    /// 41 / 63 reads 61 / 83 then 76 / 98; a Raider loses 7 of max and current (+10 infantry: Raiders are tagged infantry
    /// too, so +3 net; the oracle decides); player 0's units never move; a Zealot spawned afterwards reads 98.
    /// </summary>
    [Fact]
    public void TwoHpTechs_OverlappingOnTheZealot_StackAndKeepTheDamage()
    {
        Simulation sim = Sim();
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle full = Place(sim, 1, ZealotT, At(sim, 10, 10));
        EntityHandle hurt = Place(sim, 1, ZealotT, At(sim, 12, 10));
        EntityHandle raider = Place(sim, 1, RaiderT, At(sim, 14, 10));
        EntityHandle theirs = Place(sim, 0, ZealotT, At(sim, 16, 10));
        u.Hp[hurt.Index] = 41;
        u.Hp[raider.Index] -= 3;
        sim.Tick();
        LandAndCheck(w, 1, Prophecy);
        Assert.Equal((83, 61), (u.Hp[full.Index], u.Hp[hurt.Index]));
        LandAndCheck(w, 1, Vigor);
        Assert.Equal((98, 98), (u.Hp[full.Index], w.MaxHp(full.Index)));
        Assert.Equal((76, 98), (u.Hp[hurt.Index], w.MaxHp(hurt.Index)));
        Assert.Equal(w.MaxHp(raider.Index) - 3, u.Hp[raider.Index]);
        Assert.Equal((63, 63), (u.Hp[theirs.Index], w.MaxHp(theirs.Index)));
        EntityHandle later = Place(sim, 1, ZealotT, At(sim, 20, 10));
        Assert.Equal(98, u.Hp[later.Index]);
        sim.Tick();
        Assert.Equal(0, u.Statuses.Count[hurt.Index]); // 76 is above 49
    }

    /// <summary>
    /// The same two techs landing in the other order give the same hit points (the change is computed per tech, against the
    /// flag set before it): order-independent end state.
    /// </summary>
    [Fact]
    public void TwoHpTechs_EitherOrder_SameHitPoints()
    {
        int[] Run(bool prophecyFirst)
        {
            Simulation sim = Sim();
            World w = sim.World;
            EntityHandle a = Place(sim, 1, ZealotT, At(sim, 10, 10));
            EntityHandle b = Place(sim, 1, ZealotT, At(sim, 12, 10));
            EntityHandle c = Place(sim, 1, RaiderT, At(sim, 14, 10));
            w.Units.Hp[b.Index] = 7;
            LandAndCheck(w, 1, prophecyFirst ? Prophecy : Vigor);
            LandAndCheck(w, 1, prophecyFirst ? Vigor : Prophecy);
            return new[] { w.Units.Hp[a.Index], w.Units.Hp[b.Index], w.Units.Hp[c.Index] };
        }
        Assert.Equal(Run(true), Run(false));
    }

    /// <summary>
    /// QA Frailty (-100 Zealot) floors the maximum at 1: every live Zealot of the player is 1 / 1 and alive (the floor, not a
    /// death); Prophecy on top (63 - 100 + 20 = -17, still 1) changes nothing; a Zealot spawned under both reads 1.
    /// </summary>
    [Fact]
    public void ANegativeHpTech_FloorsAt1_AndAPositiveOneUnderTheFloor_ChangesNothing()
    {
        Simulation sim = Sim();
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle full = Place(sim, 1, ZealotT, At(sim, 10, 10));
        EntityHandle hurt = Place(sim, 1, ZealotT, At(sim, 12, 10));
        u.Hp[hurt.Index] = 5;
        LandAndCheck(w, 1, Frailty);
        Assert.Equal((1, 1, 1), (u.Hp[full.Index], u.Hp[hurt.Index], w.MaxHp(full.Index)));
        Assert.True(u.IsAlive(full) && u.IsAlive(hurt));
        LandAndCheck(w, 1, Prophecy);
        Assert.Equal((1, 1), (u.Hp[full.Index], w.MaxHp(full.Index)));
        EntityHandle later = Place(sim, 1, ZealotT, At(sim, 20, 10));
        Assert.Equal(1, u.Hp[later.Index]);
        for (int t = 0; t < 10; t++) sim.Tick(); // a 1 / 1 Zealot is not below 0.5: never Frenzied
        Assert.Equal(0, u.Statuses.Count[full.Index]);
    }

    /// <summary>
    /// A Zealot mid-Burning when Prophecy lands keeps burning: the landing adds 20 once, the pulses go on taking the same
    /// amount as on an un-teched twin, so its hit points stay exactly 20 above the twin's until the burn ends.
    /// </summary>
    [Fact]
    public void AZealotMidBurn_GainsTheChangeOnce_ThePulsesGoOn()
    {
        Simulation a = Sim(), b = Sim();
        EntityHandle za = Place(a, 1, ZealotT, At(a, 10, 10)), zb = Place(b, 1, ZealotT, At(b, 10, 10));
        int burning = Data.FindStatus("burning");
        StatusSystem.Apply(a.World, za.Index, burning, 2f, 200, 0);
        StatusSystem.Apply(b.World, zb.Index, burning, 2f, 200, 0);
        for (int t = 0; t < 30; t++) { a.Tick(); b.Tick(); }
        Assert.True(a.World.Units.Hp[za.Index] < 63, "the burn never pulsed");
        LandAndCheck(a.World, 1, Prophecy);
        for (int t = 0; t < 250; t++)
        {
            a.Tick();
            b.Tick();
            if (!b.World.Units.IsAlive(zb)) break;
            Assert.Equal(b.World.Units.Hp[zb.Index] + 20, a.World.Units.Hp[za.Index]);
        }
    }

    /// <summary>A Priest's summoned Zealots live when Prophecy lands gain the 20 like any other Zealot of the owner (they are ordinary units).</summary>
    [Fact]
    public void SummonedZealots_AliveAtTheLanding_GainTheChange()
    {
        Simulation sim = Sim();
        World w = sim.World;
        EntityHandle priest = Place(sim, 1, PriestT, At(sim, 20, 20));
        sim.Enqueue(Command.UseAbility(1, priest, 1, At(sim, 20, 20)));
        TickOf(sim, priest, resolved: true);
        List<EntityHandle> zealots = SummonTests.Summoned(w);
        Assert.Equal(3, zealots.Count);
        w.Units.Hp[zealots[0].Index] = 30;
        LandAndCheck(w, 1, Prophecy);
        Assert.Equal((50, 83, 83), (w.Units.Hp[zealots[0].Index], w.Units.Hp[zealots[1].Index], w.MaxHp(zealots[1].Index)));
    }

    /// <summary>
    /// The Frenzied line after the fix, through the sim: a Prophecy Zealot (83 max) has its line at 41.5, so 41 / 83 is
    /// Frenzied after a phase 5 and 42 / 83 is not; an un-teched one (63) has 31.5: 31 is, 32 is not.
    /// </summary>
    [Theory]
    [InlineData(true, 41, true)]
    [InlineData(true, 42, false)]
    [InlineData(false, 31, true)]
    [InlineData(false, 32, false)]
    public void TheFrenziedLine_FollowsTheRealMax(bool prophecy, int hp, bool frenzied)
    {
        Simulation sim = Sim();
        World w = sim.World;
        if (prophecy) ProductionSystem.Researched(w, 0, Prophecy);
        EntityHandle z = Place(sim, 0, ZealotT, At(sim, 10, 10));
        Assert.Equal(prophecy ? 83 : 63, w.Units.Hp[z.Index]);
        w.Units.Hp[z.Index] = hp;
        sim.Tick();
        Assert.Equal(frenzied, w.Units.Statuses.Count[z.Index] == 1);
    }

    /// <summary>
    /// BUG-0420's line against a decimal oracle: for every fraction 0.001-0.999 (the loader's float of the JSON double) the
    /// line is exactly k x 1,000 millionths, and for every max hp 1-400 and every hp 0..max, IsBelow is exactly
    /// <c>hp x 1000 &lt; k x max</c> in integers. Then 20,000 seeded six-decimal fractions against <c>hp x 10^6 &lt; m x max</c>
    /// at the hp values around the line.
    /// </summary>
    [Fact]
    public void TheLine_MatchesADecimalOracle_ForEveryThreeDecimalFraction_AndSixDecimalSamples()
    {
        var bad = new List<string>();
        for (int k = 1; k < 1000 && bad.Count < 20; k++)
        {
            float f = (float)(k / 1000.0);
            int line = PassiveSystem.LineOf(f);
            if (line != k * 1000) bad.Add($"LineOf({k / 1000.0}) = {line}");
            for (int max = 1; max <= 400; max++)
                for (int hp = 0; hp <= max; hp++)
                {
                    bool expected = hp * 1000L < (long)k * max;
                    if (PassiveSystem.IsBelow(hp, max, line) != expected) bad.Add($"{k / 1000.0} x {max} at {hp}: expected {expected}");
                }
        }
        var rng = new Determinism.SimRng(4200, 1);
        for (int s = 0; s < 20_000 && bad.Count < 20; s++)
        {
            int m = rng.NextInt(1, 1_000_000);
            float f = (float)(m / 1_000_000.0);
            if (PassiveSystem.LineOf(f) != m) bad.Add($"LineOf({m / 1e6:F6}) = {PassiveSystem.LineOf(f)}");
            int max = rng.NextInt(1, 401);
            long lineHp = (long)m * max / 1_000_000;
            for (long hp = Math.Max(0, lineHp - 1); hp <= Math.Min(max, lineHp + 1); hp++)
            {
                bool expected = hp * 1_000_000L < (long)m * max;
                if (PassiveSystem.IsBelow((int)hp, max, f) != expected) bad.Add($"{m / 1e6:F6} x {max} at {hp}: expected {expected}");
            }
        }
        Assert.True(bad.Count == 0, string.Join("; ", bad));
    }

    /// <summary>
    /// Extreme lines: a max hp at the loader's bound (int sized) times a line of 999,999 millionths does not overflow
    /// (the compare is 64-bit): a unit at max - 1 of a 2,000,000,000 max is below 0.999999, at max is not.
    /// </summary>
    [Fact]
    public void TheLine_HugeMaxHp_DoesNotOverflow()
    {
        const int max = 2_000_000_000;
        float f = (float)0.999999;
        Assert.True(PassiveSystem.IsBelow(max - 2001, max, f));
        Assert.False(PassiveSystem.IsBelow(max - 2000, max, f));
        Assert.False(PassiveSystem.IsBelow(max, max, f));
    }

    /// <summary>
    /// Twins in a 3-player brawl where each player's hp techs land at seeded ticks (as phase 3 would, between ticks on both
    /// twins): equal hashes every tick, and after every tick every live unit has 1 &lt;= hp &lt;= its max.
    /// </summary>
    [Theory]
    [InlineData(11UL)]
    [InlineData(12UL)]
    public void HpTechsLandingMidBrawl_TwinsEqual_HpWithinMaxEveryTick(ulong seed)
    {
        Simulation NewSim()
        {
            var sim = new Simulation(new SimConfig(seed, 3, 96, 1024) { Data = Data }, LocalMovementTests.Flat(48));
            var rng = new Determinism.SimRng(seed, 7);
            for (int p = 0; p < 3; p++)
                for (int k = 0; k < 20; k++)
                {
                    int type = k % 3 == 0 ? RaiderT : ZealotT;
                    Place(sim, p, type, At(sim, 10 + p * 12 + k % 5, 18 + k / 5 + rng.NextInt(0, 3)));
                }
            UnitStore u = sim.World.Units;
            for (int i = 0; i < u.Capacity; i++)
                if (u.Alive[i]) sim.Enqueue(Command.AttackMove(u.Owner[i], new EntityHandle(i, u.Generation[i]), At(sim, 24, 22)));
            return sim;
        }
        Simulation a = NewSim(), b = NewSim();
        var land = new Determinism.SimRng(seed, 9);
        var when = new int[3, 3];
        int[] techs = { Prophecy, Vigor, Frailty };
        for (int p = 0; p < 3; p++)
            for (int t = 0; t < 3; t++) when[p, t] = land.NextInt(5, 400);
        for (int tick = 0; tick < 500; tick++)
        {
            for (int p = 0; p < 3; p++)
                for (int t = 0; t < 3; t++)
                    if (when[p, t] == tick)
                    {
                        ProductionSystem.Researched(a.World, p, techs[t]);
                        ProductionSystem.Researched(b.World, p, techs[t]);
                    }
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"seed {seed}: twins differ after tick {tick}");
            UnitStore u = a.World.Units;
            for (int i = 0; i < u.Capacity; i++)
                if (u.Alive[i]) Assert.InRange(u.Hp[i], 1, a.World.MaxHp(i));
        }
    }
}
