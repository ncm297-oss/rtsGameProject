namespace Rts.Sim;

// M6-S4: teams (docs/02 "Game flow" 2 and 4, docs/03 "Implementation (M6-S4)"). The one alliance predicate every "is this
// an enemy" site asks. Built once from the seats at construction; config, not state: not hashed, not in a save's body (the
// save and replay headers carry the seats' teams).
public sealed partial class World
{
    // Per player its seat's team (0: no team). Never written after construction.
    private readonly int[] _teamOf;

    // Flat players x players table: _allied[a * players + b] is AreAllied(a, b). Shared by reference with the spatial hash.
    private readonly bool[] _allied;

    /// <summary>Whether any two players are allies (a named team holds two seats); false for a free-for-all.</summary>
    internal bool HasAlliances { get; }

    /// <summary>Player <paramref name="player"/>'s team: 0 for no team (its own side), 1 to <see cref="PlayerSeat.MaxTeam"/> a named team; 0 for no such player.</summary>
    public int TeamOf(int player) => (uint)player < (uint)_teamOf.Length ? _teamOf[player] : 0;

    /// <summary>
    /// Whether players <paramref name="a"/> and <paramref name="b"/> are on one side: the same player, or both on the same
    /// named team (team 0 is no team, so two team-0 players are enemies). An unknown player is allied only with itself.
    /// </summary>
    public bool AreAllied(int a, int b)
    {
        if (a == b) return true;
        int n = _teamOf.Length;
        return (uint)a < (uint)n && (uint)b < (uint)n && _allied[a * n + b];
    }

    /// <summary>Whether <paramref name="a"/> and <paramref name="b"/> are enemies: the negation of <see cref="AreAllied"/>.</summary>
    public bool IsEnemy(int a, int b) => !AreAllied(a, b);

    /// <summary>Whether <paramref name="player"/> has an ally: another player on its named team.</summary>
    public bool HasAlly(int player)
    {
        if (TeamOf(player) == 0) return false;
        for (int q = 0; q < _teamOf.Length; q++)
            if (q != player && _teamOf[q] == _teamOf[player]) return true;
        return false;
    }

    /// <summary>The seats' teams and the alliance table of <paramref name="config"/>; true in <paramref name="any"/> when two players are allies.</summary>
    private static bool[] AllianceTable(SimConfig config, int[] teamOf, out bool any)
    {
        int n = config.PlayerCount;
        for (int p = 0; p < n; p++) teamOf[p] = config.SeatOf(p).Team;
        var allied = new bool[n * n];
        any = false;
        for (int a = 0; a < n; a++)
        {
            for (int b = 0; b < n; b++)
            {
                bool same = a == b || (teamOf[a] != 0 && teamOf[a] == teamOf[b]);
                allied[a * n + b] = same;
                any |= same && a != b;
            }
        }
        return allied;
    }
}
