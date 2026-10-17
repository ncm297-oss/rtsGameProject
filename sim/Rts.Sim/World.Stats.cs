using System;

namespace Rts.Sim;

/// <summary>
/// M6-S1 (Requests 39): the post-match stats, read-only, per player (docs/03 "Implementation (M6-S1)"). Counted at the events
/// in <see cref="Economy.MatchStats"/>; none of it is hashed (save state outside the hash).
/// </summary>
public sealed partial class World
{
    /// <summary>M6-S1: gold each player's workers delivered at a drop-off, indexed by player (refunds and starting gold not counted). Not hashed.</summary>
    public ReadOnlySpan<int> GatheredGold => _ledger.Stats.GatheredGold;

    /// <summary>M6-S1: wood each player's workers delivered at a drop-off, indexed by player (refunds and starting wood not counted). Not hashed.</summary>
    public ReadOnlySpan<int> GatheredWood => _ledger.Stats.GatheredWood;

    /// <summary>M6-S1: units each player's production queues finished, indexed by player (summons and dev <c>SpawnUnit</c>s not counted). Not hashed.</summary>
    public ReadOnlySpan<int> UnitsTrained => _ledger.Stats.UnitsTrained;

    /// <summary>M6-S1: the units among each player's <see cref="Kills"/>, indexed by player. Not hashed.</summary>
    public ReadOnlySpan<int> UnitKills => _ledger.Stats.UnitKills;

    /// <summary>M6-S1: the buildings (sites included) among each player's <see cref="Kills"/>, indexed by player. Not hashed.</summary>
    public ReadOnlySpan<int> BuildingKills => _ledger.Stats.BuildingKills;

    /// <summary>M6-S1: the units among each player's <see cref="Losses"/>, indexed by player. Not hashed.</summary>
    public ReadOnlySpan<int> UnitLosses => _ledger.Stats.UnitLosses;

    /// <summary>M6-S1: the buildings (sites included) among each player's <see cref="Losses"/>, indexed by player. Not hashed.</summary>
    public ReadOnlySpan<int> BuildingLosses => _ledger.Stats.BuildingLosses;

    /// <summary>M6-S1: the stats screen's "buildings lost": the same counter as <see cref="BuildingLosses"/>.</summary>
    public ReadOnlySpan<int> BuildingsLost => _ledger.Stats.BuildingLosses;

    /// <summary>M6-S1: construction sites each player completed, indexed by player (buildings spawned finished, the start hall among them, not counted). Not hashed.</summary>
    public ReadOnlySpan<int> BuildingsBuilt => _ledger.Stats.BuildingsBuilt;

    /// <summary>
    /// M6-S1: the <see cref="TickNumber"/> at which <paramref name="player"/> got tech <paramref name="techId"/>, i.e. the first
    /// tick count between ticks at which <see cref="HasTech"/> reads true (ticks x 50 ms is the match time); -1 before, and for
    /// an unknown player or tech. Not hashed.
    /// </summary>
    public int TechResearchedTick(int player, int techId) => _ledger.Stats.ResearchedTick(player, techId);

    /// <summary>
    /// M6-S1: <paramref name="player"/>'s army value over time, oldest first: sample k is the summed gold + wood cost of its
    /// live non-worker units when <see cref="TickNumber"/> reached <c>(k + 1) x</c> <see cref="StatsSampleTicks"/>. Holds
    /// <c>min(TickNumber / StatsSampleTicks, StatsSampleCapacity)</c> entries (sampling stops when full); empty for an
    /// unknown player. Valid until the next tick. Not hashed.
    /// </summary>
    public ReadOnlySpan<int> ArmyValueSamples(int player) => _ledger.Stats.Samples(player);

    /// <summary>M6-S1: ticks between army-value samples (<see cref="SimConfig.StatsSampleTicks"/>).</summary>
    public int StatsSampleTicks => Config.StatsSampleTicks;

    /// <summary>M6-S1: army-value samples kept per player at most (<see cref="SimConfig.StatsSampleCapacity"/>).</summary>
    public int StatsSampleCapacity => _ledger.Stats.SampleCapacity;

    /// <summary>Phase 13, after the tick count moved on (M6-S1): an army-value sample when the new count is a multiple of <see cref="StatsSampleTicks"/>.</summary>
    internal void SampleStats()
    {
        if (TickNumber % Config.StatsSampleTicks == 0) _ledger.Stats.Sample(Units, Data);
    }
}
