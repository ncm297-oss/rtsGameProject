using System.Numerics;
using Rts.Sim.Abilities;
using Rts.Sim.Combat;
using Rts.Sim.Data;
using Rts.Sim.Entities;

namespace Rts.Sim.Vision;

/// <summary>
/// Vision (M4-3a, docs/03 "Vision, detection, fog"): the fog update of tick phase 12 (<see cref="Run"/>), the target
/// validity rule combat asks (<see cref="UnitSeesUnit"/>, <see cref="UnitSeesBuilding"/>), and the high-ground reveal a
/// hit sets (<see cref="OnHit"/>).
/// </summary>
public static class VisionSystem
{
    /// <summary>Phase 12: on every update tick (<see cref="IsUpdateTick"/>: tick 1, 5, 9, ...) every player's fog is rebuilt.</summary>
    public static void Run(World world)
    {
        if (IsUpdateTick(world.TickNumber)) world.Fog.Update();
    }

    /// <summary>Whether phase 12 of tick <paramref name="tick"/> rebuilds the fog: <c>tick % 4 == 1</c> (<see cref="VisionConstants.UpdatePhase"/>).</summary>
    public static bool IsUpdateTick(int tick) => tick % VisionConstants.UpdateInterval == VisionConstants.UpdatePhase;

    /// <summary>
    /// Whether unit <paramref name="i"/> (its owner) may take enemy unit <paramref name="j"/> as a target (scan, retaliation,
    /// an explicit Attack, keeping a target): <paramref name="i"/> sees it itself right now (within its sight, and not on
    /// a higher level unless within the lip), or its owner's fog shows <paramref name="j"/>'s cell visible, or
    /// <paramref name="j"/> is revealed to that owner. The first part is the stamp's rule measured unit to unit at this
    /// tick, so a unit never misses what its own circle covers between updates. M4-4b-2: the first part also fails when
    /// <paramref name="j"/>'s cell is inside a zone that hides it from <paramref name="i"/>'s owner and <paramref name="i"/>
    /// stands outside that zone (<see cref="ZoneHides"/>); it uses the type's sight (a Blinded unit's targets are capped by
    /// its reach in combat instead, so it takes one out to its reach though its fog circle is smaller).
    /// </summary>
    internal static bool UnitSeesUnit(World world, int i, int j)
    {
        UnitStore u = world.Units;
        FogStore fog = world.Fog;
        float sight = world.Data.Units[u.TypeId[i]].Sight;
        float d2 = Vector2.DistanceSquared(u.Position[i], u.Position[j]);
        if (d2 <= sight * sight && (!fog.MultiLevel || d2 <= VisionConstants.LipRadius * VisionConstants.LipRadius
            || fog.LevelAt(u.Position[j]) <= fog.LevelAt(u.Position[i]))
            && (world.Zones.BlockerCount == 0 || !ZoneHides(world, u.Owner[i], u.Position[i], true, u.Position[j])))
            return true;
        return fog.SeesUnit(u.Owner[i], j);
    }

    /// <summary>
    /// <see cref="UnitSeesUnit"/> for enemy building slot <paramref name="j"/>: within <paramref name="i"/>'s sight of its
    /// footprint and not on a higher level (its centre cell's) unless within the lip, or any footprint cell visible to the owner.
    /// </summary>
    internal static bool UnitSeesBuilding(World world, int i, int j)
    {
        UnitStore u = world.Units;
        FogStore fog = world.Fog;
        float sight = world.Data.Units[u.TypeId[i]].Sight;
        float d2 = CombatSystem.BuildingDistanceSquared(world, j, u.Position[i]);
        if (d2 <= sight * sight && (!fog.MultiLevel || d2 <= VisionConstants.LipRadius * VisionConstants.LipRadius
            || fog.BuildingLevel(j) <= fog.LevelAt(u.Position[i]))
            && (world.Zones.BlockerCount == 0 || !ZoneHides(world, u.Owner[i], u.Position[i], true, CombatSystem.BuildingCentre(world, j))))
            return true;
        return fog.SeesBuildingCells(u.Owner[i], j);
    }

