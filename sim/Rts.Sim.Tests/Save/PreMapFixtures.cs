using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;
using Rts.Sim.Commands;
using Rts.Sim.Data;
using Rts.Sim.Replays;

namespace Rts.Sim.Tests.Save;

/// <summary>
/// M6-S7: the oldest-format rule's proof. A generated-map match must write the save (format 4) and the recording (formats
/// 6 / 7 / 8) byte for byte as the build before the map file did. M7-1: every save is format 10 now; with its new parts
/// taken out (<see cref="WithoutRegen"/>) it is still those bytes. These scenes were saved and recorded once on the diff base
/// (ab54ddb, before the map id existed) with <see cref="WriteFlag"/> set; the replays are kept as text under
/// <c>Replays/premap/</c>, the saves as their FNV-1a 64 digest and length (<see cref="SaveDigests"/>), since a save is binary.
/// Never rewrite them: a fixture that needs regenerating means a generated match no longer writes the old bytes.
/// Exception: <c>replay_ai_ffa_standard.replay</c> (seat 1 a Hard AI) was re-recorded when Hard's play changed on purpose,
/// in M5-4c slice 2 (947a273, the Hard knobs) and in D27 (Hard's <c>gatherBonus</c> 0.25 -> 0.4); its header lines are the
/// ab54ddb ones byte for byte (the format is the point), only its commands and checkpoints moved.
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

    /// <summary>The save fixtures: scene name, the normalized save's length and FNV-1a 64 digest (recorded on ab54ddb; the saves were never re-recorded).</summary>
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

    // The formats before M7-1, by number: SaveFormat's constants all name format 10 since then.
    internal const int Format4 = 4, Format5 = 5, Format6 = 6, Format7 = 7, Format8 = 8, Format9 = 9;

    /// <summary>
    /// M7-1: a format-10 save as the build before M7-1 wrote the same match: the unit store's regeneration part
    /// (<c>REGN</c>, its tag, the last damage ticks, the accumulators, the regenerating counts and their total, right before
    /// <c>LEDR</c>) taken out, a generated map's empty map id taken out of the header, and the version the old build chose
    /// (4 / 5 without an AI seat, 8 / 9 with one, on a generated / hand-made map) with the total length fixed; the checksum
    /// left stale (<see cref="Normalize(byte[])"/> zeroes it, <c>SaveBytes.Rechecksum</c> recomputes it).
    /// </summary>
    internal static byte[] WithoutRegen(byte[] save)
    {
        Assert.Equal(Rts.Sim.Save.SaveFormat.CurrentVersion, Int32At(save, Rts.Sim.Save.SaveFormat.MagicLength));
        Header h = ReadHeader(save);
        int regenBytes = 4 + 2 * (4 + 4 * h.UnitCapacity) + (4 + h.UnitCapacity) + 4;
        int regen = -1;
        for (int at = save.AsSpan().IndexOf("REGN"u8); at >= 0;)
        {
            if (save.AsSpan(at + regenBytes, 4).SequenceEqual("LEDR"u8))
            {
                regen = at;
                break;
            }
            int next = save.AsSpan(at + 1).IndexOf("REGN"u8);
            at = next < 0 ? -1 : at + 1 + next;
        }
        Assert.True(regen > 0, "no REGN part before LEDR");
        bool mapFile = h.MapId.Length > 0;
        int idBytes = mapFile ? 0 : 4; // a generated map's "" is its length alone
        byte[] old = new byte[save.Length - regenBytes - idBytes];
        save.AsSpan(0, h.MapIdAt).CopyTo(old);
        save.AsSpan(h.MapIdAt + idBytes, regen - h.MapIdAt - idBytes).CopyTo(old.AsSpan(h.MapIdAt));
        save.AsSpan(regen + regenBytes).CopyTo(old.AsSpan(regen - idBytes));
        int version = h.AiSeat ? (mapFile ? Format9 : Format8) : (mapFile ? Format5 : Format4);
        BinaryPrimitives.WriteInt32LittleEndian(old.AsSpan(Rts.Sim.Save.SaveFormat.MagicLength), version);
        BinaryPrimitives.WriteInt64LittleEndian(old.AsSpan(Rts.Sim.Save.SaveFormat.LengthOffset), old.Length);
        return old;
    }

    /// <summary>
    /// M7-1: a format-10 save with an AI seat without its AI seats' memories (<c>CTRM</c> and <c>HUNT</c> cut, the version left
    /// at 10, the checksum stale): what a load must refuse as <see cref="Rts.Sim.Save.SaveError.Malformed"/>.
    /// </summary>
    internal static byte[] WithoutAiMemories(byte[] save)
    {
        int v = Rts.Sim.Save.SaveFormat.CurrentVersion;
        return WithoutLastSection(WithoutLastSection(save, "CTRM"u8, "HUNT"u8, v), "HUNT"u8, "AIST"u8, v);
    }

    /// <summary>M7-1: a format-10 save with an AI seat without its <c>CTRM</c> section, the version left at 10, the checksum stale.</summary>
    internal static byte[] WithoutCountersAt10(byte[] save) =>
        WithoutLastSection(save, "CTRM"u8, "HUNT"u8, Rts.Sim.Save.SaveFormat.CurrentVersion);

    /// <summary>The header fields <see cref="WithoutRegen"/> needs: the unit capacity, whether a seat is an AI, the map id and its offset.</summary>
    private readonly record struct Header(int UnitCapacity, bool AiSeat, string MapId, int MapIdAt);

    /// <summary>A format-10 header read in the format table's order (docs/03 "Save format").</summary>
    private static Header ReadHeader(byte[] save)
    {
        int at = Rts.Sim.Save.SaveFormat.FixedPrefix;
        at += 4 + Int32At(save, at); // sim version
        at += 8 + 8; // data hash, seed
        int players = Int32At(save, at);
        at += 4;
        bool ai = false;
        for (int p = 0; p < players; p++)
        {
            at += 4 + Int32At(save, at); // faction id
            ai |= save[at] == (byte)SeatController.Ai;
            at += 2; // controller, difficulty
        }
        int units = Int32At(save, at);
        at += 4 * 6 + 4 * 26 + 1 + 4 * 4 + 4 + players; // capacities, map, combat, cast floats, stats ints, tick, teams
        at += 4 + Int32At(save, at); // preset
        string mapId = Encoding.ASCII.GetString(save, at + 4, Int32At(save, at));
        return new Header(units, ai, mapId, at);
    }

    /// <summary>
    /// M5-4d: a format-6 save (a generated map with an AI seat) as format 4 wrote it, or a format-7 one (a hand-made map) as
    /// format 5: the <c>HUNT</c> section (its tag, the seat count and each seat's hunt memory, the last section before
    /// <c>END!</c>) taken out, the version and the total length fixed. M5-9a: a format-8 / 9 save first loses its <c>CTRM</c>
    /// section (<see cref="WithoutCounters"/>), so this is still "as format 4 / 5 wrote it"; M7-1: a format-10 save first
    /// becomes the format 8 / 9 one (<see cref="WithoutRegen"/>). The checksum is left stale:
    /// <see cref="Normalize(byte[])"/> zeroes it, <c>SaveBytes.Rechecksum</c> recomputes it.
    /// </summary>
    internal static byte[] WithoutHunt(byte[] save)
    {
        int version = Int32At(save, Rts.Sim.Save.SaveFormat.MagicLength);
        if (version == Rts.Sim.Save.SaveFormat.CurrentVersion)
        {
            save = WithoutRegen(save);
            version = Int32At(save, Rts.Sim.Save.SaveFormat.MagicLength);
        }
        if (version is Format8 or Format9)
        {
            save = WithoutCounters(save);
            version = Int32At(save, Rts.Sim.Save.SaveFormat.MagicLength);
        }
        Assert.True(version is Format6 or Format7, $"format {version} has no HUNT section");
        return WithoutLastSection(save, "HUNT"u8, "AIST"u8, version == Format7 ? Format5 : Format4);
    }

    /// <summary>
    /// M5-9a: a format-8 save (a generated map with an AI seat) as format 6 wrote it, or a format-9 one (a hand-made map) as
    /// format 7: the <c>CTRM</c> section (its tag, the seat count and each seat's counter memory, the last section before
    /// <c>END!</c>) taken out, the version and the total length fixed; the checksum left stale. M7-1: a format-10 save first
    /// becomes the format 8 / 9 one (<see cref="WithoutRegen"/>).
    /// </summary>
    internal static byte[] WithoutCounters(byte[] save)
    {
        int version = Int32At(save, Rts.Sim.Save.SaveFormat.MagicLength);
        if (version == Rts.Sim.Save.SaveFormat.CurrentVersion)
        {
            save = WithoutRegen(save);
            version = Int32At(save, Rts.Sim.Save.SaveFormat.MagicLength);
        }
        Assert.True(version is Format8 or Format9, $"format {version} has no CTRM section");
        return WithoutLastSection(save, "CTRM"u8, "HUNT"u8, version == Format9 ? Format7 : Format6);
    }

    /// <summary>
    /// M5-9a: the offset of the <c>CTRM</c> tag of a format-8 / 9 save: the section is the last before <c>END!</c> and its size
    /// follows from the AI seat count (tag, count, per seat its index, two 7-int arrays with their lengths and, round 2, the
    /// starved stamp; M5-9b, the far expansion's <see cref="FarBytes"/>).
    /// </summary>
    internal static int CountersAt(byte[] save, int aiSeats) =>
        save.Length - Rts.Sim.Save.SaveFormat.ChecksumLength - 4 - (8 + aiSeats * (4 + 2 * (4 + 4 * Rts.Sim.Ai.Blackboard.SlotCount) + 4 + FarBytes));

    /// <summary>
    /// M5-9b: a seat's far-expansion bytes in <c>CTRM</c>, after its starved stamp: the phase (1), the looked-at bits, the start
    /// and its look point (4 each), the Move sent (1), the leg's worker and generation (4 each), the pending Build (29), the
    /// site's type and cell, the no-site wait and the expansions (4 each).
    /// </summary>
    internal const int FarBytes = 1 + 4 + 4 + 4 + 1 + 4 + 4 + 29 + 4 + 4 + 4 + 4;

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
