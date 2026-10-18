using Rts.Sim.Commands;
using Rts.Sim.Orders;
using Rts.Sim.Save;

namespace Rts.Sim.Entities;

// M6-S2: the unit store in a save, every array whole (docs/03 "Save/load and replays"): the hashed fields, the ones outside
// the hash that decide later ticks (Flank, the counted population and live flags, the free list) and the render ones
// (PrevPosition, PrevFacing), plus the statuses.
public sealed partial class UnitStore
{
    /// <summary>Writes every slot's every field, the free list and the derived counters (M6-S2).</summary>
    internal void WriteTo(SaveWriter w)
    {
        w.Tag(SaveFormat.TagUnits);
        w.Write(Capacity);
        w.WriteArray(Position);
        w.WriteArray(PrevPosition);
        w.WriteArray(Velocity);
        w.WriteArray(Facing);
        w.WriteArray(PrevFacing);
        w.WriteArray(Owner);
        w.WriteArray(TypeId);
        w.WriteArray(Speed);
        w.WriteArray(Radius);
        w.WriteArray(State);
        w.WriteArray(Goal);
        w.WriteArray(GoalCell);
        w.WriteArray(OrderTick);
        w.WriteArray(StuckTicks);
        w.WriteArray(BestRemaining);
        w.WriteArray(WalkBack);
        w.WriteArray(Flank);
        w.WriteArray(Hold);
        w.WriteArray(QueueCount);
        w.WriteArray(QueueKind);
        w.WriteArray(QueuePosition);
        w.WriteArray(QueueTypeId);
        w.WriteArray(BuildTarget);
        w.WriteArray(GatherNode);
        w.WriteArray(GatherSite);
        w.WriteArray(GatherProgress);
        w.WriteArray(Cargo);
        w.WriteArray(CargoKind);
        w.WriteArray(Hp);
        w.WriteArray(Target);
        w.WriteArray(TargetIsBuilding);
        w.WriteArray(CooldownTicks);
        w.WriteArray(WindupTicks);
        w.WriteArray(LastAttacker);
        w.WriteArray(AnchorPosition);
        w.WriteArray(Mode);
        w.WriteArray(ChaseBest);
        w.WriteArray(ChaseStall);
        w.WriteArray(Ignored);
        w.WriteArray(IgnoredIsBuilding);
        w.WriteArray(ChasePrev);
        w.WriteArray(ChasePrevIsBuilding);
        w.WriteArray(ChaseSwitches);
        w.WriteArray(ChaseChainBest);
        w.WriteArray(GiveUps);
        w.WriteArray(Repick);
        w.WriteArray(CastAbility);
        w.WriteArray(CastTicks);
        w.WriteArray(CastPoint);
        w.WriteArray(AbilityReadyTick);
        w.WriteArray(ExpiresAtTick);
        w.WriteArray(Alive);
        w.WriteArray(Generation);
        w.WriteArray(_belowHp);
        w.WriteArray(_freeList);
        w.WriteArray(_countedHalfPop);
        w.WriteArray(_countedLive);
        w.Write(_freeCount);
        w.Write(CasterCount);
        w.Write(BelowHpUnits);
        w.Write(SummonedUnits);
        Statuses.WriteTo(w);
    }

    /// <summary>
    /// Reads what <see cref="WriteTo"/> wrote into this store of the same capacity (M6-S2). Checks the structure the next
    /// tick indexes by: the free count, every live slot's owner below <paramref name="players"/> and type below
    /// <paramref name="unitTypes"/>, the free list's slots, the queue counts, the enum values and every status id (below
    /// <paramref name="statuses"/>).
    /// </summary>
    internal void ReadFrom(SaveReader r, int players, int unitTypes, int statuses)
    {
        if (!r.Expect(SaveFormat.TagUnits) || !r.Check(r.ReadInt32() == Capacity)) return;
        r.ReadArray(Position);
        r.ReadArray(PrevPosition);
        r.ReadArray(Velocity);
        r.ReadArray(Facing);
        r.ReadArray(PrevFacing);
        r.ReadArray(Owner);
        r.ReadArray(TypeId);
        r.ReadArray(Speed);
        r.ReadArray(Radius);
        r.ReadByteEnums(State, (byte)UnitState.Casting);
        r.ReadArray(Goal);
        r.ReadArray(GoalCell);
        r.ReadArray(OrderTick);
        r.ReadArray(StuckTicks);
        r.ReadArray(BestRemaining);
        r.ReadArray(WalkBack);
        r.ReadArray(Flank);
        r.ReadBools(Hold);
        r.ReadArray(QueueCount);
        r.ReadIntEnums(QueueKind, (int)CommandKind.UseAbility);
        r.ReadArray(QueuePosition);
        r.ReadArray(QueueTypeId);
        r.ReadArray(BuildTarget);
        r.ReadArray(GatherNode);
        r.ReadArray(GatherSite);
        r.ReadArray(GatherProgress);
        r.ReadArray(Cargo);
        r.ReadIntEnums(CargoKind, (int)Data.ResourceKind.Wood);
        r.ReadArray(Hp);
        r.ReadArray(Target);
        r.ReadBools(TargetIsBuilding);
        r.ReadArray(CooldownTicks);
        r.ReadArray(WindupTicks);
        r.ReadArray(LastAttacker);
        r.ReadArray(AnchorPosition);
        r.ReadByteEnums(Mode, (byte)Combat.CombatMode.Ordered);
        r.ReadArray(ChaseBest);
        r.ReadArray(ChaseStall);
        r.ReadArray(Ignored);
        r.ReadBools(IgnoredIsBuilding);
        r.ReadArray(ChasePrev);
        r.ReadBools(ChasePrevIsBuilding);
        r.ReadArray(ChaseSwitches);
        r.ReadArray(ChaseChainBest);
        r.ReadArray(GiveUps);
        r.ReadBools(Repick);
        r.ReadArray(CastAbility);
        r.ReadArray(CastTicks);
        r.ReadArray(CastPoint);
        r.ReadArray(AbilityReadyTick);
        r.ReadArray(ExpiresAtTick);
        r.ReadBools(Alive);
        r.ReadArray(Generation);
        r.ReadBools(_belowHp);
        r.ReadArray(_freeList);
        r.ReadArray(_countedHalfPop);
        r.ReadBools(_countedLive);
        _freeCount = r.ReadInt32In(0, Capacity);
        CasterCount = r.ReadInt32In(0, Capacity);
        BelowHpUnits = r.ReadInt32In(0, Capacity);
        SummonedUnits = r.ReadInt32In(0, Capacity);
        Statuses.ReadFrom(r, statuses);
        if (r.Failed) return;

        int live = 0;
        for (int i = 0; i < Capacity; i++)
        {
            if (!r.Check(QueueCount[i] >= 0 && QueueCount[i] <= OrderConstants.QueueCapacity)) return;
            if (!Alive[i]) continue;
            live++;
            if (!r.Check((uint)Owner[i] < (uint)players && (uint)TypeId[i] < (uint)unitTypes)) return;
        }
        if (!r.Check(live == Capacity - _freeCount)) return;
        for (int k = 0; k < _freeCount; k++)
            if (!r.Check((uint)_freeList[k] < (uint)Capacity && !Alive[_freeList[k]])) return;
    }
}