    /// <summary>
    /// Whether tower (building slot) <paramref name="j"/>'s owner may take enemy unit <paramref name="unit"/> (M4-3b): the
    /// unit rule with the building as the viewer: within the building's sight of its footprint's centre and not on a higher
    /// level than the building (its centre cell's) unless within the lip, or the unit's cell visible in the owner's fog, or
    /// the unit revealed to the owner.
    /// </summary>
    internal static bool BuildingSeesUnit(World world, int j, int unit)
    {
        BuildingStore b = world.Buildings;
        FogStore fog = world.Fog;
        UnitStore u = world.Units;
        float sight = world.Data.Buildings[b.TypeId[j]].Sight;
        float d2 = Vector2.DistanceSquared(CombatSystem.BuildingCentre(world, j), u.Position[unit]);
        if (d2 <= sight * sight && (!fog.MultiLevel || d2 <= VisionConstants.LipRadius * VisionConstants.LipRadius
            || fog.LevelAt(u.Position[unit]) <= fog.BuildingLevel(j))
            && (world.Zones.BlockerCount == 0 || !ZoneHides(world, b.Owner[j], default, false, u.Position[unit]))) // a building never views from inside a zone
            return true;
        return fog.SeesUnit(b.Owner[j], unit);
    }

    /// <summary>
    /// Whether unit <paramref name="i"/>'s owner sees the ground of a remembered building now (M4-3b, an ordered Attack on a
    /// last-known building that is gone): within <paramref name="i"/>'s sight of the footprint (type
    /// <paramref name="typeId"/> at <paramref name="anchor"/>) and not above it unless within the lip, or a footprint cell
    /// visible in the owner's fog. Then the player knows the building is gone.
    /// </summary>
    internal static bool UnitSeesFootprint(World world, int i, int typeId, int anchor)
    {
        UnitStore u = world.Units;
        FogStore fog = world.Fog;
        float sight = world.Data.Units[u.TypeId[i]].Sight;
        CombatSystem.FootprintRect(world, typeId, anchor, out Vector2 min, out Vector2 max);
        Vector2 p = u.Position[i];
        float d2 = Vector2.DistanceSquared(p, Vector2.Clamp(p, min, max));
        if (d2 <= sight * sight && (!fog.MultiLevel || d2 <= VisionConstants.LipRadius * VisionConstants.LipRadius
            || fog.LevelAt((min + max) * 0.5f) <= fog.LevelAt(p)))
            return true;
        return fog.SeesFootprint(u.Owner[i], typeId, anchor);
    }

    /// <summary>
    /// M4-4b-2: whether a live zone hides what stands at <paramref name="target"/> (m) from a viewer of
    /// <paramref name="viewerOwner"/> at <paramref name="viewer"/>: a zone that blocks vision, of another owner, whose circle
    /// holds the centre of <paramref name="target"/>'s cell (the fog's rule for a cell), with the viewer outside it (its
    /// center not within the radius; a building, <paramref name="viewerIsUnit"/> false, never views from inside). Live zones,
    /// not the last fog update's: the rule holds from the tick a zone is made to the tick it is freed.
    /// </summary>
    internal static bool ZoneHides(World world, int viewerOwner, Vector2 viewer, bool viewerIsUnit, Vector2 target)
    {
        ZoneStore z = world.Zones;
        if (z.BlockerCount == 0) return false;
        Vector2 cell = world.Fog.CellCentreOf(target);
        for (int k = 0; k < z.End; k++)
        {
            if (!z.Alive[k] || !z.BlocksVision(k) || z.Owner[k] == viewerOwner || !z.Contains(k, cell)) continue;
            if (!viewerIsUnit || !z.Contains(k, viewer)) return true;
        }
        return false;
    }

    /// <summary>Whether unit <paramref name="i"/>'s owner may take <paramref name="target"/> (a unit, or a building with <paramref name="isBuilding"/>; live, checked by the caller).</summary>
    internal static bool UnitSees(World world, int i, EntityHandle target, bool isBuilding) =>
        isBuilding ? UnitSeesBuilding(world, i, target.Index) : UnitSeesUnit(world, i, target.Index);

    /// <summary>
    /// A hit by <paramref name="attacker"/> (alive, standing on <paramref name="attackerLevel"/>: its cell's level at the
    /// melee hit, or at firing for a projectile) on something of <paramref name="victimOwner"/> standing on
    /// <paramref name="victimLevel"/>: from a higher level it reveals the attacker to the victim's owner for
    /// <see cref="VisionConstants.HighGroundRevealTicks"/> (docs/02 "High ground"; refreshed by every such hit).
    /// </summary>
    internal static void OnHit(World world, EntityHandle attacker, int attackerLevel, int victimOwner, int victimLevel)
    {
        if (attackerLevel <= victimLevel || (uint)victimOwner >= (uint)world.Config.PlayerCount) return;
        UnitStore u = world.Units;
        if (!u.IsAlive(attacker) || u.Owner[attacker.Index] == victimOwner) return;
        // (A building's hit never comes here: reveals are per unit slot, M4-3b; the callers skip it.)
        world.Fog.Reveal(attacker, victimOwner, world.TickNumber + VisionConstants.HighGroundRevealTicks);
    }
}
