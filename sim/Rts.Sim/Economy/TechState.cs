using System;
using System.Collections.Immutable;
using Rts.Sim.Data;
using Rts.Sim.Determinism;

namespace Rts.Sim.Economy;

/// <summary>
/// Per-player researched techs (M3-5): one flag per player and tech (hashed state), plus each player's summed bonus per
/// unit type and stat, recomputed when a flag changes (derived, not hashed), so <see cref="Bonus"/> is one array read.
/// </summary>
/// <remarks>
/// Which effects match which unit type is worked out once here, from the effects' load-time int filters, into a
/// (unit, stat, tech) table; nothing in a tick walks effects or allocates.
/// </remarks>
internal sealed class TechState
{
    private static readonly int StatCount = DataLimits.TechStatIds.Length;

    private readonly int _players;
    private readonly int _techs;
    private readonly int _units;
    private readonly ImmutableArray<int> _ageTechs;
    // Per player, WordsPerPlayer words of one bit per tech (tech t: word t / 64, bit t % 64).
    private readonly ulong[] _flags;
    // (unit x StatCount + stat) x techs + tech: what tech adds to that unit type's stat.
    private readonly float[] _table;
    // (player x units + unit) x StatCount + stat: the sum over the player's researched techs.
    private readonly float[] _bonus;

    /// <summary>No techs researched, for <paramref name="players"/> players of <paramref name="data"/>.</summary>
    public TechState(int players, GameData data)
    {
        _players = players;
        _techs = data.Techs.Length;
        _units = data.Units.Length;
        _ageTechs = data.AgeTechs;
        WordsPerPlayer = (_techs + 63) / 64;
        _flags = new ulong[players * WordsPerPlayer];
        _bonus = new float[players * _units * StatCount];
        _table = new float[_units * StatCount * _techs];
        for (int t = 0; t < _techs; t++)
        {
            foreach (TechEffect e in data.Techs[t].Effects)
            {
                for (int u = 0; u < _units; u++)
                    if (Matches(data, e, data.Units[u])) _table[(u * StatCount + (int)e.Stat) * _techs + t] += e.Amount;
            }
        }
    }

    /// <summary>Hashed words per player (0 with no techs in the data).</summary>
    public int WordsPerPlayer { get; }

    /// <summary>True if effect <paramref name="e"/> applies to unit type <paramref name="u"/>: every filter it sets matches.</summary>
    public static bool Matches(GameData data, in TechEffect e, UnitDef u)
    {
        if (e.AttackType >= 0 && u.Attack.DamageType != e.AttackType) return false;
        if (e.Siege >= 0 && (u.Slot == UnitSlot.Siege) != (e.Siege == 1)) return false;
        if (!e.Units.IsDefaultOrEmpty && ImmutableArray.BinarySearch(e.Units, u.Id) < 0) return false;
        if (!e.Tags.IsDefaultOrEmpty)
        {
            bool any = false;
            foreach (string tag in u.Tags)
            {
                int id = ImmutableArray.BinarySearch(data.UnitTags, tag, StringComparer.Ordinal);
                any |= id >= 0 && ImmutableArray.BinarySearch(e.Tags, id) >= 0;
            }
            if (!any) return false;
        }
        return true;
    }

    /// <summary>True for a valid (player, tech) pair.</summary>
    private bool Valid(int player, int tech) => (uint)player < (uint)_players && (uint)tech < (uint)_techs;

    /// <summary>Whether <paramref name="player"/> has researched <paramref name="tech"/>; false for out-of-range ids.</summary>
    public bool Has(int player, int tech) =>
        Valid(player, tech) && (_flags[player * WordsPerPlayer + (tech >> 6)] & (1UL << (tech & 63))) != 0;

    /// <summary>Sets or clears a flag (research completing; tests) and recomputes the player's bonuses. Nothing for out-of-range ids.</summary>
    public void Set(int player, int tech, bool value)
    {
        if (!Valid(player, tech) || Has(player, tech) == value) return;
        _flags[player * WordsPerPlayer + (tech >> 6)] ^= 1UL << (tech & 63);
        for (int u = 0; u < _units; u++)
        {
            for (int s = 0; s < StatCount; s++)
            {
                float sum = 0f;
                int row = (u * StatCount + s) * _techs;
                for (int t = 0; t < _techs; t++)
                    if (Has(player, t)) sum += _table[row + t];
                _bonus[(player * _units + u) * StatCount + s] = sum;
            }
        }
    }

    /// <summary>True if <paramref name="player"/> has researched anything.</summary>
    public bool Any(int player)
    {
        if ((uint)player >= (uint)_players) return false;
        for (int w = 0; w < WordsPerPlayer; w++)
            if (_flags[player * WordsPerPlayer + w] != 0) return true;
        return false;
    }

    /// <summary>1 plus the number of age techs researched in order (Age I at start); 0 for an unknown player.</summary>
    public int Age(int player)
    {
        if ((uint)player >= (uint)_players) return 0;
        int age = 1;
        foreach (int t in _ageTechs)
        {
            if (!Has(player, t)) break;
            age++;
        }
        return age;
    }

    /// <summary>The sum of the player's researched effects on <paramref name="stat"/> of <paramref name="unitType"/>; 0 for out-of-range ids.</summary>
    public float Bonus(int player, int unitType, TechStat stat)
    {
        if ((uint)player >= (uint)_players || (uint)unitType >= (uint)_units || (uint)stat >= (uint)StatCount) return 0f;
        return _bonus[(player * _units + unitType) * StatCount + (int)stat];
    }

    /// <summary>Mixes <paramref name="player"/>'s flag words into a state hash.</summary>
    public void AddToHash(ref StateHasher h, int player)
    {
        for (int w = 0; w < WordsPerPlayer; w++)
            h.Add(_flags[player * WordsPerPlayer + w]);
    }
}
