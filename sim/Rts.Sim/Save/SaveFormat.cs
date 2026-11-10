using System;

namespace Rts.Sim.Save;

/// <summary>
/// The binary save format's constants (M6-S2, docs/03 "Save/load and replays"): magic, version, the section tags in file
/// order, the size limits, and the FNV-1a 64 checksum. Little-endian, fixed field order; no field is optional.
/// </summary>
public static class SaveFormat
{
    /// <summary>
    /// The format version this build writes and reads: 10 since M7-1, for every save (the unit store gained the regeneration
    /// part, <c>REGN</c>, which every save holds). Format 10 is format 9's layout (M5-9a) with three changes: the header always
    /// holds the map id after the preset (<c>""</c> for a generated map), the <c>HUNT</c> and <c>CTRM</c> sections are written
    /// exactly when the match seats an AI (the header's seats say so), and the unit store ends with <c>REGN</c>. Every older
    /// format is refused <see cref="SaveError.FormatVersionMismatch"/> (their unit stores lack <c>REGN</c>).
    /// Before: 9 since M5-9a (7 plus the AI seats' counter memory, <c>CTRM</c>), 7 since M5-4d (5 plus <c>HUNT</c>), 5 since
    /// M6-S7 (the map id), 4 since M6-S6 (the preset), 3 since M6-S4 (team bytes), 2 since M6-S3 (the AI seats' section).
    /// </summary>
    public const int CurrentVersion = 10;

    /// <summary>The version a match with an AI seat on a generated map is saved in: <see cref="CurrentVersion"/> since M7-1 (8 in M5-9a). Kept for the callers that name it.</summary>
    public const int HuntMaplessVersion = CurrentVersion;

    /// <summary>The version a match on a hand-made map with no AI seat is saved in: <see cref="CurrentVersion"/> since M7-1 (5 before). Kept for the callers that name it.</summary>
    public const int MapVersion = CurrentVersion;

    /// <summary>The version a match on a generated map with no AI seat is saved in: <see cref="CurrentVersion"/> since M7-1 (4 before). Kept for the callers that name it.</summary>
    public const int MaplessVersion = CurrentVersion;

    /// <summary>The oldest format version this build reads: <see cref="CurrentVersion"/> since M7-1 (3 before).</summary>
    internal const int OldestReadVersion = CurrentVersion;

    /// <summary>Bytes of the magic at offset 0.</summary>
    public const int MagicLength = 8;

    /// <summary>The file's first 8 bytes: <c>RTSSAVE</c> and a zero byte.</summary>
    internal static ReadOnlySpan<byte> Magic => "RTSSAVE\0"u8;

    /// <summary>Bytes before the header's variable part: magic, version (int32), total length (int64).</summary>
    internal const int FixedPrefix = MagicLength + 4 + 8;

    /// <summary>Offset of the total-length field (int64, the whole file with its checksum).</summary>
    internal const int LengthOffset = MagicLength + 4;

    /// <summary>Bytes of the trailing checksum.</summary>
    internal const int ChecksumLength = 8;

    /// <summary>Largest save read (1 GiB): a bound on the format, so a bad length can't ask for more memory.</summary>
    internal const long MaxBytes = 1L << 30;

    /// <summary>Most players a save may hold (as a replay).</summary>
    internal const int MaxPlayers = 16;

    /// <summary>Largest capacity a save may declare (as a replay).</summary>
    internal const int MaxCapacity = 1_000_000;

    /// <summary>
    /// BUG-0990: largest command capacity a save may declare. The queue's capacity is configuration, not a length the body
    /// holds (the <c>CMDS</c> section is a count and the commands), so the length pre-check can't bound it; this does, so a
    /// forged one allocates at most about 4 MB. BUG-1040: it is <see cref="SimConfig.MaxCommandCapacity"/>, the bound
    /// <see cref="SimConfig.Validate"/> puts on every sim, so every save a sim writes is under it.
    /// </summary>
    internal const int MaxCommandCapacity = SimConfig.MaxCommandCapacity;

    /// <summary>Longest string field (sim version, faction id), in UTF-8 bytes.</summary>
    internal const int MaxString = 256;

    // Section tags, four ASCII letters each, in file order (docs/03 has the table).
    internal const uint TagHeightmap = 0x50414D48; // "HMAP"
    internal const uint TagWorld = 0x444C5257;     // "WRLD"
    internal const uint TagRng = 0x53474E52;       // "RNGS"
    internal const uint TagGrid = 0x44495247;      // "GRID"
    internal const uint TagResources = 0x43535352; // "RSSC"
    internal const uint TagBuildings = 0x47444C42; // "BLDG"
    internal const uint TagUnits = 0x54494E55;     // "UNIT"
    internal const uint TagRegen = 0x4E474552;     // "REGN" (M7-1, format 10: the unit store's last part)
    internal const uint TagLedger = 0x5244454C;    // "LEDR"
    internal const uint TagStats = 0x54415453;     // "STAT"
    internal const uint TagTechs = 0x48434554;     // "TECH"
    internal const uint TagProjectiles = 0x4A4F5250; // "PROJ"
    internal const uint TagZones = 0x454E4F5A;     // "ZONE"
    internal const uint TagFog = 0x20474F46;       // "FOG "
    internal const uint TagSpatial = 0x54415053;   // "SPAT"
    internal const uint TagFlowFields = 0x574F4C46; // "FLOW"
    internal const uint TagSurround = 0x52525553;  // "SURR"
    internal const uint TagStealth = 0x48544C53;   // "SLTH"
    internal const uint TagEvents = 0x544E5645;    // "EVNT"
    internal const uint TagCommands = 0x53444D43;  // "CMDS"
    internal const uint TagAi = 0x54534941;        // "AIST" (M6-S3, format 2)
    internal const uint TagHunt = 0x544E5548;      // "HUNT" (M5-4d, formats 6 to 10, with an AI seat)
    internal const uint TagCounters = 0x4D525443;  // "CTRM" (M5-9a, formats 8 to 10, with an AI seat)
    internal const uint TagEnd = 0x21444E45;       // "END!"

    /// <summary>FNV-1a 64 over raw bytes, the replay file's checksum.</summary>
    internal static ulong Checksum(ReadOnlySpan<byte> bytes)
    {
        const ulong offsetBasis = 14695981039346656037UL, prime = 1099511628211UL;
        ulong h = offsetBasis;
        foreach (byte b in bytes) h = unchecked((h ^ b) * prime);
        return h;
    }
}
