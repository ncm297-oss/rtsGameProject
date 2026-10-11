using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Abilities;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Vision;
using static Rts.Sim.Tests.AbilityScenes;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M4-4b-3b (session 2026-10-10-1314): the <c>summon</c> kind and <c>spawn</c> effect attacked past the developer's rows.
/// Expiry against a lethal Burning pulse on every nearby tick (one death, one event, the right kind); expiry and a new summon
/// in the same tick reusing the slots; the fog oracle across summons and expiries on every cadence phase; placement beside a
/// cliff, on a ramp top, in a map corner, for two casters resolving the same tick, and the full 48-cell search; a store
/// that fills between two spawn effects; a summon's other effects missing its own new units; loader edges.
/// Test-only abilities on the Malazan Laborer (<see cref="Data"/>) besides <see cref="SummonTests.Data"/>'s.
/// </summary>
[Collection(SerialCollection.Name)]
public class SummonQaTests
{
    private const int Brief = 2; // the Mage's 2 s summon in SummonTests.Data

    /// <summary>
    /// SummonTests' data plus, on the Malazan Laborer: ability 0 <c>zz_qa_ring</c> (six spawns of 8 Heavy Infantry and one
    /// Crossbowman: 49 units for the 48 cells of 3 rings), ability 1 <c>zz_qa_two</c> (3 Heavy then 3 Crossbowmen), ability 2
    /// <c>zz_qa_burn</c> (3 Heavy and a Burning on own units within 6 m).
    /// </summary>
    private static GameData Data => s_data.Value;

