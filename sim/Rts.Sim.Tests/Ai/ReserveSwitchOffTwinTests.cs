using Rts.Sim.Data;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M7-8 criterion 7, the kept half: the shipped data with <c>"throughReserve": false</c> / <c>"noReserve": false</c> written on
/// every step that may carry one (<see cref="ReserveSwitchLoaderTests.WriteFalseSwitches"/>) has the shipped content hash
/// and plays the same state hash every 500 ticks as the shipped data: a three-faction four-seat AI free-for-all on Vathar
/// Crossing (the Teblor seated) and a Hard Malazan v Normal Whirlwind match on Pale Hills, 6,000 ticks each. The base
/// comparison (three matches x 30,000 ticks against 9eb1646) is in the M7-8 report, read from
/// <c>Stress/ShippedDigestReportTests</c>.
/// </summary>
public class ReserveSwitchOffTwinTests
{
    private const int Bound = 6_000, Every = 500;

    private readonly ITestOutputHelper _out;

    public ReserveSwitchOffTwinTests(ITestOutputHelper output) => _out = output;

    private static readonly Lazy<GameData> s_false = new(() => AiScenes.DataWith(dir => ReserveSwitchLoaderTests.WriteFalseSwitches(dir)));

    [Theory]
    [InlineData("vathar_crossing", 1UL, 4)]
    [InlineData("pale_hills", 3UL, 2)]
    public void TheShippedDataWithSwitchesWrittenFalse_PlaysTheSameDigests(string map, ulong seed, int players)
    {
        GameData off = s_false.Value;
        Assert.Equal(TestSim.Data.ContentHash(), off.ContentHash());
        var seats = new PlayerSeat[players];
        for (int p = 0; p < players; p++)
            seats[p] = new PlayerSeat(players > 2 ? p % TestSim.Data.Factions.Length : p % 2, SeatController.Ai, players == 2 && p == 0 ? AiDifficulty.Hard : AiDifficulty.Normal);
        Simulation shipped = TeblorSwitchOrders.Match(TestSim.Data, map, seats, seed), copy = TeblorSwitchOrders.Match(off, map, seats, seed);
        int checks = 0;
        while (shipped.TickNumber < Bound)
        {
            shipped.Tick();
            copy.Tick();
            if (shipped.TickNumber % Every != 0) continue;
            Assert.True(shipped.StateHash() == copy.StateHash(), $"{map} seed {seed}: differs at tick {shipped.TickNumber}");
            checks++;
        }
        _out.WriteLine($"{map} seed {seed}, {players} seats: {checks} checks equal, end hash {shipped.StateHash():X16}");
        Assert.Equal(Bound / Every, checks);
    }
}
