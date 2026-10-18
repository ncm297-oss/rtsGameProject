using System.Numerics;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Save;
using Rts.Sim.Tests.Save;
using Xunit.Abstractions;

namespace Rts.Sim.Tests.QA;

/// <summary>
/// QA (M6-S2): attacks on what a save restores "equivalently" rather than identically. Long runs past the developer's
/// T + 2,000; saves at every tick of a 50-tick window; a save at tick 0 and at the banner tick of a match that ends in a
/// fight; a chain of reloads (the run saved and reloaded every few hundred ticks); saving must not touch the saved sim;
/// view reads between ticks (CanPlace's SealCheck memo) that only the reference sees; and a reflection walk over both
/// whole object graphs, so a field no save writes shows up even where no hash or direct compare reads it.
/// </summary>
public class SaveRestoreQaTests
{
    private readonly ITestOutputHelper _out;

    public SaveRestoreQaTests(ITestOutputHelper output) => _out = output;

    // ---------- long runs (QA focus: T + 6,000) ----------

    [Theory]
    [InlineData("menu_seed2", 1000)]
    [InlineData("construction_and_queue", 137)]
    [InlineData("surround_brawl", 1000)]
    public void Scene_SavedAtT_FollowsTheUnsavedRun_ToTPlus6000(string name, int saveAt)
    {
        SaveTestRun.Result r = SaveTestRun.RoundTrip(SaveScenes.Named(name), new[] { saveAt }, after: 6000);
        _out.WriteLine($"{name}: save at {saveAt}: {r.Bytes[0]:N0} bytes, {r.TicksCompared} ticks compared");
        Assert.Equal(6000, r.TicksCompared);
    }

    // ---------- a window of consecutive save ticks ----------

    [Fact]
    public void TheMenuMatch_SavedAtEachOf50ConsecutiveTicks_FollowsTheUnsavedRunFromEach()
    {
        // Fifty consecutive ticks straddle every phase of the script's fixed patterns (20, 40, 50, 60, 150, 200 ticks).
        int[] ticks = Enumerable.Range(1480, 50).ToArray();
        SaveTestRun.Result r = SaveTestRun.RoundTrip(SaveScenes.Menu(3), ticks, after: 150);
        Assert.Equal(50 * 150, r.TicksCompared);
    }

    [Fact]
    public void TheMenuMatch_SavedAt50SeededTicksUpTo6000_FollowsTheUnsavedRunFromEach()
    {
        var rng = new Determinism.SimRng(7, 31);
        var ticks = new SortedSet<int>();
        while (ticks.Count < 50) ticks.Add(rng.NextInt(0, 6000));
        SaveTestRun.Result r = SaveTestRun.RoundTrip(SaveScenes.Menu(4), ticks.ToArray(), after: 100);
        Assert.Equal(50 * 100, r.TicksCompared);
    }

    [Fact]
    public void TheMenuMatch_SavedAtTickZero_WithTheBasesQueued_FollowsTheUnsavedRun()
    {
        SaveTestRun.Result r = SaveTestRun.RoundTrip(SaveScenes.Menu(5), new[] { 0 }, after: 2000);
        Assert.Equal(2000, r.TicksCompared);
    }

    // ---------- the banner tick ----------

    /// <summary>
    /// A fight to the end on a flat map: player 1 has one worker and a Tent, player 0 a Keep and four Heavy Infantry
    /// sent at them; the match ends when player 1 has nothing left. Saves at the tick the banner shows (MatchOver first
    /// true), the tick before, and the tick after; each load follows the reference for 300 ticks.
    /// </summary>
    [Fact]
    public void AMatchWonInAFight_SavedAtTheBannerTick_AndEitherSide_FollowsTheUnsavedRun()
    {
        int banner = FindBannerTick();
        _out.WriteLine($"banner tick: {banner}");
        Assert.True(banner > 10, "the scene should end in a fight, not at once");
        SaveTestRun.Result r = SaveTestRun.RoundTrip(BannerScene(), new[] { banner - 1, banner, banner + 1 }, after: 300);
        Assert.Equal(900, r.TicksCompared);
    }

