using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Replays;

namespace Rts.Sim.Tests.Save;

/// <summary>
/// M6-S7: the oldest-format rule's proof. A generated-map match must write the save (format 4) and the recording (formats
/// 6 / 7 / 8) byte for byte as the build before the map file did. These scenes were saved and recorded once on the diff base
/// (ab54ddb, before the map id existed) with <see cref="WriteFlag"/> set; the replays are kept as text under
/// <c>Replays/premap/</c>, the saves as their FNV-1a 64 digest and length (<see cref="SaveDigests"/>), since a save is binary.
/// Never rewrite them: a fixture that needs regenerating means a generated match no longer writes the old bytes.
/// <para>
/// The data hash (and so every checksum over it) is normalized away before comparing: it follows from the data, which the
/// data track edits in parallel (text only) and which this task extends with <c>maps/</c>; every other byte is compared.
/// </para>
/// </summary>
internal static class PreMapFixtures
{
    /// <summary>The env var that (re)writes the fixtures: used once, on the diff base. Do not set it on a later build.</summary>
    internal const string WriteFlag = "RTS_WRITE_PREMAP_FIXTURES";

    /// <summary>Ticks each fixture scene runs before it is saved or its recording taken.</summary>
    internal const int Ticks = 400;

    /// <summary>The folder of the replay fixtures.</summary>
    internal static string Folder => Path.Combine(TestDataDir.RepoRoot(), "sim", "Rts.Sim.Tests", "Replays", "premap");

    /// <summary>The save fixtures: scene name, the normalized save's length and FNV-1a 64 digest (recorded on ab54ddb).</summary>
    internal static readonly (string Name, int Length, ulong Digest)[] SaveDigests =
    {
        ("save_menu_seed1", 2017388, 0x31C655C77B98CAF1UL),
        ("save_ai_seed2", 1230475, 0x260D2E7D579BB2FFUL),
    };

    /// <summary>The replay fixture names (each <c>Replays/premap/&lt;name&gt;.replay</c>) and the format each must be.</summary>
    internal static readonly (string Name, int Format)[] ReplayNames =
    {
        ("replay_ffa_standard", 6),
        ("replay_teams_standard", 7),
        ("replay_ffa_high", 8),
        ("replay_ai_ffa_standard", 6),
    };

    /// <summary>The sim of save scene <paramref name="name"/>, ticked to <see cref="Ticks"/>.</summary>
    internal static Simulation SaveScene(string name)
    {
        switch (name)
        {
            case "save_menu_seed1":
            {
                SaveScene scene = SaveScenes.Menu(1);
                Simulation sim = scene.Build();
                Run(sim, scene.Script, Ticks);
                return sim;
            }
            case "save_ai_seed2":
            {
                PlayerSeat[] seats = { new(0, SeatController.Ai, AiDifficulty.Normal), new(1, SeatController.Ai, AiDifficulty.Normal) };
                Simulation sim = Rts.Sim.Tests.AiScenes.Generated(seats, 2, record: false, out _);
                for (int t = 0; t < Ticks; t++) sim.Tick();
                return sim;
            }
            default:
                throw new ArgumentException(name);
        }
    }

    /// <summary>The recording of replay scene <paramref name="name"/> over <see cref="Ticks"/> ticks.</summary>
    internal static Replay ReplayScene(string name)
    {
        if (name == "replay_ai_ffa_standard")
        {
            PlayerSeat[] seats = { new(0, SeatController.Ai, AiDifficulty.Normal), new(1, SeatController.Ai, AiDifficulty.Hard) };
            Simulation ai = Rts.Sim.Tests.AiScenes.Generated(seats, 3, record: true, out ReplayRecorder? rec);
            for (int t = 0; t < 4 * Ticks; t++) ai.Tick();
            return rec!.ToReplay();
        }
        bool teams = name == "replay_teams_standard";
        string preset = name == "replay_ffa_high" ? "high" : StartingResourcesDef.StandardId;
        var script = new MatchScript(2);
        var seatList = ImmutableArray.Create(new PlayerSeat(0, SeatController.Human, AiDifficulty.Normal, teams ? 1 : 0),
            new PlayerSeat(1, SeatController.Human, AiDifficulty.Normal, teams ? 2 : 0));
        var map = Map.MapGenParams.Default with { Forests = 12, GoldMines = 8 };
        var sim = new Simulation(new SimConfig(5, 2, 2000, 4096) { Data = TestSim.Data, Map = map, Seats = seatList, StartingResources = preset });
        var recorder = new ReplayRecorder(sim, checkpointInterval: 100, commandCapacity: 8192);
        SaveScenes.PlaceBases(sim, army: true);
        Run(sim, script.Run, Ticks);
        return recorder.ToReplay();
    }

    private static void Run(Simulation sim, Action<Simulation, List<Command>> script, int ticks)
    {
        var output = new List<Command>();
        while (sim.TickNumber < ticks)
        {
            output.Clear();
            script(sim, output);
            foreach (Command c in output) sim.Enqueue(c);
            sim.Tick();
        }
    }

    /// <summary>A save's bytes with the data hash and the trailing checksum zeroed (see the type's summary).</summary>
    internal static byte[] Normalize(byte[] save)
    {
        byte[] copy = (byte[])save.Clone();
        SaveBytes.Offsets at = SaveBytes.HeaderOffsets(copy);
        copy.AsSpan(at.DataHash, 8).Clear();
        copy.AsSpan(copy.Length - Rts.Sim.Save.SaveFormat.ChecksumLength).Clear();
        return copy;
    }

