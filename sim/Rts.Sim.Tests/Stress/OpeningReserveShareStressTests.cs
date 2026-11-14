using System.Diagnostics;
using Rts.Sim.Ai;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Tests.QA;
using Rts.Sim.Tests.Save;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA (M7-6, session 2026-11-13-1118): per-opening reserve shares (Request 52) over whole matches. Seeded share layouts
/// (every opening its own value from 0, 0.25, 0.5, 0.75, 1 or none) on two-seat AI matches: invariants every tick, twins
/// and a mid-match save hash-equal. One layout run in three processes (this one and two children) to the same per-tick
/// fold. And what share 0 ("reserve from the start") does to the first wave, printed beside the field absent.
/// </summary>
[Collection(SerialCollection.Name)]
public class OpeningReserveShareStressTests
{
    private const string ChildVariable = "RTS_QA_M76_TWIN_CHILD";
    private const string Marker = "QA-M76-TWIN ";

    private readonly ITestOutputHelper _out;

    public OpeningReserveShareStressTests(ITestOutputHelper output) => _out = output;

    private static readonly double?[] Values = { null, 0, 0.25, 0.5, 0.75, 1 };

    /// <summary>A layout from <paramref name="seed"/>: opening i of faction f gets one of <see cref="Values"/>.</summary>
    private static GameData Layout(ulong seed) => OpeningReserveShareQaTests.Shares((f, i) =>
        Values[(int)((seed * 7 + (ulong)f.Length * 5 + (ulong)i * 3) % (ulong)Values.Length)]);

