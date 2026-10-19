using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Rts.Sim.Data;
using Rts.Sim.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Save;

/// <summary>
/// M6-S2 criterion 5: <see cref="Simulation.TryLoad"/> refuses every bad save with its <see cref="SaveError"/>, no sim and no
/// exception: a flipped byte at every offset, every truncation, another format version, another data hash, an
/// unknown faction, a player count of 0 or 17, a 1-byte and an empty stream. M6-S3: the sweeps run on a small save with an
/// AI seat too; a forged opening pick, a forged seat count and a seat's controller forged either way are Malformed; a
/// version-1 file is refused; and a forged max-capacity header is refused before the world is allocated (BUG-0960 item 1).
/// </summary>
public class SaveRefusalTests
{
    private static GameData Data => TestSim.Data;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AFlippedByte_AtEveryOffset_IsRefusedWithTheCodeOfItsPlace(bool ai)
    {
        byte[] good = SaveBytes.Small(ai);
        Assert.True(good.Length < 64 * 1024, $"the small save is {good.Length} bytes");
        Assert.Equal(SaveError.None, SaveTestRun.TryLoad(Data, good));
        for (int i = 0; i < good.Length; i++)
        {
            byte[] bad = (byte[])good.Clone();
            bad[i] ^= 0xFF;
            SaveError e = SaveTestRun.TryLoad(Data, bad);
            if (i < SaveFormat.MagicLength) Assert.True(e == SaveError.BadMagic, $"offset {i}: {e}");
            else if (i < SaveFormat.LengthOffset) Assert.True(e == SaveError.FormatVersionMismatch, $"offset {i}: {e}");
            else if (i < SaveFormat.FixedPrefix) Assert.True(e is SaveError.Truncated or SaveError.Malformed, $"offset {i}: {e}");
            else Assert.True(e == SaveError.ChecksumMismatch, $"offset {i}: {e}");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryTruncation_IsTruncated(bool ai)
    {
        byte[] good = SaveBytes.Small(ai);
        for (int n = 0; n < good.Length; n++)
            Assert.True(SaveTestRun.TryLoad(Data, good[..n]) == SaveError.Truncated, $"length {n}");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AByteAppended_IsMalformed(bool ai)
    {
        byte[] good = SaveBytes.Small(ai);
        Assert.Equal(SaveError.Malformed, SaveTestRun.TryLoad(Data, good.Append((byte)0).ToArray()));
    }

    [Fact]
    public void AnEmptyStream_AndOneByte_AreRefused()
    {
        Assert.Equal(SaveError.Truncated, SaveTestRun.TryLoad(Data, Array.Empty<byte>()));
        Assert.Equal(SaveError.Truncated, SaveTestRun.TryLoad(Data, new[] { (byte)'R' })); // a magic's first byte, then nothing
        Assert.Equal(SaveError.BadMagic, SaveTestRun.TryLoad(Data, new byte[] { 0 }));
        Assert.Equal(SaveError.BadMagic, SaveTestRun.TryLoad(Data, "rts-replay 6\n"u8.ToArray())); // a replay is not a save
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)] // M6-S3: slice 1's format (no AI section) has no reader
    [InlineData(3)]
    [InlineData(-1)]
    public void AnotherFormatVersion_IsFormatVersionMismatch(int version)
    {
        byte[] bad = SaveBytes.Small();
        BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(SaveFormat.MagicLength), version);
        Assert.Equal(SaveError.FormatVersionMismatch, SaveTestRun.TryLoad(Data, bad));
        Assert.Equal(SaveError.FormatVersionMismatch, SaveTestRun.TryLoad(Data, SaveBytes.Rechecksum(bad)));
    }

    [Fact]
    public void AnotherDataHash_ADisplayNameEdit_IsDataMismatch()
    {
        GameData renamed = AiScenes.DataWith(dir => dir.EditJson("factions/malazan/units.json", root =>
            root["units"]![0]!["displayName"] = "Renamed"));
        Assert.NotEqual(Data.ContentHash(), renamed.ContentHash());
        Assert.Equal(SaveError.DataMismatch, SaveTestRun.TryLoad(renamed, SaveBytes.Small()));
    }

    [Fact]
    public void ASavedDataHashChanged_IsDataMismatch()
    {
        byte[] bad = SaveBytes.Small();
        SaveBytes.Offsets at = SaveBytes.HeaderOffsets(bad);
        bad[at.DataHash] ^= 1;
        Assert.Equal(SaveError.DataMismatch, SaveTestRun.TryLoad(Data, SaveBytes.Rechecksum(bad)));
    }

    [Fact]
    public void AnAiSeat_SavesAndLoads_AndNothingReturnsAiSeatsUnsupported()
    {
        // M6-S3: the slice-1 refusal is lifted both ways.
        var seats = ImmutableArray.Create(new PlayerSeat(0, SeatController.Human, AiDifficulty.Normal), new PlayerSeat(1, SeatController.Ai, AiDifficulty.Hard));
        var sim = new Simulation(TestSim.Config(2, 2, 64, 64) with { Seats = seats });
        using var stream = new MemoryStream();
        Assert.True(sim.TrySave(stream, out SaveError error));
        Assert.Equal(SaveError.None, error);
        Assert.True(stream.Length > 0);
        Simulation loaded = SaveTestRun.Load(Data, stream.ToArray());
        Assert.Equal(sim.AiOpening(1), loaded.AiOpening(1));
        Assert.Equal(-1, loaded.AiOpening(0));
        SaveTestRun.AssertSameState(sim, loaded, "an AI seat at tick 0");
    }

    [Fact]
    public void AHumanSeatForgedIntoAnAiOne_IsMalformed()
    {
        // The header now builds an AI for the seat, whose state the body (no AI section entry) doesn't hold.
        byte[] bad = SaveBytes.Small();
        SaveBytes.Offsets at = SaveBytes.HeaderOffsets(bad);
        Assert.Equal((byte)SeatController.Human, bad[at.Seat0Controller]);
        bad[at.Seat0Controller] = (byte)SeatController.Ai;
        Assert.Equal(SaveError.Malformed, SaveTestRun.TryLoad(Data, SaveBytes.Rechecksum(bad)));
    }

    [Fact]
    public void AnAiSeatForgedIntoAHumanOne_IsMalformed()
    {
        byte[] bad = SaveBytes.SmallAi();
        SaveBytes.Offsets at = SaveBytes.HeaderOffsets(bad);
        Assert.Equal((byte)SeatController.Ai, bad[at.Seat0Controller]);
        bad[at.Seat0Controller] = (byte)SeatController.Human;
        Assert.Equal(SaveError.Malformed, SaveTestRun.TryLoad(Data, SaveBytes.Rechecksum(bad)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    [InlineData(1000)]
    public void AForgedOpeningPick_IsMalformed(int shift)
    {
        // The AI section: tag, seat count, then per seat its player and its opening index (which the load's own pick must equal).
        byte[] good = SaveBytes.SmallAi();
        int pick = SaveBytes.AiSection(good) + 4 + 4 + 4;
        int saved = BinaryPrimitives.ReadInt32LittleEndian(good.AsSpan(pick));
        Assert.Equal(SaveTestRun.Load(Data, good).AiOpening(0), saved);
        byte[] bad = (byte[])good.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(pick), saved + shift);
        Assert.Equal(SaveError.Malformed, SaveTestRun.TryLoad(Data, SaveBytes.Rechecksum(bad)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AForgedAiSeatCount_IsMalformed(int seats)
    {
        byte[] bad = SaveBytes.SmallAi();
        BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(SaveBytes.AiSection(bad) + 4), seats);
        Assert.Equal(SaveError.Malformed, SaveTestRun.TryLoad(Data, SaveBytes.Rechecksum(bad)));
    }

    [Fact]
    public void TheLengthPreCheck_PassesRealSaves_HumanAndAi_SmallAndLarge()
    {
        // The bound is below what each section writes, so a real save is never refused by it.
        Simulation[] sims =
        {
            SaveBytes.SmallSim(), SaveBytes.SmallAiSim(), SaveScenes.Menu(1).Build(), AiSaveScenes.AiMatch(4, 4, AiDifficulty.Normal).Build(),
        };
        foreach (Simulation sim in sims) Assert.Equal(SaveError.None, SaveTestRun.TryLoad(Data, SaveTestRun.Save(sim)));
    }

    [Fact]
    public void AnUnknownFactionId_IsInvalidHeader()
    {
        byte[] bad = SaveBytes.Small();
        SaveBytes.Offsets at = SaveBytes.HeaderOffsets(bad);
        Assert.Equal("malazan", SaveBytes.StringAt(bad, at.Seat0Faction));
        bad[at.Seat0Faction + 4] = (byte)'x'; // "xalazan"
        Assert.Equal(SaveError.InvalidHeader, SaveTestRun.TryLoad(Data, SaveBytes.Rechecksum(bad)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    [InlineData(-3)]
    public void APlayerCountOutOfRange_IsInvalidHeader(int players)
    {
        byte[] bad = SaveBytes.Small();
        SaveBytes.Offsets at = SaveBytes.HeaderOffsets(bad);
        BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(at.Players), players);
        Assert.Equal(SaveError.InvalidHeader, SaveTestRun.TryLoad(Data, SaveBytes.Rechecksum(bad)));
    }

    [Fact]
    public void AnotherPlayerCountInRange_IsRefused_NotLoaded()
    {
        // Two players where one was saved: the seats no longer line up with the bytes after them.
        byte[] bad = SaveBytes.Small();
        SaveBytes.Offsets at = SaveBytes.HeaderOffsets(bad);
        BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(at.Players), 2);
        Assert.NotEqual(SaveError.None, SaveTestRun.TryLoad(Data, SaveBytes.Rechecksum(bad)));
    }

    /// <summary>
    /// Past the checksum: every byte of the small save flipped and the checksum recomputed (a forged file, not a damaged
    /// one). Nothing throws; each either loads or is refused with a code. A load that succeeds must tick without throwing
    /// only where the flipped value is one the format leaves free (a position, a counter), which this sweep doesn't claim.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AForgedByte_AtEveryOffset_WithItsChecksumFixed_NeverThrows(bool ai)
    {
        byte[] good = SaveBytes.Small(ai);
        var seen = new Dictionary<SaveError, int>();
        for (int i = SaveFormat.FixedPrefix; i < good.Length - SaveFormat.ChecksumLength; i++)
        {
            byte[] bad = (byte[])good.Clone();
            bad[i] ^= 0xFF;
            SaveError e = SaveTestRun.TryLoad(Data, SaveBytes.Rechecksum(bad));
            seen[e] = seen.GetValueOrDefault(e) + 1;
        }
        Assert.True(seen.GetValueOrDefault(SaveError.Malformed) > 0);
        Assert.False(seen.ContainsKey(SaveError.ChecksumMismatch));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheSameBytes_LoadTwice_IntoTwoEqualSims(bool ai)
    {
        byte[] bytes = SaveBytes.Small(ai);
        Simulation a = SaveTestRun.Load(Data, bytes), b = SaveTestRun.Load(Data, bytes);
        Assert.NotSame(a, b);
        SaveTestRun.AssertSameState(a, b, "two loads");
        for (int t = 0; t < 200; t++)
        {
            a.Tick();
            b.Tick();
            Assert.Equal(a.StateHash(), b.StateHash());
        }
    }

    /// <summary>This class's allocation test: runs alone in <see cref="SerialCollection"/>.</summary>
    [Collection(SerialCollection.Name)]
    public class Serial
    {
        private readonly ITestOutputHelper _out;

        public Serial(ITestOutputHelper output) => _out = output;

        /// <summary>
        /// BUG-0960 item 1: a header forged to every capacity at the format's maximum over the small save's body is refused
        /// Malformed by the length pre-check, before the world is built for it (it used to allocate about 1.4 GB first).
        /// The allocation counted is this thread's.
        /// </summary>
        [Fact]
        public void AForgedMaxCapacityHeader_IsMalformed_BeforeTheWorldIsAllocated()
        {
            byte[] bad = SaveBytes.Small();
            SaveBytes.Offsets at = SaveBytes.HeaderOffsets(bad);
            int caps = at.Seat0Controller + 2; // controller, difficulty, then unit, command, resource, building, projectile, zone
            for (int k = 0; k < 6; k++) BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(caps + 4 * k), SaveFormat.MaxCapacity);
            bad = SaveBytes.Rechecksum(bad);
            Assert.Equal(SaveError.Malformed, SaveTestRun.TryLoad(Data, bad)); // warm-up (the JIT)
            long before = GC.GetAllocatedBytesForCurrentThread();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            SaveError e = SaveTestRun.TryLoad(Data, bad);
            sw.Stop();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            _out.WriteLine($"max capacities: {e} in {sw.Elapsed.TotalMilliseconds * 1000:F0} us, {allocated:N0} bytes allocated");
            Assert.Equal(SaveError.Malformed, e);
            Assert.True(allocated < 1024 * 1024, $"{allocated:N0} bytes allocated");
        }
    }
}