    private static int FindBannerTick()
    {
        SaveScene scene = BannerScene();
        Simulation sim = scene.Build();
        var commands = new List<Command>();
        while (!sim.MatchOver)
        {
            Assert.True(sim.TickNumber < 6000, $"the banner scene never ended: p0 units {SaveScenes.UnitsOf(sim.World, 0).Count}, p1 units {SaveScenes.UnitsOf(sim.World, 1).Count}, p1 building {SaveScenes.FirstBuilding(sim.World, 1)}, defeated {sim.World.IsDefeated(0)}/{sim.World.IsDefeated(1)}");
            commands.Clear();
            scene.Script(sim, commands);
            foreach (Command c in commands) sim.Enqueue(c);
            sim.Tick();
        }
        return sim.TickNumber; // between ticks: the banner shows from here
    }

    private static SaveScene BannerScene()
    {
        GameData data = TestSim.Data;
        int infantry = data.FindUnit("malazan_heavy_infantry"), worker = Rts.Sim.ViewApi.StartBase.UnitOfSlot(data, 1, UnitSlot.Worker);
        return new SaveScene("banner", () =>
        {
            var sim = TestSim.Explored(new Simulation(new SimConfig(3, 2, 64, 256) { Data = data, Combat = true }, LocalMovementTests.Flat(64)));
            sim.Enqueue(Command.SpawnBuilding(0, data.FindBuilding("malazan_garrison_keep"), new Vector2(20f, 20f)));
            for (int k = 0; k < 4; k++) sim.Enqueue(Command.SpawnUnit(0, infantry, new Vector2(30f + 1.2f * k, 30f)));
            sim.Enqueue(Command.SpawnBuilding(1, data.FindBuilding("whirlwind_tent"), new Vector2(90f, 90f)));
            sim.Enqueue(Command.SpawnUnit(1, worker, new Vector2(80f, 80f)));
            return sim;
        }, (sim, output) =>
        {
            World w = sim.World;
            if (sim.TickNumber % 25 != 2) return;
            EntityHandle target = default;
            bool isBuilding = false;
            List<EntityHandle> enemies = SaveScenes.UnitsOf(w, 1);
            if (enemies.Count > 0) target = enemies[0];
            else if (SaveScenes.FirstBuilding(w, 1) is { } b && b != default)
            {
                target = b;
                isBuilding = true;
            }
            if (target == default) return;
            // Out of sight an Attack is refused, so walk there with an attack-move first; in sight, attack the target.
            Vector2 at = isBuilding ? w.NavGrid.CellCenter(w.Buildings.Cell[target.Index] % w.NavGrid.Width, w.Buildings.Cell[target.Index] / w.NavGrid.Width) : w.Units.Position[target.Index];
            foreach (EntityHandle h in SaveScenes.UnitsOf(w, 0, infantry))
                output.Add(Vector2.Distance(w.Units.Position[h.Index], at) > 8f ? Command.AttackMove(0, h, at) : Command.Attack(0, h, target, isBuilding));
        });
    }

    // ---------- a chain of reloads ----------

    /// <summary>
    /// The menu match where the "load" side is saved and reloaded every 250 ticks (so anything a load fails to carry is
    /// dropped again and again) beside one unsaved reference, to tick 6,000: the hash equal every tick.
    /// </summary>
    [Fact]
    public void TheMenuMatch_ReloadedEvery250Ticks_FollowsTheUnsavedRun_To6000()
    {
        SaveScene scene = SaveScenes.Menu(2);
        Simulation reference = scene.Build();
        GameData data = reference.World.Data;
        Simulation chained = SaveTestRun.Load(data, SaveTestRun.Save(reference));
        var commands = new List<Command>();
        int reloads = 0;
        while (reference.TickNumber < 6000)
        {
            commands.Clear();
            scene.Script(reference, commands);
            foreach (Command c in commands)
            {
                reference.Enqueue(c);
                chained.Enqueue(c);
            }
            if (reference.TickNumber % 250 == 125)
            {
                chained = SaveTestRun.Load(data, SaveTestRun.Save(chained));
                reloads++;
            }
            reference.Tick();
            chained.Tick();
            if (reference.StateHash() != chained.StateHash()) Assert.Fail($"hash differs at tick {reference.TickNumber} after {reloads} reloads");
        }
        SaveTestRun.AssertSameState(reference, chained, "after the chain");
    }

