namespace Rts.Game;

/// <summary>A match-over banner or stats screen text from <c>ui.json</c> <c>match_over</c> (M6-V4); the key is the member's snake_case name (<see cref="UiText.Key{T}"/>).</summary>
public enum MatchOverText
{
    /// <summary>The banner's title when the local player won.</summary>
    Victory,
    /// <summary>The banner's title when the local player was defeated.</summary>
    Defeat,
    /// <summary>The banner's title when nobody stands.</summary>
    Draw,
    /// <summary>The button that opens the stats screen (the banner's and the pause menu's).</summary>
    Stats,
    /// <summary>The banner's button that ends the match and goes back to the main menu.</summary>
    Menu,
    /// <summary>The banner's button that hides it and lets the match run on.</summary>
    Continue,
    /// <summary>The stats screen's heading.</summary>
    StatsTitle,
    /// <summary>The match length line, a format naming <c>{0}</c> (the clock text).</summary>
    MatchLength,
    /// <summary>A clock time, a format naming <c>{0}</c> (minutes) and <c>{1}</c> (seconds), e.g. <c>"{0}:{1:00}"</c>.</summary>
    Clock,
    /// <summary>The stats table's player column heading.</summary>
    Player,
    /// <summary>The kills column heading (killing blows, units and buildings).</summary>
    Kills,
    /// <summary>The losses column heading (units and buildings lost to killing blows).</summary>
    Losses,
    /// <summary>The gold column heading: gold in hand now, not gathered.</summary>
    GoldInHand,
    /// <summary>The wood column heading: wood in hand now, not gathered.</summary>
    WoodInHand,
    /// <summary>The defeat time column heading.</summary>
    DefeatedAt,
    /// <summary>The defeat time cell of a player who is not defeated.</summary>
    Standing,
    /// <summary>The seat cell of a human player other than the local one (dev runs).</summary>
    Human,
    /// <summary>The stats screen's button back to the banner or the pause menu.</summary>
    Back,
}
