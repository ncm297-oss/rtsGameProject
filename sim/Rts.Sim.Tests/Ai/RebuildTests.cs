using System.Text.Json.Nodes;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M5-4d slice 2, criteria 2 and 5 (docs/03 "Implementation (M5-4d slice 2)"): the AI rebuilds what it lost, on the flat
/// scene (combat off: the command log is what is checked). Player 0 is the Malazan AI with a build order of the test's own
/// (rule 12); a "raze" is the store freeing the building, as a kill does.
/// </summary>
public class RebuildTests
{
    private readonly ITestOutputHelper _out;

    public RebuildTests(ITestOutputHelper output) => _out = output;

    private static int Barracks => TestSim.Data.FindBuilding("malazan_barracks");

    private static int Billet => TestSim.Data.FindBuilding("malazan_billet");

    private static int Keep => TestSim.Data.FindBuilding("malazan_garrison_keep");

    private static int Heavy => TestSim.Data.FindUnit("malazan_heavy_infantry");

    /// <summary>A Barracks at time 0, then a laborer at time 3,600 (never due here); waves from 2 half-pop at no cost (the reserve row's army).</summary>
    private static readonly Lazy<GameData> s_barracks = new(() => AiScenes.DataWith(dir =>
        dir.EditJson("factions/malazan/ai.json", root =>
        {
            AiScenes.Opening(root)["buildOrder"] = AiScenes.BuildOrder(("time", 0, "build", "malazan_barracks", 0), ("time", 3600, "train", "malazan_laborer", 1));
            AiScenes.Opening(root)["attack"] = new JsonObject { ["minArmyPop"] = 1, ["minArmyCost"] = 0 };
        })));

    /// <summary>Two Billets at time 0 and a hard cap of the Keep's 10 pop plus one Billet's 8: the second Billet reaches it.</summary>
    private static readonly Lazy<GameData> s_cap = new(() => AiScenes.DataWith(dir =>
    {
        dir.EditJson("factions/malazan/ai.json", root => AiScenes.Opening(root)["buildOrder"] =
            AiScenes.BuildOrder(("time", 0, "build", "malazan_billet", 0), ("time", 1, "build", "malazan_billet", 0)));
        dir.EditJson("common/rules.json", r => r["popCap"] = 18);
    }));

    /// <summary>Workers trained at time 0 (the hall-first rule holds them back while the hall is gone).</summary>
    private static readonly Lazy<GameData> s_workers = new(() => AiScenes.DataWithAi("malazan", root =>
        AiScenes.Opening(root)["buildOrder"] = AiScenes.BuildOrder(("time", 0, "train", "malazan_laborer", 20))));

