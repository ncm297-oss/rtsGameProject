using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// QA, M6-S15 D (QA focus 5; BUG-1855, <c>MilitaryManager.MarkCut</c>): <see cref="HuntCutResumeTests"/>' scene pushed past
/// its rows. A hunt cut twice in a row keeps every mark of both legs; a save at the cut loaded in another process follows the
/// run; a resumed hunt still ends (it finds the enemy hall and stops hunting).
/// </summary>
public class M6S15HuntQaTests
{
    private const string Variable = "RTS_QA_M6S15_HUNT_SAVE", Marker = "M6S15-HUNT-DIGEST: ";

    private readonly ITestOutputHelper _out;

    public M6S15HuntQaTests(ITestOutputHelper output) => _out = output;

    // The same edit as HuntCutResumeTests' data (a save names the data by its hash, so the load needs an equal copy).
    private static readonly Lazy<GameData> s_data = new(() => AiScenes.DataWith(dir =>
        dir.EditJson("factions/malazan/ai.json", root =>
        {
            foreach (JsonNode? o in root["openings"]!.AsArray())
            {
                AiScenes.SetBuildOrder(o, AiScenes.BuildOrder(("time", 3600, "train", "malazan_laborer", 1)));
                o["attack"] = new JsonObject { ["minArmyPop"] = 2, ["minArmyCost"] = 0 };
            }
        })));

    private static void NextThink(Simulation sim)
    {
        int think = sim.AiOf(0)!.ThinkTicks;
        do sim.Tick(); while ((sim.TickNumber - 1) % think != 0);
    }

