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

    /// <summary>Exit code for a <c>match</c> that has no result (no side won) by <c>--ticks</c> (M5-5).</summary>
    public const int ExitNoResult = 3;

    /// <summary>Largest <c>--units</c>: a sanity limit of this tool, far above what a start block holds on the design map.</summary>
    public const int MaxUnits = 100_000;

    /// <summary>The one-line usage text.</summary>
    public const string Usage =
        "usage: Rts.Cli run --seed <n> --units <n> [--ticks <n>] [--players 1|2] [--checkpoint <ticks>] [--forests <n>] [--mines <n>] [--workers <n>] [--record <path>] [--data <dir>]"
        + " | Rts.Cli play <path> [--data <dir>]"
        + " | Rts.Cli mirror-map <in.json> <out.json> --symmetry point|quad [--data <dir>]"
        + " | Rts.Cli match --map <id>|generated --seed <n> [--players 2..4] [--teams <t,t,...>] [--ai easy|normal|hard[,...]] [--ticks <n>] [--record <path>] [--data <dir>]";

    /// <summary>The <c>match</c> verb's default <c>--ticks</c>: one hour at 20 Hz.</summary>
    public const int DefaultMatchTicks = 72_000;

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
            "mirror-map" => MirrorMapVerb(args, stdout, stderr),
            "match" => MatchVerb(args, stdout, stderr),
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
        // BUG-1040: the queue is capped at the config's bound; every spawn is enqueued in one tick, but a player's block on
        // the 128 x 128 map (16,384 cells) holds far fewer than 65,536 units, so the cap never drops a command --units asks for.
        int capacity = units + workers * players;
        int commands = Math.Min(economy ? capacity + players : units, SimConfig.MaxCommandCapacity);
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
                    + $" pop {Pop(sim.World.HalfPop[p])}/{Pop(sim.World.HalfPopCap[p])} age {sim.World.Age(p).ToString(Inv)}");
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

    /// <summary>
    /// M6-S8: <c>mirror-map</c> (<see cref="MirrorMap"/>): reads the source half or quarter, writes the whole map file to the
    /// out path (the only file it writes), then checks it as the loader checks a <c>maps/*.json</c>
    /// (<see cref="DataLoader.CheckMapFile"/>, against the data's rules and resources) and prints every error: exit 1 on any.
    /// </summary>
    private static int MirrorMapVerb(string[] args, TextWriter stdout, TextWriter stderr)
    {
        string? error = ParseOptions(args, 1, new[] { "--symmetry", "--data" }, out Dictionary<string, string> o, out List<string> positional);
        if (error != null) return UsageError(stderr, error);
        if (positional.Count != 2) return UsageError(stderr, "mirror-map takes a source path and an out path");
        if (!o.TryGetValue("--symmetry", out string? symmetry)) return UsageError(stderr, "--symmetry is required");
        if (symmetry is not ("point" or "quad")) return UsageError(stderr, "--symmetry must be point or quad");
        string? dir = o.TryGetValue("--data", out string? d) ? d : FindDefaultDataDir();
        if (dir == null)
        {
            stderr.WriteLine("error: cannot find game/data (no RtsGame.sln above the working or program directory); pass --data <dir>");
            return ExitError;
        }
        (string inPath, string outPath) = (positional[0], positional[1]);
        string source;
        try
        {
            source = File.ReadAllText(inPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            stderr.WriteLine($"error: cannot read '{inPath}': {e.Message}");
            return ExitError;
        }
        if (!GoldMineFootprint(dir, out int mineWidth, out int mineHeight, out string why))
        {
            stderr.WriteLine($"error: {why}");
            return ExitError;
        }
        string? text = MirrorMap.Mirror(source, symmetry == "quad", mineWidth, mineHeight, out string mirrorError);
        if (text == null)
        {
            stderr.WriteLine($"error: '{inPath}': {mirrorError}");
            return ExitError;
        }
        try
        {
            File.WriteAllBytes(outPath, System.Text.Encoding.UTF8.GetBytes(text));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            stderr.WriteLine($"error: cannot write '{outPath}': {e.Message}");
            return ExitError;
        }
        IReadOnlyList<DataError> errors = DataLoader.CheckMapFile(dir, outPath);
        foreach (DataError e in errors) stderr.WriteLine($"error: {e}");
        if (errors.Count > 0)
        {
            stderr.WriteLine($"error: '{outPath}' was written but does not load ({errors.Count.ToString(Inv)} {(errors.Count == 1 ? "error" : "errors")})");
            return ExitError;
        }
        stdout.WriteLine($"wrote {outPath} ({symmetry} symmetry): it loads");
        return ExitOk;
    }

    /// <summary>The first gold resource type's footprint in <c>common/resources.json</c> under <paramref name="dir"/> (the loader's mine type), which moves a mirrored mine's anchor.</summary>
    private static bool GoldMineFootprint(string dir, out int width, out int height, out string error)
    {
        width = height = 0;
        error = "";
        string path = Path.Combine(dir, "common", "resources.json");
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            foreach (System.Text.Json.JsonElement r in doc.RootElement.GetProperty("resources").EnumerateArray())
            {
                if (r.GetProperty("resource").GetString() != "gold") continue;
                System.Text.Json.JsonElement f = r.GetProperty("footprint");
                width = f.GetProperty("width").GetInt32();
                height = f.GetProperty("height").GetInt32();
                return true;
            }
            error = $"'{path}' has no gold resource type";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException
            or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            error = $"cannot read the gold mine footprint from '{path}': {e.Message}";
        }
        return false;
    }

    /// <summary>
    /// M5-5: <c>match</c> (<see cref="AiMatch"/>): AI seats only (factions alternate Malazan / Whirlwind from player 0, starts
    /// in player order) play on a map to their result or <c>--ticks</c>. Exit 0 on a result (a winner or a draw),
    /// <see cref="ExitNoResult"/> when no side won by then, 1 on bad usage or data.
    /// </summary>
    private static int MatchVerb(string[] args, TextWriter stdout, TextWriter stderr)
    {
        string? error = ParseOptions(args, 1, new[] { "--map", "--seed", "--players", "--teams", "--ai", "--ticks", "--record", "--data" },
            out Dictionary<string, string> o, out _);
        if (error != null) return UsageError(stderr, error);
        if (!o.TryGetValue("--map", out string? map)) return UsageError(stderr, "--map is required");
        if (!o.TryGetValue("--seed", out string? seedText)) return UsageError(stderr, "--seed is required");
        if (!ulong.TryParse(seedText, NumberStyles.None, Inv, out ulong seed)) return UsageError(stderr, "--seed must be a whole number from 0 to 18446744073709551615");
        if ((error = IntOption(o, "--players", 2, 2, 4, out int players)) != null) return UsageError(stderr, error);
        if ((error = IntOption(o, "--ticks", DefaultMatchTicks, 1, Replay.MaxTickCount, out int ticks)) != null) return UsageError(stderr, error);
        var teams = new int[players];
        if (o.TryGetValue("--teams", out string? teamText))
        {
            string[] parts = teamText.Split(',');
            if (parts.Length != players) return UsageError(stderr, $"--teams needs {players.ToString(Inv)} comma-separated teams, one per player");
            for (int p = 0; p < players; p++)
                if (!int.TryParse(parts[p], NumberStyles.None, Inv, out teams[p]) || teams[p] > PlayerSeat.MaxTeam)
                    return UsageError(stderr, $"--teams: each team must be a whole number from 0 (none) to {PlayerSeat.MaxTeam.ToString(Inv)}");
            bool twoSides = false;
            for (int p = 1; p < players; p++) twoSides |= teams[p] == 0 || teams[p] != teams[0];
            if (!twoSides) return UsageError(stderr, "--teams puts every player on one team: a match needs two sides");
        }
        var difficulty = new AiDifficulty[players];
        string[] ai = (o.TryGetValue("--ai", out string? aiText) ? aiText : "normal").Split(',');
        if (ai.Length != 1 && ai.Length != players) return UsageError(stderr, $"--ai takes one difficulty or {players.ToString(Inv)}, one per player");
        for (int p = 0; p < players; p++)
        {
            switch (ai[ai.Length == 1 ? 0 : p])
            {
                case "easy": difficulty[p] = AiDifficulty.Easy; break;
                case "normal": difficulty[p] = AiDifficulty.Normal; break;
                case "hard": difficulty[p] = AiDifficulty.Hard; break;
                default: return UsageError(stderr, "--ai must be easy, normal or hard (or a comma-separated list of them)");
            }
        }
        o.TryGetValue("--record", out string? recordPath);
        if (recordPath != null && (error = RecordPathError(recordPath)) != null)
        {
            stderr.WriteLine($"error: cannot write replay '{recordPath}': {error}");
            return ExitError;
        }
        GameData? data = LoadData(o, stderr);
        if (data == null) return ExitError;
        if (map != AiMatch.Generated)
        {
            int index = data.FindMap(map);
            if (index < 0) return UsageError(stderr, $"--map '{map}' is no map in the data (nor generated)");
            if (players > data.Maps[index].Players)
                return UsageError(stderr, $"--map '{map}' has {data.Maps[index].Players.ToString(Inv)} starts, fewer than --players {players.ToString(Inv)}");
        }
        if (data.Factions.Length < 2) return UsageError(stderr, "the data needs two factions for a match");
        using FileStream? recordFile = recordPath == null ? null : OpenRecordFile(recordPath, stderr);
        if (recordPath != null && recordFile == null) return ExitError;

        var seats = new PlayerSeat[players];
        for (int p = 0; p < players; p++) seats[p] = new PlayerSeat(p % 2, SeatController.Ai, difficulty[p], teams[p]);
        Simulation? sim = AiMatch.Build(data, map, seed, seats, recordPath != null, out ReplayRecorder? recorder, out string why);
        if (sim == null)
        {
            stderr.WriteLine($"error: {why}");
            return ExitError;
        }
        var header = new List<string>();
        for (int p = 0; p < players; p++)
            header.Add($"p{p.ToString(Inv)} {data.Factions[seats[p].Faction].Key} {ai[ai.Length == 1 ? 0 : p]} team {teams[p].ToString(Inv)}");
        stdout.WriteLine($"match {map} seed {seed.ToString(Inv)} players {players.ToString(Inv)} ticks {ticks.ToString(Inv)}: {string.Join(", ", header)}");
        bool result = AiMatch.Play(sim, ticks, stdout);
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
        return result ? ExitOk : ExitNoResult;
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
        if (args[0] is "run" or "match" && positional.Count > 0) return $"unexpected argument '{positional[0]}'";
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
