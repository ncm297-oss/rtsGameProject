using Rts.Sim.Ai;
using Rts.Sim.Data;

namespace Rts.Sim.Tests.Content;

/// <summary>
/// D21: which seed makes an AI seat play which opening. A Normal or Hard seat picks by seed (docs/03 "Implementation
/// (M5-8a)"), so every per-opening report names the seed it ran and reads the pick from the sim itself
/// (<see cref="Simulation.AiOpening"/>), never from a re-derived draw. A seat's pick depends only on the seed, the
/// player index and its difficulty, so the flat scene (cheap to build) answers for the menu map too; the reports assert
/// the pick again on the match they run.
/// </summary>
internal static class AiOpeningSeeds
{
    /// <summary>The seeds the per-opening reports scan, smallest first (the brief's 1-50).</summary>
    public const int MaxReportSeed = 50;

    /// <summary>The opening player <paramref name="player"/>'s AI picks on <paramref name="seed"/> at <paramref name="difficulty"/>, both seats AI.</summary>
    public static int Pick(ulong seed, int player, AiDifficulty difficulty)
    {
        SeatController[] seats = { SeatController.Ai, SeatController.Ai };
        Simulation sim = AiScenes.Flat(AiScenes.Seats(difficulty, seats), seed: seed, workers: 0);
        return sim.AiOpening(player);
    }

    /// <summary>The smallest seed in 1-<see cref="MaxReportSeed"/> whose <paramref name="difficulty"/> pick for <paramref name="faction"/>'s seat is <paramref name="opening"/>.</summary>
    public static ulong Smallest(string faction, int opening, AiDifficulty difficulty)
    {
        int player = TestSim.Data.FindFaction(faction); // AiScenes.Seats: player p plays faction p mod 2
        for (ulong seed = 1; seed <= MaxReportSeed; seed++)
            if (Pick(seed, player, difficulty) == opening) return seed;
        Assert.Fail($"{faction}: no seed in 1-{MaxReportSeed} picks opening {opening} on {difficulty}");
        return 0;
    }

    /// <summary>The smallest seed in 1-<paramref name="max"/> on which the Malazan seat (player 0) picks <paramref name="malazan"/> and the Whirlwind seat (player 1) <paramref name="whirlwind"/>, both Normal.</summary>
    public static ulong SmallestPair(int malazan, int whirlwind, int max = 500)
    {
        for (ulong seed = 1; seed <= (ulong)max; seed++)
            if (Pick(seed, 0, AiDifficulty.Normal) == malazan && Pick(seed, 1, AiDifficulty.Normal) == whirlwind) return seed;
        Assert.Fail($"no seed in 1-{max} pairs malazan opening {malazan} with whirlwind opening {whirlwind}");
        return 0;
    }

    /// <summary>"standard (Combined arms)": an opening as the reports print it.</summary>
    public static string Name(AiOpening o) => $"{o.Id} ({o.DisplayName})";
}