    /// <summary>
    /// M5-4d: a format-6 save (a generated map with an AI seat) as format 4 wrote it, or a format-7 one (a hand-made map) as
    /// format 5: the <c>HUNT</c> section (its tag, the seat count and each seat's hunt memory, the last section before
    /// <c>END!</c>) taken out, the version and the total length fixed. M5-9a: a format-8 / 9 save first loses its <c>CTRM</c>
    /// section (<see cref="WithoutCounters"/>), so this is still "as format 4 / 5 wrote it". The checksum is left stale:
    /// <see cref="Normalize(byte[])"/> zeroes it, <c>SaveBytes.Rechecksum</c> recomputes it.
    /// </summary>
    internal static byte[] WithoutHunt(byte[] save)
    {
        int version = Int32At(save, Rts.Sim.Save.SaveFormat.MagicLength);
        if (version is Rts.Sim.Save.SaveFormat.HuntMaplessVersion or Rts.Sim.Save.SaveFormat.CurrentVersion)
        {
            save = WithoutCounters(save);
            version = Int32At(save, Rts.Sim.Save.SaveFormat.MagicLength);
        }
        Assert.True(version is Rts.Sim.Save.SaveFormat.HuntOnlyMaplessVersion or Rts.Sim.Save.SaveFormat.HuntOnlyMapVersion, $"format {version} has no HUNT section");
        return WithoutLastSection(save, "HUNT"u8, "AIST"u8,
            version == Rts.Sim.Save.SaveFormat.HuntOnlyMapVersion ? Rts.Sim.Save.SaveFormat.MapVersion : Rts.Sim.Save.SaveFormat.MaplessVersion);
    }

    /// <summary>
    /// M5-9a: a format-8 save (a generated map with an AI seat) as format 6 wrote it, or a format-9 one (a hand-made map) as
    /// format 7: the <c>CTRM</c> section (its tag, the seat count and each seat's counter memory, the last section before
    /// <c>END!</c>) taken out, the version and the total length fixed; the checksum left stale.
    /// </summary>
    internal static byte[] WithoutCounters(byte[] save)
    {
        int version = Int32At(save, Rts.Sim.Save.SaveFormat.MagicLength);
        Assert.True(version is Rts.Sim.Save.SaveFormat.HuntMaplessVersion or Rts.Sim.Save.SaveFormat.CurrentVersion, $"format {version} has no CTRM section");
        return WithoutLastSection(save, "CTRM"u8, "HUNT"u8,
            version == Rts.Sim.Save.SaveFormat.CurrentVersion ? Rts.Sim.Save.SaveFormat.HuntOnlyMapVersion : Rts.Sim.Save.SaveFormat.HuntOnlyMaplessVersion);
    }

    /// <summary>
    /// M5-9a: the offset of the <c>CTRM</c> tag of a format-8 / 9 save: the section is the last before <c>END!</c> and its size
    /// follows from the AI seat count (tag, count, per seat its index and two 7-int arrays with their lengths).
    /// </summary>
    internal static int CountersAt(byte[] save, int aiSeats) =>
        save.Length - Rts.Sim.Save.SaveFormat.ChecksumLength - 4 - (8 + aiSeats * (4 + 2 * (4 + 4 * Rts.Sim.Ai.Blackboard.SlotCount)));

    /// <summary>The save with its last section before <c>END!</c> (tagged <paramref name="tag"/>, after a <paramref name="after"/> section) cut out, relabelled <paramref name="version"/>.</summary>
    private static byte[] WithoutLastSection(byte[] save, ReadOnlySpan<byte> tag, ReadOnlySpan<byte> after, int version)
    {
        int end = save.Length - Rts.Sim.Save.SaveFormat.ChecksumLength - 4;
        Assert.Equal("END!"u8.ToArray(), save[end..(end + 4)]);
        int at = save.AsSpan(0, end).LastIndexOf(tag);
        Assert.True(at > save.AsSpan().LastIndexOf(after), "the section is not after the one it follows");
        byte[] old = new byte[save.Length - (end - at)];
        save.AsSpan(0, at).CopyTo(old);
        save.AsSpan(end).CopyTo(old.AsSpan(at));
        BinaryPrimitives.WriteInt32LittleEndian(old.AsSpan(Rts.Sim.Save.SaveFormat.MagicLength), version);
        BinaryPrimitives.WriteInt64LittleEndian(old.AsSpan(Rts.Sim.Save.SaveFormat.LengthOffset), old.Length);
        return old;
    }

    /// <summary>A replay file's text with the data-hash value zeroed and the checksum line dropped (see the type's summary).</summary>
    internal static string Normalize(string replayText)
    {
        var sb = new StringBuilder();
        foreach (string line in replayText.Split('\n'))
        {
            if (line.Length == 0 || line.StartsWith("checksum ", StringComparison.Ordinal)) continue;
            sb.Append(line.StartsWith("data-hash ", StringComparison.Ordinal) ? "data-hash 0000000000000000" : line).Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>FNV-1a 64 over <paramref name="bytes"/> (the save's own checksum function).</summary>
    internal static ulong Digest(byte[] bytes) => Rts.Sim.Save.SaveFormat.Checksum(bytes);

    /// <summary>The path of replay fixture <paramref name="name"/>.</summary>
    internal static string ReplayPath(string name) => Path.Combine(Folder, name + ReplayFormat.FileExtension);

    /// <summary>The bytes as LF ASCII text.</summary>
    internal static string Text(byte[] bytes) => Encoding.ASCII.GetString(bytes);

    /// <summary>Little-endian helper for the tests: the int32 at <paramref name="at"/>.</summary>
    internal static int Int32At(byte[] bytes, int at) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at));
}
