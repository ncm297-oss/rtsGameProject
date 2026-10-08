using System.Numerics;
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
    /// tick, so a unit never misses what its own circle covers between updates.
    /// </summary>
    internal static bool UnitSeesUnit(World world, int i, int j)
    {
        UnitStore u = world.Units;
        FogStore fog = world.Fog;
        float sight = world.Data.Units[u.TypeId[i]].Sight;
        float d2 = Vector2.DistanceSquared(u.Position[i], u.Position[j]);
        if (d2 <= sight * sight && (!fog.MultiLevel || d2 <= VisionConstants.LipRadius * VisionConstants.LipRadius
            || fog.LevelAt(u.Position[j]) <= fog.LevelAt(u.Position[i])))
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
            || fog.BuildingLevel(j) <= fog.LevelAt(u.Position[i])))
            return true;
        return fog.SeesBuildingCells(u.Owner[i], j);
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
        world.Fog.Reveal(attacker, victimOwner, world.TickNumber + VisionConstants.HighGroundRevealTicks);
    }
}
