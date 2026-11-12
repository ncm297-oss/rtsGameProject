using Rts.Sim.Data;
using Rts.Sim.Map;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA for M7-5 (BUG-1222 ruling): the shipped-data memory row allows
/// <see cref="FieldBuildFairnessQaTests.Serial.BytesPerExtraRadiusClass1024"/> per collision-radius class beyond three. These
/// rows tie that constant to a measurement (one extra class on an otherwise two-faction world) so it can't go stale when
/// <c>World</c> sizes another table by the class count, and keep the two-faction row tight enough to catch a 100 KB leak.
/// </summary>
[Collection(SerialCollection.Name)]
public class RadiusClassAllowanceQaTests(ITestOutputHelper output)
{
    private const long ProbeSlack = 8_192; // run-to-run noise of a World construction probe (median of three, warm)

    private static readonly Lazy<GameData> s_oneMoreClass = new(() =>
    {
        // Heavy Infantry shares 0.4 m with the Laborer, so a new radius on it adds a class and removes none.
        using TestDataDir dir = TestDataDir.CopyOfShippedTwoFactions();
        dir.SetUnitField("malazan", "malazan_heavy_infantry", "radius", "0.75");
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.True(r.Ok, string.Join("; ", r.Errors));
        return r.Data!;
    });

    private static long Measure(GameData data, out int classes)
    {
        var config = new SimConfig(4950, 2, 4096, 64) { Data = data, Map = MapGenParams.Default with { Width = 1024, Height = 1024 } };
        World? built = null;
        Action create = () => built = new World(config);
        long bytes = AllocationProbe.Measure(create);
        classes = built!.RadiusClassCount;
        return bytes;
    }

    [Fact]
    public void OneExtraRadiusClass_Costs_TheNamedAllowance_OnA1024World()
    {
        GameData two = TestSim.TwoFactionData, wide = s_oneMoreClass.Value;
        Measure(two, out _); // warm: the first construction pays one-time runtime charges
        Measure(wide, out _);
        // The probe moves a few KB either way between runs under load (QA saw +128,720 / +131,072 / +138,712 single
        // measurements): the median of three differences.
        var diffs = new long[3];
        int ca = 0, cb = 0;
        for (int k = 0; k < diffs.Length; k++)
        {
            long a = Measure(two, out ca), b = Measure(wide, out cb);
            diffs[k] = b - a;
            output.WriteLine($"two-faction {a} bytes ({ca} classes), one more class {b} bytes ({cb} classes): +{b - a}");
        }
        Array.Sort(diffs);
        output.WriteLine($"median +{diffs[1]}; allowance {FieldBuildFairnessQaTests.Serial.BytesPerExtraRadiusClass1024}");
        Assert.Equal(ca + 1, cb);
        Assert.InRange(diffs[1], FieldBuildFairnessQaTests.Serial.BytesPerExtraRadiusClass1024 - ProbeSlack,
            FieldBuildFairnessQaTests.Serial.BytesPerExtraRadiusClass1024 + ProbeSlack);
    }

    [Fact]
    public void TheTwoFactionBound_LeavesLessThan100KB_OverTheMeasuredWorld()
    {
        Measure(TestSim.TwoFactionData, out _);
        long bytes = Measure(TestSim.TwoFactionData, out _);
        long margin = FieldBuildFairnessQaTests.Serial.TwoFactionBound1024 - bytes;
        output.WriteLine($"two-faction 1024 world {bytes} bytes; bound {FieldBuildFairnessQaTests.Serial.TwoFactionBound1024}; margin {margin}");
        Assert.InRange(margin, 1, 100_000 - 1);
    }
}
