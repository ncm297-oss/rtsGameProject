using System;
using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Vision;

namespace Rts.Sim.Abilities;

/// <summary>
/// Stealth and detection (M4-5, docs/02 "Stealth and detection", docs/03 "Implementation (M4-5)"): the end of tick phase 5
/// works out which players detect each unit that carries a Stealthed status (<see cref="World.IsDetectedBy"/>), and an
/// attack landing or a cast resolving from stealth applies the Revealed status (<see cref="OnAttackOrCast"/>).
/// </summary>
/// <remarks>
/// Enemy player p detects unit j when j's cell is visible to p (<see cref="FogStore.SeesUnitCell"/>: the fog's last update)
/// and a live unit of p stands within <c>rules.json</c> <c>stealth.proximity</c> of j, or within its own <c>detector</c>
/// radius, or a finished building of p has j within its <c>detector</c> radius of the footprint's centre (centre to centre,
/// the edge counts). The result is an OR over the detectors, so the order they are visited in (the spatial hash's buckets,
/// building slots ascending) never matters. Allocation free.
/// </remarks>
public static class StealthSystem
{
    /// <summary>
    /// Phase 5, after the statuses and zones: every live unit with a Stealthed status gets its detection bits rebuilt, in
    /// slot order. Returns at once while no unit carries one (<see cref="StatusStore.StealthedUnits"/>).
    /// </summary>
    public static void Run(World world)
    {
        StatusStore s = world.Units.Statuses;
        if (s.StealthedUnits == 0) return;
        UnitStore u = world.Units;
        int buildings = CollectDetectorBuildings(world);
        int[] found = world.Neighbors; // movement's scratch: free in phase 5
        for (int j = 0; j < u.Capacity; j++)
            if (u.Alive[j] && s.HasStealth(j)) Detect(world, j, found, buildings);
    }

    /// <summary>
    /// Unit slot <paramref name="j"/> just got its first Stealthed status (any phase): its detection bits are worked out now,
    /// so it is never hidden for the rest of the tick from a player whose unit stands beside it. Uses the second movement
    /// scratch (phase 6's resolve and phase 11's splash hold the first) and the spatial hash as it was built in phase 1.
    /// </summary>
    internal static void OnStealthStarted(World world, int j) => Detect(world, j, world.ShoveNeighbors, -1);

    /// <summary>
    /// Unit slot <paramref name="i"/>'s attack landed (a melee hit, a projectile's impact or splash, a damage effect of its
    /// cast) or its cast resolved: if it carries a Stealthed status it is Revealed for <c>stealth.revealSeconds</c> (the
    /// stacking rule: a reveal refreshes, never shortens). A unit without one is not touched, so a match without stealth
    /// adds no status.
    /// </summary>
    internal static void OnAttackOrCast(World world, int i)
    {
        UnitStore u = world.Units;
        if (u.Statuses.StealthedUnits == 0 || !u.Statuses.HasStealth(i) || world.RevealedStatus < 0 || world.Data.Rules.RevealTicks <= 0) return;
        StatusSystem.Apply(world, i, world.RevealedStatus, 0f, world.Data.Rules.RevealTicks, u.Owner[i]);
    }

    /// <summary>Fills <see cref="World.DetectorBuildings"/> with every live finished building whose type has a detector, slot order; returns the count.</summary>
    private static int CollectDetectorBuildings(World world)
    {
        BuildingStore b = world.Buildings;
        ReadOnlySpan<bool> alive = b.Alive;
        int[] list = world.DetectorBuildings;
        int n = 0;
        for (int k = 0; k < alive.Length; k++)
            if (alive[k] && !b.UnderConstruction[k] && world.Data.Buildings[b.TypeId[k]].Detector > 0f) list[n++] = k;
        return n;
    }

    /// <summary>
    /// Rebuilds unit slot <paramref name="j"/>'s detection bits: units near it from the spatial hash (into
    /// <paramref name="found"/>), then the first <paramref name="buildingCount"/> entries of
    /// <see cref="World.DetectorBuildings"/>, or every building slot when it is -1.
    /// </summary>
    private static void Detect(World world, int j, int[] found, int buildingCount)
    {
        world.ClearDetected(j);
        UnitStore u = world.Units;
        if (u.IsSummoned(j)) return; // never stealthed: nobody needs to detect it
        GameData data = world.Data;
        FogStore fog = world.Fog;
        int owner = u.Owner[j];
        Vector2 pos = u.Position[j];
        float proximity2 = data.Rules.StealthProximity * data.Rules.StealthProximity;
        // Players already settled for this unit (bit p: detected, or its cell is unseen by p), so the fog is asked once a player.
        ulong done = 0;
        int n = Math.Min(world.Spatial.QueryEnemies(pos, world.StealthQueryRadius, owner, found), found.Length);
        for (int k = 0; k < n; k++)
        {
            int q = found[k];
            if (!u.Alive[q] || u.Owner[q] == owner) continue;
            float d2 = Vector2.DistanceSquared(pos, u.Position[q]);
            float detector = data.Units[u.TypeId[q]].Detector;
            if (!(d2 <= proximity2) && !(detector > 0f && d2 <= detector * detector)) continue;
            Mark(world, fog, j, u.Owner[q], ref done);
        }
        BuildingStore b = world.Buildings;
        int count = buildingCount >= 0 ? buildingCount : b.Capacity;
        for (int k = 0; k < count; k++)
        {
            int slot = buildingCount >= 0 ? world.DetectorBuildings[k] : k;
            if (buildingCount < 0 && (!b.Alive[slot] || b.UnderConstruction[slot])) continue;
            int bOwner = b.Owner[slot];
            if (bOwner == owner) continue;
            float detector = data.Buildings[b.TypeId[slot]].Detector;
            if (!(detector > 0f) || !(Vector2.DistanceSquared(CombatSystem.BuildingCentre(world, slot), pos) <= detector * detector)) continue;
            Mark(world, fog, j, bOwner, ref done);
        }
    }

    /// <summary>A detector of <paramref name="player"/> covers unit slot <paramref name="j"/>: the player detects it when it sees the unit's cell.</summary>
    private static void Mark(World world, FogStore fog, int j, int player, ref ulong done)
    {
        ulong bit = player < 64 ? 1UL << player : 0UL;
        if ((done & bit) != 0) return;
        done |= bit;
        if (fog.SeesUnitCell(player, j)) world.SetDetected(player, j);
    }
}
