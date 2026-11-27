using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// M7-9 (Request 59) criterion 3, the twin proof: a Teblor seat of difficulty D on data {<c>buildOrder</c> A,
/// <c>buildOrders.D</c> B} plays exactly as a seat of D on data {<c>buildOrder</c> B}, and a seat of another difficulty on
/// the first data plays exactly as on {<c>buildOrder</c> A} (<see cref="PerDifficultyOrders"/>). Pale Hills, the Teblor seat
/// against a Normal Malazan AI, seed 1, 12,000 ticks, the state hash compared every tick (it does not fold the data hash),
/// the seat's command log and its census at the end. A and B differ early (the D seat on A is shown to part from it before
/// tick 2,000), in the War Lodge's index (the rebuild rule: <see cref="ARazedBuilding_ReopensTheStepOfItsDifficultysList"/>),
/// in three <c>throughReserve</c> train steps and a <c>noReserve</c> step, and in the expand step's position.
/// </summary>
public class PerDifficultyTwinTests
{
    private const int Bound = 12_000, Early = 2_000;
    private const ulong Seed = 1;

    private readonly ITestOutputHelper _out;

    public PerDifficultyTwinTests(ITestOutputHelper output) => _out = output;

    private static readonly Lazy<GameData> s_a = new(() => PerDifficultyOrders.Data(PerDifficultyOrders.A()));
    private static readonly Lazy<GameData> s_b = new(() => PerDifficultyOrders.Data(PerDifficultyOrders.B()));

    internal static PlayerSeat[] Seats(GameData data, AiDifficulty teblor) => new[]
    {
        new PlayerSeat(data.FindFaction(PerDifficultyOrders.Faction), SeatController.Ai, teblor),
        new PlayerSeat(data.FindFaction("malazan"), SeatController.Ai, AiDifficulty.Normal),
    };

    private static Simulation New(GameData data, AiDifficulty teblor) => TeblorSwitchOrders.Match(data, "pale_hills", Seats(data, teblor), Seed);

    [Theory]
    [InlineData(AiDifficulty.Easy)]
    [InlineData(AiDifficulty.Normal)]
    [InlineData(AiDifficulty.Hard)]
    public void ASeat_PlaysItsDifficultysEntry_AndAnotherDifficultyPlaysBuildOrder(AiDifficulty d)
    {
        GameData a = s_a.Value, b = s_b.Value, both = PerDifficultyOrders.Data(PerDifficultyOrders.A(), (d, PerDifficultyOrders.B()));
        AiOpening opening = PerDifficultyOrders.Standard(both);
        Assert.True(opening.BuildOrderFor(d).SequenceEqual(PerDifficultyOrders.Standard(b).BuildOrder));
        Assert.False(opening.BuildOrder.SequenceEqual(opening.BuildOrderFor(d)));

        // The D seat: {A, D: B} against {B}, and against {A} only to show the two lists part early.
        Simulation on = New(both, d), twin = New(b, d), plain = New(a, d);
        AiPlayer seat = on.AiOf(0)!;
        Assert.Equal(PerDifficultyOrders.Standard(both).Id, both.Ai[seat.Faction].Openings[seat.OpeningIndex].Id);
        Assert.True(seat.Steps.SequenceEqual(opening.BuildOrderFor(d)));
        int reserved = 0, parted = -1;
        seat.ThoughtForTests = ai => { if (ai.ReserveStep >= 0) reserved++; };
        List<Command> log = AiScenes.Log(on, 0), twinLog = AiScenes.Log(twin, 0);
        while (on.TickNumber < Bound)
        {
            on.Tick();
            twin.Tick();
            ulong h = on.StateHash();
            Assert.True(h == twin.StateHash(), $"{d}: the entry and its twin differ at tick {on.TickNumber}");
            if (parted >= 0 || plain.TickNumber >= Early) continue;
            plain.Tick();
            if (plain.StateHash() != h) parted = plain.TickNumber;
        }
        Assert.True(parted >= 0, $"{d}: A and B played alike to tick {Early}: the twin proves nothing");
        Assert.Equal(twinLog.Count, log.Count);
        Assert.Equal(twinLog, log);
        AssertSameCensus(on, twin, 0, d.ToString());
        _out.WriteLine($"{d}: {Bound:N0} ticks equal, {log.Count} commands, {reserved} thinks with a reserved step, A parts from B at tick {parted}, end {on.StateHash():X16}");

        // Another difficulty on the same data plays A: the same twin against {A}.
        var other = (AiDifficulty)(((int)d + 1) % 3);
        Simulation onOther = New(both, other), plainOther = New(a, other);
        Assert.True(onOther.AiOf(0)!.Steps.SequenceEqual(opening.BuildOrder));
        while (onOther.TickNumber < Bound)
        {
            onOther.Tick();
            plainOther.Tick();
            Assert.True(onOther.StateHash() == plainOther.StateHash(), $"{other} on the {d} entry's data differs from A at tick {onOther.TickNumber}");
        }
        AssertSameCensus(onOther, plainOther, 0, other.ToString());
    }