    private static void Invariants(Simulation sim, string at)
    {
        World w = sim.World;
        UnitStore u = w.Units;
        float max = Math.Max(w.NavGrid.Width, w.NavGrid.Height) * Rts.Sim.Map.MapConstants.CellSize;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            System.Numerics.Vector2 v = u.Position[i];
            Assert.True(float.IsFinite(v.X) && float.IsFinite(v.Y) && v.X >= 0 && v.Y >= 0 && v.X <= max && v.Y <= max, $"{at}: unit {i} at {v}");
            Assert.True(u.Hp[i] >= 1 && u.Hp[i] <= Math.Max(w.Data.Units[u.TypeId[i]].Hp * 10, 1_000_000), $"{at}: unit {i} hp {u.Hp[i]}");
        }
        for (int p = 0; p < w.Gold.Length; p++)
        {
            Assert.True(w.Gold[p] >= 0 && w.Wood[p] >= 0, $"{at}: player {p} resources {w.Gold[p]} / {w.Wood[p]}");
            Assert.True(w.HalfPop[p] >= 0, $"{at}: player {p} half-pop {w.HalfPop[p]}");
        }
    }

    private static Simulation Match(GameData d, ulong seed) =>
        AiScenes.Generated(new[] { new PlayerSeat(0, SeatController.Ai, AiDifficulty.Hard), new PlayerSeat(1, SeatController.Ai, (AiDifficulty)(seed % 3)) }, seed, false, out _, data: d, raiders: 2);

    /// <summary>
    /// Six seeds, each its own share layout, 6,000 ticks: invariants every tick; a twin from the same seed ends on the same
    /// per-tick fold; a save at 3,000 loaded and run on ends on the same hash. Printed per seed: the openings and shares, the
    /// thinks that named a reserve, waves, ages.
    /// </summary>
    [Theory]
    [Trait("Category", "Stress")]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    [InlineData(4UL)]
    [InlineData(5UL)]
    [InlineData(6UL)]
    public void SeededShareLayouts_InvariantsTwinsAndASave(ulong seed)
    {
        GameData d = Layout(seed);
        Simulation a = Match(d, seed), b = Match(d, seed);
        var reserved = new int[2];
        for (int p = 0; p < 2; p++)
        {
            int q = p;
            a.AiOf(p)!.ThoughtForTests = x => { if (x.ReserveStep >= 0) reserved[q]++; };
        }
        var ha = new Determinism.StateHasher();
        var hb = new Determinism.StateHasher();
        byte[]? save = null;
        for (int t = 0; t < 6000; t++)
        {
            if (t == 3000) save = SaveTestRun.Save(a);
            a.Tick();
            b.Tick();
            Invariants(a, $"seed {seed} tick {a.TickNumber}");
            ha.Add(a.StateHash());
            hb.Add(b.StateHash());
        }
        Assert.Equal(ha.Value, hb.Value);
        Simulation loaded = SaveTestRun.Load(d, save!);
        while (loaded.TickNumber < a.TickNumber) loaded.Tick();
        Assert.Equal(a.StateHash(), loaded.StateHash());
        string Seat(int p) => $"{a.AiOf(p)!.Opening.Id} share {a.AiOf(p)!.Opening.ReserveArmyShare?.ToString() ?? "-"} (x{a.AiOf(p)!.Knobs.WaveSizeMultiplier}) reserved {reserved[p]} waves {a.AiOf(p)!.Board.WavesStarted} age {a.World.Age(p)}";
        _out.WriteLine($"seed {seed}: {Seat(0)} | {Seat(1)}; over {a.MatchOver}");
    }

    // ---------- three processes ----------

    private static readonly Lazy<GameData> s_twinData = new(() => OpeningReserveShareQaTests.Shares((f, i) => f == "malazan" ? (i == 0 ? 0.5 : 0) : f == "whirlwind" ? 0.25 : null));

    /// <summary>
    /// One layout (Malazan's first opening 0.5, its others 0, every Whirlwind opening 0.25), Hard v Normal, seed 13, 8,000
    /// ticks: the fold of every tick's state hash and the end hash are the same in this process and two fresh children.
    /// </summary>
    [Fact]
    [Trait("Category", "Stress")]
    public void AShareLayout_SameInThreeProcesses()
    {
        Simulation sim = Match(s_twinData.Value, 13);
        int named = 0;
        for (int p = 0; p < 2; p++) sim.AiOf(p)!.ThoughtForTests = x => { if (x.ReserveStep >= 0) named++; };
        var h = new Determinism.StateHasher();
        while (sim.TickNumber < 8000)
        {
            sim.Tick();
            h.Add(sim.StateHash());
        }
        string mine = $"{h.Value:X16} {sim.StateHash():X16} {named}";
        if (Environment.GetEnvironmentVariable(ChildVariable) == "1")
        {
            _out.WriteLine(Marker + mine);
            return;
        }
        Assert.True(named > 0, "no reserve named: the layout does not exercise the share");
        string first = RunChild(), second = RunChild();
        _out.WriteLine($"this process {mine}; child 1 {first}; child 2 {second}; openings {sim.AiOf(0)!.Opening.Id} / {sim.AiOf(1)!.Opening.Id}");
        Assert.Equal(mine, first);
        Assert.Equal(mine, second);
    }

    private static string RunChild()
    {
        string dll = typeof(OpeningReserveShareStressTests).Assembly.Location;
        var info = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (string a in new[] { "test", dll, "--filter", $"FullyQualifiedName={typeof(OpeningReserveShareStressTests).FullName}.{nameof(AShareLayout_SameInThreeProcesses)}", "--logger", "console;verbosity=detailed" })
            info.ArgumentList.Add(a);
        info.Environment[ChildVariable] = "1";
        using Process p = Process.Start(info)!;
        System.Threading.Tasks.Task<string> err = p.StandardError.ReadToEndAsync();
        System.Threading.Tasks.Task<string> output = p.StandardOutput.ReadToEndAsync();
        if (!p.WaitForExit(600_000))
        {
            try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            Assert.Fail("the child dotnet test did not exit within 600 s");
        }
        p.WaitForExit();
        string text = output.Result;
        int at = text.IndexOf(Marker, StringComparison.Ordinal);
        Assert.True(at >= 0, $"the child printed no digest (exit {p.ExitCode}): {text} {err.Result}");
        int from = at + Marker.Length, to = text.IndexOfAny(new[] { '\r', '\n' }, from);
        return text[from..(to < 0 ? text.Length : to)].Trim();
    }

    // ---------- share 0: the first wave ----------

    private static readonly Lazy<GameData> s_zeroMalazan = new(() => OpeningReserveShareQaTests.Shares((f, _) => f == "malazan" ? 0 : null));

    /// <summary>
    /// What "reserve from the start" (every Malazan opening at 0) does to Malazan's first wave on four Normal v Normal seeds,
    /// beside the shipped data: the tick of its first wave and of its Age II, the thinks naming a reserve before the first
    /// wave. Informational (the value is the data track's); asserts only that the 0 rows name a reserve from the first thinks.
    /// </summary>
    [Theory]
    [Trait("Category", "Stress")]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    [InlineData(4UL)]
    public void ShareZero_TheFirstWave_BesideTheFieldAbsent(ulong seed)
    {
        (int Wave, int Age, int Before, int FirstReserve) Run(GameData d)
        {
            Simulation sim = AiScenes.Generated(AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Ai), seed, false, out _, data: d);
            AiPlayer ai = sim.AiOf(0)!;
            int before = 0, first = -1;
            ai.ThoughtForTests = x =>
            {
                if (x.ReserveStep >= 0 && first < 0) first = x.Now;
                if (x.ReserveStep >= 0 && x.Board.WavesStarted == 0) before++;
            };
            int wave = -1, age = -1;
            while (sim.TickNumber < 18_000 && (wave < 0 || age < 0) && !sim.MatchOver)
            {
                sim.Tick();
                if (wave < 0 && ai.Board.WavesStarted > 0) wave = sim.TickNumber;
                if (age < 0 && sim.World.Age(0) >= 2) age = sim.TickNumber;
            }
            return (wave, age, before, first);
        }
        var zero = Run(s_zeroMalazan.Value);
        var absent = Run(TestSim.Data);
        _out.WriteLine($"seed {seed}: share 0 first wave {zero.Wave}, Age II {zero.Age}, reserve thinks before the wave {zero.Before}, first reserve {zero.FirstReserve} | absent first wave {absent.Wave}, Age II {absent.Age}, before {absent.Before}, first reserve {absent.FirstReserve}");
        Assert.True(zero.FirstReserve >= 0 && zero.FirstReserve < 1200, $"share 0 named its first reserve at {zero.FirstReserve}");
    }
}
