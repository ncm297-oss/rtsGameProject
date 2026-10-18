using Rts.Sim.Save;

namespace Rts.Sim.Ai;

// M6-S3: an AI's bookkeeping in a save, whole (docs/03 "Save format (M6-S2)", slice 2): every array and scalar in a fixed
// order, the per-think scratch too (SeenEnemies, DefenseOrder / DefenseKey, SiteBuiltAt): cheaper to store than to prove
// that no think reads one before rewriting it. The arrays' lengths follow from the match and the opening, so the reader
// requires exactly the freshly built board's; the counts and cursors that index an array are range-checked.
internal sealed partial class Blackboard
{
    /// <summary>Writes every field of the board (M6-S3).</summary>
    internal void WriteTo(SaveWriter w)
    {
        // Build-order steps and the pending orders.
        w.WriteArray(StepDueTick);
        w.WriteArray(StepQueued);
        w.WriteArray(StepDone);
        w.Write(StepPending.Length);
        foreach (PendingOrder p in StepPending) Write(w, p);
        w.WriteArray(StepNoSiteUntil);
        Write(w, HousePending);
        Write(w, ProductionPending);

        // Retry marks.
        w.WriteArray(_badType);
        w.WriteArray(_badCell);
        w.Write(_badNext);

        // Per unit and building slot orders, gathering.
        w.WriteArray(UnitOrderedUntil);
        w.WriteArray(UnitGeneration);
        w.WriteArray(GatherKind);
        w.WriteArray(GatherNode);
        w.WriteArray(NodeDropped);
        w.Write(NodeMarks);
        w.WriteArray(BuildingBusyUntil);

        // Own construction sites.
        w.WriteArray(SiteGeneration);
        w.WriteArray(SiteBuiltAt);
        w.WriteArray(SiteStill);
        w.WriteArray(SiteJoins);
        w.WriteArray(SiteJoinHp);

        // The scout and the enemy memory.
        w.Write(ScoutPhase);
        w.Write(ScoutSlot);
        w.Write(ScoutGeneration);
        w.Write(ScoutTarget);
        w.WriteArray(ScoutPoints);
        w.WriteArray(ScoutVisited);
        w.Write(KnownEnemyBuildings);
        w.Write(EnemyArmyCentroid);
        w.Write(EnemyArmyCount);
        w.Write(EnemyArmyTick);

        // The military: rally and wave.
        w.Write(Rally);
        w.Write(RallyVersion);
        w.WriteArray(RalliedVersion);
        w.Write(WaveActive);
        w.Write(WaveTarget);
        w.Write(WaveTargetType);
        w.Write(WaveTargetCell);
        w.Write(WaveTargetPoint);
        w.Write(WaveStartHalfPop);
        w.Write(WaveStartTick);
        w.Write(WaveMinHalfPop);
        w.Write(WaveMinCost);
        w.Write(WaveBusyTick);
        w.Write(WaveCooldownUntil);
        w.Write(WavesStarted);
        w.WriteArray(WaveGeneration);
        w.WriteArray(WaveFlag);
        w.WriteArray(WaveTries);
        w.WriteArray(WaveBest);

        // The defense.
        w.WriteArray(KeptHp);
        w.WriteArray(KeptHpGeneration);
        w.Write(DefendTick);
        w.Write(Defending);
        w.Write(DefensePoint);
        w.Write(DefenseVersion);
        w.Write(DefensePointAway);
        w.Write(Defenses);
        w.WriteArray(DefendedVersion);
        w.WriteArray(FledGeneration);
        w.WriteArray(FledAt);
        w.WriteArray(Homing);
        w.WriteArray(DefenderGeneration);
        w.Write(DefendAnswer);
        w.Write(DefendAll);
        w.WriteArray(DefenseOrder);
        w.WriteArray(DefenseKey);
        w.Write(DefenseOrderCount);
        w.Write(WavePulls);
        w.Write(WaveRecalls);

        // The abilities: per-caster marks, the enemy scratch, the recent ground casts.
        w.WriteArray(CastGeneration);
        w.WriteArray(CastIndex);
        w.WriteArray(CastTries);
        w.WriteArray(CastReadyAt);
        w.WriteArray(CastAt);
        w.WriteArray(SeenEnemies);
        w.Write(SeenEnemyCount);
        w.Write(CastsSent);
        w.WriteArray(RecentCastAbility);
        w.WriteArray(RecentCastAt);
        w.WriteArray(RecentCastUntil);
        w.WriteArray(RecentCastSlot);
        w.WriteArray(RecentCastGeneration);
        w.WriteArray(RecentCastIndex);
        w.WriteArray(RecentCastReadyAt);
        w.Write(RecentCastCount);
    }

