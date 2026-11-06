using System.Buffers.Binary;
using System.Text;
using Rts.Sim.Data;
using Rts.Sim.Replays;
using Rts.Sim.Save;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M6-S6 (2026-11-05-1206): attacks on the starting-resources presets' format boundary (save format 4, replay format 8),
/// on the old layouts reading as <c>standard</c>, on extreme presets, and the per-call allocation of the preset lookup.
/// </summary>
[Collection(SerialCollection.Name)]
public class StartingResourcesQaTests
{
    private readonly ITestOutputHelper _out;

    public StartingResourcesQaTests(ITestOutputHelper output) => _out = output;

    private static GameData Data => TestSim.Data;

    /// <summary>The preset field's offset in any save: it is the header's last field, right before the heightmap's tag.</summary>
    private static int PresetAt(byte[] bytes, string preset)
    {
        int hmap = bytes.AsSpan().IndexOf("HMAP"u8);
        int at = hmap - 4 - preset.Length;
        Assert.Equal(preset, SaveBytes.StringAt(bytes, at));
        return at;
    }

    private static byte[] HighSave(out Simulation sim)
    {
        sim = new Simulation(TestSim.Config(9, 2, 64, 64) with { StartingResources = "high" });
        for (int t = 0; t < 5; t++) sim.Tick();
        return SaveTestRun.Save(sim);
    }

    // ---------- save format 4 ----------

