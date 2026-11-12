using Rts.Sim.Commands;
using Rts.Sim.Orders;
using Rts.Sim.Save;

namespace Rts.Sim.Entities;

// M6-S2: the unit store in a save, every array whole (docs/03 "Save/load and replays"): the hashed fields, the ones outside
// the hash that decide later ticks (Flank, the counted population and live flags, the free list) and the render ones
// (PrevPosition, PrevFacing), plus the statuses. M7-1 (format 10): then the regeneration part (REGN): the last damage tick and
// the accumulator of every slot, and the statuses' derived regenerating counts.
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
        // M7-1 (format 10): regeneration, its own tagged part at the end of the store, after the statuses.
        w.Tag(SaveFormat.TagRegen);
        w.WriteArray(LastDamagedTick);
        w.WriteArray(RegenAccum);
        Statuses.WriteRegenTo(w);
    }

    /// <summary>
    /// Reads what <see cref="WriteTo"/> wrote into this store of the same capacity (M6-S2). Checks the structure the next
    /// tick indexes by: the free count, every live slot's owner below <paramref name="players"/> and type below
    /// <paramref name="unitTypes"/>, the free list's slots, the queue counts, the enum values and every status id (below
    /// <paramref name="statuses"/>' length). BUG-1165: the regeneration part against the entries and the save's
    /// <paramref name="tick"/> (a live unit's last damage tick is -1 or at most <paramref name="tick"/>; a free slot's is -1
    /// with an empty accumulator; the regenerating counts equal the entries).
    /// </summary>
    internal void ReadFrom(SaveReader r, int players, int unitTypes, System.Collections.Immutable.ImmutableArray<Data.StatusDef> statuses, int tick)
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
        Statuses.ReadFrom(r, statuses.Length);
        if (r.Failed || !r.Expect(SaveFormat.TagRegen)) return;
        r.ReadArray(LastDamagedTick);
        r.ReadArray(RegenAccum);
        Statuses.ReadRegenFrom(r, statuses);
        if (r.Failed) return;

        int live = 0;
        for (int i = 0; i < Capacity; i++)
        {
            if (!r.Check(QueueCount[i] >= 0 && QueueCount[i] <= OrderConstants.QueueCapacity)) return;
            if (!Alive[i])
            {
                // BUG-1165: freeing a slot clears both (UnitStore.Free), so a free slot carrying either is forged.
                if (!r.Check(LastDamagedTick[i] == -1 && RegenAccum[i] == 0)) return;
                continue;
            }
            live++;
            if (!r.Check((uint)Owner[i] < (uint)players && (uint)TypeId[i] < (uint)unitTypes)) return;
            // M7-1: a fraction of a hit point, and (BUG-1165) a tick that has been: none, or at most the save's own.
            if (!r.Check(RegenAccum[i] >= 0 && RegenAccum[i] < Combat.CombatConstants.RegenFixedOne && LastDamagedTick[i] >= -1 && LastDamagedTick[i] <= tick)) return;
        }
        if (!r.Check(live == Capacity - _freeCount)) return;
        for (int k = 0; k < _freeCount; k++)
            if (!r.Check((uint)_freeList[k] < (uint)Capacity && !Alive[_freeList[k]])) return;
    }
}
