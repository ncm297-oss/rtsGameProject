using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using System.Text.Json.Nodes;
using Rts.Sim.Abilities;
using Rts.Sim.Ai;
using Rts.Sim.Combat;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Tests.QA;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA M7-1 (session 2026-11-09-2226), the long attacks on regeneration: the invariant fuzz of <see cref="RegenQaTests"/> over
/// more seeds and 8,000 ticks; 50 seeded mid-match saves of an AI match where both factions regenerate (each loaded,
/// compared directly and by its save bytes, and run 300 ticks beside the reference, hash-equal every tick); 500
/// regenerating units twice in one process (and a digest written for a second process to compare, see
/// <see cref="DigestEnv"/>); and 2,500 units whose data only differs in <c>scale</c>, hash-equal every tick to the shipped
/// data's run (scale is presentation only).
/// </summary>
[Collection(SerialCollection.Name)]
public class RegenQaStressTests(ITestOutputHelper output)
{
    /// <summary>When set, <see cref="FiveHundredRegeneratingUnits_HashTheSame_TwiceInAProcess"/> writes its digest there (QA's cross-process check).</summary>
    internal const string DigestEnv = "RTS_QA_REGEN_DIGEST";

    [Theory]
    [Trait("Category", "Stress")]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    public void RegenInvariants_HoldEveryTick_OverLongFuzzedAiMatches(int seed)
    {
        for (int v = 0; v < 3; v++)
        {
            RegenQaTests.FuzzStats stats = RegenQaTests.Fuzz(seed, v, ticks: 8_000);
            output.WriteLine($"seed {seed} variant {v}: {stats}");
            Assert.True(stats.FactionHeals > 0 && stats.Deaths > 0, $"weak run: {stats}");
        }
    }

    /// <summary>
    /// 50 saves at seeded ticks of a two-AI match on data where Malazan regenerates 1.5 HP/s after 5 s and Whirlwind 7.7 HP/s
    /// with no delay, a few units carrying a long regenerating status: each loads, equals the reference directly and byte for
    /// byte (save, load, save), and runs on hash-equal for 300 ticks. At least ten of the saves hold a unit inside its delay
    /// and one with a fraction carried.
    /// </summary>
    [Fact]
    [Trait("Category", "Stress")]
    public void FiftySeededMidMatchSaves_WithRegenState_ContinueHashIdentical()
    {
        GameData d = RegenQaTests.Variant(1);
        var rng = new Random(2026_11_09);
        var ticks = new SortedSet<int>();
        while (ticks.Count < 50) ticks.Add(20 + rng.Next(3_000));
        int[] saveTicks = ticks.ToArray();
        int midDelay = 0, fraction = 0;
        var scene = new SaveScene("regen_two_ai", () =>
        {
            PlayerSeat[] seats = AiScenes.Seats(AiDifficulty.Normal, SeatController.Ai, SeatController.Ai);
            Simulation sim = AiScenes.Generated(seats, 5, false, out _, data: d, raiders: 12);
            for (int k = 0; k < 6; k++) sim.Tick();
            UnitStore u = sim.World.Units;
            int n = 0;
            for (int i = 0; i < u.Capacity && n < 6; i++)
                if (u.Alive[i])
                {
                    StatusSystem.Apply(sim.World, i, d.FindStatus("regenerating"), 0.35f + n, 600 + 300 * n, u.Owner[i]);
                    n++;
                }
            return sim;
        }, (sim, commands) =>
        {
            UnitStore u = sim.World.Units;
            // Reinforcements every 40 ticks, by command (so every load gets them too): a line unit of each player beside the
            // first live enemy unit in slot order, which its scan then fights, so units keep being hit through the run.
            if (sim.TickNumber % 40 == 0)
                for (int p = 0; p < 2; p++)
                    for (int i = 0; i < u.Capacity; i++)
                        if (u.Alive[i] && u.Owner[i] == 1 - p)
                        {
                            int line = Rts.Sim.ViewApi.StartBase.UnitOfSlot(d, sim.World.FactionOf(p), UnitSlot.Line);
                            commands.Add(Commands.Command.SpawnUnit(p, line, u.Position[i] + new Vector2(1.2f, 0.6f)));
                            break;
                        }
            if (Array.IndexOf(saveTicks, sim.TickNumber) < 0) return;
            bool mid = false, frac = false;
            for (int i = 0; i < u.Capacity; i++)
            {
                if (!u.Alive[i]) continue;
                int owner = u.Owner[i];
                if (u.LastDamagedTick[i] >= 0 && sim.TickNumber - u.LastDamagedTick[i] < sim.World.RegenDelayOf[owner]) mid = true;
                if (u.RegenAccum[i] != 0) frac = true;
            }
            if (mid) midDelay++;
            if (frac) fraction++;
        });
        SaveTestRun.Result r = SaveTestRun.RoundTrip(scene, saveTicks, after: 300);
        output.WriteLine($"saves {saveTicks.Length}, ticks compared {r.TicksCompared}, mid-delay saves {midDelay}, fraction saves {fraction}");
        Assert.Equal(50 * 300, r.TicksCompared);
        Assert.True(midDelay >= 10 && fraction >= 10, $"mid-delay {midDelay}, fraction {fraction}");
    }

