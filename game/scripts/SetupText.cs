namespace Rts.Game;

/// <summary>A skirmish setup text from <c>ui.json</c> <c>setup</c> (M6-V1, M6-V2); the key is the member's snake_case name (<see cref="UiText.Key{T}"/>).</summary>
public enum SetupText
{
    /// <summary>The screen's heading.</summary>
    Title,
    /// <summary>The map seed's label.</summary>
    Seed,
    /// <summary>The button that picks a random seed.</summary>
    Random,
    /// <summary>The map size's label.</summary>
    MapSize,
    /// <summary>One map size list item: a format whose <c>{0}</c> is the map side in cells (M6-V2, BUG-0581 item 1).</summary>
    MapSizeItem,
    /// <summary>The local player's slot name.</summary>
    SlotHuman,
    /// <summary>The computer player's slot name.</summary>
    SlotAi,
    /// <summary>The faction column's heading.</summary>
    Faction,
    /// <summary>The difficulty column's heading.</summary>
    Difficulty,
    /// <summary>The colour column's heading (M6-V3; the colours themselves are <c>setup.colour_1</c> .. <c>colour_8</c>, <see cref="UiText.ColourName"/>).</summary>
    Colour,
    /// <summary>The Easy AI difficulty's name (M6-V2; the sim's <c>AiDifficulty</c> has no display name).</summary>
    DifficultyEasy,
    /// <summary>The Normal AI difficulty's name (M6-V2).</summary>
    DifficultyNormal,
    /// <summary>The Hard AI difficulty's name (M6-V2).</summary>
    DifficultyHard,
    /// <summary>The button that starts the match.</summary>
    Start,
    /// <summary>The button back to the main menu.</summary>
    Back,
    /// <summary>The button that adds a player slot (M6-V5: 2-4 slots).</summary>
    AddSlot,
    /// <summary>The button that removes the last player slot (M6-V5).</summary>
    RemoveSlot,
    /// <summary>The game speed list's label (M6-V15).</summary>
    Speed,
    /// <summary>The Slow game speed's name (0.75x; M6-V15).</summary>
    SpeedSlow,
    /// <summary>The Normal game speed's name (1x; M6-V15).</summary>
    SpeedNormal,
    /// <summary>The Fast game speed's name (1.5x; M6-V15).</summary>
    SpeedFast,
    /// <summary>The forests count's label (M6-V15).</summary>
    Forests,
    /// <summary>The gold mines count's label (M6-V15).</summary>
    Mines,
    /// <summary>The team column's heading (M6-V16).</summary>
    Team,
    /// <summary>The team list's "no team" item (team 0: the slot is a side of its own; M6-V16).</summary>
    TeamNone,
    /// <summary>One named team's list item: a format whose <c>{0}</c> is the team number (M6-V16).</summary>
    TeamItem,
    /// <summary>Beside a disabled Start while every shown slot is on one team (BUG-1062 item 2: such a match could never end).</summary>
    OneTeam,
    /// <summary>The starting-resources list's label (M6-V17; the items are the presets' <c>displayName</c> from <c>rules.json</c>).</summary>
    StartingResources,
    /// <summary>The map list's label (M6-V18; the items are <see cref="MapGenerated"/>, then the map files' <c>displayName</c>).</summary>
    Map,
    /// <summary>The map list's first item: a map made by the generator from the seed, size and counts (M6-V18).</summary>
    MapGenerated,
    /// <summary>
    /// Beside Add player while a map file is picked (M6-V18): a format naming <c>{0}</c> (the map's <c>displayName</c>) and
    /// <c>{1}</c> (its start locations, the most players it takes).
    /// </summary>
    MapPlayers,
    /// <summary>The Easy difficulty item's tooltip (M5-V2, docs/02 "AI opponent"); a format that may name <c>{0}</c>, Easy's gather bonus in percent.</summary>
    DifficultyEasyTip,
    /// <summary>The Normal difficulty item's tooltip (M5-V2); a format that may name <c>{0}</c>, Normal's gather bonus in percent.</summary>
    DifficultyNormalTip,
    /// <summary>The Hard difficulty item's tooltip (M5-V2): a format that must name <c>{0}</c>, Hard's gather bonus in percent (from <c>ai.json</c>).</summary>
    DifficultyHardTip,
    /// <summary>
    /// BUG-1200 (M7-V3a): a number of seconds that is exactly 1, as the difficulty tips' <c>{1}</c> says it: a format naming
    /// <c>{0}</c> (the number, <see cref="UiText.Seconds"/>), "1 second".
    /// </summary>
    SecondsOne,
    /// <summary>BUG-1200 (M7-V3a): any other number of seconds, as the tips' <c>{1}</c> says it: a format naming <c>{0}</c>, "2 seconds", "0.5 seconds".</summary>
    SecondsOther,
}
