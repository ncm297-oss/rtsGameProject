using Rts.Sim.Ai;
using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Draw;

/// <summary>
/// M6-S13 criterion 8: a drawn AI match (two Normal AI seats on a generated map where nothing can be trained:
/// <see cref="DrawScenes.Starved"/>) gives the same draw tick, state hash and per-tick fold when run twice, and in a second
/// process. (The shipped-data AI matches that draw are <c>Stress/DrawStalledRowsReportTests</c>, also run twice there.)
/// </summary>
public class DrawDeterminismTests
{
    private readonly ITestOutputHelper _out;

    public DrawDeterminismTests(ITestOutputHelper output) => _out = output;

    private const string ChildVariable = "RTS_DRAW_DETERMINISM_CHILD";
    private const string ChildLine = "DRAW-AI ";

    /// <summary>The match played to its end: "draw tick, hash, fold".</summary>
    private static string Play(ulong seed)
    {
        PlayerSeat[] seats = { new(0, SeatController.Ai, AiDifficulty.Normal), new(1, SeatController.Ai, AiDifficulty.Hard) };
        Simulation sim = AiScenes.Generated(seats, seed, false, out _, data: DrawScenes.Starved);
        var fold = new StateHasher();
        while (!sim.MatchOver && sim.TickNumber < 20 * DrawScenes.ShortTicks)
        {
            sim.Tick();
            fold.Add(sim.StateHash());
        }
        Assert.True(sim.World.MatchDrawn, $"seed {seed}: not drawn by tick {sim.TickNumber}");
        Assert.False(sim.World.IsDefeated(0) || sim.World.IsDefeated(1));
        return $"{sim.TickNumber} {sim.StateHash():X16} {fold.Value:X16}";
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(4UL)]
    public void ADrawnAiMatch_RunTwice_SameDrawTickAndHash(ulong seed)
    {
        string a = Play(seed), b = Play(seed);
        _out.WriteLine($"seed {seed}: {a}");
        Assert.Equal(a, b);
    }

    [Fact]
    public void ADrawnAiMatch_TwoProcesses_SameDrawTickAndHash()
    {
        string mine = Play(1);
        if (Environment.GetEnvironmentVariable(ChildVariable) == "1")
        {
            _out.WriteLine(ChildLine + mine);
            return;
        }
        string child = MapFileTests.RunChild(typeof(DrawDeterminismTests), nameof(ADrawnAiMatch_TwoProcesses_SameDrawTickAndHash), ChildVariable, ChildLine);
        _out.WriteLine($"this process {mine}; the child {child}");
        Assert.Equal(mine, child);
    }
}
