using System;
using Rts.Sim.Data;
using Rts.Sim.Entities;

namespace Rts.Sim.Economy;

// M6-S13: the counts the draw rule reads every tick (World.Draw.cs), kept where the stores already settle with the ledger, so
// the per-tick check reads a few numbers per seat instead of walking a store. All derived from the stores: not hashed, not
// saved (the ledger's save part is unchanged); a load rebuilds them (RecountDraw).
internal sealed partial class PlayerLedger
{
    // Per unit type, whether it is a worker (UnitDef.IsWorker): what LiveWorkers counts.
    private bool[] _isWorkerType = Array.Empty<bool>();

    /// <summary>M6-S13: each player's live workers (<see cref="UnitDef.IsWorker"/>), summoned ones included, counted with <see cref="LiveUnits"/>. Derived; not hashed or saved.</summary>
    public int[] LiveWorkers { get; private set; } = Array.Empty<int>();

    /// <summary>M6-S13: each player's unit items in production queues (a research item is not one), started or waiting. Derived; not hashed or saved.</summary>
    public int[] QueuedUnits { get; private set; } = Array.Empty<int>();

    /// <summary>M6-S13: the gold each player's live units carry (cargo of kind gold). Derived; not hashed or saved.</summary>
    public long[] CarriedGold { get; private set; } = Array.Empty<long>();

    private void InitDrawCounts(int players, GameData data)
    {
        _isWorkerType = new bool[data.Units.Length];
        for (int t = 0; t < _isWorkerType.Length; t++) _isWorkerType[t] = data.Units[t].IsWorker;
        LiveWorkers = new int[players];
        QueuedUnits = new int[players];
        CarriedGold = new long[players];
    }

    /// <summary>
    /// A unit item of <paramref name="unitType"/> joins (+1) or leaves (-1: trained, cancelled, or its building freed) one of
    /// <paramref name="player"/>'s queues; M8-4: a limited type's count moves by the item's spawnCount.
    /// </summary>
    public void AddQueuedUnit(int player, int unitType, int delta)
    {
        if (!Has(player)) return;
        QueuedUnits[player] += delta;
        if (_limited.Length != 0 && (uint)unitType < (uint)_unitTypes) AddLimited(player, unitType, delta * _spawnCount[unitType]);
    }

    /// <summary>Gold enters (positive: taken from a node) or leaves (negative: delivered, dropped, or its carrier freed) <paramref name="player"/>'s units' cargo.</summary>
    public void AddCarriedGold(int player, int delta)
    {
        if (Has(player)) CarriedGold[player] += delta;
    }

    /// <summary>
    /// Rebuilds <see cref="LiveWorkers"/>, <see cref="QueuedUnits"/> and <see cref="CarriedGold"/> from the stores (a load, after
    /// every store is read; a test that writes a store's arrays directly). Counts only what the stores' own paths count: a
    /// live unit of a player this ledger tracks, a unit item in a live building's queue.
    /// </summary>
    public void RecountDraw(UnitStore units, BuildingStore buildings)
    {
        Array.Clear(LiveWorkers);
        Array.Clear(QueuedUnits);
        Array.Clear(CarriedGold);
        for (int i = 0; i < units.Capacity; i++)
        {
            if (!units.Alive[i] || !Has(units.Owner[i])) continue;
            int owner = units.Owner[i], type = units.TypeId[i];
            if ((uint)type < (uint)_isWorkerType.Length && _isWorkerType[type]) LiveWorkers[owner]++;
            if (units.CargoKind[i] == ResourceKind.Gold) CarriedGold[owner] += units.Cargo[i];
        }
        for (int k = 0; k < buildings.Capacity; k++)
        {
            if (!buildings.Alive[k] || !Has(buildings.Owner[k])) continue;
            for (int q = 0; q < buildings.QueueCount[k]; q++)
                if (!buildings.QueueIsTechAt(k, q)) QueuedUnits[buildings.Owner[k]]++;
        }
        RecountLimits(units, buildings); // M8-4: the limit counts are derived from the same stores
    }
}