    // ---------- saving must not touch the saved sim ----------

    [Fact]
    public void SavingEveryTick_LeavesTheRunExactlyAsANeverSavedRun()
    {
        SaveScene scene = SaveScenes.Menu(1);
        Simulation saved = scene.Build(), plain = scene.Build();
        var commands = new List<Command>();
        byte[]? last = null;
        while (plain.TickNumber < 1500)
        {
            commands.Clear();
            scene.Script(plain, commands);
            foreach (Command c in commands)
            {
                plain.Enqueue(c);
                saved.Enqueue(c);
            }
            byte[] a = SaveTestRun.Save(saved), b = SaveTestRun.Save(saved);
            Assert.True(a.AsSpan().SequenceEqual(b), $"two saves in a row differ at tick {saved.TickNumber}");
            if (last != null) Assert.False(a.AsSpan().SequenceEqual(last), "a save after a tick should differ from the one before");
            last = a;
            plain.Tick();
            saved.Tick();
            Assert.True(plain.StateHash() == saved.StateHash(), $"saving moved the hash at tick {plain.TickNumber}");
        }
        SaveTestRun.AssertSameState(plain, saved, "saved every tick vs never");
    }

    // ---------- view reads only one side makes (the SealCheck memo) ----------

    /// <summary>
    /// The reference is asked CanPlace for every building type at a spread of spots between every tick (each "no" from the
    /// flood leaves SealCheck's memo set, which no save stores); the load is never asked. Both must stay hash-equal, and
    /// a placement command issued right after the reads (the memo's very footprint) must apply alike.
    /// </summary>
    [Fact]
    public void ViewPlacementReads_OnlyOnTheReference_DoNotSplitTheLoadFromIt()
    {
        SaveScene scene = SaveScenes.Menu(1);
        Simulation reference = scene.Build();
        GameData data = reference.World.Data;
        var commands = new List<Command>();
        Simulation? loaded = null;
        int asked = 0, refused = 0;
        while (reference.TickNumber < 2500)
        {
            commands.Clear();
            scene.Script(reference, commands);
            if (reference.TickNumber == 400) loaded = SaveTestRun.Load(data, SaveTestRun.Save(reference));
            if (loaded != null && reference.TickNumber % 10 == 0)
            {
                // Probe placements around player 0's home; then build at the last refused one (if any) for both.
                World w = reference.World;
                Vector2? home = MatchScript.HomeOf(w, 0);
                if (home is { } h && w.NavGrid.WorldToCell(h, out int hx, out int hy))
                {
                    int lastType = -1, lastCell = -1;
                    for (int b = 0; b < data.Buildings.Length; b++)
                        for (int dx = -14; dx <= 14; dx += 2)
                            for (int dy = -14; dy <= 14; dy += 2)
                            {
                                int x = hx + dx, y = hy + dy;
                                if (!w.NavGrid.InBounds(x, y)) continue;
                                int cell = y * w.NavGrid.Width + x;
                                asked++;
                                if (!w.CanPlace(0, b, cell, out _))
                                {
                                    refused++;
                                    (lastType, lastCell) = (b, cell);
                                }
                            }
                    // A worker ordered onto the last refused footprint: its check reads the memo the reads left (reference only).
                    List<EntityHandle> workers = SaveScenes.UnitsOf(w, 0, data.FindUnit("malazan_laborer"));
                    if (lastType >= 0 && workers.Count > 0)
                        commands.Add(Command.Build(0, workers[^1], lastType, w.NavGrid.CellCenter(lastCell % w.NavGrid.Width, lastCell / w.NavGrid.Width)));
                }
            }
            foreach (Command c in commands)
            {
                reference.Enqueue(c);
                loaded?.Enqueue(c);
            }
            reference.Tick();
            if (loaded == null) continue;
            loaded.Tick();
            if (reference.StateHash() != loaded.StateHash()) Assert.Fail($"hash differs at tick {reference.TickNumber}");
        }
        _out.WriteLine($"CanPlace asked {asked:N0} times on the reference only, {refused:N0} refused; reference floods {reference.World.Seal.Floods}, load floods {loaded!.World.Seal.Floods}");
        SaveTestRun.AssertSameState(reference, loaded, "after the reads");
    }

