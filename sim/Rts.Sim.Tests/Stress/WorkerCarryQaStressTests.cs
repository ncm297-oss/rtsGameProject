using System.Diagnostics;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.Stress;

/// <summary>
/// QA for M7-7 (Request 48, <c>units.json</c> <c>carry</c>) under load: seeded carry layouts (each shipped faction's worker
/// absent / 1 / 7 / 15 / 40, by seed) in two-seat AI matches across Easy, Normal and Hard; every tick no unit carries more
/// than its type's carry or less than 0, non-workers carry nothing, banks never go negative; a twin run ends on the same
/// hash; a save at 3,000 loads and runs 1,500 ticks hash-identical. And one layout in three processes.
/// Rules 58 / 59: every copy strips <c>carry</c> first and sets its own.
/// </summary>
[Trait("Category", "Stress")]
[Collection(SerialCollection.Name)]
public class WorkerCarryQaStressTests(ITestOutputHelper output)
{
    private const string ChildVariable = "RTS_QA_CARRY_CHILD", Marker = "QA-CARRY-DIGEST ";
    private static readonly int?[] Choices = { null, 1, 7, 15, 40 };

    /// <summary>Faction <c>f</c>'s worker carry in layout <paramref name="seed"/> (null: absent).</summary>
    private static int? CarryFor(ulong seed, int f) => Choices[(int)((seed * 7 + (ulong)f * 3) % (ulong)Choices.Length)];

    private static GameData Layout(ulong seed) => AiScenes.DataWith(dir =>
    {
        UnitCarryLoaderTests.StripCarry(dir);
        string[] factions = TestSim.ShippedFactions.ToArray();
        for (int f = 0; f < factions.Length; f++)
            if (CarryFor(seed, f) is int c) dir.SetUnitField(factions[f], AiScenes.UnitKey(factions[f], UnitSlot.Worker), "carry", c.ToString());
    });

    private static Simulation Match(GameData d, ulong seed, string a, string b, AiDifficulty da, AiDifficulty db) =>
        AiScenes.Generated(new PlayerSeat[] { new(d.FindFaction(a), SeatController.Ai, da), new(d.FindFaction(b), SeatController.Ai, db) }, seed, false, out _, data: d);

    private static void AssertInvariants(Simulation sim, int[] peak)
    {
        World w = sim.World;
        UnitStore u = w.Units;
        for (int s = 0; s < u.Capacity; s++)
        {
            if (!u.Alive[s]) continue;
            UnitDef def = w.Data.Units[u.TypeId[s]];
            int c = u.Cargo[s], max = w.Data.CarryOf(def.Id);
            if (def.Slot != UnitSlot.Worker && c != 0) Assert.Fail($"tick {sim.TickNumber}: a {def.Key} carries {c}");
            if (c < 0 || c > max) Assert.Fail($"tick {sim.TickNumber}: a {def.Key} carries {c} of {max}");
            peak[u.Owner[s]] = Math.Max(peak[u.Owner[s]], c);
        }
        for (int p = 0; p < w.Config.PlayerCount; p++)
            if (w.Gold[p] < 0 || w.Wood[p] < 0) Assert.Fail($"tick {sim.TickNumber}: player {p} bank {w.Gold[p]} / {w.Wood[p]}");
    }

    [Theory]
    [InlineData(1UL, "teblor", "malazan", AiDifficulty.Easy, AiDifficulty.Hard)]
    [InlineData(2UL, "teblor", "whirlwind", AiDifficulty.Hard, AiDifficulty.Normal)]
    [InlineData(3UL, "malazan", "teblor", AiDifficulty.Normal, AiDifficulty.Easy)]
    [InlineData(4UL, "whirlwind", "teblor", AiDifficulty.Hard, AiDifficulty.Hard)]
    [InlineData(5UL, "teblor", "teblor", AiDifficulty.Easy, AiDifficulty.Normal)]
    [InlineData(6UL, "malazan", "whirlwind", AiDifficulty.Normal, AiDifficulty.Hard)]
    public void SeededCarryLayouts_InvariantsTwinsAndASave(ulong seed, string a, string b, AiDifficulty da, AiDifficulty db)
    {
        GameData d = Layout(seed);
        Simulation sim = Match(d, seed, a, b, da, db), twin = Match(d, seed, a, b, da, db);
        var peak = new int[2];
        byte[]? saved = null;
        while (sim.TickNumber < 6000)
        {
            sim.Tick();
            twin.Tick();
            AssertInvariants(sim, peak);
            if (sim.TickNumber == 3000) saved = SaveTestRun.Save(sim);
        }
        Assert.Equal(sim.StateHash(), twin.StateHash());
        int wa = d.FindUnit(AiScenes.UnitKey(a, UnitSlot.Worker)), wb = d.FindUnit(AiScenes.UnitKey(b, UnitSlot.Worker));
        output.WriteLine($"seed {seed} {a}({da}) v {b}({db}): carry {d.CarryOf(wa)} / {d.CarryOf(wb)}; peak load {peak[0]} / {peak[1]}; end {sim.StateHash():X16}");
        Assert.True(peak[0] <= d.CarryOf(wa) && peak[1] <= d.CarryOf(wb));

        Simulation reference = Match(d, seed, a, b, da, db);
        while (reference.TickNumber < 3000) reference.Tick();
        Simulation loaded = SaveTestRun.Load(d, saved!);
        Assert.Equal(reference.StateHash(), loaded.StateHash());
        for (int t = 0; t < 1500; t++)
        {
            reference.Tick();
            loaded.Tick();
            Assert.True(reference.StateHash() == loaded.StateHash(), $"loaded run diverged at tick {reference.TickNumber}");
        }
    }

    /// <summary>One layout (seed 4: every faction its own value), Hard v Normal Teblor v Malazan, 8,000 ticks: the fold of every tick's hash and the end hash are the same in this process and two children.</summary>
    [Fact]
    public void ACarryLayout_SameInThreeProcesses()
    {
        GameData d = Layout(4);
        Simulation sim = Match(d, 13, "teblor", "malazan", AiDifficulty.Hard, AiDifficulty.Normal);
        var h = new Determinism.StateHasher();
        var peak = new int[2];
        while (sim.TickNumber < 8000)
        {
            sim.Tick();
            h.Add(sim.StateHash());
            AssertInvariants(sim, peak);
        }
        string mine = $"{h.Value:X16} {sim.StateHash():X16} {peak[0]} {peak[1]}";
        if (Environment.GetEnvironmentVariable(ChildVariable) == "1")
        {
            output.WriteLine(Marker + mine);
            return;
        }
        string first = RunChild(), second = RunChild();
        output.WriteLine($"this process {mine}; child 1 {first}; child 2 {second}");
        Assert.Equal(mine, first);
        Assert.Equal(mine, second);
    }

    /// <summary>M7-H3: the shared child runner, its 600 s now the child's processor time (a wall ceiling 3 times it), not the wall clock.</summary>
    private static string RunChild() =>
        MapFileTests.RunChild(typeof(WorkerCarryQaStressTests), nameof(ACarryLayout_SameInThreeProcesses), ChildVariable, Marker, seconds: 600);
}
