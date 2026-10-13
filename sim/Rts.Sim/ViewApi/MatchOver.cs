using System;

namespace Rts.Sim.ViewApi;

/// <summary>
/// The view's dev "match over" watch (M6-V2, docs/02 "Victory/defeat": a player with no buildings left is defeated): after each
/// tick it counts every player's live buildings (construction sites included) in <c>World.Buildings</c> and reports the players
/// whose count went from above 0 to 0 since the last look. A player that never had a building (0 at tick 0) is never reported;
/// one that builds again and loses it all again is reported again. Not the sim's rule (that is Requests 37,
/// <c>World.IsDefeated</c>): a read-only stand-in for the dev line until it exists. Allocation-free after construction.
/// </summary>
public sealed class MatchOver
{
    private readonly int[] _count;
    private readonly bool[] _had;
    private readonly bool[] _emptied;

    /// <summary>Creates a watch for players 0 to <paramref name="playerCount"/> - 1, none of them holding a building yet.</summary>
    public MatchOver(int playerCount)
    {
        if (playerCount < 1) throw new ArgumentOutOfRangeException(nameof(playerCount));
        _count = new int[playerCount];
        _had = new bool[playerCount];
        _emptied = new bool[playerCount];
    }

    /// <summary>Players watched.</summary>
    public int PlayerCount => _count.Length;

    /// <summary>Player <paramref name="player"/>'s live buildings at the last <see cref="Observe"/>; 0 for a player out of range.</summary>
    public int BuildingCount(int player) => (uint)player < (uint)_count.Length ? _count[player] : 0;

    /// <summary>Whether player <paramref name="player"/>'s building count went from above 0 to 0 at the last <see cref="Observe"/>.</summary>
    public bool JustEmptied(int player) => (uint)player < (uint)_emptied.Length && _emptied[player];

    /// <summary>
    /// Counts live buildings per owner over a building store's spans (<c>World.Buildings.Alive</c> / <c>Owner</c>: slot i alive,
    /// its owner; owners out of range are ignored); returns how many players just lost their last one (<see cref="JustEmptied"/>).
    /// </summary>
    public int Observe(ReadOnlySpan<bool> alive, ReadOnlySpan<int> owner)
    {
        Array.Clear(_count);
        int n = Math.Min(alive.Length, owner.Length);
        for (int i = 0; i < n; i++)
            if (alive[i] && (uint)owner[i] < (uint)_count.Length) _count[owner[i]]++;
        int emptied = 0;
        for (int p = 0; p < _count.Length; p++)
        {
            bool has = _count[p] > 0;
            _emptied[p] = _had[p] && !has;
            if (_emptied[p]) emptied++;
            _had[p] = has;
        }
        return emptied;
    }
}
