using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;
using Rts.Sim.Data;
using Rts.Sim.Replays;
using Rts.Sim.Save;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA, M6-S7 (session 2026-11-05-1716): attacks on the hand-made map file and the save format 5 / replay format 9 that carry
/// its id. The loader's edges the developer's refusal rows don't cover (a size the rows don't fill, a non-integer size,
/// negative cells, a zero-width ramp, two ramps on one spot, a null row, an empty forest, no mines at all, an id too long, a
/// map with no ramps, a file whose id differs from its name); the start order is the file's (player 0 stands at
/// <c>starts[0]</c>); forged format-5 saves (every resealed truncation of the header, 1,024 resealed single-byte forgeries,
/// map ids with bad characters, too long, negative or huge lengths, a format-5 save relabelled 4) and forged format-9 replays
/// (1,024 resealed single-byte forgeries, a map line in a format-8 file, a map with too many players) are refused cleanly:
/// a <see cref="SaveError"/> / <see cref="ReplayError"/>, never an exception.
/// </summary>
[Collection(SerialCollection.Name)]
public class MapFileQaTests
{
    private const string RarakuFile = "maps/raraku.json";

    private readonly ITestOutputHelper _out;

    public MapFileQaTests(ITestOutputHelper output) => _out = output;

    // ---------- the loader ----------

