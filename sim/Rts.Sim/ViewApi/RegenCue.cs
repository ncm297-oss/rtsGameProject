using Rts.Sim.Abilities;
using Rts.Sim.Data;

namespace Rts.Sim.ViewApi;

/// <summary>
/// Whether a unit heals by regeneration on the next tick (M7-V3a, the selection panel's "Regenerating" cue; docs/03
/// "Implementation (M7-1)" for the rule, "Implementation (M7-V3a)" for the cue). Read-only: it reads the values the caller
/// passes and the status store's public arrays, writes nothing, and allocates nothing, so calling it changes no state hash.
/// </summary>
/// <remarks>
/// <see cref="Combat.RegenSystem"/> runs in phase 5 of tick <c>t</c> with the world's tick number <c>t</c>. Between ticks the
/// world's <c>TickNumber</c> is the next tick to run, so a unit hit in the last tick (its <c>LastDamagedTick</c> that tick)
/// reads "not healing" at once, and the cue follows the sim's rule exactly: the faction's rate once
/// <c>next - LastDamagedTick &gt;= RegenDelayTicks</c> (or never damaged), or a regenerating status entry still on the unit after
/// next tick's countdown (<c>TicksRemaining &gt; 1</c>: its last tick's countdown ends it before regeneration reads it); and
/// only while the unit is below its maximum. One more case the rule implies: a damage-over-time pulse due next tick
/// (<c>PulseTicks == 1</c>) hits in phase 5 before regeneration and restarts a faction delay above 0.
/// </remarks>
public static class RegenCue
{
    /// <summary>
    /// True when unit slot <paramref name="slot"/> (live, hit points <paramref name="hp"/> of <paramref name="maxHp"/>, of faction
    /// <paramref name="faction"/>, last damaged in tick <paramref name="lastDamagedTick"/> or -1) gains regeneration in tick
    /// <paramref name="nextTick"/> (the world's tick number between ticks), by its faction's rule or a regenerating status.
    /// </summary>
    public static bool Healing(GameData data, StatusStore statuses, int slot, int faction, int hp, int maxHp, int lastDamagedTick, int nextTick)
    {
        if (hp >= maxHp || (uint)slot >= (uint)statuses.Count.Length) return false;
        bool pulseDue = false;
        int head = slot * StatusStore.PerUnit;
        int count = statuses.Count[slot];
        for (int k = 0; k < count; k++)
        {
            int id = statuses.StatusId[head + k];
            if ((uint)id >= (uint)data.Statuses.Length) continue;
            StatusKind kind = data.Statuses[id].Kind;
            if (kind == StatusKind.Regenerating && statuses.TicksRemaining[head + k] > 1) return true;
            if (kind == StatusKind.DamageOverTime && statuses.PulseTicks[head + k] == 1) pulseDue = true;
        }
        if ((uint)faction >= (uint)data.Factions.Length) return false;
        FactionDef f = data.Factions[faction];
        if (f.RegenPerTick <= 0) return false;
        if (pulseDue && f.RegenDelayTicks > 0) return false;
        return lastDamagedTick < 0 || nextTick - lastDamagedTick >= f.RegenDelayTicks;
    }
}
