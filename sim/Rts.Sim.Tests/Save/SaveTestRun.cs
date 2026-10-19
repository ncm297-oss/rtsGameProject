using System.Reflection;
using Rts.Sim.Ai;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Entities;
using Rts.Sim.Pathfinding;
using Rts.Sim.Save;
using Rts.Sim.Vision;

namespace Rts.Sim.Tests.Save;

/// <summary>
/// M6-S2 test helpers: save and load through a stream, the direct compares of the state outside the hash (criterion 3), and
/// the round-trip harness (criteria 2-4): a reference run that saves at the given ticks, each load ticked beside it on the
/// same commands with the hashes compared every tick.
/// </summary>
internal static class SaveTestRun
{
    /// <summary>The bytes <see cref="Simulation.TrySave"/> writes; fails the test on a refusal.</summary>
    internal static byte[] Save(Simulation sim)
    {
        using var stream = new MemoryStream();
        Assert.True(sim.TrySave(stream, out SaveError error), $"save refused: {error}");
        Assert.Equal(SaveError.None, error);
        return stream.ToArray();
    }

    /// <summary>The sim <see cref="Simulation.TryLoad"/> reads from <paramref name="bytes"/>; fails the test on a refusal.</summary>
    internal static Simulation Load(GameData data, byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        Assert.True(Simulation.TryLoad(data, stream, out Simulation? sim, out SaveError error), $"load refused: {error}");
        Assert.Equal(SaveError.None, error);
        return sim!;
    }

    /// <summary>The refusal <see cref="Simulation.TryLoad"/> gives <paramref name="bytes"/> (None when it loads); asserts the sim is null exactly when refused.</summary>
    internal static SaveError TryLoad(GameData data, byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        bool ok = Simulation.TryLoad(data, stream, out Simulation? sim, out SaveError error);
        Assert.Equal(ok, sim != null);
        Assert.Equal(ok, error == SaveError.None);
        return error;
    }

