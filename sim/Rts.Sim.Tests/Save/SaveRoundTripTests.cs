using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Replays;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Save;

/// <summary>
/// M6-S2 criteria 2-4 and 8: every scene of S saved at ticks 1, 137 and 1,000, loaded into a fresh sim and ticked beside the
/// unsaved run on the same commands to T + 2,000, the state hash compared every tick, the state outside the hash compared
/// directly at T, T + 1 and T + 2,000, and save -> load -> save byte-identical.
/// </summary>
public class SaveRoundTripTests
{
    private readonly ITestOutputHelper _out;

    public SaveRoundTripTests(ITestOutputHelper output) => _out = output;

    public static IEnumerable<object[]> Scenes() => SaveScenes.Names();

    [Theory]
    [MemberData(nameof(Scenes))]
    public void Scene_SavedAt1_137_1000_FollowsTheUnsavedRun_EveryTick(string name)
    {
        var probe = new SceneProbe(SaveScenes.SaveTicks);
        SaveTestRun.Result r = SaveTestRun.RoundTrip(SaveScenes.Named(name), SaveScenes.SaveTicks, observe: probe.Observe);
        for (int k = 0; k < r.SaveTicks.Length; k++) _out.WriteLine($"{name}: save at {r.SaveTicks[k]}: {r.Bytes[k]:N0} bytes");
        _out.WriteLine($"{name}: {probe}");
        Assert.Equal(3 * 2000, r.TicksCompared);
        AssertSubject(name, probe);
    }

    /// <summary>Each scene exercised what it is in S for (so a pass means the subject was saved and restored).</summary>
    private static void AssertSubject(string name, SceneProbe p)
    {
        if (name.StartsWith("menu_", StringComparison.Ordinal) || name == "four_seats_256")
        {
            Assert.True(p.Gathered && p.Built && p.Trained && p.Researched && p.AttackMoved && p.Cast && p.Fought, $"{name}: {p}");
            return;
        }
        switch (name)
        {
            case "surround_brawl":
                Assert.True(p.ClaimAt.IsSupersetOf(new[] { 137, 1000 }) && p.Razed, p.ToString());
                break;
            case "casts_and_sandstorm":
                Assert.True(p.CastingAt.SetEquals(SaveScenes.SaveTicks) && p.ZoneAt.IsSupersetOf(new[] { 137, 1000 }), p.ToString());
                break;
            case "stealth_beside_detector":
                Assert.True(p.Detected && p.HiddenFromStealthy, p.ToString());
                break;
            case "construction_and_queue":
                Assert.True(p.SiteAt.Count > 0 && p.FullTechQueueAt.Count > 0, p.ToString());
                break;
            case "felled_tree_and_razed_building":
                Assert.True(p.TreeFelled && p.Razed, p.ToString());
                break;
            case "defeated_seat":
                Assert.True(p.DefeatedAt.IsSupersetOf(new[] { 137, 1000 }), p.ToString());
                break;
        }
    }

    [Fact]
    public void ASaveAtTickZero_WithTheSetupStillQueued_FollowsTheUnsavedRun()
    {
        SaveTestRun.Result r = SaveTestRun.RoundTrip(SaveScenes.DefeatedSeat(), new[] { 0 }, after: 600);
        Assert.Equal(600, r.TicksCompared);
    }

    [Fact]
    public void ALoadedSim_HasTheSavedTickMatchOverAndQueue_AndNoAiOpening()
    {
        Simulation sim = SaveBytes.SmallSim();
        Assert.True(sim.PendingCommandCount > 0);
        Simulation loaded = SaveTestRun.Load(TestSim.Data, SaveTestRun.Save(sim));
        Assert.Equal((sim.TickNumber, sim.MatchOver, sim.PendingCommandCount), (loaded.TickNumber, loaded.MatchOver, loaded.PendingCommandCount));
        for (int p = 0; p < loaded.World.Config.PlayerCount; p++) Assert.Equal(-1, loaded.AiOpening(p));
    }

    [Fact]
    public void AMatchThatIsOver_LoadsOver_AndStaysOver()
    {
        // Player 1 builds only a House and has no unit: out on its first tick, so the result latches (BUG-0672).
        Simulation sim = CombatScenes.Flat(48, 32, 2);
        sim.Enqueue(Command.SpawnBuilding(0, TestSim.Data.FindBuilding("malazan_garrison_keep"), new Vector2(20f, 20f)));
        sim.Enqueue(Command.SpawnUnit(0, CombatScenes.Laborer, new Vector2(30f, 30f)));
        sim.Enqueue(Command.SpawnBuilding(1, TestSim.Data.FindBuilding("whirlwind_tent"), new Vector2(60f, 60f)));
        for (int t = 0; t < 5; t++) sim.Tick();
        Assert.True(sim.MatchOver);
        Assert.True(sim.World.MatchOverLatched);
        Simulation loaded = SaveTestRun.Load(TestSim.Data, SaveTestRun.Save(sim));
        Assert.True(loaded.MatchOver);
        Assert.True(loaded.World.MatchOverLatched);
        for (int t = 0; t < 100; t++)
        {
            sim.Tick();
            loaded.Tick();
            Assert.Equal(sim.StateHash(), loaded.StateHash());
        }
        SaveTestRun.AssertSameState(sim, loaded, "match over + 100");
    }

    [Fact]
    public void ARecorder_AttachesToALoadedSim_OnlyBeforeItsFirstTick()
    {
        // A fresh generated sim (tick 0, nothing queued) saved and loaded: the recorder's rule holds, and both recordings agree.
        SaveScene scene = SaveScenes.Generated("record", 2, 2, 128, forests: 4, mines: 4);
        Simulation original = scene.Build();
        var fresh = new Simulation(original.World.Config);
        Simulation loaded = SaveTestRun.Load(TestSim.Data, SaveTestRun.Save(fresh));
        var a = new ReplayRecorder(fresh);
        var b = new ReplayRecorder(loaded);
        var commands = new List<Command>();
        for (int t = 0; t < 300; t++)
        {
            commands.Clear();
            if (t == 0)
            {
                // The bases, as the scene queues them, then the script.
                SaveScenes.PlaceBases(fresh, army: true);
                SaveScenes.PlaceBases(loaded, army: true);
            }
            scene.Script(fresh, commands);
            foreach (Command c in commands)
            {
                fresh.Enqueue(c);
                loaded.Enqueue(c);
            }
            fresh.Tick();
            loaded.Tick();
        }
        ReplayTestRun.AssertEqual(a.ToReplay(), b.ToReplay());

        // A save made after a tick loads into a sim the recorder refuses (it can't replay from tick 0).
        Simulation later = SaveTestRun.Load(TestSim.Data, SaveTestRun.Save(fresh));
        Assert.Throws<InvalidOperationException>(() => new ReplayRecorder(later));
    }

    [Fact]
    public void TheMenuMatch_SavedAt50SeededTicks_FollowsTheUnsavedRunFromEach()
    {
        // Ticks drawn from a seeded stream (distinct, under 3,000), each load ticked 200 ticks beside the reference.
        var rng = new Determinism.SimRng(42, 9);
        var ticks = new SortedSet<int>();
        while (ticks.Count < 50) ticks.Add(rng.NextInt(0, 3000));
        SaveTestRun.Result r = SaveTestRun.RoundTrip(SaveScenes.Menu(1), ticks.ToArray(), after: 200);
        Assert.Equal(50 * 200, r.TicksCompared);
    }
}
