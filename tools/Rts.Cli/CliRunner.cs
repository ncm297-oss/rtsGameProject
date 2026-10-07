using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Numerics;
using Rts.Sim;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.Replays;

namespace Rts.Cli;

/// <summary>The headless sim CLI (docs/03 "Debug tooling"): <c>run</c> a march scenario and print hashes and tick timings, or <c>play</c> a replay.</summary>
/// <remarks>
/// Exit codes: 0 success, 1 usage, data or file error, 2 a replay that fails to read or play back.
/// Expected failures print one line on stderr and never a stack trace. Wall-clock timing
/// (<see cref="Stopwatch"/>) lives here, around <see cref="Simulation.Tick"/>, never in the sim.
/// </remarks>
public static class CliRunner
{
    /// <summary>Exit code for success (every checkpoint matched, for <c>play</c>).</summary>
    public const int ExitOk = 0;

    /// <summary>Exit code for bad usage, unloadable data, or an unreadable / unwritable file.</summary>
    public const int ExitError = 1;

    /// <summary>Exit code for a replay that is malformed, refused, or fails a checkpoint.</summary>
    public const int ExitReplayFailed = 2;

    /// <summary>Largest <c>--units</c>: a sanity limit of this tool, far above what a start block holds on the design map.</summary>
    public const int MaxUnits = 100_000;

    /// <summary>The one-line usage text.</summary>
    public const string Usage =
        "usage: Rts.Cli run --seed <n> --units <n> [--ticks <n>] [--players 1|2] [--checkpoint <ticks>] [--forests <n>] [--mines <n>] [--workers <n>] [--record <path>] [--data <dir>]"
        + " | Rts.Cli play <path> [--data <dir>]";