    /// <summary>
    /// The preset's length word forged to every hostile value: never a throw, never a load, and the refusal costs under
    /// 1 MB of allocation (the length is checked against the 256-byte string limit and the bytes left before any string).
    /// </summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(257)]
    [InlineData(1 << 20)]
    [InlineData(1 << 30)]
    [InlineData(int.MaxValue)]
    public void AForgedPresetLength_IsRefused_WithoutAllocatingItsLength(int length)
    {
        byte[] good = HighSave(out _);
        int at = PresetAt(good, "high");
        byte[] bad = (byte[])good.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(at), length);
        bad = SaveBytes.Rechecksum(bad);
        // The baseline: the same file with a same-length unknown id, refused at the same field (TryLoad's own buffers).
        byte[] unknown = Splice(good, at, "zzzz");
        SaveError e = SaveError.None;
        SaveTestRun.TryLoad(Data, bad); // warm
        SaveTestRun.TryLoad(Data, unknown);
        long baseline = AllocationProbe.Measure(() => SaveTestRun.TryLoad(Data, unknown));
        // The smaller of two runs (a first run can carry a one-time runtime charge, as AllocationProbe notes).
        long bytes = Math.Min(AllocationProbe.Measure(() => e = SaveTestRun.TryLoad(Data, bad)),
            AllocationProbe.Measure(() => e = SaveTestRun.TryLoad(Data, bad)));
        _out.WriteLine($"length {length}: {e}, {bytes} B allocated (an unknown 4-byte id: {baseline} B; file {good.Length} B)");
        Assert.NotEqual(SaveError.None, e);
        Assert.True(bytes - baseline < 4096, $"length {length}: {bytes} B allocated refusing it, {baseline} B for an unknown id");
        // 0 names no preset; 1-256 read printable header bytes that are no preset id (or run into the map): never a load.
        if (length < 0 || length > SaveFormat.MaxString) Assert.Equal(SaveError.Malformed, e);
    }

    /// <summary>
    /// Truncation at every byte of the preset field, the header shortened there and re-sealed (so the reader, not the
    /// length check, meets the cut): Malformed, never a throw or a load. And the raw file cut at every byte of the header
    /// through the first heightmap word: never a throw or a load.
    /// </summary>
    [Fact]
    public void TruncationAtEveryByteOfThePresetField_IsRefused()
    {
        byte[] good = HighSave(out _);
        int at = PresetAt(good, "high");
        int end = at + 4 + "high".Length;
        for (int cut = at; cut < end; cut++)
        {
            byte[] shortened = new byte[cut + SaveFormat.ChecksumLength];
            good.AsSpan(0, cut).CopyTo(shortened);
            BinaryPrimitives.WriteInt64LittleEndian(shortened.AsSpan(SaveFormat.LengthOffset), shortened.Length);
            SaveError e = SaveTestRun.TryLoad(Data, SaveBytes.Rechecksum(shortened));
            Assert.True(e == SaveError.Malformed, $"cut at {cut} (field {at}-{end}): {e}");
        }
        for (int cut = 0; cut < end + 8; cut++)
            Assert.NotEqual(SaveError.None, SaveTestRun.TryLoad(Data, good.AsSpan(0, cut).ToArray()));
    }

    /// <summary>
    /// Every single-byte value at every byte of the preset's id (re-sealed): only the bytes that spell a shipped preset load
    /// (here only the original); a non-printable byte is Malformed; anything else InvalidHeader. Never a throw.
    /// </summary>
    [Fact]
    public void EveryByteValue_AtEveryIdByte_IsRefusedUnlessItSpellsAPreset()
    {
        byte[] good = HighSave(out _);
        int at = PresetAt(good, "high");
        int loads = 0;
        for (int i = 0; i < 4; i++)
            for (int v = 0; v < 256; v++)
            {
                byte[] bad = (byte[])good.Clone();
                bad[at + 4 + i] = (byte)v;
                SaveError e = SaveTestRun.TryLoad(Data, SaveBytes.Rechecksum(bad));
                if (e == SaveError.None)
                {
                    loads++;
                    Assert.Equal(good[at + 4 + i], (byte)v);
                }
                else if (v < 0x20 || v > 0x7E) Assert.Equal(SaveError.Malformed, e);
                else Assert.Equal(SaveError.InvalidHeader, e);
            }
        Assert.Equal(4, loads);
    }

    /// <summary>
    /// A 256-byte (the string limit) and a 64-byte snake_case id the data lacks: InvalidHeader. A shipped id padded with a
    /// space or a trailing NUL: refused.
    /// </summary>
    [Theory]
    [InlineData(256, 'a')]
    [InlineData(64, 'b')]
    [InlineData(65, 'c')]
    public void ALongUnknownPresetId_IsInvalidHeader(int length, char fill)
    {
        byte[] good = HighSave(out _);
        int at = PresetAt(good, "high");
        Assert.Equal(SaveError.InvalidHeader, SaveTestRun.TryLoad(Data, Splice(good, at, new string(fill, length))));
        Assert.NotEqual(SaveError.None, SaveTestRun.TryLoad(Data, Splice(good, at, "high ")));
        Assert.NotEqual(SaveError.None, SaveTestRun.TryLoad(Data, Splice(good, at, " high")));
        Assert.Equal(SaveError.None, SaveTestRun.TryLoad(Data, Splice(good, at, "standard")));
    }

    /// <summary>The save with its preset string field at <paramref name="at"/> replaced by <paramref name="id"/>, length and checksum fixed.</summary>
    private static byte[] Splice(byte[] good, int at, string id)
    {
        int oldLen = BinaryPrimitives.ReadInt32LittleEndian(good.AsSpan(at));
        byte[] idBytes = Encoding.ASCII.GetBytes(id);
        byte[] bytes = new byte[good.Length - oldLen + idBytes.Length];
        good.AsSpan(0, at).CopyTo(bytes);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at), idBytes.Length);
        idBytes.CopyTo(bytes, at + 4);
        good.AsSpan(at + 4 + oldLen).CopyTo(bytes.AsSpan(at + 4 + idBytes.Length));
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(SaveFormat.LengthOffset), bytes.Length);
        return SaveBytes.Rechecksum(bytes);
    }

    /// <summary>
    /// A high save with its preset respliced to <c>standard</c> loads (the banks are body state, so the match plays on as
    /// the high one did) but names standard: the preset is configuration only. Re-saved, it says standard.
    /// </summary>
    [Fact]
    public void AHighSave_RelabelledStandard_PlaysOnWithItsBanks_AndSaysStandard()
    {
        byte[] good = HighSave(out Simulation sim);
        Simulation relabelled = SaveTestRun.Load(Data, Splice(good, PresetAt(good, "high"), "standard"));
        Assert.Equal("standard", relabelled.World.Config.StartingResources);
        Assert.Equal(sim.World.Gold.ToArray(), relabelled.World.Gold.ToArray());
        for (int t = 0; t < 20; t++)
        {
            sim.Tick();
            relabelled.Tick();
            Assert.Equal(sim.StateHash(), relabelled.StateHash());
        }
        byte[] again = SaveTestRun.Save(relabelled);
        PresetAt(again, "standard");
    }

    /// <summary>
    /// A format-3 save of a four-seat AI match at tick 1,000 (forged from its format-4 save) loads as standard with every AI
    /// seat and plays to tick 3,000 hash-equal to the format-4 load every tick and to the unsaved run. M5-4d: an AI save before
    /// format 6 has no hunt memory and is refused (FormatVersionMismatch, the summary still lists it); the current save (format
    /// 6) loads and plays on hash-equal to the unsaved run.
    /// </summary>
    [Fact]
    [Trait("Category", "Stress")]
    public void AFormat3AiSave_LoadsAsStandard_AndPlaysOnAsTheUnsavedRun()
    {
        Simulation sim = AiSaveScenes.AiMatch(4, 4, AiDifficulty.Normal).Build();
        for (int t = 0; t < 1000; t++) sim.Tick();
        byte[] current = SaveTestRun.Save(sim);
        byte[] old = SaveBytes.AsFormat3(PreMapFixtures.WithoutHunt(current), PresetAt(current, "standard"));
        Assert.Equal(3, BinaryPrimitives.ReadInt32LittleEndian(old.AsSpan(SaveFormat.MagicLength)));
        Assert.Equal(SaveError.FormatVersionMismatch, SaveTestRun.TryLoad(Data, old));
        Simulation fromCurrent = SaveTestRun.Load(Data, current);
        for (int p = 0; p < 4; p++) Assert.NotNull(fromCurrent.AiOf(p));
        for (int t = 0; t < 2000; t++)
        {
            sim.Tick();
            fromCurrent.Tick();
            Assert.Equal(sim.StateHash(), fromCurrent.StateHash());
        }
        Assert.True(Rts.Sim.ViewApi.SaveSummary.TryParse(old, out var summary));
        Assert.Equal((3, 1000, 4), (summary!.FormatVersion, summary.Tick, summary.Seats.Length));
    }

    /// <summary>A format-3 header truncated at every byte from the team bytes to the heightmap tag: never a throw, never a load.</summary>
    [Fact]
    public void AFormat3Save_TruncatedAnywhereAroundTheMissingField_IsRefused()
    {
        // M5-4d: an AI save in format 3 is refused whole (no hunt memory), so the sweep runs on the human save, which loads.
        byte[] current = SaveBytes.Small(ai: false);
        int at = PresetAt(current, "standard");
        byte[] old = SaveBytes.AsFormat3(current, at);
        Assert.Equal(SaveError.None, SaveTestRun.TryLoad(Data, old));
        for (int cut = at - 4; cut < at + 8; cut++)
        {
            byte[] shortened = new byte[cut + SaveFormat.ChecksumLength];
            old.AsSpan(0, cut).CopyTo(shortened);
            BinaryPrimitives.WriteInt64LittleEndian(shortened.AsSpan(SaveFormat.LengthOffset), shortened.Length);
            Assert.NotEqual(SaveError.None, SaveTestRun.TryLoad(Data, SaveBytes.Rechecksum(shortened)));
        }
    }

    // ---------- replay format 8 ----------

    /// <summary>
    /// The golden (format 6) plays to every checkpoint; the same replay written as format 7 (team 0 tokens) and as format 8 at
    /// standard plays to every checkpoint too; as format 8 at high it diverges at the first checkpoint.
    /// </summary>
    [Fact]
    public void TheGolden_InFormats6To8AtStandard_PlaysToEveryCheckpoint_AndAtHighDiverges()
    {
        Assert.Equal(ReplayError.None, ReplayFormat.TryReadFile(ReplayTestRun.GoldenPath, out Replay? recorded));
        Replay golden = ReplayTestRun.With(recorded!, dataHash: Data.ContentHash()); // M6-SH1: the header is the recording-time hash
        Assert.Equal(6, golden.FormatVersion);
        Assert.Equal("standard", golden.StartingResources);
        foreach (int version in new[] { 6, 7, 8 })
        {
            Replay r = ReplayTestRun.With(golden, formatVersion: version);
            byte[] bytes = ReplayFormat.Write(r);
            Assert.Equal(ReplayError.None, ReplayFormat.TryRead(bytes, out Replay? back));
            Assert.Equal(version >= 8, Encoding.ASCII.GetString(bytes).Contains("\nstarting-resources standard\n"));
            ReplayResult played = ReplayPlayer.Run(back!, Data);
            Assert.True(played.Ok, $"format {version}: {played}");
            Assert.Equal(golden.Checkpoints.Length, back!.Checkpoints.Length);
        }
        Replay high = ReplayTestRun.With(golden, formatVersion: 8, startingResources: "high");
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(ReplayFormat.Write(high), out Replay? highBack));
        ReplayResult diverged = ReplayPlayer.Run(highBack!, Data);
        Assert.Equal(ReplayError.CheckpointMismatch, diverged.Error);
        Assert.Equal(golden.Checkpoints[0].Tick, diverged.Tick);
    }

    /// <summary>
    /// A recorded 2v2 AI match at standard (format 7, as the build before presets wrote it) and at high (format 8), 2,000
    /// ticks: both read back from text and play to every checkpoint; the format-7 text has no preset line.
    /// </summary>
    [Fact]
    [Trait("Category", "Stress")]
    public void ATeamAiRecording_AtStandardIsFormat7_AtHighFormat8_BothPlayToEveryCheckpoint()
    {
        foreach (string preset in new[] { "standard", "high" })
        {
            var seats = TeamTests.Seats(1, 2, 1, 2).Select(s => s with { Controller = SeatController.Ai }).ToArray();
            Simulation sim = AiScenes.Generated(seats, 3, record: true, out ReplayRecorder? rec, configure: c => c with { StartingResources = preset });
            for (int t = 0; t < 2000; t++) sim.Tick();
            Replay replay = rec!.ToReplay();
            Assert.Equal(preset == "standard" ? 7 : 8, replay.FormatVersion);
            byte[] bytes = ReplayFormat.Write(replay);
            Assert.Equal(preset != "standard", Encoding.ASCII.GetString(bytes).Contains("starting-resources"));
            Assert.Equal(ReplayError.None, ReplayFormat.TryRead(bytes, out Replay? back));
            ReplayResult played = ReplayPlayer.Run(back!, Data);
            Assert.True(played.Ok, $"{preset}: {played}");
            Assert.True(back!.Checkpoints.Length >= 10);
        }
    }

    /// <summary>
    /// Hostile <c>starting-resources</c> lines on a format-8 file (re-sealed): never a throw; an accepted read either plays
    /// (a shipped id) or is refused InvalidHeader by playback before any tick.
    /// </summary>
    [Fact]
    public void HostilePresetLines_NeverThrow_AndUnknownIdsNeverPlay()
    {
        Replay small = ReplayTestRun.RecordSmall(seed: 5, ticks: 100).Recorder.ToReplay();
        string text = Encoding.ASCII.GetString(ReplayFormat.Write(ReplayTestRun.With(small, formatVersion: 8, startingResources: "high")));
        Assert.Contains("\nstarting-resources high\n", text);
        string[] values =
        {
            "", " ", "high high", "high\t", "standard\r", "x", "a" + new string('_', 63), new string('z', 64), new string('z', 65),
            new string('z', 10_000), "hé", "0", "_high", "high_", "standard ", "STANDARD", "-", "--", "high\nmap.width 1",
        };
        foreach (string v in values)
        {
            string forged = text.Replace("\nstarting-resources high\n", $"\nstarting-resources {v}\n");
            byte[] body = Encoding.UTF8.GetBytes(forged[..forged.LastIndexOf("checksum ", StringComparison.Ordinal)]);
            byte[] bytes = ReplayFormat.Seal(Encoding.Latin1.GetString(body));
            ReplayError e = ReplayFormat.TryRead(bytes, out Replay? r);
            if (e != ReplayError.None) continue;
            ReplayResult played = ReplayPlayer.Run(r!, Data);
            if (Data.Rules.FindStartingResources(r!.StartingResources) < 0)
            {
                Assert.Equal(ReplayError.InvalidHeader, played.Error);
                Assert.Equal(-1, played.Tick);
            }
            else Assert.True(played.Ok || played.Error == ReplayError.CheckpointMismatch, $"'{v}': {played}");
        }
    }

    // ---------- extreme presets ----------

    private static readonly Lazy<GameData> s_extreme = new(() => AiScenes.DataWith(dir => dir.EditJson("common/rules.json", r =>
    {
        r["startingResources"] = System.Text.Json.Nodes.JsonNode.Parse(
            $"[{{\"id\":\"standard\",\"displayName\":\"S\",\"gold\":200,\"wood\":200}},"
            + $"{{\"id\":\"broke\",\"displayName\":\"B\",\"gold\":0,\"wood\":0}},"
            + $"{{\"id\":\"max\",\"displayName\":\"M\",\"gold\":{DataLimits.MaxInteger},\"wood\":{DataLimits.MaxInteger}}}]");
    })));

    /// <summary>
    /// Four AI seats at 0 / 0 and at the 1,000,000 / 1,000,000 data maximum, 6,000 ticks: no throw, the banks never negative
    /// and never above the start plus everything gathered, twin runs hash-equal; at the maximum the seats train armies,
    /// at 0 / 0 nobody spends before a deposit.
    /// </summary>
    [Theory]
    [Trait("Category", "Stress")]
    [InlineData("broke")]
    [InlineData("max")]
    public void FourAiSeats_AtAnExtremePreset_KeepTheInvariants_AndAreDeterministic(string preset)
    {
        GameData data = s_extreme.Value;
        var seats = AiScenes.Seats(AiDifficulty.Hard, SeatController.Ai, SeatController.Ai, SeatController.Ai, SeatController.Ai);
        Simulation a = AiScenes.Generated(seats, 5, record: false, out _, configure: c => c with { Data = data, StartingResources = preset });
        Simulation b = AiScenes.Generated(seats, 5, record: false, out _, configure: c => c with { Data = data, StartingResources = preset });
        StartingResourcesDef start = a.World.Config.StartingPreset;
        Assert.Equal(preset, start.Key);
        for (int p = 0; p < 4; p++) Assert.Equal((start.Gold, start.Wood), (a.World.Gold[p], a.World.Wood[p]));
        for (int t = 0; t < 6000; t++)
        {
            a.Tick();
            b.Tick();
            for (int p = 0; p < 4; p++)
            {
                Assert.True(a.World.Gold[p] >= 0 && a.World.Wood[p] >= 0, $"tick {a.TickNumber} p{p}: negative bank");
                Assert.True((long)a.World.Gold[p] <= (long)start.Gold + a.World.GatheredGold[p] + 100_000, $"tick {a.TickNumber} p{p}: gold from nowhere");
            }
            if (t % 100 == 0) Assert.Equal(a.StateHash(), b.StateHash());
        }
        Assert.Equal(a.StateHash(), b.StateHash());
        for (int p = 0; p < 4; p++)
            _out.WriteLine($"{preset} p{p}: gold {a.World.Gold[p]} wood {a.World.Wood[p]} pop {a.World.HalfPop[p] / 2.0}/{a.World.HalfPopCap[p] / 2.0} waves {a.AiOf(p)!.Board.WavesStarted}");
        // At the maximum the seats train (some seat may be overrun by then: the sum, not each seat).
        if (preset == "max")
        {
            int pop = 0;
            for (int p = 0; p < 4; p++) pop += a.World.HalfPop[p];
            Assert.True(pop > 2 * 4 * 15, $"four seats at the maximum hold only {pop / 2.0} pop at 5:00");
        }
    }

    // ---------- allocation ----------

    /// <summary>The preset lookups (by id and the config's resolved def) allocate nothing; a high AI match's tick allocates nothing either.</summary>
    [Fact]
    public void ThePresetLookup_AndAHighTick_Allocate0Bytes()
    {
        PlayerSeat[] seats = AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Ai);
        Simulation sim = AiScenes.Generated(seats, 2, record: false, out _, configure: c => c with { StartingResources = "high" });
        SimConfig config = sim.World.Config;
        int found = 0;
        StartingResourcesDef? def = null;
        Action lookup = () =>
        {
            for (int i = 0; i < 1000; i++)
            {
                found += Data.Rules.FindStartingResources("high");
                def = config.StartingPreset;
            }
        };
        AllocationProbe.AssertZero(lookup, _out);
        Assert.Equal("high", def!.Key);
        for (int t = 0; t < 600; t++) sim.Tick();
        Action tick = () => { for (int t = 0; t < 50; t++) sim.Tick(); };
        AllocationProbe.AssertZero(tick, _out);
    }
}
