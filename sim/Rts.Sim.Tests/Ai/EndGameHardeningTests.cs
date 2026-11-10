using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M5-H5, the AI's end-game hardening (docs/03 "Implementation (M5-4d)" and "(M5-4d slice 2)"), on the flat scene, combat
/// off: BUG-1119 (a defeated seat's remembered buildings are no known enemy building). Player 0 is a Normal Malazan AI
/// whose army is placed by the test; players 1 and 2 are humans that never play.
/// </summary>
public class EndGameHardeningTests
{
    private readonly ITestOutputHelper? _out;

    public EndGameHardeningTests(ITestOutputHelper output) => _out = output;

    private static int Heavy => TestSim.Data.FindUnit("malazan_heavy_infantry");

    /// <summary>A quiet Malazan order (one worker at 3,600 s) and a wave threshold any test army reaches (2 half-pop, no cost).</summary>
    private static readonly Lazy<GameData> s_data = new(() => AiScenes.DataWith(dir =>
        dir.EditJson("factions/malazan/ai.json", root =>
        {
            foreach (JsonNode? o in root["openings"]!.AsArray())
            {
                o!["buildOrder"] = AiScenes.BuildOrder(("time", 3600, "train", "malazan_laborer", 1));
                o["attack"] = new JsonObject { ["minArmyPop"] = 2, ["minArmyCost"] = 0 };
            }
        })));

    private static void NextThink(Simulation sim)
    {
        int think = sim.AiOf(0)!.ThinkTicks;
        do sim.Tick(); while ((sim.TickNumber - 1) % think != 0);
    }

    private static List<EntityHandle> Army(Simulation sim, int n, int x, int y)
    {
        var list = new List<EntityHandle>();
        for (int k = 0; k < n; k++) list.Add(CombatScenes.Place(sim, 0, Heavy, sim.World.NavGrid.CellCenter(x + k % 6, y + k / 6)));
        return list;
    }

