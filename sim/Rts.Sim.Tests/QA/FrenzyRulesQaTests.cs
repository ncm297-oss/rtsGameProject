using System.Text.Json.Nodes;
using Rts.Sim.Abilities;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Xunit.Abstractions;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA (M7-4, session 2026-11-12-0546, tier full): attacks on the three status rules (a frenzied row's <c>damageBonus</c>, a
/// row's <c>onEnd</c>, the <c>statusDuration</c> tech) and the building <c>scale</c>, on test copies of the shipped data
/// (<see cref="BloodOilScenes"/> plus QA rows). The phase-5 loop's corners (two ends in one pass, a merge into a stronger
/// or longer Slow, a pulse kill on either side of the ended entry, sixteen units in one pass, an onEnd of damage over time
/// and of stealth), the damage read (tech bonus + class multiplier + bonus at an exact .5), the tech's sums, and the
/// loader's malformed shapes.
/// </summary>
public class FrenzyRulesQaTests
{
    private readonly ITestOutputHelper _out;

    public FrenzyRulesQaTests(ITestOutputHelper output) => _out = output;

    private const string Rage = "zz_qa_rage";

    /// <summary>
    /// The Blood-oil copy plus: <c>zz_qa_rage</c> (frenzied, +50 % damage, onEnd Slowed 0.3 for 2 s), <c>zz_qa_fever</c>
    /// (frenzied, onEnd Burning 5 / s for 2 s), <c>zz_qa_fade</c> (frenzied, onEnd Stealthed for 1 s), and a second
    /// statusDuration tech effect (+2 s on the Frenzy) on the common Melee Weapons I.
    /// </summary>
    private static readonly Lazy<GameData> s_data = new(() => AiScenes.DataWith(dir =>
    {
        BloodOil(dir);
        dir.EditJson("common/statuses.json", r =>
        {
            JsonArray list = r["statuses"]!.AsArray();
            list.Add(Row(Rage, 0.5, new JsonObject { ["status"] = "slowed", ["magnitude"] = 0.3, ["duration"] = 2 }));
            list.Add(Row("zz_qa_fever", null, new JsonObject { ["status"] = "burning", ["magnitude"] = 5, ["duration"] = 2 }));
            list.Add(Row("zz_qa_fade", null, new JsonObject { ["status"] = "stealthed", ["duration"] = 1 }));
        });
        dir.EditJson("common/techs.json", r =>
        {
            foreach (JsonNode? t in r["techs"]!.AsArray())
                if ((string)t!["id"]! == "melee_weapons_1")
                    t["effects"]!.AsArray().Add(new JsonObject { ["stat"] = "statusDuration", ["amount"] = 2, ["statuses"] = new JsonArray(BloodOilScenes.Status) });
        });
    }));

    private static JsonObject Row(string id, double? bonus, JsonObject onEnd)
    {
        var o = new JsonObject { ["id"] = id, ["displayName"] = "QA", ["description"] = "Test only.", ["kind"] = "frenzied", ["onEnd"] = onEnd };
        if (bonus != null) o["damageBonus"] = bonus.Value;
        return o;
    }

    private static GameData Data => s_data.Value;

    /// <summary>
    /// <see cref="BloodOilScenes.Edit"/>'s four edits, made idempotent (BUG-1280): the Frenzy row, the ability's status and
    /// the Anointing's statusDuration effect are added only when the files do not already have them, so this suite still
    /// loads once the data track ships the docs/03 "For the data track (M7-4)" rows.
    /// </summary>
    private static void BloodOil(TestDataDir dir)
    {
        dir.EditJson("common/statuses.json", r =>
        {
            JsonArray list = r["statuses"]!.AsArray();
            JsonNode? have = list.FirstOrDefault(n => (string)n!["id"]! == BloodOilScenes.Status);
            if (have != null) list.Remove(have);
            list.Add(BloodOilScenes.StatusRow());
        });
        dir.EditJson("factions/teblor/abilities.json", r => r["abilities"]![0]!["effects"]![0]!["status"] = BloodOilScenes.Status);
        dir.EditJson("factions/teblor/techs.json", r =>
        {
            JsonArray effects = r["techs"]![0]!["effects"]!.AsArray();
            JsonNode? have = effects.FirstOrDefault(e => (string?)e!["stat"] == "statusDuration");
            if (have != null) effects.Remove(have);
            effects.Add(new JsonObject { ["stat"] = "statusDuration", ["amount"] = 4, ["statuses"] = new JsonArray(BloodOilScenes.Status) });
        });
        dir.EditJson("factions/teblor/buildings.json", r =>
        {
            foreach (JsonNode? b in r["buildings"]!.AsArray())
                if ((string)b!["id"]! == "teblor_longhouse") b["scale"] = 1.25;
        });
    }

    private static readonly Lazy<GameData> s_blood = new(() => AiScenes.DataWith(BloodOil));

    /// <summary>The Blood-oil copy (as <see cref="BloodOilScenes.Data"/>), built by <see cref="BloodOil"/>.</summary>
    private static GameData Blood => s_blood.Value;

    private static Simulation Flat(GameData? d = null, ulong seed = 1) =>
        new(new SimConfig(seed, 2, 128, 1056) { Data = d ?? Data }, LocalMovementTests.Flat(48));

    private static int TicksOf(World w, EntityHandle h, int status) => BloodOilScenes.TicksOf(w, h, status);

    private static void Run(Simulation sim, int ticks)
    {
        for (int t = 0; t < ticks; t++) sim.Tick();
    }

    // ---------------------------------------------------------------- phase 5

    /// <summary>
    /// Two onEnd rows ending in the same pass, both naming Slowed: applied in entry order through the stacking rule, so the
    /// result (stronger magnitude, longer duration, the stronger one's source) is the same in either entry order.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TwoEnds_InOnePass_ApplyInEntryOrder_AndMergeByTheStackingRule(bool rageFirst)
    {
        Simulation sim = Flat();
        World w = sim.World;
        EntityHandle h = BloodOilScenes.Place(sim, 0, "teblor_warrior", At(sim, 20, 20));
        int frenzy = Data.FindStatus(BloodOilScenes.Status), rage = Data.FindStatus(Rage), slowed = Data.FindStatus("slowed");
        if (rageFirst) StatusSystem.Apply(w, h.Index, rage, 0.1f, 30, 1);
        StatusSystem.Apply(w, h.Index, frenzy, 0.4f, 30, 0);
        if (!rageFirst) StatusSystem.Apply(w, h.Index, rage, 0.1f, 30, 1);
        Assert.Equal(1.5f, StatusSystem.DamageFactor(w, h.Index)); // the larger bonus
        Run(sim, 30);
        Assert.Equal(1, w.Units.Statuses.Count[h.Index]);
        int at = w.Units.Statuses.IndexOf(h.Index, slowed);
        Assert.Equal((0.3f, 80, 1), (w.Units.Statuses.Magnitude[at], w.Units.Statuses.TicksRemaining[at], w.Units.Statuses.SourcePlayer[at]));
        Assert.Equal(w.Data.Units[w.Units.TypeId[h.Index]].SpeedPerTick * 0.7f, w.Units.Speed[h.Index]);
        Assert.Equal(1f, StatusSystem.DamageFactor(w, h.Index));
    }

    /// <summary>
    /// The aftermath merging into a Slow the unit already has (docs/02 stacking: longer duration, stronger magnitude, the
    /// stronger one's source). A stronger enemy Slow with 10 ticks left keeps its 0.5 and its source and is lengthened to
    /// the aftermath's 80 ticks; a weaker, longer one takes the 0.2 and the Frenzy's source and keeps its longer time.
    /// </summary>
    [Fact]
    public void TheAftermath_MergingIntoAnExistingSlow_FollowsTheStackingRule()
    {
        Simulation sim = Flat();
        World w = sim.World;
        UnitStore u = w.Units;
        int frenzy = Data.FindStatus(BloodOilScenes.Status), slowed = Data.FindStatus("slowed");
        EntityHandle a = BloodOilScenes.Place(sim, 0, "teblor_warrior", At(sim, 20, 20));
        EntityHandle b = BloodOilScenes.Place(sim, 0, "teblor_warrior", At(sim, 30, 20));
        StatusSystem.Apply(w, a.Index, slowed, 0.5f, 15, 1); // stronger, shorter, the enemy's
        StatusSystem.Apply(w, b.Index, slowed, 0.1f, 200, 1); // weaker, longer
        StatusSystem.Apply(w, a.Index, frenzy, 0.4f, 5, 0);
        StatusSystem.Apply(w, b.Index, frenzy, 0.4f, 5, 0);
        Run(sim, 5);
        int at = u.Statuses.IndexOf(a.Index, slowed);
        Assert.Equal((0.5f, 80, 1), (u.Statuses.Magnitude[at], u.Statuses.TicksRemaining[at], u.Statuses.SourcePlayer[at]));
        Assert.Equal(w.Data.Units[u.TypeId[a.Index]].SpeedPerTick * 0.5f, u.Speed[a.Index]);
        at = u.Statuses.IndexOf(b.Index, slowed);
        Assert.Equal((0.2f, 195, 0), (u.Statuses.Magnitude[at], u.Statuses.TicksRemaining[at], u.Statuses.SourcePlayer[at]));
        Assert.Equal(w.Data.Units[u.TypeId[b.Index]].SpeedPerTick * 0.8f, u.Speed[b.Index]);
    }

    /// <summary>A Burning pulse that kills, before the Frenzy in entry order: the pass stops, no Slow, nothing on any slot.</summary>
    [Fact]
    public void APulseKill_BeforeTheEndedEntry_InEntryOrder_LeavesNoSlow()
    {
        Simulation sim = Flat();
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle h = BloodOilScenes.Place(sim, 0, "teblor_warrior", At(sim, 20, 20));
        BloodOilScenes.Place(sim, 0, "teblor_warrior", At(sim, 40, 40)); // keeps player 0 in
        StatusSystem.Apply(w, h.Index, Data.FindStatus("burning"), 100_000f, 20, 1);
        StatusSystem.Apply(w, h.Index, Data.FindStatus(BloodOilScenes.Status), 0.4f, 20, 0);
        Run(sim, 20);
        Assert.False(u.IsAlive(h));
        for (int i = 0; i < u.Capacity; i++)
            Assert.True(!u.Alive[i] || u.Statuses.IndexOf(i, Data.FindStatus("slowed")) < 0, $"unit {i} slowed");
    }

    /// <summary>A pulse in the same pass that does not kill: the Slow still lands, and the Burning keeps pulsing.</summary>
    [Fact]
    public void APulse_ThatDoesNotKill_InThePassTheFrenzyEnds_TheSlowStillLands()
    {
        Simulation sim = Flat();
        World w = sim.World;
        EntityHandle h = BloodOilScenes.Place(sim, 0, "teblor_warrior", At(sim, 20, 20));
        StatusSystem.Apply(w, h.Index, Data.FindStatus("burning"), 1f, 100, 1);
        StatusSystem.Apply(w, h.Index, Data.FindStatus(BloodOilScenes.Status), 0.4f, 20, 0);
        int hp = w.Units.Hp[h.Index];
        Run(sim, 20);
        Assert.True(w.Units.IsAlive(h) && w.Units.Hp[h.Index] < hp, "the pulse landed and did not kill");
        Assert.Equal(80, TicksOf(w, h, Data.FindStatus("slowed")));
        Assert.Equal(80, TicksOf(w, h, Data.FindStatus("burning")));
    }

    /// <summary>
    /// Sixteen units whose Frenzies end on one tick: every one carries a full 80-tick Slow after the pass, appended after
    /// its other entry; a twin built in reverse slot order of application hashes the same (each unit's own pass).
    /// </summary>
    [Fact]
    public void SixteenUnits_EndingOnOneTick_EachTakesItsSlow_OrderOfApplicationDoesNotMatter()
    {
        Simulation Build(bool reverse)
        {
            Simulation sim = Flat();
            World w = sim.World;
            var hs = new EntityHandle[16];
            for (int k = 0; k < 16; k++) hs[k] = BloodOilScenes.Place(sim, k % 2, "teblor_warrior", At(sim, 4 + 2 * (k % 8), 4 + 30 * (k / 8)));
            for (int n = 0; n < 16; n++)
            {
                int k = reverse ? 15 - n : n;
                StatusSystem.Apply(w, hs[k].Index, Data.FindStatus("frenzied"), 0.5f, 300, k % 2);
                StatusSystem.Apply(w, hs[k].Index, Data.FindStatus(BloodOilScenes.Status), 0.4f, 40, k % 2);
            }
            Run(sim, 40);
            for (int k = 0; k < 16; k++)
            {
                int at = w.Units.Statuses.IndexOf(hs[k].Index, Data.FindStatus("slowed"));
                Assert.Equal(hs[k].Index * StatusStore.PerUnit + 1, at);
                Assert.Equal((80, k % 2), (w.Units.Statuses.TicksRemaining[at], w.Units.Statuses.SourcePlayer[at]));
            }
            return sim;
        }
        Simulation a = Build(false), b = Build(true);
        Assert.Equal(a.StateHash(), b.StateHash());
        for (int t = 0; t < 100; t++)
        {
            a.Tick();
            b.Tick();
            Assert.Equal(a.StateHash(), b.StateHash());
        }
    }

    /// <summary>
    /// An onEnd of damage over time (Burning 5 / s for 2 s): applied after the pass, so its pulse clock starts with the
    /// entry: no pulse the tick it lands, one a second later, one on its last tick (two in all), then gone.
    /// </summary>
    [Fact]
    public void AnOnEndOfDamageOverTime_PulsesASecondAfterItLands_TwiceInTwoSeconds()
    {
        Simulation sim = Flat();
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle h = BloodOilScenes.Place(sim, 0, "teblor_warrior", At(sim, 20, 20));
        int fever = Data.FindStatus("zz_qa_fever"), burning = Data.FindStatus("burning");
        StatusSystem.Apply(w, h.Index, fever, 0.4f, 10, 1);
        var drops = new List<int>();
        for (int t = 1; t <= 60; t++)
        {
            int before = u.Hp[h.Index];
            sim.Tick();
            if (u.Hp[h.Index] < before) drops.Add(t);
            if (t == 10) Assert.Equal(40, TicksOf(w, h, burning));
        }
        Assert.Equal(new[] { 30, 50 }, drops.ToArray());
        Assert.Equal(-1, TicksOf(w, h, burning));
        Assert.Equal(0, u.Statuses.Count[h.Index]);
        int at = -1;
        StatusSystem.Apply(w, h.Index, fever, 0.4f, 1, 1);
        sim.Tick();
        at = u.Statuses.IndexOf(h.Index, burning);
        Assert.Equal(1, u.Statuses.SourcePlayer[at]); // the ended entry's source: credited with a kill
    }

    /// <summary>An onEnd of stealth: the stealth count follows the applied entry and returns to 0 when it ends.</summary>
    [Fact]
    public void AnOnEndOfStealth_IsCounted_AndUncountedWhenItEnds()
    {
        Simulation sim = Flat();
        World w = sim.World;
        StatusStore s = w.Units.Statuses;
        EntityHandle h = BloodOilScenes.Place(sim, 0, "teblor_warrior", At(sim, 20, 20));
        StatusSystem.Apply(w, h.Index, Data.FindStatus("zz_qa_fade"), 0.4f, 10, 0);
        Run(sim, 10);
        Assert.Equal(20, TicksOf(w, h, Data.FindStatus("stealthed")));
        Assert.True(s.HasStealth(h.Index));
        Assert.Equal(1, s.StealthedUnits);
        Run(sim, 20);
        Assert.False(s.HasStealth(h.Index));
        Assert.Equal(0, s.StealthedUnits);
        Assert.Equal(0, s.Count[h.Index]);
    }

    /// <summary>
    /// A 1,500-tick Teblor brawl on the QA data (every QA row in play: the Frenzy and the rage cast, the fever and fade rows
    /// put on by hand every 100 ticks, the Anointing and Melee Weapons I researched), twins hash-equal every tick; the
    /// checkpoint digests are printed so QA can compare two separate processes (session 2026-11-12-0546: equal).
    /// </summary>
    [Fact]
    public void ABrawl_WithEveryRule_TwinsEqual_AndPrintsItsDigests()
    {
        Simulation Make()
        {
            Simulation sim = Flat(Data, seed: 7);
            World w = sim.World;
            w.Techs.Set(0, Data.FindTech("teblor_blood_oil_anointing"), true);
            w.Techs.Set(1, Data.FindTech("melee_weapons_1"), true);
            string[] types = { "teblor_warrior", "teblor_javelin_thrower", "teblor_shaman", "teblor_warrior" };
            for (int p = 0; p < 2; p++)
                for (int k = 0; k < 16; k++)
                    BloodOilScenes.Place(sim, p, types[k % types.Length], At(sim, 8 + 2 * (k % 8) + 14 * p, 16 + 2 * (k / 8)));
            return sim;
        }
        Simulation a = Make(), b = Make();
        string[] extra = { Rage, "zz_qa_fever", "zz_qa_fade" };
        var digests = new List<string>();
        for (int t = 0; t < 1500; t++)
        {
            foreach (Simulation s in new[] { a, b })
            {
                UnitStore u = s.World.Units;
                if (t % 40 == 3)
                    for (int i = 0; i < u.Capacity; i++)
                        if (u.Alive[i] && u.TypeId[i] == Data.FindUnit("teblor_shaman"))
                            s.Enqueue(Command.UseAbility(u.Owner[i], new EntityHandle(i, u.Generation[i]), 0, u.Position[i]));
                if (t % 150 == 1)
                    for (int i = 0; i < u.Capacity; i++)
                        if (u.Alive[i])
                            s.Enqueue(Command.AttackMove(u.Owner[i], new EntityHandle(i, u.Generation[i]), At(s, u.Owner[i] == 0 ? 36 : 8, 18)));
                if (t % 100 == 50)
                    for (int i = 0; i < u.Capacity; i += 3)
                        if (u.Alive[i]) StatusSystem.Apply(s.World, i, Data.FindStatus(extra[(i + t / 100) % 3]), 0.3f, 15 + i % 7, (i + 1) % 2);
            }
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"twins differ after tick {t}");
            if (t % 250 == 249) digests.Add($"{t}:{a.StateHash():x16}");
        }
        _out.WriteLine("DIGESTS " + string.Join(" ", digests) + $" units {a.World.Units.Count}");
    }

    // ---------------------------------------------------------------- the damage read

    /// <summary>
    /// Tech attack bonus + class multiplier + damage bonus at an exact .5: a Javelin Thrower (12 pierce) with Ranged
    /// Weapons I (+1) on a Teblor Warrior (giant x1.25, armor 2): 13 x 1.25 x 1.2 = 19.5, half up to 20, less 2 = 18;
    /// 16.25 -> 16 - 2 = 14 without the Frenzy. Read through the real throw and landing.
    /// </summary>
    [Theory]
    [InlineData(false, 14)]
    [InlineData(true, 18)]
    public void TechBonus_ClassMultiplier_AndDamageBonus_RoundHalfUp_AtAnExactHalf(bool frenzied, int expected)
    {
        Simulation sim = Flat(Blood);
        World w = sim.World;
        EntityHandle a = BloodOilScenes.Place(sim, 0, "teblor_javelin_thrower", At(sim, 14, 20));
        EntityHandle v = BloodOilScenes.Place(sim, 1, "teblor_warrior", At(sim, 18, 20));
        w.Units.Hp[v.Index] = 1_000_000;
        w.Techs.Set(0, w.Data.FindTech("ranged_weapons_1"), true);
        if (frenzied) StatusSystem.Apply(w, a.Index, w.Data.FindStatus(BloodOilScenes.Status), 0.4f, 100_000, 0);
        w.Fog.Update();
        sim.Enqueue(Command.HoldPosition(1, v));
        sim.Enqueue(Command.Attack(0, a, v, false));
        int full = w.Units.Hp[v.Index];
        Assert.True(RunUntil(sim, () => w.Units.Hp[v.Index] < full, 600) < 600, "no hit landed");
        Assert.Equal(expected, full - w.Units.Hp[v.Index]);
    }

    /// <summary>A damage bonus multiplies a tower's nothing, an ability's nothing and a pulse's nothing: only the bearer's own attacks.</summary>
    [Fact]
    public void ThePulse_OfAFrenziedBearersBurning_IsNotMultiplied()
    {
        Simulation Burn(bool frenzied)
        {
            Simulation sim = Flat(Blood);
            World w = sim.World;
            EntityHandle h = BloodOilScenes.Place(sim, 0, "teblor_warrior", At(sim, 20, 20));
            if (frenzied) StatusSystem.Apply(w, h.Index, w.Data.FindStatus(BloodOilScenes.Status), 0.4f, 1000, 0);
            StatusSystem.Apply(w, h.Index, w.Data.FindStatus("burning"), 7f, 100, 1);
            Run(sim, 20);
            return sim;
        }
        Assert.Equal(Burn(false).World.Units.Hp[0], Burn(true).World.Units.Hp[0]);
    }

    // ---------------------------------------------------------------- the tech

    /// <summary>
    /// Two statusDuration effects on one status sum (Anointing +4 s, the QA Melee Weapons I +2 s: +6 s = 120 ticks), and the
    /// application of a source player decides it, not the bearer's owner: player 1's Frenzy on player 0's unit is plain.
    /// </summary>
    [Fact]
    public void TwoTechs_OnOneStatus_Sum_AndTheSourceDecides()
    {
        Simulation sim = Flat();
        World w = sim.World;
        int frenzy = Data.FindStatus(BloodOilScenes.Status);
        EntityHandle a = BloodOilScenes.Place(sim, 0, "teblor_warrior", At(sim, 20, 20));
        EntityHandle b = BloodOilScenes.Place(sim, 0, "teblor_warrior", At(sim, 30, 20));
        w.Techs.Set(0, Data.FindTech("teblor_blood_oil_anointing"), true);
        w.Techs.Set(0, Data.FindTech("melee_weapons_1"), true);
        Assert.Equal(120, w.Techs.StatusDurationTicks(0, frenzy));
        StatusSystem.Apply(w, a.Index, frenzy, 0.4f, 200, 0);
        StatusSystem.Apply(w, b.Index, frenzy, 0.4f, 200, 1);
        Assert.Equal((320, 200), (TicksOf(w, a, frenzy), TicksOf(w, b, frenzy)));
        w.Techs.Set(0, Data.FindTech("melee_weapons_1"), false);
        Assert.Equal(80, w.Techs.StatusDurationTicks(0, frenzy));
    }

    /// <summary>
    /// Research finishing through the real command path the same tick a cast resolves, against a twin: identical hashes
    /// every tick (the order of the two inside a tick is fixed), and the cast after the research gets the +4 s.
    /// </summary>
    [Fact]
    public void ResearchByCommand_ThenACast_TwinsEqual_AndTheCastIsLengthened()
    {
        GameData d = s_free.Value;
        Simulation Make()
        {
            int teblor = d.FindFaction("teblor");
            var seats = System.Collections.Immutable.ImmutableArray.Create(
                new PlayerSeat(teblor, SeatController.Human, AiDifficulty.Normal), new PlayerSeat(teblor, SeatController.Human, AiDifficulty.Normal));
            var sim = new Simulation(new SimConfig(1, 2, 128, 1056) { Data = d, Seats = seats }, LocalMovementTests.Flat(48));
            BloodOilScenes.Place(sim, 1, "teblor_warrior", At(sim, 40, 40));
            Assert.True(sim.World.Buildings.Spawn(0, d.FindBuilding("teblor_bloodwood_carver"), 30 * 48 + 30, out _));
            return sim;
        }
        Simulation a = Make(), b = Make();
        EntityHandle shaman = default, warrior = default;
        foreach (Simulation s in new[] { a, b })
        {
            shaman = BloodOilScenes.Place(s, 0, "teblor_shaman", At(s, 10, 10));
            warrior = BloodOilScenes.Place(s, 0, "teblor_warrior", At(s, 12, 10));
            int cell = s.World.Buildings.Cell[0];
            s.Enqueue(Command.Research(0, s.World.NavGrid.CellCenter(cell % s.World.NavGrid.Width, cell / s.World.NavGrid.Width), d.FindTech("teblor_blood_oil_anointing")));
        }
        int frenzy = d.FindStatus(BloodOilScenes.Status);
        int researchedAt = -1, cast = -1;
        for (int t = 0; t < 200; t++)
        {
            if (researchedAt >= 0 && cast < 0)
            {
                BloodOilScenes.Cast(a, 0, shaman);
                BloodOilScenes.Cast(b, 0, shaman);
                cast = t;
            }
            a.Tick();
            b.Tick();
            Assert.Equal(a.StateHash(), b.StateHash());
            if (researchedAt < 0 && a.World.HasTech(0, d.FindTech("teblor_blood_oil_anointing"))) researchedAt = t;
            if (cast >= 0 && TicksOf(a.World, warrior, frenzy) > 0) break;
        }
        _out.WriteLine($"researched after tick {researchedAt}, cast enqueued at {cast}");
        Assert.True(researchedAt >= 0, "the research never finished");
        Assert.Equal(280, TicksOf(a.World, warrior, frenzy));
    }

    private static readonly Lazy<GameData> s_free = new(() => AiScenes.DataWith(dir =>
    {
        BloodOil(dir);
        dir.EditJson("factions/teblor/techs.json", r =>
        {
            JsonNode t = r["techs"]![0]!;
            t["requires"] = new JsonArray();
            t["cost"] = new JsonObject { ["gold"] = 0, ["wood"] = 0 };
            t["researchTime"] = 1;
        });
    }));

    // ---------------------------------------------------------------- the loader

    private static DataLoadResult LoadWith(Action<TestDataDir> edit)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        BloodOil(dir);
        edit(dir);
        return DataLoader.LoadAll(dir.Path);
    }

    private static void EditFrenzy(TestDataDir dir, Action<JsonObject> edit) =>
        dir.EditJson("common/statuses.json", r => edit(r["statuses"]!.AsArray().Single(n => (string)n!["id"]! == BloodOilScenes.Status)!.AsObject()));

    /// <summary>Malformed shapes of the new fields: each is a load error naming the field, never a crash or a silent default.</summary>
    [Theory]
    [InlineData("onEnd", "\"slowed\"", "onEnd")]
    [InlineData("onEnd", "{}", "onEnd.status")]
    [InlineData("onEnd", "{\"status\": \"slowed\", \"magnitude\": 0.2}", "onEnd.duration")]
    [InlineData("onEnd", "{\"status\": \"slowed\", \"magnitude\": 0.2, \"duration\": 4, \"chance\": 1}", "onEnd")]
    [InlineData("onEnd", "{\"status\": 7, \"magnitude\": 0.2, \"duration\": 4}", "onEnd")]
    [InlineData("damageBonus", "\"0.2\"", "damageBonus")]
    [InlineData("damageBonus", "1e400", "damageBonus")]
    public void MalformedStatusFields_AreErrors_AtTheField(string field, string json, string pathPart)
    {
        DataLoadResult r = LoadWith(dir => EditFrenzy(dir, x => x[field] = JsonNode.Parse(json)));
        _out.WriteLine(string.Join("\n", r.Errors));
        Assert.False(r.Ok);
        Assert.Contains(r.Errors, e => e.File == "common/statuses.json" && (e.Path.Contains(pathPart) || e.Message.Contains(field)));
    }

    /// <summary>A statusDuration effect's statuses as a string, or with a null entry: an error, not a crash.</summary>
    [Theory]
    [InlineData("\"teblor_frenzied\"")]
    [InlineData("[null]")]
    [InlineData("[\"Teblor_Frenzied\"]")]
    public void MalformedStatusDurationStatuses_AreErrors(string json)
    {
        DataLoadResult r = LoadWith(dir => dir.EditJson("factions/teblor/techs.json", root => root["techs"]![0]!["effects"]![2]!["statuses"] = JsonNode.Parse(json)));
        _out.WriteLine(string.Join("\n", r.Errors));
        Assert.False(r.Ok);
        Assert.Contains(r.Errors, e => e.File == "factions/teblor/techs.json" && e.Path.Contains("effects[2]"));
    }

    /// <summary>
    /// Two rows naming each other (A -> B, B -> A) are both chains: two errors, one at each row's onEnd.status. A row naming
    /// another whose own onEnd is broken is still a chain: the named row has an onEnd.
    /// </summary>
    [Fact]
    public void ACycle_IsTwoChainErrors_OneAtEachRow()
    {
        DataLoadResult r = LoadWith(dir => dir.EditJson("common/statuses.json", root =>
        {
            JsonArray list = root["statuses"]!.AsArray();
            list.Single(n => (string)n!["id"]! == "slowed")!["onEnd"] = new JsonObject { ["status"] = BloodOilScenes.Status, ["magnitude"] = 0.4, ["duration"] = 1 };
        }));
        _out.WriteLine(string.Join("\n", r.Errors));
        Assert.Equal(2, r.Errors.Count);
        Assert.All(r.Errors, e => Assert.True(e.Path.EndsWith(".onEnd.status") && e.Message.Contains("onEnd of its own"), e.ToString()));
    }

    /// <summary>
    /// A statusDuration effect naming one status twice loads (the list is a set) and lengthens it once, not twice; a common
    /// tech may name a status (statuses are common).
    /// </summary>
    [Fact]
    public void AStatusNamedTwice_LengthensItOnce()
    {
        DataLoadResult r = LoadWith(dir => dir.EditJson("factions/teblor/techs.json", root =>
            root["techs"]![0]!["effects"]![2]!["statuses"] = new JsonArray(BloodOilScenes.Status, BloodOilScenes.Status)));
        Assert.True(r.Ok, string.Join("; ", r.Errors));
        GameData d = r.Data!;
        var sim = new Simulation(new SimConfig(1, 2, 64, 544) { Data = d }, LocalMovementTests.Flat(48));
        sim.World.Techs.Set(0, d.FindTech("teblor_blood_oil_anointing"), true);
        Assert.Equal(80, sim.World.Techs.StatusDurationTicks(0, d.FindStatus(BloodOilScenes.Status)));
    }

    /// <summary>A building scale of null hashes and loads as absent (1); a string is an error at the field.</summary>
    [Fact]
    public void ABuildingScale_NullIsAbsent_AStringIsAnError()
    {
        DataLoadResult ok = LoadWith(dir => dir.EditJson("factions/teblor/buildings.json", root => root["buildings"]![0]!["scale"] = null));
        Assert.True(ok.Ok, string.Join("; ", ok.Errors));
        Assert.Equal(1f, ok.Data!.Buildings[ok.Data.FindBuilding((string)JsonNode.Parse(File.ReadAllText(Path.Combine(TestDataDir.Shipped, "factions/teblor/buildings.json")))!["buildings"]![0]!["id"]!)].Scale);
        DataLoadResult bad = LoadWith(dir => dir.EditJson("factions/teblor/buildings.json", root => root["buildings"]![0]!["scale"] = "big"));
        _out.WriteLine(string.Join("\n", bad.Errors));
        Assert.False(bad.Ok);
        Assert.Contains(bad.Errors, e => e.File == "factions/teblor/buildings.json");
    }

    /// <summary>Building scale is presentation only: two runs differing only in a building's scale hash alike tick for tick (the data hash aside).</summary>
    [Fact]
    public void ABuildingScale_ChangesNoStateHash()
    {
        GameData big = s_bigBuildings.Value, plain = Blood;
        Simulation Make(GameData d)
        {
            var sim = new Simulation(new SimConfig(3, 2, 128, 1056) { Data = d }, LocalMovementTests.Flat(48));
            Assert.True(sim.World.Buildings.Spawn(0, d.FindBuilding("teblor_longhouse"), 20 * 48 + 20, out _));
            BloodOilScenes.Place(sim, 0, "teblor_warrior", At(sim, 10, 10));
            BloodOilScenes.Place(sim, 1, "teblor_warrior", At(sim, 26, 22));
            return sim;
        }
        Simulation a = Make(big), b = Make(plain);
        Assert.NotEqual(big.ContentHash(), plain.ContentHash());
        for (int t = 0; t < 300; t++)
        {
            a.Tick();
            b.Tick();
            Assert.Equal(a.StateHash(), b.StateHash());
        }
    }

    private static readonly Lazy<GameData> s_bigBuildings = new(() => AiScenes.DataWith(dir =>
    {
        BloodOil(dir);
        dir.EditJson("factions/teblor/buildings.json", r =>
        {
            foreach (JsonNode? b in r["buildings"]!.AsArray()) b!["scale"] = 3.0;
        });
    }));
}
