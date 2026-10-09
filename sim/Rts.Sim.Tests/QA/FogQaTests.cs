using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Vision;
using Xunit.Abstractions;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA attacks on M4-3a fog of war (session 2026-10-08-1435): the stamp against a second, integer-only oracle on a
/// hand-built three-level map (corners, edges, the lip from level 0 onto level 2, buildings at the border, viewers
/// sharing a cell in both orders, many updates with explored = the union of every past oracle); tiny and huge sights;
/// the 4-tick staleness of the owner's fog; the Catapult (range 24 > sight 18) with and without a spotter; a shot whose
/// shooter walked down before it landed; and pairs of worlds whose hashes and fog agree (or don't) (BUG-0215).
/// </summary>
public class FogQaTests
{
    private readonly ITestOutputHelper _out;

    public FogQaTests(ITestOutputHelper output) => _out = output;

    private const int N = 48;

    /// <summary>
    /// 48 x 48: level 0 west of x 24, level 1 east of it, a level-2 block at x 32-41, y 6-17 (and touching the east and
    /// north border at x 44-47, y 0-3), and single level-2 "pillars" standing in level 0 at (8, 30) and (0, 47) and a
    /// level-1 cell at (47, 47).
    /// </summary>
    internal static Heightmap ThreeLevel()
    {
        var levels = new byte[N * N];
        var elev = new float[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                byte l = (byte)(x >= 24 ? 1 : 0);
                if (x >= 32 && x <= 41 && y >= 6 && y <= 17) l = 2;
                if (x >= 44 && y <= 3) l = 2;
                if ((x == 8 && y == 30) || (x == 0 && y == 47)) l = 2;
                levels[y * N + x] = l;
                elev[y * N + x] = l * MapConstants.LevelHeight;
            }
        return new Heightmap(N, N, levels, elev);
    }

