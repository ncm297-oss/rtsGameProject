using System.Diagnostics;
using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Replays;
using Rts.Sim.Tests.Save;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M6-S7: the hand-made map file (<c>game/data/maps/</c>, docs/03 "Implementation (M6-S7)"): the shipped Raraku loads with
/// the generator's start contract; a match with <see cref="SimConfig.MapId"/> builds its terrain, nav grid, starts and nodes
/// from the file (drawing nothing from the map stream); it is deterministic in two processes, saves (format 5) and loads
/// hash-equal, records (format 9) and plays back to every checkpoint; and two Normal AI seats play a match on it to the end.
/// </summary>
[Collection(SerialCollection.Name)]
public class MapFileTests
{
    private const string Raraku = "raraku";

    private readonly ITestOutputHelper _out;

    public MapFileTests(ITestOutputHelper output) => _out = output;

    private static MapDef RarakuDef => TestSim.Data.Maps[TestSim.Data.FindMap(Raraku)];

    internal const string PaleHills = "pale_hills", VatharCrossing = "vathar_crossing";

    private static MapDef Def(string id) => TestSim.Data.Maps[TestSim.Data.FindMap(id)];

    /// <summary>M6-S8: a match on map <paramref name="id"/> of two AI seats (starts 0 and 1), as <see cref="RarakuMatch"/>, the generator params sized to the map's side.</summary>
    internal static Simulation MapMatch(string id, ulong seed, bool record, out ReplayRecorder? recorder, AiDifficulty a = AiDifficulty.Normal, AiDifficulty b = AiDifficulty.Normal)
    {
        int size = Def(id).Size;
        PlayerSeat[] seats = { new(0, SeatController.Ai, a), new(1, SeatController.Ai, b) };
        return AiScenes.Generated(seats, seed, record, out recorder, configure: c => c with { MapId = id, Map = c.Map with { Width = size, Height = size } });
    }

    /// <summary>A Raraku match of two AI seats (Normal unless given), each base a hall and five workers placed by commands as the game does (<see cref="AiScenes.Generated"/>).</summary>
    internal static Simulation RarakuMatch(ulong seed, bool record, out ReplayRecorder? recorder, AiDifficulty a = AiDifficulty.Normal, AiDifficulty b = AiDifficulty.Normal)
    {
        PlayerSeat[] seats = { new(0, SeatController.Ai, a), new(1, SeatController.Ai, b) };
        return AiScenes.Generated(seats, seed, record, out recorder, configure: c => c with { MapId = Raraku });
    }

    [Fact]
    public void TheShippedRaraku_Loads_WithItsFields()
    {
        int index = TestSim.Data.FindMap(Raraku);
        Assert.True(index >= 0, "no raraku in GameData.Maps");
        MapDef m = TestSim.Data.Maps[index];
        Assert.Equal(index, m.Id);
        Assert.Equal("Raraku", m.DisplayName);
        Assert.False(string.IsNullOrWhiteSpace(m.Description));
        Assert.Equal(2, m.Players);
        Assert.Equal(128, m.Size);
        Assert.Equal(128 * 128, m.Levels.Length);
        Assert.Equal(2, m.Starts.Length);
        Assert.Equal(6, m.Ramps.Length);
        int mines = m.Nodes.Count(n => TestSim.Data.Resources[n.TypeId].Resource == ResourceKind.Gold);
        Assert.Equal(12, mines);
        Assert.Equal(6, m.ForestCount);
        Assert.All(m.Nodes.Where(n => TestSim.Data.Resources[n.TypeId].Resource == ResourceKind.Wood), n => Assert.Equal(TestSim.Data.Rules.TreeWood, n.Amount));
        Assert.Equal(-1, TestSim.Data.FindMap("no_such_map"));
        // Every level the file uses, levels 0-2, appears (a mesa with a butte: the terrain is not flat).
        Assert.Equal(new byte[] { 0, 1, 2 }, m.Levels.Distinct().OrderBy(l => l).ToArray());
    }