    /// <summary>Player <paramref name="p"/>'s live units and buildings by type are the same in both sims.</summary>
    internal static void AssertSameCensus(Simulation x, Simulation y, int p, string what)
    {
        World a = x.World, b = y.World;
        Assert.Equal(Census(a, p), Census(b, p));
        Assert.True(Census(a, p).Units > 0, $"{what}: no units");
    }

    private static (int Units, int Buildings, string Types) Census(World w, int p)
    {
        var units = new int[w.Data.Units.Length];
        var buildings = new int[w.Data.Buildings.Length];
        int u = 0, b = 0;
        for (int k = 0; k < w.Units.Capacity; k++)
            if (w.Units.Alive[k] && w.Units.Owner[k] == p)
            {
                units[w.Units.TypeId[k]]++;
                u++;
            }
        for (int k = 0; k < w.Buildings.Capacity; k++)
            if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == p)
            {
                buildings[w.Buildings.TypeId[k]]++;
                b++;
            }
        return (u, b, string.Join(",", units) + "|" + string.Join(",", buildings));
    }

    /// <summary>
    /// Criterion 3, the rebuild rule on a per-difficulty list (the flat scene, RebuildTests' set-up, every Malazan opening
    /// carrying the same orders so the pick doesn't matter): <c>buildOrder</c> is [Barracks at 0 s, a Laborer at 3,600 s] and
    /// the Hard entry the same two steps the other way round. A razed Barracks reopens step 0 for an Easy or Normal seat and
    /// step 1 for a Hard one, and is built again.
    /// </summary>
    [Theory]
    [InlineData(AiDifficulty.Easy)]
    [InlineData(AiDifficulty.Normal)]
    [InlineData(AiDifficulty.Hard)]
    public void ARazedBuilding_ReopensTheStepOfItsDifficultysList(AiDifficulty d)
    {
        GameData data = s_rebuild.Value;
        Simulation sim = RebuildScene(data, d);
        AiPlayer ai = sim.AiOf(0)!;
        World w = sim.World;
        int barracks = data.FindBuilding("malazan_barracks"), step = d == AiDifficulty.Hard ? 1 : 0;
        Assert.Equal(AiStepAction.Build, ai.Steps[step].Action);
        List<Command> log = AiScenes.Log(sim, 0);
        EntityHandle first = default;
        for (int t = 0; t < 6000 && first.Generation == 0; t++)
        {
            sim.Tick();
            for (int k = 0; k < w.Buildings.Capacity; k++)
                if (w.Buildings.Alive[k] && w.Buildings.Owner[k] == 0 && w.Buildings.TypeId[k] == barracks && !w.Buildings.UnderConstruction[k])
                    first = new EntityHandle(k, w.Buildings.Generation[k]);
        }
        Assert.True(first.Generation != 0, $"{d}: no Barracks finished");
        NextThink(sim);
        Assert.True(ai.Board.StepDone[step]);
        Assert.False(ai.Board.StepDone[1 - step]); // the Laborer at 3,600 s
        int razed = sim.TickNumber;
        Assert.True(w.Buildings.Free(first));
        NextThink(sim);
        Assert.False(ai.Board.StepDone[step]); // due again: its building is gone
        for (int k = 0; k < 10 && !log.Exists(c => c.Kind == CommandKind.Build && c.TypeId == barracks && c.Tick > razed); k++) NextThink(sim);
        Command again = Assert.Single(log, c => c.Kind == CommandKind.Build && c.TypeId == barracks && c.Tick > razed);
        _out.WriteLine($"{d}: step {step} reopened, razed at {razed}, built again at {again.Tick} (think {ai.ThinkTicks})");
    }

    /// <summary>The rebuild scene's data: see <see cref="ARazedBuilding_ReopensTheStepOfItsDifficultysList"/>.</summary>
    private static readonly Lazy<GameData> s_rebuild = new(() => AiScenes.DataWithAi("malazan", root =>
    {
        foreach (System.Text.Json.Nodes.JsonNode? o in root["openings"]!.AsArray())
        {
            o!.AsObject().Remove("buildOrders");
            AiScenes.SetBuildOrder(o, AiScenes.BuildOrder(("time", 0, "build", "malazan_barracks", 0), ("time", 3600, "train", "malazan_laborer", 1)));
            o["buildOrders"] = new System.Text.Json.Nodes.JsonObject
            {
                ["hard"] = AiScenes.BuildOrder(("time", 3600, "train", "malazan_laborer", 1), ("time", 0, "build", "malazan_barracks", 0)),
            };
        }
    }));

    internal static Simulation RebuildScene(GameData data, AiDifficulty d)
    {
        Simulation sim = AiScenes.Flat(AiScenes.Seats(d, SeatController.Ai, SeatController.Human), data);
        BuildMaps.Give(sim, 0, 2000, 2000);
        return sim;
    }

    internal static void NextThink(Simulation sim)
    {
        int think = sim.AiOf(0)!.ThinkTicks;
        do sim.Tick(); while ((sim.TickNumber - 1) % think != 0);
    }

    /// <summary>
    /// Criterion 10: nothing under <c>sim/Rts.Sim/Ai/</c> reads the opening's <c>buildOrder</c> directly; every step reader
    /// goes through the seat's resolved list (<c>AiPlayer.Steps</c>).
    /// </summary>
    [Fact]
    public void NoAiFile_ReadsTheOpeningsBuildOrder()
    {
        string folder = Path.Combine(TestDataDir.RepoRoot(), "sim", "Rts.Sim", "Ai");
        string[] files = Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories);
        Assert.True(files.Length > 10, $"only {files.Length} files under {folder}");
        foreach (string f in files)
            Assert.False(File.ReadAllText(f).Contains("Opening.BuildOrder", StringComparison.Ordinal), $"{Path.GetFileName(f)} reads Opening.BuildOrder");
        Assert.Contains("Steps", File.ReadAllText(Path.Combine(folder, "BuildOrderExecutor.cs")));
    }

    /// <summary>Criterion 9: a think of a Hard seat on a per-difficulty list allocates nothing (the list is a field).</summary>
    [Collection(SerialCollection.Name)]
    public class Serial
    {
        private readonly ITestOutputHelper _out;

        public Serial(ITestOutputHelper output) => _out = output;

        [Fact]
        public void AThinkOnAPerDifficultyList_AllocatesNothing()
        {
            Simulation sim = RebuildScene(s_rebuild.Value, AiDifficulty.Hard);
            AiPlayer ai = sim.AiOf(0)!;
            Assert.Equal(AiStepAction.Train, ai.Steps[0].Action); // the Hard entry
            for (int k = 0; k < 20; k++) NextThink(sim);
            while (sim.TickNumber % ai.ThinkTicks != 0) sim.Tick();
            AllocationProbe.AssertZero(() => ai.Think(), _out);
        }
    }
}
