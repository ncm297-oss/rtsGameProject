namespace Rts.Game;

/// <summary>A Load Game screen or replay playback text from <c>ui.json</c> <c>load</c> (M6-V9); the key is the member's snake_case name (<see cref="UiText.Key{T}"/>).</summary>
public enum LoadText
{
    /// <summary>The screen's heading.</summary>
    Title,
    /// <summary>The date column's heading.</summary>
    Date,
    /// <summary>The seed column's heading.</summary>
    Seed,
    /// <summary>The players column's heading.</summary>
    Seats,
    /// <summary>The length column's heading.</summary>
    Length,
    /// <summary>A date cell, a format naming <c>{0}</c> (the recording's local start time, a .NET date format such as <c>{0:yyyy-MM-dd HH:mm}</c>).</summary>
    DateItem,
    /// <summary>One seat in the players cell, a format naming <c>{0}</c> (the faction's name) and <c>{1}</c> (You, Human or the AI's difficulty).</summary>
    Seat,
    /// <summary>What goes between two seats in the players cell.</summary>
    SeatSeparator,
    /// <summary>The button that plays the picked replay.</summary>
    Watch,
    /// <summary>The button that deletes the picked replay's file.</summary>
    Delete,
    /// <summary>The button back to the main menu.</summary>
    Back,
    /// <summary>The note shown when the folder holds no replay.</summary>
    Empty,
    /// <summary>The note naming how many files could not be read, a format naming <c>{0}</c> (the count).</summary>
    Skipped,
    /// <summary>The note when a replay is refused before it plays (other game data, a bad file).</summary>
    Refused,
    /// <summary>The banner's title when playback stops because a checkpoint or a command stamp differs.</summary>
    Mismatch,
    /// <summary>The banner's title when playback reaches the end of the recording.</summary>
    Ended,
}
