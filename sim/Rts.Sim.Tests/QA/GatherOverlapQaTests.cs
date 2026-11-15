using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Movement;
using Rts.Sim.Save;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;
using static Rts.Sim.Tests.GatherMaps;
using static Rts.Sim.Tests.GatherOverlapScenes;
using static Rts.Sim.Tests.ResourceMaps;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA M6-S9 (session 2026-11-14-2156): the gather overlap's loader edges, a save taken while gather pairs overlap, the
/// knob at the cap, and the developer's two "partly met" claims re-measured (criterion 5's Stop under today's rules;
/// spawn-order dependence with the knob off).
/// </summary>
[Collection(SerialCollection.Name)]
public class GatherOverlapQaTests
{
    private readonly ITestOutputHelper _out;

    public GatherOverlapQaTests(ITestOutputHelper output) => _out = output;

    private static DataLoadResult Load(string raw)
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("common/rules.json", r => r["gatherOverlap"] = JsonNode.Parse(raw));
        return DataLoader.LoadAll(dir.Path);
    }

    // ------------------------------------------------------------------ loader edges

    /// <summary>Negative zero and spellings of zero load as off and hash as the field absent.</summary>
    [Theory]
    [InlineData("-0")]
    [InlineData("-0.0")]
    [InlineData("0e5")]
    [InlineData("0.000")]
    public void SpellingsOfZero_AreOff_AndHashAsAbsent(string raw)
    {
        DataLoadResult r = Load(raw);
        Assert.True(r.Ok, string.Join("; ", r.Errors));
        Assert.False(new World(new SimConfig(1, 1, 4, 16) { Data = r.Data! }).GatherOverlapOn);
        Assert.Equal(DataWith(null).ContentHash(), r.Data!.ContentHash());
    }

    /// <summary>The cap in other spellings loads; a hair over it does not.</summary>
    [Theory]
    [InlineData("0.75", true)]
    [InlineData("7.5e-1", true)]
    [InlineData("0.750000000000000000001", true)] // the same double as 0.75
    [InlineData("0.75000001", false)]
    [InlineData("0.7500000000001", false)]
    [InlineData("1e308", false)]
    [InlineData("-1e-300", false)]
    public void TheCap_AndJustOverIt(string raw, bool ok)
    {
        DataLoadResult r = Load(raw);
        Assert.Equal(ok, r.Ok);
        if (ok) Assert.Equal(0.75f, r.Data!.Rules.GatherOverlap);
        else
        {
            DataError e = Assert.Single(r.Errors);
            Assert.Equal("common/rules.json", e.File);
            Assert.Equal("gatherOverlap", e.Path);
        }
    }

    /// <summary>A key in the wrong case is no silent default: the strict reader refuses it.</summary>
    [Fact]
    public void AMisspelledKey_IsAnError_NotASilentOff()
    {
        using TestDataDir dir = TestDataDir.CopyOfShipped();
        dir.EditJson("common/rules.json", r => r["GatherOverlap"] = 0.5);
        DataLoadResult r = DataLoader.LoadAll(dir.Path);
        Assert.False(r.Ok);
        _out.WriteLine(string.Join("; ", r.Errors));
    }

    // ------------------------------------------------------------------ the knob at the cap runs

    [Theory]
    [InlineData(Workers.Gatherer)]
    [InlineData(Workers.Mixed)]
    public void AtTheCap_075mGatherers_NoPairPastTheScaledPackLimit_NoJump(Workers kind)
    {
        Simulation sim = NewSim(DataAt(DataLimits.MaxGatherOverlap), 24);
        Setup(sim, Node.Mine, kind, 16);
        var watch = new GatherOverlapQaWatch(sim);
        watch.Run(2400);
        _out.WriteLine(watch.Line);
        Assert.True(watch.Failure == null, watch.Failure);
        Assert.True(watch.MaxStepExcess <= 1e-3f, $"step past speed by {watch.MaxStepExcess:F4} ({watch.MaxStepAt})");
        Assert.True(watch.ClosestGatherShare > 0f, "two centres coincided");
    }

    // ------------------------------------------------------------------ a save taken while pairs overlap

    [Theory]
    [InlineData(0.5)]
    [InlineData(0.75)]
    public void ASaveTakenWhilePairsOverlap_LoadsAndContinuesEqual_AndRefusesOffData(double overlap)
    {
        GameData data = DataAt(overlap);
        Simulation sim = NewSim(data, 20);
        EntityHandle[] ws = Setup(sim, Node.Mine, Workers.Mixed, 12);
        UnitStore u = sim.World.Units;
        int overlapped = 0;
        // At the mine's steady state (after a minute), not on the walk out.
        for (int t = 0; t < 4000 && overlapped < 2; t++)
        {
            sim.Tick();
            overlapped = 0;
            if (t < 1200) continue;
            for (int a = 0; a < ws.Length; a++)
                for (int b = a + 1; b < ws.Length; b++)
                {
                    int i = ws[a].Index, j = ws[b].Index;
                    if (Vector2.Distance(u.Position[i], u.Position[j]) < 0.75f * (u.Radius[i] + u.Radius[j])) overlapped++;
                }
        }
        Assert.True(overlapped >= 2, "no two pairs overlapped at once");
        int savedAt = sim.TickNumber;
        byte[] bytes = SaveTestRun.Save(sim);
        Simulation loaded = SaveTestRun.Load(data, bytes);
        Assert.Equal(sim.StateHash(), loaded.StateHash());
        for (int t = 0; t < 2000; t++)
        {
            sim.Tick();
            loaded.Tick();
            Assert.True(sim.StateHash() == loaded.StateHash(), $"saved at tick {savedAt} with {overlapped} overlapped pairs: differs after tick {sim.TickNumber}");
        }
        // The save carries the data's hash: data with the knob off (or at another value) refuses it.
        Assert.Equal(SaveError.DataMismatch, SaveTestRun.TryLoad(Off, bytes));
        Assert.Equal(SaveError.DataMismatch, SaveTestRun.TryLoad(DataAt(overlap == 0.5 ? 0.75 : 0.5), bytes));
        _out.WriteLine($"saved at tick {savedAt} with {overlapped} pairs under 0.75 of their sum; 2,000 ticks equal after the load");
    }

    // ------------------------------------------------------------------ criterion 5, Stop: is it today's rule?

    /// <summary>
    /// Off (no gather pair at all): two Laborers standing overlapped at 0.5 of their radii sum, both Idle. Measures how many
    /// ticks they take to be back at their radii sum. If standing units never push apart today, criterion 5's Stop clause
    /// ("separates under today's rules within 2 s") cannot hold without a new rule.
    /// </summary>
    [Fact]
    public void Off_TwoIdleLaborersOverlapped_SeparationTime_Report()
    {
        Simulation sim = NewSim(Off, 8);
        EntityHandle a = Unit(sim, At(sim, 20, 20));
        EntityHandle b = Unit(sim, At(sim, 20, 20) + new Vector2(0.4f, 0f));
        UnitStore u = sim.World.Units;
        float sum = u.Radius[a.Index] + u.Radius[b.Index];
        float start = Vector2.Distance(u.Position[a.Index], u.Position[b.Index]);
        int separated = -1;
        for (int t = 0; t < 1200 && separated < 0; t++)
        {
            sim.Tick();
            if (Vector2.Distance(u.Position[a.Index], u.Position[b.Index]) >= sum - 1e-4f) separated = t;
        }
        _out.WriteLine($"off: two Idle Laborers {start:F3} m apart (sum {sum:F2}) after their spawn: back at the sum after {separated} ticks (-1: not in 1,200)");
    }

    // ------------------------------------------------------------------ spawn order: the developer's 4-worker claim

    private static ulong[] ByIdentity(double overlap, int count, bool reversed, int ticks)
    {
        Simulation sim = NewSim(DataAt(overlap), count + 4);
        EntityHandle[] ws = Setup(sim, Node.Mine, Workers.Mixed, count, reversed);
        UnitStore u = sim.World.Units;
        var hashes = new ulong[ticks];
        for (int t = 0; t < ticks; t++)
        {
            sim.Tick();
            ulong h = 14695981039346656037UL;
            foreach (EntityHandle e in ws)
            {
                int i = e.Index;
                foreach (uint bits in new[] { BitConverter.SingleToUInt32Bits(u.Position[i].X), BitConverter.SingleToUInt32Bits(u.Position[i].Y), (uint)u.State[i], (uint)u.Cargo[i] })
                    h = (h ^ bits) * 1099511628211UL;
            }
            hashes[t] = h;
        }
        return hashes;
    }

    [Theory]
    [InlineData(0.0, 3)]
    [InlineData(0.5, 3)]
    [InlineData(0.0, 4)]
    [InlineData(0.5, 4)]
    [InlineData(0.0, 8)]
    [InlineData(0.5, 8)]
    public void SpawnOrder_FirstDifference_Report(double overlap, int count)
    {
        ulong[] f = ByIdentity(overlap, count, false, 3000), r = ByIdentity(overlap, count, true, 3000);
        int first = -1;
        for (int t = 0; t < f.Length && first < 0; t++) if (f[t] != r[t]) first = t;
        _out.WriteLine($"{count} workers at {overlap}: spawn orders first differ at tick {first} (-1: never in 3,000)");
    }

    /// <summary>
    /// Re-check of BUG-1433 item 2: the developer's one-worker-each enemy scene (criterion 4, hash-equal), rebuilt as it now
    /// stands (player 1's Keep 6 rows below player 0's, both west of the mine): the two workers must actually meet, or the
    /// hash-equal row proves nothing about enemies. Counts ticks the two stand within 3 m and the closest share of their sum.
    /// </summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    public void EnemyScene_OneEach_DoTheTwoWorkersMeet_Report(double overlap)
    {
        Simulation sim = NewSim(DataAt(overlap), 8, players: 2);
        EntityHandle mine0 = Setup(sim, Node.Mine, Workers.Laborer, 1)[0];
        Building(sim, KeepX, KeepY + 6, 1);
        EntityHandle other = Unit(sim, At(sim, KeepX + 5, KeepY + 6 + 2), player: 1);
        sim.Enqueue(Command.Gather(1, other, At(sim, MineX, MineY)));
        UnitStore u = sim.World.Units;
        int near = 0;
        float closest = float.PositiveInfinity;
        for (int t = 0; t < 2400; t++)
        {
            sim.Tick();
            float d = Vector2.Distance(u.Position[mine0.Index], u.Position[other.Index]);
            if (d < 3f) near++;
            closest = MathF.Min(closest, d / (u.Radius[mine0.Index] + u.Radius[other.Index]));
        }
        _out.WriteLine($"one each at {overlap}: within 3 m for {near} ticks of 2,400; closest {closest:F3} of the radii sum; banked p0 {sim.World.Gold[0]}, p1 {sim.World.Gold[1]}");
        // A report, not a bound (the scene is the developer's): at 2026-11-15 the two never come within 3 m (0 ticks; closest
        // 5.51 of the sum) at 0 or 0.5, so the hash-equal row is vacuous for enemy contact (BUG-1433 item 2, still open for it).
    }
}