    /// <summary>
    /// Criterion 3's direct compares (and 8's counters): everything outside the hash that decides a later tick or the
    /// post-match screen, then the hash, then both sims' save bytes (every stored field at once).
    /// </summary>
    internal static void AssertSameState(Simulation expected, Simulation actual, string at)
    {
        World a = expected.World, b = actual.World;
        Assert.True(expected.TickNumber == actual.TickNumber, $"{at}: tick");
        Assert.True(expected.MatchOver == actual.MatchOver, $"{at}: match over");
        Assert.True(expected.PendingCommandCount == actual.PendingCommandCount, $"{at}: pending count");
        for (int i = 0; i < expected.PendingCommandCount; i++)
            Assert.True(SameCommand(expected.PendingAt(i), actual.PendingAt(i)), $"{at}: pending command {i}");
        for (int p = 0; p < a.Config.PlayerCount; p++)
        {
            Assert.True(expected.NextSequenceOf(p) == actual.NextSequenceOf(p), $"{at}: player {p}'s next sequence");
            Assert.True(expected.AiOpening(p) == actual.AiOpening(p), $"{at}: player {p}'s AI opening");
            AssertSameAi(expected.AiOf(p), actual.AiOf(p), $"{at}: player {p}'s AI");
        }

        Assert.True(a.SurroundClaim.AsSpan().SequenceEqual(b.SurroundClaim), $"{at}: SurroundClaim");
        Assert.True(a.SurroundHolder.AsSpan().SequenceEqual(b.SurroundHolder), $"{at}: SurroundHolder");
        Assert.True(a.Units.Flank.AsSpan().SequenceEqual(b.Units.Flank), $"{at}: UnitStore.Flank");
        Assert.True(a.Units.PrevPosition.AsSpan().SequenceEqual(b.Units.PrevPosition), $"{at}: PrevPosition");
        Assert.True(Bits(a.Units.PrevFacing).SequenceEqual(Bits(b.Units.PrevFacing)), $"{at}: PrevFacing");

        // The fog's memories: ghosts (Site included), node memory, the byte map.
        for (int p = 0; p < a.Config.PlayerCount; p++)
        {
            Assert.True(a.Fog.GhostCount(p) == b.Fog.GhostCount(p), $"{at}: player {p}'s ghost count");
            Assert.True(a.Fog.Ghosts(p).SequenceEqual(b.Fog.Ghosts(p)), $"{at}: player {p}'s ghosts");
            for (int s = 0; s < a.Fog.ResourceSlots; s++)
                Assert.True(a.Fog.NodeAt(p, s) == b.Fog.NodeAt(p, s), $"{at}: player {p}'s node memory {s}");
            Assert.True(a.Fog.Visibility(p).SequenceEqual(b.Fog.Visibility(p)), $"{at}: player {p}'s fog bytes");
            Assert.True(a.Fog.Version(p) == b.Fog.Version(p), $"{at}: player {p}'s fog version");
        }

        // The M6-S1 stats, every span, the samples and each tech's tick.
        Assert.True(a.GatheredGold.SequenceEqual(b.GatheredGold), $"{at}: GatheredGold");
        Assert.True(a.GatheredWood.SequenceEqual(b.GatheredWood), $"{at}: GatheredWood");
        Assert.True(a.UnitsTrained.SequenceEqual(b.UnitsTrained), $"{at}: UnitsTrained");
        Assert.True(a.UnitKills.SequenceEqual(b.UnitKills), $"{at}: UnitKills");
        Assert.True(a.BuildingKills.SequenceEqual(b.BuildingKills), $"{at}: BuildingKills");
        Assert.True(a.UnitLosses.SequenceEqual(b.UnitLosses), $"{at}: UnitLosses");
        Assert.True(a.BuildingLosses.SequenceEqual(b.BuildingLosses), $"{at}: BuildingLosses");
        Assert.True(a.BuildingsBuilt.SequenceEqual(b.BuildingsBuilt), $"{at}: BuildingsBuilt");
        for (int p = 0; p < a.Config.PlayerCount; p++)
        {
            Assert.True(a.ArmyValueSamples(p).SequenceEqual(b.ArmyValueSamples(p)), $"{at}: player {p}'s army samples");
            for (int t = 0; t < a.Data.Techs.Length; t++)
                Assert.True(a.TechResearchedTick(p, t) == b.TechResearchedTick(p, t), $"{at}: player {p}'s tech {t} tick");
        }

        // The flow-field cache: slot keys, versions and stamps, the clock, and every used field's contents.
        FlowFieldCache fa = a.FlowFields, fb = b.FlowFields;
        Assert.True(fa.Count == fb.Count && fa.Clock == fb.Clock, $"{at}: flow-field count / clock");
        for (int s = 0; s < fa.Count; s++)
        {
            FlowField x = fa.SlotField(s), y = fb.SlotField(s);
            Assert.True(x.RequestedCell == y.RequestedCell && x.TargetCell == y.TargetCell && x.Version == y.Version
                && x.BlockVersion == y.BlockVersion && fa.SlotLastUse(s) == fb.SlotLastUse(s), $"{at}: flow-field slot {s} keys");
            for (int c = 0; c < a.NavGrid.Width * a.NavGrid.Height; c++)
                if (x.DirectionAt(c) != y.DirectionAt(c) || BitConverter.SingleToInt32Bits(x.CostAt(c)) != BitConverter.SingleToInt32Bits(y.CostAt(c)))
                    Assert.Fail($"{at}: flow-field slot {s} cell {c}");
        }

        // The last tick's events (a view reads them between ticks).
        Assert.True(a.Deaths.SequenceEqual(b.Deaths), $"{at}: Deaths");
        Assert.True(a.Impacts.SequenceEqual(b.Impacts), $"{at}: Impacts");
        Assert.True(a.AbilityEvents.SequenceEqual(b.AbilityEvents), $"{at}: AbilityEvents");
        Assert.True(a.DefeatEvents.SequenceEqual(b.DefeatEvents), $"{at}: DefeatEvents");

        // The stealth detection mask, by its reads.
        for (int s = 0; s < a.Units.Capacity; s++)
            for (int p = 0; p < a.Config.PlayerCount; p++)
                Assert.True(a.IsDetectedBy(p, s) == b.IsDetectedBy(p, s), $"{at}: slot {s} detected by {p}");

        Assert.True(expected.StateHash() == actual.StateHash(), $"{at}: state hash");
        Assert.True(Save(expected).AsSpan().SequenceEqual(Save(actual)), $"{at}: save bytes differ");
    }

    /// <summary>
    /// M6-S3: two AI seats field for field, by reflection over the seat's own fields (the RNG, the counters, the census and
    /// scratch, the opening) and the whole <see cref="Blackboard"/>: every array and scalar. The view, the sink, the data and
    /// the test seam are references to the sim, not the seat's state, and are skipped.
    /// </summary>
    internal static void AssertSameAi(AiPlayer? expected, AiPlayer? actual, string at)
    {
        Assert.True((expected == null) == (actual == null), $"{at}: one seat has an AI, the other none");
        if (expected == null || actual == null) return;
        var diffs = new List<string>();
        foreach (FieldInfo f in typeof(AiPlayer).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (SkippedAiField(f.FieldType)) continue;
            foreach (string d in QA.SaveDeepDiff.Diff(f.GetValue(expected)!, f.GetValue(actual)!)) diffs.Add($"{f.Name}: {d}");
        }
        Assert.True(diffs.Count == 0, $"{at}: " + string.Join("; ", diffs));
    }

    /// <summary>The seat's references into the match, not its own state: the view, the sink, the data and its defs, the test seam.</summary>
    private static bool SkippedAiField(Type t) =>
        t == typeof(PlayerView) || t == typeof(ICommandSink) || t == typeof(GameData) || typeof(Delegate).IsAssignableFrom(t)
        || t == typeof(AiDef) || t == typeof(AiOpening) || t == typeof(AiDifficultyDef) || t == typeof(AiRulesDef);