    /// <summary>
    /// The generator's start contract on every shipped map, measured on the built world as the AI measures it: every start on
    /// one level, each two at least half the map side apart, each with <c>startMines.count</c> mines within 18 m and a tree
    /// within 24 m of its centre cell (the generator's defaults); the distances printed.
    /// </summary>
    [Fact]
    public void EveryShippedMap_KeepsTheGeneratorsStartContract()
    {
        Assert.NotEmpty(TestSim.Data.Maps);
        foreach (MapDef m in TestSim.Data.Maps)
        {
            var sim = new Simulation(new SimConfig(1, m.Players, 64, 64) { Data = TestSim.Data, MapId = m.Key, Map = MapGenParams.Default with { Width = m.Size, Height = m.Size } });
            World w = sim.World;
            Assert.Equal(m.Players, w.StartLocations.Length);
            int level = w.StartLocations[0].Level;
            for (int s = 0; s < m.Players; s++)
            {
                StartLocation st = w.StartLocations[s];
                Assert.Equal(level, st.Level);
                Vector2 centre = new((st.Cell % m.Size + 0.5f) * MapConstants.CellSize, (st.Cell / m.Size + 0.5f) * MapConstants.CellSize);
                var mines = new List<float>();
                float tree = float.MaxValue;
                for (int k = 0; k < w.Resources.Capacity; k++)
                {
                    if (!w.Resources.Alive[k]) continue;
                    ResourceDef def = w.Data.Resources[w.Resources.TypeId[k]];
                    int c = w.Resources.Cell[k];
                    float d = Vector2.Distance(centre, new Vector2((c % m.Size + def.FootprintWidth * 0.5f) * MapConstants.CellSize, (c / m.Size + def.FootprintHeight * 0.5f) * MapConstants.CellSize));
                    if (def.Resource == ResourceKind.Gold) mines.Add(d);
                    else tree = Math.Min(tree, d);
                }
                mines.Sort();
                _out.WriteLine($"{m.Key} start {s}: level {st.Level}; mines {string.Join(" / ", mines.Take(2).Select(d => d.ToString("F1")))} m; nearest tree {tree:F1} m");
                Assert.True(mines.Count(d => d <= MapGenParams.Default.StartMineDistance) >= TestSim.Data.Rules.StartMineCount, $"{m.Key} start {s}");
                Assert.True(tree <= MapGenParams.Default.StartForestDistance, $"{m.Key} start {s}: nearest tree {tree:F1} m");
                for (int o = 0; o < s; o++)
                {
                    int dx = st.Cell % m.Size - w.StartLocations[o].Cell % m.Size, dy = st.Cell / m.Size - w.StartLocations[o].Cell / m.Size;
                    Assert.True(4 * (dx * dx + dy * dy) >= m.Size * m.Size, $"{m.Key}: starts {o} and {s} are closer than half the map side");
                }
            }
        }
    }

    /// <summary>
    /// A world with <see cref="SimConfig.MapId"/> is the file's: the heightmap's levels are the file's rows, its starts the
    /// file's, each node of the file alive at its cell with its amount in file order, the placement counts the file's;
    /// nothing is drawn from the map stream (its state is a fresh stream's), and the terrain is shared, not rebuilt.
    /// </summary>
    [Fact]
    public void AWorldOnRaraku_IsBuiltFromTheFile_AndDrawsNothing()
    {
        MapDef m = RarakuDef;
        var config = new SimConfig(9, 2, 64, 64) { Data = TestSim.Data, MapId = Raraku };
        var w = new World(config);
        Assert.Same(m.Terrain, w.Heightmap);
        Assert.Equal(m.Levels.ToArray(), w.Heightmap.Levels.ToArray());
        Assert.Equal(m.Starts.ToArray(), w.StartLocations.ToArray().Select(s => s.Cell).ToArray());
        Assert.True(w.HasGeneratedMap, "a map file is rebuilt from its id: the recorder takes it");
        for (int i = 0; i < m.Nodes.Length; i++)
        {
            Assert.True(w.Resources.Alive[i], $"node {i}");
            Assert.Equal((m.Nodes[i].TypeId, m.Nodes[i].Cell, m.Nodes[i].Amount), (w.Resources.TypeId[i], w.Resources.Cell[i], w.Resources.Remaining[i]));
        }
        Assert.Equal(m.Nodes.Length, Enumerable.Range(0, w.Resources.Capacity).Count(k => w.Resources.Alive[k]));
        int mines = m.Nodes.Count(n => TestSim.Data.Resources[n.TypeId].Resource == ResourceKind.Gold);
        Assert.Equal(new ResourcePlacement(m.ForestCount, m.Nodes.Length - mines, mines), w.ResourcePlacement);
        var fresh = new Determinism.SimRng(9, (ulong)Determinism.RngStream.MapGen);
        Assert.Equal((fresh.State, fresh.Increment), (w.Rng(Determinism.RngStream.MapGen).State, w.Rng(Determinism.RngStream.MapGen).Increment));
        // A ramp is a ramp in the nav grid, and every level is reachable (the loader's connectivity rule).
        MapRampDef r = m.Ramps[0];
        Assert.True((w.NavGrid.FlagsAt(r.X, r.Y) & NavFlags.Ramp) != 0);
    }

