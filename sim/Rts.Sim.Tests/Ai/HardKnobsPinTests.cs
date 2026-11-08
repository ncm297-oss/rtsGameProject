using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Rts.Sim.Entities;
using Xunit.Abstractions;
using static Rts.Sim.Tests.HardKnobsScenes;

namespace Rts.Sim.Tests;

/// <summary>
/// M5-4c slice 2, "nothing but Hard moved": the Hard knobs' scenes played on Normal (and a human seat's gathering) give the
/// command logs and per-tick hashes the code gave before the slice. Each pin was printed by the pre-slice tree (6f6414b) and
/// copied here before any of the slice's code was written; a change to them is a change to Normal or human play.
/// </summary>
public class HardKnobsPinTests
{
    private readonly ITestOutputHelper _out;

    public HardKnobsPinTests(ITestOutputHelper output) => _out = output;

    /// <summary>The focus scene (combat on) on Normal: 1,200 ticks of player 0's commands.</summary>
    [Fact]
    public void Normal_TheFocusScene_SendsThePreSliceCommands()
    {
        (Simulation sim, _, _) = Focus(AiDifficulty.Normal);
        List<Command> log = AiScenes.Log(sim, 0);
        for (int t = 0; t < 1200; t++) sim.Tick();
        string digest = Digest(log);
        _out.WriteLine($"normal focus scene: {digest}; attacks on units {log.Count(c => c.Kind == CommandKind.Attack && !c.TargetIsBuilding)}");
        Assert.Equal(PreSlice.NormalFocus, digest);
    }

    /// <summary>
    /// The pull-back scene (combat off) on Normal: a wave of four goes out, one member is then cut to 30 hp (below 0.3 of
    /// its 130), 600 ticks of player 0's commands; then the wave is called off by killing the others and a second wave
    /// waits for four fresh units: the wounded one goes with it on Normal.
    /// </summary>
    [Fact]
    public void Normal_ThePullBackScene_SendsThePreSliceCommands()
    {
        Simulation sim = Scene(AiDifficulty.Normal, combat: false);
        List<Command> log = AiScenes.Log(sim, 0);
        List<EntityHandle> members = Army(sim, 4, 18, 28);
        NextThink(sim);
        Assert.True(sim.AiOf(0)!.Board.WaveActive);
        sim.World.Units.Hp[members[1].Index] = 30;
        for (int t = 0; t < 600; t++) sim.Tick();
        foreach (int k in new[] { 0, 2, 3 }) sim.World.Units.Free(members[k]);
        Army(sim, 3, 18, 34);
        for (int t = 0; t < 600; t++) sim.Tick();
        string digest = Digest(log);
        _out.WriteLine($"normal pull-back scene: {digest}; waves {sim.AiOf(0)!.Board.WavesStarted}");
        Assert.Equal(PreSlice.NormalPullBack, digest);
    }

    /// <summary>The flat scene, Normal AI against an idle human, 2,000 ticks (the AI's workers gather): the per-tick hashes folded.</summary>
    [Fact]
    public void Normal_AnAiSeatGathering_HashesAsBeforeTheSlice()
    {
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Human));
        var h = new StateHasher();
        for (int t = 0; t < 2000; t++)
        {
            sim.Tick();
            h.Add(sim.StateHash());
        }
        string digest = $"{h.Value:X16} gold {sim.World.Gold[0]} wood {sim.World.Wood[0]}";
        _out.WriteLine($"normal ai gathering: {digest}");
        Assert.Equal(PreSlice.NormalGathering, digest);
    }

    /// <summary>A human seat's five workers on gold and wood (the default seats: human, Normal), 2,000 ticks: the per-tick hashes folded.</summary>
    [Fact]
    public void Human_WorkersGathering_HashAsBeforeTheSlice()
    {
        Simulation sim = AiScenes.Flat(AiScenes.Seats(AiDifficulty.Normal, SeatController.Human, SeatController.Human));
        World w = sim.World;
        int mine = w.Data.FindResource("gold_mine"), tree = w.Data.FindResource("tree");
        Vector2Pair nodes = Nodes(w, mine, tree);
        int sent = 0;
        for (int i = 0; i < w.Units.Capacity; i++)
        {
            if (!w.Units.Alive[i] || w.Units.Owner[i] != 0) continue;
            sim.Enqueue(Command.Gather(0, new EntityHandle(i, w.Units.Generation[i]), sent++ % 2 == 0 ? nodes.Gold : nodes.Wood));
        }
        var h = new StateHasher();
        for (int t = 0; t < 2000; t++)
        {
            sim.Tick();
            h.Add(sim.StateHash());
        }
        string digest = $"{h.Value:X16} gold {w.Gold[0]} wood {w.Wood[0]}";
        _out.WriteLine($"human gathering: {digest}");
        Assert.Equal(PreSlice.HumanGathering, digest);
    }

    internal readonly record struct Vector2Pair(System.Numerics.Vector2 Gold, System.Numerics.Vector2 Wood);

    /// <summary>The centres of player 0's first gold mine and first tree (slot order) on the flat scene.</summary>
    internal static Vector2Pair Nodes(World w, int mine, int tree)
    {
        System.Numerics.Vector2 gold = default, wood = default;
        bool g = false, t = false;
        for (int k = 0; k < w.Resources.Capacity && !(g && t); k++)
        {
            if (!w.Resources.Alive[k]) continue;
            int cell = w.Resources.Cell[k];
            System.Numerics.Vector2 at = w.NavGrid.CellCenter(cell % AiScenes.FlatSize, cell / AiScenes.FlatSize);
            if (at.X > 64f) continue; // player 0's side (its hall at x 16-24 m)
            if (!g && w.Resources.TypeId[k] == mine) (gold, g) = (at, true);
            if (!t && w.Resources.TypeId[k] == tree) (wood, t) = (at, true);
        }
        Assert.True(g && t);
        return new Vector2Pair(gold, wood);
    }

    /// <summary>The pins, printed by the pre-slice tree (6f6414b).</summary>
    internal static class PreSlice
    {
        internal const string NormalFocus = "22 70CFF929E0F12D3A";
        internal const string NormalPullBack = "33 4C4C9DA505D74655";
        internal const string NormalGathering = "C90D590E1657F974 gold 40 wood 65";
        internal const string HumanGathering = "85474C30A384F903 gold 380 wood 300";
    }
}
