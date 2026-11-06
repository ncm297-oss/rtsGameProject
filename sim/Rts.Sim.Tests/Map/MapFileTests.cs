using System.Diagnostics;
using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Replays;
using Rts.Sim.Tests.Save;
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
    private static string RunChild(string row, string variable, string marker)
    {
        string dll = typeof(MapFileTests).Assembly.Location;
        var info = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (string a in new[] { "test", dll, "--filter", $"FullyQualifiedName={typeof(MapFileTests).FullName}.{row}", "--logger", "console;verbosity=detailed" })
            info.ArgumentList.Add(a);
        info.Environment[variable] = "1";
        using Process p = Process.Start(info)!;
        System.Threading.Tasks.Task<string> err = p.StandardError.ReadToEndAsync();
        System.Threading.Tasks.Task<string> output = p.StandardOutput.ReadToEndAsync();
        if (!p.WaitForExit(180_000))
        {
            try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } // it exited meanwhile
            Assert.Fail("the child dotnet test did not exit within 180 s and was killed");
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
    /// Criterion 4: a Raraku AI match saved at ticks 137 and 1,000 is format 5 (the map id after the preset), loads (the
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
        Assert.Equal(5, BitConverter.ToInt32(bytes, Rts.Sim.Save.SaveFormat.MagicLength));
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

    // ---------- the AI on the shipped map ----------

    /// <summary>
    /// Scope 4: two Normal AI seats (Malazan v Whirlwind) play a match on Raraku to the end (one side defeated), or to 60
    /// minutes; the result, the match length and each side's gathering, waves and peak army are printed. The match must end,
    /// and both sides must gather wood (the map's few forests are within the start contract's 24 m).
    /// </summary>
    [Fact]
    [Trait("Category", "Stress")]
    public void NormalVersusNormal_OnRaraku_PlaysToTheEnd()
    {
        Simulation sim = RarakuMatch(1, false, out _);
        World w = sim.World;
        const int limit = 72_000; // 60 minutes
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
        }
        string Clock(int ticks) => $"{ticks / 1200}:{ticks / 20 % 60:D2}";
        for (int p = 0; p < 2; p++)
            _out.WriteLine($"seat {p} ({w.Data.Factions[w.FactionOf(p)].Key}): defeated {w.IsDefeated(p)}; gathered gold {w.GatheredGold[p]} wood {w.GatheredWood[p]}; waves {waves[p]}; peak army {peak[p]}; kills {w.Kills[p]} losses {w.Losses[p]}");
        _out.WriteLine($"match over {sim.MatchOver} at {Clock(sim.TickNumber)} ({sim.TickNumber} ticks)");
        Assert.True(sim.MatchOver, $"no result in {Clock(limit)}");
        Assert.True(w.IsDefeated(0) ^ w.IsDefeated(1), "one side defeated");
        Assert.True(w.GatheredWood[0] > 0 && w.GatheredWood[1] > 0, "a side gathered no wood");
    }
}
