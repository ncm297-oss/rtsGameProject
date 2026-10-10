using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Abilities;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Replays;
using Rts.Sim.Vision;
using Xunit.Abstractions;
using static Rts.Sim.Tests.AbilityScenes;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M4-H2 (session 2026-10-10-0624): the sim hardening's rules at their edges. BUG-0360's unit rule with three players,
/// overlapping storms and the map corner; the death list with 2,000 units and 50 buildings dying in one tick (BUG-0330); a
/// tower's high-ground reveal on a generated map through a replay round trip (BUG-0270); the kept-chase mean bound
/// (BUG-0157) re-measured on other brawl phases.
/// </summary>
[Collection(SerialCollection.Name)]
public class SimHardeningH2QaTests
{
    private readonly ITestOutputHelper _out;

    public SimHardeningH2QaTests(ITestOutputHelper output) => _out = output;

    private static AbilityDef Sandstorm => TestSim.Data.Abilities[TestSim.Data.FindAbility("sandstorm")];
    private static int Sapper => TestSim.Data.FindUnit("malazan_sapper");

    private static Simulation ZoneScene(int players, int size = 48) =>
        new(TestSim.ConfigNoCombat(Seed: 1, PlayerCount: players, UnitCapacity: 64, CommandCapacity: 512), LocalMovementTests.Flat(size));

    private static void NextUpdate(Simulation sim)
    {
        do sim.Tick();
        while (!VisionSystem.IsUpdateTick(sim.TickNumber - 1));
    }

    // ---------- BUG-0360: the unit rule, three players ----------

    /// <summary>
    /// Three players: player 1's storm hides player 2's Raider standing 5.2 m in (its cell's centre outside the radius)
    /// from player 0 outside, but not from player 1 (the owner sees through its own storm) or player 2 (its own unit); and a
    /// Malazan Laborer of player 0 inside by its centre in an "outside" cell is hidden from player 2 outside too, both for
    /// the fog and for combat's own-sight rule.
    /// </summary>
    [Fact]
    public void ThreePlayers_TheStormHidesByTheUnitsCentre_FromEveryPlayerButItsOwner()
    {
        Simulation sim = ZoneScene(players: 3);
        World w = sim.World;
        UnitStore u = w.Units;
        Vector2 centre = At(sim, 24, 24); // (49, 49)
        EntityHandle xb0 = Place(sim, 0, Crossbowman, Off(centre, -10f));
        EntityHandle xb1 = Place(sim, 1, Crossbowman, Off(centre, 10f, 4f));
        EntityHandle xb2 = Place(sim, 2, Crossbowman, Off(centre, 0f, -10f));
        EntityHandle raider2 = Place(sim, 2, Raider, Off(centre, 5.1f, 1.1f));   // 5.2 m in; cell (27, 25), centre 6.3 m out
        EntityHandle laborer0 = Place(sim, 0, Laborer, Off(centre, -5.1f, 1.1f)); // 5.2 m in; cell (21, 25), centre 6.3 m out
        sim.Tick();
        ZoneSystem.Create(w, 1, Sandstorm, centre);
        NextUpdate(sim);
        Assert.True(w.Fog.IsVisible(0, w.Fog.CellOf(u.Position[raider2.Index])), "setup: the Raider's cell should be outside the mask");
        Assert.False(w.Fog.CanSeeUnit(0, raider2.Index), "player 0 sees player 2's Raider inside player 1's storm");
        Assert.True(w.Fog.CanSeeUnit(1, raider2.Index), "the storm's owner doesn't see through its own storm");
        Assert.True(w.Fog.CanSeeUnit(2, raider2.Index), "player 2 doesn't see its own unit");
        Assert.False(w.Fog.CanSeeUnit(2, laborer0.Index), "player 2 sees player 0's Laborer inside player 1's storm");
        Assert.True(w.Fog.CanSeeUnit(1, laborer0.Index));
        Assert.True(VisionSystem.ZoneHides(w, 0, u.Position[xb0.Index], true, u.Position[raider2.Index]));
        Assert.False(VisionSystem.ZoneHides(w, 1, u.Position[xb1.Index], true, u.Position[raider2.Index]));
        Assert.True(VisionSystem.ZoneHides(w, 2, u.Position[xb2.Index], true, u.Position[laborer0.Index]));
    }

