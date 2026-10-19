using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Numerics;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Map;
using Rts.Sim.Save;
using Rts.Sim.Tests.Save;
using Rts.Sim.ViewApi;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA (M6-S3): the AI section of a save. The loaded twin and the unsaved twin are ticked side by side and compared on the
/// AI's command stream tick by tick (every field of every command each AI seat sends, through the
/// <see cref="AiPlayer.SentForTests"/> seam), not only on the hash, saving at moments found from the Blackboards: a wave
/// active and just started, a retreat or recall just made, a defense pull just made, a no-site wait, a pending house Build,
/// a pending Train, a pending step, a cast just sent (its command queued) and a few ticks into it, a ground cast remembered.
/// A chain of 24 save / reloads on an AI-v-AI 4-seat 256 map. Forged opening picks (Easy must be 0) and stream increments.
/// The BUG-0960 item 1 length bound with every capacity at the maximum and with each one alone at the maximum.
/// </summary>
public class AiSaveQaTests
{
    private readonly ITestOutputHelper _out;

    public AiSaveQaTests(ITestOutputHelper output) => _out = output;

    private static GameData Data => TestSim.Data;

    // ---------- the command-stream twin ----------

    /// <summary>A save moment: fires on a seat's state, or on a key changing since the previous tick.</summary>
    private sealed class Probe
    {
        public required string Name { get; init; }
        public Func<Simulation, AiPlayer, bool>? Holds { get; init; }
        public Func<AiPlayer, long>? Key { get; init; }
        public int Delay { get; init; } // ticks after the key changed
        public Func<AiPlayer, bool>? Only { get; init; } // a key change counts only when this holds for the seat
        public long[] Prev = Array.Empty<long>();
        public readonly List<int> ChangedAt = new();
        public readonly List<int> Fired = new();
    }

    private static Probe[] Probes() => new[]
    {
        new Probe { Name = "wave active", Holds = (_, ai) => ai.Board.WaveActive },
        new Probe { Name = "wave just started", Key = ai => ai.Board.WavesStarted },
        new Probe { Name = "retreat / wave end just made", Key = ai => ai.Board.WaveActive ? 1 : 0, Only = ai => !ai.Board.WaveActive },
        new Probe { Name = "defense recall just made", Key = ai => ai.Board.WaveRecalls },
        new Probe { Name = "defense pull just made", Key = ai => ai.Board.WavePulls },
        new Probe { Name = "defending", Holds = (_, ai) => ai.Board.Defending },
        new Probe { Name = "defend all", Holds = (_, ai) => ai.Board.DefendAll },
        new Probe { Name = "no-site wait", Holds = (s, ai) => ai.Board.StepNoSiteUntil.Any(u => u > s.TickNumber) },
        new Probe { Name = "house Build pending", Holds = (_, ai) => ai.Board.HousePending.Kind == PendingKind.Build },
        new Probe { Name = "production Train pending", Holds = (_, ai) => ai.Board.ProductionPending.Kind == PendingKind.Train },
        new Probe { Name = "step Build pending", Holds = (_, ai) => ai.Board.StepPending.Any(p => p.Kind == PendingKind.Build) },
        new Probe { Name = "step Train/Research pending", Holds = (_, ai) => ai.Board.StepPending.Any(p => p.Kind is PendingKind.Train or PendingKind.Research) },
        new Probe { Name = "cast just sent", Key = ai => ai.Board.CastsSent },
        new Probe { Name = "cast in flight (+4)", Key = ai => ai.Board.CastsSent, Delay = 4 },
        new Probe { Name = "ground cast remembered", Holds = (_, ai) => ai.Board.RecentCastCount > 0 },
        new Probe { Name = "scout out", Holds = (_, ai) => ai.Board.ScoutPhase == 1 },
        new Probe { Name = "site searches just run", Key = ai => ai.SiteSearches },
    };

    private static string Fmt(in Command c) =>
        $"{c.Kind} p{c.Player} t{c.Tick} s{c.Sequence} ty{c.TypeId} x{BitConverter.SingleToInt32Bits(c.Position.X):X8} " +
        $"y{BitConverter.SingleToInt32Bits(c.Position.Y):X8} u{c.Unit.Index}/{c.Unit.Generation} f{c.Flags} " +
        $"tg{c.Target.Index}/{c.Target.Generation} b{c.TargetIsBuilding}";

