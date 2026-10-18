using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Rts.Sim.Data;
using Rts.Sim.Tests;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.ViewApi;

/// <summary>
/// M6-V13: <see cref="AiOpeningLookup.Pick"/> (the stats screen's "opened with" line on a watched replay, whose sim runs every
/// seat human) equals the opening the live sim's AI picked, <see cref="Simulation.AiOpening"/>, on a 3-opening fixture.
/// </summary>
public class AiOpeningLookupTests
{
    private readonly ITestOutputHelper _out;

    public AiOpeningLookupTests(ITestOutputHelper output) => _out = output;

    private static readonly string[] Factions = { "malazan", "whirlwind" };

    // Both factions' ai.json cut to exactly three openings: the first shipped one and two copies (opening_1, opening_2), so the
    // fixture holds whatever the data track ships (D21 edits both files this session).
    private static readonly Lazy<GameData> s_three = new(() => AiScenes.DataWith(dir =>
    {
        foreach (string f in Factions)
            dir.EditJson($"factions/{f}/ai.json", root =>
            {
                JsonArray openings = root["openings"]!.AsArray();
                JsonNode first = openings[0]!.DeepClone();
                openings.Clear();
                openings.Add(first);
                for (int k = 1; k < 3; k++)
                {
                    JsonNode copy = first.DeepClone();
                    copy["id"] = $"opening_{k}";
                    copy["displayName"] = $"Opening {k}";
                    openings.Add(copy);
                }
            });
    }));

    private static readonly AiDifficulty[] Difficulties = { AiDifficulty.Easy, AiDifficulty.Normal, AiDifficulty.Hard };

    [Fact]
    public void TheFixture_HoldsThreeOpeningsPerFaction()
    {
        foreach (string f in Factions) Assert.Equal(3, s_three.Value.Ai[s_three.Value.FindFaction(f)].Openings.Length);
    }

    /// <summary>Seeds 1-200 x 3 difficulty rotations x 2-4 seats, every seat an AI (factions alternating): Pick equals the sim's pick in every seat.</summary>
    [Fact]
    public void Pick_EqualsSimulationAiOpening_Seeds1To200_ThreeDifficulties_TwoToFourSeats()
    {
        GameData data = s_three.Value;
        var hits = new int[3, 3]; // difficulty x opening
        int checkedSeats = 0;
        for (int players = 2; players <= 4; players++)
            for (int rot = 0; rot < 3; rot++)
                for (ulong seed = 1; seed <= 200; seed++)
                {
                    var seats = new PlayerSeat[players];
                    for (int p = 0; p < players; p++)
                        seats[p] = new PlayerSeat(data.FindFaction(Factions[p % 2]), SeatController.Ai, Difficulties[(rot + p) % 3]);
                    var config = new SimConfig(seed, players, 16, 16) { Data = data, Seats = ImmutableArray.Create(seats) };
                    var sim = new Simulation(config, LocalMovementTests.Flat(32));
                    for (int p = 0; p < players; p++)
                    {
                        int count = data.Ai[seats[p].Faction].Openings.Length;
                        int want = sim.AiOpening(p);
                        Assert.InRange(want, 0, 2);
                        Assert.Equal(want, AiOpeningLookup.Pick(seed, p, seats[p].Difficulty, count));
                        hits[(int)seats[p].Difficulty, want]++;
                        checkedSeats++;
                    }
                }
        _out.WriteLine($"{checkedSeats} seats; picks per opening: Easy {hits[0, 0]}/{hits[0, 1]}/{hits[0, 2]}, Normal {hits[1, 0]}/{hits[1, 1]}/{hits[1, 2]}, Hard {hits[2, 0]}/{hits[2, 1]}/{hits[2, 2]}");
        Assert.Equal(0, hits[0, 1] + hits[0, 2]); // Easy always the first
        for (int d = 1; d < 3; d++)
            for (int k = 0; k < 3; k++) Assert.True(hits[d, k] > 0, $"difficulty {d} never picked opening {k}: the draw is not exercised");
    }

    [Fact]
    public void Pick_IsTheSameEveryCall_AndOneOpeningIsAlwaysTheFirst()
    {
        for (ulong seed = 1; seed <= 50; seed++)
            for (int p = 0; p < 4; p++)
            {
                Assert.Equal(AiOpeningLookup.Pick(seed, p, AiDifficulty.Hard, 3), AiOpeningLookup.Pick(seed, p, AiDifficulty.Hard, 3));
                Assert.Equal(0, AiOpeningLookup.Pick(seed, p, AiDifficulty.Normal, 1));
            }
    }

    /// <summary>Pick reads nothing of a sim and writes nothing: a sim's state hash and its AI's pick are the same after a thousand lookups.</summary>
    [Fact]
    public void Pick_MovesNoSimState()
    {
        GameData data = s_three.Value;
        var seats = ImmutableArray.Create(new PlayerSeat(0, SeatController.Ai, AiDifficulty.Hard), new PlayerSeat(1, SeatController.Ai, AiDifficulty.Normal));
        var sim = new Simulation(new SimConfig(9, 2, 16, 16) { Data = data, Seats = seats }, LocalMovementTests.Flat(32));
        ulong hash = sim.StateHash();
        int pick = sim.AiOpening(0);
        for (int k = 0; k < 1000; k++) AiOpeningLookup.Pick(9, k % 2, AiDifficulty.Hard, 3);
        Assert.Equal(hash, sim.StateHash());
        Assert.Equal(pick, sim.AiOpening(0));
    }
}
