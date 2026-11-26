using System.Buffers.Binary;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Rts.Sim.Save;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M7-9 (Request 59) criteria 7 and 8: saves and determinism on per-difficulty data, {<c>buildOrder</c> A,
/// <c>buildOrders</c> {easy: B, hard: C}} (<see cref="PerDifficultyOrders"/>: C is one step longer than A and B). No save
/// format change (10): a seat's step arrays are as long as its own list, and a save whose arrays do not match the loading
/// seat's list is refused <see cref="SaveError.Malformed"/> as before.
/// </summary>
public class PerDifficultySaveTests
{
    private const int After = 6_000, Bound = 12_000, Every = 500;

    private readonly ITestOutputHelper _out;

    public PerDifficultySaveTests(ITestOutputHelper output) => _out = output;

    private static readonly Lazy<GameData> s_data = new(() => PerDifficultyOrders.Data(PerDifficultyOrders.A(),
        (AiDifficulty.Easy, PerDifficultyOrders.B()), (AiDifficulty.Hard, PerDifficultyOrders.C())));

    private static Simulation EasyAndHard(GameData data)
    {
        int teblor = data.FindFaction(PerDifficultyOrders.Faction);
        var seats = new[] { new PlayerSeat(teblor, SeatController.Ai, AiDifficulty.Easy), new PlayerSeat(teblor, SeatController.Ai, AiDifficulty.Hard) };
        return TeblorSwitchOrders.Match(data, "pale_hills", seats, 2);
    }

    /// <summary>Whether a seat is mid-order: a step done, one not done, and a step's command sent and not yet judged.</summary>
    private static bool MidOrder(AiPlayer ai)
    {
        bool done = false, open = false, pending = false;
        for (int i = 0; i < ai.Steps.Length; i++)
        {
            done |= ai.Board.StepDone[i];
            open |= !ai.Board.StepDone[i];
            pending |= ai.Board.StepPending[i].Kind != PendingKind.None;
        }
        return done && open && pending;
    }

    private static string State(AiPlayer ai)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < ai.Steps.Length; i++) sb.Append(ai.Board.StepDone[i] ? 'D' : ai.Board.StepPending[i].Kind != PendingKind.None ? 'P' : '.');
        return sb.ToString();
    }

    /// <summary>
    /// A sim of <see cref="EasyAndHard"/> ticked to the first tick seat <paramref name="seat"/> (0 Easy, 1 Hard) is
    /// <see cref="MidOrder"/> while the other seat has a step done too. (The two seats' pending windows rarely meet: a
    /// command is pending only from its think to the next one.)
    /// </summary>
    private static Simulation AtMidOrder(GameData data, int seat)
    {
        Simulation sim = EasyAndHard(data);
        AiPlayer mid = sim.AiOf(seat)!, other = sim.AiOf(1 - seat)!;
        while (!(MidOrder(mid) && Array.IndexOf(other.Board.StepDone, true) >= 0))
        {
            sim.Tick();
            Assert.True(sim.TickNumber < 6_000, $"seat {seat} was never mid-order: {State(mid)} / {State(other)}");
        }
        return sim;
    }

    /// <summary>
    /// Criterion 7: a save taken mid-order (steps done, a step's command pending) by the Easy seat, and one by the Hard seat,
    /// of a match on per-difficulty data loads, equals the saved sim field for field (the AI seats too), and stays
    /// tick-identical to the unsaved run for 6,000 ticks.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ASaveMidOrder_LoadsAndFollowsTheRun_For6000Ticks(int seat)
    {
        GameData data = s_data.Value;
        AiOpening o = PerDifficultyOrders.Standard(data);
        Simulation sim = AtMidOrder(data, seat);
        AiPlayer easy = sim.AiOf(0)!, hard = sim.AiOf(1)!;
        Assert.True(easy.Steps.SequenceEqual(o.BuildOrderFor(AiDifficulty.Easy)) && hard.Steps.SequenceEqual(o.BuildOrderFor(AiDifficulty.Hard)));
        Assert.Equal(o.BuildOrder.Length + 1, hard.Board.StepDone.Length); // C: one step longer
        Assert.Equal(o.BuildOrder.Length, easy.Board.StepDone.Length);
        int savedAt = sim.TickNumber;
        string state = $"{State(easy)} / {State(hard)}";
        byte[] bytes = SaveTestRun.Save(sim);
        Assert.Equal(SaveFormat.CurrentVersion, BitConverter.ToInt32(bytes, SaveFormat.MagicLength));
        Simulation load = SaveTestRun.Load(data, bytes);
        Assert.Equal(bytes, SaveTestRun.Save(load));
        SaveTestRun.AssertSameState(sim, load, "mid-order");
        for (int t = 0; t < After; t++)
        {
            sim.Tick();
            load.Tick();
            Assert.True(sim.StateHash() == load.StateHash(), $"the load differs at tick {sim.TickNumber}");
        }
        SaveTestRun.AssertSameState(sim, load, "mid-order + 6,000");
        _out.WriteLine($"seat {seat} mid-order ({state}); saved at {savedAt}, {After} ticks equal, end {sim.StateHash():X16}");
    }

    /// <summary>
    /// Criterion 7: the save above, its header's data hash made another data's (the bytes re-checksummed), loads on data
    /// whose Hard list has the same length (one step's time moved) and is refused <see cref="SaveError.Malformed"/> on data
    /// whose Hard list is one step shorter (B for Hard): the step arrays are as long as the seat's own list.
    /// </summary>
    [Fact]
    public void ASaveWhoseStepArrays_DoNotMatchTheSeatsList_IsRefused()
    {
        byte[] bytes = SaveTestRun.Save(AtMidOrder(s_data.Value, 1));
        var sameLength = PerDifficultyOrders.C();
        sameLength[sameLength.Count - 1]!["at"]!["time"] = 901;
        GameData same = PerDifficultyOrders.Data(PerDifficultyOrders.A(), (AiDifficulty.Easy, PerDifficultyOrders.B()), (AiDifficulty.Hard, sameLength));
        GameData shorter = PerDifficultyOrders.Data(PerDifficultyOrders.A(), (AiDifficulty.Easy, PerDifficultyOrders.B()), (AiDifficulty.Hard, PerDifficultyOrders.B()));
        Assert.Equal(SaveError.DataMismatch, SaveTestRun.TryLoad(same, bytes)); // as made: the header names the data
        Assert.Equal(SaveError.None, SaveTestRun.TryLoad(same, WithDataHash(bytes, same.ContentHash())));
        Assert.Equal(SaveError.Malformed, SaveTestRun.TryLoad(shorter, WithDataHash(bytes, shorter.ContentHash())));
        // The Easy seat too: its list one step longer (C for Easy) is refused.
        GameData longer = PerDifficultyOrders.Data(PerDifficultyOrders.A(), (AiDifficulty.Easy, PerDifficultyOrders.C()), (AiDifficulty.Hard, PerDifficultyOrders.C()));
        Assert.Equal(SaveError.Malformed, SaveTestRun.TryLoad(longer, WithDataHash(bytes, longer.ContentHash())));
    }

    private static byte[] WithDataHash(byte[] bytes, ulong hash)
    {
        byte[] copy = (byte[])bytes.Clone();
        BinaryPrimitives.WriteUInt64LittleEndian(copy.AsSpan(SaveBytes.HeaderOffsets(copy).DataHash), hash);
        return SaveBytes.Rechecksum(copy);
    }

    /// <summary>The four-seat match of criterion 8: Teblor seats Easy, Normal, Hard, Hard on per-difficulty data, Pale Hills seed 1.</summary>
    private static Simulation FourSeats(GameData data)
    {
        int teblor = data.FindFaction(PerDifficultyOrders.Faction);
        var seats = new[]
        {
            new PlayerSeat(teblor, SeatController.Ai, AiDifficulty.Easy), new PlayerSeat(teblor, SeatController.Ai, AiDifficulty.Normal),
            new PlayerSeat(teblor, SeatController.Ai, AiDifficulty.Hard), new PlayerSeat(teblor, SeatController.Ai, AiDifficulty.Hard),
        };
        return TeblorSwitchOrders.Match(data, "pale_hills", seats, 1);
    }

    /// <summary>The checkpoint hashes (every 500 ticks to 12,000) of <see cref="FourSeats"/>, and each seat's list length and commands sent.</summary>
    private static string Checkpoints(GameData data)
    {
        Simulation sim = FourSeats(data);
        var lines = new List<string>();
        for (int p = 0; p < 4; p++) lines.Add($"seat {p}: {sim.AiOf(p)!.Steps.Length} steps");
        while (sim.TickNumber < Bound)
        {
            sim.Tick();
            if (sim.TickNumber % Every == 0) lines.Add($"{sim.TickNumber}:{sim.StateHash():X16}");
        }
        for (int p = 0; p < 4; p++) lines.Add($"seat {p}: {sim.AiOf(p)!.CommandsSent} commands");
        return string.Join(" ", lines);
    }

    /// <summary>Criterion 8, in one process: the four-seat match twice, equal hashes at every checkpoint; the seats play their own lists.</summary>
    [Fact]
    public void FourSeats_Twice_EqualAtEveryCheckpoint()
    {
        GameData data = s_data.Value;
        AiOpening o = PerDifficultyOrders.Standard(data);
        string first = Checkpoints(data), second = Checkpoints(data);
        Assert.Equal(first, second);
        Assert.StartsWith($"seat 0: {o.BuildOrderFor(AiDifficulty.Easy).Length} steps seat 1: {o.BuildOrder.Length} steps seat 2: {o.BuildOrder.Length + 1} steps seat 3: {o.BuildOrder.Length + 1} steps", first);
        _out.WriteLine(first);
    }

    private const string TwinVariable = "RTS_M79_FOUR_SEAT_CHILD";
    private const string TwinLine = "M79-FOUR-SEAT ";

    /// <summary>Criterion 8, across two processes: this process and a fresh child <c>dotnet test</c> of this row print the same checkpoints.</summary>
    [Fact]
    [Trait("Category", "Stress")]
    public void FourSeats_TwoProcesses_EqualAtEveryCheckpoint()
    {
        string mine = Checkpoints(s_data.Value);
        if (Environment.GetEnvironmentVariable(TwinVariable) == "1")
        {
            _out.WriteLine(TwinLine + mine);
            return;
        }
        Assert.Equal(mine, MapFileTests.RunChild(typeof(PerDifficultySaveTests), nameof(FourSeats_TwoProcesses_EqualAtEveryCheckpoint), TwinVariable, TwinLine, 600));
        _out.WriteLine(mine);
    }
}
