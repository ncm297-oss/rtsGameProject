using System;
using Rts.Sim.Data;

namespace Rts.Sim.Economy;

/// <summary>
/// M6-S1 (Requests 39): the post-match stats counters per player (docs/02 "Game flow" 5, docs/03 "Implementation (M6-S1)").
/// Owned by <see cref="PlayerLedger"/> and written at the events they count; every array is allocated here, once. None of it
/// decides a tick, so none of it is hashed: it is save state outside the hash (docs/03 "Save/load and replays").
/// </summary>
internal sealed partial class MatchStats
{
    private readonly int _players;
    private readonly int _techs;
    // player x techs + tech: the tick count at which the tech was granted, -1 before.
    private readonly int[] _techTick;
    // player x SampleCapacity + sample: the army value at each sample tick.
    private readonly int[] _samples;
    // The sample pass's per-player sums (long, so a huge army saturates instead of wrapping).
    private readonly long[] _sampleSums;

    /// <summary>Counters for <paramref name="players"/> players, <paramref name="techs"/> techs and <paramref name="sampleCapacity"/> samples per player.</summary>
    public MatchStats(int players, int techs, int sampleCapacity)
    {
        _players = players;
        _techs = techs;
        SampleCapacity = Math.Max(0, sampleCapacity);
        GatheredGold = new int[players];
        GatheredWood = new int[players];
        UnitsTrained = new int[players];
        UnitKills = new int[players];
        BuildingKills = new int[players];
        UnitLosses = new int[players];
        BuildingLosses = new int[players];
        BuildingsBuilt = new int[players];
        _techTick = new int[players * techs];
        Array.Fill(_techTick, -1);
        _samples = new int[players * SampleCapacity];
        _sampleSums = new long[players];
    }

    /// <summary>Gold each player's workers delivered at a drop-off.</summary>
    public int[] GatheredGold { get; }

    /// <summary>Wood each player's workers delivered at a drop-off.</summary>
    public int[] GatheredWood { get; }

    /// <summary>Units each player's production queues finished (summons and dev spawns not counted).</summary>
    public int[] UnitsTrained { get; }

    /// <summary>The unit half of <see cref="PlayerLedger.Kills"/>.</summary>
    public int[] UnitKills { get; }

    /// <summary>The building half of <see cref="PlayerLedger.Kills"/>.</summary>
    public int[] BuildingKills { get; }

    /// <summary>The unit half of <see cref="PlayerLedger.Losses"/>.</summary>
    public int[] UnitLosses { get; }

    /// <summary>The building half of <see cref="PlayerLedger.Losses"/> (also the stats screen's "buildings lost").</summary>
    public int[] BuildingLosses { get; }

    /// <summary>Construction sites each player completed (buildings spawned finished not counted).</summary>
    public int[] BuildingsBuilt { get; }

    /// <summary>Army-value samples each player's buffer holds at most.</summary>
    public int SampleCapacity { get; }

    /// <summary>Samples taken so far (the same for every player); stops at <see cref="SampleCapacity"/>.</summary>
    public int SampleCount { get; private set; }

    /// <summary>Adds a delivery to <paramref name="player"/>'s gathered count, saturating at <see cref="int.MaxValue"/>.</summary>
    public void AddGathered(int player, ResourceKind kind, int amount)
    {
        if ((uint)player >= (uint)_players || amount <= 0) return;
        int[] counts = kind == ResourceKind.Gold ? GatheredGold : GatheredWood;
        counts[player] = (int)Math.Min((long)counts[player] + amount, int.MaxValue);
    }

    /// <summary>A queue item of <paramref name="player"/>'s finished as a unit.</summary>
    public void CountTrained(int player)
    {
        if ((uint)player < (uint)_players) UnitsTrained[player]++;
    }

    /// <summary>A construction site of <paramref name="player"/>'s completed.</summary>
    public void CountBuilt(int player)
    {
        if ((uint)player < (uint)_players) BuildingsBuilt[player]++;
    }

    /// <summary>Records that <paramref name="player"/> got <paramref name="tech"/> at tick count <paramref name="tick"/>; a tech already recorded keeps its first tick.</summary>
    public void MarkResearched(int player, int tech, int tick)
    {
        if ((uint)player >= (uint)_players || (uint)tech >= (uint)_techs) return;
        ref int slot = ref _techTick[player * _techs + tech];
        if (slot < 0) slot = tick;
    }

    /// <summary>The tick count at which <paramref name="player"/> got <paramref name="tech"/>; -1 before, and for an unknown player or tech.</summary>
    public int ResearchedTick(int player, int tech) =>
        (uint)player < (uint)_players && (uint)tech < (uint)_techs ? _techTick[player * _techs + tech] : -1;

    /// <summary><paramref name="player"/>'s samples so far, oldest first; empty for an unknown player.</summary>
    public ReadOnlySpan<int> Samples(int player) =>
        (uint)player < (uint)_players ? _samples.AsSpan(player * SampleCapacity, SampleCount) : ReadOnlySpan<int>.Empty;

    /// <summary>
    /// Takes one army-value sample for every player, unless the buffers are full: the summed gold + wood cost of each player's
    /// live non-worker units (<see cref="UnitDef.IsWorker"/>), summoned ones included, saturating at <see cref="int.MaxValue"/>.
    /// One pass over the unit slots; no allocation.
    /// </summary>
    public void Sample(Entities.UnitStore u, GameData data)
    {
        if (SampleCount >= SampleCapacity) return;
        Array.Clear(_sampleSums);
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            int owner = u.Owner[i];
            if ((uint)owner >= (uint)_players) continue;
            UnitDef def = data.Units[u.TypeId[i]];
            if (def.IsWorker) continue;
            _sampleSums[owner] += def.CostGold + def.CostWood;
        }
        for (int p = 0; p < _players; p++)
            _samples[p * SampleCapacity + SampleCount] = (int)Math.Min(_sampleSums[p], int.MaxValue);
        SampleCount++;
    }
}
