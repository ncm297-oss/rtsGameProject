using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Abilities;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Xunit.Abstractions;
using static Rts.Sim.Tests.ProductionMaps;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M7-2 (session 2026-11-10-0146): attacks on the multi-spawn queue item (<c>spawnCount</c>) and the <c>onHit</c> passive.
/// The population oracle here is independent of <see cref="ProductionMaps.RecountHalfPop"/>: live, non-summoned units' type
/// half-pop plus, for every started unit head, <c>HalfPop x SpawnCount</c> computed from the def's two fields.
/// </summary>
[Collection(SerialCollection.Name)]
public class PackQaTests(ITestOutputHelper output)
{
    private static GameData Pack => PackProductionTests.s_pack.Value; // Heavy Infantry spawnCount 3, pop 1

    /// <summary>Heavy Infantry as a pack of 8 at pop 3 (48 half-pop an item).</summary>
    private static readonly Lazy<GameData> s_eight = new(() => AiScenes.DataWith(dir =>
    {
        dir.SetUnitField("malazan", "malazan_heavy_infantry", "spawnCount", "8");
        dir.SetUnitField("malazan", "malazan_heavy_infantry", "pop", "3");
    }));

    /// <summary>The independent oracle of player <paramref name="p"/>'s half-pop.</summary>
    internal static int Oracle(World w, int p)
    {
        int n = 0;
        UnitStore u = w.Units;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.Owner[i] == p && !u.IsSummoned(i)) n += w.Data.Units[u.TypeId[i]].HalfPop;
        BuildingStore b = w.Buildings;
        for (int k = 0; k < b.Capacity; k++)
        {
            if (!b.Alive[k] || b.Owner[k] != p || b.Progress[k] == 0 || b.QueueCount[k] == 0 || b.QueueIsTechAt(k, 0)) continue;
            UnitDef d = w.Data.Units[b.QueueTypeAt(k, 0)];
            n += d.HalfPop * d.SpawnCount;
        }
        return n;
    }

    private static void Check(World w, string at)
    {
        for (int p = 0; p < w.HalfPop.Length; p++)
            if (w.HalfPop[p] != Oracle(w, p)) Assert.Fail($"{at}: player {p} HalfPop {w.HalfPop[p]}, oracle {Oracle(w, p)}");
    }

    private static int Count(World w, int type, int owner = 0)
    {
        int n = 0;
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.TypeId[i] == type && w.Units.Owner[i] == owner) n++;
        return n;
    }

    /// <summary>Player 0: a Keep (cap 10) at (25, 10), a Barracks at (10, 10); player 1: a Keep far off (so both "ever built").</summary>
    private static (Simulation Sim, EntityHandle Barracks, EntityHandle Keep) Base(GameData d, int laborers, int units = 64)
    {
        var sim = new Simulation(new SimConfig(5, 2, units, 544) { Data = d, Combat = false }, LocalMovementTests.Flat(48));
        World w = sim.World;
        Assert.True(w.Buildings.Spawn(0, d.FindBuilding("malazan_garrison_keep"), 10 * 48 + 25, out EntityHandle keep));
        Assert.True(w.Buildings.Spawn(0, d.FindBuilding("malazan_barracks"), 10 * 48 + 10, out EntityHandle barracks));
        Assert.True(w.Buildings.Spawn(1, d.FindBuilding("whirlwind_holy_camp"), 40 * 48 + 40, out _));
        BuildMaps.SetTotals(sim, 0, 5000, 5000);
        for (int k = 0; k < laborers; k++) CombatScenes.Place(sim, 0, d.FindUnit("malazan_laborer"), CombatScenes.At(sim, 4 + k % 8, 30 + k / 8));
        CombatScenes.Place(sim, 1, d.FindUnit("whirlwind_camp_follower"), CombatScenes.At(sim, 44, 44));
        return (sim, barracks, keep);
    }

    private static void Tick(Simulation sim, string what)
    {
        sim.Tick();
        Check(sim.World, $"{what}, tick {sim.TickNumber}");
    }

    /// <summary>
    /// The pop oracle holds every tick through each way a started (or complete, waiting) pack leaves the queue, and each
    /// exit ends where it should: cancels refund the whole cost; a destroyed producer and a defeated owner release all 6;
    /// a raised cap starts a waiting pack; a lowered cap leaves a started pack's reservation and lets it spawn.
    /// </summary>
    [Theory]
    [InlineData("cancel-at-1")]
    [InlineData("cancel-at-279")]
    [InlineData("cancel-complete-waiting")]
    [InlineData("destroyed-mid")]
    [InlineData("destroyed-complete-waiting")]
    [InlineData("owner-defeated")]
    [InlineData("cap-raised")]
    [InlineData("cap-lowered")]
    public void PopOracle_HoldsEveryTick_ThroughEveryExitOfAPack(string exit)
    {
        GameData d = Pack;
        int heavy = d.FindUnit("malazan_heavy_infantry");
        bool waiting = exit.EndsWith("waiting");
        int laborers = exit == "cap-raised" ? 8 : exit == "owner-defeated" ? 0 : 2;
        (Simulation sim, EntityHandle bar, EntityHandle keep) = Base(d, laborers, units: waiting ? 8 : 64);
        World w = sim.World;
        BuildingStore b = w.Buildings;
        int k = bar.Index, train = d.Units[heavy].TrainTicks;
        int startHalf = w.HalfPop[0];
        if (waiting) // 2 laborers + 1 enemy + 3 more: 2 free unit slots, so the complete pack waits for a third
            for (int n = 0; n < 3; n++) CombatScenes.Place(sim, 0, d.FindUnit("malazan_laborer"), CombatScenes.At(sim, 30, 30 + n));
        int before = w.HalfPop[0];
        // A command enqueued between ticks applies in phase 1 of the tick after next (before that tick's production).
        sim.Enqueue(Command.Train(0, In(sim, k), heavy));
        Tick(sim, exit);
        if (exit == "cancel-at-1") sim.Enqueue(Command.CancelTrain(0, In(sim, k), 0)); // applies when progress is 1
        Tick(sim, exit);
        Assert.Equal(5000 - d.Units[heavy].CostGold, w.Gold[0]);
        switch (exit)
        {
            case "cancel-at-1":
            case "cancel-at-279":
            {
                int at = exit == "cancel-at-1" ? 1 : train - 1;
                if (at > 1)
                {
                    while (b.Progress[k] < at - 1) Tick(sim, exit);
                    sim.Enqueue(Command.CancelTrain(0, In(sim, k), 0));
                    Tick(sim, exit);
                }
                Assert.Equal((at, before + 6), (b.Progress[k], w.HalfPop[0]));
                Tick(sim, exit); // the cancel applies in phase 1 at progress `at`, before production
                Assert.Equal((0, 0, before), (b.QueueCount[k], b.Progress[k], w.HalfPop[0]));
                Assert.Equal((5000, 5000), (w.Gold[0], w.Wood[0]));
                for (int n = 0; n < train + 20; n++) Tick(sim, exit);
                Assert.Equal(0, Count(w, heavy));
                break;
            }
            case "cancel-complete-waiting":
            case "destroyed-complete-waiting":
            {
                for (int n = 0; n < train + 30; n++) Tick(sim, exit);
                Assert.Equal((1, train, 0), (b.QueueCount[k], b.Progress[k], Count(w, heavy)));
                Assert.Equal(before + 6, w.HalfPop[0]);
                if (exit.StartsWith("cancel"))
                {
                    sim.Enqueue(Command.CancelTrain(0, In(sim, k), 0));
                    Tick(sim, exit);
                    Assert.Equal(before + 6, w.HalfPop[0]);
                    Tick(sim, exit);
                }
                else
                {
                    b.Damage(bar, 1_000_000);
                    Check(w, "right after the destruction");
                    Tick(sim, exit);
                }
                Assert.Equal(before, w.HalfPop[0]);
                Assert.Equal((5000, 5000), (w.Gold[0], w.Wood[0]));
                // A slot frees: nothing is left to spawn.
                int one = LiveUnits(w).First(i => w.Units.Owner[i] == 0);
                w.Units.Free(new EntityHandle(one, w.Units.Generation[one]));
                for (int n = 0; n < 5; n++) Tick(sim, exit);
                Assert.Equal(0, Count(w, heavy));
                break;
            }
            case "destroyed-mid":
            {
                for (int n = 0; n < 100; n++) Tick(sim, exit);
                b.Damage(bar, 1_000_000);
                Check(w, "right after the destruction");
                Assert.Equal(before, w.HalfPop[0]);
                Assert.Equal((5000, 5000), (w.Gold[0], w.Wood[0]));
                for (int n = 0; n < train; n++) Tick(sim, exit);
                Assert.Equal(0, Count(w, heavy));
                break;
            }
            case "owner-defeated":
            {
                for (int n = 0; n < 50; n++) Tick(sim, exit);
                Assert.Equal(6, w.HalfPop[0]);
                b.Damage(keep, 1_000_000);
                Tick(sim, exit);
                Assert.False(w.IsDefeated(0));
                b.Damage(bar, 1_000_000);
                Tick(sim, exit);
                Assert.True(w.IsDefeated(0));
                Assert.Equal((0, 0), (w.HalfPop[0], w.HalfPopCap[0]));
                for (int n = 0; n < train; n++) Tick(sim, exit);
                Assert.Equal(0, Count(w, heavy));
                break;
            }
            case "cap-raised":
            {
                Assert.Equal((16, 20), (w.HalfPop[0], w.HalfPopCap[0]));
                for (int n = 0; n < 40; n++) Tick(sim, exit);
                Assert.Equal((0, 16), (b.Progress[k], w.HalfPop[0]));
                Assert.True(b.Spawn(0, d.FindBuilding("malazan_billet"), 30 * 48 + 30, out _));
                Assert.Equal(36, w.HalfPopCap[0]);
                Tick(sim, exit);
                Assert.Equal((1, 22), (b.Progress[k], w.HalfPop[0]));
                for (int n = 0; n < train + 5; n++) Tick(sim, exit);
                Assert.Equal((3, 22), (Count(w, heavy), w.HalfPop[0]));
                break;
            }
            case "cap-lowered":
            {
                for (int n = 0; n < 50; n++) Tick(sim, exit);
                Assert.Equal(startHalf + 6, w.HalfPop[0]);
                b.Damage(keep, 1_000_000);
                Tick(sim, exit);
                Assert.Equal((0, startHalf + 6), (w.HalfPopCap[0], w.HalfPop[0])); // the started reservation is kept (M3-4's rule)
                for (int n = 0; n < train; n++) Tick(sim, exit);
                Assert.Equal((3, startHalf + 6), (Count(w, heavy), w.HalfPop[0]));
                break;
            }
        }
    }

    /// <summary>
    /// The Barracks on an 8 x 8 plateau whose 28 free cells are all held, then opened to 0 / 1 / 2 / 1 / 0 / 2 / 3 across
    /// consecutive ticks while a complete pack waits: nothing spawns until the tick with 3, then exactly 3 on the 3 open
    /// cells, and the reservation holds every tick.
    /// </summary>
    [Fact]
    public void ADoor_With0_1_2_1_0_2_ThenThreeFreeCells_SpawnsNothingUntilThree_ThenExactlyThree()
    {
        (Simulation sim, int bar, List<(int X, int Y)> free, List<EntityHandle> blockers) = Plateau(Pack, keepOnPlateau: false);
        World w = sim.World;
        GameData d = Pack;
        int heavy = d.FindUnit("malazan_heavy_infantry"), raider = d.FindUnit("whirlwind_raider");
        sim.Enqueue(Command.Train(0, In(sim, bar), heavy));
        int train = d.Units[heavy].TrainTicks;
        for (int n = 0; n < train + 5; n++) Tick(sim, "door");
        Assert.Equal((1, train), (w.Buildings.QueueCount[bar], w.Buildings.Progress[bar]));
        int[] open = { 0, 1, 2, 1, 0, 2, 3 };
        for (int step = 0; step < open.Length; step++)
        {
            // Exactly the first open[step] cells free: free blockers 0..n-1 (if held), re-hold the rest.
            for (int c = 0; c < 3; c++)
            {
                bool wantFree = c < open[step];
                bool held = w.Units.IsAlive(blockers[c]);
                if (wantFree && held) w.Units.Free(blockers[c]);
                if (!wantFree && !held) blockers[c] = CombatScenes.Place(sim, 1, raider, CombatScenes.At(sim, free[c].X, free[c].Y));
            }
            Tick(sim, $"door step {step}");
            int spawned = Count(w, heavy);
            if (open[step] < 3) Assert.Equal(0, spawned);
            else
            {
                Assert.Equal(3, spawned);
                Assert.Equal(0, w.Buildings.QueueCount[bar]);
                var cells = new HashSet<(int, int)>();
                foreach (int i in LiveUnits(w).Where(i => w.Units.TypeId[i] == heavy))
                {
                    Assert.True(w.NavGrid.WorldToCell(w.Units.PrevPosition[i], out int x, out int y));
                    Assert.True(cells.Add((x, y)), "two dogs on one cell");
                    Assert.Contains((x, y), free.Take(3));
                }
            }
        }
        Assert.Equal(6, w.HalfPop[0]);
    }

    /// <summary>
    /// Two producers on one plateau, the pack's Barracks first in slot order and a Keep training a Laborer after it: with 2
    /// cells open the pack spawns nobody but does not mark the plateau full, so the Laborer takes one the same tick; with 1
    /// left the pack still waits; with 3 it spawns. With 0 open neither spawns.
    /// </summary>
    [Fact]
    public void APackThatFindsTooFewCells_LeavesThemToTheNextProducerOnThePlateau()
    {
        (Simulation sim, int bar, List<(int X, int Y)> free, List<EntityHandle> blockers) = Plateau(Pack, keepOnPlateau: true);
        World w = sim.World;
        GameData d = Pack;
        int heavy = d.FindUnit("malazan_heavy_infantry"), laborer = d.FindUnit("malazan_laborer");
        int keep = -1;
        for (int k = 0; k < w.Buildings.Capacity; k++)
            if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == 0 && w.Buildings.TypeId[k] == d.FindBuilding("malazan_garrison_keep") && k > bar && OnPlateau(w, k, bar)) keep = k;
        Assert.True(keep > bar, "the keep must follow the barracks in slot order");
        sim.Enqueue(Command.Train(0, In(sim, bar), heavy));
        sim.Enqueue(Command.Train(0, In(sim, keep), laborer));
        for (int n = 0; n < d.Units[heavy].TrainTicks + 5; n++) Tick(sim, "two producers");
        Assert.Equal((0, 0), (Count(w, heavy), Count(w, laborer)));
        Assert.Equal((d.Units[heavy].TrainTicks, d.Units[laborer].TrainTicks), (w.Buildings.Progress[bar], w.Buildings.Progress[keep]));
        w.Units.Free(blockers[0]);
        w.Units.Free(blockers[1]);
        Tick(sim, "two open");
        Assert.Equal((0, 1), (Count(w, heavy), Count(w, laborer)));
        Tick(sim, "one open");
        Assert.Equal(0, Count(w, heavy));
        w.Units.Free(blockers[2]);
        w.Units.Free(blockers[3]);
        Tick(sim, "three open");
        Assert.Equal(3, Count(w, heavy));
    }

    private static bool OnPlateau(World w, int k, int other) =>
        w.Plateaus.At(w.Buildings.Cell[k]) == w.Plateaus.At(w.Buildings.Cell[other]);

    /// <summary>
    /// A level-1 plateau (8 x 8, or 8 x 14 with the Keep on it) at (20, 20) of a 48 map, ramp on its west side; the Barracks
    /// at (22, 22), a Keep for the cap (on level 0, or on the plateau at (22, 27)); every open plateau cell held by a
    /// player-1 Raider (combat off). Returns the open cells nearest the Barracks first (the order the spawn search takes them
    /// with no rally) and the blockers in the same order.
    /// </summary>
    private static (Simulation, int, List<(int X, int Y)>, List<EntityHandle>) Plateau(GameData d, bool keepOnPlateau)
    {
        const int size = 48, p = 20;
        int h = keepOnPlateau ? 14 : 8;
        var rows = new string[size];
        for (int y = 0; y < size; y++)
        {
            var row = new char[size];
            for (int x = 0; x < size; x++) row[x] = x >= p && x <= p + 7 && y >= p && y <= p + h - 1 ? '1' : '0';
            if (y == p + 3) row[p - 1] = 'r';
            rows[y] = new string(row);
        }
        var sim = TestSim.Explored(new Simulation(new SimConfig(5, 2, 96, 512) { Data = d, Combat = false }, ResourceMaps.FromRows(rows)));
        World w = sim.World;
        Assert.True(w.Buildings.Spawn(0, d.FindBuilding("malazan_barracks"), (p + 2) * size + p + 2, out EntityHandle barracks));
        Assert.True(w.Buildings.Spawn(0, d.FindBuilding("malazan_garrison_keep"), keepOnPlateau ? (p + 7) * size + p + 2 : 40 * size + 40, out _));
        Assert.True(w.Buildings.Spawn(1, d.FindBuilding("whirlwind_holy_camp"), 4 * size + 40, out _));
        BuildMaps.SetTotals(sim, 0, 5000, 5000);
        NavGrid g = w.NavGrid;
        var free = new List<(int X, int Y)>();
        for (int y = p; y <= p + h - 1; y++)
            for (int x = p; x <= p + 7; x++)
                if (g.IsPassable(x, y) && w.Plateaus.At(y * size + x) == w.Plateaus.At(w.Buildings.Cell[barracks.Index])) free.Add((x, y));
        Vector2 centre = Rts.Sim.Economy.EconomySystem.Center(g, w.Buildings.Cell[barracks.Index], 3, 3);
        int ringOf((int X, int Y) c) => RingOf(sim, barracks.Index, c.X, c.Y);
        free = free.OrderBy(ringOf).ThenBy(c => Vector2.DistanceSquared(centre, g.CellCenter(c.X, c.Y))).ThenBy(c => c.Y * size + c.X).ToList();
        var blockers = new List<EntityHandle>();
        foreach ((int x, int y) in free) blockers.Add(CombatScenes.Place(sim, 1, d.FindUnit("whirlwind_raider"), CombatScenes.At(sim, x, y)));
        return (sim, barracks.Index, free, blockers);
    }

    /// <summary>
    /// <c>spawnCount</c> 8 at pop 3 (48 half-pop) at pop 3 under a 10-pop cap: paid, never starts, blocks the queue behind it
    /// (a second item never starts either), reserves nothing; with the cap raised to 52 half (two Billets) it still waits
    /// (54 needed); at 68 (three) it starts, and 8 spawn at once.
    /// </summary>
    [Fact]
    public void APackOfEightAtPop3_UnderA10PopCap_WaitsUnpaidForNothing_ThenStartsWhenTheCapAllows()
    {
        GameData d = s_eight.Value;
        int heavy = d.FindUnit("malazan_heavy_infantry");
        Assert.Equal((8, 6, 48), (d.Units[heavy].SpawnCount, d.Units[heavy].HalfPop, d.Units[heavy].ItemHalfPop));
        (Simulation sim, EntityHandle bar, _) = Base(d, laborers: 3, units: 64);
        World w = sim.World;
        int k = bar.Index;
        Assert.Equal((6, 20), (w.HalfPop[0], w.HalfPopCap[0])); // pop 3 against a 10-pop cap
        sim.Enqueue(Command.Train(0, In(sim, k), heavy));
        sim.Enqueue(Command.Train(0, In(sim, k), heavy));
        for (int n = 0; n < 600; n++) Tick(sim, "eight");
        Assert.Equal((2, 0, 6), (w.Buildings.QueueCount[k], w.Buildings.Progress[k], w.HalfPop[0]));
        Assert.Equal(5000 - 2 * d.Units[heavy].CostGold, w.Gold[0]);
        Assert.True(w.Buildings.Spawn(0, d.FindBuilding("malazan_billet"), 30 * 48 + 30, out _));
        Tick(sim, "one billet");
        Assert.Equal((0, 36), (w.Buildings.Progress[k], w.HalfPopCap[0])); // 6 + 48 > 36
        Assert.True(w.Buildings.Spawn(0, d.FindBuilding("malazan_billet"), 34 * 48 + 30, out _));
        Tick(sim, "two billets");
        Assert.Equal((0, 52, 6), (w.Buildings.Progress[k], w.HalfPopCap[0], w.HalfPop[0])); // 6 + 48 = 54 > 52: still waits
        Assert.True(w.Buildings.Spawn(0, d.FindBuilding("malazan_billet"), 38 * 48 + 30, out _));
        Tick(sim, "three billets");
        Assert.Equal((1, 54), (w.Buildings.Progress[k], w.HalfPop[0]));
        int ran = CombatScenes.RunUntil(sim, () => Count(w, heavy) > 0, d.Units[heavy].TrainTicks + 10);
        Check(w, "spawned");
        Assert.Equal(8, Count(w, heavy));
        Assert.Equal((1, 0, 54), (w.Buildings.QueueCount[k], w.Buildings.Progress[k], w.HalfPop[0])); // the second can't start
        output.WriteLine($"8 spawned after {ran} more ticks");
    }

    /// <summary>
    /// BUG-1167: a pack whose whole population is above the rules' hard cap (here 8 x pop 13 = 104 pop against 100) can never
    /// start: the loader accepts it, the Train is accepted and paid, and the queue is wedged for the match. A single unit of
    /// pop 101 has the same hole today; a pack makes it reachable with ordinary per-unit pops. Expected: a load error.
    /// </summary>
    /// <remarks>
    /// M7-3 (BUG-1167 fixed): un-skipped; the symptom row (<c>APackAboveTheHardCap_LoadsAndWedgesTheQueue_Bug1167Symptom</c>,
    /// a 2,000-tick wedge on the old loader) is removed with the hole. The error is one, at <c>spawnCount</c>, naming both numbers.
    /// </remarks>
    [Fact]
    public void APackAboveTheHardCap_IsALoadError()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.SetUnitField("malazan", "malazan_heavy_infantry", "spawnCount", "8");
        dir.SetUnitField("malazan", "malazan_heavy_infantry", "pop", "13");
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.False(r.Ok, "a queue item of 104 pop loads against a 100-pop hard cap");
        DataError e = Assert.Single(r.Errors);
        Assert.Equal(("factions/malazan/units.json", true), (e.File, e.Path.EndsWith(".spawnCount", StringComparison.Ordinal)));
        Assert.Contains("104", e.Message);
        Assert.Contains("100", e.Message);
    }

    // ------------------------------------------------------------------ onHit

    /// <summary>Every unit bites (slowed 0.2, 2 s); two extra statuses so a unit can hold 8 entries without a slow.</summary>
    private static readonly Lazy<GameData> s_bite = new(() => AiScenes.DataWith(dir =>
    {
        OnHitPassiveTests.BiteAll(dir);
        dir.EditJson("common/statuses.json", root =>
        {
            JsonArray list = root["statuses"]!.AsArray();
            list.Add(new JsonObject { ["id"] = "qa_scorch", ["displayName"] = "Scorch", ["description"] = "QA", ["kind"] = "damageOverTime", ["damageType"] = "magic" });
            list.Add(new JsonObject { ["id"] = "qa_mire", ["displayName"] = "Mire", ["description"] = "QA", ["kind"] = "slow" });
            list.Add(new JsonObject { ["id"] = "qa_ember", ["displayName"] = "Ember", ["description"] = "QA", ["kind"] = "damageOverTime", ["damageType"] = "magic" });
        });
    }));

    private static GameData B => s_bite.Value;

    private static Simulation Field(GameData d, int units = 64) =>
        new(new SimConfig(3, 2, units, 8 * units + 32) { Data = d }, LocalMovementTests.Flat(48));

    private static EntityHandle Put(Simulation sim, int owner, string type, int x, int y, float dx = 0f) =>
        CombatScenes.Place(sim, owner, sim.World.Data.FindUnit(type), CombatScenes.At(sim, x, y, dx));

    private static int Left(Simulation sim, EntityHandle h, int status)
    {
        StatusStore s = sim.World.Units.Statuses;
        int at = s.IndexOf(h.Index, status);
        return at < 0 ? -1 : s.TicksRemaining[at];
    }

    /// <summary>Status store bookkeeping: counts in range, UnitsWithStatuses / StealthedUnits / BlindedUnits equal to the slots, free slots empty.</summary>
    private static void CheckStatuses(World w, string at)
    {
        UnitStore u = w.Units;
        StatusStore s = u.Statuses;
        int with = 0, stealthed = 0;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (s.Count[i] < 0 || s.Count[i] > StatusStore.PerUnit) Assert.Fail($"{at}: unit {i} has {s.Count[i]} entries");
            if (!u.Alive[i])
            {
                if (s.Count[i] != 0) Assert.Fail($"{at}: free slot {i} has {s.Count[i]} entries");
                continue;
            }
            if (s.Count[i] > 0) with++;
            if (s.HasStealth(i)) stealthed++;
            float m = 0f;
            int head = i * StatusStore.PerUnit;
            for (int k = 0; k < s.Count[i]; k++)
            {
                if (s.TicksRemaining[head + k] < 1) Assert.Fail($"{at}: unit {i} entry {k} has {s.TicksRemaining[head + k]} ticks left");
                for (int j = 0; j < k; j++)
                    if (s.StatusId[head + j] == s.StatusId[head + k]) Assert.Fail($"{at}: unit {i} holds status {s.StatusId[head + k]} twice");
                if (w.Data.Statuses[s.StatusId[head + k]].Kind == StatusKind.Slow) m = Math.Max(m, s.Magnitude[head + k]);
            }
            float speed = w.Data.Units[u.TypeId[i]].SpeedPerTick;
            if (u.Speed[i] > speed + 1e-6f || u.Speed[i] < speed * (1f - m) - 1e-5f) Assert.Fail($"{at}: unit {i} speed {u.Speed[i]} vs type {speed}, slow {m}");
        }
        if (with != s.UnitsWithStatuses) Assert.Fail($"{at}: UnitsWithStatuses {s.UnitsWithStatuses}, slots {with}");
        if (stealthed != s.StealthedUnits) Assert.Fail($"{at}: StealthedUnits {s.StealthedUnits}, slots {stealthed}");
    }

    /// <summary>Two biting melee units trade blows: each is slowed by the other's bite every tick it was hit; both fight on until one dies; bookkeeping exact every tick.</summary>
    [Fact]
    public void MutualBites_SlowBothFighters_UntilOneDies()
    {
        Simulation sim = Field(B);
        UnitStore u = sim.World.Units;
        int slowed = B.FindStatus("slowed");
        EntityHandle a = Put(sim, 0, "malazan_heavy_infantry", 20, 20);
        EntityHandle z = Put(sim, 1, "whirlwind_raider", 21, 20);
        Put(sim, 0, "malazan_laborer", 2, 2);
        Put(sim, 1, "whirlwind_camp_follower", 45, 45);
        int bothSlowed = 0;
        for (int t = 0; t < 2_000 && u.IsAlive(a) && u.IsAlive(z); t++)
        {
            sim.Tick();
            CheckStatuses(sim.World, $"tick {sim.TickNumber}");
            if (u.IsAlive(a) && u.IsAlive(z) && Left(sim, a, slowed) > 0 && Left(sim, z, slowed) > 0) bothSlowed++;
        }
        Assert.False(u.IsAlive(a) && u.IsAlive(z), "nobody died");
        Assert.True(bothSlowed > 10, $"both slowed on only {bothSlowed} ticks");
        EntityHandle survivor = u.IsAlive(a) ? a : z;
        for (int t = 0; t < 41; t++) sim.Tick();
        Assert.Equal(-1, Left(sim, survivor, slowed));
        Assert.Equal(B.Units[u.TypeId[survivor.Index]].SpeedPerTick, u.Speed[survivor.Index]);
    }

    /// <summary>A Frenzied biter (attack speed +50 %) bites as often as it swings; a Blinded one bites what it reaches; both slow their victim.</summary>
    [Theory]
    [InlineData("frenzied")]
    [InlineData("blinded")]
    public void ABiter_UnderFrenziedOrBlinded_StillSlowsWhatItHits(string state)
    {
        Simulation sim = Field(B);
        UnitStore u = sim.World.Units;
        int slowed = B.FindStatus("slowed");
        EntityHandle a = Put(sim, 0, "malazan_heavy_infantry", 20, 20);
        EntityHandle v = Put(sim, 1, "whirlwind_raider", 21, 20);
        u.Hold[v.Index] = true;
        u.Hp[v.Index] = 10_000; // survives everything here
        Put(sim, 0, "malazan_laborer", 2, 2);
        StatusSystem.Apply(sim.World, a.Index, B.FindStatus(state), state == "frenzied" ? 0.5f : 0f, 10_000, 0);
        int hits = 0, slowedAfterHit = 0, hp = u.Hp[v.Index];
        for (int t = 0; t < 400; t++)
        {
            sim.Tick();
            CheckStatuses(sim.World, $"{state} tick {sim.TickNumber}");
            if (u.Hp[v.Index] < hp)
            {
                hits++;
                if (Left(sim, v, slowed) == 40) slowedAfterHit++;
            }
            hp = u.Hp[v.Index];
        }
        output.WriteLine($"{state}: {hits} hits, {slowedAfterHit} with a fresh 40-tick slow");
        Assert.True(hits >= 5, $"{state}: only {hits} hits");
        Assert.Equal(hits, slowedAfterHit);
    }

    /// <summary>The killing shot of a projectile applies nothing (the slot freed with no entry), and its splash neighbour is hurt but not slowed.</summary>
    [Fact]
    public void AKillingProjectile_AppliesNothing()
    {
        Simulation sim = Field(B);
        UnitStore u = sim.World.Units;
        EntityHandle bow = Put(sim, 0, "malazan_crossbowman", 10, 20);
        EntityHandle target = Put(sim, 1, "whirlwind_camp_follower", 16, 20);
        Put(sim, 1, "whirlwind_camp_follower", 45, 45);
        u.Hp[target.Index] = 1;
        int ran = CombatScenes.RunUntil(sim, () => !u.IsAlive(target), 300);
        Assert.True(ran < 300 && u.IsAlive(bow));
        Assert.Equal(0, u.Statuses.Count[target.Index]);
        CheckStatuses(sim.World, "after the kill");
    }

    /// <summary>
    /// A stealthed biter's hit reveals it (the Revealed status lands on the attacker) and its bite still slows the victim, both
    /// in the same tick.
    /// </summary>
    [Fact]
    public void AStealthedBiter_IsRevealed_AndItsBiteStillLands()
    {
        Simulation sim = Field(B);
        UnitStore u = sim.World.Units;
        int slowed = B.FindStatus("slowed"), stealthed = B.FindStatus("stealthed"), revealed = B.FindStatus("revealed");
        EntityHandle a = Put(sim, 0, "malazan_heavy_infantry", 20, 20);
        EntityHandle v = Put(sim, 1, "whirlwind_raider", 21, 20);
        Put(sim, 0, "malazan_laborer", 2, 2);
        Put(sim, 1, "whirlwind_camp_follower", 45, 45);
        u.Hp[v.Index] = 10_000;
        u.Hold[v.Index] = true;
        StatusSystem.Apply(sim.World, a.Index, stealthed, 0f, 10_000, 0);
        Assert.Equal(1, u.Statuses.StealthedUnits);
        sim.Enqueue(Command.Attack(0, a, v, false, false));
        int hp = u.Hp[v.Index];
        int ran = CombatScenes.RunUntil(sim, () => u.Hp[v.Index] < hp, 300);
        Assert.True(ran < 300, "no hit");
        Assert.True(Left(sim, a, revealed) > 0, "the attack from stealth did not reveal its attacker");
        Assert.Equal(40, Left(sim, v, slowed));
        CheckStatuses(sim.World, "after the reveal");
    }

    /// <summary>
    /// The victim already holds 8 entries (the per-unit limit) and no slow: the bite is dropped quietly (no entry, speed
    /// unchanged, bookkeeping exact). With a slow among the 8, the bite refreshes it to 40 ticks.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ABite_OnAVictimWithAFullStatusStore_IsDroppedOrRefreshes(bool slowAmongThem)
    {
        Simulation sim = Field(B);
        World w = sim.World;
        UnitStore u = w.Units;
        int slowed = B.FindStatus("slowed");
        EntityHandle a = Put(sim, 0, "malazan_heavy_infantry", 20, 20);
        EntityHandle v = Put(sim, 1, "whirlwind_raider", 30, 30);
        Put(sim, 0, "malazan_laborer", 2, 2);
        int n = 0;
        if (slowAmongThem)
        {
            StatusSystem.Apply(w, v.Index, slowed, 0.1f, 500, 0);
            n++;
        }
        foreach (StatusDef def in B.Statuses)
        {
            if (n == StatusStore.PerUnit) break;
            int id = def.Id;
            if (id == slowed) continue;
            if (def.Kind == StatusKind.Stealthed) continue; // keep the victim visible
            StatusSystem.Apply(w, v.Index, id, def.Kind switch { StatusKind.Slow => 0.1f, StatusKind.DamageOverTime => 1f, StatusKind.Frenzied => 0.2f, StatusKind.Regenerating => 1f, _ => 0f }, 500, 0);
            n++;
        }
        if (n < StatusStore.PerUnit) // fill with the rest by any order
            output.WriteLine($"only {n} statuses applied");
        Assert.Equal(StatusStore.PerUnit, u.Statuses.Count[v.Index]);
        Assert.Equal(slowAmongThem, u.Statuses.IndexOf(v.Index, slowed) >= 0);
        float speed = u.Speed[v.Index];
        CombatSystem.HitUnit(w, new PendingHit(a, 0, v, false, 5), u.TypeId[a.Index]);
        CheckStatuses(w, "after the bite");
        Assert.Equal(StatusStore.PerUnit, u.Statuses.Count[v.Index]);
        if (slowAmongThem)
        {
            Assert.Equal(500, Left(sim, v, slowed)); // 500 > 40: kept the longer
            Assert.Equal(0.2f, u.Statuses.Magnitude[u.Statuses.IndexOf(v.Index, slowed)]); // the stronger magnitude
        }
        else
        {
            Assert.Equal(-1, Left(sim, v, slowed));
            Assert.Equal(speed, u.Speed[v.Index]);
        }
    }

    /// <summary>
    /// Exotic onHit kinds through the mid-phase-11 apply path (data may set them): a bite that blinds, one that burns, and one
    /// that stealths its victim. A short brawl with each keeps the status bookkeeping exact every tick and hashes the same twice.
    /// </summary>
    [Theory]
    [InlineData("blinded", 0)]
    [InlineData("burning", 3)]
    [InlineData("stealthed", 0)]
    [InlineData("revealed", 0)]
    public void ExoticOnHitStatuses_KeepTheStoreExact_AndDeterministic(string status, double magnitude)
    {
        GameData d = AiScenes.DataWith(dir =>
        {
            foreach (string f in TestSim.ShippedFactions)
                dir.EditJson($"factions/{f}/units.json", root =>
                {
                    foreach (JsonNode? unit in root["units"]!.AsArray())
                    {
                        if (unit!["passives"] is not JsonArray list) unit["passives"] = list = new JsonArray();
                        var bite = new JsonObject { ["trigger"] = "onHit", ["status"] = status, ["duration"] = 2 };
                        if (magnitude > 0) bite["magnitude"] = magnitude;
                        list.Add(bite);
                    }
                });
        });
        ulong Run()
        {
            var sim = new Simulation(new SimConfig(9, 2, 200, 8 * 200 + 32) { Data = d }, LocalMovementTests.Flat(64));
            CombatScenes.MixedBrawl(sim, 40, new Vector2(64f, 64f), gap: 6f);
            ulong digest = 14695981039346656037UL;
            for (int t = 0; t < 800; t++)
            {
                sim.Tick();
                CheckStatuses(sim.World, $"{status} tick {sim.TickNumber}");
                digest = (digest ^ sim.StateHash()) * 1099511628211UL;
            }
            return digest;
        }
        Assert.Equal(Run(), Run());
    }

    // ------------------------------------------------------------------ data robustness

    [Theory]
    [InlineData("\"3\"")]
    [InlineData("true")]
    [InlineData("1e300")]
    [InlineData("8.0000001")]
    [InlineData("-0")]
    [InlineData("[3]")]
    public void SpawnCount_BadValues_AreOneErrorAtThePath(string raw)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.SetUnitField("malazan", "malazan_heavy_infantry", "spawnCount", raw);
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.False(r.Ok, $"spawnCount {raw} loaded");
        output.WriteLine(string.Join("\n", r.Errors));
        Assert.Contains(r.Errors, e => e.ToString()!.Contains("spawnCount"));
    }

    [Fact]
    public void SpawnCount_Eight_AndAnExplicitOne_Load()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.SetUnitField("malazan", "malazan_heavy_infantry", "spawnCount", "8.0");
        dir.SetUnitField("malazan", "malazan_laborer", "spawnCount", "1");
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        Assert.Equal(8, r.Data!.Units[r.Data.FindUnit("malazan_heavy_infantry")].SpawnCount);
        Assert.NotEqual(TestSim.Data.ContentHash(), r.Data.ContentHash());
    }

    [Theory]
    [InlineData("{\"trigger\": \"onHit\", \"status\": \"slowed\", \"duration\": 2}", "magnitude")]
    [InlineData("{\"trigger\": \"onHit\", \"status\": \"slowed\", \"magnitude\": 0.2, \"duration\": 0.01}", "duration")]
    [InlineData("{\"trigger\": \"onHit\", \"status\": \"slowed\", \"magnitude\": 0.2, \"duration\": -2}", "duration")]
    [InlineData("{\"trigger\": \"onHit\", \"status\": \"slowed\", \"magnitude\": 0, \"duration\": 2}", "magnitude")]
    [InlineData("{\"trigger\": \"onHit\", \"status\": \"burning\", \"magnitude\": 3, \"duration\": 2.5}", "duration")]
    [InlineData("{\"trigger\": \"onHit\", \"magnitude\": 0.2, \"duration\": 2}", "status")]
    [InlineData("{\"trigger\": \"OnHit\", \"status\": \"slowed\", \"magnitude\": 0.2, \"duration\": 2}", "trigger")]
    [InlineData("{\"trigger\": \"onHit\", \"status\": \"slowed\", \"magnitude\": 0.2, \"duration\": 2, \"count\": 3}", "count")]
    public void OnHit_BadEntries_AreErrorsNamingTheField(string entry, string field)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.SetUnitField("malazan", "malazan_heavy_infantry", "passives", $"[{entry}]");
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        output.WriteLine(string.Join("\n", r.Errors));
        Assert.False(r.Ok, $"{entry} loaded");
        Assert.Contains(r.Errors, e => e.ToString()!.Contains(field));
    }

    /// <summary>The golden header's data hash: shipped data hashes 64DF327980743173 (the M7-2 hash rule: no default appends anything).</summary>
    [Fact]
    public void TheShippedDataHash_IsTheGoldenHeaders()
    {
        // Producer patch at the 2026-11-10-0146 merge (BUG-1169): compared dynamically, not to a literal, because the golden's
        // header is the hash at recording time, not a pin (M6-SH1), and the data track moves it (D27 moved it to 3E33BCE5D418EC9B
        // in the same session as M7-2, whose own rule was "unchanged by the sim": 64DF327980743173 at its base).
        string golden = File.ReadAllText(Path.Combine(TestDataDir.RepoRoot(), "sim", "Rts.Sim.Tests", "Replays", "cross_map_seed1.replay"));
        Assert.Contains("data-hash " + TestSim.Data.ContentHash().ToString("X16"), golden);
    }
    // ------------------------------------------------------------------ BUG-1168: a pack unit's worth

    /// <summary>
    /// BUG-1168: a pack's cost is the item's (docs/03 "Implementation (M7-2)": "cost and trainTime are the pack's"), but every
    /// per-unit worth in the sim reads <c>CostGold + CostWood</c> per live unit, so three dogs of one 54 / 20 pack count 222,
    /// not 74. The end-of-match army-value sample (<see cref="Economy.MatchStats.Sample"/>) is the player-facing one; the AI's
    /// wave <c>minArmyCost</c>, reserve and defense worths read the same per-unit cost.
    /// </summary>
    /// <remarks>
    /// M7-3 (BUG-1168 fixed): un-skipped. The worth rule (docs/03 "Implementation (M7-3)") is each unit's share of the item,
    /// <c>cost / spawnCount</c> per resource with the remainder dropped: 54 / 20 of three is 18 + 6 = 24 a unit, so the pack
    /// samples <b>72</b>, not the 74 paid (the 2 wood of 20 / 3's remainder) and not 222. The QA row as written expected 74;
    /// the brief's integer rule gives 72 for this pack (the developer's report, M7-3). A pack whose cost divides (the war-dog's
    /// 120 / 0) samples exactly what was paid (<c>Ai/PackWorthTests</c>).
    /// </remarks>
    [Fact]
    public void APacksArmyValue_IsTheItemsCost_NotSpawnCountTimesIt()
    {
        UnitDef heavy = Pack.Units[Pack.FindUnit("malazan_heavy_infantry")];
        Assert.Equal((18, 6), (heavy.UnitCostGold, heavy.UnitCostWood));
        Assert.Equal(72, PackArmyValue());
    }

    private static int PackArmyValue()
    {
        GameData d = Pack;
        int heavy = d.FindUnit("malazan_heavy_infantry");
        Assert.Equal(74, d.Units[heavy].CostGold + d.Units[heavy].CostWood);
        (Simulation sim, EntityHandle bar, _) = Base(d, laborers: 0);
        World w = sim.World;
        sim.Enqueue(Command.Train(0, In(sim, bar.Index), heavy));
        int ran = CombatScenes.RunUntil(sim, () => Count(w, heavy) == 3, 400);
        Assert.True(ran < 400);
        int before = w.Ledger.Stats.SampleCount;
        w.Ledger.Stats.Sample(w.Units, d);
        Assert.Equal(before + 1, w.Ledger.Stats.SampleCount);
        return w.Ledger.Stats.Samples(0)[before];
    }
    /// <summary>
    /// A save made while a complete pack waits at the head for unit slots (progress = train ticks, 6 reserved) loads to the
    /// same state and bytes; when a slot frees in both, both spawn the three on the same tick and hash the same for 100 ticks.
    /// </summary>
    [Fact]
    public void ASave_WhileACompletePackWaits_LoadsAndSpawnsTheSame()
    {
        GameData d = Pack;
        int heavy = d.FindUnit("malazan_heavy_infantry");
        (Simulation sim, EntityHandle bar, _) = Base(d, laborers: 2, units: 8);
        World w = sim.World;
        for (int n = 0; n < 3; n++) CombatScenes.Place(sim, 0, d.FindUnit("malazan_laborer"), CombatScenes.At(sim, 30, 30 + n));
        sim.Enqueue(Command.Train(0, In(sim, bar.Index), heavy));
        for (int n = 0; n < d.Units[heavy].TrainTicks + 10; n++) sim.Tick();
        Assert.Equal((1, d.Units[heavy].TrainTicks, 0), (w.Buildings.QueueCount[bar.Index], w.Buildings.Progress[bar.Index], Count(w, heavy)));
        byte[] bytes = Save.SaveTestRun.Save(sim);
        Simulation loaded = Save.SaveTestRun.Load(d, bytes);
        Assert.Equal(sim.StateHash(), loaded.StateHash());
        Assert.Equal(bytes, Save.SaveTestRun.Save(loaded));
        Assert.Equal(w.HalfPop[0], loaded.World.HalfPop[0]);
        int one = LiveUnits(w).First(i => w.Units.Owner[i] == 0);
        foreach (Simulation s in new[] { sim, loaded }) s.World.Units.Free(new EntityHandle(one, s.World.Units.Generation[one]));
        for (int n = 0; n < 100; n++)
        {
            sim.Tick();
            loaded.Tick();
            Assert.True(sim.StateHash() == loaded.StateHash(), $"diverged at tick {sim.TickNumber}");
            Check(loaded.World, $"loaded tick {loaded.TickNumber}");
        }
        Assert.Equal((3, 3), (Count(w, heavy), Count(loaded.World, heavy)));
    }
}
