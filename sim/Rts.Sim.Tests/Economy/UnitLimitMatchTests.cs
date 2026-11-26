using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Replays;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M8-4 criteria 12-13 (Request 56 G4): saves, replays and determinism with a limited type. No save or replay format change:
/// the count is derived (rebuilt at load with the draw counts, <c>World.RecountDraw</c>), never saved, never hashed. The
/// match: <see cref="UnitLimitAiTests"/>' copy (Heavy Infantry limited to 7; the Malazan seat's order trains 9), a generated
/// map, a Hard Malazan AI against a Normal Whirlwind AI, the <c>high</c> start so the limit is reached early, combat on (units
/// die, the seat trains again).
/// </summary>
public class UnitLimitMatchTests
{
    private const int Bound = 12_000, Every = 500;

    private readonly ITestOutputHelper _out;

    public UnitLimitMatchTests(ITestOutputHelper output) => _out = output;

    internal static GameData Data => UnitLimitAiTests.s_data.Value;

    internal static PlayerSeat[] Seats() =>
        new[] { new PlayerSeat(0, SeatController.Ai, AiDifficulty.Hard), new PlayerSeat(1, SeatController.Ai, AiDifficulty.Normal) };

    internal static Simulation Match(ulong seed, bool record, out ReplayRecorder? rec) =>
        AiScenes.Generated(Seats(), seed, record, out rec, data: Data, configure: c => c with { StartingResources = "high" });

    private static int Heavy => Data.FindUnit(MaxAliveLoaderTests.Heavy);

    /// <summary>Player 0's Heavy Infantry in queues.</summary>
    private static int QueuedHeavy(World w)
    {
        BuildingStore b = w.Buildings;
        int n = 0;
        for (int k = 0; k < b.Capacity; k++)
            if (b.Alive[k] && b.Owner[k] == 0)
                for (int q = 0; q < b.QueueCount[k]; q++)
                    if (!b.QueueIsTechAt(k, q) && b.QueueTypeAt(k, q) == Heavy) n++;
        return n;
    }

    /// <summary>The match's checkpoints (state hash and player 0's count every 500 ticks to 12,000) and the highest count seen.</summary>
    internal static string Checkpoints(ulong seed)
    {
        Simulation sim = Match(seed, false, out _);
        var lines = new List<string>();
        int max = 0;
        while (sim.TickNumber < Bound && !sim.MatchOver)
        {
            sim.Tick();
            int count = UnitLimitTests.Count(sim.World, 0, Heavy);
            Assert.True(count <= 7, $"count {count} at tick {sim.TickNumber}");
            max = Math.Max(max, count);
            if (sim.TickNumber % Every == 0) lines.Add($"{sim.TickNumber}:{sim.StateHash():X16}:{count}");
        }
        Assert.Equal(7, max);
        lines.Add($"end {sim.TickNumber} max {max} commands {sim.AiOf(0)!.CommandsSent}/{sim.AiOf(1)!.CommandsSent}");
        return string.Join(" ", lines);
    }

    /// <summary>Criterion 13, one process: the match twice, equal at every checkpoint (and the seat reached the limit).</summary>
    [Fact]
    public void ACappedMatch_Twice_EqualAtEveryCheckpoint()
    {
        string first = Checkpoints(4), second = Checkpoints(4);
        Assert.Equal(first, second);
        _out.WriteLine(first);
    }

    /// <summary>
    /// Criterion 12: a save taken with player 0 at its limit with a Heavy Infantry item still queued loads, and the loaded run
    /// is hash-equal to the unsaved one every tick for 2,000 ticks; after the load the kept count equals the recount (rebuilt,
    /// not saved), a Train at the limit is refused as before, and the count never passes 7.
    /// </summary>
    [Fact]
    public void ASaveAtTheLimit_WithAQueuedItem_LoadsAndRunsEqual_AndTheLimitHolds()
    {
        Simulation sim = Match(4, false, out _);
        World w = sim.World;
        while (!(UnitLimitTests.Count(w, 0, Heavy) == 7 && QueuedHeavy(w) > 0))
        {
            sim.Tick();
            Assert.True(sim.TickNumber < Bound, "the seat never stood at its limit with an item queued");
        }
        int at = sim.TickNumber;
        Simulation loaded = SaveTestRun.Load(Data, SaveTestRun.Save(sim));
        World lw = loaded.World;
        Assert.True(lw.UnitLimit(0, Heavy, out int count, out int limit));
        Assert.Equal((7, 7), (count, limit));
        Assert.Equal(UnitLimitTests.Recount(lw, 0, Heavy), count);
        Assert.True(QueuedHeavy(lw) > 0);
        Assert.Equal(sim.StateHash(), loaded.StateHash());
        int refusedAt = 0;
        for (int t = 0; t < 2_000; t++)
        {
            sim.Tick();
            loaded.Tick();
            Assert.True(sim.StateHash() == loaded.StateHash(), $"differs at tick {sim.TickNumber} (saved at {at})");
            int c = UnitLimitTests.Count(lw, 0, Heavy);
            Assert.Equal(UnitLimitTests.Count(w, 0, Heavy), c);
            Assert.True(c <= 7);
            for (int k = 0; k < lw.Buildings.Capacity && c == 7; k++)
                if (lw.Buildings.Alive[k] && lw.Buildings.Owner[k] == 0 && !lw.Buildings.UnderConstruction[k] && lw.Buildings.TypeId[k] == Data.Units[Heavy].TrainedAtTypeId)
                {
                    Assert.False(lw.CanTrain(0, k, Heavy, out Economy.TrainError why));
                    Assert.Equal(Economy.TrainError.LimitReached, why);
                    refusedAt++;
                }
        }
        Assert.True(refusedAt > 0);
        _out.WriteLine($"saved at {at}; 2,000 ticks equal; the gate read LimitReached on {refusedAt} building-ticks after the load");
    }

    /// <summary>Criterion 12: a recorded capped match (6,000 ticks, the limit reached) plays back to every checkpoint.</summary>
    [Fact]
    public void ACappedMatchsReplay_PlaysToEveryCheckpoint()
    {
        Simulation sim = Match(5, true, out ReplayRecorder? rec);
        int max = 0;
        while (sim.TickNumber < 6_000)
        {
            sim.Tick();
            max = Math.Max(max, UnitLimitTests.Count(sim.World, 0, Heavy));
        }
        Assert.Equal(7, max);
        Replay replay = rec!.ToReplay();
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(ReplayFormat.Write(replay), out Replay? back));
        ReplayResult played = ReplayPlayer.Run(back!, Data);
        Assert.True(played.Ok, played.ToString());
        Assert.Equal(60, back!.Checkpoints.Length);
    }

    private const string TwinVariable = "RTS_M84_CAPPED_CHILD";
    private const string TwinLine = "M84-CAPPED ";

    /// <summary>Criterion 13, across two processes: this process and a fresh child <c>dotnet test</c> of this row print the same checkpoints.</summary>
    [Fact]
    [Trait("Category", "Stress")]
    public void ACappedMatch_TwoProcesses_EqualAtEveryCheckpoint()
    {
        string mine = Checkpoints(4);
        if (Environment.GetEnvironmentVariable(TwinVariable) == "1")
        {
            _out.WriteLine(TwinLine + mine);
            return;
        }
        Assert.Equal(mine, MapFileTests.RunChild(typeof(UnitLimitMatchTests), nameof(ACappedMatch_TwoProcesses_EqualAtEveryCheckpoint), TwinVariable, TwinLine, 600));
        _out.WriteLine(mine);
    }
}