    /// <summary>
    /// Three seats; player 0's army sees player 2's base (its hall becomes known), then is replaced by one far away, so the
    /// hall is a ghost in player 0's memory (the only enemy building it knows). Returns the scene and the far army.
    /// </summary>
    private static (Simulation Sim, List<EntityHandle> Army) SceneWithAGhostOfPlayer2()
    {
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human, SeatController.Human), s_data.Value);
        AiPlayer ai = sim.AiOf(0)!;
        ai.Board.WaveCooldownUntil = int.MaxValue; // no wave while the memory is laid
        List<EntityHandle> eyes = Army(sim, 4, 31, 17);
        NextThink(sim);
        NextThink(sim);
        Assert.Equal(1, ai.Board.KnownEnemyBuildings); // player 2's hall, seen
        foreach (EntityHandle h in eyes) sim.World.Units.Free(h);
        List<EntityHandle> army = Army(sim, 4, 12, 50);
        NextThink(sim);
        NextThink(sim);
        int s = -1, ghosts = 0;
        while (ai.View.NextGhost(ref s, out Vision.BuildingGhost g)) if (g.Owner == 2) ghosts++;
        Assert.Equal(1, ghosts);
        Assert.Equal(1, ai.Board.KnownEnemyBuildings); // the ghost
        return (sim, army);
    }

    /// <summary>Frees every unit and building of player <paramref name="p"/> and ticks once: it is defeated (out of player 0's sight, so the ghost stays).</summary>
    private static void Defeat(Simulation sim, int p)
    {
        World w = sim.World;
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == p) w.Units.Free(new EntityHandle(i, w.Units.Generation[i]));
        for (int i = 0; i < w.Buildings.Capacity; i++)
            if (w.Buildings.Alive[i] && w.Buildings.Owner[i] == p) Assert.True(w.Buildings.Free(new EntityHandle(i, w.Buildings.Generation[i])));
        sim.Tick();
        Assert.True(w.IsDefeated(p));
        Assert.False(w.MatchOver);
    }

    /// <summary>
    /// BUG-1119 (fail-first): once player 2 is defeated, its hall's ghost in player 0's memory is no known enemy building, so
    /// player 0 (a wave sent before, no other enemy building known) hunts at its next think instead of waving on the ghost
    /// (before the fix: <c>KnownEnemyBuildings</c> 1 and a wave on the ghost). The ghost itself stays in the fog's memory
    /// (the view still shows it): only the AI forgets it. <see cref="PlayerView.IsDefeated"/> answers the public result.
    /// </summary>
    [Fact]
    public void ADefeatedSeatsGhost_IsNoKnownEnemyBuilding_SoTheSurvivorHunts()
    {
        (Simulation sim, _) = SceneWithAGhostOfPlayer2();
        AiPlayer ai = sim.AiOf(0)!;
        Defeat(sim, 2);
        Assert.True(ai.View.IsDefeated(2));
        Assert.False(ai.View.IsDefeated(1));
        int s = -1;
        Assert.True(ai.View.NextGhost(ref s, out Vision.BuildingGhost ghost) && ghost.Owner == 2); // the fog still remembers it
        ai.Board.WavesStarted = 1;
        ai.Board.WaveCooldownUntil = 0;
        NextThink(sim);
        Assert.Equal(0, ai.Board.KnownEnemyBuildings);
        Assert.False(ai.NearestEnemyBuilding(Vector2.Zero, out _, out _, out _));
        Assert.True(ai.Board.WaveActive && ai.Board.Hunting, "no hunt: the defeated seat's ghost still held the survivor");
        Assert.Equal(1, ai.Board.HuntsStarted);
        Assert.Equal(1, ai.Board.WavesStarted); // no wave was started on the ghost
    }

    /// <summary>
    /// BUG-1119: a wave already on its way to a ghost of a seat that is then defeated turns into a hunt at the next think, as
    /// a razed target does (the same members, no trip home). Before the fix it walked on to the ghost.
    /// </summary>
    [Fact]
    public void AWaveOnADefeatedSeatsGhost_TurnsIntoAHunt()
    {
        (Simulation sim, List<EntityHandle> army) = SceneWithAGhostOfPlayer2();
        AiPlayer ai = sim.AiOf(0)!;
        ai.Board.WaveCooldownUntil = 0;
        NextThink(sim);
        Assert.True(ai.Board.WaveActive && !ai.Board.Hunting);
        Assert.Equal(2, sim.World.Buildings.Owner[ai.Board.WaveTarget.Index]); // the ghost of player 2's hall
        Defeat(sim, 2);
        List<Command> log = AiScenes.Log(sim, 0);
        NextThink(sim);
        Assert.True(ai.Board.WaveActive && ai.Board.Hunting, "the wave walked on to a defeated seat's ghost");
        Assert.Equal(1, ai.Board.HuntsStarted);
        Assert.All(army, h => Assert.Equal(h.Generation, ai.Board.WaveGeneration[h.Index])); // the same members
        Assert.All(log.Where(c => army.Contains(c.Unit)), c => Assert.Equal(CommandKind.AttackMove, c.Kind));
    }

    /// <summary>
    /// BUG-1120 (fail-first; the Producer's default): a hunt's sighting on an ally's building edge (an enemy unit 0.3 m from
    /// the allied hall's footprint, seen through the ally's eyes) is not the point the members are sent to: it is pushed
    /// off the footprint toward the hunters, clear of QA's 0.5 m slack by half a map cell. Before the fix every member was
    /// sent <c>AttackMove</c> to the sighting itself. A sighting in the open (the same scene, the unit 12 m off) is sent as seen.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AHuntsSighting_ByAnAllysBuilding_IsPushedOffItTowardTheHunters(bool byTheWall)
    {
        PlayerSeat[] seats =
        {
            new(0, SeatController.Ai, AiDifficulty.Normal, 1),
            new(1, SeatController.Human, AiDifficulty.Normal, 2),
            new(0, SeatController.Human, AiDifficulty.Normal, 1), // player 2: player 0's ally, its hall at (30, 8)
        };
        Simulation sim = AiScenes.Flat(seats, s_data.Value);
        World w = sim.World;
        AiPlayer ai = sim.AiOf(0)!;
        ai.Board.WaveCooldownUntil = int.MaxValue;
        (int hx, int hy) = AiScenes.HallAt(2);
        int hallType = w.Buildings.TypeId[FirstBuildingOf(w, 2)];
        Rts.Sim.Combat.CombatSystem.FootprintRect(w, hallType, hy * AiScenes.FlatSize + hx, out Vector2 min, out Vector2 max);
        // Player 2's workers stand below its hall: the enemy raider by the hall's east wall, the army further east.
        Vector2 seenAt = byTheWall ? new Vector2(max.X + 0.3f, (min.Y + max.Y) * 0.5f) : new Vector2(max.X + 12f, (min.Y + max.Y) * 0.5f);
        EntityHandle raider = CombatScenes.Place(sim, 1, TestSim.Data.FindUnit("whirlwind_raider"), seenAt);
        List<EntityHandle> army = Army(sim, 4, 44, 9);
        NextThink(sim);
        Assert.Equal(seenAt, ai.Board.EnemyUnitPoint);
        Assert.Equal(seenAt, w.Units.Position[raider.Index]);
        ai.Board.WavesStarted = 1;
        ai.Board.WaveCooldownUntil = 0;
        List<Command> log = AiScenes.Log(sim, 0);
        NextThink(sim);
        Assert.True(ai.Board.WaveActive && ai.Board.Hunting && ai.Board.HuntSweep == -1);
        Vector2 sent = ai.Board.WaveTargetPoint;
        List<Command> orders = log.Where(c => army.Contains(c.Unit)).ToList();
        Assert.Equal(army.Count, orders.Count);
        Assert.All(orders, c => Assert.Equal(sent, c.Position));
        float clear = Vector2.Distance(sent, Vector2.Clamp(sent, min, max));
        if (!byTheWall)
        {
            Assert.Equal(seenAt, sent);
            return;
        }
        Assert.True(clear >= MilitaryManager.AllySlack + MapConstants.CellSize * 0.5f - 1e-3f, $"the point {sent} is {clear:F2} m from the ally's hall (seen at {seenAt})");
        Assert.True(sent.X > seenAt.X, "pushed toward the hunters (east)");
        Assert.True(Vector2.Distance(sent, seenAt) < 2f, "pushed off, not away");
        // The next think: the same sighting, not re-sent (the pushed point is the target).
        int before = log.Count;
        NextThink(sim);
        Assert.Equal(sent, ai.Board.WaveTargetPoint);
        Assert.DoesNotContain(log.Skip(before), c => c.Kind == CommandKind.AttackMove && c.Position != sent);
    }

    private static int FirstBuildingOf(World w, int p)
    {
        for (int i = 0; i < w.Buildings.Capacity; i++)
            if (w.Buildings.Alive[i] && w.Buildings.Owner[i] == p) return i;
        return -1;
    }

    /// <summary>
    /// The lone-idle-worker hunt gap (under BUG-1116), kept as a known case: on a 512 m flat map (Pale Hills' size, the
    /// shipped 10 x 10 sweep grid), player 1 has nothing left but one worker standing idle 75 m from player 0's army, 8 m from
    /// an interior sweep point; a border-ring point is nearer the army than that point. With no sighting and a wave sent
    /// before, player 0 hunts by the sweep from its army. Nearest-first reaches the ring point within a few points and walks
    /// the whole perimeter leg first (2 km), seeing the worker 14,937 ticks after the hunt began (Pale Hills seed 5's camp
    /// follower under the house rule: its point unvisited for 30,000 ticks). M5-H5 measured two fixes that make this row
    /// green (the points within two grid cells of the sweep's origin first; the leg waiting for them) and both sank QA's
    /// rows (BUG-1116's file); the row waits for the next sweep change. M5-H6 measured a third (the leg deferred only while an
    /// interior point is nearer the origin than the reached ring point) on the match rows: it adds nothing once the hunt's
    /// free starts and the house rule are in, so it was not shipped (docs/03 "Implementation (M5-H6)").
    /// </summary>
    [Fact(Skip = "BUG-1116 (open, S3): the sweep walks the perimeter leg before the ground round where it began; both measured fixes sank QA's hide / leg rows")]
    public void ALoneIdleWorker_NearWhereTheHuntBegins_IsSeenBeforeThePerimeterLeg()
    {
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human), s_data.Value,
            map: LocalMovementTests.Flat(256));
        World w = sim.World;
        AiPlayer ai = sim.AiOf(0)!;
        ai.Board.WaveCooldownUntil = int.MaxValue;
        sim.Tick();
        for (int i = 0; i < w.Buildings.Capacity; i++)
            if (w.Buildings.Alive[i] && w.Buildings.Owner[i] == 1) Assert.True(w.Buildings.Free(new EntityHandle(i, w.Buildings.Generation[i])));
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == 1) w.Units.Free(new EntityHandle(i, w.Units.Generation[i]));
        int laborer = TestSim.Data.FindUnit("whirlwind_camp_follower");
        var hide = new Vector2(110f, 100f);
        EntityHandle worker = CombatScenes.Place(sim, 1, laborer, hide);
        Army(sim, 4, 18, 28); // round (40, 60) m, by the west edge
        int cols = ScoutManager.GridLines(256, ai.Rules.ScoutGrid);
        float nearestRing = float.MaxValue, workerPoint = float.MaxValue;
        Vector2 army = new(40f, 60f);
        for (int k = 0; k < MilitaryManager.HuntPoints(cols, cols); k++)
        {
            Vector2 p = MilitaryManager.HuntPoint(ai, cols, cols, k);
            if (k >= MilitaryManager.RingStart(cols, cols)) nearestRing = Math.Min(nearestRing, Vector2.Distance(army, p));
            if (Vector2.Distance(p, hide) < 10f) workerPoint = Math.Min(workerPoint, Vector2.Distance(army, p));
        }
        Assert.True(nearestRing < workerPoint, $"the scene: a ring point ({nearestRing:F0} m) nearer the army than the worker's point ({workerPoint:F0} m)");
        NextThink(sim);
        ai.Board.WavesStarted = 1;
        ai.Board.WaveCooldownUntil = 0;
        int start = sim.TickNumber, seenAt = -1;
        while (sim.TickNumber < start + 20_000 && seenAt < 0)
        {
            sim.Tick();
            if (w.Fog.CanSeeUnit(0, worker.Index)) seenAt = sim.TickNumber;
        }
        Assert.True(ai.Board.HuntsStarted >= 1);
        Assert.True(seenAt >= 0 && seenAt - start <= 6_000, $"the lone worker was seen {(seenAt < 0 ? "never" : $"{seenAt - start} ticks")} after the hunt began (ring leg {ai.Board.HuntRing}, visited {ai.Board.HuntVisited.Count(v => v)})");
    }

    /// <summary>
    /// M5-H6 (BUG-1157; failed first: the sweep went nearest-first from the army, walked the perimeter leg and saw the worker
    /// only after it): on a 512 m flat map with four start locations, player 1 has nothing left but one worker standing at a
    /// free start (2) far across the map, as a beaten side's far depot workers stand. Player 0 has hunted once before, so
    /// this hunt sweeps the free starts first, the one nearest its army (3) then the next (2), each through the sweep point
    /// at its centre, and sees the worker within 6,000 ticks of the hunt's start.
    /// </summary>
    [Fact]
    public void AHunt_SweepsTheFreeStartsFirst_AndFindsARemnantAtTheFarOne()
    {
        StartLocation[] starts = { new(7 * 256 + 136, 0), new(240 * 256 + 128, 0), new(200 * 256 + 200, 0), new(200 * 256 + 40, 0) };
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human), s_data.Value,
            map: LocalMovementTests.Flat(256).WithStartLocations(starts));
        World w = sim.World;
        AiPlayer ai = sim.AiOf(0)!;
        ai.Board.WaveCooldownUntil = int.MaxValue;
        sim.Tick();
        for (int i = 0; i < w.Buildings.Capacity; i++)
            if (w.Buildings.Alive[i] && w.Buildings.Owner[i] == 1) Assert.True(w.Buildings.Free(new EntityHandle(i, w.Buildings.Generation[i])));
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == 1) w.Units.Free(new EntityHandle(i, w.Units.Generation[i]));
        Vector2 far = ai.CellCentre(starts[2].Cell), near = ai.CellCentre(starts[3].Cell);
        EntityHandle worker = CombatScenes.Place(sim, 1, TestSim.Data.FindUnit("whirlwind_camp_follower"), far);
        Army(sim, 4, 18, 28); // round (40, 60) m: start 3 is the nearer free start
        NextThink(sim);
        ai.Board.WavesStarted = 1;
        ai.Board.HuntsStarted = 1; // a hunt before: this is the match's second
        ai.Board.WaveCooldownUntil = 0;
        int start = sim.TickNumber, seenAt = -1, cols = ScoutManager.GridLines(256, ai.Rules.ScoutGrid);
        near = MilitaryManager.HuntPoint(ai, cols, cols, MilitaryManager.StartProxy(ai, cols, cols, near));
        far = MilitaryManager.HuntPoint(ai, cols, cols, MilitaryManager.StartProxy(ai, cols, cols, far));
        var targets = new List<Vector2>();
        while (sim.TickNumber < start + 20_000 && seenAt < 0)
        {
            sim.Tick();
            if (ai.Board.Hunting && (targets.Count == 0 || targets[^1] != ai.Board.WaveTargetPoint)) targets.Add(ai.Board.WaveTargetPoint);
            if (w.Fog.CanSeeUnit(0, worker.Index)) seenAt = sim.TickNumber;
        }
        _out!.WriteLine($"seen {(seenAt < 0 ? "never" : $"{seenAt - start} ticks")} after the hunt began; targets {string.Join(" ", targets.Take(4))}");
        Assert.True(ai.Board.HuntsStarted >= 1);
        Assert.True(targets.Count >= 2, "the hunt never left its first target");
        Assert.Equal(near, targets[0]);
        Assert.Equal(far, targets[1]);
        Assert.True(seenAt >= 0 && seenAt - start <= 6_000, $"the worker at the far free start was seen {(seenAt < 0 ? "never" : $"{seenAt - start} ticks")} after the hunt began");
    }

    /// <summary>
    /// M5-H6: the starts the hunt sweeps first (<see cref="MilitaryManager.NextHuntStart"/>) are the free ones and a defeated
    /// player's, never the seat's own nor a live player's; each stands for the interior sweep point nearest its centre, and
    /// one visited is not picked again.
    /// </summary>
    [Fact]
    public void TheHuntsStarts_AreTheFreeAndDefeatedOnes_NearestFirst()
    {
        StartLocation[] starts = { new(7 * 256 + 136, 0), new(240 * 256 + 128, 0), new(200 * 256 + 200, 0), new(200 * 256 + 40, 0) };
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human), s_data.Value,
            map: LocalMovementTests.Flat(256).WithStartLocations(starts));
        AiPlayer ai = sim.AiOf(0)!;
        sim.Tick();
        int cols = ScoutManager.GridLines(256, ai.Rules.ScoutGrid);
        int Point(int k) => MilitaryManager.StartProxy(ai, cols, cols, ai.CellCentre(starts[k].Cell));
        Vector2 from = ai.CellCentre(starts[0].Cell);
        Assert.Equal(Point(2), MilitaryManager.NextHuntStart(ai, from, -1)); // start 2 nearer start 0 than start 3 is
        Assert.True(Point(2) < MilitaryManager.RingStart(cols, cols));
        ai.Board.HuntVisited[Point(2)] = true;
        Assert.Equal(Point(3), MilitaryManager.NextHuntStart(ai, from, -1));
        ai.Board.HuntVisited[Point(3)] = true;
        Assert.Equal(-1, MilitaryManager.NextHuntStart(ai, from, -1)); // 0 its own, 1 a live player's
    }

    private const string FourSeatChild = "RTS_M5H5_FOUR_SEAT_CHILD", FourSeatLine = "M5H5-FOUR-SEAT ";
    private const string RarakuChild = "RTS_M5H5_RARAKU_CHILD", RarakuLine = "M5H5-RARAKU ";

    /// <summary>
    /// Criterion 5 (M5-H5): Pale Hills, four Normal AI seats free-for-all, seed 2 (the BUG-1119 match), to 100,000 ticks, past
    /// the first defeat (so a survivor's census forgets a defeated seat's ghosts in the run): this process and a fresh child
    /// <c>dotnet test</c> of this row fold the same per-tick hashes. In this process only: a save taken at the first think a
    /// seat hunts after a defeat (else the first hunt) loads and follows the run hash for hash for 1,000 ticks.
    /// </summary>
    [Fact]
    [Trait("Category", "Stress")]
    public void PaleHillsSeed2_FourSeats_PastADefeat_TwoProcesses_AndASaveMidHunt()
    {
        var seats = new PlayerSeat[4];
        for (int p = 0; p < 4; p++) seats[p] = new PlayerSeat(p % 2, SeatController.Ai, AiDifficulty.Normal);
        Simulation sim = Stress.HuntStressTests.Match("pale_hills", seats, 2, units: 1024);
        World w = sim.World;
        bool child = Environment.GetEnvironmentVariable(FourSeatChild) == "1";
        var h = new Determinism.StateHasher();
        Simulation? load = null;
        int savedAt = -1, followed = 0, firstDefeat = -1;
        while (sim.TickNumber < 100_000)
        {
            if (!child && savedAt < 0)
            {
                bool hunting = false, defeated = false;
                for (int p = 0; p < 4; p++)
                {
                    hunting |= sim.AiOf(p)!.Board.Hunting;
                    defeated |= w.IsDefeated(p);
                }
                if (hunting && defeated)
                {
                    byte[] bytes = SaveTestRun.Save(sim);
                    load = SaveTestRun.Load(TestSim.Data, bytes);
                    Assert.Equal(bytes, SaveTestRun.Save(load));
                    savedAt = sim.TickNumber;
                }
            }
            sim.Tick();
            h.Add(sim.StateHash());
            if (firstDefeat < 0) for (int p = 0; p < 4; p++) if (w.IsDefeated(p)) firstDefeat = sim.TickNumber;
            if (load != null && followed < 1000)
            {
                load.Tick();
                followed++;
                Assert.True(load.StateHash() == sim.StateHash(), $"the save from tick {savedAt} diverged at tick {sim.TickNumber}");
            }
        }
        string mine = $"{h.Value:X16} {sim.StateHash():X16} {firstDefeat}";
        if (child)
        {
            _out!.WriteLine(FourSeatLine + mine);
            return;
        }
        _out!.WriteLine($"this process: {mine}; saved mid-hunt at {savedAt}, followed {followed} ticks");
        Assert.True(firstDefeat > 0, "no seat was defeated by 100,000");
        Assert.True(savedAt > 0 && followed == 1000, "no hunt after a defeat to save in");
        Assert.Equal(mine, MapFileTests.RunChild(typeof(EndGameHardeningTests), nameof(PaleHillsSeed2_FourSeats_PastADefeat_TwoProcesses_AndASaveMidHunt), FourSeatChild, FourSeatLine, seconds: 1200));
    }

    /// <summary>Criterion 5 (M5-H5): Raraku, Normal v Normal, seed 1, to its end: the same per-tick hashes, end tick and hunts in this process and a child.</summary>
    [Fact]
    [Trait("Category", "Stress")]
    public void Raraku_Seed1_ToTheEnd_TwoProcesses_SamePerTickHashes()
    {
        Simulation sim = MapFileTests.RarakuMatch(1, false, out _);
        var h = new Determinism.StateHasher();
        while (sim.TickNumber < 72_000 && !sim.MatchOver)
        {
            sim.Tick();
            h.Add(sim.StateHash());
        }
        string mine = $"{h.Value:X16} {sim.StateHash():X16} {sim.TickNumber} {sim.AiOf(0)!.Board.HuntsStarted + sim.AiOf(1)!.Board.HuntsStarted}";
        if (Environment.GetEnvironmentVariable(RarakuChild) == "1")
        {
            _out!.WriteLine(RarakuLine + mine);
            return;
        }
        _out!.WriteLine($"this process: {mine}");
        Assert.True(sim.MatchOver, "no result by 72,000 ticks");
        Assert.Equal(mine, MapFileTests.RunChild(typeof(EndGameHardeningTests), nameof(Raraku_Seed1_ToTheEnd_TwoProcesses_SamePerTickHashes), RarakuChild, RarakuLine, seconds: 600));
    }
}
