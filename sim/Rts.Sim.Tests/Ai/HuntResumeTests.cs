using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M6-S14 item A (BUG-1661, docs/03 "Implementation (M6-S14)"): a hunt that turned into a wave on a building it found, and
/// whose wave saw that building go, is followed by a hunt that keeps the last hunt's marks and sweeps nearest-first from
/// where the building stood; the free starts come first only for a hunt with no such find within <c>ai.huntMemory</c>.
/// The scene is <see cref="EndGameHardeningTests"/>' free-start map: a 512 m flat map with four start locations (two of
/// them free), player 0 a Normal Malazan AI with a placed army, player 1 a human seat reduced to what the test places.
/// Combat is off: the test razes the find itself (the wave's part is to see it go).
/// </summary>
public class HuntResumeTests
{
    private readonly ITestOutputHelper _out;

    public HuntResumeTests(ITestOutputHelper output) => _out = output;

    private static int Heavy => TestSim.Data.FindUnit("malazan_heavy_infantry");

    /// <summary>A quiet Malazan order (one worker at 3,600 s) and a wave threshold any test army reaches.</summary>
    private static readonly Lazy<GameData> s_data = new(() => AiScenes.DataWith(dir =>
        dir.EditJson("factions/malazan/ai.json", root =>
        {
            foreach (JsonNode? o in root["openings"]!.AsArray())
            {
                AiScenes.SetBuildOrder(o, AiScenes.BuildOrder(("time", 3600, "train", "malazan_laborer", 1)));
                o["attack"] = new JsonObject { ["minArmyPop"] = 2, ["minArmyCost"] = 0 };
            }
        })));

    private static readonly StartLocation[] s_starts =
        { new(7 * 256 + 136, 0), new(240 * 256 + 128, 0), new(200 * 256 + 200, 0), new(200 * 256 + 40, 0) };

    private static void NextThink(Simulation sim)
    {
        int think = sim.AiOf(0)!.ThinkTicks;
        do sim.Tick(); while ((sim.TickNumber - 1) % think != 0);
    }

    /// <summary>What the scene leaves standing: the sim, the find's footprint centre, the second building's handle.</summary>
    private sealed record Staged(Simulation Sim, Vector2 Find, EntityHandle Second, int Cols);

