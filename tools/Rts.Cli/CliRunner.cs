using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Rts.Sim;
using Rts.Sim.Data;
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
        "usage: Rts.Cli run --seed <n> --units <n> [--ticks <n>] [--players 1|2] [--checkpoint <ticks>] [--record <path>] [--data <dir>]"
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
        string? error = ParseOptions(args, 1, new[] { "--seed", "--units", "--ticks", "--players", "--checkpoint", "--record", "--data" },
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
        o.TryGetValue("--record", out string? recordPath);

        GameData? data = LoadData(o, stderr);
        if (data == null) return ExitError;

        var sim = new Simulation(new SimConfig(seed, players, units, units) { Data = data });
        // Before any enqueue: a replay must see every command from tick 0 (docs/03 "Save/load and replays").
        ReplayRecorder? recorder = recordPath == null ? null
            : new ReplayRecorder(sim, checkpoint, tickCapacity: ticks, commandCapacity: 2 * units);

        int spawned = March.EnqueueSpawns(sim, units, players);
        int[] goals = March.GoalCells(sim.World.NavGrid, players);
        stdout.WriteLine($"seed {seed.ToString(Inv)} units {spawned.ToString(Inv)} players {players.ToString(Inv)} ticks {ticks.ToString(Inv)} checkpoint {checkpoint.ToString(Inv)}");

        var ms = new double[ticks];
        double toMs = 1000.0 / Stopwatch.Frequency;
        for (int t = 0; t < ticks; t++)
        {
            // Commands queued at tick 0 apply in the tick run at TickNumber 1, so the units exist from
            // TickNumber 2 on; the march is ordered then (Move needs their handles).
            if (sim.TickNumber == 2) March.EnqueueMoves(sim, goals);
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

        if (recorder != null && recordPath != null)
        {
            try
            {
                ReplayFormat.WriteFile(recorder.ToReplay(), recordPath);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                stderr.WriteLine($"error: cannot write replay '{recordPath}': {e.Message}");
                return ExitError;
            }
            stdout.WriteLine($"recorded {recordPath}");
        }
        return ExitOk;
    }

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
        stdout.WriteLine($"ok: {replay.Checkpoints.Length.ToString(Inv)} checkpoints matched over {result.TicksRun.ToString(Inv)} ticks");
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
        stderr.WriteLine($"error: data in '{dir}' did not load ({result.Errors.Count.ToString(Inv)} errors), first: {result.Errors[0]}");
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