    /// <summary>The same seed on Raraku and on the generated default map are different worlds; the same seed on Raraku twice the same (in one process).</summary>
    [Fact]
    public void Raraku_SameSeedTwice_SameHash_AndNotTheGeneratedMap()
    {
        Simulation a = RarakuMatch(4, false, out _), b = RarakuMatch(4, false, out _);
        PlayerSeat[] seats = { new(0, SeatController.Ai, AiDifficulty.Normal), new(1, SeatController.Ai, AiDifficulty.Normal) };
        Simulation g = AiScenes.Generated(seats, 4, false, out _);
        bool differs = false;
        for (int t = 0; t < 600; t++)
        {
            a.Tick();
            b.Tick();
            g.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"tick {a.TickNumber}");
            differs |= a.StateHash() != g.StateHash();
        }
        Assert.True(differs);
    }

    // ---------- determinism in two processes ----------

    private const string TwinChildVariable = "RTS_MAPFILE_TWIN_CHILD";
    private const string TwinLine = "MAPFILE-TWIN ";
    private const int TwinTicks = 3000;

    /// <summary>FNV-1a 64 over the per-tick state hashes of the Raraku AI match (seed 7, Normal v Hard) for <see cref="TwinTicks"/> ticks.</summary>
    private static ulong TwinDigest(out ulong last)
    {
        Simulation sim = RarakuMatch(7, false, out _, AiDifficulty.Normal, AiDifficulty.Hard);
        var h = new Determinism.StateHasher();
        last = 0;
        for (int t = 0; t < TwinTicks; t++)
        {
            sim.Tick();
            last = sim.StateHash();
            h.Add(last);
        }
        return h.Value;
    }

    /// <summary>
    /// Criterion 4: the Raraku match is deterministic across processes: this process and a fresh child <c>dotnet test</c> of
    /// this row (<see cref="TwinChildVariable"/> set) fold the same per-tick hashes over 3,000 ticks.
    /// </summary>
    [Fact]
    public void Raraku_TwoProcesses_SamePerTickHashes_Over3000Ticks()
    {
        ulong digest = TwinDigest(out ulong last);
        if (Environment.GetEnvironmentVariable(TwinChildVariable) == "1")
        {
            _out.WriteLine($"{TwinLine}{digest:X16} {last:X16}");
            return;
        }
        string child = RunChild(nameof(Raraku_TwoProcesses_SamePerTickHashes_Over3000Ticks), TwinChildVariable, TwinLine);
        _out.WriteLine($"this process {digest:X16} {last:X16}; the child {child}");
        Assert.Equal($"{digest:X16} {last:X16}", child);
    }

    /// <summary>Runs row <paramref name="row"/> of this class as a fresh child <c>dotnet test</c> with <paramref name="variable"/> set; the text after <paramref name="marker"/> on its line.</summary>
    private static string RunChild(string row, string variable, string marker) => RunChild(typeof(MapFileTests), row, variable, marker);

    /// <summary>
    /// As <see cref="RunChild(string, string, string)"/>, for row <paramref name="row"/> of <paramref name="testClass"/> (M6-S8:
    /// <c>AiDeterminismTests</c> uses it too); the child is killed after <paramref name="seconds"/> (M5-4d: a whole match needs longer).
    /// </summary>
    internal static string RunChild(Type testClass, string row, string variable, string marker, int seconds = 180)
    {
        string dll = typeof(MapFileTests).Assembly.Location;
        var info = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (string a in new[] { "test", dll, "--filter", $"FullyQualifiedName={testClass.FullName}.{row}", "--logger", "console;verbosity=detailed" })
            info.ArgumentList.Add(a);
        info.Environment[variable] = "1";
        using Process p = Process.Start(info)!;
        System.Threading.Tasks.Task<string> err = p.StandardError.ReadToEndAsync();
        System.Threading.Tasks.Task<string> output = p.StandardOutput.ReadToEndAsync();
        if (!p.WaitForExit(seconds * 1000))
        {
            try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } // it exited meanwhile
            Assert.Fail($"the child dotnet test did not exit within {seconds} s and was killed");
        }
        p.WaitForExit();
        string text = output.Result;
        int at = text.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(at >= 0, $"the child printed no digest (exit {p.ExitCode}): {text} {err.Result}");
        int from = at + marker.Length, to = text.IndexOfAny(new[] { '\r', '\n' }, from);
        return text[from..(to < 0 ? text.Length : to)].Trim();
    }

    // ---------- save (format 5) and replay (format 9) ----------

    /// <summary>
    /// Criterion 4: a Raraku AI match saved at ticks 137 and 1,000 is format 5 (the map id after the preset; M5-4d: format 7, 5 plus the HUNT section), loads (the
    /// config names the map), follows the unsaved run hash for hash for 1,000 ticks, the state outside the hash compared, and
    /// save -> load -> save is byte-identical.
    /// </summary>
    [Fact]
    public void Raraku_SavedAt137And1000_LoadsAsFormat5_AndFollowsTheUnsavedRun()
    {
        var scene = new SaveScene("raraku_ai", () => RarakuMatch(3, false, out _), (_, _) => { });
        SaveTestRun.Result r = SaveTestRun.RoundTrip(scene, new[] { 137, 1000 }, after: 1000);
        Assert.Equal(2 * 1000, r.TicksCompared);
        Simulation sim = RarakuMatch(3, false, out _);
        for (int t = 0; t < 20; t++) sim.Tick();
        byte[] bytes = SaveTestRun.Save(sim);
        Assert.Equal(Rts.Sim.Save.SaveFormat.CurrentVersion, BitConverter.ToInt32(bytes, Rts.Sim.Save.SaveFormat.MagicLength)); // M5-4d: 5 plus HUNT
        Simulation loaded = SaveTestRun.Load(TestSim.Data, bytes);
        Assert.Equal(Raraku, loaded.World.Config.MapId);
        Assert.Same(RarakuDef, loaded.World.Config.MapFile);
    }

    /// <summary>
    /// Criterion 4: a Raraku AI match recorded for 3,000 ticks is format 9, its header names the map on the line after
    /// <c>starting-resources</c>, the text reads back, and playback (no AI) matches all 30 checkpoints.
    /// </summary>
    [Fact]
    public void Raraku_Recorded3000Ticks_IsFormat9_AndPlaysBackToEveryCheckpoint()
    {
        Simulation sim = RarakuMatch(5, true, out ReplayRecorder? rec);
        for (int t = 0; t < 3000; t++) sim.Tick();
        Replay replay = rec!.ToReplay();
        Assert.Equal(9, replay.FormatVersion);
        Assert.Equal(Raraku, replay.MapId);
        byte[] bytes = ReplayFormat.Write(replay);
        string[] lines = System.Text.Encoding.ASCII.GetString(bytes).Split('\n');
        int preset = Array.FindIndex(lines, l => l.StartsWith("starting-resources ", StringComparison.Ordinal));
        Assert.True(preset > 0, "no starting-resources line");
        Assert.Equal("map raraku", lines[preset + 1]);
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(bytes, out Replay? back));
        Assert.Equal(Raraku, back!.MapId);
        ReplayResult played = ReplayPlayer.Run(back, TestSim.Data);
        _out.WriteLine($"{back.Commands.Length} commands; {back.Checkpoints.Length} checkpoints; {played}");
        Assert.True(played.Ok, played.ToString());
        Assert.Equal(30, back.Checkpoints.Length);
        Assert.Equal(3000, played.TicksRun);
    }

    // ---------- M6-S8: Pale Hills and Vathar Crossing ----------

    /// <summary>The source quarter <c>mirror-map</c> built map <paramref name="id"/> from (tools/Rts.Cli/maps/, never shipped).</summary>
    private static string SourceOf(string id) => Path.Combine(TestDataDir.RepoRoot(), "tools", "Rts.Cli", "maps", $"{id}.quad.json");

    /// <summary>M6-S8: the two new maps load with their fields: 4 players, 256 cells, the Producer's provisional text word for word, ramps, levels.</summary>
    [Theory]
    [InlineData(PaleHills, "Pale Hills", "Four camps in dense hill woods, joined by narrow paths between the trees. Timber is everywhere, gold is scarce beyond the camps, and every path is a place for an ambush.")]
    [InlineData(VatharCrossing, "Vathar Crossing", "A sunken river of slopes cuts the steppe in two, crossed only at its fords. Hold a ford and the far bank is yours; lose it and the enemy pours through.")]
    public void TheNewMaps_Load_WithTheirFields(string id, string name, string description)
    {
        int index = TestSim.Data.FindMap(id);
        Assert.True(index >= 0, $"no {id} in GameData.Maps");
        MapDef m = TestSim.Data.Maps[index];
        Assert.Equal(name, m.DisplayName);
        Assert.Equal(description, m.Description);
        Assert.Equal(4, m.Players);
        Assert.Equal(256, m.Size);
        Assert.Equal(256 * 256, m.Levels.Length);
        Assert.Equal(4, m.Starts.Length);
        Assert.NotEmpty(m.Ramps);
        Assert.True(m.Nodes.Length <= Entities.ResourceStore.DefaultCapacity, $"{m.Nodes.Length} nodes: a match at the default resource capacity refuses the map");
        int mines = m.Nodes.Count(n => TestSim.Data.Resources[n.TypeId].Resource == ResourceKind.Gold);
        _out.WriteLine($"{id}: {m.Ramps.Length} ramps, {mines} mines, {m.Nodes.Length - mines} trees in {m.ForestCount} forests, levels {string.Join("/", m.Levels.Distinct().OrderBy(l => l))}");
    }

    /// <summary>
    /// M6-S8 criterion 2: each new map is its source quarter mirrored across both axes: every cell's level is its mirror
    /// cell's in the source; the ramps, starts and nodes are the source's, each followed by its centre, east-west and
    /// north-south mirrors (a ramp's up flipped, a mine's anchor moved by its footprint), so the whole map is symmetric.
    /// </summary>
    [Theory]
    [InlineData(PaleHills)]
    [InlineData(VatharCrossing)]
    public void EachNewMap_IsTheMirrorOfItsSource(string id)
    {
        MapDef m = Def(id);
        int s = m.Size, half = s / 2;
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(SourceOf(id)));
        System.Text.Json.JsonElement src = doc.RootElement;
        string[] rows = src.GetProperty("terrain").EnumerateArray().Select(r => r.GetString()!).ToArray();
        Assert.Equal(half, rows.Length);
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
                Assert.True(m.Levels[y * s + x] == rows[Math.Min(y, s - 1 - y)][Math.Min(x, s - 1 - x)] - '0', $"{id}: cell ({x}, {y})");
        // Ramps: source ramp k is map ramp 4k, then its three mirrors.
        System.Text.Json.JsonElement[] ramps = src.GetProperty("ramps").EnumerateArray().ToArray();
        Assert.Equal(4 * ramps.Length, m.Ramps.Length);
        for (int k = 0; k < ramps.Length; k++)
        {
            MapRampDef r = m.Ramps[4 * k];
            Assert.Equal((ramps[k].GetProperty("x").GetInt32(), ramps[k].GetProperty("y").GetInt32(), ramps[k].GetProperty("width").GetInt32(), ramps[k].GetProperty("height").GetInt32()),
                (r.X, r.Y, r.Width, r.Height));
            Assert.Equal(m.Ramps[4 * k + 1], Mirror(r, s, true, true));
            Assert.Equal(m.Ramps[4 * k + 2], Mirror(r, s, true, false));
            Assert.Equal(m.Ramps[4 * k + 3], Mirror(r, s, false, true));
        }
        // Starts: the source's one start, then its centre (start 1), east-west and north-south mirrors.
        System.Text.Json.JsonElement st = src.GetProperty("starts")[0];
        int sx = st.GetProperty("x").GetInt32(), sy = st.GetProperty("y").GetInt32();
        Assert.Equal(new[] { sy * s + sx, (s - 1 - sy) * s + s - 1 - sx, sy * s + s - 1 - sx, (s - 1 - sy) * s + sx }, m.Starts.ToArray());
        // Nodes: the set is closed under each mirror (type, footprint-moved anchor, amount).
        var set = new HashSet<(int, int, int)>();
        foreach (MapNodeDef n in m.Nodes) set.Add((n.TypeId, n.Cell, n.Amount));
        foreach (MapNodeDef n in m.Nodes)
        {
            ResourceDef d = TestSim.Data.Resources[n.TypeId];
            int x = n.Cell % s, y = n.Cell / s, mx = s - x - d.FootprintWidth, my = s - y - d.FootprintHeight;
            Assert.Contains((n.TypeId, my * s + mx, n.Amount), set);
            Assert.Contains((n.TypeId, y * s + mx, n.Amount), set);
            Assert.Contains((n.TypeId, my * s + x, n.Amount), set);
        }
        int sourceMines = src.GetProperty("mines").GetArrayLength();
        Assert.Equal(4 * sourceMines, m.Nodes.Count(n => TestSim.Data.Resources[n.TypeId].Resource == ResourceKind.Gold));
    }

    private static MapRampDef Mirror(MapRampDef r, int s, bool eastWest, bool northSouth)
    {
        MapDirection up = r.Up switch
        {
            MapDirection.North when northSouth => MapDirection.South,
            MapDirection.South when northSouth => MapDirection.North,
            MapDirection.East when eastWest => MapDirection.West,
            MapDirection.West when eastWest => MapDirection.East,
            _ => r.Up,
        };
        return new MapRampDef(eastWest ? s - r.X - r.Width : r.X, northSouth ? s - r.Y - r.Height : r.Y, r.Width, r.Height, up);
    }

    /// <summary>M6-S8 criterion 2: <c>mirror-map</c> on the checked-in source writes the checked-in map file byte for byte (and says it loads).</summary>
    [Theory]
    [InlineData(PaleHills)]
    [InlineData(VatharCrossing)]
    public void MirrorMap_OnTheCheckedInSource_ReproducesTheShippedFileByteForByte(string id)
    {
        string dst = Path.Combine(Path.GetTempPath(), $"rts-mirror-{id}-{Environment.ProcessId}.json");
        try
        {
            var o = new StringWriter();
            var e = new StringWriter();
            int exit = Rts.Cli.CliRunner.Run(new[] { "mirror-map", SourceOf(id), dst, "--symmetry", "quad", "--data", TestDataDir.Shipped }, o, e);
            Assert.True(exit == 0, e.ToString());
            Assert.Equal(File.ReadAllBytes(Path.Combine(TestDataDir.Shipped, "maps", $"{id}.json")), File.ReadAllBytes(dst));
        }
        finally
        {
            File.Delete(dst);
        }
    }

    /// <summary>
    /// M6-S8 criterion 5: on Pale Hills starts 0 and 1 (a 2-player match's) are diagonally opposite, at least half the side
    /// apart on both axes; and the woods leave at least half of the passable cells open (no tree or mine on them).
    /// </summary>
    [Fact]
    public void PaleHills_StartsZeroAndOne_AreDiagonallyOpposite_AndHalfThePassableGroundIsOpen()
    {
        MapDef m = Def(PaleHills);
        int s = m.Size;
        int dx = Math.Abs(m.Starts[0] % s - m.Starts[1] % s), dy = Math.Abs(m.Starts[0] / s - m.Starts[1] / s);
        Assert.True(dx >= s / 2 && dy >= s / 2, $"starts 0 and 1 are {dx} x {dy} cells apart");
        var nav = new NavGrid(m.Terrain);
        var node = new bool[s * s];
        foreach (MapNodeDef n in m.Nodes)
        {
            ResourceDef d = TestSim.Data.Resources[n.TypeId];
            for (int y = 0; y < d.FootprintHeight; y++)
                for (int x = 0; x < d.FootprintWidth; x++)
                    node[n.Cell + y * s + x] = true;
        }
        int passable = 0, open = 0;
        for (int i = 0; i < s * s; i++)
        {
            if (!nav.IsPassable(i % s, i / s)) continue;
            passable++;
            if (!node[i]) open++;
        }
        _out.WriteLine($"starts 0 and 1: {dx} x {dy} cells apart; passable {passable}, open {open} ({100.0 * open / passable:F1} %)");
        Assert.True(2 * open >= passable, $"open {open} of {passable} passable cells");
    }

    /// <summary>The level-0 band's rows in column <paramref name="x"/> between rows <paramref name="from"/> and <paramref name="to"/> (exclusive): how many there are.</summary>
    private static int BandCells(MapDef m, int x, int from, int to)
    {
        int n = 0;
        for (int y = from + 1; y < to; y++)
            if (m.Levels[y * m.Size + x] == 0) n++;
        return n;
    }

    /// <summary>
    /// M6-S8 criterion 4: on Vathar Crossing a 2-player match's halls stand on starts 0 and 1, on level 1 on opposite banks:
    /// every column between them crosses the level-0 river band, and a walk over level-1 cells alone (no ramp, no band)
    /// from start 0 never reaches start 1, while the loader's flood (every passable cell reaches every other) passed.
    /// </summary>
    [Fact]
    public void VatharCrossing_TwoPlayers_StandOnStartsZeroAndOne_OnOppositeBanks()
    {
        MapDef m = Def(VatharCrossing);
        int s = m.Size;
        Simulation sim = MapMatch(VatharCrossing, 1, false, out _);
        sim.Tick();
        sim.Tick();
        World w = sim.World;
        for (int p = 0; p < 2; p++)
        {
            int cx = m.Starts[p] % s, cy = m.Starts[p] / s;
            Assert.True(TryHall(w, p, out int hall), $"player {p} has no hall");
            Assert.True(Math.Abs(hall % s - cx) <= 2 && Math.Abs(hall / s - cy) <= 2, $"player {p}'s hall at ({hall % s}, {hall / s}), start {p} at ({cx}, {cy})");
            Assert.Equal(1, m.Levels[m.Starts[p]]);
        }
        int y0 = m.Starts[0] / s, y1 = m.Starts[1] / s;
        Assert.True(y0 < s / 2 && y1 >= s / 2, "start 0 on the north bank, start 1 on the south");
        for (int x = 1; x < s - 1; x++) Assert.True(BandCells(m, x, y0 < y1 ? y0 : y1, y0 < y1 ? y1 : y0) > 0, $"column {x}: no river between the starts");
        // Level-1 ground alone, 4-neighbour, from start 0: the far bank is out of reach.
        var nav = new NavGrid(m.Terrain);
        var seen = new bool[s * s];
        var queue = new Queue<int>();
        queue.Enqueue(m.Starts[0]);
        seen[m.Starts[0]] = true;
        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            foreach (int j in new[] { i - 1, i + 1, i - s, i + s })
            {
                if (j < 0 || j >= s * s || seen[j] || m.Levels[j] != 1 || !nav.IsPassable(j % s, j / s)) continue;
                seen[j] = true;
                queue.Enqueue(j);
            }
        }
        Assert.False(seen[m.Starts[1]], "start 1 is reachable from start 0 without crossing the river");
        Assert.True(seen[m.Starts[2]], "start 2 (start 0's own bank) is not reachable over the bank");
    }

    private static bool TryHall(World w, int owner, out int cell)
    {
        for (int i = 0; i < w.Buildings.Capacity; i++)
        {
            if (!w.Buildings.Alive[i] || w.Buildings.Owner[i] != owner || w.Data.Buildings[w.Buildings.TypeId[i]].Slot != BuildingSlot.TownHall) continue;
            BuildingDef d = w.Data.Buildings[w.Buildings.TypeId[i]];
            cell = w.Buildings.Cell[i] + d.FootprintHeight / 2 * w.NavGrid.Width + d.FootprintWidth / 2;
            return true;
        }
        cell = -1;
        return false;
    }

    /// <summary>
    /// M6-S8 criterion 4: a unit of player 0 ordered from start 0 to start 1 on Vathar Crossing (no AI) walks through a ford:
    /// it arrives, every step from the bank (level 1) into the river band (level 0) lands on a ramp cell, and it crosses ramp
    /// cells down and up; the band cells and ramp cells on its path are counted and printed.
    /// </summary>
    [Fact]
    public void VatharCrossing_AUnitOrderedAcross_WalksThroughAFord()
    {
        MapDef m = Def(VatharCrossing);
        int s = m.Size;
        var sim = new Simulation(new SimConfig(3, 2, 64, 64) { Data = TestSim.Data, MapId = VatharCrossing, Map = MapGenParams.Default with { Width = s, Height = s } });
        World w = sim.World;
        NavGrid g = w.NavGrid;
        int type = StartBase.UnitOfSlot(TestSim.Data, w.FactionOf(0), UnitSlot.Line);
        sim.Enqueue(Command.SpawnUnit(0, type, g.CellCenter(m.Starts[0] % s, m.Starts[0] / s)));
        sim.Tick();
        sim.Tick();
        int slot = Enumerable.Range(0, w.Units.Capacity).First(i => w.Units.Alive[i] && w.Units.Owner[i] == 0);
        var unit = new EntityHandle(slot, w.Units.Generation[slot]);
        Vector2 goal = g.CellCenter(m.Starts[1] % s, m.Starts[1] / s);
        sim.Enqueue(Command.Move(0, unit, goal));
        int last = -1, band = 0, ramps = 0, entries = 0;
        bool arrived = false;
        for (int t = 0; t < 6000 && !arrived; t++)
        {
            sim.Tick();
            Assert.True(g.WorldToCell(w.Units.Position[slot], out int x, out int y));
            int cell = y * s + x;
            arrived = Vector2.Distance(w.Units.Position[slot], goal) < 6f;
            if (cell == last) continue;
            bool isRamp = (g.FlagsAt(x, y) & NavFlags.Ramp) != 0;
            if (m.Levels[cell] == 0) band++;
            if (isRamp) ramps++;
            if (last >= 0 && m.Levels[last] == 1 && m.Levels[cell] == 0)
            {
                entries++;
                Assert.True(isRamp, $"the unit stepped from the bank at ({last % s}, {last / s}) into the river at ({x}, {y}), not onto a ford");
            }
            last = cell;
        }
        _out.WriteLine($"arrived {arrived} at tick {sim.TickNumber}; band cells on the path {band} (ramp cells {ramps}); entered the band {entries} time(s)");
        Assert.True(arrived, "the unit did not reach start 1");
        Assert.True(entries >= 1 && ramps >= 6, $"no ford crossed ({ramps} ramp cells)");
    }

    /// <summary>M6-S8 criterion 3: the same seed on each new map twice is the same match (600 ticks, hash per tick, one process).</summary>
    [Theory]
    [InlineData(PaleHills)]
    [InlineData(VatharCrossing)]
    public void ANewMap_SameSeedTwice_SameHash(string id)
    {
        Simulation a = MapMatch(id, 4, false, out _), b = MapMatch(id, 4, false, out _);
        for (int t = 0; t < 600; t++)
        {
            a.Tick();
            b.Tick();
            Assert.True(a.StateHash() == b.StateHash(), $"{id}: tick {a.TickNumber}");
        }
    }

    /// <summary>M6-S8 criterion 3: an AI match on each new map saved at tick 1,000 is format 5 naming the map (M5-4d: format 7, 5 plus the HUNT section), loads, and follows the unsaved run hash for hash for 1,000 ticks.</summary>
    [Theory]
    [InlineData(PaleHills)]
    [InlineData(VatharCrossing)]
    public void ANewMap_SavedAt1000_LoadsAsFormat5_AndFollowsTheUnsavedRun(string id)
    {
        var scene = new SaveScene($"{id}_ai", () => MapMatch(id, 3, false, out _), (_, _) => { });
        SaveTestRun.Result r = SaveTestRun.RoundTrip(scene, new[] { 1000 }, after: 1000);
        Assert.Equal(1000, r.TicksCompared);
        Simulation sim = MapMatch(id, 3, false, out _);
        for (int t = 0; t < 20; t++) sim.Tick();
        byte[] bytes = SaveTestRun.Save(sim);
        Assert.Equal(Rts.Sim.Save.SaveFormat.CurrentVersion, BitConverter.ToInt32(bytes, Rts.Sim.Save.SaveFormat.MagicLength)); // M5-4d: 5 plus HUNT
        Assert.Equal(id, SaveTestRun.Load(TestSim.Data, bytes).World.Config.MapId);
    }

    /// <summary>M6-S8 criterion 3: an AI match on each new map recorded for 3,000 ticks is format 9 with its <c>map</c> line and plays back to all 30 checkpoints.</summary>
    [Theory]
    [InlineData(PaleHills)]
    [InlineData(VatharCrossing)]
    public void ANewMap_Recorded3000Ticks_IsFormat9_AndPlaysBackToEveryCheckpoint(string id)
    {
        Simulation sim = MapMatch(id, 5, true, out ReplayRecorder? rec);
        for (int t = 0; t < 3000; t++) sim.Tick();
        byte[] bytes = ReplayFormat.Write(rec!.ToReplay());
        Assert.Contains($"\nmap {id}\n", System.Text.Encoding.ASCII.GetString(bytes));
        Assert.Equal(ReplayError.None, ReplayFormat.TryRead(bytes, out Replay? back));
        Assert.Equal(9, back!.FormatVersion);
        Assert.Equal(id, back.MapId);
        ReplayResult played = ReplayPlayer.Run(back, TestSim.Data);
        _out.WriteLine($"{id}: {back.Commands.Length} commands; {back.Checkpoints.Length} checkpoints; {played}");
        Assert.True(played.Ok, played.ToString());
        Assert.Equal(30, back.Checkpoints.Length);
    }

    // ---------- the AI on the shipped map ----------

    /// <summary>
    /// Scope 4: two Normal AI seats (Malazan v Whirlwind) play a match on Raraku to the end (one side defeated), or to 60
    /// minutes; the result, the match length and each side's gathering, waves and peak army are printed. The match must end,
    /// and both sides must gather wood (the map's few forests are within the start contract's 24 m).
    /// </summary>
    [Fact]
    [Trait("Category", "Stress")]
    public void NormalVersusNormal_OnRaraku_PlaysToTheEnd() => PlayToTheEnd(RarakuMatch(1, false, out _), 72_000); // 60 minutes

    /// <summary>
    /// M6-S8: two Normal AI seats (seed 1, Malazan v Whirlwind, starts 0 and 1) play a match on Pale Hills or Vathar Crossing to
    /// the end within 36,000 ticks (30 minutes); the result, the length and each side's gathering are printed (docs/03
    /// "Implementation (M6-S8)" quotes them), and both sides gather wood.
    /// </summary>
    [Theory]
    [Trait("Category", "Stress")]
    [InlineData(PaleHills)]
    [InlineData(VatharCrossing)]
    public void NormalVersusNormal_OnANewMap_EndsWithin36000Ticks(string id) => PlayToTheEnd(MapMatch(id, 1, false, out _), 36_000);

    /// <summary>Plays <paramref name="sim"/> (two AI seats) until the match is over or <paramref name="limit"/> ticks; prints and checks the result.</summary>
    private void PlayToTheEnd(Simulation sim, int limit)
    {
        World w = sim.World;
        var peak = new int[2];
        var waves = new int[2];
        var waving = new bool[2];
        while (!sim.MatchOver && sim.TickNumber < limit)
        {
            sim.Tick();
            for (int p = 0; p < 2; p++)
            {
                Ai.AiPlayer ai = sim.AiOf(p)!;
                peak[p] = Math.Max(peak[p], ai.ArmyCount);
                if (ai.Board.WaveActive && !waving[p]) waves[p]++;
                waving[p] = ai.Board.WaveActive;
            }
            if (sim.TickNumber % 6000 == 0)
                _out.WriteLine($"{Clock(sim.TickNumber)}: halls {Halls(w, 0)} / {Halls(w, 1)}; gathered gold {w.GatheredGold[0]} / {w.GatheredGold[1]}, wood {w.GatheredWood[0]} / {w.GatheredWood[1]}; army {sim.AiOf(0)!.ArmyCount} / {sim.AiOf(1)!.ArmyCount}");
        }
        static string Clock(int ticks) => $"{ticks / 1200}:{ticks / 20 % 60:D2}";
        for (int p = 0; p < 2; p++)
            _out.WriteLine($"seat {p} ({w.Data.Factions[w.FactionOf(p)].Key}): defeated {w.IsDefeated(p)}; gathered gold {w.GatheredGold[p]} wood {w.GatheredWood[p]}; waves {waves[p]}; peak army {peak[p]}; kills {w.Kills[p]} losses {w.Losses[p]}");
        _out.WriteLine($"match over {sim.MatchOver} at {Clock(sim.TickNumber)} ({sim.TickNumber} ticks)");
        Assert.True(sim.MatchOver, $"no result in {Clock(limit)}");
        Assert.True(w.IsDefeated(0) ^ w.IsDefeated(1), "one side defeated");
        Assert.True(w.GatheredWood[0] > 0 && w.GatheredWood[1] > 0, "a side gathered no wood");
    }

    /// <summary>How many Town Halls (finished or not) <paramref name="owner"/> has.</summary>
    private static int Halls(World w, int owner)
    {
        int n = 0;
        for (int i = 0; i < w.Buildings.Capacity; i++)
            if (w.Buildings.Alive[i] && w.Buildings.Owner[i] == owner && w.Data.Buildings[w.Buildings.TypeId[i]].Slot == BuildingSlot.TownHall) n++;
        return n;
    }
}
