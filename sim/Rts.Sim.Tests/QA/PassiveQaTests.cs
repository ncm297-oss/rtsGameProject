using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Abilities;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using static Rts.Sim.Tests.AbilityScenes;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M4-4b-3a (session 2026-10-10-1015): attacks on Frenzied, the Zealot's passives and the selfAura kind beyond the
/// developer's rows: a brute-force Martyrdom oracle in real phase-11 combat, a 20-Zealot same-tick death blob, hp
/// boundaries (tech mid-fight, 1 hp, single-precision lines), four statuses at once, eight statuses with two frenzies,
/// expiry on the swing tick swept over durations, and selfAura casters that die, are Blinded or are shoved mid-cast.
/// </summary>
public class PassiveQaTests
{
    private static int Zealot => TestSim.Data.FindUnit("whirlwind_zealot");

    private static int Frenzied => TestSim.Data.FindStatus("frenzied");

    // ---------------------------------------------------------------- Martyrdom oracle in real combat

    /// <summary>
    /// Three players brawl on a flat map: player 0 and player 2 field Zealot blobs, player 1 Heavy Infantry. Every death is
    /// a phase-11 melee death, so the end-of-tick positions are the positions at each death. After every tick, a live
    /// Zealot has a 100-tick Frenzied entry exactly when an own Zealot died this tick with its center within 6 m (the edge
    /// counts) - brute force over the death events, no spatial hash. Twins agree every tick.
    /// </summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void MartyrdomOracle_InPhase11Combat_MatchesBruteForceEveryTick(ulong seed)
    {
        Simulation a = Brawl(seed), b = Brawl(seed);
        World w = a.World;
        UnitStore u = w.Units;
        int zealot = Zealot, frenzied = Frenzied, zealotDeaths = 0, martyred = 0, checkedTicks = 0;
        for (int t = 0; t < 900; t++)
        {
            a.Tick();
            b.Tick();
            Assert.Equal(a.StateHash(), b.StateHash());
            ReadOnlySpan<DeathEvent> deaths = w.Deaths;
            int zd = 0;
            foreach (DeathEvent d in deaths) if (!d.IsBuilding && d.VictimType == zealot) zd++;
            zealotDeaths += zd;
            if (zd > 0) checkedTicks++;
            for (int j = 0; j < u.Capacity; j++)
            {
                if (!u.Alive[j] || u.TypeId[j] != zealot) continue;
                bool expected = false;
                foreach (DeathEvent d in deaths)
                    if (!d.IsBuilding && d.VictimType == zealot && d.VictimOwner == u.Owner[j] && Vector2.DistanceSquared(u.Position[j], d.Position) <= 36f)
                        expected = true;
                int at = u.Statuses.IndexOf(j, frenzied);
                bool got = at >= 0 && u.Statuses.TicksRemaining[at] == 100;
                if (got) martyred++;
                Assert.True(expected == got, $"seed {seed} tick {t}: Zealot {j} (owner {u.Owner[j]}) expected Martyrdom {expected}, has {(at < 0 ? "none" : u.Statuses.TicksRemaining[at] + " ticks")}");
                if (at >= 0)
                {
                    Assert.Equal(0.5f, u.Statuses.Magnitude[at]);
                    Assert.Equal(u.Owner[j], u.Statuses.SourcePlayer[at]);
                    Assert.True(u.Statuses.TicksRemaining[at] is >= 1 and <= 100);
                }
                Assert.True(u.Statuses.Count[j] <= 1, $"Zealot {j} has {u.Statuses.Count[j]} statuses");
            }
            // Only Zealots are ever Frenzied (Martyrdom lists them; the passive is theirs).
            for (int j = 0; j < u.Capacity; j++)
                if (u.Alive[j] && u.TypeId[j] != zealot) Assert.Equal(0, u.Statuses.Count[j]);
        }
        Assert.True(zealotDeaths > 20 && martyred > 20 && checkedTicks > 5, $"seed {seed}: {zealotDeaths} Zealot deaths over {checkedTicks} ticks, {martyred} Martyrdom unit-ticks");
    }

