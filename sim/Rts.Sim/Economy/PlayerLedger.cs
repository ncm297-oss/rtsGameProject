using System;
using Rts.Sim.Data;

namespace Rts.Sim.Economy;

/// <summary>
/// Per-player totals and population (M3-2, M3-4): gold, wood, half-pop used, and the cap the player's finished buildings
/// provide. Shared by <see cref="World"/> and the unit and building stores, so a unit freed or a building destroyed
/// settles its own population and refunds without the stores knowing the world. Since M3-6 it also counts each player's
/// finished buildings per type and per slot, for the requirement gates (derived from the building store, not hashed).
/// </summary>
internal sealed class PlayerLedger
{
    private static readonly int SlotCount = DataLimits.BuildingSlotIds.Length;

    private readonly int _rulesCap;
    private readonly int _types;
    // player x types + type, and player x SlotCount + slot: finished, live buildings the player owns.
    private readonly int[] _finishedOfType;
    private readonly int[] _finishedInSlot;

    /// <summary>A ledger for <paramref name="players"/> players starting at <paramref name="data"/>'s rules totals, no population, no cap and no buildings.</summary>
    public PlayerLedger(int players, GameData data)
    {
        RulesDef rules = data.Rules;
        _types = data.Buildings.Length;
        _finishedOfType = new int[players * _types];
        _finishedInSlot = new int[players * SlotCount];
        Gold = new int[players];
        Wood = new int[players];
        HalfPop = new int[players];
        HalfPopProvided = new int[players];
        HalfPopCap = new int[players];
        Kills = new int[players];
        Losses = new int[players];
        Array.Fill(Gold, rules.StartingGold);
        Array.Fill(Wood, rules.StartingWood);
        _rulesCap = rules.HalfPopCap;
    }

    /// <summary>Each player's gold.</summary>
    public int[] Gold { get; }

    /// <summary>Each player's wood.</summary>
    public int[] Wood { get; }

    /// <summary>Each player's population in use, half-pop: live units counted at spawn plus production items that have started (reserved). Derived from units and queues, kept incrementally; not hashed.</summary>
    public int[] HalfPop { get; }

    /// <summary>Each player's population provided by finished buildings, half-pop, before the rules cap.</summary>
    public int[] HalfPopProvided { get; }

    /// <summary>Each player's population cap, half-pop: <c>min(</c><see cref="HalfPopProvided"/><c>, rules popCap)</c>.</summary>
    public int[] HalfPopCap { get; }

    /// <summary>Each player's killing blows, units and buildings (M4-1). Hashed state.</summary>
    public int[] Kills { get; }

    /// <summary>Each player's units and buildings killed (M4-1). Hashed state.</summary>
    public int[] Losses { get; }

    /// <summary>One death: <paramref name="killer"/>'s kills and <paramref name="victim"/>'s losses go up by one (M4-1).</summary>
    public void CountDeath(int killer, int victim)
    {
        if (Has(killer)) Kills[killer]++;
        if (Has(victim)) Losses[victim]++;
    }

    /// <summary>True for a player index this ledger tracks.</summary>
    public bool Has(int player) => (uint)player < (uint)Gold.Length;

    /// <summary>Adds <paramref name="amount"/> of <paramref name="kind"/> to <paramref name="player"/>'s total, saturating at <see cref="int.MaxValue"/>.</summary>
    public void AddToTotal(int player, ResourceKind kind, int amount)
    {
        if (!Has(player)) return;
        int[] totals = kind == ResourceKind.Gold ? Gold : Wood;
        long sum = (long)totals[player] + amount;
        totals[player] = sum > int.MaxValue ? int.MaxValue : (int)sum;
    }

    /// <summary>Refunds a cost in full (saturating).</summary>
    public void Refund(int player, int gold, int wood)
    {
        AddToTotal(player, ResourceKind.Gold, gold);
        AddToTotal(player, ResourceKind.Wood, wood);
    }

    /// <summary>True (and the amounts taken) if <paramref name="player"/> has at least <paramref name="gold"/> and <paramref name="wood"/>.</summary>
    public bool TrySpend(int player, int gold, int wood)
    {
        if (!Has(player) || Gold[player] < gold || Wood[player] < wood) return false;
        Gold[player] -= gold;
        Wood[player] -= wood;
        return true;
    }

    /// <summary>Changes <paramref name="player"/>'s population in use by <paramref name="delta"/> half-pop.</summary>
    public void AddHalfPop(int player, int delta)
    {
        if (Has(player)) HalfPop[player] += delta;
    }

    /// <summary>
    /// A finished building of <paramref name="type"/> in <paramref name="slot"/> starts (+1: spawned finished or completed)
    /// or stops (-1: freed) counting for <paramref name="player"/> (M3-6). Derived from the building store, so not hashed.
    /// </summary>
    public void AddFinished(int player, int type, BuildingSlot slot, int delta)
    {
        if (!Has(player) || (uint)type >= (uint)_types || (uint)slot >= (uint)SlotCount) return;
        _finishedOfType[player * _types + type] += delta;
        _finishedInSlot[player * SlotCount + (int)slot] += delta;
    }

    /// <summary>How many finished buildings of <paramref name="type"/> <paramref name="player"/> owns; 0 for out-of-range ids.</summary>
    public int FinishedOfType(int player, int type) =>
        Has(player) && (uint)type < (uint)_types ? _finishedOfType[player * _types + type] : 0;

    /// <summary>How many finished buildings in <paramref name="slot"/> (a <see cref="BuildingSlot"/> value) <paramref name="player"/> owns; 0 for out-of-range ids.</summary>
    public int FinishedInSlot(int player, int slot) =>
        Has(player) && (uint)slot < (uint)SlotCount ? _finishedInSlot[player * SlotCount + slot] : 0;

    /// <summary>Changes the population <paramref name="player"/>'s buildings provide by <paramref name="delta"/> half-pop, and its cap with it.</summary>
    public void AddProvided(int player, int delta)
    {
        if (!Has(player) || delta == 0) return;
        long provided = (long)HalfPopProvided[player] + delta;
        HalfPopProvided[player] = (int)Math.Clamp(provided, 0L, int.MaxValue);
        HalfPopCap[player] = Math.Min(HalfPopProvided[player], _rulesCap);
    }
}
