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
    /// <summary>M6-V11: the gold gathered column heading (delivered at a drop-off over the match).</summary>
    GatheredGold,
    /// <summary>M6-V11: the wood gathered column heading.</summary>
    GatheredWood,
    /// <summary>M6-V11: the units trained column heading (finished from a production queue).</summary>
    UnitsTrained,
    /// <summary>M6-V11: the units killed column heading (killing blows on units).</summary>
    UnitsKilled,
    /// <summary>M6-V11: the units lost column heading.</summary>
    UnitsLost,
    /// <summary>M6-V11: the buildings built column heading (construction sites completed).</summary>
    BuildingsBuilt,
    /// <summary>M6-V11: the buildings killed column heading (killing blows on buildings and sites).</summary>
    BuildingsKilled,
    /// <summary>M6-V11: the buildings lost column heading.</summary>
    BuildingsLost,
    /// <summary>M6-V11: the time to Age II column heading.</summary>
    AgeIiAt,
    /// <summary>M6-V11: the Age II cell of a player who has not reached it.</summary>
    NotReached,
    /// <summary>M6-V11: the army value graph's title.</summary>
    ArmyValue,
    /// <summary>M6-V11: the graph's time axis title.</summary>
    GraphTime,
    /// <summary>M6-V11: the graph's value axis title.</summary>
    GraphValue,
    /// <summary>The defeat time column heading.</summary>
    DefeatedAt,
    /// <summary>The defeat time cell of a player who is not defeated.</summary>
    Standing,
    /// <summary>The seat cell of a human player other than the local one (dev runs).</summary>
    Human,
    /// <summary>The stats screen's button back to the banner or the pause menu.</summary>
    Back,
    /// <summary>M6-V13: an AI seat's line under the stats table, a format naming <c>{0}</c> (the seat's faction name) and <c>{1}</c> (the opening's <c>displayName</c>).</summary>
    Opening,
}
