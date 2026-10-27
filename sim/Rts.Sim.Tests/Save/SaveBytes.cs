using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Numerics;
using System.Text;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Save;

namespace Rts.Sim.Tests.Save;

/// <summary>
/// M6-S2 test helpers on raw save bytes: a small save (every capacity 1 on a 16 x 16 flat map, so a sweep over every byte
/// is quick), the header's field offsets as the format table lays them out, and re-checksumming a forged copy.
/// </summary>
internal static class SaveBytes
{
    /// <summary>A small sim between ticks: one unit walking, a pending Move queued, on a flat 16 map; every capacity 1, no stats samples.</summary>
    internal static Simulation SmallSim()
    {
        var config = new SimConfig(5, 1, 1, 2)
        {
            Data = TestSim.Data, ResourceCapacity = 1, BuildingCapacity = 1, ZoneCapacity = 1, ProjectileCapacity = 1, StatsSampleCapacity = 0,
        };
        var sim = new Simulation(config, LocalMovementTests.Flat(16));
        sim.Enqueue(Command.SpawnUnit(0, CombatScenes.HeavyInfantry, new Vector2(8f, 8f)));
        sim.Tick();
        sim.Tick();
        sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 0), new Vector2(22f, 20f)));
        sim.Tick();
        sim.Tick();
        sim.Enqueue(Command.Move(0, MoveScenario.Handle(sim, 0), new Vector2(10f, 24f), queued: true));
        return sim;
    }

    /// <summary>The small sim's save.</summary>
    internal static byte[] Small() => SaveTestRun.Save(SmallSim());

    /// <summary>
    /// M6-S3: the small sim with an AI seat: one Malazan AI at Normal (so its opening is a draw) with a hall and a worker on
    /// the flat 16 map, every capacity small, ticked past its first two thinks so its Blackboard holds something.
    /// </summary>
    internal static Simulation SmallAiSim()
    {
        var config = new SimConfig(5, 1, 2, 8)
        {
            Data = TestSim.Data, ResourceCapacity = 1, BuildingCapacity = 2, ZoneCapacity = 1, ProjectileCapacity = 1, StatsSampleCapacity = 0,
            Seats = ImmutableArray.Create(new PlayerSeat(0, SeatController.Ai, AiDifficulty.Normal)),
        };
        var sim = new Simulation(config, LocalMovementTests.Flat(16));
        sim.Enqueue(Command.SpawnBuilding(0, TestSim.Data.FindBuilding("malazan_garrison_keep"), new Vector2(10f, 10f)));
        sim.Enqueue(Command.SpawnUnit(0, CombatScenes.Laborer, new Vector2(20f, 20f)));
        int ticks = 2 * sim.AiOf(0)!.ThinkTicks + 1;
        for (int t = 0; t < ticks; t++) sim.Tick();
        return sim;
    }

    /// <summary>The small AI sim's save.</summary>
    internal static byte[] SmallAi() => SaveTestRun.Save(SmallAiSim());

    /// <summary>The small save, of the human sim or (<paramref name="ai"/>) the AI one.</summary>
    internal static byte[] Small(bool ai) => ai ? SmallAi() : Small();

    /// <summary>Offset of the AI section's tag (<c>AIST</c>, the last section before <c>END!</c>) in <paramref name="bytes"/>.</summary>
    internal static int AiSection(byte[] bytes)
    {
        int at = bytes.AsSpan().LastIndexOf("AIST"u8);
        Assert.True(at > 0, "no AI section");
        return at;
    }

    /// <summary>A copy of <paramref name="bytes"/> with its trailing checksum recomputed over the (edited) bytes before it.</summary>
    internal static byte[] Rechecksum(byte[] bytes)
    {
        byte[] copy = (byte[])bytes.Clone();
        int body = copy.Length - SaveFormat.ChecksumLength;
        BinaryPrimitives.WriteUInt64LittleEndian(copy.AsSpan(body), SaveFormat.Checksum(copy.AsSpan(0, body)));
        return copy;
    }

    /// <summary>Offsets of the header fields a refusal test forges, read from <paramref name="bytes"/> in the format table's order.</summary>
    internal sealed record Offsets(int SimVersion, int DataHash, int Seed, int Players, int Seat0Faction, int Seat0Controller)
    {
        /// <summary>The capacities (unit, command, resource, building, projectile, zone) of a one-seat save, after seat 0's controller and difficulty bytes.</summary>
        public int Capacities => Seat0Controller + 2;

        /// <summary>
        /// M6-S4 (format 3): seat 0's team byte in a one-seat save, after the tick: past the six capacities, the 26 map fields,
        /// combat (1 byte), the two cast walk-in floats, the two stats ints and the tick.
        /// </summary>
        public int Seat0Team => Capacities + 4 * 6 + 4 * 26 + 1 + 4 * 4 + 4;
    }

    internal static Offsets HeaderOffsets(byte[] bytes)
    {
        int at = SaveFormat.FixedPrefix;
        int simVersion = at;
        at += 4 + BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at));
        int dataHash = at;
        int seed = at + 8;
        int players = seed + 8;
        int seat0 = players + 4;
        int controller = seat0 + 4 + BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(seat0));
        return new Offsets(simVersion, dataHash, seed, players, seat0, controller);
    }

    /// <summary>The ASCII of the string field at <paramref name="at"/> (int32 length, then the bytes).</summary>
    internal static string StringAt(byte[] bytes, int at) =>
        Encoding.ASCII.GetString(bytes, at + 4, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at)));
}
