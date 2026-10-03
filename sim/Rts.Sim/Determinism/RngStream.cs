namespace Rts.Sim.Determinism;

/// <summary>Stream ids for the world's RNGs; each system draws only from its own stream.</summary>
/// <remarks>Separate streams mean a new random call in one system never shifts another's sequence.</remarks>
public static class RngStream
{
    /// <summary>Map generation.</summary>
    public const int MapGen = 0;

    /// <summary>Combat rolls (projectile hit/miss and similar).</summary>
    public const int Combat = 1;

    private const int FirstAi = 2;

    /// <summary>Stream id of the given player's AI.</summary>
    public static int Ai(int player) => FirstAi + player;

    /// <summary>Total number of streams for a match with this many players.</summary>
    public static int Count(int playerCount) => FirstAi + playerCount;
}