    /// <summary>
    /// The second oracle: integer cell distances only. Cell (x, y) is visible to a viewer in cell (vx, vy) with sight s m
    /// when (2 dx)^2 + (2 dy)^2 &lt;= s^2 (CellSize 2 m, centre to centre) and its level is at most the viewer's, or that
    /// squared distance is also &lt;= 16 (the 4 m lip). Buildings from the cell holding the footprint centre.
    /// </summary>
    internal static bool[] Oracle(World w, int player)
    {
        Heightmap hm = w.Heightmap;
        int width = hm.Width, height = hm.Height;
        var seen = new bool[width * height];
        void Mark(int vx, int vy, float sight)
        {
            int vl = hm.LevelAt(vx, vy);
            double s2 = (double)sight * sight;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    long d2 = 4L * ((x - vx) * (long)(x - vx) + (y - vy) * (long)(y - vy));
                    if (d2 <= s2 && (hm.LevelAt(x, y) <= vl || d2 <= 16)) seen[y * width + x] = true;
                }
        }
        UnitStore u = w.Units;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i] || u.Owner[i] != player) continue;
            int x = Math.Clamp((int)MathF.Floor(u.Position[i].X / MapConstants.CellSize), 0, width - 1);
            int y = Math.Clamp((int)MathF.Floor(u.Position[i].Y / MapConstants.CellSize), 0, height - 1);
            Mark(x, y, w.Data.Units[u.TypeId[i]].Sight);
        }
        BuildingStore b = w.Buildings;
        for (int j = 0; j < b.Capacity; j++)
        {
            if (!b.Alive[j] || b.Owner[j] != player) continue;
            BuildingDef d = w.Data.Buildings[b.TypeId[j]];
            Mark(b.Cell[j] % width + d.FootprintWidth / 2, b.Cell[j] / width + d.FootprintHeight / 2, d.Sight);
        }
        return seen;
    }

    private static void AssertOracle(World w, int player, string context)
    {
        bool[] expected = Oracle(w, player);
        ReadOnlySpan<byte> vis = w.Fog.Visibility(player);
        var wrong = new List<string>();
        int bad = 0;
        for (int c = 0; c < expected.Length; c++)
        {
            Assert.True(vis[c] <= VisionConstants.Visible, $"{context}: byte {vis[c]} at cell {c}");
            if ((vis[c] == VisionConstants.Visible) == expected[c]) continue;
            bad++;
            if (wrong.Count < 6) wrong.Add($"({c % w.Heightmap.Width},{c / w.Heightmap.Width}) L{w.Heightmap.Levels[c]} fog {vis[c]} oracle {expected[c]}");
        }
        Assert.True(bad == 0, $"{context} player {player}: {bad} cells differ: {string.Join("; ", wrong)}");
    }

    private static Simulation Sim(Heightmap map, GameData? data = null, int units = 96) =>
        new(new SimConfig(1, 2, units, 8 * units + 32) { Data = data ?? TestSim.Data, Combat = false }, map);

    private static int Type(GameData d, string key) => d.FindUnit(key);

    /// <summary>
    /// Corners, edges, the cliff lines, next to the level-2 pillars (a level-0 viewer sees a level-2 cell only within 4 m),
    /// a level-2 viewer, a keep at the map's corner and a watchtower against the east border, two viewers of different
    /// sight in one cell in both orders; then 40 updates with every unit jumped to a random cell (the oracle every
    /// update; explored == the union of every oracle so far, both players).
    /// </summary>
    [Fact]
    public void ThreeLevelHandMap_CornersEdgesLipAndBorderBuildings_MatchTheIntegerOracle_Over40Updates()
    {
        Simulation sim = Sim(ThreeLevel());
        World w = sim.World;
        GameData d = w.Data;
        int[] types = { Crossbowman, HeavyInfantry, Laborer, Type(d, "whirlwind_battering_ram"), Type(d, "malazan_wickan_lancer") };
        (int X, int Y)[] spots =
        {
            (0, 0), (47, 0), (0, 47), (47, 47), (23, 0), (0, 23), (47, 23), (23, 47), // corners and edges
            (23, 20), (24, 20), (9, 30), (7, 30), (8, 31), (1, 46), (31, 10), (42, 12), (36, 11), (43, 2), (45, 4),
            (12, 12), (12, 12), (30, 40), (30, 40),
        };
        int k = 0;
        foreach ((int x, int y) in spots)
        {
            Place(sim, k % 2, types[k % types.Length], new Vector2(x * 2f + 0.3f + 0.1f * (k % 5), y * 2f + 1.7f - 0.1f * (k % 7)));
            k++;
        }
        // Same cell, both orders: a big sight then a small one (12, 12) for player 0 and a small then a big (30, 40) for player 1.
        Place(sim, 0, Crossbowman, new Vector2(25f, 25f));
        Place(sim, 0, Laborer, new Vector2(25.5f, 25.5f));
        Place(sim, 1, Laborer, new Vector2(61f, 81f));
        Place(sim, 1, Crossbowman, new Vector2(61.5f, 81.5f));
        // Against the border (the ring of cells at the map's edge is blocked, so one cell in).
        Assert.True(w.Buildings.Spawn(0, d.FindBuilding("malazan_garrison_keep"), 1 * N + 1, out _), "a keep at the corner");
        // (Level 1 and 2 have no ramp here, so they are sealed pockets: buildings stand on level 0.)
        Assert.True(w.Buildings.Spawn(1, d.FindBuilding("whirlwind_lookout_tower"), 40 * N + 1, out _), "a tower at the west border");
        Assert.True(w.Buildings.Spawn(0, d.FindBuilding("malazan_watchtower"), 45 * N + 20, out _), "a tower at the south border");
        sim.Tick();
        AssertOracle(w, 0, "initial");
        AssertOracle(w, 1, "initial");
        var union = new[] { Oracle(w, 0), Oracle(w, 1) };
        var rng = new SimRng(77, 1);
        UnitStore u = w.Units;
        for (int round = 0; round < 40; round++)
        {
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i]) continue;
                // A random point anywhere on the map, edges included (the fog clamps a position onto the map).
                var p = new Vector2(rng.NextFloat() * N * 2f, rng.NextFloat() * N * 2f);
                u.Position[i] = u.PrevPosition[i] = p;
            }
            FogMaps.RunThroughNextUpdate(sim);
            for (int p = 0; p < 2; p++)
            {
                AssertOracle(w, p, $"round {round}");
                bool[] now = Oracle(w, p);
                for (int c = 0; c < now.Length; c++)
                {
                    union[p][c] |= now[c];
                    Assert.True(union[p][c] == w.Fog.IsExplored(p, c), $"round {round} player {p} cell {c}: explored {w.Fog.IsExplored(p, c)}, ever seen {union[p][c]}");
                }
            }
        }
    }

    /// <summary>The shipped data with the Laborer's sight at <paramref name="big"/> m and the Heavy Infantry's at <paramref name="small"/> m.</summary>
    private static GameData SightData(double big, double small)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.SetUnitField("malazan", "malazan_laborer", "sight", big.ToString(System.Globalization.CultureInfo.InvariantCulture));
        dir.SetUnitField("malazan", "malazan_heavy_infantry", "sight", small.ToString(System.Globalization.CultureInfo.InvariantCulture));
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join(Environment.NewLine, r.Errors));
        return r.Data!;
    }

    /// <summary>
    /// The largest sight the loader accepts (64 m = 32 cells) and a tiny one (0.5 m: only the viewer's own cell), from
    /// every corner and the middle, on maps of 128, 48 and 24 cells. A circle must not be cut by the map's size.
    /// </summary>
    [Theory]
    [InlineData(128)]
    [InlineData(48)]
    public void MaxSight64_AndTinySight_FromCornersAndCentre_MatchTheOracle(int size)
    {
        GameData d = SightData(64, 0.5);
        float far = size * 2f - 0.5f;
        foreach (Vector2 at in new[] { new Vector2(0.5f, 0.5f), new Vector2(far, far), new Vector2(0.5f, far), new Vector2(size, size) })
        {
            Simulation sim = Sim(LocalMovementTests.Flat(size), d);
            Place(sim, 0, Laborer, at);
            Place(sim, 1, HeavyInfantry, at);
            sim.Tick();
            AssertOracle(sim.World, 0, $"size {size}, sight 64 at {at}");
            AssertOracle(sim.World, 1, $"size {size}, sight 0.5 at {at}");
            Assert.Equal(1, FogMaps.Count(sim.World, 1, VisionConstants.Visible));
        }
    }

    /// <summary>
    /// A map smaller than the sight circle's diameter: the stamp's radius is capped at the map's diagonal, so a corner
    /// viewer of sight 64 m sees every cell within it on a 24-cell map (BUG-0216: it was capped at the longer side).
    /// </summary>
    [Fact]
    public void MaxSight64_OnA24CellMap_MatchesTheOracle() => MaxSight64_AndTinySight_FromCornersAndCentre_MatchTheOracle(24);

    /// <summary>
    /// The owner's fog is up to 4 ticks old (docs/03): a Catapult (range 24, sight 18) ordered onto a still enemy 21 m
    /// away is dropped alone; with a spotter beside the target it is taken and fired at; once the spotter is gone, the
    /// target stays seen until the next update and no longer, and the Catapult stops at the first scan after it.
    /// </summary>
    [Fact]
    public void Catapult_OutrangesItsSight_NeedsASpotter_AndLosesTheTargetAtTheFirstUpdateAfterTheSpotterDies()
    {
        Simulation sim = Flat(size: 64, units: 16);
        World w = sim.World;
        UnitStore u = w.Units;
        int catapultType = TestSim.Data.FindUnit("malazan_catapult");
        EntityHandle cat = Place(sim, 0, catapultType, At(sim, 10, 32));
        EntityHandle target = Place(sim, 1, HeavyInfantry, At(sim, 10, 32, 21f));
        sim.Enqueue(Command.HoldPosition(1, target));
        sim.Enqueue(Command.HoldPosition(0, cat));
        sim.Tick();
        sim.Tick();
        // Alone: the order is dropped and holding it never acquires (its reach 25.8 m is beyond its sight).
        sim.Enqueue(Command.Attack(0, cat, target, isBuilding: false));
        for (int t = 0; t < 60; t++)
        {
            sim.Tick();
            Assert.Equal(default, u.Target[cat.Index]);
        }
        Assert.Equal(0, w.Projectiles.Count);
        // A spotter 6 m beyond the target (Laborer, sight 14; the target holds and a worker doesn't fight unprovoked).
        EntityHandle spotter = Spotter(sim, 0, At(sim, 10, 32, 27f));
        Assert.True(w.Fog.CanSeeUnit(0, target.Index));
        sim.Enqueue(Command.Attack(0, cat, target, isBuilding: false));
        int hp = u.Hp[target.Index];
        RunUntil(sim, () => u.Hp[target.Index] < hp, 200);
        Assert.True(u.Hp[target.Index] < hp, "the spotted target was never hit");
        Assert.Equal(target, u.Target[cat.Index]);
        // Kill the spotter right after an update tick: the fog still shows the target until the next update.
        while (!VisionSystem.IsUpdateTick(sim.TickNumber - 1)) sim.Tick();
        u.Free(spotter);
        int nextUpdate = sim.TickNumber;
        while (!VisionSystem.IsUpdateTick(nextUpdate)) nextUpdate++;
        while (sim.TickNumber <= nextUpdate)
        {
            Assert.True(w.Fog.CanSeeUnit(0, target.Index), $"tick {sim.TickNumber}: lost before the update at {nextUpdate}");
            sim.Tick();
        }
        Assert.False(w.Fog.CanSeeUnit(0, target.Index));
        int lostBy = nextUpdate + CombatConstants.ScanInterval + 1;
        RunUntil(sim, () => u.Target[cat.Index] == default, lostBy - sim.TickNumber + 1);
        Assert.Equal(default, u.Target[cat.Index]);
        _out.WriteLine($"spotter freed after tick {nextUpdate - VisionConstants.UpdateInterval}; fog update {nextUpdate}; target dropped by tick {sim.TickNumber}");
        // And nothing more is fired at it.
        RunUntil(sim, () => w.Projectiles.Count == 0, 100);
        int after = u.Hp[target.Index];
        for (int t = 0; t < 100; t++) sim.Tick();
        Assert.Equal(after, u.Hp[target.Index]);
    }

    /// <summary>A bolt fired from level 1 reveals its shooter even if the shooter walked down to level 0 before it landed (the firing level counts, M4-3a brief).</summary>
    [Fact]
    public void AShotFiredFromHighGround_RevealsItsShooter_AfterTheShooterMovedDown()
    {
        Simulation sim = FogMaps.Sim(FogMaps.TwoLevel());
        World w = sim.World;
        UnitStore u = w.Units;
        EntityHandle victim = Place(sim, 0, HeavyInfantry, FogMaps.Cell(16, 30));
        EntityHandle shooter = Place(sim, 1, Crossbowman, FogMaps.Cell(21, 30));
        sim.Enqueue(Command.HoldPosition(0, victim));
        sim.Enqueue(Command.HoldPosition(1, shooter));
        sim.Tick();
        u.CooldownTicks[shooter.Index] = 1_000_000;
        u.Target[shooter.Index] = victim;
        ProjectileSystem.Fire(w, shooter.Index);
        Assert.Equal(1, w.Projectiles.Level[0]);
        // The shooter steps down onto level 0, far from the victim, before the bolt lands.
        u.Position[shooter.Index] = u.PrevPosition[shooter.Index] = FogMaps.Cell(4, 4);
        u.Target[shooter.Index] = default;
        int hp = u.Hp[victim.Index];
        RunUntil(sim, () => w.Projectiles.Count == 0, 40);
        Assert.True(u.Hp[victim.Index] < hp, "the bolt missed");
        Assert.True(w.Fog.RevealEnd(0, shooter.Index) > sim.TickNumber, "the shooter is not revealed");
        Assert.True(w.Fog.CanSeeUnit(0, shooter.Index));
    }

    /// <summary>
    /// The visible bits are a function of the positions at the last update, not of the state now, so they are hashed
    /// (BUG-0215). Two worlds reach one state by different paths (b's spotter visits a third point between updates, which
    /// no update sees); their hashes are equal, their fog is equal, and the same Attack order plays the same future.
    /// </summary>
    [Fact]
    public void TwoWorldsWithTheSameStateHash_PlayTheSameFuture() => SameHashScene(bLastUpdateNear: true, bDetour: true);

    /// <summary>
    /// BUG-0215's scene: at the last update a's spotter stood beside the enemy and b's far away; right after it a's is put
    /// where b's is. The positions match but the fog doesn't, and (since the fix) neither do the hashes.
    /// </summary>
    [Fact]
    public void Bug0215_TwoWorldsWhoseFogDiffers_HashDifferently() => SameHashScene(bLastUpdateNear: false, bDetour: false);

    private void SameHashScene(bool bLastUpdateNear, bool bDetour)
    {
        Simulation a = Flat(size: 64, units: 16), b = Flat(size: 64, units: 16);
        var cats = new EntityHandle[2];
        var targets = new EntityHandle[2];
        int k = 0;
        foreach (Simulation s in new[] { a, b })
        {
            // A Catapult (sight 18) 21 m from a holding enemy: only a spotter shows it, and only an Attack order takes it.
            cats[k] = Place(s, 0, TestSim.Data.FindUnit("malazan_catapult"), At(s, 10, 32));
            targets[k] = Place(s, 1, HeavyInfantry, At(s, 10, 32, 21f));
            EntityHandle spotter = Place(s, 0, Laborer, At(s, 10, 32, 27f));
            s.Enqueue(Command.HoldPosition(1, targets[k]));
            s.Enqueue(Command.HoldPosition(0, spotter));
            k++;
        }
        Vector2 near = At(a, 10, 32, 27f), far = At(a, 10, 5), aside = At(a, 50, 50);
        void Set(Simulation s, Vector2 p) => s.World.Units.Position[2] = s.World.Units.PrevPosition[2] = p;
        void ToNextUpdate()
        {
            do { a.Tick(); b.Tick(); } while (!VisionSystem.IsUpdateTick(a.TickNumber - 1));
        }
        a.Tick(); // tick 0: the initial stamp, both spotters near
        b.Tick();
        Set(a, far);
        Set(b, far);
        ToNextUpdate(); // both explore far too
        Set(a, near);
        if (bDetour)
        {
            // b's spotter stands aside for the ticks between updates, and is near again just before the update.
            Set(b, aside);
            a.Tick();
            b.Tick();
            Assert.False(VisionSystem.IsUpdateTick(a.TickNumber - 1));
        }
        if (bLastUpdateNear) Set(b, near);
        ToNextUpdate(); // the last update
        Set(a, far);    // the same positions again
        Set(b, far);
        ulong ha = a.StateHash(), hb = b.StateHash();
        bool sameFog = a.World.Fog.Visibility(0).SequenceEqual(b.World.Fog.Visibility(0));
        _out.WriteLine($"tick {a.TickNumber}: hash a {ha:X16}, b {hb:X16}; same fog: {sameFog}");
        if (!bLastUpdateNear)
        {
            Assert.False(sameFog);
            Assert.NotEqual(ha, hb);
            return;
        }
        Assert.True(sameFog);
        Assert.Equal(ha, hb);
        // The same order in both.
        a.Enqueue(Command.Attack(0, cats[0], targets[0], isBuilding: false));
        b.Enqueue(Command.Attack(0, cats[1], targets[1], isBuilding: false));
        int diverged = -1;
        bool taken = false;
        for (int t = 0; t < 40 && diverged < 0; t++)
        {
            a.Tick();
            b.Tick();
            taken |= a.World.Units.Target[cats[0].Index] == targets[0];
            if (a.StateHash() != b.StateHash()) diverged = a.TickNumber - 1;
        }
        _out.WriteLine($"diverged at tick {diverged}; a's catapult target {a.World.Units.Target[cats[0].Index]}, b's {b.World.Units.Target[cats[1].Index]}");
        Assert.Equal(-1, diverged);
        Assert.True(taken, "the order was never taken: the future is idle in both"); // (it is dropped at the next update: no spotter)
    }
}
