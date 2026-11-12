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

    /// <summary>
    /// The two seats a per-faction run uses (D28): <see cref="AiScenes.Seats"/>' (player p plays faction p mod 2) with
    /// <paramref name="faction"/>'s seat, player (faction id mod 2), playing that faction. For the two shipped factions these
    /// are exactly the default seats, so their runs and pins are unchanged; a third faction (teblor, id 2) takes player 0
    /// against the Whirlwind. <paramref name="own"/> and <paramref name="other"/> are the two seats' controllers.
    /// </summary>
    public static (PlayerSeat[] Seats, int Player) SeatsFor(GameData data, string faction, AiDifficulty difficulty, SeatController own, SeatController other)
    {
        int f = data.FindFaction(faction);
        Assert.True(f >= 0, $"'{faction}' is not a faction of the data");
        int player = f % 2;
        var controllers = new SeatController[2];
        controllers[player] = own;
        controllers[1 - player] = other;
        PlayerSeat[] seats = AiScenes.Seats(difficulty, controllers);
        seats[player] = seats[player] with { Faction = f };
        return (seats, player);
    }

    /// <summary>
    /// The opening <paramref name="faction"/>'s AI picks on <paramref name="seed"/> at <paramref name="difficulty"/>, seated as
    /// <see cref="SeatsFor"/> seats it, both seats AI (D-R36; D28: the other seat is the default seat's faction).
    /// </summary>
    public static int PickFor(ulong seed, string faction, AiDifficulty difficulty)
    {
        (PlayerSeat[] seats, int player) = SeatsFor(TestSim.Data, faction, difficulty, SeatController.Ai, SeatController.Ai);
        Simulation sim = AiScenes.Flat(seats, seed: seed, workers: 0);
        return sim.AiOpening(player);
    }

    /// <summary>The smallest seed in 1-<see cref="MaxReportSeed"/> whose <paramref name="difficulty"/> pick for <paramref name="faction"/>'s seat is <paramref name="opening"/>.</summary>
    public static ulong Smallest(string faction, int opening, AiDifficulty difficulty)
    {
        for (ulong seed = 1; seed <= MaxReportSeed; seed++)
            if (PickFor(seed, faction, difficulty) == opening) return seed;
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
