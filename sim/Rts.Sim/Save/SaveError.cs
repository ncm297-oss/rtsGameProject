namespace Rts.Sim.Save;

/// <summary>
/// Why <see cref="Simulation.TrySave"/> or <see cref="Simulation.TryLoad"/> refused (M6-S2, docs/03 "Save/load and replays").
/// The codes are frozen (standing rule 36): later slices add sections, never change or renumber a code. The text a player
/// sees comes from data (the view's <c>save.*</c> keys), never from these names.
/// </summary>
public enum SaveError
{
    /// <summary>No error.</summary>
    None = 0,

    /// <summary>The bytes don't start with the save magic: not a save file.</summary>
    BadMagic = 1,

    /// <summary>A save of another format version (<see cref="SaveFormat.CurrentVersion"/> is the only one read).</summary>
    FormatVersionMismatch = 2,

    /// <summary>Made with other game data (<c>GameData.ContentHash</c> differs): a balance edit or a rename refuses it.</summary>
    DataMismatch = 3,

    /// <summary>A header value is out of range: player count, a capacity, a map parameter, a seat (an unknown faction id), the tick.</summary>
    InvalidHeader = 4,

    /// <summary>
    /// A seat is played by the AI. Slice 1 saves and loads only matches whose seats are all human; slice 2 stores the AI
    /// state and lifts this.
    /// </summary>
    AiSeatsUnsupported = 5,

    /// <summary>The stream ends before the length the header declares (or before a whole header).</summary>
    Truncated = 6,

    /// <summary>The trailing FNV-1a 64 checksum doesn't match the bytes before it: a changed byte.</summary>
    ChecksumMismatch = 7,

    /// <summary>The checksum holds but the body doesn't parse: a section out of order, a wrong array length, a value out of range.</summary>
    Malformed = 8,
}
