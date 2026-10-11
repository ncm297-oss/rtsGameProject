using Rts.Sim.Abilities;
using Rts.Sim.Data;
using Rts.Sim.Entities;

namespace Rts.Sim;

// M4-5: stealth and detection (docs/03 "Implementation (M4-5)"). The predicate the vision rule asks, and the per-slot
// detection mask phase 5 keeps (Abilities/StealthSystem).
public sealed partial class World
{
    // Per unit slot, one bit per player (8 players a byte, _detectBytes bytes a slot): whether that player detects the unit
    // (StealthSystem). Derived from hashed state (positions, statuses, the fog's visible bits) by phase 5 and when a unit
    // first becomes stealthed; read only while the slot carries a stealthed entry. Not hashed.
    private readonly byte[] _detectedBy;
    private readonly int _detectBytes;

    /// <summary>Scratch for <see cref="StealthSystem.Run"/>: the finished enemy buildings with a detector, collected once a pass; derived, not hashed.</summary>
    internal int[] DetectorBuildings { get; }

    /// <summary>The status id of the <see cref="StatusKind.Revealed"/> status (at most one, the loader checks), or -1: what an attack or cast from stealth applies.</summary>
    internal int RevealedStatus { get; }

    /// <summary>The farthest any unit detects (m): <c>rules.json</c> <c>stealth.proximity</c> or the largest unit <c>detector</c>, the detection pass's query radius.</summary>
    internal float StealthQueryRadius { get; }

    /// <summary>
    /// Whether live unit slot <paramref name="slot"/> is stealthed now (M4-5, docs/02 "Stealth and detection"): it carries a
    /// Stealthed status and no Revealed one (Revealed overrides it; the Stealthed entry stays, dormant) and is not summoned
    /// (a summoned unit can't be stealthed). Its owner always sees it; an enemy player sees and targets it only while it
    /// detects it (<see cref="IsDetectedBy"/>). Derived from the hashed statuses; false for a dead or out-of-range slot.
    /// </summary>
    public bool IsStealthed(int slot)
    {
        UnitStore u = Units;
        if ((uint)slot >= (uint)u.Capacity || !u.Alive[slot] || !u.Statuses.HasStealth(slot) || u.IsSummoned(slot)) return false;
        return !HasRevealed(slot);
    }

    /// <summary>
    /// Whether <paramref name="player"/> detects live unit slot <paramref name="slot"/> (M4-5): the slot carries a Stealthed
    /// status (in force or dormant) and, at this tick's phase 5 (or the moment it became stealthed), its cell was visible to
    /// <paramref name="player"/> and one of the player's units stood within <c>stealth.proximity</c> of it or one of its
    /// detectors (a unit's or finished building's <c>detector</c>) within that radius. False for a unit with no stealth, the
    /// unit's own owner (who never needs to), a dead slot, or an unknown player. Derived, not hashed.
    /// </summary>
    public bool IsDetectedBy(int player, int slot)
    {
        UnitStore u = Units;
        if ((uint)player >= (uint)Config.PlayerCount || (uint)slot >= (uint)u.Capacity || !u.Alive[slot] || !u.Statuses.HasStealth(slot)) return false;
        return DetectedBit(player, slot);
    }

    /// <summary>
    /// The vision rule's stealth half (M4-5): whether live unit slot <paramref name="slot"/> is hidden from
    /// <paramref name="player"/> by stealth: an enemy's, stealthed (<see cref="IsStealthed"/>), and not detected by the
    /// player. Callers on the hot path test <see cref="StatusStore.StealthedUnits"/> first.
    /// </summary>
    internal bool HiddenFrom(int player, int slot)
    {
        UnitStore u = Units;
        return u.Statuses.StealthedUnits != 0 && u.Statuses.HasStealth(slot) && u.Owner[slot] != player && !u.IsSummoned(slot)
            && !DetectedBit(player, slot) && !HasRevealed(slot);
    }

    /// <summary>Clears every player's detection bit of unit slot <paramref name="slot"/>.</summary>
    internal void ClearDetected(int slot)
    {
        int head = slot * _detectBytes;
        for (int k = 0; k < _detectBytes; k++) _detectedBy[head + k] = 0;
    }

    /// <summary>Sets <paramref name="player"/>'s detection bit of unit slot <paramref name="slot"/>.</summary>
    internal void SetDetected(int player, int slot) => _detectedBy[slot * _detectBytes + (player >> 3)] |= (byte)(1 << (player & 7));

    private bool DetectedBit(int player, int slot) => (_detectedBy[slot * _detectBytes + (player >> 3)] & (1 << (player & 7))) != 0;

    /// <summary>Whether unit slot <paramref name="slot"/> carries a status of the <see cref="StatusKind.Revealed"/> kind.</summary>
    private bool HasRevealed(int slot)
    {
        StatusStore s = Units.Statuses;
        int head = slot * StatusStore.PerUnit;
        for (int k = 0; k < s.Count[slot]; k++)
            if (Data.Statuses[s.StatusId[head + k]].Kind == StatusKind.Revealed) return true;
        return false;
    }
}
