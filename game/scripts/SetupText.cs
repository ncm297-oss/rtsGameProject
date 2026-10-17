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
}
