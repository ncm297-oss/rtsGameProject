using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Rts.Sim.Data;
using Rts.Sim.Save;

namespace Rts.Sim.Tests.Save;

/// <summary>
/// M6-S2 criterion 5: <see cref="Simulation.TryLoad"/> refuses every bad save with its <see cref="SaveError"/>, no sim and no
/// exception: a flipped byte at every offset, every truncation, another format version, another data hash, an AI seat, an
/// unknown faction, a player count of 0 or 17, a 1-byte and an empty stream; and <see cref="Simulation.TrySave"/> refuses
/// an AI seat.
/// </summary>
public class SaveRefusalTests
{
    private static GameData Data => TestSim.Data;

    [Fact]
    public void AFlippedByte_AtEveryOffset_IsRefusedWithTheCodeOfItsPlace()
    {
        byte[] good = SaveBytes.Small();
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

    [Fact]
    public void EveryTruncation_IsTruncated()
    {
        byte[] good = SaveBytes.Small();
        for (int n = 0; n < good.Length; n++)
            Assert.True(SaveTestRun.TryLoad(Data, good[..n]) == SaveError.Truncated, $"length {n}");
    }

    [Fact]
    public void AByteAppended_IsMalformed()
    {
        byte[] good = SaveBytes.Small();
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
    [InlineData(2)]
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
    public void AnAiSeat_IsAiSeatsUnsupported_BothWays()
    {
        // Saving: refused, nothing written.
        var seats = ImmutableArray.Create(new PlayerSeat(0, SeatController.Human, AiDifficulty.Normal), new PlayerSeat(1, SeatController.Ai, AiDifficulty.Hard));
        var sim = new Simulation(TestSim.Config(2, 2, 64, 64) with { Seats = seats });
        using var stream = new MemoryStream();
        Assert.False(sim.TrySave(stream, out SaveError error));
        Assert.Equal(SaveError.AiSeatsUnsupported, error);
        Assert.Equal(0, stream.Length);

        // Loading: a human seat forged into an AI one.
        byte[] bad = SaveBytes.Small();
        SaveBytes.Offsets at = SaveBytes.HeaderOffsets(bad);
        Assert.Equal((byte)SeatController.Human, bad[at.Seat0Controller]);
        bad[at.Seat0Controller] = (byte)SeatController.Ai;
        Assert.Equal(SaveError.AiSeatsUnsupported, SaveTestRun.TryLoad(Data, SaveBytes.Rechecksum(bad)));
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
    [Fact]
    public void AForgedByte_AtEveryOffset_WithItsChecksumFixed_NeverThrows()
    {
        byte[] good = SaveBytes.Small();
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

    [Fact]
    public void TheSameBytes_LoadTwice_IntoTwoEqualSims()
    {
        byte[] bytes = SaveBytes.Small();
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
}
