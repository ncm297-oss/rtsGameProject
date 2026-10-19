namespace Rts.Game;

/// <summary>
/// A save / load text from <c>ui.json</c> <c>save</c> (M6-V14): the pause menu's result line and note, and one text per
/// <see cref="Rts.Sim.Save.SaveError"/> (<see cref="UiText.SaveErrorText"/>); the key is the member's snake_case name
/// (<see cref="UiText.Key{T}"/>).
/// </summary>
public enum SaveText
{
    /// <summary>The pause menu's line after a save, a format naming <c>{0}</c> (the match clock the save was made at).</summary>
    Saved,
    /// <summary>The pause menu's note in a loaded match: it is not recorded (no replay), the recorder attaches at tick 0 only.</summary>
    NotRecorded,
    /// <summary>The pause menu's line when the save file could not be written (a full disk, a locked folder).</summary>
    WriteFailed,
    /// <summary>The Load Game note when a save file could not be opened (gone, locked).</summary>
    ReadFailed,
    /// <summary><c>SaveError.BadMagic</c>: not a save file.</summary>
    BadMagic,
    /// <summary><c>SaveError.FormatVersionMismatch</c>: a save of another game version.</summary>
    VersionMismatch,
    /// <summary><c>SaveError.DataMismatch</c>: made with other game data.</summary>
    DataMismatch,
    /// <summary><c>SaveError.InvalidHeader</c>: a header value out of range.</summary>
    InvalidHeader,
    /// <summary><c>SaveError.AiSeatsUnsupported</c>: a seat is played by the AI and this build can't save or load that.</summary>
    AiSeatsUnsupported,
    /// <summary><c>SaveError.Truncated</c>: the file is cut short.</summary>
    Truncated,
    /// <summary><c>SaveError.ChecksumMismatch</c>: a changed byte.</summary>
    ChecksumMismatch,
    /// <summary><c>SaveError.Malformed</c>: the file is damaged inside.</summary>
    Malformed,
}
