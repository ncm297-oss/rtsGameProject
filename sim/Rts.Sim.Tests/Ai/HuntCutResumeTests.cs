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
/// M6-S15 D criterion 7 (BUG-1855; docs/01 2026-11-27-1857 (5), docs/03 "Implementation (M6-S15)" D): a hunt cut short by a
/// defense is resumed when the next hunt begins within <c>ai.huntMemory</c> of the defense's end: its visited marks and its
/// pass are kept and its first target is the unvisited sweep point nearest the army, not the leg it was on, nor a free start.
/// The scene is <see cref="HuntResumeTests"/>' free-start map (512 m, four starts, two free): player 0 a Normal Malazan AI
/// with four Heavy Infantry hunting (its match's second hunt), player 1 a human seat with only a Town Hall far off; the raid is
/// twelve of player 1's Raiders put beside player 0's Keep (combat off), which the defense answers by recalling the hunt.
/// </summary>
public class HuntCutResumeTests
{
    private readonly ITestOutputHelper _out;

    public HuntCutResumeTests(ITestOutputHelper output) => _out = output;

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

    /// <summary>What the cut leaves: the sim, the marks and pass at the cut, and the leg's target then.</summary>
    internal sealed record Cut(Simulation Sim, bool[] Marks, int Pass, int Leg, List<EntityHandle> Raiders);