    /// <summary>
    /// Two overlapping storms of player 1 over a Raider in their overlap: a Laborer of player 0 inside only the west storm
    /// beside it doesn't see it (the east storm still hides it); one inside only the east storm alone doesn't either; with
    /// both the Raider is seen (each storm has a viewer inside it that sees the Raider's cell). The fog and combat agree.
    /// </summary>
    [Fact]
    public void OverlappingStorms_AUnitInBoth_IsSeenOnlyWithAViewerInsideEach()
    {
        bool Seen(bool west, bool east, out bool hiddenFromWest, out bool hiddenFromEast)
        {
            Simulation sim = ZoneScene(players: 2);
            World w = sim.World;
            UnitStore u = w.Units;
            var c = new Vector2(49f, 44.8f);
            Vector2 rPos = new(49f, 49f); // cell (24, 24)'s centre: 5.16 m from each storm's centre
            EntityHandle raider = Place(sim, 1, Raider, rPos);
            Vector2 wPos = new(47f, 49f), ePos = new(51f, 49f); // the cells either side: 4.3 m into one storm, 6.5 m from the other
            EntityHandle lw = west ? Place(sim, 0, Laborer, wPos) : default;
            EntityHandle le = east ? Place(sim, 0, Laborer, ePos) : default;
            sim.Tick();
            ZoneSystem.Create(w, 1, Sandstorm, c + new Vector2(-3f, 0f));
            ZoneSystem.Create(w, 1, Sandstorm, c + new Vector2(3f, 0f));
            NextUpdate(sim);
            hiddenFromWest = VisionSystem.ZoneHides(w, 0, wPos, true, u.Position[raider.Index]);
            hiddenFromEast = VisionSystem.ZoneHides(w, 0, ePos, true, u.Position[raider.Index]);
            return w.Fog.CanSeeUnit(0, raider.Index);
        }
        Assert.False(Seen(true, false, out bool hw, out bool he), "a viewer inside only the west storm sees a unit the east storm also hides");
        Assert.True(hw && he, "combat: a viewer inside only one of the two storms should not see into the overlap");
        Assert.False(Seen(false, true, out _, out _), "a viewer inside only the east storm sees a unit the west storm also hides");
        Assert.True(Seen(true, true, out _, out _), "a viewer inside each storm: the Raider in the overlap should be seen");
    }

    /// <summary>
    /// A storm at each map corner (its box clipped by the map's edge): a Laborer of player 0 inside it sees a Raider 1.5 m
    /// away (the clipped box's bit indexes agree between the write and the read), and the Raider is hidden from a
    /// Crossbowman outside.
    /// </summary>
    [Theory]
    [InlineData(3f, 3f)]
    [InlineData(93f, 93f)]
    [InlineData(3f, 93f)]
    [InlineData(93f, 3f)]
    public void AStormAtTheMapCorner_ItsClippedRecord_SeesFromInside(float cx, float cy)
    {
        Simulation sim = ZoneScene(players: 2);
        World w = sim.World;
        var centre = new Vector2(cx, cy);
        Vector2 inward = Vector2.Normalize(new Vector2(48f, 48f) - centre);
        EntityHandle raider = Place(sim, 1, Raider, centre + inward * 0.5f);
        EntityHandle laborer = Place(sim, 0, Laborer, centre - inward * 1f);
        EntityHandle xb = Place(sim, 0, Crossbowman, centre + inward * 12f);
        sim.Tick();
        ZoneSystem.Create(w, 1, Sandstorm, centre);
        NextUpdate(sim);
        Assert.True(w.Fog.CanSeeUnit(0, raider.Index), $"corner ({cx}, {cy}): the Laborer inside doesn't see the Raider 1.5 m away");
        Assert.True(VisionSystem.ZoneHides(w, 0, w.Units.Position[xb.Index], true, w.Units.Position[raider.Index]));
        w.Units.Position[laborer.Index] = centre + inward * 9f; // walks out: nobody of player 0 inside
        NextUpdate(sim);
        Assert.False(w.Fog.CanSeeUnit(0, raider.Index), $"corner ({cx}, {cy}): seen with no viewer inside");
    }