    private static readonly Lazy<GameData> s_data = new(() =>
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        SummonTests.AddTo(dir);
        dir.EditJson("factions/malazan/abilities.json", root =>
        {
            JsonArray list = root["abilities"]!.AsArray();
            var ring = new JsonArray();
            for (int k = 0; k < 6; k++) ring.Add(Spawn("malazan_heavy_infantry", 8, 10));
            ring.Add(Spawn("malazan_crossbowman", 1, 10));
            list.Add(Summon("zz_qa_ring", ring));
            list.Add(Summon("zz_qa_two", new JsonArray(Spawn("malazan_heavy_infantry", 3, 5), Spawn("malazan_crossbowman", 3, 5))));
            JsonObject burn = Summon("zz_qa_burn", new JsonArray(Spawn("malazan_heavy_infantry", 3, 5),
                new JsonObject { ["kind"] = "applyStatus", ["status"] = "burning", ["magnitude"] = 1, ["duration"] = 5 }));
            burn["radius"] = 6;
            list.Add(burn);
        });
        dir.SetUnitField("malazan", "malazan_laborer", "abilities", "[\"zz_qa_ring\", \"zz_qa_two\", \"zz_qa_burn\"]");
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join("; ", r.Errors));
        return r.Data!;
    });

    internal static JsonObject Spawn(string unit, double count, double lifetime) =>
        new() { ["kind"] = "spawn", ["unit"] = unit, ["count"] = count, ["lifetime"] = lifetime };

    internal static JsonObject Summon(string id, JsonArray effects) => new()
    {
        ["id"] = id, ["displayName"] = "QA", ["description"] = "Test only.", ["kind"] = "summon",
        ["castTime"] = 0.1, ["cooldown"] = 100, ["affects"] = "own_units", ["autocast"] = false, ["effects"] = effects,
    };

    private static int MageT => Data.FindUnit("malazan_cadre_mage");
    private static int HeavyT => Data.FindUnit("malazan_heavy_infantry");
    private static int LaborerT => Data.FindUnit("malazan_laborer");
    private static int CrossbowT => Data.FindUnit("malazan_crossbowman");

    private static Simulation Sim(int units = 64, int size = 48) =>
        TestSim.Explored(new Simulation(new SimConfig(1, 2, units, 512) { Data = Data, Combat = false }, LocalMovementTests.Flat(size)));

    private static (int X, int Y) CellOf(World w, int slot)
    {
        Assert.True(w.NavGrid.WorldToCell(w.Units.Position[slot], out int x, out int y));
        return (x, y);
    }

    /// <summary>
    /// Places live summoned units' cells: each passable, on <paramref name="plateau"/>, within 3 rings of (cx, cy) and not
    /// it, and no two live units share a cell with a summoned one.
    /// </summary>
    private static void AssertPlacement(World w, int cx, int cy, int plateau)
    {
        UnitStore u = w.Units;
        var seen = new Dictionary<(int, int), int>();
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            (int x, int y) = CellOf(w, i);
            if (u.IsSummoned(i))
            {
                Assert.True(w.NavGrid.IsPassable(x, y), $"summoned {i} on blocked cell ({x}, {y})");
                Assert.Equal(plateau, w.Plateaus.At(y * w.NavGrid.Width + x));
                Assert.InRange(Math.Max(Math.Abs(x - cx), Math.Abs(y - cy)), 1, SummonSystem.SearchRings);
            }
            if (seen.TryGetValue((x, y), out int other) && (u.IsSummoned(i) || u.IsSummoned(other)))
                Assert.Fail($"units {other} and {i} share cell ({x}, {y})");
            seen[(x, y)] = i;
        }
    }

    // ---------------------------------------------------------------- expiry against a killing blow

    /// <summary>
    /// For every offset 1-45 ticks before a summoned Heavy's expiry tick E, a deadly Burning goes on it and on a permanent
    /// Heavy (the probe, whose death tick P is when the pulse lands). Whatever the offset the summoned unit has exactly one
    /// death event: killed (not Expired, a loss) when P &lt; E; Expired with no loss when P &gt;= E, including the offsets
    /// whose pulse lands in phase 5 of tick E itself (the expiry runs first). At least one offset hits P == E.
    /// </summary>
    [Fact]
    public void ExpiryOnTheTickOfALethalPulse_OneDeathOneEvent_ExpiryWins()
    {
        int same = 0;
        for (int k = 1; k <= 39; k++)
        {
            Simulation sim = Sim();
            World w = sim.World;
            UnitStore u = w.Units;
            EntityHandle mage = Place(sim, 0, MageT, At(sim, 20, 20));
            EntityHandle probe = Place(sim, 0, HeavyT, At(sim, 35, 35));
            sim.Enqueue(Command.UseAbility(0, mage, Brief, At(sim, 20, 20)));
            int resolve = TickOf(sim, mage, resolved: true);
            EntityHandle victim = SummonTests.Summoned(w)[0];
            int expiry = u.ExpiresAtTick[victim.Index];
            Assert.Equal(resolve + 40, expiry);
            int events = 0, victimTick = -1, probeTick = -1;
            bool expired = false;
            while (sim.TickNumber <= expiry + 80)
            {
                if (sim.TickNumber == expiry - k)
                {
                    StatusSystem.Apply(w, victim.Index, Burning, 100_000f, 60, 1);
                    StatusSystem.Apply(w, probe.Index, Burning, 100_000f, 60, 1);
                }
                sim.Tick();
                foreach (DeathEvent e in w.Deaths)
                {
                    if (e.Victim == victim)
                    {
                        events++;
                        victimTick = sim.TickNumber - 1;
                        expired = e.Expired;
                    }
                    if (e.Victim == probe) probeTick = sim.TickNumber - 1;
                }
            }
            Assert.True(events == 1, $"offset {k}: {events} death events for the summoned unit");
            Assert.True(probeTick > 0, $"offset {k}: the probe never died");
            int probeLoss = 1;
            if (probeTick < expiry)
            {
                Assert.True(!expired && victimTick == probeTick, $"offset {k}: killed at {probeTick}, event at {victimTick}, expired {expired}");
                Assert.Equal(probeLoss + 1, w.Losses[0]);
            }
            else
            {
                Assert.True(expired && victimTick == expiry, $"offset {k}: pulse at {probeTick}, expiry {expiry}, event at {victimTick}, expired {expired}");
                Assert.Equal(probeLoss, w.Losses[0]);
                if (probeTick == expiry) same++;
            }
            Assert.Equal(0, u.Statuses.Count[victim.Index]);
            Assert.Equal(0, u.SummonedUnits);
        }
        Assert.True(same > 0, "no offset put the lethal pulse on the expiry tick");
    }

    /// <summary>
    /// Mage B's three summons expire in phase 5 of tick E; Mage A, beside it, resolves its own summon in phase 6 of the same
    /// tick. After E: B's three have one Expired event each and are gone; A's three are live, ending at E + 40, on cells free
    /// at E (B's freed cells may be among them); the slots B freed are reused with a new generation; the count is 3. The next
    /// tick a dev-spawned unit takes a free slot and is permanent (no end tick, population counted).
    /// </summary>
    [Fact]
    public void ExpiryAndANewSummonInTheSameTick_ReuseTheSlotsCleanly()
    {
        // Ticks from enqueueing A's cast (between ticks) to its resolve.
        int delay;
        {
            Simulation dry = Sim();
            EntityHandle m = Place(dry, 0, MageT, At(dry, 22, 20));
            dry.Tick();
            int at = dry.TickNumber;
            dry.Enqueue(Command.UseAbility(0, m, Brief, At(dry, 22, 20)));
            delay = TickOf(dry, m, resolved: true) - at;
        }
        Simulation sim = Sim();
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle b = Place(sim, 0, MageT, At(sim, 20, 20));
        EntityHandle a = Place(sim, 0, MageT, At(sim, 22, 20));
        sim.Tick();
        sim.Enqueue(Command.UseAbility(0, b, Brief, At(sim, 20, 20)));
        int resolveB = TickOf(sim, b, resolved: true);
        List<EntityHandle> fromB = SummonTests.Summoned(w);
        Assert.Equal(3, fromB.Count);
        int e = resolveB + 40;
        while (sim.TickNumber < e - delay) sim.Tick();
        sim.Enqueue(Command.UseAbility(0, a, Brief, At(sim, 22, 20)));
        int resolveA = TickOf(sim, a, resolved: true, max: delay + 5);
        Assert.True(resolveA == e, $"A resolved at {resolveA}, B's expiry {e}, delay {delay}");
        int expiredEvents = 0;
        foreach (DeathEvent d in w.Deaths)
        {
            Assert.True(d.Expired && fromB.Contains(d.Victim), $"unexpected death {d}");
            expiredEvents++;
        }
        Assert.Equal(3, expiredEvents);
        foreach (EntityHandle h in fromB) Assert.False(u.IsAlive(h));
        List<EntityHandle> fromA = SummonTests.Summoned(w);
        Assert.Equal(3, fromA.Count);
        Assert.Equal(3, u.SummonedUnits);
        foreach (EntityHandle h in fromA) Assert.Equal(e + 40, u.ExpiresAtTick[h.Index]);
        Assert.Contains(fromA, h => fromB.Any(old => old.Index == h.Index && old.Generation != h.Generation));
        AssertPlacement(w, 22, 20, w.Plateaus.At(20 * w.NavGrid.Width + 22));
        int halfPop = w.HalfPop[0];
        sim.Enqueue(Command.SpawnUnit(0, HeavyT, At(sim, 30, 30)));
        sim.Tick();
        sim.Tick(); // a command applies in the tick after the one it was queued before
        int fresh = -1;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && !u.IsSummoned(i) && CellOf(w, i) == (30, 30)) fresh = i;
        Assert.True(fresh >= 0);
        Assert.Equal(0, u.ExpiresAtTick[fresh]);
        Assert.Equal(halfPop + w.Data.Units[HeavyT].HalfPop, w.HalfPop[0]);
        Assert.Equal(3, u.SummonedUnits);
    }

    // ---------------------------------------------------------------- fog cadence

    /// <summary>
    /// Four Mages (two per player) summon on four consecutive ticks, so their resolves and expiries fall on every phase of
    /// the 4-tick fog cadence; after every fog update for 120 ticks each player's visible cells equal the brute-force oracle.
    /// </summary>
    [Fact]
    public void SummonsAndExpiries_OnEveryCadencePhase_FogMatchesTheOracle()
    {
        var sim = new Simulation(new SimConfig(1, 2, 64, 512) { Data = Data, Combat = false }, LocalMovementTests.Flat(48));
        World w = sim.World;
        var mages = new[]
        {
            Place(sim, 0, MageT, At(sim, 8, 8)), Place(sim, 0, MageT, At(sim, 8, 38)),
            Place(sim, 1, MageT, At(sim, 38, 8)), Place(sim, 1, MageT, At(sim, 38, 38)),
        };
        sim.Tick();
        for (int k = 0; k < mages.Length; k++)
        {
            sim.Enqueue(Command.UseAbility(w.Units.Owner[mages[k].Index], mages[k], Brief, w.Units.Position[mages[k].Index]));
            sim.Tick();
        }
        int updates = 0, expiries = 0;
        for (int t = 0; t < 120; t++)
        {
            sim.Tick();
            foreach (DeathEvent d in w.Deaths) if (d.Expired) expiries++;
            if (!VisionSystem.IsUpdateTick(sim.TickNumber - 1)) continue;
            updates++;
            for (int p = 0; p < 2; p++) FogMaps.AssertMatchesOracle(w, p, $"tick {sim.TickNumber - 1}");
        }
        Assert.Equal(12, expiries);
        Assert.True(updates >= 29);
    }

    // ---------------------------------------------------------------- placement

    /// <summary>
    /// On the two-level map (cliff between x 19 and 20, ramp x 14-19, y 18-20): a Mage at (20, 10) on the plateau summons
    /// only onto the plateau; one at (19, 10) below the cliff only below it; one on the ramp top (19, 19) onto the ramp's
    /// plateau (the low ground: a ramp joins nothing above it), never onto the cliff top beside it.
    /// </summary>
    [Theory]
    [InlineData(21, 10)]
    [InlineData(19, 10)]
    [InlineData(19, 19)]
    public void BesideACliff_SummonsStayOnTheCastersPlateau(int x, int y)
    {
        var sim = new Simulation(new SimConfig(1, 2, 64, 512) { Data = Data, Combat = false }, FogMaps.TwoLevel());
        World w = sim.World;
        Assert.True(w.NavGrid.IsPassable(x, y) && w.Plateaus.At(y * w.NavGrid.Width + x) >= 0, $"({x}, {y}) passable {w.NavGrid.IsPassable(x, y)}, plateau {w.Plateaus.At(y * w.NavGrid.Width + x)}");
        EntityHandle mage = Place(sim, 0, MageT, FogMaps.Cell(x, y));
        sim.Enqueue(Command.UseAbility(0, mage, 1, FogMaps.Cell(x, y)));
        TickOf(sim, mage, resolved: true);
        Assert.Equal(3, w.Units.SummonedUnits);
        AssertPlacement(w, x, y, w.Plateaus.At(y * w.NavGrid.Width + x));
        foreach (EntityHandle h in SummonTests.Summoned(w))
        {
            (int sx, _) = CellOf(w, h.Index);
            Assert.True(x >= FogMaps.PlateauX ? sx >= FogMaps.PlateauX : sx < FogMaps.PlateauX, $"summoned at x {sx} across the cliff from {x}");
        }
    }

    /// <summary>In the map's corner (cell (1, 1), the ring around the map blocked) the three summons take the three free ring-1 cells inside the map.</summary>
    [Fact]
    public void InTheMapCorner_SummonsStayInsideTheMap()
    {
        Simulation sim = Sim();
        World w = sim.World;
        EntityHandle mage = Place(sim, 0, MageT, At(sim, 1, 1));
        sim.Enqueue(Command.UseAbility(0, mage, 1, At(sim, 1, 1)));
        TickOf(sim, mage, resolved: true);
        List<EntityHandle> s = SummonTests.Summoned(w);
        Assert.Equal(3, s.Count);
        AssertPlacement(w, 1, 1, w.Plateaus.At(1 * w.NavGrid.Width + 1));
        Assert.Equal(new[] { (1, 2), (2, 1), (2, 2) }, s.Select(h => CellOf(w, h.Index)).OrderBy(c => c).ToArray());
        for (int t = 0; t < 40; t++) sim.Tick(); // they stand in the map
        foreach (EntityHandle h in s)
        {
            Vector2 p = w.Units.Position[h.Index];
            Assert.True(float.IsFinite(p.X) && float.IsFinite(p.Y) && w.NavGrid.WorldToCell(p, out _, out _));
        }
    }

    /// <summary>Two Mages side by side resolve on the same tick: six summons on six different free cells, none on a Mage's.</summary>
    [Fact]
    public void TwoCastersResolvingTheSameTick_NeverShareACell()
    {
        Simulation sim = Sim();
        World w = sim.World;
        EntityHandle a = Place(sim, 0, MageT, At(sim, 20, 20));
        EntityHandle b = Place(sim, 0, MageT, At(sim, 21, 20));
        sim.Enqueue(Command.UseAbility(0, a, 1, At(sim, 20, 20)));
        sim.Enqueue(Command.UseAbility(0, b, 1, At(sim, 21, 20)));
        int ra = TickOf(sim, a, resolved: true);
        Assert.True(Had(sim, b, resolved: true), "not the same tick");
        Assert.Equal(6, w.Units.SummonedUnits);
        int plateau = w.Plateaus.At(20 * w.NavGrid.Width + 20);
        var cells = new HashSet<(int, int)>();
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i]) Assert.True(cells.Add(CellOf(w, i)), $"two units on {CellOf(w, i)} at tick {ra}");
    }

    /// <summary>
    /// <c>zz_qa_ring</c> on open ground asks for 49 units: the 48 cells of rings 1-3 each take one (8 + 16 + 24) and the 49th
    /// (the Crossbowman) is dropped; nothing beyond ring 3.
    /// </summary>
    [Fact]
    public void FortyNineAskedOnOpenGround_FortyEightPlaced_TheRestDropped()
    {
        Simulation sim = Sim(units: 128);
        World w = sim.World;
        EntityHandle lab = Place(sim, 0, LaborerT, At(sim, 20, 20));
        sim.Enqueue(Command.UseAbility(0, lab, 0, At(sim, 20, 20)));
        TickOf(sim, lab, resolved: true);
        Assert.Equal(48, w.Units.SummonedUnits);
        AssertPlacement(w, 20, 20, w.Plateaus.At(20 * w.NavGrid.Width + 20));
        foreach (EntityHandle h in SummonTests.Summoned(w)) Assert.Equal(HeavyT, w.Units.TypeId[h.Index]);
    }

    /// <summary><c>zz_qa_two</c> (3 Heavy then 3 Crossbowmen) with 4 free slots: 3 Heavy and 1 Crossbowman; the store is full; nothing breaks the next ticks.</summary>
    [Fact]
    public void TheStoreFillsBetweenTwoSpawnEffects_SpawnsWhatFitsInFileOrder()
    {
        Simulation sim = Sim(units: 5);
        World w = sim.World;
        EntityHandle lab = Place(sim, 0, LaborerT, At(sim, 20, 20));
        sim.Enqueue(Command.UseAbility(0, lab, 1, At(sim, 20, 20)));
        TickOf(sim, lab, resolved: true);
        List<EntityHandle> s = SummonTests.Summoned(w);
        Assert.Equal(new[] { HeavyT, HeavyT, HeavyT, CrossbowT }, s.Select(h => w.Units.TypeId[h.Index]).ToArray());
        Assert.Equal(0, w.Units.FreeCount);
        for (int t = 0; t < 120; t++) sim.Tick(); // 5 s lifetime: all gone
        Assert.Equal(0, w.Units.SummonedUnits);
        Assert.Equal(4, w.Units.FreeCount);
    }

    /// <summary><c>zz_qa_burn</c>: the Burning lands on own units within 6 m at the resolve, but not on the three units the same resolve summons (they appear after the other effects).</summary>
    [Fact]
    public void ASummonsOtherEffects_MissItsOwnNewUnits()
    {
        Simulation sim = Sim();
        World w = sim.World;
        EntityHandle lab = Place(sim, 0, LaborerT, At(sim, 20, 20));
        EntityHandle friend = Place(sim, 0, HeavyT, At(sim, 23, 20));
        sim.Enqueue(Command.UseAbility(0, lab, 2, At(sim, 20, 20)));
        TickOf(sim, lab, resolved: true);
        Assert.Equal(1, w.Units.Statuses.Count[friend.Index]);
        foreach (EntityHandle h in SummonTests.Summoned(w)) Assert.Equal(0, w.Units.Statuses.Count[h.Index]);
    }

    // ---------------------------------------------------------------- loader edges

    private static DataLoadResult LoadWithAbility(JsonObject ability)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("factions/malazan/abilities.json", root => root["abilities"]!.AsArray().Add(ability));
        dir.SetUnitField("malazan", "malazan_laborer", "abilities", $"[\"{(string)ability["id"]!}\"]");
        return DataLoader.LoadAll(dir.Path);
    }

    /// <summary>Each malformed summon is refused at its path with a message (never a crash, never a silent default).</summary>
    [Theory]
    [InlineData("lifetime-huge", "abilities[2].effects[0].lifetime")]
    [InlineData("lifetime-negative", "abilities[2].effects[0].lifetime")]
    [InlineData("lifetime-under-a-tick", "abilities[2].effects[0].lifetime")]
    [InlineData("count-huge", "abilities[2].effects[0].count")]
    [InlineData("count-negative", "abilities[2].effects[0].count")]
    [InlineData("count-missing", "abilities[2].effects[0].count")]
    [InlineData("unit-empty", "abilities[2].effects[0].unit")]
    [InlineData("unit-missing", "abilities[2].effects[0].unit")]
    [InlineData("spawn-with-amount", "abilities[2].effects[0].amount")]
    [InlineData("damage-with-unit", "abilities[2].effects[1].unit")]
    [InlineData("damage-without-radius", "abilities[2].radius")]
    [InlineData("range-zero", "abilities[2].range")]
    public void MalformedSummons_AreRefusedAtTheirPath(string row, string path)
    {
        JsonObject spawn = Spawn("malazan_heavy_infantry", 3, 5);
        var effects = new JsonArray(spawn);
        JsonObject a = Summon("zz_qa_bad", effects);
        switch (row)
        {
            case "lifetime-huge": spawn["lifetime"] = 1e12; break;
            case "lifetime-negative": spawn["lifetime"] = -5; break;
            case "lifetime-under-a-tick": spawn["lifetime"] = 0.01; break;
            case "count-huge": spawn["count"] = 1e12; break;
            case "count-negative": spawn["count"] = -3; break;
            case "count-missing": spawn.Remove("count"); break;
            case "unit-empty": spawn["unit"] = ""; break;
            case "unit-missing": spawn.Remove("unit"); break;
            case "spawn-with-amount": spawn["amount"] = 10; break;
            case "damage-with-unit":
                effects.Add(new JsonObject { ["kind"] = "damage", ["type"] = "magic", ["amount"] = 5, ["unit"] = "malazan_heavy_infantry" });
                a["radius"] = 3;
                break;
            case "damage-without-radius": effects.Add(new JsonObject { ["kind"] = "damage", ["type"] = "magic", ["amount"] = 5 }); break;
            case "range-zero": a["range"] = 0; break;
        }
        DataLoadResult r = LoadWithAbility(a);
        Assert.False(r.Ok, row + " loaded");
        Assert.True(r.Errors.Any(e => e.File == "factions/malazan/abilities.json" && e.Path == path && e.Message.Length > 10),
            $"{row}: no error at {path}: " + string.Join("; ", r.Errors));
    }

    /// <summary>Well-formed edges load: count 8 (the maximum) as 8.0, lifetime of exactly one tick. (A radius on a spawn-only summon is refused since BUG-0452: Data/StealthLoaderTests.)</summary>
    [Fact]
    public void EdgeSummons_Load()
    {
        JsonObject a = Summon("zz_qa_edge", new JsonArray(Spawn("malazan_heavy_infantry", 8.0, 0.05)));
        DataLoadResult r = LoadWithAbility(a);
        Assert.True(r.Ok, string.Join("; ", r.Errors));
        AbilityEffect e = r.Data!.Abilities[r.Data.FindAbility("zz_qa_edge")].Effects[0];
        Assert.Equal((8, 1), (e.Count, e.LifetimeTicks));
    }
}