    private static DataLoadResult LoadEdited(Action<JsonObject> edit)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson(RarakuFile, edit);
        return DataLoader.LoadAll(dir.Path);
    }

    private static void AssertOneError(DataLoadResult load, string path, string message)
    {
        Assert.False(load.Ok, "the broken map loaded");
        DataError e = Assert.Single(load.Errors);
        Assert.Equal(RarakuFile, e.File);
        Assert.Equal(path, e.Path);
        Assert.Contains(message, e.Message);
    }

    /// <summary>Size 256 on a file of 128 rows: one error for the row count (the rows aren't read one by one).</summary>
    [Fact]
    public void Size256_On128Rows_IsOneRowCountError()
    {
        AssertOneError(LoadEdited(r => r["size"] = 256), "terrain", "128 rows, expected 256");
    }

    /// <summary>A size written as a string, or a fraction, is refused (a type error, no crash, the map left out).</summary>
    [Theory]
    [InlineData("\"128\"")]
    [InlineData("128.5")]
    [InlineData("null")]
    public void ASizeNotAnInteger_IsRefused_NotCrashed(string json)
    {
        DataLoadResult load = LoadEdited(r => r["size"] = JsonNode.Parse(json));
        Assert.False(load.Ok);
        Assert.All(load.Errors, e => Assert.Equal(RarakuFile, e.File));
        _out.WriteLine(string.Join(" | ", load.Errors));
    }

    /// <summary>A negative start cell, a zero-width ramp, a null terrain row, a forest of no rows, an id of 65 characters: one error each.</summary>
    [Theory]
    [InlineData("negative-start", "starts[1]", "off the 128 x 128 map")]
    [InlineData("zero-ramp", "ramps[0].width", "below the minimum 1")]
    [InlineData("null-row", "terrain[40]", "0 cells, expected 128")]
    [InlineData("empty-forest", "forests[4].rows", "a forest with no tree")]
    [InlineData("long-id", "id", "longer than 64")]
    [InlineData("twin-ramp", "ramps[6]", "overlaps ramps[0]")]
    public void AnEdge_IsOneError(string edit, string path, string message)
    {
        DataLoadResult load = LoadEdited(r =>
        {
            switch (edit)
            {
                case "negative-start": r["starts"]![1]!["x"] = -1; break;
                case "zero-ramp": r["ramps"]![0]!["width"] = 0; break;
                case "null-row": r["terrain"]![40] = null; break;
                case "empty-forest": r["forests"]![4]!["rows"] = new JsonArray(); break;
                case "long-id": r["id"] = new string('a', 65); break;
                case "twin-ramp": r["ramps"]!.AsArray().Add(r["ramps"]![0]!.DeepClone()); break;
            }
        });
        AssertOneError(load, path, message);
    }

    /// <summary>No mines at all: each start is short of its two mines, one error per start, nothing else.</summary>
    [Fact]
    public void NoMines_IsOneErrorPerStart()
    {
        DataLoadResult load = LoadEdited(r => r["mines"] = new JsonArray());
        Assert.False(load.Ok);
        Assert.Equal(new[] { "starts[0]", "starts[1]" }, load.Errors.Select(e => e.Path).ToArray());
        Assert.All(load.Errors, e => Assert.Contains("0 gold mine(s)", e.Message));
    }

    /// <summary>
    /// No ramps: the two mesas can't be climbed. The map may load (the nav grid seals unreachable pockets, as for a generated
    /// map), but then no mesa cell is passable: no unit can be ordered onto ground it can't reach.
    /// </summary>
    [Fact]
    public void NoRamps_TheMesasAreSealed_OrTheMapIsRefused()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson(RarakuFile, r => r["ramps"] = new JsonArray());
        DataLoadResult load = DataLoader.LoadAll(dir.Path);
        _out.WriteLine(load.Ok ? "loaded" : string.Join(" | ", load.Errors));
        if (!load.Ok)
        {
            Assert.Single(load.Errors);
            return;
        }
        var w = new World(new SimConfig(1, 2, 16, 64) { Data = load.Data!, MapId = "raraku" });
        int passableHigh = 0;
        for (int y = 0; y < 128; y++)
            for (int x = 0; x < 128; x++)
                if (w.Heightmap.LevelAt(x, y) > 0 && w.NavGrid.IsPassable(x, y)) passableHigh++;
        Assert.Equal(0, passableHigh);
    }

    /// <summary>
    /// Mutation guard (QA's mutant "drop the connectivity check" survived every test): a hollow square of trees in the open
    /// centre walls in a 3 x 3 pocket of open ground. The flood finds it: one error, the map left out.
    /// </summary>
    [Fact]
    public void AForestRingWallingInAPocket_IsOneError()
    {
        DataLoadResult load = LoadEdited(r =>
        {
            var rows = new JsonArray("TTTTT", "T...T", "T...T", "T...T", "TTTTT");
            r["forests"]!.AsArray().Add(new JsonObject { ["x"] = 60, ["y"] = 60, ["rows"] = rows });
        });
        AssertOneError(load, "", "cut the map in two");
    }

    /// <summary>A file named other than its id loads under its id (the id names it, the file name only orders the list).</summary>
    [Fact]
    public void AFileNamedOtherThanItsId_LoadsUnderItsId()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        File.Move(dir.FullPath(RarakuFile), dir.FullPath("maps/desert.json"));
        DataLoadResult load = DataLoader.LoadAll(dir.Path);
        Assert.True(load.Ok, string.Join(" | ", load.Errors));
        Assert.Equal(0, load.Data!.FindMap("raraku"));
        Assert.Equal(-1, load.Data.FindMap("desert"));
        Assert.Equal(TestSim.Data.ContentHash(), load.Data.ContentHash());
    }

    /// <summary>A non-JSON file and a subfolder under maps/ are ignored; a malformed JSON map is one error, the other data loads clean.</summary>
    [Fact]
    public void StrayFiles_AreIgnored_AndAMalformedMapIsOneError()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        File.WriteAllText(dir.FullPath("maps/notes.txt"), "not a map");
        Directory.CreateDirectory(dir.FullPath("maps/drafts"));
        File.WriteAllText(dir.FullPath("maps/drafts/x.json"), "{");
        Assert.True(DataLoader.LoadAll(dir.Path).Ok);
        File.WriteAllText(dir.FullPath("maps/broken.json"), "{ \"id\": \"broken\", ");
        DataLoadResult load = DataLoader.LoadAll(dir.Path);
        DataError e = Assert.Single(load.Errors);
        Assert.Equal("maps/broken.json", e.File);
    }

    /// <summary>
    /// Mutation guard (QA's loader mutation "swap the start order"): player p's start is the file's <c>starts[p]</c>, read
    /// from the raw JSON, not only from <see cref="MapDef.Starts"/> (which a loader bug would change in step with the world).
    /// </summary>
    [Fact]
    public void PlayerOrder_IsTheFilesStartOrder()
    {
        JsonNode raw = JsonNode.Parse(File.ReadAllText(Path.Combine(TestDataDir.Shipped, "maps", "raraku.json")))!;
        var w = new World(new SimConfig(1, 2, 16, 64) { Data = TestSim.Data, MapId = "raraku" });
        for (int p = 0; p < 2; p++)
        {
            int x = raw["starts"]![p]!["x"]!.GetValue<int>(), y = raw["starts"]![p]!["y"]!.GetValue<int>();
            Assert.Equal(y * 128 + x, w.StartLocations[p].Cell);
        }
        // and the first node is the file's first mine, at its gold
        JsonNode mine = raw["mines"]![0]!;
        Assert.Equal(mine["y"]!.GetValue<int>() * 128 + mine["x"]!.GetValue<int>(), w.Resources.Cell[0]);
        Assert.Equal(mine["gold"]!.GetValue<int>(), w.Resources.Remaining[0]);
    }

    // ---------- save format 5 ----------

    private static byte[] RarakuSave()
    {
        var sim = new Simulation(TestSim.Config(7, 2, 32, 32) with { MapId = "raraku" });
        for (int t = 0; t < 3; t++) sim.Tick();
        return SaveTestRun.Save(sim);
    }

    /// <summary>Offset of the map id's string field in a one-faction-name-known 2-seat format-5 save (after the preset).</summary>
    private static int MapIdAt(byte[] bytes)
    {
        int at = bytes.AsSpan().IndexOf("\u0006\0\0\0raraku"u8);
        Assert.True(at > 0, "no map id field");
        return at;
    }

    private static byte[] WithLength(byte[] bytes)
    {
        byte[] copy = (byte[])bytes.Clone();
        BinaryPrimitives.WriteInt64LittleEndian(copy.AsSpan(SaveFormat.LengthOffset), copy.Length);
        return SaveBytes.Rechecksum(copy);
    }

    /// <summary>Loads <paramref name="bytes"/>; any exception fails the row with the case named.</summary>
    private static SaveError TryLoad(byte[] bytes, string what)
    {
        try
        {
            return SaveTestRun.TryLoad(TestSim.Data, bytes);
        }
        catch (Exception e) when (e is not Xunit.Sdk.XunitException)
        {
            Assert.Fail($"{what}: {e.GetType().Name}: {e.Message}");
            return SaveError.None;
        }
    }

    /// <summary>
    /// Every cut of the header through the map id and 8 bytes past it, the length and checksum resealed so only the reader's
    /// own checks can refuse it: never loads, never throws.
    /// </summary>
    [Fact]
    public void EveryResealedHeaderTruncation_IsRefusedCleanly()
    {
        byte[] bytes = RarakuSave();
        int end = MapIdAt(bytes) + 4 + 6 + 8;
        var errors = new SortedDictionary<SaveError, int>();
        for (int cut = SaveFormat.FixedPrefix; cut < end; cut++)
        {
            byte[] forged = new byte[bytes.Length - 1];
            bytes.AsSpan(0, cut).CopyTo(forged);
            bytes.AsSpan(cut + 1).CopyTo(forged.AsSpan(cut));
            SaveError e = TryLoad(WithLength(forged), $"byte {cut} cut");
            Assert.NotEqual(SaveError.None, e);
            errors[e] = errors.GetValueOrDefault(e) + 1;
        }
        // and the plain (unsealed) truncations of the whole header
        for (int len = 0; len < end; len++)
            Assert.NotEqual(SaveError.None, TryLoad(bytes.AsSpan(0, len).ToArray(), $"truncated to {len}"));
        _out.WriteLine(string.Join(", ", errors.Select(kv => $"{kv.Key} {kv.Value}")));
    }

    /// <summary>
    /// 1,024 single-byte forgeries (seeded), resealed: 512 in the header (through the map id), 512 anywhere. Each is refused
    /// with a <see cref="SaveError"/> or loads; none throws. Those in the map id's bytes never load.
    /// </summary>
    [Fact]
    public void A1024ResealedSingleByteForgeries_NeverThrow()
    {
        byte[] bytes = RarakuSave();
        int idAt = MapIdAt(bytes), headerEnd = idAt + 4 + 6;
        var rng = new System.Random(1716);
        var errors = new SortedDictionary<SaveError, int>();
        for (int i = 0; i < 1024; i++)
        {
            int at = i < 512 ? rng.Next(SaveFormat.FixedPrefix, headerEnd) : rng.Next(SaveFormat.FixedPrefix, bytes.Length - SaveFormat.ChecksumLength);
            byte[] forged = (byte[])bytes.Clone();
            forged[at] ^= (byte)rng.Next(1, 256);
            if (at >= SaveFormat.LengthOffset && at < SaveFormat.LengthOffset + 8) continue; // the length: the next row's subject
            SaveError e = TryLoad(SaveBytes.Rechecksum(forged), $"byte {at} forged");
            errors[e] = errors.GetValueOrDefault(e) + 1;
            if (at >= idAt && at < headerEnd) Assert.NotEqual(SaveError.None, e);
        }
        _out.WriteLine(string.Join(", ", errors.Select(kv => $"{kv.Key} {kv.Value}")));
    }

    /// <summary>A forged map id field: bad characters, upper case, 65 characters, a negative or a huge length; each refused, none throws.</summary>
    [Theory]
    [InlineData("Raraku")]
    [InlineData("rar aku")]
    [InlineData("raraku\n")]
    [InlineData("-")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("#neg")]
    [InlineData("#huge")]
    public void AForgedMapIdField_IsRefused(string id)
    {
        byte[] bytes = RarakuSave();
        int map = MapIdAt(bytes);
        byte[] forged;
        if (id.StartsWith('#'))
        {
            forged = (byte[])bytes.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(forged.AsSpan(map), id == "#neg" ? -6 : int.MaxValue);
        }
        else
        {
            byte[] idBytes = Encoding.ASCII.GetBytes(id);
            forged = new byte[bytes.Length - 6 + idBytes.Length];
            bytes.AsSpan(0, map).CopyTo(forged);
            BinaryPrimitives.WriteInt32LittleEndian(forged.AsSpan(map), idBytes.Length);
            idBytes.CopyTo(forged.AsSpan(map + 4));
            bytes.AsSpan(map + 10).CopyTo(forged.AsSpan(map + 4 + idBytes.Length));
        }
        SaveError e = TryLoad(WithLength(forged), id);
        _out.WriteLine($"{id}: {e}");
        Assert.NotEqual(SaveError.None, e);
    }

    /// <summary>
    /// A format-5 save relabelled 4 with the id left in (read as the next section) is refused. With the id cut out too it is a
    /// well-formed format-4 save: it loads, names no map, and plays on hash-equal to the Raraku save, since a save stores
    /// its terrain (the HMAP section) and a load never rebuilds it from the id or the seed (M6-S2's rule, unchanged).
    /// </summary>
    [Fact]
    public void AFormat5SaveRelabelled4_IsRefused_AndWithTheIdCut_LoadsItsStoredTerrain()
    {
        byte[] bytes = RarakuSave();
        byte[] relabelled = (byte[])bytes.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(relabelled.AsSpan(SaveFormat.MagicLength), 4);
        Assert.NotEqual(SaveError.None, TryLoad(SaveBytes.Rechecksum(relabelled), "relabelled 4"));
        int map = MapIdAt(bytes);
        byte[] cut = new byte[bytes.Length - 10];
        bytes.AsSpan(0, map).CopyTo(cut);
        bytes.AsSpan(map + 10).CopyTo(cut.AsSpan(map));
        BinaryPrimitives.WriteInt32LittleEndian(cut.AsSpan(SaveFormat.MagicLength), 4);
        Simulation asGenerated = SaveTestRun.Load(TestSim.Data, WithLength(cut));
        Simulation asRaraku = SaveTestRun.Load(TestSim.Data, bytes);
        Assert.Equal("", asGenerated.World.Config.MapId);
        Assert.Equal("raraku", asRaraku.World.Config.MapId);
        for (int t = 0; t < 50; t++)
        {
            asGenerated.Tick();
            asRaraku.Tick();
            Assert.Equal(asRaraku.StateHash(), asGenerated.StateHash());
        }
    }

    // ---------- replay format 9 ----------

    private static Replay RarakuReplay()
    {
        var sim = new Simulation(TestSim.Config(6, 2, 16, 16) with { MapId = "raraku" });
        var rec = new ReplayRecorder(sim, checkpointInterval: 10, commandCapacity: 64);
        for (int t = 0; t < 40; t++) sim.Tick();
        return rec.ToReplay();
    }

    private static ReplayError ReadAndPlay(byte[] bytes, string what)
    {
        try
        {
            ReplayError e = ReplayFormat.TryRead(bytes, out Replay? r);
            if (e != ReplayError.None) return e;
            ReplayResult played = ReplayPlayer.Run(r!, TestSim.Data);
            return played.Ok ? ReplayError.None : played.Error;
        }
        catch (Exception e) when (e is not Xunit.Sdk.XunitException)
        {
            Assert.Fail($"{what}: {e.GetType().Name}: {e.Message}");
            return ReplayError.None;
        }
    }

    /// <summary>1,024 seeded single-byte forgeries of a format-9 recording's text, resealed, read and played: none throws.</summary>
    [Fact]
    public void A1024ResealedSingleByteReplayForgeries_NeverThrow()
    {
        byte[] bytes = ReplayFormat.Write(RarakuReplay());
        string text = Encoding.ASCII.GetString(bytes);
        int body = text.LastIndexOf("checksum ", StringComparison.Ordinal);
        int mapLine = text.IndexOf("\nmap raraku\n", StringComparison.Ordinal);
        var rng = new System.Random(9);
        var errors = new SortedDictionary<ReplayError, int>();
        for (int i = 0; i < 1024; i++)
        {
            int at = i < 256 ? rng.Next(mapLine, mapLine + 12) : rng.Next(0, body);
            byte[] forged = bytes.AsSpan(0, body).ToArray();
            forged[at] = (byte)rng.Next(0x20, 0x7F);
            ReplayError e = ReadAndPlay(ReplayFormat.Seal(Encoding.ASCII.GetString(forged)), $"byte {at}");
            errors[e] = errors.GetValueOrDefault(e) + 1;
        }
        _out.WriteLine(string.Join(", ", errors.Select(kv => $"{kv.Key} {kv.Value}")));
    }

    /// <summary>A map line in a format-8 file is refused; a format-9 header naming Raraku for 3 players is refused at playback.</summary>
    [Fact]
    public void AMapLineInAFormat8File_AndTooManyPlayers_AreRefused()
    {
        string text = Encoding.ASCII.GetString(ReplayFormat.Write(RarakuReplay()));
        string body = text[..text.LastIndexOf("checksum ", StringComparison.Ordinal)];
        Assert.NotEqual(ReplayError.None, ReadAndPlay(ReplayFormat.Seal(body.Replace("rts-replay 9\n", "rts-replay 8\n")), "format 8 with map"));
        string three = body.Replace("\nplayers 2\n", "\nplayers 3\n");
        Assert.NotEqual(ReplayError.None, ReadAndPlay(ReplayFormat.Seal(three), "three players"));
    }
}
