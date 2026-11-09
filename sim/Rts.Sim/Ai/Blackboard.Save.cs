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

    /// <summary>
    /// M5-4d: writes the hunt's fields (the last enemy unit seen, the hunt flag, the sweep, its origin, marks, perimeter leg and passes, the count) for the
    /// save's <c>HUNT</c> section (formats 6 and 7). Kept out of <see cref="WriteTo"/> so the <c>AIST</c> layout is format 5's
    /// byte for byte, and a format-6 save is a format-4 one plus this section.
    /// </summary>
    internal void WriteHuntTo(SaveWriter w)
    {
        w.Write(EnemyUnitPoint);
        w.Write(EnemyUnitTick);
        w.Write(Hunting);
        w.Write(HuntSweep);
        w.Write(HuntOrigin);
        w.WriteArray(HuntVisited);
        w.Write(HuntRing);
        w.Write(HuntRingLeft);
        w.Write(HuntPass);
        w.Write(HuntsStarted);
    }

    /// <summary>Reads what <see cref="WriteHuntTo"/> wrote over this board (the sweep index range-checked, a hunt only while a wave is in progress).</summary>
    internal void ReadHuntFrom(SaveReader r)
    {
        EnemyUnitPoint = r.ReadVector2();
        EnemyUnitTick = r.ReadInt32();
        Hunting = r.ReadBool();
        HuntSweep = r.ReadInt32In(-1, HuntVisited.Length - 1);
        HuntOrigin = r.ReadVector2();
        r.ReadBools(HuntVisited);
        HuntRing = r.ReadInt32In(-1, HuntVisited.Length - 1);
        HuntRingLeft = r.ReadInt32In(0, HuntVisited.Length);
        r.Check((HuntRing < 0) == (HuntRingLeft == 0));
        HuntPass = r.ReadInt32In(0, int.MaxValue);
        HuntsStarted = r.ReadInt32();
        r.Check(!Hunting || WaveActive);
    }

    /// <summary>
    /// M5-9a: writes the counter memory (<see cref="SeenSlotCount"/>, <see cref="SeenSlotTick"/>) for the save's <c>CTRM</c>
    /// section (formats 8 and 9). Kept out of <see cref="WriteTo"/> and <see cref="WriteHuntTo"/> so a format-8 save is a
    /// format-6 one plus this section.
    /// </summary>
    internal void WriteCountersTo(SaveWriter w)
    {
        w.WriteArray(SeenSlotCount);
        w.WriteArray(SeenSlotTick);
        w.Write(StarvedSince); // M5-9a round 2: the starved stamp, after the memory (same formats 8 / 9)
        // M5-9b: the far expansion, after the stamp (same formats 8 / 9: neither is on main yet).
        w.Write(FarPhase);
        w.Write(FarLooked);
        w.Write(FarStart);
        w.Write(FarLook);
        w.Write(FarMoved);
        w.Write(FarScoutSlot);
        w.Write(FarScoutGeneration);
        Write(w, FarPending);
        w.Write(FarSiteType);
        w.Write(FarSiteCell);
        w.Write(FarNoSiteUntil);
        w.Write(FarExpansions);
    }

    /// <summary>
    /// Reads what <see cref="WriteCountersTo"/> wrote over this board: each count 0 to <paramref name="unitSlots"/> (no more
    /// units than the match holds), each tick 0 to <paramref name="now"/> (no sighting after the seat's last think); round 2:
    /// the starved stamp -1 (none) or 0 to <paramref name="now"/> (no stamp after the seat's last think).
    /// </summary>
    internal void ReadCountersFrom(SaveReader r, int unitSlots, int now, int starts, int buildingTypes, int cells)
    {
        r.ReadArray(SeenSlotCount);
        r.ReadArray(SeenSlotTick);
        for (int s = 0; s < SlotCount && !r.Failed; s++)
            r.Check(SeenSlotCount[s] >= 0 && SeenSlotCount[s] <= unitSlots && SeenSlotTick[s] >= 0 && SeenSlotTick[s] <= now);
        StarvedSince = r.ReadInt32();
        r.Check(StarvedSince == -1 || StarvedSince >= 0 && StarvedSince <= now);
        ReadFarFrom(r, unitSlots, starts, buildingTypes, cells);
    }

    /// <summary>
    /// M5-9b: the far expansion's fields, each bounded (<c>Malformed</c> otherwise): the phase 0-2; the looked-at bits only for
    /// start locations the map has (at most <see cref="FarExpansion.MaxStarts"/>); the start walked to -1 or a start, its look
    /// point in range; the leg's worker -1 or a unit slot (with a generation above 0); the pending order none or a Build; the
    /// site -1 / -1 or a building type and a cell; and the state consistent (no leg, Build or site while off, no site while
    /// a Build waits).
    /// </summary>
    private void ReadFarFrom(SaveReader r, int unitSlots, int starts, int buildingTypes, int cells)
    {
        FarPhase = r.ReadByte();
        r.Check(FarPhase <= FarExpansion.Building);
        FarLooked = r.ReadInt32();
        int usable = System.Math.Min(starts, FarExpansion.MaxStarts);
        r.Check(FarLooked >= 0 && (usable >= 31 || FarLooked >> usable == 0));
        FarStart = r.ReadInt32In(-1, usable - 1);
        FarLook = r.ReadInt32In(0, FarExpansion.LookPoints - 1);
        FarMoved = r.ReadBool();
        FarScoutSlot = r.ReadInt32In(-1, unitSlots - 1);
        FarScoutGeneration = r.ReadInt32();
        r.Check(FarScoutSlot < 0 ? FarScoutGeneration == 0 : FarScoutGeneration > 0);
        FarPending = ReadPending(r);
        r.Check(FarPending.Kind is PendingKind.None or PendingKind.Build);
        FarSiteType = r.ReadInt32In(-1, buildingTypes - 1);
        FarSiteCell = r.ReadInt32In(-1, cells - 1);
        r.Check((FarSiteType < 0) == (FarSiteCell < 0));
        FarNoSiteUntil = r.ReadInt32();
        FarExpansions = r.ReadInt32In(0, int.MaxValue);
        // Off: no leg, no Build, no site. Armed: a leg perhaps, no Build, no site. Building: no leg, and exactly one of a
        // Build waiting and a site standing.
        bool leg = FarStart >= 0 || FarScoutSlot >= 0, build = FarPending.Kind != PendingKind.None, site = FarSiteType >= 0;
        r.Check(FarPhase switch
        {
            0 => !leg && !build && !site,
            FarExpansion.Armed => !build && !site,
            _ => !leg && build != site,
        });
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
