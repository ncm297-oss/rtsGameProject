namespace Rts.Game;

/// <summary>A main menu text from <c>ui.json</c> <c>menu</c> (M6-V1); the key is the member's snake_case name (<see cref="UiText.Key{T}"/>).</summary>
public enum MenuText
{
    /// <summary>The game's name over the menu.</summary>
    Title,
    /// <summary>The button that opens the skirmish setup.</summary>
    Skirmish,
    /// <summary>The button that closes the game.</summary>
    Quit,
}
