using System.Text;
using System.Text.Json.Nodes;
using Rts.Cli;
using Rts.Sim.Data;
using Rts.Sim.Map;

namespace Rts.Sim.Tests.Cli;

/// <summary>
/// M6-S8: the <c>mirror-map</c> verb (tools/Rts.Cli, docs/03 "Implementation (M6-S8)"), called in-process: point symmetry
/// on a small north-half source (every cell, ramp, start, mine and tree has its centre mirror, a ramp's <c>up</c> flipped),
/// quad symmetry's order, and the refusals (an item outside its part, a bad option, an output the loader refuses), each one
/// line on stderr with exit 1. The shipped maps' own mirror proof is in <c>MapFileTests</c>.
/// </summary>
public sealed class MirrorMapCliTests : IDisposable
{
    private readonly List<string> _temp = new();

    public void Dispose()
    {
        foreach (string p in _temp) File.Delete(p);
    }

    private string Temp(string? text = null)
    {
        string p = Path.Combine(Path.GetTempPath(), $"rts-mirror-{Guid.NewGuid():N}.json");
        _temp.Add(p);
        if (text != null) File.WriteAllText(p, text);
        return p;
    }

    private static (int Exit, string Out, string Err) Cli(params string[] args)
    {
        var o = new StringWriter();
        var e = new StringWriter();
        int exit = CliRunner.Run(args, o, e);
        return (exit, o.ToString(), e.ToString());
    }

    /// <summary>
    /// A 128 north half: flat level 0 with a level-1 mesa (x 50-77, y 20-39) reached by one ramp from the south, one start
    /// at (22, 44) (its mirror (105, 83) is 83 cells off), two mines west of it and a woodlot north of it.
    /// </summary>
    private static JsonObject PointSource()
    {
        var rows = new JsonArray();
        for (int y = 0; y < 64; y++)
        {
            var sb = new StringBuilder();
            for (int x = 0; x < 128; x++) sb.Append(x >= 50 && x <= 77 && y >= 20 && y <= 39 ? '1' : '0');
            rows.Add(sb.ToString());
        }
        return new JsonObject
        {
            ["id"] = "mirror_probe",
            ["displayName"] = "Mirror Probe",
            ["description"] = "A test map.",
            ["players"] = 2,
            ["size"] = 128,
            ["terrain"] = rows,
            ["ramps"] = new JsonArray(new JsonObject { ["x"] = 60, ["y"] = 40, ["width"] = 3, ["height"] = 4, ["up"] = "north" }),
            ["starts"] = new JsonArray(new JsonObject { ["x"] = 22, ["y"] = 44 }),
            ["mines"] = new JsonArray(
                new JsonObject { ["x"] = 13, ["y"] = 42, ["gold"] = 2500 },
                new JsonObject { ["x"] = 13, ["y"] = 46, ["gold"] = 2400 }),
            ["forests"] = new JsonArray(new JsonObject { ["x"] = 17, ["y"] = 31, ["rows"] = new JsonArray("TTTTT", "TTT", ".TT") }),
        };
    }

    [Fact]
    public void PointSymmetry_MirrorsEveryCellAndItem_ThroughTheCentre_AndTheFileLoads()
    {
        string src = Temp(PointSource().ToJsonString()), dst = Temp();
        var r = Cli("mirror-map", src, dst, "--symmetry", "point", "--data", TestDataDir.Shipped);
        Assert.True(r.Exit == 0, r.Err);
        Assert.Equal("", r.Err);
        Assert.Contains("it loads", r.Out);
        Assert.Empty(DataLoader.CheckMapFile(TestDataDir.Shipped, dst));

        JsonNode map = JsonNode.Parse(File.ReadAllText(dst))!;
        JsonArray terrain = map["terrain"]!.AsArray();
        Assert.Equal(128, terrain.Count);
        for (int y = 0; y < 128; y++)
            for (int x = 0; x < 128; x++)
                Assert.Equal(terrain[y]!.GetValue<string>()[x], terrain[127 - y]!.GetValue<string>()[127 - x]);
        Assert.Equal('1', terrain[100]!.GetValue<string>()[60]); // the south mesa, the north one's mirror
        // Each item, then its centre mirror: the anchor moves by the footprint, the ramp's up flips.
        Assert.Equal("""[{"x":60,"y":40,"width":3,"height":4,"up":"north"},{"x":65,"y":84,"width":3,"height":4,"up":"south"}]""", map["ramps"]!.ToJsonString());
        Assert.Equal("""[{"x":22,"y":44},{"x":105,"y":83}]""", map["starts"]!.ToJsonString());
        Assert.Equal("""[{"x":13,"y":42,"gold":2500},{"x":113,"y":84,"gold":2500},{"x":13,"y":46,"gold":2400},{"x":113,"y":80,"gold":2400}]""",
            map["mines"]!.ToJsonString());
        // The forest's trees: (17-21, 31), (17-19, 32), (18-19, 33) and their mirrors (127 - x, 127 - y).
        Assert.Equal("""[{"x":17,"y":31,"rows":["TTTTT","TTT",".TT"]},{"x":106,"y":94,"rows":["..TT.","..TTT","TTTTT"]}]""",
            map["forests"]!.ToJsonString());
    }

