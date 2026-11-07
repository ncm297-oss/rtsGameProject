using System.Buffers.Binary;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Rts.Sim.Save;
using Rts.Sim.Tests.Save;
using Rts.Sim.ViewApi;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA 2026-11-05-1716 (M6-V17): <see cref="SaveSummary.StartingResources"/>, the view track's read-only ViewApi accessor for a
/// format-4 save's preset string. Reading it changes nothing in the sim (hash, tick, save bytes); a High save reads
/// <c>high</c>; every cut inside the preset field lists with <c>""</c>; forged lengths and non-UTF-8 bytes never throw; a
/// field after the preset (M6-S7's format-5 map id, forged here) doesn't move the preset read; a Standard save reads
/// <c>standard</c>.
/// </summary>
public class SaveSummaryPresetQaTests
{
    private static Simulation HighMatch(ulong seed, string preset = "high")
    {
        PlayerSeat[] seats = AiScenes.Seats(AiDifficulty.Normal, SeatController.Human, SeatController.Ai);
        return AiScenes.Generated(seats, seed, record: false, out _, configure: c => c with { StartingResources = preset });
    }

    // The offset of the preset's string field: right after the team bytes (format 4).
    private static int PresetAt(byte[] bytes)
    {
        SaveBytes.Offsets o = SaveBytes.HeaderOffsets(bytes);
        int players = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(o.Players));
        int at = o.Players + 4;
        for (int p = 0; p < players; p++) at += 4 + BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at)) + 2;
        return at + 4 * 6 + 4 * 26 + 1 + 4 + 4 + 4 + 4 + 4 + players;
    }

    [Fact]
    public void AHighSave_ReadsHigh_AndReadingChangesNothing()
    {
        Simulation sim = HighMatch(17);
        foreach (int tick in new[] { 0, 1, 250 })
        {
            while (sim.TickNumber < tick) sim.Tick();
            ulong hash = sim.StateHash();
            byte[] bytes = SaveTestRun.Save(sim);
            using (var stream = new MemoryStream(bytes, writable: false))
            {
                Assert.True(SaveSummary.TryRead(stream, out SaveSummary? s), $"tick {tick}: did not read");
                Assert.Equal("high", s!.StartingResources);
                Assert.Equal(Rts.Sim.Save.SaveHeader.VersionFor(false, Rts.Sim.Save.SaveHeader.HasAiSeat(sim.World.Config)), s.FormatVersion); // M5-4d: 6 with an AI seat
                Assert.Equal(sim.TickNumber, s.Tick);
            }
            Assert.Equal(hash, sim.StateHash());
            Assert.Equal(tick, sim.TickNumber);
            Assert.Equal(bytes, SaveTestRun.Save(sim));
        }
    }

    [Fact]
    public void AStandardSave_ReadsStandard()
    {
        byte[] bytes = SaveTestRun.Save(HighMatch(17, StartingResourcesDef.StandardId));
        Assert.True(SaveSummary.TryParse(bytes, out SaveSummary? s));
        Assert.Equal(StartingResourcesDef.StandardId, s!.StartingResources);
    }

    [Fact]
    public void EveryCut_InsideThePresetField_ListsWithNoPreset_AndTheWholeFieldReadsHigh()
    {
        byte[] bytes = SaveTestRun.Save(HighMatch(18));
        int at = PresetAt(bytes);
        Assert.Equal("high", SaveBytes.StringAt(bytes, at));
        int end = at + 4 + 4;
        for (int n = at; n < end; n++)
        {
            Assert.True(SaveSummary.TryParse(bytes.AsSpan(0, n), out SaveSummary? s), $"cut at {n}: not listed");
            Assert.Equal("", s!.StartingResources);
        }
        Assert.True(SaveSummary.TryParse(bytes.AsSpan(0, end), out SaveSummary? whole));
        Assert.Equal("high", whole!.StartingResources);
    }

    [Fact]
    public void ForgedPresetLengths_AndBadUtf8_ListWithNoPreset_AndNeverThrow()
    {
        byte[] good = SaveTestRun.Save(HighMatch(19));
        int at = PresetAt(good);
        foreach (int length in new[] { -1, 0, 257, int.MaxValue, int.MinValue, good.Length })
        {
            byte[] bad = (byte[])good.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(at), length);
            Assert.True(SaveSummary.TryParse(bad, out SaveSummary? s), $"length {length}: not listed");
            Assert.Equal("", s!.StartingResources);
        }
        byte[] utf = (byte[])good.Clone();
        utf[at + 4] = 0xFF; // not UTF-8
        Assert.True(SaveSummary.TryParse(utf, out SaveSummary? u));
        Assert.Equal("", u!.StartingResources);
    }

    [Fact]
    public void AFieldAfterThePreset_AsFormat5sMapId_DoesNotMoveThePresetRead()
    {
        byte[] good = SaveTestRun.Save(HighMatch(20));
        int at = PresetAt(good) + 4 + 4;
        byte[] id = System.Text.Encoding.UTF8.GetBytes("raraku");
        byte[] forged = new byte[good.Length + 4 + id.Length];
        good.AsSpan(0, at).CopyTo(forged);
        BinaryPrimitives.WriteInt32LittleEndian(forged.AsSpan(at), id.Length);
        id.CopyTo(forged.AsSpan(at + 4));
        good.AsSpan(at).CopyTo(forged.AsSpan(at + 4 + id.Length));
        BinaryPrimitives.WriteInt32LittleEndian(forged.AsSpan(SaveFormat.MagicLength), 5);
        Assert.True(SaveSummary.TryParse(forged, out SaveSummary? s));
        Assert.Equal(5, s!.FormatVersion);
        Assert.Equal("high", s.StartingResources);
        Assert.Equal(2, s.Seats.Length);
    }
}