    private static Simulation Scene(GameData data)
    {
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human), data);
        BuildMaps.Give(sim, 0, 2000, 2000);
        return sim;
    }

    private static void NextThink(Simulation sim)
    {
        int think = sim.AiOf(0)!.ThinkTicks;
        do sim.Tick(); while ((sim.TickNumber - 1) % think != 0);
    }

    /// <summary>The live building of player 0 of <paramref name="type"/>, finished or not; default with none.</summary>
    private static EntityHandle Own(World w, int type)
    {
        for (int k = 0; k < w.Buildings.Capacity; k++)
            if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == 0 && w.Buildings.TypeId[k] == type) return new EntityHandle(k, w.Buildings.Generation[k]);
        return default;
    }

    private static int Count(World w, int type)
    {
        int n = 0;
        for (int k = 0; k < w.Buildings.Capacity; k++)
            if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == 0 && w.Buildings.TypeId[k] == type) n++;
        return n;
    }

    /// <summary>Ticks until player 0's building of <paramref name="type"/> stands finished (fails past <paramref name="limit"/> ticks).</summary>
    private static EntityHandle UntilFinished(Simulation sim, int type, int limit = 6000)
    {
        World w = sim.World;
        for (int t = 0; t < limit; t++)
        {
            EntityHandle b = Own(w, type);
            if (b.Generation != 0 && !w.Buildings.UnderConstruction[b.Index]) return b;
            sim.Tick();
        }
        Assert.Fail($"no finished {TestSim.Data.Buildings[type].Key} by tick {sim.TickNumber}");
        return default;
    }

    /// <summary>
    /// Criterion 2 (a): a finished Barracks (the build order's done step) razed is built again: a Build of its type within
    /// two thinks of the raze, the step due again in between, and the step done again once the new site stands.
    /// </summary>
    [Fact]
    public void ARazedBarracks_IsBuiltAgain_AndItsStepIsDoneAgain()
    {
        Simulation sim = Scene(s_barracks.Value);
        AiPlayer ai = sim.AiOf(0)!;
        World w = sim.World;
        List<Command> log = AiScenes.Log(sim, 0);
        EntityHandle first = UntilFinished(sim, Barracks);
        Assert.True(ai.Board.StepDone[0]);
        Assert.Single(log, c => c.Kind == CommandKind.Build && c.TypeId == Barracks);
        int razed = sim.TickNumber;
        Assert.True(w.Buildings.Free(first));
        NextThink(sim);
        Assert.False(ai.Board.StepDone[0]); // due again: it lost its building
        NextThink(sim);
        Command again = Assert.Single(log, c => c.Kind == CommandKind.Build && c.TypeId == Barracks && c.Tick > razed);
        Assert.True(again.Tick - razed <= 2 * ai.ThinkTicks + 1, $"rebuilt at {again.Tick}, razed at {razed}");
        for (int k = 0; k < 3; k++) NextThink(sim);
        Assert.True(ai.Board.StepDone[0], "the step is not done again");
        Assert.Equal(1, ai.Board.StepQueued[0]);
        Assert.Equal(1, Count(w, Barracks));
        UntilFinished(sim, Barracks);
        for (int k = 0; k < 5; k++) NextThink(sim);
        Assert.Equal(2, log.Count(c => c.Kind == CommandKind.Build && c.TypeId == Barracks)); // once, not again and again
    }

    /// <summary>
    /// Criterion 2 (b): the hall razed with its workers alive: the first Build the seat sends after it is a Keep at the old
    /// anchor (the Train step waits meanwhile), and once the Keep stands the workers gather again (the bank rises).
    /// </summary>
    [Fact]
    public void ARazedHall_WithWorkersAlive_IsRebuiltFirst_AtItsOldAnchor_AndGatheringGoesOn()
    {
        Simulation sim = Scene(s_workers.Value);
        AiPlayer ai = sim.AiOf(0)!;
        World w = sim.World;
        List<Command> log = AiScenes.Log(sim, 0);
        for (int k = 0; k < 20; k++) NextThink(sim); // gathering under way
        EntityHandle hall = Own(w, Keep);
        int anchor = w.Buildings.Cell[hall.Index];
        int razed = sim.TickNumber;
        Assert.True(w.Buildings.Free(hall));
        NextThink(sim);
        Command build = log.First(c => c.Tick > razed && c.Kind is CommandKind.Build or CommandKind.Train);
        Assert.Equal(CommandKind.Build, build.Kind);
        Assert.Equal(Keep, build.TypeId);
        Assert.Equal(ai.CellCentre(anchor), build.Position);
        Assert.Equal(anchor, ai.Board.HousePending.Cell); // waits to be judged as a house Build does
        EntityHandle rebuilt = UntilFinished(sim, Keep);
        Assert.Equal(anchor, w.Buildings.Cell[rebuilt.Index]);
        Assert.Equal(anchor, ai.TownHallCell);
        Assert.DoesNotContain(log, c => c.Tick > razed && c.Kind == CommandKind.Train && c.Tick < build.Tick);
        int deposits = 0, before = w.Gold[0] + w.Wood[0];
        for (int t = 0; t < 1200; t++)
        {
            sim.Tick();
            int now = w.Gold[0] + w.Wood[0];
            if (now > before) deposits++;
            before = now;
        }
        Assert.True(deposits >= 3, $"{deposits} deposits after the hall stood again");
    }

    /// <summary>Criterion 2 (b), the anchor taken: a tree on the old anchor, so the Keep goes up by the ring search round it, a few cells off.</summary>
    [Fact]
    public void ARazedHall_WhoseAnchorIsTaken_IsRebuiltNearby()
    {
        Simulation sim = Scene(s_workers.Value);
        AiPlayer ai = sim.AiOf(0)!;
        World w = sim.World;
        List<Command> log = AiScenes.Log(sim, 0);
        NextThink(sim);
        EntityHandle hall = Own(w, Keep);
        int anchor = w.Buildings.Cell[hall.Index], size = AiScenes.FlatSize;
        int razed = sim.TickNumber;
        Assert.True(w.Buildings.Free(hall));
        Assert.True(w.Resources.Spawn(TestSim.Data.FindResource("tree"), anchor + size + 1, TestSim.Data.Rules.TreeWood, out _));
        NextThink(sim);
        NextThink(sim);
        Command build = log.First(c => c.Tick > razed && c.Kind == CommandKind.Build);
        Assert.Equal(Keep, build.TypeId);
        Assert.NotEqual(ai.CellCentre(anchor), build.Position);
        float off = System.Numerics.Vector2.Distance(build.Position, ai.CellCentre(anchor));
        Assert.True(off <= 10 * Map.MapConstants.CellSize, $"rebuilt {off} m from the old anchor");
        Assert.True(off > 0f);
    }

    /// <summary>
    /// Criterion 2 (c), D15 C: at the hard cap a razed house is not built again: two done Billet steps, one Billet left, the
    /// cap still the hard cap; no Billet Build after the raze and both steps stay done.
    /// </summary>
    [Fact]
    public void ARazedHouse_AtTheHardCap_IsNotRebuilt()
    {
        Simulation sim = Scene(s_cap.Value);
        AiPlayer ai = sim.AiOf(0)!;
        World w = sim.World;
        List<Command> log = AiScenes.Log(sim, 0);
        for (int t = 0; t < 6000 && !(ai.Board.StepDone[0] && ai.Board.StepDone[1] && w.HalfPopCap[0] >= s_cap.Value.Rules.HalfPopCap); t++) sim.Tick();
        Assert.Equal(s_cap.Value.Rules.HalfPopCap, w.HalfPopCap[0]);
        int billets = Count(w, Billet);
        Assert.True(billets >= 1, "no billet built");
        EntityHandle one = Own(w, Billet);
        if (billets == 1)
        {
            // The second step was done unsent at the cap (D15 C): one billet, two done steps. Raze nothing that drops the cap: add one more first.
            Assert.True(w.Buildings.Spawn(0, Billet, 40 * AiScenes.FlatSize + 20, out _));
        }
        int razed = sim.TickNumber;
        Assert.True(w.Buildings.Free(one));
        for (int k = 0; k < 10; k++) NextThink(sim);
        Assert.Equal(s_cap.Value.Rules.HalfPopCap, w.HalfPopCap[0]); // still at the hard cap
        Assert.True(ai.Board.StepDone[0] && ai.Board.StepDone[1]);
        Assert.DoesNotContain(log, c => c.Tick > razed && c.Kind == CommandKind.Build && c.TypeId == Billet);
    }

    /// <summary>
    /// Criterion 2 (c)'s control: below the cap the same razed Billet is built again (the rule is the cap, not the type).
    /// </summary>
    [Fact]
    public void ARazedHouse_BelowTheCap_IsRebuilt()
    {
        Simulation sim = Scene(s_cap.Value);
        AiPlayer ai = sim.AiOf(0)!;
        World w = sim.World;
        List<Command> log = AiScenes.Log(sim, 0);
        EntityHandle billet = UntilFinished(sim, Billet);
        for (int t = 0; t < 6000 && !(ai.Board.StepDone[0] && ai.Board.StepDone[1]); t++) sim.Tick();
        while (Count(w, Billet) > 1)
        {
            EntityHandle extra = Own(w, Billet);
            Assert.True(w.Buildings.Free(extra)); // an economy house too: keep exactly one
        }
        billet = Own(w, Billet);
        Assert.True(ai.Board.StepDone[0]);
        int razed = sim.TickNumber;
        Assert.True(w.Buildings.Free(billet));
        for (int k = 0; k < 4; k++) NextThink(sim);
        Assert.Contains(log, c => c.Tick > razed && c.Kind == CommandKind.Build && c.TypeId == Billet);
    }

    /// <summary>
    /// Criterion 2 (d): the rebuild obeys the reserve rule by being the reserved step: the razed Barracks' step, due again
    /// with its wood short and an army big enough to reserve, is the think's reserved step, so production spends only above
    /// its cost; the purse filled, it is built.
    /// </summary>
    [Fact]
    public void ARebuildShortOfItsCost_IsTheReservedStep()
    {
        Simulation sim = Scene(s_barracks.Value);
        AiPlayer ai = sim.AiOf(0)!;
        World w = sim.World;
        EntityHandle first = UntilFinished(sim, Barracks);
        for (int k = 0; k < 4; k++) CombatScenes.Place(sim, 0, Heavy, w.NavGrid.CellCenter(18 + k, 40));
        BuildMaps.SetTotals(sim, 0, 0, 0);
        var reserved = new List<(int Step, int Wood)>();
        ai.ThoughtForTests = a => reserved.Add((a.ReserveStep, a.ReserveWood));
        Assert.True(w.Buildings.Free(first));
        NextThink(sim);
        NextThink(sim);
        Assert.False(ai.Board.StepDone[0]);
        Assert.Equal((0, TestSim.Data.Buildings[Barracks].CostWood), reserved[^1]); // the reopened Barracks step holds the reserve
        List<Command> log = AiScenes.Log(sim, 0);
        BuildMaps.Give(sim, 0, 0, TestSim.Data.Buildings[Barracks].CostWood);
        for (int k = 0; k < 3; k++) NextThink(sim); // the first think a worker is free to build
        Assert.Contains(log, c => c.Kind == CommandKind.Build && c.TypeId == Barracks);
    }

    /// <summary>
    /// The hall first, short of its cost: no step is worked and no house sent while the Keep is banked for (its cost the
    /// think's reserve, <see cref="AiPlayer.HallReserved"/>); filled, the Keep is the first Build sent.
    /// </summary>
    [Fact]
    public void ARazedHall_ShortOfItsCost_IsBankedFor_BeforeAnyStepOrHouse()
    {
        Simulation sim = Scene(s_workers.Value);
        AiPlayer ai = sim.AiOf(0)!;
        World w = sim.World;
        NextThink(sim);
        BuildMaps.SetTotals(sim, 0, 0, 0);
        var hallReserved = new List<bool>();
        ai.ThoughtForTests = a => hallReserved.Add(a.HallReserved);
        List<Command> log = AiScenes.Log(sim, 0);
        Assert.True(w.Buildings.Free(Own(w, Keep)));
        for (int k = 0; k < 3; k++) NextThink(sim);
        Assert.All(hallReserved, r => Assert.True(r));
        Assert.DoesNotContain(log, c => c.Kind is CommandKind.Build or CommandKind.Train);
        BuildingDef keep = TestSim.Data.Buildings[Keep];
        BuildMaps.Give(sim, 0, keep.CostGold, keep.CostWood);
        NextThink(sim);
        Command first = Assert.Single(log, c => c.Kind is CommandKind.Build or CommandKind.Train);
        Assert.Equal(Keep, first.TypeId);
    }

    /// <summary>
    /// Criterion 5: a save mid-rebuild (the Barracks step due again with its Build sent and not yet judged, and the hall's
    /// anchor remembered with its Build sent) loads into a sim that ticks hash-equal for 1,000 ticks, the seats field for
    /// field equal, save -> load -> save byte-identical; the format is unchanged (6: an AI seat on a hand-built map).
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ASaveMidRebuild_FollowsTheRun_For1000Ticks(bool hall)
    {
        GameData data = hall ? s_workers.Value : s_barracks.Value;
        Simulation sim = Scene(data);
        AiPlayer ai = sim.AiOf(0)!;
        World w = sim.World;
        if (hall) for (int k = 0; k < 20; k++) NextThink(sim); // the workers at work (one walking to a node is no builder)
        EntityHandle lost = hall ? Own(w, Keep) : UntilFinished(sim, Barracks);
        int anchor = w.Buildings.Cell[lost.Index];
        Assert.True(w.Buildings.Free(lost));
        NextThink(sim); // the step due again (or the hall first), its Build sent, judged at the next think
        if (hall)
        {
            Assert.Equal(anchor, ai.LostHallCell);
            Assert.Equal(PendingKind.Build, ai.Board.HousePending.Kind);
        }
        else
        {
            Assert.False(ai.Board.StepDone[0]);
            Assert.Equal(PendingKind.Build, ai.Board.StepPending[0].Kind);
        }
        byte[] bytes = SaveTestRun.Save(sim);
        Assert.Equal(Rts.Sim.Save.SaveFormat.HuntMaplessVersion, BitConverter.ToInt32(bytes, Rts.Sim.Save.SaveFormat.MagicLength));
        Simulation load = SaveTestRun.Load(data, bytes);
        Assert.Equal(bytes, SaveTestRun.Save(load));
        SaveTestRun.AssertSameState(sim, load, "mid-rebuild");
        for (int t = 0; t < 1000; t++)
        {
            sim.Tick();
            load.Tick();
            Assert.True(sim.StateHash() == load.StateHash(), $"diverged at tick {sim.TickNumber}");
        }
        SaveTestRun.AssertSameState(sim, load, "mid-rebuild + 1,000");
        Assert.Equal(1, Count(w, hall ? Keep : Barracks));
    }

    private const string TwinVariable = "RTS_AI_REBUILD_TWIN_CHILD";
    private const string TwinLine = "REBUILD-TWIN ";

    /// <summary>
    /// Criterion 6 (scene row): two AI seats on the flat scene, player 0's Keep razed at tick 600 (its order training
    /// workers, its bank given the Keep's cost: the Keep was its only drop-off); over 4,000 ticks the seat rebuilds its hall, and this process and a fresh child <c>dotnet test</c> of this
    /// row fold the same per-tick hashes.
    /// </summary>
    [Fact]
    public void AHallRebuild_TwoProcesses_SamePerTickHashes()
    {
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Ai), s_workers.Value);
        BuildMaps.Give(sim, 0, 2000, 2000); // the Keep was the only drop-off: what pays for it must be in the bank
        World w = sim.World;
        AiPlayer ai = sim.AiOf(0)!;
        bool lost = false;
        ai.ThoughtForTests = a => lost |= a.LostHallCell >= 0;
        var h = new Determinism.StateHasher();
        for (int t = 0; t < 4000; t++)
        {
            if (t == 600) Assert.True(w.Buildings.Free(Own(w, Keep)));
            sim.Tick();
            h.Add(sim.StateHash());
        }
        Assert.True(lost, "the hall was never missed");
        Assert.Equal(1, Count(w, Keep));
        string mine = $"{h.Value:X16} {sim.StateHash():X16} {ai.CommandsSent}";
        if (Environment.GetEnvironmentVariable(TwinVariable) == "1")
        {
            _out.WriteLine(TwinLine + mine);
            return;
        }
        Assert.Equal(mine, MapFileTests.RunChild(typeof(RebuildTests), nameof(AHallRebuild_TwoProcesses_SamePerTickHashes), TwinVariable, TwinLine));
    }

    /// <summary>
    /// Criterion 4 (scene row): QA's AI save row seed 3, three Easy seats, cramped (<see cref="AiSaveScenes.AiMatch"/>, a
    /// generated map), where a seat rebuilds a razed build-order building from tick 440: at every think of every seat over
    /// its 12,000 ticks QA's <see cref="PlayerViewOracle"/> finds the view equal to the oracle's (no leak).
    /// </summary>
    [Fact]
    [Trait("Category", "Stress")]
    public void ARebuildingMatch_EverySeatSeesOnlyWhatItsFogGrants()
    {
        SaveScene scene = AiSaveScenes.AiMatch(3, 3, AiDifficulty.Easy, cramped: true);
        Simulation sim = scene.Build();
        World w = sim.World;
        var tally = new PlayerViewOracle.Tally();
        int reopened = 0;
        for (int p = 0; p < 3; p++)
        {
            int player = p;
            AiPlayer seat = sim.AiOf(p)!;
            var was = new bool[seat.Opening.BuildOrder.Length];
            seat.ThoughtForTests = a =>
            {
                for (int i = 0; i < was.Length; i++)
                {
                    if (was[i] && !a.Board.StepDone[i]) reopened++;
                    was[i] = a.Board.StepDone[i];
                }
                PlayerViewOracle.Check(w, a.View, player, tally, $"tick {a.Now} player {player}");
            };
        }
        var commands = new List<Command>();
        for (int t = 0; t < AiSaveScenes.ProbeWindow && tally.First.Length == 0; t++)
        {
            commands.Clear();
            scene.Script(sim, commands);
            foreach (Command c in commands) sim.Enqueue(c);
            sim.Tick();
            for (int p = 0; p < 3; p++) PlayerViewOracle.NodeOracle.For(w).Observe(w, p, tally, $"tick {sim.TickNumber} player {p}");
        }
        _out.WriteLine(tally.ToString());
        Assert.True(reopened > 0, "no step was reopened");
        Assert.True(tally.Leaks == 0 && tally.First.Length == 0, tally.First + " | " + tally);
    }
}