    /// <summary>Frees every unit of player 1 (the raiders: player 1 has only a Town Hall) and forgets the sighting, then thinks until a hunt begins.</summary>
    private static void RaidersGo_ToTheNextHunt(Simulation sim, int limit = 12_000)
    {
        World w = sim.World;
        AiPlayer ai = sim.AiOf(0)!;
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == 1) w.Units.Free(new EntityHandle(i, w.Units.Generation[i]));
        ai.Board.EnemyUnitTick = -1;
        int until = sim.TickNumber + limit;
        while (!ai.Board.Hunting && sim.TickNumber < until) NextThink(sim);
        Assert.True(ai.Board.Hunting, "no hunt after the defense");
    }

    private static void Raid(Simulation sim)
    {
        AiPlayer ai = sim.AiOf(0)!;
        int hx = (int)(ai.HallPoint.X / MapConstants.CellSize), hy = (int)(ai.HallPoint.Y / MapConstants.CellSize);
        int raider = sim.World.Data.FindUnit("whirlwind_raider");
        for (int k = 0; k < 12; k++) CombatScenes.Place(sim, 1, raider, sim.World.NavGrid.CellCenter(hx - 3 + k % 6, hy + 4 + k / 6));
    }

    private static int Marks(AiPlayer ai) => ai.Board.HuntVisited.Count(m => m);

    /// <summary>Cut, resumed, walked to two more marks, cut again, resumed again: no mark of either leg is lost and the pass is kept.</summary>
    [Fact]
    public void AHuntCutTwiceInARow_KeepsEveryMarkOfBothLegs()
    {
        HuntCutResumeTests.Cut c = HuntCutResumeTests.ToTheCut();
        Simulation sim = c.Sim;
        AiPlayer ai = sim.AiOf(0)!;
        RaidersGo_ToTheNextHunt(sim);
        Assert.True(MilitaryManager.Resumes(ai));
        int first = Marks(ai);
        while (Marks(ai) < first + 2 && ai.Board.Hunting && sim.TickNumber < 60_000) NextThink(sim);
        Assert.True(ai.Board.Hunting, "the resumed hunt ended before two more marks");
        bool[] before = (bool[])ai.Board.HuntVisited.Clone();
        int pass = ai.Board.HuntPass;
        Raid(sim);
        for (int k = 0; k < 6 && ai.Board.Hunting; k++)
        {
            before = (bool[])ai.Board.HuntVisited.Clone();
            pass = ai.Board.HuntPass;
            NextThink(sim);
        }
        Assert.False(ai.Board.Hunting, "the second raid did not cut the hunt");
        Assert.True(MilitaryManager.WasCut(ai.Board));
        RaidersGo_ToTheNextHunt(sim);
        _out.WriteLine($"first cut: {c.Marks.Count(m => m)} marks; after the first resume {first}; at the second cut {before.Count(m => m)}; resumed again at {sim.TickNumber} with {Marks(ai)} marks, pass {ai.Board.HuntPass}, target {ai.Board.HuntSweep}");
        for (int k = 0; k < before.Length; k++)
            Assert.True(!before[k] || ai.Board.HuntVisited[k], $"point {k}: a mark was lost across the second cut");
        for (int k = 0; k < c.Marks.Length; k++)
            Assert.True(!c.Marks[k] || ai.Board.HuntVisited[k], $"point {k}: a mark of the first leg was lost");
        Assert.Equal(pass, ai.Board.HuntPass);
        Assert.True(ai.Board.HuntSweep < 0 || !ai.Board.HuntVisited[ai.Board.HuntSweep], "the resumed target is a visited point");
    }

    /// <summary>A resumed hunt still ends: it stops hunting (finds the enemy's hall, or ends its pass) within 60,000 ticks.</summary>
    [Fact]
    public void AResumedHunt_StillEnds()
    {
        HuntCutResumeTests.Cut c = HuntCutResumeTests.ToTheCut();
        Simulation sim = c.Sim;
        AiPlayer ai = sim.AiOf(0)!;
        RaidersGo_ToTheNextHunt(sim);
        int resumed = sim.TickNumber;
        while (ai.Board.Hunting && sim.TickNumber < resumed + 60_000) NextThink(sim);
        _out.WriteLine($"resumed at {resumed}; hunting {ai.Board.Hunting} at {sim.TickNumber}; marks {Marks(ai)}, pass {ai.Board.HuntPass}, wave target type {ai.Board.WaveTargetType}");
        Assert.False(ai.Board.Hunting, "the resumed hunt never ended");
    }

    private static string Follow(Simulation sim)
    {
        AiPlayer ai = sim.AiOf(0)!;
        var fold = new StateHasher();
        World w = sim.World;
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.Alive[i] && w.Units.Owner[i] == 1) w.Units.Free(new EntityHandle(i, w.Units.Generation[i]));
        ai.Board.EnemyUnitTick = -1;
        int resumed = -1;
        for (int t = 0; t < 4_000; t++)
        {
            sim.Tick();
            fold.Add(sim.StateHash());
            if (resumed < 0 && ai.Board.Hunting) resumed = sim.TickNumber;
        }
        return $"resumed {resumed}, sweep {ai.Board.HuntSweep}, pass {ai.Board.HuntPass}, marks {Marks(ai)}, end {sim.StateHash():X16}, fold {fold.Value:X16}";
    }

    /// <summary>A save on the cut's think, loaded in a child <c>dotnet test</c>, follows this process's run for 4,000 ticks past the raiders' going (the resume inside it).</summary>
    [Fact]
    [Trait("Category", "Stress")]
    public void ASaveAtTheCut_LoadedInAnotherProcess_FollowsTheRun()
    {
        string? path = Environment.GetEnvironmentVariable(Variable);
        if (path != null)
        {
            Simulation child = SaveTestRun.Load(s_data.Value, File.ReadAllBytes(path));
            Assert.True(MilitaryManager.WasCut(child.AiOf(0)!.Board));
            _out.WriteLine(Marker + Follow(child));
            return;
        }
        HuntCutResumeTests.Cut c = HuntCutResumeTests.ToTheCut();
        byte[] bytes = SaveTestRun.Save(c.Sim);
        string file = Path.Combine(Path.GetTempPath(), $"rts-qa-m6s15-hunt-{Environment.ProcessId}.sav");
        File.WriteAllBytes(file, bytes);
        try
        {
            string mine = Follow(c.Sim);
            string child = MapFileTests.RunChild(typeof(M6S15HuntQaTests), nameof(ASaveAtTheCut_LoadedInAnotherProcess_FollowsTheRun), Variable, Marker, seconds: 300, value: file);
            _out.WriteLine($"this process: {mine}; the child: {child}");
            Assert.Equal(mine, child);
            Assert.DoesNotContain("resumed -1", mine);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
