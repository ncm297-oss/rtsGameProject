using System.Numerics;
using Rts.Sim.Abilities;
using Rts.Sim.Entities;

namespace Rts.Sim.Tests;

/// <summary>M4-4a test helpers: a flat map with no auto-fighting, the Cadre Mage, and the cast moments of the last tick.</summary>
public static class AbilityScenes
{
    /// <summary>Malazan Cadre Mage: Telas Fire is its ability 0.</summary>
    public static int Mage => TestSim.Data.FindUnit("malazan_cadre_mage");

    /// <summary>Burning's status id.</summary>
    public static int Burning => TestSim.Data.FindStatus("burning");

    /// <summary>Slowed's status id.</summary>
    public static int Slowed => TestSim.Data.FindStatus("slowed");

    /// <summary>
    /// A 48 x 48 flat map with two players and combat off (no unit scans or swings, so hit points change only by abilities),
    /// explored for both.
    /// </summary>
    public static Simulation NoFights(int units = 64, ulong seed = 1) =>
        TestSim.Explored(new Simulation(TestSim.ConfigNoCombat(Seed: seed, PlayerCount: 2, UnitCapacity: units, CommandCapacity: 512), LocalMovementTests.Flat(48)));

    /// <summary>Whether the last tick recorded a cast moment (<paramref name="resolved"/>: a resolve, else a start) of <paramref name="caster"/>.</summary>
    public static bool Had(Simulation sim, EntityHandle caster, bool resolved)
    {
        foreach (AbilityEvent e in sim.World.AbilityEvents)
            if (e.Caster == caster && e.Resolved == resolved) return true;
        return false;
    }

    /// <summary>Ticks until <paramref name="caster"/>'s cast starts (or <paramref name="resolved"/>: resolves); returns the number of the tick it happened in.</summary>
    public static int TickOf(Simulation sim, EntityHandle caster, bool resolved, int max = 2000)
    {
        for (int t = 0; t < max; t++)
        {
            sim.Tick();
            if (Had(sim, caster, resolved)) return sim.TickNumber - 1;
        }
        throw new Xunit.Sdk.XunitException($"no {(resolved ? "resolve" : "start")} in {max} ticks");
    }

    /// <summary>The magnitude and ticks left of status <paramref name="status"/> on <paramref name="unit"/>, or (0, 0).</summary>
    public static (float Magnitude, int Ticks) StatusOf(Simulation sim, EntityHandle unit, int status)
    {
        StatusStore s = sim.World.Units.Statuses;
        int at = s.IndexOf(unit.Index, status);
        return at < 0 ? (0f, 0) : (s.Magnitude[at], s.TicksRemaining[at]);
    }

    /// <summary>A point <paramref name="dx"/>, <paramref name="dy"/> m from <paramref name="p"/>.</summary>
    public static Vector2 Off(Vector2 p, float dx, float dy = 0f) => p + new Vector2(dx, dy);
}
