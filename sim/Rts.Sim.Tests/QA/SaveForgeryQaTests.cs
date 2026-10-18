using System.Buffers.Binary;
using Rts.Sim.Data;
using Rts.Sim.Save;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA (M6-S2): forged saves (edited bytes, checksum recomputed, so the body parser is what answers). The load must never
/// throw: it loads or refuses with a code, the sim null exactly when refused. Interesting int32 values at every offset of
/// the small save; random multi-byte edits of a full menu-match save. A forged save that loads is ticked a few times and
/// any exception counted and printed (the format only promises the load, so the count is a report, not a gate).
/// </summary>
public class SaveForgeryQaTests
{
    private readonly ITestOutputHelper _out;

    public SaveForgeryQaTests(ITestOutputHelper output) => _out = output;

    private static readonly int[] Interesting = { 0, 1, -1, 2, 7, 8, 255, 256, 65535, int.MaxValue, int.MinValue, 1_000_000, 1_000_001 };

    [Fact]
    public void TheSmallSave_AnInterestingInt32AtEveryOffset_NeverThrowsOnLoad()
    {
        byte[] good = SaveBytes.Small();
        GameData data = TestSim.Data;
        var seen = new Dictionary<SaveError, int>();
        int loaded = 0, tickThrows = 0;
        var throwSamples = new List<string>();
        for (int i = SaveFormat.FixedPrefix; i + 4 <= good.Length - SaveFormat.ChecksumLength; i++)
        {
            foreach (int v in Interesting)
            {
                byte[] bad = (byte[])good.Clone();
                if (BinaryPrimitives.ReadInt32LittleEndian(bad.AsSpan(i)) == v) continue;
                BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(i), v);
                bad = SaveBytes.Rechecksum(bad);
                SaveError e = LoadNoThrow(data, bad, out Simulation? sim, $"offset {i} value {v}");
                seen[e] = seen.GetValueOrDefault(e) + 1;
                if (sim == null) continue;
                loaded++;
                if (!TicksNoThrow(sim, 3, out string? why))
                {
                    tickThrows++;
                    if (throwSamples.Count < 15) throwSamples.Add($"offset {i} value {v}: {why}");
                }
            }
        }
        _out.WriteLine(string.Join(", ", seen.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}")));
        _out.WriteLine($"loaded {loaded}, of which {tickThrows} threw within 3 ticks");
        foreach (string s in throwSamples) _out.WriteLine(s);
    }

    [Fact]
    public void AMenuMatchSave_RandomMultiByteEdits_NeverThrowOnLoad()
    {
        SaveScene scene = SaveScenes.Menu(1);
        Simulation sim = scene.Build();
        var commands = new List<Rts.Sim.Commands.Command>();
        while (sim.TickNumber < 400)
        {
            commands.Clear();
            scene.Script(sim, commands);
            foreach (var c in commands) sim.Enqueue(c);
            sim.Tick();
        }
        byte[] good = SaveTestRun.Save(sim);
        GameData data = sim.World.Data;
        var rng = new Determinism.SimRng(99, 3);
        var seen = new Dictionary<SaveError, int>();
        int loaded = 0, tickThrows = 0;
        var throwSamples = new List<string>();
        int body = good.Length - SaveFormat.ChecksumLength;
        for (int trial = 0; trial < 250; trial++)
        {
            byte[] bad = (byte[])good.Clone();
            int edits = 1 + rng.NextInt(0, 6);
            var where = new List<int>();
            for (int k = 0; k < edits; k++)
            {
                int at = rng.NextInt(SaveFormat.FixedPrefix, body - 4);
                where.Add(at);
                if (rng.NextInt(0, 2) == 0) bad[at] = (byte)rng.NextInt(0, 256);
                else BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(at), Interesting[rng.NextInt(0, Interesting.Length)]);
            }
            bad = SaveBytes.Rechecksum(bad);
            string label = $"trial {trial} at {string.Join("/", where)}";
            SaveError e = LoadNoThrow(data, bad, out Simulation? forged, label);
            seen[e] = seen.GetValueOrDefault(e) + 1;
            if (forged == null) continue;
            loaded++;
            if (!TicksNoThrow(forged, 5, out string? why))
            {
                tickThrows++;
                if (throwSamples.Count < 15) throwSamples.Add($"{label}: {why}");
            }
        }
        _out.WriteLine($"save {good.Length:N0} bytes; " + string.Join(", ", seen.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}")));
        _out.WriteLine($"loaded {loaded}, of which {tickThrows} threw within 5 ticks");
        foreach (string s in throwSamples) _out.WriteLine(s);
    }

    /// <summary>
    /// A forged header asking for the largest capacities the format allows (1,000,000 each) over the small save's body:
    /// the load builds the whole world from the header before the body's first array length refuses it. It must refuse
    /// (Malformed), not throw, and should not take long; the time and the memory it allocated are printed.
    /// </summary>
    [Fact]
    public void AForgedHeader_WithEveryCapacityAtTheFormatMaximum_IsRefused_WithoutThrowing()
    {
        byte[] bad = SaveBytes.Small();
        SaveBytes.Offsets at = SaveBytes.HeaderOffsets(bad);
        int caps = at.Seat0Controller + 2; // controller byte, difficulty byte, then unit, command, resource, building, projectile, zone
        for (int k = 0; k < 6; k++) BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(caps + 4 * k), SaveFormat.MaxCapacity);
        bad = SaveBytes.Rechecksum(bad);
        long before = GC.GetTotalAllocatedBytes(precise: false);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        SaveError e = LoadNoThrow(TestSim.Data, bad, out Simulation? sim, "max capacities");
        sw.Stop();
        long allocated = GC.GetTotalAllocatedBytes(precise: false) - before;
        _out.WriteLine($"max capacities: {e} in {sw.Elapsed.TotalMilliseconds:F0} ms, {allocated / (1024 * 1024):N0} MB allocated");
        Assert.Null(sim);
        Assert.NotEqual(SaveError.None, e);
    }

    private static SaveError LoadNoThrow(GameData data, byte[] bytes, out Simulation? sim, string label)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            bool ok = Simulation.TryLoad(data, stream, out sim, out SaveError error);
            Assert.True(ok == (sim != null) && ok == (error == SaveError.None), $"{label}: ok {ok}, sim {(sim != null)}, {error}");
            return error;
        }
        catch (Xunit.Sdk.XunitException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Assert.Fail($"{label}: TryLoad threw {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            sim = null;
            return SaveError.None;
        }
    }

    private static bool TicksNoThrow(Simulation sim, int ticks, out string? why)
    {
        try
        {
            for (int t = 0; t < ticks; t++)
            {
                sim.Tick();
                sim.StateHash();
            }
            why = null;
            return true;
        }
        catch (Exception ex)
        {
            string frame = ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim() ?? "";
            why = $"{ex.GetType().Name} {frame}";
            return false;
        }
    }
}
