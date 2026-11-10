using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Abilities;
using Rts.Sim.Ai;
using Rts.Sim.Combat;
using Rts.Sim.Data;
using Rts.Sim.Economy;
using Rts.Sim.Entities;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M7-1 (session 2026-11-09-2226): regeneration attacked past the developer's rows. Fractional rates (0.05, 0.3, 1.5,
/// 7.7, 100 HP/s) over ten minutes against the exact integer formula; a hit stamped on the exact tick the delay ends; a
/// damage-over-time pulse (phase 5, before regeneration in the same tick) holding the faction rate off for its whole length;
/// death mid-regen and the freed slot's next occupant; a summoned unit that expires mid-regen; an Hp tech researched with
/// the unit at its old maximum and with a fraction carried; loader edges the developer's rows leave out; and a seeded
/// invariant fuzz over AI matches with regeneration (hp within 1..MaxHp, the accumulator in 0..399, never healed by the
/// faction inside its delay, never healed without a source, freed slots clean, the regenerating count exact). The long
/// fuzz, the 50-save round trip, the 500-unit cross-process digest and the 2,500-unit scale row are in
/// <c>Stress/RegenQaStressTests.cs</c>.
/// </summary>
[Collection(SerialCollection.Name)]
public class RegenQaTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private const int One = CombatConstants.RegenFixedOne;

    /// <summary>Malazan regenerates <paramref name="rate"/> HP/s with no delay; Heavy Infantry has 10,000 hp, so ten minutes never reach the top.</summary>
    private static GameData DriftData(double rate) => AiScenes.DataWith(dir =>
    {
        dir.EditJson("factions/malazan/faction.json", r => r["regen"] = new JsonObject { ["hpPerSecond"] = rate, ["delay"] = 0 });
        dir.SetUnitField("malazan", "malazan_heavy_infantry", "hp", "10000");
    });

    private static int Heavy(GameData d) => d.FindUnit("malazan_heavy_infantry");

    private static EntityHandle Hi(Simulation sim, int x = 20, int y = 20) =>
        CombatScenes.Place(sim, 0, Heavy(sim.World.Data), CombatScenes.At(sim, x, y));

    private static void Through(Simulation sim, int k)
    {
        while (sim.TickNumber <= k) sim.Tick();
    }

    /// <summary>
    /// No drift at any rate on the 0.05 HP/s step over ten minutes (12,000 ticks): after the n-th regenerating tick the unit has
    /// exactly <c>1 + floor(perTick x n / 400)</c> hp and <c>perTick x n mod 400</c> in the accumulator, every tick, until the
    /// top (then the remainder is 0). Delay 0: the hit's own tick already heals.
    /// </summary>
    [Theory]
    [InlineData(0.05, 1)]
    [InlineData(0.3, 6)]
    [InlineData(1.5, 30)]
    [InlineData(7.7, 154)]
    [InlineData(100, 2000)]
    public void AFractionalRate_HealsByTheExactIntegerFormula_ForTenMinutes(double rate, int perTick)
    {
        GameData d = DriftData(rate);
        Assert.Equal((perTick, 0), (d.Factions[d.FindFaction("malazan")].RegenPerTick, d.Factions[d.FindFaction("malazan")].RegenDelayTicks));
        Simulation sim = RegenTests.Scene(d);
        UnitStore u = sim.World.Units;
        EntityHandle h = Hi(sim);
        Assert.Equal(10_000, u.Hp[h.Index]);
        sim.Tick();
        RegenTests.Hit(sim, h, 9_999);
        for (int n = 1; n <= 12_000; n++)
        {
            sim.Tick();
            long total = (long)perTick * n;
            int hp = (int)Math.Min(10_000, 1 + total / One);
            int acc = hp == 10_000 ? 0 : (int)(total % One);
            if (u.Hp[h.Index] != hp || u.RegenAccum[h.Index] != acc)
                Assert.Fail($"{rate} HP/s after {n} ticks: hp {u.Hp[h.Index]} acc {u.RegenAccum[h.Index]}, want {hp} / {acc}");
        }
        // Ten minutes at the nominal rate, to the hit point (the step is exact for every rate on the 0.05 grid).
        Assert.Equal((int)Math.Min(10_000, 1 + Math.Floor(rate * 600 + 1e-9)), u.Hp[h.Index]);
    }

    /// <summary>
    /// A hit whose tick is exactly the one the delay would end on (t + 120) restarts it: nothing that tick and nothing for 119
    /// more; the next heals. A hit one tick later (t + 121, after one regenerating tick) keeps the 20 units already gained.
    /// </summary>
    [Fact]
    public void AHit_OnTheExactTickTheDelayEnds_RestartsIt_AndALaterOneKeepsTheFraction()
    {
        Simulation sim = RegenTests.Scene(RegenTests.s_regen.Value);
        UnitStore u = sim.World.Units;
        EntityHandle h = Hi(sim);
        sim.Tick();
        int t = sim.TickNumber;
        RegenTests.Hit(sim, h, 30);
        Through(sim, t + 119);
        Assert.Equal((100, 0), (u.Hp[h.Index], u.RegenAccum[h.Index]));
        RegenTests.Hit(sim, h, 1); // stamped t + 120
        Assert.Equal(t + 120, u.LastDamagedTick[h.Index]);
        Through(sim, t + 239);
        Assert.Equal((99, 0), (u.Hp[h.Index], u.RegenAccum[h.Index]));
        Through(sim, t + 240);
        Assert.Equal((99, 20), (u.Hp[h.Index], u.RegenAccum[h.Index]));
        RegenTests.Hit(sim, h, 1); // stamped t + 241
        Through(sim, t + 360);
        Assert.Equal((98, 20), (u.Hp[h.Index], u.RegenAccum[h.Index]));
        Through(sim, t + 361);
        Assert.Equal((98, 40), (u.Hp[h.Index], u.RegenAccum[h.Index]));
    }

    /// <summary>
    /// A damage-over-time pulse lands in phase 5 before regeneration in the same tick: while a 10 s Burning lasts the faction
    /// rate never applies (its pulses come every 20 ticks, inside the 120-tick delay); after the last pulse at tick P the
    /// unit holds until P + 119 and heals from P + 120.
    /// </summary>
    [Fact]
    public void ADamageOverTime_HoldsTheFactionRateOff_UntilTheDelayAfterItsLastPulse()
    {
        GameData d = RegenTests.s_regen.Value;
        Simulation sim = RegenTests.Scene(d);
        UnitStore u = sim.World.Units;
        EntityHandle h = Hi(sim);
        sim.Tick();
        RegenTests.Hit(sim, h, 40);
        StatusSystem.Apply(sim.World, h.Index, d.FindStatus("burning"), 1f, 200, 1);
        int last = u.Hp[h.Index];
        for (int k = 0; k < 220; k++)
        {
            sim.Tick();
            Assert.True(u.Hp[h.Index] <= last, $"healed during the burn at tick {sim.TickNumber - 1}");
            last = u.Hp[h.Index];
            Assert.Equal(0, u.RegenAccum[h.Index]);
        }
        Assert.Equal(0, u.Statuses.Count[h.Index]);
        int p = u.LastDamagedTick[h.Index];
        Assert.True(p > 0 && u.Hp[h.Index] < 90, $"no pulse landed (last {p}, hp {u.Hp[h.Index]})");
        int hp = u.Hp[h.Index];
        Through(sim, p + 119);
        Assert.Equal((hp, 0), (u.Hp[h.Index], u.RegenAccum[h.Index]));
        Through(sim, p + 120);
        Assert.Equal((hp, 20), (u.Hp[h.Index], u.RegenAccum[h.Index]));
    }

    /// <summary>
    /// Death mid-regen: a unit with a fraction carried, a recent hit and a regenerating status is killed; its slot is clean
    /// (accumulator 0, never damaged, no regenerating entry, the count back to 0 so the pass skips again) and the next unit
    /// placed into it starts at its maximum and stays there with nothing to hash.
    /// </summary>
    [Fact]
    public void DeathMidRegen_LeavesAFreedSlotClean_ForItsNextOccupant()
    {
        GameData d = RegenTests.s_regen15.Value;
        Simulation sim = RegenTests.Scene(d);
        UnitStore u = sim.World.Units;
        EntityHandle h = Hi(sim);
        sim.Tick();
        RegenTests.Hit(sim, h, 50);
        Through(sim, sim.TickNumber + 130);
        StatusSystem.Apply(sim.World, h.Index, d.FindStatus("regenerating"), 1.5f, 400, 0);
        sim.Tick();
        RegenTests.Hit(sim, h, 1);
        sim.Tick();
        Assert.True(u.RegenAccum[h.Index] != 0 && u.LastDamagedTick[h.Index] >= 0 && u.Statuses.HasRegen(h.Index), "the scene has no regen state to leak");
        Assert.Equal(1, u.Statuses.RegeneratingUnits);
        RegenTests.Hit(sim, h, 10_000);
        Assert.False(u.IsAlive(h));
        Assert.Equal((0, -1, false, 0), (u.RegenAccum[h.Index], u.LastDamagedTick[h.Index], u.Statuses.HasRegen(h.Index), u.Statuses.RegeneratingUnits));
        sim.Tick();
        EntityHandle next = Hi(sim);
        Assert.Equal(h.Index, next.Index);
        Assert.NotEqual(h.Generation, next.Generation);
        Assert.Equal((130, 0, -1, false), (u.Hp[next.Index], u.RegenAccum[next.Index], u.LastDamagedTick[next.Index], u.Statuses.HasRegen(next.Index)));
        for (int k = 0; k < 300; k++)
        {
            sim.Tick();
            Assert.Equal((130, 0, -1), (u.Hp[next.Index], u.RegenAccum[next.Index], u.LastDamagedTick[next.Index]));
        }
    }

    /// <summary>
    /// A unit at 1 hp killed by a damage-over-time pulse in phase 5 is not healed back by regeneration later in the same
    /// phase (it is freed at once), with both a faction rate and a regenerating status on it.
    /// </summary>
    [Fact]
    public void AUnitKilledByAPulse_IsNotHealedInTheSamePhase()
    {
        GameData d = RegenTests.s_regen.Value;
        Simulation sim = RegenTests.Scene(d);
        UnitStore u = sim.World.Units;
        EntityHandle h = Hi(sim);
        sim.Tick();
        RegenTests.Hit(sim, h, 29);
        int burning = d.FindStatus("burning");
        StatusSystem.Apply(sim.World, h.Index, d.FindStatus("regenerating"), 100f, 400, 0);
        StatusSystem.Apply(sim.World, h.Index, burning, 5f, 400, 1);
        StatusStore s = u.Statuses;
        int head = h.Index * StatusStore.PerUnit, at = -1;
        for (int k = 0; k < s.Count[h.Index]; k++)
            if (s.StatusId[head + k] == burning) at = head + k;
        Assert.True(at >= 0);
        while (s.PulseTicks[at] != 1) sim.Tick(); // the next tick pulses
        u.Hp[h.Index] = 1; // test seam: the pulse is lethal; 100 HP/s (5 HP a tick) would lift it if regeneration ran on the dead
        sim.Tick();
        Assert.False(u.IsAlive(h));
        Assert.Equal((0, 0, -1, false, 0), (u.Hp[h.Index], u.RegenAccum[h.Index], u.LastDamagedTick[h.Index], s.HasRegen(h.Index), s.RegeneratingUnits));
    }

    /// <summary>
    /// A summoned unit (an expiry tick) of a regenerating faction, mid-regen with a regenerating status, expires through the
    /// death path: its slot is clean and the summoned and regenerating counts are back to 0.
    /// </summary>
    [Fact]
    public void ASummonedUnit_ExpiringMidRegen_LeavesItsSlotClean()
    {
        GameData d = RegenTests.s_regen15.Value;
        Simulation sim = RegenTests.Scene(d);
        UnitStore u = sim.World.Units;
        sim.Tick();
        int type = Heavy(d);
        Assert.True(u.TrySpawn(0, type, d.Units[type], CombatScenes.At(sim, 20, 20), sim.TickNumber + 200, out EntityHandle s));
        Assert.Equal(1, u.SummonedUnits);
        RegenTests.Hit(sim, s, 60);
        StatusSystem.Apply(sim.World, s.Index, d.FindStatus("regenerating"), 0.3f, 1000, 0);
        Through(sim, sim.TickNumber + 150);
        Assert.True(u.IsAlive(s));
        Assert.True(u.RegenAccum[s.Index] != 0 || u.Hp[s.Index] > 70, "the summon never regenerated");
        Through(sim, sim.TickNumber + 60);
        Assert.False(u.IsAlive(s));
        Assert.Equal((0, -1, false, 0, 0), (u.RegenAccum[s.Index], u.LastDamagedTick[s.Index], u.Statuses.HasRegen(s.Index), u.Statuses.RegeneratingUnits, u.SummonedUnits));
    }

    /// <summary>
    /// An Hp tech researched with the unit at its old maximum: it moves to the new maximum with it and never changes after,
    /// never above it; one researched with a fraction carried (129 hp, 380 units) heals the rest to the new top and stops.
    /// </summary>
    [Fact]
    public void AnHpTech_WithTheUnitAtItsOldMaximum_OrAFractionCarried_NeverOvershoots()
    {
        GameData d = RegenTests.s_regen.Value;
        Simulation sim = RegenTests.Scene(d);
        UnitStore u = sim.World.Units;
        EntityHandle full = Hi(sim), part = Hi(sim, 26, 20);
        sim.Tick();
        int t = sim.TickNumber;
        RegenTests.Hit(sim, part, 2); // 128
        Through(sim, t + 157); // 120 + 38 regenerating ticks: 129 hp and 360 units... one more for 380
        Through(sim, t + 158);
        Assert.Equal((130, 129, 380), (u.Hp[full.Index], u.Hp[part.Index], u.RegenAccum[part.Index]));
        ProductionSystem.Researched(sim.World, 0, d.FindTech("moranth_supply"));
        Assert.Equal((150, 150, 149), (sim.World.MaxHp(full.Index), u.Hp[full.Index], u.Hp[part.Index]));
        sim.Tick();
        Assert.Equal((150, 0), (u.Hp[part.Index], u.RegenAccum[part.Index]));
        for (int k = 0; k < 400; k++)
        {
            sim.Tick();
            Assert.Equal((150, 0, -1), (u.Hp[full.Index], u.RegenAccum[full.Index], u.LastDamagedTick[full.Index]));
            Assert.Equal((150, 0), (u.Hp[part.Index], u.RegenAccum[part.Index]));
        }
    }

    /// <summary>
    /// Loader edges the developer's rows leave out: the scale range's own ends load, just outside them is one error; a
    /// <c>regen</c> that is not an object, or an empty one, is refused, never read as "no regeneration".
    /// </summary>
    [Theory]
    [InlineData("0.5", true)]
    [InlineData("3", true)]
    [InlineData("0.4999", false)]
    [InlineData("3.0001", false)]
    [InlineData("0", false)]
    public void Scale_RangeEnds(string scale, bool loads)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.SetUnitField("malazan", "malazan_heavy_infantry", "scale", scale);
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        if (loads) Assert.True(r.Data != null, string.Join("; ", r.Errors));
        else Assert.EndsWith(".scale", Assert.Single(r.Errors).Path);
    }

    [Theory]
    [InlineData("[1, 6]")]
    [InlineData("{}")]
    [InlineData("6")]
    [InlineData("{\"hpPerSecond\": 1, \"delay\": \"6\"}")]
    public void ARegenThatIsNotAWholeObject_IsRefused(string regen)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("factions/malazan/faction.json", r => r["regen"] = JsonNode.Parse(regen));
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.Null(r.Data);
        Assert.All(r.Errors, e => Assert.Equal("factions/malazan/faction.json", e.File));
    }

    /// <summary>
    /// The same written rate is the same rate a tick whether a faction's (read as a double) or a status magnitude's (stored as
    /// a float and converted each tick): on the 0.05 HP/s grid it is; at the midpoints between grid steps it is not.
    /// </summary>
    [Theory]
    [InlineData(1.5)]
    [InlineData(7.7)]
    [InlineData(0.05)]
    [InlineData(0.175, Skip = "BUG-1166: 0.175 is 4 units a tick as a faction rate, 3 as a status magnitude")]
    [InlineData(1.525, Skip = "BUG-1166: 31 vs 30")]
    public void AStatusMagnitude_ConvertsAsTheSameFactionRate(double rate) =>
        Assert.Equal(DataLoader.RegenPerTick(rate), DataLoader.RegenPerTick((float)rate));

    /// <summary>
    /// Forged <c>REGN</c> parts (checksum recomputed) that no build writes: a last damage tick after the save's own tick (the
    /// reader's comment says "a tick that has been", its check is only <c>&gt;= -1</c>), and a regenerating total of 0 beside
    /// a slot that counts an entry. Both should be refused <c>Malformed</c>, as an accumulator of 400 is.
    /// </summary>
    [Theory]
    [InlineData("future-tick", Skip = "BUG-1165: REGN last damage tick not checked against the save tick")]
    [InlineData("total-0", Skip = "BUG-1165: REGN regenerating total not cross-checked")]
    [InlineData("accumulator-400")]
    public void AForgedRegenPart_IsRefused(string forge)
    {
        GameData d = RegenTests.s_regen.Value;
        Simulation sim = RegenTests.Scene(d);
        EntityHandle h = Hi(sim);
        sim.Tick();
        RegenTests.Hit(sim, h, 30);
        StatusSystem.Apply(sim.World, h.Index, d.FindStatus("regenerating"), 1f, 400, 0);
        for (int k = 0; k < 5; k++) sim.Tick();
        byte[] bytes = Save.SaveTestRun.Save(sim);
        int cap = sim.World.Units.Capacity;
        int regn = -1;
        for (int at = bytes.AsSpan().IndexOf("REGN"u8); at >= 0; at = NextRegn(bytes, at))
            if (System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at + 4)) == cap) regn = at;
        Assert.True(regn > 0);
        int ldt = regn + 8, acc = ldt + 4 * cap + 4, counts = acc + 4 * cap + 4, total = counts + cap;
        Assert.Equal(1, bytes[counts + h.Index]);
        Assert.Equal(1, System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(total)));
        Assert.Equal(Rts.Sim.Save.SaveError.None, Save.SaveTestRun.TryLoad(d, bytes));
        byte[] bad = (byte[])bytes.Clone();
        switch (forge)
        {
            case "future-tick": System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(ldt + 4 * h.Index), sim.TickNumber + 5_000); break;
            case "total-0": System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(total), 0); break;
            default: System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(acc + 4 * h.Index), CombatConstants.RegenFixedOne); break;
        }
        Assert.Equal(Rts.Sim.Save.SaveError.Malformed, Save.SaveTestRun.TryLoad(d, Save.SaveBytes.Rechecksum(bad)));
        return;

        static int NextRegn(byte[] b, int from)
        {
            int n = b.AsSpan(from + 1).IndexOf("REGN"u8);
            return n < 0 ? -1 : from + 1 + n;
        }
    }

    /// <summary>
    /// The seeded invariant fuzz, short form (the Stress file runs more seeds, longer): two AI seats with raiders fight on data
    /// where each faction regenerates at a different rate and delay; between ticks a seeded stream hits random units (some to
    /// death), applies regenerating and burning statuses, and spawns short-lived summons. After every tick: see
    /// <see cref="CheckTick"/>.
    /// </summary>
    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    public void RegenInvariants_HoldEveryTick_InAFuzzedAiMatch(int seed, int variant)
    {
        FuzzStats stats = Fuzz(seed, variant, ticks: 2_000);
        output.WriteLine(stats.ToString());
        Assert.True(stats.FactionHeals > 0 && stats.StatusTicks > 0 && stats.Deaths > 0, $"weak run: {stats}");
    }

    internal sealed record FuzzStats(int FactionHeals, int StatusTicks, int Deaths, int MidDelaySamples);

    private static readonly Lazy<GameData>[] s_variants =
    {
        new(() => VariantData((1, 6), null)),
        new(() => VariantData((1.5, 5), (7.7, 0))),
        new(() => VariantData((100, 0.5), (0.3, 2))),
    };

    internal static GameData Variant(int v) => s_variants[v].Value;

    private static GameData VariantData((double Rate, double Delay)? malazan, (double Rate, double Delay)? whirlwind) => AiScenes.DataWith(dir =>
    {
        if (malazan is { } m) dir.EditJson("factions/malazan/faction.json", r => r["regen"] = new JsonObject { ["hpPerSecond"] = m.Rate, ["delay"] = m.Delay });
        if (whirlwind is { } w) dir.EditJson("factions/whirlwind/faction.json", r => r["regen"] = new JsonObject { ["hpPerSecond"] = w.Rate, ["delay"] = w.Delay });
    });

    internal static FuzzStats Fuzz(int seed, int variant, int ticks)
    {
        GameData d = Variant(variant);
        PlayerSeat[] seats = AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Ai);
        Simulation sim = AiScenes.Generated(seats, (ulong)seed, false, out _, data: d, raiders: 6);
        World w = sim.World;
        UnitStore u = w.Units;
        var rng = new Random(seed * 7919 + variant);
        int regen = d.FindStatus("regenerating"), burning = d.FindStatus("burning");
        var hp = new int[u.Capacity];
        var max = new int[u.Capacity];
        var gen = new int[u.Capacity];
        var ldt = new int[u.Capacity];
        var hadRegen = new bool[u.Capacity];
        var alive = new bool[u.Capacity];
        int factionHeals = 0, statusTicks = 0, deaths = 0, midDelay = 0;
        for (int k = 0; k < ticks; k++)
        {
            // The seeded stream, between ticks (as an ability or a hit landing in the next tick would).
            if (k > 10)
            {
                int pick = rng.Next(u.Capacity);
                if (u.Alive[pick])
                {
                    var h = new EntityHandle(pick, u.Generation[pick]);
                    switch (rng.Next(8))
                    {
                        case 0: CombatSystem.HitUnit(w, new PendingHit(default, 1 - u.Owner[pick], h, false, 1 + rng.Next(40))); break;
                        case 1: CombatSystem.HitUnit(w, new PendingHit(default, 1 - u.Owner[pick], h, false, 10_000)); break;
                        case 2: StatusSystem.Apply(w, pick, regen, 0.05f * (1 + rng.Next(60)), 1 + rng.Next(400), u.Owner[pick]); break;
                        case 3: StatusSystem.Apply(w, pick, burning, 1 + rng.Next(5), 1 + rng.Next(200), 1 - u.Owner[pick]); break;
                        case 4:
                            int type = u.TypeId[pick];
                            if (u.TrySpawn(u.Owner[pick], type, d.Units[type], u.Position[pick] + new Vector2(0.5f, 0.5f), w.TickNumber + 1 + rng.Next(150), out EntityHandle s))
                                CombatSystem.HitUnit(w, new PendingHit(default, 1 - u.Owner[pick], s, false, 1 + rng.Next(30)));
                            break;
                    }
                }
            }
            for (int i = 0; i < u.Capacity; i++)
            {
                alive[i] = u.Alive[i];
                if (!alive[i]) continue;
                hp[i] = u.Hp[i];
                max[i] = w.MaxHp(i);
                gen[i] = u.Generation[i];
                ldt[i] = u.LastDamagedTick[i];
                hadRegen[i] = u.Statuses.HasRegen(i);
            }
            int ran = sim.TickNumber;
            sim.Tick();
            CheckTick(sim, ran, hp, max, gen, ldt, hadRegen, alive, ref factionHeals, ref statusTicks, ref deaths, ref midDelay);
        }
        return new FuzzStats(factionHeals, statusTicks, deaths, midDelay);
    }

    /// <summary>
    /// After tick <paramref name="ran"/>: every live unit has 1..MaxHp hp, 0..399 in the accumulator, a last damage tick of -1
    /// or 0..ran (never set under a faction without regeneration); every free slot has 0 / -1 and no regenerating entry; the
    /// regenerating count equals the slots carrying one and each slot's flag equals its entries; a unit that gained hp this
    /// tick with the same maximum had a regenerating entry, or its faction regenerates and its delay was over before the tick.
    /// </summary>
    private static void CheckTick(Simulation sim, int ran, int[] hp, int[] max, int[] gen, int[] ldt, bool[] hadRegen, bool[] wasAlive,
        ref int factionHeals, ref int statusTicks, ref int deaths, ref int midDelay)
    {
        World w = sim.World;
        UnitStore u = w.Units;
        StatusStore s = u.Statuses;
        int withRegen = 0;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i])
            {
                if (u.RegenAccum[i] != 0 || u.LastDamagedTick[i] != -1 || s.HasRegen(i))
                    Assert.Fail($"tick {ran}: free slot {i} carries regen state ({u.RegenAccum[i]}, {u.LastDamagedTick[i]}, {s.HasRegen(i)})");
                if (wasAlive[i]) deaths++;
                continue;
            }
            int owner = u.Owner[i];
            int m = w.MaxHp(i);
            int rate = w.RegenPerTickOf[owner], delay = w.RegenDelayOf[owner];
            if (u.Hp[i] < 1 || u.Hp[i] > m) Assert.Fail($"tick {ran}: unit {i} hp {u.Hp[i]} outside 1..{m}");
            if (u.RegenAccum[i] < 0 || u.RegenAccum[i] >= CombatConstants.RegenFixedOne) Assert.Fail($"tick {ran}: unit {i} accumulator {u.RegenAccum[i]}");
            if (u.LastDamagedTick[i] < -1 || u.LastDamagedTick[i] > ran) Assert.Fail($"tick {ran}: unit {i} last damage {u.LastDamagedTick[i]}");
            if (rate == 0 && u.LastDamagedTick[i] != -1) Assert.Fail($"tick {ran}: unit {i} of a faction without regen has a last damage tick");
            bool has = false;
            int head = i * StatusStore.PerUnit;
            for (int k = 0; k < s.Count[i]; k++)
                if (w.Data.Statuses[s.StatusId[head + k]].Kind == StatusKind.Regenerating) has = true;
            if (has != s.HasRegen(i)) Assert.Fail($"tick {ran}: unit {i} regen flag {s.HasRegen(i)} but entries say {has}");
            if (has) withRegen++;
            if (has || hadRegen[i]) statusTicks++;
            if (rate > 0 && u.LastDamagedTick[i] >= 0 && ran - u.LastDamagedTick[i] < delay) midDelay++;
            bool same = wasAlive[i] && gen[i] == u.Generation[i];
            if (!same || u.Hp[i] <= hp[i] || m != max[i]) continue;
            if (hadRegen[i]) continue;
            bool delayOver = rate > 0 && (ldt[i] < 0 || ran - ldt[i] >= delay);
            if (!delayOver) Assert.Fail($"tick {ran}: unit {i} (player {owner}, rate {rate}, delay {delay}, last damage {ldt[i]}) healed {hp[i]} -> {u.Hp[i]} with no source");
            // At most the whole points one tick's rate and the carried fraction make.
            if (u.Hp[i] - hp[i] > (rate + CombatConstants.RegenFixedOne - 1) / CombatConstants.RegenFixedOne + 1)
                Assert.Fail($"tick {ran}: unit {i} healed {u.Hp[i] - hp[i]} in a tick at {rate} units a tick");
            factionHeals++;
        }
        if (withRegen != s.RegeneratingUnits) Assert.Fail($"tick {ran}: RegeneratingUnits {s.RegeneratingUnits}, slots with an entry {withRegen}");
    }
}
