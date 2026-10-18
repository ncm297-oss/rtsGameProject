using Rts.Sim.Save;

namespace Rts.Sim.Entities;

// M6-S2: the projectiles in flight in a save, whole, with the loop bounds (the first free slot, the end) and the count.
public sealed partial class ProjectileStore
{
    /// <summary>Writes every slot and the count and loop bounds (M6-S2).</summary>
    internal void WriteTo(SaveWriter w)
    {
        w.Tag(SaveFormat.TagProjectiles);
        w.Write(Capacity);
        w.WriteArray(_alive);
        w.WriteArray(_position);
        w.WriteArray(_prevPosition);
        w.WriteArray(_velocity);
        w.WriteArray(_target);
        w.WriteArray(_ticksLeft);
        w.WriteArray(_typeId);
        w.WriteArray(_owner);
        w.WriteArray(_attackerType);
        w.WriteArray(_attacker);
        w.WriteArray(_attackerIsBuilding);
        w.WriteArray(_victim);
        w.WriteArray(_victimIsBuilding);
        w.WriteArray(_level);
        w.Write(Count);
        w.Write(_firstFree);
        w.Write(_end);
    }

    /// <summary>
    /// Reads what <see cref="WriteTo"/> wrote into this store of the same capacity; checks the count and bounds and every
    /// live slot's projectile type (below <paramref name="projectileTypes"/>) and attacker type (below
    /// <paramref name="unitTypes"/>, or <paramref name="buildingTypes"/> for a tower's shot).
    /// </summary>
    internal void ReadFrom(SaveReader r, int projectileTypes, int unitTypes, int buildingTypes)
    {
        if (!r.Expect(SaveFormat.TagProjectiles) || !r.Check(r.ReadInt32() == Capacity)) return;
        r.ReadBools(_alive);
        r.ReadArray(_position);
        r.ReadArray(_prevPosition);
        r.ReadArray(_velocity);
        r.ReadArray(_target);
        r.ReadArray(_ticksLeft);
        r.ReadArray(_typeId);
        r.ReadArray(_owner);
        r.ReadArray(_attackerType);
        r.ReadArray(_attacker);
        r.ReadBools(_attackerIsBuilding);
        r.ReadArray(_victim);
        r.ReadBools(_victimIsBuilding);
        r.ReadArray(_level);
        Count = r.ReadInt32In(0, Capacity);
        _firstFree = r.ReadInt32In(0, Capacity);
        _end = r.ReadInt32In(0, Capacity);
        if (r.Failed) return;
        int live = 0;
        for (int i = 0; i < Capacity; i++)
        {
            if (!_alive[i]) continue;
            live++;
            int attackerTypes = _attackerIsBuilding[i] ? buildingTypes : unitTypes;
            if (!r.Check(i < _end && (uint)_typeId[i] < (uint)projectileTypes && (uint)_attackerType[i] < (uint)attackerTypes && _ticksLeft[i] >= 0)) return;
        }
        r.Check(live == Count);
    }
}