    [Fact]
    public void QuadSymmetry_ListsEachItem_ThenItsCentre_EastWest_AndNorthSouthMirrors()
    {
        // The shipped Pale Hills source: its one start becomes four, start 1 the centre mirror of start 0.
        string src = Path.Combine(TestDataDir.RepoRoot(), "tools", "Rts.Cli", "maps", "pale_hills.quad.json"), dst = Temp();
        var r = Cli("mirror-map", src, dst, "--symmetry", "quad", "--data", TestDataDir.Shipped);
        Assert.True(r.Exit == 0, r.Err);
        JsonNode source = JsonNode.Parse(File.ReadAllText(src))!, map = JsonNode.Parse(File.ReadAllText(dst))!;
        int x = source["starts"]![0]!["x"]!.GetValue<int>(), y = source["starts"]![0]!["y"]!.GetValue<int>();
        Assert.Equal($$"""[{"x":{{x}},"y":{{y}}},{"x":{{255 - x}},"y":{{255 - y}}},{"x":{{255 - x}},"y":{{y}}},{"x":{{x}},"y":{{255 - y}}}]""",
            map["starts"]!.ToJsonString());
    }

    [Theory]
    [InlineData("mines", 0, "x", 127, "mines[0]: (127, 42) 2 x 2 leaves the source part (128 x 64 cells from the north-west corner)")] // north half: x is free, y isn't
    [InlineData("mines", 0, "y", 63, "mines[0]: (13, 63) 2 x 2 leaves the source part")]
    [InlineData("ramps", 0, "up", "up", "ramps[0].up: unknown side 'up'")]
    [InlineData("starts", 0, "y", 64, "starts[0]: (22, 64) 1 x 1 leaves the source part")]
    public void AnItemOutsideItsPart_OrMalformed_IsOneErrorLine_Exit1_AndNothingWritten(string list, int index, string field, object value, string message)
    {
        JsonObject s = PointSource();
        s[list]![index]![field] = value is int i ? JsonValue.Create(i) : JsonValue.Create((string)value);
        string src = Temp(s.ToJsonString()), dst = Temp();
        var r = Cli("mirror-map", src, dst, "--symmetry", "point", "--data", TestDataDir.Shipped);
        Assert.Equal(1, r.Exit);
        if (message.EndsWith(')')) Assert.EndsWith(message, r.Err.Trim());
        else Assert.Contains(message, r.Err);
        Assert.Single(r.Err.Trim().Split('\n'));
        Assert.False(File.Exists(dst), "a refused source wrote the out file");
    }

    [Theory]
    [InlineData(new[] { "mirror-map", "a.json", "b.json" }, "--symmetry is required")]
    [InlineData(new[] { "mirror-map", "a.json", "b.json", "--symmetry", "radial" }, "--symmetry must be point or quad")]
    [InlineData(new[] { "mirror-map", "a.json", "--symmetry", "quad" }, "mirror-map takes a source path and an out path")]
    [InlineData(new[] { "mirror-map", "a.json", "b.json", "--symmetry", "quad", "--order", "across" }, "unknown option '--order' for mirror-map")]
    public void ABadCommandLine_IsAUsageError(string[] args, string message)
    {
        var r = Cli(args);
        Assert.Equal(1, r.Exit);
        Assert.Contains(message, r.Err);
    }

    [Fact]
    public void QuadSymmetry_OnAHalfSource_IsRefused()
    {
        string src = Temp(PointSource().ToJsonString()), dst = Temp();
        var r = Cli("mirror-map", src, dst, "--symmetry", "quad", "--data", TestDataDir.Shipped);
        Assert.Equal(1, r.Exit);
        Assert.Contains("quad symmetry makes 4 starts; players is 2", r.Err);
    }

    /// <summary>An output the loader refuses (the two starts 41 cells apart once mirrored) is written, every loader error printed, exit 1.</summary>
    [Fact]
    public void AMirroredMapTheLoaderRefuses_PrintsItsErrors_Exit1()
    {
        JsonObject s = PointSource();
        s["starts"]![0]!["x"] = 43;
        s["starts"]![0]!["y"] = 60;
        s["mines"]![0]!["x"] = 34;
        s["mines"]![0]!["y"] = 50;
        s["mines"]![1]!["x"] = 30;
        s["mines"]![1]!["y"] = 56;
        string src = Temp(s.ToJsonString()), dst = Temp();
        var r = Cli("mirror-map", src, dst, "--symmetry", "point", "--data", TestDataDir.Shipped);
        Assert.Equal(1, r.Exit);
        Assert.True(File.Exists(dst));
        Assert.Contains("starts[1]", r.Err);
        Assert.Contains("too close to starts[0]", r.Err);
        Assert.Contains("was written but does not load", r.Err);
    }
}