    private static Simulation Brawl(ulong seed)
    {
        var sim = new Simulation(TestSim.Config(Seed: seed, PlayerCount: 3, UnitCapacity: 160, CommandCapacity: 1024), LocalMovementTests.Flat(64));
        var rng = new Determinism.SimRng(seed, 77);
        int heavy = HeavyInfantry, zealot = Zealot;
        Vector2 c = At(sim, 32, 32);
        Vector2[] centres = { c + new Vector2(-9f, 0f), c + new Vector2(9f, 0f), c + new Vector2(0f, 9f) };
        for (int p = 0; p < 3; p++)
            for (int k = 0; k < 40; k++)
            {
                var off = new Vector2(rng.NextInt(-40, 41) / 10f, rng.NextInt(-40, 41) / 10f);
                Place(sim, p, p == 1 ? heavy : zealot, centres[p] + off);
            }
        UnitStore u = sim.World.Units;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i]) sim.Enqueue(Command.AttackMove(u.Owner[i], new EntityHandle(i, u.Generation[i]), c));
        return sim;
    }

    // ---------------------------------------------------------------- 20 same-tick deaths in a blob

    /// <summary>
    /// 40 Zealots of player 0 in a 3 m blob; 20 of them burn to death in one phase-5 pulse. Every survivor ends that tick
    /// with exactly one Frenzied entry (+50 %, 99 or 100 ticks by slot order against the deaths, owner 0) and nothing else;
    /// the store never holds more than one entry per survivor; the passive count drops by exactly 20.
    /// </summary>
    [Fact]
    public void TwentyZealotsDyingInOneTick_InA3mBlob_EverySurvivorGetsExactlyOneEntry()
    {
        Simulation sim = NoFights(units: 64);
        UnitStore u = sim.World.Units;
        Vector2 p = At(sim, 24, 24);
        var rng = new Determinism.SimRng(5, 1);
        var all = new List<EntityHandle>();
        for (int k = 0; k < 40; k++)
        {
            Vector2 off;
            do off = new Vector2(rng.NextInt(-30, 31) / 10f, rng.NextInt(-30, 31) / 10f);
            while (off.LengthSquared() > 9f);
            all.Add(Place(sim, 0, Zealot, p + off));
        }
        sim.Tick();
        // Interleave the doomed and the survivors by slot, so some survivors come before a death in slot order and some after.
        var doomed = new List<EntityHandle>();
        var survivors = new List<EntityHandle>();
        for (int k = 0; k < all.Count; k++) (k % 2 == 0 ? doomed : survivors).Add(all[k]);
        foreach (EntityHandle h in doomed) StatusSystem.Apply(sim.World, h.Index, Burning, 100_000f, 30, 1);
        int deathTick = -1;
        for (int t = 0; t < 30 && deathTick < 0; t++)
        {
            sim.Tick();
            if (sim.World.Deaths.Length > 0) deathTick = t;
        }
        Assert.Equal(20, sim.World.Deaths.Length);
        Assert.Equal(20, u.BelowHpUnits);
        foreach (EntityHandle h in doomed) Assert.False(u.IsAlive(h));
        foreach (EntityHandle h in survivors)
        {
            Assert.True(u.IsAlive(h));
            Assert.Equal(1, u.Statuses.Count[h.Index]);
            (float mag, int ticks) = StatusOf(sim, h, Frenzied);
            Assert.Equal(0.5f, mag);
            Assert.True(ticks is 99 or 100, $"survivor {h.Index}: {ticks} ticks");
            Assert.Equal(0, u.Statuses.SourcePlayer[u.Statuses.IndexOf(h.Index, Frenzied)]);
        }
        // Run it out: the Frenzied ends and nothing lingers.
        for (int t = 0; t < 101; t++) sim.Tick();
        foreach (EntityHandle h in survivors) Assert.Equal(0, u.Statuses.Count[h.Index]);
    }

    // ---------------------------------------------------------------- hp boundaries

    /// <summary>A Zealot at 41 / 63 is above its 31.5 line; Dryjhna's Prophecy finishing mid-fight moves the line to 41.5 and the next phase 5 frenzies it.</summary>
    [Fact]
    public void ProphecyResearchedMidFight_FrenziesAZealotNowBelowTheNewLine_NextTick()
    {
        Simulation sim = NoFights();
        UnitStore u = sim.World.Units;
        EntityHandle z = Place(sim, 0, Zealot, At(sim, 10, 10));
        u.Hp[z.Index] = 41;
        for (int t = 0; t < 5; t++)
        {
            sim.Tick();
            Assert.Equal(0, u.Statuses.Count[z.Index]);
        }
        sim.World.Techs.Set(0, sim.World.Data.FindTech("dryjhnas_prophecy"), true);
        sim.Tick();
        Assert.Equal((0.5f, 1), StatusOf(sim, z, Frenzied));
        Assert.Equal(83, PassiveSystem.MaxHp(sim.World, z.Index));
    }

    /// <summary>A Zealot at 1 hp is Frenzied; its death (one more hit) gives Martyrdom to the Zealot beside it and leaves no entry behind in its slot.</summary>
    [Fact]
    public void AZealotAt1Hp_IsFrenzied_AndItsDeathMartyrsItsNeighbour()
    {
        Simulation sim = NoFights();
        UnitStore u = sim.World.Units;
        EntityHandle z = Place(sim, 0, Zealot, At(sim, 10, 10));
        EntityHandle n = Place(sim, 0, Zealot, At(sim, 10, 10, 1f));
        u.Hp[z.Index] = 1;
        sim.Tick();
        Assert.Equal((0.5f, 1), StatusOf(sim, z, Frenzied));
        CombatSystem.HitUnit(sim.World, new PendingHit(default, 1, z, false, 1));
        Assert.False(u.IsAlive(z));
        Assert.Equal(0, u.Statuses.Count[z.Index]);
        Assert.Equal((0.5f, 100), StatusOf(sim, n, Frenzied));
        Assert.Equal(1, u.BelowHpUnits);
    }

    /// <summary>
    /// The boundary rule in single precision: for every two- and three-decimal fraction in (0, 1) and every max hp 1-400
    /// where fraction x max hp is a whole number in decimal, a unit at exactly that hp is not below the line (the line is
    /// computed from the loader's float of the JSON double, as the sim does).
    /// </summary>
    [Fact(Skip = "BUG-0420: hpFraction x maxHp in single precision lands above whole lines (0.15 x 100 = 15.000001), so exactly-at-the-line is below")]
    public void ExactlyAtTheLine_IsNeverBelow_ForDecimalFractions()
    {
        var bad = new List<string>();
        for (int k = 1; k < 1000; k++)
        {
            float fraction = (float)(k / 1000.0);
            for (int max = 1; max <= 400; max++)
            {
                if (k * max % 1000 != 0) continue;
                int hp = k * max / 1000;
                if (PassiveSystem.IsBelow(hp, max, fraction)) bad.Add($"{k / 1000.0} x {max} = {hp}");
                if (!PassiveSystem.IsBelow(hp - 1, max, fraction) && hp > 0) bad.Add($"{k / 1000.0} x {max}: {hp - 1} not below");
            }
        }
        Assert.True(bad.Count == 0, string.Join("; ", bad.Take(20)) + $" ({bad.Count} in all)");
    }

    // ---------------------------------------------------------------- many statuses on one unit

    /// <summary>
    /// A Zealot in a duel carries Frenzied, Slowed, Burning and Blinded at once: four entries, the swing at 13 ticks, the
    /// speed at 70 %, the blind in force, the burn pulsing; each ends on its own clock and the swing goes back to 20.
    /// </summary>
    [Fact]
    public void FrenziedSlowedBurningBlinded_AtOnce_EachDoesItsOwnThing()
    {
        Simulation sim = Duel(out EntityHandle z, out _);
        World w = sim.World;
        UnitStore u = w.Units;
        u.Hp[z.Index] = 10_000; // well above the passive's line: only the applied Frenzied
        int slowed = w.Data.FindStatus("slowed"), blinded = w.Data.FindStatus("blinded");
        StatusSystem.Apply(w, z.Index, Frenzied, 0.5f, 60, 1);
        StatusSystem.Apply(w, z.Index, slowed, 0.3f, 60, 1);
        StatusSystem.Apply(w, z.Index, Burning, 1f, 60, 1);
        StatusSystem.Apply(w, z.Index, blinded, 0f, 60, 1);
        Assert.Equal(4, u.Statuses.Count[z.Index]);
        Assert.Equal(blinded, u.Statuses.BlindOf(z.Index));
        Assert.Equal(w.Data.Units[Zealot].SpeedPerTick * 0.7f, u.Speed[z.Index]);
        int hp0 = u.Hp[z.Index];
        var cooldowns = new List<(int Cooldown, int Count)>();
        for (int t = 0; t < 140; t++)
        {
            int before = u.CooldownTicks[z.Index];
            sim.Tick();
            if (u.CooldownTicks[z.Index] > before) cooldowns.Add((u.CooldownTicks[z.Index], u.Statuses.Count[z.Index]));
        }
        Assert.True(u.Hp[z.Index] < hp0, "the burn never pulsed");
        Assert.Equal(0, u.Statuses.Count[z.Index]);
        Assert.Equal(-1, u.Statuses.BlindOf(z.Index));
        Assert.Equal(w.Data.Units[Zealot].SpeedPerTick, u.Speed[z.Index]);
        Assert.Contains(cooldowns, c => c.Count == 4);
        Assert.Contains(cooldowns, c => c.Count == 0);
        foreach ((int cd, int count) in cooldowns) Assert.Equal(count == 4 ? 13 : 20, cd);
    }

    /// <summary>
    /// Eight statuses on one unit, two of them frenzies (+20 % and +50 %): the 50 % rules (13 ticks); a ninth is dropped;
    /// when the +50 % ends first the +20 % rules (17 ticks); then 20.
    /// </summary>
    [Fact]
    public void EightStatuses_TwoFrenzies_TheLargestRules_ThenTheOther_AndANinthIsDropped()
    {
        GameData d = s_eight.Value;
        Simulation sim = Duel(out EntityHandle z, out _, d);
        World w = sim.World;
        UnitStore u = w.Units;
        u.Hp[z.Index] = 10_000;
        string[] fillers = { "zz_qa_a", "zz_qa_b", "zz_qa_c", "zz_qa_d", "zz_qa_e", "zz_qa_f" };
        foreach (string f in fillers) StatusSystem.Apply(w, z.Index, d.FindStatus(f), 0.01f, 400, 1);
        StatusSystem.Apply(w, z.Index, d.FindStatus("frenzied"), 0.5f, 60, 1);
        StatusSystem.Apply(w, z.Index, d.FindStatus("zz_qa_frenzy"), 0.2f, 200, 1);
        Assert.Equal(StatusStore.PerUnit, u.Statuses.Count[z.Index]);
        StatusSystem.Apply(w, z.Index, d.FindStatus("burning"), 1f, 100, 1); // a ninth: dropped
        Assert.Equal(StatusStore.PerUnit, u.Statuses.Count[z.Index]);
        Assert.Equal(-1, u.Statuses.IndexOf(z.Index, d.FindStatus("burning")));
        var seen = new List<(int Tick, int Cooldown)>();
        for (int t = 0; t < 460; t++)
        {
            int before = u.CooldownTicks[z.Index];
            sim.Tick();
            if (u.CooldownTicks[z.Index] > before) seen.Add((t, u.CooldownTicks[z.Index]));
        }
        Assert.Contains(seen, s => s.Cooldown == 13);
        Assert.Contains(seen, s => s.Cooldown == 17);
        Assert.Contains(seen, s => s.Cooldown == 20);
        // In that order, never back.
        int phase = 0;
        foreach ((_, int cd) in seen)
        {
            int ph = cd == 13 ? 0 : cd == 17 ? 1 : cd == 20 ? 2 : -1;
            Assert.True(ph >= phase, $"cooldown {cd} after phase {phase}");
            phase = ph;
        }
    }

    private static readonly Lazy<GameData> s_eight = new(() =>
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("common/statuses.json", root =>
        {
            JsonArray list = root["statuses"]!.AsArray();
            list.Add(new JsonObject { ["id"] = "zz_qa_frenzy", ["displayName"] = "QA Frenzy", ["description"] = "Test only.", ["kind"] = "frenzied" });
            foreach (string f in new[] { "zz_qa_a", "zz_qa_b", "zz_qa_c", "zz_qa_d", "zz_qa_e", "zz_qa_f" })
                list.Add(new JsonObject { ["id"] = f, ["displayName"] = "QA Slow", ["description"] = "Test only.", ["kind"] = "slow" });
        });
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        return r.Data!;
    });

    /// <summary>
    /// Expiry against the swing, swept: for Frenzied durations 1-45 ticks, every swing's cooldown is 13 when the entry was
    /// still on the unit at that tick's end (statuses only end in phase 5, before the swing) and 20 otherwise, including a
    /// duration that ends on the swing tick itself.
    /// </summary>
    [Fact]
    public void ExpiryOnTheSwingTick_Swept_TheSwingUsesWhatIsInForceAtPhase10()
    {
        int onSwingTick = 0;
        for (int duration = 1; duration <= 45; duration++)
        {
            Simulation sim = Duel(out EntityHandle z, out _);
            UnitStore u = sim.World.Units;
            u.Hp[z.Index] = 10_000;
            for (int t = 0; t < 8; t++) sim.Tick(); // into the swing rhythm
            StatusSystem.Apply(sim.World, z.Index, Frenzied, 0.5f, duration, 0);
            for (int t = 0; t < 80; t++)
            {
                int before = u.CooldownTicks[z.Index];
                bool hadBefore = u.Statuses.Count[z.Index] > 0;
                sim.Tick();
                if (u.CooldownTicks[z.Index] <= before) continue;
                bool has = u.Statuses.Count[z.Index] > 0;
                if (hadBefore && !has) onSwingTick++;
                Assert.Equal(has ? 13 : 20, u.CooldownTicks[z.Index]);
            }
        }
        Assert.True(onSwingTick > 0, "no duration ended on a swing tick");
    }

    /// <summary>A Zealot attacking a never-retaliating Battering Ram with a million hp (as FrenziedTests), on player 0.</summary>
    private static Simulation Duel(out EntityHandle zealot, out EntityHandle dummy, GameData? data = null)
    {
        Simulation sim = data == null ? Flat() : new Simulation(new SimConfig(1, 2, 64, 544) { Data = data }, LocalMovementTests.Flat(48));
        GameData d = sim.World.Data;
        zealot = Place(sim, 0, d.FindUnit("whirlwind_zealot"), At(sim, 10, 10));
        dummy = Place(sim, 1, d.FindUnit("whirlwind_battering_ram"), At(sim, 10, 10, 1.5f));
        sim.World.Units.Hp[dummy.Index] = 1_000_000;
        sim.Enqueue(Command.Attack(0, zealot, dummy, false));
        return sim;
    }

    // ---------------------------------------------------------------- selfAura casters under attack

    private static Simulation AuraSim(bool combat = false) =>
        TestSim.Explored(new Simulation(new SimConfig(1, 2, 96, 1024) { Data = SelfAuraTests.Data, Combat = combat }, LocalMovementTests.Flat(48)));

    private static int Infantry => SelfAuraTests.Data.FindUnit("malazan_heavy_infantry");

    /// <summary>A selfAura caster killed mid-cast: no resolve, no status lands, no cooldown, the caster count returns to 0, and the slot's reuse starts clean.</summary>
    [Fact]
    public void ACasterKilledMidCast_NeverResolves_AndLeavesNoCastState()
    {
        Simulation sim = AuraSim();
        UnitStore u = sim.World.Units;
        Vector2 p = At(sim, 20, 20);
        EntityHandle caster = Place(sim, 0, Infantry, p);
        EntityHandle friend = Place(sim, 0, Infantry, Off(p, 2f));
        sim.Enqueue(Command.UseAbility(0, caster, 0, p));
        TickOf(sim, caster, resolved: false);
        sim.Tick();
        CombatSystem.HitUnit(sim.World, new PendingHit(default, 1, caster, false, u.Hp[caster.Index]));
        Assert.False(u.IsAlive(caster));
        for (int t = 0; t < 30; t++)
        {
            sim.Tick();
            Assert.False(Had(sim, caster, resolved: true));
        }
        Assert.Equal(0, u.Statuses.Count[friend.Index]);
        Assert.Equal(0, u.CasterCount);
        EntityHandle again = Place(sim, 0, Infantry, Off(p, -3f));
        Assert.Equal(-1, u.CastAbility[again.Index]);
        Assert.Equal(0, u.AbilityReadyTick[again.Index * DataLimits.MaxUnitAbilities]);
    }

    /// <summary>A Blinded caster still casts its selfAura (blindness limits sight and attack reach, not casting): the rally lands on itself and a friend 3 m away.</summary>
    [Fact]
    public void ABlindedCaster_StillResolvesItsAura()
    {
        Simulation sim = AuraSim();
        UnitStore u = sim.World.Units;
        Vector2 p = At(sim, 20, 20);
        EntityHandle caster = Place(sim, 0, Infantry, p);
        EntityHandle friend = Place(sim, 0, Infantry, Off(p, 3f));
        StatusSystem.Apply(sim.World, caster.Index, SelfAuraTests.Data.FindStatus("blinded"), 0f, 400, 1);
        sim.Enqueue(Command.UseAbility(0, caster, 0, p));
        TickOf(sim, caster, resolved: true);
        Assert.Equal(0.5f, StatusOf(sim, caster, SelfAuraTests.Data.FindStatus("frenzied")).Magnitude);
        Assert.Equal(0.5f, StatusOf(sim, friend, SelfAuraTests.Data.FindStatus("frenzied")).Magnitude);
    }

    /// <summary>
    /// A column of 30 own units walks straight through a casting selfAura caster. The caster stays planted, and the resolve
    /// lands at where the caster stood when phase 6 resolved it (its position at the start of that tick, since only commands
    /// and phase 8-9 move a unit and none came): the event's point equals the tick's previous position, and the rally reaches
    /// exactly the own units within 4 m of that point at phase 6 (none moved between the tick start and phase 6 either).
    /// </summary>
    [Fact]
    public void ACasterShovedByACrowdDuringTheCast_ResolvesWhereItStandsAtTheResolve()
    {
        Simulation sim = AuraSim();
        UnitStore u = sim.World.Units;
        Vector2 p = At(sim, 24, 24);
        EntityHandle caster = Place(sim, 0, Infantry, p);
        var crowd = new List<EntityHandle>();
        for (int k = 0; k < 30; k++) crowd.Add(Place(sim, 0, Infantry, Off(p, -8f - (k % 3) * 1.1f, (k / 3 - 4.5f) * 1.1f)));
        sim.Tick();
        foreach (EntityHandle h in crowd) sim.Enqueue(Command.Move(0, h, Off(p, 14f, 0f)));
        for (int t = 0; t < 6; t++) sim.Tick();
        sim.Enqueue(Command.UseAbility(0, caster, 0, Off(p, 30f, 30f)));
        int frenzied = SelfAuraTests.Data.FindStatus("frenzied");
        Vector2[] before = new Vector2[u.Capacity];
        bool resolved = false;
        for (int t = 0; t < 60 && !resolved; t++)
        {
            for (int i = 0; i < u.Capacity; i++) before[i] = u.Position[i];
            int[] had = new int[u.Capacity];
            for (int i = 0; i < u.Capacity; i++) had[i] = u.Statuses.IndexOf(i, frenzied);
            sim.Tick();
            foreach (AbilityEvent e in sim.World.AbilityEvents)
            {
                if (!e.Resolved || e.Caster != caster) continue;
                resolved = true;
                Assert.Equal(before[caster.Index], e.Point);
                for (int i = 0; i < u.Capacity; i++)
                {
                    if (!u.Alive[i]) continue;
                    bool inside = Vector2.DistanceSquared(before[i], e.Point) <= 16f;
                    bool got = had[i] < 0 && u.Statuses.IndexOf(i, frenzied) >= 0;
                    Assert.True(inside == got, $"unit {i} {Vector2.Distance(before[i], e.Point):F3} m from the resolve: got {got}");
                }
            }
        }
        Assert.True(resolved, "the cast never resolved (lost to the crowd?)");
        // A casting unit is planted: the crowd flowing through never shoved it (so "shoved mid-cast" can't happen; the
        // resolve-where-it-stands rule is covered by SelfAuraTests' position seam).
        Assert.Equal(p, u.Position[caster.Index]);
    }
}
