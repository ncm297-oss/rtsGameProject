using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Rts.Sim.Data;
using Rts.Sim.Entities;
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
    [InlineData(2)] // M6-S4: M6-S3's format (no team byte) has no reader
    [InlineData(10)] // M5-9a: format 9 is current (8 / 9 with an AI seat, 4 / 5 without), formats 3-8 still read
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
        // The header now builds an AI for the seat, whose state the body (no AI section entry) doesn't hold. M5-4d: the
        // human save is format 4, which can't hold an AI seat (no HUNT): FormatVersionMismatch; relabelled 8, Malformed.
        byte[] bad = SaveBytes.Small();
        SaveBytes.Offsets at = SaveBytes.HeaderOffsets(bad);
        Assert.Equal((byte)SeatController.Human, bad[at.Seat0Controller]);
        bad[at.Seat0Controller] = (byte)SeatController.Ai;
        Assert.Equal(SaveError.FormatVersionMismatch, SaveTestRun.TryLoad(Data, SaveBytes.Rechecksum(bad)));
        BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(SaveFormat.MagicLength), SaveFormat.HuntMaplessVersion);
        Assert.Equal(SaveError.Malformed, SaveTestRun.TryLoad(Data, SaveBytes.Rechecksum(bad)));
        BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(SaveFormat.MagicLength), SaveFormat.HuntOnlyMaplessVersion); // M5-9a: 6 lacks CTRM
        Assert.Equal(SaveError.FormatVersionMismatch, SaveTestRun.TryLoad(Data, SaveBytes.Rechecksum(bad)));
    }

    /// <summary>
    /// M5-4d criterion 5: a save with an AI seat in an older format (5 and before: no hunt memory) is refused cleanly,
    /// <see cref="SaveError.FormatVersionMismatch"/>, sim null, whether the HUNT section is there or taken out; the same save
    /// with no AI seat in it (a human-only match) still loads at format 4.
    /// </summary>
    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void AnAiSave_InAFormatBeforeTheHunt_IsFormatVersionMismatch(int version)
    {
        // Format 5 names a map: its source is an AI match on Raraku (format 9); 3 and 4 the small generated one (format 8).
        byte[] good = version == 5 ? SaveTestRun.Save(MapFileTests.RarakuMatch(1, false, out _)) : SaveBytes.SmallAi();
        Assert.Equal(version == 5 ? SaveFormat.CurrentVersion : SaveFormat.HuntMaplessVersion, BinaryPrimitives.ReadInt32LittleEndian(good.AsSpan(SaveFormat.MagicLength)));
        byte[] relabelled = (byte[])good.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(relabelled.AsSpan(SaveFormat.MagicLength), version);
        Assert.Equal(SaveError.FormatVersionMismatch, SaveTestRun.TryLoad(Data, SaveBytes.Rechecksum(relabelled)));
        byte[] old = PreMapFixtures.WithoutHunt(good);
        BinaryPrimitives.WriteInt32LittleEndian(old.AsSpan(SaveFormat.MagicLength), version);
        Assert.Equal(SaveError.FormatVersionMismatch, SaveTestRun.TryLoad(Data, SaveBytes.Rechecksum(old)));
        Assert.Equal(SaveFormat.MaplessVersion, BinaryPrimitives.ReadInt32LittleEndian(SaveBytes.Small().AsSpan(SaveFormat.MagicLength)));
        Assert.Equal(SaveError.None, SaveTestRun.TryLoad(Data, SaveBytes.Small()));
    }

    /// <summary>
    /// M5-4d: the HUNT section is required in format 6: taken out (the version left at 6), or its seat count forged, Malformed.
    /// M5-9a: likewise in format 8 (HUNT and CTRM taken out, the version left at 8).
    /// </summary>
    [Fact]
    public void AFormat8Save_WithoutItsHuntSection_OrAForgedSeatCount_IsMalformed()
    {
        byte[] good = SaveBytes.SmallAi();
        byte[] cut = PreMapFixtures.WithoutHunt(good);
        BinaryPrimitives.WriteInt32LittleEndian(cut.AsSpan(SaveFormat.MagicLength), SaveFormat.HuntMaplessVersion);
        Assert.Equal(SaveError.Malformed, SaveTestRun.TryLoad(Data, SaveBytes.Rechecksum(cut)));
        byte[] forged = (byte[])good.Clone();
        int hunt = forged.AsSpan().LastIndexOf("HUNT"u8);
        BinaryPrimitives.WriteInt32LittleEndian(forged.AsSpan(hunt + 4), 2);
        Assert.Equal(SaveError.Malformed, SaveTestRun.TryLoad(Data, SaveBytes.Rechecksum(forged)));
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

    /// <summary>
    /// BUG-1118 item 2: the AI seat's <c>TownHallCell</c> is range-checked on load: -1, a map cell, or a lost hall's anchor
    /// -(cell + 2) loads; one past either end, or <see cref="int.MinValue"/>, is Malformed. The field is found as the
    /// four ints <c>HallSlot, TownHallCell, Gold, Wood</c> the seat writes in a row.
    /// </summary>
    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, true)]
    [InlineData(255, true)]
    [InlineData(-257, true)]
    [InlineData(256, false)]
    [InlineData(-258, false)]
    [InlineData(int.MinValue, false)]
    public void AForgedTownHallCell_OutsideTheMap_IsMalformed(int value, bool loads)
    {
        Simulation sim = SaveBytes.SmallAiSim();
        Ai.AiPlayer ai = sim.AiOf(0)!;
        Assert.Equal(256, ai.View.MapWidth * ai.View.MapHeight);
        byte[] good = SaveTestRun.Save(sim);
        var pattern = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(pattern.AsSpan(0), ai.HallSlot);
        BinaryPrimitives.WriteInt32LittleEndian(pattern.AsSpan(4), ai.TownHallCell);
        BinaryPrimitives.WriteInt32LittleEndian(pattern.AsSpan(8), ai.Gold);
        BinaryPrimitives.WriteInt32LittleEndian(pattern.AsSpan(12), ai.Wood);
        int aist = SaveBytes.AiSection(good);
        int at = good.AsSpan(aist).IndexOf(pattern);
        Assert.True(at > 0, "the seat's hall fields not found");
        Assert.Equal(-1, good.AsSpan(aist + at + 1).IndexOf(pattern)); // found once
        byte[] forged = (byte[])good.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(forged.AsSpan(aist + at + 4), value);
        Assert.Equal(loads ? SaveError.None : SaveError.Malformed, SaveTestRun.TryLoad(Data, SaveBytes.Rechecksum(forged)));
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

    /// <summary>
    /// BUG-0990: the pre-check's new terms never refuse a real save: an empty world (no unit, building, node, projectile or
    /// zone alive) at large capacities, the command capacity at the save's bound, saves and loads.
    /// </summary>
    [Fact]
    public void TheLengthPreCheck_PassesARealSaveOfAnEmptyWorld_AtLargeCapacities()
    {
        var config = new SimConfig(5, 2, 100_000, SaveFormat.MaxCommandCapacity)
        {
            Data = TestSim.Data, ResourceCapacity = 50_000, BuildingCapacity = 50_000, ZoneCapacity = 20_000, ProjectileCapacity = 200_000, StatsSampleCapacity = 0,
        };
        var sim = new Simulation(config, LocalMovementTests.Flat(16));
        sim.Tick();
        Assert.Equal(SaveError.None, SaveTestRun.TryLoad(Data, SaveTestRun.Save(sim)));
    }

    /// <summary>
    /// BUG-0990: a header command capacity over <see cref="SaveFormat.MaxCommandCapacity"/> is refused before the world is
    /// built; at the bound it loads (configuration, docs/03). BUG-1040: the refusal is InvalidHeader (a capacity outside its
    /// range, the config's own bound), no longer Malformed, so it is the same bound <see cref="SimConfig.Validate"/> puts on
    /// every sim and no save a sim writes is refused.
    /// </summary>
    [Theory]
    [InlineData(SaveFormat.MaxCommandCapacity + 1, SaveError.InvalidHeader)]
    [InlineData(SaveFormat.MaxCapacity, SaveError.InvalidHeader)]
    [InlineData(int.MaxValue, SaveError.InvalidHeader)]
    [InlineData(SaveFormat.MaxCommandCapacity, SaveError.None)]
    public void AForgedCommandCapacity_IsBoundedByTheFormat(int commands, SaveError expected)
    {
        byte[] bad = SaveBytes.Small();
        SaveBytes.Offsets at = SaveBytes.HeaderOffsets(bad);
        BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(at.Capacities + 4), commands);
        Assert.Equal(expected, SaveTestRun.TryLoad(Data, SaveBytes.Rechecksum(bad)));
    }

    /// <summary>
    /// BUG-0960 item 2: two forged values QA's int32 sweep loaded and then threw on within three ticks. A live unit's goal
    /// cell off the map (the flow field cache threw) and a player's last visible box past the map (the fog update indexed
    /// past the grid) are refused Malformed at load. The forged state is written by the sim's own save (the small save's
    /// walking unit; its fog box), so the bytes are otherwise real. M6-SH1: the last one QA's sweep still saw throw, a Moving
    /// unit with no goal (-1, legal for a unit at rest), refused the same way.
    /// </summary>
    [Theory]
    [InlineData("goal", int.MaxValue)]
    [InlineData("goal", -2)]
    [InlineData("goal", 16 * 16)]
    [InlineData("goal", -1)] // M6-SH1 (BUG-0960's last throw, QA's offset 2525): no goal on a Moving unit
    [InlineData("box", 16)]
    [InlineData("box", -1)]
    public void AForgedGoalCellOrVisibleBox_IsMalformed(string what, int value)
    {
        Simulation sim = SaveBytes.SmallSim();
        if (what == "goal")
        {
            Assert.True(sim.World.Units.Alive[0]);
            Assert.Equal(UnitState.Moving, sim.World.Units.State[0]); // a Moving unit's goal is read every tick
            sim.World.Units.GoalCell[0] = value;
        }
        else
        {
            var box = (int[])typeof(Rts.Sim.Vision.FogStore).GetField("_visibleBox", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(sim.World.Fog)!;
            Assert.NotEqual(int.MaxValue, box[0]); // the unit sees: the box is set
            box[value < 0 ? 0 : 2] = value; // x0 below the map, or x1 past it
        }
        Assert.Equal(SaveError.Malformed, SaveTestRun.TryLoad(Data, SaveTestRun.Save(sim)));
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

    /// <summary>
    /// M6-S6: a starting-resources preset the data lacks is InvalidHeader (rule 36: an existing code), human and AI saves,
    /// an empty id too; a control byte in it or a length past the bytes is Malformed; a format-4 file relabelled format 3 (the
    /// preset where format 3 has the heightmap's tag) is Malformed.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AForgedPreset_IsRefused(bool ai)
    {
        byte[] good = SaveBytes.Small(ai);
        SaveBytes.Offsets at = SaveBytes.HeaderOffsets(good);
        Assert.Equal("standard", SaveBytes.StringAt(good, at.Preset));
        foreach (string forged in new[] { "standarx", "Standard", "--------", "low_____" })
        {
            byte[] bad = (byte[])good.Clone();
            System.Text.Encoding.ASCII.GetBytes(forged).CopyTo(bad, at.Preset + 4);
            Assert.True(SaveTestRun.TryLoad(Data, SaveBytes.Rechecksum(bad)) == SaveError.InvalidHeader, forged);
        }
        byte[] control = (byte[])good.Clone();
        control[at.Preset + 4] = 0x07;
        Assert.Equal(SaveError.Malformed, SaveTestRun.TryLoad(Data, SaveBytes.Rechecksum(control)));
        byte[] longer = (byte[])good.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(longer.AsSpan(at.Preset), int.MaxValue);
        Assert.Equal(SaveError.Malformed, SaveTestRun.TryLoad(Data, SaveBytes.Rechecksum(longer)));
        byte[] relabelled = (byte[])good.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(relabelled.AsSpan(SaveFormat.MagicLength), 3);
        // M5-4d: with an AI seat a format-3 header is refused as a format before the hunt, before the body is read.
        Assert.Equal(ai ? SaveError.FormatVersionMismatch : SaveError.Malformed, SaveTestRun.TryLoad(Data, SaveBytes.Rechecksum(relabelled)));
        // An empty id (length 0) names no preset either.
        byte[] empty = new byte[good.Length - "standard".Length];
        good.AsSpan(0, at.Preset).CopyTo(empty);
        good.AsSpan(at.Preset + 4 + "standard".Length).CopyTo(empty.AsSpan(at.Preset + 4));
        BinaryPrimitives.WriteInt64LittleEndian(empty.AsSpan(SaveFormat.LengthOffset), empty.Length);
        Assert.Equal(SaveError.InvalidHeader, SaveTestRun.TryLoad(Data, SaveBytes.Rechecksum(empty)));
    }

    /// <summary>M6-S4: seat 0's team byte forged past 8 (9, or 255: a -1 written as a byte) is InvalidHeader, human and AI saves; 0-8 load, and the loaded seat has the forged team.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AForgedTeam_OutsideZeroToEight_IsInvalidHeader(bool ai)
    {
        byte[] good = SaveBytes.Small(ai);
        SaveBytes.Offsets at = SaveBytes.HeaderOffsets(good);
        Assert.Equal(0, good[at.Seat0Team]);
        foreach (byte team in new byte[] { 9, 10, 128, 255 })
        {
            byte[] bad = (byte[])good.Clone();
            bad[at.Seat0Team] = team;
            Assert.True(SaveTestRun.TryLoad(Data, SaveBytes.Rechecksum(bad)) == SaveError.InvalidHeader, $"team {team}");
        }
        foreach (byte team in new byte[] { 1, 8 })
        {
            byte[] forged = (byte[])good.Clone();
            forged[at.Seat0Team] = team;
            Simulation loaded = SaveTestRun.Load(Data, SaveBytes.Rechecksum(forged));
            Assert.Equal((int)team, loaded.World.Config.SeatOf(0).Team);
            Assert.Equal((int)team, loaded.World.TeamOf(0));
        }
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
        /// The allocation counted is this thread's. BUG-1040: the command capacity is forged to its own bound (over it the
        /// header refuses it InvalidHeader first, <see cref="AForgedCapacity_AloneOrTogether_IsRefused_UnderAMegabyte"/>), so
        /// this row still reaches the pre-check.
        /// </summary>
        [Fact]
        public void AForgedMaxCapacityHeader_IsMalformed_BeforeTheWorldIsAllocated()
        {
            byte[] bad = SaveBytes.Small();
            SaveBytes.Offsets at = SaveBytes.HeaderOffsets(bad);
            int caps = at.Capacities; // controller, difficulty, then unit, command, resource, building, projectile, zone
            for (int k = 0; k < 6; k++)
                BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(caps + 4 * k), k == 1 ? SaveFormat.MaxCommandCapacity : SaveFormat.MaxCapacity);
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

        /// <summary>
        /// BUG-0990: each header capacity forged to the format's maximum alone (unit, command, resource, building, projectile,
        /// zone), the command, projectile and zone capacities together, all six, and the five other than the command, over the
        /// small human and AI saves: each is refused with under 1 MB allocated on this thread, Malformed by the length
        /// pre-check, or (BUG-1040) InvalidHeader by the header's range check when the command capacity is over its bound
        /// (the config's own). Before the fix the command alone loaded (52 MB), the projectile alone allocated 88 MB, the
        /// zone 42 MB, the three 182 MB.
        /// </summary>
        [Theory]
        [InlineData(false, "0")]
        [InlineData(false, "1")]
        [InlineData(false, "2")]
        [InlineData(false, "3")]
        [InlineData(false, "4")]
        [InlineData(false, "5")]
        [InlineData(false, "1,4,5")]
        [InlineData(false, "0,1,2,3,4,5")]
        [InlineData(false, "0,2,3,4,5")]
        [InlineData(true, "1")]
        [InlineData(true, "4")]
        [InlineData(true, "5")]
        [InlineData(true, "1,4,5")]
        [InlineData(true, "0,1,2,3,4,5")]
        [InlineData(true, "0,2,3,4,5")]
        public void AForgedCapacity_AloneOrTogether_IsRefused_UnderAMegabyte(bool ai, string which)
        {
            byte[] bad = SaveBytes.Small(ai);
            SaveBytes.Offsets at = SaveBytes.HeaderOffsets(bad);
            int caps = at.Capacities; // unit, command, resource, building, projectile, zone
            foreach (string k in which.Split(',')) BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(caps + 4 * int.Parse(k)), SaveFormat.MaxCapacity);
            bad = SaveBytes.Rechecksum(bad);
            SaveTestRun.TryLoad(Data, bad); // warm-up (the JIT)
            long before = GC.GetAllocatedBytesForCurrentThread();
            SaveError e = SaveTestRun.TryLoad(Data, bad);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            _out.WriteLine($"{(ai ? "AI" : "human")} save, capacities {which} at max: {e}, {allocated:N0} bytes allocated");
            Assert.Equal(Array.IndexOf(which.Split(','), "1") >= 0 ? SaveError.InvalidHeader : SaveError.Malformed, e);
            Assert.True(allocated < 1024 * 1024, $"{allocated:N0} bytes allocated");
        }
    }

    /// <summary>
    /// M5-9a: a save with an AI seat in M5-4d's formats (6 on a generated map, 7 on a hand-made one: no counter memory) is
    /// refused <see cref="SaveError.FormatVersionMismatch"/>, whether the <c>CTRM</c> section is there or taken out; the
    /// version alone tells.
    /// </summary>
    [Theory]
    [InlineData(6)]
    [InlineData(7)]
    public void AnAiSave_InAFormatBeforeTheCounterMemory_IsFormatVersionMismatch(int version)
    {
        byte[] good = version == 7 ? SaveTestRun.Save(MapFileTests.RarakuMatch(1, false, out _)) : SaveBytes.SmallAi();
        Assert.Equal(version + 2, BinaryPrimitives.ReadInt32LittleEndian(good.AsSpan(SaveFormat.MagicLength)));
        byte[] relabelled = (byte[])good.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(relabelled.AsSpan(SaveFormat.MagicLength), version);
        Assert.Equal(SaveError.FormatVersionMismatch, SaveTestRun.TryLoad(Data, SaveBytes.Rechecksum(relabelled)));
        byte[] old = PreMapFixtures.WithoutCounters(good);
        Assert.Equal(version, BinaryPrimitives.ReadInt32LittleEndian(old.AsSpan(SaveFormat.MagicLength)));
        Assert.Equal(SaveError.FormatVersionMismatch, SaveTestRun.TryLoad(Data, SaveBytes.Rechecksum(old)));
    }

    /// <summary>
    /// M5-9a: the <c>CTRM</c> section is required in format 8: taken out (the version left at 8), or its seat count or a seat's
    /// player index forged, <see cref="SaveError.Malformed"/>; so is a forged memory: a count below 0 or above the match's unit
    /// slots, a tick below 0 or after the seat's last think; round 2, a starved stamp other than -1 or 0 to the last think. The
    /// unforged bytes load.
    /// </summary>
    [Fact]
    public void AFormat8Save_WithoutItsCounterSection_OrAForgedMemory_IsMalformed()
    {
        byte[] good = SaveBytes.SmallAi();
        Assert.Equal(SaveError.None, SaveTestRun.TryLoad(Data, good));
        byte[] cut = PreMapFixtures.WithoutCounters(good);
        BinaryPrimitives.WriteInt32LittleEndian(cut.AsSpan(SaveFormat.MagicLength), SaveFormat.HuntMaplessVersion);
        Assert.Equal(SaveError.Malformed, SaveTestRun.TryLoad(Data, SaveBytes.Rechecksum(cut)));

        int at = PreMapFixtures.CountersAt(good, 1);
        Assert.Equal("CTRM"u8.ToArray(), good[at..(at + 4)]);
        Rts.Sim.Ai.AiPlayer ai = SaveTestRun.Load(Data, good).AiOf(0)!;
        int counts = at + 4 + 4 + 4 + 4, ticks = counts + 4 * Rts.Sim.Ai.Blackboard.SlotCount + 4; // tag, seats, player, length
        SaveError Forged(int offset, int value)
        {
            byte[] bad = (byte[])good.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(offset), value);
            return SaveTestRun.TryLoad(Data, SaveBytes.Rechecksum(bad));
        }
        Assert.Equal(SaveError.Malformed, Forged(at + 4, 2)); // the seat count
        Assert.Equal(SaveError.Malformed, Forged(at + 8, 1)); // the seat's player index
        Assert.Equal(SaveError.Malformed, Forged(counts - 4, 6)); // the array's length
        Assert.Equal(SaveError.Malformed, Forged(counts + 4 * 3, -1));
        Assert.Equal(SaveError.Malformed, Forged(counts + 4 * 3, ai.View.UnitSlots + 1));
        Assert.Equal(SaveError.None, Forged(counts + 4 * 3, ai.View.UnitSlots));
        Assert.Equal(SaveError.Malformed, Forged(ticks + 4 * 3, -1));
        Assert.Equal(SaveError.Malformed, Forged(ticks + 4 * 3, ai.Now + 1)); // a sighting after the last think
        Assert.Equal(SaveError.None, Forged(ticks + 4 * 3, ai.Now));
        // M5-9a round 2: the starved stamp, after the ticks: -1 (none) or 0 to the seat's last think.
        int stamp = ticks + 4 * Rts.Sim.Ai.Blackboard.SlotCount;
        Assert.Equal(-1, BinaryPrimitives.ReadInt32LittleEndian(good.AsSpan(stamp)));
        Assert.Equal(SaveError.None, Forged(stamp, 0));
        Assert.Equal(SaveError.None, Forged(stamp, ai.Now));
        Assert.Equal(SaveError.Malformed, Forged(stamp, ai.Now + 1)); // a stamp after the last think
        Assert.Equal(SaveError.Malformed, Forged(stamp, -2));
        Assert.Equal(SaveError.Malformed, Forged(stamp, int.MinValue));
        Assert.Equal(SaveError.Malformed, Forged(stamp, int.MaxValue));
        Assert.Equal(stamp + 4 + PreMapFixtures.FarBytes + 4, good.Length - SaveFormat.ChecksumLength); // M5-9b's fields, then END!
    }

    /// <summary>
    /// M5-9b: the far expansion's fields in <c>CTRM</c> (after the starved stamp) are bounded: a phase past 2, looked-at bits for
    /// a start the map lacks, a start or look point out of range, a leg worker past the unit slots, or a slot with generation 0
    /// or a generation with no slot (BUG-1159: the reader checks only that a worker's generation is above 0; whether it
    /// matches the slot is checked at use, <c>AiPlayer.OwnUnit</c>), a pending order other than a Build, a site type or cell
    /// out of range or half set, a negative count, and any state the phases can't hold (a leg worker or a Build while off, a
    /// Build and a site at once) are <see cref="SaveError.Malformed"/>; consistent forgeries load.
    /// </summary>
    [Fact]
    public void AFormat8Save_WithAForgedFarExpansion_IsMalformed()
    {
        byte[] good = SaveBytes.SmallAi();
        Assert.Equal(SaveError.None, SaveTestRun.TryLoad(Data, good));
        Rts.Sim.Ai.AiPlayer ai = SaveTestRun.Load(Data, good).AiOf(0)!;
        int far = good.Length - SaveFormat.ChecksumLength - 4 - PreMapFixtures.FarBytes;
        int phase = far, looked = far + 1, start = far + 5, look = far + 9, moved = far + 13, scout = far + 14, generation = far + 18,
            pending = far + 22, siteType = far + 51, siteCell = far + 55, expansions = far + 63;
        Assert.Equal(0, good[phase]);
        Assert.Equal(-1, BinaryPrimitives.ReadInt32LittleEndian(good.AsSpan(start)));
        Assert.Equal(-1, BinaryPrimitives.ReadInt32LittleEndian(good.AsSpan(scout)));
        Assert.Equal(-1, BinaryPrimitives.ReadInt32LittleEndian(good.AsSpan(siteType)));
        int starts = ai.View.StartCount, cells = ai.View.MapWidth * ai.View.MapHeight;
        SaveError Forged(params (int Offset, int Value, bool Byte)[] edits)
        {
            byte[] bad = (byte[])good.Clone();
            foreach ((int offset, int value, bool oneByte) in edits)
                if (oneByte) bad[offset] = (byte)value;
                else BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(offset), value);
            return SaveTestRun.TryLoad(Data, SaveBytes.Rechecksum(bad));
        }
        (int, int, bool) I(int offset, int value) => (offset, value, false);
        (int, int, bool) B(int offset, int value) => (offset, value, true);
        // The phase.
        Assert.Equal(SaveError.None, Forged(B(phase, 1))); // armed, nothing walked yet
        Assert.Equal(SaveError.Malformed, Forged(B(phase, 3)));
        Assert.Equal(SaveError.Malformed, Forged(B(phase, 2))); // building with neither a Build nor a site
        // The looked-at bits: only starts the map has.
        Assert.Equal(SaveError.None, Forged(I(looked, (1 << starts) - 1)));
        Assert.Equal(SaveError.Malformed, Forged(I(looked, 1 << starts)));
        Assert.Equal(SaveError.Malformed, Forged(I(looked, -1)));
        // The leg: a start and look point in range, only while armed; its worker a unit slot with a live generation.
        // (The fixture: a 16-cell hand-built map with no start list, unit capacity 2.)
        Assert.Equal(0, starts);
        int last = ai.View.UnitSlots - 1;
        Assert.Equal(SaveError.None, Forged(B(phase, 1), I(look, 4), B(moved, 1), I(scout, last), I(generation, 1)));
        Assert.Equal(SaveError.Malformed, Forged(B(phase, 1), I(start, starts)));
        Assert.Equal(SaveError.Malformed, Forged(B(phase, 1), I(start, -2)));
        Assert.Equal(SaveError.Malformed, Forged(B(phase, 1), I(look, 5)));
        Assert.Equal(SaveError.Malformed, Forged(B(phase, 1), I(scout, ai.View.UnitSlots), I(generation, 1)));
        Assert.Equal(SaveError.Malformed, Forged(B(phase, 1), I(scout, last), I(generation, 0)));
        Assert.Equal(SaveError.Malformed, Forged(B(phase, 1), I(generation, 1))); // a generation with no worker
        Assert.Equal(SaveError.Malformed, Forged(I(start, 0))); // a start the map lacks (BUG-1159: a kept start is valid in any phase)
        Assert.Equal(SaveError.Malformed, Forged(I(scout, last), I(generation, 1))); // a leg worker while off
        // The Build and the site: only while building, exactly one of them.
        Assert.Equal(SaveError.None, Forged(B(phase, 2), B(pending, (int)Rts.Sim.Ai.PendingKind.Build)));
        Assert.Equal(SaveError.Malformed, Forged(B(phase, 2), B(pending, (int)Rts.Sim.Ai.PendingKind.Train)));
        Assert.Equal(SaveError.Malformed, Forged(B(phase, 1), B(pending, (int)Rts.Sim.Ai.PendingKind.Build)));
        Assert.Equal(SaveError.None, Forged(B(phase, 2), I(siteType, 0), I(siteCell, cells - 1)));
        Assert.Equal(SaveError.Malformed, Forged(B(phase, 2), I(siteType, ai.Data.Buildings.Length), I(siteCell, 0)));
        Assert.Equal(SaveError.Malformed, Forged(B(phase, 2), I(siteType, 0), I(siteCell, cells)));
        Assert.Equal(SaveError.Malformed, Forged(B(phase, 2), I(siteType, 0))); // half a site
        Assert.Equal(SaveError.Malformed, Forged(B(phase, 2), B(pending, (int)Rts.Sim.Ai.PendingKind.Build), I(siteType, 0), I(siteCell, 0)));
        Assert.Equal(SaveError.Malformed, Forged(B(phase, 2), I(siteType, 0), I(siteCell, 0), I(scout, last), I(generation, 1))); // a leg worker while building
        Assert.Equal(SaveError.Malformed, Forged(I(siteType, 0), I(siteCell, 0))); // a site while off
        // The count.
        Assert.Equal(SaveError.Malformed, Forged(I(expansions, -1)));
        Assert.Equal(SaveError.None, Forged(I(expansions, 3)));
    }

    /// <summary>
    /// M5-H5 (the M5-4d note; fail-first): the perimeter leg's next point (<c>HuntRing</c>, written just before
    /// <c>HuntRingLeft</c>, <c>HuntPass</c> and <c>HuntsStarted</c> at the end of the <c>HUNT</c> section) is a border-ring index
    /// or -1: a forged leg on a cell centre (index 0) or on the last interior point is <see cref="SaveError.Malformed"/> (both
    /// loaded before), the first ring index loads.
    /// </summary>
    [Fact]
    public void AForgedLeg_OnAPointBelowTheRing_IsMalformed()
    {
        byte[] good = SaveBytes.SmallAi();
        int end = PreMapFixtures.CountersAt(good, 1); // M5-9a: HUNT ends where CTRM starts
        Assert.Equal("CTRM"u8.ToArray(), good[end..(end + 4)]);
        Rts.Sim.Ai.AiPlayer ai = SaveTestRun.Load(TestSim.Data, good).AiOf(0)!;
        int cols = Rts.Sim.Ai.ScoutManager.GridLines(ai.View.MapWidth, ai.Rules.ScoutGrid), rows = Rts.Sim.Ai.ScoutManager.GridLines(ai.View.MapHeight, ai.Rules.ScoutGrid);
        int ring = Rts.Sim.Ai.MilitaryManager.RingStart(cols, rows);
        SaveError Forged(int huntRing)
        {
            byte[] bad = (byte[])good.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(end - 16), huntRing);
            BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(end - 12), 1);
            return SaveTestRun.TryLoad(TestSim.Data, SaveBytes.Rechecksum(bad));
        }
        Assert.Equal(SaveError.Malformed, Forged(0));
        Assert.Equal(SaveError.Malformed, Forged(ring - 1));
        Assert.Equal(SaveError.None, Forged(ring));
    }

    /// <summary>
    /// M5-H6 (the M5-4d note "HUNT's tick range-checked"; failed first: both loaded): the hunt count (the last four bytes of
    /// <c>HUNT</c>, read by the sweep since M5-H6: the free starts come first from the second hunt) is not negative, and the
    /// last sighting's tick (after its point, at the section's start) is -1 (none) or a tick; a forged -1 count or a -2 tick
    /// is <see cref="SaveError.Malformed"/>, 0 and -1 load.
    /// </summary>
    [Fact]
    public void AForgedHuntCountOrSightingTick_OutOfRange_IsMalformed()
    {
        byte[] good = SaveBytes.SmallAi();
        int end = PreMapFixtures.CountersAt(good, 1);
        Rts.Sim.Ai.AiPlayer ai = SaveTestRun.Load(TestSim.Data, good).AiOf(0)!;
        int points = ai.Board.HuntVisited.Length;
        // The seat's HUNT fields backwards from the end: the count, pass, ring left, ring (16), the marks and their length,
        // the sweep (4), the origin (8), the hunting flag (1), the sighting's tick (4), its point (8).
        Assert.Equal(points, BinaryPrimitives.ReadInt32LittleEndian(good.AsSpan(end - 16 - points - 4)));
        int hunt = end - 16 - points - 4 - 8 - 4 - 1 - 4 - 8;
        Assert.Equal(ai.Board.EnemyUnitTick, BinaryPrimitives.ReadInt32LittleEndian(good.AsSpan(hunt + 8)));
        SaveError Forged(int offset, int value)
        {
            byte[] bad = (byte[])good.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(offset), value);
            return SaveTestRun.TryLoad(TestSim.Data, SaveBytes.Rechecksum(bad));
        }
        Assert.Equal(SaveError.Malformed, Forged(end - 4, -1));
        Assert.Equal(SaveError.None, Forged(end - 4, 0));
        Assert.Equal(SaveError.Malformed, Forged(hunt + 8, -2));
        Assert.Equal(SaveError.None, Forged(hunt + 8, -1));
    }
}