    private const int DefaultTicks = 1500;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Runs the CLI with <paramref name="args"/>, writing results to <paramref name="stdout"/> and errors to <paramref name="stderr"/>; returns the exit code.</summary>
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length == 1 && args[0] is "help" or "--help" or "-h")
        {
            stdout.WriteLine(Usage);
            return ExitOk;
        }
        if (args.Length == 0) return UsageError(stderr, "no verb given");
        return args[0] switch
        {
            "run" => RunVerb(args, stdout, stderr),
            "play" => PlayVerb(args, stdout, stderr),
            _ => UsageError(stderr, $"unknown verb '{args[0]}'"),
        };
    }

    private static int RunVerb(string[] args, TextWriter stdout, TextWriter stderr)
    {
        string? error = ParseOptions(args, 1, new[] { "--seed", "--units", "--ticks", "--players", "--checkpoint", "--forests", "--mines", "--workers", "--record", "--data" },
            out Dictionary<string, string> o, out _);
        if (error != null) return UsageError(stderr, error);

        if (!o.TryGetValue("--seed", out string? seedText)) return UsageError(stderr, "--seed is required");
        if (!ulong.TryParse(seedText, NumberStyles.None, Inv, out ulong seed)) return UsageError(stderr, "--seed must be a whole number from 0 to 18446744073709551615");
        if (!o.ContainsKey("--units")) return UsageError(stderr, "--units is required");
        if ((error = IntOption(o, "--units", 0, 1, MaxUnits, out int units)) != null) return UsageError(stderr, error);
        if ((error = IntOption(o, "--ticks", DefaultTicks, 1, Replay.MaxTickCount, out int ticks)) != null) return UsageError(stderr, error);
        if ((error = IntOption(o, "--players", 1, 1, 2, out int players)) != null) return UsageError(stderr, error);
        if ((error = IntOption(o, "--checkpoint", ReplayRecorder.DefaultCheckpointInterval, 1, Replay.MaxTickCount, out int checkpoint)) != null)
            return UsageError(stderr, error);
        if ((error = IntOption(o, "--forests", 0, 0, MapGenParams.MaxResourceGroups, out int forests)) != null) return UsageError(stderr, error);
        if ((error = IntOption(o, "--mines", 0, 0, MapGenParams.MaxResourceGroups, out int mines)) != null) return UsageError(stderr, error);
        if ((error = IntOption(o, "--workers", 0, 0, Workers.MaxWorkers, out int workers)) != null) return UsageError(stderr, error);
        bool economy = o.ContainsKey("--workers");
        if (workers > 0 && forests == 0 && mines == 0) return UsageError(stderr, "--workers needs --forests or --mines above 0 (nothing to gather)");
        o.TryGetValue("--record", out string? recordPath);
        // Checked before the run, which can take minutes: a bad path found only at the end wastes it (BUG-0057).
        if (recordPath != null && (error = RecordPathError(recordPath)) != null)
        {
            stderr.WriteLine($"error: cannot write replay '{recordPath}': {error}");
            return ExitError;
        }

        GameData? data = LoadData(o, stderr);
        if (data == null) return ExitError;
        // Opened now and written at the end: a name the file system refuses (`a<b`) or a folder we may not write to
        // fails here, before the first tick, which a path check alone can't promise (BUG-0072).
        using FileStream? recordFile = recordPath == null ? null : OpenRecordFile(recordPath, stderr);
        if (recordPath != null && recordFile == null) return ExitError;

        var map = new MapGenParams { Forests = forests, GoldMines = mines };
        // Room for the march, the workers and (with --workers) one Town Hall command per player.
        int capacity = units + workers * players, commands = economy ? capacity + players : units;
        var sim = new Simulation(new SimConfig(seed, players, capacity, commands) { Data = data, Map = map });
        // Before any enqueue: a replay must see every command from tick 0 (docs/03 "Save/load and replays").
        ReplayRecorder? recorder = recordPath == null ? null
            : new ReplayRecorder(sim, checkpoint, tickCapacity: ticks, commandCapacity: capacity + commands);

        Vector2[][] blocks = March.Blocks(sim, units, players);
        int spawned = March.EnqueueSpawns(sim, blocks);
        int[] goals = March.GoalCells(sim.World.NavGrid, players);
        bool[] isWorker = economy ? Workers.EnqueueSetup(sim, workers, players, blocks) : Array.Empty<bool>();
        ResourcePlacement placed = sim.World.ResourcePlacement;
        stdout.WriteLine($"seed {seed.ToString(Inv)} units {spawned.ToString(Inv)} players {players.ToString(Inv)} ticks {ticks.ToString(Inv)} checkpoint {checkpoint.ToString(Inv)}"
            + $" forests {placed.Forests.ToString(Inv)} trees {placed.Trees.ToString(Inv)} mines {placed.Mines.ToString(Inv)}"
            + (economy ? $" workers {workers.ToString(Inv)}" : ""));

        var ms = new double[ticks];
        double toMs = 1000.0 / Stopwatch.Frequency;
        for (int t = 0; t < ticks; t++)
        {
            // Commands queued at tick 0 apply in the tick run at TickNumber 1, so the units exist from
            // TickNumber 2 on; the march is ordered then (Move needs their handles).
            if (sim.TickNumber == 2)
            {
                March.EnqueueMoves(sim, goals, isWorker);
                if (economy) Workers.EnqueueGathers(sim, isWorker);
            }
            long start = Stopwatch.GetTimestamp();
            sim.Tick();
            ms[t] = (Stopwatch.GetTimestamp() - start) * toMs;
            if (sim.TickNumber % checkpoint == 0) stdout.WriteLine(HashLine(sim.TickNumber, sim.StateHash()));
        }

        double sum = 0;
        foreach (double m in ms) sum += m;
        double[] sorted = (double[])ms.Clone();
        Array.Sort(sorted);
        double p99 = sorted[(int)Math.Ceiling(0.99 * ticks) - 1];
        stdout.WriteLine(string.Format(Inv, "ticks {0} avg {1:F3} ms p99 {2:F3} ms worst {3:F3} ms", ticks, sum / ticks, p99, sorted[^1]));

        if (recorder != null && recordFile != null)
        {
            try
            {
                recordFile.Write(ReplayFormat.Write(recorder.ToReplay()));
                recordFile.Flush();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                stderr.WriteLine($"error: cannot write replay '{recordPath}': {e.Message}");
                return ExitError;
            }
            stdout.WriteLine($"recorded {recordPath}");
        }
        // Last, so scripts can take the final lines as the totals.
        if (economy)
        {
            for (int p = 0; p < players; p++)
                stdout.WriteLine($"player {p.ToString(Inv)} gold {sim.World.Gold[p].ToString(Inv)} wood {sim.World.Wood[p].ToString(Inv)}"
                    + $" pop {Pop(sim.World.HalfPop[p])}/{Pop(sim.World.HalfPopCap[p])}");
        }
        return ExitOk;
    }

    /// <summary>A half-pop count as population: 10 → "5", 3 → "1.5".</summary>
    private static string Pop(int halfPop) =>
        (halfPop / 2).ToString(Inv) + (halfPop % 2 != 0 ? ".5" : "");

    private static int PlayVerb(string[] args, TextWriter stdout, TextWriter stderr)
    {
        string? error = ParseOptions(args, 1, new[] { "--data" }, out Dictionary<string, string> o, out List<string> positional);
        if (error != null) return UsageError(stderr, error);
        if (positional.Count != 1) return UsageError(stderr, positional.Count == 0 ? "play needs a replay path" : "play takes one replay path");
        string path = positional[0];

        ReplayError readError = ReplayFormat.TryReadFile(path, out Replay? replay);
        if (readError == ReplayError.Unreadable)
        {
            stderr.WriteLine($"error: cannot read replay '{path}'");
            return ExitError;
        }
        GameData? data = LoadData(o, stderr);
        if (data == null) return ExitError;
        if (readError != ReplayError.None || replay == null)
        {
            stderr.WriteLine($"replay failed: {readError} reading '{path}'");
            return ExitReplayFailed;
        }

        ReplayResult result = ReplayPlayer.Run(replay, data);
        if (result.Error == ReplayError.CheckpointMismatch)
        {
            stderr.WriteLine($"replay failed: {result.Error} at tick {result.Tick.ToString(Inv)}: expected {Hex(result.ExpectedHash)} actual {Hex(result.ActualHash)}");
            return ExitReplayFailed;
        }
        if (!result.Ok)
        {
            stderr.WriteLine($"replay failed: {result.Error} at tick {result.Tick.ToString(Inv)}");
            return ExitReplayFailed;
        }
        foreach (ReplayCheckpoint k in replay.Checkpoints) stdout.WriteLine(HashLine(k.Tick, k.Hash));
        // A replay shorter than one checkpoint interval is valid but proves nothing: say so (BUG-0057).
        stdout.WriteLine(replay.Checkpoints.Length == 0
            ? $"ok: 0 checkpoints (nothing compared) over {result.TicksRun.ToString(Inv)} ticks"
            : $"ok: {replay.Checkpoints.Length.ToString(Inv)} checkpoints matched over {result.TicksRun.ToString(Inv)} ticks");
        return ExitOk;
    }

    /// <summary>A checkpoint line: <c>tick &lt;n&gt; hash &lt;16 hex&gt;</c>, the same for <c>run</c> and <c>play</c>.</summary>
    private static string HashLine(int tick, ulong hash) => $"tick {tick.ToString(Inv)} hash {Hex(hash)}";

    private static string Hex(ulong value) => value.ToString("X16", Inv);

    private static int UsageError(TextWriter stderr, string message)
    {
        stderr.WriteLine($"error: {message}. {Usage}");
        return ExitError;
    }

    /// <summary>Splits <c>--name value</c> pairs from positional arguments; returns an error text or null.</summary>
    private static string? ParseOptions(string[] args, int start, string[] allowed, out Dictionary<string, string> options, out List<string> positional)
    {
        options = new Dictionary<string, string>(StringComparer.Ordinal);
        positional = new List<string>();
        for (int i = start; i < args.Length; i++)
        {
            string a = args[i];
            if (!a.StartsWith("--", StringComparison.Ordinal))
            {
                positional.Add(a);
                continue;
            }
            if (Array.IndexOf(allowed, a) < 0) return $"unknown option '{a}' for {args[0]}";
            if (options.ContainsKey(a)) return $"{a} given twice";
            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal)) return $"{a} needs a value";
            options.Add(a, args[++i]);
        }
        if (args[0] == "run" && positional.Count > 0) return $"unexpected argument '{positional[0]}'";
        return null;
    }

    private static string? IntOption(Dictionary<string, string> o, string name, int fallback, int min, int max, out int value)
    {
        value = fallback;
        if (!o.TryGetValue(name, out string? text)) return null;
        if (int.TryParse(text, NumberStyles.AllowLeadingSign, Inv, out value) && value >= min && value <= max) return null;
        return $"{name} must be a whole number from {min.ToString(Inv)} to {max.ToString(Inv)}";
    }

    /// <summary>Creates (or truncates) the replay file for writing; null after printing one error line.</summary>
    private static FileStream? OpenRecordFile(string path, TextWriter stderr)
    {
        try
        {
            return new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            stderr.WriteLine($"error: cannot write replay '{path}': {e.Message}");
            return null;
        }
    }

    /// <summary>Why a replay can't be written to <paramref name="path"/> (an invalid path, a missing folder, or a folder in its place), or null if it looks writable.</summary>
    private static string? RecordPathError(string path)
    {
        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return e.Message;
        }
        if (Directory.Exists(full)) return "it is a directory";
        string? dir = Path.GetDirectoryName(full);
        if (dir != null && !Directory.Exists(dir)) return $"directory '{dir}' does not exist";
        return null;
    }

    /// <summary>Loads <c>--data</c>, or <c>game/data</c> next to the nearest <c>RtsGame.sln</c> above the working or program directory; null after printing one error line.</summary>
    private static GameData? LoadData(Dictionary<string, string> o, TextWriter stderr)
    {
        if (!o.TryGetValue("--data", out string? dir))
        {
            dir = FindDefaultDataDir();
            if (dir == null)
            {
                stderr.WriteLine("error: cannot find game/data (no RtsGame.sln above the working or program directory); pass --data <dir>");
                return null;
            }
        }
        DataLoadResult result = DataLoader.LoadAll(dir);
        if (result.Ok) return result.Data;
        int count = result.Errors.Count;
        stderr.WriteLine($"error: data in '{dir}' did not load ({count.ToString(Inv)} {(count == 1 ? "error" : "errors")}), first: {result.Errors[0]}");
        return null;
    }

    private static string? FindDefaultDataDir()
    {
        foreach (string from in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (DirectoryInfo? d = new DirectoryInfo(from); d != null; d = d.Parent)
            {
                if (File.Exists(Path.Combine(d.FullName, "RtsGame.sln"))) return Path.Combine(d.FullName, "game", "data");
            }
        }
        return null;
    }
}