    // ---------- edges: a full queue, commands after the save, a full unit store, stream use ----------

    /// <summary>
    /// A save whose pending queue is full (capacity 16): the load holds all 16, refuses a 17th as the saved sim does, and
    /// commands queued after the save (into both, once the queue drained) get the same sequence numbers and apply alike.
    /// </summary>
    [Fact]
    public void AFullPendingQueue_AndCommandsQueuedAfterTheSave_MatchTheUnsavedSim()
    {
        GameData data = TestSim.Data;
        int infantry = data.FindUnit("malazan_heavy_infantry");
        var sim = TestSim.Explored(new Simulation(new SimConfig(9, 2, 32, 16) { Data = data }, LocalMovementTests.Flat(48)));
        for (int k = 0; k < 6; k++) sim.Enqueue(Command.SpawnUnit(k % 2, infantry, new Vector2(20f + 2f * k, 20f + 6f * (k % 2))));
        sim.Tick();
        sim.Tick();
        List<EntityHandle> mine = SaveScenes.UnitsOf(sim.World, 0);
        Assert.Equal(3, mine.Count);
        for (int k = 0; k < 16; k++) sim.Enqueue(Command.Move(0, mine[k % mine.Count], new Vector2(10f + k, 30f), queued: k >= 3));
        Assert.Equal(16, sim.PendingCommandCount);
        Simulation loaded = SaveTestRun.Load(data, SaveTestRun.Save(sim));
        SaveTestRun.AssertSameState(sim, loaded, "full queue at load");
        Assert.ThrowsAny<Exception>(() => sim.Enqueue(Command.Noop(1)));
        Assert.ThrowsAny<Exception>(() => loaded.Enqueue(Command.Noop(1)));
        SaveTestRun.AssertSameState(sim, loaded, "after both refused a 17th");
        for (int t = 0; t < 400; t++)
        {
            if (t % 7 == 3)
            {
                List<EntityHandle> theirs = SaveScenes.UnitsOf(sim.World, 1);
                foreach (EntityHandle h in theirs)
                {
                    Command c = Command.AttackMove(1, h, new Vector2(20f + t % 13, 20f));
                    sim.Enqueue(c);
                    loaded.Enqueue(c);
                }
            }
            sim.Tick();
            loaded.Tick();
            Assert.True(sim.StateHash() == loaded.StateHash(), $"hash differs at tick {sim.TickNumber}");
        }
        SaveTestRun.AssertSameState(sim, loaded, "after 400 ticks");
    }

    /// <summary>
    /// Every unit slot alive (capacity 24, no free slot), saved; then units die and spawns reuse the freed slots: the free
    /// list's LIFO order and the generations must come back exactly, so handles in later commands name the same units.
    /// </summary>
    [Fact]
    public void AFullUnitStore_SavedThenFreedAndRefilled_ReusesTheSameSlotsAndGenerations()
    {
        GameData data = TestSim.Data;
        int infantry = data.FindUnit("malazan_heavy_infantry"), raider = data.FindUnit("whirlwind_raider");
        var sim = TestSim.Explored(new Simulation(new SimConfig(13, 2, 24, 256) { Data = data }, LocalMovementTests.Flat(48)));
        for (int k = 0; k < 12; k++) sim.Enqueue(Command.SpawnUnit(0, infantry, new Vector2(30f + 1.1f * (k % 6), 30f + 1.1f * (k / 6))));
        for (int k = 0; k < 12; k++) sim.Enqueue(Command.SpawnUnit(1, raider, new Vector2(34f + 1.1f * (k % 6), 34f + 1.1f * (k / 6))));
        sim.Tick();
        sim.Tick();
        Assert.Equal(24, sim.World.Units.Count);
        sim.Enqueue(Command.SpawnUnit(0, infantry, new Vector2(10f, 10f))); // refused when it applies: no free slot
        Simulation loaded = SaveTestRun.Load(data, SaveTestRun.Save(sim));
        for (int t = 0; t < 1200; t++)
        {
            if (t % 50 == 25)
                for (int k = 0; k < 3; k++)
                {
                    Command c = Command.SpawnUnit(t % 100 == 25 ? 0 : 1, t % 100 == 25 ? infantry : raider, new Vector2(12f + k, 12f));
                    sim.Enqueue(c);
                    loaded.Enqueue(c);
                }
            sim.Tick();
            loaded.Tick();
            Assert.True(sim.StateHash() == loaded.StateHash(), $"hash differs at tick {sim.TickNumber}");
        }
        Assert.True(sim.World.Units.Generation.Max() > 1, "no slot was reused: the scene did not exercise the free list");
        SaveTestRun.AssertSameState(sim, loaded, "after the refills");
    }

