using System.Buffers.Binary;
using System.Text;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Rts.Sim.Save;
using Rts.Sim.Tests.Save;
using Rts.Sim.ViewApi;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA 2026-11-05-2148 (M6-V18): <see cref="SaveSummary.MapId"/>, the view track's read-only ViewApi accessor for a format-5
/// save's map id string (after the preset). A real Raraku AI match (High and Standard) reads its id at several ticks and the
/// read changes nothing (hash, tick, save bytes, and the match played on equals an unread twin); forged headers (ids of every
/// edge length, an empty id, unicode, a future version, a format-4 header with a string appended, an unreadable preset in
/// front of a good id) list as documented; a seeded fuzz of 4,000 mutated and cut format-5 headers never throws and never
/// gives a format 1-4 header a map id.
/// </summary>
public class SaveSummaryMapIdQaTests
{
    private const string Raraku = "raraku";

    // The offset of the preset's string field (right after the team bytes, format 4 on); the map id's follows it.
    private static int PresetAt(byte[] bytes) => Rts.Sim.Tests.ViewApi.SaveSummaryTests.TeamsAt(bytes) + BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(SaveBytes.HeaderOffsets(bytes).Players));

    private static int MapAt(byte[] bytes)
    {
        int preset = PresetAt(bytes);
        return preset + 4 + BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(preset));
    }

    // `bytes` with the map id string field replaced by `field` (raw bytes: the int32 length and the text, or anything).
    private static byte[] WithMapField(byte[] bytes, byte[] field)
    {
        int at = MapAt(bytes);
        int oldEnd = at + 4 + BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at));
        return bytes[..at].Concat(field).Concat(bytes[oldEnd..]).ToArray();
    }

    private static byte[] StringField(byte[] text)
    {
        var field = new byte[4 + text.Length];
        BinaryPrimitives.WriteInt32LittleEndian(field, text.Length);
        text.CopyTo(field, 4);
        return field;
    }

    private static Simulation RarakuAi(ulong seed, string preset)
    {
        PlayerSeat[] seats = AiScenes.Seats(AiDifficulty.Normal, SeatController.Human, SeatController.Ai);
        return AiScenes.Generated(seats, seed, record: false, out _, configure: c => c with { MapId = Raraku, StartingResources = preset });
    }

    [Theory]
    [InlineData("high")]
    [InlineData("standard")]
    public void ARarakuAiSave_ReadsItsMapIdAndPreset_AndReadingChangesNothing(string preset)
    {
        Simulation sim = RarakuAi(21, preset), twin = RarakuAi(21, preset);
        foreach (int tick in new[] { 0, 1, 200, 600 })
        {
            while (sim.TickNumber < tick) { sim.Tick(); twin.Tick(); }
            ulong hash = sim.StateHash();
            byte[] bytes = SaveTestRun.Save(sim);
            for (int i = 0; i < 3; i++)
            {
                using var stream = new MemoryStream(bytes, writable: false);
                Assert.True(SaveSummary.TryRead(stream, out SaveSummary? s), $"tick {tick}: not read");
                Assert.Equal(SaveFormat.CurrentVersion, s!.FormatVersion);
                Assert.Equal(Raraku, s.MapId);
                Assert.Equal(preset, s.StartingResources);
                Assert.Equal(tick, s.Tick);
                Assert.Equal(128, s.MapWidth);
            }
            Assert.Equal(hash, sim.StateHash());
            Assert.Equal(tick, sim.TickNumber);
            Assert.Equal(bytes, SaveTestRun.Save(sim));
        }
        // Played on after the reads, the match equals its twin that was never read.
        for (int i = 0; i < 200; i++) { sim.Tick(); twin.Tick(); }
        Assert.Equal(twin.StateHash(), sim.StateHash());
    }

    /// <summary>Forged map id fields: what the summary lists (the doc: "" unless a 1-256-byte UTF-8 string), never a throw.</summary>
    [Fact]
    public void ForgedMapIdFields_ListAsDocumented()
    {
        byte[] bytes = SaveTestRun.Save(MapFileTests.RarakuMatch(5, false, out _));
        Assert.Equal(Raraku, SaveBytes.StringAt(bytes, MapAt(bytes)));
        string max = new('m', SaveFormat.MaxString);
        (string Name, byte[] Field, string Want)[] cases =
        {
            ("empty id", StringField(Array.Empty<byte>()), ""),
            ("one char", StringField("r"u8.ToArray()), "r"),
            ("256 bytes", StringField(Encoding.UTF8.GetBytes(max)), max),
            ("257 bytes", StringField(Encoding.UTF8.GetBytes(max + "m")), ""),
            ("unicode", StringField(Encoding.UTF8.GetBytes("räraku_日本")), "räraku_日本"),
            ("path", StringField("..\\..\\maps/raraku"u8.ToArray()), "..\\..\\maps/raraku"),
            ("newline", StringField("rar\naku"u8.ToArray()), "rar\naku"),
            ("lone surrogate bytes", StringField(new byte[] { 0xED, 0xA0, 0x80 }), ""),
            ("length int.MinValue", BitConverter.GetBytes(int.MinValue), ""),
            ("length int.MaxValue", BitConverter.GetBytes(int.MaxValue), ""),
            ("length only, no bytes", BitConverter.GetBytes(6), ""),
            ("three bytes of length", new byte[] { 6, 0, 0 }, ""),
        };
        foreach ((string name, byte[] field, string want) in cases)
        {
            // Cut right after the field too, so a length past the bytes is not saved by the heightmap that follows.
            byte[] forged = WithMapField(bytes, field);
            byte[] cut = forged[..(MapAt(bytes) + field.Length)];
            foreach ((string how, byte[] b) in new[] { ("whole", forged), ("cut", cut) })
            {
                bool read = SaveSummary.TryParse(b, out SaveSummary? s);
                Assert.True(read, $"{name} ({how}): not listed");
                if (how == "cut" || field.Length >= 4 && BinaryPrimitives.ReadInt32LittleEndian(field) == field.Length - 4)
                    Assert.Equal(want, s!.MapId);
                else
                    Assert.True(s!.MapId.Length <= SaveFormat.MaxString, $"{name} ({how}): '{s.MapId}'");
                Assert.Equal(StartingResourcesDef.StandardId, s.StartingResources);
            }
        }
    }

    /// <summary>An empty format-5 map id lists with no map, and the load refuses it (docs/03: a generated map saves as 4).</summary>
    [Fact]
    public void AnEmptyFormat5MapId_ListsAsNoMap_AndTheLoadRefusesIt()
    {
        byte[] bytes = SaveTestRun.Save(MapFileTests.RarakuMatch(5, false, out _));
        byte[] forged = WithMapField(bytes, StringField(Array.Empty<byte>()));
        Assert.True(SaveSummary.TryParse(forged, out SaveSummary? s));
        Assert.Equal("", s!.MapId);
        Assert.NotEqual(SaveError.None, SaveTestRun.TryLoad(TestSim.Data, forged));
    }

    /// <summary>
    /// The version gate: a format-4 header with a map-id-shaped string after its preset reads no map (format 4 has none);
    /// a future version reads it; a preset field that doesn't read hides a good id after it (the reader can't find it).
    /// </summary>
    [Fact]
    public void TheVersionGate_AndAnUnreadablePreset_GiveNoMapId()
    {
        // M7-1: every save is format 10 (its header always holds a map id); the format-4 header is the same save as the build
        // before M7-1 wrote it.
        byte[] gen = PreMapFixtures.WithoutRegen(SaveTestRun.Save(SaveScenes.Menu(2).Build()));
        Assert.True(SaveSummary.TryParse(gen, out SaveSummary? g) && g!.FormatVersion == PreMapFixtures.Format4);
        int end = PresetAt(gen) + 4 + BinaryPrimitives.ReadInt32LittleEndian(gen.AsSpan(PresetAt(gen)));
        byte[] appended = gen[..end].Concat(StringField("raraku"u8.ToArray())).Concat(gen[end..]).ToArray();
        Assert.True(SaveSummary.TryParse(appended, out SaveSummary? a));
        Assert.Equal("", a!.MapId);

        byte[] bytes = SaveTestRun.Save(MapFileTests.RarakuMatch(5, false, out _));
        byte[] future = (byte[])bytes.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(future.AsSpan(SaveFormat.MagicLength), SaveFormat.CurrentVersion + 1);
        Assert.True(SaveSummary.TryParse(future, out SaveSummary? f));
        Assert.Equal(Raraku, f!.MapId);

        byte[] badPreset = (byte[])bytes.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(badPreset.AsSpan(PresetAt(bytes)), -5);
        Assert.True(SaveSummary.TryParse(badPreset, out SaveSummary? b));
        Assert.Equal("", b!.StartingResources);
        Assert.Equal("", b.MapId);
    }

    /// <summary>
    /// Seeded fuzz: 4,000 format-5 headers with 1-8 random bytes overwritten in the header region (biased to the preset and
    /// map fields) and a random cut; and the same on the version relabelled 1-4. Never throws; a listed format 1-4 header has
    /// no map id; any listed map id is at most 256 UTF-8 bytes.
    /// </summary>
    [Fact]
    public void FuzzedFormat5Headers_NeverThrow_AndOnlyFormat5OnReadsAMapId()
    {
        byte[] bytes = SaveTestRun.Save(MapFileTests.RarakuMatch(6, false, out _));
        int mapEnd = MapAt(bytes) + 4 + Raraku.Length;
        int presetAt = PresetAt(bytes);
        var rng = new Random(20261105);
        int listed = 0, withId = 0;
        for (int i = 0; i < 4000; i++)
        {
            byte[] b = bytes[..Math.Min(bytes.Length, mapEnd + 64)];
            int version = i % 5 == 0 ? 1 + rng.Next(4) : SaveFormat.CurrentVersion;
            BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(SaveFormat.MagicLength), version);
            int flips = 1 + rng.Next(8);
            for (int k = 0; k < flips; k++)
            {
                int at = rng.Next(3) == 0 ? rng.Next(SaveFormat.FixedPrefix, b.Length) : rng.Next(presetAt, Math.Min(b.Length, mapEnd + 8));
                b[at] = (byte)rng.Next(256);
            }
            int cut = rng.Next(4) == 0 ? rng.Next(presetAt - 8, b.Length + 1) : b.Length;
            bool read = SaveSummary.TryParse(b.AsSpan(0, cut), out SaveSummary? s);
            if (!read) continue;
            listed++;
            if (s!.FormatVersion < 5) Assert.Equal("", s.MapId);
            Assert.True(Encoding.UTF8.GetByteCount(s.MapId) <= SaveFormat.MaxString);
            if (s.MapId.Length > 0) withId++;
        }
        Assert.True(listed > 1000, $"only {listed} listed: the fuzz broke too much to test the id");
        Assert.True(withId > 100, $"only {withId} listed with an id");
    }
}
