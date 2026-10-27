using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Abilities;
using Rts.Sim.Combat;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Replays;
using Xunit.Abstractions;
using static Rts.Sim.Tests.CombatScenes;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M4-H3 (session 2026-10-10-2334): the sim hardening's rules at their edges. BUG-0461's cast walk-in rule (an own
/// fighter ahead holds the stuck count; any other stuck walk-in drops after 3 s with one Dropped event and no cooldown; a
/// queued order behind a dropped cast; twins and replay round trips with casts dropping), BUG-0401's ordered chase on a
/// reachable building at scale, BUG-0480's death-passive stealth at 5 / 30 / 200 friends.
/// </summary>
[Collection(SerialCollection.Name)]
public class SimHardeningH3QaTests
{
    private readonly ITestOutputHelper _out;

    public SimHardeningH3QaTests(ITestOutputHelper output) => _out = output;

    private static int Mage => AbilityScenes.Mage;

    /// <summary>The dev's pass map (CastWalkInTests): enemy strip rows 0-2, a level-1 band row 3, a 2 m pass row 4, cliffs below.</summary>
    private static Heightmap PassMap() => LocalMovementTests.Rows(
        "000000000000000000000000000000",
        "000000000000000000000000000000",
        "000000000000000000000000000000",
        "111111111111111111111111111100",
        "000000000000000000000000000000",
        "111111111111111111111111111111",
        "111111111111111111111111111111",
        "111111111111111111111111111111",
        "111111111111111111111111111111");

    private static Simulation PassSim() =>
        TestSim.Explored(new Simulation(TestSim.Config(Seed: 1, PlayerCount: 2, UnitCapacity: 64, CommandCapacity: 512), PassMap()));

    private static EntityHandle Dummy(Simulation sim, int owner, Vector2 at)
    {
        EntityHandle h = Place(sim, owner, Laborer, at);
        sim.World.Units.Hold[h.Index] = true;
        sim.World.Units.Hp[h.Index] = 1_000_000;
        return h;
    }

    private static int DropsOf(Simulation sim, EntityHandle caster)
    {
        int n = 0;
        foreach (AbilityEvent e in sim.World.AbilityEvents)
            if (e.Caster == caster && e.Dropped) n++;
        return n;
    }

    /// <summary>
    /// Runs up to <paramref name="max"/> ticks after the cast order; returns the tick of the (first) drop or -1, the last tick
    /// the caster made progress, and the total drop events seen.
    /// </summary>
    private static (int Drop, int LastProgress, int Drops, string Trace) RunCast(Simulation sim, EntityHandle caster, Vector2 point, int max, bool queuedMoveAfter = false, Vector2 moveTo = default)
    {
        UnitStore u = sim.World.Units;
        sim.Enqueue(Command.UseAbility(0, caster, 0, point));
        if (queuedMoveAfter) sim.Enqueue(Command.Move(0, caster, moveTo, true));
        int drop = -1, lastProgress = -1, drops = 0;
        var trace = new System.Text.StringBuilder();
        for (int t = 0; t < max; t++)
        {
            sim.Tick();
            int ran = sim.TickNumber - 1;
            if (t % 10 == 0) trace.Append($" [{ran} {u.State[caster.Index]} c{u.CastAbility[caster.Index]} s{u.StuckTicks[caster.Index]} {u.Position[caster.Index]}]");
            if (u.CastAbility[caster.Index] >= 0 && u.State[caster.Index] == UnitState.Moving && u.StuckTicks[caster.Index] == 0) lastProgress = ran;
            int d = DropsOf(sim, caster);
            drops += d;
            if (d > 0 && drop < 0) drop = ran;
        }
        return (drop, lastProgress, drops, trace.ToString());
    }

    // ---------------------------------------------------------------- BUG-0461: who holds the count

    /// <summary>
    /// A caster pressed against a wall of enemy holders in the 2 m pass is genuinely stuck. An own crossbowman stands on the
    /// level-1 band beside it (centre 1.5 m across the pass's edge, so body edge to body edge within 1.5 m), shooting a worker
    /// on the strip: it is <i>beside</i> the caster, not in its direction of travel (its centre is offset from the caster's
    /// line of travel by far more than the two bodies' width, so it blocks nothing). The brief: an own unit within 1.5 m
    /// <i>in its direction of travel</i> holds the count; one beside must not. <paramref name="aheadBy"/> is how far the
    /// fighter's centre is in front of the caster's along the pass (0 = exactly abeam).
    /// </summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(0.3f)]
    [InlineData(0.6f)]
    public void AnOwnFighterBesideTheCaster_NotInItsWay_DoesNotHoldTheStuckCount(float aheadBy)
    {
        Simulation sim = PassSim();
        UnitStore u = sim.World.Units;
        Dummy(sim, 1, At(sim, 14, 4, 0f, -0.5f));
        Dummy(sim, 1, At(sim, 14, 4, 0f, 0.5f));
        EntityHandle target = Dummy(sim, 1, At(sim, 16, 1));
        EntityHandle mage = Place(sim, 0, Mage, At(sim, 14, 4, 0.9f));
        sim.Tick();
        sim.Tick();
        // The caster has settled against the wall: put the fighter abeam of where it stands, aheadBy towards the goal (west).
        Vector2 mp = u.Position[mage.Index];
        Spot(sim, 0, target);
        EntityHandle xb = Place(sim, 0, Crossbowman, new Vector2(mp.X - aheadBy, At(sim, 14, 3, 0f, 0.5f).Y));
        sim.Enqueue(Command.Attack(0, xb, target, false));
        sim.Tick();
        sim.Tick();
        Assert.Equal(UnitState.Attacking, u.State[xb.Index]);
        Vector2 to = u.Position[xb.Index] - u.Position[mage.Index];
        float edge = to.Length() - u.Radius[mage.Index] - u.Radius[xb.Index];
        _out.WriteLine($"fighter offset {to} (edge gap {edge:F2} m, {MathF.Atan2(MathF.Abs(to.Y), -to.X) * 180f / MathF.PI:F0} deg off the travel line)");
        Assert.True(edge <= 1.5f, $"setup: the fighter is {edge} m away edge to edge");
        (int drop, int lastProgress, int drops, string trace) = RunCast(sim, mage, At(sim, 2, 4), 300);
        Assert.True(Math.Abs(u.Position[xb.Index].Y - At(sim, 14, 3, 0f, 0.5f).Y) < 0.2f, $"setup: the fighter moved to {u.Position[xb.Index]}");
        Assert.True(drop > 0, $"a caster stuck against enemies, an own fighter only beside it ({to}), never dropped its cast in 300 ticks:{trace}");
        Assert.Equal(1, drops);
        _out.WriteLine($"dropped at {drop}, last progress {lastProgress}");
    }

    /// <summary>
    /// The ring (replay 2's layout) with a fight that doesn't end: per the brief's criterion 1 a hemmed-in caster waits; this
    /// row measures how long (documented: "for as long as the fight lasts"). Observation only: it asserts the cast is still
    /// pending after 60 s and that no Dropped event fired.
    /// </summary>
    [Fact]
    public void ACasterRingedByItsOwnFightersForever_WaitsForever_Observation()
    {
        Simulation sim = TestSim.Explored(new Simulation(TestSim.Config(Seed: 1, PlayerCount: 2, UnitCapacity: 64, CommandCapacity: 512), LocalMovementTests.Flat(48)));
        UnitStore u = sim.World.Units;
        Vector2 c = At(sim, 24, 24);
        EntityHandle dummy = Dummy(sim, 1, c + new Vector2(0f, -8f));
        Spot(sim, 0, dummy);
        for (int k = 0; k < 7; k++)
        {
            float a = k * MathF.Tau / 7 + 0.3f;
            EntityHandle f = Place(sim, 0, Crossbowman, c + new Vector2(MathF.Cos(a), MathF.Sin(a)));
            sim.Enqueue(Command.Attack(0, f, dummy, false));
        }
        EntityHandle sapper = Place(sim, 0, sim.World.Data.FindUnit("malazan_sapper"), c);
        sim.Tick();
        sim.Tick();
        (int drop, _, int drops, _) = RunCast(sim, sapper, c + new Vector2(0f, 20f), 1200);
        _out.WriteLine($"after 60 s: cast {u.CastAbility[sapper.Index]}, state {u.State[sapper.Index]}, drops {drops}, moved {Vector2.Distance(u.Position[sapper.Index], c):F2} m");
        Assert.Equal(-1, drop);
        Assert.True(u.CastAbility[sapper.Index] >= 0);
    }

    /// <summary>
    /// A genuinely stuck cast (enemy wall) with a Shift-queued Move behind it: the cast drops after 3 s with exactly one
    /// Dropped event, no cooldown, and the queued Move then runs (the caster walks back east to it).
    /// </summary>
    [Fact]
    public void ADroppedCast_FiresOneDroppedEvent_SpendsNoCooldown_AndTheQueuedOrderRuns()
    {
        Simulation sim = PassSim();
        UnitStore u = sim.World.Units;
        foreach (float dy in new[] { -0.5f, 0.5f }) Dummy(sim, 1, At(sim, 17, 4, 0f, dy));
        EntityHandle mage = Place(sim, 0, Mage, At(sim, 24, 4));
        sim.Tick();
        sim.Tick();
        Vector2 back = At(sim, 26, 4);
        (int drop, int lastProgress, int drops, string trace) = RunCast(sim, mage, At(sim, 2, 4), 400, queuedMoveAfter: true, moveTo: back);
        Assert.True(drop > 0, "never dropped" + trace);
        Assert.InRange(drop - lastProgress, 60, 62);
        Assert.Equal(1, drops);
        Assert.Equal(-1, u.CastAbility[mage.Index]);
        Assert.True(u.AbilityReadyTick[mage.Index * DataLimits.MaxUnitAbilities] <= sim.TickNumber, "a dropped cast started a cooldown");
        Assert.True(Vector2.Distance(u.Position[mage.Index], back) < 1.5f, $"the queued Move didn't run: at {u.Position[mage.Index]}, {u.State[mage.Index]}" + trace);
    }

    /// <summary>A Shift-queued cast behind a cast that drops: the queued one starts after the drop and resolves (one Dropped, one resolve).</summary>
    [Fact]
    public void AShiftQueuedCast_BehindADroppedOne_RunsAndResolves()
    {
        Simulation sim = PassSim();
        UnitStore u = sim.World.Units;
        foreach (float dy in new[] { -0.5f, 0.5f }) Dummy(sim, 1, At(sim, 17, 4, 0f, dy));
        EntityHandle mage = Place(sim, 0, Mage, At(sim, 24, 4));
        sim.Tick();
        sim.Tick();
        sim.Enqueue(Command.UseAbility(0, mage, 0, At(sim, 2, 4)));
        sim.Enqueue(Command.UseAbility(0, mage, 0, At(sim, 26, 2), queued: true));
        int drops = 0, resolves = 0, dropTick = -1, resolveTick = -1;
        for (int t = 0; t < 400; t++)
        {
            sim.Tick();
            foreach (AbilityEvent e in sim.World.AbilityEvents)
            {
                if (e.Caster != mage) continue;
                if (e.Dropped)
                {
                    drops++;
                    dropTick = sim.TickNumber - 1;
                    Assert.Equal(At(sim, 2, 4), e.Point);
                }
                else if (e.Resolved)
                {
                    resolves++;
                    resolveTick = sim.TickNumber - 1;
                    Assert.Equal(At(sim, 26, 2), e.Point);
                }
            }
        }
        _out.WriteLine($"dropped at {dropTick}, queued cast resolved at {resolveTick}");
        Assert.Equal(1, drops);
        Assert.Equal(1, resolves);
        Assert.True(resolveTick > dropTick);
    }

    /// <summary>A cast cancelled by a new order (a plain Move) raises no Dropped event; one that resolves raises none either.</summary>
    [Fact]
    public void ANewOrdersCancel_AndAResolvedCast_RaiseNoDroppedEvent()
    {
        Simulation sim = PassSim();
        EntityHandle mage = Place(sim, 0, Mage, At(sim, 24, 4));
        sim.Tick();
        sim.Tick();
        sim.Enqueue(Command.UseAbility(0, mage, 0, At(sim, 2, 4)));
        int drops = 0;
        for (int t = 0; t < 10; t++)
        {
            sim.Tick();
            drops += DropsOf(sim, mage);
        }
        sim.Enqueue(Command.Move(0, mage, At(sim, 26, 4)));
        for (int t = 0; t < 40; t++)
        {
            sim.Tick();
            drops += DropsOf(sim, mage);
        }
        Assert.Equal(-1, sim.World.Units.CastAbility[mage.Index]);
        sim.Enqueue(Command.UseAbility(0, mage, 0, At(sim, 20, 4)));
        bool resolved = false;
        for (int t = 0; t < 200; t++)
        {
            sim.Tick();
            drops += DropsOf(sim, mage);
            resolved |= AbilityScenes.Had(sim, mage, resolved: true);
        }
        Assert.True(resolved);
        Assert.Equal(0, drops);
    }

    // ---------------------------------------------------------------- BUG-0461: determinism with casts dropping

    /// <summary>
    /// Generated maps, 3 seeds x 2,000 ticks: per player 12 casters (mages and sappers) and 30 crossbowmen; every 40 ticks a
    /// seeded stream orders the crossbowmen to attack-move, every 200 ticks the casters to cast at random points within 40 m
    /// (Shift-queued 30 % of the time), plus spam (a second queued cast, casts by stale handles). Twins are hash-equal every
    /// 10 ticks; Dropped events fire (seeds chosen from 17 tried for drops: 13, 15, 42 drop 64 / 50 / 65 casts; generated maps
    /// have no deep blocked ground, so most seeds' casts all resolve); the recorded match round-trips through the text format.
    /// </summary>
    [Theory]
    [InlineData(13UL)]
    [InlineData(15UL)]
    [InlineData(42UL)]
    public void CastsDropping_OnGeneratedMaps_TwinsEqual_ReplayRoundTrips(ulong seed)
    {
        Simulation Make(out ReplayRecorder? rec, bool record)
        {
            var s = new Simulation(TestSim.Config(Seed: seed, PlayerCount: 2, UnitCapacity: 256, CommandCapacity: 1024));
            rec = record ? new ReplayRecorder(s, checkpointInterval: 50) : null;
            return s;
        }
        Simulation a = Make(out ReplayRecorder? r, true);
        Simulation b = Make(out _, false);
        GameData d = a.World.Data;
        int sapper = d.FindUnit("malazan_sapper");
        float size = a.World.NavGrid.Width * MapConstants.CellSize;
        var rng = new Determinism.SimRng(seed * 7919UL + 1, 0);
        Vector2 Point() => new(1f + rng.NextFloat() * (size - 2f), 1f + rng.NextFloat() * (size - 2f));
        NavGrid grid = a.World.NavGrid;
        // Half the casts aim into blocked ground (cliff faces, water, sealed pockets): the walk stops short, out of range.
        Vector2 Near(Vector2 from) => Vector2.Clamp(from + new Vector2(rng.NextFloat() * 80f - 40f, rng.NextFloat() * 80f - 40f), new Vector2(1f), new Vector2(size - 1f));
        Vector2 CastPoint(Vector2 from)
        {
            Vector2 p = Near(from);
            if (rng.NextFloat() < 0.5f) return p;
            // Deep in blocked ground (no passable cell within 4 cells = 8 m), so a Sapper's 6 m Cusser can't reach it.
            for (int k = 0; k < 400; k++)
            {
                p = Point();
                if (!grid.WorldToCell(p, out int x, out int y)) continue;
                bool deep = true;
                for (int dy = -4; dy <= 4 && deep; dy++)
                    for (int dx = -4; dx <= 4 && deep; dx++)
                        if (grid.IsPassable(x + dx, y + dy)) deep = false;
                if (deep) return p;
            }
            return p;
        }
        void Both(Command c)
        {
            a.Enqueue(c);
            b.Enqueue(c);
        }
        for (int p = 0; p < 2; p++)
        {
            Vector2 home = p == 0 ? new Vector2(size * 0.3f, size * 0.3f) : new Vector2(size * 0.7f, size * 0.7f);
            for (int k = 0; k < 42; k++)
            {
                int type = k < 6 ? Mage : k < 12 ? sapper : Crossbowman;
                Both(Command.SpawnUnit(p, type, home + new Vector2(k % 7 * 1.2f, k / 7 * 1.2f)));
            }
        }
        a.Tick();
        b.Tick();
        int totalDrops = 0, resolves = 0;
        for (int t = 1; t < 2000; t++)
        {
            if (t % 40 == 1)
            {
                bool casts = t % 200 == 1;
                UnitStore u = a.World.Units;
                for (int i = 0; i < u.Capacity; i++)
                {
                    if (!u.Alive[i]) continue;
                    var h = new EntityHandle(i, u.Generation[i]);
                    int owner = u.Owner[i];
                    bool caster = d.Units[u.TypeId[i]].Abilities.Length > 0;
                    if (caster)
                    {
                        if (!casts) continue;
                        Vector2 from = u.Position[i];
                        Both(Command.UseAbility(owner, h, 0, CastPoint(from), rng.NextFloat() < 0.3f));
                        if (rng.NextFloat() < 0.1f) Both(Command.UseAbility(owner, h, 0, CastPoint(from), true));
                        if (rng.NextFloat() < 0.05f) Both(Command.UseAbility(owner, new EntityHandle(i, u.Generation[i] + 1), 0, CastPoint(from)));
                    }
                    else if (rng.NextFloat() < 0.5f) Both(Command.AttackMove(owner, h, Point()));
                }
            }
            a.Tick();
            b.Tick();
            foreach (AbilityEvent e in a.World.AbilityEvents)
            {
                if (e.Dropped)
                {
                    totalDrops++;
                    Assert.False(e.Resolved);
                }
                else if (e.Resolved) resolves++;
            }
            if (t % 10 == 0) Assert.True(a.StateHash() == b.StateHash(), $"seed {seed}: twins differ after tick {a.TickNumber - 1}");
            if (t % 100 == 0) Invariants(a);
        }
        int alive = 0;
        for (int i = 0; i < a.World.Units.Capacity; i++) if (a.World.Units.Alive[i]) alive++;
        _out.WriteLine($"seed {seed}: {alive} alive at the end, {totalDrops} drops, {resolves} resolves, map {size} m");
        Assert.True(totalDrops > 0, "no cast dropped: the row doesn't exercise the rule");
        Replay replay = r!.ToReplay();
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(ReplayFormat.Write(replay), out Replay? back));
        ReplayResult result = ReplayPlayer.Run(back!, TestSim.Data);
        Assert.True(result.Ok, $"replay {result.Error} at tick {result.Tick}");
    }

    private static void Invariants(Simulation sim)
    {
        UnitStore u = sim.World.Units;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            Assert.True(float.IsFinite(u.Position[i].X) && float.IsFinite(u.Position[i].Y), $"unit {i} at {u.Position[i]}");
            Assert.InRange(u.Hp[i], 1, int.MaxValue);
            Assert.True(u.CastAbility[i] < 0 || u.State[i] is UnitState.Moving or UnitState.Casting, $"unit {i}: cast {u.CastAbility[i]} in state {u.State[i]}");
        }
    }

    // ---------------------------------------------------------------- BUG-0480 at scale

    private static readonly Lazy<GameData> ZealotStealthData = new(() =>
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        StealthTests.AddTo(dir);
        dir.EditJson("factions/whirlwind/units.json", root =>
        {
            foreach (JsonNode? n in root["units"]!.AsArray())
            {
                if ((string)n!["id"]! != "whirlwind_zealot") continue;
                JsonObject death = n["passives"]![1]!.AsObject();
                death["status"] = "stealthed";
                death.Remove("magnitude");
            }
        });
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        return r.Data!;
    });

    /// <summary>
    /// BUG-0480 at scale: a dying Zealot whose on-death passive applies Stealthed, with 5 / 30 / 200 own Zealots inside its
    /// 6 m radius and enemy 10 m detectors (StealthTests.Data's laborers) within range: every friend takes the status, every
    /// one is detected by the enemy the same tick (not hidden from it), and the call allocates 0 B (after a warm-up).
    /// </summary>
    [Theory]
    [InlineData(5)]
    [InlineData(30)]
    [InlineData(200)]
    public void ADeathPassiveStealthingManyFriends_BesideEnemyDetectors_ReachesAndDetectsEveryOne_ZeroBytes(int friends)
    {
        GameData data = ZealotStealthData.Value;
        int zealot = data.FindUnit("whirlwind_zealot");
        int status = data.FindStatus("stealthed");
        int detector = data.FindUnit("malazan_laborer");
        Assert.True(data.Units[detector].Detector >= 10f, "setup: the laborer is no detector in this data");
        long allocated = 0;
        for (int round = 0; round < 2; round++)
        {
            var sim = new Simulation(new SimConfig(1, 2, 512, 1024) { Data = data, Combat = false }, LocalMovementTests.Flat(48));
            World w = sim.World;
            UnitStore u = w.Units;
            Vector2 p = At(sim, 24, 24);
            EntityHandle dying = Place(sim, 0, zealot, p);
            var list = new List<EntityHandle>();
            var rng = new Determinism.SimRng(99, 0);
            for (int k = 0; k < friends; k++)
            {
                float a = rng.NextFloat() * MathF.Tau, rad = 0.5f + rng.NextFloat() * 5.3f;
                list.Add(Place(sim, 0, zealot, p + new Vector2(MathF.Cos(a) * rad, MathF.Sin(a) * rad)));
            }
            for (int k = 0; k < 8; k++)
            {
                float a = k * MathF.Tau / 8;
                EntityHandle e = Place(sim, 1, detector, p + new Vector2(MathF.Cos(a) * 7f, MathF.Sin(a) * 7f));
                u.Hold[e.Index] = true;
            }
            for (int t = 0; t < 8; t++) sim.Tick(); // fog and the spatial hash hold everyone
            long before = GC.GetAllocatedBytesForCurrentThread();
            PassiveSystem.OnDeath(w, dying.Index);
            allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            int got = 0, hidden = 0;
            foreach (EntityHandle f in list)
            {
                if (u.Statuses.IndexOf(f.Index, status) >= 0) got++;
                if (w.HiddenFrom(1, f.Index)) hidden++;
            }
            Assert.Equal(friends, got);
            Assert.Equal(0, hidden);
        }
        _out.WriteLine($"{friends} friends: OnDeath allocated {allocated} B");
        Assert.Equal(0L, allocated);
    }
    // ---------------------------------------------------------------- BUG-0401 at scale

    /// <summary>
    /// 96 x 64: two level-1 plateaus (west x 2-40, east x 52-93, rows 1-44) across a level-0 canyon, low ground south; a
    /// 3-wide ramp down from each plateau's south edge (west x 19-21, east x 71-73, row 45).
    /// </summary>
    private static Heightmap BigCanyon()
    {
        const int w = 96, h = 64;
        float height = MapConstants.LevelHeight;
        var levels = new byte[w * h];
        var elevations = new float[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                bool plateau = y >= 1 && y <= 44 && ((x >= 2 && x <= 40) || (x >= 52 && x <= 93));
                bool ramp = y == 45 && ((x >= 19 && x <= 21) || (x >= 71 && x <= 73));
                levels[i] = (byte)(plateau ? 1 : 0);
                elevations[i] = plateau ? height : ramp ? height / 2 : 0f;
            }
        return new Heightmap(w, h, levels, elevations);
    }

    private static (Simulation Sim, EntityHandle Tent, EntityHandle[] Army) ChaseScene(int count, bool blockRamp)
    {
        var sim = TestSim.Explored(new Simulation(TestSim.Config(Seed: 6, PlayerCount: 2, UnitCapacity: count + 16, CommandCapacity: count + 64), BigCanyon()));
        World w = sim.World;
        int tentType = w.Data.FindBuilding("whirlwind_tent");
        TestSim.KeepIn(sim, 1); // M5-H2 (rule 24): else player 1 is out on tick 1 and its Tent collapses (BUG-0703)
        Assert.True(w.Buildings.Spawn(1, tentType, 10 * w.NavGrid.Width + 60, out EntityHandle tent));
        if (blockRamp)
        {
            // A ring of enemy buildings round the ramp's foot on the low ground (camps along row 47, tents on row 45 at
            // x 66 and 78): the ramp then leads only back up, and the east plateau is out of reach on foot.
            int wd = w.NavGrid.Width;
            int camp = w.Data.FindBuilding("whirlwind_raider_camp");
            for (int x = 66; x <= 78; x += 3) Assert.True(w.Buildings.Spawn(1, camp, 47 * wd + x, out _), $"setup: camp at {x} doesn't fit");
            Assert.True(w.Buildings.Spawn(1, tentType, 45 * wd + 66, out _), "setup: the west side blocker doesn't fit");
            Assert.True(w.Buildings.Spawn(1, tentType, 45 * wd + 78, out _), "setup: the east side blocker doesn't fit");
        }
        Spotter(sim, 0, At(sim, 63, 14));
        var army = new EntityHandle[count];
        for (int k = 0; k < count; k++)
            army[k] = Place(sim, 0, k % 2 == 0 ? HeavyInfantry : Crossbowman, new Vector2(30f + k % 25 * 1.1f, 10f + k / 25 * 1.1f));
        sim.Tick();
        sim.Tick();
        for (int k = 0; k < count; k++) sim.Enqueue(Command.Attack(0, army[k], tent, isBuilding: true));
        sim.Tick();
        sim.Tick();
        return (sim, tent, army);
    }

    /// <summary>
    /// 500 units (half melee, half crossbows) on the west plateau ordered onto a Tent on the east plateau: the only way leads
    /// 70 m south first, down a 3-wide ramp, across and up the other. No unit may give the order up (BUG-0401's rule) at any
    /// tick of 4,000; the Tent takes blows; ticks during the walk allocate 0 B.
    /// </summary>
    [Fact]
    public void FiveHundredUnits_OrderedOntoAReachableBuildingThroughARampDetour_NeverGiveUp_ZeroBytes()
    {
        (Simulation sim, EntityHandle tent, EntityHandle[] army) = ChaseScene(500, blockRamp: false);
        UnitStore u = sim.World.Units;
        int firstGiveUp = -1, gaveUp = 0, maxStall = 0, blows = 0;
        int full = sim.World.Buildings.Hp[tent.Index];
        long walkBytes = 0;
        var hist = new System.Collections.Generic.Queue<string>[army.Length];
        for (int k = 0; k < army.Length; k++) hist[k] = new System.Collections.Generic.Queue<string>();
        string why = "";
        var lost = new bool[army.Length];
        for (int t = 0; t < 4000; t++)
        {
            for (int k = 0; k < army.Length; k++)
            {
                int ix = army[k].Index;
                hist[k].Enqueue($"[{sim.TickNumber} {u.State[ix]} st{u.ChaseStall[ix]} g{u.GoalCell[ix]} stuck{u.StuckTicks[ix]} {u.Position[ix]}]");
                if (hist[k].Count > 30) hist[k].Dequeue();
            }
            long before = t is >= 300 and < 400 ? GC.GetAllocatedBytesForCurrentThread() : 0;
            sim.Tick();
            if (t is >= 300 and < 400) walkBytes += GC.GetAllocatedBytesForCurrentThread() - before;
            // The tent outlasts the run: count the blows, then heal it (a test seam).
            if (sim.World.Buildings.Hp[tent.Index] < full) blows++;
            Assert.True(sim.World.Buildings.IsAlive(tent), $"setup: the tent died at tick {sim.TickNumber - 1}");
            sim.World.Buildings.SetHp(tent.Index, full);
            foreach (EntityHandle h in army)
            {
                if (!u.IsAlive(h)) continue;
                maxStall = Math.Max(maxStall, u.ChaseStall[h.Index]);
                if (u.Target[h.Index] != tent && !lost[Array.IndexOf(army, h)])
                {
                    lost[Array.IndexOf(army, h)] = true;
                    if (why.Length == 0) why = $"slot {h.Index} now {u.State[h.Index]} at {u.Position[h.Index]}: " + string.Join(" ", hist[Array.IndexOf(army, h)]);
                    gaveUp++;
                    if (firstGiveUp < 0) firstGiveUp = sim.TickNumber - 1;
                }
            }
        }
        int attacking = 0, moving = 0, idle = 0;
        foreach (EntityHandle h in army)
        {
            if (u.State[h.Index] == UnitState.Attacking) attacking++;
            else if (u.State[h.Index] == UnitState.Moving) moving++;
            else idle++;
        }
        _out.WriteLine($"gave up {gaveUp} (first at {firstGiveUp}); max stall {maxStall}; ticks with a blow {blows}; attacking {attacking}, moving {moving}, other {idle}; ticks 300-399 allocated {walkBytes} B");
        Assert.True(firstGiveUp < 0, $"a unit gave up its ordered Attack on a reachable building at tick {firstGiveUp}: {why}");
        Assert.True(blows > 0, "the tent took no blow");
        Assert.Equal(0L, walkBytes);
    }

    /// <summary>
    /// The same scene with the east ramp walled off by an enemy building (the plateau no longer reachable on foot): 60 units
    /// ordered onto the Tent all give it up (none holds the order after 60 s) and then stand (no orbit: nobody moves more
    /// than 1 m over the next 10 s).
    /// </summary>
    [Fact]
    public void AnOrderedAttackOnAnUnreachableBuilding_GivesUp_AndNobodyPaces()
    {
        (Simulation sim, EntityHandle tent, EntityHandle[] army) = ChaseScene(60, blockRamp: true);
        UnitStore u = sim.World.Units;
        int holding = 0;
        for (int t = 0; t < 1200; t++) sim.Tick();
        var at = new Vector2[army.Length];
        for (int k = 0; k < army.Length; k++)
        {
            if (u.Target[army[k].Index] == tent) holding++;
            at[k] = u.Position[army[k].Index];
        }
        for (int t = 0; t < 200; t++) sim.Tick();
        float maxMove = 0f;
        for (int k = 0; k < army.Length; k++) maxMove = MathF.Max(maxMove, Vector2.Distance(at[k], u.Position[army[k].Index]));
        _out.WriteLine($"holding after 60 s: {holding}; max move over the next 10 s {maxMove:F2} m; tent hp {sim.World.Buildings.Hp[tent.Index]}");
        Assert.Equal(0, holding);
        Assert.True(maxMove < 1f, $"a unit still paces ({maxMove} m in 10 s)");
    }

    /// <summary>
    /// QA M5-H1 (BUG-0512's fix, the other way, at scale): 500 units ordered onto the Tent with the ramp walled off. Since
    /// M5-H1 a stopped chaser whose goal was dropped is judged by the field of its chase cell (a ResolveTarget a scan) instead
    /// of "unreachable" at once. All must still give the order up within 120 s, scans must allocate nothing (every tick from the 5th to the last give-up),
    /// and the tick cost is reported (wall clock, not asserted).
    /// BUG-0914 (2,568 B once in two full runs, 0 alone): the window is only ticks 5 to ~43, so the give-up path is first
    /// reached inside it, and a one-time runtime charge (a first call's JIT or type load that an earlier test on this
    /// thread may or may not have paid) landed in the count. So the scene runs once unmeasured first, a twin, as
    /// <see cref="AllocationProbe"/> re-runs a block: a steady allocation shows in the measured run too. The message names
    /// the first allocating tick and its bytes.
    /// </summary>
    [Fact]
    public void FiveHundredUnits_OrderedOntoAnUnreachableBuilding_AllGiveUp_ZeroBytes()
    {
        GiveUpRun warm = RunGiveUp();
        GiveUpRun run = RunGiveUp();
        _out.WriteLine($"warm-up twin: gave up at tick {warm.AllGone}, {warm.Bytes} B (first at tick {warm.FirstTick}: {warm.FirstBytes} B)");
        _out.WriteLine($"all gave up at tick {run.AllGone}; still holding {run.Holding}; ticks 5 on allocated {run.Bytes} B; tick mean {run.MeanMs:F2} ms, worst {run.WorstMs:F2} ms (wall clock)");
        Assert.Equal(warm.AllGone, run.AllGone);
        Assert.Equal(0, run.Holding);
        Assert.True(run.Bytes == 0, $"ticks 5 on allocated {run.Bytes} B, the first at tick {run.FirstTick} ({run.FirstBytes} B); the warm-up twin {warm.Bytes} B");
    }

    private readonly record struct GiveUpRun(int AllGone, int Holding, long Bytes, int FirstTick, long FirstBytes, double MeanMs, double WorstMs);

    /// <summary>The 500-unit unreachable-Tent scene to the last give-up: bytes allocated on this thread from tick 5 on, the first allocating tick.</summary>
    private static GiveUpRun RunGiveUp()
    {
        (Simulation sim, EntityHandle tent, EntityHandle[] army) = ChaseScene(500, blockRamp: true);
        UnitStore u = sim.World.Units;
        long bytes = 0, firstBytes = 0;
        int firstTick = -1;
        double worst = 0, sum = 0;
        int allGone = -1;
        var clock = new System.Diagnostics.Stopwatch();
        for (int t = 0; t < 2400 && allGone < 0; t++)
        {
            long before = t >= 5 ? GC.GetAllocatedBytesForCurrentThread() : 0;
            clock.Restart();
            sim.Tick();
            clock.Stop();
            if (t >= 5)
            {
                long tick = GC.GetAllocatedBytesForCurrentThread() - before;
                if (tick != 0 && firstTick < 0) (firstTick, firstBytes) = (sim.TickNumber, tick);
                bytes += tick;
            }
            worst = Math.Max(worst, clock.Elapsed.TotalMilliseconds);
            sum += clock.Elapsed.TotalMilliseconds;
            bool any = false;
            foreach (EntityHandle h in army)
                if (u.IsAlive(h) && u.Target[h.Index] == tent) { any = true; break; }
            if (!any) allGone = sim.TickNumber;
        }
        int holding = army.Count(h => u.IsAlive(h) && u.Target[h.Index] == tent);
        return new GiveUpRun(allGone, holding, bytes, firstTick, firstBytes, sum / Math.Max(1, sim.TickNumber), worst);
    }

    /// <summary>
    /// QA M5-H1 (scale, 2x the budget): 1,000 units through the same ramp detour onto the reachable Tent. Over 4,000 ticks no
    /// unit gives the order up; the highest stall count is reported (500 units peak at 9 of the 10 scans). Measured at
    /// 1dfdef3: slot 503 (Heavy Infantry), Moving and stuck in the crowd at the Tent (stall 9), stops for movement's stuck
    /// stop (goal dropped) at tick 2269 and gives the order up at the scan of 2270.
    /// </summary>
    [Fact] // BUG-0651: no longer reproduces on the M5-H2 tree (0 of 1,000 give up, max stall 9); un-skipped to guard it
    public void ThousandUnits_OrderedOntoAReachableBuildingThroughARampDetour_NeverGiveUp()
    {
        (Simulation sim, EntityHandle tent, EntityHandle[] army) = ChaseScene(1000, blockRamp: false);
        UnitStore u = sim.World.Units;
        int full = sim.World.Buildings.Hp[tent.Index];
        int gaveUp = 0, first = -1, maxStall = 0;
        var lost = new bool[army.Length];
        var hist = new Queue<string>[army.Length];
        for (int k = 0; k < army.Length; k++) hist[k] = new Queue<string>();
        string why = "";
        for (int t = 0; t < 4000; t++)
        {
            for (int k = 0; k < army.Length; k++)
            {
                int ix = army[k].Index;
                hist[k].Enqueue($"[{sim.TickNumber} {u.State[ix]} st{u.ChaseStall[ix]} g{u.GoalCell[ix]} stuck{u.StuckTicks[ix]} {u.Position[ix].X:F1},{u.Position[ix].Y:F1}]");
                if (hist[k].Count > 40) hist[k].Dequeue();
            }
            sim.Tick();
            sim.World.Buildings.SetHp(tent.Index, full);
            for (int k = 0; k < army.Length; k++)
            {
                int ix = army[k].Index;
                maxStall = Math.Max(maxStall, u.ChaseStall[ix]);
                if (!lost[k] && u.Target[ix] != tent)
                {
                    lost[k] = true;
                    gaveUp++;
                    if (first < 0) { first = sim.TickNumber; why = $"slot {ix} ({(k % 2 == 0 ? "HI" : "xbow")}) now {u.State[ix]}: " + string.Join(" ", hist[k]); }
                }
            }
        }
        _out.WriteLine($"1,000 units: gave up {gaveUp} (first at {first}); max stall {maxStall}; {why}");
        Assert.Equal(0, gaveUp);
    }
}