    /// <summary>
    /// Stream use: a save written after other bytes in a stream, read from that offset; a non-seekable stream; and the same
    /// stream object read twice (the second read starts at its end: an empty read, Truncated, no throw).
    /// </summary>
    [Fact]
    public void Streams_AtAnOffset_NonSeekable_AndReadTwice_BehaveAsDocumented()
    {
        Simulation sim = SaveBytes.SmallSim();
        var ms = new MemoryStream();
        ms.Write("prefix"u8);
        Assert.True(sim.TrySave(ms, out SaveError e1));
        Assert.Equal(SaveError.None, e1);
        ms.Position = 6;
        Assert.True(Simulation.TryLoad(TestSim.Data, ms, out Simulation? a, out SaveError e2), e2.ToString());
        Assert.Equal(sim.StateHash(), a!.StateHash());
        Assert.False(Simulation.TryLoad(TestSim.Data, ms, out Simulation? again, out SaveError e3));
        Assert.Null(again);
        Assert.Equal(SaveError.Truncated, e3);

        byte[] bytes = SaveTestRun.Save(sim);
        using var forwardOnly = new ForwardOnlyStream(bytes);
        Assert.True(Simulation.TryLoad(TestSim.Data, forwardOnly, out Simulation? b, out SaveError e4), e4.ToString());
        Assert.Equal(sim.StateHash(), b!.StateHash());
    }

    /// <summary>A read-only, non-seekable stream that hands out at most 7 bytes a read (a network or pipe-like source).</summary>
    private sealed class ForwardOnlyStream : Stream
    {
        private readonly byte[] _data;
        private int _pos;

        public ForwardOnlyStream(byte[] data) => _data = data;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = Math.Min(Math.Min(count, 7), _data.Length - _pos);
            Array.Copy(_data, _pos, buffer, offset, n);
            _pos += n;
            return n;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    // ---------- the whole object graph ----------

