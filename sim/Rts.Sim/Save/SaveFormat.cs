using System;

namespace Rts.Sim.Save;

/// <summary>
/// The binary save format's constants (M6-S2, docs/03 "Save/load and replays"): magic, version, the section tags in file
/// order, the size limits, and the FNV-1a 64 checksum. Little-endian, fixed field order; no field is optional.
/// </summary>
public static class SaveFormat
{
    /// <summary>The format version this build writes and the only one it reads (2 since M6-S3: the AI seats' section).</summary>
    public const int CurrentVersion = 2;

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
    /// BUG-0990: largest command capacity a save may declare, 16x the game's 4,096. The queue's capacity is configuration,
    /// not a length the body holds (the <c>CMDS</c> section is a count and the commands), so the length pre-check can't
    /// bound it; this does, so a forged one allocates at most about 4 MB.
    /// </summary>
    internal const int MaxCommandCapacity = 65_536;

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
