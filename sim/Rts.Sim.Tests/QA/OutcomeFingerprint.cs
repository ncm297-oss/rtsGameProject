using Rts.Sim.Entities;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M7-3b (2026-11-10-2116): a hash of a match's *outcome* state that does not depend on how data ids are numbered. Every
/// unit slot (alive, owner, type key, position and velocity bits, hp, state, cargo, ability ready ticks), every building slot
/// (alive, owner, type key, cell, hp, construction, work, progress, its queue items by key), every player's gold and wood
/// and researched tech keys. Type, tech and ability ids never enter it: only their string keys, so a renumbering of the id
/// tables (M7-3, M7-3b) leaves it alone, and a real change in play moves it. Compiles on de591f0 and later.
/// </summary>
internal static class OutcomeFingerprint
{
    private const ulong Offset = 14695981039346656037UL, Prime = 1099511628211UL;

    private static ulong Add(ulong h, ulong v) => (h ^ v) * Prime;

    private static ulong Add(ulong h, string s)
    {
        foreach (char c in s) h = Add(h, c);
        return Add(h, 0xFFFFUL);
    }

    private static ulong Bits(float f) => (uint)BitConverter.SingleToInt32Bits(f);

    /// <summary>The fingerprint of <paramref name="sim"/>'s current state.</summary>
    internal static ulong Of(Simulation sim)
    {
        World w = sim.World;
        var d = w.Data;
        ulong h = Add(Offset, (ulong)sim.TickNumber);
        UnitStore u = w.Units;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) { h = Add(h, 0); continue; }
            h = Add(h, 1);
            h = Add(h, (ulong)u.Owner[i]);
            h = Add(h, d.Units[u.TypeId[i]].Key);
            h = Add(h, Bits(u.Position[i].X));
            h = Add(h, Bits(u.Position[i].Y));
            h = Add(h, Bits(u.Velocity[i].X));
            h = Add(h, Bits(u.Velocity[i].Y));
            h = Add(h, Bits(u.Facing[i]));
            h = Add(h, (ulong)u.Hp[i]);
            h = Add(h, (ulong)u.State[i]);
            h = Add(h, (ulong)u.Cargo[i]);
            h = Add(h, (ulong)u.CooldownTicks[i]);
            for (int a = 0; a < Data.DataLimits.MaxUnitAbilities; a++)
                h = Add(h, (ulong)u.AbilityReadyTick[i * Data.DataLimits.MaxUnitAbilities + a]);
        }
        BuildingStore b = w.Buildings;
        for (int i = 0; i < b.Capacity; i++)
        {
            if (!b.Alive[i]) { h = Add(h, 0); continue; }
            h = Add(h, 1);
            h = Add(h, (ulong)b.Owner[i]);
            h = Add(h, d.Buildings[b.TypeId[i]].Key);
            h = Add(h, (ulong)b.Cell[i]);
            h = Add(h, (ulong)b.Hp[i]);
            h = Add(h, b.UnderConstruction[i] ? 1UL : 0UL);
            h = Add(h, (ulong)b.Work[i]);
            h = Add(h, (ulong)b.Progress[i]);
            h = Add(h, (ulong)b.QueueCount[i]);
            for (int k = 0; k < b.QueueCount[i]; k++)
            {
                int type = b.QueueTypeAt(i, k);
                h = Add(h, b.QueueIsTechAt(i, k) ? "T:" + d.Techs[type].Key : "U:" + d.Units[type].Key);
            }
        }
        for (int p = 0; p < w.Gold.Length; p++)
        {
            h = Add(h, (ulong)w.Gold[p]);
            h = Add(h, (ulong)w.Wood[p]);
            // Researched techs by key, in key order (not id order).
            var keys = new List<string>();
            for (int t = 0; t < d.Techs.Length; t++)
                if (w.HasTech(p, t)) keys.Add(d.Techs[t].Key);
            keys.Sort(StringComparer.Ordinal);
            foreach (string k in keys) h = Add(h, k);
        }
        return h;
    }
}