    /// <summary>
    /// 500 units of two regenerating factions, every one hit to half its hp before the first tick and every third carrying a
    /// regenerating status, brawl for 3,000 ticks; two runs in one process hash the same every tick. With
    /// <see cref="DigestEnv"/> set the run's digest (a fold of every tick's hash) is written to that file, so two processes can
    /// be compared.
    /// </summary>
    [Fact]
    [Trait("Category", "Stress")]
    public void FiveHundredRegeneratingUnits_HashTheSame_TwiceInAProcess()
    {
        (ulong a, int healedA, long msA) = FiveHundred();
        (ulong b, int healedB, _) = FiveHundred();
        output.WriteLine($"digest {a:X16}, heals {healedA}, {msA} ms");
        Assert.Equal(a, b);
        Assert.Equal(healedA, healedB);
        Assert.True(healedA > 1000, $"only {healedA} heals");
        string? path = Environment.GetEnvironmentVariable(DigestEnv);
        if (!string.IsNullOrEmpty(path)) File.WriteAllText(path, $"{a:X16} {healedA}");
    }

    private static (ulong Digest, int Heals, long Ms) FiveHundred()
    {
        GameData d = RegenQaTests.Variant(1);
        var sim = new Simulation(new SimConfig(11, 2, 600, 8 * 600 + 32) { Data = d }, LocalMovementTests.Flat(96));
        CombatScenes.FlatBrawl(sim, 250, new Vector2(96f, 96f), gap: 10f, ranks: 10);
        World w = sim.World;
        UnitStore u = w.Units;
        int regen = d.FindStatus("regenerating"), live = 0;
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            live++;
            CombatSystem.HitUnit(w, new PendingHit(default, 1 - u.Owner[i], new EntityHandle(i, u.Generation[i]), false, u.Hp[i] / 2));
            if (i % 3 == 0) StatusSystem.Apply(w, i, regen, 2.5f, 1_000, u.Owner[i]);
        }
        Assert.Equal(500, live);
        ulong digest = 14695981039346656037UL;
        int heals = 0;
        var before = new int[u.Capacity];
        var sw = Stopwatch.StartNew();
        for (int k = 0; k < 3_000; k++)
        {
            Array.Copy(u.Hp, before, u.Capacity);
            sim.Tick();
            for (int i = 0; i < u.Capacity; i++)
                if (u.Alive[i] && u.Hp[i] > before[i]) heals++;
            digest = (digest ^ sim.StateHash()) * 1099511628211UL;
        }
        return (digest, heals, sw.ElapsedMilliseconds);
    }

    /// <summary>
    /// Scale is presentation only: 2,500 units (1,250 a side, mixed armies) on data where every unit of both factions has a
    /// scale between 0.5 and 3 hash the same every tick for 400 ticks as on the shipped data, though the data hashes differ.
    /// </summary>
    [Fact]
    [Trait("Category", "Stress")]
    public void TwoThousandFiveHundredUnits_WithMixedScales_HashAsWithout()
    {
        string[] scales = { "0.5", "1.5", "3", "2.25", "0.75" };
        GameData scaled = AiScenes.DataWith(dir =>
        {
            foreach (string f in new[] { "malazan", "whirlwind" })
                dir.EditJson($"factions/{f}/units.json", root =>
                {
                    int n = 0;
                    foreach (JsonNode? unit in root["units"]!.AsArray())
                        unit!["scale"] = JsonNode.Parse(scales[n++ % scales.Length]);
                });
        });
        Assert.NotEqual(TestSim.Data.ContentHash(), scaled.ContentHash());
        Assert.Contains(scaled.Units, x => x.Scale == 3f);
        Simulation a = Big(TestSim.Data), b = Big(scaled);
        Assert.Equal(a.StateHash(), b.StateHash());
        int deaths = 0;
        for (int k = 0; k < 400; k++)
        {
            a.Tick();
            b.Tick();
            if (a.StateHash() != b.StateHash()) Assert.Fail($"scale moved the sim at tick {a.TickNumber - 1}");
        }
        for (int i = 0; i < a.World.Units.Capacity; i++) if (!a.World.Units.Alive[i]) deaths++;
        output.WriteLine($"400 ticks equal, {deaths} free slots at the end");
        Assert.True(deaths > 100, "the armies never fought");
    }

    private static Simulation Big(GameData d)
    {
        var sim = new Simulation(new SimConfig(3, 2, 2_600, 8 * 2_600 + 32) { Data = d }, LocalMovementTests.Flat(160));
        CombatScenes.MixedBrawl(sim, 1_250, new Vector2(160f, 160f), gap: 8f);
        return sim;
    }
}