    /// <summary>The scene to the think the defense recalled the hunt (the raiders still standing by the Keep).</summary>
    internal static Cut ToTheCut()
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
        int hall = s_data.Value.FindBuilding(AiScenes.BuildingKey("whirlwind", BuildingSlot.TownHall));
        Assert.True(w.Buildings.Spawn(1, hall, 120 * 256 + 90, out _));
        int heavy = s_data.Value.FindUnit("malazan_heavy_infantry");
        for (int k = 0; k < 4; k++) CombatScenes.Place(sim, 0, heavy, w.NavGrid.CellCenter(70 + k % 2, 100 + k / 2));
        NextThink(sim);
        ai.Board.WavesStarted = 1;
        ai.Board.HuntsStarted = 1; // a hunt before: this one is the match's second (the free starts first on the base)
        ai.Board.WaveCooldownUntil = 0;
        NextThink(sim);
        Assert.True(ai.Board.Hunting, "no hunt began");
        // Walk until the sweep has marked points and is on a leg toward an unvisited point.
        while ((Array.IndexOf(ai.Board.HuntVisited, true) < 0 || ai.Board.HuntSweep < 0) && sim.TickNumber < 8_000) NextThink(sim);
        for (int k = 0; k < 10; k++) NextThink(sim);
        Assert.True(ai.Board.Hunting && ai.Board.HuntSweep >= 0, "the hunt is on no leg");
        int leg = ai.Board.HuntSweep;
        // The raid: twelve Raiders beside the Keep, inside its sight.
        int hx = (int)(ai.HallPoint.X / MapConstants.CellSize), hy = (int)(ai.HallPoint.Y / MapConstants.CellSize);
        int raider = s_data.Value.FindUnit("whirlwind_raider");
        var raiders = new List<EntityHandle>();
        for (int k = 0; k < 12; k++) raiders.Add(CombatScenes.Place(sim, 1, raider, w.NavGrid.CellCenter(hx - 3 + k % 6, hy + 4 + k / 6)));
        bool[] marks = (bool[])ai.Board.HuntVisited.Clone();
        int pass = ai.Board.HuntPass;
        for (int k = 0; k < 3 && ai.Board.Hunting; k++)
        {
            marks = (bool[])ai.Board.HuntVisited.Clone();
            pass = ai.Board.HuntPass;
            leg = ai.Board.HuntSweep;
            NextThink(sim);
        }
        Assert.False(ai.Board.Hunting, $"the defense did not cut the hunt: intruders {ai.IntruderCount} worth {ai.IntruderWorth}, defending {ai.Board.Defending} all {ai.Board.DefendAll}, wave {ai.Board.WaveActive}, army {ai.ArmyCount}, recalls {ai.Board.WaveRecalls}, hall {ai.HasHall} {ai.HallPoint}, raider at {w.Units.Position[raiders[0].Index]} alive {w.Units.IsAlive(raiders[0])}");
        Assert.True(ai.Board.Defending);
        Assert.True(MilitaryManager.WasCut(ai.Board), "the cut was not remembered");
        return new Cut(sim, marks, pass, leg, raiders);
    }

    /// <summary>The raiders go; thinks until the next hunt begins (fails past <paramref name="limit"/> ticks).</summary>
    private static void ToTheNextHunt(Cut c, int limit = 12_000, bool forgetSighting = true)
    {
        Simulation sim = c.Sim;
        AiPlayer ai = sim.AiOf(0)!;
        foreach (EntityHandle r in c.Raiders) sim.World.Units.Free(r);
        // The raiders were the last enemy seen; a fresh sighting comes first (the rule), so the sweep rows forget it.
        if (forgetSighting) ai.Board.EnemyUnitTick = -1;
        int until = sim.TickNumber + limit;
        while (!ai.Board.Hunting && sim.TickNumber < until) NextThink(sim);
        Assert.True(ai.Board.Hunting, "no hunt after the defense");
    }

    /// <summary>The unvisited sweep point nearest <paramref name="from"/> (the resumed sweep's origin is the army itself, so its tie rule reduces to the least distance).</summary>
    private static int NearestUnvisited(AiPlayer ai, Vector2 from)
    {
        int cols = ScoutManager.GridLines(ai.View.MapWidth, ai.Rules.ScoutGrid), best = -1;
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
    /// Red on the base d26338d3 (the marks cleared, the pass 0, the first target the free start's proxy): the hunt begun again
    /// within <c>ai.huntMemory</c> of the defense's end keeps its marks and pass and goes to the unvisited point nearest the army.
    /// </summary>
    [Fact]
    public void AHuntCutByADefense_BegunAgainWithinTheMemory_KeepsItsMarks_AndSweepsFromTheArmy()
    {
        Cut c = ToTheCut();
        Simulation sim = c.Sim;
        AiPlayer ai = sim.AiOf(0)!;
        ToTheNextHunt(c);
        int defenseEnd = ai.Board.DefendTick + ai.Rules.DefendForTicks;
        Assert.True(sim.TickNumber - 1 - defenseEnd <= ai.Rules.HuntMemoryTicks, "the scene: the hunt began within the memory");
        Vector2 army = ai.Board.HuntOrigin;
        int want = NearestUnvisited(ai, army);
        _out.WriteLine($"cut leg {c.Leg} at pass {c.Pass} with {c.Marks.Count(m => m)} marks; resumed at {sim.TickNumber - 1} (defense end {defenseEnd}): "
            + $"target {ai.Board.HuntSweep}, want {want}, pass {ai.Board.HuntPass}, marks {ai.Board.HuntVisited.Count(m => m)}; army {army}; free start's proxy {MilitaryManager.NextHuntStart(ai, army, -1)}");
        for (int k = 0; k < c.Marks.Length; k++)
            Assert.True(!c.Marks[k] || ai.Board.HuntVisited[k], $"point {k}: the cut hunt's mark was cleared");
        Assert.Equal(c.Pass, ai.Board.HuntPass);
        Assert.True(MilitaryManager.Resumes(ai));
        Assert.Equal(want, ai.Board.HuntSweep);
        Assert.NotEqual(c.Leg, ai.Board.HuntSweep);
    }

    /// <summary>A fresh sighting (the raiders, last seen within <c>ai.huntMemory</c>) still comes first; the marks and the pass are kept for the sweep after it.</summary>
    [Fact]
    public void AFreshSighting_StillComesFirst_TheMarksKept()
    {
        Cut c = ToTheCut();
        AiPlayer ai = c.Sim.AiOf(0)!;
        ToTheNextHunt(c, forgetSighting: false);
        Assert.Equal(-1, ai.Board.HuntSweep);
        Assert.True(MilitaryManager.Resumes(ai));
        for (int k = 0; k < c.Marks.Length; k++)
            Assert.True(!c.Marks[k] || ai.Board.HuntVisited[k], $"point {k}: the cut hunt's mark was cleared");
        Assert.Equal(c.Pass, ai.Board.HuntPass);
    }

    /// <summary>Guard (green on the base): begun again later than <c>ai.huntMemory</c> after the defense's end, the hunt forgets as before.</summary>
    [Fact]
    public void Guard_AHuntBegunLaterThanTheMemory_ForgetsAsToday()
    {
        Cut c = ToTheCut();
        Simulation sim = c.Sim;
        AiPlayer ai = sim.AiOf(0)!;
        ai.Board.WaveCooldownUntil = sim.TickNumber + ai.Rules.DefendForTicks + ai.Rules.HuntMemoryTicks + 4 * ai.ThinkTicks;
        ToTheNextHunt(c, 20_000);
        Assert.True(sim.TickNumber - 1 - (ai.Board.DefendTick + ai.Rules.DefendForTicks) > ai.Rules.HuntMemoryTicks);
        Assert.Equal(-1, ai.Board.HuntFindTick);
        Assert.False(MilitaryManager.Resumes(ai));
        Assert.Equal(0, ai.Board.HuntPass);
        Assert.Equal(MilitaryManager.NextHuntStart(ai, ai.Board.HuntOrigin, -1) < 0 ? ai.Board.HuntSweep : MilitaryManager.NextHuntStart(ai, ai.Board.HuntOrigin, -1), ai.Board.HuntSweep);
    }

    /// <summary>The twin and a save between the cut and the resume: the same hash every tick for 1,000 ticks past the resume.</summary>
    [Fact]
    public void TheCut_Twins_AndASaveBetweenTheCutAndTheResume_FollowsTheRun()
    {
        Cut a = ToTheCut(), b = ToTheCut();
        Assert.Equal(a.Sim.StateHash(), b.Sim.StateHash());
        foreach (EntityHandle r in a.Raiders) a.Sim.World.Units.Free(r);
        foreach (EntityHandle r in b.Raiders) b.Sim.World.Units.Free(r);
        NextThink(a.Sim);
        NextThink(b.Sim);
        byte[] bytes = SaveTestRun.Save(a.Sim);
        Simulation load = SaveTestRun.Load(s_data.Value, bytes);
        Assert.Equal(bytes, SaveTestRun.Save(load));
        SaveTestRun.AssertSameState(a.Sim, load, "between the cut and the resume");
        Assert.True(MilitaryManager.WasCut(load.AiOf(0)!.Board));
        int resumedAt = -1;
        for (int t = 0; t < 6000 && (resumedAt < 0 || a.Sim.TickNumber < resumedAt + 1000); t++)
        {
            a.Sim.Tick();
            b.Sim.Tick();
            load.Tick();
            Assert.True(a.Sim.StateHash() == b.Sim.StateHash(), $"twins diverged at tick {a.Sim.TickNumber}");
            Assert.True(a.Sim.StateHash() == load.StateHash(), $"the load diverged at tick {a.Sim.TickNumber}");
            if (resumedAt < 0 && a.Sim.AiOf(0)!.Board.Hunting) resumedAt = a.Sim.TickNumber;
        }
        Assert.True(resumedAt > 0, "no resumed hunt");
        Assert.True(MilitaryManager.Resumes(load.AiOf(0)!));
    }
}
