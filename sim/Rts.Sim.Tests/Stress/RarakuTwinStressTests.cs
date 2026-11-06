using System.Diagnostics;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA, M6-S7 (session 2026-11-05-1716): the shipped hand-made map Raraku, two Normal AI seats (Malazan v Whirlwind), seed 11,
/// over 12,000 ticks (10 minutes, four times the developer's 3,000-tick twin): the fold of every tick's state hash is the
/// same in a fresh child process, and a save at tick 6,000 (format 5) loaded and run on to 12,000 ends on the same hash.
/// </summary>
[Collection(SerialCollection.Name)]
public class RarakuTwinStressTests
{
    private const string ChildVariable = "RTS_QA_RARAKU_TWIN_CHILD";
    private const string Marker = "QA-RARAKU-TWIN ";
    private const int Ticks = 12_000;

    private readonly ITestOutputHelper _out;

    public RarakuTwinStressTests(ITestOutputHelper output) => _out = output;

    private static Simulation Match() => MapFileTests.RarakuMatch(11, false, out _);

    private static ulong Fold(Simulation sim, int to, ref Determinism.StateHasher h)
    {
        ulong last = 0;
        while (sim.TickNumber < to)
        {
            sim.Tick();
            last = sim.StateHash();
            h.Add(last);
        }
        return last;
    }

    [Fact]
    [Trait("Category", "Stress")]
    public void Raraku_NormalVersusNormal_12000Ticks_SameInTwoProcesses_AndAcrossASaveAt6000()
    {
        Simulation sim = Match();
        var h = new Determinism.StateHasher();
        Fold(sim, 6000, ref h);
        byte[] save = SaveTestRun.Save(sim);
        ulong last = Fold(sim, Ticks, ref h);
        string mine = $"{h.Value:X16} {last:X16}";
        if (Environment.GetEnvironmentVariable(ChildVariable) == "1")
        {
            _out.WriteLine(Marker + mine);
            return;
        }
        Assert.Equal(5, BitConverter.ToInt32(save, Rts.Sim.Save.SaveFormat.MagicLength));
        Simulation loaded = SaveTestRun.Load(TestSim.Data, save);
        var ignored = new Determinism.StateHasher();
        Assert.Equal(last, Fold(loaded, Ticks, ref ignored));
        string child = RunChild();
        _out.WriteLine($"this process {mine}; child {child}; match over {sim.MatchOver}; gold {sim.World.GatheredGold[0]} / {sim.World.GatheredGold[1]}, wood {sim.World.GatheredWood[0]} / {sim.World.GatheredWood[1]}");
        Assert.Equal(mine, child);
    }

    private static string RunChild()
    {
        string dll = typeof(RarakuTwinStressTests).Assembly.Location;
        var info = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (string a in new[] { "test", dll, "--filter", $"FullyQualifiedName={typeof(RarakuTwinStressTests).FullName}.{nameof(Raraku_NormalVersusNormal_12000Ticks_SameInTwoProcesses_AndAcrossASaveAt6000)}", "--logger", "console;verbosity=detailed" })
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
}