    private static void Hook(Simulation sim, List<string> sink)
    {
        for (int p = 0; p < sim.World.Config.PlayerCount; p++)
            if (sim.AiOf(p) is { } ai) ai.SentForTests = c => sink.Add(Fmt(c));
    }

    private sealed record TwinResult(Dictionary<string, List<int>> Fired, int Loads, int TicksCompared, int CommandsCompared);

    /// <summary>
    /// Runs <paramref name="scene"/> to <paramref name="window"/>; at each probe's first <paramref name="perProbe"/> moments
    /// (at least 150 ticks apart) saves, loads and ticks the load beside the reference for <paramref name="after"/> ticks,
    /// comparing every AI command sent each tick (in order, every field), the hash every tick, and the AI seats field for
    /// field at T, T + 1 and the end.
    /// </summary>
    private static TwinResult Twin(SaveScene scene, int window, int after, int perProbe)
    {
        Simulation reference = scene.Build();
        GameData data = reference.World.Data;
        var refCmds = new List<string>();
        Hook(reference, refCmds);
        Probe[] probes = Probes();
        int players = reference.World.Config.PlayerCount;
        foreach (Probe pr in probes) pr.Prev = new long[players];
        var live = new List<(Simulation Sim, List<string> Cmds, int SavedAt, string Why)>();
        var savedTicks = new HashSet<int>();
        var commands = new List<Command>();
        int compared = 0, cmdCompared = 0, loads = 0;
        while (reference.TickNumber < window || live.Count > 0)
        {
            int t = reference.TickNumber;
            commands.Clear();
            scene.Script(reference, commands);
            foreach (Command c in commands)
            {
                reference.Enqueue(c);
                foreach (var l in live) l.Sim.Enqueue(c);
            }

            string? why = null;
            if (t < window)
            {
                foreach (Probe pr in probes)
                {
                    bool fire = false;
                    for (int p = 0; p < players; p++)
                    {
                        if (reference.AiOf(p) is not { } ai) continue;
                        if (pr.Holds != null && pr.Holds(reference, ai)) fire = true;
                        if (pr.Key != null)
                        {
                            long k = pr.Key(ai);
                            if (t > 0 && k != pr.Prev[p] && (pr.Only == null || pr.Only(ai))) pr.ChangedAt.Add(t);
                            pr.Prev[p] = k;
                        }
                    }
                    if (pr.Key != null) fire = pr.ChangedAt.Contains(t - pr.Delay);
                    if (fire && pr.Fired.Count < perProbe && (pr.Fired.Count == 0 || t - pr.Fired[^1] >= 150))
                    {
                        pr.Fired.Add(t);
                        why = why == null ? pr.Name : why + ", " + pr.Name;
                    }
                }
            }
            if (why != null && savedTicks.Add(t))
            {
                byte[] saved = SaveTestRun.Save(reference);
                Simulation loaded = SaveTestRun.Load(data, saved);
                for (int p = 0; p < players; p++) SaveTestRun.AssertSameAi(reference.AiOf(p), loaded.AiOf(p), $"{scene.Name} saved at {t} ({why}): at load, seat {p}");
                Assert.True(saved.AsSpan().SequenceEqual(SaveTestRun.Save(loaded)), $"{scene.Name} saved at {t}: save -> load -> save differs");
                var cmds = new List<string>();
                Hook(loaded, cmds);
                live.Add((loaded, cmds, t, why));
                loads++;
            }

            refCmds.Clear();
            reference.Tick();
            ulong hash = reference.StateHash();
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var l = live[i];
                l.Sim.Tick();
                compared++;
                string at = $"{scene.Name} saved at {l.SavedAt} ({l.Why}), tick {reference.TickNumber}";
                if (l.Cmds.Count != refCmds.Count || !l.Cmds.SequenceEqual(refCmds))
                    Assert.Fail($"{at}: AI commands differ\n  unsaved: {string.Join(" | ", refCmds)}\n  loaded:  {string.Join(" | ", l.Cmds)}");
                cmdCompared += refCmds.Count;
                l.Cmds.Clear();
                if (l.Sim.StateHash() != hash) Assert.Fail($"{at}: hash differs");
                if (reference.TickNumber == l.SavedAt + 1 || reference.TickNumber == l.SavedAt + after)
                    for (int p = 0; p < players; p++) SaveTestRun.AssertSameAi(reference.AiOf(p), l.Sim.AiOf(p), $"{at}: seat {p}");
                if (reference.TickNumber == l.SavedAt + after)
                {
                    SaveTestRun.AssertSameState(reference, l.Sim, at);
                    live.RemoveAt(i);
                }
            }
        }
        return new TwinResult(probes.ToDictionary(p => p.Name, p => p.Fired), loads, compared, cmdCompared);
    }

    private void Print(string name, TwinResult r)
    {
        _out.WriteLine($"{name}: {r.Loads} loads, {r.TicksCompared:N0} twin ticks, {r.CommandsCompared:N0} AI commands compared");
        foreach ((string probe, List<int> at) in r.Fired) _out.WriteLine($"  {probe}: {(at.Count == 0 ? "never" : string.Join(", ", at))}");
    }

    public static IEnumerable<object[]> AiRows() => AiSaveScenes.Rows();

    /// <summary>Every AI-v-AI row of the developer's set, the command stream compared tick by tick after each probed save.</summary>
    [Theory]
    [MemberData(nameof(AiRows))]
    public void AiMatch_LoadedTwin_SendsTheSameAiCommands_EveryTick(int seed, int players, AiDifficulty difficulty, bool cramped)
    {
        SaveScene scene = AiSaveScenes.AiMatch((ulong)seed, players, difficulty, cramped);
        TwinResult r = Twin(scene, window: 9000, after: 600, perProbe: 2);
        Print(scene.Name, r);
        Assert.True(r.CommandsCompared > 0);
        // The moments the brief names must be reached somewhere in a row (the cramped row is the no-site row).
        Assert.NotEmpty(r.Fired["wave active"].Concat(r.Fired["defending"]));
        Assert.NotEmpty(r.Fired["house Build pending"].Concat(r.Fired["production Train pending"]).Concat(r.Fired["step Build pending"]));
        if (cramped) Assert.NotEmpty(r.Fired["no-site wait"]);
    }

    /// <summary>The menu's match (a scripted human against a Normal AI), the command stream compared.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void HumanVsAi_LoadedTwin_SendsTheSameAiCommands_EveryTick(int seed)
    {
        SaveScene scene = AiSaveScenes.HumanVsAi((ulong)seed);
        TwinResult r = Twin(scene, window: 6000, after: 600, perProbe: 2);
        Print(scene.Name, r);
        Assert.True(r.CommandsCompared > 0);
    }

    /// <summary>AI and human seats interleaved (AI, human, AI, human): the AIST entries skip the human players.</summary>
    [Fact]
    public void InterleavedSeats_AiHumanAiHuman_LoadedTwin_SendsTheSameAiCommands()
    {
        var seats = new[]
        {
            new PlayerSeat(0, SeatController.Ai, AiDifficulty.Hard), new PlayerSeat(1, SeatController.Human, AiDifficulty.Normal),
            new PlayerSeat(1, SeatController.Ai, AiDifficulty.Easy), new PlayerSeat(0, SeatController.Human, AiDifficulty.Normal),
        };
        SaveScene scene = Generated("interleaved_ai_h_ai_h", 9, seats, 128, 12, 8, raiders: 0);
        TwinResult r = Twin(scene, window: 5000, after: 500, perProbe: 1);
        Print(scene.Name, r);
        Assert.True(r.CommandsCompared > 0);
        Simulation fresh = scene.Build();
        Assert.Equal(-1, fresh.AiOpening(1));
        Assert.Equal(-1, fresh.AiOpening(3));
        Assert.Equal(0, fresh.AiOpening(2)); // Easy
    }

    /// <summary>
    /// The defense against a wave's home: an AI-v-AI match whose script, 120 ticks into each wave of either seat, drops the
    /// other seat's line units on the waving seat's hall, 3, then 6, then 16 (so the defense picks home units, pulls wave
    /// members, recalls the whole wave and every unit answers): saves at the pull, the recall and while defending all.
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    public void AWaveRaidedAtHome_PullAndRecall_LoadedTwin_SendsTheSameAiCommands(int seed)
    {
        SaveScene basic = AiSaveScenes.AiMatch((ulong)seed, 2, AiDifficulty.Normal);
        int raids = 0;
        var lastWave = new int[2];
        int[] sizes = { 3, 6, 16 };
        SaveScene scene = basic with
        {
            Name = basic.Name + "_raided_home",
            Script = (sim, output) =>
            {
                for (int p = 0; p < 2; p++)
                {
                    AiPlayer ai = sim.AiOf(p)!;
                    if (!ai.Board.WaveActive || !ai.HasHall || ai.Board.WavesStarted == lastWave[p] || sim.TickNumber - ai.Board.WaveStartTick < 120) continue;
                    lastWave[p] = ai.Board.WavesStarted;
                    int enemy = 1 - p;
                    int line = StartBase.UnitOfSlot(sim.World.Data, sim.World.FactionOf(enemy), UnitSlot.Line);
                    int n = sizes[Math.Min(raids++, sizes.Length - 1)];
                    for (int k = 0; k < n; k++)
                        output.Add(Command.SpawnUnit(enemy, line, ai.ClampToMap(ai.HallPoint + new Vector2(6f + (k % 4), 6f + (k / 4)))));
                }
            },
        };
        TwinResult r = Twin(scene, window: 12000, after: 600, perProbe: 2);
        Print(scene.Name, r);
        _out.WriteLine($"raids dropped: {raids}");
        Assert.True(raids >= 1, "no seat sent a wave");
        Assert.NotEmpty(r.Fired["defense pull just made"].Concat(r.Fired["defense recall just made"]).Concat(r.Fired["defend all"]));
    }

    /// <summary>
    /// A defeated AI seat (its only building, a House, razed by the next seat's army spawned beside it) keeps its AI in the
    /// save: three AI seats on a flat map, the twin compared on commands and hash across the defeat.
    /// </summary>
    [Fact]
    public void ADefeatedAiSeat_LoadedTwin_SendsTheSameAiCommands()
    {
        GameData data = Data;
        var seats = ImmutableArray.Create(new PlayerSeat(0, SeatController.Ai, AiDifficulty.Hard),
            new PlayerSeat(1, SeatController.Ai, AiDifficulty.Normal), new PlayerSeat(0, SeatController.Ai, AiDifficulty.Easy));
        var scene = new SaveScene("ai_defeated_seat", () =>
        {
            var sim = TestSim.Explored(new Simulation(new SimConfig(11, 3, 256, 1024) { Data = data, Seats = seats }, LocalMovementTests.Flat(64)));
            for (int p = 0; p < 2; p++)
            {
                int faction = sim.World.FactionOf(p);
                float x0 = p == 0 ? 24f : 96f;
                sim.Enqueue(Command.SpawnBuilding(p, StartBase.BuildingOfSlot(data, faction, BuildingSlot.TownHall), new Vector2(x0, 60f)));
                for (int k = 0; k < 5; k++) sim.Enqueue(Command.SpawnUnit(p, StartBase.UnitOfSlot(data, faction, UnitSlot.Worker), new Vector2(x0 + 1.2f * k, 70f)));
                for (int k = 0; k < 4; k++) sim.Enqueue(Command.SpawnUnit(p, StartBase.UnitOfSlot(data, faction, UnitSlot.Line), new Vector2(x0 + 1.2f * k, 76f)));
            }
            sim.Enqueue(Command.SpawnBuilding(2, StartBase.BuildingOfSlot(data, sim.World.FactionOf(2), BuildingSlot.House), new Vector2(60f, 110f)));
            for (int k = 0; k < 8; k++)
                sim.Enqueue(Command.SpawnUnit(1, StartBase.UnitOfSlot(data, sim.World.FactionOf(1), UnitSlot.Line), new Vector2(54f + 1.2f * k, 104f)));
            return sim;
        }, (sim, output) =>
        {
            // Seat 1's raiders attack the House until it falls (a script command, sent to both twins).
            if (sim.TickNumber % 40 != 3 || sim.World.IsDefeated(2)) return;
            EntityHandle house = SaveScenes.FirstBuilding(sim.World, 2);
            if (house == default) return;
            foreach (EntityHandle u in SaveScenes.UnitsOf(sim.World, 1, StartBase.UnitOfSlot(data, sim.World.FactionOf(1), UnitSlot.Line)))
                output.Add(Command.Attack(1, u, house, isBuilding: true));
        });
        TwinResult r = Twin(scene, window: 3000, after: 400, perProbe: 2);
        Print(scene.Name, r);
        Simulation end = scene.Build();
        var cmds = new List<Command>();
        while (end.TickNumber < 3000)
        {
            cmds.Clear();
            scene.Script(end, cmds);
            foreach (Command c in cmds) end.Enqueue(c);
            end.Tick();
        }
        _out.WriteLine($"seat 2 defeated: {end.World.IsDefeated(2)}");
        Assert.True(end.World.IsDefeated(2), "seat 2 was never defeated");
        // And a save made after the defeat loads with seat 2's AI still there, field for field.
        Simulation loaded = SaveTestRun.Load(data, SaveTestRun.Save(end));
        SaveTestRun.AssertSameState(end, loaded, "after the defeat");
    }

    // ---------- 24 reloads in a chain on a 4-seat 256 map ----------

    /// <summary>
    /// An AI-v-AI 4-seat 256 match; from tick 4,000 the twin is saved and reloaded 24 times in a chain (the load of a load
    /// of a load...) at uneven intervals (some on a think tick, some just before one), every tick compared on hash and AI
    /// commands, the seats field for field after every reload and the whole state at the end.
    /// </summary>
    [Fact]
    public void FourSeats256_AiVsAi_AChainOf24Reloads_FollowsTheUnsavedRun()
    {
        var seats = Enumerable.Range(0, 4).Select(p => new PlayerSeat(p % 2, SeatController.Ai, (AiDifficulty)(p % 3))).ToArray();
        SaveScene scene = Generated("ai_four_seats_256", 7, seats, 256, 24, 16, raiders: 2);
        Simulation reference = scene.Build();
        var refCmds = new List<string>();
        Hook(reference, refCmds);
        while (reference.TickNumber < 4000) reference.Tick();
        Simulation chain = SaveTestRun.Load(Data, SaveTestRun.Save(reference));
        var chainCmds = new List<string>();
        Hook(chain, chainCmds);
        int[] gaps = { 1, 2, 3, 5, 7, 10, 11, 19, 20, 21, 39, 40, 41, 50, 60, 79, 80, 81, 99, 100, 120, 150, 199, 200, 1 };
        int reloads = 0, commandsCompared = 0;
        long bytesMax = 0;
        for (int g = 0; g <= gaps.Length; g++)
        {
            int run = g < gaps.Length ? gaps[g] : 600;
            for (int k = 0; k < run; k++)
            {
                refCmds.Clear();
                chainCmds.Clear();
                reference.Tick();
                chain.Tick();
                string at = $"after {reloads} reloads, tick {reference.TickNumber}";
                Assert.True(chainCmds.SequenceEqual(refCmds), $"{at}: AI commands differ\n  unsaved: {string.Join(" | ", refCmds)}\n  chain:   {string.Join(" | ", chainCmds)}");
                commandsCompared += refCmds.Count;
                Assert.True(reference.StateHash() == chain.StateHash(), $"{at}: hash differs");
            }
            if (g == gaps.Length || reloads == 24) continue;
            byte[] bytes = SaveTestRun.Save(chain);
            bytesMax = Math.Max(bytesMax, bytes.Length);
            chain = SaveTestRun.Load(Data, bytes);
            chainCmds = new List<string>();
            Hook(chain, chainCmds);
            reloads++;
            for (int p = 0; p < 4; p++) SaveTestRun.AssertSameAi(reference.AiOf(p), chain.AiOf(p), $"reload {reloads} at {reference.TickNumber}: seat {p}");
        }
        SaveTestRun.AssertSameState(reference, chain, $"after {reloads} reloads at {reference.TickNumber}");
        _out.WriteLine($"{reloads} reloads, ended at tick {reference.TickNumber}, {commandsCompared:N0} AI commands compared, largest save {bytesMax:N0} bytes");
        for (int p = 0; p < 4; p++)
            _out.WriteLine($"seat {p}: opening {reference.AiOpening(p)}, thinks {reference.AiOf(p)!.Thinks}, sent {reference.AiOf(p)!.CommandsSent}, waves {reference.AiOf(p)!.Board.WavesStarted}");
        Assert.Equal(24, reloads);
        Assert.True(commandsCompared > 0);
    }

    // ---------- forged picks and streams ----------

    private static Simulation SmallAiSim(AiDifficulty difficulty, ulong seed = 5)
    {
        var config = new SimConfig(seed, 1, 2, 8)
        {
            Data = Data, ResourceCapacity = 1, BuildingCapacity = 2, ZoneCapacity = 1, ProjectileCapacity = 1, StatsSampleCapacity = 0,
            Seats = ImmutableArray.Create(new PlayerSeat(0, SeatController.Ai, difficulty)),
        };
        var sim = new Simulation(config, LocalMovementTests.Flat(16));
        sim.Enqueue(Command.SpawnBuilding(0, Data.FindBuilding("malazan_garrison_keep"), new Vector2(10f, 10f)));
        sim.Enqueue(Command.SpawnUnit(0, CombatScenes.Laborer, new Vector2(20f, 20f)));
        int ticks = 2 * sim.AiOf(0)!.ThinkTicks + 1;
        for (int t = 0; t < ticks; t++) sim.Tick();
        return sim;
    }

    /// <summary>The offset of the first AI seat's opening index: AIST tag, seat count, the seat's player index, then the pick.</summary>
    private static int PickOffset(byte[] bytes) => SaveBytes.AiSection(bytes) + 12;

    [Theory]
    [InlineData(AiDifficulty.Easy)]
    [InlineData(AiDifficulty.Normal)]
    [InlineData(AiDifficulty.Hard)]
    public void EveryForgedOpeningPick_OtherThanTheLoadsOwn_IsMalformed(AiDifficulty difficulty)
    {
        int openings = Data.Ai[Data.FindFaction("malazan")].Openings.Length;
        foreach (ulong seed in new ulong[] { 1, 2, 5, 9, 12345 })
        {
            Simulation sim = SmallAiSim(difficulty, seed);
            byte[] good = SaveTestRun.Save(sim);
            int at = PickOffset(good);
            int pick = BinaryPrimitives.ReadInt32LittleEndian(good.AsSpan(at));
            Assert.Equal(sim.AiOpening(0), pick);
            if (difficulty == AiDifficulty.Easy) Assert.Equal(0, pick);
            var values = Enumerable.Range(-1, openings + 3).Concat(new[] { int.MinValue, int.MaxValue, 255, 256, 65536 });
            foreach (int v in values)
            {
                byte[] bad = (byte[])good.Clone();
                BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(at), v);
                SaveError e = SaveTestRun.TryLoad(Data, SaveBytes.Rechecksum(bad));
                Assert.True(e == (v == pick ? SaveError.None : SaveError.Malformed), $"{difficulty} seed {seed}: pick {pick} forged to {v}: {e}");
            }
        }
    }

    [Fact]
    public void AForgedStreamIncrementOrPlayerIndex_IsMalformed_AForgedStreamState_Loads()
    {
        byte[] good = SaveTestRun.Save(SmallAiSim(AiDifficulty.Hard));
        int player = SaveBytes.AiSection(good) + 8, state = PickOffset(good) + 4, increment = state + 8;
        foreach (int v in new[] { 1, -1, 16, int.MaxValue })
        {
            byte[] bad = (byte[])good.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(player), v);
            Assert.Equal(SaveError.Malformed, SaveTestRun.TryLoad(Data, SaveBytes.Rechecksum(bad)));
        }
        foreach (ulong delta in new ulong[] { 1, 2, 0x8000_0000_0000_0000 })
        {
            byte[] bad = (byte[])good.Clone();
            ulong inc = BinaryPrimitives.ReadUInt64LittleEndian(bad.AsSpan(increment));
            BinaryPrimitives.WriteUInt64LittleEndian(bad.AsSpan(increment), inc + delta);
            Assert.Equal(SaveError.Malformed, SaveTestRun.TryLoad(Data, SaveBytes.Rechecksum(bad)));
        }
        {
            // The state word is free: any value is a stream. It loads, and the loaded seat carries the forged state.
            byte[] bad = (byte[])good.Clone();
            BinaryPrimitives.WriteUInt64LittleEndian(bad.AsSpan(state), 0xDEADBEEFUL);
            Simulation s = SaveTestRun.Load(Data, SaveBytes.Rechecksum(bad));
            Assert.Equal(0xDEADBEEFUL, s.AiOf(0)!.Rng.State);
        }
    }

    /// <summary>12 int32 values at every offset of the AI section, checksum fixed: the load never throws (gate); a sample of the loads is ticked through two thinks and the throws counted (printed, not gated).</summary>
    [Fact]
    public void TheAiSection_AnInterestingInt32AtEveryOffset_NeverThrowsOnLoad()
    {
        byte[] good = SaveTestRun.Save(SmallAiSim(AiDifficulty.Normal));
        int from = SaveBytes.AiSection(good), to = good.Length - SaveFormat.ChecksumLength - 4;
        int[] interesting = { 0, 1, -1, 2, 15, 16, 17, 255, 256, int.MaxValue, int.MinValue, 1_000_000 };
        var seen = new Dictionary<SaveError, int>();
        int loaded = 0, ticked = 0, tickThrows = 0;
        var samples = new List<string>();
        for (int i = from; i + 4 <= to; i++)
            foreach (int v in interesting)
            {
                byte[] bad = (byte[])good.Clone();
                BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(i), v);
                bad = SaveBytes.Rechecksum(bad);
                SaveError e;
                try
                {
                    using var stream = new MemoryStream(bad, writable: false);
                    bool ok = Simulation.TryLoad(Data, stream, out Simulation? sim, out e);
                    Assert.True(ok == (sim != null) && ok == (e == SaveError.None), $"offset {i - from} value {v}: {e}");
                    // A sample of the loads ticked through two thinks: a count (the format promises the load only).
                    if (sim != null && ++loaded % 7 == 0)
                    {
                        ticked++;
                        try
                        {
                            for (int t = 0; t < 2 * sim.AiOf(0)!.ThinkTicks + 1; t++) sim.Tick();
                        }
                        catch (Exception ex)
                        {
                            if (tickThrows++ < 10) samples.Add($"offset {i - from} value {v}: {ex.GetType().Name} at {ex.TargetSite?.DeclaringType?.Name}.{ex.TargetSite?.Name}");
                        }
                    }
                }
                catch (Exception ex) when (ex is not Xunit.Sdk.XunitException)
                {
                    Assert.Fail($"offset {i - from} into AIST, value {v}: TryLoad threw {ex.GetType().Name}: {ex.Message}");
                    throw;
                }
                seen[e] = seen.GetValueOrDefault(e) + 1;
            }
        _out.WriteLine($"AIST is {to + 4 - from} bytes: " + string.Join(", ", seen.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}")));
        _out.WriteLine($"{ticked} of {loaded} loads ticked through two thinks: {tickThrows} threw");
        foreach (string sample in samples) _out.WriteLine("  " + sample);
    }

    // ---------- BUG-0960 item 1 ----------

    /// <summary>
    /// The bound is a lower bound: a real save of an almost empty world at large capacities (where the stores' per-slot
    /// bytes, not the entities, make the body) still loads. Small map so the grid term is small.
    /// </summary>
    [Theory]
    [InlineData(200_000, 1, 1)]
    [InlineData(1, 200_000, 1)]
    [InlineData(1, 1, 200_000)]
    [InlineData(50_000, 50_000, 50_000)]
    public void TheLengthBound_NeverRefusesARealSave_OfAnEmptyWorldAtLargeCapacities(int units, int buildings, int resources)
    {
        var config = new SimConfig(3, 1, units, 2)
        {
            Data = Data, ResourceCapacity = resources, BuildingCapacity = buildings, ZoneCapacity = 1, ProjectileCapacity = 0, StatsSampleCapacity = 0,
        };
        var sim = new Simulation(config, LocalMovementTests.Flat(8));
        sim.Tick();
        byte[] bytes = SaveTestRun.Save(sim);
        long bound = SaveHeader.MinBodyBytes(config, 8 * 8);
        _out.WriteLine($"{units}/{buildings}/{resources}: save {bytes.Length:N0} bytes, bound {bound:N0} ({(double)bytes.Length / bound:F2}x)");
        Assert.Equal(SaveError.None, SaveTestRun.TryLoad(Data, bytes));
    }

    /// <summary>The allocation-measuring test of this class: runs alone in <see cref="SerialCollection"/>.</summary>
    [Collection(SerialCollection.Name)]
    public class Serial
    {
        private readonly ITestOutputHelper _out;

        public Serial(ITestOutputHelper output) => _out = output;

        private static readonly string[] CapNames = { "unit", "command", "resource", "building", "projectile", "zone" };

        /// <summary>
        /// The length pre-check with every capacity at the format maximum (human and AI small saves) and with each one alone at
        /// the maximum. Printed: the code, the time and this thread's allocation. Gate: every forged header is refused, and (the
        /// brief's bound) the all-at-max one allocates under 1 MB.
        /// </summary>
        [Fact]
        public void AForgedHeader_EveryCapacityAtMax_AndEachOneAlone_AllocationAndTime_Printed()
        {
            var gateFailures = new List<string>();
            foreach (bool ai in new[] { false, true })
            {
                byte[] good = SaveBytes.Small(ai);
                SaveBytes.Offsets at = SaveBytes.HeaderOffsets(good);
                int caps = at.Seat0Controller + 2;
                for (int which = -1; which <= CapNames.Length; which++)
                {
                    byte[] bad = (byte[])good.Clone();
                    for (int k = 0; k < 6; k++)
                        if (which < 0 || which == k || which == CapNames.Length && k is 1 or 4 or 5) BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(caps + 4 * k), SaveFormat.MaxCapacity);
                    bad = SaveBytes.Rechecksum(bad);
                    SaveTestRun.TryLoad(Data, bad); // warm-up
                    GC.Collect();
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    SaveError e = SaveTestRun.TryLoad(Data, bad);
                    sw.Stop();
                    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                    string label = $"{(ai ? "AI" : "human")} small save, {(which < 0 ? "every capacity" : which == CapNames.Length ? "command, projectile and zone capacities" : CapNames[which] + " capacity alone")} at max";
                    _out.WriteLine($"{label}: {e} in {sw.Elapsed.TotalMilliseconds:F2} ms, {allocated:N0} bytes allocated");
                    // The command queue's capacity is not an array length the body holds, so a forged one loads (a sim with another
                    // queue size than the saved one's): reported in the bug, not gated here.
                    if (which != 1 && which != CapNames.Length) Assert.NotEqual(SaveError.None, e);
                    if (allocated >= 1024 * 1024) gateFailures.Add($"{label}: {allocated / (1024 * 1024):N0} MB in {sw.Elapsed.TotalMilliseconds:F0} ms");
                }
            }
            foreach (string f in gateFailures) _out.WriteLine("over 1 MB: " + f);
            Assert.DoesNotContain(gateFailures, f => f.Contains("every capacity"));
        }
    }

    // ---------- scenes ----------

    /// <summary>A generated map with <paramref name="seats"/>, the bases placed as the save scenes place them, and optional raiders (as <c>AiSaveScenes</c>).</summary>
    private static SaveScene Generated(string name, ulong seed, PlayerSeat[] seats, int size, int forests, int mines, int raiders) =>
        new(name, () =>
        {
            var map = MapGenParams.Default with { Width = size, Height = size, Forests = forests, GoldMines = mines, StartLocations = seats.Length };
            var sim = new Simulation(new SimConfig(seed, seats.Length, 2000, 4096) { Data = Data, Map = map, Seats = seats.ToImmutableArray() });
            SaveScenes.PlaceBases(sim, army: true);
            if (raiders > 0) Raid(sim, raiders);
            return sim;
        }, (_, _) => { });

    private static void Raid(Simulation sim, int raiders)
    {
        World w = sim.World;
        int players = w.Config.PlayerCount;
        float maxRadius = 0f;
        foreach (UnitDef d in w.Data.Units) maxRadius = Math.Max(maxRadius, d.Radius);
        var blocks = new Vector2[players][];
        var factions = new int[players];
        for (int p = 0; p < players; p++)
        {
            blocks[p] = Array.Empty<Vector2>();
            factions[p] = w.FactionOf(p);
        }
        StartBasePlan plan = StartBase.Plan(w.NavGrid, w.Data, w.Buildings, w.Resources.Alive, w.Resources.TypeId, w.Resources.Cell, factions, blocks, 5, maxRadius);
        for (int p = 0; p < players; p++)
        {
            int line = StartBase.UnitOfSlot(w.Data, factions[p], UnitSlot.Line);
            Vector2[] spots = plan.Workers[(p + 1) % players];
            for (int k = 0; k < raiders; k++) sim.Enqueue(Command.SpawnUnit(p, line, spots[k % spots.Length] + new Vector2(0.4f, 0.3f)));
        }
    }
}