    // ---------- BUG-0330: the death list ----------

    /// <summary>
    /// 2,000 units (every unit slot) and 50 buildings (every building slot) die in one tick: 50 Sappers of player 0 each
    /// throw a Cusser (radius widened to the loader's 16 m maximum in a data copy) on a group of one enemy Keep and 39 units at 1 hp (the
    /// enemy's and their own, friendly fire). The tick's death list holds all 2,050, no unit or building is left, and
    /// nothing throws.
    /// </summary>
    [Fact]
    public void TwoThousandUnitsAndFiftyBuildings_DieInOneTick_TheDeathListHoldsAll()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("factions/malazan/abilities.json", root =>
        {
            foreach (JsonNode? a in root["abilities"]!.AsArray())
                if ((string?)a!["id"] == "cusser") a["radius"] = 16; // the loader's maximum
        });
        DataLoadResult load = DataLoader.LoadAll(dir.Path);
        Assert.True(load.Ok, string.Join(Environment.NewLine, load.Errors));
        GameData data = load.Data!;
        const int groups = 50, perGroup = 40, units = groups * perGroup;
        var config = new SimConfig(1, 2, units, 4096) { Data = data, Combat = false, BuildingCapacity = groups };
        var sim = TestSim.Explored(new Simulation(config, LocalMovementTests.Flat(260)));
        World w = sim.World;
        UnitStore u = w.Units;
        int keep = data.FindBuilding("malazan_garrison_keep"), sapper = data.FindUnit("malazan_sapper"), laborer = data.FindUnit("malazan_laborer");
        var sappers = new List<EntityHandle>();
        for (int g = 0; g < groups; g++)
        {
            int gx = 20 + (g % 10) * 23, gy = 20 + (g / 10) * 40; // cells
            Vector2 p = w.NavGrid.CellCenter(gx, gy);
            Assert.True(w.Buildings.Spawn(1, keep, gy * w.NavGrid.Width + gx + 2, out EntityHandle hall)); // footprint 4 m east of p
            w.Buildings.Damage(hall, w.Buildings.Hp[hall.Index] - 1);
            EntityHandle s = Place(sim, 0, sapper, p);
            u.Hp[s.Index] = 1;
            sappers.Add(s);
            int placed = 1;
            for (int row = -4; row <= 4 && placed < perGroup; row++)
                for (int col = 1; col <= 6 && placed < perGroup; col++)
                {
                    EntityHandle h = Place(sim, placed % 2, laborer, p + new Vector2(-2f * col, 2f * row));
                    u.Hp[h.Index] = 1;
                    placed++;
                }
            Assert.Equal(perGroup, placed);
        }
        Assert.Equal(units, u.Count);
        Assert.Equal(groups, w.Buildings.Count);
        foreach (EntityHandle s in sappers) sim.Enqueue(Command.UseAbility(0, s, 0, u.Position[s.Index]));
        int most = 0, total = 0, at = -1;
        for (int t = 0; t < 80 && u.Count + w.Buildings.Count > 0; t++)
        {
            sim.Tick();
            int n = w.Deaths.Length;
            total += n;
            if (n > most) { most = n; at = sim.TickNumber - 1; }
        }
        _out.WriteLine($"most deaths in one tick: {most} at tick {at}; total {total}; left {u.Count} units, {w.Buildings.Count} buildings");
        Assert.Equal(units + groups, most);
        Assert.Equal(0, u.Count);
        Assert.Equal(0, w.Buildings.Count);
    }

    // ---------- BUG-0157: the mean bound on other brawls ----------

    /// <summary>
    /// BUG-0157's bound (Producer decision 2026-10-08-2144: jittered attack-move spam over intervals 1-20 ticks at 40 v 40
    /// deals on average at least 90 % of one order's damage in 400 ticks, no interval under 80 %) on brawls the dev's row
    /// doesn't run: the jitter cycle started at another corner, the fronts 6 or 10 m apart, 4 ranks deep. Measured at QA
    /// (2026-10-10-0624): means 93.7-98.8 %, worst 84-90 %; the base (chasers dropped) fell to 75.7 % mean, 60 % worst.
    /// </summary>
    [Theory]
    [InlineData(1, 8f, 5)]
    [InlineData(3, 8f, 5)]
    [InlineData(0, 6f, 5)]
    [InlineData(0, 10f, 5)]
    [InlineData(0, 8f, 4)]
    public void KeptChase_MeanBound_HoldsOnOtherBrawls(int phase, float gap, int ranks)
    {
        int[] intervals = { 1, 2, 3, 4, 5, 6, 7, 8, 10, 12, 15, 20 };
        int once = BrawlDamage(0, phase, gap, ranks);
        Assert.True(once > 0, "setup: no damage");
        double sum = 0, worst = double.MaxValue;
        var row = new List<string>();
        foreach (int every in intervals)
        {
            double share = 100.0 * BrawlDamage(every, phase, gap, ranks) / once;
            sum += share;
            worst = Math.Min(worst, share);
            row.Add($"{every}: {share:F0} %");
        }
        double mean = sum / intervals.Length;
        _out.WriteLine($"phase {phase}, gap {gap}, ranks {ranks}: one order {once}; {string.Join(", ", row)}; mean {mean:F1} %, worst {worst:F0} %");
        Assert.True(mean >= 90.0, $"mean {mean:F1} % ({string.Join(", ", row)})");
        Assert.True(worst >= 80.0, $"an interval under 80 %: {string.Join(", ", row)}");
    }

    /// <summary>40 Heavy Infantry v 40 Raiders (<see cref="FlatBrawl"/>); player 0 re-attack-moved every <paramref name="every"/> ticks (0: never) to a point jittered 2-3 m round the far block's centre, the cycle started at <paramref name="phase"/>; the damage dealt in 400 ticks.</summary>
    private static int BrawlDamage(int every, int phase, float gap, int ranks)
    {
        Simulation sim = Flat(size: 64, units: 80);
        var center = new Vector2(64f, 64f);
        FlatBrawl(sim, 40, center, gap, ranks);
        UnitStore u = sim.World.Units;
        Vector2 goal = center + new Vector2(gap / 2 + (ranks - 1) * 0.5f, 0f);
        for (int t = 1; t <= 400; t++)
        {
            if (every > 0 && t % every == 0)
            {
                int k = (t / every + phase) % 4;
                Vector2 to = goal + k switch { 0 => new Vector2(0f, 2.5f), 1 => new Vector2(2f, -2f), 2 => new Vector2(-2.5f, 0f), _ => new Vector2(0f, -3f) };
                for (int i = 0; i < u.Capacity; i++)
                    if (u.Alive[i] && u.Owner[i] == 0) sim.Enqueue(Command.AttackMove(0, new EntityHandle(i, u.Generation[i]), to));
            }
            sim.Tick();
        }
        int left = 0;
        for (int i = 0; i < u.Capacity; i++)
            if (u.Alive[i] && u.Owner[i] == 1) left += u.Hp[i];
        return 40 * sim.World.Data.Units[Raider].Hp - left;
    }

    // ---------- BUG-0270: a tower's reveal on a generated map, replayed ----------

    /// <summary>
    /// On a generated map, a Watchtower on high ground shoots a holding Crossbowman below: the reveal goes live, ends 40
    /// ticks after the last hit, and the recorded match (spawns, the hold, the reveal's hashed life) round-trips through the
    /// text format and plays back equal at every checkpoint; twins are hash-equal every tick.
    /// </summary>
    [Fact]
    public void ATowersReveal_OnAGeneratedMap_TwinsEqual_ReplayRoundTrips()
    {
        int tower = TowerTests.Watchtower;
        BuildingDef def = TestSim.Data.Buildings[tower];
        Simulation Make(out ReplayRecorder? rec, bool record, out int anchor, out Vector2 below)
        {
            var s = new Simulation(TestSim.Config(Seed: 9, PlayerCount: 2, UnitCapacity: 16, CommandCapacity: 256));
            rec = record ? new ReplayRecorder(s, checkpointInterval: 5) : null;
            Assert.True(FindCliffSpot(s.World, def, out anchor, out below), "no cliff-top spot with low ground in reach on this map");
            NavGrid g = s.World.NavGrid;
            s.Enqueue(Command.SpawnBuilding(0, tower, g.CellCenter(anchor % g.Width, anchor / g.Width)));
            s.Enqueue(Command.SpawnUnit(1, Crossbowman, below));
            s.Tick();
            s.Tick();
            return s;
        }
        Simulation a = Make(out ReplayRecorder? r, true, out int anchorA, out _);
        Simulation b = Make(out _, false, out _, out _);
        EntityHandle th = default, xb = default;
        for (int k = 0; k < a.World.Buildings.Capacity; k++)
            if (a.World.Buildings.Alive[k] && a.World.Buildings.Cell[k] == anchorA) th = a.World.Buildings.HandleOf(k);
        for (int i = 0; i < a.World.Units.Capacity; i++)
            if (a.World.Units.Alive[i] && a.World.Units.Owner[i] == 1) xb = new EntityHandle(i, a.World.Units.Generation[i]);
        Assert.True(th.Generation != 0 && xb.Generation != 0, "setup: spawns refused");
        a.Enqueue(Command.HoldPosition(1, xb));
        b.Enqueue(Command.HoldPosition(1, xb));
        int liveTicks = 0, firstLive = -1, lastLive = -1;
        for (int t = 0; t < 600; t++)
        {
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"twins differ after tick {a.TickNumber - 1}");
            if (a.World.Buildings.IsAlive(th) && a.World.Fog.BuildingRevealed(1, th.Index))
            {
                liveTicks++;
                if (firstLive < 0) firstLive = a.TickNumber;
                lastLive = a.TickNumber;
            }
        }
        _out.WriteLine($"tower at {anchorA}: reveal live {liveTicks} ticks ({firstLive}..{lastLive}); crossbow alive {a.World.Units.IsAlive(xb)}, tower hp {a.World.Buildings.Hp[th.Index]}");
        Assert.True(liveTicks >= VisionConstants.HighGroundRevealTicks, $"the reveal was live only {liveTicks} ticks");
        Replay replay = r!.ToReplay();
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(ReplayFormat.Write(replay), out Replay? back));
        ReplayResult result = ReplayPlayer.Run(back!, TestSim.Data);
        Assert.True(result.Ok, $"replay {result.Error} at tick {result.Tick}");
    }

    /// <summary>A Watchtower anchor whose footprint is all passable level-1 cells, and a passable level-0 cell 8-12 m from its centre, nearest the map's middle.</summary>
    private static bool FindCliffSpot(World w, BuildingDef def, out int anchor, out Vector2 below)
    {
        NavGrid g = w.NavGrid;
        Heightmap hm = w.Heightmap;
        anchor = -1;
        below = default;
        float best = float.MaxValue;
        var mid = new Vector2(g.Width, g.Height); // metres: the middle of a 2 m grid
        for (int y = 2; y < g.Height - def.FootprintHeight - 2; y++)
            for (int x = 2; x < g.Width - def.FootprintWidth - 2; x++)
            {
                bool ok = true;
                for (int dy = 0; dy < def.FootprintHeight && ok; dy++)
                    for (int dx = 0; dx < def.FootprintWidth && ok; dx++)
                        ok = g.IsPassable(x + dx, y + dy) && hm.LevelAt(x + dx, y + dy) == 1;
                if (!ok) continue;
                var fc = new Vector2((x + def.FootprintWidth / 2f) * MapConstants.CellSize, (y + def.FootprintHeight / 2f) * MapConstants.CellSize);
                float d = Vector2.Distance(fc, mid);
                if (d >= best) continue;
                for (int ly = y - 7; ly <= y + 7 + def.FootprintHeight; ly++)
                    for (int lx = x - 7; lx <= x + 7 + def.FootprintWidth; lx++)
                    {
                        if (lx < 1 || ly < 1 || lx >= g.Width - 1 || ly >= g.Height - 1) continue;
                        if (!g.IsPassable(lx, ly) || hm.LevelAt(lx, ly) != 0) continue;
                        Vector2 c = g.CellCenter(lx, ly);
                        float dist = Vector2.Distance(c, fc);
                        if (dist < 8f || dist > 12f) continue;
                        best = d;
                        anchor = y * g.Width + x;
                        below = c;
                        goto next;
                    }
                next:;
            }
        return anchor >= 0;
    }
}