    /// <summary>Reads what <see cref="WriteTo"/> wrote over this board, built for the same match and opening.</summary>
    internal void ReadFrom(SaveReader r)
    {
        int unitSlots = UnitOrderedUntil.Length;

        r.ReadArray(StepDueTick);
        r.ReadArray(StepQueued);
        r.ReadBools(StepDone);
        if (!r.ExpectLength(StepPending.Length)) return;
        for (int k = 0; k < StepPending.Length; k++) StepPending[k] = ReadPending(r);
        r.ReadArray(StepNoSiteUntil);
        HousePending = ReadPending(r);
        ProductionPending = ReadPending(r);

        r.ReadArray(_badType);
        r.ReadArray(_badCell);
        _badNext = r.ReadInt32In(0, RetryMarks - 1);

        r.ReadArray(UnitOrderedUntil);
        r.ReadArray(UnitGeneration);
        r.ReadArray(GatherKind);
        r.ReadArray(GatherNode);
        r.ReadArray(NodeDropped);
        NodeMarks = r.ReadInt32In(0, NodeDropped.Length);
        r.ReadArray(BuildingBusyUntil);

        r.ReadArray(SiteGeneration);
        r.ReadArray(SiteBuiltAt);
        r.ReadArray(SiteStill);
        r.ReadArray(SiteJoins);
        r.ReadArray(SiteJoinHp);

        ScoutPhase = r.ReadByte();
        r.Check(ScoutPhase <= 2);
        ScoutSlot = r.ReadInt32();
        ScoutGeneration = r.ReadInt32();
        ScoutTarget = r.ReadInt32In(-1, ScoutPoints.Length - 1);
        r.ReadArray(ScoutPoints);
        r.ReadBools(ScoutVisited);
        KnownEnemyBuildings = r.ReadInt32();
        EnemyArmyCentroid = r.ReadVector2();
        EnemyArmyCount = r.ReadInt32();
        EnemyArmyTick = r.ReadInt32();

        Rally = r.ReadVector2();
        RallyVersion = r.ReadInt32();
        r.ReadArray(RalliedVersion);
        WaveActive = r.ReadBool();
        WaveTarget = r.ReadHandle();
        WaveTargetType = r.ReadInt32();
        WaveTargetCell = r.ReadInt32();
        WaveTargetPoint = r.ReadVector2();
        WaveStartHalfPop = r.ReadInt32();
        WaveStartTick = r.ReadInt32();
        WaveMinHalfPop = r.ReadFloat();
        WaveMinCost = r.ReadFloat();
        WaveBusyTick = r.ReadInt32();
        WaveCooldownUntil = r.ReadInt32();
        WavesStarted = r.ReadInt32();
        r.ReadArray(WaveGeneration);
        r.ReadArray(WaveFlag);
        r.ReadArray(WaveTries);
        r.ReadArray(WaveBest);

        r.ReadArray(KeptHp);
        r.ReadArray(KeptHpGeneration);
        DefendTick = r.ReadInt32();
        Defending = r.ReadBool();
        DefensePoint = r.ReadVector2();
        DefenseVersion = r.ReadInt32();
        DefensePointAway = r.ReadBool();
        Defenses = r.ReadInt32();
        r.ReadArray(DefendedVersion);
        r.ReadArray(FledGeneration);
        r.ReadArray(FledAt);
        r.ReadBools(Homing);
        r.ReadArray(DefenderGeneration);
        DefendAnswer = r.ReadFloat();
        DefendAll = r.ReadBool();
        r.ReadArray(DefenseOrder);
        r.ReadArray(DefenseKey);
        DefenseOrderCount = r.ReadInt32In(0, unitSlots);
        WavePulls = r.ReadInt32();
        WaveRecalls = r.ReadInt32();

        r.ReadArray(CastGeneration);
        r.ReadArray(CastIndex);
        r.ReadArray(CastTries);
        r.ReadArray(CastReadyAt);
        r.ReadArray(CastAt);
        r.ReadArray(SeenEnemies);
        SeenEnemyCount = r.ReadInt32In(-1, unitSlots); // -1: not filled this think
        CastsSent = r.ReadInt32();
        r.ReadArray(RecentCastAbility);
        r.ReadArray(RecentCastAt);
        r.ReadArray(RecentCastUntil);
        r.ReadArray(RecentCastSlot);
        r.ReadArray(RecentCastGeneration);
        r.ReadArray(RecentCastIndex);
        r.ReadArray(RecentCastReadyAt);
        RecentCastCount = r.ReadInt32In(0, AbilityManager.MaxRecentCasts);
    }

    /// <summary>A pending order field by field (its byte kind leaves padding, so it isn't written as raw bytes).</summary>
    private static void Write(SaveWriter w, in PendingOrder p)
    {
        w.Write((byte)p.Kind);
        w.Write(p.AppliesAt);
        w.Write(p.TypeId);
        w.Write(p.Cell);
        w.Write(p.Slot);
        w.Write(p.Generation);
        w.Write(p.QueuedOfType);
        w.Write(p.HeadMayPopAt);
    }

    private static PendingOrder ReadPending(SaveReader r)
    {
        byte kind = r.ReadByte();
        r.Check(kind <= (byte)PendingKind.Research);
        return new PendingOrder
        {
            Kind = (PendingKind)kind, AppliesAt = r.ReadInt32(), TypeId = r.ReadInt32(), Cell = r.ReadInt32(), Slot = r.ReadInt32(),
            Generation = r.ReadInt32(), QueuedOfType = r.ReadInt32(), HeadMayPopAt = r.ReadInt32(),
        };
    }
}
