using System;
using Rts.Sim.Data;
using Rts.Sim.Entities;

namespace Rts.Sim.Economy;

// M8-4 (Request 56 G4, docs/03 "Implementation (M8-4)"): the count a unit type's maxAlive is judged against, kept where the
// stores already settle with the ledger (a unit counted live at its spawn and released at its free; a unit item joining or
// leaving a queue), so the train check reads one number instead of walking a store. Kept only for types with a limit and
// only when the data has one. Derived from the stores: not hashed, not saved; a load rebuilds it (RecountLimits, from
// RecountDraw).
internal sealed partial class PlayerLedger
{
    private int _unitTypes;
    // Per unit type: its maxAlive (0 = no limit) and spawnCount.
    private int[] _maxAlive = Array.Empty<int>();
    private int[] _spawnCount = Array.Empty<int>();
    // player x unit types + type: live units of the type plus queued items x spawnCount. Empty when no type has a limit.
    private int[] _limited = Array.Empty<int>();

    private void InitLimits(int players, GameData data)
    {
        _unitTypes = data.Units.Length;
        bool any = false;
        foreach (UnitDef u in data.Units) any |= u.MaxAlive > 0;
        if (!any) return;
        _maxAlive = new int[_unitTypes];
        _spawnCount = new int[_unitTypes];
        for (int t = 0; t < _unitTypes; t++)
        {
            _maxAlive[t] = data.Units[t].MaxAlive;
            _spawnCount[t] = data.Units[t].SpawnCount;
        }
        _limited = new int[players * _unitTypes];
    }

    /// <summary>
    /// M8-4: <paramref name="player"/>'s live units of <paramref name="unitType"/> plus the units its queued items of the type will
    /// spawn; 0 for a type without a limit or out-of-range ids. A kept count: one read.
    /// </summary>
    public int LimitedCount(int player, int unitType) =>
        _limited.Length != 0 && Has(player) && (uint)unitType < (uint)_unitTypes ? _limited[player * _unitTypes + unitType] : 0;

    /// <summary>Adds <paramref name="delta"/> units to <paramref name="player"/>'s count of <paramref name="unitType"/> when the type has a limit.</summary>
    private void AddLimited(int player, int unitType, int delta)
    {
        if (_limited.Length == 0 || !Has(player) || (uint)unitType >= (uint)_unitTypes || _maxAlive[unitType] == 0) return;
        _limited[player * _unitTypes + unitType] += delta;
    }

    /// <summary>
    /// Rebuilds the limit counts from the stores (a load, a test that writes a store's arrays directly), counting what the
    /// stores' own paths count: a live unit of a tracked player, a unit item in a live building's queue (x its spawnCount).
    /// </summary>
    public void RecountLimits(UnitStore units, BuildingStore buildings)
    {
        if (_limited.Length == 0) return;
        Array.Clear(_limited);
        for (int i = 0; i < units.Capacity; i++)
            if (units.Alive[i]) AddLimited(units.Owner[i], units.TypeId[i], 1);
        for (int k = 0; k < buildings.Capacity; k++)
        {
            if (!buildings.Alive[k]) continue;
            for (int q = 0; q < buildings.QueueCount[k]; q++)
            {
                if (buildings.QueueIsTechAt(k, q)) continue;
                int type = buildings.QueueTypeAt(k, q);
                if ((uint)type < (uint)_unitTypes) AddLimited(buildings.Owner[k], type, _spawnCount[type]);
            }
        }
    }
}
