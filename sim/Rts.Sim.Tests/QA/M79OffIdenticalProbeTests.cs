using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA (M7-9, Request 59), a probe for "off is identical": three shipped-data AI matches (one per shipped map, two with a
/// Teblor seat, every difficulty on some seat), each tick's state hash folded and a checkpoint every 1,000 ticks. The row
/// asserts only that the matches ran; its lines are the evidence, compared by hand (or by a script) against the same file
/// run on an earlier commit. It reads no API newer than M7-8, so the file can be dropped into a clone of an older commit.
/// With <c>M79_HASH_OUT</c> set the lines also go to that file.
/// </summary>
[Trait("Category", "Stress")]
public class M79OffIdenticalProbeTests
{
    private const int Ticks = 12_000;

    private readonly ITestOutputHelper _out;

    public M79OffIdenticalProbeTests(ITestOutputHelper output) => _out = output;

    private static readonly (string Map, ulong Seed, (string Faction, AiDifficulty Difficulty)[] Seats)[] Matches =
    {
        ("pale_hills", 3, new[] { ("teblor", AiDifficulty.Hard), ("malazan", AiDifficulty.Easy) }),
        ("raraku", 5, new[] { ("malazan", AiDifficulty.Normal), ("whirlwind", AiDifficulty.Hard) }),
        ("vathar_crossing", 7, new[] { ("whirlwind", AiDifficulty.Easy), ("teblor", AiDifficulty.Normal) }),
    };

    [Fact]
    public void ThreeShippedDataMatches_PerTickHashes()
    {
        GameData data = TestSim.Data;
        var lines = new List<string> { $"data {data.ContentHash():X16}" };
        foreach ((string map, ulong seed, (string Faction, AiDifficulty Difficulty)[] seatSpec) in Matches)
        {
            var seats = new PlayerSeat[seatSpec.Length];
            for (int p = 0; p < seats.Length; p++)
                seats[p] = new PlayerSeat(data.FindFaction(seatSpec[p].Faction), SeatController.Ai, seatSpec[p].Difficulty);
            Simulation sim = TeblorSwitchOrders.Match(data, map, seats, seed);
            var fold = new StateHasher();
            var checkpoints = new List<string>();
            while (sim.TickNumber < Ticks)
            {
                sim.Tick();
                ulong h = sim.StateHash();
                fold.Add(h);
                if (sim.TickNumber % 1000 == 0) checkpoints.Add($"{sim.TickNumber}:{h:X16}");
            }
            lines.Add($"{map} seed {seed}: fold {fold.Value:X16} | {string.Join(" ", checkpoints)}");
        }
        foreach (string l in lines) _out.WriteLine(l);
        string? path = Environment.GetEnvironmentVariable("M79_HASH_OUT");
        if (!string.IsNullOrEmpty(path)) File.WriteAllLines(path, lines);
        Assert.Equal(Matches.Length + 1, lines.Count);
    }
}
