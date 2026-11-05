using System;

namespace Rts.Sim.Vision;

// M6-S5: shared vision between allies (docs/02 "Vision and fog of war", docs/03 "Implementation (M6-S5)"). Each player keeps
// its own fog exactly as before (stamped from its own units and buildings, hashed, saved); after every update the allies'
// own bits are OR-ed into one team map, and every reader of what a player sees reads the team's. The team maps are derived
// from the saved own bits: rebuilt after a load, never saved, never hashed. A player without an ally reads its own arrays
// (the team arrays are the same references), so a free-for-all runs no extra pass and reads what it read before.
public sealed partial class FogStore
{
    // Per player its team: the players allied with it, ascending, itself included ([p] without an ally). One array a team,
    // shared by its members.
    private readonly int[][] _mates;

    // Per player its team's visible / explored bits: one array a team, shared by its members; the player's own arrays when
    // it has no ally. Derived (MergeTeams).
    private readonly ulong[][] _teamVisible;
    private readonly ulong[][] _teamExplored;

    // Per player the union of its team's last stamped boxes (x0 int.MaxValue: empty): every cell the team sees is inside.
    private readonly int[] _teamBox;

    // Per player its team's byte map for the view, built on request (TeamVisibility) from the team bits: one array a team;
    // the own map without an ally. Per player the fog version its team's map was built at (-1: never).
    private readonly byte[][] _teamVisibility;
    private readonly int[] _teamVisibilityVersion;

    /// <summary>Whether any player has an ally (M6-S5): only then does an update merge the teams' fog.</summary>
    internal bool Sharing { get; }

    /// <summary>
    /// Fills the per-team tables from <paramref name="world"/>'s alliances (teams are equivalence classes: allied with one
    /// member is allied with all). Returns whether any team holds two players. Construction only.
    /// </summary>
    private bool BuildTeams(World world)
    {
        bool any = false;
        int cells = _levels.Length;
        for (int p = 0; p < _players; p++)
        {
            _teamVisibilityVersion[p] = -1;
            _teamBox[4 * p] = int.MaxValue;
            if (_mates[p] != null) continue;
            int count = 0;
            for (int q = 0; q < _players; q++)
                if (world.AreAllied(p, q)) count++;
            var team = new int[count];
            count = 0;
            for (int q = 0; q < _players; q++)
                if (world.AreAllied(p, q)) team[count++] = q;
            bool alone = team.Length == 1;
            any |= !alone;
            ulong[] visible = alone ? _visible[p] : new ulong[(cells + 63) / 64];
            ulong[] explored = alone ? _explored[p] : new ulong[(cells + 63) / 64];
            byte[] bytes = alone ? _visibility[p] : new byte[cells];
            foreach (int q in team)
            {
                _mates[q] = team;
                _teamVisible[q] = visible;
                _teamExplored[q] = explored;
                _teamVisibility[q] = bytes;
            }
        }
        return any;
    }

    /// <summary>
    /// Every team's union (allocation-free): its members' own visible and explored bits OR-ed word by word into the team
    /// arrays, and their stamped boxes joined; a player alone keeps its own arrays and copies its box. Run after every
    /// update's own passes, after a load and after the explore seam; only while <see cref="Sharing"/>.
    /// </summary>
    private void MergeTeams()
    {
        for (int p = 0; p < _players; p++)
        {
            int[] team = _mates[p];
            int b = 4 * p;
            if (team.Length == 1)
            {
                Array.Copy(_visibleBox, b, _teamBox, b, 4);
                continue;
            }
            int lead = team[0];
            if (lead != p)
            {
                Array.Copy(_teamBox, 4 * lead, _teamBox, b, 4); // the lead (lowest member) merged the team first
                continue;
            }
            ulong[] visible = _teamVisible[p], explored = _teamExplored[p];
            Array.Copy(_visible[lead], visible, visible.Length);
            Array.Copy(_explored[lead], explored, explored.Length);
            Array.Copy(_visibleBox, 4 * lead, _teamBox, b, 4);
            for (int m = 1; m < team.Length; m++)
            {
                int q = team[m];
                ulong[] v = _visible[q], e = _explored[q];
                for (int k = 0; k < visible.Length; k++)
                {
                    visible[k] |= v[k];
                    explored[k] |= e[k];
                }
                int qb = 4 * q;
                if (_visibleBox[qb] == int.MaxValue) continue; // nothing stamped
                if (_teamBox[b] == int.MaxValue)
                {
                    Array.Copy(_visibleBox, qb, _teamBox, b, 4);
                    continue;
                }
                _teamBox[b] = Math.Min(_teamBox[b], _visibleBox[qb]);
                _teamBox[b + 1] = Math.Min(_teamBox[b + 1], _visibleBox[qb + 1]);
                _teamBox[b + 2] = Math.Max(_teamBox[b + 2], _visibleBox[qb + 2]);
                _teamBox[b + 3] = Math.Max(_teamBox[b + 3], _visibleBox[qb + 3]);
            }
        }
    }

    /// <summary>
    /// Player <paramref name="player"/>'s (who has an ally) team byte map, rebuilt from the team bits when its fog version
    /// moved since the last build: visible where an ally sees the cell, explored where one has seen it, else unexplored.
    /// Derived, view-only (no rule reads the byte map), so it is built only when asked for.
    /// </summary>
    private byte[] TeamVisibility(int player)
    {
        int lead = _mates[player][0];
        byte[] bytes = _teamVisibility[lead];
        if (_teamVisibilityVersion[lead] == _version[lead]) return bytes;
        _teamVisibilityVersion[lead] = _version[lead];
        ulong[] visible = _teamVisible[lead], explored = _teamExplored[lead];
        int cells = bytes.Length;
        for (int k = 0; k < explored.Length; k++)
        {
            int c0 = k << 6, end = Math.Min(64, cells - c0);
            ulong e = explored[k] | visible[k], v = visible[k];
            if (e == 0)
            {
                Array.Clear(bytes, c0, end);
                continue;
            }
            for (int i = 0; i < end; i++)
                bytes[c0 + i] = (v >> i & 1) != 0 ? VisionConstants.Visible : (e >> i & 1) != 0 ? VisionConstants.Explored : VisionConstants.Unexplored;
        }
        return bytes;
    }

    /// <summary>
    /// After a load (<see cref="ReadFrom"/>): the team maps rebuilt from the loaded own bits, the team byte maps marked stale.
    /// </summary>
    private void RebuildTeamsAfterLoad()
    {
        for (int p = 0; p < _players; p++) _teamVisibilityVersion[p] = -1;
        if (Sharing) MergeTeams();
    }

    /// <summary>Player <paramref name="player"/>'s own fog byte map (its own eyes, before the team's union): what the oracle tests build the union from.</summary>
    internal ReadOnlySpan<byte> OwnVisibility(int player) => _visibility[player];

    /// <summary>
    /// <see cref="SeesUnitCell"/> by <paramref name="player"/>'s own eyes alone (its own visible bits, its own zone records):
    /// the rule each ally answers before the team's union, for the oracle tests.
    /// </summary>
    internal bool SeesUnitCellOwn(int player, int slot) =>
        _world.Zones.BlockerCount > 0 ? SeesUnitCellNearZonesOwn(player, slot) : Bit(_visible[player], CellOf(_units.Position[slot]));

    /// <summary>Player <paramref name="player"/>'s team, ascending, itself included (M6-S5).</summary>
    internal ReadOnlySpan<int> TeamOf(int player) => _mates[player];
}