    /// <summary>
    /// The scene up to the raze: player 1 keeps only a Town Hall far from the army (out of its sight); player 0 has hunted
    /// once before, so its hunt here is the match's second (the free starts first on the base); once that hunt has marked a
    /// sweep point a tent of player 1 is put in the army's sight and another 50 m beyond it out of sight, the hunt turns into
    /// a wave on the first, and the test razes it.
    /// </summary>
    private static Staged ToTheRaze()
    {
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human), s_data.Value,
            map: LocalMovementTests.Flat(256).WithStartLocations(s_starts));
        World w = sim.World;
        AiPlayer ai = sim.AiOf(0)!;
        ai.Board.WaveCooldownUntil = int.MaxValue;
        sim.Tick();
        for (int i = 0; i < w.Buildings.Capacity; i++)
            if (w.Buildings.Alive[i] && w.Buildings.Owner[i] == 1) Assert.True(w.Buildings.Free(new EntityHandle(i, w.Buildings.Generation[i])));
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == 1) w.Units.Free(new EntityHandle(i, w.Units.Generation[i]));
        int tent = s_data.Value.FindBuilding("whirlwind_tent");
        // The second building: player 1's Town Hall (a unit-producing building, so the seat stands) at (90, 120) cells, far
        // from the army and every start.
        int hall = s_data.Value.FindBuilding(AiScenes.BuildingKey("whirlwind", BuildingSlot.TownHall));
        Assert.True(w.Buildings.Spawn(1, hall, 120 * 256 + 90, out _));
        for (int k = 0; k < 4; k++) CombatScenes.Place(sim, 0, Heavy, w.NavGrid.CellCenter(70 + k % 2, 100 + k / 2));
        NextThink(sim);
        ai.Board.WavesStarted = 1;
        ai.Board.HuntsStarted = 1; // a hunt before: the hunts here are the match's second and third
        ai.Board.WaveCooldownUntil = 0;
        NextThink(sim);
        Assert.True(ai.Board.Hunting, $"no hunt began: wave {ai.Board.WaveActive} known {ai.Board.KnownEnemyBuildings} army {ai.ArmyCount} defending {ai.Board.Defending} over {ai.View.MatchOver} cooldown {ai.Board.WaveCooldownUntil} now {sim.TickNumber}");
        Assert.Equal(2, ai.Board.HuntsStarted);
        // The hunt walks until its sweep has marked a point, so there are marks to keep.
        while (Array.IndexOf(ai.Board.HuntVisited, true) < 0 && sim.TickNumber < 8_000) NextThink(sim);
        Assert.True(ai.Board.Hunting && Array.IndexOf(ai.Board.HuntVisited, true) >= 0, "the sweep marked nothing");
        // The find: a tent 6 cells from the army's centroid, in its sight; the second building, a tent 25 cells (50 m) the
        // other way along x, out of sight.
        Vector2 sum = Vector2.Zero;
        for (int k = 0; k < ai.ArmyCount; k++) sum += w.Units.Position[ai.ArmySlots[k]];
        int cx = (int)(sum.X / ai.ArmyCount / MapConstants.CellSize), cy = (int)(sum.Y / ai.ArmyCount / MapConstants.CellSize);
        int side = cx < 128 ? 1 : -1, findCell = cy * 256 + cx + 6 * side, nearCell = cy * 256 + cx + 6 * side + 25 * side;
        Assert.True(w.Buildings.Spawn(1, tent, findCell, out EntityHandle find));
        Assert.True(w.Buildings.Spawn(1, tent, nearCell, out EntityHandle second));
        for (int k = 0; k < 4 && ai.Board.Hunting; k++) NextThink(sim);
        Assert.False(ai.Board.Hunting, "the hunt never turned into a wave on the tent it found");
        Assert.True(ai.Board.WaveActive);
        Assert.Equal(find, ai.Board.WaveTarget);
        Assert.True(ai.Board.WaveFromHunt);
        Assert.Equal(1, ai.Board.KnownEnemyBuildings); // the second tent is not seen
        Vector2 at = ai.BuildingCentre(tent, findCell);
        Assert.True(w.Buildings.Free(find)); // razed, in the army's sight
        int cols = ScoutManager.GridLines(256, ai.Rules.ScoutGrid);
        return new Staged(sim, at, second, cols);
    }

    /// <summary>
    /// The unvisited sweep point nearest <paramref name="from"/>, written out again: the sweep's tie (within a map cell, to
    /// the point nearer the origin) reduces to the least distance when the origin is <paramref name="from"/> itself, as it is
    /// for a resumed hunt; the lower index on an exact tie.
    /// </summary>
    private static int NearestUnvisited(AiPlayer ai, int cols, Vector2 from)
    {
        int best = -1;
        float least = float.MaxValue;
        for (int k = 0; k < ai.Board.HuntVisited.Length; k++)
        {
            if (ai.Board.HuntVisited[k]) continue;
            float d = Vector2.Distance(from, MilitaryManager.HuntPoint(ai, cols, cols, k));
            if (d >= least) continue;
            least = d;
            best = k;
        }
        return best;
    }

    /// <summary>
    /// Criterion 1 (failed on the base: the third hunt's first sweep target was the proxy of the free start nearest the
    /// find, start 3, 135 m off): a hunt's wave razes the building it found, a second enemy building stands within 60 m of
    /// it, and the next hunt keeps the last hunt's marks and goes first to the unvisited sweep point nearest the razed
    /// building, not to a free start.
    /// </summary>
    [Fact]
    public void AHuntsRazedFind_TheNextHuntSweepsFromWhereItStood_NotTheFreeStarts()
    {
        Staged s = ToTheRaze();
        Simulation sim = s.Sim;
        AiPlayer ai = sim.AiOf(0)!;
        Vector2 second = ai.BuildingCentre(sim.World.Buildings.TypeId[s.Second.Index], sim.World.Buildings.Cell[s.Second.Index]);
        Assert.InRange(Vector2.Distance(second, s.Find), 1f, 60f);
        bool[] marks = (bool[])ai.Board.HuntVisited.Clone();
        Assert.Contains(true, marks); // the second hunt visited points before it found the tent
        int freeStart = MilitaryManager.NextHuntStart(ai, s.Find, -1);
        Assert.True(freeStart >= 0, "the scene: a free start's proxy is unvisited, so the old rule would have walked there first");
        int want = NearestUnvisited(ai, s.Cols, s.Find);
        Assert.NotEqual(freeStart, want);

        NextThink(sim);
        Assert.True(ai.Board.Hunting, "the wave did not turn into a hunt when its target went");
        Assert.Equal(3, ai.Board.HuntsStarted);
        Assert.True(MilitaryManager.Resumes(ai));
        Assert.Equal(s.Find, ai.Board.HuntFindPoint);
        _out.WriteLine($"find {s.Find}, second building {second} ({Vector2.Distance(second, s.Find):F0} m); first target {ai.Board.HuntSweep} "
            + $"{ai.Board.WaveTargetPoint} (want {want}; the free start's proxy {freeStart} {MilitaryManager.HuntPoint(ai, s.Cols, s.Cols, freeStart)})");
        Assert.True(ai.Board.HuntSweep == want, $"the first sweep target is point {ai.Board.HuntSweep}, want {want} (the free start's proxy is {freeStart})");
        for (int k = 0; k < marks.Length; k++)
            Assert.True(!marks[k] || ai.Board.HuntVisited[k], $"point {k}: the last hunt's mark was cleared");
        Assert.Equal(s.Find, ai.Board.HuntOrigin);
    }

    /// <summary>
    /// Criterion 1's other half (green on the base and after): a hunt that begins more than <c>ai.huntMemory</c> after the
    /// last find went sweeps as before, from the match's second hunt on the free start nearest the army first, its marks
    /// cleared; and the old find is forgotten.
    /// </summary>
    [Fact]
    public void AHuntLongAfterTheFind_GoesToTheFreeStartsFirst()
    {
        Staged s = ToTheRaze();
        Simulation sim = s.Sim;
        AiPlayer ai = sim.AiOf(0)!;
        NextThink(sim);
        Assert.True(ai.Board.Hunting);
        MilitaryManager.End(ai);
        ai.Board.WaveCooldownUntil = sim.TickNumber + ai.Rules.HuntMemoryTicks + 2 * ai.ThinkTicks;
        while (sim.TickNumber < ai.Board.WaveCooldownUntil) NextThink(sim);
        while (!ai.Board.Hunting && sim.TickNumber < ai.Board.WaveCooldownUntil + 10 * ai.ThinkTicks) NextThink(sim);
        Assert.True(ai.Board.Hunting, "no hunt after the cooldown");
        Assert.Equal(4, ai.Board.HuntsStarted);
        Assert.False(MilitaryManager.Resumes(ai));
        Assert.Equal(-1, ai.Board.HuntFindTick);
        Vector2 army = ai.Board.HuntOrigin;
        int cols = s.Cols;
        int Proxy(int k) => MilitaryManager.StartProxy(ai, cols, cols, ai.CellCentre(s_starts[k].Cell));
        int nearer = Vector2.DistanceSquared(army, ai.CellCentre(s_starts[2].Cell)) < Vector2.DistanceSquared(army, ai.CellCentre(s_starts[3].Cell)) ? 2 : 3;
        Assert.Equal(Proxy(nearer), ai.Board.HuntSweep);
    }

    /// <summary>
    /// Criterion 8 (the rule's twins): the scene played twice to the raze and 2,000 ticks on gives the same state hash every
    /// tick; a save taken between the raze and the next think (the find still the target, <c>WaveFromHunt</c> set) and one
    /// taken right after the resumed hunt began each load equal field for field, save again to the same bytes, and follow the
    /// run hash for hash for 1,000 ticks.
    /// </summary>
    [Fact]
    public void TheResume_Twins_AndSavesAroundTheRaze_FollowTheRun()
    {
        Staged a = ToTheRaze(), b = ToTheRaze();
        Assert.Equal(a.Sim.StateHash(), b.Sim.StateHash());
        byte[] beforeThink = SaveTestRun.Save(a.Sim);
        Simulation loadBefore = SaveTestRun.Load(s_data.Value, beforeThink);
        Assert.True(loadBefore.AiOf(0)!.Board.WaveFromHunt);
        Assert.Equal(beforeThink, SaveTestRun.Save(loadBefore));
        SaveTestRun.AssertSameState(a.Sim, loadBefore, "between the raze and the think");
        NextThink(a.Sim);
        NextThink(b.Sim);
        NextThink(loadBefore);
        Assert.True(a.Sim.AiOf(0)!.Board.Hunting && MilitaryManager.Resumes(a.Sim.AiOf(0)!));
        byte[] resumed = SaveTestRun.Save(a.Sim);
        Simulation loadResumed = SaveTestRun.Load(s_data.Value, resumed);
        Assert.Equal(resumed, SaveTestRun.Save(loadResumed));
        SaveTestRun.AssertSameState(a.Sim, loadResumed, "the resumed hunt's first think");
        for (int t = 0; t < 2000; t++)
        {
            a.Sim.Tick();
            b.Sim.Tick();
            Assert.True(a.Sim.StateHash() == b.Sim.StateHash(), $"twins diverged at tick {a.Sim.TickNumber}");
            if (t >= 1000) continue;
            loadBefore.Tick();
            loadResumed.Tick();
            Assert.True(a.Sim.StateHash() == loadBefore.StateHash(), $"the save before the think diverged at tick {a.Sim.TickNumber}");
            Assert.True(a.Sim.StateHash() == loadResumed.StateHash(), $"the save of the resumed hunt diverged at tick {a.Sim.TickNumber}");
        }
        SaveTestRun.AssertSameState(a.Sim, b.Sim, "twins at the end");
    }
}