    /// <summary>
    /// Reflection over both sims' whole graphs, at the load and after 1, 50 and 1,000 ticks on the same commands: the
    /// fields that differ. At the load only fields the save leaves out may differ, and those must be per-tick scratch
    /// (the list is printed); after ticks, any difference outside the documented scratch is a field a save drops.
    /// </summary>
    [Theory]
    [InlineData("menu_seed1", 1000)]
    [InlineData("casts_and_sandstorm", 137)]
    [InlineData("stealth_beside_detector", 137)]
    [InlineData("construction_and_queue", 1000)]
    [InlineData("surround_brawl", 137)]
    [InlineData("defeated_seat", 137)]
    public void TheWholeObjectGraph_OfALoad_MatchesTheSavedSim_OutsideScratch(string name, int saveAt)
    {
        SaveScene scene = SaveScenes.Named(name);
        Simulation reference = scene.Build();
        GameData data = reference.World.Data;
        var commands = new List<Command>();
        Simulation? loaded = null;
        var report = new List<string>();
        var unexpected = new SortedSet<string>(StringComparer.Ordinal);
        int[] checkpoints = { saveAt, saveAt + 1, saveAt + 50, saveAt + 1000 };
        while (reference.TickNumber <= checkpoints[^1])
        {
            commands.Clear();
            scene.Script(reference, commands);
            foreach (Command c in commands)
            {
                reference.Enqueue(c);
                loaded?.Enqueue(c);
            }
            if (reference.TickNumber == saveAt)
            {
                loaded = SaveTestRun.Load(data, SaveTestRun.Save(reference));
                SimConfig rc = reference.World.Config, lc = loaded.World.Config;
                for (int p = 0; p < rc.PlayerCount; p++) Assert.Equal(rc.SeatOf(p), lc.SeatOf(p));
                Assert.Equal(rc.HasAiSeat, lc.HasAiSeat);
                Assert.Equal(rc with { Seats = lc.Seats }, lc);
            }
            if (loaded != null && Array.IndexOf(checkpoints, reference.TickNumber) >= 0)
            {
                List<string> diffs = SaveDeepDiff.Diff(reference, loaded);
                report.Add($"--- {name} saved at {saveAt}, at tick {reference.TickNumber}: {diffs.Count} differing paths");
                report.AddRange(diffs);
                foreach (string d in diffs)
                    if (!IsScratch(d)) unexpected.Add($"tick {reference.TickNumber}: {d}");
            }
            if (reference.TickNumber == checkpoints[^1]) break;
            reference.Tick();
            loaded?.Tick();
        }
        foreach (string line in report) _out.WriteLine(line);
        Assert.True(unexpected.Count == 0, "fields a load does not restore:\n" + string.Join("\n", unexpected));
    }

    /// <summary>
    /// The fields documented "derived, not hashed" scratch (rewritten before read every tick) or a memo of an answer that
    /// follows from saved state, plus diagnostics counters. Matched by path prefix after "sim.World.".
    /// </summary>
    private static bool IsScratch(string diff)
    {
        foreach (string p in ScratchPaths)
            if (diff.StartsWith(p, StringComparison.Ordinal)) return true;
        return false;
    }

    private static readonly string[] ScratchPaths =
    {
        "sim.World.Hits", "sim.World.HitCount", "sim.World.CombatBuildings", "sim.World.CombatBuildingCount",
        "sim.World.CombatBuildingsOf", "sim.World.CombatEnemyExists", "sim.World.SiteWorkers", "sim.World.SpawnCacheCleared",
        "sim.World.SpawnPlateauFull", "sim.World.PushedUnits", "sim.World.ShoveStep", "sim.World.ShoveNeighbors",
        "sim.World.DetectNeighbors", "sim.World.ShovedGoals", "sim.World.AnchorQueue", "sim.World.AnchorLinked",
        "sim.World.BackedOffStops", "sim.World.WallNormals", "sim.World.WallLimits", "sim.World.HardWalls",
        "sim.World.DetourLo", "sim.World.DetourHi", "sim.World.DetourWall", "sim.World.PlugMembers", "sim.World.PlugAnswers",
        "sim.World.FlankBlock", "sim.World.FlankFirst", "sim.World.FlankNext", "sim.World.FlankQueue", "sim.World.FlankHeads",
        "sim.World.PlannedFlank", "sim.World.ChainMembers", "sim.World.Neighbors", "sim.World.PlannedStep",
        "sim.World.PlannedAction", "sim.World.PlannedRemaining", "sim.World.MoveOrder", "sim.World.FieldMisses",
        "sim.World.FieldRefreshes", "sim.World.Seal.",
        // The fog's per-level stamp boxes (read only under _levelUsed, which every update clears) and the spatial hash's
        // query buffer (filled before it is read by each query).
        "sim.World.Fog._box", "sim.World.Spatial._scratch",
        // The event arrays past their counts (the spans, up to the counts, are compared directly by AssertSameState).
        "sim.World._deaths", "sim.World._impacts", "sim.World._abilityEvents", "sim.World._defeatEvents",
        // A loaded config has explicit seats where the match had defaults: checked seat by seat in the test instead.
        "sim.World.Config.Seats",
    };
}
