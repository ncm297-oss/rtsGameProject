using Rts.Sim.Determinism;
using Rts.Sim.Save;

namespace Rts.Sim.Ai;

// M6-S3: an AI seat in a save (docs/03 "Save format (M6-S2)", slice 2). The load builds the seat as a match does, so the
// opening is picked from the seed by the one draw construction makes; the stored pick is then checked against it (a
// mismatch is a forged or foreign save: Malformed) and the stored stream state written over the fresh one. Then the
// counters, the census and the planning scratch (all rebuilt at the start of a think, stored anyway so a loaded seat is
// the saved one field for field), and the Blackboard whole. The view holds nothing of its own (PlayerView is stateless:
// what the player remembers is the fog's, saved in FOG).
internal sealed partial class AiPlayer
{
    /// <summary>Writes the seat's pick, stream, counters, census, scratch and <see cref="Board"/> (M6-S3).</summary>
    internal void WriteTo(SaveWriter w)
    {
        w.Write(OpeningIndex);
        w.Write(_rng.State);
        w.Write(_rng.Increment);
        w.Write(_queueFull);
        w.Write(NoSiteFound);
        w.Write(SiteSearches);
        w.Write(Thinks);
        w.Write(CommandsSent);

        // The census and the think's purse (rebuilt at every think's start).
        w.Write(Now);
        w.WriteArray(Live);
        w.WriteArray(Queued);
        w.WriteArray(WorkerSlots);
        w.Write(WorkerCount);
        w.WriteArray(ArmySlots);
        w.Write(ArmyCount);
        w.Write(HasDropOff);
        w.WriteArray(OwnBuildingSlots);
        w.WriteArray(OwnBuildingTypes);
        w.WriteArray(OwnBuildingCells);
        w.WriteArray(OwnBuildingSites);
        w.Write(OwnBuildingCount);
        w.Write(HasHall);
        w.Write(HallPoint);
        w.Write(IntruderCount);
        w.Write(IntruderCentroid);
        w.Write(IntruderWorth);
        w.Write(HallSlot);
        w.Write(TownHallCell);
        w.Write(Gold);
        w.Write(Wood);
        w.Write(ExecutorSent);

        // The placement scratch.
        w.WriteArray(_plannedTypes);
        w.WriteArray(_plannedCells);
        w.Write(_plannedCount);
        w.WriteArray(_mineCells);
        w.WriteArray(_mineTypes);
        w.Write(_mineCount);

        Board.WriteTo(w);
    }

    /// <summary>Reads what <see cref="WriteTo"/> wrote over this seat, freshly built for the same match (its pick already drawn).</summary>
    internal void ReadFrom(SaveReader r)
    {
        // The pick follows from the seed, the seat and the data, which the header matched: another one is not this match's.
        if (!r.Check(r.ReadInt32() == OpeningIndex)) return;
        ulong state = r.ReadUInt64(), increment = r.ReadUInt64();
        if (!r.Check(increment == _rng.Increment)) return; // the stream id is the seat's (RngStream.Ai)
        _rng = SimRng.FromWords(state, increment);
        _queueFull = r.ReadBool();
        NoSiteFound = r.ReadBool();
        SiteSearches = r.ReadInt32();
        Thinks = r.ReadInt32();
        CommandsSent = r.ReadInt32();

        Now = r.ReadInt32();
        r.ReadArray(Live);
        r.ReadArray(Queued);
        r.ReadArray(WorkerSlots);
        WorkerCount = r.ReadInt32In(0, WorkerSlots.Length);
        r.ReadArray(ArmySlots);
        ArmyCount = r.ReadInt32In(0, ArmySlots.Length);
        HasDropOff = r.ReadBool();
        r.ReadArray(OwnBuildingSlots);
        r.ReadArray(OwnBuildingTypes);
        r.ReadArray(OwnBuildingCells);
        r.ReadBools(OwnBuildingSites);
        OwnBuildingCount = r.ReadInt32In(0, OwnBuildingSlots.Length);
        HasHall = r.ReadBool();
        HallPoint = r.ReadVector2();
        IntruderCount = r.ReadInt32();
        IntruderCentroid = r.ReadVector2();
        IntruderWorth = r.ReadInt32();
        HallSlot = r.ReadInt32();
        TownHallCell = r.ReadInt32();
        Gold = r.ReadInt32();
        Wood = r.ReadInt32();
        ExecutorSent = r.ReadBool();

        r.ReadArray(_plannedTypes);
        r.ReadArray(_plannedCells);
        _plannedCount = r.ReadInt32In(0, PlannedScratch);
        r.ReadArray(_mineCells);
        r.ReadArray(_mineTypes);
        _mineCount = r.ReadInt32In(0, MineScratch);

        Board.ReadFrom(r);
    }
}
