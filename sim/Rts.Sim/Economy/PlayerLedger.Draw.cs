using System;
using Rts.Sim.Data;
using Rts.Sim.Entities;

namespace Rts.Sim.Economy;

// M6-S13: the counts the draw rule reads every tick (World.Draw.cs), kept where the stores already settle with the ledger, so
// the per-tick check reads a few numbers per seat instead of walking a store. All derived from the stores: not hashed, not
// saved (the ledger's save part is unchanged); a load rebuilds them (RecountDraw). M6-SH3 (BUG-1854 / 1860 / 1731) added the
// wood carried, the finished drop-offs, and the unit items in training and waiting (per type), the same way.
internal sealed partial class PlayerLedger
{
    // Per unit type, whether it is a worker (UnitDef.IsWorker): what LiveWorkers counts.
    private bool[] _isWorkerType = Array.Empty<bool>();
    // M6-SH3: per building type, whether it takes deliveries (BuildingDef.DropOff): what FinishedDropOffs counts.
    private bool[] _dropOffType = Array.Empty<bool>();
    // M6-SH3: player x unit types + type: unit items of the type in the player's queues that have not started (no population
    // reserved yet). Empty for a ledger with no unit types.
    private int[] _waitingOfType = Array.Empty<int>();
    private int _waitingTypes;

    /// <summary>M6-S13: each player's live workers (<see cref="UnitDef.IsWorker"/>), summoned ones included, counted with <see cref="LiveUnits"/>. Derived; not hashed or saved.</summary>
    public int[] LiveWorkers { get; private set; } = Array.Empty<int>();

    /// <summary>M6-S13: each player's unit items in production queues (a research item is not one), started or waiting. Derived; not hashed or saved.</summary>
    public int[] QueuedUnits { get; private set; } = Array.Empty<int>();

    /// <summary>M6-SH3: each player's unit items being trained: a queue's head that has started (its population reserved), a finished one waiting for a free cell included. Derived; not hashed or saved.</summary>
    public int[] TrainingUnits { get; private set; } = Array.Empty<int>();

    /// <summary>M6-S13: the gold each player's live units carry (cargo of kind gold). Derived; not hashed or saved.</summary>
    public long[] CarriedGold { get; private set; } = Array.Empty<long>();

    /// <summary>M6-SH3: the wood each player's live units carry (cargo of kind wood). Derived; not hashed or saved.</summary>
    public long[] CarriedWood { get; private set; } = Array.Empty<long>();

    /// <summary>M6-SH3: each player's finished drop-off buildings (<see cref="BuildingDef.DropOff"/>; a site is none), counted with the finished counts. Derived; not hashed or saved.</summary>
    public int[] FinishedDropOffs { get; private set; } = Array.Empty<int>();

    private void InitDrawCounts(int players, GameData data)
    {
        _isWorkerType = new bool[data.Units.Length];
        for (int t = 0; t < _isWorkerType.Length; t++) _isWorkerType[t] = data.Units[t].IsWorker;
        _dropOffType = new bool[data.Buildings.Length];
        for (int t = 0; t < _dropOffType.Length; t++) _dropOffType[t] = data.Buildings[t].DropOff;
        _waitingTypes = data.Units.Length;
        _waitingOfType = new int[players * _waitingTypes];
        LiveWorkers = new int[players];
        QueuedUnits = new int[players];
        TrainingUnits = new int[players];
        CarriedGold = new long[players];
        CarriedWood = new long[players];
        FinishedDropOffs = new int[players];
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

    /// <summary>M6-SH3: a unit item of <paramref name="unitType"/> that has not started joins (+1) or leaves (-1: it starts, or leaves the queue unstarted) <paramref name="player"/>'s waiting items.</summary>
    public void AddWaitingUnit(int player, int unitType, int delta)
    {
        if (Has(player) && (uint)unitType < (uint)_waitingTypes) _waitingOfType[player * _waitingTypes + unitType] += delta;
    }

    /// <summary>M6-SH3: a unit item of <paramref name="player"/>'s starts training (+1) or stops (-1: trained, cancelled, or its building freed).</summary>
    public void AddTrainingUnit(int player, int delta)
    {
        if (Has(player)) TrainingUnits[player] += delta;
    }

    /// <summary>M6-SH3: <paramref name="player"/>'s unit items of <paramref name="unitType"/> waiting in its queues, not started; 0 for out-of-range ids.</summary>
    public int WaitingUnits(int player, int unitType) =>
        Has(player) && (uint)unitType < (uint)_waitingTypes ? _waitingOfType[player * _waitingTypes + unitType] : 0;

    /// <summary>Resources of <paramref name="kind"/> enter (positive: taken from a node) or leave (negative: delivered, dropped, or the carrier freed) <paramref name="player"/>'s units' cargo (M6-S13 gold; M6-SH3 wood too).</summary>
    public void AddCarried(int player, ResourceKind kind, int delta)
    {
        if (!Has(player)) return;
        if (kind == ResourceKind.Gold) CarriedGold[player] += delta;
        else CarriedWood[player] += delta;
    }

    /// <summary>
    /// Rebuilds every count the draw rule reads (live workers, queued, training and waiting unit items, carried gold and wood,
    /// finished drop-offs) from the stores (a load, after every store is read; a test that writes a store's arrays directly).
    /// Counts only what the stores' own paths count: a live unit of a player this ledger tracks, a unit item in a live building's
    /// queue (the head started when its progress is above 0), a live finished drop-off.
    /// </summary>
    public void RecountDraw(UnitStore units, BuildingStore buildings)
    {
        Array.Clear(LiveWorkers);
        Array.Clear(QueuedUnits);
        Array.Clear(TrainingUnits);
        Array.Clear(_waitingOfType);
        Array.Clear(CarriedGold);
        Array.Clear(CarriedWood);
        Array.Clear(FinishedDropOffs);
        for (int i = 0; i < units.Capacity; i++)
        {
            if (!units.Alive[i] || !Has(units.Owner[i])) continue;
            int owner = units.Owner[i], type = units.TypeId[i];
            if ((uint)type < (uint)_isWorkerType.Length && _isWorkerType[type]) LiveWorkers[owner]++;
            if (units.Cargo[i] != 0) AddCarried(owner, units.CargoKind[i], units.Cargo[i]);
        }
        for (int k = 0; k < buildings.Capacity; k++)
        {
            if (!buildings.Alive[k] || !Has(buildings.Owner[k])) continue;
            int owner = buildings.Owner[k];
            if (!buildings.UnderConstruction[k] && (uint)buildings.TypeId[k] < (uint)_dropOffType.Length && _dropOffType[buildings.TypeId[k]]) FinishedDropOffs[owner]++;
            for (int q = 0; q < buildings.QueueCount[k]; q++)
            {
                if (buildings.QueueIsTechAt(k, q)) continue;
                QueuedUnits[owner]++;
                if (q == 0 && buildings.Progress[k] > 0) TrainingUnits[owner]++;
                else AddWaitingUnit(owner, buildings.QueueTypeAt(k, q), 1);
            }
        }
        RecountLimits(units, buildings); // M8-4: the limit counts are derived from the same stores
    }
}
