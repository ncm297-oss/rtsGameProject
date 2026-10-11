namespace Rts.Game;

/// <summary>A skirmish setup text from <c>ui.json</c> <c>setup</c> (M6-V1); the key is the member's snake_case name (<see cref="UiText.Key{T}"/>).</summary>
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
    /// <summary>The local player's slot name.</summary>
    SlotHuman,
    /// <summary>The computer player's slot name.</summary>
    SlotAi,
    /// <summary>The faction column's heading.</summary>
    Faction,
    /// <summary>The difficulty column's heading.</summary>
    Difficulty,
    /// <summary>What a choice that isn't wired yet shows (faction and difficulty until M6-V2).</summary>
    ComingSoon,
    /// <summary>The button that starts the match.</summary>
    Start,
    /// <summary>The button back to the main menu.</summary>
    Back,
}