    private static int[] Bits(float[] values) => values.Select(BitConverter.SingleToInt32Bits).ToArray();

    private static bool SameCommand(in Command x, in Command y) =>
        x.Kind == y.Kind && x.Player == y.Player && x.Tick == y.Tick && x.Sequence == y.Sequence && x.TypeId == y.TypeId
        && BitConverter.SingleToInt32Bits(x.Position.X) == BitConverter.SingleToInt32Bits(y.Position.X)
        && BitConverter.SingleToInt32Bits(x.Position.Y) == BitConverter.SingleToInt32Bits(y.Position.Y)
        && x.Unit == y.Unit && x.Flags == y.Flags && x.Target == y.Target && x.TargetIsBuilding == y.TargetIsBuilding;

    /// <summary>
    /// What a round trip saw: per save tick, the save's size, and how many ticks every load matched; M6-S3: per probe, the
    /// tick it first held and the run saved at (-1: it never held within the probe window).
    /// </summary>
    internal sealed record Result(int[] SaveTicks, long[] Bytes, int TicksCompared, int[] ProbedAt);

    /// <summary>
    /// The round trip (criteria 2-4, 8): runs <paramref name="scene"/>'s reference sim; between ticks it asks the script for
    /// this tick's commands and enqueues them into the reference and every live load; at each save tick (after those
    /// commands, so the pending queue is in the save) it saves, loads into a fresh sim, compares directly, checks save -> load
    /// -> save is byte-identical, and from then on ticks the load beside the reference, comparing the hash every tick and
    /// directly at T + 1 and at T + <paramref name="after"/>. <paramref name="observe"/> sees the reference after each tick
    /// (a scene's probe: did its subject happen). M6-S3: each of <paramref name="probes"/> (read between ticks, at the point a
    /// save is made) saves too, once, at the first tick before <paramref name="probeUntil"/> it holds, so a scene saves "mid-wave"
    /// or "during a defense" whenever its run gets there, never at a tick pinned to the shipped data.
    /// </summary>
    internal static Result RoundTrip(SaveScene scene, int[] saveTicks, int after = 2000, Action<Simulation>? observe = null,
        Func<Simulation, bool>[]? probes = null, int probeUntil = 0)
    {
        Simulation reference = scene.Build();
        GameData data = reference.World.Data;
        var live = new List<(Simulation Sim, int SavedAt)>();
        var bytes = new long[saveTicks.Length];
        probes ??= Array.Empty<Func<Simulation, bool>>();
        var probedAt = Enumerable.Repeat(-1, probes.Length).ToArray();
        int end = saveTicks.Max() + after, compared = 0;
        var commands = new List<Command>();
        while (reference.TickNumber < end || reference.TickNumber < probeUntil && probedAt.Contains(-1))
        {
            int t = reference.TickNumber;
            commands.Clear();
            scene.Script(reference, commands);
            foreach (Command c in commands)
            {
                reference.Enqueue(c);
                foreach ((Simulation sim, _) in live) sim.Enqueue(c);
            }
            int k = Array.IndexOf(saveTicks, t);
            bool save = k >= 0;
            for (int i = 0; i < probes.Length; i++)
                if (probedAt[i] < 0 && t < probeUntil && probes[i](reference))
                {
                    probedAt[i] = t;
                    save = true;
                }
            if (save && live.TrueForAll(l => l.SavedAt != t))
            {
                byte[] saved = Save(reference);
                if (k >= 0) bytes[k] = saved.Length;
                Simulation loaded = Load(data, saved);
                AssertSameState(reference, loaded, $"{scene.Name} saved at {t}, at load");
                Assert.True(saved.AsSpan().SequenceEqual(Save(loaded)), $"{scene.Name} saved at {t}: save -> load -> save differs");
                live.Add((loaded, t));
                end = Math.Max(end, t + after);
            }
            reference.Tick();
            observe?.Invoke(reference);
            ulong hash = reference.StateHash();
            for (int i = live.Count - 1; i >= 0; i--)
            {
                (Simulation sim, int savedAt) = live[i];
                sim.Tick();
                compared++;
                if (sim.StateHash() != hash) Assert.Fail($"{scene.Name} saved at {savedAt}: hash differs at tick {reference.TickNumber}");
                if (reference.TickNumber == savedAt + 1) AssertSameState(reference, sim, $"{scene.Name} saved at {savedAt}, at T + 1");
                if (reference.TickNumber == savedAt + after)
                {
                    AssertSameState(reference, sim, $"{scene.Name} saved at {savedAt}, at T + {after}");
                    live.RemoveAt(i);
                }
            }
        }
        return new Result(saveTicks, bytes, compared, probedAt);
    }
}

/// <summary>A round-trip scene: a fresh reference sim (setup commands may be queued) and the script that drives it between ticks.</summary>
internal sealed record SaveScene(string Name, Func<Simulation> Build, Action<Simulation, List<Command>> Script);
