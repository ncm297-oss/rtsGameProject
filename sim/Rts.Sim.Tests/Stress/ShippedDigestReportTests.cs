using Rts.Sim.Data;
using Rts.Sim.Determinism;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// M7-8 criterion 7, the base comparison (a measurement: it prints, the comparison is made by running the same file on the
/// base tree): three AI matches on the shipped data, 30,000 ticks each, the state hash every 500 ticks, printed as a list
/// and a fold. A four-seat free-for-all on Vathar Crossing (factions 0, 1, 2, 0), a 2v2 there, and Hard v Normal on Pale
/// Hills. The same digests on the branch and the base say the switches, absent from the shipped data, change no match.
/// </summary>
[Trait("Category", "Stress")]
public class ShippedDigestReportTests(ITestOutputHelper output)
{
    private const int Bound = 30_000, Every = 500;

    [Theory]
    [InlineData("ffa")]
    [InlineData("2v2")]
    [InlineData("hard")]
    public void ShippedAiMatch_Digests(string which)
    {
        int players = which == "hard" ? 2 : 4;
        var seats = new PlayerSeat[players];
        for (int p = 0; p < players; p++)
        {
            int faction = which == "ffa" ? p % 3 : p % 2;
            int team = which == "2v2" ? 1 + p % 2 : 0;
            seats[p] = new PlayerSeat(faction, SeatController.Ai, which == "hard" && p == 0 ? AiDifficulty.Hard : AiDifficulty.Normal, team);
        }
        string map = which == "hard" ? "pale_hills" : "vathar_crossing";
        ulong seed = which == "hard" ? 3UL : 1UL;
        Simulation sim = HuntStressTests.Match(map, seats, seed, units: players > 2 ? 1024 : 400);
        var hashes = new List<string>();
        var fold = new StateHasher();
        while (sim.TickNumber < Bound)
        {
            sim.Tick();
            if (sim.TickNumber % Every != 0) continue;
            ulong h = sim.StateHash();
            fold.Add(h);
            hashes.Add($"{h:X16}");
        }
        output.WriteLine($"DIGEST | {which} | {map} seed {seed} | data {TestSim.Data.ContentHash():X16} | over {sim.MatchOver} at {sim.TickNumber} | fold {fold.Value:X16}");
        output.WriteLine(string.Join(" ", hashes));
        Assert.Equal(Bound / Every, hashes.Count);
    }
}
