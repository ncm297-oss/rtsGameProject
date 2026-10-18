using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using Rts.Sim.Commands;
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

    /// <summary>A copy of <paramref name="bytes"/> with its trailing checksum recomputed over the (edited) bytes before it.</summary>
    internal static byte[] Rechecksum(byte[] bytes)
    {
        byte[] copy = (byte[])bytes.Clone();
        int body = copy.Length - SaveFormat.ChecksumLength;
        BinaryPrimitives.WriteUInt64LittleEndian(copy.AsSpan(body), SaveFormat.Checksum(copy.AsSpan(0, body)));
        return copy;
    }

    /// <summary>Offsets of the header fields a refusal test forges, read from <paramref name="bytes"/> in the format table's order.</summary>
    internal sealed record Offsets(int SimVersion, int DataHash, int Seed, int Players, int Seat0Faction, int Seat0Controller);

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
